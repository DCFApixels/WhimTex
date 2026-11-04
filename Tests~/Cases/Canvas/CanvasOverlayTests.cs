using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class CanvasOverlayTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Assembly = typeof(WhimTexDocument).Assembly;
    static object Create(Type type, params object[] args) => Activator.CreateInstance(type, F, null, args, null);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, F).Invoke(owner, args);
    static void Near(TestContext t, Vector2 expected, Vector2 actual, float tolerance, string message)
    {
        t.Near(expected.x, actual.x, tolerance, message + " X");
        t.Near(expected.y, actual.y, tolerance, message + " Y");
    }

    public static string Run() => TestContext.Run("Canvas coordinates and detached overlay lifecycle", t =>
    {
        var viewport = Create(Assembly.GetType("DCFApixels.WhimTex.CanvasViewport", true));
        Type coordinates = Assembly.GetType("DCFApixels.WhimTex.CanvasCoordinates", true);
        var view = new Rect(17, 23, 900, 650);
        foreach (Vector2 size in new[] { new Vector2(8192, 8192), new Vector2(8192, 1024), new Vector2(31, 73) })
        foreach (Rect image in new[] { new Rect(80, 60, 450, 320), new Rect(-1300, -700, 2400, 1700) })
        foreach (float angle in new[] { 0f, 37f, 90f, -180f })
        {
            Call(viewport, "SetRotation", angle, false);
            object map = Create(coordinates, viewport, view, image, size);
            foreach (Vector2 uv in new[] { Vector2.zero, Vector2.one, new Vector2(.2f, .75f), new Vector2(-.15f, 1.2f) })
            {
                // Independent corner/axis oracle, including outside-canvas handles.
                double x = image.x + uv.x * image.width - view.center.x;
                double y = image.yMax - uv.y * image.height - view.center.y;
                double radians = angle * Math.PI / 180;
                var expected = new Vector2((float)(view.center.x + x * Math.Cos(radians) - y * Math.Sin(radians)),
                    (float)(view.center.y + x * Math.Sin(radians) + y * Math.Cos(radians)));
                var screen = (Vector2)Call(map, "UvToView", uv);
                var pixel = Vector2.Scale(uv, size);
                Near(t, expected, screen, .001f, "UV axis, pan, zoom and rotation");
                Near(t, expected, (Vector2)Call(map, "DocumentToView", pixel), .001f, "Document and UV anchors agree");
                Near(t, uv, (Vector2)Call(map, "ViewToUv", screen), .00001f, "UV inverse");
                Near(t, pixel, (Vector2)Call(map, "ViewToDocument", screen), .01f, "Pixel inverse including 8K");
            }
        }
        Call(viewport, "SetRotation", 0f, false);
        var fixedImage = new Rect(0, 0, 512, 512);
        object small = Create(coordinates, viewport, view, fixedImage, new Vector2(4096, 4096));
        object large = Create(coordinates, viewport, view, fixedImage, new Vector2(8192, 8192));
        Near(t, new Vector2(256, 256), (Vector2)Call(small, "DocumentToView", new Vector2(2048, 2048)), .001f, "Small document center");
        Near(t, new Vector2(128, 384), (Vector2)Call(large, "DocumentToView", new Vector2(2048, 2048)), .001f, "Same view rectangle, changed document size");

        // Only our own members are reflected; no panel, window, native input or textures.
        var canvas = (VisualElement)Create(typeof(WhimTexWindow).GetNestedType("CanvasElement", F), viewport);
        object host = canvas.GetType().GetProperty("Overlays", F).GetValue(canvas);
        var order = new List<int>();
        var retained = new VisualElement();
        Call(host, "Add", retained, (Action)(() => { order.Add(1); Call(host, "Refresh"); }));
        var painted = (VisualElement)Call(host, "Create", (Action<MeshGenerationContext>)(_ => { }), (Action)(() => order.Add(2)));
        try
        {
            t.True(retained.parent == canvas && painted.parent == canvas, "Both overlay kinds attach to the same canvas");
            t.True(canvas.IndexOf(retained) < canvas.IndexOf(painted), "Registration preserves draw order");
            t.Equal(PickingMode.Ignore, painted.pickingMode, "Painted overlays do not intercept pointer input");
            t.Equal(PickingMode.Position, retained.pickingMode, "Interactive retained overlays keep picking");
            void RefreshCount(int expected, string message)
            {
                t.Equal(expected, order.Count, message);
                for (int i = 0; i < order.Count; i++) t.Equal(i % 2 + 1, order[i], "Ordered callbacks without recursive refresh");
                order.Clear();
            }
            Call(host, "Refresh"); RefreshCount(2, "Explicit refresh is reentrancy safe");
            void SetSize(int n) => Call(canvas, "SetDocument", null, n, n, null, false, null, null);
            SetSize(4096); order.Clear();
            SetSize(8192); RefreshCount(2, "Document resize notifies overlays even with unchanged empty view");
            SetSize(8192); RefreshCount(0, "Unchanged document/view does not notify again");
            // Exercise the exact subscription methods called by detach/attach hooks.
            Call(host, "Listen", false); Call(host, "Listen", false);
            SetSize(4096); RefreshCount(0, "Detached host has no view subscription");
            Call(host, "Listen", true); Call(host, "Listen", true);
            SetSize(8192); RefreshCount(2, "Reattachment subscribes exactly once");
            Call(host, "Refresh"); RefreshCount(2, "Retained overlays still update after reattachment");
        }
        finally
        {
            Call(host, "Listen", false);
            canvas.Clear();
        }
    });
}
