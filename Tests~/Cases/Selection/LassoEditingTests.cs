using System;
using System.Collections;
using System.Collections.Generic;
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

public static class LassoEditingTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static T Cue<T>(object value, string name) => (T)value.GetType().GetProperty(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static bool Ready(object gesture) => (bool)gesture.GetType().GetProperty("ClosingPolygon", Flags).GetValue(gesture);
    static Vector2 View(VisualElement canvas, WhimTexDocument document, Vector2 point)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + point.x / document.width * image.width, image.yMax - point.y / document.height * image.height));
    }
    static void Pointer(VisualElement canvas, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None, int button = 0, int clickCount = 1)
    {
        var input = new Event { type = type, button = button, clickCount = clickCount, modifiers = modifiers, mousePosition = canvas.LocalToWorld(point) };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = canvas; canvas.SendEvent(e); }
        else if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(input); e.target = canvas; canvas.SendEvent(e); }
        else { using var e = PointerMoveEvent.GetPooled(input); e.target = canvas; canvas.SendEvent(e); }
    }
    static void Key(VisualElement canvas, KeyCode key, EventModifiers modifiers = EventModifiers.None)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = modifiers }); e.target = canvas; canvas.SendEvent(e);
    }
    static void KeyUp(VisualElement canvas, KeyCode key, EventModifiers modifiers = EventModifiers.None)
    {
        using var e = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = key, modifiers = modifiers }); e.target = canvas; canvas.SendEvent(e);
    }
    static async Task Tick(VisualElement root, CancellationToken token)
    {
        var done = new TaskCompletionSource<bool>(); var item = root.schedule.Execute(() => done.TrySetResult(true)).StartingIn(70);
        using (token.Register(() => done.TrySetCanceled())) try { await done.Task; } finally { item.Pause(); }
    }
    public static string Start(string id) => UnityAAsync.Start(id, (t, token) => UnityAScope.RunOwnedAsync(scope => Execute(t, scope, token)));
    public static string Poll(string id) => UnityAAsync.Poll(id);
    public static Task<string> Cancel(string id) => UnityAAsync.Cancel(id);
    public static Task<string> Cleanup(string id) => UnityAAsync.Cleanup(id);

    static async Task Execute(TestContext t, UnityAScope scope, CancellationToken token)
    {
        void Need(bool good, string message) => t.True(good, message);
        string ownedName = scope.Tag + "-lasso-document";
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>()); document.hideFlags = HideFlags.HideAndDontSave;
        document.name = ownedName; document.width = 320; document.height = 240;
        Layer layer = new ColorFillLayerBehaviour { color = new Color(.16f, .16f, .16f, 1f) }; document.layers.Add(layer);
        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>()); Call(window, "SetDocument", document);
        window.ShowUtility(); window.position = new Rect(60, 60, 1250, 850); window.CreateGUI();
        var root = window.rootVisualElement; for (int i = 0; i < 5; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas"); canvas.Focus();
        Call(window, "SetCanvasZoomPercent", 200f); Call(window, "SetCanvasRotation", 0f); await Tick(root, token);
        object tool(string name) => Enum.Parse(window.GetType().GetNestedType("CanvasTool", Flags), name);
        Call(window, "SetCanvasTool", tool("RectangleSelect")); await Tick(root, token);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, new Vector2(10, 10)));
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, new Vector2(40, 40)));
        Pointer(canvas, EventType.MouseUp, View(canvas, document, new Vector2(40, 40)));
        byte[] Mask()
        {
            object selected = Read(window, "areaSelection");
            return (byte[])selected.GetType().GetProperty("Coverage", Flags).GetValue(selected);
        }
        byte[] ExpectedPolygon(Vector2[] points)
        {
            Assembly assembly = typeof(WhimTexDocument).Assembly;
            Type selectedType = assembly.GetType("DCFApixels.WhimTex.CanvasSelection", true);
            object expected = Activator.CreateInstance(selectedType, Flags, null, new object[] { document.width, document.height }, null);
            object replace = Enum.Parse(assembly.GetType("DCFApixels.WhimTex.SelectionCombine", true), "Replace");
            Call(expected, "Polygon", points, replace, false, false);
            return (byte[])selectedType.GetProperty("Coverage", Flags).GetValue(expected);
        }
        byte[] committed = (byte[])Mask().Clone(); Need(committed.Any(v => v != 0), "The fixture begins with an owned committed mask.");
        void MaskSame(string reason) => Need(Mask().SequenceEqual(committed), reason);
        Key(canvas, KeyCode.L); await Tick(root, token);
        var gesture = Read(window, "areaSelectionManipulator"); var vertices = (IList)Read(gesture, "Vertices");
        Vector2 P(int index) => (Vector2)vertices[index];
        Vector2 V(int index) => View(canvas, document, P(index));
        void Click(Vector2 view, EventModifiers modifiers = EventModifiers.None, int button = 0)
        {
            Pointer(canvas, EventType.MouseDown, view, modifiers, button); Pointer(canvas, EventType.MouseUp, view, modifiers, button);
        }
        void Add(Vector2 point, EventModifiers modifiers = EventModifiers.None) => Click(View(canvas, document, point), modifiers);
        void Cancel() { Key(canvas, KeyCode.Escape); Need(vertices.Count == 0 && !Ready(gesture), "Escape clears the draft and readiness."); MaskSame("Cancelling draft preserves committed mask."); }
        void Build(params Vector2[] points)
        {
            if (vertices.Count > 0) Cancel();
            foreach (Vector2 point in points) Add(point, EventModifiers.Control);
            Need(vertices.Count == points.Length, "Ctrl at press can construct an exact fixture without vertex/edge editing.");
        }
        Vector2[] square = { new Vector2(60, 50), new Vector2(230, 50), new Vector2(230, 185), new Vector2(60, 185) };
        Vector2[] triangle = { new Vector2(60, 50), new Vector2(230, 50), new Vector2(230, 185) };

        // Creating a point owns that new point immediately, including a Ctrl-created point.
        Build(triangle); Vector2 create = new Vector2(85, 190), createMoved = new Vector2(105, 205);
        Vector2 savedFirst = P(0), savedSecond = P(1), savedThird = P(2);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, create));
        Need(vertices.Count == 4 && (P(3) - create).magnitude < .001f, "A background press creates a new point immediately.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, createMoved));
        Need(vertices.Count == 4 && (P(3) - createMoved).magnitude < .001f && P(0) == savedFirst && P(1) == savedSecond && P(2) == savedThird,
            "Dragging before release moves the newly created point, leaving old points unchanged.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, createMoved));
        Need((P(3) - createMoved).magnitude < .001f, "Mouse release keeps the created and dragged point.");
        MaskSame("Creating and dragging a new point leaves the saved selection untouched.");
        Build(square); Vector2 copiedNode = P(1); createMoved = copiedNode + new Vector2(12, 12);
        Pointer(canvas, EventType.MouseDown, V(1), EventModifiers.Control);
        Need(vertices.Count == 5 && P(4) == copiedNode, "Ctrl at press creates a new tail point on top of an old node.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, createMoved));
        Need(vertices.Count == 5 && P(1) == copiedNode && (P(4) - createMoved).magnitude < .001f,
            "Releasing Ctrl while held and dragging keeps editing the newly created point.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, createMoved));
        Need(P(1) == copiedNode && vertices.Count == 5, "Ctrl-created point remains distinct after release.");
        Build(square); Vector2[] savedSquare = Enumerable.Range(0, 4).Select(P).ToArray();
        create = new Vector2(85, 210); Pointer(canvas, EventType.MouseDown, View(canvas, document, create));
        Need(vertices.Count == 5, "Capture-loss fixture creates a point on press.");
        await Tick(root, token);
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, create + new Vector2(12, 0)));
        await Tick(root, token);
        Need(canvas.HasPointerCapture(PointerId.mousePointerId), "Created-point fixture establishes native pointer capture before releasing it.");
        if (canvas.HasPointerCapture(PointerId.mousePointerId)) canvas.ReleasePointer(PointerId.mousePointerId);
        Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Created-point fixture removes pending capture ownership.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, create + new Vector2(12, 0)));
        await Tick(root, token);
        Need(vertices.Count == 4 && Enumerable.Range(0, 4).All(i => P(i).Equals(savedSquare[i])),
            "Capture loss removes only the uncommitted created point: count=" + vertices.Count + ", points=" + string.Join(";", vertices.Cast<Vector2>().Select(p => p.ToString("R"))));
        Pointer(canvas, EventType.MouseUp, View(canvas, document, create + new Vector2(12, 0))); MaskSame("Capture loss of a new point preserves the saved mask.");

        // Stable-edge insertion has a visual candidate, without touching the existing mask.
        Build(square); Vector2 middle = (P(0) + P(1)) * .5f;
        Pointer(canvas, EventType.MouseMove, View(canvas, document, middle)); await Tick(root, token);
        Need(vertices.Count == 4, "Hovering a stable edge does not insert until click.");
        Need(Cue<int>(gesture, "InsertSegment") == 0 && (Cue<Vector2>(gesture, "InsertPoint") - middle).magnitude < .001f,
            "Stable-edge hover exposes an insertion candidate exactly on the edge.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-insert-preview.png"), token);
        Click(View(canvas, document, middle)); Need(vertices.Count == 5 && (P(1) - middle).magnitude < .001f, "Stable-edge click inserts a vertex between its neighbors.");
        MaskSame("Inserting a point preserves the committed mask.");
        Vector2 before = P(0), after = P(2);
        Pointer(canvas, EventType.MouseMove, V(1), EventModifiers.Alt); await Tick(root, token);
        Need(Cue<int>(gesture, "HoverVertex") == 1 && Cue<bool>(gesture, "DeletingVertex"), "Alt-hover exposes the exact deletion candidate.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-delete-preview.png"), token);
        Click(V(1), EventModifiers.Alt);
        Need(vertices.Count == 4 && P(0) == before && P(1) == after, "Alt-click deletes exactly the chosen point and rejoins its neighbors.");
        MaskSame("Deleting a point preserves the committed mask.");
        Build(square); middle = (P(0) + P(1)) * .5f;
        Pointer(canvas, EventType.MouseDown, View(canvas, document, middle));
        Need(vertices.Count == 5 && (P(1) - middle).magnitude < .001f, "Stable-edge press creates the inserted node immediately.");
        createMoved = middle + new Vector2(1, 1); Pointer(canvas, EventType.MouseDrag, View(canvas, document, createMoved));
        Need((P(1) - createMoved).magnitude < .001f, "An inserted point follows the first tiny drag without the old-node threshold.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, createMoved));
        Need(vertices.Count == 5 && (P(1) - createMoved).magnitude < .001f, "Inserted-and-dragged point stays between its neighbors.");
        Build(square); savedSquare = Enumerable.Range(0, 4).Select(P).ToArray();
        middle = (P(0) + P(1)) * .5f; Pointer(canvas, EventType.MouseDown, View(canvas, document, middle));
        await Tick(root, token);
        Need(canvas.HasPointerCapture(PointerId.mousePointerId), "Inserted-point fixture establishes native pointer capture before releasing it.");
        if (canvas.HasPointerCapture(PointerId.mousePointerId)) canvas.ReleasePointer(PointerId.mousePointerId);
        Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Inserted-point fixture removes pending capture ownership.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, middle));
        await Tick(root, token);
        Need(vertices.Count == 4 && Enumerable.Range(0, 4).All(i => P(i).Equals(savedSquare[i])), "Capture loss removes only the new inserted node.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, middle)); MaskSame("Aborted insertion preserves the saved mask.");
        Build(square); var insertGuides = (IList)Read(window, "canvasGuides");
        Type insertGuideType = window.GetType().GetNestedType("CanvasGuide", Flags); object insertGuide = Activator.CreateInstance(insertGuideType);
        middle = (P(0) + P(1)) * .5f;
        float insertTolerance = (float)window.GetType().GetProperty("GuideSnapTolerance", Flags).GetValue(window);
        insertGuideType.GetField("normal", Flags).SetValue(insertGuide, Vector2.up);
        insertGuideType.GetField("position", Flags).SetValue(insertGuide, document.height - middle.y + insertTolerance * .5f); insertGuides.Add(insertGuide);
        Click(View(canvas, document, middle));
        Need(vertices.Count == 5 && (P(1) - middle).magnitude < .001f, "A no-movement insertion stays projected on the clicked edge even near a guide.");
        insertGuides.Clear(); Build(square);

        // A node is not moved by a small click jitter; real drag edits it.
        Vector2 original = P(1), press = V(1);
        Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, press + new Vector2(3, 0));
        Need(P(1) == original && vertices.Count == 4, "A drag shorter than four view points does not move or insert a node.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(3, 0)); Need(P(1) == original && vertices.Count == 4, "Releasing a sub-threshold node gesture keeps its position.");
        press = V(1); Pointer(canvas, EventType.MouseDown, press);
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(20, -14));
        Need(P(1) != original && vertices.Count == 4, "An established node drag moves one node without adding points.");
        Vector2 moved = P(1); Pointer(canvas, EventType.MouseDrag, press + new Vector2(34, -20), EventModifiers.Control);
        Need(P(1) != moved && vertices.Count == 4, "Ctrl during an established node drag continues editing instead of appending.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(34, -20), EventModifiers.Control);
        MaskSame("Dragging a point preserves the committed mask.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-edited-points.png"), token);
        Build(square);
        var guides = (IList)Read(window, "canvasGuides");
        Type guideType = window.GetType().GetNestedType("CanvasGuide", Flags); object guide = Activator.CreateInstance(guideType);
        guideType.GetField("normal", Flags).SetValue(guide, Vector2.right); guideType.GetField("position", Flags).SetValue(guide, 245f); guides.Add(guide);
        float tolerance = (float)window.GetType().GetProperty("GuideSnapTolerance", Flags).GetValue(window);
        Vector2 nearGuide = new Vector2(245f - tolerance * .5f, 65f); press = V(1);
        Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, View(canvas, document, nearGuide));
        Need(Mathf.Abs(P(1).x - 245f) < .001f, "Established vertex drag snaps to an owned guide.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, nearGuide), EventModifiers.Control);
        Need((P(1) - nearGuide).magnitude < .001f && vertices.Count == 4, "Ctrl during established drag disables only snapping and keeps editing the same node.");
        KeyUp(canvas, KeyCode.LeftControl);
        Need(Mathf.Abs(P(1).x - 245f) < .001f, "Releasing Ctrl restores guide snapping during the same drag.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, nearGuide)); guides.Clear();
        MaskSame("Guide-assisted vertex editing preserves committed mask.");
        before = P(0); Click(V(0), EventModifiers.Control);
        Need(vertices.Count == 5 && P(0) == before && P(4) == before, "Ctrl at press bypasses node editing and first-point closure.");
        MaskSame("Ctrl-appending at a node does not commit a mask.");

        // Existing points constrain relative to their original position, independent of the grab offset.
        Vector2[] oldPointMoves = { new Vector2(45, 10), new Vector2(-45, 10), new Vector2(10, 45), new Vector2(10, -45),
            new Vector2(45, 30), new Vector2(-45, 30), new Vector2(45, -30), new Vector2(-45, -30) };
        Vector2[] oldPointProjected = { new Vector2(45, 0), new Vector2(-45, 0), new Vector2(0, 45), new Vector2(0, -45),
            new Vector2(37.5f, 37.5f), new Vector2(-37.5f, 37.5f), new Vector2(37.5f, -37.5f), new Vector2(-37.5f, -37.5f) };
        foreach (float angle in new[] { 0f, 27f })
        {
            Call(window, "SetCanvasZoomPercent", 150f); Call(window, "SetCanvasRotation", angle); await Tick(root, token);
            for (int i = 0; i < oldPointMoves.Length; i++)
            {
                Build(square); savedSquare = Enumerable.Range(0, 4).Select(P).ToArray(); original = P(1);
                press = V(1) + new Vector2(2, 1);
                Vector2 target = press + View(canvas, document, original + oldPointMoves[i]) - View(canvas, document, original);
                Pointer(canvas, EventType.MouseDown, press);
                Pointer(canvas, EventType.MouseDrag, target, EventModifiers.Shift);
                Need(vertices.Count == 4 && (P(1) - original - oldPointProjected[i]).magnitude < .001f,
                    "Shift existing-node drag follows the canvas45-degree direction about the original node, despite off-center grab, rotation=" + angle + ", direction=" + i);
                Need(Enumerable.Range(0, 4).Where(n => n != 1).All(n => P(n).Equals(savedSquare[n])), "Constrained existing-node drag leaves all other nodes untouched.");
                KeyUp(canvas, KeyCode.LeftShift);
                Need((P(1) - original - oldPointMoves[i]).magnitude < .001f, "Releasing Shift restores the free existing-node position without mouse movement.");
                Key(canvas, KeyCode.LeftShift, EventModifiers.Shift);
                Need((P(1) - original - oldPointProjected[i]).magnitude < .001f, "Pressing Shift restores the original-node direction constraint without mouse movement.");
                if (angle == 27f && i == 4)
                    await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-existing-point-shift.png"), token);
                Pointer(canvas, EventType.MouseUp, target, EventModifiers.Shift);
                Need(vertices.Count == 4 && (P(1) - original - oldPointProjected[i]).magnitude < .001f, "Release retains the constrained existing-node position and its index.");
                MaskSame("Constrained old-node editing cannot modify the committed mask.");
            }

            // Ctrl changes snapping during the drag, preserving the old node and Shift direction constraint.
            Build(square); savedSquare = Enumerable.Range(0, 4).Select(P).ToArray(); original = P(1);
            float oldNodeTolerance = (float)window.GetType().GetProperty("GuideSnapTolerance", Flags).GetValue(window);
            Vector2 rawOldTarget = original + new Vector2(30, 20); float guideOffset = oldNodeTolerance * .5f;
            object oldNodeGuide = Activator.CreateInstance(guideType);
            guideType.GetField("normal", Flags).SetValue(oldNodeGuide, Vector2.right);
            guideType.GetField("position", Flags).SetValue(oldNodeGuide, rawOldTarget.x + guideOffset); guides.Add(oldNodeGuide);
            press = V(1) + new Vector2(2, 1);
            Vector2 oldTarget = press + View(canvas, document, rawOldTarget) - View(canvas, document, original);
            Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, oldTarget);
            Key(canvas, KeyCode.LeftShift, EventModifiers.Shift);
            Vector2 guidedDiagonal = original + Vector2.one * (25f + guideOffset * .5f);
            Need(vertices.Count == 4 && Cue<int>(gesture, "HoverVertex") == 1 && (P(1) - guidedDiagonal).magnitude < .001f,
                "Shift after an old-node drag starts constrains the guide-snapped point about its original position.");
            Key(canvas, KeyCode.LeftControl, EventModifiers.Control | EventModifiers.Shift);
            Need(vertices.Count == 4 && Cue<int>(gesture, "HoverVertex") == 1 && (P(1) - original - Vector2.one * 25f).magnitude < .001f,
                "Ctrl+Shift during an old-node drag disables guide snapping while retaining its index and diagonal constraint.");
            KeyUp(canvas, KeyCode.LeftControl, EventModifiers.Shift);
            Need((P(1) - guidedDiagonal).magnitude < .001f, "Releasing Ctrl restores guide snapping while the old-node Shift constraint remains.");
            Pointer(canvas, EventType.MouseUp, oldTarget, EventModifiers.Control | EventModifiers.Shift); guides.Clear();
            Need((P(1) - original - Vector2.one * 25f).magnitude < .001f && Enumerable.Range(0, 4).Where(n => n != 1).All(n => P(n).Equals(savedSquare[n])),
                "Ctrl+Shift release keeps only the constrained old-node change.");
            MaskSame("Guide and Shift old-node editing leave the committed mask intact.");

            Build(square); window.Focus(); canvas.Focus(); await Tick(root, token);
            Need(vertices.Count == 4, "Constrained loss fixture settles its draft before starting a new captured press.");
            savedSquare = Enumerable.Range(0, 4).Select(P).ToArray(); original = P(1); press = V(1) + new Vector2(2, 1);
            oldTarget = press + View(canvas, document, original + new Vector2(45, 30)) - View(canvas, document, original);
            Pointer(canvas, EventType.MouseDown, press);
            Need(vertices.Count == 4, "Constrained loss fixture keeps four points after captured press: tool=" + Read(window, "canvasTool") +
                ", polygonPointer=" + Read(gesture, "polygonPointer") + ", pressedVertex=" + Read(gesture, "pressedVertex") +
                ", capture=" + canvas.HasPointerCapture(PointerId.mousePointerId) + ", focus=" + (EditorWindow.focusedWindow == null ? "null" : EditorWindow.focusedWindow.GetType().Name));
            Pointer(canvas, EventType.MouseDrag, oldTarget, EventModifiers.Shift);
            Need(vertices.Count == 4 && (P(1) - original - Vector2.one * 37.5f).magnitude < .001f, "Capture-loss fixture establishes a constrained old-node drag: count=" + vertices.Count);
            canvas.ReleasePointer(PointerId.mousePointerId); Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Constrained drag fixture loses pointer capture ownership.");
            Pointer(canvas, EventType.MouseDrag, oldTarget, EventModifiers.Shift);
            Need(vertices.Count == 4 && Enumerable.Range(0, 4).All(n => P(n).Equals(savedSquare[n])), "Capture loss restores the constrained existing node and keeps all original points.");
            Pointer(canvas, EventType.MouseUp, oldTarget, EventModifiers.Shift); MaskSame("Capture loss of constrained existing-node drag cannot commit the mask.");
            press = V(1); oldTarget = press + View(canvas, document, original + new Vector2(45, 30)) - View(canvas, document, original);
            Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, oldTarget, EventModifiers.Shift);
            Key(canvas, KeyCode.Escape); Need(vertices.Count == 0 && !canvas.HasPointerCapture(PointerId.mousePointerId), "Escape cancels a constrained existing-node draft and releases capture.");
            Pointer(canvas, EventType.MouseUp, oldTarget, EventModifiers.Shift); MaskSame("Release after Escape cannot apply the constrained old-node edit.");
        }
        Call(window, "SetCanvasRotation", 0f); await Tick(root, token);

        // Dense sampling near the live tail must win over editing hits.
        Cancel(); Call(window, "SetCanvasZoomPercent", 100f); await Tick(root, token);
        Add(new Vector2(100, 100)); Add(new Vector2(101, 100)); Add(new Vector2(102, 100)); Add(new Vector2(103, 100));
        Need(vertices.Count == 4, "One-canvas-pixel increments append normally inside the live-tail zone.");
        Pointer(canvas, EventType.MouseMove, V(0)); Need(!Ready(gesture), "First snap is not armed while the tail remains inside the first radius.");
        Pointer(canvas, EventType.MouseDown, View(canvas, document, new Vector2(104, 100)), EventModifiers.None, 0, 2);
        Pointer(canvas, EventType.MouseUp, View(canvas, document, new Vector2(104, 100)), EventModifiers.None, 0, 2);
        Need(vertices.Count == 5, "Dense tail append survives nearby nodes/edges and a double-click flag.");
        Add(new Vector2(205, 180)); Need(vertices.Count == 6, "Leaving the first radius creates another point instead of closing.");
        Pointer(canvas, EventType.MouseMove, V(0)); Need(Ready(gesture), "Returning to first is armed only after the tail leaves its radius.");
        Pointer(canvas, EventType.MouseDown, V(0)); Need(vertices.Count == 6, "First-point press cannot close before mouse release.");
        before = P(0); press = V(0); Pointer(canvas, EventType.MouseDrag, press + new Vector2(-18, 18));
        Need(vertices.Count == 6 && P(0) != before, "Dragging the first node moves it instead of closing.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(-18, 18)); Need(vertices.Count == 6, "Releasing a first-node drag never completes the polygon.");
        MaskSame("Dense first-node editing preserves committed mask.");

        // Shift constrains new segments in canvas axes, including a rotated view.
        foreach (float angle in new[] { 0f, 27f })
        {
            Call(window, "SetCanvasZoomPercent", 150f); Call(window, "SetCanvasRotation", angle); await Tick(root, token);
            Build(triangle); Vector2 anchor = P(2), rawEnd = anchor + new Vector2(45, 10);
            object operation = Read(gesture, "combine");
            Pointer(canvas, EventType.MouseMove, View(canvas, document, rawEnd), EventModifiers.Shift);
            Need(((Vector2)Read(gesture, "Current") - new Vector2(rawEnd.x, anchor.y)).magnitude < .001f,
                "Shift hover produces a canvas-horizontal segment, independent of view rotation " + angle);
            KeyUp(canvas, KeyCode.LeftShift);
            Need(((Vector2)Read(gesture, "Current") - rawEnd).magnitude < .001f, "Releasing Shift restores free preview without mouse movement.");
            Key(canvas, KeyCode.LeftShift, EventModifiers.Shift);
            Need(Mathf.Abs(((Vector2)Read(gesture, "Current")).y - anchor.y) < .001f, "Pressing Shift restores the axis constraint without mouse movement.");
            Pointer(canvas, EventType.MouseDown, View(canvas, document, rawEnd), EventModifiers.Shift);
            Need(vertices.Count == 4 && (P(3) - new Vector2(rawEnd.x, anchor.y)).magnitude < .001f, "Shift click creates the new point on the constrained axis.");
            Vector2 rawDrag = anchor + new Vector2(55, 20);
            Pointer(canvas, EventType.MouseDrag, View(canvas, document, rawDrag), EventModifiers.Shift);
            Need((P(3) - new Vector2(rawDrag.x, anchor.y)).magnitude < .001f, "A newly created point remains axis-constrained while held with Shift.");
            KeyUp(canvas, KeyCode.LeftShift);
            Need((P(3) - rawDrag).magnitude < .001f, "Releasing Shift restores the new point's free position without moving the mouse.");
            rawDrag = anchor + new Vector2(5, 40);
            Pointer(canvas, EventType.MouseDrag, View(canvas, document, rawDrag), EventModifiers.Control | EventModifiers.Shift);
            Need((P(3) - new Vector2(anchor.x, rawDrag.y)).magnitude < .001f, "Ctrl+Shift retains the canvas-vertical creation constraint while disabling snapping.");
            Pointer(canvas, EventType.MouseUp, View(canvas, document, rawDrag), EventModifiers.Control | EventModifiers.Shift);
            Need(vertices.Count == 4 && (P(3) - new Vector2(anchor.x, rawDrag.y)).magnitude < .001f,
                "Axis-constrained new-point release keeps its position.");
            Need(Read(gesture, "combine").Equals(operation), "Shift used after the first point does not change selection combine operation.");
            MaskSame("Shift segment creation never changes the saved selection before completion.");
            foreach (Vector2 direction in new[] { new Vector2(45, 30), new Vector2(-45, 30), new Vector2(45, -30), new Vector2(-45, -30) })
            {
                Build(triangle); anchor = P(2); rawEnd = anchor + direction;
                Vector2 diagonal = anchor + new Vector2(Mathf.Sign(direction.x), Mathf.Sign(direction.y)) * 37.5f;
                Pointer(canvas, EventType.MouseMove, View(canvas, document, rawEnd), EventModifiers.Shift);
                Need(((Vector2)Read(gesture, "Current") - diagonal).magnitude < .001f, "Shift preview projects to the nearest canvas diagonal in all quadrants at view rotation " + angle);
                Pointer(canvas, EventType.MouseDown, View(canvas, document, rawEnd), EventModifiers.Control | EventModifiers.Shift);
                Need(vertices.Count == 4 && (P(3) - diagonal).magnitude < .001f, "Ctrl+Shift creation retains nearest45-degree projection while disabling snap/edit hits.");
                Pointer(canvas, EventType.MouseUp, View(canvas, document, rawEnd), EventModifiers.Control | EventModifiers.Shift);
                Need((P(3) - diagonal).magnitude < .001f, "Projected diagonal point remains exact after mouse release.");
                MaskSame("Diagonal Shift creation leaves the saved mask unchanged.");
            }
        }
        Call(window, "SetCanvasZoomPercent", 100f); Call(window, "SetCanvasRotation", 0f); await Tick(root, token);

        // A crossing/tied hit is bounded and only edits one node.
        Build(new Vector2(70, 60), new Vector2(71, 60), new Vector2(225, 65), new Vector2(220, 185));
        vertices[1] = vertices[0]; // Owned fixture models an overlap produced by point editing.
        before = P(0); press = V(0); Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, press + new Vector2(-18, -18));
        Need(vertices.Count == 4 && ((P(0) == before) != (P(1) == before)), "Overlapping-node hit deterministically moves one point without appending or closing.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(-18, -18)); MaskSame("Overlapping-node editing preserves committed mask.");

        // The moving final rubber segment and implicit final-to-first edge are not editable edges.
        Build(triangle); Pointer(canvas, EventType.MouseMove, View(canvas, document, new Vector2(60, 175)));
        Vector2 rubberMiddle = new Vector2(145, 180); Add(rubberMiddle);
        Need(vertices.Count == 4 && (P(3) - rubberMiddle).magnitude < .001f, "Click on uncommitted rubber segment appends at the tail instead of inserting earlier.");
        Build(triangle); Vector2 implicitMiddle = (P(0) + P(2)) * .5f; Add(implicitMiddle);
        Need(vertices.Count == 4 && (P(3) - implicitMiddle).magnitude < .001f, "Uncommitted last-to-first edge is not an insertion target.");
        Build(square); before = P(3); Click(V(3), EventModifiers.None, 1);
        Need(vertices.Count == 3 && P(2) != before, "Right-click retains removal of the last draft point.");
        Key(canvas, KeyCode.Backspace); Need(vertices.Count == 2, "Backspace retains removal of the last point.");
        Pointer(canvas, EventType.MouseMove, V(0)); Need(!Ready(gesture), "Two-point drafts cannot offer closure.");
        MaskSame("Removing points does not change committed mask.");
        Build(square); create = new Vector2(85, 210); Pointer(canvas, EventType.MouseDown, View(canvas, document, create));
        Need(vertices.Count == 5, "Backspace fixture has one held newly created point.");
        Key(canvas, KeyCode.Backspace); Need(vertices.Count == 4, "Backspace while creation is held removes only the new point.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, create)); Need(vertices.Count == 4, "Release after Backspace cannot restore the removed new point.");
        for (int expected = 3; expected >= 0; expected--)
        {
            Key(canvas, KeyCode.Backspace); Need(vertices.Count == expected, "Repeated Backspace removes the next last point down to zero.");
        }
        Need(!Ready(gesture), "Deleting all points clears closure readiness.");
        MaskSame("Backspace to zero leaves the committed selection intact.");

        // Capture loss rolls back just the edit and Escape cancels without changing selection.
        Build(square); original = P(1); press = V(1); Pointer(canvas, EventType.MouseDown, press);
        await Tick(root, token);
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(25, 20)); Need(P(1) != original, "Capture-loss fixture establishes an actual move.");
        await Tick(root, token);
        Need(canvas.HasPointerCapture(PointerId.mousePointerId), "Existing-node fixture establishes native pointer capture before releasing it.");
        if (canvas.HasPointerCapture(PointerId.mousePointerId)) canvas.ReleasePointer(PointerId.mousePointerId);
        Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Existing-node fixture removes pending capture ownership.");
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(25, 20));
        await Tick(root, token);
        Need(vertices.Count == 4 && P(1) == original, "Capture loss rolls back the edited vertex and preserves the unfinished polygon.");
        MaskSame("Capture loss never commits or alters the saved mask.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(25, 20));
        press = V(1); original = P(1); Pointer(canvas, EventType.MouseDown, press);
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(18, 20));
        Need(P(1) != original, "Explicit CaptureOut fixture establishes a moved existing node.");
        canvas.ReleasePointer(PointerId.mousePointerId);
        using (var lost = PointerCaptureOutEvent.GetPooled(canvas, null, PointerId.mousePointerId))
        { lost.target = canvas; canvas.SendEvent(lost); }
        Need(vertices.Count == 4 && P(1).Equals(original), "Public CaptureOut event rolls back the existing node through the Lost callback.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(18, 20)); MaskSame("CaptureOut callback and later release never commit the mask.");
        press = V(1); Pointer(canvas, EventType.MouseDown, press); Pointer(canvas, EventType.MouseDrag, press + new Vector2(18, 20));
        Key(canvas, KeyCode.Escape); Need(vertices.Count == 0, "Escape during editing cancels the unfinished polygon.");
        Pointer(canvas, EventType.MouseUp, press + new Vector2(18, 20)); MaskSame("Mouse release after Escape cannot commit a stale edit.");

        // The same interaction remains stable under rotated view and zoom.
        Call(window, "SetCanvasZoomPercent", 150f); Call(window, "SetCanvasRotation", 27f); await Tick(root, token);
        Build(square); original = P(1); press = V(1); Pointer(canvas, EventType.MouseDown, press);
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(18, 8)); Pointer(canvas, EventType.MouseUp, press + new Vector2(18, 8));
        Need(vertices.Count == 4 && P(1) != original, "Rotated/zoomed node dragging still moves exactly one point.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-edited-rotated.png"), token);
        Vector2 nearFirst = V(0) + new Vector2(3, 0); Pointer(canvas, EventType.MouseMove, nearFirst);
        Need(Ready(gesture) && (Vector2)Read(gesture, "Current") == P(0), "Rotated/zoomed first snap has the same screen-space radius.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-edited-ready.png"), token);
        Pointer(canvas, EventType.MouseDown, nearFirst); Need(vertices.Count == 4, "Closing starts on press but commits only on release.");
        Pointer(canvas, EventType.MouseUp, nearFirst); Need(vertices.Count == 0 && !Ready(gesture), "One plain first-node click completes on release.");
        Need(!Mask().SequenceEqual(committed), "Completed edited polygon replaces the fixture mask.");
        committed = (byte[])Mask().Clone();

        // Explicit completion remains available without a hover hit.
        Build(triangle); Key(canvas, KeyCode.Return); Need(vertices.Count == 0, "Enter explicitly completes an unfinished polygon.");
        committed = (byte[])Mask().Clone(); Build(square);
        Button close = root.Query<Button>().ToList().First(button => button.text == "Close");
        Pointer(close, EventType.MouseDown, close.contentRect.center); Pointer(close, EventType.MouseUp, close.contentRect.center);
        Need(vertices.Count == 0, "Header Close explicitly completes the polygon.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/lasso-edited-completed.png"), token);

        // Explicit completion must roll back a gesture whose capture ownership was already lost.
        committed = (byte[])Mask().Clone(); Build(square); savedSquare = Enumerable.Range(0, 4).Select(P).ToArray();
        byte[] retainedMask = ExpectedPolygon(savedSquare); create = new Vector2(85, 210);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, create)); await Tick(root, token);
        Need(vertices.Count == 5 && canvas.HasPointerCapture(PointerId.mousePointerId), "Lost-before-Enter fixture establishes a held created point.");
        canvas.ReleasePointer(PointerId.mousePointerId); Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Enter fixture first loses capture ownership.");
        Key(canvas, KeyCode.Return);
        Need(vertices.Count == 0 && Mask().SequenceEqual(retainedMask), "Enter after loss completes only the retained polygon, rolling back the new point.");
        committed = (byte[])Mask().Clone(); Build(square); savedSquare = Enumerable.Range(0, 4).Select(P).ToArray(); retainedMask = ExpectedPolygon(savedSquare);
        original = P(1); press = V(1); Pointer(canvas, EventType.MouseDown, press); await Tick(root, token);
        Pointer(canvas, EventType.MouseDrag, press + new Vector2(25, 20)); Need(P(1) != original, "Lost-before-Enter fixture moves an existing node.");
        canvas.ReleasePointer(PointerId.mousePointerId); Need(!canvas.HasPointerCapture(PointerId.mousePointerId), "Edited-node Enter fixture first loses capture ownership.");
        Key(canvas, KeyCode.Return);
        Need(vertices.Count == 0 && Mask().SequenceEqual(retainedMask), "Enter after loss rolls the old node back before completing the retained polygon.");

        // Explicit same-position double click must remain distinct from dense one-pixel sampling.
        committed = (byte[])Mask().Clone(); Build(triangle); create = new Vector2(85, 195); Add(create);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, create), EventModifiers.None, 0, 2);
        Need(vertices.Count == 4, "Exact-repeat double click cannot complete before release or add a duplicate.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, create), EventModifiers.None, 0, 2);
        Need(vertices.Count == 0, "Exact same-position background double click completes the polygon on release.");

        // Neighboring rectangle/ellipse behavior still uses its own capture state and Shift constraint.
        Call(window, "SetCanvasZoomPercent", 200f); Call(window, "SetCanvasRotation", 0f); await Tick(root, token);
        Call(window, "SetCanvasTool", tool("RectangleSelect")); await Tick(root, token);
        Vector2 start = new Vector2(80, 75), end = new Vector2(115, 90);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, start));
        Need(Cue<bool>(gesture, "RectangleDragging") && !Cue<bool>(gesture, "EllipseDragging"), "Rectangle tool has its own capture rather than a polygon press.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, end), EventModifiers.Shift);
        Vector2 constrained = (Vector2)Read(gesture, "Current") - (Vector2)Read(gesture, "Start");
        Need(Mathf.Abs(Mathf.Abs(constrained.x) - Mathf.Abs(constrained.y)) < .001f, "Shift during rectangle drag constrains both dimensions equally.");
        Pointer(canvas, EventType.MouseUp, View(canvas, document, end), EventModifiers.Shift);
        Need(!Cue<bool>(gesture, "RectangleDragging") && vertices.Count == 0, "Rectangle completion releases its own capture and never creates polygon nodes.");
        window.GetType().GetField("marqueeShape", Flags).SetValue(window, Enum.Parse(window.GetType().GetNestedType("MarqueeShape", Flags), "Ellipse"));
        start = new Vector2(80, 75); end = new Vector2(120, 105);
        Pointer(canvas, EventType.MouseDown, View(canvas, document, start));
        Need(Cue<bool>(gesture, "RectangleDragging") && Cue<bool>(gesture, "EllipseDragging"), "Ellipse keeps the shared marquee capture and ellipse flag.");
        Pointer(canvas, EventType.MouseDrag, View(canvas, document, end)); Pointer(canvas, EventType.MouseUp, View(canvas, document, end));
        byte[] ellipse = Mask(); Need(ellipse[75 * document.width + 80] == 0 && ellipse[90 * document.width + 100] > 0,
            "Ellipse completion excludes bounding-box corners and includes its center.");
        UnityAScope.CloseOwned(window);
        if (document != null) UnityEngine.Object.DestroyImmediate(document);
        Need(window == null && document == null, "All owned temporary window and document objects are removed.");
    }
}

