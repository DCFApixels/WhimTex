using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// run_script entry: ContextToolsTests.Main. Only an owned window and in-memory objects.
public static class ContextToolsTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type WindowType = typeof(TextureCompositorWindow);
    static object Tool(string name) => Enum.Parse(WindowType.GetNestedType("CanvasTool", BindingFlags.NonPublic), name);
    static object Read(object target, string field) => target.GetType().GetField(field, Instance).GetValue(target);
    static void Write(object target, string field, object value) => target.GetType().GetField(field, Instance).SetValue(target, value);
    static object Call(object target, string method, params object[] args)
    {
        var info = target.GetType().GetMethod(method, Instance);
        var parameters = info.GetParameters();
        int supplied = args.Length;
        Array.Resize(ref args, parameters.Length);
        for (int i = supplied; i < args.Length; i++) args[i] = parameters[i].DefaultValue;
        return info.Invoke(target, args);
    }
    static bool Property(object target, string name) => (bool)target.GetType().GetProperty(name, Instance).GetValue(target);

    sealed class ToolbarLayoutTrace : IDisposable
    {
        readonly TextureCompositorWindow window;
        readonly VisualElement root;
        readonly ScrollView scroll;
        readonly VisualElement button;
        readonly string path;
        readonly Action<ToolbarLayoutTrace, string> probe;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        readonly List<string> lines = new List<string>();
        readonly List<(VisualElement element, EventCallback<GeometryChangedEvent> callback)> callbacks =
            new List<(VisualElement, EventCallback<GeometryChangedEvent>)>();
        int updates;
        bool disposed;
        bool disposing;
        bool updateSubscribed;
        string previous;
        public int ActiveSubscriptions => callbacks.Count + (updateSubscribed ? 1 : 0);
        public int GeometryCallbackCount { get; private set; }
        public ToolbarLayoutTrace(TextureCompositorWindow owner, string output,
            Action<ToolbarLayoutTrace, string> faultProbe = null)
        {
            window = owner; path = output; probe = faultProbe;
            root = owner.rootVisualElement;
            scroll = root.Q<ScrollView>("canvasToolScroll");
            button = root.Q<Button>("temporaryCanvasTool");
            try
            {
                Observe(root, "root"); Observe(scroll, "scroll");
                Observe(scroll.contentViewport, "viewport"); Observe(scroll.contentContainer, "content");
                Observe(button, "button");
                EditorApplication.update += Update; updateSubscribed = true;
                Sample("attached");
            }
            catch (Exception error)
            {
                disposed = true;
                var failures = new List<Exception> { error };
                Detach(failures);
                throw new AggregateException("Owned toolbar trace setup failed.", failures);
            }
        }
        void Observe(VisualElement element, string name)
        {
            if (element == null) throw new InvalidOperationException("Missing owned trace element: " + name);
            EventCallback<GeometryChangedEvent> callback = evt =>
            {
                GeometryCallbackCount++;
                Sample("geometry:" + name + ":" + evt.oldRect + "->" + evt.newRect);
            };
            callbacks.Add((element, callback)); element.RegisterCallback(callback);
        }
        string State() => "window=" + (window != null ? window.position.ToString() : "<destroyed>") + ";root=" + root.worldBound
            + ";viewport=" + scroll.contentViewport.worldBound + ";content=" + scroll.contentContainer.worldBound
            + ";offset=" + scroll.scrollOffset + ";range=" + scroll.verticalScroller.lowValue + "," + scroll.verticalScroller.highValue
            + ";button=" + button.worldBound + ";display=" + button.resolvedStyle.display
            + ";selected=" + scroll.Q<Button>(className: "whimtex-tool-button--selected")?.name;
        public void Sample(string reason)
        {
            if (disposed || lines.Count >= 1024) return;
            probe?.Invoke(this, reason);
            previous = State();
            lines.Add(clock.ElapsedMilliseconds + "ms;update=" + updates + ";" + reason + ";" + previous);
        }
        public async Task AwaitResizedPanel(int width, int height, System.Threading.CancellationToken cancellation)
        {
            var budget = System.Diagnostics.Stopwatch.StartNew();
            while (Mathf.Abs(window.rootVisualElement.worldBound.width - width) > .1f ||
                Mathf.Abs(window.rootVisualElement.worldBound.height - height) > .1f)
            {
                if (budget.ElapsedMilliseconds >= 1000) throw new TimeoutException("Owned panel did not apply the requested resize.");
                await WhimTex.Tests.UnityA.UnityAAsync.Delay(10, cancellation);
            }
            // Geometry schedules the product's reveal. Join the following panel scheduler turn,
            // not the assertion's desired button position; a broken ScrollTo still fails below.
            var completed = new TaskCompletionSource<bool>();
            var barrier = scroll.schedule.Execute(() => completed.TrySetResult(true));
            try
            {
                int remaining = Math.Max(1, 1000 - (int)budget.ElapsedMilliseconds);
                if (await Task.WhenAny(completed.Task, WhimTex.Tests.UnityA.UnityAAsync.Delay(remaining, cancellation)) != completed.Task)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Owned panel scheduler did not complete after resize.");
                }
                await completed.Task;
                Sample("minimum-panel-scheduler-completed");
            }
            finally { barrier.Pause(); }
        }
        void Update()
        {
            if (disposed) return;
            updates++;
            if (State() != previous) Sample("editor-update");
        }
        static void Attempt(List<Exception> failures, Action action)
        {
            try { action(); } catch (Exception error) { failures.Add(error); }
        }
        void Detach(List<Exception> failures)
        {
            if (updateSubscribed)
                Attempt(failures, () => { EditorApplication.update -= Update; updateSubscribed = false; });
            for (int i = callbacks.Count - 1; i >= 0; i--)
            {
                var item = callbacks[i];
                try { item.element.UnregisterCallback(item.callback); callbacks.RemoveAt(i); }
                catch (Exception error) { failures.Add(error); }
            }
        }
        public void Dispose()
        {
            if (disposing || (disposed && ActiveSubscriptions == 0)) return;
            disposing = true;
            var failures = new List<Exception>();
            try
            {
                bool firstDispose = !disposed;
                if (firstDispose) Attempt(failures, () => Sample("finished"));
                disposed = true;
                Detach(failures);
                if (firstDispose)
                {
                    Attempt(failures, () => System.IO.File.WriteAllLines(path, lines));
                    Attempt(failures, () => Debug.Log("WhimTex ContextTools layout trace: " + path));
                }
            }
            finally { disposing = false; }
            if (failures.Count != 0) throw new AggregateException("Owned toolbar trace cleanup failed.", failures);
        }
    }

    static void SendTraceGeometryEvents(TextureCompositorWindow window)
    {
        var root = window.rootVisualElement;
        var scroll = root.Q<ScrollView>("canvasToolScroll");
        foreach (var element in new[] { root, scroll, scroll.contentViewport, scroll.contentContainer,
            root.Q<Button>("temporaryCanvasTool") })
        {
            using var evt = GeometryChangedEvent.GetPooled(element.layout, element.layout);
            evt.target = element;
            element.SendEvent(evt);
        }
    }

    private static async Task<string> BodyRun()
    {
        string[] prefs = { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool" };
        var previousPrefs = new string[prefs.Length];
        for (int i = 0; i < prefs.Length; i++) previousPrefs[i] = EditorPrefs.HasKey(prefs[i]) ? EditorPrefs.GetString(prefs[i]) : null;
        var previousFocus = EditorWindow.focusedWindow;
        TextureCompositorWindow window = null;
        ShaderFX fx = null;
        ToolbarLayoutTrace trace = null;
        int checks = 0;
        void Check(bool ok, string message) { T.True(ok, message); }
        try
        {
            window = Scope.OwnWindow(ScriptableObject.CreateInstance<TextureCompositorWindow>());
            var document = (TextureCompositor)Read(window, "compositor");
            document.width = document.height = 32;
            var first = new GradientLayerBehaviour();
            var second = new GradientLayerBehaviour();
            var ordinary = new NoiseLayerBehaviour();
            foreach (var layer in new[] { first.Owner, second.Owner, ordinary.Owner })
            {
                Write(layer, "id", Guid.NewGuid().ToString("N"));
                document.layers.Add(layer);
            }
            window.ShowUtility();
            window.position = new Rect(120, 120, 900, 650);
            window.CreateGUI();
            void Refresh() => Call(window, "RefreshToolkitInterface");
            void Select(Layer layer, bool multi = false)
            {
                Write(window, "selectedLayerId", layer?.Id);
                Write(window, "selectedLayerIds", multi ? new List<string> { ordinary.Id, layer.Id } :
                    layer == null ? new List<string>() : new List<string> { layer.Id });
                Refresh();
            }
            void SetTool(string name) => Call(window, "SetCanvasTool", Tool(name));
            void IsTool(string name, string message) => Check(Read(window, "canvasTool").ToString() == name, message);
            void Escape()
            {
                using var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                Call(window, "OnToolkitKeyDown", key);
            }
            void TogglePrevious()
            {
                var shortcut = (KeyCode)Convert.ToInt32(WindowType.GetField("CanvasToolToggleKey", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue());
                using var down = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = shortcut });
                using var up = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = shortcut });
                Call(window, "OnToolkitKeyDown", down);
                Call(window, "OnToolkitKeyUp", up);
            }
            bool Visible(string name) => !window.rootVisualElement.Q<Button>(name).ClassListContains("whimtex-context-tool--hidden");
            Select(ordinary.Owner);
            SetTool("Brush");
            Check(!Visible("gradientHandlesTool") && !Visible("temporaryCanvasTool"), "No unrelated context buttons");
            Select(first.Owner);
            IsTool("GradientHandles", "Gradient auto-selected");
            Check(Visible("gradientHandlesTool") && Property(window, "IsGradientCanvasEnabled"), "Gradient button and handles visible");
            Check(!Property(window, "IsCanvasPaintTool"), "Base brush does not receive contextual clicks");
            Check(EditorPrefs.GetString(prefs[0]) == "Brush", "Context does not replace base preference");
            SetTool("Pencil"); Refresh(); Call(window, "Update");
            IsTool("Pencil", "Explicit base choice survives refresh and update");
            Select(second.Owner, true);
            IsTool("GradientHandles", "Active gradient auto-selected in a multiple selection");
            Escape(); IsTool("Pencil", "Context Escape restores last base");
            SetTool("GradientHandles"); SetTool("GradientHandles");
            IsTool("Pencil", "Active context button toggles off");
            Select(ordinary.Owner); Write(window, "uvEnabled", true); Refresh();
            Check(Visible("uvIslandSelectTool"), "UV toggle exposes contextual tool");
            IsTool("Pencil", "UV visibility does not select it");
            SetTool("UvIslandSelect");
            Check(Property(window, "IsUvSelectionTool"), "Separate UV selection tool");
            Write(window, "uvEnabled", false); Refresh();
            IsTool("Pencil", "Disabling UV returns base");
            Check(!Visible("uvIslandSelectTool"), "UV button hidden when disabled");
            Write(window, "uvEnabled", true); Refresh(); SetTool("UvIslandSelect");
            TogglePrevious(); IsTool("Pencil", "Previous shortcut exits UV context to preceding tool");
            TogglePrevious(); IsTool("UvIslandSelect", "Previous shortcut restores available UV context");
            Write(window, "uvEnabled", false); Refresh(); TogglePrevious();
            IsTool("Pencil", "Previous shortcut never reactivates unavailable UV context");

            fx = Scope.OwnObject(ScriptableObject.CreateInstance<ShaderFX>());
            fx.hideFlags = HideFlags.HideAndDontSave;
            var point = new ShaderFXParameter { name = "_Point", type = ShaderFXParameterType.Point, vectorValue = new Vector4(.5f, .5f, 0, 0) };
            var normal = new ShaderFXParameter { name = "_Normal", type = ShaderFXParameterType.Normal, vectorValue = new Vector4(0, 0, 1, 0) };
            var transform = new ShaderFXParameter { name = "_Transform", type = ShaderFXParameterType.Transform2D };
            Write(fx, "parameters", new List<ShaderFXParameter> { point, normal, transform });
            first.Owner.modifiers.Add(fx);
            Select(first.Owner, true);
            void Activate(string kind, ShaderFXParameter parameter) => Call(window, "ActivateTemporaryTool", Tool(kind), fx, parameter.id);
            SetTool("Brush"); SetTool("SmudgeBrush"); Activate("FXPoint", point);
            TogglePrevious(); IsTool("SmudgeBrush", "Previous shortcut exits temporary FX to its return tool");
            TogglePrevious(); IsTool("Brush", "Temporary FX does not replace ordinary tool history");
            TogglePrevious(); IsTool("SmudgeBrush", "Ordinary tool history still switches both ways after FX");
            SetTool("Brush");
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            var previewCanvas = (VisualElement)Read(window, "toolkitCanvas");
            Rect stableCanvas = previewCanvas.worldBound;
            async Task CheckCanvasLayout(string name)
            {
                await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
                var settingsPanel = window.rootVisualElement.Q<VisualElement>("canvasToolSettings");
                Check(Mathf.Abs(window.rootVisualElement.Q("canvasToolSettingsSpace").resolvedStyle.height - 28f) < .1f,
                    name + " reserves a fixed 28px settings row");
                Check(settingsPanel.resolvedStyle.height >= 28f, name + " settings panel remains visible");
                var topRail = (VisualElement)Read(window, "canvasGuideTopRail");
                var leftRail = (VisualElement)Read(window, "canvasGuideLeftRail");
                Check(Mathf.Abs(topRail.worldBound.yMin - Mathf.Max(settingsPanel.worldBound.yMax, previewCanvas.worldBound.yMin)) < .1f,
                    name + " guide rail follows settings bottom");
                Check(Mathf.Abs(leftRail.worldBound.yMin - topRail.worldBound.yMax) < .1f,
                    name + " left guide rail starts below top rail");
                Check(window.rootVisualElement.panel.Pick(topRail.worldBound.center) == topRail,
                    name + " guide rail is not covered by settings");
                var guideManipulator = Read(window, "canvasGuideManipulator");
                Check((int)Call(guideManipulator, "RailAt", previewCanvas.WorldToLocal(topRail.worldBound.center)) == 1,
                    name + " moved horizontal rail hit-test follows its visual");
                Check((int)Call(guideManipulator, "RailAt", previewCanvas.WorldToLocal(leftRail.worldBound.center)) == 0,
                    name + " vertical rail hit-test follows its visual");
                foreach (var row in settingsPanel.Children())
                {
                    if (row.resolvedStyle.display == DisplayStyle.None) continue;
                    foreach (var control in row.Children())
                        if (control.resolvedStyle.display != DisplayStyle.None)
                            Check(control.worldBound.yMin >= settingsPanel.worldBound.yMin - .1f &&
                                control.worldBound.yMax <= settingsPanel.worldBound.yMax + .1f,
                                name + " control fits settings height: " + control.GetType().Name + " " + control.worldBound + " panel " + settingsPanel.worldBound);
                }
                Check(Mathf.Abs(previewCanvas.worldBound.y - stableCanvas.y) < .1f &&
                    Mathf.Abs(previewCanvas.worldBound.height - stableCanvas.height) < .1f,
                    name + " preserves preview position and height: " + previewCanvas.worldBound + " vs " + stableCanvas);
            }
            foreach (string name in new[] { "None", "Pencil", "BlurBrush", "HealingBrush", "Fill", "Transform", "Zoom", "Shape", "RectangleSelect", "PolygonSelect", "Brush" })
            {
                SetTool(name); await CheckCanvasLayout(name);
            }
            Write(window, "uvEnabled", true); Refresh(); SetTool("UvIslandSelect");
            await CheckCanvasLayout("UV Island Select");
            Write(window, "uvEnabled", false); Refresh();
            SetTool("GradientHandles"); await CheckCanvasLayout("Gradient Handles");
            Check(window.rootVisualElement.Q("canvasToolSettings").Children()
                .All(row => row.resolvedStyle.display == DisplayStyle.None), "Parameterless tool leaves the fixed panel empty");
            Activate("FXPoint", point); await CheckCanvasLayout("FX Point");
            Activate("FXNormal", normal); await CheckCanvasLayout("FX Normal");
            Activate("FXTransform", transform); await CheckCanvasLayout("FX Transform");
            Escape();
            Activate("FXPoint", point);
            IsTool("FXPoint", "Edit on Canvas selects temporary point");
            Check(Visible("temporaryCanvasTool"), "Temporary button appears only after activation");
            Check(!Property(window, "IsGradientCanvasEnabled") && !Property(window, "IsCanvasTransformEnabled"), "No competing manipulator active");
            Check(window.rootVisualElement.Q<Button>("temporaryCanvasTool").tooltip.Contains("_Point"), "Temporary description identifies parameter");
            var toolbar = window.rootVisualElement.Q<Button>("temporaryCanvasTool").parent;
            Check(toolbar[toolbar.childCount - 1].name == "temporaryCanvasTool", "Temporary slot is always last");
            Activate("FXNormal", normal); Activate("FXTransform", transform);
            Check(Property(window, "IsCanvasTransformEnabled"), "Temporary transform is enabled for active layer in multiple selection");
            Escape(); IsTool("GradientHandles", "Replacing temporary target preserves original return tool");
            Check(!Visible("temporaryCanvasTool"), "Temporary slot disappears on exit");
            Activate("FXPoint", point); Activate("FXPoint", point);
            IsTool("GradientHandles", "Repeated Edit on Canvas exits temporary tool");
            Activate("FXPoint", point); Call(window, "OnLostFocus");
            IsTool("FXPoint", "Window focus loss is not context loss");
            SetTool("Brush"); IsTool("Brush", "Explicit base choice wins over temporary return");
            Activate("FXNormal", normal); Escape(); IsTool("Brush", "Temporary returns to preceding base");
            Write(window, "uvEnabled", true); Refresh(); SetTool("UvIslandSelect");
            Activate("FXPoint", point); Escape(); IsTool("UvIslandSelect", "Temporary returns to UV context");
            Activate("FXPoint", point); Write(window, "uvEnabled", false); Refresh(); Escape();
            IsTool("Brush", "Missing return context falls back to base");
            SetTool("GradientHandles"); Activate("FXPoint", point);
            first.Owner.modifiers.Remove(fx); Refresh();
            IsTool("GradientHandles", "Removed FX returns to surviving gradient context");
            first.Owner.modifiers.Add(fx);
            Activate("FXPoint", point); Select(ordinary.Owner);
            IsTool("Brush", "Temporary target lost on active-layer change");
            Select(first.Owner); Activate("FXPoint", point); Select(second.Owner);
            IsTool("GradientHandles", "New gradient context replaces expired temporary tool");
            Select(first.Owner); Activate("FXPoint", point);
            point.type = ShaderFXParameterType.Float; Refresh();
            IsTool("GradientHandles", "Parameter type change invalidates temporary tool");
            point.type = ShaderFXParameterType.Point;

            // Exercise Escape during a real captured-pointer gesture and the corresponding Undo group.
            foreach (var pair in new[] { ("FXPoint", "pointManipulator", point), ("FXNormal", "normalManipulator", normal) })
            {
                Activate(pair.Item1, pair.Item3);
                var manipulator = Read(window, pair.Item2);
                var canvas = (VisualElement)Read(window, "toolkitCanvas");
                Vector4 original = pair.Item3.vectorValue;
                Undo.IncrementCurrentGroup();
                Write(manipulator, "undoGroup", Undo.GetCurrentGroup());
                Write(manipulator, "pointer", PointerId.mousePointerId);
                Write(manipulator, "effect", fx); Write(manipulator, "parameter", pair.Item3);
                Write(manipulator, "originalValue", original);
                canvas.CapturePointer(PointerId.mousePointerId);
                Undo.RecordObject(fx, "Context tools smoke gesture");
                pair.Item3.vectorValue = new Vector4(.2f, .7f, .4f, 0);
                TogglePrevious();
                IsTool(pair.Item1, "Previous shortcut does not interrupt a captured FX gesture");
                Check(Property(manipulator, "IsDragging") && canvas.HasPointerCapture(PointerId.mousePointerId),
                    "Blocked shortcut retains the active FX gesture and capture");
                Escape();
                IsTool(pair.Item1, "Escape cancels gesture, not temporary tool");
                Check(pair.Item3.vectorValue == original, "Escape restores parameter value");
                Check(!Property(manipulator, "IsDragging") && !canvas.HasPointerCapture(PointerId.mousePointerId), "Escape releases capture");
                Escape(); IsTool("GradientHandles", "Second Escape exits temporary tool");
            }
            first.gradientType = GradientLayerBehaviour.GradientType.Circular; Refresh();
            IsTool("Brush", "Unsupported gradient context returns base");
            Check(!Visible("gradientHandlesTool"), "Circular gradient has no handles tool");
            first.gradientType = GradientLayerBehaviour.GradientType.Horizontal;
            Select(second.Owner); Select(first.Owner); SetTool("Brush");
            window.CreateGUI(); Refresh();
            IsTool("Brush", "View rebuild preserves manual tool selection");
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            Check(window.rootVisualElement.Q<Button>("gradientHandlesTool").resolvedStyle.width > 0, "Visible context button has a layout");
            trace = new ToolbarLayoutTrace(window, System.IO.Path.Combine(Scope.Temp, "context-tools-layout.txt"));
            Scope.Finally(trace.Dispose);

            // A known, owned top Undo record makes this safe for the user's existing history.
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(fx, "Context tools smoke undo");
            point.vectorValue = new Vector4(.1f, .9f, 0, 0);
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Activate("FXPoint", point);
            int group = Undo.GetCurrentGroup();
            Escape();
            Check(Undo.GetCurrentGroup() == group, "Selecting and exiting tools adds no Undo group");
            Activate("FXPoint", point);
            Undo.PerformUndo(); Refresh();
            IsTool("FXPoint", "Parameter Undo does not restore a different selected tool");
            var parameters = (List<ShaderFXParameter>)Read(fx, "parameters");
            Check(parameters[0].vectorValue == new Vector4(.5f, .5f, 0, 0), "Undo restores owned parameter data");
            Undo.PerformRedo(); Refresh();
            IsTool("FXPoint", "Parameter Redo preserves temporary tool");
            parameters = (List<ShaderFXParameter>)Read(fx, "parameters");
            Check(parameters[0].vectorValue == new Vector4(.1f, .9f, 0, 0), "Redo restores owned parameter data");
            Write(window, "uvEnabled", true); Refresh();
            trace.Sample("before-minimum-resize");
            window.position = new Rect(120, 120, 640, 420);
            trace.Sample("minimum-resize-requested");
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            trace.Sample("minimum-after-original-100ms");
            await trace.AwaitResizedPanel(640, 420, Cancellation);
            var lastButton = window.rootVisualElement.Q<Button>("temporaryCanvasTool");
            Check(lastButton.worldBound.yMax <= window.rootVisualElement.worldBound.yMax,
                "Temporary tool fits minimum window: " + lastButton.worldBound + " root: " + window.rootVisualElement.worldBound);
            Escape();
            SetTool("Fill");
            await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            var narrowSettings = window.rootVisualElement.Q("canvasToolSettings");
            Check(narrowSettings.resolvedStyle.height > 28f, "Narrow settings wrap onto additional rows");
            Rect narrowCanvas = ((VisualElement)Read(window, "toolkitCanvas")).worldBound;
            var lastField = narrowSettings.Q<IntegerField>(className: "whimtex-fill-expand");
            Check(lastField.worldBound.xMin >= narrowSettings.worldBound.xMin - .1f &&
                lastField.worldBound.xMax <= narrowSettings.worldBound.xMax + .1f &&
                lastField.worldBound.yMax <= narrowSettings.worldBound.yMax + .1f,
                "Last setting fits inside the wrapped panel");
            Check(lastField.worldBound.center.y > narrowCanvas.yMin, "Extra rows overlap the preview");
            var picked = window.rootVisualElement.panel.Pick(lastField.worldBound.center);
            Check(picked == lastField || lastField.Contains(picked), "Overlapping settings receive input instead of the canvas");
            SetTool("GradientHandles"); await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
            Check(Mathf.Abs(narrowSettings.resolvedStyle.height - 28f) < .1f, "Empty panel collapses to one row");
            Check(((VisualElement)Read(window, "toolkitCanvas")).worldBound == narrowCanvas,
                "Collapsing wrapped settings does not move or resize the preview");
            // Repeat actual window resizes and tool switches after rebuilding the view.
            foreach (float width in new[] { 1280f, 640f, 900f, 640f })
            {
                window.position = new Rect(120, 120, width, 650);
                SetTool("Brush"); await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
                previewCanvas = (VisualElement)Read(window, "toolkitCanvas");
                stableCanvas = previewCanvas.worldBound;
                foreach (string name in new[] { "Fill", "Zoom", "Transform", "Pencil", "BlurBrush", "HealingBrush", "GradientHandles", "Brush" })
                {
                    SetTool(name); await CheckCanvasLayout(name + " at " + width);
                }
            }
            var constructorFault = new InvalidOperationException("Owned trace attached-sample probe.");
            ToolbarLayoutTrace constructorTrace = null;
            AggregateException constructorFailure = null;
            try
            {
                constructorTrace = new ToolbarLayoutTrace(window, System.IO.Path.Combine(Scope.Temp, "trace-constructor.txt"),
                    (instance, reason) =>
                    {
                        if (reason != "attached") return;
                        constructorTrace = instance;
                        Scope.Finally(instance.Dispose);
                        throw constructorFault;
                    });
                Scope.Finally(constructorTrace.Dispose);
            }
            catch (AggregateException error) { constructorFailure = error; }
            Check(constructorFailure != null && constructorFailure.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, constructorFault)),
                "Trace constructor preserves its original setup failure");
            Check(constructorTrace != null && constructorTrace.ActiveSubscriptions == 0,
                "Trace constructor rollback detaches all geometry/update subscriptions");
            int constructorCallbacks = constructorTrace.GeometryCallbackCount;
            SendTraceGeometryEvents(window);
            Check(constructorTrace.GeometryCallbackCount == constructorCallbacks,
                "Public geometry events do not invoke rolled-back trace callbacks");

            var disposalFault = new InvalidOperationException("Owned trace finished-sample probe.");
            var disposalTrace = new ToolbarLayoutTrace(window, System.IO.Path.Combine(Scope.Temp, "trace-dispose.txt"),
                (instance, reason) => { if (reason == "finished") throw disposalFault; });
            Scope.Finally(disposalTrace.Dispose);
            Check(disposalTrace.ActiveSubscriptions == 6, "Live trace owns five geometry callbacks and one update subscription");
            int liveCallbacks = disposalTrace.GeometryCallbackCount;
            SendTraceGeometryEvents(window);
            Check(disposalTrace.GeometryCallbackCount == liveCallbacks + 5, "Public geometry events reach all five live trace callbacks");
            AggregateException disposalFailure = null;
            try { disposalTrace.Dispose(); } catch (AggregateException error) { disposalFailure = error; }
            Check(disposalFailure != null && disposalFailure.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, disposalFault)),
                "Trace disposal preserves its sample failure while attempting cleanup");
            Check(disposalTrace.ActiveSubscriptions == 0, "Trace disposal detaches all subscriptions despite sample failure");
            int disposedCallbacks = disposalTrace.GeometryCallbackCount;
            SendTraceGeometryEvents(window);
            Check(disposalTrace.GeometryCallbackCount == disposedCallbacks, "Public geometry events do not invoke disposed trace callbacks");
            disposalTrace.Dispose();
            Check(disposalTrace.ActiveSubscriptions == 0, "Trace disposal is idempotent after reporting its original error");

            var bodyFault = new InvalidOperationException("Owned trace body-failure probe.");
            var cleanupFault = new InvalidOperationException("Owned trace cleanup-failure probe.");
            ToolbarLayoutTrace aggregateTrace = null;
            AggregateException combinedFailure = null;
            try
            {
                await WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(faultScope =>
                {
                    aggregateTrace = new ToolbarLayoutTrace(window, System.IO.Path.Combine(Scope.Temp, "trace-aggregate.txt"),
                        (instance, reason) => { if (reason == "finished") throw cleanupFault; });
                    faultScope.Finally(aggregateTrace.Dispose);
                    Scope.Finally(aggregateTrace.Dispose);
                    return Task.FromException(bodyFault);
                });
            }
            catch (AggregateException error) { combinedFailure = error; }
            Check(combinedFailure != null && combinedFailure.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, bodyFault)) &&
                combinedFailure.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, cleanupFault)) &&
                combinedFailure.Data["WhimTexCleanupFailure"] is string,
                "Owned async scope retains both body and trace-cleanup failures plus cleanup status evidence");
            Check(aggregateTrace != null && aggregateTrace.ActiveSubscriptions == 0, "Combined body/cleanup failure still detaches trace subscriptions");
            int aggregateCallbacks = aggregateTrace.GeometryCallbackCount;
            SendTraceGeometryEvents(window);
            Check(aggregateTrace.GeometryCallbackCount == aggregateCallbacks, "Combined-failure trace receives no subsequent geometry callbacks");
            return null;
        }
        finally
        {
            if (fx != null) Undo.ClearUndo(fx);
            if (window != null) { window.DiscardChanges(); WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window); }
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            for (int i = 0; i < prefs.Length; i++)
                if (previousPrefs[i] == null) EditorPrefs.DeleteKey(prefs[i]); else EditorPrefs.SetString(prefs[i], previousPrefs[i]);
            if (previousFocus != null) previousFocus.Focus();
        }
    }
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyRun(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
