using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal void RenderBrushPreview(RenderTexture target, PaintStrokeParameters parameters, Material display, float marginScale = 1f)
        {
            // Preview the tip itself; write locks depend on existing document pixels.
            parameters = parameters.WithWriteProtection(15);
            if (parameters.Dynamics != null)
            {
                var preview = JsonUtility.FromJson<BrushDynamics>(JsonUtility.ToJson(parameters.Dynamics));
                preview.tip = parameters.Dynamics.tip;
                preview.BorrowHlsl(parameters.Dynamics);
                preview.Normalize();
                parameters = parameters.WithDynamics(preview);
            }
            int width = target.width, height = target.height;
            RenderTexture previous = RenderTexture.active;
            RenderTexture straight = null;
            try
            {
                RenderTexture surface = EnsurePaintSurface(width, height);
                RenderTexture.active = surface;
                GL.Clear(false, true, parameters.Erase ? HdrUtility.Decode(new Color(.65f, .65f, .65f, 1f)) : Color.clear);
                Vector2 from = BrushPreviewPoint(0f, width, height, marginScale);
                BeginStroke(from);
                const int segments = 96;
                SetKnownBrushDirection(Vector2.Scale(BrushPreviewPoint(1f / segments, width, height, marginScale) - from,
                    new Vector2(width, height)));
                PaintPoint(from, width, height, parameters.WithPressure(BrushPreviewPressure(0f)));
                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector2 to = BrushPreviewPoint(t, width, height, marginScale);
                    PaintSegment(from, to, width, height, false, parameters.WithPressure(BrushPreviewPressure(t)));
                    from = to;
                }
                straight = HdrUtility.Temporary(width, height);
                Material conversion = WhimTexMaterials.AlphaConversion;
                if (conversion == null) return;
                conversion.SetFloat("_Mode", 1f);
                Graphics.Blit(surface, straight, conversion);
                Graphics.Blit(straight, target, display);
            }
            finally
            {
                EndStroke();
                parameters.Dynamics?.ReleaseHlsl();
                RenderTexture.active = previous;
                if (straight != null) ProfiledRenderTexture.ReleaseTemporary(straight);
            }
        }

        internal static Vector2 BrushPreviewPoint(float t, int width, int height, float marginScale = 1f)
        {
            float margin = Mathf.Min(height * .45f, width * .2f) / width * marginScale;
            // Short straight tails follow the curve's endpoint tangents, within the same preview margins.
            const float tail = .12f;
            float phase = t * (1f + 2f * tail) - tail;
            float curvePhase = Mathf.Clamp01(phase);
            float wave = Mathf.Sin(curvePhase * Mathf.PI * 2f) + (phase - curvePhase) * Mathf.PI * 2f;
            return new Vector2(Mathf.Lerp(margin, 1f - margin, t), .5f + .18f * wave);
        }

        internal static float BrushPreviewPressure(float t) => Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(Mathf.Min(t, 1f - t) / .3f));
    }
}
