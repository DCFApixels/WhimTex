using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class TextSpacingField : BaseField<TextSpacing>
    {
        private readonly FloatField[] fields;

        internal TextSpacingField() : base("Spacing Options (em)", new VisualElement())
        {
            AddToClassList("whimtex-text-spacing");
            tooltip = "Extra spacing in em: 1 em is the current font size. Zero keeps the font's standard spacing.";
            var input = this.Q<VisualElement>(className: inputUssClassName);
            fields = new FloatField[4];
            string[] labels = { "Character", "Word", "Line", "Paragraph" };
            for (int rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = new VisualElement(); row.AddToClassList("whimtex-text-spacing-row"); input.Add(row);
                for (int column = 0; column < 2; column++)
                {
                    int index = rowIndex * 2 + column;
                    var field = new FloatField(labels[index]) { name = "textSpacing" + labels[index] };
                    field.tooltip = index == 3 ? "Extra space after explicit paragraph breaks, not automatic wrapping (em)."
                        : "Extra " + labels[index].ToLowerInvariant() + " spacing (em).";
                    field.RegisterValueChangedCallback(e =>
                    {
                        var next = value; float amount = TextSpacing.Clamp(e.newValue);
                        if (index == 0) next.character = amount;
                        else if (index == 1) next.word = amount;
                        else if (index == 2) next.line = amount;
                        else next.paragraph = amount;
                        value = next; field.SetValueWithoutNotify(amount);
                    });
                    fields[index] = field; row.Add(field);
                }
            }
        }

        public override void SetValueWithoutNotify(TextSpacing next)
        {
            base.SetValueWithoutNotify(next);
            if (fields == null) return;
            fields[0].SetValueWithoutNotify(next.character); fields[1].SetValueWithoutNotify(next.word);
            fields[2].SetValueWithoutNotify(next.line); fields[3].SetValueWithoutNotify(next.paragraph);
        }
    }
}
