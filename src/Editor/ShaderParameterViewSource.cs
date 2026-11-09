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
        internal abstract IReadOnlyList<ShaderParameter> Parameters { get; }
        internal abstract string Code { get; }
        internal abstract bool IsReadOnly { get; }
        internal virtual WhimTexDocument Document => null;
        internal virtual bool SupportsLayerTextures => false;
        internal virtual ShaderFX PreviewEffect => null;
        internal virtual bool IsShaderLayer => false;
        internal virtual bool CanEditOnCanvas => false;
        internal virtual Vector2 Dimensions => Vector2.one;
        internal abstract void Change(string id, Action<ShaderParameter> edit, bool recordUndo = true);
        internal abstract void ChangeParameters(string undoName, IReadOnlyList<ShaderParameter> values);
        internal virtual void RefreshParameters() { }
        internal virtual void BeginDrag() { }
        internal virtual void EndDrag(bool changed) { }
        internal virtual void EditOnCanvas(ShaderParameterType type, string id) { }
        internal virtual bool CanPasteValue(ShaderParameter value) => value != null &&
            (value.type != ShaderParameterType.Texture2D || value.textureSource is ShaderTextureSource.Texture or ShaderTextureSource.None ||
             SupportsLayerTextures && value.textureSource is ShaderTextureSource.Layer or ShaderTextureSource.Self);

        internal ShaderParameter Find(string id)
        {
            foreach (var parameter in Parameters) if (parameter != null && parameter.id == id) return parameter;
            return null;
        }

        protected bool CopyParameterValues(List<ShaderParameter> targets, IReadOnlyList<ShaderParameter> values)
        {
            foreach (var value in values)
                if (!CanPasteValue(value) || !targets.Exists(p => p.id == value.id && p.type == value.type && p.ValueType == value.ValueType)) return false;
            foreach (var value in values) ShaderMetadata.CopyValue(value, targets.Find(p => p.id == value.id));
            return true;
        }

        internal virtual WhimTexColorField CreateColorField(string label, string id, List<Action> refresh)
        {
            var field = WhimTexColorInputs.Bind(new WhimTexColorField(label), () => Find(id).GetNumericColor(),
                value => Change(id, p => p.SetNumericColor(value)));
            refresh.Add(() => field.SetValueWithoutNotify(WhimTexColorInputs.DisplayColor(Find(id).GetNumericColor())));
            field.EditableComponents = Find(id).Components;
            field.showAlpha = field.EditableComponents == 4;
            return field;
        }

        internal virtual VisualElement CreateTextureField(string label, string id, List<Action> refresh)
        {
            var root = new VisualElement();
            var source = new DropdownField(label + " Source", new List<string> { "Texture", "None" }, 0);
            var texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
            source.RegisterValueChangedCallback(e => Change(id, p => p.textureSource =
                e.newValue == "None" ? ShaderTextureSource.None : ShaderTextureSource.Texture));
            texture.RegisterValueChangedCallback(e => Change(id, p => p.SetTextureValue(e.newValue as Texture2D)));
            root.Add(source); root.Add(texture);
            refresh.Add(() =>
            {
                var p = Find(id);
                source.SetValueWithoutNotify(p.textureSource == ShaderTextureSource.None ? "None" : "Texture");
                texture.SetValueWithoutNotify(p.textureValue);
                texture.EnableInClassList("whimtex-shader-fx-hidden", p.textureSource == ShaderTextureSource.None);
            });
            return root;
        }
    }

    internal sealed class ShaderFXParameterViewSource : ShaderParameterViewSource
    {
        private readonly ShaderFX effect;
        private readonly List<ShaderParameter> graphParameters = new List<ShaderParameter>();
        private bool GraphMode => effect != null && effect.Mode == ShaderProgramMode.Graph;
        internal ShaderFX Effect => effect;
        internal override ShaderFX PreviewEffect => GraphMode ? null : effect;
        private int undoGroup;
        internal ShaderFXParameterViewSource(ShaderFX effect) { this.effect = effect; RefreshParameters(); }
        internal override IReadOnlyList<ShaderParameter> Parameters => GraphMode ? graphParameters : effect != null ? effect.Parameters : Array.Empty<ShaderParameter>();
        internal override string Code => effect != null && !GraphMode ? effect.Code : string.Empty;
        internal override void RefreshParameters()
        {
            if (GraphMode) ShaderGraphParameterViewSource.RefreshDisplayParameters(graphParameters, effect.Parameters);
            else graphParameters.Clear();
        }
        private ShaderParameter FindValue(string id)
        {
            if (effect != null) foreach (var parameter in effect.Parameters)
                if (parameter?.id == id) return parameter;
            return null;
        }
        private void RecordUndo(string name)
        {
            Undo.RecordObject(effect, name);
            var document = Document;
            if (document != null) Undo.RecordObject(document, name);
        }
        internal override bool IsReadOnly => effect == null || WhimTexApi.IsShaderFXContentLocked(effect);
        internal override WhimTexDocument Document => effect != null
            ? effect.EmbeddedOwner ?? WhimTexWindow.FindFXTransformDocument(effect) : null;
        internal override bool SupportsLayerTextures => true;
        internal override bool CanPasteValue(ShaderParameter value) => base.CanPasteValue(value) &&
            (value.type != ShaderParameterType.Texture2D || value.textureSource != ShaderTextureSource.Layer ||
             string.IsNullOrEmpty(value.textureLayerId) || Document != null && Document.IsUsableShaderTexture(effect, value.textureLayerId));
        internal override bool CanEditOnCanvas => true;
        internal override Vector2 Dimensions
        {
            get
            {
                var document = Document;
                return document != null ? new Vector2(document.width, document.height) : Vector2.one;
            }
        }
        internal override void Change(string id, Action<ShaderParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly || FindValue(id) is not ShaderParameter parameter) return;
            if (recordUndo) RecordUndo("Change FX Parameter");
            edit(parameter); RefreshParameters(); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged();
        }
        internal override void BeginDrag()
        {
            if (IsReadOnly) return;
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change FX Parameter"); RecordUndo("Change FX Parameter");
        }
        internal override void EndDrag(bool changed) { if (changed) Undo.CollapseUndoOperations(undoGroup); }
        internal override void ChangeParameters(string undoName, IReadOnlyList<ShaderParameter> values)
        {
            if (IsReadOnly || values.Count == 0) return;
            foreach (var value in values)
                if (!CanPasteValue(value) || FindValue(value.id) is not ShaderParameter parameter || parameter.type != value.type || parameter.ValueType != value.ValueType) return;
            RecordUndo(undoName);
            foreach (var value in values) ShaderMetadata.CopyValue(value, FindValue(value.id));
            RefreshParameters(); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged();
        }
        internal override void EditOnCanvas(ShaderParameterType type, string id)
        {
            if (type == ShaderParameterType.Point) WhimTexWindow.EditFXPoint(effect, id);
            else if (type == ShaderParameterType.Normal) WhimTexWindow.EditFXNormal(effect, id);
            else WhimTexWindow.EditFXTransform(effect, id);
        }
        internal override WhimTexColorField CreateColorField(string label, string id, List<Action> refresh)
        {
            return base.CreateColorField(label, id, refresh);
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
        internal override IReadOnlyList<ShaderParameter> Parameters => read().dynamics.hlslParameters;
        internal override string Code => read().dynamics.hlslCode;
        internal override bool IsReadOnly => read().dynamics.source != BrushTipSource.HLSL;
        internal override void Change(string id, Action<ShaderParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly) return;
            try
            {
                var settings = read();
                var values = new List<ShaderParameter>(Parameters.Count);
                foreach (var parameter in Parameters) values.Add(parameter.Copy());
                var value = values.Find(p => p.id == id);
                if (value == null) return;
                edit(value);
                apply(() => settings.ApplyHlsl(settings.dynamics.hlslCode, values, settings.dynamics.hlslResolution));
            }
            catch (Exception exception) { error(exception.Message); }
        }
        internal override void ChangeParameters(string undoName, IReadOnlyList<ShaderParameter> replacements)
        {
            if (IsReadOnly || replacements.Count == 0) return;
            try
            {
                var settings = read();
                var values = new List<ShaderParameter>(Parameters.Count);
                foreach (var parameter in Parameters) values.Add(parameter.Copy());
                if (!CopyParameterValues(values, replacements)) return;
                apply(() => settings.ApplyHlsl(settings.dynamics.hlslCode, values, settings.dynamics.hlslResolution));
            }
            catch (Exception exception) { error(exception.Message); }
        }
    }
}
