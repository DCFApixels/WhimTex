using System;
using System.Collections.Generic;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX
    {
        internal ShaderProgramMode Mode { get => Program.mode; set => SetMode(value); }
        internal ShaderGraph Graph => Program.draftGraph;
        internal IReadOnlyList<ShaderParameter> BoundGraphParameters => runtime?.BoundGraphParameters ?? Array.Empty<ShaderParameter>();
        private ShaderCompilationState Status(ShaderProgramMode mode) => mode == ShaderProgramMode.Code
            ? codeCompilationStatus ??= new ShaderCompilationState() : graphCompilationStatus ??= new ShaderCompilationState();
        internal ShaderCompilationState CompilationStatus
        {
            get
            {
                var state = Status(Mode);
                if (Mode == ShaderProgramMode.Graph) state.ObserveGraph(ShaderGraphRevision.Fingerprint(Graph));
                return state;
            }
        }
        internal string GeneratedSource
        {
            get
            {
                if (HasWorkingProgram && runtime.Fingerprint == ShaderGraphRevision.Fingerprint(Graph)) return runtime.Source;
                try { return ShaderGraphCompiler.Compile(Graph, effectContext: true).source; }
                catch (Exception) { return string.Empty; }
            }
        }
        internal void ReplaceProgram(ShaderProgram value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ReleaseRuntime();
            program = value;
            codeCompilationStatus = graphCompilationStatus = null;
            InvalidateGraph();
        }
        internal void SetMode(ShaderProgramMode value)
        {
            if (value == Mode || WhimTexApi.IsShaderFXContentLocked(this)) return;
            Program.mode = value;
            ReleaseRuntime(); InvalidateGraph();
            try { RestoreWorkingProgram(); }
            catch (Exception) { }
            EditorUtility.SetDirty(this);
            NotifyValuesChanged();
        }
        internal void InvalidateGraph() { ShaderGraphRevision.Invalidate(Graph); CompilationStatus.Invalidate(); }
        internal bool TryRetainUnchangedGraph()
        {
            if (Mode != ShaderProgramMode.Graph || CompilationStatus.IsPending || !CompilationStatus.CanRetainUnchangedGraph ||
                !HasWorkingProgram || !runtime.TryRetainUnchangedGraph(Graph, true)) return false;
            Program.Accept(runtime, Mode); CompilationStatus.Succeed(ShaderGraphRevision.Fingerprint(Graph), runtime);
            return true;
        }
        internal bool CompileGraph() => Mode == ShaderProgramMode.Graph && Compile();
        internal bool PrepareWorkingProgram()
        {
            if (Mode == ShaderProgramMode.Graph && (CompilationStatus.IsPending || CompilationStatus.NeedsCompile(ShaderGraphRevision.Fingerprint(Graph), HasWorkingProgram)))
            { try { CompileDraftProgram(); } catch (Exception) { } }
            if (!HasWorkingProgram && Program.HasWorkingProgram(Mode))
            {
                try { RestoreWorkingProgram(); }
                catch (Exception) { return false; }
            }
            return HasWorkingProgram && Program.HasWorkingProgram(Mode);
        }
        // Explicit boundary before rendering/dependency analysis; status getters never compile.
        internal void PrepareRuntime()
        {
            if (compilingProgram || WhimTexApi.IsShaderFXContentLocked(this)) return;
            ConsumeUndoChanges();
            if (Mode == ShaderProgramMode.Graph)
            {
                ulong fingerprint = ShaderGraphRevision.Fingerprint(Graph);
                if (!CompilationStatus.NeedsCompile(fingerprint, HasWorkingProgram)) return;
                try
                {
                    var version = !HasWorkingProgram && Program.HasWorkingProgram(Mode) ? ShaderProgramVersion.Working : ShaderProgramVersion.Draft;
                    if (ShaderProgramUpdate.Synchronous)
                    { if (version == ShaderProgramVersion.Working) RestoreWorkingProgram(); else CompileDraftProgram(); }
                    else QueueProgram(version, fingerprint);
                }
                catch (Exception) { }
            }
            else if (!HasWorkingProgram && Program.HasWorkingProgram(Mode) && CompilationStatus.NeedsCompile(0, false))
            {
                try
                {
                    if (ShaderProgramUpdate.Synchronous) { CompilationStatus.RecordAttempt(0); RestoreWorkingProgram(); }
                    else QueueProgram(ShaderProgramVersion.Working, 0);
                }
                catch (Exception) { }
            }
        }
        void QueueProgram(ShaderProgramVersion version, ulong stamp)
        {
            var selected = Mode; var state = Status(selected); var sourceProgram = Program;
            if (state.pending?.Matches(stamp) == true) return;
            state.pending?.Dispose();
            bool working = version == ShaderProgramVersion.Working;
            string path = SourcePath;
            string code = selected == ShaderProgramMode.Code ? sourceProgram.CodeSource(working) : null;
            state.pending = new ShaderProgramUpdate(stamp,
                () => ShaderProgramRuntime.BeginProgram(sourceProgram, selected, path, true, version, runtimeMode == selected ? runtime : null).WithLighting(embeddedOwner),
                () => this != null && ReferenceEquals(Program, sourceProgram) && Mode == selected &&
                    !WhimTexApi.IsShaderFXContentLocked(this) && (selected == ShaderProgramMode.Graph
                        ? ShaderGraphRevision.Fingerprint(Graph) == stamp : sourceProgram.CodeSource(working) == code),
                compiled =>
                {
                    AcceptProgram(compiled, working, refreshValues: true);
                    QueueNotification(false);
                },
                error =>
                {
                    state.RecordAttempt(stamp); RecordCompileFailure(error, draft: !working);
                    if (!HasWorkingProgram && sourceProgram.HasWorkingProgram(selected) && !working) QueueProgram(ShaderProgramVersion.Working, stamp);
                    QueueNotification(false);
                });
        }
    }
}
