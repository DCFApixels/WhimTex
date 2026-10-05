// run_script entry GradientClipboardCleanupTests.Run. Only owned transient values and a unique Temp folder.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class GradientClipboardCleanupTests
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Preference = "DCFApixels.WhimTex.PresetsFolder";
    static Assembly Assembly => typeof(WhimTexGradient).Assembly;
    static int checks;
    static void Check(bool ok, string reason) { checks++; UnityBRun.Check(!(!ok), reason); }
    static object Call(string type, string method, params object[] args) => Assembly.GetType("DCFApixels.WhimTex." + type, true)
        .GetMethod(method, Any).Invoke(null, args);
    static WhimTexGradient Read(string text) => (WhimTexGradient)Call("WhimTexGradientClipboard", "Read", text);
    static string Write(WhimTexGradient value) => (string)Call("WhimTexGradientClipboard", "Write", value);
    static void Same(WhimTexGradient expected, WhimTexGradient actual, string reason)
    {
        Check(actual.Equals(expected), reason + ": keys/settings");
        for (int i = -32; i <= 288; i++)
            Check(actual.Evaluate(i / 256f).Equals(expected.Evaluate(i / 256f)), reason + ": sample " + i);
    }
    static void Reject(Action action, string label)
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException?.GetType().FullName == "DCFApixels.WhimTex.WhimTexApiException")
        { Check(true, label); return; }
        throw new Exception("Accepted " + label);
    }
    static string Body(string json)
    {
        int start = json.IndexOf('{', json.IndexOf("\"gradient\":", StringComparison.Ordinal));
        return json.Substring(start, json.LastIndexOf('}') - start).Trim();
    }
    static byte[] Encode(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write(writer);
        return stream.ToArray();
    }
    static void UnknownDocument(string folder)
    {
        var document = UnityBRun.Create<TextureCompositor>();
        TextureCompositor loaded = null; Texture2D image = null;
        try
        {
            document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 8;
            document.layers.Add(new GradientLayerBehaviour());
            typeof(TextureCompositor).GetMethod("NormalizeModel", Any).Invoke(document, null);
            using var container = new WhimTexDocumentContainer();
            byte[] model = (byte[])Call("WhimTexDocumentSerializer", "Serialize", document, container);
            byte[] header = Encode(writer => { writer.Write((byte)29); writer.Write(typeof(WhimTexGradient).FullName); });
            int offset = -1;
            for (int i = 0; i <= model.Length - header.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < header.Length; j++) if (model[i+j] != header[j]) { match = false; break; }
                if (match) { Check(offset == -1, "Unique gradient record"); offset = i + header.Length; }
            }
            Check(offset >= 0, "Gradient record found");
            int count;
            using (var reader = new BinaryReader(new MemoryStream(model)))
            { reader.BaseStream.Position = offset; count = reader.ReadInt32(); }
            Check(count == 6, "Canonical gradient has six fields");
            byte[] extra = Encode(writer => { writer.Write("transition"); writer.Write((byte)14);
                writer.Write("DCFApixels.WhimTex.WhimTexGradientTransition"); writer.Write("Rounded"); writer.Write(5L); });
            byte[] altered = Encode(writer => { writer.Write(model, 0, offset); writer.Write(count + 1);
                writer.Write(extra); writer.Write(model, offset + 4, model.Length - offset - 4); });
            container.Set("document", altered); image = document.ComposeCanvas();
            byte[] carrier = (byte[])Call("WhimTexTiffCarrier", "Write", container, image, null);
            string path = Path.Combine(folder, "UnknownTransition.tiff"); File.WriteAllBytes(path, carrier);
            loaded = WhimTexDocumentFile.Load(path);
            string warning = (string)typeof(TextureCompositor).GetField("documentLoadWarning", Any).GetValue(loaded);
            Check(warning == "WhimTexGradient.transition", "TIFF unknown transition diagnosed");
            Same(((GradientLayerBehaviour)document.layers[0].Behaviour).gradient,
                ((GradientLayerBehaviour)loaded.layers[0].Behaviour).gradient, "Diagnosed TIFF retains known data");
            bool blocked = false;
            try { WhimTexDocumentFile.Save(loaded, path); }
            catch (WhimTexDocumentException error) { blocked = error.Message.Contains("prevent data loss"); }
            Check(blocked, "Unknown transition blocks lossy TIFF Save");
            blocked = false;
            try { WhimTexDocumentJson.Write(loaded); }
            catch (WhimTexDocumentException error) { blocked = error.Message.Contains("incompletely loaded document") && error.Message.Contains("WhimTexGradient.transition"); }
            Check(blocked, "Unknown transition blocks lossy JSON writer");
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(carrier), "Blocked Save leaves TIFF bytes intact");
            string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions {Mode = WhimTexJsonWriteMode.Full}).Json;
            string unknown = json.Replace("\"gradient\": {", "\"gradient\": {\"transition\":\"Rounded\",");
            Check(unknown != json, "JSON adversarial insertion matched");
            bool rejected = false;
            try { using var read = WhimTexDocumentJson.Read(unknown); }
            catch (WhimTexDocumentException error) { rejected = error.Message.Contains("transition"); }
            Check(rejected, "Document JSON rejects unknown gradient field");
        }
        finally
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
    static string ExecuteRun()
    {
        checks = 0;
        var gradient = new WhimTexGradient { Smoothness = .625f };
        gradient.SetKeys(new[] {new GradientColorKey(new Color(4, -.25f, .5f, .8f), 0),
            new GradientColorKey(new Color(.1f, 2, 1, .5f), .375f), new GradientColorKey(Color.blue, 1)},
            new[] {new GradientAlphaKey(.125f, .125f), new GradientAlphaKey(.875f, .75f)});
        gradient.SetMidpoint(false, 0, .3f); gradient.SetMidpoint(false, 1, .7f); gradient.SetMidpoint(true, 0, .2f);
        foreach (WhimTexGradientMode mode in Enum.GetValues(typeof(WhimTexGradientMode)))
        foreach (WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
        foreach (ColorSpace space in new[] {ColorSpace.Gamma, ColorSpace.Linear})
        {
            gradient.Mode = mode; gradient.WrapMode = wrap; gradient.ColorSpace = space;
            string json = Write(gradient);
            string body = Body(json);
            Check(body.Contains("\"mode\": \"" + mode + "\""), "Mode writes name");
            Check(body.Contains("\"wrapMode\": \"" + wrap + "\""), "Wrap writes name");
            Check(body.Contains("\"colorSpace\": \"" + space + "\""), "Color space writes name");
            Check(!body.Contains("transition"), "No transition output");
            Same(gradient, Read(json), "Envelope");
            Same(gradient, Read(body), "Unwrapped object");
            Same(gradient, Read("```json\n" + json + "\n```"), "JSON fence");
            Same(gradient, Read("```\n" + json + "\n```"), "Plain fence");
            Same(gradient, Read("\uFEFF" + json), "BOM");
        }
        const string stops = "[{\"time\":0,\"color\":[1,0,0,1]},{\"time\":1,\"color\":[0,0,1,0]}]";
        Same(Read(stops), Read("{\"colors\":" + stops + "}"), "Bare stops");
        Same(Read(stops), Read("```json\n" + stops + "\n```"), "Fenced stops");
        string raw = JsonUtility.ToJson(gradient);
        var inputs = new System.Collections.Generic.List<string> {raw, "WhimTex.Gradient/1\n" + raw,
            "WhimTex.Gradient/1\n" + Write(gradient)};
        inputs.Add("{\"colors\":[{\"time\":0,\"color\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1}},{\"time\":1,\"color\":[0,0,1,1]}]}");
        foreach (string name in new[] {"mode", "wrapMode", "colorSpace"})
            inputs.Add("{\"colors\":" + stops + ",\"" + name + "\":0}");
        foreach (string value in new[] {"0", "\"Rounded\"", "null", "{}"})
            inputs.Add("{\"colors\":" + stops + ",\"transition\":" + value + "}");
        foreach (string input in inputs)
        {
            object[] args = {input, null};
            Check(!(bool)Call("WhimTexGradientClipboard", "TryRead", args) && args[1] == null, "Unsupported clipboard rejected");
        }
        foreach (string input in inputs.GetRange(3, inputs.Count - 3))
        {
            object data = Call("AgentJson", "Parse", input);
            Reject(() => Call("WhimTexApi", "ReadGradient", data, WhimTexGradientMode.Perceptual, 107f), "API gradient");
            foreach (string name in new[] {"tipGradient", "tintGradient"})
            {
                string brush = "{\"format\":\"whimtex.brush\",\"version\":1,\"source\":\"Standard\",\"settings\":{\"" + name + "\":" + input + "}}";
                Reject(() => Call("WhimTexApi", "ReadBrushClipboard", new object[] {brush, null}), "Brush " + name);
            }
        }
        bool existed = EditorPrefs.HasKey(Preference); string previous = EditorPrefs.GetString(Preference);
        string folder = Path.GetFullPath(Path.Combine(UnityBRun.TempPath(""), "WhimTexGradientCleanup_" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Gradients"));
            EditorPrefs.SetString(Preference, folder);
            string source = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Compatibility0125/gradient.json";
            string preset = Path.Combine(folder, "Gradients", Guid.NewGuid().ToString("N") + ".json");
            byte[] frozen = File.ReadAllBytes(source); File.Copy(source, preset);
            object[] args = {null}; var entries = (IList)Call("WhimTexGradientPresets", "Read", args);
            Check(entries.Count == 1 && args[0] == null, "Frozen 0.12.5 preset discovers without warning");
            var loaded = (WhimTexGradient)entries[0].GetType().GetField("Gradient", Any).GetValue(entries[0]);
            Same(Read(File.ReadAllText(source)), loaded, "Frozen library preset");
            Same(loaded, Read(File.ReadAllText((string)Call("WhimTexGradientPresets", "Save", loaded))), "Preset resave");
            Check(Convert.ToBase64String(File.ReadAllBytes(preset)) == Convert.ToBase64String(frozen), "Original preset bytes unchanged");
            UnknownDocument(folder);
        }
        finally
        {
            if (existed) EditorPrefs.SetString(Preference, previous); else EditorPrefs.DeleteKey(Preference);
            UnityBRun.Check(UnityBRun.IsOwnedTemp(folder), "Gradient cleanup remains in owned temporary directory");
            UnityBRun.DeleteTemp(folder);
        }
        return "";
    }
    public static string Run() => UnityBRun.Run("GradientClipboardCleanupSmoke.Run", () => ExecuteRun());
}
