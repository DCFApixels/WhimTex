using System;
using System.IO;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class BrushHeaderLayoutSmoke
{
    const string CaptureKey = "WhimTex.BrushHeaderLayoutCapture";
    const string TestName = "WhimTex brush header layout test";
    static VisualElement FindRow(out EditorWindow owner)
    {
        foreach (var test in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (test.name == TestName) { owner = test; return test.rootVisualElement.Q(className: "whimtex-brush-header"); }
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
        {
            var edge = window.rootVisualElement.Q(className: "whimtex-brush-edge");
            if (edge != null && edge.worldBound.height > 0 && edge.parent.resolvedStyle.display != DisplayStyle.None)
            { owner = window; return edge.parent; }
        }
        throw new Exception("Select Brush in an open WhimTex window before running this read-only layout check.");
    }
    public static string Inspect() => Measure(false);
    public static string Verify() => Measure(true);
    public static string SetupGradient()
    {
        Cleanup();
        var window = ScriptableObject.CreateInstance<EditorWindow>();
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
        return "Temporary gradient header ready; verify/capture after layout, then Cleanup.";
    }
    public static string Cleanup()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) if (window.name == TestName) window.Close();
        return "Temporary header test closed.";
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
            if (verify && (Mathf.Abs(delta) > .6f || label.resolvedStyle.unityTextAlign != TextAnchor.MiddleLeft))
                throw new Exception("Label not vertically centered: " + label.text + ", " + delta);
        });
        var gradient = row.Q<WhimTexGradientValueField>();
        if (gradient != null && gradient.resolvedStyle.display != DisplayStyle.None)
        {
            var button = gradient.Q<Button>();
            var sizeInput = row.Q(className: "whimtex-brush-size").Q(className: "unity-base-field__input");
            report.AppendLine("Gradient=" + button.worldBound + ", Size input=" + sizeInput.worldBound);
            if (verify && (Mathf.Abs(button.worldBound.height - sizeInput.worldBound.height) > .6f ||
                Mathf.Abs(button.worldBound.center.y - gradient.labelElement.worldBound.center.y) > .6f ||
                button.worldBound.yMin < gradient.worldBound.yMin || button.worldBound.yMax > gradient.worldBound.yMax))
                throw new Exception("Gradient swatch does not fit/match the ordinary field: " + report);
        }
        return (verify ? "PASS\n" : "Layout\n") + report;
    }
    public static string Capture()
    {
        var row = FindRow(out var owner);
        SessionState.SetString(CaptureKey, "Pending repaint");
        var probe = new Probe(row, owner) { pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute;
        probe.style.width = 1; probe.style.height = 1;
        owner.rootVisualElement.Add(probe);
        owner.Repaint();
        return "Queued read-only header capture.";
    }
    public static string CaptureResult() => SessionState.GetString(CaptureKey, "Not started");
    sealed class Probe : ImmediateModeElement
    {
        readonly VisualElement row;
        readonly EditorWindow owner;
        bool done;
        public Probe(VisualElement row, EditorWindow owner) { this.row = row; this.owner = owner; }
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
                string path = Path.GetFullPath("Temp/WhimTex/brush-header-layout.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
                SessionState.SetString(CaptureKey, path);
            }
            catch (Exception e) { SessionState.SetString(CaptureKey, "Failed: " + e.Message); }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                schedule.Execute(RemoveFromHierarchy);
            }
        }
    }
}
