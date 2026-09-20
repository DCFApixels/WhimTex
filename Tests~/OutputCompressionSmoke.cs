using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class OutputCompressionSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        var d = ScriptableObject.CreateInstance<TextureCompositor>();
        string path = "Assets/WhimTexCompressionTest_" + Guid.NewGuid().ToString("N") + ".asset";
        object Call(string method, params object[] args) => typeof(TextureCompositor).GetMethod(method, F).Invoke(d, args);
        void Save() => Call("SaveLegacyAssetForCompatibility", new object[] { null });
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        int count = 0;
        try
        {
            d.width = 16; d.height = 16; d.layers.Add(new ColorFillLayerBehaviour());
            var settings = (TextureCompositor.OutputSettings)typeof(TextureCompositor).GetField("outputSettings", F).GetValue(d);
            settings.mipMaps = true;
            Call("SaveLegacyAssetForCompatibility", path);
            var texture = d.OutputTexture; var sprite = d.OutputSprite;
            foreach (var compression in new[] { TextureCompositor.OutputCompression.BC1, TextureCompositor.OutputCompression.BC3,
                TextureCompositor.OutputCompression.BC7, TextureCompositor.OutputCompression.BC6H })
            {
                settings.compression = compression;
                settings.storage = compression == TextureCompositor.OutputCompression.BC6H ? TextureCompositor.OutputStorage.HdrHalf : TextureCompositor.OutputStorage.SrgbRgba32;
                settings.compressionQuality = TextureCompressionQuality.Fast;
                Save();
                Check(d.OutputTexture == texture && d.OutputSprite == sprite, "References changed");
                Check(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat), "Not compressed");
                Check(texture.mipmapCount == 5, "Mip chain lost");
                Check(texture.isDataSRGB == (compression != TextureCompositor.OutputCompression.BC6H), "Encoding changed");
                var rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                try
                {
                    Graphics.Blit(texture, rt); RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); read.Apply();
                    Check(read.GetPixel(0, 0).a > .9f, "Compressed GPU output is empty");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(read); }
                count++;
            }
            settings.compression = TextureCompositor.OutputCompression.Automatic;
            settings.storage = TextureCompositor.OutputStorage.SrgbRgba32;
            foreach (var level in new[] { TextureCompositor.OutputCompressionLevel.None, TextureCompositor.OutputCompressionLevel.LowQuality,
                TextureCompositor.OutputCompressionLevel.NormalQuality, TextureCompositor.OutputCompressionLevel.HighQuality })
            {
                settings.compressionLevel = level; Save();
                Check(texture.format == (level == TextureCompositor.OutputCompressionLevel.None ? TextureFormat.RGBA32 :
                    level == TextureCompositor.OutputCompressionLevel.HighQuality ? TextureFormat.BC7 : TextureFormat.DXT1), "Automatic opaque format");
            }
            d.layers.Clear();
            settings.compressionLevel = TextureCompositor.OutputCompressionLevel.NormalQuality; Save();
            Check(texture.format == TextureFormat.DXT5, "Automatic alpha format");
            settings.alphaIsTransparency = true; Save();
            Check(texture.format == TextureFormat.DXT5, "Alpha Is Transparency must preserve alpha");
            settings.compressionLevel = TextureCompositor.OutputCompressionLevel.NormalQuality;
            settings.storage = TextureCompositor.OutputStorage.HdrHalf; Save();
            Check(texture.format == TextureFormat.RGBAHalf, "HDR alpha must remain uncompressed");
            d.layers.Add(new ColorFillLayerBehaviour()); Save();
            Check(texture.format == TextureFormat.BC6H, "Automatic opaque HDR format");
            settings.compression = TextureCompositor.OutputCompression.None; Save();
            Check(texture.format == TextureFormat.RGBAHalf && texture.mipmapCount == 5, "Uncompressed restore");
            settings.compression = TextureCompositor.OutputCompression.BC7;
            bool rejected = false;
            try { Save(); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected && texture.format == TextureFormat.RGBAHalf, "Invalid HDR/BC7 combination changed output");
            return "PASS: " + count + " codecs, Automatic levels/alpha/HDR, mipmaps, GPU output, color space, stable texture/sprite references and invalid settings.";
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else UnityEngine.Object.DestroyImmediate(d);
        }
    }
}
