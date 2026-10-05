using System;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class CanonicalReaderTests
{
    public static string Run() => TestContext.Run("Canonical names and unknown-field diagnostics", context =>
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
    });
}
