using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class TextAlignmentField : BaseField<TextAlignmentField.Settings>
    {
        internal readonly struct Settings : IEquatable<Settings>
        {
            internal readonly TextAnchor alignment;
            internal readonly bool justify;
            internal Settings(TextAnchor alignment, bool justify) { this.alignment = alignment; this.justify = justify; }
            public bool Equals(Settings other) => alignment == other.alignment && justify == other.justify;
            public override bool Equals(object other) => other is Settings settings && Equals(settings);
            public override int GetHashCode() => (int)alignment * 2 + (justify ? 1 : 0);
        }

        private readonly Button[] buttons;
        private readonly bool vertical;
        private bool canJustify;

        internal TextAlignmentField(string label = null, bool vertical = false) : base(label, new VisualElement())
        {
            this.vertical = vertical;
            AddToClassList("whimtex-text-alignment");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            buttons = new Button[vertical ? 3 : 4];
            var icons = vertical
                ? new[] { LayerActionIcon.Kind.TextAlignTop, LayerActionIcon.Kind.TextAlignMiddle, LayerActionIcon.Kind.TextAlignBottom }
                : new[] { LayerActionIcon.Kind.TextAlignLeft, LayerActionIcon.Kind.TextAlignCenter, LayerActionIcon.Kind.TextAlignRight, LayerActionIcon.Kind.TextJustify };
            var names = vertical ? new[] { "Top", "Middle", "Bottom" } : new[] { "Left", "Center", "Right", "Justify" };
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var button = WhimTexUI.CreateButton("", () => Select(index));
                button.name = "textAlign" + names[i];
                button.tooltip = i == 3 ? "Justify\nExpand word spacing on wrapped lines; keep paragraph-ending lines unchanged." : "Align " + names[i];
                button.AddToClassList("whimtex-icon-action");
                button.Add(new LayerActionIcon(icons[i]));
                buttons[i] = button; input.Add(button);
            }
            RefreshSelection();
        }

        internal void SetCanJustify(bool enabled)
        {
            if (canJustify == enabled) return;
            canJustify = enabled; RefreshSelection();
        }

        private void Select(int index)
        {
            int anchor = (int)value.alignment;
            value = vertical
                ? new Settings((TextAnchor)(index * 3 + anchor % 3), value.justify)
                : index == 3 ? new Settings((TextAnchor)(anchor / 3 * 3), canJustify)
                : new Settings((TextAnchor)(anchor / 3 * 3 + index), false);
        }

        public override void SetValueWithoutNotify(Settings next)
        {
            base.SetValueWithoutNotify(next); RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (buttons == null) return;
            int selected = vertical ? (int)value.alignment / 3 : canJustify && value.justify ? 3 : (int)value.alignment % 3;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].EnableInClassList("whimtex-text-format-button--selected", i == selected);
            }
        }
    }
}
