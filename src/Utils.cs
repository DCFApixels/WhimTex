using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public enum BlendMode
    {
        Normal = 0,
        Multiply = 1,
        Overwrite = 2,
        None = 3,
        Add = 4,
        Subtract = 5,
        Divide = 6,
        Screen = 7,
        Overlay = 8,
        Darken = 9,
        Lighten = 10,
        [InspectorName("Color Dodge")] Dodge = 11,
        [InspectorName("Color Burn")] Burn = 12,
        [InspectorName("Linear Dodge (Add)")] LinearDodge = 13,
        [InspectorName("Linear Burn")] LinearBurn = 14,
        [InspectorName("Linear Light")] LinearLight = 15,
        [InspectorName("Linear Light Add/Sub")] LinearLightAddSub = 16,
        [InspectorName("Vivid Light")] VividLight = 17,
        [InspectorName("Pin Light")] PinLight = 18,
        [InspectorName("Hard Mix")] HardMix = 19,
        [InspectorName("Hard Light")] HardLight = 20,
        [InspectorName("Soft Light")] SoftLight = 21,
        Difference = 22,
        Exclusion = 23,
        Negation = 24,
        Hue = 25,
        Saturation = 26,
        Color = 27,
        Luminosity = 28
    }

    public enum DistanceMetric
    {
        EuclideanExact = 0,
        EuclideanApproximate = 1,
        Manhattan = 2,
        Chebyshev = 3,
        EuclideanAntialiased = 4
    }

    public enum EffectInputMode
    {
        Previous = 0,
        Specific = 1,
        [InspectorName("All Below")] AllBelow = 2
    }

    public enum PaintToolMode
    {
        Brush = 0,
        Eraser = 1
    }

    public enum FillSampleMode
    {
        CurrentLayer = 0,
        AllLayers = 1
    }

    public enum BlurBrushSampleMode
    {
        CurrentLayer = 0,
        BelowLayers = 1,
        AllLayers = 2
    }

    public enum PaintRepeatMode
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2,
        Grid = 3,
        Radial = 4,
        Mirror = 5
    }

    public enum PaintRepeatElementMode
    {
        Copy = 0,
        AlternateMirror = 1
    }

    public enum PaintRepeatBoundaryMode
    {
        Continue = 0,
        Clip = 1
    }

    public enum TransformTilingMode
    {
        Source = 3,
        Clip = 0,
        Repeat = 1,
        Mirror = 2,
        Clamp = 4,
        Unbounded = 5
    }

    public enum LayerFilterMode
    {
        Source = 0,
        Point = 1,
        Bilinear = 2,
        Trilinear = 3
    }

    [Serializable]
    public partial struct TextureTransform : IEquatable<TextureTransform>
    {
        public static readonly TextureTransform Default = new TextureTransform
        {
            pivot = new Vector2(0.5f, 0.5f),
            position = Vector2.zero,
            scale = Vector2.one,
            rotation = 0f,
            tiling = TransformTilingMode.Clip
        };

        public Double2 pivot, position, scale;
        public double rotation;
        public Vector2 pivotF { get => pivot; set => pivot = value; }
        public Vector2 positionF { get => position; set => position = value; }
        public Vector2 scaleF { get => scale; set => scale = value; }
        public float rotationF { get => (float)rotation; set => rotation = value; }
        public TransformStorage storage;
        public ProjectiveMatrix matrix;
        public TransformTilingMode tiling;

        public bool Equals(TextureTransform other) => pivot==other.pivot && position==other.position && scale==other.scale &&
            rotation==other.rotation && storage==other.storage && matrix.Equals(other.matrix) && tiling==other.tiling;
        public override bool Equals(object other) => other is TextureTransform value && Equals(value);
        public override int GetHashCode() => position.GetHashCode() ^ scale.GetHashCode()*397 ^ matrix.GetHashCode();

        public bool IsIdentity()
        {
            return storage == TransformStorage.TRS && position.x == 0 && position.y == 0 && scale.x == 1 && scale.y == 1 && rotation == 0 &&
                tiling == TransformTilingMode.Clip;
        }

        public void Reset()
        {
            this = Default;
        }

        internal bool TryFitOriginalAspect(Vector2 canvasSize, Vector2 sourceSize, out TextureTransform fitted, bool originalSize = false)
        {
            fitted = this;
            if (storage == TransformStorage.Projective || !TiledCanvasUtility.IsInvertible(this) ||
                !Finite(canvasSize) || !Finite(sourceSize) || canvasSize.x<=0 || canvasSize.y<=0 || sourceSize.x<=0 || sourceSize.y<=0) return false;
            double fit=originalSize ? 1d : Math.Min(Math.Abs(scale.x)*canvasSize.x/sourceSize.x,Math.Abs(scale.y)*canvasSize.y/sourceSize.y);
            var nextScale=new Double2(sourceSize.x*fit/canvasSize.x*Math.Sign(scale.x),sourceSize.y*fit/canvasSize.y*Math.Sign(scale.y));
            if (Math.Abs(nextScale.x-scale.x) <= 1e-12*Math.Max(1d,Math.Abs(scale.x))) nextScale.x=scale.x;
            if (Math.Abs(nextScale.y-scale.y) <= 1e-12*Math.Max(1d,Math.Abs(scale.y))) nextScale.y=scale.y;
            var offset=new Double2((.5-pivot.x)*canvasSize.x,(.5-pivot.y)*canvasSize.y);
            var delta=new Double2(offset.x*(scale.x-nextScale.x),offset.y*(scale.y-nextScale.y));
            fitted.scale=nextScale;
            fitted.position=position+ProjectiveMatrix.Rotate(rotation).Point(delta);
            return TiledCanvasUtility.IsInvertible(fitted);
        }

        private static bool Finite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }

    public static class GradientUtility
    {
        private static readonly GradientAlphaKey[] OpaqueAlphaKeys =
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 1f)
        };

        public static readonly WhimTexGradient WhiteToBlack = Create(new[]
        {
            new GradientColorKey(Color.white, 0f),
            new GradientColorKey(Color.black, 1f)
        });

        public static WhimTexGradient Create(WhimTexGradient gradient)
        {
            return gradient?.Clone() ?? new WhimTexGradient();
        }
        public static WhimTexGradient CreateLinearWhiteToBlack()
        {
            var result = WhiteToBlack.Clone();
            result.Mode = WhimTexGradientMode.Linear;
            return result;
        }

        public static WhimTexGradient Create(GradientColorKey[] colorKeys)
        {
            return Create(colorKeys, OpaqueAlphaKeys);
        }

        public static WhimTexGradient Create(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
        {
            WhimTexGradient result = new WhimTexGradient();
            result.SetKeys(colorKeys, alphaKeys);
            return result;
        }

        public static bool IsTwoColorGradient(WhimTexGradient gradient, out Color left, out Color right)
        {
            left = default;
            right = default;
            if (gradient == null || gradient.Mode != WhimTexGradientMode.Classic || gradient.ColorSpace != ColorSpace.Gamma ||
                gradient.WrapMode != WhimTexGradientWrapMode.Clamp || gradient.Smoothness != 1f ||
                gradient.GetMidpoint(false, 0) != .5f || gradient.GetMidpoint(true, 0) != .5f)
                return false;

            GradientColorKey[] colors = gradient.ColorKeys;
            GradientAlphaKey[] alphas = gradient.AlphaKeys;
            if (colors.Length == 0 || alphas.Length == 0 || colors.Length > 2 || alphas.Length > 2)
                return false;

            if (colors.Length == 1)
            {
                left = right = colors[0].color;
            }
            else if (Mathf.Approximately(colors[0].time, 0f) && Mathf.Approximately(colors[1].time, 1f))
            {
                left = colors[0].color;
                right = colors[1].color;
            }
            else
            {
                return false;
            }

            if (alphas.Length == 1)
            {
                left.a = right.a = alphas[0].alpha;
            }
            else if (Mathf.Approximately(alphas[0].time, 0f) && Mathf.Approximately(alphas[1].time, 1f))
            {
                left.a = alphas[0].alpha;
                right.a = alphas[1].alpha;
            }
            else
            {
                return false;
            }

            return true;
        }

        public static int ComputeHash(WhimTexGradient gradient)
        {
            return gradient?.GetHashCode() ?? 0;
        }

    }

    [InitializeOnLoad]
    internal static class WhimTexMaterials
    {
        private static Material blendMaterial;
        private static Material transformMaterial;
        private static Material paintBrushMaterial;
        private static Material alphaConversionMaterial;
        private static Material paintWriteProtectionMaterial;
        private static Material displayChannelsMaterial;
        private static Material hdrMaterial;
        private static Material normalMapMaterial;
        private static Material gaussianBlurMaterial;
        private static Material blurBrushMaterial;
        private static Material smudgeBrushMaterial;
        private static Material smudgeTransportMaterial;
        private static Material healingBrushMaterial;
        private static Material sharpenMaterial;
        private static Material motionBlurMaterial;
        private static Material makeSeamlessMaterial;
        private static Material gpuFourierTransformMaterial;
        private static Material screenedSeamlessMaterial;
        private static Material histogramSeamlessMaterial;
        private static Material patchQuiltingMaterial;
        private static Material noiseMaterial;
        private static Material gradientMaterial;
        private static Material fillUvMaterial;
        private static Material fillPatternMaterial;
        private static Material shapeMaterial;
        private static Material textMaterial;
        private static Material effectCacheMaterial;

        static WhimTexMaterials()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        public static Material Blend => GetOrCreate(ref blendMaterial, "Hidden/WhimTex/Blend");
        public static Material Hdr => GetOrCreate(ref hdrMaterial, "Hidden/WhimTex/Hdr");
        public static Material NormalMap => GetOrCreate(ref normalMapMaterial, "Hidden/WhimTex/NormalMap");
        public static Material GaussianBlur => GetOrCreate(ref gaussianBlurMaterial, "Hidden/WhimTex/GaussianBlur");
        public static Material BlurBrush => GetOrCreate(ref blurBrushMaterial, "Hidden/WhimTex/BlurBrush");
        public static Material SmudgeBrush => GetOrCreate(ref smudgeBrushMaterial, "Hidden/WhimTex/SmudgeBrush");
        public static Material SmudgeTransport => GetOrCreate(ref smudgeTransportMaterial, "Hidden/WhimTex/SmudgeTransport");
        public static Material HealingBrush => GetOrCreate(ref healingBrushMaterial, "Hidden/WhimTex/HealingBrush");
        public static Material Sharpen => GetOrCreate(ref sharpenMaterial, "Hidden/WhimTex/Sharpen");
        public static Material MotionBlur => GetOrCreate(ref motionBlurMaterial, "Hidden/WhimTex/MotionBlur");
        public static Material MakeSeamless => GetOrCreate(ref makeSeamlessMaterial, "Hidden/WhimTex/MakeSeamless");
        public static Material GpuFourierTransform => GetOrCreate(ref gpuFourierTransformMaterial, "Hidden/WhimTex/GpuFourierTransform");
        public static Material ScreenedSeamless => GetOrCreate(ref screenedSeamlessMaterial, "Hidden/WhimTex/ScreenedSeamless");
        public static Material HistogramSeamless => GetOrCreate(ref histogramSeamlessMaterial, "Hidden/WhimTex/HistogramSeamless");
        public static Material PatchQuilting => GetOrCreate(ref patchQuiltingMaterial, "Hidden/WhimTex/PatchQuilting");
        public static Material Noise => GetOrCreate(ref noiseMaterial, "Hidden/WhimTex/Noise");
        public static Material Gradient => GetOrCreate(ref gradientMaterial, "Hidden/WhimTex/Gradient");
        public static Material FillUv => GetOrCreate(ref fillUvMaterial, "Hidden/WhimTex/FillUv");
        public static Material FillPattern => GetOrCreate(ref fillPatternMaterial, "Hidden/WhimTex/FillPattern");
        public static Material Shape => GetOrCreate(ref shapeMaterial, "Hidden/WhimTex/Shape");
        public static Material Text => GetOrCreate(ref textMaterial, "Hidden/WhimTex/Text");
        public static Material EffectCache => GetOrCreate(ref effectCacheMaterial, "Hidden/WhimTex/EffectCache");
        public static Material Transform => GetOrCreate(ref transformMaterial, "Hidden/WhimTex/Transform");
        public static Material PaintBrush => GetOrCreate(ref paintBrushMaterial, "Hidden/WhimTex/PaintBrush");
        public static Material PaintWriteProtection => GetOrCreate(ref paintWriteProtectionMaterial, "Hidden/WhimTex/PaintWriteProtection");
        public static Material DisplayChannels => GetOrCreate(ref displayChannelsMaterial, "Hidden/WhimTex/DisplayChannels");
        public static Material AlphaConversion => GetOrCreate(
            ref alphaConversionMaterial,
            "Hidden/WhimTex/AlphaConversion");

        private static Material GetOrCreate(ref Material material, string shaderName)
        {
            if (material != null)
                return material;

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"WhimTex shader '{shaderName}' was not found.");
                return null;
            }

            material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return material;
        }

        private static void Dispose()
        {
            if (paintWriteProtectionMaterial != null) UnityEngine.Object.DestroyImmediate(paintWriteProtectionMaterial);
            paintWriteProtectionMaterial = null;
            if (smudgeTransportMaterial != null) UnityEngine.Object.DestroyImmediate(smudgeTransportMaterial);
            smudgeTransportMaterial = null;
            if (smudgeBrushMaterial != null) UnityEngine.Object.DestroyImmediate(smudgeBrushMaterial);
            smudgeBrushMaterial = null;
            if (healingBrushMaterial != null) UnityEngine.Object.DestroyImmediate(healingBrushMaterial);
            healingBrushMaterial = null;
            if (fillUvMaterial != null) UnityEngine.Object.DestroyImmediate(fillUvMaterial);
            fillUvMaterial = null;
            if (fillPatternMaterial != null) UnityEngine.Object.DestroyImmediate(fillPatternMaterial);
            fillPatternMaterial = null;
            if (gradientMaterial != null) UnityEngine.Object.DestroyImmediate(gradientMaterial);
            gradientMaterial = null;
            if (shapeMaterial != null) UnityEngine.Object.DestroyImmediate(shapeMaterial);
            shapeMaterial = null;
            if (textMaterial != null) UnityEngine.Object.DestroyImmediate(textMaterial);
            textMaterial = null;
            if (noiseMaterial != null) UnityEngine.Object.DestroyImmediate(noiseMaterial);
            noiseMaterial = null;
            if (gaussianBlurMaterial != null) UnityEngine.Object.DestroyImmediate(gaussianBlurMaterial);
            gaussianBlurMaterial = null;
            if (sharpenMaterial != null) UnityEngine.Object.DestroyImmediate(sharpenMaterial);
            sharpenMaterial = null;
            if (motionBlurMaterial != null) UnityEngine.Object.DestroyImmediate(motionBlurMaterial);
            motionBlurMaterial = null;
            if (makeSeamlessMaterial != null) UnityEngine.Object.DestroyImmediate(makeSeamlessMaterial);
            makeSeamlessMaterial = null;
            if (gpuFourierTransformMaterial != null) UnityEngine.Object.DestroyImmediate(gpuFourierTransformMaterial);
            gpuFourierTransformMaterial = null;
            if (screenedSeamlessMaterial != null) UnityEngine.Object.DestroyImmediate(screenedSeamlessMaterial);
            screenedSeamlessMaterial = null;
            if (histogramSeamlessMaterial != null) UnityEngine.Object.DestroyImmediate(histogramSeamlessMaterial);
            histogramSeamlessMaterial = null;
            if (patchQuiltingMaterial != null) UnityEngine.Object.DestroyImmediate(patchQuiltingMaterial);
            patchQuiltingMaterial = null;
            if (effectCacheMaterial != null) UnityEngine.Object.DestroyImmediate(effectCacheMaterial);
            effectCacheMaterial = null;
            if (normalMapMaterial != null) UnityEngine.Object.DestroyImmediate(normalMapMaterial);
            normalMapMaterial = null;
            if (hdrMaterial != null) UnityEngine.Object.DestroyImmediate(hdrMaterial);
            hdrMaterial = null;
            if (blendMaterial != null)
                UnityEngine.Object.DestroyImmediate(blendMaterial);
            if (transformMaterial != null)
                UnityEngine.Object.DestroyImmediate(transformMaterial);
            if (paintBrushMaterial != null)
                UnityEngine.Object.DestroyImmediate(paintBrushMaterial);
            if (alphaConversionMaterial != null)
                UnityEngine.Object.DestroyImmediate(alphaConversionMaterial);
            if (displayChannelsMaterial != null)
                UnityEngine.Object.DestroyImmediate(displayChannelsMaterial);
            blendMaterial = null;
            transformMaterial = null;
            paintBrushMaterial = null;
            alphaConversionMaterial = null;
            displayChannelsMaterial = null;
        }
    }

    public abstract class LayerEditorWindowBase : EditorWindow
    {
        [SerializeField] private WhimTexDocument activeDocument;
        [SerializeField] private string layerId;
        [SerializeField] private bool transformSettingsExpanded;
        [SerializeField] private bool renderingSettingsExpanded;
        [SerializeField] private bool propertiesExpanded = true;
        [SerializeField] private bool fxExpanded;
        [SerializeField] private LayerPreviewPanel.ViewState layerPreviewState = new LayerPreviewPanel.ViewState();

        [NonSerialized] private Layer currentLayer;
        [NonSerialized] private LayerPreviewPanel layerPreview;
        [NonSerialized] private EffectTargetSettingsView effectTargetSettings;
        [NonSerialized] private LayerShaderFXView shaderFXView;
        [NonSerialized] private bool applyingChange;
        [NonSerialized] private bool interfaceBuilt;
        [NonSerialized] private bool interfaceRefreshRequested;
        [NonSerialized] private Layer boundLayer;
        [NonSerialized] private LayerBehaviour boundBehaviour;
        [NonSerialized] private WhimTexDocument boundDocument;
        internal readonly WhimTexUI.ValueBindings SettingsBindings = new WhimTexUI.ValueBindings();

        protected Layer CurrentLayer => currentLayer;
        protected WhimTexDocument Document => activeDocument;
        protected virtual string LayerPreviewTitle => "Layer Preview";
        protected virtual bool ImmediateLayerPreviewUpdates => false;
        protected abstract Type EditedLayerType { get; }

        protected static void OpenPropertiesWindow<T>(Layer layer, WhimTexDocument owner)
            where T : LayerEditorWindowBase
        {
            T window = CreateInstance<T>();
            window.titleContent = WhimTexBranding.WindowTitle($"Properties — {layer.layerName}");
            window.Initialize(layer, owner);
            window.ShowUtility();
        }

        protected void Initialize(Layer layer, WhimTexDocument owner)
        {
            currentLayer = layer;
            activeDocument = owner;
            layerId = layer?.Id;
            minSize = new Vector2(320f, 430f);
            InvalidateEffectTargetOptions();
            if (rootVisualElement != null && rootVisualElement.panel != null)
                RefreshInterface();
            RequestLayerPreview(true);
        }

        protected virtual void OnEnable()
        {
            titleContent = WhimTexBranding.WindowTitle(titleContent.text);
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnDocumentChanged;
            WhimTexApi.LiveEditLocksChanged += RefreshAgentLock;
            RequestLayerPreview(true);
        }

        protected virtual void OnDisable()
        {
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnDocumentChanged;
            WhimTexApi.LiveEditLocksChanged -= RefreshAgentLock;
            layerPreview?.Dispose();
            layerPreview = null;
        }

        protected virtual void Update()
        {
            RefreshAgentLock();
            if (interfaceRefreshRequested)
                RefreshInterface();
        }

        public void CreateGUI()
        {
            interfaceBuilt = false;
            RefreshInterface();
        }

        protected abstract void BuildSettings(VisualElement root, Layer layer);

        private void RefreshAgentLock() => rootVisualElement.SetEnabled(!WhimTexApi.IsLayerContentLocked(activeDocument, currentLayer));

        protected void ApplyLayerChange(string undoName, Action change)
        {
            if (activeDocument == null || change == null || WhimTexApi.IsLayerContentLocked(activeDocument, currentLayer))
                return;
            if (!ResolveLayer() || !ReferenceEquals(boundLayer, currentLayer) || !ReferenceEquals(boundBehaviour, boundLayer?.Behaviour))
            {
                RefreshInterface();
                return;
            }

            Undo.RecordObject(activeDocument, undoName);
            applyingChange = true;
            try
            {
                change();
                activeDocument.NormalizeModel();
                activeDocument.MarkChanged();
                InvalidateEffectTargetOptions();
            }
            finally
            {
                applyingChange = false;
            }
            SettingsBindings.Refresh();
            RequestLayerPreview();
        }

        protected void AddEffectTarget(VisualElement root, TargetedLayerBehaviour effect)
        {
            effectTargetSettings = new EffectTargetSettingsView(activeDocument, ApplyLayerChange, SettingsBindings);
            effectTargetSettings.Build(root, effect);
        }

        protected void RequestLayerPreview(bool immediate = false)
        {
            layerPreview?.RequestLayerPreview(immediate || ImmediateLayerPreviewUpdates);
        }

        protected void RefreshInterface(bool forceValues = false)
        {
            interfaceRefreshRequested = false;
            bool valid = ResolveLayer();
            if (valid)
            {
                string title = $"Properties — {currentLayer.layerName}";
                if (titleContent.text != title)
                    titleContent = WhimTexBranding.WindowTitle(title);
            }
            Layer nextLayer = valid ? currentLayer : null;
            if (interfaceBuilt && ReferenceEquals(boundLayer, nextLayer) && ReferenceEquals(boundBehaviour, nextLayer?.Behaviour) && boundDocument == activeDocument)
            {
                shaderFXView?.Refresh();
                SettingsBindings.Refresh(forceValues);
                return;
            }
            interfaceBuilt = true;
            boundLayer = nextLayer;
            boundBehaviour = nextLayer?.Behaviour;
            boundDocument = activeDocument;
            SettingsBindings.Clear();
            shaderFXView = null;
            layerPreview?.Dispose();
            layerPreview = null;
            InvalidateEffectTargetOptions();
            VisualElement root = rootVisualElement;
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-properties-window");
            root.EnableInClassList("whimtex-properties-window--light", !EditorGUIUtility.isProSkin);

            if (!valid)
            {
                WhimTexUI.AddHelpBox(
                    root,
                    "The edited layer no longer exists in this document.",
                    HelpBoxMessageType.Info);
                return;
            }

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("whimtex-properties-scroll");
            shaderFXView = WhimTexUI.BuildLayerInspectorSections(scroll, currentLayer, activeDocument,
                ApplyLayerChange, SettingsBindings, properties => BuildSettings(properties, currentLayer),
                renderingSettingsExpanded, value => renderingSettingsExpanded = value,
                propertiesExpanded, value => propertiesExpanded = value,
                fxExpanded, value => fxExpanded = value,
                transformSettingsExpanded, value => transformSettingsExpanded = value);
            SettingsBindings.Refresh(forceValues);
            root.Add(scroll);
            layerPreviewState ??= new LayerPreviewPanel.ViewState();
            layerPreview = new LayerPreviewPanel(layerPreviewState) { tooltip = LayerPreviewTitle };
            layerPreview.Bind(activeDocument, currentLayer);
            root.Add(layerPreview);
        }

        private bool ResolveLayer()
        {
            if (activeDocument == null || string.IsNullOrEmpty(layerId))
                return false;

            currentLayer = activeDocument.FindLayer(layerId);

            return currentLayer != null && EditedLayerType.IsInstanceOfType(currentLayer.Behaviour);
        }

        private void InvalidateEffectTargetOptions()
        {
            effectTargetSettings?.Invalidate();
        }

        private void OnDocumentChanged(WhimTexDocument changedDocument)
        {
            if (changedDocument != activeDocument || applyingChange)
                return;
            if (WhimTexDocument.IsRefreshingUndo)
            {
                OnUndoRedo();
                return;
            }
            InvalidateEffectTargetOptions();
            interfaceRefreshRequested = true;
            RequestLayerPreview();
        }

        private void OnUndoRedo()
        {
            InvalidateEffectTargetOptions();
            RefreshInterface(forceValues: true);
            RequestLayerPreview(true);
        }

    }
}
