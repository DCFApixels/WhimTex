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
using WhimTex.Tests.UnityD;

public sealed class SliderFieldTestsWindow : EditorWindow { }

// Public UI Toolkit events against one owned, unsaved window; no asset imports.
public static class SliderFieldTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string StartIntegration(string runId) => AsyncD.Start(runId, ExecuteIntegration);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static void Pointer(VisualElement target, EventType type, Vector2 position, int clicks = 1)
    {
        var input = new Event { type = type, button = 0, mousePosition = position, clickCount = clicks };
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

    private static void Key(VisualElement target, KeyCode key, char character = '\0', bool up = false)
    {
        var input = new Event { type = up ? EventType.KeyUp : EventType.KeyDown, keyCode = key, character = character };
        if (up)
        {
            using var evt = KeyUpEvent.GetPooled(input);
            evt.target = target; target.SendEvent(evt);
        }
        else
        {
            using var evt = KeyDownEvent.GetPooled(input);
            evt.target = target; target.SendEvent(evt);
        }
    }

    private static VisualElement EditingTarget(FloatField editor)
    {
        var focused = editor.focusController?.focusedElement as VisualElement;
        if (focused != null && (focused == editor || editor.Contains(focused))) return focused;
        return editor.Q<VisualElement>(className: "unity-base-text-field__input")?.Q<TextElement>() ?? (VisualElement)editor;
    }

    private static void Type(FloatField editor, string text)
    {
        editor.textSelection.SelectAll();
        foreach (char character in text) Key(EditingTarget(editor), KeyCode.None, character);
    }

    private static bool CapturedWithin(VisualElement element)
    {
        var captured = element.panel == null ? null :
            PointerCaptureHelper.GetCapturingElement(element.panel, PointerId.mousePointerId) as VisualElement;
        return captured != null && (captured == element || element.Contains(captured));
    }

    private static async Task Execute(TestContext context, CancellationToken token)
    {
        var previousFocus = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<SliderFieldTestsWindow>();
        window.hideFlags = HideFlags.HideAndDontSave;
        window.titleContent = new GUIContent("WhimTex slider field regression");
        try
        {
            window.ShowUtility();
            window.position = new Rect(80, 80, 420, 180);
            window.Focus();
            var root = window.rootVisualElement;
            var container = new VisualElement();
            root.Add(container);
            var field = new WhimTexSliderField(null, 0f, 1f);
            field.style.width = 220f; field.style.height = 24f;
            container.Add(field);
            var outside = new Button { text = "Other focus" };
            root.Add(outside);
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);

            var input = field.Q<VisualElement>(className: "whimtex-slider-field__input");
            var display = field.Q<Label>(className: "whimtex-slider-field__value");
            var editor = field.Q<FloatField>(className: "whimtex-slider-field__editor");
            int editorFocusEntries = 0;
            editor.RegisterCallback<FocusInEvent>(_ => editorFocusEntries++);
            context.True(input != null && input.worldBound.width > 50f && input.worldBound.height > 0f,
                "Combined field has a laid-out input");
            context.True(display != null && editor != null && editor.isDelayed,
                "Display and delayed numeric editor occupy one input");
            context.True(input.Contains(display) && input.Contains(editor), "Display and editor belong to the same input");
            context.True(field.Q<Slider>() == null, "Combined input has no separate standard slider");

            int changes = 0, starts = 0, ends = 0;
            float previous = 0, next = 0;
            field.RegisterValueChangedCallback(evt =>
            {
                changes++; previous = evt.previousValue; next = evt.newValue;
                context.True(ReferenceEquals(evt.target, field), "Consumers receive the outer field ChangeEvent");
            });
            field.interactionStarted += () => starts++;
            field.interactionEnded += () => ends++;

            field.SetValueWithoutNotify(.123456f);
            context.Equal(0, changes, "Model synchronization is silent");
            context.Near(.123456f, field.value, 0, "Model synchronization preserves full precision");
            field.formatString = "0.00";
            context.Equal("0.12", display.text, "Display format does not round the stored value");
            context.Near(.123456f, field.value, 0, "Changing display format preserves data");
            field.value = .42f;
            context.Equal(1, changes, "External value assignment emits one event");
            context.Near(.123456f, previous, 0, "ChangeEvent contains the previous value");
            context.Near(.42f, next, 0, "ChangeEvent contains the committed value");
            field.value = .42f;
            context.Equal(1, changes, "An unchanged value emits no event");
            field.value = -10f;
            context.Near(0f, field.value, 0, "Value assignment clamps at the lower boundary");
            field.value = 10f;
            context.Near(1f, field.value, 0, "Value assignment clamps at the upper boundary");
            int finiteChanges = changes;
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                field.value = invalid;
                field.SetValueWithoutNotify(invalid);
                context.Near(1f, field.value, 0, "Non-finite assignment preserves the current bounded value");
            }
            context.Equal(finiteChanges, changes, "Rejected non-finite values emit no event");
            field.SetValueWithoutNotify(-10f);
            context.Near(0f, field.value, 0, "Silent assignment also clamps below range");
            field.SetValueWithoutNotify(10f);
            context.Near(1f, field.value, 0, "Silent assignment also clamps above range");
            context.Equal(finiteChanges, changes, "Silent clamping emits no event");

            field.lowValue = -2f; field.highValue = 8f;
            field.step = .25f;
            field.SetValueWithoutNotify(.123456f);
            context.Near(.123456f, field.value, 0, "Step leaves external model precision intact");
            field.value = 12f;
            context.Near(8f, field.value, 0, "Reusable field accepts a non-opacity upper boundary");
            field.value = -12f;
            context.Near(-2f, field.value, 0, "Reusable field accepts a negative lower boundary");
            field.lowValue = 0f; field.highValue = 1f; field.step = 0f;
            field.SetValueWithoutNotify(.5f);

            Rect bounds = input.worldBound;
            Vector2 press = bounds.center;
            int beforeChanges = changes, beforeStarts = starts, beforeEnds = ends;
            Pointer(input, EventType.MouseDown, new Vector2(bounds.xMin + 4f, press.y));
            context.Near(.5f, field.value, 0, "Pointer press away from the thumb does not jump");
            context.True(!field.isEditing, "Single press stays in slider mode");
            Pointer(input, EventType.MouseUp, new Vector2(bounds.xMin + 4f, press.y));
            context.Equal(beforeChanges, changes, "A short single click does not change the value");
            context.Equal(beforeStarts, starts, "A short single click opens no edit transaction");
            context.Equal(beforeEnds, ends, "A short single click closes no edit transaction");

            Pointer(input, EventType.MouseDown, press);
            Pointer(input, EventType.MouseDrag, press + new Vector2(bounds.width * 2f, 0));
            context.True(field.isDragging && CapturedWithin(field), "Real drag owns pointer capture");
            context.Near(1f, field.value, .00001, "Drag can reach the upper edge");
            Pointer(input, EventType.MouseDrag, press - new Vector2(bounds.width * 2f, 0));
            context.Near(0f, field.value, .00001, "The same drag can reach the lower edge");
            context.Equal(beforeStarts + 1, starts, "Multiple moves share one interaction start");
            Pointer(input, EventType.MouseUp, press - new Vector2(bounds.width * 2f, 0));
            context.True(!field.isDragging && !CapturedWithin(field), "Pointer release clears drag and capture");
            context.Equal(beforeEnds + 1, ends, "Multiple moves share one interaction end");

            field.SetValueWithoutNotify(.37f);
            beforeChanges = changes; beforeStarts = starts; beforeEnds = ends;
            Pointer(input, EventType.MouseDown, new Vector2(bounds.xMin + 5f, press.y), 1);
            Pointer(input, EventType.MouseUp, new Vector2(bounds.xMin + 5f, press.y), 1);
            Pointer(input, EventType.MouseDown, new Vector2(bounds.xMin + 5f, press.y), 2);
            Pointer(input, EventType.MouseUp, new Vector2(bounds.xMin + 5f, press.y), 2);
            await AsyncD.Tick(root, token);
            context.True(field.isEditing && !field.isDragging, "Double click enters numeric edit mode");
            context.Near(.37f, field.value, 0, "Double click leaves the original value intact");
            context.Equal(beforeChanges, changes, "Double click emits no model value change");
            context.Equal(beforeStarts + 1, starts, "Text edit starts exactly one interaction");
            var focused = field.focusController?.focusedElement as VisualElement;
            context.True(editorFocusEntries > 0 && focused != null &&
                (focused == field || focused == editor || editor.Contains(focused)),
                "Double click transfers keyboard focus into the numeric editor");
            Type(editor, "0.64");
            context.True(editor.text.Contains("0.64"), "Public keyboard events change the pending numeric text");
            context.Near(.37f, field.value, 0, "Pending keyboard input does not change the model");
            Key(EditingTarget(editor), KeyCode.Return, '\n');
            await AsyncD.Tick(root, token);
            context.Near(.64f, field.value, .00001, "Enter commits the typed value");
            context.True(!field.isEditing, "Enter exits numeric edit mode");
            context.Equal(beforeChanges + 1, changes, "Enter emits one outer value change");
            context.Equal(beforeEnds + 1, ends, "Enter closes exactly one edit interaction");

            beforeChanges = changes; beforeEnds = ends;
            field.BeginEditing(); await AsyncD.Tick(root, token);
            Type(editor, "0.27");
            Key(EditingTarget(editor), KeyCode.Escape);
            await AsyncD.Tick(root, token);
            context.Near(.64f, field.value, .00001, "Escape discards pending numeric input");
            context.True(!field.isEditing, "Escape exits numeric edit mode");
            context.Equal(beforeChanges, changes, "Discarded text emits no outer value change");
            context.Equal(beforeEnds + 1, ends, "Escape closes the edit interaction");

            beforeChanges = changes;
            field.BeginEditing(); await AsyncD.Tick(root, token);
            Type(editor, "0.23");
            Key(EditingTarget(editor), KeyCode.Tab, '\t');
            await AsyncD.Tick(root, token);
            context.Near(.23f, field.value, .00001, "Tab commits pending numeric input");
            context.True(!field.isEditing, "Tab leaves numeric edit mode");
            context.Equal(beforeChanges + 1, changes, "Tab commits exactly once");

            beforeChanges = changes;
            field.BeginEditing(); await AsyncD.Tick(root, token);
            Type(editor, "0.31");
            outside.Focus(); await AsyncD.Tick(root, token);
            context.Near(.31f, field.value, .00001, "Focus loss commits pending numeric input");
            context.True(!field.isEditing, "Focus loss exits numeric edit mode");
            context.Equal(beforeChanges + 1, changes, "Focus loss commits exactly once");

            field.step = .1f;
            field.SetValueWithoutNotify(.37f);
            context.Near(.37f, field.value, 0, "Changing the user step does not round model synchronization");
            field.BeginEditing(); await AsyncD.Tick(root, token);
            Type(editor, "0.42"); Key(EditingTarget(editor), KeyCode.Return, '\n');
            await AsyncD.Tick(root, token);
            context.Near(.4f, field.value, .00001, "Committed user input follows the configured step");
            field.step = 0f;

            field.Focus(); beforeStarts = starts; beforeEnds = ends;
            Key(field, KeyCode.End);
            context.Near(1f, field.value, 0, "End adjusts to the upper boundary without opening the editor");
            context.Equal(beforeStarts + 1, starts, "Keyboard adjustment begins an interaction");
            Key(field, KeyCode.End, up: true);
            context.Equal(beforeEnds + 1, ends, "Keyboard release closes its interaction");
            Key(field, KeyCode.Home); Key(field, KeyCode.Home, up: true);
            context.Near(0f, field.value, 0, "Home adjusts to the lower boundary");

            field.SetValueWithoutNotify(.5f); beforeEnds = ends;
            Pointer(input, EventType.MouseDown, press);
            Pointer(input, EventType.MouseDrag, press + new Vector2(20f, 0));
            context.True(field.isDragging, "Capture-loss case begins a real drag");
            outside.CapturePointer(PointerId.mousePointerId);
            await AsyncD.Tick(root, token);
            context.True(!field.isDragging && !CapturedWithin(field), "Capture loss ends the active drag");
            context.Equal(beforeEnds + 1, ends, "Capture loss closes the interaction once");
            outside.ReleasePointer(PointerId.mousePointerId);

            beforeEnds = ends;
            Pointer(input, EventType.MouseDown, press);
            Pointer(input, EventType.MouseDrag, press + new Vector2(-20f, 0));
            container.SetEnabled(false);
            for (int tick = 0; tick < 2; tick++) await AsyncD.Tick(root, token);
            context.True(!field.isDragging && !CapturedWithin(field), "Disabling an ancestor releases drag capture");
            context.Equal(beforeEnds + 1, ends, "Ancestor disable closes the drag interaction once");
            beforeStarts = starts; field.BeginEditing();
            context.True(!field.isEditing && starts == beforeStarts, "Disabled field cannot enter edit mode");
            container.SetEnabled(true);

            field.BeginEditing(); await AsyncD.Tick(root, token); beforeEnds = ends;
            container.SetEnabled(false);
            for (int tick = 0; tick < 2; tick++) await AsyncD.Tick(root, token);
            context.True(!field.isEditing, "Disabling an ancestor exits numeric edit mode");
            context.Equal(beforeEnds + 1, ends, "Ancestor disable closes the text interaction once");
            container.SetEnabled(true);

            container.style.display = DisplayStyle.None;
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            beforeStarts = starts;
            field.BeginEditing();
            context.True(!field.isEditing, "A hidden ancestor prevents keyboard editing");
            context.Equal(beforeStarts, starts, "A hidden field does not open an interaction");
            container.style.display = DisplayStyle.Flex;
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            beforeEnds = ends;
            field.BeginEditing();
            for (int tick = 0; tick < 2; tick++) await AsyncD.Tick(root, token);
            context.True(field.isEditing, "Showing the ancestor restores keyboard editing");
            container.style.display = DisplayStyle.None;
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            context.True(!field.isEditing, "Hiding an ancestor ends keyboard editing");
            context.Equal(beforeEnds + 1, ends, "Ancestor hide closes the text interaction once");
            container.style.display = DisplayStyle.Flex;
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);

            beforeEnds = ends;
            Pointer(input, EventType.MouseDown, press);
            Pointer(input, EventType.MouseDrag, press + new Vector2(20f, 0));
            field.RemoveFromHierarchy();
            context.True(!field.isDragging && !field.isEditing, "Detach synchronously ends active interaction");
            context.Equal(beforeEnds + 1, ends, "Detach closes the interaction once");
            container.Add(field); await AsyncD.Tick(root, token);
            context.True(!CapturedWithin(field), "Reattaching does not restore stale pointer capture");
            context.Equal(starts, ends, "All started interactions have a matching end");

            root.Clear();
            window.position = new Rect(80, 80, 580, 260);
            AddPreviewTheme(root, false);
            AddPreviewTheme(root, true);
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            foreach (var preview in root.Query<WhimTexSliderField>().ToList())
            {
                context.Near(20f, preview.worldBound.height, .1, "Preview retains the native field height");
                var previewInput = preview.Q<VisualElement>(className: "whimtex-slider-field__input");
                if (!preview.ClassListContains("whimtex-slider-field--editing"))
                {
                    var previewThumb = preview.Q<VisualElement>(className: "whimtex-slider-field__thumb");
                    context.True(previewThumb.worldBound.xMin >= previewInput.worldBound.xMin + previewInput.resolvedStyle.borderLeftWidth - .1f &&
                        previewThumb.worldBound.xMax <= previewInput.worldBound.xMax - previewInput.resolvedStyle.borderRightWidth + .1f,
                        "Thumb remains inside the side borders at every preview value and width");
                    var previewFill = preview.Q<VisualElement>(className: "whimtex-slider-field__fill");
                    var previewTrack = preview.Q<VisualElement>(className: "whimtex-slider-field__track");
                    context.True(previewFill.style.backgroundColor.value.grayscale > previewTrack.style.backgroundColor.value.grayscale,
                        "Filled tone remains brighter than the empty tone in both themes");
                }
            }
            string previewPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                "Temp", "WhimTex", "slider-field-preview-" + Guid.NewGuid().ToString("N") + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(previewPath));
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window, previewPath, token);
            context.True(System.IO.File.Exists(previewPath) && new System.IO.FileInfo(previewPath).Length > 32,
                "Owned-window pixels were saved for manual visual review");
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (window != null) window.Close(); },
                () => { if (previousFocus != null) previousFocus.Focus(); });
        }
    }

    private static void AddPreviewTheme(VisualElement root, bool light)
    {
        var panel = new VisualElement();
        panel.style.paddingLeft = panel.style.paddingRight = 8f;
        panel.style.paddingTop = panel.style.paddingBottom = 6f;
        panel.style.backgroundColor = light ? new Color(.84f, .84f, .84f) : new Color(.23f, .23f, .23f);
        panel.style.color = light ? new Color(.16f, .16f, .16f) : new Color(.8f, .8f, .8f);
        root.Add(panel);
        panel.Add(new Label(light ? "Light" : "Dark"));
        var compactRow = PreviewRow(panel, "48 x 20");
        foreach (float value in new[] { 0f, .01f, .1234567f, .25f, .5f, .75f, .99f, 1f })
            PreviewField(compactRow, value, 48f, light);
        var wideRow = PreviewRow(panel, "120 x 20");
        foreach (float value in new[] { 0f, .5f, 1f }) PreviewField(wideRow, value, 120f, light);
        var states = PreviewRow(panel, "States");
        foreach (string state in new[] { "Focus", "Drag", "Disabled", "Type" })
        {
            var cell = new VisualElement();
            states.Add(cell);
            cell.Add(new Label(state));
            var preview = PreviewField(cell, state == "Type" ? .1234567f : .5f, 104f, light);
            if (state == "Focus") preview.AddToClassList("whimtex-slider-field--focused");
            else if (state == "Drag") preview.AddToClassList("whimtex-slider-field--dragging");
            else if (state == "Disabled") preview.SetEnabled(false);
            else preview.AddToClassList("whimtex-slider-field--editing");
        }
    }

    private static VisualElement PreviewRow(VisualElement parent, string label)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 3f } };
        row.Add(new Label(label) { style = { width = 80f } });
        parent.Add(row);
        return row;
    }

    private static WhimTexSliderField PreviewField(VisualElement parent, float value, float width, bool light)
    {
        var field = new WhimTexSliderField(null, 0f, 1f) { formatString = "0.####" };
        field.style.width = width; field.style.height = 20f; field.style.marginRight = 4f;
        parent.Add(field);
        field.EnableInClassList("whimtex-slider-field--light", light);
        field.EnableInClassList("whimtex-slider-field--dark", !light);
        field.SetValueWithoutNotify(value);
        return field;
    }

    private static Task ExecuteIntegration(TestContext context, CancellationToken token)
        => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope =>
    {
        // Reflection is limited to package-owned WhimTex state and methods.
        // Pointer, keyboard, capture and focus use supported public Unity APIs.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var windowType = typeof(WhimTexWindow);
        var document = scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        document.hideFlags = HideFlags.HideAndDontSave;
        Layer FindLayer(string id)
        {
            foreach (var layer in document.layers)
                if (layer.Id == id) return layer;
            throw new InvalidOperationException("Missing owned test layer: " + id);
        }
        document.width = document.height = 8;
        var first = new Layer(new ColorFillLayerBehaviour { color = Color.red })
            { layerName = "First", opacity = .2f };
        var second = new Layer(new ColorFillLayerBehaviour { color = Color.green })
            { layerName = "Second", opacity = .6f };
        var group = new Layer(new GroupLayerBehaviour()) { layerName = "Group", opacity = .8f };
        document.layers.Add(first); document.layers.Add(second); document.layers.Add(group);
        string firstId = null, secondId = null, groupId = null;
        WhimTexWindow window = null;
        try
        {
            window = scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexWindow>());
            windowType.GetMethod("SetDocument", flags).Invoke(window, new object[] { document });
            firstId = first.Id; secondId = second.Id; groupId = group.Id;
            context.True(firstId != null && secondId != null && groupId != null,
                "SetDocument normalizes stable IDs on the owned layer models");
            window.titleContent = new GUIContent("WhimTex opacity integration regression");
            window.ShowUtility();
            window.position = new Rect(80, 80, 1000, 720);
            window.CreateGUI();
            var root = window.rootVisualElement;
            windowType.GetMethod("SelectOnlyLayer", flags).Invoke(window, new object[] { firstId });
            var selected = (List<string>)windowType.GetField("selectedLayerIds", flags).GetValue(window);
            selected.Add(secondId);
            windowType.GetMethod("RefreshToolkitInterface", flags).Invoke(window, new object[] { false });
            for (int tick = 0; tick < 4; tick++) await AsyncD.Tick(root, token);

            VisualElement Row(string id)
            {
                foreach (var row in root.Query<VisualElement>(className: "whimtex-layer-row").ToList())
                    if (Equals(row.userData, id)) return row;
                return null;
            }
            var firstRow = Row(firstId);
            var groupRow = Row(groupId);
            context.True(firstRow != null && groupRow != null, "Owned document builds both leaf and group rows");
            var opacity = firstRow.Q<WhimTexSliderField>(className: "whimtex-layer-opacity");
            context.True(opacity != null && groupRow.Q<WhimTexSliderField>(className: "whimtex-layer-opacity") != null,
                "Leaf and group opacity use the reusable combined field");
            context.Near(.2f, opacity.value, .00001, "Leaf field initially reflects its model value");
            context.Near(48f, opacity.worldBound.width, .1, "Layer opacity keeps its compact column width");
            context.Near(20f, opacity.worldBound.height, .1, "Layer opacity keeps the row height");
            var input = opacity.Q<VisualElement>(className: "whimtex-slider-field__input");
            context.True(input != null && input.worldBound.width > 10f, "Layer opacity input is laid out");
            Vector2 press = input.worldBound.center;
            float width = input.worldBound.width;
            Pointer(input, EventType.MouseDown, press);
            Pointer(input, EventType.MouseDrag, press + new Vector2(width * 2f, 0));
            Pointer(input, EventType.MouseDrag, press + new Vector2(width * .3f, 0));
            Pointer(input, EventType.MouseUp, press + new Vector2(width * .3f, 0));
            await AsyncD.Tick(root, token);
            float committed = FindLayer(firstId).opacity;
            context.True(committed > .2f && committed < .9f, "Row drag commits its final relative adjustment");
            context.Near(committed, FindLayer(secondId).opacity, .00001,
                "Dragging one selected row adjusts the second selected layer too");
            context.Near(.8f, FindLayer(groupId).opacity, .00001, "Unselected group opacity is preserved");
            selected = (List<string>)windowType.GetField("selectedLayerIds", flags).GetValue(window);
            context.True(selected.Count == 2 && selected.Contains(firstId) && selected.Contains(secondId),
                "Opacity drag preserves the existing layer selection");
            context.True(!opacity.isDragging && !CapturedWithin(opacity), "Row interaction ends without layer drag capture");

            groupRow = Row(groupId);
            var groupOpacity = groupRow.Q<WhimTexSliderField>(className: "whimtex-layer-opacity");
            var groupInput = groupOpacity.Q<VisualElement>(className: "whimtex-slider-field__input");
            Pointer(groupInput, EventType.MouseDown, groupInput.worldBound.center, 2);
            Pointer(groupInput, EventType.MouseUp, groupInput.worldBound.center, 2);
            await AsyncD.Tick(root, token);
            context.True(groupOpacity.isEditing, "Group opacity double click reaches text edit rather than row drag");
            context.Near(.8f, FindLayer(groupId).opacity, .00001, "Group double click does not jump its value");
            var groupEditor = groupOpacity.Q<FloatField>(className: "whimtex-slider-field__editor");
            Type(groupEditor, "0.35");
            Key(EditingTarget(groupEditor), KeyCode.Return, '\n');
            await AsyncD.Tick(root, token);
            context.Near(.35f, FindLayer(groupId).opacity, .00001, "Group numeric edit commits to the model");
            context.Near(committed, FindLayer(firstId).opacity, .00001, "Unselected leaf is unchanged by group edit");
            var settingsOpacity = root.Q<Slider>("layerOpacity");
            context.True(settingsOpacity != null && settingsOpacity.showInputField,
                "Layer Settings retains its ordinary opacity slider");
            context.Near(.35f, settingsOpacity.value, .00001, "Layer Settings reflects the committed group opacity");

            firstRow = Row(firstId);
            opacity = firstRow.Q<WhimTexSliderField>(className: "whimtex-layer-opacity");
            var nameField = firstRow.Q<TextField>(className: "whimtex-layer-name");
            var nameInput = nameField.Q<VisualElement>(className: "unity-base-field__input");
            var blendInput = firstRow.Q<EnumField>(className: "whimtex-layer-blend")
                .Q<VisualElement>(className: "unity-base-field__input");
            var measurements = new List<string> { "Control\tBackground\tTop\tBottom\tLeft\tRight\tText\tRadius\tFont size\tHeight" };
            measurements.Add(DescribeStyle("Name label", nameField.Q<Label>(className: "whimtex-layer-name__display")));
            measurements.Add(DescribeStyle("Blend", blendInput));
            Pointer(nameField, EventType.MouseDown, nameField.worldBound.center, 2);
            Pointer(nameField, EventType.MouseUp, nameField.worldBound.center, 2);
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            context.True(nameField.ClassListContains("whimtex-layer-name--editing"),
                "Name style comparison opens its keyboard editor");
            measurements.Add(DescribeStyle("Name focused", nameInput));
            float nameInputHeight = nameInput.worldBound.height;
            float nameInputRadius = nameInput.resolvedStyle.borderTopLeftRadius;
            float nameInputFontSize = nameInput.resolvedStyle.fontSize;
            Key(nameField, KeyCode.Escape);
            opacity.Focus();
            for (int tick = 0; tick < 3; tick++) await AsyncD.Tick(root, token);
            var opacityInput = opacity.Q<VisualElement>(className: "whimtex-slider-field__input");
            measurements.Add(DescribeStyle("Opacity focused", opacityInput));
            context.Near(nameInputHeight, opacityInput.worldBound.height, .1,
                "Combined input matches the neighboring name field height");
            context.Near(nameInputRadius, opacityInput.resolvedStyle.borderTopLeftRadius, .1,
                "Combined input matches the neighboring field corner radius");
            context.Near(nameInputFontSize, opacityInput.resolvedStyle.fontSize, .1,
                "Combined input uses the same font size as neighboring fields");
            string previewDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Temp", "WhimTex", "slider-field-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(previewDirectory);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(previewDirectory, "slider-field-neighbor-styles.tsv"), measurements);
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window,
                System.IO.Path.Combine(previewDirectory, "slider-field-context.png"), token);
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (document != null) Undo.ClearUndo(document); },
                () => { if (window != null) window.DiscardChanges(); },
                () => { if (window != null) WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); });
        }
    });

    private static string DescribeStyle(string name, VisualElement element)
    {
        var style = element.resolvedStyle;
        return string.Join("\t", name, ColorUtility.ToHtmlStringRGBA(style.backgroundColor),
            ColorUtility.ToHtmlStringRGBA(style.borderTopColor), ColorUtility.ToHtmlStringRGBA(style.borderBottomColor),
            ColorUtility.ToHtmlStringRGBA(style.borderLeftColor), ColorUtility.ToHtmlStringRGBA(style.borderRightColor),
            ColorUtility.ToHtmlStringRGBA(style.color), style.borderTopLeftRadius.ToString(),
            style.fontSize.ToString(), element.worldBound.height.ToString());
    }
}
