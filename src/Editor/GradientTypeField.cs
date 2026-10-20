using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal class GradientTypeField<T> : BaseField<T> where T : struct, Enum
    {
        private readonly T[] types;
        private readonly Button[] buttons;

        protected GradientTypeField(string label, T[] types, Func<T, VisualElement> createIcon, Func<T, string> typeName = null)
            : base(label, new VisualElement())
        {
            this.types = types;
            AddToClassList("whimtex-gradient-type");
            var input = this.Q<VisualElement>(className: inputUssClassName);
            buttons = new Button[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                var type = types[i];
                string title = typeName != null ? typeName(type) : type.ToString();
                var button = WhimTexUI.CreateButton("", () => value = type);
                button.name = "gradientType" + title; button.tooltip = title;
                button.AddToClassList("whimtex-tool-button");
                button.Add(createIcon(type));
                buttons[i] = button; input.Add(button);
            }
            SetValueWithoutNotify(types[0]);
        }

        public override void SetValueWithoutNotify(T next)
        {
            base.SetValueWithoutNotify(next);
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].EnableInClassList("whimtex-tool-button--selected", EqualityComparer<T>.Default.Equals(types[i], next));
        }
    }

    internal sealed class GradientTypeField : GradientTypeField<GradientLayerBehaviour.GradientType>
    {
        private static readonly GradientLayerBehaviour.GradientType[] Types =
        {
            GradientLayerBehaviour.GradientType.Linear, GradientLayerBehaviour.GradientType.Radial,
            GradientLayerBehaviour.GradientType.Angular, GradientLayerBehaviour.GradientType.Square,
            GradientLayerBehaviour.GradientType.Freeform
        };
        internal GradientTypeField(string label) : base(label, Types, WhimTexWindow.CreateGradientTypeIcon) { }
    }

    internal sealed class FigureFillTypeField : GradientTypeField<VectorFillMode>
    {
        private static readonly VectorFillMode[] Types =
        {
            VectorFillMode.None, VectorFillMode.Solid, VectorFillMode.LinearGradient, VectorFillMode.RadialGradient,
            VectorFillMode.AngularGradient, VectorFillMode.SquareGradient, VectorFillMode.Freeform
        };
        private static readonly VectorFillMode[] PaletteTypes =
        {
            VectorFillMode.None, VectorFillMode.Solid, VectorFillMode.LinearGradient, VectorFillMode.RadialGradient,
            VectorFillMode.AngularGradient, VectorFillMode.SquareGradient
        };
        internal FigureFillTypeField(string label, bool freeform)
            : base(label, freeform ? Types : PaletteTypes, WhimTexWindow.CreateFigureFillTypeIcon, TypeName) { }

        private static string TypeName(VectorFillMode type) => type switch
        {
            VectorFillMode.LinearGradient => "Linear",
            VectorFillMode.RadialGradient => "Radial",
            VectorFillMode.AngularGradient => "Angular",
            VectorFillMode.SquareGradient => "Square",
            _ => type.ToString()
        };
    }
}
