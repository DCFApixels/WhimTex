using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests.UnityC;

public static class CropToolTests
{
    static readonly string[] OrientationButtons = { "canvasCropFlipHorizontal", "canvasCropFlipVertical", "canvasCropRotateLeft", "canvasCropRotateRight" };
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Read(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static void Write(object value, string name, object next) => value.GetType().GetField(name, Flags).SetValue(value, next);
    static object Call(object value, string name, params object[] args) =>
        Array.Find(value.GetType().GetMethods(Flags), m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static RectInt Frame(WhimTexWindow window) => (RectInt)Call(window, "GetCanvasCropFrame");
    static void SetFrame(WhimTexWindow window, RectInt frame) => Call(Read(window, "cropManipulator"), "SetFrame", frame);
    static bool Dragging(WhimTexWindow window) => (bool)Read(window, "cropManipulator").GetType().GetProperty("IsDragging", Flags).GetValue(Read(window, "cropManipulator"));
    static Vector2 View(VisualElement canvas, WhimTexDocument document, Vector2 pixels)
    {
        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        return (Vector2)Call(canvas, "ToView", new Vector2(image.x + pixels.x / document.width * image.width,
            image.yMax - pixels.y / document.height * image.height));
    }
    static Vector2 Grip(RectInt frame, int index) => new Vector2(
        index == 0 || index == 6 || index == 7 ? frame.xMin : index == 2 || index == 3 || index == 4 ? frame.xMax : frame.x + frame.width * .5f,
        index <= 2 ? frame.yMin : index >= 4 && index <= 6 ? frame.yMax : frame.y + frame.height * .5f);
    static Vector2 OrientedPoint(Vector2 point, Vector2Int size, int action)
    {
        switch (action)
        {
            case 0: return new Vector2(size.x - point.x, point.y);
            case 1: return new Vector2(point.x, size.y - point.y);
            case 2: return new Vector2(size.y - point.y, point.x);
            default: return new Vector2(point.y, size.x - point.x);
        }
    }
    static RectInt OrientedFrame(RectInt frame, Vector2Int size, int action)
    {
        switch (action)
        {
            case 0: return new RectInt(size.x - frame.xMax, frame.y, frame.width, frame.height);
            case 1: return new RectInt(frame.x, size.y - frame.yMax, frame.width, frame.height);
            case 2: return new RectInt(size.y - frame.yMax, frame.x, frame.height, frame.width);
            default: return new RectInt(frame.y, size.x - frame.xMax, frame.height, frame.width);
        }
    }
    static Vector2Int OrientedPixel(Vector2Int pixel, Vector2Int size, int action)
    {
        switch (action)
        {
            case 0: return new Vector2Int(size.x - 1 - pixel.x, pixel.y);
            case 1: return new Vector2Int(pixel.x, size.y - 1 - pixel.y);
            case 2: return new Vector2Int(size.y - 1 - pixel.y, pixel.x);
            default: return new Vector2Int(pixel.y, size.x - 1 - pixel.x);
        }
    }
    static void Pointer(VisualElement target, EventType type, Vector2 point, EventModifiers modifiers = EventModifiers.None, int button = 0)
    {
        var input = new Event { type = type, button = button, mousePosition = target.LocalToWorld(point), modifiers = modifiers };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = target; target.SendEvent(e); }
    }
    static void Key(VisualElement target, KeyCode code, EventModifiers modifiers = EventModifiers.None)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, modifiers = modifiers });
        e.target = target; target.SendEvent(e);
    }
    static void KeyUp(VisualElement target, KeyCode code)
    {
        using var e = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = code });
        e.target = target; target.SendEvent(e);
    }
    static async Task Tick(VisualElement root, CancellationToken token)
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

    static async Task Execute(AsyncFixture job, CancellationToken token)
    {
        var t = job.Context; var scope = job.Scope;
        var document = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave; document.width = 320; document.height = 240;
        Layer layer = new ColorFillLayerBehaviour { color = Color.white }; document.layers.Add(layer);
        var window = scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        WhimTexDocument other = null;
        Write(window, "activeDocument", document);
        job.OwnCleanup(() => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); if (other != null) Undo.ClearUndo(other); });
        window.ShowUtility(); window.position = new Rect(60, 60, 1400, 800); window.CreateGUI();
        var root = window.rootVisualElement;
        for (int i = 0; i < 4; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        Key(canvas, KeyCode.C); await Tick(root, token);
        t.Equal("Crop", Read(window, "canvasTool").ToString(), "C activates Crop");
        var mode = root.Q<EnumField>("canvasCropMode");
        t.True(mode != null, "Crop mode has a common header selector");
        t.Equal("Resize", mode.value.ToString(), "New Crop windows start in Resize mode");
        var cropButton = root.Q<Button>("cropTool");
        void SelectMode(string name) => mode.value = (Enum)Enum.Parse(mode.value.GetType(), name);
        async Task PickMode(string name, bool hold)
        {
            Vector2 press = cropButton.contentRect.center;
            Pointer(cropButton, EventType.MouseDown, press);
            if (hold) for (int i = 0; i < 4; i++) await Tick(root, token);
            else { Pointer(cropButton, EventType.MouseDrag, press + new Vector2(0, 4)); await Tick(root, token); }
            t.True(root.Q("cropPicker") != null, "Hold or short drag opens the Crop subtype picker");
            t.Equal(2, root.Q("cropPicker").childCount, "Crop has exactly two subtype icons");
            VisualElement item = root.Q(name == "Scale" ? "cropScale" : "cropResize");
            Vector2 choice = cropButton.WorldToLocal(item.worldBound.center);
            Pointer(cropButton, EventType.MouseDrag, choice); Pointer(cropButton, EventType.MouseUp, choice);
            await Tick(root, token);
            t.Equal(name, mode.value.ToString(), "Picker release synchronizes the header mode");
            t.Equal(name, Read(window, "canvasCropMode").ToString(), "Picker chooses a Crop mode rather than a new tool");
            t.Equal("Crop", Read(window, "canvasTool").ToString(), "Both picker items use the one Crop tool");
            t.True(cropButton.tooltip.Contains("Crop: " + name), "Toolbar tooltip names the current Crop subtype");
            t.True(root.Q("cropPicker") == null && !cropButton.HasPointerCapture(PointerId.mousePointerId), "Choosing a subtype closes its flyout and releases capture");
        }
        await PickMode("Scale", true);
        Pointer(cropButton, EventType.MouseDown, cropButton.contentRect.center);
        Pointer(cropButton, EventType.MouseUp, cropButton.contentRect.center);
        t.Equal("Scale", mode.value.ToString(), "Reselecting Crop retains its chosen subtype");
        Key(canvas, KeyCode.B); Key(canvas, KeyCode.Q); KeyUp(canvas, KeyCode.Q);
        t.Equal("Crop", Read(window, "canvasTool").ToString(), "Previous-tool history returns to the one Crop tool");
        t.Equal("Scale", mode.value.ToString(), "Q preserves the Crop subtype");
        Key(canvas, KeyCode.B); Key(canvas, KeyCode.C);
        t.Equal("Scale", mode.value.ToString(), "C preserves the Crop subtype");
        Pointer(cropButton, EventType.MouseDown, cropButton.contentRect.center);
        Pointer(cropButton, EventType.MouseDrag, cropButton.contentRect.center + new Vector2(0, 4)); await Tick(root, token);
        Key(root, KeyCode.Escape);
        t.True(root.Q("cropPicker") == null && !cropButton.HasPointerCapture(PointerId.mousePointerId), "Escape cancels the subtype picker and its capture");
        t.Equal("Scale", mode.value.ToString(), "Picker cancellation preserves the chosen mode");
        Pointer(cropButton, EventType.MouseUp, cropButton.contentRect.center);
        await Tick(root, token);
        Pointer(cropButton, EventType.MouseDown, cropButton.contentRect.center);
        Pointer(cropButton, EventType.MouseDrag, cropButton.contentRect.center + new Vector2(0, 4)); await Tick(root, token);
        t.True(root.Q("cropResize") != null, "Picker reopens after Escape and pointer release");
        Vector2 cancelledChoice = cropButton.WorldToLocal(root.Q("cropResize").worldBound.center);
        Pointer(cropButton, EventType.MouseDrag, cancelledChoice);
        cropButton.ReleasePointer(PointerId.mousePointerId); await Tick(root, token);
        Pointer(cropButton, EventType.MouseUp, cancelledChoice);
        t.Equal("Scale", mode.value.ToString(), "Capture loss cannot commit a hovered subtype");
        t.True(root.Q("cropPicker") == null, "Capture loss removes the flyout");
        await PickMode("Resize", false);
        SelectMode("Scale"); SelectMode("Resize");
        t.True(cropButton.tooltip.Contains("Crop: Resize"), "Header mode changes synchronize the toolbar");
        t.True(root.Q<Button>("cropTool").ClassListContains("whimtex-tool-button--selected"), "Crop has a selected toolbar button");
        t.Equal(1, root.Query<Button>(className: "whimtex-tool-button--selected").ToList().Count, "One basic tool is selected");
        t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "Initial frame is the full canvas");
        t.True(!root.Q<Button>("cropApply").enabledSelf, "Apply is disabled for unchanged bounds");
        foreach (string name in OrientationButtons)
            t.True(root.Q<Button>(name) != null && root.Q<Button>(name).enabledSelf, "Document orientation action is available: " + name);
        var transform = layer.transform;
        string documentBefore = EditorJsonUtility.ToJson(document);
        root.Q<IntegerField>("cropX").value = 13;
        root.Q<IntegerField>("cropY").value = -7;
        root.Q<IntegerField>("cropWidth").value = 120;
        root.Q<IntegerField>("cropHeight").value = 90;
        var numeric = new RectInt(13, 157, 120, 90);
        t.Equal(numeric, Frame(window), "X/Y use top-left coordinates and W/H keep that corner fixed");
        Key(root.Q<IntegerField>("cropX"), KeyCode.Escape);
        t.Equal(numeric, Frame(window), "Keys used in a numeric field do not reset the crop frame");
        t.Equal(documentBefore, EditorJsonUtility.ToJson(document), "Editing the draft leaves the document untouched");
        PublicInput.Click(root.Q<Button>("cropReset"));
        t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "Reset button restores canvas bounds");

        var pixelScale = root.Q<FloatField>("cropPixelScale");
        var autoPixelScale = root.Q<Toggle>("cropAutoPixelScale");
        t.True(pixelScale != null && pixelScale.isDelayed, "Crop has a delayed Pixel Scale numeric field");
        t.Near(1, document.pixelScale, 0, "New documents have Pixel Scale 1");
        t.Near(1, pixelScale.value, 0, "Initial Crop draft uses the document multiplier");
        t.True(autoPixelScale != null && autoPixelScale.value, "Automatic pixel sizes start enabled");
        t.True(pixelScale.enabledSelf, "Resize permits manual pixel sizes");
        t.True(autoPixelScale.resolvedStyle.display == DisplayStyle.None, "Automatic fitting is specific to Scale");
        pixelScale.value = 1.75f;
        t.Near(1.75, (float)Call(window, "GetCanvasCropPixelScale"), 0, "Resize keeps a manual multiplier draft");
        t.Equal(documentBefore, EditorJsonUtility.ToJson(document), "Manual Pixel Scale does not mutate the document before Apply");
        t.True(root.Q<Button>("cropApply").enabledSelf && root.Q<Button>("cropReset").enabledSelf, "A multiplier-only draft enables Apply and Reset");
        PublicInput.Click(root.Q<Button>("cropReset"));
        t.Near(1, pixelScale.value, 0, "Reset discards a manual multiplier draft");
        SelectMode("Scale"); await Tick(root, token);
        t.True(!pixelScale.enabledSelf && autoPixelScale.resolvedStyle.display != DisplayStyle.None, "Automatic Scale displays a read-only computed multiplier");
        SetFrame(window, new RectInt(0, 0, 480, 360));
        t.Near(1.5, pixelScale.value, .000001, "Proportional Scale fits pixel sizes to both axes");
        SetFrame(window, new RectInt(0, 0, 640, 120));
        t.Near(.5, pixelScale.value, .000001, "Anisotropic Scale chooses the smaller factor, including shrinking one axis");
        SetFrame(window, new RectInt(-20, 15, 640, 480));
        t.Near(2, pixelScale.value, .000001, "Repeated edits calculate from the applied size instead of accumulating ratios");
        t.Equal(documentBefore, EditorJsonUtility.ToJson(document), "Automatic Pixel Scale previews leave all model data unchanged");
        autoPixelScale.value = false;
        t.True(pixelScale.enabledSelf, "Disabling Auto permits a manual multiplier");
        t.Near(2, pixelScale.value, .000001, "Turning Auto off retains its current result");
        pixelScale.value = 1.75f; SetFrame(window, new RectInt(0, 0, 320, 120));
        t.Near(1.75, pixelScale.value, .000001, "Scale with Auto off preserves the manual pixel size during geometry changes");
        PublicInput.Click(root.Q<Button>("cropReset"));
        t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "Reset discards the Scale geometry draft");
        t.Near(1, pixelScale.value, 0, "Reset restores the applied multiplier together with Scale geometry");
        t.True(!autoPixelScale.value, "Reset preserves the chosen Auto preference");
        autoPixelScale.value = true; SelectMode("Resize"); await Tick(root, token);

        var original = new RectInt(70, 65, 120, 80);
        foreach (float angle in new[] { 0f, 31f })
        {
            Call(window, "SetCanvasRotation", angle); Call(window, "SetCanvasZoomPercent", 175f);
            for (int i = 0; i < 8; i++)
            {
                SetFrame(window, original);
                Vector2 a = Grip(original, i);
                Vector2 delta = new Vector2(a.x == original.xMin ? -20 : 20, a.y == original.yMin ? -15 : 15);
                bool x = i != 1 && i != 5, y = i != 3 && i != 7;
                Vector2 b = a + new Vector2(x ? delta.x : 0, y ? delta.y : 0);
                Vector2 grab = View(canvas, document, a), end = View(canvas, document, b);
                t.True(canvas.contentRect.Contains(grab), "Owned fixture exposes handle " + angle + "/" + i);
                Pointer(canvas, EventType.MouseDown, grab, EventModifiers.Control);
                t.True(Dragging(window) && canvas.HasPointerCapture(PointerId.mousePointerId), "Handle captures pointer " + i);
                if (angle == 0 && i == 0)
                    foreach (string name in OrientationButtons)
                        t.True(!root.Q<Button>(name).enabledSelf, "Document orientation is disabled during a crop gesture: " + name);
                Pointer(canvas, EventType.MouseDrag, end, EventModifiers.Control);
                Pointer(canvas, EventType.MouseUp, end, EventModifiers.Control);
                var expected = new RectInt(original.x + (x && a.x == original.xMin ? (int)delta.x : 0),
                    original.y + (y && a.y == original.yMin ? (int)delta.y : 0), original.width + (x ? 20 : 0), original.height + (y ? 15 : 0));
                t.Equal(expected, Frame(window), "Handle resizes in document axes at rotated view " + angle + "/" + i);
                t.True(!Dragging(window) && !canvas.HasPointerCapture(PointerId.mousePointerId), "Release keeps a draft without capture");
                t.Equal(documentBefore, EditorJsonUtility.ToJson(document), "Handle previews do not mutate model data");
            }
        }
        Call(window, "SetCanvasRotation", 0f);
        SetFrame(window, original);
        Vector2 corner = View(canvas, document, Grip(original, 4));
        Vector2 proportionEnd = View(canvas, document, Grip(original, 4) + new Vector2(30, 30));
        Pointer(canvas, EventType.MouseDown, corner, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, proportionEnd, EventModifiers.Control | EventModifiers.Shift);
        t.Near(original.width / (double)original.height, Frame(window).width / (double)Frame(window).height, .02, "Shift preserves the corner aspect ratio");
        Key(canvas, KeyCode.Escape); Pointer(canvas, EventType.MouseUp, proportionEnd);
        t.Equal(original, Frame(window), "First Escape cancels only the gesture and restores its prior draft");
        Key(canvas, KeyCode.Escape); t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "Idle Escape resets the draft to canvas bounds");

        SetFrame(window, original);
        Vector2 center = View(canvas, document, original.center);
        Vector2 moveEnd = View(canvas, document, original.center + new Vector2(17, -11));
        Pointer(canvas, EventType.MouseDown, center, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, moveEnd, EventModifiers.Control);
        Pointer(canvas, EventType.MouseUp, moveEnd, EventModifiers.Control);
        t.Equal(new RectInt(87, 54, 120, 80), Frame(window), "Dragging inside moves the frame without resizing");
        Key(canvas, KeyCode.UpArrow, EventModifiers.Shift);
        t.Equal(new RectInt(87, 64, 120, 80), Frame(window), "Shift arrow moves the draft by ten pixels");
        t.True(layer.transform.Equals(transform), "Crop arrow keys do not nudge selected layers");

        SetFrame(window, original);
        Pointer(canvas, EventType.MouseDown, corner, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, proportionEnd, EventModifiers.Control);
        canvas.ReleasePointer(PointerId.mousePointerId); await Tick(root, token);
        t.Equal(original, Frame(window), "Capture loss restores the pre-gesture draft");
        Pointer(canvas, EventType.MouseDown, corner, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, proportionEnd, EventModifiers.Control);
        Call(window, "OnLostFocus");
        t.Equal(original, Frame(window), "Focus loss cancels the current drag");
        t.True(!Dragging(window), "Focus loss clears pointer ownership");
        window.Focus(); canvas.Focus();

        Pointer(canvas, EventType.MouseDown, canvas.contentRect.center, button: 2);
        Pointer(canvas, EventType.MouseDrag, canvas.contentRect.center + new Vector2(15, 8), button: 2);
        Pointer(canvas, EventType.MouseUp, canvas.contentRect.center + new Vector2(15, 8), button: 2);
        t.Equal(original, Frame(window), "Middle-button navigation preserves the idle crop draft");
        Call(window, "SetCanvasZoomPercent", 150f);
        t.Equal(original, Frame(window), "Changing zoom preserves the idle crop draft");

        Call(window, "ChangeCanvasZoom", true); await Tick(root, token);
        SetFrame(window, new RectInt(0, 0, 320, 240));
        Vector2 first = View(canvas, document, new Vector2(-8, 20)), last = View(canvas, document, new Vector2(330, 245));
        t.True(canvas.contentRect.Contains(first) && canvas.contentRect.Contains(last), "Fixture includes space outside canvas for expansion");
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, last, EventModifiers.Control);
        Pointer(canvas, EventType.MouseUp, last, EventModifiers.Control);
        t.Equal(new RectInt(-8, 20, 338, 225), Frame(window), "Drawing outside the canvas permits expansion");
        SetFrame(window, original);
        first = View(canvas, document, new Vector2(35, 25)); last = View(canvas, document, new Vector2(10, 5));
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, last, EventModifiers.Control);
        Pointer(canvas, EventType.MouseUp, last, EventModifiers.Control);
        t.Equal(new RectInt(10, 5, 25, 20), Frame(window), "A reverse drag normalizes a new frame");
        SetFrame(window, new RectInt(0, 0, 0, 0));
        t.Equal(new Vector2Int(1, 1), Frame(window).size, "Numeric bounds have a one-pixel minimum");

        SetFrame(window, original); Key(canvas, KeyCode.B); Key(canvas, KeyCode.C);
        t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "Changing tools discards the prior crop draft");
        SetFrame(window, original); Call(window, "OnUndoRedo");
        t.Equal(new RectInt(0, 0, 320, 240), Frame(window), "History invalidation resets the draft without invoking global Undo");

        var gradient = new Layer(new GradientLayerBehaviour()); document.layers.Add(gradient);
        Call(document, "NormalizeModel");
        Call(window, "SelectOnlyLayer", gradient.Id); Call(window, "RefreshToolkitInterface", false);
        t.Equal("Crop", Read(window, "canvasTool").ToString(), "Selecting a gradient keeps Crop active");

        byte[] coverage = new byte[document.width * document.height]; coverage[30 * document.width + 20] = 1; coverage[40 * document.width + 30] = 255;
        var selection = Call(window, "GetAreaSelection");
        Type combine = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SelectionCombine", true);
        Call(selection, "Set", coverage, Enum.Parse(combine, "Replace"));
        SetFrame(window, original);
        first = View(canvas, document, Grip(original, 7));
        last = View(canvas, document, new Vector2(21.2f, original.center.y));
        Pointer(canvas, EventType.MouseDown, first);
        Pointer(canvas, EventType.MouseDrag, last); Pointer(canvas, EventType.MouseUp, last);
        t.Equal(20, Frame(window).xMin, "Crop snaps to the faint outermost nonzero selection pixel");
        SetFrame(window, original);
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, last, EventModifiers.Control); Pointer(canvas, EventType.MouseUp, last, EventModifiers.Control);
        t.Equal(21, Frame(window).xMin, "Ctrl bypasses selection-bound snapping");

        var guides = (System.Collections.IList)Read(window, "canvasGuides");
        Type guideType = typeof(WhimTexWindow).GetNestedType("CanvasGuide", BindingFlags.NonPublic);
        object Guide(Vector2 normal, float position)
        { object value = Activator.CreateInstance(guideType); Write(value, "normal", normal); Write(value, "position", position); return value; }
        guides.Add(Guide(Vector2.right, 50)); guides.Add(Guide(Vector2.up, 150));
        Write(window, "canvasGuidesDocument", document); Write(window, "canvasGuidesSnap", true); Write(window, "canvasGuidesHidden", false);
        SetFrame(window, original);
        last = View(canvas, document, new Vector2(51.2f, original.center.y));
        Pointer(canvas, EventType.MouseDown, first); Pointer(canvas, EventType.MouseDrag, last); Pointer(canvas, EventType.MouseUp, last);
        t.Equal(50, Frame(window).xMin, "Crop side snaps to an axis-aligned guide");
        SetFrame(window, original); Call(window, "SetCanvasGuidesHidden", true);
        Pointer(canvas, EventType.MouseDown, first); Pointer(canvas, EventType.MouseDrag, last); Pointer(canvas, EventType.MouseUp, last);
        t.Equal(51, Frame(window).xMin, "Hidden guides do not attract Crop");
        Call(window, "SetCanvasGuidesHidden", false); Write(window, "canvasGuidesSnap", false); SetFrame(window, original);
        Pointer(canvas, EventType.MouseDown, first); Pointer(canvas, EventType.MouseDrag, last); Pointer(canvas, EventType.MouseUp, last);
        t.Equal(51, Frame(window).xMin, "Guide snapping toggle is respected");
        Write(window, "canvasGuidesSnap", true); SetFrame(window, original);
        first = View(canvas, document, Grip(original, 0)); last = View(canvas, document, new Vector2(51.2f, 91.2f));
        Pointer(canvas, EventType.MouseDown, first); Pointer(canvas, EventType.MouseDrag, last); Pointer(canvas, EventType.MouseUp, last);
        t.Equal(new Vector2Int(50, 90), Frame(window).min, "A crop corner snaps to a guide intersection");
        SetFrame(window, new RectInt(50, 65, 120, 80)); first = View(canvas, document, new Vector2(50, 105));
        Pointer(canvas, EventType.MouseDown, first);
        t.True(Dragging(window), "Crop handles capture instead of moving a coincident guide");
        t.True(!(bool)Read(window, "canvasGuideManipulator").GetType().GetProperty("IsDragging", Flags).GetValue(Read(window, "canvasGuideManipulator")),
            "Existing guide stays idle with Crop active");
        Key(canvas, KeyCode.Escape); Pointer(canvas, EventType.MouseUp, first);
        var applied = new RectInt(-11, 23, 200, 120); SetFrame(window, applied);
        await Tick(root, token);
        string preview = System.IO.Path.GetFullPath("Temp/WhimTex/crop-tool-preview.png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preview));
        await WhimTex.Tests.UnityA.UnityACapture.Capture(window, preview, token);
        Key(canvas, KeyCode.Return); await Tick(root, token);
        t.Equal(new Vector2Int(200, 120), new Vector2Int(document.width, document.height), "Enter applies the new document dimensions");
        t.Equal(new RectInt(0, 0, 200, 120), Frame(window), "Applied frame returns to the new canvas bounds");
        t.Equal(2, document.layers.Count, "Apply keeps editable layers");
        selection = Call(window, "GetAreaSelection");
        byte[] shifted = (byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection);
        t.Equal((byte)1, shifted[7 * 200 + 31], "Apply shifts faint selection coverage without discarding it");
        t.Equal((byte)255, shifted[17 * 200 + 41], "Apply shifts opaque selection coverage");
        t.Near(61, (float)Read(guides[0], "position"), .0001, "Apply shifts the vertical guide with the canvas origin");
        t.Near(53, (float)Read(guides[1], "position"), .0001, "Apply shifts the horizontal guide with the top-left origin");

        guides.Add(Guide(new Vector2(.6f, .8f), 75));
        guides.Add(Guide(new Vector2(-.8f, .6f), -17));
        Call(window, "RefreshCanvasGuides");
        object[] SelectionUndoSnapshot(object state) => new object[] { Read(state, "document"), Read(state, "width"), Read(state, "height"),
            Read(state, "coverage") is byte[] values ? values.Clone() : null, Read(state, "revision") };
        void RestoreSelectionUndoSnapshot(object state, object[] values)
        {
            Write(state, "document", values[0]); Write(state, "width", values[1]); Write(state, "height", values[2]);
            Write(state, "coverage", values[3] is byte[] bytes ? bytes.Clone() : null); Write(state, "revision", values[4]);
        }
        var pending = new RectInt(-9, 14, 70, 50); SetFrame(window, pending);
        for (int action = 0; action < OrientationButtons.Length; action++)
        {
            pixelScale.value = 1.75f;
            var size = new Vector2Int(document.width, document.height);
            var nextSize = action < 2 ? size : new Vector2Int(size.y, size.x);
            RectInt expectedFrame = OrientedFrame(Frame(window), size, action);
            var samples = new Vector2[document.layers.Count];
            for (int index = 0; index < document.layers.Count; index++)
            {
                var currentTransform = (TextureTransform)Call(document, "GetCanvasTransform", document.layers[index]);
                samples[index] = Vector2.Scale(currentTransform.Map(new Vector2(.2f, .35f), new Vector2(size.x, size.y)), size);
            }
            selection = Call(window, "GetAreaSelection");
            byte[] beforeCoverage = ((byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection)).Clone() as byte[];
            guides = (System.Collections.IList)Read(window, "canvasGuides");
            var pointsOnGuides = new Vector2[guides.Count];
            for (int index = 0; index < guides.Count; index++)
            {
                Vector2 normal = (Vector2)Read(guides[index], "normal");
                Vector2 top = normal * ((float)Read(guides[index], "position") / normal.sqrMagnitude);
                pointsOnGuides[index] = OrientedPoint(new Vector2(top.x, size.y - top.y), size, action);
            }
            Button button = root.Q<Button>(OrientationButtons[action]);
            object selectionUndoState = Read(window, "canvasChangeSelectionUndo");
            object[] undoBefore = action == 0 ? SelectionUndoSnapshot(selectionUndoState) : null;
            t.True(button.enabledSelf, "Idle document orientation action is enabled: " + button.name);
            PublicInput.Click(button); await Tick(root, token);
            t.Equal(nextSize, new Vector2Int(document.width, document.height), "Whole-document orientation sets expected dimensions: " + action);
            t.Equal(expectedFrame, Frame(window), "Whole-document orientation carries the pending crop frame: " + action);
            t.Near(1.75, pixelScale.value, 0, "Orientation retains the pending manual multiplier: " + action);
            t.Near(1, document.pixelScale, 0, "Orientation leaves pending pixel sizes unapplied: " + action);
            pixelScale.value = 1;
            t.Equal("Crop", Read(window, "canvasTool").ToString(), "Orientation keeps Crop active");
            t.Equal(gradient.Id, (string)Read(window, "selectedLayerId"), "Document action preserves the active layer");
            for (int index = 0; index < document.layers.Count; index++)
            {
                var changed = (TextureTransform)Call(document, "GetCanvasTransform", document.layers[index]);
                Vector2 actual = Vector2.Scale(changed.Map(new Vector2(.2f, .35f), new Vector2(nextSize.x, nextSize.y)), nextSize);
                Vector2 expected = OrientedPoint(samples[index], size, action);
                t.Near(expected.x, actual.x, .001, "Orientation changes every layer, regardless of selection X: " + action + "/" + index);
                t.Near(expected.y, actual.y, .001, "Orientation changes every layer, regardless of selection Y: " + action + "/" + index);
            }
            selection = Call(window, "GetAreaSelection");
            byte[] afterCoverage = (byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection);
            t.Equal(beforeCoverage.Length, afterCoverage.Length, "Orientation retains mask pixel count");
            bool sameCoverage = true;
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int to = OrientedPixel(new Vector2Int(x, y), size, action);
                if (beforeCoverage[y * size.x + x] != afterCoverage[to.y * nextSize.x + to.x]) sameCoverage = false;
            }
            t.True(sameCoverage, "Selection coverage follows whole-document orientation exactly: " + action);
            guides = (System.Collections.IList)Read(window, "canvasGuides");
            t.Equal(pointsOnGuides.Length, guides.Count, "Orientation preserves the guide count");
            for (int index = 0; index < guides.Count; index++)
            {
                Vector2 top = new Vector2(pointsOnGuides[index].x, nextSize.y - pointsOnGuides[index].y);
                t.Near((float)Read(guides[index], "position"), Vector2.Dot((Vector2)Read(guides[index], "normal"), top), .001,
                    "Guides remain attached to the oriented image: " + action + "/" + index);
            }
            if (action == 0)
            {
                object[] undoAfter = SelectionUndoSnapshot(selectionUndoState);
                foreach (object[] state in new[] { undoBefore, undoAfter })
                {
                    var oldTexture = (Texture)Call(window, "GetAreaSelectionTexture");
                    int guideRevision = (int)Read(window, "canvasGuidesRevision");
                    RestoreSelectionUndoSnapshot(selectionUndoState, state);
                    Call(window, "OnUndoRedo");
                    selection = Call(window, "GetAreaSelection");
                    byte[] restored = (byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection);
                    t.True(((byte[])state[3]).SequenceEqual(restored), "Same-size history restores exact selection coverage from its owned holder");
                    t.Equal(nextSize, new Vector2Int(document.width, document.height), "Selection playback does not mutate model dimensions");
                    t.True(oldTexture == null, "History invalidation destroys the stale selection texture");
                    t.True((int)Read(window, "canvasGuidesRevision") > guideRevision, "History playback invalidates guide interactions");
                }
                SetFrame(window, expectedFrame);
            }
        }
        SetFrame(window, new RectInt(-16380, 14, 70, 50));
        var distantSize = new Vector2Int(document.width, document.height);
        RectInt distantFrame = OrientedFrame(Frame(window), distantSize, 0);
        PublicInput.Click(root.Q<Button>("canvasCropFlipHorizontal")); await Tick(root, token);
        t.Equal(distantFrame, Frame(window), "Orientation preserves a remote expansion frame without clamping its mapped origin");

        SetFrame(window, original);
        first = View(canvas, document, Grip(original, 4));
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control);
        SelectMode("Scale"); await Tick(root, token);
        t.True(!Dragging(window) && !canvas.HasPointerCapture(PointerId.mousePointerId), "Changing modes cancels a Resize gesture");
        t.Equal(new RectInt(0, 0, document.width, document.height), Frame(window), "Changing modes resets the pending frame");
        t.True(root.Q<IntegerField>("cropX").resolvedStyle.display == DisplayStyle.None && root.Q<IntegerField>("cropY").resolvedStyle.display == DisplayStyle.None,
            "Scale hides origin fields");
        Call(window, "ChangeCanvasZoom", true); await Tick(root, token);
        var scaleSize = new Vector2Int(document.width, document.height);
        Rect originalImage = (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas);
        string beforeScaleDraft = EditorJsonUtility.ToJson(document);
        first = View(canvas, document, new Vector2(scaleSize.x * .5f, scaleSize.y * .5f));
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control); Pointer(canvas, EventType.MouseUp, first, EventModifiers.Control);
        t.True(!Dragging(window), "Scale cannot move the frame by dragging its interior");
        first = View(canvas, document, new Vector2(-8, 20));
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control); Pointer(canvas, EventType.MouseUp, first, EventModifiers.Control);
        t.True(!Dragging(window), "Scale cannot draw an outside frame");
        t.Equal(new RectInt(0, 0, scaleSize.x, scaleSize.y), Frame(window), "Scale interior/outside clicks preserve the full frame");
        first = View(canvas, document, Vector2.zero); last = View(canvas, document, new Vector2(-60, -90));
        Pointer(canvas, EventType.MouseDown, first, EventModifiers.Control);
        Pointer(canvas, EventType.MouseDrag, last, EventModifiers.Control); Pointer(canvas, EventType.MouseUp, last, EventModifiers.Control);
        RectInt scaleDraft = Frame(window);
        t.Equal(new RectInt(-60, -90, scaleSize.x + 60, scaleSize.y + 90), scaleDraft, "Scale handles resize around the opposite preview anchor");
        t.Equal(beforeScaleDraft, EditorJsonUtility.ToJson(document), "Scale preview changes no model data");
        float expectedScalePixels = document.pixelScale * Mathf.Min(scaleDraft.width / (float)scaleSize.x, scaleDraft.height / (float)scaleSize.y);
        t.Near(expectedScalePixels, pixelScale.value, .000001, "Handle-based nonproportional Scale fits pixels to the smaller axis");
        t.Equal(originalImage, (Rect)canvas.GetType().GetProperty("ImageRect", Flags).GetValue(canvas), "Preview keeps the original input mapping stable");
        Rect displayed = (Rect)canvas.GetType().GetProperty("DisplayedImageRect", Flags).GetValue(canvas);
        t.Near(originalImage.x + scaleDraft.x / (double)scaleSize.x * originalImage.width, displayed.x, .001, "Scale preview moves its visible left edge");
        t.Near(originalImage.yMax - scaleDraft.yMax / (double)scaleSize.y * originalImage.height, displayed.y, .001, "Scale preview follows its top edge");
        t.Near(originalImage.width * scaleDraft.width / (double)scaleSize.x, displayed.width, .001, "Scale preview visibly stretches image width");
        t.Near(originalImage.height * scaleDraft.height / (double)scaleSize.y, displayed.height, .001, "Scale preview visibly stretches image height");
        selection = Call(window, "GetAreaSelection");
        byte[] scaleCoverage = ((byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection)).Clone() as byte[];
        var scaleSamples = new Vector2[document.layers.Count];
        for (int index = 0; index < document.layers.Count; index++)
        {
            var currentTransform = (TextureTransform)Call(document, "GetCanvasTransform", document.layers[index]);
            scaleSamples[index] = Vector2.Scale(currentTransform.Map(new Vector2(.2f, .35f), scaleSize), scaleSize);
        }
        guides = (System.Collections.IList)Read(window, "canvasGuides");
        var scaleGuidePoints = new Vector2[guides.Count];
        Vector2 stretch = new Vector2(scaleDraft.width / (float)scaleSize.x, scaleDraft.height / (float)scaleSize.y);
        for (int index = 0; index < guides.Count; index++)
        {
            Vector2 normal = (Vector2)Read(guides[index], "normal");
            scaleGuidePoints[index] = Vector2.Scale(normal * ((float)Read(guides[index], "position") / normal.sqrMagnitude), stretch);
        }
        await WhimTex.Tests.UnityA.UnityACapture.Capture(window, System.IO.Path.GetFullPath("Temp/WhimTex/crop-scale-preview.png"), token);
        PublicInput.Click(root.Q<Button>("cropApply")); await Tick(root, token);
        var scaledSize = new Vector2Int(scaleDraft.width, scaleDraft.height);
        t.Equal(scaledSize, new Vector2Int(document.width, document.height), "Scale applies the preview width and height");
        t.Near(expectedScalePixels, document.pixelScale, .000001, "Apply commits fitted pixel sizes with the new dimensions");
        t.Near(expectedScalePixels, pixelScale.value, .000001, "The next Scale draft uses the newly committed multiplier");
        t.Equal(new RectInt(0, 0, scaledSize.x, scaledSize.y), Frame(window), "Applied Scale resets its frame to the whole new image");
        t.Equal(2, document.layers.Count, "Scale keeps all editable layers");
        for (int index = 0; index < document.layers.Count; index++)
        {
            var changed = (TextureTransform)Call(document, "GetCanvasTransform", document.layers[index]);
            Vector2 actual = Vector2.Scale(changed.Map(new Vector2(.2f, .35f), scaledSize), scaledSize);
            Vector2 expected = Vector2.Scale(scaleSamples[index], stretch);
            t.Near(expected.x, actual.x, .001, "Scale stretches every layer without cropping preview-origin X: " + index);
            t.Near(expected.y, actual.y, .001, "Scale stretches every layer without cropping preview-origin Y: " + index);
        }
        selection = Call(window, "GetAreaSelection");
        byte[] scaledCoverage = (byte[])selection.GetType().GetProperty("Coverage", Flags).GetValue(selection);
        bool nearestMatches = true;
        for (int y = 0; y < scaledSize.y; y++)
        for (int x = 0; x < scaledSize.x; x++)
        {
            int sourceX = (int)((2L * x + 1) * scaleSize.x / (2L * scaledSize.x));
            int sourceY = (int)((2L * y + 1) * scaleSize.y / (2L * scaledSize.y));
            if (scaledCoverage[y * scaledSize.x + x] != scaleCoverage[sourceY * scaleSize.x + sourceX]) nearestMatches = false;
        }
        t.True(nearestMatches, "Scale resamples every selection byte with center-nearest coverage");
        guides = (System.Collections.IList)Read(window, "canvasGuides");
        for (int index = 0; index < guides.Count; index++)
        {
            Vector2 normal = (Vector2)Read(guides[index], "normal");
            t.Near(1, normal.magnitude, .0001, "Anisotropically scaled guide normal is normalized");
            t.Near((float)Read(guides[index], "position"), Vector2.Dot(normal, scaleGuidePoints[index]), .001, "Scaled guide remains on its scaled image location");
        }
        SelectMode("Resize"); await Tick(root, token);
        t.True(root.Q<IntegerField>("cropX").resolvedStyle.display != DisplayStyle.None && root.Q<IntegerField>("cropY").resolvedStyle.display != DisplayStyle.None,
            "Returning to Resize restores the origin fields");

        pixelScale.value = 2.25f;
        t.Near(expectedScalePixels, document.pixelScale, .000001, "Manual multiplier edits remain pending after an earlier Apply");
        PublicInput.Click(root.Q<Button>("cropApply")); await Tick(root, token);
        t.Equal(scaledSize, new Vector2Int(document.width, document.height), "Multiplier-only Apply keeps canvas dimensions");
        t.Near(2.25, document.pixelScale, 0, "Multiplier-only Apply commits the exact entered value");
        t.True(!root.Q<Button>("cropApply").enabledSelf && !root.Q<Button>("cropReset").enabledSelf, "Applied multiplier is the new unchanged baseline");
        SelectMode("Scale"); SetFrame(window, new RectInt(0, 0, scaledSize.x * 2, scaledSize.y * 3));
        t.Near(4.5, pixelScale.value, .000001, "Later automatic Scale multiplies the committed multiplier by the new minimum factor");
        PublicInput.Click(root.Q<Button>("cropReset"));
        t.Equal(new RectInt(0, 0, scaledSize.x, scaledSize.y), Frame(window), "Reset after Apply restores the applied canvas size");
        t.Near(2.25, pixelScale.value, 0, "Reset after Apply retains the applied Pixel Scale rather than default 1");
        t.Near(2.25, document.pixelScale, 0, "Reset changes no committed pixel data");
        SelectMode("Resize"); pixelScale.value = 1.25f;
        PublicInput.Click(root.Q<Button>("cropReset"));
        t.Near(2.25, pixelScale.value, 0, "Reset also discards Resize's later manual multiplier draft");

        other = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        other.hideFlags = HideFlags.HideAndDontSave; other.width = 80; other.height = 60;
        SetFrame(window, original); Write(window, "activeDocument", other); Call(window, "RefreshToolkitInterface", true);
        t.Equal(new RectInt(0, 0, 80, 60), Frame(window), "Switching documents resets the draft");
        t.Near(1, root.Q<FloatField>("cropPixelScale").value, 0, "Switching documents uses that document's own Pixel Scale");
        t.True(!root.Q<Button>("cropTool").ClassListContains("whimtex-tool-button--unavailable"), "Crop remains available on an empty document");
        other.width = 90; Call(window, "RefreshCanvasCropTool");
        t.Equal(new RectInt(0, 0, 90, 60), Frame(window), "External canvas resizing invalidates the draft");
        PublicInput.Click(root.Q<Button>("canvasCropRotateLeft")); await Tick(root, token);
        t.Equal(new Vector2Int(60, 90), new Vector2Int(other.width, other.height), "Quarter-turn actions also work on an empty document");
        t.Equal(new RectInt(0, 0, 60, 90), Frame(window), "Empty document orientation carries the full-canvas frame");
    }
}
