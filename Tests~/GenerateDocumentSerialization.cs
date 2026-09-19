// Generates the manual serialization of the built-in document types from the field table the automatic
// pass uses, so the generated code cannot miss a field the automatic pass would have written.
// Each level of a type hierarchy writes its own fields, because a private field of a base type is only
// reachable inside that base type; the root level owns the public contract and the value count.
// Run with Unity Pipeline eval_file. Writes src/WhimTexDocumentSerialization.Generated.cs and adds the
// partial modifier to the declarations of every type it writes for.
var report = new System.Text.StringBuilder();
var serializer = typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly
    .GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
var fieldsMethod = serializer.GetMethod("Fields",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
System.Reflection.FieldInfo[] FieldsOf(Type type) => (System.Reflection.FieldInfo[])fieldsMethod.Invoke(null, new object[] { type });

string FullName(Type type)
{
    string name = (type.FullName ?? type.Name).Replace('+', '.');
    if (!type.IsGenericType) return name;
    var text = new System.Text.StringBuilder();
    text.Append(name, 0, name.IndexOf('`'));
    text.Append('<');
    Type[] arguments = type.GetGenericArguments();
    for (int i = 0; i < arguments.Length; i++)
    {
        if (i > 0) text.Append(", ");
        text.Append(FullName(arguments[i]));
    }
    text.Append('>');
    return text.ToString();
}

bool IsUnityStruct(Type t) => t == typeof(UnityEngine.Vector2) || t == typeof(UnityEngine.Vector3)
    || t == typeof(UnityEngine.Vector4) || t == typeof(UnityEngine.Vector2Int) || t == typeof(UnityEngine.Vector3Int)
    || t == typeof(UnityEngine.Quaternion) || t == typeof(UnityEngine.Color) || t == typeof(UnityEngine.Color32)
    || t == typeof(UnityEngine.Rect) || t == typeof(UnityEngine.RectInt) || t == typeof(UnityEngine.Bounds)
    || t == typeof(UnityEngine.AnimationCurve);

bool IsList(Type t) => t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>));

string ReadCall(Type t)
{
    if (t == typeof(int)) return "reader.ReadInt()";
    if (t == typeof(long)) return "reader.ReadLong()";
    if (t == typeof(float)) return "reader.ReadFloat()";
    if (t == typeof(double)) return "reader.ReadDouble()";
    if (t == typeof(bool)) return "reader.ReadBool()";
    if (t == typeof(char)) return "reader.ReadChar()";
    if (t == typeof(string)) return "reader.ReadString()";
    if (t == typeof(UnityEngine.Texture2D)) return "reader.ReadTexture()";
    if (t.IsEnum) return "reader.ReadEnum<" + FullName(t) + ">()";
    if (t == typeof(UnityEngine.Vector2)) return "reader.ReadVector2()";
    if (t == typeof(UnityEngine.Vector3)) return "reader.ReadVector3()";
    if (t == typeof(UnityEngine.Vector4)) return "reader.ReadVector4()";
    if (t == typeof(UnityEngine.Vector2Int)) return "reader.ReadVector2Int()";
    if (t == typeof(UnityEngine.Vector3Int)) return "reader.ReadVector3Int()";
    if (t == typeof(UnityEngine.Quaternion)) return "reader.ReadQuaternion()";
    if (t == typeof(UnityEngine.Color)) return "reader.ReadColor()";
    if (t == typeof(UnityEngine.Color32)) return "reader.ReadColor32()";
    if (t == typeof(UnityEngine.Rect)) return "reader.ReadRect()";
    if (t == typeof(UnityEngine.RectInt)) return "reader.ReadRectInt()";
    if (t == typeof(UnityEngine.Bounds)) return "reader.ReadBounds()";
    if (t == typeof(UnityEngine.AnimationCurve)) return "reader.ReadCurve()";
    if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return "(" + FullName(t) + ")reader.ReadReference()";
    if (IsList(t)) return "(" + FullName(t) + ")reader.ReadList(typeof(" + FullName(t) + "))";
    return "(" + FullName(t) + ")reader.ReadObject(typeof(" + FullName(t) + "))";
}

string WriteStatement(System.Reflection.FieldInfo field)
{
    Type t = field.FieldType;
    if (t.IsEnum) return "writer.WriteEnum(\"" + field.Name + "\", " + field.Name + ");";
    if (t == typeof(UnityEngine.Texture2D)) return "writer.WriteTexture(\"" + field.Name + "\", " + field.Name + ");";
    if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return "writer.WriteReference(\"" + field.Name + "\", " + field.Name + ");";
    if (IsList(t)) return "writer.WriteList(\"" + field.Name + "\", " + field.Name + ", typeof(" + FullName(t) + "));";
    if (t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t == typeof(bool)
        || t == typeof(char) || t == typeof(string) || IsUnityStruct(t))
        return "writer.Write(\"" + field.Name + "\", " + field.Name + ");";
    return "writer.WriteObject(\"" + field.Name + "\", " + field.Name + ", typeof(" + FullName(t) + "));";
}

bool Supported(Type t)
{
    // An abstract declared type is fine: the value is written by its runtime type, so a
    // [SerializeReference] field keeps its concrete type through the same writer.
    if (t == typeof(object) || t.IsInterface || t.IsPointer || t.IsByRef) return false;
    if (IsList(t)) return Supported(t.IsArray ? t.GetElementType() : t.GetGenericArguments()[0]);
    return true;
}

var chains = new List<List<Type>>();
void Collect(Type concrete)
{
    var chain = new List<Type>();
    for (Type current = concrete; current != null && current != typeof(object); current = current.BaseType)
    {
        string space = current.Namespace ?? string.Empty;
        if (space.StartsWith("UnityEngine", StringComparison.Ordinal) || space.StartsWith("UnityEditor", StringComparison.Ordinal))
            break;
        chain.Insert(0, current);
    }
    if (chain.Count == 0) return;
    foreach (List<Type> existing in chains)
        if (existing[existing.Count - 1] == chain[chain.Count - 1]) return;
    chains.Add(chain);
}

foreach (Type type in typeof(DCFApixels.WhimTex.WhimTexDocumentFile).Assembly.GetTypes())
{
    if (!type.IsClass || type.IsAbstract) continue;
    if (typeof(DCFApixels.WhimTex.LayerBehaviour).IsAssignableFrom(type)) Collect(type);
}
Collect(typeof(DCFApixels.WhimTex.Layer));
Collect(typeof(DCFApixels.WhimTex.ShaderFX));
Collect(typeof(DCFApixels.WhimTex.TextureCompositor));

var body = new System.Text.StringBuilder();
body.Append("// Generated by Tests~/GenerateDocumentSerialization.cs from the serializer's own field table.\n");
body.Append("// Do not edit by hand: regenerate when fields change. Values keep the automatic encoding, so a\n");
body.Append("// document stays readable whichever pass wrote it.\n\n");
body.Append("namespace DCFApixels.WhimTex\n{\n");
var partials = new System.Collections.Generic.HashSet<string>();
var seen = new System.Collections.Generic.HashSet<Type>();
var skip = new List<string>();
int levelCount = 0, fieldCount = 0;

void AppendSwitch(System.Type level, System.Reflection.FieldInfo[] own, string tail)
{
    body.Append("            switch (name)\n            {\n");
    foreach (System.Reflection.FieldInfo field in own)
        body.Append("                case \"").Append(field.Name).Append("\": ").Append(field.Name)
            .Append(" = ").Append(ReadCall(field.FieldType)).Append("; return true;\n");
    body.Append("            }\n").Append(tail);
}

foreach (List<Type> chain in chains)
{
    for (int index = 0; index < chain.Count; index++)
    {
        Type level = chain[index];
        if (!seen.Add(level)) continue;
        System.Reflection.FieldInfo[] own = FieldsOf(level);
        if (index > 0)
        {
            var inherited = new System.Collections.Generic.HashSet<string>();
            foreach (System.Reflection.FieldInfo f in FieldsOf(chain[index - 1])) inherited.Add(f.Name);
            own = System.Array.FindAll(own, f => !inherited.Contains(f.Name));
        }
        bool unsupported = false;
        foreach (System.Reflection.FieldInfo f in own)
            if (!Supported(f.FieldType)) { unsupported = true; skip.Add(level.Name + "." + f.Name); }
        if (unsupported)
        {
            report.Append("SKIP ").Append(level.FullName).Append(' ');
            continue;
        }
        bool root = index == 0;
        // A sealed root has no derived levels, and a sealed type may not declare virtual members.
        string modifier = root && level.IsSealed ? string.Empty : "virtual ";
        levelCount++;
        fieldCount += own.Length;
        partials.Add(level.Name);
        body.Append("    public partial class ").Append(level.Name)
            .Append(root ? " : IWhimTexDocumentSerializable" : string.Empty).Append('\n');
        body.Append("    {\n");
        if (root)
        {
            body.Append("        public void WriteDocument(IWhimTexDocumentWriter writer)\n        {\n");
            body.Append("            if (!CoversAllFields()) { writer.WriteAutomaticFields(this); return; }\n");
            body.Append("            writer.Begin(CountLevels());\n            WriteLevels(writer);\n        }\n\n");
            body.Append("        public void ReadDocument(IWhimTexDocumentReader reader)\n        {\n");
            body.Append("            if (!CoversAllFields()) { reader.ReadAutomaticFields(this); return; }\n");
            body.Append("            int count = reader.Count;\n");
            body.Append("            for (int i = 0; i < count; i++)\n            {\n");
            body.Append("                string name = reader.NextName();\n");
            body.Append("                if (!ReadOwn(name, reader)) reader.Skip();\n            }\n        }\n\n");
            body.Append("        private static readonly System.Collections.Generic.Dictionary<System.Type, bool> Coverage = new System.Collections.Generic.Dictionary<System.Type, bool>();\n\n");
            body.Append("        /// <summary>\n");
            body.Append("        /// Generated code is used only while it covers exactly the fields the automatic pass would\n");
            body.Append("        /// write. A behaviour defined outside this assembly, or a field added since the last\n");
            body.Append("        /// generation, falls back to the automatic pass instead of losing its values.\n");
            body.Append("        /// </summary>\n");
            body.Append("        private bool CoversAllFields()\n        {\n");
            body.Append("            System.Type type = GetType();\n");
            body.Append("            lock (Coverage)\n            {\n");
            body.Append("                if (Coverage.TryGetValue(type, out bool cached)) return cached;\n            }\n");
            body.Append("            bool covers = CountLevels() == WhimTexDocumentSerializer.ReflectedFieldCount(type);\n");
            body.Append("            lock (Coverage) Coverage[type] = covers;\n            return covers;\n        }\n\n");
            body.Append("        protected ").Append(modifier).Append("int CountLevels() => ").Append(own.Length).Append(";\n\n");
            body.Append("        protected ").Append(modifier).Append("void WriteLevels(IWhimTexDocumentWriter writer)\n        {\n");
            foreach (System.Reflection.FieldInfo field in own) body.Append("            ").Append(WriteStatement(field)).Append('\n');
            body.Append("        }\n\n");
            body.Append("        protected ").Append(modifier).Append("bool ReadOwn(string name, IWhimTexDocumentReader reader)\n        {\n");
            AppendSwitch(level, own, "            return false;\n");
            body.Append("        }\n    }\n\n");
        }
        else
        {
            body.Append("        protected override int CountLevels() => base.CountLevels() + ").Append(own.Length).Append(";\n\n");
            body.Append("        protected override void WriteLevels(IWhimTexDocumentWriter writer)\n        {\n");
            body.Append("            base.WriteLevels(writer);\n");
            foreach (System.Reflection.FieldInfo field in own) body.Append("            ").Append(WriteStatement(field)).Append('\n');
            body.Append("        }\n\n");
            body.Append("        protected override bool ReadOwn(string name, IWhimTexDocumentReader reader)\n        {\n");
            AppendSwitch(level, own, "            return base.ReadOwn(name, reader);\n");
            body.Append("        }\n    }\n\n");
        }
    }
}
body.Append("}\n");

string output = "Packages/com.dcfapixels.whimtex/src/WhimTexDocumentSerialization.Generated.cs";
System.IO.File.WriteAllText(output, body.ToString());

int patched = 0;
foreach (string file in System.IO.Directory.GetFiles("Packages/com.dcfapixels.whimtex/src", "*.cs",
             System.IO.SearchOption.AllDirectories))
{
    if (file.EndsWith("Generated.cs", StringComparison.Ordinal)) continue;
    string text = System.IO.File.ReadAllText(file);
    string updated = text;
    foreach (string name in partials)
        updated = System.Text.RegularExpressions.Regex.Replace(updated, @"(?<!partial )\bclass\s+" + name + @"\b", "partial class " + name);
    if (updated != text) { System.IO.File.WriteAllText(file, updated); patched++; }
}

return "GENERATED: types=" + partials.Count + " levels=" + levelCount + " fields=" + fieldCount
    + " filesPatched=" + patched + " skipped=[" + string.Join(", ", skip) + "] " + report;
