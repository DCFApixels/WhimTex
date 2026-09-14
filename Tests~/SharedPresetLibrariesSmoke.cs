// Unity Pipeline eval_file. Uses a unique Temp fixture and restores the user's folder preference.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var assembly = typeof(DCFApixels.WhimTex.ShaderFX).Assembly;
System.Type Type(string name) => assembly.GetType("DCFApixels.WhimTex." + name, true);
object Call(string type, string method, params object[] args) => Type(type).GetMethod(method, Hidden).Invoke(null, args);
object Field(object obj, string name) => obj.GetType().GetField(name, Hidden).GetValue(obj);
void Set(object obj, string name, object value) => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
void Reject(System.Action action, string message)
{
    try { action(); }
    catch (System.Reflection.TargetInvocationException e) when (e.InnerException is System.IO.IOException || e.InnerException is System.FormatException || e.InnerException is System.InvalidOperationException) { checks++; return; }
    throw new System.Exception("Accepted invalid input: " + message);
}
var metadata = Type("ShaderFXMetadata");
System.Collections.Generic.List<DCFApixels.WhimTex.ShaderFXParameter> Parse(string source) =>
    (System.Collections.Generic.List<DCFApixels.WhimTex.ShaderFXParameter>)metadata.GetMethod("Parse", Hidden).Invoke(null, new object[] { source, true, null });
string temp = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp"));
string root = System.IO.Path.Combine(temp, "SharedPresetsSmoke_" + System.Guid.NewGuid().ToString("N"));
const string key = "DCFApixels.WhimTex.PresetsFolder";
bool hadKey = UnityEditor.EditorPrefs.HasKey(key);
string previous = UnityEditor.EditorPrefs.GetString(key);
var owned = new System.Collections.Generic.List<UnityEngine.Object>();
var pathsField = Type("PresetLibraryPaths").GetField("projectPaths", Hidden);
try
{
    System.IO.Directory.CreateDirectory(root);
    UnityEditor.EditorPrefs.SetString(key, root);
    string fxFolder = (string)Type("ShaderFXCatalog").GetProperty("Folder", Hidden).GetValue(null);
    string brushFolder = (string)Type("BrushPresetLibrary").GetProperty("Folder", Hidden).GetValue(null);
    Check(fxFolder == System.IO.Path.Combine(root, "ShaderFX"), "FX subfolder");
    Check(brushFolder == System.IO.Path.Combine(root, "Brushes"), "Brushes subfolder");
    const string code = "// @whimtex-effect Test/Original\n// @param float _Amount = 1 [0 .. 4]\n// @param color _Tint = (1, 1, 1, 1)\n// @param float4 _Vector = (0, 0, 0, 0)\n// @param transform2D _Area\n// @param texture2D _Mask\nfloat4 ApplyFX(float2 uv, float4 color) { return color * _Amount; }\n";
    var values = Parse(code);
    values[0].floatValue = 2.75f;
    values[1].colorValue = new UnityEngine.Color(.25f, .5f, 3f, .75f);
    values[2].vectorValue = new UnityEngine.Vector4(-1f, 2f, .125f, 4f);
    values[3].transformValue = new DCFApixels.WhimTex.ShaderFXTransform { position = new UnityEngine.Vector2(.2f, .7f), size = new UnityEngine.Vector2(-.3f, .4f), rotation = 27f };
    var effect = (DCFApixels.WhimTex.ShaderFX)Call("ShaderFX", "CreateAgentDraft", null, code, values);
    owned.Add(effect);
    var effectEditor = UnityEditor.Editor.CreateEditor(effect); owned.Add(effectEditor);
    var editorView = effectEditor.CreateInspectorGUI();
    bool foundSave = false;
    foreach (var child in editorView.Children())
        if (child is UnityEngine.UIElements.Button button && button.text == "Save HLSL Preset…") foundSave = true;
    Check(foundSave, "Save preset is in the code editor");
    string before = UnityEngine.JsonUtility.ToJson(effect);
    string generated = (string)Call("ShaderFXPresetWriter", "BuildSource", effect, "Test/Saved");
    var defaults = Parse(generated);
    Check(generated.StartsWith("// @whimtex-effect Test/Saved\n"), "Required first line");
    Check(defaults.Count == 5 && defaults[0].floatValue == 2.75f && defaults[0].maximum == 4, "Current float + range");
    Check(defaults[1].colorValue == values[1].colorValue, "HDR color default");
    Check(defaults[2].vectorValue == values[2].vectorValue, "Vector default");
    Check(defaults[3].transformValue.position == values[3].transformValue.position && defaults[3].transformValue.size == values[3].transformValue.size && defaults[3].transformValue.rotation == 27, "Transform default");
    Check(before == UnityEngine.JsonUtility.ToJson(effect), "Export does not edit original FX");
    var culture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
        Check((string)Call("ShaderFXPresetWriter", "BuildSource", effect, "Test/Saved") == generated, "Locale invariant numbers");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    Reject(() => Call("ShaderFXPresetWriter", "BuildSource", effect, "Bad\nName"), "Header injection");
    var transientTip = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
    transientTip.hideFlags = UnityEngine.HideFlags.HideAndDontSave; owned.Add(transientTip);
    values[4].textureValue = transientTip;
    Reject(() => Call("ShaderFXPresetWriter", "BuildSource", effect, "Test/Transient"), "Unsaved texture default");
    values[4].textureValue = null;
    foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Packages/com.dcfapixels.whimtex" }))
    {
        var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
        if (texture == null) continue;
        values[4].textureValue = texture;
        Check(Parse((string)Call("ShaderFXPresetWriter", "BuildSource", effect, "Test/Texture"))[4].textureValue == texture, "Persistent texture GUID roundtrip");
        break;
    }
    values[4].textureValue = null;
    string saved = System.IO.Path.Combine(fxFolder, "Nested", "Saved.hlsl");
    Call("ShaderFXPresetWriter", "Save", saved, effect, false);
    Check(Parse(System.IO.File.ReadAllText(saved))[0].floatValue == 2.75f, "Save HLSL defaults");
    Reject(() => Call("ShaderFXPresetWriter", "Save", saved, effect, false), "Overwrite protection");
    Call("ShaderFXPresetWriter", "Save", saved, effect, true);
    Check(System.IO.File.Exists(saved + ".bak"), "Overwrite backup");
    var catalog = (System.Collections.IEnumerable)Call("ShaderFXCatalog", "GetEntries");
    object entry = null;
    foreach (object candidate in catalog) if ((string)Field(candidate, "path") == saved) entry = candidate;
    Check(entry != null && (bool)Field(entry, "user"), "User HLSL recursive discovery");
    var instance = (DCFApixels.WhimTex.ShaderFX)Call("ShaderFX", "FromCatalog", null, entry); owned.Add(instance);
    Check(!(bool)Type("ShaderFX").GetProperty("IsCatalogLinked", Hidden).GetValue(instance), "User preset embedded independently");
    Check((bool)Type("ShaderFX").GetProperty("HasAppliedShader", Hidden).GetValue(instance), "User preset compiles");
    Check(((System.Collections.Generic.IReadOnlyList<DCFApixels.WhimTex.ShaderFXParameter>)Type("ShaderFX").GetProperty("Parameters", Hidden).GetValue(instance))[0].floatValue == 2.75f, "Applied instance uses saved defaults");
    System.IO.File.WriteAllText(System.IO.Path.Combine(fxFolder, "Unmarked.hlsl"), "float Unmarked() { return 1; }");
    foreach (object candidate in (System.Collections.IEnumerable)Call("ShaderFXCatalog", "GetEntries"))
        Check((string)Field(candidate, "path") != System.IO.Path.Combine(fxFolder, "Unmarked.hlsl"), "Unmarked file excluded");
    string helper = System.IO.Path.Combine(fxFolder, "Helper.hlsl");
    System.IO.File.WriteAllText(helper, "float SharedGain() { return 2; }\n");
    string expanded = (string)Call("ShaderFXSourceBuilder", "ExportIncludes", "#include \"./Helper.hlsl\"\n", System.IO.Path.Combine(fxFolder, "Source.hlsl"));
    Check(expanded.Contains("float SharedGain()"), "Custom include embedded");
    Check(((string)Call("ShaderFXSourceBuilder", "ExportIncludes", "#include \"UnityCG.cginc\"\n", System.IO.Path.Combine(fxFolder, "Source.hlsl"))).Contains("#include \"UnityCG.cginc\""), "Engine include retained");
    System.IO.File.WriteAllText(helper, "#include \"./Helper.hlsl\"\n");
    Reject(() => Call("ShaderFXSourceBuilder", "ExportIncludes", "#include \"./Helper.hlsl\"\n", System.IO.Path.Combine(fxFolder, "Source.hlsl")), "Cyclic includes rejected");
    Reject(() => Call("ShaderFXSourceBuilder", "ExportIncludes", "#include \"../Outside.hlsl\"\n", System.IO.Path.Combine(fxFolder, "Source.hlsl")), "User include escape");
    var settings = System.Activator.CreateInstance(Type("PaintToolSettings"), true);
    Set(settings, "brushSize", 57f);
    Set(Field(settings, "dynamics"), "tip", transientTip);
    string brush = System.IO.Path.Combine(brushFolder, "Nested", "Test.sebrush");
    Call("BrushPresetLibrary", "Save", brush, settings, false);
    var loadArgs = new object[] { brush, null };
    var brushPreset = Type("BrushPresetLibrary").GetMethod("Load", Hidden).Invoke(null, loadArgs);
    owned.Add((UnityEngine.Texture2D)loadArgs[1]);
    Check((float)Field(brushPreset, "size") == 57f && loadArgs[1] != null, "Portable brush with texture roundtrip");
    Check(System.Array.IndexOf((string[])Call("BrushPresetLibrary", "List"), brush) >= 0, "User brush recursive discovery");
    Check((string)Call("BrushPresetLibrary", "MenuLabel", brush) == "User/Nested/Test", "Brush folder menu");
    string fakeProjectBrush = System.IO.Path.Combine(root, "ProjectBrush.sebrush");
    System.IO.File.Copy(brush, fakeProjectBrush);
    pathsField.SetValue(null, new[] { fakeProjectBrush });
    Check(System.Array.IndexOf((string[])Call("BrushPresetLibrary", "List"), fakeProjectBrush) >= 0, "Project index merged with user library");
    string projectDestination = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Presets", "Brush.sebrush");
    Check((string)Call("BrushPresetLibrary", "MenuLabel", projectDestination) == "Project/Assets/Presets/Brush", "Project brush menu");
    Check((string)Call("BrushPresetLibrary", "ValidateDestination", projectDestination) == System.IO.Path.GetFullPath(projectDestination), "Project brush destination allowed");
    Reject(() => Call("BrushPresetLibrary", "ValidateDestination", System.IO.Path.Combine(root, "BrushesOther", "Brush.sebrush")), "Sibling prefix rejected");
    Reject(() => Call("BrushPresetLibrary", "ValidateDestination", System.IO.Path.Combine(brushFolder, "Bad.txt")), "Wrong extension rejected");
    UnityEditor.EditorPrefs.SetString(key, System.IO.Path.Combine(root, "OtherLibrary"));
    foreach (object candidate in (System.Collections.IEnumerable)Call("ShaderFXCatalog", "GetEntries"))
        Check((string)Field(candidate, "path") != saved, "Folder switch removes old catalog entries");
    Check((bool)Type("ShaderFX").GetProperty("HasAppliedShader", Hidden).GetValue(instance), "Folder switch retains applied copy");
    return "Shared preset libraries passed: " + checks + " checks.";
}
finally
{
    pathsField.SetValue(null, null);
    if (hadKey) UnityEditor.EditorPrefs.SetString(key, previous); else UnityEditor.EditorPrefs.DeleteKey(key);
    foreach (var obj in owned) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
    if (root.StartsWith(temp + System.IO.Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(root).StartsWith("SharedPresetsSmoke_"))
        System.IO.Directory.Delete(root, true);
    Call("ShaderFXCatalog", "GetEntries");
}
