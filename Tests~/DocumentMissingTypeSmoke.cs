// A document written by another version may reference a layer type this build does not have.
// It must still open, keep the other layers intact, and report what it dropped.
// Run with Unity Pipeline eval_file; the report is returned as the result string.
const System.Reflection.BindingFlags Static = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var report = new System.Text.StringBuilder();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception("FAIL: " + message); checks++; }
object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
var assembly = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly;
var serializerType = assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
object Serialize(object document, DCFApixels.WhimTex.WhimTexDocumentContainer container) =>
    serializerType.GetMethod("Serialize", Static).Invoke(null, new[] { document, container });
object Deserialize(byte[] model, DCFApixels.WhimTex.WhimTexDocumentContainer container) =>
    serializerType.GetMethod("Deserialize", Static).Invoke(null, new object[] { model, container, typeof(DCFApixels.WhimTex.TextureCompositor) });
string[] MissingTypes()
{
    var list = (System.Collections.Generic.IReadOnlyList<string>)serializerType.GetProperty("LastMissingTypes", Static).GetValue(null);
    var result = new string[list.Count];
    for (int i = 0; i < list.Count; i++) result[i] = list[i];
    return result;
}
byte[] Patch(byte[] model, string from, string to)
{
    byte[] source = System.Text.Encoding.UTF8.GetBytes(from);
    byte[] target = System.Text.Encoding.UTF8.GetBytes(to);
    Check(source.Length == target.Length, "the replacement name must have the same length");
    var result = (byte[])model.Clone();
    // The first occurrence is the type name written for the first layer behaviour; later ones are
    // recovered identifiers that also embed the type name and must stay untouched.
    for (int i = 0; i + source.Length <= result.Length; i++)
    {
        bool match = true;
        for (int j = 0; j < source.Length; j++) if (result[i + j] != source[j]) { match = false; break; }
        if (!match) continue;
        System.Array.Copy(target, 0, result, i, target.Length);
        return result;
    }
    throw new System.Exception("FAIL: the type name was not found in the payload");
}

// --- a document with a behaviour that will become unknown, plus layers parsed after it ---
var doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.width = doc.height = 8;
doc.layers.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = UnityEngine.Color.blue }) { layerName = "vanishing" });
var group = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.GroupLayerBehaviour()) { layerName = "group", opacity = .5f };
group.children.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = UnityEngine.Color.green }) { layerName = "child" });
doc.layers.Add(group);
var outline = new DCFApixels.WhimTex.OutlineLayerBehaviour();
doc.layers.Add(new DCFApixels.WhimTex.Layer(outline) { layerName = "outline" });
Call(doc, "NormalizeModel");
outline.TargetLayerId = doc.layers[2].Id;
var container = new DCFApixels.WhimTex.WhimTexDocumentContainer();
byte[] model = (byte[])Serialize(doc, container);
report.Append("payload=").Append(model.Length).Append("B");

// --- simulate a build without that type ---
string known = typeof(DCFApixels.WhimTex.ColorFillLayerBehaviour).FullName;
byte[] damaged = Patch(model, known, known.Substring(0, known.Length - 1) + "X");
var loaded = (DCFApixels.WhimTex.TextureCompositor)Deserialize(damaged, container);
Check(loaded != null, "the document still loads");
Check(loaded.layers.Count == 3, "all layers survive, got " + loaded.layers.Count);
Check(loaded.layers[0].layerName == "vanishing" && loaded.layers[0].Behaviour == null,
    "the layer without its type keeps its common fields and has no behaviour");
Check(loaded.layers[1].Behaviour is DCFApixels.WhimTex.GroupLayerBehaviour, "the group behaviour survives");
Check(loaded.layers[1].children.Count == 1, "the group keeps its child, got " + loaded.layers[1].children.Count);
Check(loaded.layers[1].children[0].Behaviour is DCFApixels.WhimTex.ColorFillLayerBehaviour,
    "the second layer of the same type is unaffected, so parsing after the unknown object stayed aligned");
Check(System.Math.Abs(loaded.layers[1].opacity - .5f) < .0001f, "values parsed after the unknown object are intact");
var outlineLoaded = loaded.layers[2].Behaviour as DCFApixels.WhimTex.OutlineLayerBehaviour;
Check(outlineLoaded != null, "the outline survives");
Check(outlineLoaded.TargetLayerId == doc.layers[2].Id, "a string field parsed after the unknown object is intact");
var missing = MissingTypes();
Check(missing.Length == 1 && missing[0] == known.Substring(0, known.Length - 1) + "X", "the missing type is reported: " + string.Join(", ", missing));
report.Append(" missing=").Append(missing.Length);

// --- the loaded document must be writable again without corruption ---
var second = new DCFApixels.WhimTex.WhimTexDocumentContainer();
byte[] again = (byte[])Serialize(loaded, second);
var reloaded = (DCFApixels.WhimTex.TextureCompositor)Deserialize(again, second);
Check(reloaded.layers.Count == 3, "the document can be saved and loaded again");
Check(reloaded.layers[1].children.Count == 1 && reloaded.layers[2].Behaviour is DCFApixels.WhimTex.OutlineLayerBehaviour,
    "structure survives a second pass");
Check(MissingTypes().Length == 0, "a document without unknown types reports nothing");
return "PASS: missing type checks=" + checks + ", " + report;
