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
        [NonSerialized] private ShaderProgramRuntime runtime;
        [NonSerialized] private ShaderProgramMode runtimeMode;
        [NonSerialized] private bool compilingProgram;
        [SerializeField, HideInInspector] private ShaderCompilationState codeCompilationStatus, graphCompilationStatus;
        [SerializeField, HideInInspector] private WhimTexDocument embeddedOwner;
        [SerializeField, HideInInspector] private string documentIncludeBasePath;
        [SerializeField, HideInInspector] private string shaderKey = Guid.NewGuid().ToString("N");
        private IReadOnlyList<ShaderParameter> WorkingParameters => runtime?.Parameters ??
            (Mode == ShaderProgramMode.Code ? Program.WorkingCodeParameters : Program.workingGraph?.parameters) ?? Array.Empty<ShaderParameter>();

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
            ReleaseRuntime();
            InvalidateGraph();
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
        internal string Diagnostics => CompilationStatus.Diagnostics ?? "Not compiled yet. Click Compile to use this effect.";
        internal bool LastCompileFailed => CompilationStatus.HasError;
        internal bool HasWorkingShader => runtime?.Shader != null && runtimeMode == Mode;
        private bool HasWorkingProgram => HasWorkingShader;
        internal Shader PreviewCompiledShader => HasWorkingShader ? runtime.Shader : null;
        internal string PreviewCompiledSource => HasWorkingShader ? runtime.ShaderSource : null;
        internal bool IsUnavailable => !HasWorkingProgram;
        internal string UnavailableReason => IsUnavailable ? $"{name}: FX is skipped until it compiles successfully.\n{Diagnostics}" : null;
        internal bool SupportsRegionalColorInput => HasWorkingProgram && runtime.Analysis.SupportsRegionalColorInput;
        /// <summary>Whether the applied, usable shader has a verified pixel-local authoring contract.</summary>
        public bool IsPixelLocal => HasWorkingProgram && runtime.Analysis.IsPixelLocal;
        /// <summary>Conservative dependencies of the applied, usable shader, including its libraries.</summary>
        public bool UsesTime => HasWorkingProgram && runtime.UsesTime;
        public bool UsesInstanceIndex => HasWorkingProgram && runtime.UsesInstanceIndex;
        internal string WorkingLocalityStatus => HasWorkingProgram ? runtime.Analysis.LocalityStatus : null;
        internal IReadOnlyList<ShaderLocalityReason> WorkingLocalityReasons => runtime?.LocalityReasons ?? Array.Empty<ShaderLocalityReason>();

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
            catch (Exception) when (LastCompileFailed) { warning = DiagnosticNotice; return !IsUnavailable; }
        }
        internal bool UsesUnityTimeInputs => runtime?.Analysis.UnityTimeWarning != null;
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

        internal void CompileAgentDraft() => CompileDraftProgram();

        internal void RestoreDocumentShader()
        {
            ValidatePresetReference();
            EditorApplication.delayCall -= ReloadCatalogAfterEnable;
            if (AssetDatabase.Contains(this) || HasWorkingShader || LastCompileFailed) return;
            try
            {
                // The serialized draftCode is deliberately kept as a fallback. For a catalog-linked
                // effect it is normally the document-safe snapshot produced during serialization,
                // not the current source file, so prefer the catalog when it is still available.
                if (IsCatalogLinked)
                {
                    ReloadCatalogSource(true);
                    if (HasWorkingShader && !LastCompileFailed) return;
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

        private void RestoreWorkingProgram()
        {
            try { BuildProgram(ShaderProgramVersion.Working); }
            catch (Exception error) { RecordCompileFailure(error); throw; }
        }

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
            if (string.IsNullOrEmpty(copy.documentIncludeBasePath)) copy.documentIncludeBasePath = SourcePath;
            try
            {
                if (Program.HasWorkingProgram(Mode)) copy.RestoreWorkingProgram();
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
                if (Mode == ShaderProgramMode.Graph) return !HasWorkingShader || runtime.Fingerprint != ShaderGraphCompiler.CodeFingerprint(Graph);
                if (!HasWorkingShader || draftCode != Program.workingCode || parameters.Count != WorkingParameters.Count)
                    return true;
                for (int i = 0; i < parameters.Count; i++)
                    if (parameters[i] == null || parameters[i].name != WorkingParameters[i].name ||
                        parameters[i].type != WorkingParameters[i].type)
                        return true;
                return false;
            }
        }

        private void OnEnable()
        {
            undoDeserialized = Undo.isProcessing;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseRuntime;
            EditorApplication.quitting += ReleaseRuntime;
            EditorApplication.delayCall += ReloadCatalogAfterEnable;
            WhimTexApi.LiveEditLocksChanged += RetryCatalogAfterUnlock;
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= ReloadCatalogAfterEnable;
            WhimTexApi.LiveEditLocksChanged -= RetryCatalogAfterUnlock;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseRuntime;
            EditorApplication.quitting -= ReleaseRuntime;
            EditorApplication.delayCall -= SendNotification;
            notificationQueued = false;
            notificationContainsChanges = false;
            ReleaseCatalogDependencies();
            ReleaseRuntime();
        }

        private void ReloadCatalogAfterEnable()
        {
            if (this == null || Undo.isProcessing) return;
            ReloadCatalogSource();
            PrepareRuntime();
        }

        internal void DestroyEmbeddedWithUndo(WhimTexDocument owner)
        {
            if (embeddedOwner != owner) return;
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
            PrepareRuntime();
            if (IsUnavailable) return null;
            if (Mode == ShaderProgramMode.Graph) runtime.BindEffect(Graph, context);
            else runtime.BindCode(parameters, context, effectContext: true);
            return runtime.Material;
        }

        internal void Render(Texture input, RenderTexture destination, in LayerRenderContext context)
            => runtime.Render(input, destination, context);

        internal bool Compile()
        {
            if (compilingProgram || WhimTexApi.IsShaderFXContentLocked(this)) return false;
            Undo.RecordObject(this, "Compile Shader FX");
            try { CompileDraftProgram(); return true; }
            catch (Exception) { return false; }
            finally
            {
                EditorUtility.SetDirty(this);
                NotifyValuesChanged();
            }
        }

        private void CompileDraftProgram()
        {
            try { BuildProgram(ShaderProgramVersion.Draft); }
            catch (Exception error)
            {
                RecordCompileFailure(error, draft: true);
                if (!HasWorkingProgram && Program.HasWorkingProgram(Mode))
                {
                    // Restore the executable without replacing the failed draft or its diagnostics.
                    try { BuildProgram(ShaderProgramVersion.Working); }
                    catch (Exception) { }
                }
                throw;
            }
        }

        private void BuildProgram(ShaderProgramVersion version)
        {
            if (compilingProgram) throw new InvalidOperationException("Shader compilation is already in progress.");
            compilingProgram = true;
            ShaderProgramRuntime candidate = null;
            try
            {
                if (Mode == ShaderProgramMode.Graph) CompilationStatus.RecordAttempt(ShaderGraphCompiler.CodeFingerprint(Graph));
                bool working = version == ShaderProgramVersion.Working;
                candidate = working ? ShaderProgramRuntime.RestoreWorking(Program, Mode, SourcePath, effectContext: true)
                    : ShaderProgramRuntime.CompileDraft(Program, Mode, SourcePath, effectContext: true);
                if (Mode == ShaderProgramMode.Code && (!working || !Program.HasWorkingProgram(Mode)))
                    parameters = candidate.CodeParameters;
                Program.Accept(candidate, Mode);
                ReleaseRuntime();
                runtime = candidate; candidate = null; runtimeMode = Mode;
                CompilationStatus.Succeed(Mode == ShaderProgramMode.Graph ? ShaderGraphCompiler.CodeFingerprint(Graph) : runtime.Fingerprint,
                    runtime, preserveDraftError: working);
                if (!LastCompileFailed) ShaderDiagnostics.Report(DiagnosticMessages, SourcePath, this);
            }
            finally
            {
                candidate?.Dispose(); compilingProgram = false;
                QueueNotification(false);
            }
        }

        private void ReleaseRuntime()
        {
            runtime?.Dispose(); runtime = null;
            codeCompilationStatus?.Invalidate(); graphCompilationStatus?.Invalidate();
        }
    }
}
