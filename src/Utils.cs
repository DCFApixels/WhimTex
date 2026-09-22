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
        Negation = 24
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
        Specific = 1
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
        BelowLayers = 1
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
        private static Material previewChannelsMaterial;
        private static Material hdrMaterial;
        private static Material normalMapMaterial;
        private static Material gaussianBlurMaterial;
        private static Material blurBrushMaterial;
        private static Material sharpenMaterial;
        private static Material motionBlurMaterial;
        private static Material makeSeamlessMaterial;
        private static Material noiseMaterial;
        private static Material gradientMaterial;
        private static Material fillUvMaterial;
        private static Material shapeMaterial;
        private static Material effectCacheMaterial;

        static WhimTexMaterials()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        public static Material Blend => GetOrCreate(ref blendMaterial, "Hidden/TextureCompositor/Blend");
        public static Material Hdr => GetOrCreate(ref hdrMaterial, "Hidden/TextureCompositor/Hdr");
        public static Material NormalMap => GetOrCreate(ref normalMapMaterial, "Hidden/TextureCompositor/NormalMap");
        public static Material GaussianBlur => GetOrCreate(ref gaussianBlurMaterial, "Hidden/TextureCompositor/GaussianBlur");
        public static Material BlurBrush => GetOrCreate(ref blurBrushMaterial, "Hidden/TextureCompositor/BlurBrush");
        public static Material Sharpen => GetOrCreate(ref sharpenMaterial, "Hidden/TextureCompositor/Sharpen");
        public static Material MotionBlur => GetOrCreate(ref motionBlurMaterial, "Hidden/TextureCompositor/MotionBlur");
        public static Material MakeSeamless => GetOrCreate(ref makeSeamlessMaterial, "Hidden/TextureCompositor/MakeSeamless");
        public static Material Noise => GetOrCreate(ref noiseMaterial, "Hidden/TextureCompositor/Noise");
        public static Material Gradient => GetOrCreate(ref gradientMaterial, "Hidden/TextureCompositor/Gradient");
        public static Material FillUv => GetOrCreate(ref fillUvMaterial, "Hidden/TextureCompositor/FillUv");
        public static Material Shape => GetOrCreate(ref shapeMaterial, "Hidden/TextureCompositor/Shape");
        public static Material EffectCache => GetOrCreate(ref effectCacheMaterial, "Hidden/TextureCompositor/EffectCache");
        public static Material Transform => GetOrCreate(ref transformMaterial, "Hidden/TextureCompositor/Transform");
        public static Material PaintBrush => GetOrCreate(ref paintBrushMaterial, "Hidden/TextureCompositor/PaintBrush");
        public static Material PreviewChannels => GetOrCreate(ref previewChannelsMaterial, "Hidden/TextureCompositor/PreviewChannels");
        public static Material AlphaConversion => GetOrCreate(
            ref alphaConversionMaterial,
            "Hidden/TextureCompositor/AlphaConversion");

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
            if (fillUvMaterial != null) UnityEngine.Object.DestroyImmediate(fillUvMaterial);
            fillUvMaterial = null;
            if (gradientMaterial != null) UnityEngine.Object.DestroyImmediate(gradientMaterial);
            gradientMaterial = null;
            if (shapeMaterial != null) UnityEngine.Object.DestroyImmediate(shapeMaterial);
            shapeMaterial = null;
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
            if (previewChannelsMaterial != null)
                UnityEngine.Object.DestroyImmediate(previewChannelsMaterial);
            blendMaterial = null;
            transformMaterial = null;
            paintBrushMaterial = null;
            alphaConversionMaterial = null;
            previewChannelsMaterial = null;
        }
    }

    public abstract class LayerEditorWindowBase : EditorWindow
    {
        private const int PreviewMaxSize = 256;
        private const double PreviewDelay = 0.12d;

        [SerializeField] private TextureCompositor compositor;
        [SerializeField] private string layerId;
        [SerializeField] private bool transformSettingsExpanded;
        [SerializeField] private bool colorSettingsExpanded;
        [SerializeField] private bool propertiesExpanded = true;
        [SerializeField] private bool fxExpanded;

        [NonSerialized] private Layer currentLayer;
        [NonSerialized] private RenderTexture previewTexture;
        [NonSerialized] private bool previewRequested;
        [NonSerialized] private double previewAt;
        [NonSerialized] private EffectTargetSettingsView effectTargetSettings;
        [NonSerialized] private LayerShaderFXView shaderFXView;
        [NonSerialized] private Image previewImage;
        [NonSerialized] private Label previewPlaceholder;
        [NonSerialized] private bool applyingChange;
        [NonSerialized] private bool interfaceBuilt;
        [NonSerialized] private bool interfaceRefreshRequested;
        [NonSerialized] private Layer boundLayer;
        [NonSerialized] private LayerBehaviour boundBehaviour;
        [NonSerialized] private TextureCompositor boundCompositor;
        internal readonly WhimTexUI.ValueBindings SettingsBindings = new WhimTexUI.ValueBindings();

        protected Layer CurrentLayer => currentLayer;
        protected TextureCompositor Compositor => compositor;
        protected virtual string PreviewTitle => "Preview";
        protected virtual bool ImmediatePreviewUpdates => false;
        protected abstract Type EditedLayerType { get; }

        protected static void OpenPropertiesWindow<T>(Layer layer, TextureCompositor owner)
            where T : LayerEditorWindowBase
        {
            T window = CreateInstance<T>();
            window.titleContent = WhimTexBranding.WindowTitle($"Properties — {layer.layerName}");
            window.Initialize(layer, owner);
            window.ShowUtility();
        }

        protected void Initialize(Layer layer, TextureCompositor owner)
        {
            currentLayer = layer;
            compositor = owner;
            layerId = layer?.Id;
            minSize = new Vector2(320f, 430f);
            InvalidateEffectTargetOptions();
            if (rootVisualElement != null && rootVisualElement.panel != null)
                RefreshInterface();
            RequestPreview(true);
        }

        protected virtual void OnEnable()
        {
            titleContent = WhimTexBranding.WindowTitle(titleContent.text);
            TextureCompositor.Changed += OnCompositorChanged;
            WhimTexApi.LiveEditLocksChanged += RefreshAgentLock;
            RequestPreview(true);
        }

        protected virtual void OnDisable()
        {
            TextureCompositor.Changed -= OnCompositorChanged;
            WhimTexApi.LiveEditLocksChanged -= RefreshAgentLock;
            ReleasePreview();
        }

        protected virtual void Update()
        {
            RefreshAgentLock();
            if (interfaceRefreshRequested)
                RefreshInterface();
            if (!previewRequested || EditorApplication.timeSinceStartup < previewAt)
                return;

            previewRequested = false;
            UpdatePreview();
        }

        public void CreateGUI()
        {
            interfaceBuilt = false;
            RefreshInterface();
        }

        protected abstract void BuildSettings(VisualElement root, Layer layer);

        private void RefreshAgentLock() => rootVisualElement.SetEnabled(!WhimTexApi.IsLayerContentLocked(compositor, currentLayer));

        protected void ApplyLayerChange(string undoName, Action change)
        {
            if (compositor == null || change == null || WhimTexApi.IsLayerContentLocked(compositor, currentLayer))
                return;
            if (!ResolveLayer() || !ReferenceEquals(boundLayer, currentLayer) || !ReferenceEquals(boundBehaviour, boundLayer?.Behaviour))
            {
                RefreshInterface();
                return;
            }

            Undo.RecordObject(compositor, undoName);
            applyingChange = true;
            try
            {
                change();
                compositor.NormalizeModel();
                compositor.MarkChanged();
                InvalidateEffectTargetOptions();
            }
            finally
            {
                applyingChange = false;
            }
            SettingsBindings.Refresh();
            RequestPreview();
        }

        protected void AddEffectTarget(VisualElement root, TargetedLayerBehaviour effect)
        {
            effectTargetSettings = new EffectTargetSettingsView(compositor, ApplyLayerChange, SettingsBindings);
            effectTargetSettings.Build(root, effect);
        }

        protected void RequestPreview(bool immediate = false)
        {
            immediate |= ImmediatePreviewUpdates;
            previewRequested = true;
            previewAt = EditorApplication.timeSinceStartup + (immediate ? 0d : PreviewDelay);
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
            if (interfaceBuilt && ReferenceEquals(boundLayer, nextLayer) && ReferenceEquals(boundBehaviour, nextLayer?.Behaviour) && boundCompositor == compositor)
            {
                shaderFXView?.Refresh();
                SettingsBindings.Refresh(forceValues);
                return;
            }
            interfaceBuilt = true;
            boundLayer = nextLayer;
            boundBehaviour = nextLayer?.Behaviour;
            boundCompositor = compositor;
            SettingsBindings.Clear();
            shaderFXView = null;
            previewImage = null;
            previewPlaceholder = null;
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
                    "The edited layer no longer exists in this compositor.",
                    HelpBoxMessageType.Info);
                root.Add(WhimTexUI.CreateButton("Close", Close));
                return;
            }

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            shaderFXView = WhimTexUI.BuildLayerInspectorSections(scroll, currentLayer, compositor,
                ApplyLayerChange, SettingsBindings, properties => BuildSettings(properties, currentLayer),
                colorSettingsExpanded, value => colorSettingsExpanded = value,
                propertiesExpanded, value => propertiesExpanded = value,
                fxExpanded, value => fxExpanded = value,
                transformSettingsExpanded, value => transformSettingsExpanded = value);
            SettingsBindings.Refresh(forceValues);
            scroll.Add(WhimTexUI.CreateHeading(PreviewTitle));

            VisualElement preview = new VisualElement();
            preview.style.height = PreviewMaxSize;
            preview.style.minHeight = 96f;
            preview.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            preview.style.borderTopWidth = 1f;
            preview.style.borderRightWidth = 1f;
            preview.style.borderBottomWidth = 1f;
            preview.style.borderLeftWidth = 1f;
            preview.style.borderTopColor = new Color(0f, 0f, 0f, 0.4f);
            preview.style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);
            preview.style.borderBottomColor = new Color(0f, 0f, 0f, 0.4f);
            preview.style.borderLeftColor = new Color(0f, 0f, 0f, 0.4f);

            previewImage = new Image
            {
                image = previewTexture,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            previewImage.style.position = Position.Absolute;
            previewImage.style.left = 0f;
            previewImage.style.right = 0f;
            previewImage.style.top = 0f;
            previewImage.style.bottom = 0f;
            preview.Add(previewImage);

            previewPlaceholder = new Label(previewTexture == null ? "Rendering preview…" : string.Empty);
            previewPlaceholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            previewPlaceholder.style.position = Position.Absolute;
            previewPlaceholder.style.left = 0f;
            previewPlaceholder.style.right = 0f;
            previewPlaceholder.style.top = 0f;
            previewPlaceholder.style.bottom = 0f;
            previewPlaceholder.pickingMode = PickingMode.Ignore;
            preview.Add(previewPlaceholder);
            scroll.Add(preview);

            Button close = WhimTexUI.CreateButton("Close", Close);
            close.style.marginTop = 8f;
            scroll.Add(close);
            root.Add(scroll);
        }

        private bool ResolveLayer()
        {
            if (compositor == null || string.IsNullOrEmpty(layerId))
                return false;

            currentLayer = compositor.FindLayer(layerId);

            return currentLayer != null && EditedLayerType.IsInstanceOfType(currentLayer.Behaviour);
        }

        private void InvalidateEffectTargetOptions()
        {
            effectTargetSettings?.Invalidate();
        }

        private void OnCompositorChanged(TextureCompositor changedCompositor)
        {
            if (changedCompositor != compositor || applyingChange)
                return;
            if (TextureCompositor.IsRefreshingUndo)
            {
                OnUndoRedo();
                return;
            }
            InvalidateEffectTargetOptions();
            interfaceRefreshRequested = true;
            RequestPreview();
        }

        private void OnUndoRedo()
        {
            InvalidateEffectTargetOptions();
            RefreshInterface(forceValues: true);
            RequestPreview(true);
        }

        private void UpdatePreview()
        {
            ReleasePreview();
            if (!ResolveLayer())
                return;

            previewTexture = compositor.RenderLayerPreview(currentLayer, PreviewMaxSize);

            if (previewImage != null)
                previewImage.image = previewTexture;
            if (previewPlaceholder != null)
                previewPlaceholder.text = previewTexture == null ? "Preview unavailable" : string.Empty;
        }

        private void ReleasePreview()
        {
            if (previewImage != null)
                previewImage.image = null;
            if (previewTexture == null)
                return;
            RenderTexture.ReleaseTemporary(previewTexture);
            previewTexture = null;
        }
    }
}
