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
        [NonSerialized] private RenderTexture advancedStroke, advancedStrokeBase;
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
            int count = brushSpacingState.Sample(distance, parameters.SpacingPixels, includeStart, out double first);
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, budget)));
            BrushDynamics dynamics = parameters.Dynamics;
            float fromPressure = hasBrushPressure && !includeStart ? previousBrushPressure : parameters.Pressure;
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
            for (long i = 0; i < count; i += stride)
            {
                uint stampIndex = unchecked(firstStamp + (uint)i);
                float t = distance <= 0f ? 0f : (float)((first + i * (double)parameters.SpacingPixels) / distance);
                Vector2 point = Vector2.LerpUnclamped(from, to, t);
                Vector2 deltaPixels = initialBrushDeltaPixels.sqrMagnitude > .000001f
                    ? initialBrushDeltaPixels.normalized * parameters.SpacingPixels : Vector2.zero;
                if (dynamic && (i > 0 || dynamicHasPreviousStamp))
                {
                    // Use the previous logical stamp, even when the render budget skips it.
                    Vector2 previous = i > 0
                        ? Vector2.LerpUnclamped(from, to, distance <= 0f ? 0f :
                            (float)((first + (i - 1) * (double)parameters.SpacingPixels) / distance))
                        : dynamicPreviousStampPoint;
                    deltaPixels = Vector2.Scale(point - previous, new Vector2(width, height));
                }
                float pressure = Mathf.Lerp(fromPressure, parameters.Pressure, Mathf.Clamp01(t));
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
                if (!parameters.WrapCanvas && (point.x + size * extent * .5f / width <= 0f || point.x - size * extent * .5f / width >= 1f ||
                    point.y + size * extent * .5f / height <= 0f || point.y - size * extent * .5f / height >= 1f)) continue;
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
                        double at = brushDistance + Math.Min(distance, Math.Max(0d, first + i * (double)parameters.SpacingPixels));
                        stamp.dynamicContext = new BrushStampContext(dynamicStroke.seed, dynamicStroke.strokeIndex, stampIndex,
                            at, dynamicStrokeStartDistance + at, deltaPixels);
                    }
                    segmentStamps.Add(stamp);
                }
            }
            if (dynamic && count > 0)
            {
                // Keep the unscattered, unwrapped position across input events and clipping.
                float lastT = distance <= 0f ? 0f : (float)((first + (count - 1) * (double)parameters.SpacingPixels) / distance);
                dynamicPreviousStampPoint = Vector2.LerpUnclamped(from, to, lastT);
                dynamicHasPreviousStamp = true;
            }
            brushDistance += distance;
            if (dynamic) dynamics.Sequence.Advance(distance);
        }

        private void RememberFirstBrush(Vector2 position, int width, int height, PaintStrokeParameters parameters)
        {
            firstBrushStamps ??= new System.Collections.Generic.List<PaintStamp>();
            firstBrushStamps.Clear();
            firstBrushStamps.AddRange(segmentStamps);
            firstBrushPosition = position;
            firstBrushWidth = width; firstBrushHeight = height;
            firstBrushSpacing = parameters.SpacingPixels;
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
            if (!firstBrushPending || advancedStrokeBase == null) return;
            Vector2 delta = Vector2.Scale(point - firstBrushPosition, new Vector2(firstBrushWidth, firstBrushHeight));
            // Below the minimum logical spacing (one pixel), so no continuation dab is lost to jitter.
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

        // Explicit cancellation restores the GPU source, never the quantized storage texture.
        internal void CancelStroke()
        {
            if (advancedStrokeBase != null && paintSurface != null)
            {
                var previous = RenderTexture.active;
                try { Graphics.Blit(advancedStrokeBase, paintSurface); unchecked { paintSurfaceRevision++; } }
                finally { RenderTexture.active = previous; }
            }
            EndStroke();
        }

        private void ResetAdvancedStroke(bool withBackdrop, bool pressureOpacity = false)
        {
            var previous = RenderTexture.active;
            try
            {
                if (withBackdrop && pressureOpacity) Graphics.Blit(advancedStrokeBase, advancedStroke, WhimTexMaterials.Blend, 2);
                else if (withBackdrop) Graphics.Blit(advancedStrokeBase, advancedStroke);
                else { RenderTexture.active = advancedStroke; GL.Clear(false, true, Color.clear); }
            }
            finally { RenderTexture.active = previous; }
        }

        private void EnsureAdvancedStroke(RenderTexture surface, bool withBackdrop = false, bool pressureOpacity = false)
        {
            if (advancedStroke != null) return;
            var previous = RenderTexture.active;
            try
            {
                advancedStrokeBase = HdrUtility.Temporary(surface.width, surface.height);
                advancedStroke = HdrUtility.Temporary(surface.width, surface.height);
                Graphics.Blit(surface, advancedStrokeBase);
                ResetAdvancedStroke(withBackdrop, pressureOpacity);
            }
            catch { ReleaseAdvancedStroke(); throw; }
            finally { RenderTexture.active = previous; }
        }

        private void CompositeAdvancedStroke(RenderTexture target, PaintStrokeParameters parameters)
        {
            Material material = WhimTexMaterials.Blend;
            if (material == null) return;
            var previous = RenderTexture.active;
            try
            {
                material.SetTexture("_Blend", advancedStroke);
                material.SetFloat("_Opacity", parameters.Dynamics.opacity);
                material.SetFloat("_Mode", (int)parameters.Dynamics.blend);
                material.SetFloat("_HdrBlend", blendRange == LayerBlendRange.HDR ? 1f : 0f);
                material.SetFloat("_BrushErase", parameters.Erase ? 1f : 0f);
                material.SetFloat("_BrushStandard", colorRange == LayerColorRange.Standard ? 1f : 0f);
                material.SetFloat("_BrushStampAccumulation", parameters.Dynamics.BlendsEachStamp(parameters.Erase) ? 1f : 0f);
                material.SetFloat("_BrushPressureOpacity", parameters.Dynamics.PressureOpacity ? 1f : 0f);
                Graphics.Blit(advancedStrokeBase, target, material, 1);
            }
            finally
            {
                material.SetTexture("_Blend", null);
                material.SetFloat("_BrushStampAccumulation", 0f);
                material.SetFloat("_BrushPressureOpacity", 0f);
                RenderTexture.active = previous;
            }
        }

        private void ReleaseAdvancedStroke()
        {
            ClearFirstBrush();
            if (advancedStroke != null) RenderTexture.ReleaseTemporary(advancedStroke);
            if (advancedStrokeBase != null) RenderTexture.ReleaseTemporary(advancedStrokeBase);
            advancedStroke = advancedStrokeBase = null;
        }
    }
}
