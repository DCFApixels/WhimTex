using System;
using System.Collections.Generic;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphEditorTarget
    {
        internal readonly WhimTexDocument Document;
        internal readonly string LayerId;
        internal readonly bool IsEffect;
        private readonly ShaderFX effect;

        internal ShaderGraphEditorTarget(WhimTexDocument document, string layerId, ShaderFX effect = null, bool effectTarget = false)
        { Document = document; LayerId = layerId; this.effect = effect; IsEffect = effectTarget; }

        internal Layer Layer => Document != null ? Document.FindLayer(LayerId) : null;
        internal ShaderFX Effect => IsEffect && effect != null && Layer?.fx?.Contains(effect) == true ? effect : null;
        internal ShaderLayerBehaviour Behaviour => !IsEffect ? Layer?.Behaviour as ShaderLayerBehaviour : null;
        internal ShaderProgram Program => IsEffect ? Effect?.Program : Behaviour?.program;
        internal ShaderGraph Graph => Program?.draftGraph;
        internal bool Available => Program != null;
        internal bool GraphActive => Available && Program.mode == ShaderProgramMode.Graph;
        internal bool IsLinked => IsEffect ? Effect?.IsCatalogLinked == true : ShaderPresetLink.IsLinked(Program);
        internal bool IsReadOnly => !Available || WhimTexApi.IsLayerContentLocked(Document, Layer) ||
            IsEffect && WhimTexApi.IsShaderFXContentLocked(Effect);
        internal string Identity => UnityObjectID.FromObject(Document) + ":" + LayerId +
            (IsEffect ? ":fx:" + UnityObjectID.FromObject(effect) : ":layer");
        internal string Title => IsEffect ? Layer?.layerName + " / " + Effect?.name : Layer?.layerName;
        private ShaderCompilationState Status => IsEffect ? Effect?.CompilationStatus : Behaviour?.CompilationStatus;
        internal string Diagnostics => Status?.Diagnostics ?? "Not compiled yet.";
        internal IReadOnlyList<ShaderGraphMessage> Messages =>
            Status?.Messages ?? Array.Empty<ShaderGraphMessage>();
        internal string GeneratedSource => IsEffect ? Effect?.GeneratedSource : Behaviour?.GeneratedSource;
        internal bool HasCompileError => Status?.HasError == true;
        internal bool IsCompiling => Status?.IsPending == true;
        internal ShaderGraphMessageLevel DiagnosticSeverity => Status?.Severity ?? ShaderGraphMessageLevel.Info;

        internal void InvalidateGraph()
        {
            if (IsEffect) Effect?.InvalidateGraph();
            else Behaviour?.InvalidateGraph();
        }
        internal bool CompileGraph() => IsEffect ? Effect != null && Effect.CompileGraph() : Behaviour?.CompileGraph(Document) == true;
        internal bool EnsureGraphCompiled()
        {
            if (!GraphActive) return false;
            if (IsEffect) Effect.PrepareRuntime(); else Behaviour.PrepareRuntime(Document);
            return !Status.HasError;
        }

        internal void RecordUndo(string name)
        {
            if (Document != null) Undo.RecordObject(Document, name);
            if (Effect != null) Undo.RecordObject(Effect, name);
        }
        internal void MarkChanged(bool layoutOnly = false)
        {
            if (Effect != null)
            {
                EditorUtility.SetDirty(Effect);
                if (!layoutOnly) Effect.NotifyValuesChanged();
            }
            if (Document == null) return;
            if (!layoutOnly) Document.MarkChanged();
            else
            {
                EditorUtility.SetDirty(Document);
                if (Document.documentBinding != null) Document.documentBinding.dirty = true;
            }
        }
    }
}
