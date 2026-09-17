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
        private string layoutKey;
        private readonly List<Action> refresh = new List<Action>();

        internal ShaderFXParameterView(ShaderFX effect) { this.effect = effect; Refresh(); }

        internal void Refresh()
        {
            if (effect == null) return;
            string key = "";
            foreach (var p in effect.Parameters)
                if (p != null)
                {
                    key += $"{p.id}:{p.name}:{p.type}:{p.hasMinimum}:{p.minimum}:{p.hasMaximum}:{p.maximum}:{p.softMinimum}:{p.softMaximum}|";
                    foreach (var control in p.controls) key += JsonUtility.ToJson(control);
                }
            if (layoutKey != key)
            {
                layoutKey = key;
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
            foreach (var p in effect.Parameters) if (p != null && p.id == id) return p;
            return null;
        }

        private void Change(string id, Action<ShaderFXParameter> update)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            var value = Find(id);
            if (value == null) return;
            Undo.RecordObject(effect, "Change FX Parameter");
            update(value);
            EditorUtility.SetDirty(effect);
            effect.NotifyValuesChanged();
            Refresh();
        }

        private void AddParameter(ShaderFXParameter declaration, ShaderFXParameterControl control = null)
        {
            int firstChild = childCount;
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
                case ShaderFXParameterType.Gradient:
                    var gradient = new WhimTexGradientValueField(label);
                    gradient.RegisterValueChangedCallback(e => Change(id, p => p.gradientValue = e.newValue?.Clone() ?? new WhimTexGradient()));
                    Add(gradient);
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
                    Add(dropdown);
                    refresh.Add(() =>
                    {
                        int index = Array.IndexOf(control.optionValues, Find(id).floatValue);
                        dropdown.SetValueWithoutNotify(index >= 0 ? choices[index] : "Custom (" + Find(id).floatValue.ToString("G9") + ")");
                    });
                    break;
                case ShaderFXParameterType.Bool:
                    var toggle = new Toggle(label);
                    toggle.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue ? 1f : 0f));
                    Add(toggle);
                    refresh.Add(() => toggle.SetValueWithoutNotify(Find(id).BoolValue));
                    break;
                case ShaderFXParameterType.Float:
                    if (declaration.HasSoftRange)
                    {
                        var soft = new WhimTexSoftRangeField(label, declaration.minimum, declaration.maximum, declaration.softMinimum, declaration.softMaximum);
                        soft.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = e.newValue));
                        Add(soft);
                        refresh.Add(() => soft.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    else if (declaration.hasMinimum && declaration.hasMaximum && declaration.minimum < declaration.maximum)
                    {
                        var slider = new Slider(label, declaration.minimum, declaration.maximum) { showInputField = true };
                        slider.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        Add(slider);
                        refresh.Add(() =>
                        {
                            float value = Find(id).floatValue;
                            slider.SetValueWithoutNotify(value);
                            slider.Q<TextField>()?.SetValueWithoutNotify(value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                        });
                    }
                    else
                    {
                        var field = new FloatField(label);
                        field.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = declaration.Clamp(e.newValue)));
                        Add(field);
                        refresh.Add(() => field.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    break;
                case ShaderFXParameterType.Vector2:
                    var vector2 = new Vector2Field(label);
                    vector2.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    Add(vector2); refresh.Add(() => vector2.SetValueWithoutNotify(Find(id).vectorValue));
                    break;
                case ShaderFXParameterType.Vector3:
                case ShaderFXParameterType.Normal:
                    var vector3 = new Vector3Field(label);
                    bool normal = declaration.type == ShaderFXParameterType.Normal;
                    vector3.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = normal ? ShaderFXParameter.NormalizeNormal(e.newValue) : e.newValue));
                    Add(vector3); refresh.Add(() => vector3.SetValueWithoutNotify(Find(id).vectorValue));
                    if (normal) Add(new Button(() => TextureCompositorWindow.EditFXNormal(effect,id)) { text = "Edit on Canvas" });
                    break;
                case ShaderFXParameterType.Vector:
                    var vector = new Vector4Field(label);
                    vector.RegisterValueChangedCallback(e => Change(id, p => p.vectorValue = e.newValue));
                    Add(vector); refresh.Add(() => vector.SetValueWithoutNotify(Find(id).vectorValue));
                    break;
                case ShaderFXParameterType.Color:
                    // Use the shared picker binding for HDR/Standard display semantics.
                    var data = new SerializedObject(effect);
                    int index = 0;
                    for (; index < effect.Parameters.Count; index++) if (effect.Parameters[index].id == id) break;
                    var property = data.FindProperty("parameters").GetArrayElementAtIndex(index).FindPropertyRelative("colorValue");
                    var color = WhimTexColorInputs.Bind(new ColorField(label), property, effect.NotifyValuesChanged);
                    Add(color);
                    color.RegisterCallback<DetachFromPanelEvent>(_ => data.Dispose());
                    break;
                case ShaderFXParameterType.Texture2D:
                    var texture = new ShaderFXTextureField(effect, id, label);
                    Add(texture); refresh.Add(texture.Refresh);
                    break;
                case ShaderFXParameterType.Transform2D:
                    var foldout = new Foldout { text = label, value = true };
                    Add(foldout);
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
                for (int i = firstChild; i < childCount; i++) this[i].tooltip = control.tooltip;
        }
    }
}
