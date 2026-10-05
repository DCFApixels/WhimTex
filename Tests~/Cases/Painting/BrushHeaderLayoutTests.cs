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
        var row = new VisualElement(); row.AddToClassList("whimtex-tool-settings-row"); row.AddToClassList("whimtex-brush-header"); root.Add(row);
        var size = new FloatField("Size") { value = 122 }; size.AddToClassList("whimtex-brush-size");
        size.style.width = 76; size.style.height = 22; row.Add(size);
        var edge = new VisualElement(); edge.AddToClassList("whimtex-brush-edge"); row.Add(edge);
        var gradient = new WhimTexGradientValueField("Gradient") { value = new WhimTexGradient() };
        gradient.AddToClassList("whimtex-brush-edge-field"); edge.Add(gradient);
        var mode = new Button { text = "▾" }; mode.AddToClassList("whimtex-brush-edge-mode"); edge.Add(mode);
        foreach (string label in new[] { "Opacity", "Flow" })
        { var field = new FloatField(label) { value = 100 }; field.AddToClassList("whimtex-brush-strength"); field.style.width = 94; field.style.height = 22; row.Add(field); }
        var pressure = new Toggle("Pressure"); pressure.AddToClassList("whimtex-brush-pressure"); pressure.style.width = 86; pressure.style.height = 22; row.Add(pressure);
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
            if (label.worldBound.height <= 0 || label.parent.resolvedStyle.display == DisplayStyle.None) return;
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
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
