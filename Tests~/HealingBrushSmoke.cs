using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// run_script HealingBrushSmoke.Main. Reflection targets WhimTex only, never Unity internals.
// Own transient document/window; generated comparison PNGs live under Temp, not Assets.
public static class HealingBrushSmoke
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object owner, string name, params object[] args) =>
        (owner as Type ?? owner.GetType()).GetMethod(name, Flags).Invoke(owner is Type ? null : owner, args);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static Texture2D Pixels(DrawingLayerBehaviour layer) => (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Flags).GetValue(layer);
    static Color[] ReadMask(object stroke)
    {
        var rt = (RenderTexture)stroke.GetType().GetProperty("Texture", Flags).GetValue(stroke);
        var previous = RenderTexture.active;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
        try { RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); return image.GetPixels(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }

    public static async Task<string> Main()
    {
        int checks = 0;
        void Check(bool good, string why) { if (!good) throw new Exception(why); checks++; }
        var assembly = typeof(TextureCompositorWindow).Assembly;
        var utility = assembly.GetType("DCFApixels.WhimTex.HealingBrushUtility", true);
        var mask = new byte[48 * 32];
        for (int y = 8; y < 17; y++) for (int x = 8; x < 40; x++) mask[y * 48 + x] = 255;
        var source = new Color[48 * 32];
        for (int i = 0; i < source.Length; i++) source[i] = mask[i] > 0 ? Color.clear : new Color(2, .3f, -.1f, 1);
        object Heal(Color[] input, byte[] coverage, bool empty, CancellationToken token = default) =>
            Call(utility, "Heal", input, coverage, 48, 32, empty, 0, 1, token, null);
        var result = Heal(source, mask, false);
        var restored = (Color[])Get(result, "pixels");
        Check(restored[12 * 48 + 24].r > 1.99f && restored[12 * 48 + 24].a > .99f, "Restores HDR holes");
        Check(source[12 * 48 + 24].a == 0 && restored[0] == source[0], "Input and outside pixels preserved");
        source[12 * 48 + 24] = Color.red;
        result = Heal(source, mask, true);
        Check(((byte[])Get(result, "target"))[12 * 48 + 24] == 0, "Transparent Only excludes visible pixels");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool cancelled = false;
            try { Heal(source, mask, false, cancel.Token); }
            catch (TargetInvocationException e) { cancelled = e.InnerException is OperationCanceledException; }
            Check(cancelled, "Cancellation reaches worker");
        }
        bool noDonors = false;
        try { Heal(new Color[source.Length], mask, false); }
        catch (TargetInvocationException e) { noDonors = e.InnerException is InvalidOperationException; }
        Check(noDonors, "No donors is a safe failure");
        bool oversized = false;
        try { Call(utility, "CheckWorkingSize", new RectInt(0, 0, 2048, 2048)); }
        catch (TargetInvocationException e) { oversized = e.InnerException is InvalidOperationException; }
        Check(oversized, "Oversized work rejected before mask allocation");

        var focused = EditorWindow.focusedWindow;
        const string pref = "DCFApixels.WhimTex.PreviewTool";
        bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        TextureCompositorWindow window = null;
        TextureCompositor document = null;
        var ownedTextures = new List<Texture2D>();
        try
        {
            window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
            document = (TextureCompositor)Get(window, "compositor");
            document.width = 128; document.height = 96;
            var texture = new Texture2D(256, 192, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            ownedTextures.Add(texture);
            var original = new Color[256 * 192];
            for (int y = 0; y < 192; y++) for (int x = 0; x < 256; x++)
                original[y * 256 + x] = x >= 124 && x < 132 && y >= 80 && y < 112 ? Color.red : new Color(.25f, .55f, .15f, 1);
            texture.SetPixels(original); texture.Apply();
            Color outsideBefore = texture.GetPixel(4, 4);
            var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", texture);
            document.layers.Add(drawing);
            Call(document, "NormalizeModel");
            Call(window, "SelectOnlyLayer", drawing.Id);
            var toolType = typeof(TextureCompositorWindow).GetNestedType("PreviewTool", Flags);
            Call(window, "ChangePreviewTool", Enum.Parse(toolType, "HealingBrush"));
            window.ShowUtility(); window.position = new Rect(80, 80, 1050, 720); window.Focus();
            await Task.Delay(250);
            Call(window, "RefreshToolkitInterface", false);
            var canvas = (VisualElement)Get(window, "toolkitPreviewCanvas");
            Check(window.rootVisualElement.Q<Button>("healingBrushTool") != null, "Healing tool exists");
            var settings = Get(window, "paintSettings");
            var searchField = window.rootVisualElement.Q<IntegerField>(className: "whimtex-healing-search");
            Check(searchField != null && searchField.Q<VisualElement>(className: "unity-base-field__input").worldBound.width >= 35,
                "Search input remains visible beside its label");
            Set(settings, "healingSize", 20f); Set(settings, "healingSearch", 24);
            Set(settings, "healingQuality", Enum.ToObject(settings.GetType().GetField("healingQuality").FieldType, 0));
            var selection = Call(window, "GetAreaSelection");
            void Arm(DrawingLayerBehaviour layer)
            {
                Call(window, "CancelHealing");
                Call(window, "SelectOnlyLayer", layer.Id);
                Set(window, "healingDocument", document); Set(window, "healingLayer", layer);
                Set(window, "healingCanvasSize", new Vector2Int(128, 96));
                Set(window, "healingSelectionRevision", (int)selection.GetType().GetProperty("Revision", Flags).GetValue(selection));
                var transform = (TextureTransform)Call(document, "GetCanvasTransform", (Layer)layer);
                Set(window, "healingTransform", transform.ToMatrix(128, 96));
                Call(window, "BeginHealingStroke", new Vector2(64, 39));
                Call(Get(window, "healingStroke"), "Add", new Vector2(64, 57));
            }
            async Task Complete()
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (Get(window, "healingJob") != null && watch.ElapsedMilliseconds < 15000)
                { await Task.Delay(20); Call(window, "UpdateHealing"); }
                Check(Get(window, "healingJob") == null, "Async healing completes within test timeout");
            }
            Check(window.rootVisualElement.Query<Slider>().ToList().Exists(s => s.label == "Hardness"), "Hardness slider available");
            Set(settings, "healingHardness", 0f);
            Arm(drawing); Call(window, "BeginHealingStroke", new Vector2(64, 48));
            var soft = ReadMask(Get(window, "healingStroke"));
            Check(soft[48 * 128 + 71].a > .05f && soft[48 * 128 + 71].a < .3f, "Zero hardness produces soft raster coverage");
            Call(Get(window, "healingStroke"), "Add", new Vector2(64, 48));
            Check(ReadMask(Get(window, "healingStroke"))[48 * 128 + 71].a == soft[48 * 128 + 71].a,
                "Repeated stamps do not harden the mask");
            var pendingTexture = (RenderTexture)Get(window, "healingStroke").GetType().GetProperty("Texture", Flags).GetValue(Get(window, "healingStroke"));
            Call(window, "CancelHealing");
            Check(pendingTexture == null, "Cancellation destroys transient mask texture");
            Set(settings, "healingHardness", 1f);
            Arm(drawing); Call(window, "BeginHealingStroke", new Vector2(64, 48));
            Check(ReadMask(Get(window, "healingStroke"))[48 * 128 + 71].a > .99f, "Full hardness fills the same edge pixel");
            Set(settings, "healingHardness", .8f);
            Arm(drawing);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Call(window, "StartHealing");
            Check(Get(window, "healingJob") != null, "Job starts");
            Check(texture.GetPixel(128, 96).r > .9f, "No write before completion");
            await Complete(); watch.Stop();
            drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            var healed = Pixels(drawing);
            Check(healed.width == 256 && healed.height == 192, "Native Drawing resolution retained");
            Check(healed.GetPixel(128, 96).g > .5f && healed.GetPixel(128, 96).r < .3f, "Actual GPU commit removes red scratch");
            Check(healed.GetPixel(4, 4) == outsideBefore, "Outside stroke unchanged");
            var after = healed.GetPixels();
            Undo.PerformUndo();
            drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(drawing).GetPixel(128, 96).r > .9f, "Undo restores defect");
            Undo.PerformRedo();
            drawing = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(drawing).GetPixel(128, 96).g > .5f, "Redo restores healing");
            Arm(drawing); Call(window, "StartHealing");
            Check(Get(window, "healingJob") != null, "Second job starts");
            Call(window, "CancelHealing");
            await Task.Delay(100); Call(window, "UpdateHealing");
            Check(Get(window, "healingJob") == null && Pixels(drawing).GetPixel(128, 96).g > .5f, "Cancelled job cannot apply later");
            var blank = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR };
            document.layers.Insert(0, blank); Call(document, "NormalizeModel");
            Set(settings, "healingSample", Enum.ToObject(settings.GetType().GetField("healingSample").FieldType, 1));
            Arm(blank); Call(window, "StartHealing"); await Complete();
            blank = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(blank) != null && Pixels(blank).GetPixel(64, 48).a > .99f, "Below sample paints into empty retouching layer");
            ownedTextures.Add(Pixels(blank));
            Check(Pixels(blank).GetPixel(1, 1).a == 0, "Retouching layer is empty outside stroke");
            Undo.PerformUndo();
            blank = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(blank) == null || Pixels(blank).GetPixel(64, 48).a == 0, "Undo handles newly allocated Drawing pixels");
            Undo.PerformRedo();
            blank = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(blank) != null && Pixels(blank).GetPixel(64, 48).a > .99f, "Redo restores newly allocated pixels");
            if (!ownedTextures.Contains(Pixels(blank))) ownedTextures.Add(Pixels(blank));
            Arm(blank); Call(window, "StartHealing"); Call(document, "MarkChanged");
            Check(Get(window, "healingJob") == null, "Document changes invalidate pending healing");
            await Task.Delay(100);
            Arm(blank); Call(window, "StartHealing");
            Call(window, "ChangePreviewTool", Enum.Parse(toolType, "Brush"));
            Check(Get(window, "healingJob") == null, "Tool switch cancels pending work");

            // A transformed, selected stroke affects only its editable half in native source space.
            drawing = (DrawingLayerBehaviour)document.layers[1].Behaviour;
            var transformed = drawing.transform;
            transformed.position = new Vector2(12, 0);
            transformed.scale = new Vector2(.75f, .75f);
            drawing.transform = transformed;
            texture.SetPixels(original); texture.Apply(); Call(drawing, "InvalidatePaintSurface");
            Call(document, "MarkChanged");
            Call(window, "ChangePreviewTool", Enum.Parse(toolType, "HealingBrush"));
            Set(settings, "healingSample", Enum.ToObject(settings.GetType().GetField("healingSample").FieldType, 0));
            var selectionMask = new byte[128 * 96];
            for (int y = 48; y < 96; y++) for (int x = 0; x < 128; x++) selectionMask[y * 128 + x] = 255;
            Call(selection, "Set", selectionMask, Enum.ToObject(assembly.GetType("DCFApixels.WhimTex.SelectionCombine"), 0));
            Arm(drawing);
            Call(window, "BeginHealingStroke", new Vector2(76, 41));
            Call(Get(window, "healingStroke"), "Add", new Vector2(76, 55));
            Call(window, "StartHealing"); await Complete();
            Check(Pixels(drawing).GetPixel(128, 105).g > .5f, "Transform maps healed canvas region back to native source");
            Check(Pixels(drawing).GetPixel(128, 86).r > .9f, "Selection protects unselected side of scratch");
            Call(selection, "Clear");

            // Exercise the actual public UI event dispatch path and pointer lifecycle.
            Call(window, "RefreshToolkitInterface", false);
            await Task.Delay(120);
            canvas = (VisualElement)Get(window, "toolkitPreviewCanvas");
            Rect image = (Rect)canvas.GetType().GetProperty("ImageRect").GetValue(canvas);
            Vector2 local = (Vector2)Call(canvas, "ToView", new Vector2(image.x + image.width * 76 / 128f, image.center.y));
            Vector2 world = canvas.LocalToWorld(local);
            void Down()
            {
                using var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = world });
                evt.target = canvas; canvas.SendEvent(evt);
            }
            void Up()
            {
                using var evt = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = world });
                evt.target = canvas; canvas.SendEvent(evt);
            }
            Down(); Check((int)Get(window, "healingPointer") >= 0, "Real pointer down starts mask gesture");
            using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
                canvas.SendEvent(evt);
            Check((int)Get(window, "healingPointer") < 0 && Get(window, "healingJob") == null, "Escape cancels actual gesture");
            Down(); Up();
            Check((int)Get(window, "healingPointer") < 0, "Real pointer up releases capture");
            Check(Get(window, "healingJob") != null, "Real pointer up starts background healing");
            await Complete();
            Down();
            int captured = (int)Get(window, "healingPointer");
            if (captured >= 0) canvas.ReleasePointer(captured);
            await Task.Delay(60);
            Call(window, "UpdateHealing");
            Check(Get(window, "healingLayer") == null, "Unexpected capture loss cancels without applying");

            // Repair a transparent hole in a semi-transparent HDR layer without changing its opacity.
            var translucent = new Texture2D(128, 96, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            ownedTextures.Add(translucent);
            var translucentPixels = new Color[128 * 96];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++)
                translucentPixels[y * 128 + x] = x >= 62 && x < 66 && y >= 40 && y < 56 ? Color.clear : new Color(2, .3f, .1f, .5f);
            translucent.SetPixels(translucentPixels); translucent.Apply();
            var translucentLayer = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", translucent);
            document.layers.Insert(0, translucentLayer); Call(document, "NormalizeModel");
            Set(settings, "healingTransparentOnly", true);
            Arm(translucentLayer); Call(window, "StartHealing"); await Complete();
            var filledPixel = Pixels(translucentLayer).GetPixel(64, 48);
            Check(Mathf.Abs(filledPixel.a - .5f) < .01f && Mathf.Abs(filledPixel.r - 2) < .02f,
                "HDR and partial alpha survive readback and premultiplied GPU commit");
            Check(Pixels(translucentLayer).GetPixel(60, 48) == translucentPixels[48 * 128 + 60] ||
                Mathf.Abs(Pixels(translucentLayer).GetPixel(60, 48).a - .5f) < .001f, "Transparent Only does not build opacity on visible pixels");

            // Periodic masks and the source search cross the seam together, even when drawing on another tile.
            Set(settings, "healingTransparentOnly", false);
            Set(settings, "healingHardness", .8f);
            var tileTexture = new Texture2D(128, 96, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            ownedTextures.Add(tileTexture);
            var tilePixels = new Color[128 * 96];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++)
                tilePixels[y * 128 + x] = (x < 2 || x >= 126) && y >= 38 && y < 58 ? Color.red : new Color(.2f, .6f, .1f, 1);
            tileTexture.SetPixels(tilePixels); tileTexture.Apply();
            var tileLayer = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", tileTexture);
            document.layers.Insert(0, tileLayer); Call(document, "NormalizeModel");
            Call(window, "SetTiledPreview", true);
            Arm(tileLayer); Call(window, "BeginHealingStroke", new Vector2(-128, 38));
            Call(Get(window, "healingStroke"), "Add", new Vector2(-128, 58));
            var periodic = ReadMask(Get(window, "healingStroke"));
            Check(periodic[48 * 128].a > .99f && periodic[48 * 128 + 127].a > .99f && periodic[48 * 128 + 64].a == 0,
                "Negative repeated tile maps to both edges, never a line across the center");
            var periodicBounds = (RectInt)Call(Get(window, "healingStroke"), "Bounds", 24);
            Check(periodicBounds.xMax > 128 && periodicBounds.width < 100, "Seam search is one compact unwrapped region");
            Call(window, "StartHealing"); await Complete();
            Check(Pixels(tileLayer).GetPixel(0, 48).g > .55f && Pixels(tileLayer).GetPixel(127, 48).g > .55f,
                "Actual GPU commit repairs both sides of seam");
            Check(Pixels(tileLayer).GetPixel(64, 48).r < .21f, "Periodic repair leaves center unchanged");
            Undo.PerformUndo(); tileLayer = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Check(Pixels(tileLayer).GetPixel(0, 48).r > .9f && Pixels(tileLayer).GetPixel(127, 48).r > .9f, "Seam repair is one Undo");
            Undo.PerformRedo(); tileLayer = (DrawingLayerBehaviour)document.layers[0].Behaviour;
            Arm(tileLayer); Call(window, "BeginHealingStroke", new Vector2(127, 95));
            Call(Get(window, "healingStroke"), "Add", new Vector2(131, 99));
            periodic = ReadMask(Get(window, "healingStroke"));
            Check(periodic[0].a > .99f && periodic[127].a > .99f && periodic[95 * 128].a > .99f && periodic[96 * 128 - 1].a > .99f,
                "Corner crossing wraps the mask into all four corners");
            for (int y = 0; y < 96; y++) for (int x = 0; x < 128; x++)
                tilePixels[y * 128 + x] = (x < 2 || x >= 126) && (y < 2 || y >= 94) ? Color.red : new Color(.2f, .6f, .1f, 1);
            Call(window, "CancelHealing");
            Pixels(tileLayer).SetPixels(tilePixels); Pixels(tileLayer).Apply(); Call(tileLayer, "InvalidatePaintSurface");
            Arm(tileLayer); Call(window, "BeginHealingStroke", new Vector2(256, -96));
            Call(window, "StartHealing"); await Complete();
            Check(Pixels(tileLayer).GetPixel(0, 0).g > .55f && Pixels(tileLayer).GetPixel(127, 95).g > .55f &&
                Pixels(tileLayer).GetPixel(0, 95).g > .55f && Pixels(tileLayer).GetPixel(127, 0).g > .55f,
                "Corner repair commits all four wrapped quadrants");
            var halfSelection = new byte[128 * 96]; Array.Fill(halfSelection, (byte)128);
            Call(selection, "Set", halfSelection, Enum.ToObject(assembly.GetType("DCFApixels.WhimTex.SelectionCombine"), 0));
            Arm(tileLayer); Call(window, "BeginHealingStroke", new Vector2(0, 48));
            Check(Mathf.Abs(ReadMask(Get(window, "healingStroke"))[48 * 128].a - 128 / 255f) < .005f,
                "Feathered selection is applied once, not accumulated by overlapping dabs");
            Call(window, "CancelHealing"); Call(selection, "Clear");
            Call(window, "RefreshToolkitInterface", false);
            Call(canvas, "ZoomAt", canvas.contentRect.center, .35f);
            Call(canvas, "SetViewRotation", 27f, false);
            await Task.Delay(100);
            image = (Rect)canvas.GetType().GetProperty("ImageRect").GetValue(canvas);
            local = (Vector2)Call(canvas, "ToView", new Vector2(image.x - image.width / 128f, image.center.y));
            world = canvas.LocalToWorld(local);
            Check(canvas.contentRect.Contains(local), "Repeated tile test point is inside viewport");
            Down();
            Check(Get(window, "healingStroke") != null, "Pointer down on rotated, zoomed neighboring tile starts a raster stroke");
            periodic = ReadMask(Get(window, "healingStroke"));
            Check(periodic[48 * 128 + 127].a > .99f && periodic[48 * 128].a > .99f, "Real tiled pointer maps to the wrapped mask");
            Call(window, "SetTiledPreview", false);
            Check(Get(window, "healingStroke") == null, "Tiled switch cancels and releases stroke");

            // On an empty retouching layer, the mask alpha should become the applied repair alpha exactly.
            var featherLayer = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR };
            document.layers.Insert(0, featherLayer); Call(document, "NormalizeModel");
            Set(settings, "healingHardness", 0f);
            Set(settings, "healingSample", Enum.ToObject(settings.GetType().GetField("healingSample").FieldType, 1));
            Arm(featherLayer); Call(window, "BeginHealingStroke", new Vector2(64, 48));
            var expectedMask = ReadMask(Get(window, "healingStroke"));
            Call(window, "StartHealing"); await Complete();
            Check(Pixels(featherLayer) != null, "Soft repair creates retouching pixels");
            ownedTextures.Add(Pixels(featherLayer));
            Check(Mathf.Abs(Pixels(featherLayer).GetPixel(71, 48).a - expectedMask[48 * 128 + 71].a) < .005f,
                "Final healing uses the same soft coverage as the visible raster buffer");

            var comparison = new Texture2D(512, 192, TextureFormat.RGBA32, false);
            try
            {
                var display = new Color[512 * 192];
                for (int y = 0; y < 192; y++) for (int x = 0; x < 256; x++)
                { display[y * 512 + x] = original[y * 256 + x].gamma; display[y * 512 + x + 256] = after[y * 256 + x].gamma; }
                comparison.SetPixels(display); comparison.Apply();
                Directory.CreateDirectory("Temp/WhimTex");
                File.WriteAllBytes("Temp/WhimTex/HealingBrushSmoke.png", comparison.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(comparison); }
            return "PASS HealingBrushSmoke: " + checks + " checks; first complete stroke " + watch.ElapsedMilliseconds + " ms; Temp/WhimTex/HealingBrushSmoke.png";
        }
        finally
        {
            if (window != null) { Call(window, "CancelHealing"); window.DiscardChanges(); window.Close(); }
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            foreach (var texture in ownedTextures) if (texture != null) { Undo.ClearUndo(texture); UnityEngine.Object.DestroyImmediate(texture); }
            if (hadPref) EditorPrefs.SetString(pref, oldPref); else EditorPrefs.DeleteKey(pref);
            if (focused != null) focused.Focus();
        }
    }
}
