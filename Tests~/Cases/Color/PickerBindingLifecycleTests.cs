using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class PickerBindingLifecycleHost : EditorWindow { }

public static class PickerBindingLifecycleTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static void Set(object value, string name, object next) => value.GetType().GetField(name, F).SetValue(value, next);
    static object Get(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    const string Code = "// @param color _Tint = #FFFFFFFF\n// @param gradient _Ramp\nfloat4 ApplyFX(float4 c) { return c; }";

    public static string Parameters() => TestContext.Run("Picker fields survive equivalent parameter-list replacement", t =>
    {
        ShaderFX effect = null;
        PickerBindingLifecycleHost host = null;
        WhimTexColorPicker picker = null;
        var previousFocus = EditorWindow.focusedWindow;
        try
        {
            t.True(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0, "No borrowed color picker is open");
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null)
                .Invoke(null, new object[] { null, Code, new List<ShaderParameter>() });
            Call(effect, "CompileAgentDraft");
            var view = (VisualElement)Activator.CreateInstance(Type("ShaderParameterView"), F, null, new object[] { effect }, null);
            host = ScriptableObject.CreateInstance<PickerBindingLifecycleHost>(); host.titleContent = new GUIContent("Picker Binding Test");
            host.ShowUtility(); host.rootVisualElement.Add(view);
            var color = view.Q<WhimTexColorField>(); var gradient = view.Q<WhimTexGradientValueField>();
            t.True(color != null && gradient != null, "Both fields are present");
            Call(color, "OpenPicker"); picker = Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single();
            var parameters = typeof(ShaderFX).GetProperty("Parameters", F);
            var initial = (IReadOnlyList<ShaderParameter>)parameters.GetValue(effect);
            Call(effect, "CompileAgentDraft");
            var current = (IReadOnlyList<ShaderParameter>)parameters.GetValue(effect);
            t.True(!ReferenceEquals(initial, current), "Actual compilation replaced the parameter list");
            Call(view, "Refresh");
            t.True(ReferenceEquals(color, view.Q<WhimTexColorField>()), "Compile must not replace a color field with unchanged layout");
            t.True(ReferenceEquals(gradient, view.Q<WhimTexGradientValueField>()), "Compile must not detach an unchanged gradient field");
            Call(picker, "SetColor", new Color(.2f, .4f, .7f, .3f), true);
            t.Equal(color.value, (Color)Call(current.First(p => p.name == "_Tint"), "GetNumericColor"), "Existing callback writes the current parameter, not the previous list");
            t.Equal(new Color(.2f, .4f, .7f, .3f), color.value, "Picker remains connected after compilation");
            var next = new WhimTexGradient(); next.SetKeys(new[] { new GradientColorKey(Color.red, 0) }, new[] { new GradientAlphaKey(.4f, 0) });
            gradient.value = next;
            t.True(current.First(p => p.name == "_Ramp").gradientValue.Equals(next), "Existing gradient callback remains connected");
            current.First(p => p.name == "_Tint").controls[0].label = "Changed label";
            Call(view, "Refresh");
            t.True(!ReferenceEquals(color, view.Q<WhimTexColorField>()), "Real declaration changes still rebuild the controls");
        }
        finally
        {
            if (picker != null) Call(picker, "Finish", true);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); }
            if (previousFocus != null) previousFocus.Focus();
        }
    });

    public static string Graph() => GraphCase(false);
    public static string NestedGraph() => GraphCase(true);
    static string GraphCase(bool nested) => TestContext.Run("First inline graph edit preserves its existing fields; nested=" + nested, t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
        ShaderGraphWindow window = null;
        WhimTexGradientWindow editor = null;
        WhimTexColorPicker picker = null;
        var previousFocus = EditorWindow.focusedWindow;
        Layer layer = null;
        try
        {
            t.True(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length == 0 && Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length == 0,
                "No borrowed color or gradient editor is open");
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
            layer = shader; Call(layer, "AssignNewId"); document.layers.Add(layer);
            var graph = shader.draftGraph; graph.nodes.Clear(); graph.edges.Clear(); graph.parameters.Clear();
            var output = new ShaderGraphNode { kind = ShaderGraphNodeKind.Output };
            var sample = new ShaderGraphNode { kind = ShaderGraphNodeKind.SampleGradient };
            graph.nodes.Add(output); graph.nodes.Add(sample);
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); window.hideFlags = HideFlags.HideAndDontSave;
            Set(window, "document", document); Set(window, "layerId", layer.Id);
            string colorPort = "RGBA";
            if (nested)
            {
                var sub = new ShaderGraphSubGraph();
                var pin = new ShaderGraphInterfacePort { name = "Color", type = ShaderGraphValueType.Color, value = Vector4.one };
                sub.outputs.Add(pin); colorPort = pin.id;
                graph.nodes.Remove(sample);
                output = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphOutput };
                sub.nodes.Add(output); sub.nodes.Add(sample); graph.subGraphs.Add(sub);
                Set(window, "editingSubGraphId", sub.id);
            }
            window.ShowUtility(); window.CreateGUI();
            var gradient = window.rootVisualElement.Q<WhimTexGradientValueField>();
            var color = window.rootVisualElement.Query<WhimTexColorField>().ToList().First(f => string.IsNullOrEmpty(f.label));
            t.True(gradient != null && color != null, "Hidden graph contains inline gradient and color fields");
            t.True(sample.FindInput("Gradient") == null && output.FindInput(colorPort) == null, "Inputs initially use implicit defaults");
            Call(gradient, "OpenEditor"); editor = Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Single();
            Call(editor, "Edit", (Action)(() => ((WhimTexGradient)Get(editor, "gradient")).SetKeys(
                new[] { new GradientColorKey(Color.red, 0) }, new[] { new GradientAlphaKey(1, 0) })));
            t.True(sample.FindInput("Gradient") != null, "First edit materializes its resource input");
            Call(window, "RefreshInterface");
            t.True(ReferenceEquals(gradient, window.rootVisualElement.Q<WhimTexGradientValueField>()), "First gradient edit must not rebuild the canvas");
            Call(editor, "Edit", (Action)(() => ((WhimTexGradient)Get(editor, "gradient")).SetKeys(
                new[] { new GradientColorKey(Color.green, 0) }, new[] { new GradientAlphaKey(1, 0) })));
            t.Equal(Color.green, sample.FindInput("Gradient").resource.gradientValue.Evaluate(0), "Second gradient edit still reaches the model");
            editor.Close(); editor = null;
            Call(color, "OpenPicker"); picker = Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single();
            Call(picker, "SetColor", new Color(.3f, .6f, .2f, .5f), true);
            Call(window, "RefreshInterface");
            t.True(window.rootVisualElement.Contains(color), "First color edit must not rebuild the canvas");
            Call(window, "Compile"); Call(window, "RefreshInterface");
            Call(window, "OnDocumentChanged", document); Call(window, "RefreshInterface");
            t.True(ReferenceEquals(gradient, window.rootVisualElement.Q<WhimTexGradientValueField>()), "Compilation keeps the same gradient binding");
            t.True(window.rootVisualElement.Contains(color), "Compilation/notification keeps the same color binding");
            Call(picker, "SetColor", Color.blue, true);
            t.Equal(new Vector4(0, 0, 1, 1), output.FindInput(colorPort).value, "Second color input still reaches the model");
        }
        finally
        {
            if (picker != null) Call(picker, "Finish", true);
            if (editor != null) editor.Close();
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            if (layer != null) Call(layer, "ReleaseTransientResources");
            Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document);
            if (previousFocus != null) previousFocus.Focus();
        }
    });

    public static string Reattach() => TestContext.Run("Temporary field detach does not kill a reattached gradient session", t =>
    {
        var field = new WhimTexGradientValueField(null);
        var previousFocus = EditorWindow.focusedWindow;
        var host = ScriptableObject.CreateInstance<PickerBindingLifecycleHost>();
        var session = ScriptableObject.CreateInstance(Type("WhimTexGradientSession"));
        session.hideFlags = HideFlags.HideAndDontSave;
        var pending = new List<EditorApplication.CallbackFunction>();
        try
        {
            host.titleContent = new GUIContent("Gradient Reattach Test"); host.ShowUtility(); host.rootVisualElement.Add(field);
            Set(field, "session", session);
            Set(session, "changed", (Action<WhimTexGradient>)(value => field.value = value.Clone()));
            var before = EditorApplication.delayCall?.GetInvocationList() ?? Array.Empty<Delegate>();
            field.RemoveFromHierarchy();
            foreach (var callback in EditorApplication.delayCall?.GetInvocationList() ?? Array.Empty<Delegate>())
                if (!before.Contains(callback)) pending.Add((EditorApplication.CallbackFunction)callback);
            t.Equal(1, pending.Count, "Exactly one owned deferred cleanup");
            t.True(Get(session, "changed") == null, "Detached session cannot write to an absent control");
            host.rootVisualElement.Add(field);
            t.True(Get(session, "changed") != null, "Reattach restores the existing session callback");
            foreach (var callback in pending) { EditorApplication.delayCall -= callback; callback(); }
            t.True(session != null, "A stale queued detach must not destroy a reattached session");
            var next = new WhimTexGradient(); next.SetKeys(new[] { new GradientColorKey(Color.green, 0) }, new[] { new GradientAlphaKey(1, 0) });
            ((Action<WhimTexGradient>)Get(session, "changed"))(next);
            t.True(field.value.Equals(next), "Reattached session still updates the field");
            field.RemoveFromHierarchy();
            Call(field, "CloseDetachedSession");
            t.True(session == null, "Permanent removal releases its owned session");
            t.True(Get(field, "session") == null, "Removed field no longer owns a session");
        }
        finally
        {
            foreach (var callback in pending) EditorApplication.delayCall -= callback;
            Set(field, "session", null);
            if (session != null) { Undo.ClearUndo(session); UnityEngine.Object.DestroyImmediate(session); }
            field.RemoveFromHierarchy(); UnityEngine.Object.DestroyImmediate(host);
            if (previousFocus != null) previousFocus.Focus();
        }
    });
}
