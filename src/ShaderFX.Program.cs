using System;
using System.Collections.Generic;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        internal ShaderProgramMode Mode { get => Program.mode; set => SetMode(value); }
        internal ShaderGraph Graph => Program.draftGraph;
        internal IReadOnlyList<ShaderParameter> BoundGraphParameters => graphRuntime?.Parameters ?? Array.Empty<ShaderParameter>();
        private ShaderCompilationState Status(ShaderProgramMode mode) => mode == ShaderProgramMode.Code
            ? codeCompilationStatus ??= new ShaderCompilationState() : graphCompilationStatus ??= new ShaderCompilationState();
        internal ShaderCompilationState CompilationStatus
        {
            get
            {
                var state = Status(Mode);
                // Native serialization/Undo own these two fields; transient bookkeeping follows them.
                state.SetDiagnostics(diagnostics, lastCompileFailed);
                return state;
            }
        }
        internal string GeneratedSource
        {
            get
            {
                if (graphCompilation != null && compiledGraphFingerprint == ShaderGraphCompiler.CodeFingerprint(Graph)) return graphCompilation.source;
                try { return ShaderGraphCompiler.Compile(Graph, effectContext: true).source; }
                catch (Exception) { return string.Empty; }
            }
        }
        internal void ReplaceProgram(ShaderProgram value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ReleaseMaterial();
            program = value;
            codeCompilationStatus = graphCompilationStatus = null;
            InvalidateGraph();
            graphCompilation = null;
            lastCompileFailed = false;
        }
        internal void SetMode(ShaderProgramMode value)
        {
            if (value == Mode || WhimTexApi.IsShaderFXContentLocked(this)) return;
            _ = CompilationStatus;
            Program.mode = value;
            var state = Status(value);
            diagnostics = state.Diagnostics ?? "Not compiled yet. Click Compile to use this effect.";
            lastCompileFailed = state.HasError;
            ReleaseMaterial(); InvalidateGraph();
            CompileProgram(false, useWorkingCode: true);
            EditorUtility.SetDirty(this);
            NotifyValuesChanged();
        }
        internal void InvalidateGraph() => Status(Mode).Invalidate();
        internal bool CompileGraph() => Mode == ShaderProgramMode.Graph && CompileProgram(true);
        internal bool PrepareWorkingProgram()
        {
            if (!HasWorkingProgram)
            {
                if (!Program.HasWorkingProgram(Mode)) return false;
                CompileProgram(false, useWorkingCode: true);
            }
            return HasWorkingProgram && Program.HasWorkingProgram(Mode);
        }
        private void EnsureGraphCompiled()
        {
            if (Mode != ShaderProgramMode.Graph || compilingProgram) return;
            ulong fingerprint = ShaderGraphCompiler.CodeFingerprint(Graph);
            if (!CompilationStatus.NeedsCompile(fingerprint, HasWorkingProgram)) return;
            CompileProgram(false, useWorkingCode: !HasWorkingProgram && Program.HasWorkingProgram(Mode));
        }
    }
}
