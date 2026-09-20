using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class LinkedOutputSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type Doc = typeof(TextureCompositor);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static object Call(object obj, string name, params object[] args) => Doc.GetMethod(name, F).Invoke(obj, args);
    public static string Main()
    {
        string folder = "Assets/WhimTexLinkedOutputTest_" + Guid.NewGuid().ToString("N");
        string documentPath = folder + "/Document.asset";
        TextureCompositor d = null;
        Texture2D seed = null, decoded = null;
        string guid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        Check(!string.IsNullOrEmpty(guid), "Could not create isolated test folder");
        var previousRT = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            seed = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            seed.SetPixels(Enumerable.Repeat(Color.black, 16).ToArray()); seed.Apply();
            string imagePath = folder + "/Linked.png";
            File.WriteAllBytes(imagePath, seed.EncodeToPNG());
            AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(.2f, .7f);
            importer.spriteBorder = new Vector4(1, 1, 1, 1);
            importer.spritePixelsPerUnit = 42;
            importer.mipmapEnabled = true; importer.isReadable = false;
            importer.sRGBTexture = true; importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var platform = importer.GetPlatformTextureSettings("Android");
            platform.overridden = true; platform.maxTextureSize = 128;
            platform.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(platform); importer.SaveAndReimport();
            string imageGuid = AssetDatabase.AssetPathToGUID(imagePath);
            byte[] meta = File.ReadAllBytes(imagePath + ".meta");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string spriteGuid, out long spriteId);
            d = ScriptableObject.CreateInstance<TextureCompositor>();
            d.width = 32; d.height = 16;
            var fill = new ColorFillLayerBehaviour { color = new Color(.5f, .25f, .75f, .5f) };
            d.layers.Add(fill);
            var settings = (TextureCompositor.OutputSettings)Doc.GetField("outputSettings", F).GetValue(d);
            settings.outputType = TextureCompositor.OutputType.Texture;
            settings.storage = TextureCompositor.OutputStorage.LinearRgba32;
            settings.maxSize = 8;
            settings.linkedTextureGuid = imageGuid;
            Call(d, "SaveLegacyAssetForCompatibility", documentPath);
            Check(d.OutputTexture.width == 8 && d.OutputTexture.height == 4, "Embedded Max Size was ignored");
            decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Check(decoded.LoadImage(File.ReadAllBytes(imagePath)), "Output is not PNG");
            Check(decoded.width == 32 && decoded.height == 16, "Linked file was resized by embedded settings");
            Color c = decoded.GetPixel(16, 8);
            Check(Mathf.Abs(c.r - .5f) < .015f && Mathf.Abs(c.a - .5f) < .015f, "sRGB or alpha changed: " + c);
            Check(meta.SequenceEqual(File.ReadAllBytes(imagePath + ".meta")), "Importer metadata was modified");
            Check(AssetDatabase.AssetPathToGUID(imagePath) == imageGuid, "Texture GUID changed");
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string nextGuid, out long nextId);
            Check(spriteGuid == nextGuid && spriteId == nextId, "Sprite reference changed");
            Check(RenderTexture.active == previousRT && GL.sRGBWrite == previousSrgb, "GPU state leaked");
            string moved = folder + "/Moved.png";
            Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(imagePath, moved)), "Move failed");
            fill.color = Color.green;
            Call(d, "MarkChanged");
            Call(d, "SaveLegacyAssetForCompatibility", new object[] { null });
            decoded.LoadImage(File.ReadAllBytes(moved));
            Check(decoded.GetPixel(16, 8).g > .99f, "Moved output did not update");
            byte[] unchanged = File.ReadAllBytes(moved);
            settings.linkedTextureGuid = "";
            fill.color = Color.red; Call(d, "MarkChanged");
            Call(d, "SaveLegacyAssetForCompatibility", new object[] { null });
            Check(unchanged.SequenceEqual(File.ReadAllBytes(moved)), "Unlinked image was overwritten");
            fill.color = new Color(.5f, .25f, .75f, .5f); Call(d, "MarkChanged");
            foreach (string extension in new[] { ".png", ".tga", ".jpg", ".exr" })
            {
                string formatPath = folder + "/Format" + extension;
                byte[] initial = extension == ".png" ? seed.EncodeToPNG() : extension == ".tga" ? seed.EncodeToTGA() :
                    extension == ".jpg" ? seed.EncodeToJPG() : seed.EncodeToEXR();
                File.WriteAllBytes(formatPath, initial);
                AssetDatabase.ImportAsset(formatPath, ImportAssetOptions.ForceSynchronousImport);
                var formatImporter = (TextureImporter)AssetImporter.GetAtPath(formatPath);
                formatImporter.isReadable = true; formatImporter.sRGBTexture = false;
                formatImporter.textureCompression = TextureImporterCompression.Uncompressed;
                formatImporter.mipmapEnabled = false; formatImporter.SaveAndReimport();
                settings.linkedTextureGuid = AssetDatabase.AssetPathToGUID(formatPath);
                if (extension == ".exr")
                {
                    fill.colorRange = LayerColorRange.HDR;
                    fill.blendRange = LayerBlendRange.HDR;
                    fill.color = new Color(2, .25f, .75f, .5f); Call(d, "MarkChanged");
                }
                Call(d, "SaveLegacyAssetForCompatibility", new object[] { null });
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(formatPath);
                Check(image.width == 32 && image.height == 16, "Wrong source dimensions for " + extension);
                Color pixel = image.GetPixel(16, 8);
                float expected = extension == ".exr" ? Mathf.Pow((2 + .055f) / 1.055f, 2.4f) : Mathf.GammaToLinearSpace(.5f);
                if (extension == ".jpg") expected = Mathf.Lerp(1, expected, .5f);
                Check(Mathf.Abs(pixel.r - expected) < .03f, "Linear/HDR conversion for " + extension + ": " + pixel);
                Check(Mathf.Abs(pixel.a - (extension == ".jpg" ? 1 : .5f)) < .015f, "Alpha conversion for " + extension);
            }
            settings.linkedTextureGuid = imageGuid;
            string baseline = (string)Call(d, "CaptureOutputSettings");
            Doc.GetField("savedOutputSettings", F).SetValue(d, baseline);
            settings.linkedTextureGuid = ""; Call(d, "RevertOutputSettings");
            settings = (TextureCompositor.OutputSettings)Doc.GetField("outputSettings", F).GetValue(d);
            Check(settings.linkedTextureGuid == imageGuid, "Revert lost link");
            File.SetAttributes(moved, File.GetAttributes(moved) | FileAttributes.ReadOnly);
            object[] args = { null };
            Check((string)Call(d, "ValidateOutputSettings", args) != null && ((string[])args[0]).Contains("linkedTextureGuid"), "Read-only output accepted");
            File.SetAttributes(moved, File.GetAttributes(moved) & ~FileAttributes.ReadOnly);
            settings.linkedTextureGuid = AssetDatabase.AssetPathToGUID(documentPath);
            Check((string)Call(d, "ValidateOutputSettings", args) != null, "Compositor accepted as image output");
            settings.linkedTextureGuid = Guid.NewGuid().ToString("N");
            Check((string)Call(d, "ValidateOutputSettings", args) != null, "Missing link silently accepted");
            Check(!Directory.GetFiles(folder, "*~").Any(), "Temporary output files leaked");
            return "PASS: PNG/TGA/JPG/EXR save, full canvas size, linear/sRGB/HDR/alpha, GUID and Sprite ID, unchanged importer metadata/platform overrides, rename, disconnect, Revert, invalid/read-only targets, render state.";
        }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        finally
        {
            if (d != null) Undo.ClearUndo(d);
            if (seed != null) UnityEngine.Object.DestroyImmediate(seed);
            if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
            if (d != null && !AssetDatabase.Contains(d)) UnityEngine.Object.DestroyImmediate(d);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
    }
}
