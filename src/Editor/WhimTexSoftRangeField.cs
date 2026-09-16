using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexSoftRangeField : BaseField<float>
    {
        private readonly Slider slider;
        private readonly FloatField number;
        private readonly FieldMouseDragger<float> labelDragger;

        internal WhimTexSoftRangeField(string label, float minimum, float maximum, bool softMinimum, bool softMaximum) : base(label, new VisualElement())
        {
            WhimTexUI.ApplyWindowStyles(this);
            AddToClassList("whimtex-soft-range");
            var input = this.Q<VisualElement>(className: "unity-base-field__input");
            slider = new Slider(minimum, maximum);
            slider.AddToClassList("whimtex-soft-range-slider");
            number = new FloatField();
            number.AddToClassList("whimtex-soft-range-number");
            labelDragger = new FieldMouseDragger<float>(number);
            labelDragger.SetDragZone(labelElement);
            labelElement.AddToClassList("unity-base-field__label--with-dragger");
            input.Add(slider); input.Add(number);
            slider.RegisterValueChangedCallback(e => { e.StopPropagation(); value = e.newValue; });
            slider.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button == 0) value = slider.value;
            }, TrickleDown.TrickleDown);
            number.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) number.SetValueWithoutNotify(value);
                else
                {
                    float next = e.newValue;
                    if (!softMinimum) next = Mathf.Max(minimum, next);
                    if (!softMaximum) next = Mathf.Min(maximum, next);
                    value = next;
                    number.SetValueWithoutNotify(value);
                }
            });
        }

        public override void SetValueWithoutNotify(float newValue)
        {
            base.SetValueWithoutNotify(newValue);
            slider?.SetValueWithoutNotify(Mathf.Clamp(newValue, slider.lowValue, slider.highValue));
            number?.SetValueWithoutNotify(newValue);
        }
    }
}
