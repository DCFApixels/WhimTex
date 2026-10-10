using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class TextCasingField : BaseField<TextCasing>
    {
        private readonly Button[] buttons;

        internal TextCasingField(string label = null) : base(label, new VisualElement())
        {
            AddToClassList("whimtex-text-casing");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            buttons = new Button[3];
            for (int i = 0; i < buttons.Length; i++)
            {
                var mode = (TextCasing)(i + 1);
                var button = WhimTexUI.CreateButton("", () => value = value == mode ? TextCasing.Normal : mode);
                button.name = "textCasing" + mode;
                button.tooltip = mode == TextCasing.SmallCaps ? "Small Caps\nShow lowercase letters as smaller capitals." : mode.ToString();
                button.AddToClassList("whimtex-icon-action");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("whimtex-text-casing-icon");
                var letters = new Label(mode == TextCasing.Lowercase ? "aa" : mode == TextCasing.Uppercase ? "AA" : "A") { pickingMode = PickingMode.Ignore };
                icon.Add(letters);
                if (mode == TextCasing.SmallCaps)
                {
                    var small = new Label("A") { pickingMode = PickingMode.Ignore };
                    small.AddToClassList("whimtex-text-casing-icon--small"); icon.Add(small);
                }
                button.Add(icon); buttons[i] = button; input.Add(button);
            }
            RefreshSelection();
        }

        public override void SetValueWithoutNotify(TextCasing next)
        {
            base.SetValueWithoutNotify(next); RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].EnableInClassList("whimtex-text-format-button--selected", value == (TextCasing)(i + 1));
        }
    }
}
