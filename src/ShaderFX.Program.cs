using System;
using System.Collections.Generic;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        internal ShaderLayerMode Mode { get => Program.mode; set => SetMode(value); }
        internal ShaderGraph Graph => Program.graph;
        internal IReadOnlyList<ShaderFXParameter> BoundGraphParameters => graphRuntime?.Parameters ?? Array.Empty<ShaderFXParameter>();
        internal string GraphDiagnostics => diagnostics;
        internal IReadOnlyList<ShaderGraphMessage> GraphMessages => graphMessages;
        internal string GraphErrorNodeId => graphErrorNodeId;
        internal string GeneratedSource
        {
            get
            {
                if (graphCompilation != null && compiledGraphFingerprint == ShaderGraphCompiler.CodeFingerprint(Graph)) return graphCompilation.source;
                try { return ShaderGraphCompiler.Compile(Graph, effectContext: true).source; }
                catch (Exception) { return string.Empty; }
            }
        }
        private void ThrowProgramErrors(ShaderGraphProgram candidate, IReadOnlyList<ShaderFXDiagnostic> messages)
        {
            if (!ShaderFXDiagnostics.HasErrors(messages)) return;
            string text = ShaderFXDiagnostics.Format(messages);
            if (Mode != ShaderLayerMode.Graph) throw new InvalidOperationException(text);
            string node = null;
            foreach (var message in candidate.Messages)
                if (message.level == ShaderGraphMessageLevel.Error) { node = message.nodeId; break; }
            throw new ShaderGraphException(node, text, candidate.Messages);
        }
        internal void ReplaceProgram(ShaderProgram value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ReleaseMaterial();
            program = value;
            InvalidateGraph();
            graphCompilation = null;
            lastApplyFailed = false;
        }
        internal void SetMode(ShaderLayerMode value)
        {
            if (value == Mode || WhimTexApi.IsShaderFXContentLocked(this)) return;
            Program.mode = value;
            ReleaseMaterial(); InvalidateGraph();
            string draft = code;
            if (value == ShaderLayerMode.Code && !string.IsNullOrEmpty(Program.compiledCode)) code = Program.compiledCode;
            try { ApplyCore(false); }
            finally { code = draft; }
            EditorUtility.SetDirty(this);
            NotifyValuesChanged();
        }
        internal void InvalidateGraph() { graphAttempted = false; graphCompilation = null; }
        internal bool CompileGraph()
        {
            if (Mode != ShaderLayerMode.Graph) return false;
            return CompileGraphCore(true);
        }
        private void EnsureGraphCompiled()
        {
            if (Mode != ShaderLayerMode.Graph || compilingProgram) return;
            ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(Graph);
            if (graphAttempted && attemptedGraphFingerprint == fingerprint && (HasAppliedShader || lastApplyFailed)) return;
            CompileGraphCore(false);
        }
        private bool CompileGraphCore(bool changed)
        {
            compilingProgram = true;
            graphAttempted = true;
            attemptedGraphFingerprint = ShaderGraphCompiler.CodeFingerprint(Graph);
            try { return ApplyCore(changed); }
            finally { compilingProgram = false; }
        }
    }
}
