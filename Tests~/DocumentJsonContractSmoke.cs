using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class DocumentJsonContractSmoke
{
    static int checks;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static Layer Add(TextureCompositor document, LayerBehaviour behaviour)
    {
        var layer = new Layer(behaviour);
        typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
        document.layers.Add(layer);
        return layer;
    }
    static string Full(TextureCompositor document) => WhimTexDocumentJson.Write(document,
        new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
    static void Rejected(Action action, string message)
    { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }

    public static string Run()
    {
        checks = 0;
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        string path = "Assets/WhimTexJsonContract_" + Guid.NewGuid().ToString("N") + ".whimtex.json";
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
            Rejected(() => WhimTexDocumentJson.Read(Full(document).Replace("\"version\": 1", "\"version\": 999"), false).Dispose(), "Unsupported version accepted.");
            Rejected(() => WhimTexDocumentJson.Read("{\"format\":\"whimtex.document\",\"version\":1,\"document\":{},\"layers\":[{}]}", false).Dispose(), "Incomplete layer silently accepted.");
            const string fxJson = "{\"format\":\"whimtex.document\",\"version\":1,\"document\":{},\"layers\":[" +
                "{\"id\":\"first\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"modifiers\":[{\"$type\":\"ShaderFX\",\"$id\":\"shared\",\"code\":\"float4 ApplyFX(float2 uv,float4 color){return color;}\"}]}," +
                "{\"id\":\"second\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"modifiers\":[{\"$ref\":\"shared\"}]}]}";
            using (var shared = WhimTexDocumentJson.Read(fxJson, false))
            using (var again = WhimTexDocumentJson.Read(Full(shared.Document), false))
                Check(ReferenceEquals(again.Document.layers[0].modifiers[0], again.Document.layers[1].modifiers[0]), "Shared FX identity lost.");
            var fragment = WhimTexDocumentJson.WriteLayers(document, new[] { layer });
            using (var read = WhimTexDocumentJson.Read(fragment.Json, false)) Check(!fragment.Json.Contains("\"kind\"") && read.Document.width == 128, "Selected-layer context lost or obsolete discriminator written.");

            document.layers.Clear();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Learn/Steam.png");
            Check(texture != null, "Fixture texture missing.");
            Add(document, new FileLayerBehaviour { sourceTexture = texture });
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId);
            string fileJson = Full(document);
            Check(fileJson.Contains(guid) && fileJson.Contains("Assets/Learn/Steam.png") && fileJson.Contains(localId.ToString()), "Asset identity incomplete.");
            string unknownGuid = "00000000000000000000000000000001";
            string fallbackJson = fileJson.Replace(guid, unknownGuid);
            using (var fallback = WhimTexDocumentJson.Read(fallbackJson, false))
                Check(((FileLayerBehaviour)fallback.Document.layers[0].Behaviour).sourceTexture == texture && fallback.Warnings.Count == 0, "GUID to Path fallback failed.");
            string missingJson = fallbackJson.Replace("Assets/Learn/Steam.png", "Assets/MissingJsonFixture.png");
            using (var missing = WhimTexDocumentJson.Read(missingJson, false))
            {
                Check(missing.Warnings.Count == 1 && ((FileLayerBehaviour)missing.Document.layers[0].Behaviour).sourceTexture == null, "Missing asset did not load softly.");
                var clone = UnityEngine.Object.Instantiate(missing.Document);
                try { Check(Full(clone).Contains(unknownGuid) && Full(clone).Contains("Assets/MissingJsonFixture.png"), "Unresolved asset lost after Unity serialization."); }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }

            document.layers.Clear();
            var drawing = new DrawingLayerBehaviour();
            Add(document, drawing);
            var pixels = new Texture2D(2, 2);
            typeof(DrawingLayerBehaviour).GetMethod("AdoptStoredTexture", F).Invoke(drawing, new object[] { pixels });
            Rejected(() => Full(document), "Drawing omission was not opt-in.");
            var omitted = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { AllowDrawingOmission = true });
            Check(omitted.Warnings.Count == 1 && omitted.Json.Contains("contentOmitted"), "Drawing warning/placeholder absent.");
            using (var read = WhimTexDocumentJson.Read(omitted.Json, false))
                Check(WhimTexDocumentJson.Write(read.Document).Warnings.Count == 0, "Empty Drawing placeholder cannot be saved.");
            Check(pixels != null, "Export destroyed source Drawing pixels.");
            document.layers.Clear(); UnityEngine.Object.DestroyImmediate(pixels);
            Add(document, new NoiseLayerBehaviour());
            WhimTexDocumentFile.SaveJson(document, path);
            Check(WhimTexDocumentFile.IsDocument(path), "JSON not detected as document.");
            var service = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService");
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
            return "PASS: " + checks + " JSON storage, optimization, Drawing, reference and atomic-save checks.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(document);
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        }
    }
}
