using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using System.Collections.Generic;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class CanonicalReaderTests
{
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Serializer = typeof(WhimTexDocumentFile).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
    static object BinaryRead(byte[] bytes, WhimTexDocumentContainer container, Type type) =>
        Serializer.GetMethod("Deserialize", Any).Invoke(null, new object[] { bytes, container, type, null, false });
    static object Property(object value, string name) => value.GetType().GetProperty(name, Any).GetValue(value);
    static void Reject(TestContext context, Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (WhimTexDocumentException) { rejected = true; }
        catch (TargetInvocationException error) when (error.InnerException is WhimTexDocumentException) { rejected = true; }
        context.True(rejected, message);
    }
    static byte[] LayerPayload(params string[] names)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(1); writer.Write((byte)29); writer.Write(typeof(Layer).FullName); writer.Write(names.Length);
        foreach (string name in names)
        {
            writer.Write(name); writer.Write((byte)30); writer.Write(3);
            // One disabled FX, an empty reference slot and the same FX again.
            writer.Write((byte)29); writer.Write(typeof(ShaderFX).FullName); writer.Write(1);
            writer.Write("active"); writer.Write((byte)1); writer.Write(false);
            writer.Write((byte)0); writer.Write((byte)31); writer.Write(1);
        }
        writer.Flush(); return stream.ToArray();
    }
    static void LayerFxFiles(TestContext context)
    {
        context.True(typeof(Layer).GetField("fx") != null && typeof(Layer).GetField("modifiers") == null, "Canonical C# layer FX field, no alias");
        context.True(typeof(LayerBehaviour).GetProperty("fx") != null && typeof(LayerBehaviour).GetProperty("modifiers") == null, "Canonical behaviour API, no alias");
        context.True(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.LayerFxEditorWindow") != null &&
            typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ModifierEditorWindow") == null, "Layer-scoped FX editor name");
        const string json = "{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[" +
            "{\"id\":\"a\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$type\":\"ShaderFX\",\"$id\":\"shared\",\"active\":false},null,{\"$ref\":\"shared\"}]}," +
            "{\"id\":\"b\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"},\"fx\":[{\"$ref\":\"shared\"}]}]}";
        foreach (string input in new[] { json, json.Replace("\"fx\"", "\"modifiers\"") })
        using (var read = WhimTexDocumentJson.Read(input, false))
        {
            var fx = read.Document.layers[0].fx;
            context.Equal(0, read.Warnings.Count, "Complete FX JSON read");
            context.True(fx.Count == 3 && fx[1] == null && ReferenceEquals(fx[0], fx[2]) &&
                ReferenceEquals(fx[0], read.Document.layers[1].fx[0]) && !((ShaderFX)fx[0]).Active, "JSON list order, disabled state and shared identity");
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string written = WhimTexDocumentJson.Write(read.Document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                context.True(written.Contains("\"fx\"") && !written.Contains("\"modifiers\""), "Canonical JSON writer: " + mode);
                using var again = WhimTexDocumentJson.Read(written, false);
                context.True(again.Document.layers[0].fx.Count == 3 && ReferenceEquals(again.Document.layers[0].fx[0], again.Document.layers[1].fx[0]), "FX roundtrip: " + mode);
            }
            string snapshot = typeof(WhimTexApi).GetMethod("Snapshot", Any).Invoke(null, new object[] { read.Document, null }).ToString();
            context.True(System.Text.RegularExpressions.Regex.IsMatch(snapshot, "\"fxCount\"\\s*:\\s*3") &&
                !snapshot.Contains("modifierCount"), "Agent API exposes fxCount only");
        }
        const string minimal = "{\"format\":\"whimtex.document\",\"version\":1,\"layers\":[{\"id\":\"a\",\"behaviour\":{\"$type\":\"ColorFillLayerBehaviour\"}FIELDS}]}";
        foreach (string field in new[] { "", ",\"fx\":[]", ",\"modifiers\":[]" })
        using (var read = WhimTexDocumentJson.Read(minimal.Replace("FIELDS", field), false))
            context.True(read.Document.layers[0].fx != null && read.Document.layers[0].fx.Count == 0, "Empty/omitted FX preserves frozen v1 default");
        foreach (string fields in new[] { ",\"fx\":[],\"modifiers\":[]", ",\"modifiers\":null,\"fx\":[]", ",\"fx\":null,\"modifiers\":[]" })
            Reject(context, () => WhimTexDocumentJson.Read(minimal.Replace("FIELDS", fields), false).Dispose(), "Ambiguous JSON list rejected");
        Reject(context, () => WhimTexDocumentJson.Read(minimal.Replace("FIELDS", "").Replace("\"$type\":\"ColorFillLayerBehaviour\"", "\"$type\":\"ColorFillLayerBehaviour\",\"modifiers\":[]"), false).Dispose(), "Conversion is scoped to Layer, not behaviours");
        using var container = new WhimTexDocumentContainer();
        foreach (string name in new[] { "fx", "modifiers" })
        {
            object result = BinaryRead(LayerPayload(name), container, typeof(Layer));
            var layer = (Layer)Property(result, "Model");
            Layer again = null;
            try
            {
                context.Equal(0, ((IReadOnlyList<string>)Property(result, "SkippedFields")).Count, "Complete binary FX read");
                context.True(layer.fx.Count == 3 && layer.fx[1] == null && ReferenceEquals(layer.fx[0], layer.fx[2]) && !((ShaderFX)layer.fx[0]).Active, "Binary list order, state and identity");
                byte[] written = (byte[])Serializer.GetMethod("Serialize", Any).Invoke(null, new object[] { layer, container });
                context.True(!Encoding.UTF8.GetString(written).Contains("modifiers"), "TIFF model writer omits old field name");
                again = (Layer)Property(BinaryRead(written, container, typeof(Layer)), "Model");
                context.True(again.fx.Count == 3 && ReferenceEquals(again.fx[0], again.fx[2]), "New TIFF model roundtrip");
            }
            finally
            {
                if (again?.fx[0] != null) UnityEngine.Object.DestroyImmediate(again.fx[0]);
                if (layer.fx[0] != null) UnityEngine.Object.DestroyImmediate(layer.fx[0]);
            }
        }
        Reject(context, () => BinaryRead(LayerPayload("fx", "modifiers"), container, typeof(Layer)), "Ambiguous binary FX fields rejected");
        Reject(context, () => BinaryRead(LayerPayload("modifiers", "fx"), container, typeof(Layer)), "Reversed ambiguous binary FX fields rejected");
    }

    public static string Run() => TestContext.Run("Canonical names, layer FX file conversion and unknown-field diagnostics", context =>
    {
        var serializer = typeof(WhimTexDocumentFile).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var resolveType = serializer.GetMethod("ResolveType", flags);
        var fieldMap = serializer.GetMethod("FieldNameMap", flags);
        foreach (var type in new[] { typeof(Layer), typeof(DrawingLayerBehaviour), typeof(ShaderFX), typeof(ShaderFXParameterControl) })
            context.Equal(type, (Type)resolveType.Invoke(null, new object[] { type.FullName }), "Canonical type: " + type.Name);
        context.True(resolveType.Invoke(null, new object[] { "Unavailable.ExtensionBehaviour" }) == null, "Unknown type is not guessed");
        var controls = (System.Collections.IDictionary)fieldMap.Invoke(null, new object[] { typeof(ShaderFXParameterControl) });
        context.True(controls.Contains("groupHeaderParameter"), "0.12.5 group header field remains readable");
        context.True(!controls.Contains("groupToggleParameter"), "Pre-baseline group alias removed");
        using (var stream = new System.IO.MemoryStream())
        using (var writer = new System.IO.BinaryWriter(stream))
        using (var container = new WhimTexDocumentContainer())
        {
            writer.Write(1); writer.Write((byte)29); writer.Write(typeof(ShaderFXParameterControl).FullName);
            writer.Write(1); writer.Write("groupToggleParameter"); writer.Write((byte)0); writer.Flush();
            object read = serializer.GetMethod("Deserialize", flags).Invoke(null,
                new object[] { stream.ToArray(), container, typeof(ShaderFXParameterControl), null, false });
            var skipped = (System.Collections.Generic.IReadOnlyList<string>)read.GetType().GetProperty("SkippedFields",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(read);
            context.True(skipped.Contains("ShaderFXParameterControl.groupToggleParameter"), "Removed field is diagnosed rather than silently discarded");
        }
        var layer = (System.Collections.IDictionary)fieldMap.Invoke(null, new object[] { typeof(Layer) });
        foreach (var name in new[] { "layerName", "opacity", "behaviour" })
            context.True(layer.Contains(name), "Layer field remains readable: " + name);
        LayerFxFiles(context);
    });
}
