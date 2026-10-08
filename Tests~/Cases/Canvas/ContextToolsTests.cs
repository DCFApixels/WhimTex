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
    static readonly Type WindowType = typeof(WhimTexWindow);
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

    sealed class PanelRepaintBarrier : ImmediateModeElement
    {
        internal readonly TaskCompletionSource<bool> completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void ImmediateRepaint() => completion.TrySetResult(true);
    }

    static async Task AwaitPanelRepaint(EditorWindow window)
    {
        Cancellation.ThrowIfCancellationRequested();
        var barrier = new PanelRepaintBarrier { pickingMode = PickingMode.Ignore };
        barrier.style.position = Position.Absolute;
        barrier.style.width = barrier.style.height = 1;
        window.rootVisualElement.Add(barrier);
        try
        {
            window.Repaint();
            if (await Task.WhenAny(barrier.completion.Task, WhimTex.Tests.UnityA.UnityAAsync.Delay(1000, Cancellation)) != barrier.completion.Task)
            {
                Cancellation.ThrowIfCancellationRequested();
                throw new TimeoutException("Owned panel did not repaint after its layout/style change.");
            }
            await barrier.completion.Task;
        }
        finally { barrier.RemoveFromHierarchy(); }
    }

    sealed class ToolbarLayoutTrace : IDisposable
    {
        readonly WhimTexWindow window;
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
        public ToolbarLayoutTrace(WhimTexWindow owner, string output,
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

    static void SendTraceGeometryEvents(WhimTexWindow window)
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
        WhimTexWindow window = null;
        ShaderFX fx = null;
        ToolbarLayoutTrace trace = null;
        int checks = 0;
        void Check(bool ok, string message) { T.True(ok, message); }
        try
        {
            window = Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
            var document = (WhimTexDocument)Read(window, "activeDocument");
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
            first.Owner.fx.Add(fx);
            Select(first.Owner, true);
            void Activate(string kind, ShaderFXParameter parameter) => Call(window, "ActivateTemporaryTool", Tool(kind), fx, parameter.id);
            async Task CheckCanvasParameterActions()
            {
                point = ((List<ShaderFXParameter>)Read(fx, "parameters")).Find(p => p.id == point.id);
                normal = ((List<ShaderFXParameter>)Read(fx, "parameters")).Find(p => p.id == normal.id);
                transform = ((List<ShaderFXParameter>)Read(fx, "parameters")).Find(p => p.id == transform.id);
                transform.controls.Add(new ShaderFXParameterControl
                {
                    type = ShaderFXParameterType.Transform2D,
                    label = "Transform with a long parameter label"
                });
                point.controls.Add(new ShaderFXParameterControl { type = ShaderFXParameterType.Point, label = "Point", order = 1 });
                normal.controls.Add(new ShaderFXParameterControl { type = ShaderFXParameterType.Normal, label = "Normal", order = 2 });
                var offsetParameter = new ShaderFXParameter { name = "_Offset", type = ShaderFXParameterType.Vector2 };
                offsetParameter.controls.Add(new ShaderFXParameterControl { type = ShaderFXParameterType.Vector2, label = "Offset", order = 3 });
                ((List<ShaderFXParameter>)Read(fx, "parameters")).Add(offsetParameter);
                var parameterWindow = Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTex.Tests.UnityA.UnityAHeaderWindow>());
                try
                {
                    parameterWindow.ShowUtility();
                    parameterWindow.position = new Rect(180, 180, 320, 220);
                    var assembly = typeof(ShaderFX).Assembly;
                    var snapRadiusField = assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings")
                        .GetField("snapRadius", BindingFlags.Static | Instance);
                    var previousSnapRadius = snapRadiusField.GetValue(null);
                    Scope.Finally(() => snapRadiusField.SetValue(null, previousSnapRadius));
                    snapRadiusField.SetValue(null, 8f);
                    assembly.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles", BindingFlags.Static | Instance)
                        .Invoke(null, new object[] { parameterWindow.rootVisualElement });
                    var parameterView = (VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"),
                        Instance, null, new object[] { fx }, null);
                    parameterWindow.rootVisualElement.Add(parameterView);
                    var referenceVector3 = new Vector3Field("Reference Vector3");
                    var referenceVector4 = new Vector4Field("Reference Vector4");
                    parameterView.Add(referenceVector3);
                    parameterView.Add(referenceVector4);
                    var transformFoldout = parameterView.Q<Foldout>(className: "whimtex-fx-transform");
                    var header = transformFoldout.Q(className: "whimtex-fx-transform-header");
                    var edit = header.Q<Button>("editFXTransform");
                    var reset = header.Q<Button>("resetFXTransform");
                    var editPoint = parameterView.Q<Button>("editFXPoint");
                    var editNormal = parameterView.Q<Button>("editFXNormal");
                    var pointField = editPoint.parent.Q<Vector2Field>();
                    var normalField = editNormal.parent.Q<Vector3Field>();
                    var offsetField = parameterView.Query<Vector2Field>().ToList().Find(field => field.label == "Offset");
                    Check(parameterView.Query<Foldout>().ToList().Count == 1 && pointField != null && normalField != null,
                        "Point and Normal stay inline numeric fields without foldouts");
                    foreach (var action in new[] { editPoint, editNormal })
                        Check(action.parent[1] == action && string.IsNullOrEmpty(action.text) &&
                            action.ClassListContains("whimtex-fx-parameter-action") &&
                            Read(action.Q(className: "whimtex-tool-icon"), "tool").ToString() == "Transform" &&
                            action.tooltip.StartsWith("Edit on Canvas."), "Canvas actions share the Transform hand and button style");
                    Check(!transformFoldout.value, "Transform starts collapsed with header actions available");
                    Check(header[0] is Toggle && header[1] == edit && header[2] == reset,
                        "Transform header orders foldout, edit icon and reset icon");
                    Check(!transformFoldout.contentContainer.Contains(edit) && !transformFoldout.contentContainer.Contains(reset),
                        "Transform actions are outside collapsible content");
                    Check(string.IsNullOrEmpty(edit.text) && edit.Q(className: "whimtex-tool-icon") != null && reset.text == "↺",
                        "Transform reuses the tool icon and brush-section reset glyph");
                    Check(Read(edit.Q(className: "whimtex-tool-icon"), "tool").ToString() == "Transform" &&
                        Read(window.rootVisualElement.Q<Button>("temporaryCanvasTool").Q(className: "whimtex-tool-icon"), "tool").ToString() == "FXTransform",
                        "Parameter action uses Transform while the temporary tool retains its distinct icon");
                    foreach (int width in new[] { 320, 180 })
                    {
                        parameterWindow.position = new Rect(180, 180, width, 220);
                        foreach (bool expanded in new[] { true, false })
                        {
                            transformFoldout.value = expanded;
                            await AwaitPanelRepaint(parameterWindow);
                            foreach (var action in new[] { edit, reset })
                            {
                                action.style.width = 20; action.style.height = 18;
                                action.style.marginTop = 0; action.style.marginBottom = 0;
                                action.style.fontSize = 12;
                            }
                            await AwaitPanelRepaint(parameterWindow);
                            float previousHeaderHeight = header.layout.height;
                            float previousContentY = parameterView.Q<Vector2Field>().worldBound.y;
                            foreach (var action in new[] { edit, reset })
                            {
                                action.style.width = StyleKeyword.Null; action.style.height = StyleKeyword.Null;
                                action.style.marginTop = StyleKeyword.Null; action.style.marginBottom = StyleKeyword.Null;
                                action.style.fontSize = StyleKeyword.Null;
                            }
                            await AwaitPanelRepaint(parameterWindow);
                            Check(Mathf.Abs(edit.resolvedStyle.width - 24f) < .1f && Mathf.Abs(edit.resolvedStyle.height - 20f) < .1f &&
                                Mathf.Abs(reset.resolvedStyle.width - 24f) < .1f && Mathf.Abs(reset.resolvedStyle.height - 20f) < .1f,
                                "Transform actions use the shared enlarged 24x20 size");
                            Check(Mathf.Abs(header.layout.height - previousHeaderHeight) < .1f &&
                                (!expanded || Mathf.Abs(parameterView.Q<Vector2Field>().worldBound.y - previousContentY) < .1f),
                                "Enlarged transform actions preserve header height and parameter positions");
                            Check(edit.worldBound.width > 0 && reset.worldBound.width > 0 &&
                                Mathf.Abs(edit.worldBound.center.y - reset.worldBound.center.y) < .1f &&
                                edit.worldBound.xMax <= reset.worldBound.xMin + .1f &&
                                reset.worldBound.xMax <= header.worldBound.xMax + .1f &&
                                header.worldBound.xMax - reset.worldBound.xMax < 3f,
                                "Transform actions stay aligned at the right edge, width=" + width + ", expanded=" + expanded);
                            Check(header[0].worldBound.xMax <= edit.worldBound.xMin + .1f,
                                "Long foldout label does not overlap transform actions");
                            foreach (var action in new[] { editPoint, editNormal })
                            {
                                Check(Mathf.Abs(action.resolvedStyle.width - edit.resolvedStyle.width) < .1f &&
                                    Mathf.Abs(action.resolvedStyle.height - edit.resolvedStyle.height) < .1f &&
                                    action.parent[0].worldBound.xMax <= action.worldBound.xMin + .1f &&
                                    action.worldBound.xMax <= action.parent.worldBound.xMax + .1f &&
                                    action.parent.worldBound.xMax - action.worldBound.xMax < 3f,
                                    "Point/Normal actions fit at the right edge without overlapping their field, width=" + width);
                                var actionPicked = parameterWindow.rootVisualElement.panel.Pick(action.worldBound.center);
                                Check(actionPicked == action || action.Contains(actionPicked), "Inline canvas action remains clickable");
                                if (width == 320)
                                    foreach (var number in action.parent[0].Query<FloatField>().ToList())
                                        Check(number.worldBound.width >= 20f && number.worldBound.xMax <= action.worldBound.xMin + .1f,
                                            "Inline numeric components remain visible without overlapping the action: " + number.worldBound);
                            }
                            Check(Mathf.Abs(pointField.labelElement.worldBound.width - offsetField.labelElement.worldBound.width) < .1f &&
                                Mathf.Abs(pointField.Q<FloatField>().worldBound.xMin - offsetField.Q<FloatField>().worldBound.xMin) < .1f &&
                                Mathf.Abs(normalField.Q<FloatField>().worldBound.xMin - offsetField.Q<FloatField>().worldBound.xMin) < .1f,
                                "Inline Point/Normal retain standard vector label width and X-component alignment");
                            foreach (var vector2 in parameterView.Query<Vector2Field>().ToList())
                            {
                                foreach (var spacer in vector2.Query(className: "unity-composite-field__field-spacer").ToList())
                                    Check(spacer.resolvedStyle.display == DisplayStyle.None, "Every Vector2 hides the reserved Z-column spacer");
                                var components = vector2.Query<FloatField>().ToList();
                                Check(components.Count == 2 && components.All(component => component.resolvedStyle.flexGrow == 1f &&
                                    component.resolvedStyle.flexBasis.value == 0f), "Vector2 components share the available width equally");
                                if (width == 320 && vector2.worldBound.width > 0 && vector2.worldBound.height > 0)
                                    Check(Mathf.Abs(components[0].worldBound.width - components[1].worldBound.width) < .1f &&
                                        vector2.worldBound.xMax - components[1].worldBound.xMax < 6f,
                                        "Vector2 Y reaches the right edge without a spare Z column");
                            }
                            if (width == 320)
                            {
                                var referenceComponents = referenceVector3.Query<FloatField>().ToList()
                                    .Concat(referenceVector4.Query<FloatField>().ToList()).ToList();
                                var styledBounds = referenceComponents.Select(component => component.worldBound).ToArray();
                                parameterWindow.rootVisualElement.RemoveFromClassList("whimtex-theme");
                                try
                                {
                                    await WhimTex.Tests.UnityA.UnityAAsync.Delay(50, Cancellation);
                                    for (int i = 0; i < referenceComponents.Count; i++)
                                        Check(Mathf.Abs(referenceComponents[i].worldBound.x - styledBounds[i].x) < .1f &&
                                            Mathf.Abs(referenceComponents[i].worldBound.width - styledBounds[i].width) < .1f,
                                            "Shared Vector2 style leaves native Vector3/Vector4 component geometry unchanged");
                                }
                                finally { parameterWindow.rootVisualElement.AddToClassList("whimtex-theme"); }
                                await WhimTex.Tests.UnityA.UnityAAsync.Delay(50, Cancellation);
                            }
                            var resetPicked = parameterWindow.rootVisualElement.panel.Pick(reset.worldBound.center);
                            Check(resetPicked == reset || reset.Contains(resetPicked), "Reset remains clickable when collapsed or narrow");
                            if (width == 320 && expanded || width == 180 && !expanded)
                                await WhimTex.Tests.UnityA.UnityACapture.Capture(parameterWindow,
                                    System.IO.Path.Combine(Scope.Temp, "fx-transform-" + width + ".png"), Cancellation);
                        }
                    }
                    void Press(Button button)
                    {
                        using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
                    }
                    Press(edit); IsTool("FXTransform", "Collapsed transform edit action activates the matching canvas tool");
                    Check(!transformFoldout.value, "Edit action does not toggle the foldout");
                    Press(edit); IsTool("GradientHandles", "Repeated transform edit action returns to the preceding tool");
                    transform.transformValue = ShaderFXTransform.Default;
                    transform.transformValue.position += new Double2(.2, -.1);
                    transform.transformValue.size = new Double2(.4, .7);
                    transform.transformValue.rotation = 35;
                    Press(reset);
                    Check(transform.transformValue.Equals(ShaderFXTransform.Default), "Collapsed transform reset restores every component");
                    var transformFields = transformFoldout.Query<Vector2Field>().ToList();
                    Check(!transformFoldout.value && transformFields[0].value == new Vector2(.5f, .5f) &&
                        transformFields[1].value == Vector2.one && parameterView.Q<DoubleField>().value == 0,
                        "Reset preserves foldout state and refreshes position, size and rotation");
                    Press(editPoint); IsTool("FXPoint", "Inline Point action activates its matching canvas tool");
                    pointField.value = new Vector2(-.25f, 1.25f);
                    Check(point.vectorValue == new Vector4(-.25f, 1.25f, 0, 0) && pointField.value == new Vector2(-.25f, 1.25f),
                        "Point numeric edits preserve coordinates outside every canvas edge");
                    pointField.value = new Vector2(.5f, .5f);
                    var canvas = (VisualElement)Read(window, "toolkitCanvas");
                    var viewport = Read(canvas, "viewport");
                    Write(viewport, "fit", false); Write(viewport, "scale", 4f);
                    foreach (float angle in new[] { 0f, 37f })
                    {
                        Call(viewport, "SetRotation", angle, false);
                        canvas.GetType().GetMethod("UpdateImageLayout", Instance, null, Type.EmptyTypes, null).Invoke(canvas, null);
                        await WhimTex.Tests.UnityA.UnityAAsync.Delay(100, Cancellation);
                        var manipulator = Read(window, "pointManipulator");
                        Rect image = (Rect)canvas.GetType().GetProperty("ImageRect", Instance).GetValue(canvas);
                        Vector2 Position(Vector2 uv) => canvas.LocalToWorld((Vector2)Call(canvas, "ToView",
                            new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height)));
                        void Pointer(EventType type, Vector2 uv, bool control = false)
                        {
                            var input = new Event { type = type, button = 0, mousePosition = Position(uv),
                                modifiers = control ? EventModifiers.Control : EventModifiers.None };
                            if (type == EventType.MouseDown)
                            { using var evt = PointerDownEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
                            else if (type == EventType.MouseDrag)
                            { using var evt = PointerMoveEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
                            else
                            { using var evt = PointerUpEvent.GetPooled(input); evt.target = canvas; canvas.SendEvent(evt); }
                        }
                        foreach (var destination in new[] { new Vector2(-.75f, 1.75f), new Vector2(1.75f, -.75f), new Vector2(.5f, .5f) })
                        {
                            var origin = new Vector2(point.vectorValue.x, point.vectorValue.y);
                            Pointer(EventType.MouseDown, origin);
                            Check(Property(manipulator, "IsDragging"), "Point handle can be grabbed outside the canvas, rotation=" + angle);
                            Pointer(EventType.MouseDrag, destination);
                            Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), destination) < .0001f,
                                "Captured Point drag preserves out-of-canvas UV under view rotation: " + point.vectorValue);
                            Pointer(EventType.MouseUp, destination);
                            Check(!Property(manipulator, "IsDragging") && !canvas.HasPointerCapture(PointerId.mousePointerId),
                                "Point drag releases pointer capture");
                        }
                        float radius = (float)assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings")
                            .GetProperty("SnapRadius", BindingFlags.Static | Instance).GetValue(null);
                        float near = radius * .25f / image.width;
                        Pointer(EventType.MouseDown, new Vector2(.5f, .5f));
                        foreach (var edge in new[] { new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(.5f, 0), new Vector2(.5f, 1), Vector2.zero, Vector2.one })
                        {
                            var nearby = edge + new Vector2(edge.x == 0 ? -near : edge.x == 1 ? near : 0,
                                edge.y == 0 ? -near : edge.y == 1 ? near : 0);
                            Pointer(EventType.MouseDrag, nearby);
                            Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), edge) < .0001f,
                                "Point snaps to all canvas edges and corners from outside, rotation=" + angle);
                            Pointer(EventType.MouseDrag, nearby, true);
                            Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), nearby) < .0001f,
                                "Ctrl disables edge snapping without clamping Point to the canvas");
                        }
                        var guides = (System.Collections.IList)Read(window, "canvasGuides");
                        var guideType = WindowType.GetNestedType("CanvasGuide", BindingFlags.NonPublic);
                        void Guide(Vector2 axis, float location)
                        {
                            var guide = Activator.CreateInstance(guideType);
                            guideType.GetField("normal").SetValue(guide, axis);
                            guideType.GetField("position").SetValue(guide, location);
                            guides.Add(guide);
                        }
                        Guide(Vector2.right, document.width * .3f);
                        Guide(Vector2.up, document.height * .6f);
                        Write(window, "canvasGuidesHidden", false); Write(window, "canvasGuidesSnap", true);
                        Write(window, "canvasGuidesDocument", document);
                        var guideTarget = new Vector2(.3f, .4f);
                        var nearGuide = guideTarget + new Vector2(near, near);
                        Pointer(EventType.MouseDrag, nearGuide);
                        Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), guideTarget) < .0001f,
                            "Point snaps to guide intersections in bottom-left UV coordinates, rotation=" + angle);
                        Pointer(EventType.MouseDrag, nearGuide, true);
                        Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), nearGuide) < .0001f,
                            "Ctrl also disables guide snapping during the same drag");
                        Write(window, "canvasGuidesHidden", true);
                        Pointer(EventType.MouseDrag, nearGuide);
                        Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), nearGuide) < .0001f,
                            "Hidden guides do not snap Point");
                        Write(window, "canvasGuidesHidden", false); Write(window, "canvasGuidesSnap", false);
                        Pointer(EventType.MouseDrag, nearGuide);
                        Check(Vector2.Distance(new Vector2(point.vectorValue.x, point.vectorValue.y), nearGuide) < .0001f,
                            "Disabled guide snapping is respected by Point");
                        guides.Clear();
                        Pointer(EventType.MouseDrag, new Vector2(.5f, .5f));
                        Pointer(EventType.MouseUp, new Vector2(.5f, .5f));
                        Guide(Vector2.right, document.width * .1f);
                        var guideManipulator = Read(window, "canvasGuideManipulator");
                        foreach (var action in new[] { editPoint, editNormal, edit })
                        {
                            SetTool("GradientHandles"); Press(action);
                            Check(Property(window, "CanMoveCanvasGuides"), "Every Edit on Canvas tool permits guide dragging");
                            Vector4 beforePoint = point.vectorValue, beforeNormal = normal.vectorValue;
                            var beforeTransform = transform.transformValue;
                            float guideY = action == edit ? -.2f : .2f;
                            Pointer(EventType.MouseDown, new Vector2(.1f, guideY));
                            Check(Property(guideManipulator, "IsDragging"), "Temporary canvas tool captures an existing guide");
                            Pointer(EventType.MouseDrag, new Vector2(.2f, guideY));
                            Pointer(EventType.MouseUp, new Vector2(.2f, guideY));
                            Check(Mathf.Abs((float)guideType.GetField("position").GetValue(guides[0]) - document.width * .2f) < .0001f &&
                                !Property(guideManipulator, "IsDragging"), "Guide moves and releases capture in every temporary tool, rotation=" + angle);
                            Check(point.vectorValue == beforePoint && normal.vectorValue == beforeNormal && transform.transformValue.Equals(beforeTransform),
                                "Guide dragging does not edit the active FX parameter");
                            guides.Clear(); Guide(Vector2.right, document.width * .1f);
                        }
                        Write(window, "canvasGuidesLocked", true);
                        Pointer(EventType.MouseDown, new Vector2(.1f, .2f));
                        Check(!Property(guideManipulator, "IsDragging"), "Locked guides stay locked in Edit on Canvas");
                        Pointer(EventType.MouseUp, new Vector2(.1f, .2f));
                        Write(window, "canvasGuidesLocked", false);
                        guides.Clear();
                        transform.transformValue = ShaderFXTransform.Default;
                        Write(viewport, "scale", 2f);
                        canvas.GetType().GetMethod("UpdateImageLayout", Instance, null, Type.EmptyTypes, null).Invoke(canvas, null);
                        await WhimTex.Tests.UnityA.UnityAAsync.Delay(50, Cancellation);
                        image = (Rect)canvas.GetType().GetProperty("ImageRect", Instance).GetValue(canvas);
                        var transformManipulator = Read(window, "canvasTransformManipulator");
                        foreach (string mode in new[] { "Layer", "Multiple", "FX" })
                        {
                            Select(first.Owner, mode == "Multiple"); SetTool("Transform");
                            if (mode == "FX") { SetTool("GradientHandles"); Press(edit); }
                            var grips = new List<Vector2> { Vector2.zero, new Vector2(.5f, 0), new Vector2(1, 0),
                                new Vector2(1, .5f), Vector2.one, new Vector2(.5f, 1), new Vector2(0, 1), new Vector2(0, .5f),
                                new Vector2(.5f, 1 + 24f / image.height) };
                            if (mode != "FX") grips.Add(new Vector2(.5f, .5f));
                            foreach (var grip in grips)
                            {
                                guides.Clear(); Guide(Vector2.right, document.width * grip.x);
                                Guide(Vector2.up, document.height * (1 - grip.y));
                                Vector2 local = canvas.WorldToLocal(Position(grip));
                                Check((int)Call(guideManipulator, "RailAt", local) == -1, "Handle test is outside guide rails: " + mode + ", " + grip + ", angle=" + angle);
                                Check((bool)Call(transformManipulator, "WantsPointer", local),
                                    "Transform recognizes its handle: " + mode + ", " + grip + ", view rotation=" + angle);
                                Check((int)Call(guideManipulator, "Hit", local) == -1 &&
                                    !(bool)Call(guideManipulator, "WantsCursor", local, false),
                                    "Overlapping guides yield hover and cursor to the handle");
                                var expectedCursor = grip.y > 1 ? MouseCursor.RotateArrow :
                                    grip == new Vector2(.5f, .5f) ? MouseCursor.MoveArrow : MouseCursor.ScaleArrow;
                                Check((MouseCursor)Call(transformManipulator, "GetCursor", local, false) == expectedCursor,
                                    "Handle retains the matching transform cursor");
                                Write(window, "selectedCanvasGuide", 0);
                                Pointer(EventType.MouseDown, grip);
                                Check(Property(transformManipulator, "IsDragging") && !Property(guideManipulator, "IsDragging") &&
                                    canvas.HasPointerCapture(PointerId.mousePointerId), "Handle captures the real UI event before overlapping guides");
                                Check((int)Read(window, "selectedCanvasGuide") == -1,
                                    "Tool-handle clicks deselect the guide before it can intercept keyboard input");
                                Pointer(EventType.MouseUp, grip);
                                Check(!Property(transformManipulator, "IsDragging") && !canvas.HasPointerCapture(PointerId.mousePointerId),
                                    "Transform handle releases capture");
                            }
                            guides.Clear(); Guide(Vector2.right, document.width * .2f);
                            Vector2 body = canvas.WorldToLocal(Position(new Vector2(.2f, .2f)));
                            Check((bool)Call(transformManipulator, "WantsPointer", body) &&
                                !(bool)Call(guideManipulator, "WantsCursor", body, false), "Transform frame body also has priority over guides");
                            Pointer(EventType.MouseDown, new Vector2(.2f, .2f));
                            Check(!Property(guideManipulator, "IsDragging") && Property(transformManipulator, "IsDragging"),
                                "Transform captures frame-body clicks before guides");
                            Pointer(EventType.MouseUp, new Vector2(.2f, .2f));
                            guides.Clear();
                            SetTool("GradientHandles"); Press(editPoint); Guide(Vector2.right, 0);
                            Check((bool)Call(guideManipulator, "WantsCursor", canvas.WorldToLocal(Position(new Vector2(0, 0))), false),
                                "Inactive transform does not reserve its old handle positions");
                            guides.Clear();
                        }
                        foreach (var action in new[] { editPoint, editNormal })
                        {
                            SetTool("GradientHandles"); Press(action);
                            var active = Read(window, action == editPoint ? "pointManipulator" : "normalManipulator");
                            Vector2 grip = action == editPoint ? new Vector2(point.vectorValue.x, point.vectorValue.y) : new Vector2(.5f, .5f);
                            Guide(Vector2.right, document.width * grip.x);
                            Vector2 local = canvas.WorldToLocal(Position(grip));
                            Check((bool)Call(active, "WantsPointer", local) && !(bool)Call(guideManipulator, "WantsCursor", local, false),
                                "Point and Normal handles also take priority over guide hover");
                            Pointer(EventType.MouseDown, grip);
                            Check(Property(active, "IsDragging") && !Property(guideManipulator, "IsDragging"),
                                "Point and Normal capture the real pointer before guides");
                            Call(active, "Finish", true);
                            Check(!canvas.HasPointerCapture(PointerId.mousePointerId), "Parameter drag cancellation releases capture");
                            guides.Clear();
                        }
                        Select(first.Owner, true);
                        SetTool("GradientHandles"); Press(editPoint);
                    }
                    Press(editPoint); IsTool("GradientHandles", "Repeated inline Point action returns to the preceding tool");
                    Press(editNormal); IsTool("FXNormal", "Inline Normal action activates its matching canvas tool");
                    Press(editNormal); IsTool("GradientHandles", "Repeated inline Normal action returns to the preceding tool");
                    Undo.ClearUndo(fx);
                }
                finally
                {
                    WhimTex.Tests.UnityA.UnityAScope.CloseOwned(parameterWindow);
                    window.Focus();
                }
            }
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
                await AwaitPanelRepaint(window);
                window.rootVisualElement.panel.Pick(Vector2.zero);
                var settingsPanel = window.rootVisualElement.Q<VisualElement>("canvasToolSettings");
                Check(Mathf.Abs(window.rootVisualElement.Q("canvasToolSettingsSpace").resolvedStyle.height - 28f) < .1f,
                    name + " reserves a fixed 28px settings row");
                Check(settingsPanel.resolvedStyle.height >= 28f, name + " settings panel remains visible");
                var topRail = (VisualElement)Read(window, "canvasGuideTopRail");
                var leftRail = (VisualElement)Read(window, "canvasGuideLeftRail");
                var guideOverlay = (VisualElement)Read(window, "canvasGuideOverlay");
                Check(previewCanvas.IndexOf(guideOverlay) > previewCanvas.IndexOf((VisualElement)Read(previewCanvas, "tiledImage")),
                    name + " guides draw above the canvas image");
                foreach (var toolOverlay in new[] { (VisualElement)Read(previewCanvas, "overlay"),
                    (VisualElement)Read(window, "gradientCanvasOverlay"), (VisualElement)Read(window, "normalOverlay"),
                    (VisualElement)Read(window, "pointOverlay"), (VisualElement)Read(window, "canvasTransformOverlay"),
                    (VisualElement)Read(window, "areaSelectionOverlay"), (VisualElement)Read(window, "healingOverlay") })
                    Check(previewCanvas.IndexOf(toolOverlay) > previewCanvas.IndexOf(guideOverlay),
                        name + " all tool overlays draw above guides");
                Check(Mathf.Abs(topRail.worldBound.yMin - Mathf.Max(settingsPanel.worldBound.yMax, previewCanvas.worldBound.yMin)) < .1f,
                    name + " guide rail follows settings bottom");
                Check(Mathf.Abs(leftRail.worldBound.yMin - topRail.worldBound.yMax) < .1f,
                    name + " left guide rail starts below top rail");
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
                foreach (var rail in new[] { topRail, leftRail })
                {
                    Vector2 railCenter = rail.worldBound.center;
                    var picked = window.rootVisualElement.panel.Pick(railCenter);
                    Check(picked != null && (picked == previewCanvas || guideOverlay.Contains(picked)),
                        name + " guide rail is not covered by settings: picked=" + picked?.GetType().Name + "/" + picked?.name +
                        ", picked bounds=" + picked?.worldBound + ", rail=" + rail.worldBound + ", settings=" + settingsPanel.worldBound);
                    int guideCount = ((System.Collections.IList)Read(window, "canvasGuides")).Count;
                    try
                    {
                        using var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = railCenter });
                        down.target = picked;
                        picked.SendEvent(down);
                        Check(Property(guideManipulator, "IsDragging") && previewCanvas.HasPointerCapture(PointerId.mousePointerId) &&
                            !Property(Read(window, "canvasTransformManipulator"), "IsDragging") &&
                            !Property(Read(window, "pointManipulator"), "IsDragging") &&
                            !Property(Read(window, "normalManipulator"), "IsDragging") &&
                            !Property(Read(window, "canvasZoomManipulator"), "IsDragging") &&
                            !Property(Read(window, "gradientCanvasManipulator"), "IsDragging"),
                            name + " rail creates a guide through the actual picked UI target, without starting the active tool");
                    }
                    finally
                    {
                        Call(guideManipulator, "Cancel");
                        using var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = railCenter });
                        up.target = picked;
                        picked.SendEvent(up);
                    }
                    Check(!previewCanvas.HasPointerCapture(PointerId.mousePointerId) &&
                        ((System.Collections.IList)Read(window, "canvasGuides")).Count == guideCount,
                        name + " cancelled rail gesture releases capture without committing a guide");
                }
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
            first.Owner.fx.Remove(fx); Refresh();
            IsTool("GradientHandles", "Removed FX returns to surviving gradient context");
            first.Owner.fx.Add(fx);
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
            var rebuiltContextButton = window.rootVisualElement.Q<Button>("gradientHandlesTool");
            var rebuildBudget = System.Diagnostics.Stopwatch.StartNew();
            window.Repaint();
            while (!(rebuiltContextButton.resolvedStyle.width > 0) && rebuildBudget.ElapsedMilliseconds < 1000)
                await WhimTex.Tests.UnityA.UnityAAsync.Delay(10, Cancellation);
            Check(rebuiltContextButton.resolvedStyle.width > 0,
                "Visible context button has a layout after view rebuild: " + rebuiltContextButton.worldBound);
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
            Select(first.Owner); SetTool("GradientHandles");
            await CheckCanvasParameterActions();
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
