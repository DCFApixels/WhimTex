using System;
using System.Collections.Generic;
using UnityEditor;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "DrawingLayerBehaviour")]
    [Serializable]
    public sealed partial class DrawingLayerBehaviour : LayerBehaviour
    {
        private const int MinimumRepeatCount = 2;
        private const int MaximumRepeatCount = 64;
        private const float DefaultBrushSpacing = 0.16f;
        internal const float MinimumBrushSpacing = 0.01f;
        internal const float MaximumBrushSpacing = 4f;

        [SerializeField] private Texture2D pixels;
        [SerializeField, HideInInspector] private int pixelsRevision;
        [SerializeField, HideInInspector] private string originalImageUrl;
        [SerializeField, HideInInspector] private int originalImageRevision;
        [NonSerialized] private WhimTexDocumentSerializer.DeferredTextureInfo? deferredTexture;
        // A deferred TIFF still represents the same untouched source image. Do not force a full
        // materialization merely to preserve its portable URL during clipboard/export operations.
        internal string PortableImageUrl => (pixels != null || deferredTexture.HasValue) &&
            pixelsRevision == originalImageRevision ? originalImageUrl : null;
        internal void RememberImageUrl(string url)
        {
            originalImageUrl = url;
            originalImageRevision = pixelsRevision;
        }

        public PaintToolMode tool = PaintToolMode.Brush;
        public Color brushColor = Color.white;
        public Color secondaryBrushColor = Color.black;
        public float brushSize = 32f;
        [Range(0f, 1f)] public float brushHardness = 0.8f;
        [Range(MinimumBrushSpacing, MaximumBrushSpacing)] public float brushSpacing = DefaultBrushSpacing;
        [SerializeField, HideInInspector] internal BrushDynamics brushDynamics;
        public FillSampleMode fillSampleMode = FillSampleMode.CurrentLayer;
        public bool fillContiguous = true;
        [Range(0, 255)] public int fillTolerance = 32;
        public bool fillAntialias = true;
        [Range(0, 32)] public int fillExpand;

        public bool mirrorAcrossVerticalAxis;
        public bool mirrorAcrossHorizontalAxis;
        [Range(0f, 360f)] public float mirrorAngle;
        public Vector2 patternCenter = new Vector2(0.5f, 0.5f);

        public PaintRepeatMode repeatMode;
        public PaintRepeatElementMode repeatElementMode;
        public PaintRepeatBoundaryMode repeatBoundaryMode;
        public int repeatCount = 8;
        public int repeatSecondaryCount = 4;
        [Range(0f, 360f)] public float radialStartAngle;

        [NonSerialized] private RenderTexture paintSurface;
        [NonSerialized] private bool paintSurfaceDirty;
        [NonSerialized] private ulong paintSurfaceRevision;
        internal ulong PaintSurfaceRevision => paintSurfaceRevision;
        [NonSerialized] private List<Vector2> symmetryPoints;
        [NonSerialized] private List<PaintStamp> patternStamps;
        [NonSerialized] private HashSet<PaintStamp> patternStampSet;
        [NonSerialized] private List<PaintStamp> segmentStamps;
        [NonSerialized] private bool clipStrokeToInitialShape;
        [NonSerialized] private Vector2 strokeRepeatShapeAnchor;
        [NonSerialized] private bool strokeWrapCanvas;

        internal Texture2D StoredTexture { get { EnsureDeferredTexture(); return pixels; } }
        internal long DocumentPixelBytes
        {
            get
            {
                EnsureDeferredTexture();
                return paintSurface != null && (paintSurfaceDirty || pixels == null)
                    ? (long)paintSurface.width * paintSurface.height * (colorRange == LayerColorRange.HDR ? 8 : 4)
                    : pixels == null ? 0 : WhimTexDocumentLimits.ExpectedBytes(pixels.width, pixels.height, pixels.format, pixels.mipmapCount, !pixels.isDataSRGB);
            }
        }
        internal bool UsesMirrorPattern => repeatMode == PaintRepeatMode.Mirror;
        internal bool UsesRepeatedPattern => repeatMode != PaintRepeatMode.None && !UsesMirrorPattern;
        internal float RadialStartAngleRadians => -Mathf.PI + Mathf.Repeat(radialStartAngle, 360f) * Mathf.Deg2Rad;

        internal static DrawingLayerBehaviour FromRasterizedLayer(Layer source, Texture2D texture, bool applyTransform,
            bool preserveGroupBlend = false)
        {
            DrawingLayerBehaviour result = source?.Behaviour is DrawingLayerBehaviour drawing
                ? JsonUtility.FromJson<DrawingLayerBehaviour>(JsonUtility.ToJson(drawing))
                : new DrawingLayerBehaviour();
            result.CopyRasterizedIdentityFrom(source);
            result.transform = applyTransform ? TextureTransform.Default : source.transform;
            if (source.IsGroup)
            {
                result.transform = TextureTransform.Default;
                result.opacity = preserveGroupBlend ? source.opacity : 1f;
                result.blendMode = preserveGroupBlend ? ((Layer)source).EffectiveBlendMode : BlendMode.Normal;
                if (((Layer)source).IsPassThrough)
                    result.colorRange = LayerColorRange.HDR;
                result.swizzle = default;
                result.modifiers.Clear();
            }
            if ((applyTransform || source.IsGroup) && source.transformCache?.parent != null)
                result.transform.TrySetMatrix(source.transformCache.parentInverse);
            result.pixels = texture;
            result.originalImageUrl = null;
            // Source settings must survive conversion from a File layer to owned pixels.
            Texture samplingSource = source.SamplingSource;
            texture.filterMode = source.ResolveFilterMode();
            texture.wrapModeU = samplingSource != null ? samplingSource.wrapModeU : TextureWrapMode.Clamp;
            texture.wrapModeV = samplingSource != null ? samplingSource.wrapModeV : TextureWrapMode.Clamp;
            texture.name = result.GetTextureName();
            return result;
        }

        internal static DrawingLayerBehaviour FromMergedTexture(Texture2D texture)
        {
            var result = new DrawingLayerBehaviour { pixels = texture, colorRange = LayerColorRange.HDR };
            result.AssignNewId();
            return result;
        }

        public override Texture2D GetPreviewTexture(int size)
        {
            return StoredTexture;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            RenderTexture surface = EnsurePaintSurface(context.compositor.width, context.compositor.height);
            if (surface == null)
                return null;

            // This is an owned transient surface, never the imported source texture.
            surface.filterMode = ResolveFilterMode();

            // Unpremultiply at source resolution. Resampling to the output size before
            // the transform loses details that a magnified/cropped layer still needs.
            RenderTexture straight = RenderTexture.GetTemporary(
                surface.width,
                surface.height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            straight.filterMode = ResolveFilterMode();
            straight.wrapMode = TextureWrapMode.Clamp;

            try
            {
                Material conversion = WhimTexMaterials.AlphaConversion;
                if (conversion == null)
                {
                    Graphics.Blit(surface, straight);
                }
                else
                {
                    conversion.SetFloat("_Mode", 1f);
                    Graphics.Blit(surface, straight, conversion);
                }

                return ApplyTransformAndModifiers(straight, context);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(straight);
            }
        }

        internal void NormalizeSettings()
        {
            brushSize = Mathf.Max(1f, brushSize);
            brushHardness = Mathf.Clamp01(brushHardness);
            if (brushSpacing <= 0f)
                brushSpacing = DefaultBrushSpacing;
            brushSpacing = Mathf.Clamp(brushSpacing, MinimumBrushSpacing, MaximumBrushSpacing);
            patternCenter.x = Mathf.Clamp01(patternCenter.x);
            patternCenter.y = Mathf.Clamp01(patternCenter.y);
            repeatCount = Mathf.Clamp(repeatCount, MinimumRepeatCount, MaximumRepeatCount);
            repeatSecondaryCount = Mathf.Clamp(repeatSecondaryCount, MinimumRepeatCount, MaximumRepeatCount);
            radialStartAngle = Mathf.Clamp(radialStartAngle, 0f, 360f);
            mirrorAngle = Mathf.Clamp(mirrorAngle, 0f, 360f);
            fillTolerance = Mathf.Clamp(fillTolerance, 0, 255);
            fillExpand = Mathf.Clamp(fillExpand, 0, 32);
        }

        internal void ApplyFillPixels(NativeArray<Color32> output, int width, int height, string undoName)
        {
            using var linear = new NativeArray<Color>(output.Length, Allocator.TempJob);
            var values = linear;
            for (int i = 0; i < output.Length; i++) values[i] = HdrUtility.Decode(output[i]);
            ApplyFillPixels(linear, width, height, undoName);
        }

        internal void InitializeCanvas(int width, int height)
        {
            if (pixels != null)
                return;
            EnsurePaintSurface(width, height);
            SyncSurfaceToTexture();
        }

        internal void PrepareStroke(int width, int height, string undoName)
        {
            unchecked { pixelsRevision++; }
            EnsureHdrStorage();
            EnsurePaintSurface(width, height);
            if (pixels == null)
                SyncSurfaceToTexture();
            if (pixels != null)
                Undo.RegisterCompleteObjectUndo(pixels, undoName);
        }

        internal void BeginStroke(Vector2 sourceUv)
        {
            ReleaseAdvancedStroke();
            brushSpacingState = default;
            brushRandomState = 0;
            brushStampIndex = 0;
            brushDirection = 0f;
            brushTintPrepared = false;
            strokeWrapCanvas = false;
            strokeRepeatShapeAnchor = sourceUv;
            clipStrokeToInitialShape =
                repeatBoundaryMode == PaintRepeatBoundaryMode.Clip &&
                (UsesRepeatedPattern || UsesMirrorPattern && (mirrorAcrossVerticalAxis || mirrorAcrossHorizontalAxis));
        }

        internal void BeginTiledStroke(Vector2 sourceUv, int width, int height)
        {
            BeginStroke(TiledCanvasUtility.CanonicalSource(sourceUv, Owner.CanvasTransform, width, height));
            strokeWrapCanvas = true;
        }

        private Vector2 StrokeShapePoint(Vector2 sourceUv, int width, int height) => strokeWrapCanvas
            ? TiledCanvasUtility.CanonicalSource(sourceUv, Owner.CanvasTransform, width, height)
            : sourceUv;

        internal void EndStroke()
        {
            ReleaseAdvancedStroke();
            clipStrokeToInitialShape = false;
            strokeWrapCanvas = false;
        }

        internal bool IsStrokePointInsideRepeatShape(
            Vector2 sourceUv,
            int outputWidth,
            int outputHeight)
        {
            return !clipStrokeToInitialShape ||
                   IsInSameRepeatShape(
                       strokeRepeatShapeAnchor,
                       StrokeShapePoint(sourceUv, outputWidth, outputHeight),
                       outputWidth,
                       outputHeight);
        }

        internal bool TryClipStrokeSegmentToRepeatShape(
            Vector2 fromSourceUv,
            Vector2 toSourceUv,
            int outputWidth,
            int outputHeight,
            out Vector2 clippedSourceUv)
        {
            clippedSourceUv = toSourceUv;
            if (!clipStrokeToInitialShape ||
                !IsInSameRepeatShape(
                    strokeRepeatShapeAnchor,
                    StrokeShapePoint(fromSourceUv, outputWidth, outputHeight),
                    outputWidth,
                    outputHeight))
            {
                return false;
            }

            if (IsInSameRepeatShape(
                    strokeRepeatShapeAnchor,
                    StrokeShapePoint(toSourceUv, outputWidth, outputHeight),
                    outputWidth,
                    outputHeight))
            {
                return true;
            }

            float inside = 0f;
            float outside = 1f;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                float midpoint = (inside + outside) * 0.5f;
                Vector2 candidate = Vector2.Lerp(fromSourceUv, toSourceUv, midpoint);
                if (IsInSameRepeatShape(
                        strokeRepeatShapeAnchor,
                        StrokeShapePoint(candidate, outputWidth, outputHeight),
                        outputWidth,
                        outputHeight))
                {
                    inside = midpoint;
                }
                else
                {
                    outside = midpoint;
                }
            }

            clippedSourceUv = Vector2.Lerp(fromSourceUv, toSourceUv, inside);
            return true;
        }

        internal void SwapBrushColors()
        {
            Color previousPrimary = brushColor;
            brushColor = secondaryBrushColor;
            secondaryBrushColor = previousPrimary;
        }

        internal PaintStrokeParameters GetStrokeParameters(bool erase)
        {
            NormalizeSettings();
            brushDynamics?.Normalize();
            return new PaintStrokeParameters(brushColor, brushSize, brushHardness, brushSpacing, erase, dynamics: brushDynamics);
        }

        internal void PaintPoint(Vector2 sourceUv, int outputWidth, int outputHeight, PaintStrokeParameters parameters)
        {
            PaintSegment(sourceUv, sourceUv, outputWidth, outputHeight, true, parameters);
        }

        internal void PaintSegment(
            Vector2 fromSourceUv,
            Vector2 toSourceUv,
            int outputWidth,
            int outputHeight,
            bool includeStart,
            PaintStrokeParameters parameters)
        {
            Color color = parameters.Color;
            if (color.a <= 0f || parameters.Dynamics != null && (parameters.Dynamics.opacity <= 0f || parameters.Dynamics.flow <= 0f))
                return;
            color.a *= parameters.Pressure;
            if (color.a <= 0f) return;
            if (!TiledCanvasUtility.IsInvertible(Owner.CanvasTransform)) return;
            var canvasSize = new Vector2(outputWidth, outputHeight);
            fromSourceUv = Owner.CanvasTransform.Map(fromSourceUv, canvasSize);
            toSourceUv = Owner.CanvasTransform.Map(toSourceUv, canvasSize);
            if (!ProjectiveMatrix.Finite(fromSourceUv.x) || !ProjectiveMatrix.Finite(fromSourceUv.y) ||
                !ProjectiveMatrix.Finite(toSourceUv.x) || !ProjectiveMatrix.Finite(toSourceUv.y)) return;
            RenderTexture surface = EnsurePaintSurface(outputWidth, outputHeight);
            if (surface == null)
                return;

            NormalizeSettings();
            outputWidth = Mathf.Max(1, outputWidth);
            outputHeight = Mathf.Max(1, outputHeight);
            Vector2 pixelDelta = new Vector2(
                (toSourceUv.x - fromSourceUv.x) * outputWidth,
                (toSourceUv.y - fromSourceUv.y) * outputHeight);
            float distance = pixelDelta.magnitude;
            int steps = distance > 0f ? Mathf.Max(1, Mathf.CeilToInt(distance / parameters.SpacingPixels)) : 0;
            if (parameters.WrapCanvas && steps > 0)
            {
                int copies = UsesRepeatedPattern ? repeatCount : 1;
                if (repeatMode == PaintRepeatMode.Grid) copies *= repeatSecondaryCount;
                if (UsesMirrorPattern && mirrorAcrossVerticalAxis) copies *= 2;
                if (UsesMirrorPattern && mirrorAcrossHorizontalAxis) copies *= 2;
                float maxStampSize = parameters.Size * (1f + (parameters.Dynamics?.sizeJitter ?? 0f));
                if (parameters.Dynamics?.CanRotateTip == true) maxStampSize *= Mathf.Sqrt(2f);
                double coverage = Math.Max(1d, copies * (double)Mathf.Min(maxStampSize, outputWidth) * Mathf.Min(maxStampSize, outputHeight));
                int sampleBudget = Mathf.Clamp((int)Math.Min(8192d, 64000000d / coverage), 1, 8192);
                steps = Mathf.Min(steps, sampleBudget);
            }

            segmentStamps ??= new List<PaintStamp>(256);
            segmentStamps.Clear();
            if (parameters.PixelPerfect)
                BuildPencilSegment(fromSourceUv, toSourceUv, outputWidth, outputHeight, includeStart, parameters, steps);
            else BuildBrushSegment(fromSourceUv, toSourceUv, outputWidth, outputHeight, includeStart, parameters,
                distance, parameters.WrapCanvas ? Mathf.Max(1, steps + 1) : 8192);

            BrushDynamics dynamics = parameters.Dynamics;
            bool isolatedStroke = dynamics != null && dynamics.NeedsStrokeBuffer(parameters.Erase);
            bool stampBlend = dynamics != null && dynamics.BlendsEachStamp(parameters.Erase);
            if (isolatedStroke && segmentStamps.Count > 0) EnsureAdvancedStroke(surface, stampBlend);
            color.a *= dynamics != null ? dynamics.flow : 1f;

            paintSurfaceDirty |= segmentStamps.Count > 0;
            if (segmentStamps.Count > 0)
            {
                unchecked { paintSurfaceRevision++; }
                originalImageUrl = null;
            }
            PaintBrushRenderer.Draw(
                isolatedStroke ? advancedStroke : surface,
                segmentStamps,
                parameters.Size,
                parameters.Hardness,
                parameters.PixelPerfect,
                parameters.Shape,
                color,
                parameters.Erase && !isolatedStroke,
                outputWidth,
                outputHeight,
                patternCenter,
                parameters.WrapCanvas,
                Owner.CanvasTransform,
                colorRange == LayerColorRange.Standard,
                !isolatedStroke && HdrUtility.IsHdr(pixels), parameters.SelectionMask, dynamics, parameters.StandardColorInputs,
                stampBlend, blendRange == LayerBlendRange.HDR);
            if (isolatedStroke && segmentStamps.Count > 0) CompositeAdvancedStroke(surface, parameters);
        }

        private void BuildPencilSegment(Vector2 from, Vector2 to, int width, int height,
            bool includeStart, PaintStrokeParameters parameters, int maxSteps)
        {
            from = PaintStrokeParameters.SnapPencilCenter(from, width, height, parameters.Size);
            to = PaintStrokeParameters.SnapPencilCenter(to, width, height, parameters.Size);
            float offset = ((Mathf.RoundToInt(parameters.Size) & 1) == 0) ? 0f : 0.5f;
            int x = Mathf.RoundToInt(from.x * width - offset), y = Mathf.RoundToInt(from.y * height - offset);
            int endX = Mathf.RoundToInt(to.x * width - offset), endY = Mathf.RoundToInt(to.y * height - offset);
            int dx = Mathf.Abs(endX - x), dy = Mathf.Abs(endY - y);
            if (maxSteps > 0 && maxSteps < Mathf.Max(dx, dy))
            {
                for (int step = includeStart ? 0 : 1; step <= maxSteps; step++)
                    AddPoint(Vector2.Lerp(from, to, (float)step / maxSteps));
                return;
            }
            int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1;
            int error = dx - dy;
            bool first = true;
            while (true)
            {
                if (!first || includeStart)
                    AddPoint(new Vector2((x + offset) / width, (y + offset) / height));
                if (x == endX && y == endY) break;
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x += sx; }
                if (twice < dx) { error += dx; y += sy; }
                first = false;
            }

            void AddPoint(Vector2 point)
            {
                if (parameters.WrapCanvas)
                    point = TiledCanvasUtility.Wrap(point);
                point = PaintStrokeParameters.SnapPencilCenter(point, width, height, parameters.Size);
                if (!parameters.OverlapsCanvas(point, width, height)) return;
                BuildPatternStamps(point, width, height);
                patternStampSet.Clear();
                for (int i = 0; i < patternStamps.Count; i++)
                {
                    PaintStamp stamp = patternStamps[i];
                    stamp.center = PaintStrokeParameters.SnapPencilCenter(stamp.center, width, height, parameters.Size);
                    if (patternStampSet.Add(stamp)) segmentStamps.Add(stamp);
                }
            }
        }

        internal void ClearSurface(int width, int height)
        {
            originalImageUrl = null;
            RenderTexture surface = EnsurePaintSurface(width, height);
            if (surface == null)
                return;

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = surface;
                GL.Clear(true, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = previous;
            }
            SyncSurfaceToTexture();
        }

        internal void SyncPendingSurfaceToTexture()
        {
            EnsureDeferredTexture();
            // A surface created for preview is only a derived copy. Reading it back
            // can quantize otherwise untouched source pixels and invalidate save caches.
            if (paintSurfaceDirty || pixels == null)
                SyncSurfaceToTexture();
        }

        internal void SyncSurfaceToTexture()
        {
            EnsureDeferredTexture();
            if (paintSurface == null)
                return;

            if (pixels == null || pixels.width != paintSurface.width || pixels.height != paintSurface.height)
            {
                if (pixels != null && !AssetDatabase.Contains(pixels))
                    UnityEngine.Object.DestroyImmediate(pixels);
                pixels = new Texture2D(paintSurface.width, paintSurface.height, colorRange == LayerColorRange.HDR ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, colorRange == LayerColorRange.HDR)
                {
                    name = GetTextureName(),
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            RenderTexture straight = RenderTexture.GetTemporary(
                paintSurface.width,
                paintSurface.height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Material conversion = WhimTexMaterials.AlphaConversion;
                if (conversion == null)
                {
                    Graphics.Blit(paintSurface, straight);
                }
                else
                {
                    conversion.SetFloat("_Mode", HdrUtility.IsHdr(pixels) ? 3f : 2f);
                    Graphics.Blit(paintSurface, straight, conversion);
                }

                RenderTexture.active = straight;
                pixels.ReadPixels(new Rect(0f, 0f, straight.width, straight.height), 0, 0, false);
                pixels.Apply(false, false);
                paintSurfaceDirty = false;
                pixels.name = GetTextureName();
                if (AssetDatabase.Contains(pixels))
                    EditorUtility.SetDirty(pixels);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(straight);
            }
        }

        // Adopts an already decoded image as this layer's own pixels. The texture keeps its source
        // resolution, so callers fit the transform instead of resampling it to the canvas.
        internal void AdoptStoredTexture(Texture2D texture)
        {
            deferredTexture = null;
            originalImageUrl = null;
            if (pixels != null && !AssetDatabase.Contains(pixels))
                UnityEngine.Object.DestroyImmediate(pixels);
            pixels = texture;
            if (pixels == null)
                return;
            unchecked { pixelsRevision++; }
            pixels.name = GetTextureName();
            pixels.hideFlags = HideFlags.HideAndDontSave;
            ReleasePaintSurface();
        }

        internal void CloneStoredTexture()
        {
            EnsureDeferredTexture();
            SyncPendingSurfaceToTexture();
            if (pixels == null)
                return;

            pixels = UnityEngine.Object.Instantiate(pixels);
            pixels.name = GetTextureName();
            pixels.hideFlags = HideFlags.HideAndDontSave;
            ReleasePaintSurface();
        }

        internal bool MakeTexturePersistent(TextureCompositor owner)
        {
            if (owner == null || !AssetDatabase.Contains(owner))
                return false;
            EnsureDeferredTexture();
            if (pixels == null)
            {
                EnsurePaintSurface(owner.width, owner.height);
                SyncSurfaceToTexture();
            }
            if (pixels == null || AssetDatabase.Contains(pixels))
                return false;

            pixels.name = GetTextureName();
            pixels.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(pixels, owner);
            EditorUtility.SetDirty(pixels);
            EditorUtility.SetDirty(owner);
            return true;
        }

        internal void DestroyStoredTextureWithUndo()
        {
            EnsureDeferredTexture();
            ReleasePaintSurface();
            if (pixels == null)
                return;

            Undo.DestroyObjectImmediate(pixels);
            pixels = null;
        }

        internal void InvalidatePaintSurface()
        {
            ReleasePaintSurface();
        }

        internal override void ReleaseTransientResources()
        {
            ReleasePaintSurface();
            if (pixels != null && !AssetDatabase.Contains(pixels) &&
                (pixels.hideFlags & HideFlags.DontSave) != 0)
            {
                UnityEngine.Object.DestroyImmediate(pixels);
                pixels = null;
            }
        }

        // Detaching does not delete owned pixels: the caller or Undo may still own them.
        internal override void OnDetached() => ReleasePaintResources();

        internal void ReleasePaintResources()
        {
            if (paintSurfaceDirty)
                SyncSurfaceToTexture();
            ReleasePaintSurface();
        }

        private RenderTexture EnsurePaintSurface(int fallbackWidth, int fallbackHeight)
        {
            EnsureDeferredTexture();
            int width = pixels != null ? pixels.width : Mathf.Max(1, fallbackWidth);
            int height = pixels != null ? pixels.height : Mathf.Max(1, fallbackHeight);
            if (paintSurface != null && paintSurface.width == width && paintSurface.height == height)
                return paintSurface;

            ReleasePaintSurface();
            paintSurface = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = GetTextureName() + " (Paint Surface)",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            paintSurface.Create();

            if (pixels != null)
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Material conversion = WhimTexMaterials.AlphaConversion;
                    if (conversion == null)
                    {
                        Graphics.Blit(pixels, paintSurface);
                    }
                    else
                    {
                        conversion.SetFloat("_Mode", 0f);
                        conversion.SetFloat("_DecodeSource", HdrUtility.IsHdr(pixels) ? 0f : 1f);
                        Graphics.Blit(pixels, paintSurface, conversion);
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            else
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = paintSurface;
                    GL.Clear(true, true, Color.clear);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            return paintSurface;
        }

        internal bool HasDeferredTexture => deferredTexture.HasValue && pixels == null;

        internal void SetDeferredTexture(WhimTexDocumentSerializer.DeferredTextureInfo info)
        {
            if (pixels != null && !AssetDatabase.Contains(pixels))
                UnityEngine.Object.DestroyImmediate(pixels);
            pixels = null;
            deferredTexture = info;
            ReleasePaintSurface();
        }

        private void EnsureDeferredTexture()
        {
            if (!deferredTexture.HasValue || pixels != null)
                return;
            Texture2D loaded = WhimTexDocumentSerializer.MaterializeDeferredTexture(deferredTexture.Value);
            if (loaded == null)
                return;
            deferredTexture = null;
            pixels = loaded;
            pixels.name = GetTextureName();
            pixels.hideFlags = HideFlags.HideAndDontSave;
        }

        private void BuildPatternStamps(Vector2 sourceUv, int outputWidth, int outputHeight)
        {
            symmetryPoints ??= new List<Vector2>(4);
            patternStamps ??= new List<PaintStamp>(MaximumRepeatCount * 4);
            patternStampSet ??= new HashSet<PaintStamp>();
            symmetryPoints.Clear();
            patternStamps.Clear();
            patternStampSet.Clear();

            if (UsesMirrorPattern && repeatBoundaryMode == PaintRepeatBoundaryMode.Clip &&
                (mirrorAcrossVerticalAxis || mirrorAcrossHorizontalAxis))
            {
                int region = GetMirrorRegion(clipStrokeToInitialShape ? Owner.CanvasTransform.Map(strokeRepeatShapeAnchor,new Vector2(outputWidth,outputHeight)) : sourceUv, outputWidth, outputHeight);
                AddMirrorClippedStamp(sourceUv, region);
                if (mirrorAcrossVerticalAxis)
                    AddMirrorClippedStamp(ReflectPoint(sourceUv, true, outputWidth, outputHeight), region ^ 1);
                if (mirrorAcrossHorizontalAxis)
                    AddMirrorClippedStamp(ReflectPoint(sourceUv, false, outputWidth, outputHeight), region ^ 2);
                if (mirrorAcrossVerticalAxis && mirrorAcrossHorizontalAxis)
                    AddMirrorClippedStamp(patternCenter * 2f - sourceUv, region ^ 3);
                return;
            }

            AddSymmetryPoint(sourceUv);
            if (UsesMirrorPattern && mirrorAcrossVerticalAxis)
                AddSymmetryPoint(ReflectPoint(sourceUv, true, outputWidth, outputHeight));
            if (UsesMirrorPattern && mirrorAcrossHorizontalAxis)
                AddSymmetryPoint(ReflectPoint(sourceUv, false, outputWidth, outputHeight));
            if (UsesMirrorPattern && mirrorAcrossVerticalAxis && mirrorAcrossHorizontalAxis)
            {
                AddSymmetryPoint(new Vector2(
                    patternCenter.x * 2f - sourceUv.x,
                    patternCenter.y * 2f - sourceUv.y));
            }

            for (int i = 0; i < symmetryPoints.Count; i++)
                AddRepeatedStamps(symmetryPoints[i], outputWidth, outputHeight);
        }

        private void AddSymmetryPoint(Vector2 point)
        {
            for (int i = 0; i < symmetryPoints.Count; i++)
            {
                if ((symmetryPoints[i] - point).sqrMagnitude < 0.00000001f)
                    return;
            }
            symmetryPoints.Add(point);
        }

        internal Vector2 GetMirrorAxisDirection(bool vertical)
        {
            float angle = Mathf.Repeat(mirrorAngle, 360f) * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            return vertical ? new Vector2(-sine, cosine) : new Vector2(cosine, sine);
        }

        private Vector2 ReflectPoint(Vector2 sourceUv, bool vertical, int width, int height)
        {
            Vector2 size = new Vector2(Mathf.Max(1, width), Mathf.Max(1, height));
            Vector2 delta = Vector2.Scale(sourceUv - patternCenter, size);
            Vector2 axis = GetMirrorAxisDirection(vertical);
            Vector2 reflected = 2f * Vector2.Dot(delta, axis) * axis - delta;
            return patternCenter + new Vector2(reflected.x / size.x, reflected.y / size.y);
        }

        private int GetMirrorRegion(Vector2 pointUv, int width, int height)
        {
            Vector2 delta = Vector2.Scale(pointUv - patternCenter, new Vector2(Mathf.Max(1, width), Mathf.Max(1, height)));
            Vector2 horizontal = GetMirrorAxisDirection(false);
            Vector2 vertical = new Vector2(-horizontal.y, horizontal.x);
            return (mirrorAcrossVerticalAxis && Vector2.Dot(delta, horizontal) >= 0f ? 1 : 0) |
                   (mirrorAcrossHorizontalAxis && Vector2.Dot(delta, vertical) >= 0f ? 2 : 0);
        }

        private void AddMirrorClippedStamp(Vector2 point, int region)
        {
            Vector2 horizontal = GetMirrorAxisDirection(false);
            AddStamp(point, 3, new Vector4(horizontal.x, horizontal.y,
                mirrorAcrossVerticalAxis ? ((region & 1) != 0 ? 1f : -1f) : 0f,
                mirrorAcrossHorizontalAxis ? ((region & 2) != 0 ? 1f : -1f) : 0f), 0f, 0f);
        }

        private void AddRepeatedStamps(Vector2 point, int outputWidth, int outputHeight)
        {
            int primaryCount = Mathf.Clamp(repeatCount, MinimumRepeatCount, MaximumRepeatCount);
            int secondaryCount = Mathf.Clamp(repeatSecondaryCount, MinimumRepeatCount, MaximumRepeatCount);
            bool clip = repeatBoundaryMode == PaintRepeatBoundaryMode.Clip;
            bool alternate = repeatElementMode == PaintRepeatElementMode.AlternateMirror;

            switch (repeatMode)
            {
                case PaintRepeatMode.Horizontal:
                {
                    GetRepeatedAxisCoordinate(point.x, primaryCount, out int sourceX, out float localX);
                    if (alternate && (sourceX & 1) != 0)
                        localX = 1f - localX;
                    for (int x = 0; x < primaryCount; x++)
                    {
                        float repeatedX = (x + (alternate && (x & 1) != 0 ? 1f - localX : localX)) /
                                          primaryCount;
                        Vector4 clipRect = new Vector4(
                            (float)x / primaryCount,
                            0f,
                            (float)(x + 1) / primaryCount,
                            1f);
                        AddStamp(new Vector2(repeatedX, point.y), clip ? 1 : 0, clipRect, 0f, 0f);
                    }
                    break;
                }

                case PaintRepeatMode.Vertical:
                {
                    GetRepeatedAxisCoordinate(point.y, primaryCount, out int sourceY, out float localY);
                    if (alternate && (sourceY & 1) != 0)
                        localY = 1f - localY;
                    for (int y = 0; y < primaryCount; y++)
                    {
                        float repeatedY = (y + (alternate && (y & 1) != 0 ? 1f - localY : localY)) /
                                          primaryCount;
                        Vector4 clipRect = new Vector4(
                            0f,
                            (float)y / primaryCount,
                            1f,
                            (float)(y + 1) / primaryCount);
                        AddStamp(new Vector2(point.x, repeatedY), clip ? 1 : 0, clipRect, 0f, 0f);
                    }
                    break;
                }

                case PaintRepeatMode.Grid:
                {
                    GetRepeatedAxisCoordinate(point.x, primaryCount, out int sourceX, out float localX);
                    GetRepeatedAxisCoordinate(point.y, secondaryCount, out int sourceY, out float localY);
                    if (alternate && ((sourceX + sourceY) & 1) != 0)
                        localX = 1f - localX;
                    for (int y = 0; y < secondaryCount; y++)
                    {
                        for (int x = 0; x < primaryCount; x++)
                        {
                            bool mirrored = alternate && ((x + y) & 1) != 0;
                            float repeatedX = (x + (mirrored ? 1f - localX : localX)) / primaryCount;
                            float repeatedY = (y + localY) / secondaryCount;
                            Vector4 clipRect = new Vector4(
                                (float)x / primaryCount,
                                (float)y / secondaryCount,
                                (float)(x + 1) / primaryCount,
                                (float)(y + 1) / secondaryCount);
                            AddStamp(new Vector2(repeatedX, repeatedY), clip ? 1 : 0, clipRect, 0f, 0f);
                        }
                    }
                    break;
                }

                case PaintRepeatMode.Radial:
                {
                    Vector2 canvasSize = new Vector2(Mathf.Max(1, outputWidth), Mathf.Max(1, outputHeight));
                    Vector2 deltaPixels = Vector2.Scale(point - patternCenter, canvasSize);
                    float radius = deltaPixels.magnitude;
                    float angle = Mathf.Atan2(deltaPixels.y, deltaPixels.x);
                    float sectorWidth = Mathf.PI * 2f / primaryCount;
                    float startAngle = RadialStartAngleRadians;
                    float sourceSectorPosition = (angle - startAngle) / sectorWidth;
                    int sourceSector = PositiveModulo(
                        Mathf.FloorToInt(sourceSectorPosition),
                        primaryCount);
                    float localAngle = Mathf.Repeat(angle - startAngle, sectorWidth);
                    if (alternate && (sourceSector & 1) != 0)
                        localAngle = sectorWidth - localAngle;
                    for (int sector = 0; sector < primaryCount; sector++)
                    {
                        bool mirrored = alternate && (sector & 1) != 0;
                        float repeatedAngle = startAngle + sector * sectorWidth +
                                              (mirrored ? sectorWidth - localAngle : localAngle);
                        Vector2 repeatedPixels = new Vector2(
                            Mathf.Cos(repeatedAngle) * radius,
                            Mathf.Sin(repeatedAngle) * radius);
                        Vector2 repeatedUv = patternCenter + new Vector2(
                            repeatedPixels.x / canvasSize.x,
                            repeatedPixels.y / canvasSize.y);
                        float clipCenter = startAngle + (sector + 0.5f) * sectorWidth;
                        AddStamp(
                            repeatedUv,
                            clip ? 2 : 0,
                            new Vector4(0f, 0f, 1f, 1f),
                            clipCenter,
                            sectorWidth * 0.5f);
                    }
                    break;
                }

                default:
                    AddStamp(point, 0, new Vector4(0f, 0f, 1f, 1f), 0f, 0f);
                    break;
            }
        }

        private static void GetRepeatedAxisCoordinate(
            float coordinate,
            int count,
            out int cell,
            out float local)
        {
            float repeatedCoordinate = coordinate * count;
            int unwrappedCell = Mathf.FloorToInt(repeatedCoordinate);
            cell = PositiveModulo(unwrappedCell, count);
            local = Mathf.Repeat(repeatedCoordinate, 1f);
        }

        private bool IsInSameRepeatShape(
            Vector2 anchorUv,
            Vector2 pointUv,
            int outputWidth,
            int outputHeight)
        {
            anchorUv=Owner.CanvasTransform.Map(anchorUv,new Vector2(outputWidth,outputHeight));
            pointUv=Owner.CanvasTransform.Map(pointUv,new Vector2(outputWidth,outputHeight));
            int primaryCount = Mathf.Clamp(repeatCount, MinimumRepeatCount, MaximumRepeatCount);
            int secondaryCount = Mathf.Clamp(repeatSecondaryCount, MinimumRepeatCount, MaximumRepeatCount);

            switch (repeatMode)
            {
                case PaintRepeatMode.Mirror:
                    return GetMirrorRegion(anchorUv, outputWidth, outputHeight) ==
                           GetMirrorRegion(pointUv, outputWidth, outputHeight);

                case PaintRepeatMode.Horizontal:
                    return GetCanvasRepeatCell(anchorUv.x, primaryCount) ==
                           GetCanvasRepeatCell(pointUv.x, primaryCount);

                case PaintRepeatMode.Vertical:
                    return GetCanvasRepeatCell(anchorUv.y, primaryCount) ==
                           GetCanvasRepeatCell(pointUv.y, primaryCount);

                case PaintRepeatMode.Grid:
                    return GetCanvasRepeatCell(anchorUv.x, primaryCount) ==
                           GetCanvasRepeatCell(pointUv.x, primaryCount) &&
                           GetCanvasRepeatCell(anchorUv.y, secondaryCount) ==
                           GetCanvasRepeatCell(pointUv.y, secondaryCount);

                case PaintRepeatMode.Radial:
                    return GetRadialRepeatSector(anchorUv, primaryCount, outputWidth, outputHeight) ==
                           GetRadialRepeatSector(pointUv, primaryCount, outputWidth, outputHeight);

                default:
                    return true;
            }
        }

        private static int GetCanvasRepeatCell(float coordinate, int count)
        {
            float clamped = Mathf.Clamp(coordinate, 0f, 1f - Mathf.Epsilon);
            return Mathf.Clamp(Mathf.FloorToInt(clamped * count), 0, count - 1);
        }

        private int GetRadialRepeatSector(
            Vector2 pointUv,
            int count,
            int outputWidth,
            int outputHeight)
        {
            Vector2 canvasSize = new Vector2(Mathf.Max(1, outputWidth), Mathf.Max(1, outputHeight));
            Vector2 deltaPixels = Vector2.Scale(pointUv - patternCenter, canvasSize);
            float angle = Mathf.Atan2(deltaPixels.y, deltaPixels.x);
            float sectorWidth = Mathf.PI * 2f / count;
            float startAngle = RadialStartAngleRadians;
            return PositiveModulo(Mathf.FloorToInt((angle - startAngle) / sectorWidth), count);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private void AddStamp(
            Vector2 center,
            int clipMode,
            Vector4 clipRect,
            float clipAngleCenter,
            float clipAngleHalfWidth)
        {
            PaintStamp stamp = new PaintStamp
            {
                center = center,
                clipMode = clipMode,
                clipRect = clipRect,
                clipAngleCenter = clipAngleCenter,
                clipAngleHalfWidth = clipAngleHalfWidth
            };
            if (patternStampSet.Add(stamp))
                patternStamps.Add(stamp);
        }

        private void ReleasePaintSurface()
        {
            ReleaseAdvancedStroke();
            paintSurfaceDirty = false;
            if (paintSurface == null)
                return;
            paintSurface.Release();
            UnityEngine.Object.DestroyImmediate(paintSurface);
            paintSurface = null;
        }

        private string GetTextureName()
        {
            return (string.IsNullOrWhiteSpace(layerName) ? "Drawing Layer" : layerName) + " Pixels";
        }

        internal struct PaintStamp : IEquatable<PaintStamp>
        {
            public float size;
            public float rotation;
            public int flip;
            public Color color;
            public Vector2 center;
            public int clipMode;
            public Vector4 clipRect;
            public float clipAngleCenter;
            public float clipAngleHalfWidth;

            public bool Equals(PaintStamp other)
            {
                return center.Equals(other.center) &&
                       clipMode == other.clipMode &&
                       clipRect.Equals(other.clipRect) &&
                       clipAngleCenter.Equals(other.clipAngleCenter) &&
                       clipAngleHalfWidth.Equals(other.clipAngleHalfWidth);
            }

            public override bool Equals(object obj)
            {
                return obj is PaintStamp other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = center.GetHashCode();
                    hashCode = (hashCode * 397) ^ clipMode;
                    hashCode = (hashCode * 397) ^ clipRect.GetHashCode();
                    hashCode = (hashCode * 397) ^ clipAngleCenter.GetHashCode();
                    hashCode = (hashCode * 397) ^ clipAngleHalfWidth.GetHashCode();
                    return hashCode;
                }
            }
        }

        private static partial class PaintBrushRenderer
        {
            private static readonly int ColorId = Shader.PropertyToID("_Color");
            private static readonly int HardnessId = Shader.PropertyToID("_Hardness");
            private static readonly int CanvasSizeId = Shader.PropertyToID("_CanvasSize");
            private static readonly int PatternCenterId = Shader.PropertyToID("_PatternCenter");
            private static readonly int SourceBlendId = Shader.PropertyToID("_SrcBlend");
            private static readonly int DestinationBlendId = Shader.PropertyToID("_DstBlend");

            public static void Draw(
                RenderTexture target,
                List<PaintStamp> stamps,
                float sizePixels,
                float hardness,
                bool pixelPerfect,
                PencilShape shape,
                Color color,
                bool erase,
                int outputWidth,
                int outputHeight,
                Vector2 patternCenter,
                bool wrapCanvas,
                TextureTransform transform, bool standard, bool hdrStorage, Texture selectionMask = null, BrushDynamics dynamics = null,
                bool standardColorInputs = false, bool stampBlend = false, bool hdrBlend = false)
            {
                if (target == null || stamps == null || stamps.Count == 0)
                    return;

                Material material = WhimTexMaterials.PaintBrush;
                if (material == null)
                    return;

                bool variation = dynamics != null && dynamics.PerStamp;
                Texture2D tip = dynamics?.tip;
                bool textured = tip != null;
                if (variation && !textured) material.EnableKeyword("BRUSH_DYNAMICS");
                else material.DisableKeyword("BRUSH_DYNAMICS");
                if (textured) material.EnableKeyword("BRUSH_TEXTURE");
                else material.DisableKeyword("BRUSH_TEXTURE");
                material.SetTexture("_BrushTip", textured ? tip : Texture2D.whiteTexture);
                material.SetFloat("_TipChannel", dynamics != null ? (int)dynamics.tipChannel : 0f);
                bool sdfGradient = !pixelPerfect && dynamics != null && dynamics.UsesSdfGradient;
                material.SetFloat("_TipSdf", sdfGradient ? 1f : 0f);
                material.SetTexture("_BrushSdfGradient", sdfGradient
                    ? GetBrushSdfGradient(dynamics, standardColorInputs) : Texture2D.whiteTexture);
                material.SetFloat("_TipStandard", standard ? 1f : 0f);
                material.SetFloat("_StampBlendEnabled", stampBlend ? 1f : 0f);
                material.SetFloat("_StampBlendMode", dynamics != null ? (int)dynamics.blend : 0f);
                material.SetFloat("_HdrBlend", hdrBlend ? 1f : 0f);
                float tipExtent = textured ? Mathf.Max(tip.width, tip.height) : 1f;
                material.SetVector("_TipAspect", textured ? new Vector4(tip.width / tipExtent, tip.height / tipExtent, 0f, 0f) : Vector4.one);
                material.SetFloat("_TipDecode", textured && tip.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f);
                material.SetFloat("_TipEncodeMask", textured && tip.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);

                float radiusX = sizePixels / Mathf.Max(1f, outputWidth) * 0.5f;
                float radiusY = sizePixels / Mathf.Max(1f, outputHeight) * 0.5f;
                color = HdrUtility.DecodePaintColor(color);
                if (standard) color = HdrUtility.Saturate(color);
                material.SetVector(ColorId, (Vector4)color);
                material.SetFloat(HardnessId, Mathf.Clamp01(hardness));
                material.SetFloat("_PencilShape", pixelPerfect ? (int)shape + 1f : 0f);
                material.SetVector(CanvasSizeId, new Vector4(outputWidth, outputHeight, 0f, 0f));
                material.SetVector(PatternCenterId, new Vector4(patternCenter.x, patternCenter.y, 0f, 0f));
                material.SetTexture("_SelectionMask", selectionMask != null ? selectionMask : Texture2D.whiteTexture);
                material.SetFloat("_UseSelection", selectionMask != null ? 1f : 0f);
                material.SetFloat("_SelectionWrap", wrapCanvas ? 1f : 0f);
                var sourceToCanvas = transform.ToMatrix(outputWidth, outputHeight);
                if (!sourceToCanvas.TryInverse(out var canvasToSource)) return;
                sourceToCanvas.SetShader(material, "_PaintRow");
                material.SetFloat("_BrushSize", sizePixels);
                material.SetFloat("_CanvasWrap", wrapCanvas ? 1 : 0);
                material.SetFloat(
                    SourceBlendId,
                    erase
                        ? (float)UnityEngine.Rendering.BlendMode.Zero
                        : (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat(
                    DestinationBlendId,
                    (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                RenderTexture previous = RenderTexture.active;
                RenderTexture snapshot = null;
                try
                {
                    if (stampBlend)
                    {
                        snapshot = RenderTexture.GetTemporary(target.descriptor);
                        snapshot.filterMode = FilterMode.Point;
                        snapshot.wrapMode = TextureWrapMode.Clamp;
                        material.SetTexture("_Backdrop", snapshot);
                    }
                    else if (standard && hdrStorage && !erase)
                    {
                        snapshot = HdrUtility.Temporary(target.width, target.height);
                        Graphics.Blit(target, snapshot);
                        material.SetTexture("_Backdrop", snapshot);
                    }
                    RenderTexture.active = target;
                    GL.PushMatrix();
                    try
                    {
                        GL.LoadOrtho();
                        for (int pass = snapshot != null && !stampBlend ? 0 : 1; pass < 2; pass++)
                        {
                            material.SetFloat("_PrepareStandard", pass == 0 ? 1f : 0f);
                            material.SetFloat(SourceBlendId, pass == 0 || !erase ? 1f : 0f);
                            material.SetFloat(DestinationBlendId, pass == 0 || stampBlend ? 0f : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            if (!material.SetPass(0))
                                return;

                            if (!stampBlend) BeginBrushMesh();
                            try
                            {
                                for (int i = 0; i < stamps.Count; i++)
                                {
                                    PaintStamp stamp = stamps[i];
                                    float dabSize = variation ? stamp.size : sizePixels;
                                    {
                                        Color dabColor = color;
                                        if (variation)
                                        {
                                            Color encoded = stamp.color;
                                            encoded.a *= dynamics.flow;
                                            dabColor = HdrUtility.DecodePaintColor(encoded);
                                            if (standard) dabColor = HdrUtility.Saturate(dabColor);
                                        }
                                        meshColor = new Vector3(dabColor.r, dabColor.g, dabColor.b);
                                        meshStamp = new Vector4(dabSize, dabColor.a, stamp.rotation, stamp.flip);
                                    }
                                    float dabRadiusX = variation ? dabSize / outputWidth * .5f : radiusX;
                                    float dabRadiusY = variation ? dabSize / outputHeight * .5f : radiusY;
                                    if (textured && stamp.rotation != 0f)
                                    {
                                        float extent = Mathf.Abs(Mathf.Cos(stamp.rotation)) + Mathf.Abs(Mathf.Sin(stamp.rotation));
                                        dabRadiusX *= extent;
                                        dabRadiusY *= extent;
                                    }
                                    if (stampBlend) BeginBrushMesh();
                                    try
                                    {
                                        DrawCanvasStamp(stamp, dabRadiusX, dabRadiusY, canvasToSource, wrapCanvas);
                                    }
                                    finally
                                    {
                                        if (stampBlend) EndBrushMesh(target, material, snapshot);
                                    }
                                }
                            }
                            finally
                            {
                                if (!stampBlend) EndBrushMesh(target, material);
                            }
                        }
                    }
                    finally
                    {
                        GL.PopMatrix();
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                    material.SetFloat("_PrepareStandard", 0f);
                    material.SetFloat("_StampBlendEnabled", 0f);
                    material.SetTexture("_Backdrop", null);
                    if (snapshot != null) RenderTexture.ReleaseTemporary(snapshot);
                }
            }

            private static void DrawCanvasStamp(PaintStamp stamp, float rx, float ry, ProjectiveMatrix inverse, bool wrap)
            {
                Vector2 min = Vector2.one, max = Vector2.zero;
                int first=wrap?-1:0, last=wrap?1:0;
                for(int tx=first;tx<=last;tx++)
                for(int ty=first;ty<=last;ty++)
                {
                    double x0=stamp.center.x+tx-rx,x1=stamp.center.x+tx+rx;
                    double y0=stamp.center.y+ty-ry,y1=stamp.center.y+ty+ry;
                    if(wrap) { x0=Math.Max(0,x0);y0=Math.Max(0,y0);x1=Math.Min(1,x1);y1=Math.Min(1,y1); }
                    if(x1<=x0 || y1<=y0)continue;
                    var a = new Double2(x0,y0);
                    var b = new Double2(x1,y0);
                    var c = new Double2(x1,y1);
                    var d = new Double2(x0,y1);
                    double wa=inverse.m20*a.x+inverse.m21*a.y+inverse.m22;
                    double wb=inverse.m20*b.x+inverse.m21*b.y+inverse.m22;
                    double wc=inverse.m20*c.x+inverse.m21*c.y+inverse.m22;
                    double wd=inverse.m20*d.x+inverse.m21*d.y+inverse.m22;
                    if (wa*wb>0 && wa*wc>0 && wa*wd>0 &&
                        inverse.TryPoint(a,out var pa) && inverse.TryPoint(b,out var pb) &&
                        inverse.TryPoint(c,out var pc) && inverse.TryPoint(d,out var pd))
                    {
                        min = Vector2.Min(min,Vector2.Max(Vector2.zero,Vector2.Min(Vector2.Min(pa,pb),Vector2.Min(pc,pd))));
                        max = Vector2.Max(max,Vector2.Min(Vector2.one,Vector2.Max(Vector2.Max(pa,pb),Vector2.Max(pc,pd))));
                    }
                    else { min=Vector2.zero;max=Vector2.one; }
                }
                if (max.x<=min.x || max.y<=min.y) return;
                DrawVertex(min.x,min.y,0,0,stamp);
                DrawVertex(min.x,max.y,0,1,stamp);
                DrawVertex(max.x,max.y,1,1,stamp);
                DrawVertex(max.x,min.y,1,0,stamp);
            }

            private static void DrawVertex(float x, float y, float brushU, float brushV, PaintStamp stamp, int tileMode = 0)
            {
                AddBrushMeshVertex(x, y, brushU, brushV, stamp, tileMode);
            }
        }
    }
}
