using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    public enum ShaderFXTextureSource { Texture = 0, Layer = 1, None = 2, Self = 3 }

    public enum ShaderFXParameterType { Float, Color, Vector, Texture2D, Transform2D, Bool, Enum, Gradient, Vector2, Vector3, Normal, Curve }

    [Serializable]
    public sealed class ShaderFXParameterControl
    {
        public string tooltip;
        public string[] headers = Array.Empty<string>();
        public ShaderFXParameterType type;
        public int order;
        public bool hasMinimum, hasMaximum;
        public bool softMinimum, softMaximum;
        public float minimum, maximum;
        public string[] optionNames = Array.Empty<string>();
        public float[] optionValues = Array.Empty<float>();
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
        [HideInInspector] public bool declaredInCode;
        [HideInInspector] public bool hasMinimum, hasMaximum;
        [HideInInspector] public bool softMinimum, softMaximum;
        internal bool HasSoftRange => softMinimum || softMaximum;
        [HideInInspector] public float minimum, maximum;
        [HideInInspector] public List<ShaderFXParameterControl> controls = new List<ShaderFXParameterControl>();
        [NonSerialized] private string cachedName, cachedId;
        [NonSerialized] private int[] transformPropertyIds;

        internal string InternalPrefix => "_WhimTex_" + name.TrimStart('_') + "_" + id + "_";
        internal int[] TransformPropertyIds
        {
            get
            {
                if (transformPropertyIds == null || cachedName != name || cachedId != id)
                {
                    cachedName = name;
                    cachedId = id;
                    string prefix = InternalPrefix;
                    transformPropertyIds = new[] { Shader.PropertyToID(prefix + "ToLocalRow0"), Shader.PropertyToID(prefix + "ToLocalRow1"), Shader.PropertyToID(prefix + "ToLocalRow2"),
                        Shader.PropertyToID(prefix + "ToInputRow0"), Shader.PropertyToID(prefix + "ToInputRow1"), Shader.PropertyToID(prefix + "ToInputRow2") };
                }
                return transformPropertyIds;
            }
        }
        internal float Clamp(float value) => hasMinimum && !softMinimum && value < minimum ? minimum : hasMaximum && !softMaximum && value > maximum ? maximum : value;
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
                copy.controls.Add(new ShaderFXParameterControl { type = c.type, order = c.order, tooltip = c.tooltip,
                    headers = c.headers == null ? Array.Empty<string>() : (string[])c.headers.Clone(),
                    hasMinimum = c.hasMinimum, hasMaximum = c.hasMaximum, softMinimum = c.softMinimum, softMaximum = c.softMaximum, minimum = c.minimum, maximum = c.maximum,
                    optionNames = (string[])c.optionNames.Clone(), optionValues = (float[])c.optionValues.Clone() });
            return copy;
        }

        internal void SetValue(Material material, ShaderFXParameter declaration, Vector2 dimensions)
        {
            string propertyName = declaration.name;
            switch (type)
            {
                case ShaderFXParameterType.Enum:
                case ShaderFXParameterType.Float: material.SetFloat(propertyName, controls.Count > 0 ? floatValue : Clamp(floatValue)); break;
                case ShaderFXParameterType.Bool: material.SetFloat(propertyName, BoolValue ? 1f : 0f); break;
                case ShaderFXParameterType.Color: HdrUtility.SetShaderColor(material, propertyName, colorValue); break;
                case ShaderFXParameterType.Vector2:
                case ShaderFXParameterType.Vector3:
                case ShaderFXParameterType.Vector: material.SetVector(propertyName, vectorValue); break;
                case ShaderFXParameterType.Normal: material.SetVector(propertyName, NormalizeNormal(vectorValue)); break;
                case ShaderFXParameterType.Texture2D:
                    material.SetTexture(propertyName, textureValue != null ? textureValue : Texture2D.whiteTexture);
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

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "ShaderFX")]
    [CreateAssetMenu(fileName = "New Shader FX", menuName = "WhimTex/Shader FX")]
    public sealed partial class ShaderFX : ScriptableObject, ISerializationCallbackReceiver
    {
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
            "float4 ApplyFX(float2 uv, float4 color)\n{\n    return color * _Tint;\n}\n";
        [SerializeField] private List<ShaderFXParameter> parameters = new List<ShaderFXParameter>();
        [SerializeField, HideInInspector] private Shader compiledShader;
        [SerializeField, HideInInspector] private string appliedCode;
        [SerializeField, HideInInspector] private string appliedSource;
        [SerializeField, HideInInspector] private List<ShaderFXParameter> appliedParameters = new List<ShaderFXParameter>();
        [SerializeField, HideInInspector] private string diagnostics = "Not applied yet. Click Apply to compile this effect.";
        [SerializeField, HideInInspector] private bool lastApplyFailed;
        [SerializeField, HideInInspector] private TextureCompositor embeddedOwner;
        [SerializeField, HideInInspector] private string documentIncludeBasePath;
        [SerializeField, HideInInspector] private string shaderKey = Guid.NewGuid().ToString("N");
        [SerializeField, HideInInspector] private bool shaderCreationRecorded;
        [NonSerialized] private Material material;
        [NonSerialized] private Shader materialSourceShader, upgradedTransformShader;
        [NonSerialized] private Dictionary<ShaderFXParameter, GradientBinding> gradientBindings;
        [NonSerialized] private string determinismWarningSource;
        [NonSerialized] private bool determinismWarningCached, determinismWarningFound;

        [NonSerialized] private Dictionary<ShaderFXParameter, WhimTexCurveTexture> curveBindings;

        private sealed class GradientBinding : IDisposable
        {
            internal readonly WhimTexGradientTexture lut = new WhimTexGradientTexture();
            internal readonly int propertyId;
            internal GradientBinding(ShaderFXParameter declaration) => propertyId = Shader.PropertyToID(declaration.InternalPrefix + "Gradient");
            public void Dispose() => lut.Dispose();
        }
        [NonSerialized] private bool notificationQueued;
        [NonSerialized] private volatile bool undoDeserialized;

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }
        void ISerializationCallbackReceiver.OnAfterDeserialize() => undoDeserialized = true;

        internal bool ConsumeUndoChanges()
        {
            if (!undoDeserialized)
                return false;
            undoDeserialized = false;
            EditorApplication.delayCall -= SendNotification;
            notificationQueued = false;
            ReleaseMaterial();
            MarkDraftChanged();
            return true;
        }

        internal string Code => code;
        internal IReadOnlyList<ShaderFXParameter> Parameters => parameters;
        internal string Diagnostics => diagnostics;
        internal bool LastApplyFailed => lastApplyFailed;
        internal bool HasAppliedShader => compiledShader != null;
        internal bool UsesUnsupportedTimeInputs
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
        internal TextureCompositor EmbeddedOwner => embeddedOwner;
        internal void RestoreDocumentOwner(TextureCompositor owner) => embeddedOwner = owner;
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
                return !string.IsNullOrEmpty(path) ? path : "Assets/Untitled.spritefx";
            }
        }

        internal static ShaderFX CreateEmbedded(TextureCompositor owner)
        {
            ShaderFX effect = CreateInstance<ShaderFX>();
            effect.name = "Shader FX";
            effect.embeddedOwner = owner;
            effect.hideFlags = HideFlags.HideAndDontSave;
            Undo.RegisterCreatedObjectUndo(effect, "Add Shader FX");
            return effect;
        }

        internal static ShaderFX CreateAgentDraft(TextureCompositor owner, string source, List<ShaderFXParameter> values)
        {
            var effect = CreateInstance<ShaderFX>();
            effect.name = "Shader FX";
            effect.embeddedOwner = owner;
            effect.hideFlags = HideFlags.HideAndDontSave;
            effect.code = source;
            effect.parameters = values;
            return effect;
        }

        internal void ApplyAgentDraft()
        {
            Shader candidate = null;
            Material test = null;
            try
            {
                PrepareParameterDeclarations();
                string source = ShaderFXSourceBuilder.Build(this, SourcePath);
                candidate = ShaderUtil.CreateShaderAsset(source, true);
                if (candidate == null) throw new InvalidOperationException("Unity could not create the shader.");
                candidate.hideFlags = HideFlags.HideAndDontSave;
                test = new Material(candidate) { hideFlags = HideFlags.HideAndDontSave };
                if (test.passCount > 0) ShaderUtil.CompilePass(test, 0, true);
                var messages = new StringBuilder();
                bool errors = false;
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(candidate))
                {
                    errors |= message.severity == ShaderCompilerMessageSeverity.Error;
                    messages.AppendLine($"{message.severity}: {message.file}:{message.line}: {message.message}");
                }
                string determinismWarning = ShaderFXSourceBuilder.GetDeterminismWarning(source);
                if (!string.IsNullOrEmpty(determinismWarning)) messages.AppendLine(determinismWarning);
                if (errors || !candidate.isSupported || test.passCount == 0)
                    throw new InvalidOperationException(messages.Length > 0 ? messages.ToString() : "Shader is unsupported on this graphics device.");
                compiledShader = candidate;
                candidate = null;
                appliedCode = code;
                appliedSource = source;
                appliedParameters = new List<ShaderFXParameter>();
                foreach (var parameter in parameters) appliedParameters.Add(parameter.Copy());
                diagnostics = messages.Length > 0 ? messages.ToString() : "Applied successfully.";
                lastApplyFailed = false;
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
            if (AssetDatabase.Contains(this) || HasAppliedShader) return;
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
                    if (!locked) DetachCatalog();
                    code = fallback;
                }
                ApplyAgentDraft();
            }
            catch (Exception error)
            {
                lastApplyFailed = true;
                diagnostics = error.Message;
                throw;
            }
        }

        internal void SuspendDocumentCatalogReload() => EditorApplication.delayCall -= ReloadCatalogAfterEnable;

        internal void PreserveDocumentIncludeBase()
        {
            if (!AssetDatabase.Contains(this) && string.IsNullOrEmpty(documentIncludeBasePath) &&
                ShaderFXSourceBuilder.HasRelativeIncludes(code)) documentIncludeBasePath = SourcePath;
        }

        internal ShaderFX CloneForDocument(TextureCompositor owner)
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

        internal void PersistEmbedded(TextureCompositor owner)
        {
            if (embeddedOwner != owner || !AssetDatabase.Contains(owner))
                return;
            hideFlags = HideFlags.HideInHierarchy;
            if (!AssetDatabase.Contains(this))
                AssetDatabase.AddObjectToAsset(this, owner);
            if (compiledShader != null && !AssetDatabase.Contains(compiledShader))
            {
                compiledShader.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                AssetDatabase.AddObjectToAsset(compiledShader, owner);
            }
            EditorUtility.SetDirty(this);
            if (compiledShader != null)
                EditorUtility.SetDirty(compiledShader);
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
            ReleaseMaterial();
        }

        private void ReloadCatalogAfterEnable() { if (this != null && !Undo.isProcessing) ReloadCatalogSource(); }

        private void OnDestroy()
        {
            if (shaderCreationRecorded && Undo.isProcessing)
                return;
            if (compiledShader != null && !AssetDatabase.Contains(compiledShader))
                DestroyImmediate(compiledShader);
        }

        internal void DestroyEmbeddedWithUndo(TextureCompositor owner)
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
            if (notificationQueued)
                return;
            notificationQueued = true;
            EditorApplication.delayCall += SendNotification;
        }

        private void SendNotification()
        {
            notificationQueued = false;
            if (this != null)
                TextureCompositor.NotifyShaderFXChanged(this);
        }

        internal Material GetMaterial(in LayerRenderContext context)
        {
            if (compiledShader == null)
                return null;
            if (material == null || materialSourceShader != compiledShader)
            {
                ReleaseMaterial();
                materialSourceShader = compiledShader;
                string upgraded = ShaderFXSourceBuilder.UpgradeTransformHelpers(appliedSource, appliedParameters);
                if (upgraded != appliedSource)
                {
                    upgradedTransformShader = ShaderUtil.CreateShaderAsset(upgraded, true);
                    if (upgradedTransformShader != null) upgradedTransformShader.hideFlags = HideFlags.HideAndDontSave;
                }
                material = new Material(upgradedTransformShader != null ? upgradedTransformShader : compiledShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            foreach (ShaderFXParameter applied in appliedParameters)
            {
                ShaderFXParameter value = applied;
                foreach (ShaderFXParameter draft in parameters)
                    if (draft != null && draft.name == applied.name && draft.type == applied.type)
                    {
                        value = draft;
                        break;
                    }
                if (applied.type == ShaderFXParameterType.Gradient)
                {
                    gradientBindings ??= new Dictionary<ShaderFXParameter, GradientBinding>();
                    if (!gradientBindings.TryGetValue(applied, out var binding))
                        gradientBindings.Add(applied, binding = new GradientBinding(applied));
                    value.gradientValue ??= new WhimTexGradient();
                    material.SetTexture(binding.propertyId, binding.lut.GetTexture(value.gradientValue));
                }
                else if (applied.type == ShaderFXParameterType.Curve)
                {
                    curveBindings ??= new Dictionary<ShaderFXParameter, WhimTexCurveTexture>();
                    if (!curveBindings.TryGetValue(applied, out var lut))
                        curveBindings.Add(applied, lut = new WhimTexCurveTexture());
                    value.curveValue ??= WhimTexCurveTexture.Default();
                    material.SetTexture(applied.InternalPrefix + "Curve", lut.GetTexture(value.curveValue));
                }
                else value.SetValue(material, applied, new Vector2(context.compositor.width, context.compositor.height));
            }
            material.SetVector("_InputSize", new Vector4(context.width, context.height, 1f / context.width, 1f / context.height));
            material.SetVector("_CanvasSize", new Vector4(context.compositor.width, context.compositor.height,
                1f / context.compositor.width, 1f / context.compositor.height));
            material.SetFloat("_PreviewScale", context.scaleMultiplier);
            return material;
        }

        internal bool Apply()
        {
            if (WhimTexApi.IsShaderFXContentLocked(this)) return false;
            Shader candidate = null;
            Material candidateMaterial = null;
            List<ShaderFXParameter> previousParameters = parameters;
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
                StringBuilder messages = new StringBuilder();
                bool errors = false;
                foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(candidate))
                {
                    errors |= message.severity == ShaderCompilerMessageSeverity.Error;
                    messages.AppendLine($"{message.severity}: {message.file}:{message.line}: {message.message}");
                }
                string determinismWarning = ShaderFXSourceBuilder.GetDeterminismWarning(source);
                if (!string.IsNullOrEmpty(determinismWarning)) messages.AppendLine(determinismWarning);
                if (errors || !candidate.isSupported || candidateMaterial.passCount == 0)
                    throw new InvalidOperationException(messages.Length > 0 ? messages.ToString() : "The shader is not supported on this graphics device.");

                List<ShaderFXParameter> snapshot = new List<ShaderFXParameter>(parameters.Count);
                foreach (ShaderFXParameter parameter in parameters)
                    snapshot.Add(parameter.Copy());
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
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
                lastApplyFailed = false;
                diagnostics = messages.Length > 0 ? messages.ToString() : "Applied successfully.";
                ReleaseMaterial();
                EditorUtility.SetDirty(compiledShader);
                EditorUtility.SetDirty(this);
                if (embeddedOwner != null)
                {
                    PersistEmbedded(embeddedOwner);
                    EditorUtility.SetDirty(embeddedOwner);
                }
                else if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.SetMainObject(this, path);
                    AssetDatabase.SaveAssetIfDirty(this);
                }
                NotifyValuesChanged();
                return true;
            }
            catch (Exception exception)
            {
                parameters = previousParameters;
                lastApplyFailed = true;
                diagnostics = exception.Message;
                EditorUtility.SetDirty(this);
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
            if (curveBindings != null)
            {
                foreach (var lut in curveBindings.Values) lut.Dispose();
                curveBindings.Clear();
            }
            if (gradientBindings != null)
            {
                foreach (var binding in gradientBindings.Values) binding.Dispose();
                gradientBindings.Clear();
            }
            if (material != null)
                DestroyImmediate(material);
            material = null;
            materialSourceShader = null;
            if (upgradedTransformShader != null) DestroyImmediate(upgradedTransformShader);
            upgradedTransformShader = null;
        }
    }
}
