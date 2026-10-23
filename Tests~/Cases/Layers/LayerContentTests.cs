using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;
using WhimTex.Tests.UnityD;

// Only owned transient models, pixels and effects; package reflection does not
// access Unity internals. No native drag, global Undo execution or asset imports.
public static class LayerContentTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static Task Execute(TestContext context, CancellationToken token)
        => UnityAScope.RunOwnedAsync(async scope =>
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 16;
        var stateMethod = typeof(WhimTexDocument).GetMethod("GetLayerContentState", instance);
        context.True(stateMethod != null, "The document exposes its package-owned content query");
        string State(Layer layer, bool defer = false)
            => stateMethod.Invoke(document, new object[] { layer, defer }).ToString();

        Texture2D Pixels(int width, int height)
        {
            var texture = scope.OwnObject(new Texture2D(width, height, TextureFormat.RGBAFloat, false, true));
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(new Color[width * height]);
            texture.Apply(false, false);
            return texture;
        }
        var transparentPixels = Pixels(33, 17);
        var sparsePixels = Pixels(513, 257);
        sparsePixels.SetPixel(512, 256, Color.white); sparsePixels.Apply(false, false);
        var drawingPixels = Pixels(65, 33);
        var transparentFile = new Layer(new FileLayerBehaviour { sourceTexture = transparentPixels }) { layerName = "Transparent File" };
        var sparseFile = new Layer(new FileLayerBehaviour { sourceTexture = sparsePixels }) { layerName = "One source pixel" };
        var drawingBehaviour = new DrawingLayerBehaviour();
        typeof(DrawingLayerBehaviour).GetField("pixels", instance).SetValue(drawingBehaviour, drawingPixels);
        var drawing = new Layer(drawingBehaviour) { layerName = "Drawing pixels" };
        var clearColor = new Layer(new ColorFillLayerBehaviour { color = new Color(.8f, .3f, .1f, 0f) }) { layerName = "Transparent Color" };
        var opaqueColor = new Layer(new ColorFillLayerBehaviour { color = Color.red }) { layerName = "Opaque Color" };
        var fxClearLayer = new Layer(new ColorFillLayerBehaviour { color = Color.white }) { layerName = "Cleared by FX" };
        var fxOpaqueLayer = new Layer(new ColorFillLayerBehaviour { color = Color.clear }) { layerName = "Made opaque by FX" };
        var groupChild = new Layer(new ColorFillLayerBehaviour { color = Color.green }) { layerName = "Group child" };
        var group = new Layer(new GroupLayerBehaviour()) { layerName = "Group content" };
        group.children.Add(groupChild);
        var unavailableFile = new Layer(new FileLayerBehaviour()) { layerName = "Unknown source" };
        document.layers.AddRange(new[] { transparentFile, sparseFile, drawing, clearColor, opaqueColor,
            fxClearLayer, fxOpaqueLayer, group, unavailableFile });

        ShaderFX Effect(string source)
        {
            var factory = typeof(ShaderFX).GetMethod("CreateAgentDraft", statics, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var effect = scope.OwnObject((ShaderFX)factory.Invoke(null,
                new object[] { document, source, new List<ShaderFXParameter>() }));
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", instance).Invoke(effect, null);
            context.True(typeof(ShaderFX).GetField("compiledShader", instance).GetValue(effect) is Shader shader && shader.isSupported,
                "The owned inline FX compiled through the supported product path");
            return effect;
        }
        var clearFx = Effect("float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return float4(color.rgb, 0); }");
        var opaqueFx = Effect("float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return float4(.2, .6, .9, 1); }");
        fxClearLayer.fx.Add(clearFx); fxOpaqueLayer.fx.Add(opaqueFx);

        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
        typeof(WhimTexWindow).GetMethod("SetDocument", instance).Invoke(window, new object[] { document });
        window.ShowUtility(); window.position = new Rect(80, 80, 1120, 900); window.CreateGUI();
        var root = window.rootVisualElement;
        async Task Tick(int count = 1)
        {
            for (int i = 0; i < count; i++) await AsyncD.Tick(root, token);
        }
        void Refresh() => typeof(WhimTexWindow).GetMethod("RefreshToolkitInterface", instance)
            .Invoke(window, new object[] { false });
        void Changed() => typeof(WhimTexDocument).GetMethod("MarkChanged", instance).Invoke(document, null);
        async Task Expect(Layer layer, string expected, string description)
        {
            string actual = State(layer);
            for (int tick = 0; tick < 200 && actual != expected; tick++)
            { await Tick(); actual = State(layer); }
            if (actual != expected)
            {
                string diagnostic = "";
                foreach (var item in layer.fx)
                    if (item is ShaderFX effect)
                        diagnostic += $" FX active={effect.Active}" +
                            " unavailable=" + typeof(ShaderFX).GetProperty("IsUnavailable", instance).GetValue(effect) +
                            " time=" + typeof(ShaderFX).GetProperty("UsesUnityTimeInputs", instance).GetValue(effect);
                object cache = typeof(WhimTexDocument).GetField("layerContent", instance).GetValue(document);
                foreach (string field in new[] { "updating", "disposed" })
                    diagnostic += " cache." + field + "=" + cache.GetType().GetField(field, instance).GetValue(cache);
                diagnostic += " now=" + EditorApplication.timeSinceStartup;
                object pendingEntry = cache.GetType().GetField("pending", instance).GetValue(cache);
                diagnostic += " pending=" + (pendingEntry == null ? "null" :
                    ((Layer)pendingEntry.GetType().GetField("layer", instance).GetValue(pendingEntry)).layerName);
                object detector = cache.GetType().GetField("detector", instance).GetValue(cache);
                diagnostic += " detectorPending=" + (detector != null &&
                    (bool)detector.GetType().GetProperty("isPending", instance).GetValue(detector));
                var entries = (System.Collections.IDictionary)cache.GetType().GetField("entries", instance).GetValue(cache);
                object entry = entries[layer];
                foreach (string field in new[] { "ready", "queued", "stamp", "generation", "state" })
                    diagnostic += " " + field + "=" + entry.GetType().GetField(field, instance).GetValue(entry);
                object renders = cache.GetType().GetField("renders", instance).GetValue(cache);
                diagnostic += " inspectable=" + typeof(WhimTexDocument).GetMethod("CanInspectLayerContent", instance)
                    .Invoke(document, new object[] { layer, new HashSet<Layer>() });
                try { diagnostic += " currentStamp=" + renders.GetType().GetMethod("Stamp", instance).Invoke(renders, new object[] { layer }); }
                catch (Exception error) { diagnostic += " stampError=" + error; }
                RenderTexture rendered = null;
                try
                {
                    rendered = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerContent", instance)
                        .Invoke(document, new object[] { layer, 16, 16, renders });
                    diagnostic += " directRender=" + (rendered != null);
                }
                catch (Exception error) { diagnostic += " directRenderError=" + error; }
                finally { if (rendered != null) RenderTexture.ReleaseTemporary(rendered); }
                throw new InvalidOperationException(description + ": expected " + expected + ", got " + actual + diagnostic);
            }
            context.Equal(expected, actual, description);
        }
        VisualElement Row(Layer layer)
        {
            foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
                if (Equals(row.userData, layer.Id)) return row;
            throw new InvalidOperationException("Missing owned content row: " + layer.layerName);
        }
        async Task Icon(Layer layer, bool empty, string description)
        {
            VisualElement row = null, icon = null;
            for (int tick = 0; tick < 40; tick++)
            {
                Refresh(); await Tick(2);
                row = Row(layer);
                icon = row.Q<VisualElement>(className: "whimtex-layer-empty-icon");
                if (icon != null && icon.ClassListContains("whimtex-hidden") == !empty) break;
            }
            context.True(icon != null, description + " has an empty-content icon");
            context.Equal(!empty, icon.ClassListContains("whimtex-hidden"), description + " icon visibility follows confirmed content");
            context.Equal(PickingMode.Ignore, icon.pickingMode, description + " icon does not intercept row input");
            context.Near(20f, icon.parent.worldBound.width, .1f, description + " keeps the thumbnail slot width");
            context.Near(20f, icon.parent.worldBound.height, .1f, description + " keeps the thumbnail slot height");
            var selectableSlot = row.Q<VisualElement>(className: "whimtex-layer-thumbnail") ??
                row.Q<VisualElement>(className: "whimtex-group-foldout");
            context.True(selectableSlot == icon.parent && selectableSlot.worldBound.width > 0f,
                description + " retains the original thumbnail or foldout alpha-selection slot");
            var slotPicked = root.panel.Pick(selectableSlot.worldBound.center);
            context.True(slotPicked == selectableSlot || slotPicked != null && selectableSlot.Contains(slotPicked),
                description + " preserves actual picking of the alpha-selection slot");
            var image = icon.parent.Q<Image>();
            if (image != null)
                context.Equal(empty ? Visibility.Hidden : Visibility.Visible, image.resolvedStyle.visibility,
                    description + " retains the regular image layout while switching its visibility");
            var name = row.Q<TextField>(className: "whimtex-layer-name");
            var picked = root.panel.Pick(name.worldBound.center);
            context.True(picked == name || picked != null && name.Contains(picked),
                description + " preserves actual name hit testing");
        }

        await Tick(4);
        context.Equal("Unknown", State(unavailableFile), "A missing File source remains Unknown");
        await Icon(unavailableFile, false, "Unknown File");
        await Expect(transparentFile, "Empty", "A transparent File is Empty at intrinsic source dimensions");
        await Icon(transparentFile, true, "Transparent File");
        Rect originalSlot = Row(transparentFile).Q<VisualElement>(className: "whimtex-layer-empty-icon").parent.worldBound;
        float originalNameX = Row(transparentFile).Q<TextField>(className: "whimtex-layer-name").worldBound.x;
        await Expect(sparseFile, "Present", "One pixel outside the small preview sampling grid remains Present");
        var dimensions = new object[] { sparseFile, 0, 0 };
        typeof(WhimTexDocument).GetMethod("GetLayerContentSize", instance).Invoke(document, dimensions);
        context.Equal(513, (int)dimensions[1], "File content retains its full width instead of the document or thumbnail width");
        context.Equal(257, (int)dimensions[2], "File content retains its full height instead of the document or thumbnail height");
        await Icon(sparseFile, false, "One source pixel File");
        await Expect(drawing, "Empty", "Transparent owned Drawing pixels are Empty");
        await Expect(clearColor, "Empty", "Nonzero RGB with zero alpha is Empty");
        await Expect(opaqueColor, "Present", "Opaque Color content is Present");
        await Expect(fxClearLayer, "Empty", "FX can make opaque source content Empty");
        await Icon(fxClearLayer, true, "Cleared by FX");
        await Expect(fxOpaqueLayer, "Present", "FX can make transparent source content Present");
        await Icon(fxOpaqueLayer, false, "Made opaque by FX");

        // A requested content check may borrow an already required exact FX render.
        typeof(WhimTexDocument).GetMethod("ReleaseLayerContent", instance).Invoke(document, null);
        context.Equal("Unknown", State(fxClearLayer), "Changed FX content starts asynchronous detection");
        object reuseCache = typeof(WhimTexDocument).GetField("layerContent", instance).GetValue(document);
        object Pending() => reuseCache.GetType().GetField("pending", instance).GetValue(reuseCache);
        RenderTexture RenderContent(int w, int h) => (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerContent", instance)
            .Invoke(document, new object[] { fxClearLayer, w, h, null });
        var miniature = RenderContent(8, 8); RenderTexture.ReleaseTemporary(miniature);
        context.True(Pending() == null, "Reduced preview cannot establish exact alpha presence");
        var exactContent = RenderContent(16, 16); RenderTexture.ReleaseTemporary(exactContent);
        var reuseEntries = (System.Collections.IDictionary)reuseCache.GetType().GetField("entries", instance).GetValue(reuseCache);
        object reuseEntry = reuseEntries[fxClearLayer];
        context.True(Pending() == reuseEntry || (bool)reuseEntry.GetType().GetField("ready", instance).GetValue(reuseEntry),
            "Full-size finished FX output starts detection before the fallback render queue runs");
        await Expect(fxClearLayer, "Empty", "Borrowed full-size FX result retains the correct alpha answer");
        typeof(WhimTexDocument).GetMethod("ReleaseLayerContent", instance).Invoke(document, null);
        await Expect(drawing, "Empty", "Drawing begins with confirmed block coverage");
        drawing.fx.Add(opaqueFx); Changed();
        var drawingOutput = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderLayerContent", instance)
            .Invoke(document, new object[] { drawing, 65, 33, null });
        RenderTexture.ReleaseTemporary(drawingOutput);
        reuseCache = typeof(WhimTexDocument).GetField("layerContent", instance).GetValue(document);
        reuseEntries = (System.Collections.IDictionary)reuseCache.GetType().GetField("entries", instance).GetValue(reuseCache);
        reuseEntry = reuseEntries[drawing];
        context.True(Pending() == reuseEntry || (bool)reuseEntry.GetType().GetField("ready", instance).GetValue(reuseEntry) &&
            reuseEntry.GetType().GetField("state", instance).GetValue(reuseEntry).ToString() == "Present",
            "Enabling FX offers its ready output before the next icon poll switches away from block coverage");
        await Expect(drawing, "Present", "Enabled FX can create alpha over an empty Drawing");
        drawing.fx.Remove(opaqueFx); Changed();
        await Expect(drawing, "Empty", "Removing FX returns to the unchanged block map");
        await Expect(clearColor, "Empty", "Recreated cache confirms the independent transparent layer before deferral");

        opaqueColor.opacity = 0f; opaqueColor.enabled = false; Changed();
        context.Equal("Empty", State(clearColor, true),
            "An unrelated opacity/visibility edit preserves a confirmed transparent layer during deferred updates");
        await Expect(opaqueColor, "Present", "Own zero Opacity and visibility do not change opaque content to Empty");
        await Icon(opaqueColor, false, "Hidden zero-Opacity Color");
        group.opacity = 0f; group.enabled = false; Changed();
        await Expect(group, "Present", "Group content ignores its own zero Opacity and visibility");
        context.True(Row(group).Q<VisualElement>(className: "whimtex-layer-empty-icon") == null,
            "A nonempty group retains its ordinary foldout without the content icon");
        groupChild.opacity = 0f; Changed();
        await Expect(group, "Empty", "Child Opacity remains part of the group's own composited content");
        context.True(Row(group).Q<VisualElement>(className: "whimtex-layer-empty-icon") == null,
            "An empty group also retains its ordinary foldout without the content icon");
        var foldout = Row(group).Q<VisualElement>(className: "whimtex-group-foldout");
        bool caret = false;
        foreach (var label in foldout.Query<Label>().ToList()) caret |= label.text == "▼" || label.text == "▶";
        context.True(caret, "The empty group preserves its foldout caret");

        transparentPixels.SetPixel(32, 16, Color.white); transparentPixels.Apply(false, false); Changed();
        context.Equal("Unknown", State(transparentFile, true), "Deferred content refresh cannot retain the old Empty answer");
        Refresh();
        context.True(Row(transparentFile).Q<VisualElement>(className: "whimtex-layer-empty-icon").ClassListContains("whimtex-hidden"),
            "Unknown content immediately restores the regular thumbnail");
        await Expect(transparentFile, "Present", "A File upload invalidates Empty and detects its new visible pixel");
        await Icon(transparentFile, false, "Updated File");
        var updatedSlot = Row(transparentFile).Q<VisualElement>(className: "whimtex-layer-empty-icon").parent.worldBound;
        context.Near(originalSlot.width, updatedSlot.width, .1f, "Empty-to-Present does not change the thumbnail slot width");
        context.Near(originalNameX, Row(transparentFile).Q<TextField>(className: "whimtex-layer-name").worldBound.x, .1f,
            "Empty-to-Present does not move the layer name");
        transparentPixels.SetPixel(32, 16, Color.clear); transparentPixels.Apply(false, false); Changed();
        await Expect(transparentFile, "Empty", "Clearing the File pixel invalidates Present and restores Empty");
        await Icon(transparentFile, true, "Recleared File");

        drawingPixels.SetPixel(64, 32, Color.white); drawingPixels.Apply(false, false);
        typeof(WhimTexDocument).GetMethod("InvalidateDrawingLayerSurfaces", instance).Invoke(document, null); Changed();
        await Expect(drawing, "Present", "Drawing pixel changes invalidate cached Empty");
        await Icon(drawing, false, "Updated Drawing");
        clearFx.Active = false; Changed();
        await Expect(fxClearLayer, "Present", "Disabling alpha-clearing FX invalidates cached Empty");
        clearFx.Active = true; Changed();
        await Expect(fxClearLayer, "Empty", "Re-enabling alpha-clearing FX restores Empty");
        groupChild.opacity = 1f; Changed();
        await Expect(group, "Present", "Restoring child content invalidates the empty group answer");
        await Expect(transparentFile, "Empty", "The final File badge remains confirmed");
        await Expect(clearColor, "Empty", "The final transparent Color badge remains confirmed");
        await Expect(fxClearLayer, "Empty", "The final alpha-clearing FX badge remains confirmed");

        Refresh(); await Tick(4);
        string previewPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
            "Temp", "WhimTex", "layer-content-preview-" + System.Guid.NewGuid().ToString("N") + ".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(previewPath));
        await UnityACapture.Capture(window, previewPath, token);
        context.True(System.IO.File.Exists(previewPath) && new System.IO.FileInfo(previewPath).Length > 32,
            "The owned content-icon preview was retained for actual visual review");
    });
}
