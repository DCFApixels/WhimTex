using System;
using UnityEngine;
using WhimTex.Tests;

public static class DisplayChannelsTests
{
    public static string Run() => TestContext.Run("All 16 display-channel masks", context =>
    {
        var shader = Shader.Find("Hidden/WhimTex/DisplayChannels");
        if (shader == null || !shader.isSupported) throw new Exception("Display channel shader is unavailable.");
        var samples = new[] {
            new Color32(51, 128, 204, 64), new Color32(255, 0, 0, 255),
            new Color32(0, 255, 0, 128), new Color32(0, 0, 255, 0),
            new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 0)
        };
        Texture2D source = null, readback = null;
        RenderTexture target = null;
        Material material = null;
        var previousActive = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            source = new Texture2D(samples.Length, 1, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            source.SetPixels32(samples); source.Apply(false, false);
            readback = new Texture2D(samples.Length, 1, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            target = RenderTexture.GetTemporary(samples.Length, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false;
            for (int mask = 0; mask < 16; mask++)
            {
                material.SetVector("_Channels", new Vector4((mask & 1) != 0 ? 1 : 0,
                    (mask & 2) != 0 ? 1 : 0, (mask & 4) != 0 ? 1 : 0, (mask & 8) != 0 ? 1 : 0));
                Graphics.Blit(source, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, samples.Length, 1), 0, 0, false);
                var actual = readback.GetPixels32();
                for (int i = 0; i < samples.Length; i++)
                {
                    Color32 input = samples[i], expected;
                    if (QualitySettings.activeColorSpace == ColorSpace.Gamma) input = ((Color)input).gamma;
                    int rgb = mask & 7;
                    byte alpha = (mask & 8) != 0 ? input.a : (byte)255;
                    if (rgb == 0)
                    {
                        byte value = (mask & 8) != 0 ? input.a : (byte)0;
                        expected = new Color32(value, value, value, 255);
                    }
                    else if (rgb == 1 || rgb == 2 || rgb == 4)
                    {
                        byte value = rgb == 1 ? input.r : rgb == 2 ? input.g : input.b;
                        expected = new Color32(value, value, value, alpha);
                    }
                    else expected = new Color32((rgb & 1) != 0 ? input.r : (byte)0,
                        (rgb & 2) != 0 ? input.g : (byte)0, (rgb & 4) != 0 ? input.b : (byte)0, alpha);
                    var pixel = actual[i];
                    context.True(Math.Abs(pixel.r - expected.r) <= 1 && Math.Abs(pixel.g - expected.g) <= 1
                        && Math.Abs(pixel.b - expected.b) <= 1 && Math.Abs(pixel.a - expected.a) <= 1,
                        $"Channel mask {mask}, sample {i}: expected {expected}, got {pixel}.");
                }
            }
        }
        finally
        {
            RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgbWrite;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
        }
    });
}
