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

public static class FreehandLassoTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static T Cue<T>(object value, string name) => (T)value.GetType().GetProperty(name, Flags).GetValue(value);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Flags).SetValue(value, next);
    static object Call(object value, string name, params object[] args) => Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static Vector2 View(VisualElement canvas, WhimTexDocument document, Vector2 point)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + point.x / document.width * image.width, image.yMax - point.y / document.height * image.height));
    }
    static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None, int button = 0)
    {
        var input = new Event { type = type, button = button, clickCount = 1, modifiers = modifiers, mousePosition = target.LocalToWorld(point) };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Key(VisualElement target, KeyCode key, EventModifiers modifiers = EventModifiers.None)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = modifiers }); e.target = target; target.SendEvent(e);
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
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave; document.name = scope.Tag + "-freehand-document"; document.width = 320; document.height = 240;
        document.layers.Add(new ColorFillLayerBehaviour { color = new Color(.16f, .16f, .16f, 1f) });
        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
        // OwnWindow has isolated this session's store before defaults are changed.
        Call(Read(window, "toolPreferences"), "Reset"); Call(window, "SetDocument", document);
        window.ShowUtility(); window.position = new Rect(60, 60, 1250, 850); window.CreateGUI();
        var root = window.rootVisualElement; for (int i = 0; i < 5; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas"); canvas.Focus();
        var gesture = Read(window, "areaSelectionManipulator");
        bool Drawing() => Cue<bool>(gesture, "IsFreehandDrawing");
        IList Points() => (IList)Read(gesture, "FreehandPoints");
        IList Nodes() => (IList)Read(gesture, "Vertices");
        object Mode(string name) => Enum.Parse(Read(window, "areaSelectionMode").GetType(), name);
        object Lasso(string name) => Enum.Parse(Read(window, "lassoMode").GetType(), name);
        byte[] Mask() => (byte[])Call(window, "GetAreaSelection").GetType().GetProperty("Coverage", Flags).GetValue(Call(window, "GetAreaSelection"));
        byte[] Expected(Vector2[] shape, object combine, byte[] baseline, bool wrap = false)
        {
            Type selectedType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.CanvasSelection", true);
            object expected = Activator.CreateInstance(selectedType, Flags, null, new object[] { document.width, document.height }, null);
            if (baseline != null) Call(expected, "Set", baseline, Mode("Replace"));
            Call(expected, "Polygon", shape, combine, wrap, false);
            return (byte[])selectedType.GetProperty("Coverage", Flags).GetValue(expected);
        }
        byte[] baseline = Expected(new[] { new Vector2(100, 90), new Vector2(180, 90), new Vector2(180, 170), new Vector2(100, 170) }, Mode("Replace"), null);
        void ResetMask()
        {
            Need(!Drawing(), "Fixture resets masks only after the prior freehand gesture stopped.");
            Call(Call(window, "GetAreaSelection"), "Set", baseline, Mode("Replace")); Call(Read(window, "areaSelectionOverlay"), "Invalidate");
        }
        void Same(byte[] saved, string why) => Need(Mask().SequenceEqual(saved), why);
        void Begin(Vector2 point, EventModifiers mods = EventModifiers.None) => Pointer(canvas, EventType.MouseDown, View(canvas, document, point), mods);
        void Move(Vector2 point, EventModifiers mods = EventModifiers.None) => Pointer(canvas, EventType.MouseDrag, View(canvas, document, point), mods);
        void End(Vector2 point, EventModifiers mods = EventModifiers.None) => Pointer(canvas, EventType.MouseUp, View(canvas, document, point), mods);
        void SetFreehand() { Call(window, "SetLassoMode", Lasso("Freehand")); Key(canvas, KeyCode.L); }
        void Cancel(byte[] saved, string why)
        {
            Key(canvas, KeyCode.Escape); Need(!Drawing() && Points().Count == 0, "Escape removes all freehand points.");
            Same(saved, why);
        }

        // Both toolbar variants use one basic tool, and the shortcut recalls the selected variant.
        Need(Read(window, "lassoMode").ToString() == "Polygonal", "New windows start with the polygonal lasso variant.");
        Key(canvas, KeyCode.L); await Tick(root, token);
        var button = root.Q<Button>("polygonSelectTool"); Need(button != null, "One lasso toolbar button is available.");
        Pointer(button, EventType.MouseDown, button.contentRect.center);
        Pointer(button, EventType.MouseDrag, button.contentRect.center + new Vector2(0, 4)); await Tick(root, token);
        Need(root.Q("lassoPicker") != null && root.Q("lassoPicker").childCount == 2, "A short toolbar drag opens two lasso variants.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/freehand-lasso-picker.png"), token);
        Vector2 choice = button.WorldToLocal(root.Q("lassoFreehand").worldBound.center);
        Pointer(button, EventType.MouseDrag, choice); Pointer(button, EventType.MouseUp, choice); await Tick(root, token);
        Need(Read(window, "lassoMode").ToString() == "Freehand" && Read(window, "canvasTool").ToString() == "PolygonSelect", "The flyout selects Freehand within the same lasso tool.");
        Need(root.Q("lassoPicker") == null && !button.HasPointerCapture(PointerId.mousePointerId), "Variant selection closes the flyout and releases capture.");
        Need((bool)Read(Read(window, "lassoToolIcon"), "freehand") && button.tooltip.Contains("Freehand Lasso"), "The toolbar icon and tooltip display the freehand variant.");
        Key(canvas, KeyCode.M); Key(canvas, KeyCode.L); await Tick(root, token);
        Need(Read(window, "canvasTool").ToString() == "PolygonSelect" && Read(window, "lassoMode").ToString() == "Freehand", "L recalls the selected freehand variant after another tool.");
        var headerMode = root.Q<EnumField>("lassoMode"); Need(headerMode != null && headerMode.value.ToString() == "Freehand", "The header variant selector is synchronized.");
        Need(!root.Query<Button>().ToList().Any(b => b.text == "Close" && b.resolvedStyle.display != DisplayStyle.None && b.parent.resolvedStyle.display != DisplayStyle.None), "Freehand hides explicit polygon Close.");
        Call(window, "SetCanvasZoomPercent", 200f); Call(window, "SetCanvasRotation", 0f); await Tick(root, token);

        // Drag preview stays open and the saved mask changes only on release.
        ResetMask(); Write(window, "areaSelectionMode", Mode("Replace")); byte[] savedMask = (byte[])Mask().Clone();
        Vector2[] rectangle = { new Vector2(80, 75), new Vector2(200, 75), new Vector2(200, 160), new Vector2(80, 160) };
        Begin(rectangle[0]); Move(rectangle[1]); Move(rectangle[2]);
        Need(Drawing() && Points().Count >= 3 && Nodes().Count == 0, "A held freehand stroke stores its own points without polygon nodes.");
        Need(!Cue<bool>(gesture, "ClosingPolygon") && Cue<int>(gesture, "HoverVertex") < 0 && Cue<int>(gesture, "InsertSegment") < 0, "Freehand has no polygon vertex editing or close-ready state.");
        Same(savedMask, "A freehand preview cannot change the saved mask before release.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/freehand-lasso-preview.png"), token);
        Vector2 firstView = View(canvas, document, rectangle[0]), lastView = View(canvas, document, rectangle[2]);
        var previewEdges = ((IList)Read(Read(window, "areaSelectionOverlay"), "visibleEdges")).Cast<Vector4>();
        bool Closes(Vector4 e) => (new Vector2(e.x, e.y) - lastView).magnitude < .01f && (new Vector2(e.z, e.w) - firstView).magnitude < .01f;
        Need(!previewEdges.Any(Closes), "The held preview does not add a last-to-first closing segment.");
        Move(rectangle[3]); End(rectangle[3]);
        Need(!Drawing() && Points().Count == 0 && Nodes().Count == 0, "Release closes freehand and removes the draft path.");
        Same(Expected(rectangle, Mode("Replace"), savedMask), "Release automatically commits the closed shape without clicking its first point.");
        await UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/freehand-lasso-completed.png"), token);

        // Selection operation is captured at press, independently of later header/modifier changes.
        Vector2[] overlap = { new Vector2(140, 110), new Vector2(220, 110), new Vector2(220, 190), new Vector2(140, 190) };
        foreach (string combine in new[] { "Replace", "Add", "Subtract", "Intersect" })
        {
            ResetMask(); Write(window, "areaSelectionMode", Mode(combine)); savedMask = (byte[])Mask().Clone();
            Begin(overlap[0]); Write(window, "areaSelectionMode", Mode(combine == "Add" ? "Replace" : "Add"));
            Move(overlap[1], EventModifiers.Shift | EventModifiers.Alt); Move(overlap[2], EventModifiers.Shift); Move(overlap[3], EventModifiers.Control);
            Same(savedMask, "Mid-gesture mode and modifiers cannot commit the mask."); End(overlap[3]);
            Same(Expected(overlap, Mode(combine), savedMask), "Freehand retains the press-time " + combine + " operation.");
        }
        EventModifiers[] modifiers = { EventModifiers.Shift, EventModifiers.Alt, EventModifiers.Shift | EventModifiers.Alt };
        string[] modifierModes = { "Add", "Subtract", "Intersect" };
        for (int i = 0; i < modifiers.Length; i++)
        {
            ResetMask(); Write(window, "areaSelectionMode", Mode("Replace")); savedMask = (byte[])Mask().Clone();
            Begin(overlap[0], modifiers[i]); Move(overlap[1]); Move(overlap[2]); Move(overlap[3]); End(overlap[3]);
            Same(Expected(overlap, Mode(modifierModes[i]), savedMask), "Press-time modifiers choose " + modifierModes[i] + " without constraining freehand to45 degrees.");
        }

        // Invalid gestures preserve previous coverage rather than accidentally clearing it.
        foreach (Vector2[] invalid in new[] { new[] { new Vector2(90, 80) },
            new[] { new Vector2(90, 80), new Vector2(90.1f, 80), new Vector2(90.1f, 80.1f) },
            new[] { new Vector2(90, 80), new Vector2(160, 80), new Vector2(210, 80) } })
        {
            ResetMask(); Write(window, "areaSelectionMode", Mode("Replace")); savedMask = (byte[])Mask().Clone();
            Begin(invalid[0]); foreach (Vector2 point in invalid.Skip(1)) Move(point); End(invalid[invalid.Length - 1]);
            Need(!Drawing() && Points().Count == 0, "Invalid freehand gesture stops and removes its preview."); Same(savedMask, "Click, tiny and collinear paths cannot clear the prior selection.");
        }

        // Freehand always follows raw pointer positions: nearby guides never bend its curve.
        var guides = (IList)Read(window, "canvasGuides"); Type guideType = window.GetType().GetNestedType("CanvasGuide", Flags);
        object guide = Activator.CreateInstance(guideType); guideType.GetField("normal", Flags).SetValue(guide, Vector2.right); guideType.GetField("position", Flags).SetValue(guide, 150f); guides.Add(guide);
        float tolerance = (float)window.GetType().GetProperty("GuideSnapTolerance", Flags).GetValue(window); Vector2 raw = new Vector2(150f - tolerance * .5f, 100);
        foreach (EventModifiers control in new[] { EventModifiers.None, EventModifiers.Control })
        {
            ResetMask(); savedMask = (byte[])Mask().Clone(); Begin(new Vector2(90, 80), control); Move(raw, control);
            Need(((Vector2)Read(gesture, "FreehandCurrent") - raw).magnitude < .001f, "Freehand remains unsnapped near a guide with or without Ctrl."); Cancel(savedMask, "Cancelling an unsnapped guide-near curve preserves the mask.");
        }
        guides.Clear();

        // Zoom/rotation leave canvas-space shape semantics intact; coverage clips without clamping the raw path.
        foreach (float angle in new[] { 0f, 27f })
        {
            Call(window, "SetCanvasZoomPercent", 100f); Call(window, "SetCanvasRotation", angle); await Tick(root, token);
            ResetMask(); Write(window, "areaSelectionMode", Mode("Replace")); savedMask = (byte[])Mask().Clone();
            Vector2[] outside = { new Vector2(20, 20), new Vector2(340, 20), new Vector2(340, 260), new Vector2(-20, 260), new Vector2(-20, 20) };
            Begin(outside[0]); foreach (Vector2 point in outside.Skip(1)) Move(point);
            Need(Points().Cast<Vector2>().Any(p => p.x < 0 || p.y < 0 || p.x > document.width || p.y > document.height), "Freehand retains raw out-of-canvas path points instead of flattening them onto canvas edges.");
            End(outside[outside.Length - 1]); Same(Expected(outside, Mode("Replace"), savedMask), "Out-of-canvas freehand coverage clips correctly at rotated view " + angle);
        }
        Write(window, "tiledCanvas", true); Call(window, "UpdateToolkitCanvasPresentation"); await Tick(root, token);
        ResetMask(); savedMask = (byte[])Mask().Clone(); Vector2[] seam = { new Vector2(310, 100), new Vector2(340, 100), new Vector2(340, 145), new Vector2(310, 145) };
        Begin(seam[0]); foreach (Vector2 point in seam.Skip(1)) Move(point);
        Need(Points().Cast<Vector2>().Any(p => p.x > document.width), "Tiled freehand samples remain unwrapped across the seam."); End(seam[3]);
        Same(Expected(seam, Mode("Replace"), savedMask, true), "Tiled freehand wraps coverage across the canvas seam.");
        Write(window, "tiledCanvas", false); Call(window, "UpdateToolkitCanvasPresentation"); Call(window, "SetCanvasRotation", 0f); await Tick(root, token);

        // Gentle curvature must survive online sampling instead of accumulating a flattening error.
        ResetMask(); Write(window, "areaSelectionMode", Mode("Replace")); savedMask = (byte[])Mask().Clone();
        Vector2[] gentle = Enumerable.Range(0, 181).Select(i => new Vector2(50 + i, 80 + .0005f * i * i)).ToArray();
        Begin(gentle[0]); foreach (Vector2 point in gentle.Skip(1)) Move(point);
        Need(Drawing() && Points().Count >= 175, "A fresh sampler preserves dense gentle-curvature points rather than collapsing the arc into its chord.");
        Same(savedMask, "Gentle curvature remains a preview until release."); End(gentle[180]);
        Same(Expected(gentle, Mode("Replace"), savedMask), "Gentle-curve release creates the curved sliver instead of preserving or flattening the mask.");
        Need(Mask()[85 * document.width + 140] != 0, "The gentle curve creates visible area between its arc and closing chord.");

        // The final endpoint is forced even when it is closer than the ordinary sampling spacing.
        ResetMask(); savedMask = (byte[])Mask().Clone(); Vector2 finalNear = new Vector2(200.6f, 160);
        Vector2[] forcedFinalShape = { rectangle[0], rectangle[1], rectangle[2], finalNear };
        Begin(rectangle[0]); Move(rectangle[1]); Move(rectangle[2]); Move(finalNear);
        Need(Drawing() && Points().Count == 3 && ((Vector2)Read(gesture, "FreehandCurrent") - finalNear).magnitude < .001f,
            "A sub-.75-view-point live endpoint moves the preview without prematurely adding a sample.");
        byte[] finalExpected = Expected(forcedFinalShape, Mode("Replace"), savedMask);
        Need(!finalExpected.SequenceEqual(Expected(rectangle.Take(3).ToArray(), Mode("Replace"), savedMask)), "Forced-endpoint fixture has a raster-visible effect.");
        End(finalNear); Same(finalExpected, "Release forces the final nearby endpoint into the closed selection.");
        ResetMask(); savedMask = (byte[])Mask().Clone();
        Begin(rectangle[0]); Move(rectangle[1]); Move(rectangle[2]); Move(finalNear); Key(canvas, KeyCode.Return);
        Need(!Drawing() && Points().Count == 0, "Enter completes the held freehand stroke and removes its draft.");
        Same(Expected(forcedFinalShape, Mode("Replace"), savedMask), "Enter forces the final nearby live endpoint just like mouse release.");
        End(finalNear); Same(Expected(forcedFinalShape, Mode("Replace"), savedMask), "A later release cannot replace an Enter-completed freehand mask.");

        // Successful bounded compaction keeps accepting the tail rather than silently stopping at its old limit.
        ResetMask(); savedMask = (byte[])Mask().Clone(); Vector2 compactStart = new Vector2(40, 60);
        Vector2 compactTail = compactStart; Begin(compactStart);
        for (int i = 1; i <= 4200; i++) { compactTail = compactStart + new Vector2(.8f * i, 0); Move(compactTail); }
        Need(Drawing() && Points().Count >= 3 && Points().Count < 4096, "A collinear path continues drawing after successful bounded compaction.");
        Need(((Vector2)Read(gesture, "FreehandCurrent") - compactTail).magnitude < .001f,
            "Successful compaction retains the latest endpoint beyond the original sample limit.");
        Same(savedMask, "Compaction does not prematurely commit the saved mask.");
        Vector2 compactEnd = new Vector2(40, 190); End(compactEnd);
        Same(Expected(new[] { compactStart, compactTail, compactEnd }, Mode("Replace"), savedMask),
            "Release after compaction closes the entire continued path with its non-collinear final endpoint.");

        // A detailed path crosses the polygon node limit without truncation.
        ResetMask(); savedMask = (byte[])Mask().Clone(); Vector2[] detailed = Enumerable.Range(0, 400).Select(i =>
        {
            float a = i * Mathf.PI * 2f / 400; float radius = (i & 1) == 0 ? 80 : 84;
            return new Vector2(160 + Mathf.Cos(a) * radius, 120 + Mathf.Sin(a) * radius);
        }).ToArray();
        Begin(detailed[0]); foreach (Vector2 point in detailed.Skip(1)) Move(point);
        Need(Drawing() && Points().Count > 256 && Points().Count <= 4096, "Detailed freehand retains more than256 samples without the polygon cap.");
        Same(savedMask, "A long detailed path remains a preview until release."); End(detailed[399]);
        byte[] detailedMask = Mask(); Need(detailedMask[120 * document.width + 85] != 0 && detailedMask[120 * document.width + 235] != 0 &&
            detailedMask[45 * document.width + 160] != 0 && detailedMask[195 * document.width + 160] != 0,
            "Late-path quadrants are present in the committed detailed shape.");
        ResetMask(); savedMask = (byte[])Mask().Clone(); Begin(detailed[0]);
        for (int i = 1; i < 4300 && Drawing(); i++) Move(detailed[i % detailed.Length]);
        Need(!Drawing() && Points().Count == 0, "An irreducibly complex bounded path cancels instead of silently truncating its tail.");
        End(detailed[299]); Same(savedMask, "Complexity cancellation preserves the saved mask after release.");

        // Input interruptions discard an unfinished path and never apply stale geometry.
        foreach (string cancellation in new[] { "Escape", "CaptureGuard", "CaptureOut", "Blur", "Tool", "Subtype", "Wheel", "Middle" })
        {
            SetFreehand(); ResetMask(); savedMask = (byte[])Mask().Clone(); Begin(rectangle[0]); Move(rectangle[1]); Move(rectangle[2]);
            Need(Drawing(), "Cancellation fixture starts an owned freehand stroke.");
            if (cancellation == "Escape") Key(canvas, KeyCode.Escape);
            else if (cancellation == "CaptureGuard") { canvas.ReleasePointer(PointerId.mousePointerId); Move(rectangle[2]); }
            else if (cancellation == "CaptureOut") { canvas.ReleasePointer(PointerId.mousePointerId); using var e = PointerCaptureOutEvent.GetPooled(canvas, null, PointerId.mousePointerId); e.target = canvas; canvas.SendEvent(e); }
            else if (cancellation == "Blur") root.Query<Button>().ToList().First().Focus();
            else if (cancellation == "Tool") Key(canvas, KeyCode.M);
            else if (cancellation == "Subtype") Call(window, "SetLassoMode", Lasso("Polygonal"));
            else if (cancellation == "Wheel") { using var e = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = canvas.LocalToWorld(View(canvas, document, rectangle[2])), delta = new Vector2(0, 1) }); e.target = canvas; canvas.SendEvent(e); }
            else { Pointer(canvas, EventType.MouseDown, View(canvas, document, rectangle[2]), EventModifiers.None, 2); Pointer(canvas, EventType.MouseUp, View(canvas, document, rectangle[2]), EventModifiers.None, 2); }
            Need(!Drawing() && Points().Count == 0, cancellation + " discards the freehand draft."); End(rectangle[2]); Same(savedMask, cancellation + " and later release cannot change the saved mask.");
        }
        SetFreehand(); ResetMask(); savedMask = (byte[])Mask().Clone(); Begin(rectangle[0]); Move(rectangle[1]); Move(rectangle[2]);
        var next = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>()); next.hideFlags = HideFlags.HideAndDontSave; next.width = 320; next.height = 240;
        next.layers.Add(new ColorFillLayerBehaviour { color = Color.white }); Call(window, "SetDocument", next); document = next;
        Need(!Drawing() && Points().Count == 0, "Document replacement cancels the unfinished freehand path."); End(rectangle[2]);
        object newSelection = Call(window, "GetAreaSelection"); byte[] newCoverage = Mask();
        Need(!(bool)newSelection.GetType().GetProperty("Active", Flags).GetValue(newSelection) && (newCoverage == null || newCoverage.All(v => v == 0)),
            "Release after document replacement cannot activate coverage from the old document's path.");
        UnityAScope.CloseOwned(window); if (document != null) UnityEngine.Object.DestroyImmediate(document);
        Need(window == null && document == null, "All owned temporary windows and documents are removed.");
    }
}
