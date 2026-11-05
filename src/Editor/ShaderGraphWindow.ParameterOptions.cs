using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private void BuildParameterOptions(VisualElement parent, ShaderParameter parameter)
        {
            string id = parameter.id;
            var kind = ShaderGraphParameters.Kind(parameter);
            if (kind == ShaderParameterType.Enum)
            {
                var control = ShaderGraphParameters.EnsureControl(parameter);
                for (int i = 0; i < control.optionNames.Length; i++)
                {
                    int index = i;
                    var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                    var name = new TextField { value = control.optionNames[i], isDelayed = true };
                    var value = new FloatField { value = (float)control.optionValues[i], isDelayed = true };
                    name.style.flexGrow = value.style.flexGrow = 1;
                    name.style.flexBasis = value.style.flexBasis = 0;
                    name.style.minWidth = value.style.minWidth = 0;
                    name.RegisterValueChangedCallback(e =>
                    {
                        string text = e.newValue?.Trim();
                        var c = ShaderGraphParameters.EnsureControl(Graph.FindParameter(id));
                        if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, @"^[A-Za-z_]\w*$") || Array.FindIndex(c.optionNames, s => s == text) is int other && other >= 0 && other != index)
                        { name.SetValueWithoutNotify(e.previousValue); return; }
                        Edit("Rename Shader Parameter Option", g => ShaderGraphParameters.EnsureControl(g.FindParameter(id)).optionNames[index] = text, rebuild: false);
                    });
                    value.RegisterValueChangedCallback(e =>
                    {
                        var c = ShaderGraphParameters.EnsureControl(Graph.FindParameter(id));
                        int other = Array.IndexOf(c.optionValues, (double)e.newValue);
                        if (!ProjectiveMatrix.Finite(e.newValue) || other >= 0 && other != index) { value.SetValueWithoutNotify(e.previousValue); return; }
                        Edit("Change Shader Parameter Option", g => ShaderGraphParameters.EnsureControl(g.FindParameter(id)).optionValues[index] = e.newValue, rebuild: false);
                    });
                    row.AddManipulator(new ContextualMenuManipulator(e =>
                    {
                        e.menu.AppendAction("Move Up", _ => MoveParameterOption(id, index, -1), index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                        e.menu.AppendAction("Move Down", _ => MoveParameterOption(id, index, 1), index + 1 < control.optionNames.Length ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                        e.menu.AppendAction("Remove", _ => MoveParameterOption(id, index, 0), control.optionNames.Length > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    }));
                    declarationRefresh.Add(() =>
                    {
                        var c = ShaderGraphParameters.EnsureControl(Graph.FindParameter(id));
                        if (index >= c.optionNames.Length) return;
                        name.SetValueWithoutNotify(c.optionNames[index]); value.SetValueWithoutNotify((float)c.optionValues[index]);
                    });
                    row.Add(name); row.Add(value); parent.Add(row);
                }
                parent.Add(new Button(() => Edit("Add Shader Parameter Option", g =>
                {
                    var c = ShaderGraphParameters.EnsureControl(g.FindParameter(id));
                    var names = new List<string>(c.optionNames); var numbers = new List<double>(c.optionValues);
                    int index = names.Count; while (names.Contains("Option" + index) || numbers.Contains(index)) index++;
                    names.Add("Option" + index); numbers.Add(index); c.optionNames = names.ToArray(); c.optionValues = numbers.ToArray();
                })) { text = "Add Option" });
            }
            if (kind is not (ShaderParameterType.Float or ShaderParameterType.Seed or ShaderParameterType.Vector2)) return;
            bool interval = kind == ShaderParameterType.Vector2, seed = kind == ShaderParameterType.Seed;
            if (interval || seed)
            {
                var range = new Toggle("Range") { value = parameter.hasMinimum && parameter.hasMaximum };
                range.RegisterValueChangedCallback(e => Edit("Change Shader Parameter Range", g =>
                {
                    var p = g.FindParameter(id); p.hasMinimum = p.hasMaximum = e.newValue;
                    if (e.newValue && p.maximum <= p.minimum) p.maximum = p.minimum + 1;
                    if (!e.newValue) p.softMinimum = p.softMaximum = p.cyclicRange = false;
                    ShaderGraphParameters.SyncRange(p);
                }));
                parent.Add(range);
                if (!range.value) return;
            }
            if (!seed && (interval || parameter.hasMinimum && parameter.hasMaximum && !parameter.softMinimum && !parameter.softMaximum && parameter.minimum < parameter.maximum))
            {
                var cyclic = new Toggle("Cyclic") { value = parameter.cyclicRange };
                cyclic.RegisterValueChangedCallback(e => Edit("Change Shader Parameter Range", g =>
                {
                    var p = g.FindParameter(id); p.cyclicRange = e.newValue && p.minimum < p.maximum && p.hasMinimum && p.hasMaximum && !p.softMinimum && !p.softMaximum;
                    ShaderGraphParameters.SyncRange(p);
                }));
                parent.Add(cyclic);
            }
            Boundary(true); Boundary(false);
            void Boundary(bool minimum)
            {
                bool enabled = minimum ? parameter.hasMinimum : parameter.hasMaximum;
                bool soft = minimum ? parameter.softMinimum : parameter.softMaximum;
                if (!interval && !parameter.cyclicRange)
                {
                    var options = new List<string>(seed ? new[] { "Hard", "Soft" } : new[] { "None", "Hard", "Soft" });
                    if (!seed && !(parameter.hasMinimum && parameter.hasMaximum && parameter.minimum < parameter.maximum)) options.Remove("Soft");
                    string selected = !enabled ? "None" : soft ? "Soft" : "Hard";
                    var mode = new DropdownField(minimum ? "Min" : "Max", options, Math.Max(0, options.IndexOf(selected)));
                    mode.style.flexGrow = 1; mode.style.minWidth = 0;
                    mode.RegisterValueChangedCallback(e => Edit("Change Shader Parameter Boundary", g =>
                    {
                        var p = g.FindParameter(id); bool has = e.newValue != "None", isSoft = e.newValue == "Soft";
                        if (minimum) { p.hasMinimum = has; p.softMinimum = isSoft; } else { p.hasMaximum = has; p.softMaximum = isSoft; }
                        if (!has) p.softMinimum = p.softMaximum = p.cyclicRange = false;
                        if (p.hasMinimum && p.hasMaximum && p.maximum <= p.minimum) p.maximum = p.minimum + 1;
                        ShaderGraphParameters.SyncRange(p);
                    }));
                    parent.Add(mode);
                }
                if (!enabled) return;
                var field = new FloatField(minimum ? "Minimum" : "Maximum") { value = minimum ? parameter.minimum : parameter.maximum, isDelayed = true };
                field.RegisterValueChangedCallback(e =>
                {
                    var p = Graph.FindParameter(id);
                    float min = minimum ? e.newValue : p.minimum, max = minimum ? p.maximum : e.newValue;
                    if (!ProjectiveMatrix.Finite(e.newValue) || seed && (Mathf.Abs(e.newValue) > ShaderSeed.Limit || e.newValue != Mathf.Round(e.newValue)) ||
                        p.hasMinimum && p.hasMaximum && (min > max || (interval || p.cyclicRange || p.HasSoftRange) && min == max))
                    { field.SetValueWithoutNotify(e.previousValue); return; }
                    Edit("Change Shader Parameter Limits", g =>
                    {
                        var current = g.FindParameter(id); current.minimum = min; current.maximum = max; ShaderGraphParameters.SyncRange(current);
                    });
                });
                declarationRefresh.Add(() => { var p = Graph.FindParameter(id); field.SetValueWithoutNotify(minimum ? p.minimum : p.maximum); });
                parent.Add(field);
            }
        }
        private void MoveParameterOption(string id, int index, int direction) => Edit("Change Shader Parameter Options", g =>
        {
            var c = ShaderGraphParameters.EnsureControl(g.FindParameter(id));
            var names = new List<string>(c.optionNames); var values = new List<double>(c.optionValues);
            if (direction == 0) { names.RemoveAt(index); values.RemoveAt(index); }
            else { int to = index + direction; (names[index], names[to]) = (names[to], names[index]); (values[index], values[to]) = (values[to], values[index]); }
            c.optionNames = names.ToArray(); c.optionValues = values.ToArray();
        });
    }
}
