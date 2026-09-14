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
                if (p != null) key += $"{p.id}:{p.name}:{p.type}:{p.hasMinimum}:{p.minimum}:{p.hasMaximum}:{p.maximum}|";
            if (layoutKey != key)
            {
                layoutKey = key;
                Clear(); refresh.Clear();
                foreach (var p in effect.Parameters) if (p != null) AddParameter(p);
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

        private void AddParameter(ShaderFXParameter declaration)
        {
            string id = declaration.id;
            string label = ObjectNames.NicifyVariableName(declaration.name.TrimStart('_'));
            switch (declaration.type)
            {
                case ShaderFXParameterType.Float:
                    if (declaration.hasMinimum && declaration.hasMaximum && declaration.minimum < declaration.maximum)
                    {
                        var slider = new Slider(label, declaration.minimum, declaration.maximum) { showInputField = true };
                        slider.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = p.Clamp(e.newValue)));
                        Add(slider);
                        refresh.Add(() => slider.SetValueWithoutNotify(Find(id).floatValue));
                    }
                    else
                    {
                        var field = new FloatField(label);
                        field.RegisterValueChangedCallback(e => Change(id, p => p.floatValue = p.Clamp(e.newValue)));
                        Add(field);
                        refresh.Add(() => field.SetValueWithoutNotify(Find(id).floatValue));
                    }
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
                    for (; index < effect.Parameters.Count; index++) if (effect.Parameters[index] == declaration) break;
                    var property = data.FindProperty("parameters").GetArrayElementAtIndex(index).FindPropertyRelative("colorValue");
                    var color = WhimTexColorInputs.Bind(new ColorField(label), property, effect.NotifyValuesChanged);
                    Add(color);
                    color.RegisterCallback<DetachFromPanelEvent>(_ => data.Dispose());
                    break;
                case ShaderFXParameterType.Texture2D:
                    var texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
                    texture.RegisterValueChangedCallback(e => Change(id, p => p.textureValue = e.newValue as Texture2D));
                    Add(texture); refresh.Add(() => texture.SetValueWithoutNotify(Find(id).textureValue));
                    break;
                case ShaderFXParameterType.Transform2D:
                    var foldout = new Foldout { text = label, value = true };
                    Add(foldout);
                    var position = new Vector2Field("Position");
                    var size = new Vector2Field("Size");
                    var rotation = new FloatField("Rotation");
                    position.tooltip = "Normalized input coordinates. (0.5, 0.5) is the image center.";
                    size.tooltip = "Relative to the input image. (1, 1) covers the whole image.";
                    position.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.position = e.newValue));
                    size.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.size = new Vector2(ShaderFXTransform.SafeSize(e.newValue.x), ShaderFXTransform.SafeSize(e.newValue.y))));
                    rotation.RegisterValueChangedCallback(e => Change(id, p => p.transformValue.rotation = e.newValue));
                    foldout.Add(position); foldout.Add(size); foldout.Add(rotation);
                    foldout.Add(new Button(() => TextureCompositorWindow.EditFXTransform(effect, id)) { text = "Edit on Canvas", tooltip = "Toggle the green FX frame on the selected layer. Rotate around its center; no pivot handle." });
                    foldout.Add(new Button(() => Change(id, p => p.transformValue = ShaderFXTransform.Default)) { text = "Reset Transform" });
                    refresh.Add(() => { var p = Find(id); position.SetValueWithoutNotify(p.transformValue.position); size.SetValueWithoutNotify(p.transformValue.size); rotation.SetValueWithoutNotify(p.transformValue.rotation); });
                    break;
            }
        }
    }
}
