using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

// run_script entry: ContextToolsSmoke.Main. Only an owned window and in-memory objects.
public static class ContextToolsSmoke
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly Type WindowType = typeof(TextureCompositorWindow);
    static object Tool(string name) => Enum.Parse(WindowType.GetNestedType("PreviewTool", BindingFlags.NonPublic), name);
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

    public static async Task<string> Main()
    {
        string[] prefs = { "DCFApixels.WhimTex.PreviewTool", "DCFApixels.WhimTex.PreviewTransformReturnTool" };
        var previousPrefs = new string[prefs.Length];
        for (int i = 0; i < prefs.Length; i++) previousPrefs[i] = EditorPrefs.HasKey(prefs[i]) ? EditorPrefs.GetString(prefs[i]) : null;
        var previousFocus = EditorWindow.focusedWindow;
        TextureCompositorWindow window = null;
        ShaderFX fx = null;
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        try
        {
            window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
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
            void SetTool(string name) => Call(window, "SetPreviewTool", Tool(name));
            void IsTool(string name, string message) => Check(Read(window, "previewTool").ToString() == name, message);
            void Escape()
            {
                using var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
                Call(window, "OnToolkitKeyDown", key);
            }
            bool Visible(string name) => !window.rootVisualElement.Q<Button>(name).ClassListContains("whimtex-context-tool--hidden");
            Select(ordinary.Owner);
            SetTool("Brush");
            Check(!Visible("gradientHandlesTool") && !Visible("temporaryCanvasTool"), "No unrelated context buttons");
            Select(first.Owner);
            IsTool("GradientHandles", "Gradient auto-selected");
            Check(Visible("gradientHandlesTool") && Property(window, "IsGradientCanvasEnabled"), "Gradient button and handles visible");
            Check(!Property(window, "IsPreviewPaintTool"), "Base brush does not receive contextual clicks");
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

            fx = ScriptableObject.CreateInstance<ShaderFX>();
            fx.hideFlags = HideFlags.HideAndDontSave;
            var point = new ShaderFXParameter { name = "_Point", type = ShaderFXParameterType.Point, vectorValue = new Vector4(.5f, .5f, 0, 0) };
            var normal = new ShaderFXParameter { name = "_Normal", type = ShaderFXParameterType.Normal, vectorValue = new Vector4(0, 0, 1, 0) };
            var transform = new ShaderFXParameter { name = "_Transform", type = ShaderFXParameterType.Transform2D };
            Write(fx, "parameters", new List<ShaderFXParameter> { point, normal, transform });
            first.Owner.modifiers.Add(fx);
            Select(first.Owner, true);
            void Activate(string kind, ShaderFXParameter parameter) => Call(window, "ActivateTemporaryTool", Tool(kind), fx, parameter.id);
            SetTool("Brush");
            await Task.Delay(100);
            var previewCanvas = (VisualElement)Read(window, "toolkitPreviewCanvas");
            Rect stableCanvas = previewCanvas.worldBound;
            async Task CheckCanvasLayout(string name)
            {
                await Task.Delay(100);
                var settingsPanel = window.rootVisualElement.Q<VisualElement>("previewToolSettings");
                Check(Mathf.Abs(window.rootVisualElement.Q("previewToolSettingsSpace").resolvedStyle.height - 28f) < .1f,
                    name + " reserves a fixed 28px settings row");
                Check(settingsPanel.resolvedStyle.height >= 28f, name + " settings panel remains visible");
                var topRail = (VisualElement)Read(window, "previewGuideTopRail");
                var leftRail = (VisualElement)Read(window, "previewGuideLeftRail");
                Check(Mathf.Abs(topRail.worldBound.yMin - Mathf.Max(settingsPanel.worldBound.yMax, previewCanvas.worldBound.yMin)) < .1f,
                    name + " guide rail follows settings bottom");
                Check(Mathf.Abs(leftRail.worldBound.yMin - topRail.worldBound.yMax) < .1f,
                    name + " left guide rail starts below top rail");
                Check(window.rootVisualElement.panel.Pick(topRail.worldBound.center) == topRail,
                    name + " guide rail is not covered by settings");
                var guideManipulator = Read(window, "previewGuideManipulator");
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
            foreach (string name in new[] { "None", "Pencil", "BlurBrush", "Fill", "Transform", "Zoom", "Shape", "RectangleSelect", "PolygonSelect", "Brush" })
            {
                SetTool(name); await CheckCanvasLayout(name);
            }
            Write(window, "uvEnabled", true); Refresh(); SetTool("UvIslandSelect");
            await CheckCanvasLayout("UV Island Select");
            Write(window, "uvEnabled", false); Refresh();
            SetTool("GradientHandles"); await CheckCanvasLayout("Gradient Handles");
            Check(window.rootVisualElement.Q("previewToolSettings").Children()
                .All(row => row.resolvedStyle.display == DisplayStyle.None), "Parameterless tool leaves the fixed panel empty");
            Activate("FXPoint", point); await CheckCanvasLayout("FX Point");
            Activate("FXNormal", normal); await CheckCanvasLayout("FX Normal");
            Activate("FXTransform", transform); await CheckCanvasLayout("FX Transform");
            Escape();
            Activate("FXPoint", point);
            IsTool("FXPoint", "Edit on Canvas selects temporary point");
            Check(Visible("temporaryCanvasTool"), "Temporary button appears only after activation");
            Check(!Property(window, "IsGradientCanvasEnabled") && !Property(window, "IsPreviewTransformEnabled"), "No competing manipulator active");
            Check(window.rootVisualElement.Q<Button>("temporaryCanvasTool").tooltip.Contains("_Point"), "Temporary description identifies parameter");
            var toolbar = window.rootVisualElement.Q<Button>("temporaryCanvasTool").parent;
            Check(toolbar[toolbar.childCount - 1].name == "temporaryCanvasTool", "Temporary slot is always last");
            Activate("FXNormal", normal); Activate("FXTransform", transform);
            Check(Property(window, "IsPreviewTransformEnabled"), "Temporary transform is enabled for active layer in multiple selection");
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
                var canvas = (VisualElement)Read(window, "toolkitPreviewCanvas");
                Vector4 original = pair.Item3.vectorValue;
                Undo.IncrementCurrentGroup();
                Write(manipulator, "undoGroup", Undo.GetCurrentGroup());
                Write(manipulator, "pointer", PointerId.mousePointerId);
                Write(manipulator, "effect", fx); Write(manipulator, "parameter", pair.Item3);
                Write(manipulator, "originalValue", original);
                canvas.CapturePointer(PointerId.mousePointerId);
                Undo.RecordObject(fx, "Context tools smoke gesture");
                pair.Item3.vectorValue = new Vector4(.2f, .7f, .4f, 0);
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
            await Task.Delay(100);
            Check(window.rootVisualElement.Q<Button>("gradientHandlesTool").resolvedStyle.width > 0, "Visible context button has a layout");

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
            window.position = new Rect(120, 120, 640, 420);
            await Task.Delay(100);
            var lastButton = window.rootVisualElement.Q<Button>("temporaryCanvasTool");
            Check(lastButton.worldBound.yMax <= window.rootVisualElement.worldBound.yMax,
                "Temporary tool fits minimum window: " + lastButton.worldBound + " root: " + window.rootVisualElement.worldBound);
            Escape();
            SetTool("Fill");
            await Task.Delay(100);
            var narrowSettings = window.rootVisualElement.Q("previewToolSettings");
            Check(narrowSettings.resolvedStyle.height > 28f, "Narrow settings wrap onto additional rows");
            Rect narrowCanvas = ((VisualElement)Read(window, "toolkitPreviewCanvas")).worldBound;
            var lastField = narrowSettings.Q<IntegerField>(className: "whimtex-fill-expand");
            Check(lastField.worldBound.xMin >= narrowSettings.worldBound.xMin - .1f &&
                lastField.worldBound.xMax <= narrowSettings.worldBound.xMax + .1f &&
                lastField.worldBound.yMax <= narrowSettings.worldBound.yMax + .1f,
                "Last setting fits inside the wrapped panel");
            Check(lastField.worldBound.center.y > narrowCanvas.yMin, "Extra rows overlap the preview");
            var picked = window.rootVisualElement.panel.Pick(lastField.worldBound.center);
            Check(picked == lastField || lastField.Contains(picked), "Overlapping settings receive input instead of the canvas");
            SetTool("GradientHandles"); await Task.Delay(100);
            Check(Mathf.Abs(narrowSettings.resolvedStyle.height - 28f) < .1f, "Empty panel collapses to one row");
            Check(((VisualElement)Read(window, "toolkitPreviewCanvas")).worldBound == narrowCanvas,
                "Collapsing wrapped settings does not move or resize the preview");
            // Repeat actual window resizes and tool switches after rebuilding the view.
            foreach (float width in new[] { 1280f, 640f, 900f, 640f })
            {
                window.position = new Rect(120, 120, width, 650);
                SetTool("Brush"); await Task.Delay(100);
                previewCanvas = (VisualElement)Read(window, "toolkitPreviewCanvas");
                stableCanvas = previewCanvas.worldBound;
                foreach (string name in new[] { "Fill", "Zoom", "Transform", "Pencil", "BlurBrush", "GradientHandles", "Brush" })
                {
                    SetTool(name); await CheckCanvasLayout(name + " at " + width);
                }
            }
            return "PASS: " + checks + " context, temporary return, active-layer, Escape/capture, toolbar and preference checks. No Assets modified.";
        }
        finally
        {
            if (fx != null) Undo.ClearUndo(fx);
            if (window != null) { window.DiscardChanges(); window.Close(); }
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            for (int i = 0; i < prefs.Length; i++)
                if (previousPrefs[i] == null) EditorPrefs.DeleteKey(prefs[i]); else EditorPrefs.SetString(prefs[i], previousPrefs[i]);
            if (previousFocus != null) previousFocus.Focus();
        }
    }
}
