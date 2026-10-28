using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderFXParameterView
    {
        private static void RenameComponents(VisualElement root)
        {
            root.Query<FloatField>().ForEach(field =>
            {
                var label = field.labelElement;
                if (label.text?.Length == 1)
                {
                    int i = "XYZW".IndexOf(label.text[0]);
                    if (i >= 0) label.text = "RGBA"[i].ToString();
                }
            });
        }

        private bool TryAddTypedField(VisualElement parent, ShaderFXParameter parameter, ShaderFXParameterControl control, bool header)
        {
            if (control.type != ShaderFXParameterType.Toggle && control.type != ShaderFXParameterType.Color &&
                control.type != ShaderFXParameterType.Enum && control.type != ShaderFXParameterType.Seed) return false;
            string id = parameter.id;
            string label = header ? string.Empty : !string.IsNullOrWhiteSpace(control.label) ? control.label : ObjectNames.NicifyVariableName(parameter.name.TrimStart('_'));
            VisualElement field;
            if (control.type == ShaderFXParameterType.Color)
                field = source.CreateColorField(label, id, refresh);
            else if (control.type == ShaderFXParameterType.Enum)
            {
                var choices = new List<string>();
                foreach (string name in control.optionNames) choices.Add(ObjectNames.NicifyVariableName(name));
                for (int i = 0; i < choices.Count; i++)
                    for (int j = i + 1; j < choices.Count; j++)
                        if (choices[i] == choices[j]) { choices[i] += " (" + control.optionNames[i] + ")"; choices[j] += " (" + control.optionNames[j] + ")"; }
                var dropdown = new DropdownField(label, choices, 0);
                TwoChoiceDropdown.Attach(dropdown);
                dropdown.RegisterValueChangedCallback(e =>
                {
                    int index = choices.IndexOf(e.newValue);
                    if (index >= 0) Change(id, p => p.SetNumber(0, control.optionValues[index]));
                });
                refresh.Add(() =>
                {
                    double value = Find(id).GetNumber(); int index = Array.IndexOf(control.optionValues, value);
                    dropdown.SetValueWithoutNotify(index < 0 ? "Custom (" + value.ToString("G17") + ")" : choices[index]);
                });
                field = dropdown;
            }
            else if (control.type == ShaderFXParameterType.Seed)
            {
                double Clamp(double value) => Math.Max(control.softMinimum ? parameter.ValueType == "float" ? -ShaderFXSeed.Limit : ShaderFXNumericValue.Minimum(parameter.ValueType) : control.valueMinimum,
                    Math.Min(control.softMaximum ? parameter.ValueType == "float" ? ShaderFXSeed.Limit : ShaderFXNumericValue.Maximum(parameter.ValueType) : control.valueMaximum, Math.Round(value)));
                var number = new LongField(label);
                number.RegisterValueChangedCallback(e => Change(id, p => p.SetNumber(0, Clamp(e.newValue))));
                refresh.Add(() => number.SetValueWithoutNotify((long)Find(id).GetNumber()));
                field = SeedField(number, id, control);
            }
            else if (parameter.Components == 1)
            {
                var toggle = new Toggle(label);
                toggle.RegisterValueChangedCallback(e => Change(id, p => p.SetNumber(0, e.newValue ? 1 : 0)));
                refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).BoolValue));
                field = toggle;
            }
            else
            {
                var row = new VisualElement();
                row.AddToClassList(BaseField<Vector4>.ussClassName);
                row.AddToClassList(BaseCompositeField<Vector4, FloatField, float>.ussClassName);
                // Match the 18 px row measured on the native Vector4Field in this Editor.
                row.style.minHeight = 18;
                if (label.Length > 0)
                {
                    var title = new Label(label);
                    title.AddToClassList(BaseField<Vector4>.labelUssClassName);
                    title.AddToClassList(BaseCompositeField<Vector4, FloatField, float>.labelUssClassName);
                    title.style.minWidth = 0;
                    title.style.width = 120;
                    title.style.flexShrink = 1;
                    row.Add(title);
                }
                var input = new VisualElement();
                input.AddToClassList(BaseField<Vector4>.inputUssClassName);
                input.AddToClassList(BaseCompositeField<Vector4, FloatField, float>.inputUssClassName);
                input.style.minWidth = parameter.Components * (17 + 14 + 4);
                row.Add(input);
                for (int i = 0; i < parameter.Components; i++)
                {
                    int component = i;
                    var toggle = new Toggle((control.rgba ? "RGBA" : "XYZW")[i].ToString()) { name = "component" + i };
                    toggle.AddToClassList(BaseCompositeField<Vector4, FloatField, float>.fieldUssClassName);
                    if (i == 0) toggle.AddToClassList(BaseCompositeField<Vector4, FloatField, float>.firstFieldVariantUssClassName);
                    // Native Vector4Field component label in Unity 6000.7: resolved width/min-width = 17 px.
                    toggle.labelElement.style.width = toggle.labelElement.style.minWidth = toggle.labelElement.style.maxWidth = 17;
                    toggle.labelElement.style.flexShrink = 0;
                    // Native component label (17) plus native checkbox (14); keep the input visible when narrow.
                    toggle.style.minWidth = 31;
                    toggle.Q(className: Toggle.inputUssClassName).style.justifyContent = Justify.FlexStart;
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.SetNumber(component, e.newValue ? 1 : 0)));
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).GetNumber(component) >= .5));
                    input.Add(toggle);
                }
                field = row;
            }
            if (header) field.AddToClassList("whimtex-fx-group-header-field");
            if (!string.IsNullOrEmpty(control.tooltip)) field.tooltip = control.tooltip;
            parent.Add(WithPreview(field, parameter, control));
            return true;
        }
    }
}
