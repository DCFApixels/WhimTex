using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class TwoChoiceDropdownManipulator<T> : PointerManipulator
    {
        internal const long HoldMilliseconds = 300;
        internal const float DragOpenDistance = 4f;
        private readonly Func<IReadOnlyList<T>> getChoices;
        private readonly Func<T> getValue;
        private readonly Action<T> setValue;
        private readonly Func<T, string> getLabel;
        private readonly VisualElement input;
        private IVisualElementScheduledItem hold;
        private int pointer = -1;
        private double started;
        private Vector2 pressPosition;
        private T first, second, initial;
        private Rect menuScreenRect;
        private EditorWindow owner;

        internal TwoChoiceDropdownManipulator(VisualElement input, Func<IReadOnlyList<T>> getChoices,
            Func<T> getValue, Action<T> setValue, Func<T, string> getLabel)
        {
            this.input = input;
            this.getChoices = getChoices;
            this.getValue = getValue;
            this.setValue = setValue;
            this.getLabel = getLabel;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            target.RegisterCallback<FocusOutEvent>(OnFocusOut);
            target.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Cancel();
            target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            target.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            target.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (pointer >= 0 || evt.button != 0 || evt.pointerType != UnityEngine.UIElements.PointerType.mouse ||
                evt.modifiers != EventModifiers.None || !target.enabledInHierarchy ||
                input == null || !input.worldBound.Contains(evt.position)) return;
            var choices = getChoices();
            if (choices == null || choices.Count != 2 || Equal(choices[0], choices[1])) return;
            initial = getValue();
            if (!Equal(initial, choices[0]) && !Equal(initial, choices[1])) return;
            first = choices[0];
            second = choices[1];
            target.Focus();
            owner = EditorWindow.focusedWindow;
            menuScreenRect = input.worldBound;
            menuScreenRect.position = GUIUtility.GUIToScreenPoint(menuScreenRect.position);
            pointer = evt.pointerId;
            pressPosition = evt.position;
            started = EditorApplication.timeSinceStartup;
            target.CapturePointer(pointer);
            hold ??= target.schedule.Execute(OpenMenu);
            hold.ExecuteLater(HoldMilliseconds);
            evt.StopImmediatePropagation();
        }

        private bool IsCurrent()
        {
            var choices = getChoices();
            return target.panel != null && target.enabledInHierarchy &&
                EditorWindow.focusedWindow == owner && Equal(getValue(), initial) &&
                choices != null && choices.Count == 2 && Equal(choices[0], first) && Equal(choices[1], second);
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointer || evt.button != 0) return;
            bool apply = IsCurrent() && input.worldBound.Contains(evt.position);
            bool held = (EditorApplication.timeSinceStartup - started) * 1000 >= HoldMilliseconds;
            evt.StopImmediatePropagation();
            if (apply && held) OpenMenu();
            else
            {
                var next = Equal(initial, first) ? second : first;
                Cancel();
                if (apply) setValue(next);
            }
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointer) return;
            evt.StopImmediatePropagation();
            if ((evt.pressedButtons & 1) == 0) { Cancel(); return; }
            if (IsDownwardDrag(evt.position)) { OpenMenu(); return; }
            var bounds = input.worldBound;
            bool below = evt.position.y >= bounds.yMax && evt.position.x >= bounds.xMin && evt.position.x <= bounds.xMax;
            if (!bounds.Contains(evt.position) && !below) Cancel();
        }

        private bool IsDownwardDrag(Vector2 position) => position.y - pressPosition.y >= DragOpenDistance;

        private void OpenMenu()
        {
            if (pointer < 0) return;
            bool valid = IsCurrent();
            var a = first;
            var b = second;
            var selected = initial;
            var rect = menuScreenRect;
            Cancel();
            if (!valid) return;
            var menu = new GenericMenu { allowDuplicateNames = true };
            menu.AddItem(new GUIContent(getLabel(a)), Equal(selected, a), () => Choose(a));
            menu.AddItem(new GUIContent(getLabel(b)), Equal(selected, b), () => Choose(b));
            rect.position = GUIUtility.ScreenToGUIPoint(rect.position);
            menu.DropDown(rect);
        }

        private void Choose(T value)
        {
            if (target.panel == null || !target.enabledInHierarchy) return;
            var choices = getChoices();
            if (choices == null) return;
            for (int i = 0; i < choices.Count; i++)
                if (Equal(choices[i], value)) { setValue(value); return; }
        }

        private void Cancel()
        {
            hold?.Pause();
            int captured = pointer;
            pointer = -1;
            owner = null;
            if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
        }

        private void OnCancel(PointerCancelEvent evt) { if (evt.pointerId == pointer) Cancel(); }
        private void OnCaptureOut(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
        private void OnDetach(DetachFromPanelEvent evt) => Cancel();
        private void OnFocusOut(FocusOutEvent evt) => Cancel();
        private void OnKey(KeyDownEvent evt)
        {
            if (pointer < 0 || evt.keyCode != KeyCode.Escape) return;
            Cancel();
            evt.StopImmediatePropagation();
        }
        private static bool Equal(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
    }

    internal static class TwoChoiceDropdown
    {
        private static readonly ConditionalWeakTable<VisualElement, IManipulator> attached = new ConditionalWeakTable<VisualElement, IManipulator>();

        internal static void Attach(VisualElement field)
        {
            switch (field)
            {
                case EnumField value: Attach(value); break;
                case PopupField<string> value: Attach(value); break;
                case PopupField<int> value: Attach(value); break;
                case PopupField<Layer> value: Attach(value); break;
                case PopupField<PaintRepeatMode> value: Attach(value); break;
            }
        }

        private static void Install(VisualElement field, IManipulator manipulator)
        {
            attached.Add(field, manipulator);
            field.AddManipulator(manipulator);
        }

        internal static void Attach(EnumField field)
        {
            if (attached.TryGetValue(field, out _)) return;
            Type cachedType = null;
            Enum[] choices = null;
            var labels = new Dictionary<Enum, string>();
            IReadOnlyList<Enum> ReadChoices()
            {
                if (field.showMixedValue) return null;
                Type type = field.value?.GetType();
                if (type == cachedType) return choices;
                cachedType = type;
                choices = null;
                labels.Clear();
                if (type == null || type.IsDefined(typeof(FlagsAttribute), false)) return null;
                var values = Enum.GetValues(type);
                if (values.Length != 2) return null;
                foreach (string name in Enum.GetNames(type))
                {
                    var member = type.GetField(name);
                    // EnumField can include or exclude obsolete values; that setting is not public.
                    if (member.IsDefined(typeof(ObsoleteAttribute), false)) return null;
                    var display = (InspectorNameAttribute)Attribute.GetCustomAttribute(member, typeof(InspectorNameAttribute));
                    labels[(Enum)Enum.Parse(type, name)] = display?.displayName ?? ObjectNames.NicifyVariableName(name);
                }
                choices = new[] { (Enum)values.GetValue(0), (Enum)values.GetValue(1) };
                return choices;
            }
            Install(field, new TwoChoiceDropdownManipulator<Enum>(
                field.Q<VisualElement>(className: EnumField.inputUssClassName), ReadChoices,
                () => field.value, value => field.value = value, value => labels[value]));
        }

        internal static void Attach<T>(PopupField<T> field)
        {
            if (attached.TryGetValue(field, out _)) return;
            Install(field, new TwoChoiceDropdownManipulator<T>(
                field.Q<VisualElement>(className: PopupField<T>.inputUssClassName), () => field.showMixedValue ? null : field.choices,
                () => field.value, value => field.value = value,
                value => field.formatListItemCallback?.Invoke(value) ?? (value is null ? "None" : value.ToString())));
        }
    }
}
