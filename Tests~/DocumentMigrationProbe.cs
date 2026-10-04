// Unity Pipeline eval_file. Canonical file names, unknown-name diagnostics and no historical aliases.
var serializer = typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly
    .GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var resolveType = serializer.GetMethod("ResolveType", F);
var fieldMap = serializer.GetMethod("FieldNameMap", F);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
foreach (var type in new[] { typeof(DCFApixels.WhimTex.Layer), typeof(DCFApixels.WhimTex.DrawingLayerBehaviour),
    typeof(DCFApixels.WhimTex.ShaderFX), typeof(DCFApixels.WhimTex.ShaderFXParameterControl) })
    Check((Type)resolveType.Invoke(null, new object[] { type.FullName }) == type, "Canonical type: " + type.Name);
Check(resolveType.Invoke(null, new object[] { "Unavailable.ExtensionBehaviour" }) == null, "Unknown type is not guessed");
var controls = (System.Collections.IDictionary)fieldMap.Invoke(null,
    new object[] { typeof(DCFApixels.WhimTex.ShaderFXParameterControl) });
Check(controls.Contains("groupHeaderParameter"), "0.12.5 group header field remains readable");
Check(!controls.Contains("groupToggleParameter"), "Pre-baseline group alias removed");
using (var stream = new System.IO.MemoryStream())
using (var writer = new System.IO.BinaryWriter(stream))
using (var container = new DCFApixels.WhimTex.WhimTexDocumentContainer())
{
    writer.Write(1); writer.Write((byte)29); writer.Write(typeof(DCFApixels.WhimTex.ShaderFXParameterControl).FullName);
    writer.Write(1); writer.Write("groupToggleParameter"); writer.Write((byte)0); writer.Flush();
    object read = serializer.GetMethod("Deserialize", F).Invoke(null,
        new object[] { stream.ToArray(), container, typeof(DCFApixels.WhimTex.ShaderFXParameterControl), null, false });
    var skipped = (System.Collections.Generic.IReadOnlyList<string>)read.GetType().GetProperty("SkippedFields",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(read);
    Check(skipped.Contains("ShaderFXParameterControl.groupToggleParameter"), "Removed field is diagnosed rather than silently discarded");
}
var layer = (System.Collections.IDictionary)fieldMap.Invoke(null, new object[] { typeof(DCFApixels.WhimTex.Layer) });
foreach (var name in new[] { "layerName", "opacity", "behaviour" })
    Check(layer.Contains(name), "Layer field remains readable: " + name);
return "PASS: " + checks + " canonical name-resolution and unknown-data guards.";
