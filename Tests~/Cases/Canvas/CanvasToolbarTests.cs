using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class CanvasToolbarTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    private static async Task<string> BodyRun()
    {
        var window = Scope.OwnWindow(ScriptableObject.CreateInstance<TextureCompositorWindow>());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(TextureCompositorWindow);
        void Check(bool condition, string message) { T.True(condition, message); }
        try
        {
            window.ShowUtility();
            window.position = new Rect(100, 100, 1000, 720);
            window.CreateGUI();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
            var root = window.rootVisualElement;
            var canvas = root.Q<VisualElement>(className: "whimtex-canvas-toolbar");
            var output = root.Q<Button>("canvasOutputSettings");
            Check(output != null && canvas.Contains(output), "Output is not in canvas toolbar");
            Check(root.Query<Button>().ToList().Count(b => b.text == "Output") == 1, "Duplicate Output button");
            var children = canvas.Children().ToList();
            Check(children[children.IndexOf(output) - 1] is IntegerField, "Output does not follow canvas dimensions");
            var tiled = root.Q<Button>("tiledCanvasButton");
            Check(tiled != null && root.Q<VisualElement>("canvasFooterContext").Contains(tiled), "Tiled is not in footer");
            Check(!canvas.Query<Toggle>().ToList().Any(t => t.label == "Tiled"), "Old Tiled checkbox remains");
            type.GetMethod("SetTiledCanvas", flags).Invoke(window, new object[] { true });
            Check(tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled enabled state missing");
            window.CreateGUI();
            tiled = window.rootVisualElement.Q<Button>("tiledCanvasButton");
            Check(tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled state lost on view rebuild");
            type.GetMethod("SetTiledCanvas", flags).Invoke(window, new object[] { false });
            Check(!tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled disabled state missing");
            return null;
        }
        finally { WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window); }
    }
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyRun(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}

