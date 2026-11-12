using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    [AttributeUsage(AttributeTargets.Enum)]
    public sealed class EnumMenuAttribute : Attribute
    {
        public Type FactoryType { get; }

        public EnumMenuAttribute(Type factoryType) => FactoryType = factoryType;
    }

    public interface IEnumMenuFactory
    {
        void Build(EnumMenuBuilder menu);
    }

    public sealed class EnumMenuBuilder
    {
        private readonly Type enumType;
        private readonly GenericMenu menu;
        private readonly Predicate<Enum> selected;
        private readonly Action<Enum> change;
        private readonly Predicate<Enum> allowed;
        private bool hasItems;
        private bool pendingSeparator;

        internal EnumMenuBuilder(Type enumType, GenericMenu menu, Predicate<Enum> selected,
            Action<Enum> change, Predicate<Enum> allowed = null)
        {
            this.enumType = enumType;
            this.menu = menu;
            this.selected = selected;
            this.change = change;
            this.allowed = allowed;
            hasItems = menu.GetItemCount() != 0;
        }

        public void Add(Enum value, string label = null)
        {
            if (value == null || value.GetType() != enumType)
                throw new ArgumentException($"Expected a value of type {enumType.Name}.", nameof(value));
            if (allowed != null && !allowed(value)) return;
            if (pendingSeparator && hasItems) menu.AddSeparator("");
            pendingSeparator = false;
            hasItems = true;
            // GenericMenu treats ASCII '/' as a submenu delimiter.
            menu.AddItem(new GUIContent((label ?? EnumDropdown.Label(value)).Replace('/', '∕')),
                selected(value), () => change(value));
        }

        public void Separator() => pendingSeparator = true;
    }

    public static class EnumDropdown
    {
        private static readonly ConditionalWeakTable<VisualElement, MenuManipulator> attached = new();
        private static readonly Dictionary<Type, IEnumMenuFactory> defaults = new();
        private static readonly Dictionary<Enum, string> labels = new();

        public static string Label(Enum value)
        {
            if (labels.TryGetValue(value, out string label)) return label;
            string name = value.ToString();
            label = value.GetType().GetField(name)?.GetCustomAttribute<InspectorNameAttribute>()?.displayName
                ?? ObjectNames.NicifyVariableName(name);
            labels[value] = label;
            return label;
        }

        public static bool Attach(EnumField field, IEnumMenuFactory factory = null)
        {
            if (factory == null && IsAttached(field)) return true;
            Type enumType = field.value?.GetType();
            if (enumType == null) return false;
            IEnumMenuFactory resolved = factory ?? DefaultFactory(enumType);
            if (resolved == null) return false;
            return Install(field, field.Q(className: EnumField.inputUssClassName), () =>
            {
                var menu = new GenericMenu();
                resolved.Build(new EnumMenuBuilder(enumType, menu,
                    value => !field.showMixedValue && Equals(field.value, value),
                    value => { if (CanChoose(field)) field.value = value; }));
                return menu;
            });
        }

        public static bool Attach<TEnum>(DropdownField field, IEnumMenuFactory factory = null,
            Func<TEnum, string> choiceText = null, string leadingChoice = null) where TEnum : struct, Enum
        {
            if (factory == null && IsAttached(field)) return true;
            Type enumType = typeof(TEnum);
            IEnumMenuFactory resolved = factory ?? DefaultFactory(enumType);
            if (resolved == null) return false;
            string Choice(Enum value) => choiceText == null ? Label(value) : choiceText((TEnum)(object)value);
            void Choose(string value)
            {
                if (CanChoose(field) && field.choices.Contains(value)) field.value = value;
            }

            return Install(field, field.Q(className: DropdownField.inputUssClassName), () =>
            {
                var menu = new GenericMenu();
                bool hasLeadingChoice = leadingChoice != null && field.choices.Contains(leadingChoice);
                if (hasLeadingChoice)
                    menu.AddItem(new GUIContent(leadingChoice.Replace('/', '∕')),
                        !field.showMixedValue && field.value == leadingChoice, () => Choose(leadingChoice));

                var builder = new EnumMenuBuilder(enumType, menu,
                    value => !field.showMixedValue && field.value == Choice(value),
                    value => Choose(Choice(value)), value => field.choices.Contains(Choice(value)));
                if (hasLeadingChoice) builder.Separator();
                resolved.Build(builder);
                return menu;
            });
        }

        internal static bool IsAttached(VisualElement field) => attached.TryGetValue(field, out _);

        private static bool CanChoose(VisualElement field) => field.panel != null && field.enabledInHierarchy;

        private static IEnumMenuFactory DefaultFactory(Type enumType)
        {
            if (defaults.TryGetValue(enumType, out var factory)) return factory;
            var attribute = enumType.GetCustomAttribute<EnumMenuAttribute>();
            if (attribute != null)
            {
                Type factoryType = attribute.FactoryType;
                if (factoryType == null || factoryType.IsAbstract || !typeof(IEnumMenuFactory).IsAssignableFrom(factoryType))
                    throw new InvalidOperationException($"The menu factory for {enumType.Name} must implement {nameof(IEnumMenuFactory)}.");
                factory = (IEnumMenuFactory)Activator.CreateInstance(factoryType, nonPublic: true);
            }
            defaults.Add(enumType, factory);
            return factory;
        }

        private static bool Install(VisualElement field, VisualElement input, Func<GenericMenu> create)
        {
            if (input == null) return false;
            TwoChoiceDropdown.Detach(field);
            if (attached.TryGetValue(field, out var previous))
            {
                field.RemoveManipulator(previous);
                attached.Remove(field);
            }
            var manipulator = new MenuManipulator(input, create);
            attached.Add(field, manipulator);
            field.AddManipulator(manipulator);
            return true;
        }

        private sealed class MenuManipulator : PointerManipulator
        {
            private readonly VisualElement input;
            private readonly Func<GenericMenu> create;

            internal MenuManipulator(VisualElement input, Func<GenericMenu> create)
            {
                this.input = input;
                this.create = create;
            }

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

            private void Open()
            {
                target.Focus();
                create().DropDown(input.worldBound);
            }

            private void Down(PointerDownEvent evt)
            {
                if (!CanChoose(target) || evt.button != 0 || !input.worldBound.Contains(evt.position)) return;
                evt.StopImmediatePropagation();
                Open();
            }

            private void Key(KeyDownEvent evt)
            {
                if (!CanChoose(target) || !(evt.keyCode is KeyCode.Space or KeyCode.Return or KeyCode.KeypadEnter or KeyCode.DownArrow)) return;
                evt.StopImmediatePropagation();
                Open();
            }

            private void Submit(NavigationSubmitEvent evt)
            {
                if (!CanChoose(target)) return;
                evt.StopImmediatePropagation();
                Open();
            }
        }
    }
}
