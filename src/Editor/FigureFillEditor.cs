using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class FigureFillEditor : VisualElement
    {
        private struct Style
        {
            internal VectorFillMode mode;
            internal Color color;
            internal WhimTexGradient gradient;
            internal Vector2 start, end;
            internal float repetitions;
        }
        internal FigureFillEditor(ShapeLayerBehaviour shape, Action<string, Action> change, WhimTexUI.ValueBindings bindings, bool stroke = false)
            : this(() => stroke ? new Style { mode = shape.strokeMode, color = shape.strokeColor, gradient = shape.strokeGradient, start = shape.strokeGradientStart, end = shape.strokeGradientEnd, repetitions = shape.strokeGradientRepetitions } :
                new Style { mode = shape.fillMode, color = shape.fillColor, gradient = shape.gradient, start = shape.gradientStart, end = shape.gradientEnd, repetitions = shape.gradientRepetitions },
                s => { if (stroke) { shape.strokeMode = s.mode; shape.strokeColor = s.color; shape.strokeGradient = s.gradient; shape.strokeGradientStart = s.start; shape.strokeGradientEnd = s.end; shape.strokeGradientRepetitions = s.repetitions; }
                    else { shape.fillMode = s.mode; shape.fillColor = s.color; shape.gradient = s.gradient; shape.gradientStart = s.start; shape.gradientEnd = s.end; shape.gradientRepetitions = s.repetitions; } }, change, bindings, stroke) { }
        internal FigureFillEditor(VectorShape shape, Action<string, Action> change, WhimTexUI.ValueBindings bindings, bool stroke = false)
            : this(() => stroke ? new Style { mode = shape.strokeMode, color = shape.strokeColor, gradient = shape.strokeGradient, start = shape.strokeGradientStart, end = shape.strokeGradientEnd, repetitions = shape.strokeGradientRepetitions } :
                new Style { mode = shape.fillMode, color = shape.fillColor, gradient = shape.gradient, start = shape.gradientStart, end = shape.gradientEnd, repetitions = shape.gradientRepetitions },
                s => { if (stroke) { shape.strokeMode = s.mode; shape.strokeColor = s.color; shape.strokeGradient = s.gradient; shape.strokeGradientStart = s.start; shape.strokeGradientEnd = s.end; shape.strokeGradientRepetitions = s.repetitions; }
                    else { shape.fillMode = s.mode; shape.fillColor = s.color; shape.gradient = s.gradient; shape.gradientStart = s.start; shape.gradientEnd = s.end; shape.gradientRepetitions = s.repetitions; } }, change, bindings, stroke) { }
        private FigureFillEditor(Func<Style> read, Action<Style> write, Action<string, Action> change, WhimTexUI.ValueBindings bindings, bool stroke)
        {
            string channel = stroke ? "Stroke" : "Fill", prefix = "figure" + channel;
            name = prefix + "Options";
            var choices = new List<string> { "None", "Solid", "Linear", "Radial", "Square", "Diamond", "Circular" };
            var type = WhimTexUI.ConfigureField(new PopupField<string>("Type", choices, Mathf.Clamp((int)read().mode, 0, 6)) { name = prefix + "Type" });
            Add(type); bindings.Track(type, () => choices[Mathf.Clamp((int)read().mode, 0, 6)]);
            type.RegisterValueChangedCallback(e => change("Change " + channel + " Type", () => { var s = read(); s.mode = (VectorFillMode)choices.IndexOf(e.newValue); write(s); }));
            var color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexColorField("Color") { name = prefix + "Color" }, bindings, () => read().color));
            Add(color); color.RegisterValueChangedCallback(e => change("Change " + channel + " Color", () => { var s = read(); s.color = e.newValue; write(s); }));
            var settings = new VisualElement(); Add(settings);
            var gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient") { name = prefix + "Gradient" }, bindings, () => read().gradient));
            settings.Add(gradient);
            gradient.RegisterValueChangedCallback(e => change("Change " + channel + " Gradient", () => { var s = read(); s.gradient = GradientUtility.Create(e.newValue); write(s); }));
            var start = WhimTexUI.ConfigureField(new Vector2Field("Start") { name = prefix + "Start" });
            var end = WhimTexUI.ConfigureField(new Vector2Field("End") { name = prefix + "End" });
            settings.Add(start); settings.Add(end);
            start.tooltip = "Layer-local normalized coordinates: (0,0) bottom left, (1,1) top right.";
            end.tooltip = "Linear: end. Radial: radius endpoint. Square/Diamond: radius and orientation. Circular: starting ray.";
            bindings.Track(start, () => read().start); bindings.Track(end, () => read().end);
            void Position(ChangeEvent<Vector2> e, bool first)
            {
                if (!float.IsFinite(e.newValue.x) || !float.IsFinite(e.newValue.y))
                { (first ? start : end).SetValueWithoutNotify(first ? read().start : read().end); return; }
                change("Move " + channel + " Gradient", () => { var s = read(); if (first) s.start = e.newValue; else s.end = e.newValue; write(s); });
            }
            start.RegisterValueChangedCallback(e => Position(e, true)); end.RegisterValueChangedCallback(e => Position(e, false));
            var repetitions = WhimTexUI.ConfigureField(new FloatField("Repetitions") { name = prefix + "Repetitions" });
            settings.Add(repetitions); bindings.Track(repetitions, () => read().repetitions);
            repetitions.RegisterValueChangedCallback(e =>
            {
                if (!float.IsFinite(e.newValue) || e.newValue < .0001f)
                { repetitions.SetValueWithoutNotify(read().repetitions); return; }
                change("Change " + channel + " Repetitions", () => { var s = read(); s.repetitions = e.newValue; write(s); });
            });
            bindings.Add(() =>
            {
                var mode = read().mode;
                color.EnableInClassList("whimtex-hidden", mode != VectorFillMode.Solid);
                settings.EnableInClassList("whimtex-hidden", !FigureFill.IsGradient(mode));
                repetitions.EnableInClassList("whimtex-hidden", mode != VectorFillMode.CircularGradient);
                start.label = mode == VectorFillMode.LinearGradient ? "Start" : "Center";
                end.label = mode == VectorFillMode.CircularGradient ? "Direction" : mode == VectorFillMode.LinearGradient ? "End" : "Radius / Direction";
            });
        }
    }
}
