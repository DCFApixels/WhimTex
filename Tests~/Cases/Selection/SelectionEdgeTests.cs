using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;

public static class SelectionEdgeTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Flags).SetValue(value, next);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Flags).Invoke(value, args);
    static Vector2 View(VisualElement canvas, WhimTexDocument document, Vector2 point)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + point.x / document.width * image.width, image.yMax - point.y / document.height * image.height));
    }
    static void Pointer(VisualElement target, EventType type, Vector2 point)
    {
        var input = new Event { type = type, button = 0, clickCount = 1, modifiers = EventModifiers.Control, mousePosition = target.LocalToWorld(point) };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Enter(VisualElement target)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }); e.target = target; target.SendEvent(e);
    }
    static async Task Tick(VisualElement root, CancellationToken token)
    {
        var done = new TaskCompletionSource<bool>(); var scheduled = root.schedule.Execute(() => done.TrySetResult(true)).StartingIn(70);
        using (token.Register(() => done.TrySetCanceled())) try { await done.Task; } finally { scheduled.Pause(); }
    }
    public static string Start(string id) => UnityAAsync.Start(id, (t, token) => UnityAScope.RunOwnedAsync(scope => Execute(t, scope, token)));
    public static string Poll(string id) => UnityAAsync.Poll(id);
    public static Task<string> Cancel(string id) => UnityAAsync.Cancel(id);
    public static Task<string> Cleanup(string id) => UnityAAsync.Cleanup(id);

    static async Task Execute(TestContext t, UnityAScope scope, CancellationToken token)
    {
        const int side = 16;
        var assembly = typeof(WhimTexDocument).Assembly;
        Type selectionType = assembly.GetType("DCFApixels.WhimTex.CanvasSelection", true);
        Type combineType = assembly.GetType("DCFApixels.WhimTex.SelectionCombine", true);
        object Mode(string name) => Enum.Parse(combineType, name);
        object NewSelection() => Activator.CreateInstance(selectionType, Flags, null, new object[] { side, side }, null);
        byte[] Mask(object selection) => (byte[])selectionType.GetProperty("Coverage", Flags).GetValue(selection);
        bool Binary(byte[] mask) => mask.All(v => v == 0 || v == 255);
        bool Fractional(byte[] mask) => mask.Any(v => v > 0 && v < 255);
        byte[] Rect(Vector2 a, Vector2 b, bool aa, bool wrap = false)
        { object s = NewSelection(); Call(s, "Rectangle", a, b, Mode("Replace"), wrap, aa); return Mask(s); }
        Vector2[] triangle = { new Vector2(3, 3), new Vector2(13, 3), new Vector2(3, 13) };
        byte[] Outline(string kind, bool aa)
        { object s = NewSelection(); Call(s, kind, triangle, Mode("Replace"), false, aa); return Mask(s); }
        Vector2 low = new Vector2(2.5f, 3.5f), high = new Vector2(10.5f, 11.5f);

        // Exact rectangle area is an independent oracle; AA changes only intersected boundary pixels.
        byte[] sharpRect = Rect(low, high, false), softRect = Rect(low, high, true);
        t.True(Binary(sharpRect) && sharpRect.Count(v => v != 0) == 81, "Aliased fractional rectangles preserve the established floor/ceil footprint.");
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        {
            double width = Math.Max(0, Math.Min(x + 1, high.x) - Math.Max(x, low.x));
            double height = Math.Max(0, Math.Min(y + 1, high.y) - Math.Max(y, low.y));
            t.Near(width * height * 255, softRect[y * side + x], .51, "Rectangle AA equals geometric pixel area, without feather outside its bounds.");
        }
        t.True(Fractional(softRect) && softRect[6 * side + 6] == 255 && softRect[0] == 0, "AA keeps full interior, empty exterior and fractional boundary coverage.");
        t.True(Rect(new Vector2(2, 3), new Vector2(10, 11), true).SequenceEqual(Rect(new Vector2(2, 3), new Vector2(10, 11), false)), "Pixel-aligned rectangle AA remains fully sharp.");
        t.True(Rect(high, low, true).SequenceEqual(softRect), "AA rectangles do not depend on drag direction.");
        foreach (string method in new[] { "Rectangle", "Ellipse", "Polygon", "Freehand" })
        {
            MethodInfo m = selectionType.GetMethod(method, Flags); ParameterInfo last = m.GetParameters().Last();
            t.True(last.IsOptional && object.Equals(last.DefaultValue, false), method + " defaults to aliased coverage.");
        }

        // A 45-degree boundary cuts these cells exactly in half. The whole shape area is 50 pixels.
        foreach (string kind in new[] { "Polygon", "Freehand" })
        {
            byte[] binary = Outline(kind, false), aa = Outline(kind, true);
            t.True(Binary(binary) && Fractional(aa), kind + " supports both binary and fractional edges.");
            t.Near(127.5, aa[8 * side + 7], .51, kind + " AA resolves a diagonal half-pixel by area.");
            t.Near(50 * 255, aa.Sum(v => (int)v), 6, kind + " AA retains analytic triangle area.");
            t.True(aa[5 * side + 5] == 255 && aa[14 * side + 14] == 0 && aa[2 * side + 5] == 0,
                kind + " AA has no blurred halo beyond the polygon or weakened distant interior.");
        }
        object ellipseSelection = NewSelection(); Call(ellipseSelection, "Ellipse", new Vector2(3, 3), new Vector2(13, 13), Mode("Replace"), false, true);
        byte[] ellipse = Mask(ellipseSelection);
        t.True(Fractional(ellipse) && ellipse[8 * side + 8] == 255 && ellipse[0] == 0, "Ellipse AA includes fractional contour pixels with a full center and empty exterior.");
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        {
            double nearX = Math.Max(0, Math.Max(8 - (x + 1), x - 8)), nearY = Math.Max(0, Math.Max(8 - (y + 1), y - 8));
            double farX = Math.Max(Math.Abs(x - 8), Math.Abs(x + 1 - 8)), farY = Math.Max(Math.Abs(y - 8), Math.Abs(y + 1 - 8));
            if (nearX * nearX + nearY * nearY > 25) t.Equal((byte)0, ellipse[y * side + x], "Ellipse AA never feathers into a geometrically disjoint pixel.");
            if (farX * farX + farY * farY < 25) t.Equal((byte)255, ellipse[y * side + x], "An ellipse pixel wholly inside the shape has full coverage.");
        }
        t.Near(Math.PI * 25 * 255, ellipse.Sum(v => (int)v), 255, "Ellipse AA approximately preserves its analytic area.");
        Call(ellipseSelection, "Ellipse", new Vector2(3, 3), new Vector2(13, 13), Mode("Replace"), false, false);
        t.True(Binary(Mask(ellipseSelection)), "Aliased ellipse remains strictly binary.");

        // Soft selections combine as coverage, and sampling retains the fractional value.
        foreach (string combine in new[] { "Add", "Subtract", "Intersect" })
        {
            object s = NewSelection(); Call(s, "Set", Enumerable.Repeat((byte)128, side * side).ToArray(), Mode("Replace"));
            Call(s, "Rectangle", low, high, Mode(combine), false, true); byte[] result = Mask(s);
            for (int i = 0; i < result.Length; i++)
            {
                double expected = combine == "Add" ? Math.Max(128, (int)softRect[i]) : 128 * (combine == "Subtract" ? 1 - softRect[i] / 255.0 : softRect[i] / 255.0);
                t.Near(expected, result[i], .51, "AA " + combine + " preserves coverage arithmetic.");
            }
        }
        object sampled = NewSelection(); Call(sampled, "Set", (byte[])softRect.Clone(), Mode("Replace"));
        t.Near(64 / 255.0, (float)Call(sampled, "Sample", new Vector2(2.5f / side, 3.5f / side), false), 1e-6, "Painting Sample retains quarter-pixel coverage.");
        t.Near(0, (float)Call(sampled, "Sample", new Vector2(.5f / side, .5f / side), false), 1e-6, "Painting Sample rejects exterior pixels.");
        byte[] seam = Rect(new Vector2(14.5f, 4.5f), new Vector2(17.5f, 10.5f), true, true);
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        {
            double horizontal = x == 14 || x == 1 ? .5 : x == 15 || x == 0 ? 1 : 0;
            double vertical = y == 4 || y == 10 ? .5 : y > 4 && y < 10 ? 1 : 0;
            t.Near(horizontal * vertical * 255, seam[y * side + x], .51, "Wrapped AA rectangle preserves pixel area on both sides of the seam.");
        }
        t.True(Rect(new Vector2(-4, -4), new Vector2(36, 36), true, true).All(v => v == 255), "Overlapping wrapped AA spans union to full coverage.");
        foreach (float offset in new[] { 0f, 16f, -16f })
        {
            Vector2 thinA = new Vector2(2, 3.45f + offset), thinB = new Vector2(10, 3.55f + offset);
            foreach (string kind in new[] { "Ellipse", "Polygon", "Freehand" })
            {
                object thin = NewSelection();
                if (kind == "Ellipse") Call(thin, kind, thinA, thinB, Mode("Replace"), offset != 0, true);
                else Call(thin, kind, new[] { thinA, new Vector2(thinB.x, thinA.y), thinB, new Vector2(thinA.x, thinB.y) }, Mode("Replace"), offset != 0, true);
                byte[] coverage = Mask(thin);
                t.True(Fractional(coverage) && coverage.Where((v, i) => i / side != 3).All(v => v == 0),
                    "A narrow " + kind + " survives between sample lanes and stays within its intersected row, including translated repeats.");
                t.Near((kind == "Ellipse" ? Math.PI * .2 : .8) * 255, coverage.Sum(v => (int)v), 8,
                    "Narrow " + kind + " coverage approximately retains its known subpixel area.");
            }
        }

        // A joined pair of UV triangles must form one seamless rectangular selection.
        var mesh = scope.OwnObject(new Mesh { hideFlags = HideFlags.HideAndDontSave, name = scope.Tag + "-selection-aa-uv" });
        Vector2[] uv = { low / side, new Vector2(high.x, low.y) / side, high / side, new Vector2(low.x, high.y) / side };
        mesh.vertices = uv.Select(p => new Vector3(p.x, p.y, 0)).ToArray(); mesh.uv = uv; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        Type mapType = assembly.GetType("DCFApixels.WhimTex.UvIslandMap", true);
        object uvMap = mapType.GetMethod("Build", Flags).Invoke(null, new object[] { mesh, 0, -1 });
        byte[] uvAa = (byte[])Call(uvMap, "Rasterize", 0, side, side, true);
        byte[] uvSharp = (byte[])Call(uvMap, "Rasterize", 0, side, side, false);
        t.True(uvAa.SequenceEqual(softRect), "UV AA unions both triangle spans without a soft internal diagonal seam.");
        t.True(Binary(uvSharp) && Fractional(uvAa), "UV islands support both edge modes.");

        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave; document.name = scope.Tag + "-selection-edges-document";
        document.width = document.height = side; document.outputFilter = FilterMode.Point;
        var painted = new DrawingLayerBehaviour(); document.layers.Add(painted);
        document.layers.Add(new ColorFillLayerBehaviour { color = new Color(.08f, .08f, .08f, 1) });
        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>()); Call(window, "SetDocument", document);
        window.ShowUtility(); window.position = new Rect(60, 60, 1400, 850); window.CreateGUI();
        var root = window.rootVisualElement; for (int i = 0; i < 5; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas"); Call(window, "SetCanvasZoomPercent", 3200f); await Tick(root, token); canvas.Focus();
        object Tool(string name) => Enum.Parse(Read(window, "canvasTool").GetType(), name);
        object Edge(bool aa) => Enum.Parse(Read(window, "selectionEdgeMode").GetType(), aa ? "Antialiased" : "Aliased");
        EnumField EdgeField() => root.Query<EnumField>().ToList().First(f => f.name == "selectionEdgeMode" && f.parent.resolvedStyle.display != DisplayStyle.None);
        object CurrentSelection() => Call(window, "GetAreaSelection");
        byte[] CurrentMask() => Mask(CurrentSelection());
        void SetEdges(bool aa) => EdgeField().value = (Enum)Edge(aa);
        void Down(Vector2 p) => Pointer(canvas, EventType.MouseDown, View(canvas, document, p));
        void Move(Vector2 p) => Pointer(canvas, EventType.MouseDrag, View(canvas, document, p));
        void Up(Vector2 p) => Pointer(canvas, EventType.MouseUp, View(canvas, document, p));
        void Click(Vector2 p) { Down(p); Up(p); }
        t.True(Read(window, "selectionEdgeMode").ToString() == "Aliased", "New windows default to aliased selection edges.");
        Call(window, "SetCanvasTool", Tool("RectangleSelect")); await Tick(root, token);
        t.True(EdgeField().value.ToString() == "Aliased" && string.IsNullOrEmpty(EdgeField().label), "The compact edge selector displays its option without an extra label.");
        Call(CurrentSelection(), "Set", (byte[])softRect.Clone(), Mode("Replace")); byte[] saved = (byte[])CurrentMask().Clone(); SetEdges(true);
        t.True(CurrentMask().SequenceEqual(saved), "Changing the edge selector never converts existing selection coverage.");

        Vector2 a = new Vector2(2.25f, 3.25f), b = new Vector2(11.25f, 12.25f);
        Vector2[] uiTriangle = { a, new Vector2(12.25f, 4.25f), new Vector2(3.25f, 13.25f) };
        foreach (string kind in new[] { "Rectangle", "Ellipse", "Polygon", "Freehand" })
        {
            if (kind == "Rectangle" || kind == "Ellipse")
            {
                Call(window, "SetCanvasTool", Tool("RectangleSelect")); Write(window, "marqueeShape", Enum.Parse(Read(window, "marqueeShape").GetType(), kind)); Call(window, "RefreshToolkitInterface", false);
            }
            else
            { Call(window, "SetLassoMode", Enum.Parse(Read(window, "lassoMode").GetType(), kind == "Polygon" ? "Polygonal" : "Freehand")); Call(window, "SetCanvasTool", Tool("PolygonSelect")); }
            await Tick(root, token);
            t.True(Read(window, "selectionEdgeMode").ToString() == EdgeField().value.ToString(), kind + " shares the synchronized window edge mode.");
            foreach (bool aa in new[] { true, false })
            {
                SetEdges(aa); Write(window, "areaSelectionMode", Mode("Replace")); saved = (byte[])CurrentMask().Clone(); Down(a); SetEdges(!aa);
                var gesture = Read(window, "areaSelectionManipulator");
                t.True((bool)Read(gesture, "antialias") == aa, kind + " captures edge mode at the first pointer DOWN.");
                t.True(CurrentMask().SequenceEqual(saved), "A selector change during a gesture does not commit or convert its saved mask.");
                if (kind == "Rectangle" || kind == "Ellipse") { Move(b); Up(b); }
                else if (kind == "Polygon") { Up(a); Click(uiTriangle[1]); Click(uiTriangle[2]); Enter(canvas); }
                else { Move(uiTriangle[1]); Move(uiTriangle[2]); Up(uiTriangle[2]); }
                t.True(aa ? Fractional(CurrentMask()) : Binary(CurrentMask()), kind + " applies its original edge mode despite a later dropdown change.");
                object expected = NewSelection();
                if (kind == "Rectangle" || kind == "Ellipse") Call(expected, kind, a, b, Mode("Replace"), false, aa);
                else Call(expected, kind, uiTriangle, Mode("Replace"), false, aa);
                byte[] expectedMask = Mask(expected);
                t.True(CurrentMask().Zip(expectedMask, (v, e) => Math.Abs(v - e)).All(d => d <= (aa ? 1 : 0)), kind + " gesture commits the complete expected geometry and edge coverage.");
            }
        }
        Write(document, "uvReferenceMesh", mesh); Write(window, "uvEnabled", true); Call(window, "RefreshUvReference");
        Call(window, "SetCanvasTool", Tool("UvIslandSelect")); await Tick(root, token);
        foreach (bool aa in new[] { true, false })
        {
            SetEdges(aa); Write(window, "areaSelectionMode", Mode("Replace")); Click(new Vector2(6, 7));
            t.True(CurrentMask().SequenceEqual(aa ? uvAa : uvSharp), "UV click uses the shared " + Edge(aa) + " edge mode.");
        }

        // Native mask texture and pencil actually consume fractional coverage, with no preview filtering.
        Write(window, "uvEnabled", false);
        Call(window, "SetCanvasTool", Tool("PolygonSelect")); await Tick(root, token);
        Type settingsType = assembly.GetType("DCFApixels.WhimTex.PaintToolSettings", true);
        object settings = Activator.CreateInstance(settingsType, true); Write(settings, "pencilSize", 64);
        Write(settings, "pencilShape", Enum.Parse(Read(settings, "pencilShape").GetType(), "Square"));
        foreach (bool aa in new[] { true, false })
        {
            SetEdges(aa); Call(CurrentSelection(), "Polygon", triangle, Mode("Replace"), false, aa); Call(Read(window, "areaSelectionOverlay"), "Invalidate");
            var maskTexture = (Texture2D)Call(window, "GetAreaSelectionTexture");
            t.Near(CurrentMask()[8 * side + 7] / 255.0, maskTexture.GetPixel(7, 8).r, 1e-6, "Selection texture retains boundary coverage for painting.");
            object erase = Call(settings, "GetPencilParameters", true, Color.white), parameters = Call(settings, "GetPencilParameters", false, Color.white);
            void Stamp(object p) { Call(painted, "BeginStroke", new Vector2(.5f, .5f)); try { Call(painted, "PaintPoint", new Vector2(.5f, .5f), side, side, p); } finally { Call(painted, "EndStroke"); } Call(painted, "SyncSurfaceToTexture"); }
            Stamp(erase); Stamp(Call(parameters, "WithSelectionMask", maskTexture));
            var texture = (Texture2D)painted.GetType().GetProperty("StoredTexture", Flags).GetValue(painted);
            for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                t.Near(CurrentMask()[y * side + x] / 255.0, texture.GetPixel(x, y).a, 1.5 / 255, "Actual pencil alpha follows the selection's binary or fractional pixel coverage.");
            Call(document, "MarkChanged"); Call(window, "RefreshCanvasDuringPainting"); await Tick(root, token);
            var composite = (RenderTexture)Read(window, "canvasTexture");
            t.True(composite != null && composite.width == side && composite.height == side, "Direct-stamp fixture refreshes the native canvas composite at source resolution.");
            var readback = scope.OwnObject(new Texture2D(side, side, TextureFormat.RGBAFloat, false, true));
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = composite; readback.ReadPixels(new Rect(0, 0, side, side), 0, 0, false); readback.Apply(false); }
            finally { RenderTexture.active = previous; }
            t.True(readback.GetPixel(5, 5).r > .9f, "The native composite displays the painted white interior.");
            float edgeValue = readback.GetPixel(7, 8).r;
            t.True(aa ? edgeValue > .05f && edgeValue < .9f : edgeValue < .05f,
                "The native composite displays a gray AA boundary or the aliased background instead of a stale preview.");
            Call(window, "RefreshToolkitInterface", false); window.Repaint(); await Tick(root, token);
            await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/selection-edges-" + (aa ? "antialiased" : "aliased") + ".png"), token);
        }
        UnityAScope.CloseOwned(window); t.True(window == null, "Owned selection test window is destroyed before scope cleanup completes.");
    }
}
