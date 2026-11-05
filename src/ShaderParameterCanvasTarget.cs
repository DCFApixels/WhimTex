using System;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderParameterCanvasTarget
    {
        private readonly ShaderGraphEditorTarget owner;
        private readonly ShaderProgramMode mode;
        internal readonly string ParameterId;
        internal readonly ShaderParameterType Type;

        internal ShaderParameterCanvasTarget(ShaderGraphEditorTarget owner, string parameterId, ShaderParameterType type)
        {
            this.owner = owner;
            mode = owner.Program?.mode ?? default;
            ParameterId = parameterId;
            Type = type;
        }

        internal WhimTexDocument Document => owner.Document;
        internal Layer Layer => owner.Layer;
        internal ShaderFX Effect => owner.Effect;
        internal bool IsEffect => owner.IsEffect;
        internal static bool IsOwnerReady(ShaderGraphEditorTarget owner) => owner != null && !owner.IsReadOnly &&
            owner.Document.width > 0 && owner.Document.height > 0 && owner.Layer?.Behaviour != null &&
            !WhimTexApi.ContainsReservation(owner.Layer);

        internal ShaderParameter Parameter
        {
            get
            {
                if (!IsOwnerReady(owner)) return null;
                var program = owner.Program;
                if (program == null || program.mode != mode || program.ActiveParameters == null) return null;
                foreach (var parameter in program.ActiveParameters)
                    if (parameter != null && parameter.id == ParameterId && Supports(parameter, Type)) return parameter;
                return null;
            }
        }

        internal bool IsReady => Parameter != null;
        internal bool MatchesSelection(WhimTexDocument document, Layer selected) =>
            Document == document && selected != null && selected.Id == owner.LayerId && IsReady;
        internal bool IsCurrent(ShaderParameter parameter) => !Undo.isProcessing && !WhimTexDocument.IsRefreshingUndo &&
            parameter != null && ReferenceEquals(Parameter, parameter);
        internal bool Matches(ShaderParameterCanvasTarget other) => other != null && Document == other.Document &&
            owner.LayerId == other.owner.LayerId && IsEffect == other.IsEffect && Effect == other.Effect &&
            mode == other.mode && Type == other.Type && ParameterId == other.ParameterId;

        private static bool Supports(ShaderParameter parameter, ShaderParameterType type)
        {
            if (type == ShaderParameterType.Transform2D) return parameter.type == type;
            if (type != ShaderParameterType.Point && type != ShaderParameterType.Normal || parameter.IsInteger ||
                parameter.type == ShaderParameterType.Color || parameter.Components != (type == ShaderParameterType.Point ? 2 : 3)) return false;
            if (parameter.type == type) return true;
            if (parameter.controls != null)
                foreach (var control in parameter.controls)
                    if (control != null && control.type == type) return true;
            return false;
        }

        internal void RecordUndo(string name, bool complete = false)
        {
            if (!complete) { owner.RecordUndo(name); return; }
            if (Document != null) Undo.RegisterCompleteObjectUndo(Document, name);
            if (Effect != null) Undo.RegisterCompleteObjectUndo(Effect, name);
        }

        internal void NotifyValuesChanged() => owner.MarkChanged();
    }
}
