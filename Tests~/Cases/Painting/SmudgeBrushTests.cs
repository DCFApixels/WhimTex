using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

// Only package-owned members are reflected; all inputs and windows are transient.
public static class SmudgeBrushTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static FixtureScope S => FixtureContext.Scope;
    static WhimTex.Tests.TestContext T => FixtureContext.Context;
    static object Call(object owner, string name, params object[] args) =>
        (owner as Type ?? owner.GetType()).GetMethod(name, Flags).Invoke(owner is Type ? null : owner, args);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static Texture2D Pixels(DrawingLayerBehaviour layer) => (Texture2D)typeof(DrawingLayerBehaviour).GetProperty("StoredTexture", Flags).GetValue(layer);
    static TextureCompositor Document(int width = 128, int height = 64)
    {
        var doc = S.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        doc.width = width; doc.height = height;
        return doc;
    }
    static DrawingLayerBehaviour Drawing(TextureCompositor doc, Func<int, int, Color> color, int width = 128, int height = 64)
    {
        var texture = S.Own(new Texture2D(width, height, TextureFormat.RGBAHalf, false, true));
        var values = new Color[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) values[y * width + x] = color(x, y);
        texture.SetPixels(values); texture.Apply();
        var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", texture);
        doc.layers.Add(drawing);
        Call(doc, "NormalizeModel");
        return drawing;
    }
    static void Begin(TextureCompositor doc, DrawingLayerBehaviour layer, Vector2 uv, float size = 20, RenderTexture sample = null, bool tiled = false)
    {
        Call(doc, "GetPaintTransform", layer.Owner);
        Call(layer, "PrepareStroke", doc.width, doc.height, "Smudge test");
        Call(layer, "BeginStroke", uv);
        Call(layer, "BeginSmudgeStroke", uv, doc.width, doc.height, size, sample, tiled);
    }
    static void Segment(TextureCompositor doc, DrawingLayerBehaviour layer, Vector2 from, Vector2 to,
        float strength = .95f, float flow = 1, float hardness = .8f, Texture selection = null) =>
        Call(layer, "SmudgeSegment", from, to, doc.width, doc.height, hardness, strength, flow, selection);
    static void End(DrawingLayerBehaviour layer)
    {
        Call(layer, "EndStroke"); Call(layer, "SyncSurfaceToTexture");
        foreach (string field in new[] { "smudgeCarry", "smudgePickup", "smudgeNext", "smudgeBackdrop", "smudgeSample", "smudgeSampleBackdrop" })
            T.True(Get(layer, field) == null, "Owned Smudge buffer released: " + field);
    }
    static double Difference(Color[] a, Color[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a);
        return sum;
    }
    static Color Split(int x, int y) => x < 48 ? new Color(3, .1f, -.2f, 1) : new Color(.1f, .2f, 2, 1);
    static Vector2 Start => new Vector2(40f / 128, .5f);
    static Vector2 Finish => new Vector2(84f / 128, .5f);

    public static string Run() => FixtureContext.Run("Smudge pixels, spacing, masks, HDR, transforms and sampling", () =>
    {
        var doc = Document(); var layer = Drawing(doc, Split);
        var original = Pixels(layer).GetPixels();
        var previous = RenderTexture.active;
        Begin(doc, layer, Start); End(layer);
        T.Near(0, Difference(original, Pixels(layer).GetPixels()), .001, "A click without movement leaves pixels unchanged");
        foreach (var settings in new[] { new Vector2(0, 1), new Vector2(1, 0) })
        {
            Begin(doc, layer, Start); Segment(doc, layer, Start, Finish, settings.x, settings.y); End(layer);
            T.Near(0, Difference(original, Pixels(layer).GetPixels()), .001, "Zero Strength/Flow does not paint");
        }
        Begin(doc, layer, Start); Segment(doc, layer, Start, Finish); End(layer);
        var single = Pixels(layer).GetPixels();
        T.True(Difference(original, single) > 10, "Dragging changes actual Drawing pixels");
        T.True(Pixels(layer).GetPixel(62, 32).r > .4f, "Color is carried in the direction of movement");
        T.True(Pixels(layer).GetPixel(56, 32).r > 1 && Pixels(layer).GetPixel(42, 32).b < 0, "HDR and negative RGB survive");
        T.Near(0, Difference(new[] { original[0], original[32 * 128 + 110] }, new[] { single[0], single[32 * 128 + 110] }), .001, "Pixels outside the brush remain exact");
        T.True(RenderTexture.active == previous, "Smudge restores caller render target");

        var segmented = Drawing(doc, Split);
        Begin(doc, segmented, Start);
        Vector2 last = Start;
        for (int i = 1; i <= 44; i++) { Vector2 next = Vector2.Lerp(Start, Finish, i / 44f); Segment(doc, segmented, last, next); last = next; }
        End(segmented);
        T.True(Difference(single, Pixels(segmented).GetPixels()) / single.Length < .0003, "The same path is independent of pointer-event segmentation");

        var gentle = Drawing(doc, Split);
        Begin(doc, gentle, Start); Segment(doc, gentle, Start, Finish, .95f, .1f); End(gentle);
        T.True(Difference(original, Pixels(gentle).GetPixels()) < Difference(original, single), "Lower Flow deposits less color");
        var shortCarry = Drawing(doc, Split);
        Begin(doc, shortCarry, Start); Segment(doc, shortCarry, Start, Finish, .2f); End(shortCarry);
        T.True(Pixels(shortCarry).GetPixel(70, 32).r < Pixels(layer).GetPixel(70, 32).r, "Lower Strength picks up the destination sooner");

        var selection = S.Own(new Texture2D(128, 64, TextureFormat.RGBA32, false, true));
        var mask = new Color[128 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++) mask[y * 128 + x] = x < 60 ? Color.white : Color.black;
        selection.SetPixels(mask); selection.Apply(); selection.filterMode = FilterMode.Point;
        var masked = Drawing(doc, Split);
        Begin(doc, masked, Start); Segment(doc, masked, Start, Finish, .95f, 1, .8f, selection); End(masked);
        T.True(Pixels(masked).GetPixel(54, 32).r > .2f, "Selection permits a deposit inside");
        T.Near(original[32 * 128 + 72].r, Pixels(masked).GetPixel(72, 32).r, .001, "Selection blocks deposits outside");

        var alphaDoc = Document();
        var alpha = Drawing(alphaDoc, (x, y) => x < 48 ? new Color(4, 0, 0, .5f) : new Color(0, 9, 0, 0));
        Begin(alphaDoc, alpha, Start); Segment(alphaDoc, alpha, Start, Finish, 1); End(alpha);
        var carried = Pixels(alpha).GetPixel(72, 32);
        T.True(carried.a > .1f && carried.a <= .501f && carried.r > 3.9f && Math.Abs(carried.g) < .01,
            "Transparent RGB does not produce colored fringes; carried alpha and HDR stay intact");

        var nativeDoc = Document(128, 64);
        var native = Drawing(nativeDoc, (x, y) => Split(x / 2, y / 2), 256, 128);
        var transform = native.Owner.transform; transform.position = new Vector2(8, 0); native.Owner.transform = transform;
        Begin(nativeDoc, native, Start); Segment(nativeDoc, native, Start, Finish); End(native);
        T.True(Pixels(native).width == 256 && Pixels(native).height == 128, "Native Drawing resolution is preserved");
        T.True(Pixels(native).GetPixel(120, 64).r > .2f, "Translated native-resolution Drawing can be smudged");

        var tiledDoc = Document(); var tiled = Drawing(tiledDoc, (x, y) => x > 112 ? Color.red : Color.blue);
        var edge = new Vector2(.96f, .5f); Begin(tiledDoc, tiled, edge, 16, tiled: true);
        Segment(tiledDoc, tiled, edge, new Vector2(1.12f, .5f), 1); End(tiled);
        T.True(Pixels(tiled).GetPixel(7, 32).r > .2f, "Tiled stroke carries color across the canvas edge");
        var wideDoc = Document(128, 32); var wide = Drawing(wideDoc, (x, y) => x < 48 ? Color.red : Color.blue, 128, 32);
        Begin(wideDoc, wide, Start, 96, tiled: true); Segment(wideDoc, wide, Start, Finish, 1, .3f); End(wide);
        T.True(Pixels(wide).GetPixel(60, 0).r > .01f && Pixels(wide).GetPixel(60, 31).r > .01f,
            "A large Tiled tip covers the short canvas axis without missing its seam");
        var smallDoc = Document(32, 32); var largeTip = Drawing(smallDoc, (x, y) => Color.red, 32, 32);
        Begin(smallDoc, largeTip, new Vector2(.25f, .5f), 64);
        T.Near(64, (float)Get(largeTip, "smudgeDiameter"), 0, "Tip diameter matches the control even when larger than the canvas");
        Segment(smallDoc, largeTip, new Vector2(.25f, .5f), new Vector2(.9f, .5f), 1, 1, 1); End(largeTip);
        T.True(Pixels(largeTip).GetPixel(1, 31).a < .1f, "Large round tip includes pixels beyond the small-canvas-sized circle");

        foreach (string mode in new[] { "RenderLayerAndBelow", "RenderCanvasAtSize" })
        {
            var sampleDoc = Document(); var donor = Drawing(sampleDoc, Split); var empty = Drawing(sampleDoc, (x, y) => Color.clear);
            sampleDoc.layers.Remove(empty.Owner); sampleDoc.layers.Insert(0, empty.Owner);
            sampleDoc.layers.Insert(0, new ColorFillLayerBehaviour { color = Color.green });
            var donorBefore = Pixels(donor).GetPixels();
            var sample = S.Temporary((RenderTexture)(mode == "RenderLayerAndBelow"
                ? Call(sampleDoc, mode, empty.Owner, 128, 64) : Call(sampleDoc, mode, 128, 64)));
            Begin(sampleDoc, empty, Start, 20, sample); Segment(sampleDoc, empty, Start, Finish, 1); End(empty);
            var output = Pixels(empty).GetPixel(68, 32);
            T.True(mode == "RenderLayerAndBelow" ? output.r > 1 : output.g > .9f && output.r < .01f,
                "Below ignores upper layers; All includes them, writing only the empty Drawing: " + mode);
            T.Near(0, Difference(donorBefore, Pixels(donor).GetPixels()), 0, "Sampling never edits donor layers");
            S.Release(sample);
        }
        var composite = S.Own(doc.ComposeCanvas());
        T.True(composite != null && composite.width == 128, "Smudged layers compose normally");
        var thumbnail = layer.GetPreviewTexture(32);
        T.True(thumbnail == Pixels(layer), "Drawing thumbnail sees the edited pixels");
        var groupDoc = Document(); var grouped = Drawing(groupDoc, Split);
        Layer group = new GroupLayerBehaviour(); group.children.Add(grouped);
        groupDoc.layers.Clear(); groupDoc.layers.Add(group);
        var groupTransform = group.transform; groupTransform.rotation = 12; groupTransform.scale = new Vector2(.85f, .85f); group.transform = groupTransform;
        Call(groupDoc, "NormalizeModel");
        Color[] Compose(TextureCompositor model) { var image = S.Own(model.ComposeCanvas()); try { return image.GetPixels(); } finally { S.Destroy(image); } }
        var groupBefore = Compose(groupDoc);
        Begin(groupDoc, grouped, Start); Segment(groupDoc, grouped, Start, Finish); End(grouped);
        var groupAfter = Compose(groupDoc);
        T.True(Difference(groupBefore, groupAfter) > 1, "Smudge follows rotated/scaled parent transforms and group composition");
        var target = new BlurLayerBehaviour { radius = 0, colorRange = LayerColorRange.HDR, inputMode = EffectInputMode.Specific, TargetLayerId = grouped.Id };
        group.children.Insert(0, target); Call(groupDoc, "NormalizeModel");
        var targetResult = S.Temporary((RenderTexture)Call(groupDoc, "RenderLayerPreview", target.Owner, 128));
        var targetPixels = S.Own(new Texture2D(targetResult.width, targetResult.height, TextureFormat.RGBAFloat, false, true));
        RenderTexture active = RenderTexture.active;
        try { RenderTexture.active = targetResult; targetPixels.ReadPixels(new Rect(0, 0, targetResult.width, targetResult.height), 0, 0); }
        finally { RenderTexture.active = active; }
        T.True(Difference(groupBefore, targetPixels.GetPixels()) > 1, "Target input sees the smudged Drawing");
        T.True(Difference(groupAfter, targetPixels.GetPixels()) / groupAfter.Length < .001, "Target input matches the smudged group render");
        S.Release(targetResult); group.children.Remove(target.Owner);
        grouped.clippingMask = true;
        Layer clipBase = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse };
        group.children.Add(clipBase); Call(groupDoc, "NormalizeModel");
        var clipped = Compose(groupDoc);
        T.True(clipped[0].a < .01f && clipped[32 * 128 + 64].a > .9f, "Clipping still masks the smudged Drawing");
        var copy = S.Own((TextureCompositor)Call(typeof(WhimTexDocumentFile), "CreateEditableCopy", alphaDoc));
        var restored = (DrawingLayerBehaviour)copy.layers[0].Behaviour;
        T.Near(carried.r, Pixels(restored).GetPixel(72, 32).r, .005, "Smudged HDR pixels survive document roundtrip");
    });

    static TextureCompositorWindow Window(out TextureCompositor doc)
    {
        var window = S.Own(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        doc = S.Own((TextureCompositor)Get(window, "compositor")); doc.width = 128; doc.height = 64;
        return window;
    }

    [Serializable] public class Reply { public bool success; public string error; }
    static string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static string Api() => FixtureContext.Run("Smudge API validation and Undo/Redo", () =>
    {
        var window = Window(out var doc); var layer = Drawing(doc, Split);
        string session = (string)typeof(TextureCompositorWindow).GetProperty("AgentSessionId", Flags).GetValue(window);
        string Operation(string extra = "") => "{\"op\":\"smudgeStroke\",\"layer\":" + Q(layer.Id) + ",\"points\":[[40,32],[84,32]],\"size\":20,\"strength\":0.95" + extra + "}";
        Reply Batch(string op, bool dry = false)
        {
            string inspection = WhimTexApi.LiveJson("{\"apiVersion\":1,\"op\":\"inspect\",\"sessionId\":" + Q(session) + "}");
            var inspected = JsonUtility.FromJson<Reply>(inspection);
            T.True(inspected.success, "Inspect owned session");
            var jsonType = typeof(WhimTexApi).GetMethod("PaintRepair", Flags).GetParameters()[2].ParameterType;
            object token = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { inspection });
            foreach (string key in new[] { "document", "revision" })
                token = token.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(token, new object[] { key });
            return JsonUtility.FromJson<Reply>(WhimTexApi.AssistantExecuteJson("{\"apiVersion\":1,\"sessionId\":" + Q(session) +
                ",\"expectedRevision\":" + Q(token.ToString()) + ",\"dryRun\":" + (dry ? "true" : "false") + ",\"operations\":[" + op + "]}"));
        }
        var before = Pixels(layer).GetPixels();
        T.True(Batch(Operation(), true).success, "Smudge dry run is accepted");
        T.Near(0, Difference(before, Pixels(layer).GetPixels()), 0, "Dry run does not paint");
        T.True(!Batch(Operation(",\"flow\":2")).success, "Invalid Flow is rejected");
        var longPath = new string[500];
        for (int i = 0; i < longPath.Length; i++) longPath[i] = i % 2 == 0 ? "[-128,0]" : "[256,128]";
        var oversized = Batch("{\"op\":\"smudgeStroke\",\"layer\":" + Q(layer.Id) + ",\"points\":[" + string.Join(",", longPath) + "],\"size\":512}", true);
        T.True(!oversized.success && oversized.error.Contains("budget"), "Oversized tip work is rejected during dry run");
        T.Near(0, Difference(before, Pixels(layer).GetPixels()), 0, "Invalid operation preserves pixels");
        var result = Batch(Operation()); T.True(result.success, "Smudge API succeeds: " + result.error);
        DrawingLayerBehaviour Current() => (DrawingLayerBehaviour)((Layer)Call(doc, "FindLayer", layer.Id)).Behaviour;
        var after = Pixels(Current()).GetPixels();
        T.True(Difference(before, after) > 10, "Smudge API actually moves pixels");
        Undo.PerformUndo();
        T.True(Difference(before, Pixels(Current()).GetPixels()) < .01, "One Undo restores the stroke");
        Undo.PerformRedo();
        T.True(Difference(after, Pixels(Current()).GetPixels()) < .01, "Redo restores the stroke");
    });

    public static string StartUi(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = ExecuteUi(job, id);
        return job.Read();
    }

    sealed class IconCapture : ImmediateModeElement
    {
        internal Button button;
        internal EditorWindow owner;
        internal TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
        bool done;
        protected override void ImmediateRepaint()
        {
            if (done) return;
            done = true;
            Texture2D image = null;
            try
            {
                float scale = EditorGUIUtility.pixelsPerPoint;
                int height = RenderTexture.active != null ? RenderTexture.active.height : Mathf.RoundToInt(owner.position.height * scale);
                Rect rect = button.worldBound;
                int w = Mathf.RoundToInt(rect.width * scale), h = Mathf.RoundToInt(rect.height * scale);
                image = new Texture2D(w, h, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(Mathf.RoundToInt(rect.x * scale), height - Mathf.RoundToInt(rect.yMax * scale), w, h), 0, 0);
                image.Apply();
                completion.TrySetResult(Convert.ToBase64String(image.EncodeToPNG()));
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }

    static async Task ExecuteUi(AsyncFixture job, string id)
    {
        string icon = null;
        await FixtureContext.RunAsync(job, async cancellation =>
        {
            var window = Window(out var doc); var layer = Drawing(doc, Split);
            window.ShowUtility(); window.position = new Rect(100, 100, 1100, 700);
            await Task.Delay(250, cancellation);
            Call(window, "SelectOnlyLayer", layer.Id);
            var button = window.rootVisualElement.Q<Button>("smudgeBrushTool");
            T.True(button != null && button.childCount == 1, "Smudge button has its own finger icon");
            PublicInput.Click(button); Call(window, "UpdateToolkitCanvasPresentation");
            await Task.Delay(100, cancellation);
            var row = window.rootVisualElement.Q("smudgeBrushSettings");
            T.True(row != null && !row.ClassListContains("whimtex-tool-options--hidden"), "Smudge settings row is active");
            var settings = Get(window, "paintSettings"); var size = row.Q<FloatField>("smudgeSize");
            float blurBefore = (float)Get(settings, "blurSize");
            size.value = 24;
            T.Near(24, (float)Get(settings, "smudgeSize"), 0, "Size field edits Smudge size");
            T.Near(blurBefore, (float)Get(settings, "blurSize"), 0, "Smudge settings do not overwrite Blur settings");
            T.True(size.Q(className: "unity-base-field__input").worldBound.width > 30, "Compact Size field has usable input width");
            T.True(row.Q<Slider>("smudgeHardness") != null && row.Q<Toggle>("smudgePressure") != null && row.Q<EnumField>("smudgeSampleMode") != null,
                "Hardness, Pressure and source controls exist");
            T.True(Call(typeof(TextureCompositorWindow), "ParseCanvasTool", "SmudgeBrush").ToString() == "SmudgeBrush", "Smudge is a persistent base tool");
            var capture = new IconCapture { button = button, owner = window };
            capture.style.height = 1; window.rootVisualElement.Add(capture); window.Repaint();
            try
            {
                if (await Task.WhenAny(capture.completion.Task, Task.Delay(3000, cancellation)) != capture.completion.Task)
                    throw new TimeoutException("Owned icon capture did not repaint.");
                icon = await capture.completion.Task;
                T.True(!string.IsNullOrEmpty(icon), "Actual tool icon captured with public UI APIs");
            }
            finally { capture.RemoveFromHierarchy(); }
            var canvas = (VisualElement)Get(window, "toolkitCanvas");
            var imageRect = (Rect)canvas.GetType().GetProperty("ImageRect").GetValue(canvas);
            Vector2 View(Vector2 uv) => (Vector2)Call(canvas, "ToView", new Vector2(imageRect.x + uv.x * imageRect.width, imageRect.y + (1 - uv.y) * imageRect.height));
            var original = Pixels(layer).GetPixels();
            Set(window, "temporaryDocumentDirty", false);
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Canvas begins a Smudge stroke");
            Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(layer).GetPixels()) < .001, "Canvas click without dragging does not paint");
            T.True(!(bool)Get(window, "temporaryDocumentDirty"), "A click does not mark the document dirty");
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Canvas starts another Smudge stroke");
            Call(window, "PaintTowardsLayerPoint", Finish); Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(layer).GetPixels()) > 10, "Canvas movement uses Smudge, not the ordinary Brush");
            double pressureDifference = Difference(original, Pixels(layer).GetPixels());
            var light = Drawing(doc, Split); Call(window, "SelectOnlyLayer", light.Id);
            Set(settings, "smudgePressure", true); Set(window, "paintingPressure", .2f);
            T.True((bool)Call(window, "TryBeginCanvasStroke", View(Start), false), "Begin low-pressure stroke");
            Call(window, "PaintTowardsLayerPoint", Finish); Call(window, "FinishPaintingStroke");
            T.True(Difference(original, Pixels(light).GetPixels()) < pressureDifference, "Pressure reduces Flow, not color retention");
            S.CloseWindow(window);
        });
        string reply = job.Read();
        if (icon != null && JsonUtility.FromJson<WhimTex.Tests.TestResult>(reply).status == "passed")
            SessionState.SetString("WhimTex.Tests.UnityC." + id, reply.Substring(0, reply.Length - 1) +
                ",\"artifacts\":[{\"name\":\"smudge-icon.png\",\"encoding\":\"base64\",\"content\":\"" + icon + "\"}]}" );
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);

    public static string Capture()
    {
        string before = null, after = null;
        string reply = FixtureContext.Diagnostic("Smudge visual sample", () =>
        {
            var doc = Document(256, 128);
            var layer = Drawing(doc, (x, y) => x < 96 ? new Color(.8f, .15f, .05f, 1) : new Color(.03f, .15f, .7f, 1), 256, 128);
            before = Convert.ToBase64String(Pixels(layer).EncodeToPNG());
            var start = new Vector2(84f / 256, .5f); Begin(doc, layer, start, 40);
            Segment(doc, layer, start, new Vector2(185f / 256, .63f), .97f, .65f); End(layer);
            var image = S.Own(doc.ComposeCanvas());
            after = Convert.ToBase64String(image.EncodeToPNG());
            return "Before and after PNGs; visual inspection is separate from regression verdicts.";
        });
        // Ephemeral Unity serialization can omit nested artifact types. Preserve
        // the actual images explicitly, after all fixture cleanup has completed.
        var result = JsonUtility.FromJson<WhimTex.Tests.TestResult>(reply);
        string json = result.ToJson();
        return before == null || after == null ? json : json.Substring(0, json.Length - 1) +
            ",\"artifacts\":[{\"name\":\"before.png\",\"encoding\":\"base64\",\"content\":\"" + before +
            "\"},{\"name\":\"after.png\",\"encoding\":\"base64\",\"content\":\"" + after + "\"}]}";
    }

    // Opt-in timing, not a regression threshold. One-pixel readback drains queued GPU
    // work; setup and final storage synchronization are outside the stroke measurement.
    public static string Benchmark()
    {
        var measurements = new System.Collections.Generic.List<string>();
        return FixtureContext.Diagnostic("Smudge interactive timing", () =>
        {
            var drain = S.Own(new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true));
            void Flush(RenderTexture surface)
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = surface; drain.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); }
                finally { RenderTexture.active = active; }
            }
            foreach (int resolution in new[] { 2048, 4096 })
            foreach (int size in new[] { 32, 128, 512 })
            foreach (bool sampled in new[] { false, true })
            {
                var doc = Document(resolution, resolution);
                var layer = Drawing(doc, (x, y) => x < resolution * .375f
                    ? new Color(3, .1f + y / (float)resolution, -.2f, .7f)
                    : new Color(.1f, .2f, 2, .3f), resolution, resolution);
                RenderTexture sample = null;
                if (sampled)
                {
                    sample = S.Temporary(RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                    Graphics.Blit(Pixels(layer), sample);
                }
                var start = new Vector2(.35f, .5f);
                Begin(doc, layer, start, size, sample);
                var surface = (RenderTexture)Get(layer, "paintSurface");
                Flush(surface);
                var clock = new System.Diagnostics.Stopwatch();
                const int events = 100;
                double queued = 0, completed = 0, canvas = 0;
                for (int trial = 0; trial < 4; trial++)
                {
                    Vector2 last = start;
                    clock.Restart();
                    for (int i = 1; i <= events; i++)
                    {
                        Vector2 next = start + new Vector2(i * 10f / resolution, .025f * Mathf.Sin(i * .08f));
                        Segment(doc, layer, last, next, .95f, .65f); last = next;
                    }
                    double submitted = clock.Elapsed.TotalMilliseconds;
                    Flush(surface);
                    double drained = clock.Elapsed.TotalMilliseconds;
                    clock.Restart();
                    var preview = S.Temporary((RenderTexture)Call(doc, "RenderCanvas", 512));
                    Flush(preview);
                    double rendered = clock.Elapsed.TotalMilliseconds;
                    S.Release(preview);
                    if (trial > 0) { queued += submitted / 3; completed += drained / 3; canvas += rendered / 3; }
                }
                measurements.Add(resolution + "px tip=" + size + " sample=" + sampled +
                    " events=" + events + " enqueueMs=" + queued.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    " completedMs=" + completed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    " canvasMs=" + canvas.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                Call(layer, "EndStroke");
                if (sample != null) S.Release(sample);
                S.Destroy(doc); S.Destroy(Pixels(layer));
            }
            return string.Join("; ", measurements);
        });
    }

    public static string Precision()
    {
        var artifacts = new System.Collections.Generic.List<string>();
        string result = FixtureContext.Diagnostic("Smudge precision snapshot", () =>
        {
            foreach (bool sampled in new[] { false, true })
            foreach (bool tiled in new[] { false, true })
            {
                var doc = Document();
                var layer = Drawing(doc, (x, y) => new Color(x / 31f - .7f, Mathf.Sin(y * .25f) * 2,
                    x < 48 ? 3 : .1f, (x + y) % 17 / 16f));
                RenderTexture sample = null;
                if (sampled)
                {
                    sample = S.Temporary(RenderTexture.GetTemporary(128, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
                    Graphics.Blit(Pixels(layer), sample);
                }
                Begin(doc, layer, Start, 24, sample, tiled);
                Vector2 last = Start;
                for (int i = 1; i <= 20; i++)
                {
                    Vector2 next = Start + new Vector2(i * .042f, Mathf.Sin(i * .3f) * .22f);
                    Segment(doc, layer, last, next, .83f, .62f, .55f); last = next;
                }
                End(layer);
                artifacts.Add("{\"name\":\"pixels-" + sampled + "-" + tiled + ".rgba16f\",\"encoding\":\"base64\",\"content\":\"" +
                    Convert.ToBase64String(Pixels(layer).GetRawTextureData()) + "\"}");
                if (sample != null) S.Release(sample);
            }
            return "Actual native HDR output for a before/after comparison; not an automatically regenerated golden.";
        });
        var json = JsonUtility.FromJson<WhimTex.Tests.TestResult>(result).ToJson();
        return json.Substring(0, json.Length - 1) + ",\"artifacts\":[" + string.Join(",", artifacts) + "]}";
    }
}
