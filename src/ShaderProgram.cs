using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public enum ShaderProgramMode { Graph, Code }

    internal enum ShaderProgramVersion { Draft, Working }

    // Native Undo retains diagnostics; executable resources remain transient.
    [Serializable]
    internal class ShaderCompilationState : UnityEngine.ISerializationCallbackReceiver
    {
        [NonSerialized] ulong attemptedFingerprint;
        [NonSerialized] bool attempted;
        [NonSerialized] int attemptedRevision;
        [NonSerialized] internal ShaderProgramUpdate pending;
        internal bool IsPending => pending?.IsPending == true;
        [UnityEngine.SerializeField] bool draftFailed;
        [UnityEngine.SerializeField] bool hasError;
        [UnityEngine.SerializeField] ShaderDiagnostic[] diagnosticMessages;
        [NonSerialized] string diagnostics;
        [NonSerialized] IReadOnlyList<ShaderDiagnostic> diagnosticView;
        internal bool HasError => hasError;
        internal IReadOnlyList<ShaderDiagnostic> DiagnosticMessages => diagnosticView ??= Array.AsReadOnly(diagnosticMessages ?? Array.Empty<ShaderDiagnostic>());
        internal string Diagnostics => diagnostics ??= diagnosticMessages == null ? null : diagnosticMessages.Length == 0
            ? "Compiled successfully." : ShaderDiagnostics.Format(diagnosticMessages);
        internal string ErrorNodeId { get; private set; }
        internal IReadOnlyList<ShaderGraphMessage> Messages { get; private set; } = Array.Empty<ShaderGraphMessage>();
        internal ShaderGraphMessageLevel Severity
        {
            get
            {
                var level = HasError ? ShaderGraphMessageLevel.Error : ShaderGraphMessageLevel.Info;
                foreach (var message in Messages) if (message.level > level) level = message.level;
                return level;
            }
        }

        void UnityEngine.ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void UnityEngine.ISerializationCallbackReceiver.OnAfterDeserialize()
        { diagnostics = null; diagnosticView = null; attempted = false; }

        internal bool NeedsCompile(ulong fingerprint, bool hasProgram) =>
            !attempted || attemptedRevision != ShaderProgramSourceRevision.Current || attemptedFingerprint != fingerprint || !hasProgram && !HasError;
        internal void Invalidate() => attempted = false;
        internal void RecordAttempt(ulong fingerprint) { attemptedFingerprint = fingerprint; attemptedRevision = ShaderProgramSourceRevision.Current; attempted = true; }
        internal void SetDiagnostics(IReadOnlyList<ShaderDiagnostic> messages, bool failed)
        {
            hasError = failed; diagnostics = null; diagnosticView = null;
            diagnosticMessages = messages.Count == 0 ? Array.Empty<ShaderDiagnostic>() : new ShaderDiagnostic[messages.Count];
            for (int i = 0; i < messages.Count; i++) diagnosticMessages[i] = messages[i];
        }
        internal void Succeed(ulong fingerprint, ShaderProgramRuntime program, bool preserveDraftError = false)
        {
            RecordAttempt(fingerprint);
            if (preserveDraftError && draftFailed) return;
            draftFailed = false; ErrorNodeId = null;
            Messages = program.Messages;
            SetDiagnostics(program.DiagnosticMessages, false);
        }
        internal void Fail(Exception error, bool draft = false, string diagnostics = null, string sourcePath = null)
        {
            draftFailed = draft;
            var graphError = error as ShaderGraphException;
            ErrorNodeId = graphError?.nodeId;
            Messages = graphError?.messages ?? new[] { new ShaderGraphMessage(null, error.Message, ShaderGraphMessageLevel.Error) };
            SetDiagnostics(ShaderDiagnostics.Failure(error, sourcePath), true);
            this.diagnostics = diagnostics;
        }
    }

    /// <summary>Editable shader source shared by layer generators, FX and presets.</summary>
    [Serializable]
    public sealed class ShaderProgram
    {
        public ShaderProgramMode mode;
        public ShaderGraph draftGraph = ShaderGraph.CreateDefault();
        [UnityEngine.SerializeReference] public ShaderGraph workingGraph;
        public string draftCode = ShaderLayerBehaviour.DefaultCode;
        public string workingCode = ShaderLayerBehaviour.DefaultCode;
        public List<ShaderParameter> draftCodeParameters = new List<ShaderParameter>();
        public List<ShaderParameter> workingCodeParameters = new List<ShaderParameter>();
        public string presetId;
        public int presetVersion;

        internal IReadOnlyList<ShaderParameter> ActiveParameters => mode == ShaderProgramMode.Graph ? draftGraph?.parameters : draftCodeParameters;

        internal string CodeSource(bool useWorkingCode) => useWorkingCode && !string.IsNullOrEmpty(workingCode) ? workingCode : draftCode;
        internal bool HasWorkingProgram(ShaderProgramMode selected) => selected == ShaderProgramMode.Graph
            ? workingGraph != null : !string.IsNullOrEmpty(workingCode);
        internal IReadOnlyList<ShaderParameter> WorkingCodeParameters => workingCodeParameters?.Count > 0
            ? workingCodeParameters : draftCodeParameters;
        internal void Accept(ShaderProgramRuntime compiled, ShaderProgramMode selected)
        {
            if (selected == ShaderProgramMode.Graph) workingGraph = compiled.WorkingGraph;
            else
            {
                workingCode = compiled.Source;
                workingCodeParameters = new List<ShaderParameter>(compiled.Parameters);
            }
        }

        // Prepare a candidate without replacing the editable parameters before compilation succeeds.
        internal List<ShaderParameter> PrepareCodeParameters(string source, bool effectContext, IReadOnlyList<ShaderParameter> values = null)
        {
            var next = ShaderMetadata.Parse(source, false, out _);
            ShaderMetadata.PreserveValues(next, values ?? draftCodeParameters);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in next)
            {
                if (parameter == null) continue;
                if (!effectContext && parameter.type == ShaderParameterType.Texture2D && parameter.textureSource == ShaderTextureSource.Self)
                    throw new InvalidOperationException("Self is not available for Shader layers. Choose Texture, Layer or None.");
                if (string.IsNullOrEmpty(parameter.id) || (effectContext && !Guid.TryParseExact(parameter.id, "N", out _)) || !ids.Add(parameter.id))
                {
                    parameter.id = Guid.NewGuid().ToString("N");
                    ids.Add(parameter.id);
                }
            }
            return next;
        }

        internal static ShaderProgram CreateEffect()
        {
            var result = new ShaderProgram { mode = ShaderProgramMode.Code,
                draftCode = "// @param color(float4) _Tint = (1, 1, 1, 1)\n\nfloat4 ApplyFX(float4 color)\n{\n    return color * _Tint;\n}\n",
                workingCode = null };
            result.draftGraph.parameters.Clear();
            result.draftGraph.nodes.Clear(); result.draftGraph.edges.Clear();
            var input = ShaderGraphNodes.Create(ShaderGraphNodeKind.SampleInput, new UnityEngine.Vector2(40, 80));
            var output = ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, new UnityEngine.Vector2(360, 80));
            result.draftGraph.nodes.Add(input); result.draftGraph.nodes.Add(output);
            result.draftGraph.edges.Add(new ShaderGraphEdge { outputNode = input.id, inputNode = output.id, inputPort = "RGBA" });
            return result;
        }
    }
}
