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
    sealed class IconCapture : ImmediateModeElement
    {
        internal readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        internal WhimTexWindow owner;
        internal string path = "Temp/WhimTex/gradient-picker-preview.png";
        internal Action<Texture2D> inspect;
        protected override void ImmediateRepaint()
        {
            if (completion.Task.IsCompleted) return;
            Texture2D image = null;
            try
            {
                var rt = RenderTexture.active;
                float scale = EditorGUIUtility.pixelsPerPoint;
                int width = rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * scale);
                int height = rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * scale);
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                inspect?.Invoke(image);
                completion.TrySetResult(true);
            }
            catch (Exception e) { completion.TrySetException(e); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
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
        async Task Capture(string path, Action<Texture2D> inspect = null)
        {
            var capture = new IconCapture { owner = window, path = path, inspect = inspect, pickingMode = PickingMode.Ignore };
            capture.style.position = Position.Absolute; capture.style.width = 1; capture.style.height = 1;
            root.Add(capture);
            try
            {
                window.Repaint();
                using (token.Register(() => capture.completion.TrySetCanceled())) await capture.completion.Task;
            }
            finally { capture.RemoveFromHierarchy(); }
        }
        for (int i = 0; i < 4; i++) await Tick(root, token);
        var canvas = (VisualElement)Read(window, "toolkitCanvas");
        void AssertSquare(TextureTransform transform, Vector2 dimensions, string label)
        {
            Vector2 corner = Vector2.Scale(transform.Map(Vector2.zero, dimensions), dimensions);
            Vector2 x = Vector2.Scale(transform.Map(Vector2.right, dimensions), dimensions) - corner;
            Vector2 y = Vector2.Scale(transform.Map(Vector2.up, dimensions), dimensions) - corner;
            t.Near(x.magnitude, y.magnitude, .001, label + " has equal pixel side lengths");
            t.Near(0, Vector2.Dot(x.normalized, y.normalized), .00001, label + " has perpendicular sides");
        }
        var toolType = Read(window, "canvasTool").GetType();
        Call(window, "SetCanvasTool", Enum.Parse(toolType, "Gradient"));
        await Tick(root, token);
        var button = root.Q<Button>("gradientTool");
        t.True(button != null && button.parent.IndexOf(button) == button.parent.IndexOf(root.Q<Button>("fillTool")) + 1, "Gradient is directly after Fill");
        t.True(root.Q<WhimTexGradientValueField>("gradientToolRamp") != null, "Editable ramp in common header");
        Vector2 start = canvas.contentRect.center - new Vector2(90, 30), end = start + new Vector2(180, -45);
        Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseUp, start);
        t.Equal(0, document.layers.Count, "A click does not create a degenerate layer");
        var type = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientToolType");
        t.True(type != null && type.Q<EnumField>() == null, "Tool header uses icons instead of a dropdown");
        t.Equal(5, type.Query<Button>().ToList().Count, "Header offers all five gradient types");
        async Task OpenPicker(bool hold = false)
        {
            Rect previous = default;
            int stable = 0;
            for (int i = 0; i < 30 && stable < 4; i++)
            {
                window.Repaint(); await Tick(root, token);
                Rect bounds = button.worldBound;
                stable = bounds == previous && bounds.width > 0 && bounds.height > 0 ? stable + 1 : 0;
                previous = bounds;
            }
            t.True(stable >= 4, "Toolbar layout settles before opening the subtype picker");
            Pointer(button, EventType.MouseDown, button.contentRect.center);
            if (hold) for (int i = 0; i < 20 && root.Q("gradientPicker") == null; i++) await Tick(root, token);
            else { Pointer(button, EventType.MouseDrag, button.contentRect.center + new Vector2(0, 4)); await Tick(root, token); }
            t.True(root.Q("gradientPicker") != null, "Hold or drag opens the gradient picker");
            t.Equal(5, root.Q("gradientPicker").childCount, "Five gradient creation subtypes");
            for (int i = 0; i < 20 && !(root.Q("gradientFreeform")?.worldBound.height >= 20); i++)
            { window.Repaint(); await Tick(root, token); }
            t.True(root.Q("gradientFreeform").worldBound.height >= 20, "Picker layout is ready before pointer targeting");
        }
        async Task Pick(string kind)
        {
            await OpenPicker(kind == "Linear");
            var menu = root.Q("gradientPicker");
            t.True(menu.Q("gradient" + type.value).ClassListContains("whimtex-shape-picker-item--selected"), "Menu marks the current header type");
            if (kind == "Linear")
            {
                var capture = new IconCapture { owner = window, pickingMode = PickingMode.Ignore };
                capture.style.position = Position.Absolute; capture.style.width = 1; capture.style.height = 1;
                root.Add(capture);
                try
                {
                    window.Repaint();
                    using (token.Register(() => capture.completion.TrySetCanceled())) await capture.completion.Task;
                }
                finally { capture.RemoveFromHierarchy(); }
            }
            Vector2 choice = button.WorldToLocal(menu.Q("gradient" + kind).worldBound.center);
            Pointer(button, EventType.MouseDrag, choice); Pointer(button, EventType.MouseUp, choice);
            await Tick(root, token);
            t.Equal(kind, type.value.ToString(), "Picker synchronizes the header type");
            int selected = 0;
            type.Query<Button>().ForEach(choice =>
            {
                if (choice.ClassListContains("whimtex-tool-button--selected"))
                { selected++; t.Equal("gradientType" + kind, choice.name, "Header marks the picker-selected type"); }
            });
            t.Equal(1, selected, "Exactly one header icon is selected");
            t.Equal(kind, Read(Read(window, "gradientCreationToolIcon"), "gradientType").ToString(), "Toolbar icon shows the selected gradient type");
            t.True(button.tooltip.StartsWith("Gradient: " + kind), "Tooltip names the selected type");
            t.Equal("Gradient", Read(window, "canvasTool").ToString(), "Subtypes share the one Gradient tool");
            Call(window, "UpdateCanvasCursor", canvas.contentRect.center, false);
            var cursor = canvas.style.cursor.value;
            string cursorName = kind == "Linear" ? "GradientCursor" : "Gradient" + kind + "Cursor";
            t.True(cursor.texture != null && cursor.texture.name == cursorName, "Canvas uses the selected gradient cursor " + kind);
            t.True(cursor.hotspot == Vector2.zero && cursor.texture.width == 32 && cursor.texture.height == 32,
                "Gradient cursors share their dimensions and exact pointer hotspot");
            t.True(cursor.texture.isReadable && cursor.texture.mipmapCount == 1 && cursor.texture.filterMode == FilterMode.Point,
                "Cursor stays readable and pixel crisp");
            Call(window, "UpdateCanvasCursor", canvas.contentRect.center, true);
            t.True(canvas.style.cursor.value.texture == null, "Alt temporarily releases the subtype cursor");
            Call(window, "UpdateCanvasCursor", canvas.contentRect.center, false);
            t.True(root.Q("gradientPicker") == null && !button.HasPointerCapture(PointerId.mousePointerId), "Choosing closes the picker and releases capture");
        }
        var originalPalette = root.Q<WhimTexGradientValueField>("gradientToolRamp").value.Clone();
        foreach (string kind in new[] { "Linear", "Radial", "Angular", "Square", "Freeform" })
        {
            await Pick(kind);
            if (kind == "Freeform")
            {
                var palette = new WhimTexGradient();
                palette.SetKeys(new[] { new GradientColorKey(new Color(2, -.2f, .3f), 0),
                    new GradientColorKey(new Color(.1f, 1.4f, .6f), .5f), new GradientColorKey(new Color(.4f, .2f, 3), 1) },
                    new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(.9f, 1) });
                root.Q<WhimTexGradientValueField>("gradientToolRamp").value = palette;
            }
            int count = document.layers.Count;
            Vector2 size = new Vector2(document.width, document.height);
            Vector2 expectedStart = DocumentPoint(canvas, size, start), expectedEnd = DocumentPoint(canvas, size, end);
            Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseDrag, end);
            if (kind == "Freeform")
            {
                await Tick(root, token);
                t.Equal(count, document.layers.Count, "Color preview does not insert a layer before release");
                Vector2 drag = end - start;
                Vector2 apexView = (start + end) * .5f + new Vector2(drag.y, -drag.x) * .8660254f;
                await Capture("Temp/WhimTex/freeform-creation-colors-preview.png", image =>
                {
                    var positions = new[] { start, apexView, end };
                    float scale = EditorGUIUtility.pixelsPerPoint;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector2 pixel = canvas.LocalToWorld(positions[i]) * scale;
                        Color actual = image.GetPixel(Mathf.RoundToInt(pixel.x), image.height - 1 - Mathf.RoundToInt(pixel.y));
                        t.True(actual[i] > .6f && actual[i] > actual[(i + 1) % 3] + .2f && actual[i] > actual[(i + 2) % 3] + .2f,
                            "Creation preview shows the actual red/green/blue point " + i + ": " + actual);
                    }
                });
            }
            Pointer(canvas, EventType.MouseUp, end);
            t.Equal(count + 1, document.layers.Count, kind + " creates one layer");
            t.Equal(kind == "Freeform" ? "GradientHandles" : "Gradient", Read(window, "canvasTool").ToString(), "Freeform enters point editing; other types keep creation active");
            var behaviour = document.layers[0].Behaviour as GradientLayerBehaviour;
            string expectedType = kind;
            t.Equal(expectedType, behaviour.gradientType.ToString(), "Creation uses the picked type");
            t.True(behaviour != null && behaviour.gradient.Mode == WhimTexGradientMode.Perceptual, "Current gradient model/default interpolation");
            t.True(!ReferenceEquals(behaviour.gradient, root.Q<WhimTexGradientValueField>("gradientToolRamp").value), "Each new layer owns a ramp copy");
            t.Equal(TransformTilingMode.Unbounded, document.layers[0].EffectiveTiling, "Gradient extends beyond its handles");
            var transform = document.layers[0].transform;
            AssertSquare(transform, size, kind);
            if (kind == "Angular")
            {
                t.Near(Mathf.Min(size.x, size.y) / size.x, transform.scale.x, .00001, "Angular uses the canvas unit square, X");
                t.Near(Mathf.Min(size.x, size.y) / size.y, transform.scale.y, .00001, "Angular uses the canvas unit square, Y");
            }
            Vector2 actualStart = Vector2.Scale(transform.Map(kind == "Freeform" ? behaviour.freeform.points[0].position : kind == "Linear" ? new Vector2(0, .5f) : Vector2.one * .5f, size), size);
            t.Near(expectedStart.x, actualStart.x, .001, kind + " starts at the drag position, X");
            t.Near(expectedStart.y, actualStart.y, .001, kind + " starts at the drag position, Y");
            if (kind != "Angular")
            {
                Vector2 actualEnd = Vector2.Scale(transform.Map(kind == "Freeform" ? behaviour.freeform.points[2].position : new Vector2(1, .5f), size), size);
                t.Near(expectedEnd.x, actualEnd.x, .001, kind + " ends at the drag position, X");
                t.Near(expectedEnd.y, actualEnd.y, .001, kind + " ends at the drag position, Y");
            }
            if (kind == "Freeform")
            {
                t.Equal(3, behaviour.freeform.points.Count, "Freeform creates exactly three points");
                t.Equal(LayerColorRange.HDR, document.layers[0].colorRange, "Creation preserves HDR range");
                var saved = behaviour.freeform.Clone();
                for (int i = 0; i < 3; i++)
                {
                    Color expected = behaviour.gradient.Evaluate(i * .5f), actual = saved.points[i].color;
                    for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .000001, "Palette color/alpha " + i + "/" + c);
                    t.Near(.35, saved.points[i].influence, .000001, "Initial influence is editable");
                }
                Vector2 apex = Vector2.Scale(transform.Map(saved.points[1].position, size), size);
                t.Near(Vector2.Distance(expectedStart, expectedEnd), Vector2.Distance(expectedStart, apex), .001, "Initial points form an equilateral triangle");
                var manipulator = Read(window, "gradientCanvasManipulator");
                Vector2 first = (Vector2)Call(manipulator, "FreeformView", saved.points[0].position);
                t.Equal(MouseCursor.Pan, (MouseCursor)Call(manipulator, "GetFreeformCursor", first), "Created point is immediately grabbable");
                Pointer(canvas, EventType.MouseDown, first); Pointer(canvas, EventType.MouseDrag, first + new Vector2(20, 10));
                Pointer(canvas, EventType.MouseUp, first + new Vector2(20, 10));
                t.True(behaviour.freeform.points[0].position != saved.points[0].position, "Immediately drag the created point");
                t.Equal(count + 1, document.layers.Count, "Editing does not create another layer");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                t.True(((GradientLayerBehaviour)document.layers[0].Behaviour).freeform.points[0].Equals(saved.points[0]), "Point edit has its own Undo");
                Undo.PerformUndo(); t.Equal(count, document.layers.Count, "One Undo removes Freeform creation");
                Undo.PerformRedo(); t.Equal(count + 1, document.layers.Count, "Redo restores Freeform");
                behaviour = (GradientLayerBehaviour)document.layers[0].Behaviour;
                for (int i = 0; i < 3; i++) t.True(saved.points[i].Equals(behaviour.freeform.points[i]), "Redo preserves complete point " + i);
            }
            await Tick(root, token);
        }
        Call(window, "SetCanvasTool", Enum.Parse(toolType, "Gradient"));
        root.Q<WhimTexGradientValueField>("gradientToolRamp").value = originalPalette;
        PublicInput.Click(type.Q<Button>("gradientTypeSquare"));
        t.Equal("Square", Read(Read(window, "gradientCreationToolIcon"), "gradientType").ToString(), "Header updates the toolbar icon");
        t.Equal("GradientSquareCursor", canvas.style.cursor.value.texture.name, "Header refreshes the cursor without pointer movement");
        await OpenPicker();
        using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        { e.target = root; root.SendEvent(e); }
        Pointer(button, EventType.MouseUp, button.contentRect.center);
        t.True(root.Q("gradientPicker") == null && !button.HasPointerCapture(PointerId.mousePointerId), "Escape closes the picker and releases capture");
        t.Equal("Square", type.value.ToString(), "Escape preserves the chosen type");
        await OpenPicker();
        Vector2 cancelledChoice = button.WorldToLocal(root.Q("gradientRadial").worldBound.center);
        Pointer(button, EventType.MouseDrag, cancelledChoice);
        button.ReleasePointer(PointerId.mousePointerId); await Tick(root, token);
        Pointer(button, EventType.MouseUp, cancelledChoice);
        t.True(root.Q("gradientPicker") == null, "Capture loss closes the picker");
        t.Equal("Square", type.value.ToString(), "Capture loss cannot select a hovered type");
        await OpenPicker(); Call(window, "OnLostFocus");
        Pointer(button, EventType.MouseUp, button.contentRect.center);
        t.True(root.Q("gradientPicker") == null, "Window focus loss cancels the picker");
        await OpenPicker(); Call(window, "CancelCanvasZoomGesture");
        Pointer(button, EventType.MouseUp, button.contentRect.center);
        t.True(root.Q("gradientPicker") == null, "Canvas navigation cancels the picker");
        Call(window, "SetCanvasTool", Enum.Parse(toolType, "Shape"));
        Pointer(button, EventType.MouseDown, button.contentRect.center); Pointer(button, EventType.MouseUp, button.contentRect.center);
        t.Equal("Gradient", Read(window, "canvasTool").ToString(), "Quick click activates Gradient");
        t.Equal("Square", type.value.ToString(), "Quick click retains the subtype");
        int before = document.layers.Count;
        Pointer(canvas, EventType.MouseDown, start); Pointer(canvas, EventType.MouseDrag, end);
        using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }))
        { e.target = canvas; canvas.SendEvent(e); }
        Pointer(canvas, EventType.MouseUp, end);
        t.Equal(before, document.layers.Count, "Escape cancels without inserting a layer");
        type.value = GradientLayerBehaviour.GradientType.Linear;
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
        ((GradientLayerBehaviour)last.Behaviour).gradientType = GradientLayerBehaviour.GradientType.Radial;
        var radial = scope.Own(document.ComposeCanvas());
        Vector2 center = (a + b) * .5f;
        float radius = Vector2.Distance(a, b) * .2f;
        Color RadialSample(Vector2 p) => radial.GetPixelBilinear(p.x / radial.width, p.y / radial.height);
        float horizontal = RadialSample(center + Vector2.right * radius).r;
        float vertical = RadialSample(center + Vector2.up * radius).r;
        t.True(horizontal > .1f && horizontal < .9f, "Radial check samples a visible transition");
        t.Near(horizontal, vertical, .015, "Switching created Linear to Radial renders a circle");
        document.width = 256; document.height = 512;
        Call(window, "RefreshToolkitInterface", true);
        await Tick(root, token);
        type = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientToolType");
        foreach (string kind in new[] { "Linear", "Radial", "Angular", "Square", "Freeform" })
        {
            type.value = (GradientLayerBehaviour.GradientType)Enum.Parse(typeof(GradientLayerBehaviour.GradientType), kind);
            Call(window, "CreateGradient", new Vector2(64, 180), new Vector2(120, 260), null);
            AssertSquare(document.layers[0].transform, new Vector2(256, 512), "Portrait " + kind);
        }
        type.value = GradientLayerBehaviour.GradientType.Angular;
        foreach (Vector2 size in new[] { new Vector2(512, 512), new Vector2(512, 256), new Vector2(256, 512) })
        {
            document.width = (int)size.x; document.height = (int)size.y;
            Vector2 origin = size * .4f, direction = new Vector2(3, 4).normalized;
            TextureTransform shortDrag = default;
            foreach (float length in new[] { 10f, 230f })
            {
                Call(window, "CreateGradient", origin, origin + direction * length, null);
                var transform = document.layers[0].transform;
                AssertSquare(transform, size, "Angular " + size + " drag " + length);
                t.Near(1, Math.Max(transform.scale.x, transform.scale.y), .00001, "Angular fits a unit square regardless of drag length");
                Vector2 actualCenter = Vector2.Scale(transform.Map(Vector2.one * .5f, size), size);
                t.Near(origin.x, actualCenter.x, .001, "Angular preserves the drawn center X");
                t.Near(origin.y, actualCenter.y, .001, "Angular preserves the drawn center Y");
                t.Near(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 180, transform.rotation, .0001, "Angular preserves the drawn direction");
                if (length == 10) shortDrag = transform;
                else
                {
                    t.True(shortDrag.scale == transform.scale, "Short and long Angular drags produce identical scale");
                    int count = document.layers.Count;
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                    t.Equal(count - 1, document.layers.Count, "Angular creation is one Undo");
                    Undo.PerformRedo();
                    t.True(document.layers[0].transform.Equals(transform), "Redo retains the normalized Angular transform");
                }
            }
        }
        Call(window, "SelectOnlyLayer", document.layers[0].Id);
        Call(window, "RefreshToolkitInterface", true);
        await Tick(root, token);
        var layerType = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientLayerType");
        t.True(layerType != null && layerType.Q<EnumField>() == null, "Gradient layer uses icons instead of a dropdown");
        var paletteBefore = ((GradientLayerBehaviour)document.layers[0].Behaviour).gradient.Clone();
        string pointsBefore = JsonUtility.ToJson(((GradientLayerBehaviour)document.layers[0].Behaviour).freeform);
        string headerBefore = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientToolType").value.ToString();
        foreach (var kind in new[] { GradientLayerBehaviour.GradientType.Linear, GradientLayerBehaviour.GradientType.Radial,
            GradientLayerBehaviour.GradientType.Angular, GradientLayerBehaviour.GradientType.Square, GradientLayerBehaviour.GradientType.Freeform })
        {
            layerType = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientLayerType");
            var choice = layerType.Q<Button>("gradientType" + kind);
            t.Equal(kind.ToString(), choice.tooltip, "Type name remains in its icon tooltip");
            Undo.IncrementCurrentGroup();
            PublicInput.Click(choice);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            await Tick(root, token);
            var model = (GradientLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(kind, model.gradientType, "Icon changes the layer type");
            layerType = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientLayerType");
            int selected = 0;
            layerType.Query<Button>().ForEach(button =>
            {
                if (button.ClassListContains("whimtex-tool-button--selected"))
                { selected++; t.Equal("gradientType" + kind, button.name, "Correct type icon is highlighted"); }
            });
            t.Equal(1, selected, "Exactly one selected gradient type");
            t.True(model.gradient.Equals(paletteBefore) && JsonUtility.ToJson(model.freeform) == pointsBefore, "Switching retains the palette and points");
        }
        type = root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientToolType");
        foreach (var kind in new[] { GradientLayerBehaviour.GradientType.Linear, GradientLayerBehaviour.GradientType.Radial,
            GradientLayerBehaviour.GradientType.Angular, GradientLayerBehaviour.GradientType.Square, GradientLayerBehaviour.GradientType.Freeform })
        {
            PublicInput.Click(type.Q<Button>("gradientType" + kind));
            await Tick(root, token);
            t.Equal(kind, type.value, "Header click selects subtype " + kind);
            t.Equal(kind.ToString(), Read(Read(window, "gradientCreationToolIcon"), "gradientType").ToString(), "Header click updates toolbar glyph");
            t.Equal(GradientLayerBehaviour.GradientType.Freeform, ((GradientLayerBehaviour)document.layers[0].Behaviour).gradientType, "Header does not change existing layer type");
            t.True(((GradientLayerBehaviour)document.layers[0].Behaviour).gradient.Equals(paletteBefore), "Header does not change layer palette");
        }
        PublicInput.Click(type.Q<Button>("gradientType" + headerBefore));
        await Tick(root, token);
        var headerFirst = type.Q<Button>("gradientTypeLinear");
        var headerLast = type.Q<Button>("gradientTypeFreeform");
        t.Near(headerFirst.worldBound.y, headerLast.worldBound.y, .01, "Header type icons share one row");
        t.Near(24, headerFirst.resolvedStyle.width, .01, "Header icon button matches action button width");
        t.Near(20, headerFirst.resolvedStyle.height, .01, "Header icon button matches action button height");
        t.True(headerLast.worldBound.xMax <= type.worldBound.xMax + .01f, "Header field contains the whole icon strip");
        t.True(type.worldBound.xMax <= root.Q<WhimTexGradientValueField>("gradientToolRamp").worldBound.xMin, "Icon strip does not overlap the gradient palette");
        await Capture("Temp/WhimTex/gradient-type-icons-preview.png");
        var firstButton = layerType.Q<Button>("gradientTypeLinear");
        var lastButton = layerType.Q<Button>("gradientTypeFreeform");
        t.Near(firstButton.worldBound.y, lastButton.worldBound.y, .01, "Type icons share one row");
        t.Near(24, firstButton.resolvedStyle.width, .01, "Compact icon width");
        t.Near(firstButton.resolvedStyle.width, lastButton.resolvedStyle.width, .01, "Equal type button widths");
        t.Equal(headerBefore, root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientToolType").value.ToString(), "Layer icons do not change the creation subtype");
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick(root, token);
        t.Equal(GradientLayerBehaviour.GradientType.Square, ((GradientLayerBehaviour)document.layers[0].Behaviour).gradientType, "One Undo restores previous type");
        t.True(root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientLayerType").Q<Button>("gradientTypeSquare").ClassListContains("whimtex-tool-button--selected"), "Undo updates the selected icon");
        Undo.PerformRedo(); await Tick(root, token);
        t.True(root.Q<BaseField<GradientLayerBehaviour.GradientType>>("gradientLayerType").Q<Button>("gradientTypeFreeform").ClassListContains("whimtex-tool-button--selected"), "Redo updates the selected icon");
    }
}
