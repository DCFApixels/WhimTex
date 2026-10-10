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
        [NonSerialized] private bool brushTintPrepared;
        [NonSerialized] private DynamicBrushContext dynamicStroke;
        [NonSerialized] private bool dynamicStrokeStarted;
        [NonSerialized] private double brushDistance;
        [NonSerialized] private double dynamicStrokeStartDistance;
        [NonSerialized] private Vector2 dynamicPreviousStampPoint;
        [NonSerialized] private bool dynamicHasPreviousStamp;
        [NonSerialized] private RenderTexture advancedStroke, advancedStrokeBase;

        private void BuildBrushSegment(Vector2 from, Vector2 to, int width, int height, bool includeStart,
            PaintStrokeParameters parameters, float distance, int budget)
        {
            int count = brushSpacingState.Sample(distance, parameters.SpacingPixels, includeStart, out double first);
            int stride = Mathf.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, budget)));
            BrushDynamics dynamics = parameters.Dynamics;
            bool dynamic = dynamics?.DynamicTip == true;
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
                Vector2 deltaPixels = Vector2.zero;
                if (dynamic && (i > 0 || dynamicHasPreviousStamp))
                {
                    // Use the previous logical stamp, even when the render budget skips it.
                    Vector2 previous = i > 0
                        ? Vector2.LerpUnclamped(from, to, distance <= 0f ? 0f :
                            (float)((first + (i - 1) * (double)parameters.SpacingPixels) / distance))
                        : dynamicPreviousStampPoint;
                    deltaPixels = Vector2.Scale(point - previous, new Vector2(width, height));
                }
                float size = parameters.Size;
                float rotation = baseRotation;
                int flip = 0;
                Color tint = parameters.Color;
                tint.a *= parameters.Pressure;
                if (variation)
                {
                    if (dynamics.scatter > 0f)
                    {
                        float angle = dynamics.SampleRandom(ref brushRandomState, stampIndex, 0) * Mathf.PI * 2f;
                        float radius = BrushDynamics.ScatterRadius(dynamics.SampleRandom(ref brushRandomState, stampIndex, 1), scatterExponent) * dynamics.scatter * size;
                        point += new Vector2(Mathf.Cos(angle) * radius / width, Mathf.Sin(angle) * radius / height);
                    }
                    if (dynamics.sizeJitter > 0f)
                        size = Mathf.Max(1f, size * (1f + dynamics.sizeJitter * (2f * dynamics.SampleRandom(ref brushRandomState, stampIndex, 2) - 1f)));
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
                    if (dynamic)
                    {
                        double at = brushDistance + Math.Min(distance, Math.Max(0d, first + i * (double)parameters.SpacingPixels));
                        stamp.dynamicContext = new DynamicBrushContext(dynamicStroke.seed, dynamicStroke.strokeIndex, stampIndex,
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

        private void EnsureAdvancedStroke(RenderTexture surface, bool withBackdrop = false)
        {
            if (advancedStroke != null) return;
            var previous = RenderTexture.active;
            try
            {
                advancedStrokeBase = HdrUtility.Temporary(surface.width, surface.height);
                advancedStroke = HdrUtility.Temporary(surface.width, surface.height);
                Graphics.Blit(surface, advancedStrokeBase);
                if (withBackdrop) Graphics.Blit(advancedStrokeBase, advancedStroke);
                else
                {
                    RenderTexture.active = advancedStroke;
                    GL.Clear(false, true, Color.clear);
                }
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
                Graphics.Blit(advancedStrokeBase, target, material, 1);
            }
            finally
            {
                material.SetTexture("_Blend", null);
                material.SetFloat("_BrushStampAccumulation", 0f);
                RenderTexture.active = previous;
            }
        }

        private void ReleaseAdvancedStroke()
        {
            if (advancedStroke != null) RenderTexture.ReleaseTemporary(advancedStroke);
            if (advancedStrokeBase != null) RenderTexture.ReleaseTemporary(advancedStrokeBase);
            advancedStroke = advancedStrokeBase = null;
        }
    }
}
