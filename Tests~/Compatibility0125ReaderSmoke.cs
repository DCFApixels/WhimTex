// run_script entry Compatibility0125ReaderSmoke.Run. Adversarial payload checks, not golden fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using Object = UnityEngine.Object;

public static class Compatibility0125ReaderSmoke
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Serializer => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("FAIL: " + message); }
    static object ReadResult(byte[] bytes, WhimTexDocumentContainer container) => Serializer.GetMethod("Deserialize", Any)
        .Invoke(null, new object[] { bytes, container, typeof(TextureCompositor), null, false });
    static object Property(object result, string name) => result.GetType().GetProperty(name, Any).GetValue(result);
    static TextureCompositor Read(byte[] bytes, WhimTexDocumentContainer container) => (TextureCompositor)Property(ReadResult(bytes, container), "Model");
    static IReadOnlyList<string> Diagnostics(object result, string property) => (IReadOnlyList<string>)Property(result, property);
    static byte[] Payload(string retiredName = "outputSettings", bool crossReference = false, bool malformed = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(1); writer.Write((byte)29); writer.Write(typeof(TextureCompositor).FullName); writer.Write(4);
        writer.Write("width"); writer.Write((byte)6); writer.Write(8);
        writer.Write(retiredName); writer.Write((byte)29); writer.Write("Removed0125.OutputSettings");
        writer.Write(malformed ? -1 : 3);
        if (!malformed)
        {
            writer.Write("child"); writer.Write((byte)29); writer.Write("Removed0125.Nested"); writer.Write(1);
            writer.Write("missingAsset"); writer.Write((byte)27); writer.Write("0123456789abcdef0123456789abcdef"); writer.Write(123L);
            writer.Write("sameChild"); writer.Write((byte)31); writer.Write(2);
            writer.Write("oldEnum"); writer.Write((byte)14); writer.Write("Removed0125.Enum"); writer.Write("Old"); writer.Write(7L);
        }
        writer.Write("height"); writer.Write((byte)6); writer.Write(8);
        writer.Write("layers"); writer.Write((byte)30); writer.Write(1);
        if (crossReference) { writer.Write((byte)31); writer.Write(1); }
        else
        {
            writer.Write((byte)29); writer.Write(typeof(Layer).FullName); writer.Write(2);
            writer.Write("layerName"); writer.Write((byte)13); writer.Write("After discarded objects");
            writer.Write("behaviour"); writer.Write((byte)29); writer.Write(typeof(ColorFillLayerBehaviour).FullName); writer.Write(1);
            writer.Write("storedColor"); writer.Write((byte)21); writer.Write(.25f); writer.Write(.5f); writer.Write(.75f); writer.Write(1f);
        }
        writer.Flush(); return stream.ToArray();
    }
    static void Reject(byte[] bytes, string label)
    {
        using var container = new WhimTexDocumentContainer();
        TextureCompositor doc = null;
        try { doc = Read(bytes, container); throw new Exception("Accepted " + label); }
        catch (TargetInvocationException error) { Check(error.InnerException is WhimTexDocumentException, label + ": structured rejection"); }
        finally { if (doc != null) Object.DestroyImmediate(doc); }
    }
    static byte[] RetiredValue(Action<BinaryWriter> value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(1); writer.Write((byte)29); writer.Write(typeof(TextureCompositor).FullName); writer.Write(1);
        writer.Write("outputSettings"); value(writer); writer.Flush(); return stream.ToArray();
    }
    public static string Run()
    {
        checks = 0;
        using var container = new WhimTexDocumentContainer();
        var read = ReadResult(Payload(), container);
        var doc = (TextureCompositor)Property(read, "Model");
        try
        {
            Check(doc.width == 8 && doc.height == 8 && doc.layers.Count == 1, "parser alignment after retired subgraph");
            Check(doc.layers[0].layerName == "After discarded objects" && doc.layers[0].Behaviour is ColorFillLayerBehaviour fill && fill.color.b == .75f,
                "active data after retired object slots");
            Check(Diagnostics(read, "SkippedFields").Count == 0 && Diagnostics(read, "MissingTypes").Count == 0 && Diagnostics(read, "UnresolvedReferences").Count == 0,
                "retired types/references are not resolved or reported as lost data");
            byte[] rewritten = (byte[])Serializer.GetMethod("Serialize", Any).Invoke(null, new object[] { doc, container });
            string text = System.Text.Encoding.UTF8.GetString(rewritten);
            foreach (string name in new[] { "outputSettings", "savedOutputSettings", "spriteSlices", "Removed0125" })
                Check(!text.Contains(name), "modern model omits " + name);
        }
        finally { Object.DestroyImmediate(doc); }
        var unknownRead = ReadResult(Payload("unknownSettings"), container);
        var unknown = (TextureCompositor)Property(unknownRead, "Model");
        try { Check(Diagnostics(unknownRead, "SkippedFields").Count > 0, "non-whitelisted unknown field remains diagnosed"); }
        finally { Object.DestroyImmediate(unknown); }
        Reject(Payload(crossReference: true), "active reference to discarded object");
        Reject(Payload(malformed: true), "negative retired field count");
        Reject(RetiredValue(writer => { writer.Write((byte)30); writer.Write(1000001); }), "oversized retired list");
        Reject(RetiredValue(writer => {
            for (int i = 0; i < 130; i++) { writer.Write((byte)29); writer.Write("Removed0125.Deep"); writer.Write(1); writer.Write("next"); }
            writer.Write((byte)0);
        }), "excessive retired depth");
        Reject(RetiredValue(writer => { writer.Write((byte)31); writer.Write(999); }), "invalid retired back-reference");
        Reject(RetiredValue(writer => writer.Write((byte)255)), "unknown retired value tag");
        Reject(RetiredValue(writer => writer.Write((byte)17)), "truncated retired vector");
        Reject(RetiredValue(writer => {
            writer.Write((byte)28); writer.Write(0); writer.Write(1); writer.Write("RGBA32"); writer.Write(1);
            writer.Write(false); writer.Write("missing");
        }), "invalid retired texture dimensions");
        Type adapter = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexFileCompatibility0125", true);
        Check(!(bool)adapter.GetMethod("IsRetiredField", Any).Invoke(null, new object[] { typeof(ColorFillLayerBehaviour), "outputSettings" }),
            "retired field whitelist is owner-scoped");
        var valid = ScriptableObject.CreateInstance<TextureCompositor>(); valid.width = valid.height = 8;
        valid.layers.Add(new Layer(new ColorFillLayerBehaviour()));
        typeof(TextureCompositor).GetMethod("NormalizeModel", Any).Invoke(valid, null);
        try
        {
            string json = WhimTexDocumentJson.Write(valid, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            string retired = json.Replace("\"document\": {", "\"document\": {\"spriteSlices\":[{\"$type\":\"Removed0125.Slice\",\"unused\":7}],");
            Check(retired != json, "JSON adversarial insertion matched");
            using (var result = WhimTexDocumentJson.Read(retired)) Check(result.Document.layers.Count == 1 && result.Warnings.Count == 0, "retired JSON data accepted");
            string unexpected = json.Replace("\"document\": {", "\"document\": {\"unknownSettings\":7,");
            bool rejected = false;
            try { using var result = WhimTexDocumentJson.Read(unexpected); }
            catch (WhimTexDocumentException) { rejected = true; }
            Check(rejected, "unknown JSON data still rejected");
        }
        finally { Object.DestroyImmediate(valid); }
        return "PASS: compatibility reader guards=" + checks + "; discarded subgraphs are bounded, live references and unknown data remain protected.";
    }
}
