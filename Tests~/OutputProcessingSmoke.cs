using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class OutputProcessingSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        string path = "Assets/WhimTexProcessingTest_" + Guid.NewGuid().ToString("N") + ".asset";
        var source = new Texture2D(32, 16, TextureFormat.RGBA32, false, true);
        Texture2D result = null;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        try
        {
            document.width = 32; document.height = 16;
            var settings = (TextureCompositor.OutputSettings)typeof(TextureCompositor).GetField("outputSettings", F).GetValue(document);
            settings.storage = TextureCompositor.OutputStorage.LinearRgba32;
            settings.alphaIsTransparency = true;
            var pixels = new Color[512];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) pixels[y * 32 + x] = Color.red;
            source.SetPixels(pixels); source.Apply();
            var process = typeof(TextureCompositor).GetMethod("ProcessOutputTexture", F);
            result = (Texture2D)process.Invoke(document, new object[] { source, settings });
            Check(result.GetPixel(16, 8).r > .8f && result.GetPixel(16, 8).a == 0, "Alpha dilation must extend RGB without removing alpha");
            UnityEngine.Object.DestroyImmediate(result); result = null;
            settings.maxSize = 16; settings.mipMaps = true; settings.preserveCoverage = true;
            foreach (TextureResizeAlgorithm resize in Enum.GetValues(typeof(TextureResizeAlgorithm)))
            foreach (TextureImporterMipFilter filter in Enum.GetValues(typeof(TextureImporterMipFilter)))
            {
                settings.resizeAlgorithm = resize; settings.mipFilter = filter;
                result = (Texture2D)process.Invoke(document, new object[] { source, settings });
                Check(result.width == 16 && result.height == 8 && result.mipmapCount == 5, "Downsample/mip dimensions");
                UnityEngine.Object.DestroyImmediate(result); result = null;
            }
            document.layers.Add(new ColorFillLayerBehaviour());
            settings.readable = false;
            var save = typeof(TextureCompositor).GetMethod("SaveWithOutput", F);
            save.Invoke(document, new object[] { path });
            Check(!document.OutputTexture.isReadable && document.OutputTexture.width == 16, "Read/Write disabled");
            Check(Mathf.Abs(document.OutputSprite.bounds.size.x - .32f) < .001f, "Sprite world size changed after resize");
            settings.readable = true;
            save.Invoke(document, new object[] { null });
            Check(document.OutputTexture.isReadable, "Read/Write restore");
            return "PASS: alpha dilation, Mitchell/Bilinear, Box/Kaiser, coverage generation, output size, sprite world size and Read/Write roundtrip.";
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
            UnityEngine.Object.DestroyImmediate(source);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
