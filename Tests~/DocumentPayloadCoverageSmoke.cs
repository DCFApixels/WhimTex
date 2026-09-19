// Serializer coverage: build a document with one layer of every LayerBehaviour type found in the assembly,
// fill every Unity-serializable field with a non-default value, round-trip it and compare the whole graph
// field by field. Catches any field the serializer silently drops.
// Run with Unity Pipeline eval_file; the report is returned as the result string.
const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                           System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
var report = new System.Text.StringBuilder();
int checks = 0;
string phase = "build";
void Check(bool ok, string message) { if (!ok) throw new System.Exception("FAIL[" + phase + "]: " + message); checks++; }

// Fields Unity would serialize, independent of the serializer's own list.
var derived = new System.Collections.Generic.HashSet<string>
{
    "compiledShader", "appliedCode", "appliedSource", "appliedParameters", "diagnostics", "lastApplyFailed",
    "embeddedOwner", "transformCache", "outputTexture", "outputSprite", "sliceOutputs", "documentLoadWarning", "documentBinding"
};
System.Collections.Generic.List<System.Reflection.FieldInfo> Fields(Type type)
{
    var list = new System.Collections.Generic.List<System.Reflection.FieldInfo>();
    for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
    {
        string space = current.Namespace ?? string.Empty;
        if (space.StartsWith("UnityEngine") || space.StartsWith("UnityEditor")) break;
        foreach (var field in current.GetFields(All))
        {
            if (field.IsStatic || field.IsInitOnly || field.IsNotSerialized) continue;
            if (!field.IsPublic && !System.Attribute.IsDefined(field, typeof(UnityEngine.SerializeField)) &&
                !System.Attribute.IsDefined(field, typeof(UnityEngine.SerializeReference))) continue;
            // Identity fields stay untouched: rewriting ids would break layer lookup and effect targets.
            if (field.Name == "id" || field.Name.EndsWith("Id", StringComparison.Ordinal) ||
                field.Name.EndsWith("Guid", StringComparison.Ordinal)) continue;
            if (derived.Contains(field.Name)) continue;
            list.Add(field);
        }
    }
    return list;
}
bool IsUnityType(Type type) { var s = type.Namespace ?? ""; return s.StartsWith("UnityEngine") || s.StartsWith("UnityEditor"); }
bool IsLeaf(Type type) =>
    type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
    type == typeof(UnityEngine.Vector2) || type == typeof(UnityEngine.Vector3) || type == typeof(UnityEngine.Vector4) ||
    type == typeof(UnityEngine.Vector2Int) || type == typeof(UnityEngine.Vector3Int) || type == typeof(UnityEngine.Quaternion) ||
    type == typeof(UnityEngine.Color) || type == typeof(UnityEngine.Color32) || type == typeof(UnityEngine.Rect) ||
    type == typeof(UnityEngine.RectInt) || type == typeof(UnityEngine.Bounds);

int counter = 0;
bool Mutate(object target, Type type, int depth)
{
    if (target == null || depth > 6 || IsUnityType(type)) return false;
    bool touched = false;
    foreach (var field in Fields(type))
    {
        Type fieldType = field.FieldType;
        if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType)) continue;
        if (typeof(System.Collections.IList).IsAssignableFrom(fieldType)) continue;
        if (fieldType == typeof(UnityEngine.AnimationCurve)) { field.SetValue(target, new UnityEngine.AnimationCurve(new UnityEngine.Keyframe(0f, 0.1f), new UnityEngine.Keyframe(1f, 0.7f))); touched = true; continue; }
        object value = field.GetValue(target);
        counter++;
        if (fieldType.IsEnum)
        {
            var values = Enum.GetValues(fieldType);
            if (values.Length > 1) field.SetValue(target, values.GetValue(values.Length - 1));
            touched = true;
        }
        else if (fieldType == typeof(bool)) { field.SetValue(target, true); touched = true; }
        else if (fieldType == typeof(int)) { field.SetValue(target, counter + 7); touched = true; }
        else if (fieldType == typeof(uint)) { field.SetValue(target, (uint)(counter + 7)); touched = true; }
        else if (fieldType == typeof(short)) { field.SetValue(target, (short)(counter + 7)); touched = true; }
        else if (fieldType == typeof(byte)) { field.SetValue(target, (byte)((counter % 200) + 20)); touched = true; }
        else if (fieldType == typeof(long)) { field.SetValue(target, (long)counter + 7); touched = true; }
        else if (fieldType == typeof(float)) { field.SetValue(target, counter + .25f); touched = true; }
        else if (fieldType == typeof(double)) { field.SetValue(target, counter + .3125d); touched = true; }
        else if (fieldType == typeof(string)) { field.SetValue(target, "value" + counter); touched = true; }
        else if (fieldType == typeof(UnityEngine.Vector2)) { field.SetValue(target, new UnityEngine.Vector2(counter, counter + 1)); touched = true; }
        else if (fieldType == typeof(UnityEngine.Vector3)) { field.SetValue(target, new UnityEngine.Vector3(counter, counter + 1, counter + 2)); touched = true; }
        else if (fieldType == typeof(UnityEngine.Vector4)) { field.SetValue(target, new UnityEngine.Vector4(counter, counter + 1, counter + 2, counter + 3)); touched = true; }
        else if (fieldType == typeof(UnityEngine.Color)) { field.SetValue(target, new UnityEngine.Color(.125f, .25f, .5f, .875f)); touched = true; }
        else if (fieldType == typeof(UnityEngine.Rect)) { field.SetValue(target, new UnityEngine.Rect(1f, 2f, 3f, 4f)); touched = true; }
        else if (IsUnityType(fieldType)) { }
        else if (value == null && !fieldType.IsValueType && fieldType.GetConstructor(Type.EmptyTypes) != null)
        {
            var nested = Activator.CreateInstance(fieldType);
            field.SetValue(target, nested);
            if (Mutate(nested, fieldType, depth + 1)) touched = true;
        }
        else if (value != null && Mutate(value, fieldType, depth + 1)) touched = true;
    }
    return touched;
}
string Compare(object expected, object actual, Type type, int depth, System.Collections.Generic.HashSet<object> seen)
{
    if (depth > 8) return null;
    if (expected == null || actual == null) return ReferenceEquals(expected, actual) ? null : "null mismatch on " + type.Name;
    if (IsLeaf(type))
    {
        if (expected is float a && actual is float b) return System.Math.Abs(a - b) <= 1e-6f ? null : type.Name + ": " + a + " != " + b;
        if (expected is double c && actual is double d) return System.Math.Abs(c - d) <= 1e-9 ? null : type.Name + ": " + c + " != " + d;
        return expected.Equals(actual) ? null : type.Name + ": " + expected + " != " + actual;
    }
    if (type == typeof(UnityEngine.AnimationCurve))
    {
        var left = (UnityEngine.AnimationCurve)expected;
        var right = (UnityEngine.AnimationCurve)actual;
        if (left.keys.Length != right.keys.Length) return "AnimationCurve key count";
        for (int i = 0; i < left.keys.Length; i++)
            if (System.Math.Abs(left.keys[i].value - right.keys[i].value) > 1e-6f) return "AnimationCurve key " + i;
        return null;
    }
    if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return ReferenceEquals(expected, actual) ? null : null;
    if (!seen.Add(expected)) return null;
    if (expected.GetType() != actual.GetType()) return "type " + expected.GetType().Name + " != " + actual.GetType().Name;
    if (expected is System.Collections.IList leftList)
    {
        var rightList = (System.Collections.IList)actual;
        if (leftList.Count != rightList.Count) return type.Name + " count " + leftList.Count + " != " + rightList.Count;
        for (int i = 0; i < leftList.Count; i++)
        {
            string issue = Compare(leftList[i], rightList[i], leftList[i]?.GetType() ?? typeof(object), depth + 1, seen);
            if (issue != null) return type.Name + "[" + i + "] " + issue;
        }
        return null;
    }
    string nested = null;
    foreach (var field in Fields(type))
    {
        object left = field.GetValue(expected);
        object right = field.GetValue(actual);
        string issue = Compare(left, right, field.FieldType, depth + 1, seen);
        if (issue != null && nested == null) nested = type.Name + "." + field.Name + " → " + issue;
    }
    return nested;
}

// --- one layer per behaviour type ---
var behaviourTypes = new System.Collections.Generic.List<Type>();
foreach (Type type in typeof(DCFApixels.WhimTex.LayerBehaviour).Assembly.GetTypes())
    if (typeof(DCFApixels.WhimTex.LayerBehaviour).IsAssignableFrom(type) && !type.IsAbstract &&
        type.Namespace != null && type.Namespace.StartsWith("DCFApixels.WhimTex"))
        behaviourTypes.Add(type);
behaviourTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
var doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
DCFApixels.WhimTex.TextureCompositor loaded = null;
string folder = "Assets/WhimTexCoverage_" + System.Guid.NewGuid().ToString("N");
UnityEditor.AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
try
{
doc.width = doc.height = 16;
var mutated = new System.Collections.Generic.List<object>();
foreach (Type type in behaviourTypes)
{
    var behaviour = (DCFApixels.WhimTex.LayerBehaviour)Activator.CreateInstance(type, true);
    Mutate(behaviour, type, 0);
    mutated.Add(behaviour);
    var layer = new DCFApixels.WhimTex.Layer(behaviour) { layerName = "L_" + type.Name, opacity = .625f };
    layer.transform.rotation = 12.5d;
    Mutate(layer, typeof(DCFApixels.WhimTex.Layer), 0);
    doc.layers.Add(layer);
}
Mutate(doc, typeof(DCFApixels.WhimTex.TextureCompositor), 0);
var method = typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("NormalizeModel", All | System.Reflection.BindingFlags.Static);
method.Invoke(doc, null);
report.Append("behaviourTypes=").Append(behaviourTypes.Count).Append(" fieldsTouched=").Append(counter);

// --- round trip ---
phase = "roundtrip";
string path = DCFApixels.WhimTex.WhimTexDocumentFile.Save(doc, folder + "/coverage-test");
Check(DCFApixels.WhimTex.WhimTexDocumentFile.TryLoad(path, out loaded, out string error),
    "document loads: " + error);
Check(loaded.layers.Count == doc.layers.Count, "layer count " + loaded.layers.Count + " != " + doc.layers.Count);

phase = "compare";
int mismatches = 0;
var problems = new System.Text.StringBuilder();
for (int i = 0; i < doc.layers.Count; i++)
{
    var expected = doc.layers[i];
    var actual = loaded.layers[i];
    Check(expected.Behaviour != null && actual.Behaviour != null, "behaviour present on layer " + i);
    Check(expected.Behaviour.GetType() == actual.Behaviour.GetType(),
        "behaviour type " + expected.Behaviour.GetType().Name + " != " + actual.Behaviour.GetType().Name);
    string issue = Compare(expected, actual, typeof(DCFApixels.WhimTex.Layer), 0, new System.Collections.Generic.HashSet<object>());
    if (issue != null)
    {
        mismatches++;
        if (problems.Length < 900) problems.Append(" | ").Append(issue);
    }
    else checks++;
}
Check(mismatches == 0, "field mismatches=" + mismatches + problems);
string documentIssue = Compare(doc, loaded, typeof(DCFApixels.WhimTex.TextureCompositor), 0, new System.Collections.Generic.HashSet<object>());
Check(documentIssue == null, "document level: " + documentIssue);
return "PASS: coverage checks=" + checks + ", " + report;
}
finally
{
    UnityEngine.Object.DestroyImmediate(doc);
    if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
    UnityEditor.AssetDatabase.DeleteAsset(folder);
}
