using System;
using System.IO;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonTests
{
    static string ExecuteRun(int start = 0, int count = 8)
    {
        // Package-owned 0.12.5 fixtures; never depend on the user's sample assets.
        var files = new[] {
            "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Compatibility0125/procedural.tiff",
            "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BASE_Gradient_128.tiff"
        };
        Array.Sort(files, StringComparer.Ordinal);
        if (start < 0 || start >= files.Length || count <= 0) throw new ArgumentOutOfRangeException("start/count");
        int checkedCount = 0;
        long characters = 0;
        for (int i = start; i < Math.Min(files.Length, start + count); i++)
        {
            string temporary = UnityBRun.AssetPath("__WhimTexJsonRoundtrip_") + Guid.NewGuid().ToString("N") + ".json";
            string original = Convert.ToBase64String(File.ReadAllBytes(files[i]));
            var source = WhimTexDocumentFile.Load(files[i]);
            Texture2D baseline = null;
            try
            {
                baseline = source.ComposeCanvas();
                var expected = baseline.GetPixels();
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    var write = WhimTexDocumentJson.Write(source, new WhimTexJsonWriteOptions { Mode = mode });
                    characters += write.Json.Length;
                    var exported = WhimTexDocumentFile.ExportJson(source, temporary, new WhimTexJsonWriteOptions { Mode = mode });
                    UnityBRun.Check(!(exported.Warnings.Count != 0), "Unexpected export warning.");
                    var loaded = WhimTexDocumentFile.Load(temporary);
                    try
                    {
                        UnityBRun.Check(!(loaded.JsonWriteMode != mode), "File open lost write mode.");
                        WhimTexDocumentFile.Save(loaded, temporary);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(loaded); }
                    using var read = WhimTexDocumentJson.Read(File.ReadAllText(temporary));
                    UnityBRun.Check(!(read.Document.JsonWriteMode != mode), "Save lost write mode.");
                    UnityBRun.Check(!(read.Warnings.Count != 0), files[i] + ": " + string.Join(",", read.Warnings));
                    UnityBRun.Check(!(read.Document.outputSrgb != source.outputSrgb || read.Document.outputFilter != source.outputFilter || read.Document.outputPrecision != source.outputPrecision), "Output settings changed: " + files[i]);
                    Texture2D actual = read.Document.ComposeCanvas();
                    try
                    {
                        var pixels = actual.GetPixels();
                        UnityBRun.Check(!(pixels.Length != expected.Length), "Canvas changed.");
                        float worst = 0;
                        for (int p = 0; p < pixels.Length; p++)
                            for (int c = 0; c < 4; c++)
                            {
                                float delta = Mathf.Abs(pixels[p][c] - expected[p][c]);
                                UnityBRun.Check(!(float.IsNaN(delta) || float.IsInfinity(delta)), "Nonfinite render.");
                                worst = Mathf.Max(worst, delta);
                            }
                        UnityBRun.Check(!(worst > .00001f), files[i] + " " + mode + ": max delta=" + worst);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actual); }
                    UnityBRun.Check(!(read.Document.layers.Count > 0 && read.Document.layers[0].Id != source.layers[0].Id), "Layer identity changed.");
                    checkedCount++;
                }
                UnityBRun.Check(!(original != Convert.ToBase64String(File.ReadAllBytes(files[i]))), "Export modified source TIFF.");
            }
            finally
            {
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(source);
                if (File.Exists(temporary)) AssetDatabase.DeleteAsset(temporary);
            }
        }
        return "";
    }
    public static string Run(int start = 0, int count = 8) => UnityBRun.Run("DocumentJsonSmoke.Run", () => ExecuteRun(start, count));
}

