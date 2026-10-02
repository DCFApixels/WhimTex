using System;
using System.IO;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonSmoke
{
    public static string Run(int start = 0, int count = 8)
    {
        var files = Directory.GetFiles("Assets/Learn/Pass", "*.tiff");
        Array.Sort(files, StringComparer.Ordinal);
        int checkedCount = 0;
        long characters = 0;
        for (int i = start; i < Math.Min(files.Length, start + count); i++)
        {
            string temporary = "Assets/__WhimTexJsonRoundtrip_" + Guid.NewGuid().ToString("N") + ".json";
            string original = Convert.ToBase64String(File.ReadAllBytes(files[i]));
            var source = WhimTexDocumentFile.Load(files[i]);
            Texture2D baseline = null;
            try
            {
                baseline = source.Compose();
                var expected = baseline.GetPixels();
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    var write = WhimTexDocumentJson.Write(source, new WhimTexJsonWriteOptions { Mode = mode });
                    characters += write.Json.Length;
                    var exported = WhimTexDocumentFile.ExportJson(source, temporary, new WhimTexJsonWriteOptions { Mode = mode });
                    if (exported.Warnings.Count != 0) throw new Exception("Unexpected export warning.");
                    var loaded = WhimTexDocumentFile.Load(temporary);
                    try
                    {
                        if (loaded.JsonWriteMode != mode) throw new Exception("File open lost write mode.");
                        WhimTexDocumentFile.Save(loaded, temporary);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(loaded); }
                    using var read = WhimTexDocumentJson.Read(File.ReadAllText(temporary));
                    if (read.Document.JsonWriteMode != mode) throw new Exception("Save lost write mode.");
                    if (read.Warnings.Count != 0) throw new Exception(files[i] + ": " + string.Join(",", read.Warnings));
                    if (read.Document.outputSrgb != source.outputSrgb || read.Document.outputFilter != source.outputFilter || read.Document.outputPrecision != source.outputPrecision)
                        throw new Exception("Output settings changed: " + files[i]);
                    Texture2D actual = read.Document.Compose();
                    try
                    {
                        var pixels = actual.GetPixels();
                        if (pixels.Length != expected.Length) throw new Exception("Canvas changed.");
                        float worst = 0;
                        for (int p = 0; p < pixels.Length; p++)
                            for (int c = 0; c < 4; c++)
                            {
                                float delta = Mathf.Abs(pixels[p][c] - expected[p][c]);
                                if (float.IsNaN(delta) || float.IsInfinity(delta)) throw new Exception("Nonfinite render.");
                                worst = Mathf.Max(worst, delta);
                            }
                        if (worst > .00001f) throw new Exception(files[i] + " " + mode + ": max delta=" + worst);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actual); }
                    if (read.Document.layers.Count > 0 && read.Document.layers[0].Id != source.layers[0].Id) throw new Exception("Layer identity changed.");
                    checkedCount++;
                }
                if (original != Convert.ToBase64String(File.ReadAllBytes(files[i]))) throw new Exception("Export modified source TIFF.");
            }
            finally
            {
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(source);
                if (File.Exists(temporary)) AssetDatabase.DeleteAsset(temporary);
            }
        }
        return "PASS: " + checkedCount + " TIFF to JSON export/open/save/render roundtrips; " + characters + " JSON characters.";
    }
}
