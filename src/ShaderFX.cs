using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderFXTextureSource { Texture = 0, Layer = 1, None = 2, Self = 3 }

    public enum ShaderFXParameterType { Float, Color, Vector, Texture2D, Transform2D, Bool, Enum, Gradient, Vector2, Vector3, Normal, Curve, Point }

    public enum ShaderFXContainerKind { Group, Foldout, Tabs, Tab }

    [Serializable]
    public struct ShaderFXLayoutContainer : IEquatable<ShaderFXLayoutContainer>
    {
        public ShaderFXContainerKind kind;
        public int id;
        public string title;
        public string headerParameter;

        public bool Equals(ShaderFXLayoutContainer other) => kind == other.kind && id == other.id &&
            string.Equals(title ?? string.Empty, other.title ?? string.Empty, StringComparison.Ordinal) &&
            string.Equals(headerParameter ?? string.Empty, other.headerParameter ?? string.Empty, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ShaderFXLayoutContainer other && Equals(other);
        public override int GetHashCode() => id;
    }

    [Serializable]
    public struct ShaderFXEnableCondition : IEquatable<ShaderFXEnableCondition>
    {
        public string parameter;
        public bool notEqual;
        public float value;
        public bool Equals(ShaderFXEnableCondition other) => parameter == other.parameter && notEqual == other.notEqual && value.Equals(other.value);
        public override bool Equals(object obj) => obj is ShaderFXEnableCondition other && Equals(other);
        public override int GetHashCode() => parameter?.GetHashCode() ?? 0;
    }

    [Serializable]
    public sealed class ShaderFXParameterControl
    {
        public string tooltip;
        public string label;
        public string previewFunction;
        public string[] previewParameters = Array.Empty<string>();
        public string[] headers = Array.Empty<string>();
        public string[] helpBoxes = Array.Empty<string>();
        public bool hidden;
        public bool seed;
        public bool inGroup;
        public int groupId;
        public string groupTitle;
        public string groupHeaderParameter;
        public ShaderFXLayoutContainer[] containers = Array.Empty<ShaderFXLayoutContainer>();
        public ShaderFXParameterType type;
        public int order;
        public bool hasMinimum, hasMaximum;
        public bool softMinimum, softMaximum;
        public bool cyclicRange;
        public float minimum, maximum;
        public string[] optionNames = Array.Empty<string>();
        public float[] optionValues = Array.Empty<float>();
        // Editor-only visibility condition. Values remain serialized and applied to the shader.
        public string visibleIfParameter;
        public bool visibleIfNotEqual;
        public float visibleIfValue;
        public ShaderFXEnableCondition[] enableIf = Array.Empty<ShaderFXEnableCondition>();
    }

    [Serializable]
    public sealed class ShaderFXParameter
    {
        public string name = "_Amount";
        public ShaderFXParameterType type;
        public float floatValue = 1f;
        [ColorUsage(true, true)] public Color colorValue = Color.white;
        public Vector4 vectorValue;
        public Texture2D textureValue;
        public ShaderFXTextureSource textureSource;
        public string textureLayerId;
        public WhimTexGradient gradientValue;
        public AnimationCurve curveValue;
        public ShaderFXTransform transformValue = ShaderFXTransform.Default;
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        [HideInInspector] public bool hasMinimum, hasMaximum;
        [HideInInspector] public bool softMinimum, softMaximum;
        [HideInInspector] public bool cyclicRange;
        [HideInInspector] public bool pixelUnits;
        internal bool HasSoftRange => softMinimum || softMaximum;
        internal bool IsInterval => type == ShaderFXParameterType.Vector2 && hasMinimum && hasMaximum;
        [HideInInspector] public float minimum, maximum;
        [HideInInspector] public List<ShaderFXParameterControl> controls = new List<ShaderFXParameterControl>();
        [NonSerialized] private string cachedName, cachedId;
        [NonSerialized] private string cachedPrefix;
        [NonSerialized] private int propertyId, gradientPropertyId, gradientWrapPropertyId, curvePropertyId;
        [NonSerialized] private int textureMapId, textureSizeId;
        [NonSerialized] private int[] transformPropertyIds;
        [NonSerialized] internal RenderFingerprint.ObjectStamp textureFingerprint;
        [NonSerialized] internal RenderFingerprint.TextStamp nameFingerprint, idFingerprint, textureLayerFingerprint;

        private void PreparePropertyIds()
        {
            if (cachedPrefix != null && cachedName == name && cachedId == id) return;
            cachedName = name; cachedId = id;
            cachedPrefix = "_WhimTex_" + name.TrimStart('_') + "_" + id + "_";
            propertyId = Shader.PropertyToID(name);
            textureMapId = Shader.PropertyToID(ShaderFXTextureViews.Prefix(name) + "_UvMap");
            textureSizeId = Shader.PropertyToID(ShaderFXTextureViews.Prefix(name) + "_Size");
            gradientPropertyId = Shader.PropertyToID(cachedPrefix + "Gradient");
            gradientWrapPropertyId = Shader.PropertyToID(cachedPrefix + "GradientWrap");
            curvePropertyId = Shader.PropertyToID(cachedPrefix + "Curve");
            transformPropertyIds = null;
        }
        internal string InternalPrefix { get { PreparePropertyIds(); return cachedPrefix; } }
        internal int PropertyId { get { PreparePropertyIds(); return propertyId; } }
        internal int TextureMapId { get { PreparePropertyIds(); return textureMapId; } }
        internal int TextureSizeId { get { PreparePropertyIds(); return textureSizeId; } }
        internal int GradientPropertyId { get { PreparePropertyIds(); return gradientPropertyId; } }
        internal int GradientWrapPropertyId { get { PreparePropertyIds(); return gradientWrapPropertyId; } }
        internal int CurvePropertyId { get { PreparePropertyIds(); return curvePropertyId; } }
        internal int[] TransformPropertyIds
        {
            get
            {
                PreparePropertyIds();
                if (transformPropertyIds == null)
                {
                    string prefix = cachedPrefix;
                    transformPropertyIds = new[] { Shader.PropertyToID(prefix + "ToLocalRow0"), Shader.PropertyToID(prefix + "ToLocalRow1"), Shader.PropertyToID(prefix + "ToLocalRow2"),
                        Shader.PropertyToID(prefix + "ToInputRow0"), Shader.PropertyToID(prefix + "ToInputRow1"), Shader.PropertyToID(prefix + "ToInputRow2") };
                }
                return transformPropertyIds;
            }
        }
        internal float Clamp(float value) => cyclicRange ? RepeatRange(value, minimum, maximum) :
            hasMinimum && !softMinimum && value < minimum ? minimum : hasMaximum && !softMaximum && value > maximum ? maximum : value;
        internal static float RepeatRange(float value, float minimum, float maximum)
        {
            if (!ProjectiveMatrix.Finite(value)) return minimum;
            if (value >= minimum && value <= maximum) return value;
            double period = (double)maximum - minimum;
            double offset = ((double)value - minimum) % period;
            return (float)(minimum + (offset < 0 ? offset + period : offset));
        }
        internal Vector4 ClampInterval(Vector4 value)
        {
            if (!IsInterval) return value;
            if (cyclicRange && IsFullCycle(value, minimum, maximum)) return new Vector4(value.x, value.y, 0, 0);
            float x = ProjectiveMatrix.Finite(value.x) ? Mathf.Clamp(value.x, minimum, maximum) : minimum;
            float y = ProjectiveMatrix.Finite(value.y) ? Mathf.Clamp(value.y, minimum, maximum) : maximum;
            return cyclicRange ? new Vector4(x, y, 0, 0) : new Vector4(Mathf.Min(x, y), Mathf.Max(x, y), 0, 0);
        }
        internal static bool IsFullCycle(Vector2 value, float minimum, float maximum)
        {
            float period = maximum - minimum;
            double rounding = Math.Max(Math.Abs(value.x), Math.Max(Math.Abs(value.y), Math.Abs(period))) * (1.0 / 16777216.0);
            return ProjectiveMatrix.Finite(value.x) && ProjectiveMatrix.Finite(value.y) && value.y > value.x &&
                (value.x >= minimum && value.x <= maximum || value.y >= minimum && value.y <= maximum) &&
                Math.Abs((double)value.y - value.x - period) <= rounding;
        }
        internal static bool IsValidInterval(Vector2 value, float minimum, float maximum, bool cyclic)
        {
            return cyclic && IsFullCycle(value, minimum, maximum) ||
                ProjectiveMatrix.Finite(value.x) && ProjectiveMatrix.Finite(value.y) &&
                value.x >= minimum && value.x <= maximum && value.y >= minimum && value.y <= maximum &&
                (cyclic || value.x <= value.y);
        }
        internal static Vector3 NormalizeNormal(Vector4 value)
        {
            var v = new Vector3(value.x, value.y, value.z);
            return v.sqrMagnitude > 1e-12f && !float.IsInfinity(v.sqrMagnitude) ? v.normalized : Vector3.forward;
        }
        internal bool BoolValue => floatValue >= 0.5f;

        internal ShaderFXParameter Copy()
        {
            var copy = (ShaderFXParameter)MemberwiseClone();
            copy.gradientValue = gradientValue?.Clone();
            copy.curveValue = curveValue == null ? null : WhimTexCurveTexture.Copy(curveValue);
            copy.controls = new List<ShaderFXParameterControl>(controls.Count);
            foreach (var c in controls)
                copy.controls.Add(new ShaderFXParameterControl { type = c.type, order = c.order, tooltip = c.tooltip, label = c.label, previewFunction = c.previewFunction,
                    previewParameters = c.previewParameters == null ? Array.Empty<string>() : (string[])c.previewParameters.Clone(),
                    headers = c.headers == null ? Array.Empty<string>() : (string[])c.headers.Clone(),
                    helpBoxes = c.helpBoxes == null ? Array.Empty<string>() : (string[])c.helpBoxes.Clone(),
                    hidden = c.hidden, seed = c.seed, inGroup = c.inGroup, groupId = c.groupId,
                    groupTitle = c.groupTitle, groupHeaderParameter = c.groupHeaderParameter,
                    containers = c.containers == null ? Array.Empty<ShaderFXLayoutContainer>() : (ShaderFXLayoutContainer[])c.containers.Clone(),
                    hasMinimum = c.hasMinimum, hasMaximum = c.hasMaximum, softMinimum = c.softMinimum, softMaximum = c.softMaximum, cyclicRange = c.cyclicRange, minimum = c.minimum, maximum = c.maximum,
                    optionNames = (string[])c.optionNames.Clone(), optionValues = (float[])c.optionValues.Clone(),
                    visibleIfParameter = c.visibleIfParameter, visibleIfNotEqual = c.visibleIfNotEqual, visibleIfValue = c.visibleIfValue,
                    enableIf = c.enableIf == null ? Array.Empty<ShaderFXEnableCondition>() : (ShaderFXEnableCondition[])c.enableIf.Clone() });
            return copy;
        }

        internal void SetValue(Material material, ShaderFXParameter declaration, Vector2 dimensions, float pixelScale = 1f)
        {
            int property = declaration.PropertyId;
            float units = declaration.pixelUnits ? pixelScale : 1f;
            switch (type)
            {
                case ShaderFXParameterType.Enum:
                case ShaderFXParameterType.Float: material.SetFloat(property, floatValue * units); break;
                case ShaderFXParameterType.Bool: material.SetFloat(property, BoolValue ? 1f : 0f); break;
                case ShaderFXParameterType.Color: HdrUtility.SetShaderColor(material, declaration.name, colorValue); break;
                case ShaderFXParameterType.Vector2:
                case ShaderFXParameterType.Point:
                case ShaderFXParameterType.Vector3:
                case ShaderFXParameterType.Vector: material.SetVector(property, vectorValue * units); break;
                case ShaderFXParameterType.Normal: material.SetVector(property, NormalizeNormal(vectorValue)); break;
                case ShaderFXParameterType.Texture2D:
                    material.SetTexture(property, textureValue != null ? textureValue : Texture2D.whiteTexture);
                    break;
                case ShaderFXParameterType.Transform2D:
                    transformValue.GetRows(dimensions, out var l0, out var l1, out var l2, out var i0, out var i1, out var i2);
                    int[] ids = declaration.TransformPropertyIds;
                    material.SetVector(ids[0], l0);
                    material.SetVector(ids[1], l1);
                    material.SetVector(ids[2], l2);
                    material.SetVector(ids[3], i0);
                    material.SetVector(ids[4], i1);
                    material.SetVector(ids[5], i2);
                    break;
            }
        }
    }
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

        [SerializeField, TextArea(12, 40)] private string code =
            "// @param color _Tint = (1, 1, 1, 1)\n" +
            "// #include \"./MyLibrary.hlsl\"\n\n" +
            "float4 ApplyFX(float4 color)\n{\n" + CodeIndent + "return color * _Tint;\n}\n";
        [SerializeField] private List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        [SerializeField, HideInInspector] private Shader compiledShader;
        [SerializeField, HideInInspector] private string appliedCode;
        [SerializeField, HideInInspector] private string appliedSource;
        [SerializeField, HideInInspector] private List<ShaderFXParameter> appliedParameters = new List<ShaderFXParameter>();
        [SerializeField, HideInInspector] private string diagnostics = "Not compiled yet. Click Compile to use this effect.";
        [SerializeField, HideInInspector] private bool lastApplyFailed;
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
            MarkDraftChanged();
            return true;
        }

        internal string Code => code;
        internal IReadOnlyList<ShaderFXParameter> Parameters => parameters;
        internal string Diagnostics => diagnostics;
        internal bool LastApplyFailed => lastApplyFailed;
        internal bool HasAppliedShader => compiledShader != null;
        internal Shader PreviewCompiledShader => compiledShader;
        internal string PreviewCompiledSource => appliedSource;
        internal bool IsUnavailable => lastApplyFailed || compiledShader == null;
        internal string UnavailableReason => IsUnavailable ? $"{name}: FX is skipped until it compiles successfully.\n{diagnostics}" : null;
        internal bool SupportsRegionalColorInput => appliedSource != null &&
            appliedSource.Contains(ShaderFXSourceBuilder.RegionalColorMarker);

        /// <summary>Whether the applied, usable shader has a verified pixel-local authoring contract.</summary>
        public bool IsPixelLocal => !IsUnavailable && appliedSource != null &&
            appliedSource.StartsWith(ShaderFXSourceBuilder.ViewportWrapperMarker + ShaderFXEntryPoint.WrapperMarker +
                ShaderFXEntryPoint.PixelLocalMarker, StringComparison.Ordinal);

        internal bool TryPrepareDocumentEffect(out string warning)
        {
            try { ApplyAgentDraft(); warning = DiagnosticNotice; return true; }
            catch (Exception) when (lastApplyFailed) { warning = UnavailableReason; return false; }
        }
        internal bool UsesUnityTimeInputs
        {
            get
            {
                string source = !string.IsNullOrEmpty(appliedSource) ? appliedSource : code;
                if (!determinismWarningCached || !string.Equals(determinismWarningSource, source, StringComparison.Ordinal))
                {
                    determinismWarningSource = source;
                    determinismWarningFound = ShaderFXSourceBuilder.GetDeterminismWarning(source) != null;
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

        internal static ShaderFX CreateAgentDraft(WhimTexDocument owner, string source, List<ShaderFXParameter> values)
        {
            return CreateAgentDraft(owner, source, values, null);
        }

        internal static ShaderFX CreateAgentDraft(WhimTexDocument owner, string source, List<ShaderFXParameter> values, string sourcePath)
        {
            var effect = CreateInstance<ShaderFX>();
            effect.name = "Shader FX";
            effect.embeddedOwner = owner;
            effect.hideFlags = HideFlags.HideAndDontSave;
            effect.code = source;
            effect.parameters = values;
            effect.documentIncludeBasePath = sourcePath;
            return effect;
        }

        internal void ApplyAgentDraft()
        {
            Shader candidate = null;
            Material test = null;
            var previousParameters = parameters;
            List<ShaderFXDiagnostic> messages = null;
            try
            {
                PrepareParameterDeclarations();
                string source = ShaderFXSourceBuilder.Build(this, SourcePath);
                candidate = ShaderUtil.CreateShaderAsset(source, true);
                if (candidate == null) throw new InvalidOperationException("Unity could not create the shader.");
                candidate.hideFlags = HideFlags.HideAndDontSave;
                test = new Material(candidate) { hideFlags = HideFlags.HideAndDontSave };
                if (test.passCount > 0) ShaderUtil.CompilePass(test, 0, true);
                messages = ShaderFXDiagnostics.Collect(candidate, source, code, parameters, SourcePath, candidate.isSupported && test.passCount > 0);
                if (ShaderFXDiagnostics.HasErrors(messages))
                    throw new InvalidOperationException(ShaderFXDiagnostics.Format(messages));
                compiledShader = candidate;
                candidate = null;
                appliedCode = code;
                appliedSource = source;
                appliedParameters = new List<ShaderFXParameter>();
                foreach (var parameter in parameters) appliedParameters.Add(parameter.Copy());
                ReleaseMaterial();
                QueueNotification(false);
                SetDiagnostics(messages, false);
            }
            catch (Exception error)
            {
                parameters = previousParameters;
                RecordApplyFailure(error, messages);
                QueueNotification(false);
                throw;
            }
            finally
            {
                if (test != null) DestroyImmediate(test);
                if (candidate != null) DestroyImmediate(candidate);
            }
        }

        internal void RestoreDocumentShader()
        {
            EditorApplication.delayCall -= ReloadCatalogAfterEnable;
            RefreshGeneratedWrapper();
            if (AssetDatabase.Contains(this) || HasAppliedShader || lastApplyFailed) return;
            try
            {
                // The serialized code is deliberately kept as a fallback. For a catalog-linked
                // effect it is normally the document-safe snapshot produced during serialization,
                // not the current source file, so prefer the catalog when it is still available.
                if (IsCatalogLinked)
                {
                    string fallback = code;
                    bool locked = WhimTexApi.IsShaderFXContentLocked(this);
                    ReloadCatalogSource(true);
                    if (HasAppliedShader && !lastApplyFailed) return;

                    // A live edit lock only postpones catalog refresh; retain the link so the
                    // unlock callback can retry it. A deleted or broken preset, however, must
                    // detach and use the source snapshot stored in the TIFF.
                    if (!locked)
                    {
                        DetachCatalog();
                        NotifyValuesChanged();
                    }
                    code = fallback;
                }
                ApplyAgentDraft();
                QueueNotification(false);
            }
            catch (Exception error)
            {
                RecordApplyFailure(error);
                throw;
            }
        }

        internal void SuspendDocumentCatalogReload() => EditorApplication.delayCall -= ReloadCatalogAfterEnable;

        internal void PreserveDocumentIncludeBase()
        {
            if (!AssetDatabase.Contains(this) && string.IsNullOrEmpty(documentIncludeBasePath) &&
                ShaderFXSourceBuilder.HasRelativeIncludes(code)) documentIncludeBasePath = SourcePath;
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
            code = value ?? string.Empty;
            EditorUtility.SetDirty(this);
            MarkDraftChanged();
        }
        internal bool HasPendingChanges
        {
            get
            {
                if (compiledShader == null || code != appliedCode || parameters.Count != appliedParameters.Count)
                    return true;
                for (int i = 0; i < parameters.Count; i++)
                    if (parameters[i] == null || parameters[i].name != appliedParameters[i].name ||
                        parameters[i].type != appliedParameters[i].type)
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
            if (!HasAppliedShader || lastApplyFailed || AssetDatabase.Contains(this) ||
                appliedSource == null || appliedSource.Contains(ShaderFXSourceBuilder.ViewportWrapperMarker)) return;
            // Rebuild derived resources after a package/script reload without applying
            // pending edits, changing user values, marking a document dirty or adding Undo.
            var draft = code; var values = parameters; var shader = compiledShader;
            try
            {
                code = appliedCode; parameters = appliedParameters;
                ApplyAgentDraft();
                if (shader != null && !AssetDatabase.Contains(shader))
                {
                    ShaderUtil.UpdateShaderAsset(shader, appliedSource, true);
                    DestroyImmediate(compiledShader); compiledShader = shader;
                }
            }
            finally { code = draft; parameters = values; }
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

        private bool TryGetParameterDefaults(out List<ShaderFXParameter> defaults)
        {
            defaults = null;
            try { defaults = ShaderFXMetadata.Parse(code, false, out _); }
            catch (FormatException) { return false; }
            if (defaults.Count == 0 || defaults.Count != parameters.Count) return false;
            foreach (var parameter in parameters)
                if (parameter == null || !defaults.Exists(p => p.name == parameter.name && p.type == parameter.type))
                    return false;
            return true;
        }

        internal bool ResetParameters()
        {
            if (WhimTexApi.IsShaderFXContentLocked(this) || !TryGetParameterDefaults(out var defaults)) return false;
            Undo.RecordObject(this, "Reset FX");
            foreach (var parameter in parameters)
                ShaderFXMetadata.CopyValue(defaults.Find(p => p.name == parameter.name), parameter);
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
            if (material == null || materialSourceShader != compiledShader)
            {
                ReleaseMaterial();
                materialSourceShader = compiledShader;
                material = new Material(compiledShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var plan = PrepareParameterPlan();
            foreach (var binding in plan)
            {
                parameterBindings ??= new ShaderParameterBindings();
                parameterBindings.Apply(material, binding.value, binding.declaration, new Vector2(context.activeDocument.width, context.activeDocument.height), context.pixelScale, textureViews: true);
            }
            var document = context.activeDocument;
            float inputWidth = document.RenderingViewport ? Mathf.Ceil(document.width / context.scaleMultiplier) : context.width;
            float inputHeight = document.RenderingViewport ? Mathf.Ceil(document.height / context.scaleMultiplier) : context.height;
            material.SetVector("_InputSize", new Vector4(inputWidth, inputHeight, 1f / inputWidth, 1f / inputHeight));
            var viewport = document.ViewportToCanvas;
            material.SetVector("_WhimTex_Viewport", document.RenderingViewport
                ? new Vector4((float)viewport.m02, (float)viewport.m12, (float)viewport.m00, (float)viewport.m11)
                : new Vector4(0, 0, 1, 1));
            material.SetVector("_CanvasSize", new Vector4(context.activeDocument.width, context.activeDocument.height,
                1f / context.activeDocument.width, 1f / context.activeDocument.height));
            material.SetFloat("_RenderScale", context.scaleMultiplier);
            material.SetFloat("_PixelScale", context.pixelScale);
            ShaderTextureView.ForContext(null, context).BindMetadata(material, ShaderTextureView.MainMapId, ShaderTextureView.MainSizeId);
            return material;
        }

        internal bool Apply() => ApplyCore(true);

        private bool ApplyCore(bool contentChanged)
        {
            if (WhimTexApi.IsShaderFXContentLocked(this)) return false;
            Shader candidate = null;
            Material candidateMaterial = null;
            List<ShaderFXParameter> previousParameters = parameters;
            List<ShaderFXDiagnostic> messages = null;
            try
            {
                PrepareParameterDeclarations();
                UnityEngine.Object storage = embeddedOwner != null ? (UnityEngine.Object)embeddedOwner : this;
                string path = AssetDatabase.GetAssetPath(storage);
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsOpenForEdit(storage))
                    throw new InvalidOperationException("The Shader FX asset is read-only. Make it editable before applying.");
                string source = ShaderFXSourceBuilder.Build(this, SourcePath);
                candidate = ShaderUtil.CreateShaderAsset(source, true);
                if (candidate == null)
                    throw new InvalidOperationException("Unity could not create the shader.");
                candidateMaterial = new Material(candidate) { hideFlags = HideFlags.HideAndDontSave };
                if (candidateMaterial.passCount > 0)
                    ShaderUtil.CompilePass(candidateMaterial, 0, true);
                messages = ShaderFXDiagnostics.Collect(candidate, source, code, parameters, SourcePath, candidate.isSupported && candidateMaterial.passCount > 0);
                if (ShaderFXDiagnostics.HasErrors(messages))
                    throw new InvalidOperationException(ShaderFXDiagnostics.Format(messages));

                List<ShaderFXParameter> snapshot = new List<ShaderFXParameter>(parameters.Count);
                foreach (ShaderFXParameter parameter in parameters)
                    snapshot.Add(parameter.Copy());
                if (contentChanged)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                }
                if (compiledShader != null && AssetDatabase.GetAssetPath(compiledShader) == path)
                {
                    try
                    {
                        ShaderUtil.UpdateShaderAsset(compiledShader, source, true);
                    }
                    catch
                    {
                        if (!string.IsNullOrEmpty(appliedSource))
                            ShaderUtil.UpdateShaderAsset(compiledShader, appliedSource, true);
                        throw;
                    }
                }
                else
                {
                    candidate.hideFlags = string.IsNullOrEmpty(path) ? HideFlags.HideAndDontSave : HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                    if (!string.IsNullOrEmpty(path))
                        AssetDatabase.AddObjectToAsset(candidate, storage);
                    compiledShader = candidate;
                    candidate = null;
                }
                appliedCode = code;
                appliedSource = source;
                appliedParameters = snapshot;
                SetDiagnostics(messages, false);
                ReleaseMaterial();
                EditorUtility.SetDirty(compiledShader);
                if (contentChanged) EditorUtility.SetDirty(this);
                if (contentChanged && embeddedOwner != null)
                {
                    EditorUtility.SetDirty(embeddedOwner);
                }
                else if (contentChanged && !string.IsNullOrEmpty(path))
                {
                    AssetDatabase.SetMainObject(this, path);
                    AssetDatabase.SaveAssetIfDirty(this);
                }
                if (contentChanged) NotifyValuesChanged();
                else QueueNotification(false);
                return true;
            }
            catch (Exception exception)
            {
                parameters = previousParameters;
                RecordApplyFailure(exception, messages);
                if (contentChanged)
                {
                    EditorUtility.SetDirty(this);
                    NotifyValuesChanged();
                }
                else QueueNotification(false);
                return false;
            }
            finally
            {
                if (candidateMaterial != null)
                    DestroyImmediate(candidateMaterial);
                if (candidate != null && !AssetDatabase.Contains(candidate))
                    DestroyImmediate(candidate);
            }
        }

        private void ReleaseMaterial()
        {
            parameterBindings?.Dispose();
            parameterBindings = null;
            if (material != null)
                DestroyImmediate(material);
            material = null;
            materialSourceShader = null;
        }
    }
}
