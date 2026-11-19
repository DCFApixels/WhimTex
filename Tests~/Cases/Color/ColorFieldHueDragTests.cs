using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;

public static class ColorFieldHueDragTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, F).Invoke(owner, args);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, F).SetValue(owner, value);
    static object Get(object owner, string name) => owner.GetType().GetField(name, F).GetValue(owner);
    static Color Rotate(Color value, double turns) => (Color)typeof(WhimTexColorField).GetMethod("ShiftHue", F).Invoke(null, new object[] { value, turns });
    static void Near(TestContext t, Color expected, Color actual, string message)
    {
        for (int i = 0; i < 4; i++) t.Near(expected[i], actual[i], 0.00002, message + " component " + i);
    }
    static void Pointer(VisualElement label, EventType type, Vector2 position, int button = 0)
    {
        var input = new Event { type = type, button = button, mousePosition = position };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(input); e.target = label; label.SendEvent(e); }
        else if (type == EventType.MouseDrag) { using var e = PointerMoveEvent.GetPooled(input); e.target = label; label.SendEvent(e); }
        else { using var e = PointerUpEvent.GetPooled(input); e.target = label; label.SendEvent(e); }
    }
    static void Key(VisualElement receiver, KeyCode code)
    {
        using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = code });
        e.target = receiver; receiver.SendEvent(e);
    }
    static void Drag(VisualElement label, float delta)
    {
        Vector2 start = label.worldBound.center;
        Pointer(label, EventType.MouseDown, start);
        Pointer(label, EventType.MouseDrag, start + Vector2.right * delta);
        Pointer(label, EventType.MouseUp, start + Vector2.right * delta);
    }

    public static string Start(string runId) => UnityAAsync.Start(runId, (t, cancellation) => UnityAScope.RunOwnedAsync(async scope =>
    {
        for (int h = 0; h < 12; h++)
        foreach (float v in new[] { 0.35f, 1f, 8f })
        foreach (double turns in new[] { -2.25, -0.1, 0.0, 0.25, 1.125 })
        {
            var original = Color.HSVToRGB(h / 12f, 0.7f, v, true); original.a = 0.37f;
            var expected = Color.HSVToRGB(Mathf.Repeat(h / 12f + (float)turns, 1), 0.7f, v, true); expected.a = original.a;
            Near(t, expected, Rotate(original, turns), "Hue rotation preserves S/V/HDR/alpha");
        }
        var signed = Rotate(new Color(-2, 7, 1, -0.2f), 0.37);
        t.Equal(-2f, Mathf.Min(signed.r, Mathf.Min(signed.g, signed.b)), "Signed RGB minimum retained");
        t.Equal(7f, Mathf.Max(signed.r, Mathf.Max(signed.g, signed.b)), "Signed RGB maximum retained");
        t.Equal(-0.2f, signed.a, "Raw alpha retained");

        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        var host = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexGradientWindow>());
        document.hideFlags = HideFlags.HideAndDontSave;
        host.ShowUtility(); host.rootVisualElement.Clear(); host.position = new Rect(160, 160, 280, 180);
        var fill = new ColorFillLayerBehaviour();
        document.layers.Add(fill);
        ColorFillLayerBehaviour Fill() => (ColorFillLayerBehaviour)document.layers[0].Behaviour;
        int edits = 0, opens = 0;
        var field = new WhimTexColorField("Color") { hdr = true, showAlpha = true };
        Set(field, "ReadPickerColor", (Func<Color>)(() => Fill().color));
        Set(field, "OpenPickerOverride", (Action)(() => opens++));
        field.RegisterValueChangedCallback(e =>
        {
            Undo.RecordObject(document, "Change Color"); Fill().color = e.newValue; edits++;
        });
        host.rootVisualElement.Add(field);
        var native = new UnityEditor.UIElements.ColorField("Color") { hdr = true, showAlpha = true };
        host.rootVisualElement.Add(native);
        await UnityAAsync.Delay(160, cancellation);
        t.True(field.labelElement.ClassListContains("unity-base-field__label--with-dragger"), "Native drag-label cursor class");
        t.Near(native.worldBound.height, field.worldBound.height, 0.1, "Native row height retained");
        t.Near(native.labelElement.worldBound.width, field.labelElement.worldBound.width, 0.1, "Native label width retained on narrow panel");
        t.True(field.labelElement.worldBound.width > 10 && field.worldBound.xMax <= host.rootVisualElement.worldBound.xMax + 1, "Label and field remain inside narrow panel");

        var originalHdr = new Color(8, 2, 2, 0.35f);
        void Reset(Color value, bool bounded = false)
        {
            Fill().color = value;
            field.SetValueWithoutNotify(bounded ? new Color(value.r / 8, value.g / 8, value.b / 8, value.a) : value);
        }
        Reset(originalHdr);
        Vector2 start = field.labelElement.worldBound.center;
        Pointer(field.labelElement, EventType.MouseDown, start);
        Pointer(field.labelElement, EventType.MouseDrag, start + new Vector2(1, 80));
        Pointer(field.labelElement, EventType.MouseUp, start);
        t.Equal(0, edits, "Click and vertical movement do not edit color");
        t.Equal(0, opens, "Label does not open picker");
        Pointer(field.labelElement, EventType.MouseDown, start);
        Pointer(field.labelElement, EventType.MouseDrag, start + Vector2.right * 60);
        Undo.FlushUndoRecordObjects();
        Pointer(field.labelElement, EventType.MouseDrag, start + Vector2.right * 120);
        Undo.FlushUndoRecordObjects();
        Pointer(field.labelElement, EventType.MouseUp, start + Vector2.right * 120);
        Near(t, new Color(2, 8, 2, 0.35f), Fill().color, "Drag applies horizontal hue shift");
        t.True(!field.labelElement.HasPointerCapture(PointerId.mousePointerId), "Release ends capture");
        Undo.PerformUndo(); Near(t, originalHdr, Fill().color, "Single Undo restores entire multi-move gesture");
        Undo.PerformRedo(); Near(t, new Color(2, 8, 2, 0.35f), Fill().color, "Single Redo reapplies entire gesture");

        Reset(originalHdr, bounded: true); field.hdr = false;
        Drag(field.labelElement, -120);
        Near(t, new Color(2, 2, 8, 0.35f), Fill().color, "Bounded swatch retains stored HDR during hue editing");
        Reset(new Color(8, 8, 8, 0.37f), bounded: true); int before = edits;
        Drag(field.labelElement, 100);
        t.Equal(before, edits, "Achromatic HDR does not emit redundant edits");
        Reset(originalHdr); field.hdr = true;
        Pointer(field.labelElement, EventType.MouseDown, start);
        Pointer(field.labelElement, EventType.MouseDrag, start + Vector2.right * 70);
        Key(field, KeyCode.Escape);
        Near(t, originalHdr, Fill().color, "Escape restores exact initial color");
        t.True(!field.labelElement.HasPointerCapture(PointerId.mousePointerId), "Escape releases capture");

        field.SetEnabled(false); before = edits; Drag(field.labelElement, 100);
        t.Equal(before, edits, "Disabled color remains unchanged"); field.SetEnabled(true);
        field.showMixedValue = true; Drag(field.labelElement, 100);
        t.Equal(before, edits, "Mixed value is not collapsed by hue drag"); field.showMixedValue = false;
        Pointer(field.labelElement, EventType.MouseDown, start, 1);
        Pointer(field.labelElement, EventType.MouseDrag, start + Vector2.right * 90, 1);
        Pointer(field.labelElement, EventType.MouseUp, start, 1);
        t.Equal(before, edits, "Right drag leaves color unchanged");
        var external = new Label("External Color"); host.rootVisualElement.Add(external);
        Call(field, "BindHueDragLabel", external);
        await UnityAAsync.Delay(80, cancellation);
        Drag(external, 120); Near(t, new Color(2, 8, 2, 0.35f), Fill().color, "Separate associated label edits same field");
        Pointer(external, EventType.MouseDown, external.worldBound.center);
        field.RemoveFromHierarchy();
        t.True(!external.HasPointerCapture(PointerId.mousePointerId), "Field detach releases external label capture");
        before = edits; Drag(external, 80); t.Equal(before, edits, "Detached field cannot be edited by stale external label");
        host.rootVisualElement.Add(field);
        Pointer(field, EventType.MouseDown, field.worldBound.center);
        t.Equal(1, opens, "Swatch click keeps existing picker action");

        await Header(t, scope, host, cancellation);
        await Graph(t, scope, cancellation);
    }));

    static async Task Header(TestContext t, UnityAScope scope, EditorWindow host, CancellationToken cancellation)
    {
        const string code = "// @group(Tint; _Tint)\n// @param color _Tint = #CC333359\n// @endgroup\nfloat4 ApplyFX(float4 c) { return c * _Tint; }";
        var effect = scope.OwnObject((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null)
            .Invoke(null, new object[] { null, code, new List<ShaderParameter>() }));
        Call(effect, "CompileAgentDraft");
        var type = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderParameterView", true);
        var view = (VisualElement)Activator.CreateInstance(type, F, null, new object[] { effect }, null);
        host.rootVisualElement.Clear(); host.rootVisualElement.Add(view);
        await UnityAAsync.Delay(120, cancellation);
        var title = view.Q<Label>(className: "whimtex-fx-parameter-group-title");
        var field = view.Q<WhimTexColorField>();
        t.True(title != null && field != null && title.ClassListContains("whimtex-color-hue-label"), "Group color header binds its separate title");
        var read = (Func<Color>)Get(field, "ReadPickerColor");
        Color original = read();
        Drag(title, 120);
        Near(t, Rotate(original, 1.0 / 3), read(), "Parameter group title shifts source hue with S/V/alpha preserved");
    }

    static async Task Graph(TestContext t, UnityAScope scope, CancellationToken cancellation)
    {
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        var window = scope.OwnWindow(ScriptableObject.CreateInstance<ShaderGraphWindow>());
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 16;
        var layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
        Call((Layer)layer, "AssignNewId"); document.layers.Add(layer);
        Set(window, "document", document); Set(window, "layerId", ((Layer)layer).Id);
        var graph = layer.draftGraph; graph.nodes.Clear(); graph.edges.Clear();
        var nodes = typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphNodes", true);
        var hue = (ShaderGraphNode)nodes.GetMethod("Create", F).Invoke(null, new object[] { ShaderGraphNodeKind.Hue, new Vector2(200, 100) });
        hue.previewExpanded = false;
        var color = hue.FindInput("Color");
        if (color == null) hue.inputs.Add(color = new ShaderGraphInput { port = "Color", type = ShaderGraphValueType.Color });
        color.value = new Vector4(0.8f, 0.2f, 0.2f, 0.35f);
        graph.nodes.Add(hue);
        var output = (ShaderGraphNode)nodes.GetMethod("Create", F).Invoke(null, new object[] { ShaderGraphNodeKind.Output, new Vector2(500, 100) });
        output.previewExpanded = false; graph.nodes.Add(output);
        graph.edges.Add(new ShaderGraphEdge { outputNode = hue.id, outputPort = "Out", inputNode = output.id, inputPort = "RGBA" });
        window.ShowUtility(); window.CreateGUI();
        await UnityAAsync.Delay(180, cancellation);
        var canvas = (GraphView)Get(window, "canvas");
        var port = canvas.ports.First(p => p.direction == Direction.Input && p.Q("port-label")?.ClassListContains("whimtex-color-hue-label") == true);
        var label = port.Q("port-label");
        var view = canvas.nodes.First(); Vector2 position = view.GetPosition().position;
        string inputName = (string)Get(port.userData, "name");
        var initial = hue.FindInput(inputName)?.value;
        t.True(initial.HasValue, "Color port has stored inline input");
        t.Equal(PickingMode.Position, label.pickingMode, "Unconnected color label remains pickable");
        Drag(label, 120);
        t.Equal(position, view.GetPosition().position, "Hue drag does not move the graph node");
        t.True(!port.connected && graph.edges.Count == 1 && graph.edges[0].inputNode == output.id, "Hue drag does not create a wire or change port connection state");
        t.True(hue.FindInput(inputName).value != initial.Value, "Port label updates stored color");
        t.Equal(0, canvas.selection.Count, "Hue drag does not select an input branch");
        // Verify the actual public compatibility mouse event is consumed before node draggers.
        Vector2 start = label.worldBound.center;
        Pointer(label, EventType.MouseDown, start);
        bool bubbled = false;
        EventCallback<MouseDownEvent> handler = _ => bubbled = true;
        view.RegisterCallback(handler);
        using (var e = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
        { e.target = label; label.SendEvent(e); }
        view.UnregisterCallback(handler);
        Pointer(label, EventType.MouseUp, start);
        t.True(!bubbled, "Color-label mouse compatibility event cannot start node drag");
        window.Close();
        Call((Layer)layer, "ReleaseTransientResources");
    }

    public static string Poll(string id) => UnityAAsync.Poll(id);
    public static Task<string> Cancel(string id) => UnityAAsync.Cancel(id);
    public static Task<string> Cleanup(string id) => UnityAAsync.Cleanup(id);
}
