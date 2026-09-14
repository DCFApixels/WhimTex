// Unity Pipeline run_script, entry DrawingResolutionSmoke.Main. Transient data only.
using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.SpriteEditor;

public static class DrawingResolutionSmoke
{
    public static string Main()
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var source = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
        Texture2D readback = null;
        RenderTexture output = null;
        var previous = RenderTexture.active;
        try
        {
            document.width = document.height = 64;
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                    pixels[y * 256 + x] = new Color32((byte)((x & 1) * 255), 0, 0, 128);
            source.SetPixels32(pixels); source.Apply();
            var drawing = (DrawingLayerBehaviour)typeof(DrawingLayerBehaviour)
                .GetMethod("FromMergedTexture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { source });
            Layer layer = drawing;
            layer.transform = TextureTransform.Default;
            var transform = layer.transform;
            transform.scale = new Vector2(4, 4);
            layer.transform = transform;
            document.layers.Add(layer);
            output = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview", hidden)
                .Invoke(document, new object[] { 64 });
            if (RenderTexture.active != previous) throw new Exception("Render target leaked.");
            RenderTexture.active = output;
            readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
            float min = 100, max = -100;
            for (int x = 8; x < 56; x++)
            {
                Color c = readback.GetPixel(x, 32);
                min = Mathf.Min(min, c.r); max = Mathf.Max(max, c.r);
                if (Mathf.Abs(c.a - 128f / 255) > .02f) throw new Exception("Alpha changed: " + c.a);
            }
            if (max - min < .4f) throw new Exception("Fine source details were lost: contrast " + (max - min));
            if (source.width != 256 || source.height != 256) throw new Exception("Source was resized.");
            return "Fine-detail render, alpha, original dimensions and render-target restoration passed. Contrast: " + (max - min);
        }
        finally
        {
            RenderTexture.active = previous;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(document);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
        }
    }
}
