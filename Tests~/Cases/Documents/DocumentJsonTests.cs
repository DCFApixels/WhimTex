using System;
using System.IO;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonTests
{
    static string ExecuteRun(int start = 0, int count = 8)
    {
        // Current inputs are generated only inside this run's owned test folder.
        // Frozen old TIFFs remain negative inputs in the compatibility boundary tests.
        var recipes = new[] {
            "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{\"width\":32,\"height\":24},\"layers\":[" +
                "{\"id\":\"noise\",\"behaviour\":{\"$type\":\"NoiseLayerBehaviour\",\"scale\":5,\"scaleY\":3}}," +
                "{\"id\":\"shape\",\"behaviour\":{\"$type\":\"ShapeLayerBehaviour\",\"rectangleCorners\":[{\"amount\":0.1},{\"amount\":0.2},{\"amount\":0.3},{\"amount\":0.4}]},\"opacity\":0.3}]}",
            "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{\"width\":32,\"height\":16,\"outputSrgb\":false},\"layers\":[" +
                "{\"id\":\"gradient\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\"," +
                "\"code\":\"float4 ApplyFX(float2 uv,float4 color){return float4(uv.x,uv.y,uv.x*uv.y,0.7);}\"}]}]}"
        };
        if (start < 0 || start >= recipes.Length || count <= 0) throw new ArgumentOutOfRangeException("start/count");
        int checkedCount = 0;
        long characters = 0;
        for (int i = start; i < Math.Min(recipes.Length, start + count); i++)
        {
            string input = UnityBRun.AssetPath("CurrentInput_" + i + ".tiff");
            string temporary = UnityBRun.AssetPath("__WhimTexJsonRoundtrip_") + Guid.NewGuid().ToString("N") + ".json";
            using (var fixture = WhimTexDocumentJson.Read(recipes[i], false))
            {
                foreach (var layer in fixture.Document.layers) foreach (var fx in layer.fx)
                    if (fx is ShaderFX shaderFX) typeof(ShaderFX).GetMethod("ApplyAgentDraft",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(shaderFX, null);
                WhimTexDocumentFile.Save(fixture.Document, input);
            }
            string original = Convert.ToBase64String(File.ReadAllBytes(input));
            var source = WhimTexDocumentFile.Load(input);
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
                    UnityBRun.Check(!(read.Warnings.Count != 0), input + ": " + string.Join(",", read.Warnings));
                    UnityBRun.Check(!(read.Document.outputSrgb != source.outputSrgb || read.Document.outputFilter != source.outputFilter || read.Document.outputPrecision != source.outputPrecision), "Output settings changed: " + input);
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
                        UnityBRun.Check(!(worst > .00001f), input + " " + mode + ": max delta=" + worst);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(actual); }
                    UnityBRun.Check(!(read.Document.layers.Count > 0 && read.Document.layers[0].Id != source.layers[0].Id), "Layer identity changed.");
                    checkedCount++;
                }
                UnityBRun.Check(!(original != Convert.ToBase64String(File.ReadAllBytes(input))), "Export modified source TIFF.");
            }
            finally
            {
                if (baseline != null) UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(source);
                if (File.Exists(temporary)) AssetDatabase.DeleteAsset(temporary);
                if (File.Exists(input)) AssetDatabase.DeleteAsset(input);
            }
        }
        return "";
    }
    public static string Run(int start = 0, int count = 8) => UnityBRun.Run("DocumentJsonSmoke.Run", () => ExecuteRun(start, count));
}
