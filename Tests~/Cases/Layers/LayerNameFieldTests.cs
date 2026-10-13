using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityA;
using WhimTex.Tests.UnityD;

// Reflection is limited to the owned package window for fixture setup.
// Pointer editing and layout use public UI Toolkit events; no native drag or global Undo execution.
public static class LayerNameFieldTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static void Pointer(VisualElement target, EventType type, int clicks = 1, Vector2? position = null)
    {
        var input = new Event
        {
            type = type,
            button = 0,
            mousePosition = position ?? target.worldBound.center,
            clickCount = clicks
        };
        if (type == EventType.MouseDown)
        {
            using var evt = PointerDownEvent.GetPooled(input);
            evt.target = target; target.SendEvent(evt);
        }
        else if (type == EventType.MouseDrag)
        {
            using var evt = PointerMoveEvent.GetPooled(input);
            evt.target = target; target.SendEvent(evt);
        }
        else
        {
            using var evt = PointerUpEvent.GetPooled(input);
            evt.target = target; target.SendEvent(evt);
        }
    }

    private static void Click(TestContext context, VisualElement target, int clicks = 1)
    {
        context.True(target.panel != null && target.worldBound.width > 0f && target.worldBound.height > 0f,
            "Click target is attached and laid out");
        Vector2 point = target.worldBound.center;
        var picked = target.panel.Pick(point);
        context.True(picked != null && (picked == target || target.Contains(picked)),
            "The actual panel picks the intended field or a descendant at its center");
        Pointer(picked, EventType.MouseDown, clicks, point);
        picked = target.panel.Pick(point);
        context.True(picked != null && (picked == target || target.Contains(picked)),
            "The actual panel still picks the field at mouse release");
        Pointer(picked, EventType.MouseUp, clicks, point);
    }

    private static VisualElement EditingTarget(TextField field)
        => field.Q<VisualElement>(className: "whimtex-layer-name__editor")?.Q<TextElement>()
            ?? field.focusController?.focusedElement as VisualElement ?? field;

    private static void Key(TextField field, KeyCode key, char character = '\0')
    {
        using var evt = KeyDownEvent.GetPooled(new Event
        {
            type = EventType.KeyDown, keyCode = key, character = character
        });
        var target = EditingTarget(field);
        evt.target = target; target.SendEvent(evt);
    }

    private static void Type(TextField field, string text)
    {
        field.textSelection.SelectAll();
        foreach (char character in text) Key(field, KeyCode.None, character);
    }

    private static bool Editing(TextField field) => field.ClassListContains("whimtex-layer-name--editing");

    private static void AssertIdle(TestContext context, TextField field, string expected, string description)
    {
        var display = field.Q<Label>(className: "whimtex-layer-name__display");
        var input = field.Q<VisualElement>(className: "whimtex-layer-name__editor");
        context.True(display != null && input != null, description + " contains a label and native text input");
        context.True(!Editing(field) && display.resolvedStyle.display == DisplayStyle.Flex &&
            input.resolvedStyle.display == DisplayStyle.None, description + " shows only its ordinary label while idle");
        context.Equal(expected, display.text, description + " label reflects the committed name");
        foreach (float x in new[] { field.worldBound.xMin + 2f, field.worldBound.center.x, field.worldBound.xMax - 2f })
        {
            var point = new Vector2(x, field.worldBound.center.y);
            var picked = field.panel.Pick(point);
            context.True(field.worldBound.Contains(point) && picked != null &&
                (picked == field || field.Contains(picked)), description + " is picked at its center and side gutters");
        }
    }

    private static Task Execute(TestContext context, CancellationToken token)
        => UnityAScope.RunOwnedAsync(async scope =>
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var windowType = typeof(WhimTexWindow);
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = document.height = 8;
        var first = new Layer(new ColorFillLayerBehaviour { color = Color.red }) { layerName = "First", opacity = .25f };
        var second = new Layer(new ColorFillLayerBehaviour { color = Color.green }) { layerName = "Second", opacity = .75f };
        var group = new Layer(new GroupLayerBehaviour()) { layerName = "Group" };
        var nestedGroup = new Layer(new GroupLayerBehaviour()) { layerName = "Nested group" };
        var nestedChild = new Layer(new ColorFillLayerBehaviour { color = Color.blue }) { layerName = "Nested child" };
        group.children.Add(nestedGroup); nestedGroup.children.Add(nestedChild);
        var missing = new Layer { layerName = "Missing behaviour" };
        document.layers.Add(first); document.layers.Add(second); document.layers.Add(group); document.layers.Add(missing);

        var window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
        windowType.GetMethod("SetDocument", flags).Invoke(window, new object[] { document });
        string firstId = first.Id, secondId = second.Id, groupId = group.Id, missingId = missing.Id;
        string nestedChildId = nestedChild.Id;
        context.True(!string.IsNullOrEmpty(firstId) && !string.IsNullOrEmpty(groupId), "Owned layers receive stable IDs");
        window.titleContent = new GUIContent("WhimTex layer name regression");
        window.ShowUtility(); window.position = new Rect(80, 80, 1000, 720); window.CreateGUI(); window.Focus();
        var root = window.rootVisualElement;
        var outside = new Button { text = "Owned focus target", style = { position = Position.Absolute, left = 8f, bottom = 8f } };
        root.Add(outside);

        async Task Settle(int ticks = 3)
        {
            for (int tick = 0; tick < ticks; tick++) await AsyncD.Tick(root, token);
        }
        void Refresh() => windowType.GetMethod("RefreshToolkitInterface", flags).Invoke(window, new object[] { false });
        VisualElement Row(string id)
        {
            foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
                if (Equals(row.userData, id)) return row;
            throw new InvalidOperationException("Missing owned layer row: " + id);
        }
        TextField Name(string id) => Row(id).Q<TextField>(className: "whimtex-layer-name");
        Layer Find(List<Layer> layers, string id)
        {
            foreach (var layer in layers)
            {
                if (layer.Id == id) return layer;
                if (!(layer.Behaviour is GroupLayerBehaviour)) continue;
                Layer nested = Find(layer.children, id);
                if (nested != null) return nested;
            }
            return null;
        }
        Layer Model(string id) => Find(document.layers, id) ?? throw new InvalidOperationException("Missing owned layer model: " + id);
        string ActiveId() => (string)windowType.GetField("selectedLayerId", flags).GetValue(window);
        async Task Begin(string id)
        {
            if (ActiveId() == id)
            {
                windowType.GetMethod("SelectOnlyLayer", flags).Invoke(window, new object[] { id == firstId ? secondId : firstId });
                Refresh(); await Settle();
            }
            context.True(ActiveId() != id, "The two-click sequence starts on an unselected owned layer");
            Click(context, Name(id)); await Settle(1);
            context.Equal(id, ActiveId(), "The first actual name click selects its layer");
            context.True(!Editing(Name(id)), "The first actual name click keeps the plain label");
            Click(context, Name(id), 2);
            await Settle();
            var field = Name(id);
            context.True(Editing(field), "Double click begins keyboard editing for " + id);
            context.Equal(DisplayStyle.Flex, field.Q<VisualElement>(className: "whimtex-layer-name__editor").resolvedStyle.display,
                "Native input appears during keyboard editing");
            context.Equal(DisplayStyle.None, field.Q<Label>(className: "whimtex-layer-name__display").resolvedStyle.display,
                "Idle label is hidden during keyboard editing");
        }

        windowType.GetMethod("SelectOnlyLayer", flags).Invoke(window, new object[] { secondId });
        Refresh(); await Settle();
        AssertIdle(context, Name(firstId), "First", "Leaf name");
        AssertIdle(context, Name(groupId), "Group", "Group name");
        Click(context, Name(firstId)); await Settle();
        context.Equal(firstId, ActiveId(), "A single name click selects its layer");
        context.True(!Editing(Name(firstId)), "A single name click does not begin editing");
        var captured = PointerCaptureHelper.GetCapturingElement(root.panel, PointerId.mousePointerId);
        context.True(captured == null, "Name click completes without retaining row pointer capture");

        await Begin(firstId);
        var name = Name(firstId);
        Type(name, "Renamed layer"); await Settle();
        context.Equal("Renamed layer", name.text, "Keyboard characters reach the native editor");
        context.Equal("First", Model(firstId).layerName, "Delayed typing leaves model data unchanged");
        Refresh(); await Settle();
        context.True(Editing(Name(firstId)), "Binding refresh preserves the active editing session");
        context.Equal("Renamed layer", Name(firstId).text, "Binding refresh preserves uncommitted input");
        context.Equal(firstId, ActiveId(), "Typing leaves the edited layer selected");
        var textTarget = EditingTarget(Name(firstId));
        Vector2 textPress = textTarget.worldBound.center;
        Pointer(textTarget, EventType.MouseDown, position: textPress);
        Pointer(textTarget, EventType.MouseDrag, position: textPress + new Vector2(20f, 0f));
        captured = PointerCaptureHelper.GetCapturingElement(root.panel, PointerId.mousePointerId);
        context.True(captured == null || captured == Name(firstId) ||
            captured is VisualElement capturedElement && Name(firstId).Contains(capturedElement),
            "Pointer selection during name editing retains capture only inside the text editor");
        context.True(windowType.GetField("activeLayerDrag", flags).GetValue(window) == null,
            "Pointer movement during name editing cannot start a row drag");
        Pointer(textTarget, EventType.MouseUp, position: textPress + new Vector2(20f, 0f));
        await Settle();
        context.True(Editing(Name(firstId)), "Pointer selection leaves the name editor active");
        context.Equal("Renamed layer", Name(firstId).text, "Pointer selection preserves the keyboard draft");
        Key(Name(firstId), KeyCode.Return, '\n'); await Settle();
        context.Equal("Renamed layer", Model(firstId).layerName, "Enter commits the typed name");
        AssertIdle(context, Name(firstId), "Renamed layer", "Committed leaf name");

        await Begin(firstId); Type(Name(firstId), "Discarded draft");
        Key(Name(firstId), KeyCode.Escape); await Settle();
        context.Equal("Renamed layer", Model(firstId).layerName, "Escape discards the draft without modifying the model");
        AssertIdle(context, Name(firstId), "Renamed layer", "Cancelled leaf name");
        await Begin(firstId); Type(Name(firstId), "Committed on Tab");
        Key(Name(firstId), KeyCode.Tab, '\t'); await Settle();
        context.Equal("Committed on Tab", Model(firstId).layerName, "Tab commits the draft before moving keyboard focus");
        AssertIdle(context, Name(firstId), "Committed on Tab", "Tabbed leaf name");
        await Begin(secondId); Type(Name(secondId), "Committed on blur");
        outside.Focus(); await Settle();
        context.Equal("Committed on blur", Model(secondId).layerName, "Leaving the text field commits its draft");
        AssertIdle(context, Name(secondId), "Committed on blur", "Blurred leaf name");
        await Begin(groupId); Type(Name(groupId), "Renamed group");
        Key(Name(groupId), KeyCode.Return, '\n'); await Settle();
        context.Equal("Renamed group", Model(groupId).layerName, "Group names use the same keyboard editing contract");
        AssertIdle(context, Name(groupId), "Renamed group", "Committed group name");
        context.Equal("Committed on Tab", Model(firstId).layerName, "Group rename leaves other layer names unchanged");

        AssertIdle(context, Name(missingId), "Missing behaviour", "Missing behaviour name");
        context.True(Name(missingId).isReadOnly, "Missing behaviour names retain their metadata restriction");
        Click(context, Name(missingId), 2); await Settle();
        context.True(!Editing(Name(missingId)), "A read-only name cannot enter keyboard editing");
        context.Equal("Missing behaviour", Model(missingId).layerName, "Read-only double click preserves its name");

        windowType.GetMethod("SelectOnlyLayer", flags).Invoke(window, new object[] { firstId }); Refresh(); await Settle();
        var firstRow = Row(firstId);
        var header = root.Q<VisualElement>(className: "whimtex-layer-table-header");
        context.True(header != null, "Layers has a responsive column header");
        async Task Width(VisualElement row, float width)
        {
            row.style.width = width;
            await Settle();
        }
        void Columns(VisualElement row, bool opacityVisible, bool blendVisible, string description)
        {
            var alpha = row.Q<VisualElement>(className: "whimtex-layer-opacity");
            var blend = row.Q<VisualElement>(className: "whimtex-layer-blend");
            context.True(alpha != null && blend != null, description + " contains both optional columns");
            context.Equal(opacityVisible ? DisplayStyle.Flex : DisplayStyle.None, alpha.resolvedStyle.display,
                description + " opacity visibility");
            context.Equal(blendVisible ? DisplayStyle.Flex : DisplayStyle.None, blend.resolvedStyle.display,
                description + " blend visibility");
        }
        void AllColumns(bool opacityVisible, bool blendVisible, string description)
        {
            Columns(header, opacityVisible, blendVisible, description + " header");
            foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
                Columns(row, opacityVisible, blendVisible, description + " row " + row.userData);
        }
        foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
            row.style.width = 520f;
        header.style.width = 520f;
        await Settle(); AllColumns(true, true, "Wide Layers fixture");
        context.True(Name(firstId).worldBound.width >= 96f, "A wide row preserves the fixed minimum name width");
        var opacityField = firstRow.Q<WhimTexSliderField>(className: "whimtex-layer-opacity");
        int alphaStarts = 0, alphaEnds = 0;
        opacityField.interactionStarted += () => alphaStarts++;
        opacityField.interactionEnded += () => alphaEnds++;
        Click(context, opacityField.Q<VisualElement>(className: "whimtex-slider-field__input"), 2);
        await Settle();
        context.True(opacityField.isEditing && alphaStarts == 1, "Wide row opacity can enter keyboard editing");
        await Width(firstRow, 320f); AllColumns(true, false, "Blend hides first across the list");
        context.True(opacityField.isEditing && alphaEnds == 0,
            "Hiding Blend leaves the visible Opacity editor active");
        await Width(firstRow, 200f); AllColumns(false, false, "A narrower leaf also hides Opacity");
        await Settle(2);
        context.True(!opacityField.isEditing && !opacityField.isDragging && alphaEnds == 1,
            "Hiding opacity finishes its active interaction exactly once");
        var focused = opacityField.focusController?.focusedElement as VisualElement;
        context.True(focused == null || focused != opacityField && !opacityField.Contains(focused),
            "A hidden opacity editor cannot retain keyboard focus");
        context.True(PointerCaptureHelper.GetCapturingElement(root.panel, PointerId.mousePointerId) == null,
            "A hidden opacity editor cannot retain pointer capture");
        context.Equal(firstId, ActiveId(), "Responsive columns preserve the current selection");
        context.Near(.25f, Model(firstId).opacity, 0, "Hiding opacity does not modify its model value");
        context.True(Name(firstId).worldBound.width > 96f, "Hiding optional columns returns room to the label");
        await Settle(5); AllColumns(false, false, "Settled global collapse");
        for (int repeat = 0; repeat < 2; repeat++)
        {
            await Width(firstRow, 520f); AllColumns(true, true, "Expanded leaf restores all columns");
            await Width(firstRow, 320f); AllColumns(true, false, "Intermediate leaf keeps only Opacity");
            await Settle(5); AllColumns(true, false, "Settled intermediate leaf keeps only Opacity");
            await Width(firstRow, 200f); AllColumns(false, false, "Narrow leaf hides both columns");
            await Width(firstRow, 320f); AllColumns(true, false, "Expansion restores Opacity first");
            await Width(firstRow, 520f); AllColumns(true, true, "Further expansion restores Blend");
        }

        // Find the transition through layout, without coupling to control text widths.
        float opacityTransition = 0f, blendTransition = 0f;
        for (float width = 160f; width <= 440f; width += 10f)
        {
            await Width(firstRow, width);
            if (opacityTransition == 0f && firstRow.Q<VisualElement>(className: "whimtex-layer-opacity").resolvedStyle.display != DisplayStyle.None)
                opacityTransition = width;
            if (firstRow.Q<VisualElement>(className: "whimtex-layer-blend").resolvedStyle.display != DisplayStyle.None)
            { blendTransition = width; break; }
        }
        context.True(opacityTransition > 160f && blendTransition > opacityTransition + 60f && blendTransition <= 440f,
            "Fixed name budgets produce two distinct layout transitions");
        foreach (string text in new[] { "A", "A long layer name that cannot fit in the ordinary label space" })
        {
            Model(firstId).layerName = text; Refresh(); await Settle();
            await Width(firstRow, opacityTransition - 10f); AllColumns(false, false, "Fixed Opacity threshold with changed name text");
            await Width(firstRow, opacityTransition + 10f); AllColumns(true, false, "Opacity returns at a fixed threshold with changed name text");
            await Width(firstRow, blendTransition - 10f); AllColumns(true, false, "Fixed Blend threshold with changed name text");
            await Width(firstRow, blendTransition + 10f); AllColumns(true, true, "Blend returns at a fixed threshold with changed name text");
        }
        context.True(Name(firstId).worldBound.width >= 96f, "Visible columns leave at least the fixed label width");
        Model(firstId).layerName = "Renamed layer"; Refresh();
        var nestedRow = Row(nestedChildId);
        context.True(Name(nestedChildId).parent.resolvedStyle.paddingLeft >= 28f,
            "The owned nested child reserves the two-level indentation budget");
        await Width(nestedRow, blendTransition + 10f);
        AllColumns(true, false, "Worst nested name budget hides Blend across header and all rows");
        await Settle(5); AllColumns(true, false, "Settled nested intermediate layout");
        await Width(nestedRow, opacityTransition + 10f);
        AllColumns(false, false, "A narrower nested name budget also hides Opacity across the list");
        await Settle(5); AllColumns(false, false, "Settled nested global collapse");
        await Width(nestedRow, blendTransition + 10f); AllColumns(true, false, "Nested expansion restores Opacity first");
        await Width(nestedRow, 520f); AllColumns(true, true, "Expanded nested row restores all columns");
        await Width(firstRow, 520f);
        var groupRow = Row(groupId);
        await Width(groupRow, 200f); AllColumns(false, false, "One narrow group collapses all columns");
        await Settle(5); AllColumns(false, false, "Settled group global collapse");
        await Width(groupRow, 300f); AllColumns(true, false, "Intermediate group keeps only Opacity");
        await Width(groupRow, 520f); AllColumns(true, true, "Wide group restores all columns");
        context.Equal(firstId, ActiveId(), "Group layout changes preserve leaf selection");

        await Width(header, 200f); AllColumns(false, false, "One narrow header collapses all rows");
        await Settle(5); AllColumns(false, false, "Settled header global collapse");
        await Width(header, 300f); AllColumns(true, false, "Intermediate header keeps only Opacity");
        await Width(header, 520f); AllColumns(true, true, "Wide header restores all columns");
        foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
            row.style.width = StyleKeyword.Null;
        header.style.width = StyleKeyword.Null;
        root.Focus(); outside.RemoveFromHierarchy();
        var settingsPane = root.Q<VisualElement>(className: "whimtex-settings-pane");
        var workspaceSplit = root.Q<TwoPaneSplitView>(className: "whimtex-workspace-split");
        context.True(settingsPane != null && workspaceSplit != null, "The owned workspace exposes its public split layout");
        workspaceSplit.fixedPaneInitialDimension = 520f;
        await Settle(5);
        AllColumns(true, true, "Normal wide Layers context");
        context.True(Row(firstId).worldBound.xMax <= settingsPane.worldBound.xMax + .1f,
            "The wide preview uses naturally fitting rows without oversized clipping");
        string previewPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
            "Temp", "WhimTex", "layer-name-preview.png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(previewPath));
        await UnityACapture.Capture(window, previewPath, token);
        context.True(System.IO.File.Exists(previewPath) && new System.IO.FileInfo(previewPath).Length > 32,
            "Owned wide Layers context pixels were retained for visual review");
        settingsPane.style.minWidth = 0f;
        settingsPane.style.maxWidth = 350f;
        workspaceSplit.fixedPaneInitialDimension = 350f;
        await Settle(5);
        AllColumns(true, false, "Normal intermediate Layers context keeps only Opacity");
        context.True(Row(firstId).worldBound.xMax <= settingsPane.worldBound.xMax + .1f,
            "The intermediate preview uses naturally fitting rows without oversized clipping");
        string middlePreviewPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(previewPath), "layer-name-opacity-only-preview.png");
        await UnityACapture.Capture(window, middlePreviewPath, token);
        context.True(System.IO.File.Exists(middlePreviewPath) && new System.IO.FileInfo(middlePreviewPath).Length > 32,
            "Owned Opacity-only Layers context pixels were retained for visual review");
        settingsPane.style.maxWidth = 230f;
        workspaceSplit.fixedPaneInitialDimension = 230f;
        await Settle(5);
        context.True(settingsPane.worldBound.width <= 230.1f,
            "The narrow preview resizes the actual settings pane");
        AllColumns(false, false, "Normal narrow Layers context");
        context.True(Row(firstId).worldBound.xMax <= settingsPane.worldBound.xMax + .1f,
            "The narrow preview uses naturally fitting rows without oversized clipping");
        string narrowPreviewPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(previewPath), "layer-name-narrow-preview.png");
        await UnityACapture.Capture(window, narrowPreviewPath, token);
        context.True(System.IO.File.Exists(narrowPreviewPath) && new System.IO.FileInfo(narrowPreviewPath).Length > 32,
            "Owned narrow Layers context pixels were retained for visual review");
    });
}
