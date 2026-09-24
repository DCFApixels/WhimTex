using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXParameterView : VisualElement
    {
        private readonly ShaderFX effect;
        private readonly List<ShaderFXParameter> layout = new List<ShaderFXParameter>();
        private readonly Dictionary<string, ShaderFXParameter> parametersById = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShaderFXParameter> parametersByName = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
        private bool layoutBuilt;
        private readonly List<Action> refresh = new List<Action>();

        internal ShaderFXParameterView(ShaderFX effect) { this.effect = effect; Refresh(); }

        internal void Refresh()
        {
            if (effect == null) return;
            parametersById.Clear();
            parametersByName.Clear();
            bool changed = !layoutBuilt || layout.Count != effect.Parameters.Count;
            for (int i = 0; i < effect.Parameters.Count; i++)
            {
                var p = effect.Parameters[i];
                if (p != null)
                {
                    if (p.id != null && !parametersById.ContainsKey(p.id)) parametersById.Add(p.id, p);
                    if (p.name != null && !parametersByName.ContainsKey(p.name)) parametersByName.Add(p.name, p);
                }
                if (!changed && !SameLayout(layout[i], p)) changed = true;
            }
            if (changed)
            {
                layoutBuilt = true;
                layout.Clear();
                foreach (var p in effect.Parameters) layout.Add(CopyLayout(p));
                Clear(); refresh.Clear();
                var rows = new List<(ShaderFXParameter parameter, ShaderFXParameterControl control)>();
                foreach (var p in effect.Parameters)
                    if (p != null)
                    {
                        if (p.controls.Count == 0) rows.Add((p, null));
                        else foreach (var control in p.controls) rows.Add((p, control));
                    }
                rows.Sort((a, b) => (a.control?.order ?? 0).CompareTo(b.control?.order ?? 0));
                foreach (var row in rows) AddParameter(row.parameter, row.control);
            }
            foreach (var update in refresh) update();
        }

        private ShaderFXParameter Find(string id)
        {
            return id != null && parametersById.TryGetValue(id, out var parameter) ? parameter : null;
        }

        private static bool SameArray<T>(T[] a, T[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
            return true;
        }

        private static bool SameLayout(ShaderFXParameter a, ShaderFXParameter b)
        {
            if (a == null || b == null) return a == b;
            if (a.id != b.id || a.name != b.name || a.type != b.type ||
                a.hasMinimum != b.hasMinimum || a.hasMaximum != b.hasMaximum ||
                a.softMinimum != b.softMinimum || a.softMaximum != b.softMaximum ||
                !a.minimum.Equals(b.minimum) || !a.maximum.Equals(b.maximum) || a.controls.Count != b.controls.Count)
                return false;
            for (int i = 0; i < a.controls.Count; i++)
            {
                var x = a.controls[i]; var y = b.controls[i];
                if (x.type != y.type || x.order != y.order || x.tooltip != y.tooltip ||
                    x.hasMinimum != y.hasMinimum || x.hasMaximum != y.hasMaximum ||
                    x.softMinimum != y.softMinimum || x.softMaximum != y.softMaximum ||
                    !x.minimum.Equals(y.minimum) || !x.maximum.Equals(y.maximum) ||
                    x.visibleIfParameter != y.visibleIfParameter || x.visibleIfNotEqual != y.visibleIfNotEqual ||
                    !x.visibleIfValue.Equals(y.visibleIfValue) || !SameArray(x.headers, y.headers) ||
                    !SameArray(x.optionNames, y.optionNames) || !SameArray(x.optionValues, y.optionValues))
                    return false;
            }
            return true;
        }

        private static ShaderFXParameter CopyLayout(ShaderFXParameter p)
        {
            if (p == null) return null;
            var copy = new ShaderFXParameter { id = p.id, name = p.name, type = p.type,
                hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum,
                softMinimum = p.softMinimum, softMaximum = p.softMaximum, minimum = p.minimum, maximum = p.maximum };
            foreach (var c in p.controls)
                copy.controls.Add(new ShaderFXParameterControl { type = c.type, order = c.order, tooltip = c.tooltip,
                    headers = c.headers == null ? null : (string[])c.headers.Clone(),
                    hasMinimum = c.hasMinimum, hasMaximum = c.hasMaximum,
                    softMinimum = c.softMinimum, softMaximum = c.softMaximum, minimum = c.minimum, maximum = c.maximum,
                    optionNames = c.optionNames == null ? null : (string[])c.optionNames.Clone(),
                    optionValues = c.optionValues == null ? null : (float[])c.optionValues.Clone(),
                    visibleIfParameter = c.visibleIfParameter, visibleIfNotEqual = c.visibleIfNotEqual, visibleIfValue = c.visibleIfValue });
            return copy;
        }

        private bool IsVisible(ShaderFXParameterControl condition)
        {
            parametersByName.TryGetValue(condition.visibleIfParameter, out var driver);
            if (driver == null) return false;
            bool floatDriver = false;
            foreach (var control in driver.controls)
                if (control.type == ShaderFXParameterType.Float) { floatDriver = true; break; }
            bool equal = floatDriver ? Mathf.Approximately(driver.floatValue, condition.visibleIfValue) : driver.floatValue == condition.visibleIfValue;
            return condition.visibleIfNotEqual ? !equal : equal;
        }

        private void Change(string id, Action<ShaderFXParameter> update)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            // Resolve against the current model: Undo can replace parameter objects before the view refreshes.
            ShaderFXParameter value = null;
            foreach (var p in effect.Parameters) if (p != null && p.id == id) { value = p; break; }
            if (value == null) return;
            Undo.RecordObject(effect, "Change FX Parameter");
            update(value);
            EditorUtility.SetDirty(effect);
            effect.NotifyValuesChanged();
            Refresh();
        }

        private void AddParameter(ShaderFXParameter declaration, ShaderFXParameterControl control = null)
        {
            VisualElement rowRoot = this;
            if (!string.IsNullOrEmpty(control?.visibleIfParameter))
            {
                rowRoot = new VisualElement();
                rowRoot.AddToClassList("whimtex-fx-conditional-parameter");
                Add(rowRoot);
                ShaderFXParameterControl condition = control;
                refresh.Add(() =>
                {
                    var display = IsVisible(condition) ? DisplayStyle.Flex : DisplayStyle.None;
                    if (rowRoot.style.display.value != display) rowRoot.style.display = display;
                });
            }

            if (control?.headers != null)
                foreach (string title in control.headers)
                {
                    var heading = new Label(title);
                    heading.AddToClassList("whimtex-fx-parameter-header");
                    rowRoot.Add(heading);
                }
            int firstChild = rowRoot.childCount;
            if (control != null)
            {
                declaration = declaration.Copy();
                declaration.type = control.type;
                declaration.hasMinimum = control.hasMinimum; declaration.hasMaximum = control.hasMaximum;
                declaration.softMinimum = control.softMinimum; declaration.softMaximum = control.softMaximum;
                declaration.minimum = control.minimum; declaration.maximum = control.maximum;
            }
            string id = declaration.id;
            string label = ObjectNames.NicifyVariableName(declaration.name.TrimStart('_'));
            switch (declaration.type)
            {
                case ShaderFXParameterType.Curve:
                    var curve = new CurveField(label);
                    curve.RegisterValueChangedCallback(e => Change(id, p => p.curveValue = WhimTexCurveTexture.Copy(e.newValue)));
                    rowRoot.Add(curve);
                    refresh.Add(() => curve.SetValueWithoutNotify(Find(id).curveValue ?? WhimTexCurveTexture.Default()));
                    break;
                case ShaderFXParameterType.Gradient:
                    var gradient = new WhimTexGradientValueField(label);
                    gradient.RegisterValueChangedCallback(e => Change(id, p => p.gradientValue = e.newValue?.Clone() ?? new WhimTexGradient()));
                    rowRoot.Add(gradient);
                    refresh.Add(() => gradient.SetValueWithoutNotify(Find(id).gradientValue ??= new WhimTexGradient()));
                    break;
                case ShaderFXParameterType.Enum:
                    if (control == null) goto case ShaderFXParameterType.Float;
                    var choices = new List<string>();
                    foreach (var option in control.optionNames) choices.Add(ObjectNames.NicifyVariableName(option));
                    for (int i = 0; i < choices.Count; i++)
                        for (int j = i + 1; j < choices.Count; j++)
                            if (choices[i] == choices[j])
                            {
                                choices[i] += " (" + control.optionNames[i] + ")";
                                choices[j] += " (" + control.optionNames[j] + ")";
                            }
                    var dropdown = new DropdownField(label, choices, 0);
                    dropdown.RegisterValueChangedCallback(e =>
                    {
                        int index = choices.IndexOf(e.newValue);
                        if (index >= 0) Change(id, p => p.floatValue = control.optionValues[index]);
                    });
                    rowRoot.Add(dropdown);
                    refresh.Add(() =>
                    {
                        int index = Array.IndexOf(control.optionValues, Find(id).floatValue);
                        dropdown.SetValueWithoutNotify(index >= 0 ? choices[index] : "Custom (" + Find(id).floatValue.ToString("G9") + ")");
                    });
                    break;
                case ShaderFXParameterType.Bool:
                    var toggle = new Toggle(label);
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue ? 1f : 0f));
                    rowRoot.Add(toggle);
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).BoolValue));
                    break;
                case ShaderFXParameterType.Float:
                    if (declaration.HasSoftRange)
                    {
                        var soft = new WhimTexSoftRangeField(label, declaration.minimum, declaration.maximum, declaration.softMinimum, declaration.softMaximum);
                        soft.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue));
                        rowRoot.Add(soft);
                        refresh.Add(() => soft.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    else if (declaration.hasMinimum && declaration.hasMaximum && declaration.minimum < declaration.maximum)
                    {
                        var slider = new Slider(label, declaration.minimum, declaration.maximum) { showInputField = true };
                        slider.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        rowRoot.Add(slider);
                        var sliderInput = slider.Q<TextField>();
                        float displayedValue = float.NaN;
                        string displayedText = null;
                        refresh.Add(() =>
                        {
                            float value = Find(id).floatValue;
                            if (displayedText == null || !value.Equals(displayedValue))
                            {
                                displayedValue = value;
                                displayedText = value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
                            }
                            slider.SetValueWithoutNotify(value);
                            sliderInput?.SetValueWithoutNotify(displayedText);
                        });
                    }
                    else
                    {
                        var field = new FloatField(label);
                        field.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        rowRoot.Add(field);
                        refresh.Add(() => field.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    break;
                case ShaderFXParameterType.Vector2:
                case ShaderFXParameterType.Point:
                    var vector2 = new Vector2Field(label);
                    if (declaration.type == ShaderFXParameterType.Point)
                        vector2.tooltip = "Normalized canvas UV: bottom-left (0, 0), top-right (1, 1).";
                    vector2.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = declaration.type == ShaderFXParameterType.Point
                        ? new Vector2(Mathf.Clamp01(e.newValue.x), Mathf.Clamp01(e.newValue.y)) : e.newValue));
                    rowRoot.Add(vector2); refresh.Add(() => vector2.SetValueWithoutNotify(Find(id).vectorValue));
                    if (declaration.type == ShaderFXParameterType.Point)
                        rowRoot.Add(new Button(() => TextureCompositorWindow.EditFXPoint(effect, id)) { text = "Edit on Canvas", tooltip = "Drag the point handle on the selected layer. Coordinates are normalized canvas UV from bottom-left (0, 0) to top-right (1, 1)." });
                    break;
                case ShaderFXParameterType.Vector3:
                case ShaderFXParameterType.Normal:
                    var vector3 = new Vector3Field(label);
                    bool normal = declaration.type == ShaderFXParameterType.Normal;
                    vector3.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = normal ? ShaderFXParameter.NormalizeNormal(e.newValue) : e.newValue));
                    rowRoot.Add(vector3); refresh.Add(() => vector3.SetValueWithoutNotify(Find(id).vectorValue));
                    if (normal) rowRoot.Add(new Button(() => TextureCompositorWindow.EditFXNormal(effect,id)) { text = "Edit on Canvas" });
                    break;
                case ShaderFXParameterType.Vector:
                    var vector = new Vector4Field(label);
                    vector.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    rowRoot.Add(vector); refresh.Add(() => vector.SetValueWithoutNotify(Find(id).vectorValue));
                    break;
                case ShaderFXParameterType.Color:
                    // Use the shared picker binding for HDR/Standard display semantics.
                    var data = new SerializedObject(effect);
                    int index = 0;
                    for (; index < effect.Parameters.Count; index++) if (effect.Parameters[index].id == id) break;
                    var property = data.FindProperty("parameters").GetArrayElementAtIndex(index).FindPropertyRelative("colorValue");
                    var color = WhimTexColorInputs.Bind(new ColorField(label), property, effect.NotifyValuesChanged);
                    rowRoot.Add(color);
                    color.RegisterCallback<DetachFromPanelEvent>(_ => data.Dispose());
                    break;
                case ShaderFXParameterType.Texture2D:
                    var texture = new ShaderFXTextureField(effect, id, label);
                    rowRoot.Add(texture); refresh.Add(texture.Refresh);
                    break;
                case ShaderFXParameterType.Transform2D:
                    var foldout = new Foldout { text = label, value = true };
                    rowRoot.Add(foldout);
                    var position = new Vector2Field("Position");
                    var size = new Vector2Field("Size");
                    var rotation = new DoubleField("Rotation");
                    var document = TextureCompositorWindow.FindFXTransformDocument(effect);
                    Vector2 Dimensions() => document != null ? new Vector2(document.width, document.height) : Vector2.one;
                    position.tooltip = "Normalized input coordinates. (0.5, 0.5) is the image center.";
                    size.tooltip = "Relative to the input image. (1, 1) covers the whole image.";
                    position.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditPosition(e.newValue, Dimensions())));
                    size.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditSize(new Vector2(ShaderFXTransform.SafeSize(e.newValue.x), ShaderFXTransform.SafeSize(e.newValue.y)), Dimensions())));
                    rotation.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.EditRotation(e.newValue, Dimensions())));
                    foldout.Add(position); foldout.Add(size); foldout.Add(rotation);
                    foldout.Add(new Button(() => TextureCompositorWindow.EditFXTransform(effect, id)) { text = "Edit on Canvas", tooltip = "Toggle the green FX frame on the selected layer. Rotate around its center; no pivot handle." });
                    foldout.Add(new Button(() => Change(id, p => p.transformValue = ShaderFXTransform.Default)) { text = "Reset Transform" });
                    refresh.Add(() => { var p = Find(id); p.transformValue.GetDisplay(Dimensions(), out var location, out var scale, out var angle); position.SetValueWithoutNotify(location); size.SetValueWithoutNotify(scale); rotation.SetValueWithoutNotify(angle); });
                    break;
            }
            if (!string.IsNullOrEmpty(control?.tooltip))
                for (int i = firstChild; i < rowRoot.childCount; i++) rowRoot[i].tooltip = control.tooltip;
        }
    }
}
