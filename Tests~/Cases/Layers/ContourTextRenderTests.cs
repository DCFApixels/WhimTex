using System;
using System.IO;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ContourTextRenderTests
{
    public static string Run() => TestContext.Run("Native text inputs: Round AA / AAEDT / Contour AA comparison", t =>
    {
        const int width = 384, height = 80;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = width; doc.height = height;
        var text = new TextLayerBehaviour { text = "Text 123 ЧЧ", fontSize = 28 };
        var sdf = new SDFLayerBehaviour { metric = DistanceMetric.Contour, maxDistanceNormalization = 4,
            encoding = SDFLayerBehaviour.OutputEncoding.LinearData, inputMode = EffectInputMode.Specific };
        doc.layers.Add(text.Owner);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D sheet = null;
        try
        {
            t.True(!string.IsNullOrEmpty(text.fontFamily), "An installed font is available");
            sheet = new Texture2D(width * 4, height * 6, TextureFormat.RGBA32, false, true);
            int row = 0;
            foreach (TextEdgeMode edge in Enum.GetValues(typeof(TextEdgeMode)))
            foreach (float phase in new[] { 0f, .37f })
            {
                text.edgeMode = edge;
                var transform = TextureTransform.Default; transform.position = new Vector2(phase, -phase);
                text.Owner.transform = transform;
                sdf.Owner.enabled = false;
                var source = doc.ComposeCanvas();
                Color32[] mask;
                try { mask = source.GetPixels32(); }
                finally { UnityEngine.Object.DestroyImmediate(source); }
                int ink = 0;
                var raw = new Color32[mask.Length];
                for (int i = 0; i < mask.Length; i++)
                {
                    if (mask[i].a > 128) ink++;
                    byte gray = (byte)(255 - mask[i].a); raw[i] = new Color32(gray,gray,gray,255);
                }
                t.True(ink > 100, "Real text coverage " + edge + "/" + phase);
                sheet.SetPixels32(0, (5-row)*height, width, height, raw);
                if (!doc.layers.Contains(sdf.Owner)) doc.layers.Insert(0, sdf.Owner);
                sdf.TargetLayerId = text.Owner.Id; sdf.Owner.enabled = true;
                int column = 1;
                foreach (var mode in new[] { DistanceMetric.EuclideanAntialiased, DistanceMetric.AAEDT, DistanceMetric.Contour })
                {
                    sdf.metric = mode;
                    var image = doc.ComposeCanvas();
                    try
                    {
                        var pixels = image.GetPixels();
                        int dark = 0;
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            Color p = pixels[i];
                            t.True(float.IsFinite(p.r) && p.r >= 0 && p.r <= 1, "Finite native text field " + mode);
                            t.Near(1, p.a, .001, "Opaque scalar output " + mode);
                            if (p.r < .49f) dark++;
                            if (mode == DistanceMetric.Contour && mask[i].a != 128)
                                t.True(mask[i].a > 128 ? p.r <= .5001f : p.r >= .4999f, "Contour agrees with sampled text threshold");
                        }
                        t.True(dark > 50, "Text retains negative interior " + mode);
                        sheet.SetPixels(column*width, (5-row)*height, width, height, pixels);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                    column++;
                }
                row++;
            }
            sheet.Apply();
            string directory = Path.GetFullPath("Temp/WhimTex/distance-fields/contour"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,"text-source-round-aaedt-contour.png"),sheet.EncodeToPNG());
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, "Text/SDF composition preserves caller render state");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
            UnityEngine.Object.DestroyImmediate(doc);
        }
    });
}
