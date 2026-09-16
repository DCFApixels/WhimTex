using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class Double2Field : BaseField<Double2>
    {
        private readonly DoubleField x, y;
        internal Double2Field(string label) : base(label, new VisualElement())
        {
            AddToClassList("unity-vector2-field");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            input.AddToClassList("whimtex-double2-input");
            x = new DoubleField("X"); y = new DoubleField("Y");
            x.AddToClassList("whimtex-double2-component"); y.AddToClassList("whimtex-double2-component");
            input.Add(x); input.Add(y);
            x.RegisterValueChangedCallback(e => { e.StopPropagation(); if(ProjectiveMatrix.Finite(e.newValue)) value=new Double2(e.newValue,value.y); else x.SetValueWithoutNotify(value.x); });
            y.RegisterValueChangedCallback(e => { e.StopPropagation(); if(ProjectiveMatrix.Finite(e.newValue)) value=new Double2(value.x,e.newValue); else y.SetValueWithoutNotify(value.y); });
        }
        public override void SetValueWithoutNotify(Double2 value)
        {
            base.SetValueWithoutNotify(value);
            x?.SetValueWithoutNotify(value.x); y?.SetValueWithoutNotify(value.y);
        }
    }
}
