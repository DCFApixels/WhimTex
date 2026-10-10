using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal abstract class ShaderParameterViewSource
    {
        internal abstract IReadOnlyList<ShaderFXParameter> Parameters { get; }
        internal abstract string Code { get; }
        internal abstract bool IsReadOnly { get; }
        internal virtual bool CanEditOnCanvas => false;
        internal virtual Vector2 Dimensions => Vector2.one;
        internal abstract void Change(string id, Action<ShaderFXParameter> edit, bool recordUndo = true);
        internal virtual void BeginDrag() { }
        internal virtual void EndDrag(bool changed) { }
        internal virtual void EditOnCanvas(ShaderFXParameterType type, string id) { }

        internal ShaderFXParameter Find(string id)
        {
            foreach (var parameter in Parameters) if (parameter != null && parameter.id == id) return parameter;
            return null;
        }

        internal virtual WhimTexColorField CreateColorField(string label, string id, List<Action> refresh)
        {
            var field = WhimTexColorInputs.Bind(new WhimTexColorField(label), () => Find(id).colorValue,
                value => Change(id, p => p.colorValue = value));
            refresh.Add(() => field.SetValueWithoutNotify(WhimTexColorInputs.DisplayColor(Find(id).colorValue)));
            return field;
        }

        internal virtual VisualElement CreateTextureField(string label, string id, List<Action> refresh)
        {
            var root = new VisualElement();
            var source = new DropdownField(label + " Source", new List<string> { "Texture", "None" }, 0);
            var texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
            source.RegisterValueChangedCallback(e => Change(id, p => p.textureSource =
                e.newValue == "None" ? ShaderFXTextureSource.None : ShaderFXTextureSource.Texture));
            texture.RegisterValueChangedCallback(e => Change(id, p => p.textureValue = e.newValue as Texture2D));
            root.Add(source); root.Add(texture);
            refresh.Add(() =>
            {
                var p = Find(id);
                source.SetValueWithoutNotify(p.textureSource == ShaderFXTextureSource.None ? "None" : "Texture");
                texture.SetValueWithoutNotify(p.textureValue);
                texture.EnableInClassList("whimtex-shader-fx-hidden", p.textureSource == ShaderFXTextureSource.None);
            });
            return root;
        }
    }

    internal sealed class ShaderFXParameterViewSource : ShaderParameterViewSource
    {
        private readonly ShaderFX effect;
        private int undoGroup;
        internal ShaderFXParameterViewSource(ShaderFX effect) => this.effect = effect;
        internal override IReadOnlyList<ShaderFXParameter> Parameters => effect != null ? effect.Parameters : Array.Empty<ShaderFXParameter>();
        internal override string Code => effect != null ? effect.Code : string.Empty;
        internal override bool IsReadOnly => effect == null || WhimTexApi.IsShaderFXContentLocked(effect);
        internal override bool CanEditOnCanvas => true;
        internal override Vector2 Dimensions
        {
            get
            {
                var document = WhimTexWindow.FindFXTransformDocument(effect);
                return document != null ? new Vector2(document.width, document.height) : Vector2.one;
            }
        }
        internal override void Change(string id, Action<ShaderFXParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly || Find(id) is not ShaderFXParameter parameter) return;
            if (recordUndo) Undo.RecordObject(effect, "Change FX Parameter");
            edit(parameter); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged();
        }
        internal override void BeginDrag()
        {
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change FX Parameter"); Undo.RecordObject(effect, "Change FX Parameter");
        }
        internal override void EndDrag(bool changed) { if (changed) Undo.CollapseUndoOperations(undoGroup); }
        internal override void EditOnCanvas(ShaderFXParameterType type, string id)
        {
            if (type == ShaderFXParameterType.Point) WhimTexWindow.EditFXPoint(effect, id);
            else if (type == ShaderFXParameterType.Normal) WhimTexWindow.EditFXNormal(effect, id);
            else WhimTexWindow.EditFXTransform(effect, id);
        }
        internal override WhimTexColorField CreateColorField(string label, string id, List<Action> refresh)
        {
            var data = new SerializedObject(effect);
            int index = 0;
            for (; index < Parameters.Count; index++) if (Parameters[index].id == id) break;
            var property = data.FindProperty("parameters").GetArrayElementAtIndex(index).FindPropertyRelative("colorValue");
            var field = WhimTexColorInputs.Bind(new WhimTexColorField(label), property, effect.NotifyValuesChanged);
            field.RegisterCallback<DetachFromPanelEvent>(_ => data.Dispose());
            return field;
        }
        internal override VisualElement CreateTextureField(string label, string id, List<Action> refresh)
        {
            var field = new ShaderFXTextureField(effect, id, label);
            refresh.Add(field.Refresh);
            return field;
        }
    }

    internal sealed class BrushParameterViewSource : ShaderParameterViewSource
    {
        private readonly Func<PaintToolSettings> read;
        private readonly Action<Action> apply;
        private readonly Action<string> error;
        internal BrushParameterViewSource(Func<PaintToolSettings> read, Action<Action> apply, Action<string> error)
        { this.read = read; this.apply = apply; this.error = error; }
        internal override IReadOnlyList<ShaderFXParameter> Parameters => read().dynamics.hlslParameters;
        internal override string Code => read().dynamics.hlslCode;
        internal override bool IsReadOnly => read().dynamics.source != BrushTipSource.HLSL;
        internal override void Change(string id, Action<ShaderFXParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly) return;
            try
            {
                var settings = read();
                var values = new List<ShaderFXParameter>(Parameters.Count);
                foreach (var parameter in Parameters) values.Add(parameter.Copy());
                var value = values.Find(p => p.id == id);
                if (value == null) return;
                edit(value);
                apply(() => settings.ApplyHlsl(settings.dynamics.hlslCode, values, settings.dynamics.hlslResolution));
            }
            catch (Exception exception) { error(exception.Message); }
        }
    }
}
