using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class BlendModeMenu
    {
        internal static readonly BlendMode[][] Groups =
        {
            new[] { BlendMode.Normal, BlendMode.Overwrite, BlendMode.None },
            new[] { BlendMode.Lighten, BlendMode.Screen, BlendMode.Dodge, BlendMode.LinearDodge, BlendMode.Add },
            new[] { BlendMode.Darken, BlendMode.Multiply, BlendMode.Burn, BlendMode.LinearBurn },
            new[] { BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight, BlendMode.VividLight,
                BlendMode.LinearLight, BlendMode.LinearLightAddSub, BlendMode.PinLight, BlendMode.HardMix },
            new[] { BlendMode.Difference, BlendMode.Exclusion, BlendMode.Negation, BlendMode.Subtract, BlendMode.Divide },
            new[] { BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity }
        };

        private static readonly ConditionalWeakTable<VisualElement, IManipulator> attached = new();
        private static readonly Dictionary<BlendMode, string> labels = new();

        internal static string Label(BlendMode mode)
        {
            if (labels.TryGetValue(mode, out string result)) return result;
            var member = typeof(BlendMode).GetField(mode.ToString());
            var display = member == null ? null : (InspectorNameAttribute)Attribute.GetCustomAttribute(member, typeof(InspectorNameAttribute));
            result = display?.displayName ?? ObjectNames.NicifyVariableName(mode.ToString());
            labels[mode] = result;
            return result;
        }

        internal static GenericMenu Create(BlendMode? selected, bool passThrough, Action<BlendMode> change,
            Action choosePassThrough = null, Predicate<BlendMode> allowed = null)
        {
            var menu = new GenericMenu();
            bool hasItems = false;
            if (choosePassThrough != null)
            {
                menu.AddItem(new GUIContent("Pass Through"), passThrough, () => choosePassThrough());
                hasItems = true;
            }
            foreach (var group in Groups)
            {
                bool started = false;
                foreach (var mode in group)
                {
                    if (allowed != null && !allowed(mode)) continue;
                    if (!started && hasItems) menu.AddSeparator("");
                    started = hasItems = true;
                    var captured = mode;
                    // GenericMenu treats ASCII '/' as a submenu delimiter.
                    menu.AddItem(new GUIContent(Label(mode).Replace('/', '∕')), !passThrough && selected == mode, () => change(captured));
                }
            }
            return menu;
        }

        internal static void Attach(EnumField field)
        {
            Install(field, field.Q(className: EnumField.inputUssClassName), () => Create(
                field.showMixedValue ? null : (BlendMode?)field.value, false,
                mode => { if (field.panel != null && field.enabledInHierarchy) field.value = mode; }));
        }

        internal static void Attach(DropdownField field)
        {
            bool Allowed(BlendMode mode) => field.choices.Contains(Label(mode)) || field.choices.Contains(mode.ToString());
            Install(field, field.Q(className: DropdownField.inputUssClassName), () =>
            {
                BlendMode? selected = null;
                if (!field.showMixedValue)
                    foreach (var group in Groups)
                        foreach (var mode in group)
                            if (field.value == Label(mode) || field.value == mode.ToString()) selected = mode;
                void Choose(string value)
                {
                    if (field.panel != null && field.enabledInHierarchy && field.choices.Contains(value)) field.value = value;
                }
                return Create(selected, !field.showMixedValue && field.value == "Pass Through",
                    mode => Choose(field.choices.Contains(Label(mode)) ? Label(mode) : mode.ToString()),
                    field.choices.Contains("Pass Through") ? () => Choose("Pass Through") : (Action)null, Allowed);
            });
        }

        private static void Install(VisualElement field, VisualElement input, Func<GenericMenu> create)
        {
            if (attached.TryGetValue(field, out _) || input == null) return;
            var manipulator = new MenuManipulator(input, create);
            attached.Add(field, manipulator);
            field.AddManipulator(manipulator);
        }

        private sealed class MenuManipulator : PointerManipulator
        {
            private readonly VisualElement input;
            private readonly Func<GenericMenu> create;
            internal MenuManipulator(VisualElement input, Func<GenericMenu> create) { this.input = input; this.create = create; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.RegisterCallback<NavigationSubmitEvent>(Submit, TrickleDown.TrickleDown);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                target.UnregisterCallback<NavigationSubmitEvent>(Submit, TrickleDown.TrickleDown);
            }
            private bool Ready => target.panel != null && target.enabledInHierarchy;
            private void Open() { target.Focus(); create().DropDown(input.worldBound); }
            private void Down(PointerDownEvent evt)
            {
                if (!Ready || evt.button != 0 || !input.worldBound.Contains(evt.position)) return;
                evt.StopImmediatePropagation(); Open();
            }
            private void Key(KeyDownEvent evt)
            {
                if (!Ready || !(evt.keyCode == KeyCode.Space || evt.keyCode == KeyCode.Return ||
                    evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.DownArrow)) return;
                evt.StopImmediatePropagation(); Open();
            }
            private void Submit(NavigationSubmitEvent evt)
            {
                if (!Ready) return;
                evt.StopImmediatePropagation(); Open();
            }
        }
    }
}
