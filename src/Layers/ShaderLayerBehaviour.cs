using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderLayerMode { Graph, Code }
    public enum ShaderLayerInputMode { None, Texture, Layer, CompositeBelow }

    [Serializable]
    public sealed class ShaderLayerBehaviour : LayerBehaviour, ISerializationCallbackReceiver
    {
        public const string DefaultCode = "// @whimtex-effect Shader\n\nfloat4 ApplyFX(PinnedUV uv, float4 color)\n{\n    return float4(1, 1, 1, 1);\n}\n";
        public const string PassthroughCode = "float4 ApplyFX(float4 color)\n{\n    return color;\n}\n";
        public static ShaderLayerBehaviour CreateCompositeFilter()
        {
            var shader = new ShaderLayerBehaviour
            {
                mode = ShaderLayerMode.Code, inputMode = ShaderLayerInputMode.CompositeBelow,
                code = PassthroughCode, compiledCode = PassthroughCode
            };
            shader.blendMode = BlendMode.Crossfade;
            shader.transform.tiling = TransformTilingMode.Clip;
            return shader;
        }
        internal bool IsPassthrough => mode == ShaderLayerMode.Code && compiledCode == PassthroughCode;
        public ShaderProgram program = new ShaderProgram();
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("mode")] private ShaderLayerMode reloadShaderMode;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("graph")] private ShaderGraph reloadShaderGraph;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("code")] private string reloadShaderCode;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("compiledCode")] private string reloadShaderCompiledCode;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("codeParameters")] private List<ShaderFXParameter> reloadShaderParameters;
        [SerializeField, HideInInspector] private bool hasSerializedProgram;
        void ISerializationCallbackReceiver.OnBeforeSerialize() => hasSerializedProgram = true;
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (!hasSerializedProgram && (reloadShaderGraph?.nodes?.Count > 0 || !string.IsNullOrEmpty(reloadShaderCode)))
                program = new ShaderProgram { mode = reloadShaderMode, graph = reloadShaderGraph ?? ShaderGraph.CreateDefault(),
                    code = reloadShaderCode ?? DefaultCode, compiledCode = reloadShaderCompiledCode ?? DefaultCode,
                    codeParameters = reloadShaderParameters ?? new List<ShaderFXParameter>() };
            hasSerializedProgram = true;
            reloadShaderGraph = null; reloadShaderCode = reloadShaderCompiledCode = null; reloadShaderParameters = null;
        }
        public ShaderLayerMode mode { get => program.mode; set => program.mode = value; }
        public ShaderGraph graph { get => program.graph; set => program.graph = value; }
        public string code { get => program.code; set => program.code = value; }
        public string compiledCode { get => program.compiledCode; set => program.compiledCode = value; }
        public List<ShaderFXParameter> codeParameters { get => program.codeParameters; set => program.codeParameters = value; }
        public ShaderLayerInputMode inputMode;
        public Texture2D inputTexture;
        public string inputLayerId;
        sealed class CompilationState
        {
            internal ShaderGraphProgram program;
            internal ulong fingerprint;
            internal bool attempted, failed, draftFailed;
            internal string diagnostics, errorNodeId;
            internal IReadOnlyList<ShaderGraphMessage> messages = Array.Empty<ShaderGraphMessage>();
        }
        [NonSerialized] CompilationState graphState, codeState;
        [NonSerialized] RenderFingerprint.TextStamp codeFingerprint;
        CompilationState State(ShaderLayerMode selected) => selected == ShaderLayerMode.Code
            ? codeState ??= new CompilationState() : graphState ??= new CompilationState();
        CompilationState CurrentState => State(mode);
        ShaderGraphProgram Program => CurrentState.program;
        internal IReadOnlyList<ShaderFXParameter> ActiveParameters => mode == ShaderLayerMode.Code ? codeParameters : graph?.parameters;
        internal string Diagnostics => CurrentState.diagnostics ?? "Not compiled yet.";
        internal string GeneratedSource => graphState?.program?.Source ?? "";
        public override bool UsesTime => Program?.UsesTime == true;
        public override bool UsesInstanceIndex => Program?.UsesInstanceIndex == true;
        internal bool HasCompileError => CurrentState.failed;
        internal string ErrorNodeId => CurrentState.errorNodeId;
        internal IReadOnlyList<ShaderGraphMessage> Messages => CurrentState.messages;
        internal string CodeDiagnostics => codeState?.diagnostics ?? "Not compiled yet.";
        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;
        public override string ToString() => "Shader";
        internal override void InitializeLayer(Layer layer)
        {
            layer.colorRange = LayerColorRange.HDR;
            layer.blendRange = LayerBlendRange.HDR;
        }
        internal void InvalidateGraph() => CurrentState.attempted = false;
        internal string CodeSourcePath(WhimTexDocument document)
        {
            string presetPath = ShaderPresetLink.LayerSourcePath(this);
            if (!string.IsNullOrEmpty(presetPath)) return presetPath;
            string path = WhimTexDocumentService.PathOf(document);
            return string.IsNullOrEmpty(path) ? "Assets/Untitled.hlsl" : path;
        }
        ulong Fingerprint(ShaderLayerMode selected)
        {
            if (selected == ShaderLayerMode.Graph) return ShaderGraphCompiler.CodeFingerprint(graph);
            ulong hash = codeFingerprint.Get(string.IsNullOrEmpty(compiledCode) ? code : compiledCode);
            foreach (var parameter in codeParameters)
                if (parameter != null) hash = RenderFingerprint.Mix(hash, parameter.idFingerprint.Get(parameter.id));
            return hash;
        }
        internal bool CompileGraph(WhimTexDocument document) => Compile(document, false);
        internal bool CompileCode(WhimTexDocument document) => Compile(document, true);
        bool Compile(WhimTexDocument document, bool applyCode)
        {
            var selected = applyCode ? ShaderLayerMode.Code : mode;
            var state = State(selected);
            // A failed draft must not suppress restoration of the saved working code.
            if (!applyCode) { state.fingerprint = Fingerprint(selected); state.attempted = true; }
            try
            {
                ShaderGraphProgram compiled;
                if (selected == ShaderLayerMode.Code)
                {
                    bool restoreInitialCode = string.IsNullOrEmpty(compiledCode);
                    string source = applyCode || restoreInitialCode ? code : compiledCode;
                    var parameters = ShaderFXMetadata.Parse(source, false, out _);
                    ShaderFXMetadata.PreserveValues(parameters, codeParameters);
                    foreach (var parameter in parameters)
                        if (parameter.type == ShaderFXParameterType.Texture2D && parameter.textureSource == ShaderFXTextureSource.Self)
                            throw new InvalidOperationException("Self is not available for Shader layers. Choose Texture, Layer or None.");
                    compiled = ShaderGraphProgram.CreateCode(source, parameters, CodeSourcePath(document));
                    if (applyCode || restoreInitialCode)
                    {
                        WhimTexDocumentJson.PreserveShaderCodeParameterAssets(codeParameters, parameters);
                        compiledCode = source; codeParameters = parameters;
                    }
                }
                else compiled = ShaderGraphProgram.Create(graph);
                state.program?.Dispose(); state.program = compiled;
                state.fingerprint = Fingerprint(selected); state.attempted = true;
                if (applyCode) state.draftFailed = false;
                if (!state.draftFailed)
                {
                    state.failed = false; state.errorNodeId = null;
                    state.messages = compiled.Messages;
                    state.diagnostics = "Compiled successfully.";
                }
                return true;
            }
            catch (Exception error)
            {
                state.failed = true; state.draftFailed = applyCode;
                state.errorNodeId = (error as ShaderGraphException)?.nodeId;
                state.messages = (error as ShaderGraphException)?.messages ?? new[] { new ShaderGraphMessage(null, error.Message, ShaderGraphMessageLevel.Error) };
                state.diagnostics = error.Message + (state.program == null ? "" : "\nShowing the last successfully compiled shader in this mode.");
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
            if (!state.attempted || state.fingerprint != fingerprint) Compile(context.activeDocument, false);
            var program = Program;
            var source = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear; source.wrapMode = TextureWrapMode.Clamp;
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                if (program == null)
                {
                    RenderTexture.active = source; GL.Clear(false, true, Color.clear);
                    return ApplyTransformAndFx(source, context);
                }
                if (mode == ShaderLayerMode.Code) program.BindCode(codeParameters, context);
                else program.Bind(graph, context);
                var renderContext = program.PrepareCoordinates(Owner, context);
                using var input = context.activeDocument.ResolveShaderInput(this, context);
                var inputTexture = input.Bind(program.Material, program.EmptyInput);
                using var textures = context.activeDocument.BindShaderTextureLayers(program.Parameters, Owner, context, null);
                textures?.Apply(program.Material);
                GL.sRGBWrite = false;
                Graphics.Blit(inputTexture, source, program.Material, 0);
                return ApplyTransformAndFx(source, renderContext);
            }
            finally
            {
                program?.Material.SetTexture("_MainTex", null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }
        internal override void ReleaseTransientResources()
        {
            ShaderPresetLink.ReleaseLayer(this);
            graphState?.program?.Dispose(); graphState = null;
            codeState?.program?.Dispose(); codeState = null;
        }
    }

}
