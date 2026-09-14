using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal void RenderBrushPreview(RenderTexture target, PaintStrokeParameters parameters, Material display)
        {
            int width = target.width, height = target.height;
            RenderTexture previous = RenderTexture.active;
            RenderTexture straight = null;
            try
            {
                RenderTexture surface = EnsurePaintSurface(width, height);
                RenderTexture.active = surface;
                GL.Clear(false, true, parameters.Erase ? HdrUtility.Decode(new Color(.65f, .65f, .65f, 1f)) : Color.clear);
                Vector2 from = BrushPreviewPoint(0f, width, height);
                BeginStroke(from);
                const int segments = 96;
                for (int i = 1; i <= segments; i++)
                {
                    Vector2 to = BrushPreviewPoint(i / (float)segments, width, height);
                    PaintSegment(from, to, width, height, i == 1, parameters);
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
                RenderTexture.active = previous;
                if (straight != null) RenderTexture.ReleaseTemporary(straight);
            }
        }

        internal static Vector2 BrushPreviewPoint(float t, int width, int height)
        {
            float margin = Mathf.Min(height * .45f, width * .2f) / width;
            return new Vector2(Mathf.Lerp(margin, 1f - margin, t), .5f + .18f * Mathf.Sin(t * Mathf.PI * 2f));
        }
    }
}
