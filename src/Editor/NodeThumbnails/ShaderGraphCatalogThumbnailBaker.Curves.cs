using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        const int CurveSamples = 256;
        static bool HasCurve(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.SampleCurve or
            ShaderGraphNodeKind.Power or ShaderGraphNodeKind.Smoothstep or ShaderGraphNodeKind.TriangleWave or
            ShaderGraphNodeKind.RangeMask or ShaderGraphNodeKind.Step or ShaderGraphNodeKind.SawtoothWave or
            ShaderGraphNodeKind.SquareWave or ShaderGraphNodeKind.Absolute or ShaderGraphNodeKind.Saturate;

        static float[] ReadCurveSamples(ShaderProgramRuntime runtime, in LayerRenderContext context)
        {
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture strip = null;
            Texture2D readback = null;
            try
            {
                strip = RenderTexture.GetTemporary(CurveSamples, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                readback = new Texture2D(CurveSamples, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                GL.sRGBWrite = false;
                runtime.Render(runtime.EmptyInput, strip, context);
                RenderTexture.active = strip;
                readback.ReadPixels(new Rect(0, 0, CurveSamples, 1), 0, 0); readback.Apply();
                var data = readback.GetPixels();
                var values = new float[CurveSamples];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = data[i].r;
                    if (!float.IsFinite(values[i])) throw new InvalidOperationException("Non-finite catalog curve sample.");
                }
                return values;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (strip != null) RenderTexture.ReleaseTemporary(strip);
            }
        }

        static void DrawCurve(ShaderGraphNodeKind kind, Color[] tile, float[] values)
        {
            if (!HasCurve(kind)) return;
            if (values == null || values.Length != CurveSamples || tile.Length != Cell * Cell)
                throw new ArgumentException("Invalid catalog curve samples or tile.");
            const float display = ShaderGraphCatalogThumbnails.DisplaySize;
            bool smoothstep = kind == ShaderGraphNodeKind.Smoothstep;
            bool discontinuous = kind is ShaderGraphNodeKind.Step or ShaderGraphNodeKind.SquareWave or ShaderGraphNodeKind.SawtoothWave;
            float inset = smoothstep ? .6f : .75f, radius = smoothstep ? .4f : .525f;
            float outline = smoothstep ? .575f : .95f, shadow = smoothstep ? .55f : .75f;
            var green = new Color(.36f, .86f, .32f, 1);
            var dark = new Color(.025f, .055f, .025f, 1);
            var points = new Vector2[values.Length + 2];
            float Y(float value) => inset + Mathf.Clamp01(value) * (display - 2 * inset);
            for (int i = 0; i < values.Length; i++) points[i + 1] = new Vector2((i + .5f) / values.Length * display, Y(values[i]));
            points[0] = new Vector2(0, Y(values[0] * 1.5f - values[1] * .5f));
            points[points.Length - 1] = new Vector2(display, Y(values[values.Length - 1] * 1.5f - values[values.Length - 2] * .5f));
            const int aa = 4;
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                int pixel = (y + Padding) * Cell + x + Padding;
                var background = tile[pixel];
                var sum = Color.clear;
                for (int sy = 0; sy < aa; sy++) for (int sx = 0; sx < aa; sx++)
                {
                    var p = new Vector2(x + (sx + .5f) / aa, y + (sy + .5f) / aa) * (display / Size);
                    int first = Mathf.Clamp(Mathf.FloorToInt((p.x - outline) / display * values.Length), 1, points.Length - 1);
                    int last = Mathf.Clamp(Mathf.CeilToInt((p.x + outline) / display * values.Length) + 2, 1, points.Length - 1);
                    float distanceSquared = float.PositiveInfinity;
                    for (int i = first; i <= last; i++)
                    {
                        var a = points[i - 1]; var b = points[i];
                        if (discontinuous && Mathf.Abs(a.y - b.y) > display * .5f)
                        {
                            // Draw the jump vertically, without interpolating it into a sloped ramp.
                            var left = new Vector2((a.x + b.x) * .5f, a.y);
                            var right = new Vector2(left.x, b.y);
                            distanceSquared = Mathf.Min(distanceSquared, Mathf.Min(CurveSegmentDistanceSquared(p, a, left),
                                Mathf.Min(CurveSegmentDistanceSquared(p, left, right), CurveSegmentDistanceSquared(p, right, b))));
                        }
                        else distanceSquared = Mathf.Min(distanceSquared, CurveSegmentDistanceSquared(p, a, b));
                    }
                    var color = distanceSquared < outline * outline ? Color.Lerp(background, dark, shadow) : background;
                    sum += distanceSquared < radius * radius ? green : color;
                }
                tile[pixel] = sum / (aa * aa);
            }
            // Extrude the final line as well as its background into the atlas gutter.
            for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
                if (x < Padding || y < Padding || x >= Size + Padding || y >= Size + Padding)
                    tile[y * Cell + x] = tile[Mathf.Clamp(y, Padding, Size + Padding - 1) * Cell + Mathf.Clamp(x, Padding, Size + Padding - 1)];
        }

        static float CurveSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, delta) / Mathf.Max(delta.sqrMagnitude, 1e-10f));
            return (point - start - delta * t).sqrMagnitude;
        }
    }
}
