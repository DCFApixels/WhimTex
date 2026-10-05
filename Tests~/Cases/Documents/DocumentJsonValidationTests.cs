using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;

public static class DocumentJsonValidationTests
{
    static int checks;
    const string Header = "\"format\":\"whimtex.document\",\"version\":1,";
    static string Doc(string settings = "{}", string layers = "[]") => "{" + Header + "\"document\":" + settings + ",\"layers\":" + layers + "}";
    static string Layer(string behaviour, string extra = "") => "[{\"id\":\"fixture\",\"behaviour\":" + behaviour + extra + "}]";
    static string Noise(string fields) => Doc(layers: Layer("{\"$type\":\"NoiseLayerBehaviour\"," + fields + "}"));
    static string Curve(string key, string wrap = "\"Default\"") => Doc(layers: Layer("{\"$type\":\"ColorFillLayerBehaviour\"}",
        ",\"modifiers\":[{\"$type\":\"ShaderFX\",\"parameters\":[{\"type\":\"Curve\",\"curveValue\":{\"preWrap\":" + wrap +
        ",\"postWrap\":\"Default\",\"keys\":[" + key + "]}}]}]"));
    static void Check(bool ok, string message) { checks++; UnityBRun.Check(!(!ok), message); }
    static void Accept(string json)
    { using var read = WhimTexDocumentJson.Read(json, false); Check(read.Document != null, "Valid JSON rejected."); }
    static void Reject(string json, string path)
    {
        string error = null;
        try { using var read = WhimTexDocumentJson.Read(json, false); }
        catch (WhimTexDocumentException failure) { error = failure.Message; }
        Check(error != null && error.Contains(path), "Expected rejection at " + path + ", got: " + error);
    }
    [Serializable] public class Reply { public bool success, valid, open; public string format, error, errorCode; public string[] warnings, errors; }
    static Reply ReplyOf(string json) => (Reply)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { json, typeof(Reply) });

    static string ExecuteRun()
    {
        checks = 0;
        foreach (string value in new[] { "\"8\"", "true", "null", "{}", "[]", "1e40", "NaN", "Infinity" })
            Reject(Noise("\"scale\":" + value), "layers[0].behaviour.scale");
        foreach (string value in new[] { "\"999\"", "999", "\"perlin\"", "\" Perlin\"", "\"Perlin, Value\"", "null" })
            Reject(Noise("\"noiseType\":" + value), "layers[0].behaviour.noiseType");
        foreach (string value in new[] { "0.5", "2147483648", "-2147483649", "\"2\"", "null" })
            Reject(Noise("\"seed\":" + value), "layers[0].behaviour.seed");
        foreach (string value in new[] { "0", "\"false\"", "null" })
            Reject(Doc("{\"outputSrgb\":" + value + "}"), "document.outputSrgb");
        foreach (string value in new[] { "0", "16385", "2.5", "\"32\"", "null" })
            Reject(Doc("{\"width\":" + value + "}"), "document.width");
        Reject(Doc("{\"width\":8192,\"height\":8192}"), "document.width");
        Reject(Doc("{\"layers\":[]}"), "document.layers");
        Reject(Doc().Replace("\"version\":1", "\"version\":\"1\""), "version");
        Reject(Noise("\"offset\":[0,\"1\",0]"), "layers[0].behaviour.offset[1]");
        Reject(Noise("\"offset\":[0,1,1e40]"), "layers[0].behaviour.offset[2]");
        Reject(Noise("\"offset\":[0]"), "layers[0].behaviour.offset");
        Reject(Noise("\"offset\":[0,1,2,3]"), "layers[0].behaviour.offset");
        using (var read = WhimTexDocumentJson.Read(Noise("\"offset\":[-1.25,2.5]"), false))
        {
            Check(((NoiseLayerBehaviour)read.Document.layers[0].Behaviour).offset == new UnityEngine.Vector3(-1.25f, 2.5f, 0f), "Vector2 expands to Vector3.");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                using var restored = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode }).Json, false);
                Check(((NoiseLayerBehaviour)restored.Document.layers[0].Behaviour).offset == new UnityEngine.Vector3(-1.25f, 2.5f, 0f), "Expanded Vector3 roundtrip: " + mode);
            }
        }
        foreach (string components in new[] { "[0.1,0.2]", "[0.1,0.2,0.3]" })
        {
            using var read = WhimTexDocumentJson.Read(Doc(layers: Layer("{\"$type\":\"ShapeLayerBehaviour\",\"cornerRoundness\":" + components + "}")), false);
            var vector = ((ShapeLayerBehaviour)read.Document.layers[0].Behaviour).cornerRoundness;
            Check(vector == new UnityEngine.Vector4(.1f, .2f, components.Contains("0.3") ? .3f : 0f, 0f), "Vector2/3 expands to Vector4.");
        }
        Reject(Doc(layers: Layer("{\"$type\":\"ColorFillLayerBehaviour\",\"storedColor\":[1,0,0]}")), "storedColor");
        Reject(Doc(layers: Layer("{\"$type\":\"ColorFillLayerBehaviour\",\"storedColor\":[1,0,0,\"1\"]}")), "storedColor[3]");
        Reject(Doc(layers: Layer("{\"$type\":\"DrawingLayerBehaviour\",\"contentOmitted\":false}")), "contentOmitted");
        Reject(Doc(layers: Layer("{\"$type\":\"DrawingLayerBehaviour\",\"pixels\":null}")), "pixels");
        Reject(Doc(layers: Layer("{\"$type\":12}")), "$type");
        Reject(Doc(layers: Layer("{\"$type\":\"NoiseLayerBehaviour\",\"$id\":\"bad\"}")), "$id");
        Reject(Curve("[0,1,0,0,0,0,\"999\"]"), "keys[0][6]");
        Reject(Curve("[0,1,0,0,0,0,\"None\"]", "\"999\""), "preWrap");
        Reject(Curve("[0,1,\"NaN\",0,0,0,\"None\"]"), "keys[0][2]");
        Reject(Curve("[\"Infinity\",1,0,0,0,0,\"None\"]"), "keys[0][0]");
        Accept(Curve("[0,1,\"Infinity\",\"-Infinity\",0.3,0.3,\"Both\"]"));
        Accept(Noise("\"scale\":0,\"scaleY\":0,\"offset\":[-100,200,0]")); // native clamping/sentinel, not a UI-patch contract
        Accept(Noise("\"seed\":8.0,\"scale\":2000,\"warpStrength\":-25")); // finite persisted settings need not equal slider limits
        Accept(Noise("\"seed\":-2147483648"));
        Accept(Noise("\"seed\":2147483647"));

        string path = UnityBRun.AssetPath("__WhimTexValidation_") + Guid.NewGuid().ToString("N") + ".whimtex.json";
        string asset = "{\"$asset\":{\"guid\":\"00000000000000000000000000000000\",\"path\":\"Assets/__AbsentJsonValidation.png\",\"localId\":\"2800000\",\"type\":\"UnityEngine.Texture2D\"}}";
        string missing = Doc(layers: Layer("{\"$type\":\"FileLayerBehaviour\",\"sourceTexture\":" + asset + "}"));
        Reject(missing.Replace("\"2800000\"", "2800000"), "localId");
        try
        {
            File.WriteAllText(path, Doc("{\"width\":0}"));
            var status = ReplyOf(WhimTexApi.Status(path));
            Check(status.success && status.format == WhimTexDocumentJson.Format && !status.open, "JSON status has wrong format.");
            var opened = ReplyOf(WhimTexApi.DocumentJson("{\"apiVersion\":1,\"action\":\"open\",\"assetPath\":\"" + path + "\"}"));
            Check(!opened.success && opened.errorCode == "open_failed" && opened.error.Contains("document.width"), "Invalid open reported success or lost reason.");
            Check(!ReplyOf(WhimTexApi.Status(path)).open, "Failed open created a window.");
            var invalid = ReplyOf(WhimTexApi.Validate(path));
            Check(invalid.success && !invalid.valid && invalid.errors.Length > 0, "Validate did not return its validation report.");
            File.WriteAllText(path, missing);
            var report = ReplyOf(WhimTexApi.Validate(path));
            Check(report.success && report.valid && report.warnings.Length == 1 && report.warnings[0].Contains("Missing asset"), "Load warning missing from Validate.");
            Check(File.ReadAllText(path) == missing, "Validation changed source bytes.");
            string broken = Doc(layers: Layer("{\"$type\":\"ColorFillLayerBehaviour\"}",
                ",\"modifiers\":[{\"$type\":\"ShaderFX\",\"code\":\"// @param float _Amount = invalid\\nfloat4 ApplyFX(float2 uv,float4 color){return color;}\"}]"));
            File.WriteAllText(path, broken);
            report = ReplyOf(WhimTexApi.Validate(path));
            Check(report.success && !report.valid && report.warnings.Length > 0 && report.errors.Length > 0, "Failed FX readiness/load warnings not reported.");
        }
        finally
        {
            if (File.Exists(path)) { if (!AssetDatabase.DeleteAsset(path)) File.Delete(path); }
        }

        foreach (string file in Directory.GetFiles("Packages/com.dcfapixels.whimtex/Samples~/AgentTextures", "*.whimtex.json"))
        {
            using var read = WhimTexDocumentJson.Read(File.ReadAllText(file), false);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                Accept(WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode }).Json);
        }
        return "";
    }
    public static string Run() => UnityBRun.Run("DocumentJsonValidationSmoke.Run", () => ExecuteRun());
}
