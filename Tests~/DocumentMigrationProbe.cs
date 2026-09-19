// Temporary probe: verifies that the document migrations work, not just compile.
// 1. A type name from the old namespace must resolve through [MovedFrom] to the current type.
// 2. A field carrying [FormerlySerializedAs] must be found by its old name.
// 3. An unknown field name must be reported instead of dropped in silence.
// The aliased field is emitted at runtime, so the package needs no fixture type for the test.
// Run with Unity Pipeline eval_file.
var serializer = typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly
    .GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
if (serializer == null) return "FAIL: the serializer type was not found";
const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Static
    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var resolveType = serializer.GetMethod("ResolveType", Any);
var fieldNameMap = serializer.GetMethod("FieldNameMap", Any);
var recordSkipped = serializer.GetMethod("RecordSkippedField", Any);
var lastSkipped = serializer.GetProperty("LastSkippedFields", Any);
var report = new System.Text.StringBuilder();

// 1. MovedFrom: the package itself was renamed from DCFApixels.SpriteEditor to DCFApixels.WhimTex.
var moved = resolveType.Invoke(null, new object[] { "DCFApixels.SpriteEditor.DrawingLayerBehaviour" });
report.Append("movedType=").Append(moved == null ? "NULL" : ((Type)moved).FullName);
var movedLayer = resolveType.Invoke(null, new object[] { "DCFApixels.SpriteEditor.Layer" });
report.Append(" movedLayer=").Append(movedLayer == null ? "NULL" : ((Type)movedLayer).FullName);
var bogus = resolveType.Invoke(null, new object[] { "DCFApixels.SpriteEditor.NoSuchThing" });
report.Append(" bogus=").Append(bogus == null ? "null(ok)" : "UNEXPECTED:" + bogus);

// 2. Emit a type whose field was renamed and carries the former name.
Type attributeType = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
{
    Type[] types;
    try { types = assembly.GetTypes(); }
    catch (System.Exception) { continue; }
    foreach (var candidate in types)
        if (candidate.Name == "FormerlySerializedAsAttribute") { attributeType = candidate; break; }
    if (attributeType != null) break;
}
report.Append(" || attribute=").Append(attributeType == null ? "NOT FOUND" : attributeType.FullName);
Type emitted = null;
if (attributeType != null)
{
    var dynamic = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
        new System.Reflection.AssemblyName("WhimTexMigrationProbe"),
        System.Reflection.Emit.AssemblyBuilderAccess.Run);
    var module = dynamic.DefineDynamicModule("probe");
    var builder = module.DefineType("ProbeBehaviour",
        System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
    var field = builder.DefineField("newName", typeof(int), System.Reflection.FieldAttributes.Public);
    var constructor = attributeType.GetConstructor(new[] { typeof(string) });
    field.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(constructor, new object[] { "oldName" }));
    emitted = builder.CreateType();
}
string mapSummary = "not built";
bool oldFound = false, newFound = false, ghostFound = true;
if (emitted != null)
{
    var map = (System.Collections.IDictionary)fieldNameMap.Invoke(null, new object[] { emitted });
    oldFound = map.Contains("oldName");
    newFound = map.Contains("newName");
    var keys = new System.Text.StringBuilder();
    foreach (string key in map.Keys) keys.Append(key).Append(' ');
    mapSummary = keys.ToString();
    recordSkipped.Invoke(null, new object[] { emitted, "ghostField" });
    var skipped = (System.Collections.IEnumerable)lastSkipped.GetValue(null);
    ghostFound = false;
    foreach (string name in skipped) if (name == "ProbeBehaviour.ghostField") ghostFound = true;
}
report.Append(" || emittedMap=").Append(mapSummary)
    .Append(" oldNameFound=").Append(oldFound)
    .Append(" newNameFound=").Append(newFound)
    .Append(" ghostReported=").Append(ghostFound);

// 3. A real type keeps answering to its declared names.
var layerMap = (System.Collections.IDictionary)fieldNameMap.Invoke(null, new object[] { typeof(DCFApixels.WhimTex.Layer) });
report.Append(" || layerHasName=").Append(layerMap.Contains("layerName"))
    .Append(" layerHasOpacity=").Append(layerMap.Contains("opacity"))
    .Append(" layerHasBehaviour=").Append(layerMap.Contains("behaviour"));
return "PROBE: " + report;
