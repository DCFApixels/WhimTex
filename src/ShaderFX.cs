using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderFX : ScriptableObject, ISerializationCallbackReceiver
    {
        internal static event Action<ShaderFX> Changed;
        internal const string CodeIndent = "    ";
        internal const string HlslOnlyMessage = "External FX references are no longer supported. Use a marked .hlsl preset or inline HLSL code.";
        [SerializeField, HideInInspector] private bool active = true;
        public bool Active
        {
            get => active;
            set
            {
                if (active == value) return;
                active = value;
                EditorUtility.SetDirty(this);
                NotifyValuesChanged();
            }
        }

        [SerializeField] private ShaderProgram program = ShaderProgram.CreateEffect();
        internal ShaderProgram Program => program ??= ShaderProgram.CreateEffect();
        private string draftCode { get => Program.draftCode; set => Program.draftCode = value; }
        private List<ShaderParameter> parameters
        {
            get => Mode == ShaderProgramMode.Graph ? Program.draftGraph.parameters : Program.draftCodeParameters;
            set { if (Mode == ShaderProgramMode.Graph) Program.draftGraph.parameters = value; else Program.draftCodeParameters = value; }
        }
        [SerializeField] private ShaderProgramMode workingMode = ShaderProgramMode.Code;
        [NonSerialized] private ShaderGraphCompilation graphCompilation;
        [NonSerialized] private ShaderProgramRuntime graphRuntime;
        [NonSerialized] private ulong compiledGraphFingerprint;
        [NonSerialized] private bool compilingProgram;
        [NonSerialized] private ShaderCompilationState codeCompilationStatus, graphCompilationStatus;
        [SerializeField, HideInInspector] private Shader compiledShader;
        [SerializeField, HideInInspector] private string workingProgramSource;
        [SerializeField, HideInInspector] private string workingShaderSource;
        [SerializeField, HideInInspector] private ShaderLocalityReason[] workingLocalityReasons = Array.Empty<ShaderLocalityReason>();
        [SerializeField, HideInInspector] private List<ShaderParameter> workingParameters = new List<ShaderParameter>();
        [SerializeField, HideInInspector] private string diagnostics = "Not compiled yet. Click Compile to use this effect.";
        [SerializeField, HideInInspector] private bool lastCompileFailed;
        [SerializeField, HideInInspector] private WhimTexDocument embeddedOwner;
        [SerializeField, HideInInspector] private string documentIncludeBasePath;
        [SerializeField, HideInInspector] private string shaderKey = Guid.NewGuid().ToString("N");
        [SerializeField, HideInInspector] private bool shaderCreationRecorded;
        [NonSerialized] private Material material;
        [NonSerialized] private Shader materialSourceShader;
        [NonSerialized] private ShaderParameterBindings parameterBindings;
        [NonSerialized] private string determinismWarningSource;
        [NonSerialized] private bool determinismWarningCached, determinismWarningFound;

        [NonSerialized] private bool notificationQueued;
        [NonSerialized] private bool notificationContainsChanges;
        [NonSerialized] private volatile bool undoDeserialized;

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => undoDeserialized = true;

        internal void ResetUndoTrackingAfterLoad() => undoDeserialized = false;

        internal bool ConsumeUndoChanges()
        {
            if (!undoDeserialized)
                return false;
            undoDeserialized = false;
            EditorApplication.delayCall -= SendNotification;
            notificationQueued = false;
            notificationContainsChanges = false;
            ReleaseMaterial();
            codeCompilationStatus = graphCompilationStatus = null;
            graphCompilation = null;
            InvalidateGraph();
            // Native Undo restores the source fields, but an in-place Shader update is not
            // reverted with them. Graph recompiles lazily; Code must restore its saved shader.
            if (Mode == ShaderProgramMode.Code && HasWorkingShader && !string.IsNullOrEmpty(workingShaderSource))
            {
                try { ShaderUtil.UpdateShaderAsset(compiledShader, workingShaderSource, true); }
                catch (Exception error) { RecordCompileFailure(error); }
            }
            MarkDraftChanged();
            return true;
        }

        internal string Code => Mode == ShaderProgramMode.Graph ? GeneratedSource : draftCode;
        [NonSerialized] private string iconCode;
        [NonSerialized] private ShaderIconMetadata.Descriptor iconMetadata;
        internal ShaderIconMetadata.Descriptor IconMetadata
        {
            get
            {
                if (!ReferenceEquals(iconCode, draftCode)) { iconCode = draftCode; iconMetadata = ShaderIconMetadata.Read(draftCode); }
                return iconMetadata;
            }
        }
        internal IReadOnlyList<ShaderParameter> Parameters => parameters;
        internal string Diagnostics => diagnostics;
        internal bool LastCompileFailed => lastCompileFailed;
        internal bool HasWorkingShader => compiledShader != null && workingMode == Mode;
        private bool HasWorkingProgram => HasWorkingShader && (Mode == ShaderProgramMode.Code || graphCompilation != null);
        internal Shader PreviewCompiledShader => compiledShader;
        internal string PreviewCompiledSource => workingShaderSource;
        internal bool IsUnavailable
        {
            get { EnsureGraphCompiled(); return !HasWorkingProgram; }
        }
        internal string UnavailableReason => IsUnavailable ? $"{name}: FX is skipped until it compiles successfully.\n{diagnostics}" : null;
        internal bool SupportsRegionalColorInput => workingShaderSource != null &&
            workingShaderSource.Contains(ShaderSourceBuilder.RegionalColorMarker);

        /// <summary>Whether the applied, usable shader has a verified pixel-local authoring contract.</summary>
        public bool IsPixelLocal => !IsUnavailable && workingShaderSource != null &&
            workingShaderSource.StartsWith(ShaderSourceBuilder.ViewportWrapperMarker + ShaderEntryPoint.WrapperMarker +
                ShaderEntryPoint.PixelLocalMarker, StringComparison.Ordinal);

        [NonSerialized] private string contextDependencySource;
        [NonSerialized] private ShaderContextDependencies contextDependencies;
        private ShaderContextDependencies ContextDependencies
        {
            get
            {
                if (IsUnavailable) return ShaderContextDependencies.None;
                if (!ReferenceEquals(contextDependencySource, workingShaderSource))
                {
                    contextDependencySource = workingShaderSource;
                    contextDependencies = ShaderContextUsage.ReadMarker(workingShaderSource);
                }
                return contextDependencies;
            }
        }
        /// <summary>Conservative dependencies of the applied, usable shader, including its libraries.</summary>
        public bool UsesTime => (ContextDependencies & ShaderContextDependencies.Time) != 0;
        public bool UsesInstanceIndex => (ContextDependencies & ShaderContextDependencies.InstanceIndex) != 0;

        internal string WorkingLocalityStatus => IsUnavailable || workingShaderSource == null ? null :
            IsPixelLocal ? "pixelLocal" : workingShaderSource.StartsWith(ShaderSourceBuilder.ViewportWrapperMarker +
                ShaderEntryPoint.WrapperMarker + ShaderEntryPoint.UnverifiedMarker, StringComparison.Ordinal) ? "unverified" : "spatial";
        internal IReadOnlyList<ShaderLocalityReason> WorkingLocalityReasons => workingLocalityReasons ?? Array.Empty<ShaderLocalityReason>();

        internal bool TryPrepareDocumentEffect(out string warning)
        {
            try
            {
                ValidatePresetReference();
                if (IsCatalogLinked) ReloadCatalogSource(true);
                else CompileAgentDraft();
                warning = DiagnosticNotice;
                return !IsUnavailable;
            }
            catch (Exception) when (lastCompileFailed) { warning = DiagnosticNotice; return !IsUnavailable; }
        }
        internal bool UsesUnityTimeInputs
        {
            get
            {
                string source = !string.IsNullOrEmpty(workingShaderSource) ? workingShaderSource : draftCode;
                if (!determinismWarningCached || !string.Equals(determinismWarningSource, source, StringComparison.Ordinal))
                {
                    determinismWarningSource = source;
                    determinismWarningFound = ShaderSourceBuilder.GetDeterminismWarning(source) != null;
                    determinismWarningCached = true;
                }
                return determinismWarningFound;
            }
        }
        internal WhimTexDocument EmbeddedOwner => embeddedOwner;
        internal void RestoreDocumentOwner(WhimTexDocument owner) => embeddedOwner = owner;
        internal string ShaderKey => string.IsNullOrEmpty(shaderKey) ? shaderKey = Guid.NewGuid().ToString("N") : shaderKey;
        internal string SourcePath
        {
            get
            {
                if (!string.IsNullOrEmpty(catalogSourcePath)) return catalogSourcePath;
                if (!string.IsNullOrEmpty(documentIncludeBasePath)) return documentIncludeBasePath;
                string documentPath = WhimTexDocumentService.PathOf(embeddedOwner);
                if (!string.IsNullOrEmpty(documentPath)) return documentPath;
                string path = AssetDatabase.GetAssetPath(embeddedOwner != null ? (UnityEngine.Object)embeddedOwner : this);
                if (string.IsNullOrEmpty(path) && embeddedOwner != null && embeddedOwner.OutputTexture != null)
                    path = AssetDatabase.GetAssetPath(embeddedOwner.OutputTexture);
                return !string.IsNullOrEmpty(path) ? path : "Assets/Untitled.hlsl";
            }
        }

        internal static ShaderFX CreateEmbedded(WhimTexDocument owner)
        {
            ShaderFX effect = CreateInstance<ShaderFX>();
            effect.name = "Shader FX";
            effect.embeddedOwner = owner;
            effect.hideFlags = HideFlags.HideAndDontSave;
            Undo.RegisterCreatedObjectUndo(effect, "Add Shader FX");
            return effect;
        }

        internal static ShaderFX CreateAgentDraft(WhimTexDocument owner, string source, List<ShaderParameter> values)
        {
            return CreateAgentDraft(owner, source, values, null);
        }

        internal static ShaderFX CreateAgentDraft(WhimTexDocument owner, string source, List<ShaderParameter> values, string sourcePath)
        {
            var effect = CreateInstance<ShaderFX>();
            effect.name = "Shader FX";
            effect.embeddedOwner = owner;
            effect.hideFlags = HideFlags.HideAndDontSave;
            effect.draftCode = source;
            effect.parameters = values;
            effect.documentIncludeBasePath = sourcePath;
            return effect;
        }

        internal void CompileAgentDraft() => CompileProgram(false, detached: true);

        internal void RestoreDocumentShader()
        {
            ValidatePresetReference();
            EditorApplication.delayCall -= ReloadCatalogAfterEnable;
            RefreshGeneratedWrapper();
            if (AssetDatabase.Contains(this) || HasWorkingShader || lastCompileFailed) return;
            try
            {
                // The serialized draftCode is deliberately kept as a fallback. For a catalog-linked
                // effect it is normally the document-safe snapshot produced during serialization,
                // not the current source file, so prefer the catalog when it is still available.
                if (IsCatalogLinked)
                {
                    ReloadCatalogSource(true);
                    if (HasWorkingShader && !lastCompileFailed) return;
                }
                RestoreWorkingProgram();
                QueueNotification(false);
            }
            catch (Exception error)
            {
                RecordCompileFailure(error);
                throw;
            }
        }

        internal void SuspendDocumentCatalogReload() => EditorApplication.delayCall -= ReloadCatalogAfterEnable;

        private void RestoreWorkingProgram() => CompileProgram(false, detached: true, useWorkingCode: true);

        internal void PreserveDocumentIncludeBase()
        {
            if (!AssetDatabase.Contains(this) && string.IsNullOrEmpty(documentIncludeBasePath) &&
                ShaderSourceBuilder.HasRelativeIncludes(draftCode)) documentIncludeBasePath = SourcePath;
        }

        internal ShaderFX CloneForDocument(WhimTexDocument owner)
        {
            ShaderFX copy = Instantiate(this);
            copy.name = name;
            copy.embeddedOwner = owner;
            copy.shaderKey = Guid.NewGuid().ToString("N");
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.compiledShader = null;
            copy.shaderCreationRecorded = false;
            try
            {
                if (compiledShader != null)
                {
                    copy.compiledShader = Instantiate(compiledShader);
                    copy.compiledShader.hideFlags = HideFlags.HideAndDontSave;
                }
                return copy;
            }
            catch
            {
                DestroyImmediate(copy);
                throw;
            }
        }

        internal ShaderFX CloneForClipboard()
        {
            string includeBasePath = SourcePath;
            ShaderFX copy = CloneForDocument(null);
            copy.documentIncludeBasePath = includeBasePath;
            copy.DetachCatalog();
            return copy;
        }

        internal void RegisterCreatedCopyUndo(string undoName)
        {
            shaderCreationRecorded = compiledShader != null;
            if (compiledShader != null)
                Undo.RegisterCreatedObjectUndo(compiledShader, undoName);
            Undo.RegisterCreatedObjectUndo(this, undoName);
        }

        internal void MarkDraftChanged()
        {
            if (embeddedOwner != null)
                EditorUtility.SetDirty(embeddedOwner);
        }

        internal void SetDraftCode(string value)
        {
            if (WhimTexApi.IsShaderFXContentLocked(this)) return;
            draftCode = value ?? string.Empty;
            EditorUtility.SetDirty(this);
            MarkDraftChanged();
        }
        internal bool HasPendingChanges
        {
            get
            {
                if (Mode == ShaderProgramMode.Graph) return !HasWorkingShader || compiledGraphFingerprint != ShaderGraphCompiler.CodeFingerprint(Graph);
                if (!HasWorkingShader || draftCode != workingProgramSource || parameters.Count != workingParameters.Count)
                    return true;
                for (int i = 0; i < parameters.Count; i++)
                    if (parameters[i] == null || parameters[i].name != workingParameters[i].name ||
                        parameters[i].type != workingParameters[i].type)
                        return true;
                return false;
            }
        }

        private void OnEnable()
        {
            undoDeserialized = Undo.isProcessing;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseMaterial;
            EditorApplication.quitting += ReleaseMaterial;
            EditorApplication.delayCall += ReloadCatalogAfterEnable;
            WhimTexApi.LiveEditLocksChanged += RetryCatalogAfterUnlock;
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= ReloadCatalogAfterEnable;
            WhimTexApi.LiveEditLocksChanged -= RetryCatalogAfterUnlock;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseMaterial;
            EditorApplication.quitting -= ReleaseMaterial;
            EditorApplication.delayCall -= SendNotification;
            notificationQueued = false;
            notificationContainsChanges = false;
            ReleaseCatalogDependencies();
            ReleaseMaterial();
        }

        private void ReloadCatalogAfterEnable()
        {
            if (this == null || Undo.isProcessing) return;
            ReloadCatalogSource();
            RefreshGeneratedWrapper();
        }

        private void RefreshGeneratedWrapper()
        {
            if (!HasWorkingShader || lastCompileFailed || AssetDatabase.Contains(this) ||
                workingShaderSource == null || (workingShaderSource.StartsWith(ShaderSourceBuilder.ViewportWrapperMarker, StringComparison.Ordinal) &&
                workingShaderSource.Contains(ShaderEntryPoint.WrapperMarker))) return;
            // Rebuild derived resources after a package/script reload without applying
            // pending edits, changing user values, marking a document dirty or adding Undo.
            if (Mode == ShaderProgramMode.Graph) { InvalidateGraph(); EnsureGraphCompiled(); return; }
            var shader = compiledShader;
            CompileProgram(false, detached: true, rebuildApplied: true);
            if (shader != null && !AssetDatabase.Contains(shader))
            {
                ShaderUtil.UpdateShaderAsset(shader, workingShaderSource, true);
                DestroyImmediate(compiledShader); compiledShader = shader;
            }
        }

        private void OnDestroy()
        {
            if (shaderCreationRecorded && Undo.isProcessing)
                return;
            if (compiledShader != null && !AssetDatabase.Contains(compiledShader))
                DestroyImmediate(compiledShader);
        }

        internal void DestroyEmbeddedWithUndo(WhimTexDocument owner)
        {
            if (embeddedOwner != owner)
                return;
            Shader shader = compiledShader;
            string ownerPath = AssetDatabase.GetAssetPath(owner);
            bool ownsShader = shader != null && (!AssetDatabase.Contains(shader) ||
                !string.IsNullOrEmpty(ownerPath) && AssetDatabase.GetAssetPath(shader) == ownerPath);
            Undo.RegisterCompleteObjectUndo(this, "Remove Shader FX");
            if (ownsShader)
                Undo.DestroyObjectImmediate(shader);
            Undo.DestroyObjectImmediate(this);
        }

        internal void NotifyValuesChanged()
        {
            undoDeserialized = false;
            MarkDraftChanged();
            QueueNotification(true);
        }

        internal bool CanResetParameters => !WhimTexApi.IsShaderFXContentLocked(this) && TryGetParameterDefaults(out _);

        private bool TryGetParameterDefaults(out List<ShaderParameter> defaults)
        {
            defaults = null;
            if (Mode == ShaderProgramMode.Graph)
            {
                if (!IsCatalogLinked) return false;
                try
                {
                    var entry = ShaderPresetCatalog.FindPreset(PresetId);
                    if (entry == null || entry.version != PresetVersion || !ShaderPresetIO.IsProgramPath(entry.path)) return false;
                    var stored = ShaderPresetIO.Load(entry).program.draftGraph.parameters;
                    if (stored.Count == 0 || stored.Count != parameters.Count) return false;
                    defaults = new List<ShaderParameter>(parameters.Count);
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var parameter in parameters)
                    {
                        if (parameter == null) return false;
                        // Preset refresh matches by name and retains host IDs. A source ID can
                        // therefore belong to a different name after a rename or reorder.
                        var value = stored.Find(p => p != null && p.name == parameter.name);
                        if (value == null || value.type != parameter.type || value.ValueType != parameter.ValueType || !used.Add(value.id)) return false;
                        var copy = value.Copy();
                        copy.id = parameter.id;
                        copy.name = parameter.name;
                        defaults.Add(copy);
                    }
                    return true;
                }
                catch (Exception) { defaults = null; return false; }
            }
            try { defaults = ShaderMetadata.Parse(draftCode, false, out _); }
            catch (FormatException) { return false; }
            if (defaults.Count == 0 || defaults.Count != parameters.Count) return false;
            foreach (var parameter in parameters)
                if (parameter == null || !defaults.Exists(p => p != null && p.name == parameter.name &&
                    p.type == parameter.type && p.ValueType == parameter.ValueType))
                    return false;
            return true;
        }

        internal bool ResetParameters()
        {
            if (WhimTexApi.IsShaderFXContentLocked(this) || !TryGetParameterDefaults(out var defaults)) return false;
            Undo.RecordObject(this, "Reset FX");
            if (embeddedOwner != null) Undo.RecordObject(embeddedOwner, "Reset FX");
            foreach (var parameter in parameters)
                ShaderMetadata.CopyValue(defaults.Find(p => Mode == ShaderProgramMode.Graph ? p.id == parameter.id : p.name == parameter.name), parameter);
            EditorUtility.SetDirty(this);
            NotifyValuesChanged();
            return true;
        }

        private void QueueNotification(bool contentChanged)
        {
            Changed?.Invoke(this);
            // Resource restoration must never downgrade a real edit already queued this frame.
            notificationContainsChanges |= contentChanged;
            if (notificationQueued)
                return;
            notificationQueued = true;
            EditorApplication.delayCall += SendNotification;
        }

        private void SendNotification()
        {
            bool contentChanged = notificationContainsChanges;
            notificationQueued = false;
            notificationContainsChanges = false;
            if (this != null)
                WhimTexDocument.NotifyShaderFXChanged(this, contentChanged);
        }

        internal Material GetMaterial(in LayerRenderContext context)
        {
            if (IsUnavailable)
                return null;
            if (Mode == ShaderProgramMode.Graph)
            {
                if (graphRuntime == null || graphRuntime.Shader != compiledShader)
                {
                    graphRuntime?.Dispose();
                    graphCompilation ??= ShaderGraphCompiler.Compile(Graph, effectContext: true);
                    graphRuntime = ShaderProgramRuntime.Attach(compiledShader, graphCompilation, compiledGraphFingerprint, Program.workingGraph);
                }
                graphRuntime.BindEffect(Graph, context);
                return graphRuntime.Material;
            }
            if (material == null || materialSourceShader != compiledShader)
            {
                ReleaseMaterial();
                materialSourceShader = compiledShader;
                material = new Material(compiledShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var plan = PrepareParameterPlan();
            if (plan.Length != 0)
            {
                parameterBindings ??= new ShaderParameterBindings();
                parameterBindings.Apply(material, plan, context, retainValues: true);
            }
            ShaderParameterBindings.BindContext(material, context, effectContext: true);
            return material;
        }

        internal bool Compile() => CompileCore(true);
        private bool CompileCore(bool contentChanged) => CompileProgram(contentChanged);

        private bool CompileProgram(bool contentChanged, bool detached = false, bool useWorkingCode = false, bool rebuildApplied = false)
        {
            void RestoreFallback()
            {
                if (!compilingProgram && !HasWorkingProgram && !useWorkingCode && Program.HasWorkingProgram(Mode))
                    CompileProgramCore(false, detached: false, useWorkingCode: true);
            }
            try
            {
                bool success = CompileProgramCore(contentChanged, detached, useWorkingCode, rebuildApplied);
                if (!success) RestoreFallback();
                return success;
            }
            catch { RestoreFallback(); throw; }
        }

        private bool CompileProgramCore(bool contentChanged, bool detached = false, bool useWorkingCode = false, bool rebuildApplied = false)
        {
            if (compilingProgram || !detached && WhimTexApi.IsShaderFXContentLocked(this)) return false;
            compilingProgram = true;
            ShaderProgramRuntime candidateProgram = null;
            List<ShaderParameter> previousParameters = parameters;
            IReadOnlyList<ShaderDiagnostic> messages = null;
            try
            {
                if (Mode == ShaderProgramMode.Graph) CompilationStatus.RecordAttempt(ShaderGraphCompiler.CodeFingerprint(Graph));
                candidateProgram = ShaderProgramRuntime.CreateProgram(Program, Mode, SourcePath, effectContext: true, useWorkingCode: useWorkingCode,
                    codeSource: rebuildApplied ? workingProgramSource : null, codeValues: rebuildApplied ? workingParameters : null,
                    useWorkingGraph: useWorkingCode);
                Shader candidate = candidateProgram.Shader;
                var nextParameters = candidateProgram.CodeParameters ?? candidateProgram.WorkingGraph.parameters;
                string source = candidateProgram.ShaderSource;
                var localityReasons = candidateProgram.LocalityReasons;
                messages = candidateProgram.DiagnosticMessages;

                List<ShaderParameter> snapshot = null;
                if (Mode == ShaderProgramMode.Graph)
                {
                    snapshot = new List<ShaderParameter>(nextParameters.Count);
                    foreach (ShaderParameter parameter in nextParameters) snapshot.Add(parameter.Copy());
                }
                if (contentChanged)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                }
                if (!detached && compiledShader != null && !AssetDatabase.Contains(compiledShader))
                {
                    try
                    {
                        ShaderUtil.UpdateShaderAsset(compiledShader, source, true);
                    }
                    catch
                    {
                        if (!string.IsNullOrEmpty(workingShaderSource))
                            ShaderUtil.UpdateShaderAsset(compiledShader, workingShaderSource, true);
                        throw;
                    }
                }
                else
                {
                    candidate.hideFlags = HideFlags.HideAndDontSave;
                    compiledShader = candidate;
                }
                if (!rebuildApplied && Mode == ShaderProgramMode.Code && (!useWorkingCode || string.IsNullOrEmpty(Program.workingCode))) parameters = nextParameters;
                workingMode = Mode;
                workingProgramSource = candidateProgram.Source;
                Program.Accept(candidateProgram, Mode);
                graphCompilation = candidateProgram.Compilation;
                compiledGraphFingerprint = candidateProgram.Fingerprint;
                var status = CompilationStatus;
                status.Succeed(Mode == ShaderProgramMode.Graph ? ShaderGraphCompiler.CodeFingerprint(Graph) : compiledGraphFingerprint, candidateProgram,
                    preserveDraftError: useWorkingCode || rebuildApplied);
                workingShaderSource = source;
                workingLocalityReasons = localityReasons;
                workingParameters = Mode == ShaderProgramMode.Code ? Program.workingCodeParameters : snapshot;
                if (!status.HasError) SetDiagnostics(messages, false);
                ReleaseMaterial();
                if (!detached) EditorUtility.SetDirty(compiledShader);
                if (contentChanged) EditorUtility.SetDirty(this);
                if (contentChanged && embeddedOwner != null)
                {
                    EditorUtility.SetDirty(embeddedOwner);
                }
                if (contentChanged) NotifyValuesChanged();
                else QueueNotification(false);
                return true;
            }
            catch (Exception exception)
            {
                parameters = previousParameters;
                RecordCompileFailure(exception, messages, draft: !useWorkingCode && !rebuildApplied);
                if (contentChanged)
                {
                    EditorUtility.SetDirty(this);
                    NotifyValuesChanged();
                }
                else QueueNotification(false);
                if (detached) throw;
                return false;
            }
            finally
            {
                compilingProgram = false;
                if (candidateProgram != null)
                {
                    candidateProgram.OwnsShader = candidateProgram.Shader != compiledShader;
                    candidateProgram.Dispose();
                }
            }
        }

        private void ReleaseMaterial()
        {
            graphRuntime?.Dispose(); graphRuntime = null;
            parameterBindings?.Dispose();
            parameterBindings = null;
            if (material != null)
                DestroyImmediate(material);
            material = null;
            materialSourceShader = null;
        }
    }
}
