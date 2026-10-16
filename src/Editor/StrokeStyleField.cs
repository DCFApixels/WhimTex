using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class StrokeStyleField<T> : BaseField<T> where T : Enum
    {
        private readonly Button[] buttons;
        private readonly T[] choices;

        internal StrokeStyleField(string label, params (T value, LayerActionIcon.Kind icon, string tooltip)[] options)
            : base(label, new VisualElement())
        {
            name = "stroke" + label;
            AddToClassList("whimtex-stroke-style");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            buttons = new Button[options.Length]; choices = new T[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var option = options[i]; choices[i] = option.value;
                var button = new Button(() => value = option.value)
                {
                    name = name + option.value,
                    tooltip = option.tooltip
                };
                button.AddToClassList("whimtex-icon-action");
                button.Add(new LayerActionIcon(option.icon));
                input.Add(button); buttons[i] = button;
            }
            RefreshSelection();
        }

        public override void SetValueWithoutNotify(T next)
        {
            base.SetValueWithoutNotify(next); RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].EnableInClassList("whimtex-stroke-style--selected", EqualityComparer<T>.Default.Equals(value, choices[i]));
        }
    }
}
