using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class TextStyleField : BaseField<FontStyle>
    {
        private readonly Button bold, italic;

        internal TextStyleField(string label = null) : base(label, new VisualElement())
        {
            AddToClassList("whimtex-text-style");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            bold = CreateButton("B", "Bold", FontStyle.Bold);
            italic = CreateButton("I", "Italic", FontStyle.Italic);
            input.Add(bold); input.Add(italic);
            RefreshSelection();
        }

        private Button CreateButton(string glyph, string title, FontStyle flag)
        {
            var button = WhimTexUI.CreateButton("", () => value = (FontStyle)((int)value ^ (int)flag));
            button.name = "textStyle" + title; button.tooltip = title;
            button.AddToClassList("whimtex-icon-action");
            var icon = new Label(glyph) { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("whimtex-text-style-icon");
            icon.AddToClassList(flag == FontStyle.Bold ? "whimtex-text-style-icon--bold" : "whimtex-text-style-icon--italic");
            button.Add(icon); return button;
        }

        public override void SetValueWithoutNotify(FontStyle next)
        {
            base.SetValueWithoutNotify(next); RefreshSelection();
        }

        private void RefreshSelection()
        {
            bold?.EnableInClassList("whimtex-text-format-button--selected", ((int)value & (int)FontStyle.Bold) != 0);
            italic?.EnableInClassList("whimtex-text-format-button--selected", ((int)value & (int)FontStyle.Italic) != 0);
        }
    }
}
