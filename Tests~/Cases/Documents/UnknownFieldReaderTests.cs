using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class UnknownFieldReaderTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static object Read(byte[] bytes, WhimTexDocumentContainer container) => Serializer.GetMethod("Deserialize", Flags)
        .Invoke(null, new object[] { bytes, container, typeof(WhimTexDocument), null, false });
    static object Property(object result, string name) => result.GetType().GetProperty(name, Flags).GetValue(result);
    static byte[] Payload(string name, Action<BinaryWriter> writeValue)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(2); writer.Write((byte)29); writer.Write(typeof(WhimTexDocument).FullName); writer.Write(3);
        writer.Write("width"); writer.Write((byte)6); writer.Write(8);
        writer.Write(name); writeValue(writer);
        writer.Write("height"); writer.Write((byte)6); writer.Write(8);
        writer.Flush(); return stream.ToArray();
    }
    static void Reject(TestContext context, byte[] bytes, string label)
    {
        using var container = new WhimTexDocumentContainer();
        try
        {
            var result = Read(bytes, container);
            if (Property(result, "Model") is WhimTexDocument model) UnityEngine.Object.DestroyImmediate(model);
            context.True(false, label);
        }
        catch (TargetInvocationException error) when (error.InnerException is WhimTexDocumentException)
        { context.True(true, label); }
    }
    public static string Run() => TestContext.Run("Unknown fields remain diagnosed and bounded", context =>
    {
        context.True(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexFileCompatibility0125") == null,
            "No retired-field adapter");
        foreach (string name in new[] { "outputSettings", "savedOutputSettings", "spriteSlices", "unknownSettings" })
        {
            using var container = new WhimTexDocumentContainer();
            var result = Read(Payload(name, writer =>
            {
                writer.Write((byte)29); writer.Write("Unavailable.ExtensionSettings"); writer.Write(1);
                writer.Write("value"); writer.Write((byte)6); writer.Write(7);
            }), container);
            var model = (WhimTexDocument)Property(result, "Model");
            try
            {
                context.Equal(8, model.width, "Current data before unknown field");
                context.Equal(8, model.height, "Parser alignment after unknown field");
                context.True(((IReadOnlyList<string>)Property(result, "SkippedFields")).Contains("WhimTexDocument." + name),
                    "Unknown and retired fields all produce loss diagnostics: " + name);
                context.True(((IReadOnlyList<string>)Property(result, "MissingTypes")).Count > 0, "Unavailable type is diagnosed");
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            string json = "{\"format\":\"whimtex.document\",\"version\":2,\"document\":{\"" + name + "\":null},\"layers\":[]}";
            bool rejected = false;
            try { using var read = WhimTexDocumentJson.Read(json, false); }
            catch (WhimTexDocumentException) { rejected = true; }
            context.True(rejected, "JSON rejects non-current fields: " + name);
        }
        Reject(context, Payload("unknown", w => { w.Write((byte)30); w.Write(1000001); }), "Oversized list rejected");
        Reject(context, Payload("unknown", w => { w.Write((byte)31); w.Write(999); }), "Invalid back-reference rejected");
        Reject(context, Payload("unknown", w => w.Write((byte)255)), "Unknown tag rejected");
        Reject(context, Payload("unknown", w =>
        {
            for (int i = 0; i < 130; i++) { w.Write((byte)29); w.Write("Unavailable.Deep"); w.Write(1); w.Write("next"); }
            w.Write((byte)0);
        }), "Excessive nesting rejected");
    });
}
