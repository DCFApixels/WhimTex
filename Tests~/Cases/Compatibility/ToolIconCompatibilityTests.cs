// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using WhimTex.Tests.UnityA;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// run_script entry: ToolIconCompatibilityTests.Main. Owns only a temporary window.
public static class ToolIconCompatibilityTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Start(string runId) => AsyncD.Start(runId, ExecuteMain);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static async Task ExecuteMain(TestContext runContext, System.Threading.CancellationToken token)
    {
        context = runContext;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        Type iconType = typeof(WhimTexWindow).GetNestedType("CanvasToolIcon", flags);
        Type toolType = typeof(WhimTexWindow).GetNestedType("CanvasTool", flags);
        var points = (Vector2[])iconType.GetField("BlurBrushOutline", flags).GetValue(null);
        void Check(bool valid, string message) { context.True(valid, message); }
        Check(points.Length == 64, "Drop outline resolution");
        Vector2 center = new Vector2(12f, 14.1f);
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[(i + 1) % points.Length] - center;
            Vector2 b = points[i] - center;
            Check(a.x * b.y - a.y * b.x > 0f, "Triangle must be nondegenerate and clockwise: " + i);
            Check(points[i].y >= 2.2f && points[i].y <= 21.6f, "Drop bounds: " + i);
        }
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        EditorWindow previousFocus = EditorWindow.focusedWindow;
        string drawError = null;
        void Log(string message, string stack, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                && (stack.Contains("CanvasToolIcon") || stack.Contains("UIElements")))
                drawError = message;
        }
        Application.logMessageReceived += Log;
        try
        {
            window.titleContent = new GUIContent("WhimTex icon check");
            window.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.dcfapixels.whimtex/src/WhimTexSplitView.uss"));
            window.rootVisualElement.AddToClassList("whimtex-theme");
            window.rootVisualElement.style.backgroundColor = new Color(.22f, .22f, .22f);
            var sizes = new VisualElement();
            sizes.style.flexDirection = FlexDirection.Row;
            sizes.style.alignItems = Align.Center;
            sizes.style.height = 110;
            window.rootVisualElement.Add(sizes);
            foreach (string tool in new[] { "BlurBrush", "Text", "HealingBrush" })
            foreach (int size in new[] { 22, 24, 48, 96 })
            {
                var icon = (VisualElement)Activator.CreateInstance(iconType, flags, null,
                    new object[] { Enum.Parse(toolType, tool), false }, null);
                icon.style.width = size; icon.style.height = size; icon.style.color = Color.white;
                icon.style.flexShrink = 0; icon.style.marginRight = 6;
                sizes.Add(icon);
            }
#if UNITY_6000_3_OR_NEWER
            var svgImages = new System.Collections.Generic.List<Image>();
            foreach (string mode in new[] { "Normal", "Selected", "Light" })
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                row.style.height = 34; row.AddToClassList("whimtex-tools");
                if (mode == "Light")
                {
                    row.AddToClassList("whimtex-tools--light");
                    row.style.backgroundColor = new Color(.77f, .77f, .77f);
                }
                var label = new Label(mode); label.style.width = 80; row.Add(label);
                foreach (float phase in new[] { 0f, .25f, .5f, .75f })
                {
                    var cell = new VisualElement(); cell.style.width = 42; cell.style.height = 34;
                    cell.AddToClassList("whimtex-tools");
                    if (mode == "Light") cell.AddToClassList("whimtex-tools--light");
                    var button = new Button(); button.AddToClassList("whimtex-tool-button");
                    if (mode == "Selected") button.AddToClassList("whimtex-tool-button--selected");
                    button.style.translate = new Translate(phase, phase);
                    var icon = (VisualElement)Activator.CreateInstance(iconType, flags, null,
                        new object[] { Enum.Parse(toolType, "HealingBrush"), false }, null);
                    button.Add(icon); cell.Add(button); row.Add(cell);
                    var image = icon.Q<Image>();
                    Check(image != null && image.vectorImage != null, "Healing Brush uses the imported SVG");
                    Check(image.pickingMode == PickingMode.Ignore, "SVG preserves button hit testing");
                    svgImages.Add(image);
                }
                window.rootVisualElement.Add(row);
            }
#endif
            window.ShowUtility();
            window.position = new Rect(150, 150, 650, 225);
            window.Repaint();
            await Task.Delay(750, token);
            Check(window.rootVisualElement.panel != null, "Icon panel is attached");
            Check(drawError == null, "Icon rendering error: " + drawError);
#if UNITY_6000_3_OR_NEWER
            for (int i = 0; i < svgImages.Count; i++)
            {
                Check(Math.Abs(svgImages[i].worldBound.width - 20f) < .05f, "SVG ink viewport is 20px in its 22px slot");
                Color expected = i < 4 ? new Color(210f / 255f, 210f / 255f, 210f / 255f)
                    : i < 8 ? new Color(238f / 255f, 238f / 255f, 238f / 255f)
                    : new Color(.2f, .2f, .2f);
                Check(Math.Abs(svgImages[i].tintColor.r - expected.r) < .01f,
                    "SVG follows the toolbar state tint: " + i + " actual=" + svgImages[i].tintColor.r + " expected=" + expected.r);
                Vector2 pixelCenter = svgImages[i].worldBound.center * EditorGUIUtility.pixelsPerPoint;
                Check(Math.Abs(pixelCenter.x - .5f - Mathf.Round(pixelCenter.x - .5f)) < .01f
                    && Math.Abs(pixelCenter.y - .5f - Mathf.Round(pixelCenter.y - .5f)) < .01f,
                    "SVG detail centers remain on physical pixel centers at every layout phase: " + i + " center=" + pixelCenter);
            }
#endif
            string preview = MigrationD.ProjectPath("Temp/WhimTex/tool-icons-preview.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preview));
            await UnityACapture.Capture(window, preview, token);
            return;
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => Application.logMessageReceived -= Log,
                () => window.Close(),
                () => { if (window != null) UnityEngine.Object.DestroyImmediate(window); },
                () => { if (previousFocus != null) previousFocus.Focus(); });
        }
    }
}
