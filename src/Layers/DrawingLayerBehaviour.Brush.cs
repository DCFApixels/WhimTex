using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private BrushSpacingState brushSpacingState;
        [NonSerialized] private uint brushRandomState;
        [NonSerialized] private uint brushStampIndex;
        [NonSerialized] private float brushDirection;
        [NonSerialized] private Vector2 initialBrushDeltaPixels;
        [NonSerialized] private bool brushTintPrepared;
        [NonSerialized] private float previousBrushPressure;
        [NonSerialized] private bool hasBrushPressure;
        [NonSerialized] private BrushStampContext dynamicStroke;
        [NonSerialized] private bool dynamicStrokeStarted;
        [NonSerialized] private double brushDistance;
        [NonSerialized] private double dynamicStrokeStartDistance;
        [NonSerialized] private Vector2 dynamicPreviousStampPoint;
        [NonSerialized] private bool dynamicHasPreviousStamp;
        // paintSurface is the immutable base; only this accumulation changes until commit.
        [NonSerialized] private PaintStrokeBuffer advancedStroke;
        [NonSerialized] private PaintStrokeParameters brushCompositeParameters;
        [NonSerialized] private ProjectiveMatrix brushCompositeTransform;
        [NonSerialized] private bool brushBaseDirty;
        [NonSerialized] private RectInt brushDirtyBounds;
        [NonSerialized] private bool initialDirectionKnown;
        [NonSerialized] private bool firstBrushPending;
        [NonSerialized] private Vector2 firstBrushPosition;
        [NonSerialized] private float firstBrushSpacing, firstBrushAngle;
        [NonSerialized] private int firstBrushWidth, firstBrushHeight;
        [NonSerialized] private PaintStrokeParameters firstBrushParameters;
        [NonSerialized] private System.Collections.Generic.List<PaintStamp> firstBrushStamps;

        // Known paths (API/preview/Shift lines) do not borrow pointer history or need a provisional dab.
        internal void SetKnownBrushDirection(Vector2 deltaPixels)
        {
            SetInitialBrushDelta(deltaPixels);
            initialDirectionKnown = initialBrushDeltaPixels.sqrMagnitude > .000001f;
        }

        internal void SetInitialBrushDelta(Vector2 deltaPixels)
        {
            initialBrushDeltaPixels = float.IsFinite(deltaPixels.x) && float.IsFinite(deltaPixels.y)
                ? deltaPixels : Vector2.zero;
            brushDirection = BrushDynamics.MovementAngle(initialBrushDeltaPixels.x, initialBrushDeltaPixels.y, 0f);
        }

        internal void SetBrushPressure(float pressure)
        {
            previousBrushPressure = Mathf.Clamp01(pressure);
            hasBrushPressure = true;
        }

        private void BuildBrushSegment(Vector2 from, Vector2 to, int width, int height, bool includeStart,
            PaintStrokeParameters parameters, float distance, int budget)
        {
            BrushDynamics dynamics = parameters.Dynamics;
            float fromPressure = hasBrushPressure && !includeStart ? previousBrushPressure : parameters.Pressure;
            float spacing = parameters.SpacingAtPressure(fromPressure);
            bool pressureSpacing = dynamics?.pressure == true && dynamics.pressureSize?.enabled == true && fromPressure != parameters.Pressure;
            double first;
            int count;
            if (pressureSpacing)
            {
                first = brushSpacingState.BeginSegment(spacing, includeStart);
                // Count without allocating per-stamp storage. The second pass emits only the render budget.
                double next = first;
                count = 0;
                while (next <= distance + .000001d && count < int.MaxValue)
                {
                    count++;
                    next += PressureSpacingAt(next, distance, fromPressure, parameters);
                }
                brushSpacingState.EndSegment(distance, next);
            }
            else count = brushSpacingState.Sample(distance, spacing, includeStart, out first);
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, budget)));
            previousBrushPressure = parameters.Pressure;
            hasBrushPressure = true;
            bool dynamic = dynamics?.DynamicTip == true;
            if (brushStampIndex == 0 && initialBrushDeltaPixels.sqrMagnitude <= .000001f && distance > .001f)
                SetKnownBrushDirection(Vector2.Scale(to - from, new Vector2(width, height)));
            if (dynamic && !dynamicStrokeStarted)
            {
                dynamicStroke = dynamics.Sequence.Begin(dynamics.seed);
                dynamicStrokeStartDistance = dynamics.Sequence.TotalDistance;
                dynamicStrokeStarted = true;
            }
            if (!brushTintPrepared)
            {
                dynamics?.PrepareTint();
                brushTintPrepared = true;
            }
            bool variation = dynamics != null && dynamics.PerStamp;
            float baseRotation = 0f;
            if (dynamics?.HasTip == true && dynamics.rotationMode == BrushRotationMode.StrokeDirection)
            {
                brushDirection = BrushDynamics.MovementAngle((to.x - from.x) * width, (to.y - from.y) * height, brushDirection);
                baseRotation = brushDirection;
            }
            if (dynamics?.HasTip == true) baseRotation += dynamics.angleOffset * Mathf.Deg2Rad;
            float scatterExponent = dynamics != null && dynamics.scatter > 0f ? dynamics.GetScatterExponent() : .5f;
            if (brushRandomState == 0) brushRandomState = unchecked((uint)(dynamics?.seed ?? 1));
            uint firstStamp = brushStampIndex;
            // Advance even for clipped or budget-skipped stamps, not for symmetry copies.
            brushStampIndex = unchecked(brushStampIndex + (uint)count);
            double offset = first, previousOffset = first;
            for (long i = 0; i < count; i += pressureSpacing ? 1 : stride)
            {
                double atOffset = pressureSpacing ? offset : first + i * (double)spacing;
                double priorOffset = pressureSpacing ? previousOffset : first + (i - 1) * (double)spacing;
                if (pressureSpacing)
                {
                    previousOffset = atOffset;
                    offset += PressureSpacingAt(atOffset, distance, fromPressure, parameters);
                    if (i % stride != 0) continue;
                }
                uint stampIndex = unchecked(firstStamp + (uint)i);
                float t = distance <= 0f ? 0f : (float)(atOffset / distance);
                float pressure = Mathf.Lerp(fromPressure, parameters.Pressure, Mathf.Clamp01(t));
                Vector2 point = Vector2.LerpUnclamped(from, to, t);
                Vector2 deltaPixels = initialBrushDeltaPixels.sqrMagnitude > .000001f
                    ? initialBrushDeltaPixels.normalized * parameters.SpacingAtPressure(pressure) : Vector2.zero;
                if (dynamic && (i > 0 || dynamicHasPreviousStamp))
                {
                    // Use the previous logical stamp, even when the render budget skips it.
                    Vector2 previous = i > 0
                        ? Vector2.LerpUnclamped(from, to, distance <= 0f ? 0f :
                            (float)(priorOffset / distance))
                        : dynamicPreviousStampPoint;
                    deltaPixels = Vector2.Scale(point - previous, new Vector2(width, height));
                }
                float size = parameters.Size * (dynamics?.SizePressure(pressure) ?? 1f);
                float rotation = baseRotation;
                int flip = 0;
                Color tint = parameters.Color;
                float opacityPressure = dynamics?.OpacityPressure(pressure) ?? 1f;
                tint.a *= (dynamics?.flow ?? 1f) * (dynamics?.FlowPressure(pressure) ?? 1f);
                if (variation)
                {
                    if (dynamics.scatter > 0f)
                    {
                        float angle = dynamics.SampleRandom(ref brushRandomState, stampIndex, 0) * Mathf.PI * 2f;
                        float radius = BrushDynamics.ScatterRadius(dynamics.SampleRandom(ref brushRandomState, stampIndex, 1), scatterExponent) * dynamics.scatter * size;
                        point += new Vector2(Mathf.Cos(angle) * radius / width, Mathf.Sin(angle) * radius / height);
                    }
                    if (dynamics.sizeJitter > 0f)
                        size *= 1f + dynamics.sizeJitter * (2f * dynamics.SampleRandom(ref brushRandomState, stampIndex, 2) - 1f);
                    if (dynamics.HasTip && dynamics.angleJitter > 0f)
                        rotation += dynamics.angleJitter * Mathf.Deg2Rad * (2f * dynamics.SampleRandom(ref brushRandomState, stampIndex, 3) - 1f);
                    if (dynamics.HasTint)
                        tint *= dynamics.SampleTint(ref brushRandomState, stampIndex);
                    if (dynamics.HasTip)
                    {
                        if (dynamics.SampleFlip(ref brushRandomState, stampIndex, 5, dynamics.flipX)) flip |= 1;
                        if (dynamics.SampleFlip(ref brushRandomState, stampIndex, 6, dynamics.flipY)) flip |= 2;
                    }
                }
                float extent = rotation != 0f ? Mathf.Abs(Mathf.Cos(rotation)) + Mathf.Abs(Mathf.Sin(rotation)) : 1f;
                // Keep a provisional dab whose corrected rotation can reach the canvas edge.
                if (stampIndex == 0 && includeStart && distance == 0f && !initialDirectionKnown && dynamics?.NeedsDirection == true)
                    extent = Mathf.Sqrt(2f);
                if (size <= .0001f || tint.a <= 0f || opacityPressure <= 0f) continue;
                float padding = dynamics?.HasTip != true && dynamics?.UsesSdfGradient != true ? 1f : 0f;
                if (!parameters.WrapCanvas && (point.x + (size * extent * .5f + padding) / width <= 0f || point.x - (size * extent * .5f + padding) / width >= 1f ||
                    point.y + (size * extent * .5f + padding) / height <= 0f || point.y - (size * extent * .5f + padding) / height >= 1f)) continue;
                if (parameters.WrapCanvas) point = TiledCanvasUtility.Wrap(point);
                BuildPatternStamps(point, width, height);
                if (!variation) segmentStamps.AddRange(patternStamps);
                else for (int j = 0; j < patternStamps.Count; j++)
                {
                    PaintStamp stamp = patternStamps[j];
                    stamp.size = size;
                    stamp.rotation = rotation;
                    stamp.flip = flip;
                    stamp.color = tint;
                    stamp.opacityPressure = opacityPressure;
                    if (dynamic)
                    {
                        double at = brushDistance + Math.Min(distance, Math.Max(0d, atOffset));
                        stamp.dynamicContext = new BrushStampContext(dynamicStroke.seed, dynamicStroke.strokeIndex, stampIndex,
                            at, dynamicStrokeStartDistance + at, deltaPixels);
                    }
                    segmentStamps.Add(stamp);
                }
            }
            if (dynamic && count > 0)
            {
                // Keep the unscattered, unwrapped position across input events and clipping.
                double lastOffset = pressureSpacing ? previousOffset : first + (count - 1) * (double)spacing;
                float lastT = distance <= 0f ? 0f : (float)(lastOffset / distance);
                dynamicPreviousStampPoint = Vector2.LerpUnclamped(from, to, lastT);
                dynamicHasPreviousStamp = true;
            }
            brushDistance += distance;
            if (dynamic) dynamics.Sequence.Advance(distance);
        }

        private static float PressureSpacingAt(double offset, float distance, float fromPressure, PaintStrokeParameters parameters)
        {
            float t = distance <= 0f ? 0f : Mathf.Clamp01((float)(offset / distance));
            return parameters.SpacingAtPressure(Mathf.Lerp(fromPressure, parameters.Pressure, t));
        }

        private void RememberFirstBrush(Vector2 position, int width, int height, PaintStrokeParameters parameters)
        {
            firstBrushStamps ??= new System.Collections.Generic.List<PaintStamp>();
            firstBrushStamps.Clear();
            firstBrushStamps.AddRange(segmentStamps);
            firstBrushPosition = position;
            firstBrushWidth = width; firstBrushHeight = height;
            firstBrushSpacing = parameters.SpacingAtPressure(parameters.Pressure);
            firstBrushAngle = brushDirection;
            firstBrushParameters = parameters.WithDynamics(parameters.Dynamics.SnapshotForStroke());
            firstBrushPending = true;
        }

        private void ClearFirstBrush()
        {
            firstBrushPending = false;
            firstBrushStamps?.Clear();
            firstBrushParameters = default;
        }

        private void RefineFirstBrush(Vector2 point)
        {
            if (!firstBrushPending || advancedStroke == null) return;
            Vector2 delta = Vector2.Scale(point - firstBrushPosition, new Vector2(firstBrushWidth, firstBrushHeight));
            // Below the minimum logical spacing (half a pixel), so no continuation dab is lost to jitter.
            if (delta.sqrMagnitude < .0625f) return;
            float actual = Mathf.Atan2(delta.y, delta.x);
            float refined = initialBrushDeltaPixels.sqrMagnitude > .000001f
                ? BrushDynamics.RefineStartAngle(firstBrushAngle, actual)
                : actual;
            Vector2 phantomDelta = new Vector2(Mathf.Cos(refined), Mathf.Sin(refined)) * firstBrushSpacing;
            bool rotate = firstBrushParameters.Dynamics.rotationMode == BrushRotationMode.StrokeDirection;
            for (int i = 0; i < firstBrushStamps.Count; i++)
            {
                PaintStamp stamp = firstBrushStamps[i];
                if (rotate) stamp.rotation += refined - firstBrushAngle;
                var context = stamp.dynamicContext;
                stamp.dynamicContext = new BrushStampContext(context.seed, context.strokeIndex, context.stampIndex,
                    context.distance, context.totalDistance, phantomDelta);
                firstBrushStamps[i] = stamp;
            }
            ResetAdvancedStroke(firstBrushParameters.Dynamics.BlendsEachStamp(firstBrushParameters.Erase), firstBrushParameters.Dynamics.PressureOpacity);
            DrawBrushStamps(paintSurface, firstBrushStamps, firstBrushWidth, firstBrushHeight, firstBrushParameters);
            ClearFirstBrush();
        }

        // The GPU source remains unchanged until commit.
        internal void CancelStroke()
        {
            pixelUndoLabel = null;
            if (advancedStroke != null)
            {
                paintSurfaceDirty = brushBaseDirty;
                unchecked { paintSurfaceRevision++; }
                ReleaseAdvancedStroke();
            }
            EndStroke();
        }

        private void ResetAdvancedStroke(bool withBackdrop, bool pressureOpacity = false)
        {
            brushPreviewValid = false;
            brushInspectorValid = false;
            brushInspectorTiles?.Reset(advancedStroke.width, advancedStroke.height);
            brushPreviewTiles ??= new PaintTileSet();
            brushPreviewTiles.Reset(advancedStroke.width, advancedStroke.height);
            brushDirtyBounds = default;
            brushDirtyTiles ??= new PaintTileSet();
            brushDirtyTiles.Reset(advancedStroke.width, advancedStroke.height);
            advancedStroke.Reset(withBackdrop, pressureOpacity);
        }

        private void EnsureAdvancedStroke(RenderTexture surface, bool withBackdrop = false, bool pressureOpacity = false)
        {
            if (advancedStroke != null) return;
            using var profile = WhimTexProfiler.Measure("brush.allocate_stroke", layer: Owner);
            using var gpu = profile.Gpu();
            var previous = RenderTexture.active;
            try
            {
                brushBaseDirty = paintSurfaceDirty;
                advancedStroke = new PaintStrokeBuffer(surface);
                ResetAdvancedStroke(withBackdrop, pressureOpacity);
            }
            catch { ReleaseAdvancedStroke(); throw; }
            finally { RenderTexture.active = previous; }
        }

        private void ConfigureBrushComposite(Material material)
        {
            material.SetFloat("_PendingBrush", advancedStroke != null ? 1f : 0f);
            if (advancedStroke == null) return;
            var parameters = brushCompositeParameters;
            var dynamics = parameters.Dynamics;
            material.SetTexture("_BrushStroke", null);
            material.SetVector(PaintStrokeBuffer.MapId, new Vector4(1, 1, 0, 0));
            material.SetVector("_BrushDirtyRect", new Vector4(brushDirtyBounds.xMin / (float)advancedStroke.width,
                brushDirtyBounds.yMin / (float)advancedStroke.height, brushDirtyBounds.xMax / (float)advancedStroke.width,
                brushDirtyBounds.yMax / (float)advancedStroke.height));
            material.SetFloat("_StrokeOpacity", dynamics?.opacity ?? 1f);
            material.SetFloat("_StrokeMode", (int)(dynamics?.blend ?? BlendMode.Normal));
            material.SetFloat("_HdrBlend", blendRange == LayerBlendRange.HDR ? 1f : 0f);
            material.SetFloat("_BrushErase", parameters.Erase ? 1f : 0f);
            material.SetFloat("_BrushStandard", colorRange == LayerColorRange.Standard ? 1f : 0f);
            material.SetFloat("_BrushStampAccumulation", dynamics?.BlendsEachStamp(parameters.Erase) == true ? 1f : 0f);
            material.SetFloat("_BrushPressureOpacity", dynamics?.PressureOpacity == true ? 1f : 0f);
            material.SetTexture("_BrushSelection", parameters.SelectionMask);
            material.SetFloat("_BrushUseSelection", parameters.SelectionMask != null ? 1f : 0f);
            material.SetFloat("_BrushSelectionWrap", parameters.WrapCanvas ? 1f : 0f);
            brushCompositeTransform.SetShader(material, "_BrushCanvasRow");
            int channels = parameters.WriteChannels;
            material.SetVector("_BrushWriteChannels", new Vector4((channels & 1) != 0 ? 1 : 0,
                (channels & 2) != 0 ? 1 : 0, (channels & 4) != 0 ? 1 : 0, (channels & 8) != 0 ? 1 : 0));
            material.SetTexture("_BrushOriginalStraight", pixels);
            material.SetFloat("_BrushStraightFallback", pixels != null ? (HdrUtility.IsHdr(pixels) ? 1f : 2f) : 0f);
            material.SetFloat("_BrushGamma", QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f);
        }

        private static void ClearBrushComposite(Material material)
        {
            material.SetFloat("_PendingBrush", 0f);
            material.SetTexture("_BrushStroke", null);
            material.SetTexture("_BrushSelection", null);
            material.SetTexture("_BrushOriginalStraight", null);
        }

        private void CommitBrushStroke()
        {
            if (advancedStroke == null || paintSurface == null) return;
            if (brushDirtyBounds.width <= 0 || brushDirtyBounds.height <= 0) { ReleaseAdvancedStroke(); return; }
            Material material = WhimTexMaterials.Blend;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Brush composite shader is unavailable.");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                ConfigureBrushComposite(material);
                using var profile = WhimTexProfiler.Measure("brush.composite", layer: Owner);
                using (profile.Gpu())
                {
                    if (CommitPixelHistory(material)) { ReleaseAdvancedStroke(); return; }
                    if (brushDirtyTiles != null && brushDirtyTiles.Count > 0 &&
                        !BrushStrokeCompute.TryCommitTiles(paintSurface, brushDirtyTiles.ToArray(), material,
                            advancedStroke, brushCompositeParameters.SelectionMask, pixels, alphaCoverage))
                    {
                        InvalidateAlphaCoverage();
                        for (int i = 0; i < brushDirtyTiles.Count; i++) CommitBrushTile(material, brushDirtyTiles[i]);
                    }
                }
                ReleaseAdvancedStroke();
            }
            finally
            {
                ClearBrushComposite(material);
                material.SetTexture("_MainTex", null);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }

        private void ReleaseAdvancedStroke()
        {
            ReleaseBrushPreview();
            ReleaseBrushInspector();
            ClearFirstBrush();
            advancedStroke?.Dispose();
            advancedStroke = null;
            brushCompositeParameters = default;
            brushDirtyBounds = default;
            brushDirtyTiles?.Clear();
        }

        private void IncludeBrushBounds(Rect uv)
        {
            if (uv.width <= 0f || uv.height <= 0f || advancedStroke == null) return;
            brushDirtyTiles?.Union(PaintBrushRenderer.DrawTiles);
            brushPreviewTiles?.Union(PaintBrushRenderer.DrawTiles);
            brushInspectorTiles?.Union(PaintBrushRenderer.DrawTiles);
            // One source texel guards bilinear sampling and rasterization at a quad boundary.
            int x = Mathf.Max(0, Mathf.FloorToInt(uv.xMin * advancedStroke.width) - 1);
            int y = Mathf.Max(0, Mathf.FloorToInt(uv.yMin * advancedStroke.height) - 1);
            int right = Mathf.Min(advancedStroke.width, Mathf.CeilToInt(uv.xMax * advancedStroke.width) + 1);
            int top = Mathf.Min(advancedStroke.height, Mathf.CeilToInt(uv.yMax * advancedStroke.height) + 1);
            if (brushDirtyBounds.width > 0 && brushDirtyBounds.height > 0)
            {
                x = Mathf.Min(x, brushDirtyBounds.xMin); y = Mathf.Min(y, brushDirtyBounds.yMin);
                right = Mathf.Max(right, brushDirtyBounds.xMax); top = Mathf.Max(top, brushDirtyBounds.yMax);
            }
            brushDirtyBounds = new RectInt(x, y, right - x, top - y);
        }

        private static void CopyBrushRegion(RenderTexture source, RenderTexture target, RectInt bounds)
        {
            RenderTexture.active = null;
            if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)
                Graphics.CopyTexture(source, 0, 0, 0, 0, bounds.width, bounds.height, target, 0, 0, bounds.x, bounds.y);
            else
                CopyBrushRegionWithShader(source, target, bounds);
        }

        private static void CopyBrushRegionWithShader(Texture source, RenderTexture target, RectInt bounds)
        {
            var material = WhimTexMaterials.Blend;
            try
            {
                DrawBrushRegion(source, target, material, 3,
                    new Rect(bounds.x / (float)target.width, bounds.y / (float)target.height,
                        bounds.width / (float)target.width, bounds.height / (float)target.height),
                    new Rect(0, 0, bounds.width / (float)source.width, bounds.height / (float)source.height));
            }
            finally { material.SetTexture("_MainTex", null); }
        }

        internal static void DrawBrushRegion(Texture source, RenderTexture target, Material material, int pass, Rect destination, Rect uv)
        {
            material.SetTexture("_MainTex", source);
            RenderTexture.active = target;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                if (!material.SetPass(pass)) throw new InvalidOperationException("Cannot render brush region.");
                GL.Begin(GL.QUADS);
                GL.TexCoord2(uv.xMin, uv.yMin); GL.Vertex3(destination.xMin, destination.yMin, 0);
                GL.TexCoord2(uv.xMin, uv.yMax); GL.Vertex3(destination.xMin, destination.yMax, 0);
                GL.TexCoord2(uv.xMax, uv.yMax); GL.Vertex3(destination.xMax, destination.yMax, 0);
                GL.TexCoord2(uv.xMax, uv.yMin); GL.Vertex3(destination.xMax, destination.yMin, 0);
                GL.End();
            }
            finally { GL.PopMatrix(); }
        }
    }
}
