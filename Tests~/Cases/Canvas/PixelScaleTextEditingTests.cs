using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityC;

public static class PixelScaleTextEditingTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    private static object Call(object value, string name, params object[] args) =>
        Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    private static void Near(TestContext t, Vector2 expected, Vector2 actual, string label)
    { t.Near(expected.x, actual.x, .03, label + " X"); t.Near(expected.y, actual.y, .03, label + " Y"); }
    private static Vector2 View(VisualElement canvas, WhimTexDocument document, Vector2 pixels)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + pixels.x / document.width * image.width,
            image.yMax - pixels.y / document.height * image.height));
    }
    private static Vector2 LocalView(VisualElement canvas, WhimTexDocument document, Layer layer, Vector2 local)
    {
        var dimensions = new Vector2(document.width, document.height);
        var transform = (TextureTransform)Call(document, "GetCanvasTransform", layer);
        Vector2 uv = transform.Map(Vector2.one * .5f + local / dimensions, dimensions);
        return View(canvas, document, uv * dimensions);
    }
    private static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None)
    {
        var input = new Event { type = type, button = 0, mousePosition = target.LocalToWorld(point), modifiers = modifiers };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    private static async Task Tick(VisualElement root, CancellationToken token)
    {
        var ready = new TaskCompletionSource<bool>();
        var item = root.schedule.Execute(() => ready.TrySetResult(true)).StartingIn(50);
        using (token.Register(() => { item.Pause(); ready.TrySetCanceled(); }))
            try { await ready.Task; } finally { item.Pause(); }
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

    private static async Task Execute(AsyncFixture job, CancellationToken token)
    {
        var t = job.Context; var scope = job.Scope;
        var document = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.width = document.height = 512; document.hideFlags = HideFlags.HideAndDontSave;
        var text = new TextLayerBehaviour { text = "Pixel scale", fontSize = 18, layoutMode = TextLayoutMode.Frame,
            layoutOffset = new Vector2(15, -10), frameSize = new Vector2(120, 60) };
        var layer = new Layer(text); document.layers.Add(layer);
        var window = scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        Call(window, "SetDocument", document);
        job.OwnCleanup(() => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); });
        window.ShowUtility(); window.position = new Rect(70, 70, 1200, 780); window.CreateGUI();
        var root = window.rootVisualElement;
        for (int i = 0; i < 4; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        Call(window, "SetCanvasTool", Enum.Parse(Read(window, "canvasTool").GetType(), "Text"));
        Call(window, "SelectOnlyLayer", layer.Id);
        Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
        t.True((bool)text.GetType().GetProperty("FontAvailable", Flags).GetValue(text), "Default font is available for owned text editing");
        var offset = new Vector2(15, -10); var size = new Vector2(120, 60);

        foreach (float scale in new[] { 2f, .5f })
        {
            document.pixelScale = scale;
            text.layoutOffset = offset; text.frameSize = size;
            Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
            Rect bounds = (Rect)Call(text, "GetLayoutBounds", 512, 512);
            Near(t, size * scale, bounds.size, "Frame overlay dimensions are physical pixels at scale " + scale);
            Near(t, offset * scale, bounds.center, "Frame overlay center follows logical layout offset at scale " + scale);

            Vector2 rightLocal = new Vector2(bounds.xMax, bounds.center.y);
            Vector2 a = LocalView(canvas, document, layer, rightLocal);
            Vector2 b = LocalView(canvas, document, layer, rightLocal + new Vector2(40, 0));
            Pointer(canvas, EventType.MouseDown, a);
            t.True(canvas.HasPointerCapture(PointerId.mousePointerId), "Scaled physical frame handle can be grabbed");
            Pointer(canvas, EventType.MouseDrag, b, EventModifiers.Control);
            Near(t, size + new Vector2(40 / scale, 0), text.frameSize, "Physical frame resize writes logical dimensions");
            Near(t, offset + new Vector2(20 / scale, 0), text.layoutOffset, "Frame resize keeps the opposite physical edge fixed");
            Pointer(canvas, EventType.MouseUp, b, EventModifiers.Control);
            t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Scaled resize releases capture");
            t.Near(18, text.fontSize, 0, "Frame resize leaves authored font size unchanged");

            text.layoutOffset = offset; text.frameSize = size;
            Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
            a = LocalView(canvas, document, layer, offset * scale);
            b = LocalView(canvas, document, layer, offset * scale + new Vector2(40, 20));
            Pointer(canvas, EventType.MouseDown, a); Pointer(canvas, EventType.MouseDrag, b, EventModifiers.Control);
            Near(t, offset + new Vector2(40, 20) / scale, text.layoutOffset, "Frame move divides physical drag by pixel scale exactly once");
            Pointer(canvas, EventType.MouseUp, b, EventModifiers.Control);
            t.True(layer.transform.Equals(TextureTransform.Default), "Layout edits preserve layer Transform");

            Call(window, "FocusTextInput", false); await Tick(root, token);
            var input = root.Q<TextField>("canvasTextInput");
            t.Near(18 * scale, input.style.fontSize.value.value, .001, "Keyboard editor font matches the physical rendered size");
            t.Near(text.frameSize.x * scale, input.style.width.value.value, .03, "Keyboard editor width matches the physical frame");
            Call(window, "EndTextEditing", false);
        }

        document.pixelScale = 2; text.layoutOffset = offset; text.frameSize = size;
        Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
        Vector2 center = LocalView(canvas, document, layer, offset * 2);
        Pointer(canvas, EventType.MouseDown, center); Pointer(canvas, EventType.MouseDrag, center + new Vector2(30, 0), EventModifiers.Control);
        document.pixelScale = 3;
        Pointer(canvas, EventType.MouseUp, center + new Vector2(30, 0), EventModifiers.Control);
        Near(t, offset, text.layoutOffset, "Changing document pixel scale cancels the owned text drag");
        t.True(!canvas.HasPointerCapture(PointerId.mousePointerId), "Pixel-scale invalidation releases text pointer capture");

        document.pixelScale = 2;
        text.layoutMode = TextLayoutMode.Point; text.layoutOffset = offset;
        Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
        Vector2 pointAnchor = (Vector2)Call(text, "GetPointAnchor", 512, 512);
        Vector2 pointStart = LocalView(canvas, document, layer, pointAnchor);
        Vector2 pointEnd = LocalView(canvas, document, layer, pointAnchor + new Vector2(40, -20));
        Pointer(canvas, EventType.MouseDown, pointStart);
        t.True(canvas.HasPointerCapture(PointerId.mousePointerId), "Scaled Point text anchor can be grabbed");
        Pointer(canvas, EventType.MouseDrag, pointEnd, EventModifiers.Control);
        Near(t, offset + new Vector2(20, -10), text.layoutOffset, "Point text move divides physical drag by pixel scale");
        Pointer(canvas, EventType.MouseUp, pointEnd, EventModifiers.Control);
        t.True(layer.transform.Equals(TextureTransform.Default), "Point layout moves preserve the Transform");

        Call(window, "RefreshToolkitInterface", false); await Tick(root, token);
        Vector2 first = new Vector2(24, 24), last = first + new Vector2(120, 60);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, first), EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, last), EventModifiers.Control);
        Pointer(canvas, EventType.MouseUp, View(canvas, document, last), EventModifiers.Control);
        var created = (TextLayerBehaviour)((Layer)Call(window, "GetSelectedLayer")).Behaviour;
        t.Equal(2, document.layers.Count, "A physical frame drag creates one separate Text layer");
        Near(t, new Vector2(60, 30), created.frameSize, "New frame dimensions are stored in logical pixels");
        Near(t, ((first + last) * .5f - Vector2.one * 256) / 2, created.layoutOffset,
            "New frame placement converts physical canvas position to logical layout units");
        Call(window, "EndTextEditing", false);
        window.DiscardChanges();
    }
}
