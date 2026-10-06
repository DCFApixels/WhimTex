using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class BrushHeaderLayoutTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    static bool Visible(VisualElement element, VisualElement row)
    {
        for (var current = element; current != null; current = current.parent)
        {
            if (current.resolvedStyle.display == DisplayStyle.None) return false;
            if (current == row) return true;
        }
        return false;
    }

    static void DragLabel(Label label)
    {
        Vector2 start = label.worldBound.center;
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
        { down.target = label; label.SendEvent(down); }
        using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = start + Vector2.right * 24, delta = Vector2.right * 24 }))
        { move.target = label; label.SendEvent(move); }
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + Vector2.right * 24 }))
        { up.target = label; label.SendEvent(up); }
    }

    static void CheckRow(VisualElement row, string context)
    {
        var trace = new StringBuilder(context + " row=" + row.worldBound + " compact=" + row.ClassListContains("whimtex-canvas-header-row--compact") + "\n");
        const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        trace.AppendLine("settled=" + row.GetType().GetField("settledWidth", hidden)?.GetValue(row) + " pending=" +
            ((IVisualElementScheduledItem)row.GetType().GetField("pending", hidden)?.GetValue(row))?.isActive);
        foreach (var child in row.Children()) trace.AppendLine(child.GetType().Name + " " + string.Join(" ", child.GetClasses()) + " " + child.worldBound);
        row.Query<Slider>().ForEach(slider =>
        {
            var track = slider.Q(className: "unity-base-slider__drag-container");
            trace.AppendLine(slider.label + ": " + slider.worldBound + " track=" + track.worldBound + " display=" + track.resolvedStyle.display + " min=" + track.resolvedStyle.minWidth);
        });
        File.AppendAllText(Path.Combine(Scope.Temp, "canvas-header-trace.txt"), trace.ToString());
        T.True(row.ClassListContains("whimtex-canvas-header-row"), context + ": shared row");
        var children = new System.Collections.Generic.List<VisualElement>();
        foreach (var child in row.Children()) if (Visible(child, row)) children.Add(child);
        for (int i = 0; i < children.Count; i++)
        {
            var a = children[i].worldBound;
            for (int j = i + 1; j < children.Count; j++)
            {
                var b = children[j].worldBound;
                T.True(Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) < .6f ||
                    Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) < .6f, context + ": fields overlap");
            }
        }
        row.Query<Label>(className: "unity-base-field__label").ForEach(label =>
        {
            if (!Visible(label, row) || string.IsNullOrEmpty(label.text)) return;
            var input = label.parent.Q(className: "unity-base-field__input");
            T.Near(12, label.resolvedStyle.fontSize, .01, context + ": common label font size");
            T.Equal(FontStyle.Normal, label.resolvedStyle.unityFontStyleAndWeight, context + ": common label font weight");
            T.Equal(TextAnchor.MiddleLeft, label.resolvedStyle.unityTextAlign, context + ": common label alignment");
            T.Near(label.worldBound.center.y, input.worldBound.center.y, .6, context + ": label/input center " + label.text);
            float intrinsic = label.MeasureTextSize(label.text, 0, TextElement.MeasureMode.Undefined, 0, TextElement.MeasureMode.Undefined).x;
            T.Near(intrinsic, label.worldBound.width, 1.1, context + ": intrinsic label width " + label.text);
            T.True(label.worldBound.xMax <= input.worldBound.xMin + .6f, context + ": label covers input " + label.text);
            T.True(input.worldBound.xMax <= label.parent.worldBound.xMax + .6f, context + ": input exceeds its field " + label.text);
        });
        row.Query<Slider>().ForEach(slider =>
        {
            if (!Visible(slider, row)) return;
            T.True(slider.showInputField, context + ": slider always has numeric input");
            var track = slider.Q(className: "unity-base-slider__drag-container");
            var number = slider.Q<TextField>(className: "unity-base-slider__text-field");
            T.True(number != null && Visible(number, row) && number.worldBound.width >= 53.5f, context + ": numeric input survives compact layout");
            if (row.ClassListContains("whimtex-canvas-header-row--compact"))
                T.Equal(DisplayStyle.None, track.resolvedStyle.display, context + ": compact hides every slider track");
            else T.True(track.worldBound.width >= 79.5f, context + ": common slider minimum width");
        });
        row.Query<Button>().ForEach(button =>
        {
            if (!Visible(button, row) || string.IsNullOrEmpty(button.text)) return;
            float text = button.MeasureTextSize(button.text, 0, TextElement.MeasureMode.Undefined, 0, TextElement.MeasureMode.Undefined).x;
            float expected = text + button.resolvedStyle.paddingLeft + button.resolvedStyle.paddingRight +
                button.resolvedStyle.borderLeftWidth + button.resolvedStyle.borderRightWidth;
            T.Near(expected, button.worldBound.width, 1.1, context + ": content-sized button " + button.text);
        });
    }

    static async Task WaitForLayout(EditorWindow window, VisualElement row, bool? compact = null)
    {
        int stable = 0;
        string previous = null;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            window.Repaint();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(20, Cancellation);
            bool mode = row.ClassListContains("whimtex-canvas-header-row--compact");
            bool ready = compact == null || mode == compact.Value;
            var signature = new StringBuilder(row.worldBound.ToString() + mode);
            foreach (var child in row.Children()) signature.Append(child.worldBound);
            row.Query(className: "unity-base-slider__drag-container").ForEach(track =>
            {
                if (!Visible(track.parent, row)) return;
                ready &= mode ? track.resolvedStyle.display == DisplayStyle.None : track.worldBound.width >= 79.5f;
                signature.Append(track.worldBound).Append(track.resolvedStyle.display);
            });
            string current = signature.ToString();
            stable = ready && current == previous ? stable + 1 : 0;
            previous = current;
            if (stable >= 3) return;
        }
        T.True(false, "Header did not reach a stable layout: " + row.name + ", expected compact=" + compact);
    }

    static void CheckDropdownWidths(VisualElement toolbar, VisualElement settings, string tool)
    {
        T.Near(142, toolbar.Q("canvasOutputFilter").worldBound.width, .6, "Canvas Filter retains its configured width");
        T.Near(158, toolbar.Q("canvasOutputPrecision").worldBound.width, .6, "Canvas Precision retains its configured width");
        foreach (var pair in new[] {
            ("whimtex-header-dropdown--72", 72f), ("whimtex-header-dropdown--78", 78f),
            ("whimtex-header-dropdown--90", 90f), ("whimtex-header-dropdown--118", 118f),
            ("whimtex-header-dropdown--150", 150f), ("whimtex-shape-kind", 112f), ("whimtex-area-mode", 86f) })
        {
            settings.Query(className: pair.Item1).ForEach(field =>
            {
                if (!Visible(field, settings)) return;
                T.Near(pair.Item2, field.worldBound.width, .6, tool + ": dropdown retains its configured width");
                var input = field.Q(className: "unity-base-field__input");
                T.True(input.worldBound.width >= field.worldBound.width - 5, tool + ": dropdown uses its whole configured width");
            });
        }
        foreach (int width in new[] { 94, 100 })
            settings.Query(className: "whimtex-header-dropdown-input--" + width).ForEach(field =>
            {
                if (!Visible(field, settings)) return;
                var dump = new StringBuilder();
                void Dump(VisualElement element, int depth)
                {
                    dump.Append(' ', depth * 2).Append(element.GetType().Name).Append(' ').Append(string.Join(" ", element.GetClasses()))
                        .Append(" bounds=").Append(element.worldBound).Append(" width=").Append(element.resolvedStyle.width).AppendLine();
                    foreach (var child in element.Children()) Dump(child, depth + 1);
                }
                Dump(field, 0);
                File.AppendAllText(Path.Combine(Scope.Temp, "canvas-header-trace.txt"), dump.ToString());
                T.Near(width, field.Q(className: "unity-base-field__input").worldBound.width, .6,
                    tool + ": labeled dropdown retains its input width");
            });
    }

    static async Task BodyAllHeaders(int first, int count, bool dynamic)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(TextureCompositorWindow);
        var window = Scope.OwnWindow(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        var document = (TextureCompositor)type.GetField("compositor", flags).GetValue(window);
        document.width = document.height = 32;
        Layer layer = new DrawingLayerBehaviour(); document.layers.Add(layer);
        type.GetField("selectedLayerId", flags).SetValue(window, layer.Id);
        window.ShowUtility(); window.position = new Rect(60, 60, 1800, 800); window.CreateGUI();
        var root = window.rootVisualElement;
        var toolbar = root.Q(className: "whimtex-canvas-toolbar");
        var settings = root.Q("canvasToolSettings");
        var toolType = type.GetNestedType("CanvasTool", System.Reflection.BindingFlags.NonPublic);
        var setTool = type.GetMethod("SetCanvasTool", flags);
        string[] tools = { "None", "Transform", "Zoom", "Shape", "RectangleSelect", "PolygonSelect", "Fill", "Pencil", "Brush", "BlurBrush", "SmudgeBrush", "HealingBrush" };
        for (int index = first; index < first + count; index++)
        {
            string tool = tools[index];
            setTool.Invoke(window, new object[] { Enum.Parse(toolType, tool) });
            foreach (float width in new[] { 1200f, 600f, 280f, 1200f })
            {
                toolbar.style.width = width;
                settings.style.width = width;
                window.Repaint();
                await WaitForLayout(window, toolbar);
                CheckRow(toolbar, tool + " toolbar " + width);
                int visible = 0;
                foreach (var row in settings.Children())
                {
                    if (!Visible(row, settings)) continue;
                    visible++;
                    await WaitForLayout(window, row, width == 1200 ? false : (bool?)null);
                    CheckRow(row, tool + " settings " + width);
                    if (width == 1200) T.True(!row.ClassListContains("whimtex-canvas-header-row--compact"), tool + ": tracks restore when width returns");
                }
                T.Equal(1, visible, tool + ": exactly one settings row");
                CheckDropdownWidths(toolbar, settings, tool);
            }
        }
        if (!dynamic) { window.DiscardChanges(); return; }
        setTool.Invoke(window, new object[] { Enum.Parse(toolType, "SmudgeBrush") });
        var smudge = settings.Q("smudgeBrushSettings");
        var future = new Slider("Long future parameter label", 0, 100) { value = 40 };
        smudge.Add(future);
        type.GetMethod("RefreshToolkitInterface", flags).Invoke(window, new object[] { true });
        settings.style.width = 1200;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation);
        await WaitForLayout(window, smudge, false);
        CheckRow(smudge, "Dynamically added parameter");
        T.True(future.showInputField, "New sliders receive numeric input automatically");
        future.label = "Short";
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation);
        await WaitForLayout(window, smudge, false);
        CheckRow(smudge, "Changed label text");
        var source = smudge.Q<EnumField>(className: "whimtex-smudge-mode");
        Enum previous = source.value;
        foreach (Enum choice in Enum.GetValues(previous.GetType()))
        {
            source.SetValueWithoutNotify(choice);
            window.Repaint();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            T.Near(118, source.worldBound.width, .6, "Source dropdown width does not change with its text");
        }
        source.SetValueWithoutNotify(previous);
        settings.style.width = 600;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(250, Cancellation);
        await WaitForLayout(window, smudge, true);
        CheckRow(smudge, "Dynamically added slider in compact row");
        T.True(smudge.ClassListContains("whimtex-canvas-header-row--compact"), "Insufficient width hides tracks");
        var number = future.Q<TextField>(className: "unity-base-slider__text-field");
        number.value = "73";
        T.Near(73, future.value, .01, "Numeric slider editing works with its track hidden");
        float beforeDrag = future.value;
        DragLabel(future.labelElement);
        T.True(future.value != beforeDrag, "Slider value changes by dragging its own label with track hidden");
        var size = smudge.Q<FloatField>("smudgeSize");
        beforeDrag = size.value;
        DragLabel(size.labelElement);
        T.True(size.value != beforeDrag, "Numeric fields retain native label dragging");
        await WhimTex.Tests.UnityA.UnityACapture.Capture(window, Path.Combine(Scope.Temp, "canvas-header-compact.png"), Cancellation);
        settings.style.width = toolbar.style.width = 1200;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(250, Cancellation);
        await WaitForLayout(window, smudge, false);
        float expanded = 0;
        foreach (var child in smudge.Children())
            if (Visible(child, smudge)) expanded += child.worldBound.width + child.resolvedStyle.marginLeft + child.resolvedStyle.marginRight;
        settings.style.width = expanded + 7;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(250, Cancellation);
        await WaitForLayout(window, smudge, true);
        T.True(smudge.ClassListContains("whimtex-canvas-header-row--compact"), "Just below measured expanded width hides tracks");
        for (int frame = 0; frame < 20; frame++)
        {
            settings.style.width = expanded + 8 + (frame % 2 == 0 ? -.25f : .25f);
            window.Repaint();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(25, Cancellation);
            T.True(smudge.ClassListContains("whimtex-canvas-header-row--compact"), "Boundary does not flicker, frame " + frame);
        }
        settings.style.width = expanded + 20;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(250, Cancellation);
        await WaitForLayout(window, smudge, false);
        T.True(!smudge.ClassListContains("whimtex-canvas-header-row--compact"), "Tracks restore beyond measured width and hysteresis");
        settings.style.width = 1200;
        window.Repaint();
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(150, Cancellation);
        await WhimTex.Tests.UnityA.UnityACapture.Capture(window, Path.Combine(Scope.Temp, "canvas-header-wide.png"), Cancellation);
        var dump = new StringBuilder();
        void Dump(VisualElement element, int depth)
        {
            dump.Append(' ', depth * 2).Append(element.GetType().Name).Append(' ').Append(string.Join(" ", element.GetClasses()))
                .Append(" bounds=").Append(element.worldBound).Append(" width=").Append(element.resolvedStyle.width)
                .Append(" inline=").Append(element.style.width).AppendLine();
            foreach (var child in element.Children()) Dump(child, depth + 1);
        }
        Dump(toolbar, 0); Dump(smudge, 0);
        File.WriteAllText(Path.Combine(Scope.Temp, "canvas-header-fields.txt"), dump.ToString());
        future.RemoveFromHierarchy();
        var outside = new FloatField("Outside header"); root.Add(outside);
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(150, Cancellation);
        T.True(outside.labelElement.resolvedStyle.minWidth.value > 0, "Ordinary fields outside header retain the Editor label style");
        window.DiscardChanges();
    }

    static string TestName => Scope.Tag + "-brush-header";
    static VisualElement FindRow(out EditorWindow owner) { foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (window.name == TestName) { owner = window; return window.rootVisualElement.Q(className: "whimtex-brush-header"); } throw new Exception("Owned header fixture missing"); }
    
    private static string Inspect() => Measure(false);
    private static string Verify() => Measure(true);
    private static string SetupGradient()
    {
        CleanupFixture();
        var window = Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTex.Tests.UnityA.UnityAHeaderWindow>());
        window.name = TestName; window.titleContent = new GUIContent("Brush header test");
        window.ShowUtility(); window.position = new Rect(150, 150, 650, 80);
        var root = window.rootVisualElement;
        root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/WhimTexSplitView.uss"));
        var row = (VisualElement)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexCanvasHeaderRow", true), true);
        row.AddToClassList("whimtex-tool-settings-row"); row.AddToClassList("whimtex-brush-header"); root.Add(row);
        var size = new FloatField("Size") { value = 122 }; size.AddToClassList("whimtex-brush-size");
        row.Add(size);
        var edge = new VisualElement(); edge.AddToClassList("whimtex-brush-edge"); row.Add(edge);
        var gradient = new WhimTexGradientValueField("Gradient") { value = new WhimTexGradient() };
        gradient.AddToClassList("whimtex-brush-edge-field"); edge.Add(gradient);
        var mode = new Button { text = "▾" }; mode.AddToClassList("whimtex-brush-edge-mode"); edge.Add(mode);
        foreach (string label in new[] { "Opacity", "Flow" })
        { var field = new FloatField(label) { value = 100 }; field.AddToClassList("whimtex-brush-strength"); row.Add(field); }
        var pressure = new Toggle("Pressure"); pressure.AddToClassList("whimtex-brush-pressure"); row.Add(pressure);
        return null;
    }
    private static string CleanupFixture()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (window.name == TestName) WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
        return null;
    }
    static string Measure(bool verify)
    {
        var row = FindRow(out var owner);
        var report = new StringBuilder();
        row.Query<Label>(className: "unity-base-field__label").ForEach(label =>
        {
            if (label.worldBound.height <= 0 || !Visible(label, row)) return;
            var input = label.parent.Q(className: "unity-base-field__input");
            float delta = label.worldBound.center.y - input.worldBound.center.y;
            report.AppendLine(label.text + ": label=" + label.worldBound + ", input=" + input.worldBound + ", center delta=" + delta);
            if (verify) T.True(!(Mathf.Abs(delta) > .6f || label.resolvedStyle.unityTextAlign != TextAnchor.MiddleLeft), "Label not vertically centered: " + label.text + ", " + delta);
        });
        var gradient = row.Q<WhimTexGradientValueField>();
        if (gradient != null && gradient.resolvedStyle.display != DisplayStyle.None)
        {
            var button = gradient.Q<Button>();
            var sizeInput = row.Q(className: "whimtex-brush-size").Q(className: "unity-base-field__input");
            report.AppendLine("Gradient=" + button.worldBound + ", Size input=" + sizeInput.worldBound);
            if (verify) T.True(!(Mathf.Abs(button.worldBound.height - sizeInput.worldBound.height) > .6f ||
                Mathf.Abs(button.worldBound.center.y - gradient.labelElement.worldBound.center.y) > .6f ||
                button.worldBound.yMin < gradient.worldBound.yMin || button.worldBound.yMax > gradient.worldBound.yMax), "Gradient swatch does not fit/match the ordinary field: " + report);
        }
        return (verify ? "PASS\n" : "Layout\n") + report;
    }
    sealed class CaptureProbe : ImmediateModeElement
    {
        internal VisualElement row;
        internal EditorWindow owner;
        internal string path;
        internal TaskCompletionSource<string> completion;
        bool done;
        protected override void ImmediateRepaint()
        {
            if (done) return;
            done = true;
            Texture2D image = null;
            try
            {
                var target = RenderTexture.active;
                float scale = EditorGUIUtility.pixelsPerPoint;
                int targetWidth = target != null ? target.width : Mathf.RoundToInt(owner.position.width * scale);
                int targetHeight = target != null ? target.height : Mathf.RoundToInt(owner.position.height * scale);
                var b = row.worldBound;
                int x = Mathf.Max(0, Mathf.RoundToInt(b.x * scale));
                int y = Mathf.Max(0, targetHeight - Mathf.RoundToInt(b.yMax * scale));
                int w = Mathf.Min(targetWidth - x, Mathf.RoundToInt(b.width * scale));
                int h = Mathf.Min(targetHeight - y, Mathf.RoundToInt(b.height * scale));
                image = new Texture2D(w, h, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(x, y, w, h), 0, 0, false); image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                completion.TrySetResult(path);
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }
    static async Task<string> Capture()
    {
        var row = FindRow(out var owner);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new CaptureProbe { row = row, owner = owner, path = Path.Combine(Scope.Temp, "brush-header-layout.png"),
            completion = completion, pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute; probe.style.width = 1; probe.style.height = 1;
        owner.rootVisualElement.Add(probe);
        try
        {
            using (Cancellation.Register(() => completion.TrySetCanceled())) { owner.Repaint(); return await completion.Task; }
        }
        finally { probe.RemoveFromHierarchy(); }
    }

private static async Task<string> BodyLayoutScenario() {
SetupGradient();
try {
    await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
    File.WriteAllText(Path.Combine(Scope.Temp, "brush-header-layout.txt"), Inspect());
    Verify();
    string capture = await Capture();
    T.True(File.Exists(capture), "Owned header repaint capture completed");
} finally { CleanupFixture(); }
return null;
}

private static async Task BodyLiveHeader()
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var type = typeof(TextureCompositorWindow);
    var window = Scope.OwnWindow(ScriptableObject.CreateInstance<TextureCompositorWindow>());
    window.name = TestName;
    var document = Scope.OwnObject((TextureCompositor)type.GetField("compositor", flags).GetValue(window));
    document.width = document.height = 32;
    Layer layer = new DrawingLayerBehaviour(); document.layers.Add(layer);
    type.GetField("selectedLayerId", flags).SetValue(window, layer.Id);
    var settingsField = type.GetField("paintSettings", flags);
    var settings = settingsField.GetValue(window);
    settings.GetType().GetMethod("ReleasePresetTip", flags).Invoke(settings, null);
    settings = Activator.CreateInstance(settings.GetType(), true);
    settingsField.SetValue(window, settings);
    settings.GetType().GetField("brushSize").SetValue(settings, 122f);
    var dynamics = settings.GetType().GetField("dynamics").GetValue(settings);
    var mode = dynamics.GetType().GetField("proceduralMode");
    window.ShowUtility(); window.position = new Rect(150, 150, 900, 650); window.CreateGUI();
    type.GetMethod("SetCanvasTool", flags).Invoke(window, new[] { Enum.Parse(type.GetNestedType("CanvasTool", flags), "Brush") });
    foreach (string value in new[] { "Hardness", "SdfGradient" })
    {
        mode.SetValue(dynamics, Enum.Parse(mode.FieldType, value));
        type.GetMethod("RefreshToolkitInterface", flags).Invoke(window, new object[] { true });
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
        var row = FindRow(out var owner);
        T.True(row != null && row.worldBound.height > 0 && row.resolvedStyle.display != DisplayStyle.None, "Actual owned Brush header is visible");
        File.WriteAllText(Path.Combine(Scope.Temp, "brush-header-" + value + ".txt"), Inspect());
        Verify();
        await Capture();
    }
    window.DiscardChanges();
}

public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLayoutScenario(); } finally { T = null; Scope = null; } }));
public static string StartLiveHeader(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLiveHeader(); } finally { T = null; Scope = null; } }));
static string StartHeaders(string runId, int first, int count, bool dynamic) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyAllHeaders(first, count, dynamic); } finally { T = null; Scope = null; } }));
public static string StartAllHeaders(string runId) => StartHeaders(runId, 0, 4, false);
public static string StartPaintHeaders(string runId) => StartHeaders(runId, 4, 4, false);
public static string StartBrushHeaders(string runId) => StartHeaders(runId, 8, 4, true);
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
