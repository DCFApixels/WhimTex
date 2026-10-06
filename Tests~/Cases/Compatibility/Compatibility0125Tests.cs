// run_script entry Compatibility0125Tests.Run. Reads frozen 0.12.5 files; never regenerates them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class Compatibility0125Tests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const string Baseline = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Compatibility0125";
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] public sealed class LayerInfo
    {
        public string id, name, type, pixelsSha256, format, filter, wrapU, wrapV;
        public double rotation;
        public int sourceWidth, sourceHeight;
    }
    [Serializable] public sealed class Entry { public string file, render; public int width, height; public List<LayerInfo> layers; }
    [Serializable] public sealed class FileHash { public string file, sha256; }
    [Serializable] public sealed class Manifest { public string packageVersion, commit, unityVersion; public List<Entry> documents; public List<FileHash> sha256; }
    static int checks;
    static void Check(bool ok, string message) { T.True(ok, message); }
    static object Field(object value, string name) => value.GetType().GetField(name, Any).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Any).Invoke(value, args);
    static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    static void VerifyHashes(Manifest manifest)
    {
        Check(manifest.packageVersion == "0.12.5" && manifest.commit == "a72cc9544d39f93555ca6e9f39c137340e3028f0", "baseline origin");
        Check(manifest.documents.Count == 8 && manifest.sha256.Count >= 30, "complete baseline manifest");
        foreach (var file in manifest.sha256)
            Check(Hash(File.ReadAllBytes(Baseline + "/" + file.file)) == file.sha256, "frozen SHA256: " + file.file);
    }
    static void Verify(TextureCompositor doc, Entry entry)
    {
        Check(string.IsNullOrEmpty((string)Field(doc, "documentLoadWarning")), entry.file + ": complete read");
        Check(doc.width == entry.width && doc.height == entry.height, entry.file + ": canvas dimensions");
        var actual = new List<Layer>();
        void Visit(List<Layer> list)
        {
            foreach (Layer layer in list) { actual.Add(layer); if (layer.children != null) Visit(layer.children); }
        }
        Visit(doc.layers);
        Check(actual.Count == entry.layers.Count, entry.file + ": layer count");
        for (int i = 0; i < actual.Count; i++)
        {
            var expected = entry.layers[i]; var layer = actual[i];
            Check(layer.Id == expected.id && layer.layerName == expected.name, entry.file + ": identity " + i);
            Check(layer.Behaviour != null && layer.Behaviour.GetType().FullName == expected.type, entry.file + ": behaviour " + i);
            Check(Math.Abs(layer.transform.rotation - expected.rotation) < 1e-9, entry.file + ": rotation " + i);
            if (!string.IsNullOrEmpty(expected.pixelsSha256))
            {
                var drawing = (DrawingLayerBehaviour)layer.Behaviour;
                var pixels = (Texture2D)drawing.GetType().GetProperty("StoredTexture", Any).GetValue(drawing);
                Check(pixels.width == expected.sourceWidth && pixels.height == expected.sourceHeight && pixels.format.ToString() == expected.format,
                    entry.file + ": source resolution/precision");
                Check(Hash(pixels.GetRawTextureData<byte>().ToArray()) == expected.pixelsSha256, entry.file + ": exact Drawing bytes");
                Check(pixels.filterMode.ToString() == expected.filter && pixels.wrapModeU.ToString() == expected.wrapU && pixels.wrapModeV.ToString() == expected.wrapV,
                    entry.file + ": source sampling");
                Check(drawing.brushSize == 57 && Math.Abs(drawing.brushHardness - .625f) < 1e-6f, entry.file + ": saved brush values");
            }
        }
        if (actual.Count > 2)
        {
            var group = doc.layers[1];
            Check(group.children.Count == 2 && Math.Abs(group.opacity - .73f) < 1e-6f, entry.file + ": group structure");
            var outline = (OutlineLayerBehaviour)actual.Find(layer => layer.Behaviour is OutlineLayerBehaviour).Behaviour;
            Check(outline.TargetLayerId == group.Id, entry.file + ": target identity");
            var fx = group.fx[0] as ShaderFX;
            var embedded = (List<ShaderFX>)Field(doc, "embeddedShaderFX");
            Check(embedded.Count == 1 && ReferenceEquals(embedded[0], fx), entry.file + ": shared FX identity after retired object slots");
            var parameters = (List<ShaderFXParameter>)Field(fx, "parameters");
            Check(parameters.Count == 1 && parameters[0].name == "_Gain" && Math.Abs(parameters[0].floatValue - .625f) < 1e-6f,
                entry.file + ": saved FX value");
            Check(((string)Field(fx, "code")).Contains("// @param float _Gain") && parameters[0].controls.Count == 1,
                entry.file + ": manual FX normalized to declaration");
        }
        Texture2D image = doc.ComposeCanvas();
        try
        {
            using var reader = new BinaryReader(File.OpenRead(Baseline + "/" + entry.render));
            float worst = 0;
            Color[] pixels = image.GetPixels();
            Check(Array.Exists(pixels, pixel => Math.Abs(pixel.r - pixels[0].r) + Math.Abs(pixel.g - pixels[0].g) + Math.Abs(pixel.b - pixels[0].b) > .01f),
                entry.file + ": non-background layers are visible");
            foreach (Color pixel in pixels)
            {
                worst = Math.Max(worst, Math.Abs(pixel.r - reader.ReadSingle()));
                worst = Math.Max(worst, Math.Abs(pixel.g - reader.ReadSingle()));
                worst = Math.Max(worst, Math.Abs(pixel.b - reader.ReadSingle()));
                worst = Math.Max(worst, Math.Abs(pixel.a - reader.ReadSingle()));
            }
            Check(reader.BaseStream.Position == reader.BaseStream.Length && worst <= .002f, entry.file + ": rendered image, worst=" + worst);
        }
        finally { Object.DestroyImmediate(image); }
    }
    private static string BodyRun()
    {
        checks = 0;
        Manifest manifest;
        using (var stream = File.OpenRead(Baseline + "/manifest.json"))
            manifest = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(stream);
        VerifyHashes(manifest);
        string folder = Scope.Assets;
        // Scope.Assets already created its owned GUID folder.
        try
        {
            int number = 0;
            foreach (Entry entry in manifest.documents)
            {
                if (Path.GetExtension(entry.file) == ".asset") continue; // Archived negative input, not a supported document.
                string path = folder + "/" + entry.file;
                File.Copy(Baseline + "/" + entry.file, path);
                if (File.Exists(Baseline + "/" + entry.file + ".meta")) File.WriteAllText(path + ".meta", System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Baseline + "/" + entry.file + ".meta"), @"(?m)^guid: [0-9a-fA-F]+", "guid: " + Guid.NewGuid().ToString("N")));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureCompositor doc = null, reloaded = null;
                try
                {
                    doc = WhimTexDocumentFile.Load(path);
                    Verify(doc, entry);
                    string saved = WhimTexDocumentFile.Save(doc, folder + "/Resaved" + number++ + ".tiff");
                    reloaded = WhimTexDocumentFile.Load(saved);
                    Verify(reloaded, entry);
                }
                finally
                {
                    if (reloaded != null) Object.DestroyImmediate(reloaded);
                    if (doc != null && !AssetDatabase.Contains(doc)) Object.DestroyImmediate(doc);
                }
            }
            Type brushLibrary = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary", true);
            object[] brushArgs = { Baseline + "/brush.sebrush", null };
            object preset = brushLibrary.GetMethod("Load", Any).Invoke(null, brushArgs);
            try
            {
                Check((float)Field(preset, "size") == 57f && (float)Field(preset, "hardness") == .625f && (float)Field(preset, "spacing") == .2f,
                    "saved brush preset values");
            }
            finally { if (brushArgs[1] is Texture2D tip) Object.DestroyImmediate(tip); }
            Type gradientClipboard = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientClipboard", true);
            var gradient = (WhimTexGradient)gradientClipboard.GetMethod("Read", Any).Invoke(null, new object[] { File.ReadAllText(Baseline + "/gradient.json") });
            Check(gradient.ColorKeys.Length >= 2, "saved gradient preset");
            VerifyHashes(manifest);
            return null;
        }
        finally { { /* Asset deletion is owned by UnityAScope. */ } }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

