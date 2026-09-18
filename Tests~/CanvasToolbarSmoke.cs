using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class CanvasToolbarSmoke
{
    public static async Task<string> Main()
    {
        var window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(TextureCompositorWindow);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        try
        {
            window.ShowUtility();
            window.position = new Rect(100, 100, 1000, 720);
            window.CreateGUI();
            await Task.Delay(300);
            var root = window.rootVisualElement;
            var canvas = root.Q<VisualElement>(className: "whimtex-canvas-toolbar");
            var output = root.Q<Button>("canvasOutputSettings");
            Check(output != null && canvas.Contains(output), "Output is not in canvas toolbar");
            Check(root.Query<Button>().ToList().Count(b => b.text == "Output") == 1, "Duplicate Output button");
            var children = canvas.Children().ToList();
            Check(children[children.IndexOf(output) - 1] is IntegerField, "Output does not follow canvas dimensions");
            var tiled = root.Q<Button>("tiledPreviewButton");
            Check(tiled != null && root.Q<VisualElement>("previewFooterContext").Contains(tiled), "Tiled is not in footer");
            Check(!canvas.Query<Toggle>().ToList().Any(t => t.label == "Tiled"), "Old Tiled checkbox remains");
            type.GetMethod("SetTiledPreview", flags).Invoke(window, new object[] { true });
            Check(tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled enabled state missing");
            window.CreateGUI();
            tiled = window.rootVisualElement.Q<Button>("tiledPreviewButton");
            Check(tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled state lost on view rebuild");
            type.GetMethod("SetTiledPreview", flags).Invoke(window, new object[] { false });
            Check(!tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled disabled state missing");
            return "PASS: Output beside canvas size, no duplicate header control, Tiled in footer with enabled/disabled state and view rebuild persistence.";
        }
        finally { window.Close(); }
    }
}
