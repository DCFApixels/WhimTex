using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderLayerInputMode { None, Texture, Layer, CompositeBelow }

    [Serializable]
    public sealed class ShaderLayerBehaviour : LayerBehaviour
    {
        public const string DefaultCode = "// @whimtex-effect Shader\n\nfloat4 ApplyFX(PinnedUV uv, float4 color)\n{\n    return float4(1, 1, 1, 1);\n}\n";
        public const string PassthroughCode = "float4 ApplyFX(float4 color)\n{\n    return color;\n}\n";
        public static ShaderLayerBehaviour CreateCompositeFilter()
        {
            var shader = new ShaderLayerBehaviour
            {
                mode = ShaderProgramMode.Code, inputMode = ShaderLayerInputMode.CompositeBelow,
                draftCode = PassthroughCode, workingCode = PassthroughCode
            };
            shader.blendMode = BlendMode.Crossfade;
            shader.transform.tiling = TransformTilingMode.Clip;
            return shader;
        }
        internal bool IsPassthrough => mode == ShaderProgramMode.Code && workingCode == PassthroughCode;
        public ShaderProgram program = new ShaderProgram();
        public ShaderProgramMode mode { get => program.mode; set => program.mode = value; }
        public ShaderGraph draftGraph { get => program.draftGraph; set => program.draftGraph = value; }
        public string draftCode { get => program.draftCode; set => program.draftCode = value; }
        public string workingCode { get => program.workingCode; set => program.workingCode = value; }
        public List<ShaderParameter> draftCodeParameters { get => program.draftCodeParameters; set => program.draftCodeParameters = value; }
        public ShaderLayerInputMode inputMode;
        public Texture2D inputTexture;
        public string inputLayerId;
        sealed class CompilationState : ShaderCompilationState
        {
            internal ShaderProgramRuntime runtime;
        }
        [NonSerialized] CompilationState graphState, codeState;
        [NonSerialized] RenderFingerprint.TextStamp codeFingerprint;
        CompilationState State(ShaderProgramMode selected) => selected == ShaderProgramMode.Code
            ? codeState ??= new CompilationState() : graphState ??= new CompilationState();
        CompilationState CurrentState => State(mode);
        internal ShaderCompilationState CompilationStatus => CurrentState;
        ShaderProgramRuntime Runtime => CurrentState.runtime;
        internal IReadOnlyList<ShaderParameter> ActiveParameters => mode == ShaderProgramMode.Code ? draftCodeParameters : draftGraph?.parameters;
        internal string Diagnostics => CurrentState.Diagnostics ?? "Not compiled yet.";
        internal string GeneratedSource => graphState?.runtime?.Source ?? "";
        public override bool UsesTime => Runtime?.UsesTime == true;
        public override bool UsesInstanceIndex => Runtime?.UsesInstanceIndex == true;
        internal bool HasCompileError => CurrentState.HasError;
        internal string CodeDiagnostics => codeState?.Diagnostics ?? "Not compiled yet.";
        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;
        public override string ToString() => "Shader";
        internal override void InitializeLayer(Layer layer)
        {
            layer.colorRange = LayerColorRange.HDR;
            layer.blendRange = LayerBlendRange.HDR;
        }
        internal void InvalidateGraph() => CurrentState.Invalidate();
        internal string CodeSourcePath(WhimTexDocument document)
        {
            string presetPath = ShaderPresetLink.LayerSourcePath(this);
            if (!string.IsNullOrEmpty(presetPath)) return presetPath;
            string path = WhimTexDocumentService.PathOf(document);
            return string.IsNullOrEmpty(path) ? "Assets/Untitled.hlsl" : path;
        }
        ulong Fingerprint(ShaderProgramMode selected)
        {
            if (selected == ShaderProgramMode.Graph) return ShaderGraphCompiler.CodeFingerprint(draftGraph);
            ulong hash = codeFingerprint.Get(string.IsNullOrEmpty(workingCode) ? draftCode : workingCode);
            foreach (var parameter in draftCodeParameters)
                if (parameter != null) hash = RenderFingerprint.Mix(hash, parameter.idFingerprint.Get(parameter.id));
            return hash;
        }
        internal bool CompileGraph(WhimTexDocument document) => Compile(document, false);
        internal bool CompileCode(WhimTexDocument document) => Compile(document, true);
        internal bool PrepareWorkingProgram(WhimTexDocument document)
        {
            if (CurrentState.runtime == null)
                Compile(document, false, restoreGraph: program.workingGraph != null);
            return CurrentState.runtime != null && program.HasWorkingProgram(mode);
        }
        bool Compile(WhimTexDocument document, bool compileDraftCode, bool restoreGraph = false)
        {
            var selected = compileDraftCode ? ShaderProgramMode.Code : mode;
            var state = State(selected);
            string sourcePath = null;
            // A failed draft must not suppress restoration of the working code.
            if (!compileDraftCode) state.RecordAttempt(Fingerprint(selected));
            try
            {
                bool acceptCode = selected == ShaderProgramMode.Code && (compileDraftCode || string.IsNullOrEmpty(workingCode));
                sourcePath = selected == ShaderProgramMode.Code ? CodeSourcePath(document) : "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl";
                var compiled = ShaderProgramRuntime.CreateProgram(program, selected, sourcePath,
                    effectContext: false, useWorkingCode: !compileDraftCode, useWorkingGraph: restoreGraph);
                if (acceptCode)
                {
                    WhimTexDocumentJson.PreserveShaderCodeParameterAssets(draftCodeParameters, compiled.CodeParameters);
                    workingCode = compiled.Source; draftCodeParameters = compiled.CodeParameters;
                }
                program.Accept(compiled, selected);
                state.runtime?.Dispose(); state.runtime = compiled;
                state.Succeed(Fingerprint(selected), compiled, preserveDraftError: selected == ShaderProgramMode.Code ? !compileDraftCode : restoreGraph);
                if (!state.HasError) ShaderDiagnostics.Report(compiled.DiagnosticMessages, sourcePath, document);
                return true;
            }
            catch (Exception error)
            {
                state.Fail(error, draft: compileDraftCode || selected == ShaderProgramMode.Graph && !restoreGraph,
                    diagnostics: error.Message + (state.runtime == null ? "" : "\nShowing the last successfully compiled shader in this mode."));
                ShaderDiagnostics.Report(ShaderDiagnostics.Failure(error, sourcePath), sourcePath, document);
                if (state.runtime == null && program.HasWorkingProgram(selected) && (compileDraftCode || selected == ShaderProgramMode.Graph && !restoreGraph))
                    Compile(document, false, restoreGraph: selected == ShaderProgramMode.Graph);
                return false;
            }
        }
        internal override RenderTexture Render(in LayerRenderContext context)
        {
            ShaderPresetLink.EnsureLayer(this, context.activeDocument);
            if (IsPassthrough && inputMode == ShaderLayerInputMode.CompositeBelow)
            {
                using var input = context.activeDocument.ResolveShaderInput(this, context);
                return ApplyTransformAndFx(input.Texture, context);
            }
            var state = CurrentState;
            ulong fingerprint = Fingerprint(mode);
            if (state.NeedsCompile(fingerprint, state.runtime != null))
                Compile(context.activeDocument, false, restoreGraph: state.runtime == null && this.program.workingGraph != null);
            var runtime = Runtime;
            var source = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear; source.wrapMode = TextureWrapMode.Clamp;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                if (runtime == null)
                {
                    RenderTexture.active = source; GL.Clear(false, true, Color.clear);
                    return ApplyTransformAndFx(source, context);
                }
                if (mode == ShaderProgramMode.Code) runtime.BindCode(draftCodeParameters, context);
                else runtime.Bind(draftGraph, context);
                var renderContext = runtime.PrepareCoordinates(Owner, context);
                using var input = context.activeDocument.ResolveShaderInput(this, context);
                var inputTexture = input.Bind(runtime.Material, runtime.EmptyInput);
                using var textures = context.activeDocument.BindShaderTextureLayers(runtime.Parameters, Owner, context, null);
                textures?.Apply(runtime.Material);
                GL.sRGBWrite = false;
                Graphics.Blit(inputTexture, source, runtime.Material, 0);
                return ApplyTransformAndFx(source, renderContext);
            }
            finally
            {
                runtime?.Material.SetTexture("_MainTex", null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }
        internal override void ReleaseTransientResources()
        {
            ShaderPresetLink.ReleaseLayer(this);
            graphState?.runtime?.Dispose(); graphState = null;
            codeState?.runtime?.Dispose(); codeState = null;
        }
    }

}
