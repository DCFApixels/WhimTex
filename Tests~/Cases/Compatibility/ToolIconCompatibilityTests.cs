// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
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
        int checks = 0;
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
            window.rootVisualElement.style.flexDirection = FlexDirection.Row;
            foreach (string tool in new[] { "BlurBrush", "Text" })
            foreach (int size in new[] { 22, 24, 48, 96 })
            {
                var icon = (VisualElement)Activator.CreateInstance(iconType, flags, null,
                    new object[] { Enum.Parse(toolType, tool), false }, null);
                icon.style.width = size; icon.style.height = size; icon.style.color = Color.white;
                window.rootVisualElement.Add(icon);
            }
            window.ShowUtility();
            window.position = new Rect(150, 150, 440, 140);
            window.Repaint();
            await Task.Delay(750, token);
            Check(window.rootVisualElement.panel != null, "Icon panel is attached");
            Check(drawError == null, "Icon rendering error: " + drawError);
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
