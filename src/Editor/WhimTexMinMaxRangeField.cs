using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexMinMaxRangeField : BaseField<Vector2>
    {
        private readonly MinMaxSlider slider;
        private readonly FloatField minimumField, maximumField;

        internal WhimTexMinMaxRangeField(string label, float minimum, float maximum) : base(label, new VisualElement())
        {
            WhimTexUI.ApplyWindowStyles(this);
            AddToClassList("whimtex-minmax-range");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            slider = new MinMaxSlider(minimum, maximum, minimum, maximum);
            slider.AddToClassList("whimtex-minmax-range-slider");
            minimumField = new FloatField() { tooltip = "Lower endpoint (X)" };
            maximumField = new FloatField() { tooltip = "Upper endpoint (Y)" };
            minimumField.AddToClassList("whimtex-minmax-range-number");
            maximumField.AddToClassList("whimtex-minmax-range-number");
            input.Add(slider); input.Add(minimumField); input.Add(maximumField);
            slider.RegisterValueChangedCallback(e => { e.StopPropagation(); value = e.newValue; });
            minimumField.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                if (ProjectiveMatrix.Finite(e.newValue)) value = new Vector2(Mathf.Clamp(e.newValue, minimum, value.y), value.y);
                minimumField.SetValueWithoutNotify(value.x);
            });
            maximumField.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                if (ProjectiveMatrix.Finite(e.newValue)) value = new Vector2(value.x, Mathf.Clamp(e.newValue, value.x, maximum));
                maximumField.SetValueWithoutNotify(value.y);
            });
            SetValueWithoutNotify(new Vector2(minimum, maximum));
        }

        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            if (slider != null)
            {
                float x = ProjectiveMatrix.Finite(newValue.x) ? Mathf.Clamp(newValue.x, slider.lowLimit, slider.highLimit) : slider.lowLimit;
                float y = ProjectiveMatrix.Finite(newValue.y) ? Mathf.Clamp(newValue.y, slider.lowLimit, slider.highLimit) : slider.highLimit;
                newValue = new Vector2(Mathf.Min(x, y), Mathf.Max(x, y));
            }
            base.SetValueWithoutNotify(newValue);
            slider?.SetValueWithoutNotify(newValue);
            minimumField?.SetValueWithoutNotify(newValue.x);
            maximumField?.SetValueWithoutNotify(newValue.y);
        }
    }
}
