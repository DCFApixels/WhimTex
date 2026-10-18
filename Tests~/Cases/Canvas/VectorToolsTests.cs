// Proposed Canvas catalog IDs (not registered by this file):
// vector-tools-geometry-v1 -> StartGeometry; vector-tools-cancel-v1 -> StartCancel;
// vector-tools-brush-v1 -> StartBrush; vector-tools-pen-v1 -> StartPen;
// vector-tools-stale-layer-v1 -> StartStaleLayer; vector-tools-focus-v1 -> StartFocus.
// All entries take one $runId; Poll/Cancel/Cleanup share the same GUID.
// Explicit support: Framework/TestApi.cs, Framework/Unity/MigrationD.cs,
// Framework/Unity/AsyncD.cs. Suggested timeout: 60000; effects: windows,user-state.
// Owned transient documents/windows/preview textures only. No assets, files,
// Unity-internal reflection. The opt-in StrokePreview writes its owned window to Temp.
// Package-private reflection selects
// tools and reads state; gestures use public UI Toolkit pointer/key events.
// These are functional UI/Undo checks, not visual goldens or reload verification.
using System;
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
using WhimTex.Tests.UnityD;

public static class VectorToolsTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Package = typeof(WhimTexWindow).Assembly;

    public static string StartGeometry(string id) => AsyncD.Start(id, (t, token) => Run(t, token, Geometry));
    public static string StartCancel(string id) => AsyncD.Start(id, (t, token) => Run(t, token, Cancellation));
    public static string StartBrush(string id) => AsyncD.Start(id, (t, token) => Run(t, token, Brush));
    public static string StartBrushLines(string id) => AsyncD.Start(id, (t, token) => Run(t, token, BrushLines));
    public static string StartBrushEditing(string id) => AsyncD.Start(id, (t, token) => Run(t, token, BrushEditing));
    public static string StartPen(string id) => AsyncD.Start(id, (t, token) => Run(t, token, Pen));
    public static string StartStaleLayer(string id) => AsyncD.Start(id, (t, token) => Run(t, token, StaleLayer));
    public static string StartFocus(string id) => AsyncD.Start(id, (t, token) => Run(t, token, FocusLoss));
    public static string StartStrokeStyle(string id) => AsyncD.Start(id, (t, token) => Run(t, token, StrokeStyle));
    public static string StartStrokePreview(string id) => AsyncD.Start(id, (t, token) => Run(t, token, StrokePreview));
    public static string StartDashes(string id) => AsyncD.Start(id, (t, token) => Run(t, token, Dashes));
    public static string StartDashPreview(string id) => AsyncD.Start(id, (t, token) => Run(t, token, DashPreview));
    public static string StartShapeStrokes(string id) => AsyncD.Start(id, (t, token) => Run(t, token, ShapeStrokes));
    public static string Poll(string id) => AsyncD.Poll(id);
    public static Task<string> Cancel(string id) => AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncD.Cleanup(id);

    static async Task Run(TestContext test, CancellationToken token, Func<Fixture, Task> body)
    {
        string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
        test.True(string.Equals(project, System.IO.Path.GetFullPath("D:/DCFA/Projects/Test6.6"), StringComparison.OrdinalIgnoreCase),
            "Vector UI tests target only the assigned project");
        var fixture = new Fixture(test, token);
        try { await fixture.Open(); await body(fixture); }
        finally { fixture.Dispose(); }
    }

    static object Read(object owner, string name)
    {
        FieldInfo field = owner.GetType().GetField(name, Instance);
        if (field == null || field.DeclaringType.Assembly != Package) throw new InvalidOperationException("Missing package field: " + name);
        return field.GetValue(owner);
    }

    static object Property(object owner, string name)
    {
        PropertyInfo property = owner.GetType().GetProperty(name, Instance);
        if (property == null || property.DeclaringType.Assembly != Package) throw new InvalidOperationException("Missing package property: " + name);
        return property.GetValue(owner);
    }

    static void Write(object owner, string name, object value)
    {
        FieldInfo field = owner.GetType().GetField(name, Instance);
        if (field == null || field.DeclaringType.Assembly != Package) throw new InvalidOperationException("Missing package field: " + name);
        field.SetValue(owner, value);
    }

    static object Call(object owner, string name, params object[] args)
    {
        MethodInfo method = Array.Find(owner.GetType().GetMethods(Instance), m => m.Name == name && m.GetParameters().Length == args.Length);
        if (method == null || method.DeclaringType.Assembly != Package) throw new InvalidOperationException("Missing package method: " + name);
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    sealed class Fixture : IDisposable
    {
        public readonly TestContext t;
        public readonly CancellationToken token;
        public WhimTexDocument document;
        public WhimTexWindow window;
        public VectorToolsFocusWindow focusWindow;
        public VisualElement canvas;
        public string layerId;
        public Layer detachedLayer;
        readonly List<Action> restorePreferences = new List<Action>();
        public Layer Layer => (Layer)Call(document, "FindLayer", layerId);
        public VectorLayerBehaviour Vector => (VectorLayerBehaviour)Layer.Behaviour;
        public object Manipulator => Read(window, "vectorManipulator");
        public Vector2 Size => new Vector2(document.width, document.height);

        public Fixture(TestContext t, CancellationToken token)
        {
            this.t = t; this.token = token;
            foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool",
                "DCFApixels.WhimTex.Canvas.PaintToolSettings" })
            {
                bool existed = EditorPrefs.HasKey(key); string value = EditorPrefs.GetString(key);
                restorePreferences.Add(() => { if (existed) EditorPrefs.SetString(key, value); else EditorPrefs.DeleteKey(key); });
            }
            const string scaleKey = "DCFApixels.WhimTex.Canvas.PaintingScale";
            bool hadScale = EditorPrefs.HasKey(scaleKey); float oldScale = EditorPrefs.GetFloat(scaleKey);
            restorePreferences.Add(() => { if (hadScale) EditorPrefs.SetFloat(scaleKey, oldScale); else EditorPrefs.DeleteKey(scaleKey); });
            const string dashKey = "DCFApixels.WhimTex.Vector.DashSegmentsExpanded";
            bool hadDash = EditorPrefs.HasKey(dashKey), oldDash = EditorPrefs.GetBool(dashKey);
            restorePreferences.Add(() => { if (hadDash) EditorPrefs.SetBool(dashKey, oldDash); else EditorPrefs.DeleteKey(dashKey); });
        }

        public async Task Open()
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.name = "Vector tools " + Guid.NewGuid().ToString("N");
            document.width = 256; document.height = 192;
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            // SetDocument also releases the automatically created owned empty document.
            Call(window, "SetDocument", document);
            window.ShowUtility(); window.position = new Rect(80, 80, 1280, 820); window.CreateGUI();
            for (int i = 0; i < 5; i++) await Tick();
            canvas = (VisualElement)Read(window, "toolkitCanvas");
            // An empty document has no render texture, so the window navigation commands
            // are disabled. Set the owned viewport explicitly before its first stroke.
            Call(canvas, "ZoomAt", canvas.contentRect.center, 1f);
            Call(canvas, "SetViewRotation", 0f, false);
            await Tool("None");
            window.Focus();
            for (int i = 0; i < 4; i++) await Tick();
            t.True(EditorWindow.focusedWindow == window, "Owned test window received focus before gestures");
            t.True(canvas.panel != null && canvas.contentRect.width > 300 && canvas.contentRect.height > 220,
                "Owned attached canvas has usable layout");
            foreach (string name in new[] { "vectorNodesTool", "vectorPenTool", "vectorBrushTool" })
                t.True(window.rootVisualElement.Q<Button>(name) != null, name + " is available");
        }

        public Task Tick() => AsyncD.Tick(window.rootVisualElement, token);
        public async Task Tool(string name)
        {
            Call(window, "SetCanvasTool", Enum.Parse(Read(window, "canvasTool").GetType(), name));
            await Tick();
            canvas?.Focus();
        }

        public async Task Seed(bool draft = false)
        {
            var vector = new VectorLayerBehaviour { referenceSize = Size };
            VectorShape shape;
            if (draft)
            {
                var builder = new VectorBrushBuilder(); builder.Begin(128, 96);
                builder.AddPoint(new Vector2(45, 48), 13);
                vector.brushDraft = builder.Finish();
                shape = VectorBrushGeometry.Rebuild(vector.brushDraft, Size);
            }
            else
            {
                shape = new VectorShape { name = "Rectangle", fillColor = new Color(.2f, .5f, .8f, 1) };
                var contour = new VectorContour();
                foreach (Vector2 point in new[] { new Vector2(.25f, .3f), new Vector2(.6f, .3f), new Vector2(.6f, .65f), new Vector2(.25f, .65f) })
                    contour.points.Add(new VectorPoint(point));
                shape.contours.Add(contour);
            }
            vector.shapes.Add(shape);
            var layer = new Layer(vector); Call(layer, "AssignNewId");
            var transform = layer.transform;
            transform.rotation = 9; transform.scale = new Vector2(.86f, 1.04f); layer.transform = transform;
            document.layers.Add(layer); layerId = layer.Id;
            Call(document, "NormalizeModel"); Call(window, "SelectOnlyLayer", layerId);
            Call(window, "SelectVectorShape", shape);
            Call(window, "RefreshToolkitInterface", true); Call(window, "RequestCanvasRender", true);
            await Tick(); await Tick();
            Undo.ClearUndo(document);
        }

        public Vector2 View(Vector2 local)
        {
            Vector2 uv = Layer == null ? local : ((TextureTransform)Call(document, "GetCanvasTransform", Layer)).Map(local, Size);
            Rect image = (Rect)Property(canvas, "ImageRect");
            return (Vector2)Call(canvas, "ToView", new Vector2(image.x + uv.x * image.width, image.yMax - uv.y * image.height));
        }

        public Vector2 Local(Vector2 view)
        {
            Vector2 point = (Vector2)Call(canvas, "ToCanvas", view);
            Rect image = (Rect)Property(canvas, "ImageRect");
            var uv = new Vector2((point.x - image.x) / image.width, (image.yMax - point.y) / image.height);
            return Layer == null ? uv : ((TextureTransform)Call(document, "GetCanvasTransform", Layer)).Unmap(uv, Size);
        }

        public void Pointer(EventType type, Vector2 view, EventModifiers modifiers = EventModifiers.Control, int clicks = 1, int button = 0)
        {
            var input = new Event { type = type, button = button, clickCount = clicks, modifiers = modifiers, mousePosition = canvas.LocalToWorld(view) };
            if (type == EventType.MouseDown) { using var evt = PointerDownEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
            else if (type == EventType.MouseUp) { using var evt = PointerUpEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
            else { using var evt = PointerMoveEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
        }

        public void Click(Vector2 local)
        {
            Vector2 view = View(local); Pointer(EventType.MouseDown, view); Pointer(EventType.MouseUp, view);
        }

        public void Key(KeyCode key)
        {
            using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
            evt.target = canvas; canvas.SendEvent(evt);
        }

        public void Fence() { Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); }

        public async Task UndoRedo(string before, string after, TextureTransform transform, string label, TextureTransform? afterTransform = null)
        {
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.Equal(before, JsonUtility.ToJson(Vector), label + ": one Undo restores the complete vector model");
            t.True(Layer.transform.Equals(transform), label + ": Undo preserves the layer transform");
            Undo.PerformRedo(); await Tick();
            t.Equal(after, JsonUtility.ToJson(Vector), label + ": one Redo restores the complete vector model");
            t.True(Layer.transform.Equals(afterTransform ?? transform), label + ": Redo restores the layer transform");
        }

        public async Task NoUndo(Func<Task> action, string label)
        {
            string name = document.name, marker = name + "-undo-fence";
            Fence(); Undo.RegisterCompleteObjectUndo(document, "Vector tools owned fence");
            document.name = marker; Fence();
            await action();
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Tick();
            t.Equal(name, document.name, label + ": next Undo reaches the owned sentinel, so the gesture added no Undo step");
        }

        public FloatField BrushField(string label)
        {
            var inspector = (VisualElement)Read(window, "toolkitLayerSettingsScroll");
            FloatField field = inspector.Query<FloatField>().ToList().Find(f => f.label == label);
            t.True(field != null, "Brush inspector contains " + label);
            return field;
        }

        public void Dispose()
        {
            var actions = new List<Action>
            {
                () => { if (window != null && Read(window, "vectorManipulator") != null) Call(Manipulator, "Cancel"); },
                () => { if (canvas != null) Pointer(EventType.MouseUp, canvas.contentRect.center); },
                () => { if (canvas != null) Pointer(EventType.MouseUp, canvas.contentRect.center, button: 1); },
                () => { if (document != null) Undo.ClearUndo(document); if (window != null) Undo.ClearUndo(window); },
                () => { if (detachedLayer != null) Call(detachedLayer, "ReleaseTransientResources"); },
                () => { if (focusWindow != null) { focusWindow.DiscardChanges(); focusWindow.Close(); } },
                () => { if (focusWindow != null) UnityEngine.Object.DestroyImmediate(focusWindow); },
                () => { if (window != null) { window.DiscardChanges(); if (window.rootVisualElement.panel != null) window.Close(); } },
                () => { if (window != null) UnityEngine.Object.DestroyImmediate(window); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); }
            };
            actions.AddRange(restorePreferences);
            AsyncD.CleanupOwned(actions.ToArray());
        }
    }

    sealed class StrokeCapture : ImmediateModeElement
    {
        public WhimTexWindow owner;
        public string path = "Temp/WhimTex/vector-stroke-style-preview.png";
        public bool retainPixels;
        public Color32[] pixels;
        public int imageWidth, imageHeight;
        public readonly TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D texture = null;
            try
            {
                var target = RenderTexture.active;
                int width = target != null ? target.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int height = target != null ? target.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); texture.Apply(false);
                if (retainPixels) { pixels = texture.GetPixels32(); imageWidth = width; imageHeight = height; }
                System.IO.Directory.CreateDirectory("Temp/WhimTex");
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
                done.TrySetResult(true);
            }
            catch (Exception error) { done.TrySetException(error); }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }

    static async Task StrokePreview(Fixture f)
    {
        await f.Seed(); f.Vector.shapes[0].stroke = true; f.Vector.shapes[0].strokeWidth = 6;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        var inspector = (ScrollView)Read(f.window, "toolkitLayerSettingsScroll");
        foreach (var foldout in inspector.Query<Foldout>().ToList())
            if (foldout.name == "transformSection" || foldout.name == "renderingSection" || foldout.text == "Fill" || foldout.text == "Figures (bottom to top)")
                foldout.value = false;
        await f.Tick(); inspector.ScrollTo(inspector.Q("strokeCap")); await f.Tick(); await f.Tick();
        var probe = new StrokeCapture { owner = f.window, pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute; probe.style.left = 0; probe.style.top = 0;
        probe.style.width = 1; probe.style.height = 1; f.window.rootVisualElement.Add(probe);
        try
        {
            for (int i = 0; i < 40 && !probe.done.Task.IsCompleted; i++) { f.window.Repaint(); await f.Tick(); }
            f.t.True(probe.done.Task.IsCompleted, "Owned window preview was captured"); await probe.done.Task;
        }
        finally { probe.RemoveFromHierarchy(); }
    }

    static void SubmitDash(Button button)
    {
        using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
    }

    static void DashKey(TextField field, KeyCode key)
    {
        using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key });
        evt.target = field; field.SendEvent(evt);
    }

    static async Task Dashes(Fixture f)
    {
        await f.Seed(true);
        f.Vector.shapes[0].stroke = true; f.Vector.shapes[0].strokeWidth = 4;
        f.Vector.shapes[0].dashPattern = new List<float> { 8, 4 };
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        var inspector = (ScrollView)Read(f.window, "toolkitLayerSettingsScroll");
        VisualElement Block() => inspector.Q("strokeDashes");
        TextField Text() => Block().Q<TextField>("dashPattern");
        VectorShape Shape() => f.Vector.shapes[0];
        void Pattern(params float[] expected) => f.t.True(expected.SequenceEqual(Shape().dashPattern), "Model pattern matches " + string.Join(" ", expected));
        void Enter(string value) { Text().value = value; DashKey(Text(), KeyCode.Return); }
        f.t.True(Text() != null && Block().Q("dashPreview") != null, "Text and live preview always present");
        f.t.Equal("Pattern (× Width)", Text().label, "Pattern declares stroke-width units");
        f.t.Equal("2 1", Text().value, "Older pixel pattern displays relative lengths without modifying source");
        f.t.True(!Shape().dashPatternRelative, "Viewing an older pattern does not rewrite it");
        string legacy = JsonUtility.ToJson(f.Vector); f.Fence();
        inspector.Query<FloatField>().ToList().Find(v => v.label == "Width (px)").value = 8;
        await f.Tick(); Pattern(2, 1);
        f.t.True(Shape().dashPatternRelative, "Width edit converts older pixels using the previous width");
        f.t.Equal("2 1", Text().value, "Width edit keeps displayed multipliers");
        await f.UndoRedo(legacy, JsonUtility.ToJson(f.Vector), f.Layer.transform, "Relative width conversion");
        inspector.Query<FloatField>().ToList().Find(v => v.label == "Width (px)").value = 4;
        Block().Q<Foldout>("dashSegments").value = true;
        inspector.ScrollTo(Block()); await f.Tick();
        string before = JsonUtility.ToJson(f.Vector); var transform = f.Layer.transform;
        f.Fence(); Text().value = "8, 4; 2\t4";
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Typing previews without committing half-entered text");
        f.t.Equal(8f, Block().Q<FloatField>("dashLength0").value, "Text updates Dash immediately");
        f.t.Equal(4f, Block().Q<FloatField>("dashGap1").value, "Text updates Gap immediately");
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        f.t.Equal("8, 4; 2\t4", Text().value, "Binding refresh retains uncommitted input");
        DashKey(Text(), KeyCode.Return); Pattern(8, 4, 2, 4);
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Dash text commit");
        f.t.Equal("8 4 2 4", Text().value, "Redo synchronizes the raw string");
        before = JsonUtility.ToJson(f.Vector); f.Fence();
        Block().Q<FloatField>("dashGap0").value = 7;
        Pattern(8, 7, 2, 4); f.t.Equal("8 7 2 4", Text().value, "Gap edit synchronizes raw text");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Dash pair commit");
        f.t.True(Block().Q<Button>("dashPresets").text.StartsWith("Custom"), "Edited pattern is Custom");
        string stable = Text().value;
        foreach (string bad in new[] { "8 zero", "8 0", "8 -2", "NaN 4", "Infinity 4", "8193 4", string.Join(" ", Enumerable.Repeat("1", 257)) })
        {
            before = JsonUtility.ToJson(f.Vector); Text().value = bad; DashKey(Text(), KeyCode.Return);
            Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
            f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Invalid input keeps model");
            f.t.Equal(bad, Text().value, "Invalid input is retained for correction");
            f.t.True(Text().ClassListContains("whimtex-dashes-invalid"), "Invalid input is highlighted");
            DashKey(Text(), KeyCode.Escape); f.t.Equal(stable, Text().value, "Escape restores last committed string");
        }
        Text().Focus(); Text().value = "6 3"; f.canvas.Focus(); await f.Tick(); Pattern(6, 3);
        Enter("8 4 2"); Pattern(8, 4, 2);
        f.t.Equal(6, Block().Q("dashPairs").Query<FloatField>().ToList().Count, "Odd list shows its entire two-repeat cycle");
        Block().Q<FloatField>("dashLength1").value = 3;
        Pattern(8, 4, 3);
        f.t.Equal(3f, Block().Q<FloatField>("dashGap2").value, "Repeated odd-list value stays linked");
        before = JsonUtility.ToJson(f.Vector); f.Fence();
        SubmitDash(Block().Q<Button>("dashAddPair")); Pattern(8, 4, 3, 8, 4, 3, 4, 3);
        f.t.Equal("8 4 3 8 4 3 4 3", Text().value, "Add Pair copies the last displayed pair of an odd cycle");
        f.t.Equal(4f, Block().Q<FloatField>("dashLength3").value, "Copied Dash synchronizes its field");
        f.t.Equal(3f, Block().Q<FloatField>("dashGap3").value, "Copied Gap synchronizes its field");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Copy last dash pair");
        SubmitDash(Block().Q<Button>("dashRemovePair1")); Pattern(8, 4, 4, 3, 4, 3);
        SubmitDash(Block().Q<Button>("dashAddPair")); Pattern(8, 4, 4, 3, 4, 3, 4, 3);
        Enter("11 2.5"); SubmitDash(Block().Q<Button>("dashAddPair")); Pattern(11, 2.5f, 11, 2.5f);
        Enter("5"); SubmitDash(Block().Q<Button>("dashAddPair")); Pattern(5, 5, 5, 5);
        var samples = new[] { Array.Empty<float>(), new[] { 2f, 2f }, new[] { 6f, 3f }, new[] { .01f, 2f }, new[] { 6f, 3f, .01f, 3f }, new[] { 6f, 3f, .01f, 3f, .01f, 3f } };
        for (int i = 0; i < samples.Length; i++)
        {
            before = JsonUtility.ToJson(f.Vector); var saved = Shape().Clone(); f.Fence();
            SubmitDash(Block().Q<Button>("dashPresets")); await f.Tick();
            f.t.True(!Block().Q("dashPresetGallery").ClassListContains("whimtex-hidden"), "Preset gallery opens");
            SubmitDash(Block().Q<Button>("dashPreset" + i)); Pattern(samples[i]);
            f.t.True(Block().Q("dashPresetGallery").ClassListContains("whimtex-hidden"), "Preset gallery closes after choice");
            saved.dashPattern = new List<float>(samples[i]); if (i >= 3) saved.cap = VectorStrokeCap.Round;
            f.t.Equal(JsonUtility.ToJson(saved), JsonUtility.ToJson(Shape()), "Preset changes only pattern and explicitly advertised cap");
            if (i == 3) await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Dotted preset with cap");
        }
        await f.NoUndo(async () => { SubmitDash(Block().Q<Button>("dashPreset5")); await f.Tick(); }, "Repeated dash preset");
        inspector.Q<FloatField>("strokeDashOffset").value = -7;
        inspector.Query<FloatField>().ToList().Find(v => v.label == "Width (px)").value = 9;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        Pattern(samples[5]);
        f.t.Equal(-7f, (float)Read(Block().Q("dashPreview"), "offset"), "Preview follows negative offset");
        f.t.Equal(9f, (float)Read(Block().Q("dashPreview"), "width"), "Preview follows stroke width");
        SubmitDash(inspector.Q<Button>("strokeCapSquare")); await f.Tick();
        f.t.Equal(VectorStrokeCap.Square, (VectorStrokeCap)Read(Block().Q("dashPreview"), "cap"), "Preview follows external cap edits");
        Enter(""); Pattern(); f.t.True(Block().Q<Button>("dashPresets").text.StartsWith("Solid"), "Empty text selects Solid");
        SubmitDash(Block().Q<Button>("dashAddPair")); Pattern(2, 1);
        SubmitDash(Block().Q<Button>("dashRemovePair0")); Pattern();
        Block().Q<Foldout>("dashSegments").value = false; await f.Tick();
        f.t.True(Text().resolvedStyle.display != DisplayStyle.None && Text().worldBound.width > 100, "Raw field remains visible with pairs collapsed");
        inspector.Query<Toggle>().ToList().Find(v => v.label == "Enabled").value = false;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        f.t.True(!Block().enabledInHierarchy, "Disabled stroke disables the complete dash editor");
        f.t.True(f.Vector.brushDraft != null, "Dash style edits retain brush source mask");
    }

    static async Task DashPreview(Fixture f)
    {
        await f.Seed(); var shape = f.Vector.shapes[0]; shape.stroke = true; shape.strokeWidth = 3;
        shape.fillMode = VectorFillMode.None; shape.cap = VectorStrokeCap.Round;
        shape.dashPatternRelative = true; shape.dashPattern = new List<float> { 6, 3, .01f, 3 };
        Call(f.window, "RefreshToolkitInterface", true); Call(f.window, "RequestCanvasRender", true); await f.Tick();
        var inspector = (ScrollView)Read(f.window, "toolkitLayerSettingsScroll");
        foreach (var foldout in inspector.Query<Foldout>().ToList())
            if (foldout.name == "transformSection" || foldout.name == "renderingSection" || foldout.text == "Fill" || foldout.text == "Figures (bottom to top)") foldout.value = false;
        inspector.Q<Foldout>("dashSegments").value = true;
        for (int i = 0; i < 2; i++)
        {
            if (i == 1) SubmitDash(inspector.Q<Button>("dashPresets"));
            await f.Tick(); inspector.ScrollTo(inspector.Q("strokeDashes")); await f.Tick(); await f.Tick();
            f.t.True(inspector.Q("dashPreview").worldBound.width > 100, "Preview has usable width");
            foreach (var field in inspector.Q("dashPairs").Query<FloatField>().ToList())
                f.t.True(field.worldBound.width >= 65, "Pair fields have usable width");
            var probe = new StrokeCapture { owner = f.window, path = "Temp/WhimTex/vector-dashes-" + (i == 0 ? "editor" : "presets") + ".png", pickingMode = PickingMode.Ignore };
            probe.style.position = Position.Absolute; probe.style.left = 0; probe.style.top = 0; probe.style.width = 1; probe.style.height = 1;
            f.window.rootVisualElement.Add(probe);
            try
            {
                for (int tick = 0; tick < 40 && !probe.done.Task.IsCompleted; tick++) { f.window.Repaint(); await f.Tick(); }
                f.t.True(probe.done.Task.IsCompleted, "Dash UI was captured"); await probe.done.Task;
            }
            finally { probe.RemoveFromHierarchy(); }
        }
    }

    static async Task StrokeStyle(Fixture f)
    {
        await f.Seed(true);
        f.Vector.shapes[0].stroke = true;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        var inspector = (ScrollView)Read(f.window, "toolkitLayerSettingsScroll");
        BaseField<T> Field<T>(string label) => inspector.Q<BaseField<T>>("stroke" + label);
        void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
        void Selected<T>(string label, T value)
        {
            var field = Field<T>(label);
            f.t.True(field != null, label + " uses an icon field");
            var buttons = field.Query<Button>().ToList();
            f.t.Equal(3, buttons.Count, label + " shows all three choices");
            f.t.Equal(1, buttons.Count(b => b.ClassListContains("whimtex-stroke-style--selected")), label + " has one selected choice");
            f.t.True(field.Q<Button>("stroke" + label + value).ClassListContains("whimtex-stroke-style--selected"), label + " selection matches model");
            f.t.True(buttons.All(b => !string.IsNullOrEmpty(b.tooltip) && b.Q(className: "whimtex-layer-action-icon") != null), "Every choice has a named tooltip and icon");
            f.t.True(field.Query<EnumField>().ToList().Count == 0, label + " has no dropdown");
        }
        async Task Check<T>(string label, T next, Func<T> read)
        {
            Selected(label, read());
            string before = JsonUtility.ToJson(f.Vector);
            var transform = f.Layer.transform;
            var field = Field<T>(label);
            inspector.ScrollTo(field); await f.Tick();
            var button = field.Q<Button>("stroke" + label + next);
            f.t.True(button.enabledInHierarchy && button.worldBound.width >= 24 && button.worldBound.height >= 20, "Visible usable button size");
            f.Fence(); Submit(button); await f.Tick();
            f.t.Equal(next, read(), label + " button updates the model");
            Selected(label, next);
            string after = JsonUtility.ToJson(f.Vector);
            await f.UndoRedo(before, after, transform, label + " button");
            Selected(label, next);
            await f.NoUndo(async () => { Submit(Field<T>(label).Q<Button>("stroke" + label + next)); await f.Tick(); }, "Repeated choice");
        }
        foreach (var position in new[] { VectorStrokePosition.Inside, VectorStrokePosition.Outside, VectorStrokePosition.Center })
            await Check("Position", position, () => f.Vector.shapes[0].strokePosition);
        foreach (var join in new[] { VectorStrokeJoin.Round, VectorStrokeJoin.Bevel, VectorStrokeJoin.Miter })
            await Check("Join", join, () => f.Vector.shapes[0].join);
        foreach (var cap in new[] { VectorStrokeCap.Round, VectorStrokeCap.Square, VectorStrokeCap.Butt })
            await Check("Cap", cap, () => f.Vector.shapes[0].cap);
        var enabled = inspector.Query<Toggle>().ToList().Find(e => e.label == "Enabled");
        f.t.True(enabled != null, "Stroke toggle found"); enabled.value = false;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        f.t.True(!Field<VectorStrokePosition>("Position").enabledInHierarchy && !Field<VectorStrokeJoin>("Join").enabledInHierarchy && !Field<VectorStrokeCap>("Cap").enabledInHierarchy, "Disabled stroke disables all choice groups");
        f.t.True(f.Vector.brushDraft != null, "Style edits retain the original brush mask");
    }

    static async Task ShapeStrokes(Fixture f)
    {
        await f.Seed();
        f.Layer.SetBehaviour(new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Star, stroke = true,
            strokeWidth = 5, strokePosition = ShapeLayerBehaviour.StrokePosition.Outside, fillColor = new Color(.2f,.5f,.8f),
            strokeColor = Color.white, feather = 1, dashPattern = new List<float> { 3, 2 } });
        f.Layer.transform = TextureTransform.Default; f.Layer.transform.scaleF = Vector2.one * .65f;
        Call(f.window,"RefreshToolkitInterface",true); Call(f.window,"RequestCanvasRender",true); await f.Tick();
        var inspector = (ScrollView)Read(f.window,"toolkitLayerSettingsScroll");
        ShapeLayerBehaviour Shape() => (ShapeLayerBehaviour)f.Layer.Behaviour;
        TextField Text() => inspector.Q<TextField>("dashPattern");
        var before = JsonUtility.ToJson(Shape()); var transform = f.Layer.transform;
        f.Fence(); SubmitDash(inspector.Q<Button>("strokeJoinMiter")); await f.Tick();
        f.t.Equal(VectorStrokeJoin.Miter,Shape().strokeJoin,"Shape join icon edits outline");
        string after = JsonUtility.ToJson(Shape());
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await f.Tick();
        f.t.Equal(before,JsonUtility.ToJson(Shape()),"Shape style Undo");
        Undo.PerformRedo(); await f.Tick();
        f.t.Equal(after,JsonUtility.ToJson(Shape()),"Shape style Redo");
        SubmitDash(inspector.Q<Button>("strokePositionInside")); await f.Tick();
        f.t.Equal(ShapeLayerBehaviour.StrokePosition.Inside,Shape().strokePosition,"Shape alignment enum maps explicitly");
        SubmitDash(inspector.Q<Button>("strokePositionOutside")); await f.Tick();
        SubmitDash(inspector.Q<Button>("dashPreset3")); await f.Tick();
        f.t.True(Shape().strokeCap == VectorStrokeCap.Round && Shape().dashPattern.SequenceEqual(new[]{.01f,2f}),"Shape dotted preset synchronizes caps and pattern");
        Text().value="3 2"; DashKey(Text(),KeyCode.Return); await f.Tick();
        inspector.Q<Foldout>("dashSegments").value=true;
        SubmitDash(inspector.Q<Button>("dashAddPair")); await f.Tick();
        f.t.True(Shape().dashPattern.SequenceEqual(new[]{3f,2f,3f,2f}),"Shape Add Pair copies last pair");
        inspector.Query<FloatField>().ToList().Find(field=>field.label=="Stroke Width (px)").value=7;
        await f.Tick(); f.t.Equal("3 2 3 2",Text().value,"Shape width keeps pattern multipliers");
        f.t.True(Shape().feather==1 && f.Layer.transform.Equals(transform),"Stroke settings preserve Feather and Transform");
        foreach(var foldout in inspector.Query<Foldout>().ToList())
            if(foldout.name=="transformSection" || foldout.name=="renderingSection") foldout.value=false;
        inspector.ScrollTo(inspector.Q("strokeOptions")); await f.Tick(); await f.Tick();
        f.t.True(Text().worldBound.width>100 && inspector.Q("dashPreview").worldBound.width>100,"Shape shared editor has usable layout");
        var probe=new StrokeCapture { owner=f.window,path="Temp/WhimTex/shape-strokes-editor.png",pickingMode=PickingMode.Ignore };
        probe.style.position=Position.Absolute;probe.style.left=0;probe.style.top=0;probe.style.width=1;probe.style.height=1;
        f.window.rootVisualElement.Add(probe);
        try
        {
            for(int i=0;i<40&&!probe.done.Task.IsCompleted;i++){ f.window.Repaint();await f.Tick(); }
            f.t.True(probe.done.Task.IsCompleted,"Shape inspector and render captured");await probe.done.Task;
        }
        finally{probe.RemoveFromHierarchy();}
    }

    static async Task Geometry(Fixture f)
    {
        await f.Seed();
        await f.Tool("None");
        await f.NoUndo(async () =>
        {
            Call(f.window, "SelectOnlyLayer", (object)null);
            Vector2 edge = f.View(new Vector2(-.025f, .5f));
            f.Pointer(EventType.MouseDown, edge); f.Pointer(EventType.MouseUp, edge);
            f.t.Equal(f.layerId, (string)Read(f.window, "selectedLayerId"), "Layer Select picks the curve just outside its rendered pixels");
            await f.Tick();
        }, "Geometry-only layer selection");
        await CaptureCommonOutline("select");
        await f.Tool("Transform");
        await CaptureCommonOutline("transform");
        TextureTransform transform = f.Layer.transform;
        string before = JsonUtility.ToJson(f.Vector);
        VectorShape source = f.Vector.shapes[0].Clone();
        Vector2 start = Bounds(source).center + new Vector2(-.12f, .12f), delta = new Vector2(.08f, .05f);
        Vector2 moveStart = f.View(start), moveEnd = f.View(start + delta);
        f.Fence();
        f.Pointer(EventType.MouseDown, moveStart); f.Pointer(EventType.MouseDrag, moveEnd);
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Figure move preview does not write the layer model");
        f.Pointer(EventType.MouseUp, moveEnd);
        CheckFrame(f, source, transform, p => p + delta, "Move changes the layer transform");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Whole-figure move leaves local geometry and style intact");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Move figure", f.Layer.transform);

        await f.Tool("VectorNodes");
        source = f.Vector.shapes[0].Clone(); before = JsonUtility.ToJson(f.Vector); transform = f.Layer.transform;
        Vector2 anchor = source.contours[0].points[0].position, nodeDelta = new Vector2(.04f, -.025f);
        f.Fence(); f.Pointer(EventType.MouseDown, f.View(anchor)); f.Pointer(EventType.MouseDrag, f.View(anchor + nodeDelta));
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Node move preview is detached");
        f.Pointer(EventType.MouseUp, f.View(anchor + nodeDelta));
        for (int i = 0; i < 4; i++) Near(f.t, transform.Map(source.contours[0].points[i].position + (i == 0 ? nodeDelta : Vector2.zero), f.Size),
            f.Layer.transform.Map(f.Vector.shapes[0].contours[0].points[i].position, f.Size), "Node edit preserves other canvas anchors while fitting the frame");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Move node", f.Layer.transform);

        await f.Tool("Transform");
        source = f.Vector.shapes[0].Clone(); before = JsonUtility.ToJson(f.Vector); transform = f.Layer.transform;
        Rect bounds = Bounds(source);
        Vector2 opposite = bounds.min, corner = bounds.max, next = corner + new Vector2(.06f, .04f);
        Vector2 scale = new Vector2((next.x - opposite.x) / bounds.width, (next.y - opposite.y) / bounds.height);
        Vector2 resizeStart = f.View(corner), resizeEnd = f.View(next);
        f.Fence(); f.Pointer(EventType.MouseDown, resizeStart, EventModifiers.None); f.Pointer(EventType.MouseDrag, resizeEnd, EventModifiers.None); f.Pointer(EventType.MouseUp, resizeEnd, EventModifiers.None);
        CheckFrame(f, source, transform, p => opposite + Vector2.Scale(p - opposite, scale), "Resize changes the frame around the opposite corner");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Resize retains local Beziers and style");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Resize figure", f.Layer.transform);

        source = f.Vector.shapes[0].Clone(); before = JsonUtility.ToJson(f.Vector); bounds = Bounds(source); transform = f.Layer.transform;
        Vector2 top = f.View(new Vector2(bounds.center.x, bounds.yMax));
        Vector2 handle = top + (top - f.View(bounds.center)).normalized * 24;
        Vector2 pivot = transform.pivotF;
        Vector2 canvasPivot = transform.Map(pivot, f.Size);
        Vector2 arm = Vector2.Scale(transform.Map(f.Local(handle), f.Size) - canvasPivot, f.Size);
        Vector2 target = transform.Unmap(canvasPivot + new Vector2(-arm.y / f.Size.x, arm.x / f.Size.y), f.Size);
        f.t.True(f.canvas.contentRect.Contains(handle) && f.canvas.contentRect.Contains(f.View(target)), "Rotation gesture remains in the canvas");
        Vector2 rotateEnd = f.View(target);
        f.Fence(); f.Pointer(EventType.MouseDown, handle); f.Pointer(EventType.MouseDrag, rotateEnd); f.Pointer(EventType.MouseUp, rotateEnd);
        CheckFrame(f, source, transform, p =>
        {
            Vector2 d = Vector2.Scale(transform.Map(p, f.Size) - canvasPivot, f.Size);
            return transform.Unmap(canvasPivot + new Vector2(-d.y / f.Size.x, d.x / f.Size.y), f.Size);
        }, "Rotate changes the layer frame");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Rotation retains local Beziers and style");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Rotate figure", f.Layer.transform);

        // Create a group through the same command used by the layer context menu.
        string originalId=f.layerId, originalStyle=JsonUtility.ToJson(f.Vector.shapes[0]);
        var other=new Layer(new ShapeLayerBehaviour { kind=ShapeLayerBehaviour.ShapeKind.Ellipse,fillColor=Color.green });
        other.transform.scaleF=Vector2.one*.2f; f.document.layers.Insert(0,other); Call(f.document,"NormalizeModel");
        f.Fence(); Call(f.window,"GroupBooleanLayers",new List<Layer>{other,f.Layer},VectorBooleanOperation.Subtract);await f.Tick();
        f.t.Equal(1,f.document.layers.Count,"Grouping replaces selected siblings by one group");
        var group=f.document.layers[0];f.t.True(group.Behaviour is BooleanGroupLayerBehaviour,"Boolean command creates geometry group");
        f.t.Equal(2,group.children.Count,"Source layers retained");
        f.t.Equal(originalStyle,JsonUtility.ToJson(f.Vector.shapes[0]),"Group preserves source appearance");
        var inspector=(ScrollView)Read(f.window,"toolkitLayerSettingsScroll");
        f.t.True(inspector.Q("strokeJoin")!=null && inspector.Q("strokeDashes")!=null,"Group exposes common stroke editor");
        string groupId=group.Id;
        Undo.FlushUndoRecordObjects();Undo.PerformUndo();await f.Tick();f.t.Equal(2,f.document.layers.Count,"Group Undo restores siblings");
        Undo.PerformRedo();await f.Tick();f.t.Equal(groupId,f.document.layers[0].Id,"Group Redo restores identity");
        Call(f.window,"SelectOnlyLayer",originalId);Call(f.window,"RefreshToolkitInterface",true);await f.Tick();
        inspector=(ScrollView)Read(f.window,"toolkitLayerSettingsScroll");
        f.t.True(!inspector.Q("booleanOperandSettings").ClassListContains("whimtex-hidden"),"Operand operation is visible");
        f.t.True(!inspector.Q("strokeJoin").enabledInHierarchy,"Operand appearance is retained but disabled");
        var operation=inspector.Q("booleanOperandSettings").Q<EnumField>();operation.value=VectorBooleanOperation.Xor;await f.Tick();
        f.t.Equal(VectorBooleanOperation.Xor,f.Layer.booleanOperation,"Operation control edits operand");
        Call(f.window,"SelectOnlyLayer",groupId);
        var visibleGroup=(Layer)Call(f.document,"FindLayer",groupId);
        ((BooleanGroupLayerBehaviour)visibleGroup.Behaviour).style.stroke=true;
        ((BooleanGroupLayerBehaviour)visibleGroup.Behaviour).style.strokeWidth=3;
        Call(f.window,"RefreshToolkitInterface",true);Call(f.window,"RequestCanvasRender",true);await f.Tick();
        inspector=(ScrollView)Read(f.window,"toolkitLayerSettingsScroll");
        foreach(var foldout in inspector.Query<Foldout>().ToList())
            if(foldout.name=="transformSection" || foldout.name=="renderingSection")foldout.value=false;
        await f.Tick();
        var probe=new StrokeCapture { owner=f.window,path="Temp/WhimTex/vector-boolean-editor.png",pickingMode=PickingMode.Ignore };
        probe.style.position=Position.Absolute;probe.style.left=0;probe.style.top=0;probe.style.width=1;probe.style.height=1;
        f.window.rootVisualElement.Add(probe);
        try
        {
            for(int i=0;i<40&&!probe.done.Task.IsCompleted;i++){ f.window.Repaint();await f.Tick(); }
            f.t.True(probe.done.Task.IsCompleted,"Boolean Group inspector and result captured");await probe.done.Task;
        }
        finally{probe.RemoveFromHierarchy();}

        async Task CaptureCommonOutline(string name)
        {
            var capture = new StrokeCapture { owner = f.window, path = "Temp/WhimTex/vector-common-" + name + ".png", pickingMode = PickingMode.Ignore };
            f.window.rootVisualElement.Add(capture);
            try
            {
                for (int i = 0; i < 40 && !capture.done.Task.IsCompleted; i++) { f.window.Repaint(); await f.Tick(); }
                f.t.True(capture.done.Task.IsCompleted, "Common tool contour preview captured: " + name);
                await capture.done.Task;
            }
            finally { capture.RemoveFromHierarchy(); }
        }
    }

    static void CheckFrame(Fixture f,VectorShape source,TextureTransform previous,Func<Vector2,Vector2> map,string label)
    {
        var current=f.Vector.shapes[0];
        for(int c=0;c<source.contours.Count;c++)for(int p=0;p<source.contours[c].points.Count;p++)
            Near(f.t,previous.Map(map(source.contours[c].points[p].position),f.Size),
                f.Layer.transform.Map(current.contours[c].points[p].position,f.Size),label);
    }

    static async Task Cancellation(Fixture f)
    {
        await f.Seed(true);
        foreach (string tool in new[] { "VectorNodes", "VectorPen", "VectorBrush" })
        {
            await f.Tool(tool);
            string before = JsonUtility.ToJson(f.Vector);
            TextureTransform transform = f.Layer.transform;
            Vector2 local = tool == "VectorNodes" ? f.Vector.shapes[0].contours[0].points[0].position : new Vector2(.35f, .5f);
            await f.NoUndo(async () =>
            {
                Vector2 start = f.View(local), end = start + new Vector2(28, -18);
                f.Pointer(EventType.MouseDown, start); f.Pointer(EventType.MouseDrag, end);
                Texture2D preview = Read(f.Manipulator, "brushPreview") as Texture2D;
                f.Key(KeyCode.Escape); f.Pointer(EventType.MouseUp, end);
                f.t.Equal(before, JsonUtility.ToJson(f.Vector), tool + " Escape preserves shape and retained mask");
                f.t.True(f.Layer.transform.Equals(transform), tool + " Escape preserves transform");
                f.t.True(!f.canvas.HasPointerCapture(PointerId.mousePointerId), tool + " Escape releases capture");
                f.t.True(!(bool)Property(f.Manipulator, "IsDragging"), tool + " Escape discards transient geometry");
                f.t.True(preview == null, tool + " Escape destroys its owned raster preview");
                await f.Tick();
            }, tool + " cancellation");
        }
        await f.Tool("VectorBrush");
        string snapshot = JsonUtility.ToJson(f.Vector);
        await f.NoUndo(async () =>
        {
            Vector2 start = f.View(new Vector2(.5f, .5f));
            f.Pointer(EventType.MouseDown, start); f.Pointer(EventType.MouseDrag, start + Vector2.right * 20);
            await f.Tick();
            f.canvas.ReleasePointer(PointerId.mousePointerId); await f.Tick();
            f.Pointer(EventType.MouseUp, start + Vector2.right * 20);
            f.t.Equal(snapshot, JsonUtility.ToJson(f.Vector), "Lost capture preserves the previous stroke and draft");
            f.t.True(Read(f.Manipulator, "brushBuilder") == null && Read(f.Manipulator, "brushPreview") == null, "Lost capture releases transient brush state");
        }, "Lost capture");
        await f.Tool("VectorPen");
        await f.NoUndo(async () =>
        {
            f.Click(new Vector2(.2f, .2f)); f.Click(new Vector2(.7f, .3f));
            await f.Tool("None"); f.Key(KeyCode.Return);
            f.t.Equal(snapshot, JsonUtility.ToJson(f.Vector), "Switching tools cancels an unfinished pen path");
        }, "Tool change");
    }

    static async Task BrushLines(Fixture f)
    {
        await f.Tool("VectorBrush");
        var shape = f.window.rootVisualElement.Q<EnumField>("vectorBrushShape");
        f.t.True(shape != null, "Vector brush has the Pencil-style Shape selector");
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 12;
        const EventModifiers shifted = EventModifiers.Shift | EventModifiers.Control;
        Vector2 ActualView() => (Vector2)Call(f.Manipulator, "View", Read(f.Manipulator, "brushLastPoint"));
        void Check(Vector2 expected, string label)
        {
            Near(f.t, expected, ActualView(), label);
            Near(f.t, expected, (Vector2)Read(f.canvas, "cursorPosition"), label + " cursor");
        }
        foreach (VectorBrushShape tip in Enum.GetValues(typeof(VectorBrushShape)))
        {
            shape.value = tip;
            f.Pointer(EventType.MouseMove, f.canvas.contentRect.center);
            f.t.Equal(tip, (VectorBrushShape)Read(f.canvas, "brushCursorShape"), "Hover cursor reflects Shape");
            foreach (float rotation in new[] { 0f, 37f })
            {
                Call(f.canvas, "SetViewRotation", rotation, false);
                Call(f.canvas, "ZoomAt", f.canvas.contentRect.center, rotation == 0 ? 1f : 2f);
                await f.Tick();
                Vector2 a = f.canvas.contentRect.center, b = a + new Vector2(42, 9);
                f.Pointer(EventType.MouseDown, a, shifted);
                f.Pointer(EventType.MouseDrag, b, shifted);
                Check(new Vector2(b.x, a.y), "Held Shift locks horizontal in view space");
                f.Pointer(EventType.MouseDrag, a + new Vector2(48, 65), shifted);
                Check(new Vector2(a.x + 48, a.y), "Chosen axis stays locked despite crossing the diagonal");
                // KeyUp changes the constraint even without pointer movement; next event is freehand.
                using (var key = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = KeyCode.LeftShift }))
                { key.target = f.canvas; f.canvas.SendEvent(key); }
                f.t.True(!(bool)Read(f.Manipulator, "brushShift"), "Shift key release exits the constraint");
                Vector2 free = a + new Vector2(10, 20);
                f.Pointer(EventType.MouseDrag, free); Check(free, "Released Shift is freehand");
                f.Key(KeyCode.LeftShift);
                f.Pointer(EventType.MouseDrag, free + new Vector2(4, 35), shifted);
                Check(free + new Vector2(0, 35), "Pressing Shift mid-stroke uses the last painted point");
                f.Key(KeyCode.Escape);
                f.t.Equal(0, f.document.layers.Count, "Cancelled line leaves no figure");
            }
        }
        Call(f.canvas, "SetViewRotation", 0f, false); Call(f.canvas, "ZoomAt", f.canvas.contentRect.center, 1f); await f.Tick();
        shape.value = VectorBrushShape.Diamond;
        Vector2 first = f.canvas.contentRect.center - new Vector2(42, 24);
        f.Pointer(EventType.MouseDown, first); f.Pointer(EventType.MouseUp, first); await f.Tick();
        f.t.Equal(1, f.document.layers.Count, "First dab creates its own figure");
        var previous = (VectorLayerBehaviour)f.document.layers[0].Behaviour;
        string previousJson = JsonUtility.ToJson(previous);
        byte[] previousMask = (byte[])previous.brushDraft.coverage.Clone();
        var previousDraft = previous.brushDraft;
        Vector2 next = first + new Vector2(64, 48);
        f.Fence(); f.Pointer(EventType.MouseDown, next, shifted);
        var builder = (VectorBrushBuilder)Read(f.Manipulator, "brushBuilder");
        f.t.Equal(2, builder.SampleCount, "Shift-click starts at the previous endpoint then connects to the new point");
        Vector2 midpoint = (Vector2)Call(f.Manipulator, "Local", (first + next) / 2, true);
        Vector2 maskPoint = builder.ToPixel(midpoint);
        f.t.True(builder.Coverage[Mathf.FloorToInt(maskPoint.y) * builder.Width + Mathf.FloorToInt(maskPoint.x)] == 255,
            "Shift-click paints the segment between strokes");
        f.Pointer(EventType.MouseUp, next, shifted); await f.Tick();
        f.t.Equal(1, f.document.layers.Count, "Connected segment extends the active figure");
        f.t.True(previousMask.SequenceEqual(previousDraft.coverage), "Connecting never mutates an old Undo mask");
        string result = JsonUtility.ToJson(f.document.layers[0].Behaviour);
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await f.Tick();
        f.t.Equal(1, f.document.layers.Count, "Undo keeps the figure");
        f.t.Equal(previousJson, JsonUtility.ToJson(f.document.layers[0].Behaviour), "Undo removes only the connected segment");
        Undo.PerformRedo(); await f.Tick();
        f.t.Equal(result, JsonUtility.ToJson(f.document.layers[0].Behaviour), "Redo restores its shape and original mask");
        await f.Tool("VectorPen"); await f.Tool("VectorBrush");
        f.Pointer(EventType.MouseDown, next, shifted);
        builder = (VectorBrushBuilder)Read(f.Manipulator, "brushBuilder");
        f.t.Equal(1, builder.SampleCount, "Tool switch invalidates the old line anchor");
        f.Key(KeyCode.Escape);
    }

    static async Task Brush(Fixture f)
    {
        await f.Seed(true); await f.Tool("VectorBrush");
        await BrushCursor(f);
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 20f;
        Write(f.window, "vectorBrushTolerance", .75f);
        TextureTransform transform = f.Layer.transform;
        string before = JsonUtility.ToJson(f.Vector);
        VectorBrushDraft previous = f.Vector.brushDraft;
        byte[] previousCoverage = (byte[])previous.coverage.Clone();
        Vector2 start = f.View(new Vector2(.55f, .35f)), end = f.View(new Vector2(.7f, .65f));
        f.Fence(); f.Pointer(EventType.MouseDown, start); f.Pointer(EventType.MouseDrag, end);
        await f.Tick();
        f.t.True(((VisualElement)Read(f.canvas, "overlay")).resolvedStyle.display != DisplayStyle.None,
            "Cursor overlay stays displayed during a captured drawing stroke");
        f.t.True((bool)Read(f.canvas, "cursorVisible"), "Vector cursor remains visible during the captured stroke");
        Near(f.t, end, (Vector2)Read(f.canvas, "cursorPosition"), "Vector cursor follows captured pointer movement");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Raster stroke preview leaves committed figures and draft unchanged");
        f.t.True(ReferenceEquals(previous, f.Vector.brushDraft), "Previous draft remains owned until successful stroke commit");
        var builder = (VectorBrushBuilder)Read(f.Manipulator, "brushBuilder");
        var preview = (Texture2D)Read(f.Manipulator, "brushPreview");
        f.t.True(builder != null && preview != null && builder.Coverage.Any(value => value > 0), "Held stroke has a real raster buffer and preview texture");
        byte[] beforeResize = (byte[])builder.Coverage.Clone();
        f.Key(KeyCode.RightBracket);
        f.t.Equal(22f, (float)Read(f.window, "vectorBrushSize"), "Size shortcut works during a captured stroke");
        f.t.Equal(11f, (float)Read(f.Manipulator, "brushRadius"), "Pending samples use the updated brush radius");
        f.t.Near(11f, (float)Property(f.canvas, "RoundCursorRadius"), .001, "Captured-stroke cursor updates without pointer movement");
        f.t.True(beforeResize.SequenceEqual(builder.Coverage), "Size changes do not rewrite the mask or insert a stamp");
        f.Key(KeyCode.LeftBracket);
        f.Pointer(EventType.MouseUp, end);
        f.t.Equal(2, f.document.layers.Count, "One release creates a separate figure layer");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "New stroke leaves the previous figure and mask intact");
        string previousLayerId=f.layerId;
        f.layerId=(string)Read(f.window,"selectedLayerId");
        f.t.Equal(1, f.Vector.shapes.Count, "New layer has exactly one figure");
        f.t.True(preview == null && Read(f.Manipulator, "brushPreview") == null, "Release destroys the transient raster preview");
        f.t.True(f.Vector.brushDraft.shapeId != previous.shapeId, "Successful next stroke replaces the retained draft");
        f.t.True(previousCoverage.SequenceEqual(previous.coverage), "Replacing a draft never mutates its old mask");
        f.t.Equal(f.Vector.shapes[0].id, f.Vector.brushDraft.shapeId, "New shape and retained draft have matching IDs");
        f.t.True(f.Vector.shapes[0].contours.SelectMany(c => c.points).Any(p => p.inTangent.sqrMagnitude > 0 && p.outTangent.sqrMagnitude > 0),
            "Committed brush figure contains editable cubic handles");
        string created=JsonUtility.ToJson(f.Vector);transform=f.Layer.transform;
        Undo.FlushUndoRecordObjects();Undo.PerformUndo();await f.Tick();
        f.t.Equal(1,f.document.layers.Count,"Brush Undo removes only the new layer");
        f.t.Equal(previousLayerId,f.document.layers[0].Id,"Previous layer identity retained");
        Undo.PerformRedo();await f.Tick();f.t.Equal(created,JsonUtility.ToJson(f.Vector),"Brush Redo restores mask and figure");
        Call(f.window,"SelectOnlyLayer",f.layerId);Call(f.window,"RefreshToolkitInterface",true);await f.Tick();

        VectorShape selected = f.Vector.shapes.Find(s => s.id == f.Vector.brushDraft.shapeId);
        Call(f.window, "SelectVectorShape", selected);
        selected.fillMode = VectorFillMode.LinearGradient; selected.fillColor = new Color(.3f, .7f, .2f, .8f);
        selected.stroke = true; selected.strokeWidth = 3; selected.dashPattern = new List<float> { 7, 3 };
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        byte[] source = (byte[])f.Vector.brushDraft.coverage.Clone();
        string shapeId = selected.id, style = Style(selected);
        before = JsonUtility.ToJson(f.Vector); f.Fence();
        f.BrushField("Expand (px)").value = 4;
        selected = f.Vector.shapes.Find(s => s.id == shapeId);
        f.t.Equal(1, f.Vector.shapes.Count, "Inspector rebuild replaces geometry without duplicating the figure");
        f.t.Equal(shapeId, f.Vector.brushDraft.shapeId, "Rebuild retains shape identity");
        f.t.Equal(style, Style(selected), "Rebuild retains fills, gradients, stroke and dashes");
        f.t.True(source.SequenceEqual(f.Vector.brushDraft.coverage), "Expansion does not rewrite source coverage");
        f.t.Equal(4f, f.Vector.brushDraft.expansion, "Expansion is committed with derived geometry");
        f.t.True(before != JsonUtility.ToJson(f.Vector), "Expansion changes the actual model");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Brush rebuild", f.Layer.transform);
        f.Fence(); f.BrushField("Expand (px)").value = -256;
        f.t.Equal(0, f.Vector.shapes.Find(s => s.id == shapeId).contours.Count, "Complete contraction leaves an empty editable figure");
        f.t.True(f.Vector.brushDraft != null && source.SequenceEqual(f.Vector.brushDraft.coverage), "Empty geometry retains its source for recovery");
        f.BrushField("Expand (px)").value = 0;
        f.t.True(f.Vector.shapes.Find(s => s.id == shapeId).contours.Count > 0, "Returning expansion recovers the stroke from its retained mask");
        before = JsonUtility.ToJson(f.Vector); transform=f.Layer.transform; f.Fence();
        f.BrushField("Tolerance (px)").value = 2;
        f.t.Equal(2f, f.Vector.brushDraft.tolerance, "Inspector precision is persisted");
        f.t.True(source.SequenceEqual(f.Vector.brushDraft.coverage), "Repeated procedural edits preserve source pixels");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Bezier precision rebuild", f.Layer.transform);

        // Valid source with an intentionally too-small reference frame exceeds the
        // expanded-field budget. The UI must reject before changing the model/Undo.
        Vector2 reference = f.Vector.referenceSize;
        f.Vector.referenceSize = Vector2.one;
        string rejected = JsonUtility.ToJson(f.Vector);
        try
        {
            await f.NoUndo(async () =>
            {
                f.BrushField("Expand (px)").value = 256;
                f.t.Equal(rejected, JsonUtility.ToJson(f.Vector), "Rejected rebuild is atomic");
                f.Vector.referenceSize = reference;
                await f.Tick();
            }, "Rejected brush rebuild");
        }
        finally { f.Vector.referenceSize = reference; }
    }

    static async Task BrushEditing(Fixture f)
    {
        await f.Tool("VectorBrush");
        Button Toggle() => f.window.rootVisualElement.Q<Button>("vectorBrushEditMask");
        async Task Dab(Vector2 view, int button = 0)
        {
            f.Pointer(EventType.MouseDown, view, button: button);
            f.Pointer(EventType.MouseUp, view, button: button);
            await f.Tick();
        }
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 40;
        Vector2 a = f.canvas.contentRect.center, b = a + new Vector2(45, 0);
        f.t.True(!Toggle().enabledInHierarchy, "No source mask to edit before the first stroke");
        await Dab(a, 1); f.t.Equal(0, f.document.layers.Count, "RMB never creates an empty figure");
        await Dab(a); f.layerId = (string)Read(f.window, "selectedLayerId");
        f.t.Equal("Finish Editing", Toggle().text, "First stroke enters mask editing");
        f.t.Equal(1, f.document.layers.Count, "First stroke creates exactly one figure");
        var oldDraft = f.Vector.brushDraft; byte[] oldPixels = (byte[])oldDraft.coverage.Clone();
        string shapeId = oldDraft.shapeId, first = JsonUtility.ToJson(f.Vector);
        var firstTransform = f.Layer.transform;
        f.Fence(); f.Pointer(EventType.MouseDown, b);
        var adding = (VectorBrushBuilder)Read(f.Manipulator, "brushBuilder");
        f.t.True(adding != null && adding.HasChanges, "Second dab changes the editable mask");
        f.t.Near(20, (float)Read(f.Manipulator, "brushRadius"), .001, "Rebased frame keeps the brush's source-pixel radius");
        f.Pointer(EventType.MouseUp, b); await f.Tick();
        f.t.Equal(1, f.document.layers.Count, "LMB adds a disconnected island to the same layer");
        f.t.Equal(shapeId, f.Vector.shapes[0].id, "Dabs keep the same shape ID");
        f.t.True(f.Vector.shapes[0].contours.Count >= 2, "Both islands are retained; contours=" + f.Vector.shapes[0].contours.Count +
            ", changed=" + (first != JsonUtility.ToJson(f.Vector)) + ", frame=" + f.Vector.brushDraft.frame + ", reference=" + f.Vector.referenceSize);
        f.t.True(oldPixels.SequenceEqual(oldDraft.coverage), "Old snapshot stays immutable");
        await f.UndoRedo(first, JsonUtility.ToJson(f.Vector), firstTransform, "Add mask island", f.Layer.transform);

        // Styles and procedural controls survive edits; their coordinates rebase with the frame.
        var shape = f.Vector.shapes[0]; shape.fillMode = VectorFillMode.LinearGradient;
        shape.stroke = true; shape.strokeWidth = 3; shape.strokeMode = VectorFillMode.RadialGradient;
        shape.dashPattern = new List<float> { 2, 1 }; shape.dashPatternRelative = true;
        Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        if (Toggle().text == "Edit Mask") { SubmitDash(Toggle()); await f.Tick(); }
        string before = JsonUtility.ToJson(f.Vector); var transform = f.Layer.transform;
        string style = Style(f.Vector.shapes[0]);
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 10;
        f.Fence(); f.Pointer(EventType.MouseDown, a, button: 1);
        f.t.True((bool)Read(f.canvas, "cursorErase"), "RMB immediately shows the eraser cursor");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Erase preview does not modify the committed figure");
        f.Pointer(EventType.MouseUp, a, button: 1); await f.Tick();
        f.t.True(!((bool)Read(f.canvas, "cursorErase")), "Releasing RMB restores the paint cursor");
        f.t.Equal(1, f.document.layers.Count, "RMB edits, never duplicates");
        f.t.True(f.Vector.shapes[0].contours.Count >= 3, "Erasure creates a hole");
        f.t.Equal(style, Style(f.Vector.shapes[0]), "Erasure keeps gradients, stroke and dash settings");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Erase mask hole", f.Layer.transform);

        before = JsonUtility.ToJson(f.Vector);
        await f.NoUndo(async () =>
        {
            f.Pointer(EventType.MouseDown, b, button: 1); f.Pointer(EventType.MouseDrag, b + Vector2.right * 7, button: 1);
            f.Key(KeyCode.Escape); f.Pointer(EventType.MouseUp, b, button: 1); await f.Tick();
            f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Escape cancels erasing atomically");
        }, "Cancelled erase");
        f.Key(KeyCode.Return); await f.Tick();
        f.t.Equal("Edit Mask", Toggle().text, "Enter ends editing but keeps the source");
        f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Finishing does not discard or refit the mask");
        await Dab(a, 1); f.t.Equal(before, JsonUtility.ToJson(f.Vector), "RMB on a finished figure cannot erase without Edit Mask");
        SubmitDash(Toggle()); await f.Tick();
        f.t.Equal("Finish Editing", Toggle().text, "Toolbar resumes selected mask");
        var inspector = (VisualElement)Read(f.window, "toolkitLayerSettingsScroll");
        f.t.Equal("Finish Editing", inspector.Q<Button>("vectorPropertiesEditMask").text, "Inspector button stays synchronized");
        SubmitDash(inspector.Q<Button>("vectorPropertiesEditMask")); await f.Tick();
        f.t.Equal("Edit Mask", Toggle().text, "Inspector can finish editing");
        string finishedId = f.layerId, finished = JsonUtility.ToJson(f.Vector);
        await Dab(a + new Vector2(-50, 0));
        f.t.Equal(2, f.document.layers.Count, "Next LMB after Finish starts a new layer");
        f.t.Equal(finished, JsonUtility.ToJson(f.Vector), "Finished figure is untouched by the next stroke");
        f.layerId = (string)Read(f.window, "selectedLayerId");
        f.t.True(f.layerId != finishedId, "New figure has its own layer ID");
        Call(f.window, "SelectOnlyLayer", finishedId); Call(f.window, "RefreshToolkitInterface", true); await f.Tick();
        f.layerId = finishedId;
        f.t.Equal("Edit Mask", Toggle().text, "Changing selection ends the previous editing session");
        SubmitDash(((VisualElement)Read(f.window, "toolkitLayerSettingsScroll")).Q<Button>("vectorPropertiesEditMask")); await f.Tick();
        before = JsonUtility.ToJson(f.Vector); transform = f.Layer.transform;
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 220;
        f.Fence(); await Dab(a, 1);
        f.t.Equal(0, f.Vector.shapes[0].contours.Count, "Erasing everything retains an empty editable figure");
        f.t.True(f.Vector.brushDraft != null, "Empty figure retains its editable mask");
        await f.UndoRedo(before, JsonUtility.ToJson(f.Vector), transform, "Erase whole mask", f.Layer.transform);
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 20;
        await Dab(a); f.t.True(f.Vector.shapes[0].contours.Count > 0, "An empty figure can be painted again");
        f.t.Equal(2, f.document.layers.Count, "Painting after full erase still uses the same layer");
        f.Key(KeyCode.Return); await f.Tick();
        await BrushCursorPixels(f);
        var probe = new StrokeCapture { owner = f.window, path = "Temp/WhimTex/vector-mask-editing.png", pickingMode = PickingMode.Ignore };
        probe.style.position = Position.Absolute; probe.style.left = 0; probe.style.top = 0; probe.style.width = 1; probe.style.height = 1;
        f.window.rootVisualElement.Add(probe);
        try
        {
            for (int i = 0; i < 40 && !probe.done.Task.IsCompleted; i++) { f.window.Repaint(); await f.Tick(); }
            f.t.True(probe.done.Task.IsCompleted, "Owned mask editing controls captured"); await probe.done.Task;
        }
        finally { probe.RemoveFromHierarchy(); }
    }

    static async Task BrushCursor(Fixture f)
    {
        var inspector = (VisualElement)Read(f.window, "toolkitLayerSettingsScroll");
        f.t.True(inspector.Query<Foldout>().ToList().Any(v => v.text == "Original Mask"), "Retained mask uses the Original Mask heading");
        var size = f.window.rootVisualElement.Q<FloatField>("vectorBrushSize");
        f.t.True(size != null, "Vector brush size control is available");
        var paint = Read(f.window, "paintSettings");
        var oldTool = Read(paint, "tool");
        try
        {
            Write(paint, "tool", PaintToolMode.Eraser);
            foreach (float zoom in new[] { 25f, 100f, 200f })
            {
                Call(f.window, "SetCanvasZoomPercent", zoom);
                await f.Tick();
                Vector2 center = f.canvas.contentRect.center;
                f.Pointer(EventType.MouseMove, center);
                Call(f.window, "UpdateToolkitCanvasPresentation"); await f.Tick();
                var cursorOverlay = (VisualElement)Read(f.canvas, "overlay");
                f.t.True(cursorOverlay.resolvedStyle.display != DisplayStyle.None && cursorOverlay.worldBound.width > 0,
                    "Vector cursor overlay is actually displayed after refreshing the canvas without raster settings: " +
                    cursorOverlay.resolvedStyle.display + ", width=" + cursorOverlay.worldBound.width +
                    ", visible=" + Read(f.canvas, "cursorVisible") + ", helper=" +
                    (f.canvas.GetType().GetMethod("UpdateOverlayVisibility", Instance) != null));
                f.t.True((bool)Read(f.canvas, "cursorVisible"), "Vector brush shows its circle on hover");
                f.t.True((bool)Read(f.canvas, "toolCursorHidden"), "System arrow is hidden over the vector brush canvas");
                f.t.True(!(bool)Read(f.canvas, "cursorErase"), "Raster eraser setting does not color the vector cursor");
                f.t.True(Read(f.canvas, "brushSettings") == null, "Vector cursor does not require raster paint settings");
                foreach (float diameter in new[] { 8f, 24f, 96f })
                {
                    size.value = diameter;
                    f.t.Near(Mathf.Max(2f, diameter * zoom / 200f), (float)Property(f.canvas, "RoundCursorRadius"), .001,
                        "Circle radius responds immediately to Size and zoom (minimum 2 screen points)");
                }
                f.Pointer(EventType.MouseMove, center, EventModifiers.Alt);
                f.t.True(!(bool)Read(f.canvas, "cursorVisible") && !(bool)Read(f.canvas, "toolCursorHidden"), "Alt restores the navigation cursor");
                f.Pointer(EventType.MouseMove, center);
                using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = f.canvas; f.canvas.SendEvent(leave); }
                f.t.True(!(bool)Read(f.canvas, "cursorVisible") && !(bool)Read(f.canvas, "toolCursorHidden"), "Pointer leave hides the circle and restores the arrow");
            }
            Call(f.window, "SetCanvasZoomPercent", 100f);
            f.Pointer(EventType.MouseMove, f.canvas.contentRect.center);
            await f.Tool("VectorPen");
            f.t.True(!(bool)Read(f.canvas, "cursorVisible"), "Pen does not inherit a brush circle");
            await f.Tool("VectorBrush");
            f.Pointer(EventType.MouseMove, f.canvas.contentRect.center);
            f.t.True((bool)Read(f.canvas, "cursorVisible"), "Switching back restores the circle");
            f.t.Equal(96f, (float)Read(f.canvas, "roundCursorSize"), "Switching tools retains vector brush size");
            BrushSizeShortcuts(f, size);
        }
        finally { Write(paint, "tool", oldTool); }
    }

    static async Task BrushCursorPixels(Fixture f)
    {
        var toggle = f.window.rootVisualElement.Q<Button>("vectorBrushEditMask");
        if (toggle.text == "Edit Mask") { SubmitDash(toggle); await f.Tick(); }
        f.window.rootVisualElement.Q<FloatField>("vectorBrushSize").value = 40;
        Vector2 center = f.canvas.contentRect.center + new Vector2(80, 60);
        async Task<StrokeCapture> Capture(string name)
        {
            var capture = new StrokeCapture { owner = f.window, retainPixels = true,
                path = "Temp/WhimTex/vector-cursor-" + name + ".png", pickingMode = PickingMode.Ignore };
            capture.style.position = Position.Absolute; capture.style.left = 0; capture.style.top = 0;
            capture.style.width = 1; capture.style.height = 1; f.window.rootVisualElement.Add(capture);
            try
            {
                for (int i = 0; i < 40 && !capture.done.Task.IsCompleted; i++) { f.window.Repaint(); await f.Tick(); }
                f.t.True(capture.done.Task.IsCompleted, "Owned cursor pixels captured: " + name);
                await capture.done.Task; return capture;
            }
            finally { capture.RemoveFromHierarchy(); }
        }
        f.Pointer(EventType.MouseMove, center, EventModifiers.Alt); await f.Tick();
        var hidden = await Capture("hidden");
        f.Pointer(EventType.MouseMove, center);
        Call(f.window, "UpdateToolkitCanvasPresentation"); await f.Tick();
        var hover = await Capture("hover");
        f.Pointer(EventType.MouseDown, center); await f.Tick();
        // Keep the same held-stroke preview in both images so its white fill cannot
        // accidentally satisfy the cursor-pixel assertion.
        Call(f.window, "UpdateCanvasCursor", center, true); await f.Tick();
        var drawingHidden = await Capture("drawing-hidden");
        Call(f.window, "UpdateCanvasCursor", center, false); await f.Tick();
        var drawing = await Capture("drawing");
        f.Key(KeyCode.Escape); f.Pointer(EventType.MouseUp, center); await f.Tick();
        f.Pointer(EventType.MouseDown, center, button: 1); await f.Tick();
        var erase = await Capture("erase");
        f.Key(KeyCode.Escape); f.Pointer(EventType.MouseUp, center, button: 1); await f.Tick();
        f.t.Equal(hidden.imageWidth, hover.imageWidth, "Captured viewport width remains fixed");
        f.t.Equal(hidden.imageWidth, drawing.imageWidth, "Drawing capture width remains fixed");
        f.t.Equal(hidden.imageHeight, drawing.imageHeight, "Drawing capture height remains fixed");
        f.t.Equal(drawingHidden.imageWidth, drawing.imageWidth, "Held-stroke comparison width remains fixed");
        f.t.Equal(drawingHidden.imageHeight, drawing.imageHeight, "Held-stroke comparison height remains fixed");
        f.t.Equal(hidden.imageHeight, erase.imageHeight, "Captured viewport height remains fixed");
        Vector2 panel = f.canvas.LocalToWorld(center) - f.window.rootVisualElement.worldBound.min;
        float sx = hover.imageWidth / f.window.rootVisualElement.worldBound.width;
        float sy = hover.imageHeight / f.window.rootVisualElement.worldBound.height;
        int cx = Mathf.RoundToInt(panel.x * sx), cy = Mathf.RoundToInt(hover.imageHeight - panel.y * sy);
        int radius = Mathf.CeilToInt(26 * Mathf.Max(sx, sy)), white = 0, drawingWhite = 0, red = 0;
        float cursorRadius = (float)Property(f.canvas, "RoundCursorRadius");
        for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(hover.imageHeight, cy + radius); y++)
        for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(hover.imageWidth, cx + radius); x++)
        {
            int index = y * hover.imageWidth + x;
            var a = hidden.pixels[index]; var b = hover.pixels[index]; var c = erase.pixels[index];
            if (b.r > a.r + 40 && b.g > a.g + 40 && b.b > a.b + 40) white++;
            var d = drawing.pixels[index]; var e = drawingHidden.pixels[index];
            float ringDistance = new Vector2((x + .5f - cx) / sx, (y + .5f - cy) / sy).magnitude;
            if (Mathf.Abs(ringDistance - cursorRadius) <= 1.5f &&
                d.r > e.r + 40 && d.g > e.g + 40 && d.b > e.b + 40) drawingWhite++;
            if (c.r > c.g + 45 && c.r > c.b + 45 && c.r > a.r + 35) red++;
        }
        f.t.True(white >= 20, "Hover draws visible white cursor pixels, not only a visible flag; pixels=" + white);
        f.t.True(drawingWhite >= 20, "Drawing retains visible white cursor pixels on its circumference; pixels=" + drawingWhite);
        f.t.True(red >= 20, "Erasing draws visible red cursor pixels; pixels=" + red);
        f.Key(KeyCode.Return); await f.Tick();
    }

    static void BrushSizeShortcuts(Fixture f, FloatField size)
    {
        var paint = Read(f.window, "paintSettings");
        string rasterSettings = JsonUtility.ToJson(paint);
        string documentBefore = JsonUtility.ToJson(f.Vector);
        void Key(KeyCode code, char character = '\0', EventModifiers modifiers = EventModifiers.None, VisualElement target = null)
        {
            using var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code, character = character, modifiers = modifiers });
            evt.target = target ?? f.canvas; (target ?? f.canvas).SendEvent(evt);
        }
        void Check(float expected)
        {
            f.t.Equal(expected, (float)Read(f.window, "vectorBrushSize"), "Shortcut changes vector size");
            f.t.Equal(expected, size.value, "Size field is synchronized immediately");
            f.t.Near(Mathf.Max(2f, expected * .5f), (float)Property(f.canvas, "RoundCursorRadius"), .001,
                "Circle updates without moving the pointer");
        }
        f.t.True(size.ClassListContains("whimtex-brush-size"), "Size uses the common brush field styling");
        size.value = 20; Key(KeyCode.RightBracket); Check(22);
        Key(KeyCode.RightBracket); Check(24);
        Key(KeyCode.LeftBracket); Check(22);
        size.value = 7.6f; Key(KeyCode.RightBracket); Check(9);
        size.value = 1; Key(KeyCode.LeftBracket); Check(1);
        size.value = 2048; Key(KeyCode.RightBracket); Check(2048);
        size.value = 20; Key(KeyCode.None, ']'); Check(22);
        Key(KeyCode.None, '['); Check(20);
        foreach (var modifier in new[] { EventModifiers.Control, EventModifiers.Command, EventModifiers.Alt })
        { Key(KeyCode.RightBracket, '\0', modifier); Check(20); }
        var text = new TextField(); f.window.rootVisualElement.Add(text);
        try { Key(KeyCode.RightBracket, ']', target: text); Check(20); }
        finally { text.RemoveFromHierarchy(); }
        f.t.Equal(rasterSettings, JsonUtility.ToJson(paint), "Vector size shortcuts leave raster brush settings unchanged");
        f.t.Equal(documentBefore, JsonUtility.ToJson(f.Vector), "Size shortcuts do not change existing figures or masks");
    }

    static async Task Pen(Fixture f)
    {
        await f.Tool("VectorPen");
        var a = new Vector2(.22f, .3f); var b = new Vector2(.5f, .65f); var c = new Vector2(.78f, .3f);
        f.Fence(); f.Click(a);
        Vector2 bv = f.View(b), tangentEnd = f.View(b + new Vector2(.07f, .04f));
        f.Pointer(EventType.MouseDown, bv); f.Pointer(EventType.MouseDrag, tangentEnd); f.Pointer(EventType.MouseUp, tangentEnd);
        f.Click(c);
        f.t.Equal(0, f.document.layers.Count, "Pending pen path does not insert a layer");
        f.Key(KeyCode.Return);
        f.t.Equal(1, f.document.layers.Count, "Enter inserts one vector layer");
        f.layerId = f.document.layers[0].Id;
        VectorShape shape = f.Vector.shapes[0]; VectorContour contour = shape.contours[0];
        f.t.Equal(3, contour.points.Count, "Three actual pointer gestures create three editable anchors");
        f.t.True(!contour.closed && shape.fillMode == VectorFillMode.None && shape.stroke, "Enter finishes an open stroked path");
        Near(f.t, a, f.Layer.transform.Map(contour.points[0].position,f.Size), "Pen retains canvas positions when fitting the frame");
        Near(f.t, b, f.Layer.transform.Map(contour.points[1].position,f.Size), "Dragging does not move the pen anchor");
        Near(f.t, b+new Vector2(.07f, .04f), f.Layer.transform.Map(contour.points[1].position+contour.points[1].outTangent,f.Size), "Dragging creates a tangent");
        Near(f.t, -contour.points[1].outTangent, contour.points[1].inTangent, "Pen tangent pair is mirrored");
        string completed = JsonUtility.ToJson(f.Vector);
        Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await f.Tick();
        f.t.Equal(0, f.document.layers.Count, "One Undo removes the complete new-layer pen transaction");
        Undo.PerformRedo(); await f.Tick();
        f.t.Equal(completed, JsonUtility.ToJson(f.Vector), "Redo restores exact pen geometry and identities");
        // Undo removed the selected layer; the editor intentionally clears stale selection.
        Call(f.window, "SelectOnlyLayer", f.layerId);
        Call(f.window, "RefreshToolkitInterface", true);
        TextureTransform transform = f.Layer.transform;
        await f.Tool("VectorPen");
        f.Fence(); f.Click(new Vector2(.2f, .2f)); f.Click(new Vector2(.8f, .2f)); f.Click(new Vector2(.5f, .75f)); f.Click(new Vector2(.2f, .2f));
        f.t.Equal(2, f.document.layers.Count, "Clicking the first anchor completes a new layer");
        f.t.True(f.Layer.transform.Equals(transform), "Pen does not rewrite an existing layer transform");
        f.layerId=(string)Read(f.window,"selectedLayerId");
        f.t.Equal(1,f.Vector.shapes.Count,"One closed figure per new layer");
        f.t.True(f.Vector.shapes[0].contours[0].closed && f.Vector.shapes[0].fillMode == VectorFillMode.Solid, "Closed pen path retains its fill");
        await f.Tool("None");
        await f.NoUndo(async () =>
        {
            f.Click(new Vector2(1.5f, 1.5f));
            f.t.True(Read(f.window, "selectedLayerId") == null, "Empty-space click clears layer selection");
            f.Click(new Vector2(.5f, .36f));
            f.t.Equal(f.layerId, (string)Read(f.window, "selectedLayerId"), "Layer Select picks the filled figure");
            await f.Tick();
        }, "Selection-only clicks");
    }

    static async Task StaleLayer(Fixture f)
    {
        await f.Seed(); await f.Tool("VectorNodes");
        Vector2 start = f.View(f.Vector.shapes[0].contours[0].points[0].position), end = start + new Vector2(30, -15);
        f.Pointer(EventType.MouseDown, start); f.Pointer(EventType.MouseDrag, end);
        f.t.True((bool)Property(f.Manipulator, "IsDragging"), "Existing layer has an active gesture before removal");
        Layer removed = f.Layer;
        f.detachedLayer = removed;
        f.document.layers.Remove(removed); Call(f.document, "MarkChanged");
        f.Pointer(EventType.MouseUp, end);
        f.t.True(f.Layer == null && f.document.layers.Count == 0,
            "Finishing a stale gesture cannot resurrect a removed existing layer as a new layer");
        f.t.True(!f.canvas.HasPointerCapture(PointerId.mousePointerId), "Stale gesture releases capture");
        await f.Tick();
    }

    static async Task FocusLoss(Fixture f)
    {
        await f.Seed(); await f.Tool("VectorPen");
        string before = JsonUtility.ToJson(f.Vector);
        await f.NoUndo(async () =>
        {
            f.Click(new Vector2(.25f, .25f)); f.Click(new Vector2(.65f, .65f));
            f.t.True((bool)Property(f.Manipulator, "IsDragging"), "Pending pen path exists with no button held");
            f.focusWindow = ScriptableObject.CreateInstance<VectorToolsFocusWindow>();
            f.focusWindow.ShowUtility(); f.focusWindow.position = new Rect(160, 120, 240, 120); f.focusWindow.Focus();
            for (int i = 0; i < 6; i++) await f.Tick();
            f.t.True(EditorWindow.focusedWindow == f.focusWindow, "A second owned window actually received focus");
            f.t.True(!(bool)Property(f.Manipulator, "IsDragging"), "Losing window focus cancels a pending pen path");
            f.window.Focus(); f.canvas.Focus(); f.Key(KeyCode.Return);
            f.t.Equal(before, JsonUtility.ToJson(f.Vector), "Refocus and Enter cannot commit an abandoned path");
        }, "Focus-loss cancellation");
    }

    static void Near(TestContext t, Vector2 expected, Vector2 actual, string label)
    { t.Near(expected.x, actual.x, .0002, label + " X"); t.Near(expected.y, actual.y, .0002, label + " Y"); }

    static void CheckMapped(TestContext t, VectorShape before, VectorShape after, Func<Vector2, Vector2> map, string label)
    {
        t.Equal(before.id, after.id, label + " keeps shape identity");
        t.Equal(before.contours.Count, after.contours.Count, label + " keeps contours");
        for (int c = 0; c < before.contours.Count; c++)
        {
            t.Equal(before.contours[c].points.Count, after.contours[c].points.Count, label + " keeps anchors");
            for (int i = 0; i < before.contours[c].points.Count; i++)
            {
                VectorPoint a = before.contours[c].points[i], b = after.contours[c].points[i];
                t.Equal(a.id, b.id, label + " keeps anchor identity");
                Near(t, map(a.position), b.position, label);
                Near(t, map(a.position + a.inTangent), b.position + b.inTangent, label + " incoming handle");
                Near(t, map(a.position + a.outTangent), b.position + b.outTangent, label + " outgoing handle");
            }
        }
    }

    static Rect Bounds(VectorShape shape)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (var contour in shape.contours) foreach (var point in contour.points)
        { min = Vector2.Min(min, point.position); max = Vector2.Max(max, point.position); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static string Style(VectorShape shape)
    {
        VectorShape copy = shape.Clone(); copy.contours.Clear();
        // Anchor coordinates are rebased along with the geometry; the palette/style remains unchanged.
        copy.gradientStart=Vector2.zero;copy.gradientEnd=Vector2.right;
        copy.strokeGradientStart=Vector2.zero;copy.strokeGradientEnd=Vector2.right;return JsonUtility.ToJson(copy);
    }
}

public sealed class VectorToolsFocusWindow : EditorWindow { }
