using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class TextHorizontalScaleTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(WhimTexDocument).Assembly;

    public static string Run() => TestContext.Run("Horizontal Scale glyph sharpness and layout", t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        document.width = 384; document.height = 128;
        string family = (string)Product.GetType("DCFApixels.WhimTex.SystemFontCatalog", true)
            .GetProperty("DefaultName", Flags).GetValue(null);
        var text = new TextLayerBehaviour { text = "HHHH", fontFamily = family, fontSize = 24,
            layoutMode = TextLayoutMode.Frame, frameSize = new Vector2(384, 128),
            alignment = TextAnchor.MiddleCenter, wrapping = TextWrapping.Manual, overflow = TextOverflowMode.Clip };
        var layer = new Layer(text);
        typeof(Layer).GetMethod("AssignNewId", Flags).Invoke(layer, null);
        document.layers.Add(layer);
        var scales = new[] { .5f, 1f, 1.25f, 2f, 4f };
        var edges = new float[scales.Length];
        var bounds = new Rect[scales.Length];
        var proof = new Texture2D(document.width, document.height * scales.Length, TextureFormat.RGBA32, false, true);
        try
        {
            for (int n = 0; n < scales.Length; n++)
            {
                text.characterHorizontalScale = scales[n];
                var image = document.ComposeCanvas();
                try
                {
                    var pixels = image.GetPixels();
                    proof.SetPixels(0, n * document.height, document.width, document.height, pixels);
                    int left = document.width, right = -1, bottom = document.height, top = -1;
                    for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > .05f)
                    {
                        int x = i % document.width, y = i / document.width;
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    }
                    t.True(right >= left && top >= bottom, "Visible glyphs at " + scales[n]);
                    bounds[n] = Rect.MinMaxRect(left, bottom, right + 1, top + 1);
                    int fractional = 0;
                    foreach (float height in new[] { .25f, .75f })
                    {
                        int y = bottom + Mathf.RoundToInt((top - bottom) * height);
                        for (int x = left; x <= right; x++)
                        {
                            float alpha = pixels[y * document.width + x].a;
                            if (alpha > .05f && alpha < .95f) fractional++;
                        }
                    }
                    // Four H glyphs, two stems, two edges, sampled on two rows.
                    edges[n] = fractional / 32f;
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            proof.Apply();
            string folder = "Temp/WhimTex/text-scale-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "proof.png");
            File.WriteAllBytes(path, proof.EncodeToPNG());
            Debug.Log("WhimTex text scale proof: " + path + "; edge widths: " + string.Join(", ", edges));
            for (int n = 0; n < scales.Length; n++)
            {
                t.Near(bounds[1].height, bounds[n].height, 2, "Horizontal Scale preserves height " + scales[n]);
                t.Near(bounds[1].width * scales[n], bounds[n].width, 4, "Horizontal Scale preserves width multiplier " + scales[n]);
            }
            t.True(edges[4] <= edges[1] + 1f, "Stretching to 4x must not stretch antialiasing to broad blurry bands: " + string.Join(", ", edges));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(proof);
            UnityEngine.Object.DestroyImmediate(document);
        }
    });
}
