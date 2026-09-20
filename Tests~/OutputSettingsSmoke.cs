using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class OutputSettingsSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        string path = "Assets/WhimTexOutputSettingsTest_" + Guid.NewGuid().ToString("N") + ".asset";
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        Texture2D readback = null;
        RenderTexture source = null;
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
        object Call(string name, params object[] args) => typeof(TextureCompositor).GetMethod(name, F).Invoke(document, args);
        void Save(string destination) => Call("SaveLegacyAssetForCompatibility", destination);
        long Id(UnityEngine.Object value) { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id); return id; }
        try
        {
            document.width = 32; document.height = 16;
            document.layers.Add(new ColorFillLayerBehaviour());
            Save(path);
            var texture = document.OutputTexture; var sprite = document.OutputSprite;
            long textureId = Id(texture), spriteId = Id(sprite);
            Check(texture.format == TextureFormat.RGBAHalf && texture.mipmapCount == 1, "Default format changed");
            Check(sprite.pixelsPerUnit == 100 && sprite.pivot == new Vector2(16, 8), "Default sprite changed");
            var settings = (TextureCompositor.OutputSettings)typeof(TextureCompositor).GetField("outputSettings", F).GetValue(document);
            settings.mipMaps = true; settings.wrapU = TextureWrapMode.Repeat; settings.wrapV = TextureWrapMode.Mirror;
            settings.anisoLevel = 4; settings.pixelsPerUnit = 32; settings.pivot = new Vector2(.25f, .75f);
            settings.border = new Vector4(2, 3, 4, 5); settings.generatePhysicsShape = true;
            foreach (TextureCompositor.OutputStorage storage in Enum.GetValues(typeof(TextureCompositor.OutputStorage)))
            {
                settings.storage = storage; Save(null);
                Check(Id(document.OutputTexture) == textureId && Id(document.OutputSprite) == spriteId, "Output identity changed");
                Check(document.OutputTexture.mipmapCount == 6, "Missing mip chain");
                Check(document.OutputTexture.isDataSRGB == (storage == TextureCompositor.OutputStorage.SrgbRgba32), "Color encoding");
                Check(document.OutputTexture.wrapModeU == TextureWrapMode.Repeat && document.OutputTexture.wrapModeV == TextureWrapMode.Mirror && document.OutputTexture.anisoLevel == 4, "Sampling settings");
                Check(document.OutputSprite.pivot == new Vector2(8, 12) && document.OutputSprite.pixelsPerUnit == 32 && document.OutputSprite.border == settings.border, "Sprite settings");
                var pixels = document.OutputTexture.GetPixels(5);
                Check(pixels.Length == 1 && pixels[0].a > .99f, "Mip pixels missing");
                source = RenderTexture.GetTemporary(32, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                try { RenderTexture.active = source; GL.Clear(false, true, new Color(.25f, .5f, .75f, 1)); }
                finally { RenderTexture.active = previous; }
                Call("PublishLiveOutput", source);
                readback = new Texture2D(32, 16, TextureFormat.RGBAFloat, false, true);
                var rt = RenderTexture.GetTemporary(32, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(document.OutputTexture, rt); RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, 32, 16), 0, 0); readback.Apply();
                    Check(Mathf.Abs(readback.GetPixel(8, 8).r - .25f) < .01f, "Live output color");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
                Call("StopLiveOutput");
                rt = RenderTexture.GetTemporary(32, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(document.OutputTexture, rt); RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, 32, 16), 0, 0); readback.Apply();
                    Color saved = document.OutputTexture.GetPixel(8, 8);
                    float expected = document.OutputTexture.isDataSRGB ? Mathf.GammaToLinearSpace(saved.r) : saved.r;
                    Check(Mathf.Abs(readback.GetPixel(8, 8).r - expected) < .01f, "Saved output GPU restoration");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
                UnityEngine.Object.DestroyImmediate(readback); readback = null;
                RenderTexture.ReleaseTemporary(source); source = null;
            }
            settings.meshType = SpriteMeshType.Tight; Save(null);
            Check(Id(document.OutputSprite) == spriteId, "Tight mesh identity");
            settings.pixelsPerUnit = 0;
            bool rejected = false;
            try { Save(null); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected && Id(document.OutputTexture) == textureId, "Invalid settings changed output");
            return "PASS: " + checks + " output format, mipmaps, sprite settings, stable asset IDs, live output and validation checks.";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        finally
        {
            Call("StopLiveOutput");
            if (source != null) RenderTexture.ReleaseTemporary(source);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            else if (document != null) UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
