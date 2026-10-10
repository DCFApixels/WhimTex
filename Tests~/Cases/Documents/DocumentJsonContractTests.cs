using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonContractTests
{
    static int checks;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(bool value, string message) { UnityBRun.Check(!(!value), message); checks++; }
    static Layer Add(WhimTexDocument document, LayerBehaviour behaviour)
    {
        var layer = new Layer(behaviour);
        typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
        document.layers.Add(layer);
        return layer;
    }
    static string Full(WhimTexDocument document) => WhimTexDocumentJson.Write(document,
        new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
    static void Rejected(Action action, string message)
    { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }

    static string ExecuteRun()
    {
        checks = 0;
        var document = UnityBRun.Create<WhimTexDocument>();
        string path = UnityBRun.AssetPath("WhimTexJsonContract_") + Guid.NewGuid().ToString("N") + ".whimtex.json";
        try
        {
            document.width = 128; document.height = 64; document.outputSrgb = false; document.outputFilter = FilterMode.Point;
            using (var empty = WhimTexDocumentJson.Read(Full(document), false)) Check(empty.Document.layers.Count == 0, "Empty document failed.");
            var noise = new NoiseLayerBehaviour { warp = NoiseLayerBehaviour.WarpType.None, warpStrength = 73 };
            var layer = Add(document, noise);
            layer.enabled = false;
            using (var full = WhimTexDocumentJson.Read(Full(document), false))
                Check(((NoiseLayerBehaviour)full.Document.layers[0].Behaviour).warpStrength == 73 && !full.Document.layers[0].enabled, "Full lost inactive settings or disabled layer.");
            var optimized = WhimTexDocumentJson.Write(document);
            Check(!optimized.Json.Contains("\"warpStrength\""), "Optimized retained inactive warp strength.");
            using (var restored = WhimTexDocumentJson.Read(optimized.Json, false))
                Check(((NoiseLayerBehaviour)restored.Document.layers[0].Behaviour).warpStrength == 1, "Inactive settings did not use version defaults.");
            Rejected(() => WhimTexDocumentJson.Read(Full(document).Replace("\"width\": 128", "\"unknownSetting\": 128"), false).Dispose(), "Unknown field silently lost.");
            Rejected(() => WhimTexDocumentJson.Read(Full(document).Replace("\"version\": 2", "\"version\": 999"), false).Dispose(), "Unsupported version accepted.");
            Rejected(() => WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":2,\"document\":{},\"layers\":[{}]}", false).Dispose(), "Incomplete layer silently accepted.");
            const string fxJson = "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{},\"layers\":[" +
                "{\"id\":\"first\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\",\"$id\":\"shared\",\"code\":\"float4 ApplyFX(float2 uv,float4 color){return color;}\"}]}," +
                "{\"id\":\"second\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$ref\":\"shared\"}]}]}";
            using (var shared = WhimTexDocumentJson.Read(fxJson, false))
            using (var again = WhimTexDocumentJson.Read(Full(shared.Document), false))
                Check(ReferenceEquals(again.Document.layers[0].fx[0], again.Document.layers[1].fx[0]), "Shared FX identity lost.");
            var fragment = WhimTexDocumentJson.WriteLayers(document, new[] { layer });
            using (var read = WhimTexDocumentJson.Read(fragment.Json, false)) Check(!fragment.Json.Contains("\"kind\"") && read.Document.width == 128, "Selected-layer context lost or obsolete discriminator written.");

            document.layers.Clear();
            const string texturePath = "Assets/Learn/Steam.png";
            byte[] originalTexture = File.ReadAllBytes(texturePath);
            byte[] originalMeta = File.Exists(texturePath + ".meta") ? File.ReadAllBytes(texturePath + ".meta") : null;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Check(texture != null, "Fixture texture missing.");
            Add(document, new FileLayerBehaviour { sourceTexture = texture });
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId);
            string fileJson = Full(document);
            Check(fileJson.Contains(guid) && fileJson.Contains("Assets/Learn/Steam.png") && fileJson.Contains(localId.ToString()), "Asset identity incomplete.");
            string unknownGuid = "00000000000000000000000000000001";
            string fallbackJson = fileJson.Replace(guid, unknownGuid);
            using (var fallback = WhimTexDocumentJson.Read(fallbackJson, false))
                Check(((FileLayerBehaviour)fallback.Document.layers[0].Behaviour).sourceTexture == texture && fallback.Warnings.Count == 0, "GUID to Path fallback failed.");
            string missingJson = fallbackJson.Replace(texturePath, "Assets/MissingJsonFixture.png");
            using (var missing = WhimTexDocumentJson.Read(missingJson, false))
            {
                Check(missing.Warnings.Count == 1 && ((FileLayerBehaviour)missing.Document.layers[0].Behaviour).sourceTexture == null, "Missing asset did not load softly.");
                var clone = UnityEngine.Object.Instantiate(missing.Document);
                try { Check(Full(clone).Contains(unknownGuid) && Full(clone).Contains("Assets/MissingJsonFixture.png"), "Unresolved asset lost after Unity serialization."); }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
            const string missingFx = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"fx-texture\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\",\"code\":\"// @param texture2D _Map\\nfloat4 ApplyFX(float2 uv,float4 color){return color;}\",\"parameters\":[{\"name\":\"_Map\",\"type\":\"Texture2D\",\"textureValue\":{\"$asset\":{\"guid\":\"00000000000000000000000000000001\",\"path\":\"Assets/MissingJsonFxTexture.png\",\"localId\":\"2800000\",\"type\":\"UnityEngine.Texture2D\"}}}]}]}]}";
            using (var missing = WhimTexDocumentJson.Read(missingFx, false))
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    var exported = WhimTexDocumentJson.Write(missing.Document, new WhimTexJsonWriteOptions { Mode = mode });
                    Check(exported.Json.Contains("Assets/MissingJsonFxTexture.png") && exported.Json.Contains(unknownGuid), "Reparsed FX parameters lost unresolved texture identity: " + mode);
                    using var restored = WhimTexDocumentJson.Read(exported.Json, false);
                    Check(restored.Warnings.Count == 1, "Missing FX texture warning was lost: " + mode);
                }
            Check(originalTexture.AsSpan().SequenceEqual(File.ReadAllBytes(texturePath)), "Reference read/write preserves source PNG bytes.");
            Check(originalMeta == null ? !File.Exists(texturePath + ".meta") : originalMeta.AsSpan().SequenceEqual(File.ReadAllBytes(texturePath + ".meta")), "Reference read/write preserves source PNG import settings.");

            document.layers.Clear();
            var drawing = new DrawingLayerBehaviour();
            Add(document, drawing);
            var pixels = UnityBRun.Track(new Texture2D(2, 2));
            typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", F).Invoke(drawing, new object[] { pixels });
            Rejected(() => Full(document), "Drawing omission was not opt-in.");
            var omitted = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { AllowDrawingOmission = true });
            Check(omitted.DrawingPixelsOmitted && omitted.Warnings.Count == 1 && omitted.Json.Contains("contentOmitted"), "Drawing warning/placeholder absent.");
            using (var read = WhimTexDocumentJson.Read(omitted.Json, false))
            {
                var placeholder = WhimTexDocumentJson.Write(read.Document);
                Check(!placeholder.DrawingPixelsOmitted && placeholder.Warnings.Count == 0, "Empty Drawing placeholder cannot be saved.");
            }
            Check(pixels != null, "Export destroyed source Drawing pixels.");
            document.layers.Clear(); UnityEngine.Object.DestroyImmediate(pixels);
            Add(document, new TextLayerBehaviour { fontFamily = "__WhimTexAbsentJsonFont__" });
            var textWarning = WhimTexDocumentJson.Write(document);
            Check(textWarning.Warnings.Count == 1 && !textWarning.DrawingPixelsOmitted, "Font warning was treated as omitted Drawing pixels.");
            Check((bool)typeof(WhimTexWindow).GetMethod("ConfirmJsonDrawingOmission", F).Invoke(null, new object[] { textWarning, false }), "Font warning must not request Drawing omission confirmation.");
            document.layers.Clear();
            var blur = Add(document, new BlurLayerBehaviour());
            var pending = Add(document, new PendingLayerBehaviour());
            var input = Add(document, new ColorFillLayerBehaviour());
            using (var previous = WhimTexDocumentJson.Read(WhimTexDocumentJson.WriteLayers(document, new[] { blur, input }).Json, false))
                Check(previous.Document.layers.Count == 2, "Previous selection incorrectly requires a skipped Pending layer.");
            Rejected(() => WhimTexDocumentJson.WriteLayers(document, new[] { blur, pending }), "Previous fragment omitted the actual content input.");
            document.layers.Clear();
            Add(document, new NoiseLayerBehaviour());
            WhimTexDocumentFile.SaveJson(document, path);
            Check(WhimTexDocumentFile.IsDocument(path), "JSON not detected as document.");
            var service = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService");
            service.GetMethod("Attach", F).Invoke(null, new object[] { document, document });
            try
            {
                using var snapshot = WhimTexDocumentJson.Read(Full(document), false);
                typeof(WhimTexDocumentFile).GetMethod("SaveJsonSnapshot", F).Invoke(null, new object[] { document, snapshot.Document, path, new WhimTexJsonWriteOptions() });
                Check(File.Exists(path), "Saving a snapshot over its displayed source failed.");
            }
            finally { service.GetMethod("Detach", F).Invoke(null, new object[] { document }); }
            var loaded = WhimTexDocumentFile.Load(path);
            try
            {
                Check(loaded.width == 128 && loaded.height == 64 && !loaded.outputSrgb && loaded.outputFilter == FilterMode.Point, "File settings lost.");
                string saved = File.ReadAllText(path);
                File.AppendAllText(path, "\n");
                Rejected(() => WhimTexDocumentFile.SaveJson(loaded, path), "External modification was overwritten.");
                Check(File.ReadAllText(path) == saved + "\n", "Rejected save changed file.");
            }
            finally { UnityEngine.Object.DestroyImmediate(loaded); }
            return "";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(document);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }
    }
    public static string Run() => UnityBRun.Run("DocumentJsonContractSmoke.Run", () => ExecuteRun());
}
