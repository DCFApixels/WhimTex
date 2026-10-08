// Frozen 0.12.5 documents remain negative inputs; current readers reject their versions.
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
    private static string BodyRun()
    {
        checks = 0;
        Manifest manifest;
        using (var stream = File.OpenRead(Baseline + "/manifest.json"))
            manifest = (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(stream);
        VerifyHashes(manifest);
        try
        {
            foreach (Entry entry in manifest.documents)
            {
                WhimTexDocument document = null;
                bool rejected = false;
                try { document = WhimTexDocumentFile.Load(Baseline + "/" + entry.file); }
                catch (WhimTexDocumentException) { rejected = true; }
                finally { if (document != null) Object.DestroyImmediate(document); }
                Check(rejected, entry.file + ": archived format is explicitly rejected");
            }
            Type brushLibrary = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.BrushPresetLibrary", true);
            object[] brushArgs = { Baseline + "/brush.sebrush", null };
            object preset = brushLibrary.GetMethod("Load", Any).Invoke(null, brushArgs);
            try
            {
                Check((float)Field(preset, "size") == 57f && (float)Field(preset, "hardness") == .625f && (float)Field(preset, "spacing") == .2f,
                    "saved brush preset values");
            }
            finally { if (brushArgs[1] is Texture2D tip) Object.DestroyImmediate(tip); }
            Type gradientClipboard = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientClipboard", true);
            var gradient = (WhimTexGradient)gradientClipboard.GetMethod("Read", Any).Invoke(null, new object[] { File.ReadAllText(Baseline + "/gradient.json") });
            Check(gradient.ColorKeys.Length >= 2, "saved gradient preset");
            VerifyHashes(manifest);
            return null;
        }
        finally { T = null; }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Archived document version boundary", context => { T = context; try { BodyRun(); } finally { T = null; } });
}
