using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static bool IsSymbolExample(ShaderGraphNodeKind kind) =>
            kind is ShaderGraphNodeKind.IsNaN or ShaderGraphNodeKind.IsInfinite or
                ShaderGraphNodeKind.Constant or ShaderGraphNodeKind.Value || CatalogArtwork.ContainsKey(kind);

        static Color[] SymbolTile(ShaderGraphNodeKind kind)
        {
            if (CatalogArtwork.TryGetValue(kind, out var draw))
            {
                var painter = new ThumbnailPainter();
                draw(painter);
                return painter.Finish();
            }

            const float display = ShaderGraphCatalogThumbnails.DisplaySize;
            var strokes = kind switch
            {
                ShaderGraphNodeKind.IsNaN => new[] {
                    CapitalN(3.5f),
                    SampleLoop(t => new Vector2(15.8f + 3.1f * Mathf.Cos(t), 14.9f + 4.1f * Mathf.Sin(t))),
                    new[] { new Vector2(18.9f, 10.8f), new Vector2(18.9f, 19.0f) },
                    CapitalN(22)
                },
                ShaderGraphNodeKind.Constant => new[] {
                    new[] { new Vector2(6.5f, 20.5f), new Vector2(8.5f, 22), new Vector2(25.5f, 22) },
                    new[] { new Vector2(11.5f, 22), new Vector2(11, 14), new Vector2(10, 10), new Vector2(8.5f, 8.5f) },
                    new[] { new Vector2(21, 22), new Vector2(20, 12), new Vector2(20.5f, 9.5f), new Vector2(22, 8.5f), new Vector2(24, 9.5f) }
                },
                ShaderGraphNodeKind.Value => new[] {
                    new[] { new Vector2(4.5f, 19.5f), new Vector2(8, 22), new Vector2(8, 10) },
                    new[] { new Vector2(4.5f, 10), new Vector2(11.5f, 10) },
                    new[] { new Vector2(15.5f, 10), new Vector2(15.5f, 10) },
                    SampleLoop(t => new Vector2(23.5f + 3.8f * Mathf.Cos(t), 16 + 6 * Mathf.Sin(t)))
                },
                _ => new[] { SampleLoop(t => new Vector2(16 + 10.5f * Mathf.Sin(t), 16 + 5.7f * Mathf.Sin(2 * t))) }
            };

            var tile = new Color[Cell * Cell];
            var background = new Color(.13f, .14f, .16f, 1);
            var ink = new Color(.36f, .86f, .92f, 1);
            for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
            {
                var p = new Vector2(Mathf.Clamp(x - Padding, 0, Size - 1) + .5f,
                    Mathf.Clamp(y - Padding, 0, Size - 1) + .5f) * (display / Size);
                float nearest = float.PositiveInfinity;
                foreach (var stroke in strokes)
                    for (int i = 1; i < stroke.Length; i++)
                        nearest = Mathf.Min(nearest, CurveSegmentDistanceSquared(p, stroke[i - 1], stroke[i]));
                float coverage = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1.25f, Mathf.Sqrt(nearest)));
                tile[y * Cell + x] = Color.Lerp(background, ink, coverage);
            }
            return tile;
        }

        static Vector2[] CapitalN(float x) => new[] {
            new Vector2(x, 10.8f), new Vector2(x, 22.2f),
            new Vector2(x + 6.2f, 10.8f), new Vector2(x + 6.2f, 22.2f)
        };

        static Vector2[] SampleLoop(Func<float, Vector2> evaluate)
        {
            var points = new Vector2[65];
            for (int i = 0; i < points.Length - 1; i++) points[i] = evaluate(i * (2 * Mathf.PI / (points.Length - 1)));
            points[points.Length - 1] = points[0];
            return points;
        }
    }
}
