using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests.UnityC;

public static class GradientCreationTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static Vector2 DocumentPoint(VisualElement canvas, Vector2 size, Vector2 point)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        Vector2 p = (Vector2)Call(canvas, "ToCanvas", point);
        return new Vector2((p.x - image.x) / image.width * size.x, (image.yMax - p.y) / image.height * size.y);
    }
    static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point), modifiers = modifiers };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = FixtureContext.RunAsync(job, token => Execute(job, token));
        return AsyncFixture.Poll(id);
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);
    static async Task Tick(VisualElement root, CancellationToken token)
    {
        var done = new TaskCompletionSource<bool>();
        var item = root.schedule.Execute(() => done.TrySetResult(true)).StartingIn(50);
        using (token.Register(() => { item.Pause(); done.TrySetCanceled(); }))
            try { await done.Task; } finally { item.Pause(); }
    }
    static async Task Execute(AsyncFixture job, CancellationToken token)
    {
        var t = job.Context;
        var scope = job.Scope;
        var document = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave; document.width = 512; document.height = 256;
        var window = scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        typeof(WhimTexWindow).GetField("activeDocument", Flags).SetValue(window, document);
        window.ShowUtility(); window.position = new Rect(80, 80, 1400, 800); window.CreateGUI();
        job.OwnCleanup(() => Undo.ClearUndo(document));
        var root = window.rootVisualElement;
        for (int i = 0; i < 4; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        var toolType = Read(window, "canvasTool").GetType();
        Call(window, "SetCanvasTool", Enum.Parse(toolType, "Gradient"));
        await Tick(root, token);
        var button = root.Q<Button>("gradientTool");
        t.True(button != null && button.parent.IndexOf(button) == button.parent.IndexOf(root.Q<Button>("fillTool")) + 1, "Gradient is directly after Fill");
        t.True(root.Q<WhimTexGradientValueField>("gradientToolRamp") != null, "Editable ramp in common header");
        Vector2 start = canvas.contentRect.center - new Vector2(90, 30), end = start + new Vector2(180, -45);
        Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseUp, start);
        t.Equal(0, document.layers.Count, "A click does not create a degenerate layer");
        var type = root.Q<EnumField>("gradientToolType");
        foreach (string kind in new[] { "Linear", "Radial", "Angular", "Diamond", "Square" })
        {
            type.value = (Enum)Enum.Parse(type.value.GetType(), kind);
            int count = document.layers.Count;
            Vector2 size = new Vector2(document.width, document.height);
            Vector2 expectedStart = DocumentPoint(canvas, size, start), expectedEnd = DocumentPoint(canvas, size, end);
            Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseDrag, end); Pointer(canvas, EventType.MouseUp, end);
            t.Equal(count + 1, document.layers.Count, kind + " creates one layer");
            t.Equal("Gradient", Read(window, "canvasTool").ToString(), "Creating/selecting a gradient keeps the creation tool");
            var behaviour = document.layers[0].Behaviour as GradientLayerBehaviour;
            t.True(behaviour != null && behaviour.gradient.Mode == WhimTexGradientMode.Perceptual, "Current gradient model/default interpolation");
            t.True(!ReferenceEquals(behaviour.gradient, root.Q<WhimTexGradientValueField>("gradientToolRamp").value), "Each new layer owns a ramp copy");
            t.Equal(TransformTilingMode.Unbounded, document.layers[0].transform.tiling, "Gradient extends beyond its handles");
            var transform = document.layers[0].transform;
            Vector2 actualStart = Vector2.Scale(transform.Map(kind == "Linear" ? new Vector2(0, .5f) : Vector2.one * .5f, size), size);
            t.Near(expectedStart.x, actualStart.x, .001, kind + " starts at the drag position, X");
            t.Near(expectedStart.y, actualStart.y, .001, kind + " starts at the drag position, Y");
            if (kind != "Angular")
            {
                Vector2 actualEnd = Vector2.Scale(transform.Map(new Vector2(1, .5f), size), size);
                t.Near(expectedEnd.x, actualEnd.x, .001, kind + " ends at the drag position, X");
                t.Near(expectedEnd.y, actualEnd.y, .001, kind + " ends at the drag position, Y");
            }
            await Tick(root, token);
        }
        int before = document.layers.Count;
        Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseDrag, end);
        using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        { e.target = canvas; canvas.SendEvent(e); }
        Pointer(canvas, EventType.MouseUp, end);
        t.Equal(before, document.layers.Count, "Escape cancels without inserting a layer");
        type.value = (Enum)Enum.Parse(type.value.GetType(), "Linear");
        Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseUp, end, EventModifiers.Shift);
        t.Near(0, document.layers[0].transform.rotation, .01, "Shift constrains the angle to 45-degree increments");
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
        t.Equal(before, document.layers.Count, "One Undo removes the last gradient");
        Undo.PerformRedo(); t.Equal(before + 1, document.layers.Count, "Redo restores the editable gradient");
        var rendered = scope.Own(document.ComposeCanvas());
        t.True(rendered != null, "Gradient composition renders");
        Vector2 dimensions = new Vector2(document.width, document.height);
        var last = document.layers[0];
        Vector2 a = Vector2.Scale(last.transform.Map(new Vector2(0, .5f), dimensions), dimensions);
        Vector2 b = Vector2.Scale(last.transform.Map(new Vector2(1, .5f), dimensions), dimensions);
        Color Sample(Vector2 p) => rendered.GetPixel(Mathf.Clamp(Mathf.RoundToInt(p.x), 0, rendered.width - 1), Mathf.Clamp(Mathf.RoundToInt(p.y), 0, rendered.height - 1));
        t.Near(1, Sample(a).r, .03, "Rendered ramp has its first color at the drag start");
        t.Near(0, Sample(b).r, .03, "Rendered ramp has its last color at the drag end");
    }
}
