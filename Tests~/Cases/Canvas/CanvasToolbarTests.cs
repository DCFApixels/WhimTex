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
        var window = Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(WhimTexWindow);
        void Check(bool condition, string message) { T.True(condition, message); }
        try
        {
            window.ShowUtility();
            window.position = new Rect(100, 100, 1000, 720);
            window.CreateGUI();
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation);
            var root = window.rootVisualElement;
            void CheckToolOrder()
            {
                var tools = window.rootVisualElement.Q<VisualElement>("canvasTools");
                var brush = tools.Q<Button>("brushTool");
                var pencil = tools.Q<Button>("pencilTool");
                var ordered = tools.Children().ToList();
                Check(brush != null && pencil != null && ordered.IndexOf(pencil) == ordered.IndexOf(brush) + 1,
                    "Pencil must immediately follow Brush in the tool column");
            }
            CheckToolOrder();
            var brushButton = root.Q<Button>("brushTool");
            var pencilButton = root.Q<Button>("pencilTool");
            T.Near(brushButton.worldBound.center.x, pencilButton.worldBound.center.x, .6, "Pencil aligns under Brush");
            T.Near(brushButton.worldBound.yMax + 3, pencilButton.worldBound.yMin, .6, "Pencil is directly below Brush");
            CheckPreviousToolShortcut(window);
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
            CheckToolOrder();
            tiled = window.rootVisualElement.Q<Button>("tiledCanvasButton");
            Check(tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled state lost on view rebuild");
            type.GetMethod("SetTiledCanvas", flags).Invoke(window, new object[] { false });
            Check(!tiled.ClassListContains("whimtex-channel-button--enabled"), "Tiled disabled state missing");
            return null;
        }
        finally { WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window); }
    }

    private static void CheckPreviousToolShortcut(WhimTexWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(WhimTexWindow);
        var toolType = type.GetNestedType("CanvasTool", BindingFlags.NonPublic);
        var current = type.GetField("canvasTool", flags);
        var previous = type.GetField("previousCanvasTool", flags);
        var toggleKey = (KeyCode)Convert.ToInt32(type.GetField("CanvasToolToggleKey", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue());
        void Select(string tool) => type.GetMethod("SetCanvasTool", flags).Invoke(window, new[] { Enum.Parse(toolType, tool) });
        void IsTool(string tool, string message) => T.Equal(tool, current.GetValue(window).ToString(), message);
        void Send(KeyCode key, bool down, VisualElement target = null, EventModifiers modifiers = EventModifiers.None)
        {
            var source = new Event { type = down ? EventType.KeyDown : EventType.KeyUp, keyCode = key, modifiers = modifiers };
            using EventBase evt = down ? (EventBase)KeyDownEvent.GetPooled(source) : KeyUpEvent.GetPooled(source);
            evt.target = target ?? window.rootVisualElement;
            (target ?? window.rootVisualElement).SendEvent(evt);
        }
        void Press(KeyCode key, VisualElement target = null, EventModifiers modifiers = EventModifiers.None)
        { Send(key, true, target, modifiers); Send(key, false, target, modifiers); }

        previous.SetValue(window, null);
        string initial = current.GetValue(window).ToString();
        Press(toggleKey); IsTool(initial, "No previous tool means no switch");
        Select("Brush"); Select("SmudgeBrush"); Select("SmudgeBrush");
        Send(toggleKey, true); IsTool("Brush", "Shortcut restores preceding tool, ignoring repeated selection");
        Send(toggleKey, true); IsTool("Brush", "Held shortcut does not toggle on OS repeat");
        Send(toggleKey, false);
        Press(toggleKey); IsTool("SmudgeBrush", "Next press swaps back to current tool");
        foreach (var modifier in new[] { EventModifiers.Control, EventModifiers.Command, EventModifiers.Alt, EventModifiers.Shift })
        {
            Press(toggleKey, modifiers: modifier);
            IsTool("SmudgeBrush", "Modified shortcut is not intercepted: " + modifier);
        }
        Press(KeyCode.B); Press(KeyCode.P);
        Press(toggleKey); IsTool("Brush", "Direct tool shortcuts update previous tool");
        Press(toggleKey); IsTool("Pencil", "Direct shortcut pair switches both ways");
        foreach (string tool in new[] { "BlurBrush", "HealingBrush", "Fill", "Shape", "Transform", "Zoom", "RectangleSelect", "PolygonSelect", "None" })
        {
            Select("Brush"); Select(tool);
            Press(toggleKey); IsTool("Brush", tool + " restores Brush");
            Press(toggleKey); IsTool(tool, tool + " is restored on next press");
            T.Equal(1, window.rootVisualElement.Query<Button>(className: "whimtex-tool-button--selected").ToList().Count,
                "Exactly one selected toolbar button after switching " + tool);
        }
        Select("Brush"); Select("SmudgeBrush");
        foreach (VisualElement field in new VisualElement[] { new TextField(), new IntegerField(), new FloatField() })
        {
            window.rootVisualElement.Add(field);
            try
            {
                Press(toggleKey, field);
                IsTool("SmudgeBrush", "Field input does not switch tools: " + field.GetType().Name);
            }
            finally { field.RemoveFromHierarchy(); }
        }
        var pointer = type.GetField("healingPointer", flags);
        pointer.SetValue(window, PointerId.mousePointerId);
        try { Press(toggleKey); IsTool("SmudgeBrush", "An active canvas gesture blocks switching"); }
        finally { pointer.SetValue(window, -1); }
        Press(toggleKey); IsTool("Brush", "Blocked gesture does not corrupt tool history");
        Send(toggleKey, true); IsTool("SmudgeBrush", "First held press switches once");
        type.GetMethod("OnLostFocus", flags).Invoke(window, null);
        Press(toggleKey); IsTool("Brush", "Focus loss clears held shortcut state");
        window.CreateGUI();
        Press(toggleKey); IsTool("SmudgeBrush", "View rebuild preserves previous tool history");
    }
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyRun(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
