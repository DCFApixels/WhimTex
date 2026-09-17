// Run with Unity Pipeline eval_file. Transient objects only; no document/scene saves.

const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var assembly = typeof(DCFApixels.WhimTex.ShaderFX).Assembly;
var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true);
var parse = metadata.GetMethod("Parse", Hidden);
int checks = 0;
void Check(bool test, string message) { if (!test) throw new Exception(message); checks++; }
List<DCFApixels.WhimTex.ShaderFXParameter> Parse(string text, bool header = true) =>
    (List<DCFApixels.WhimTex.ShaderFXParameter>)parse.Invoke(null, new object[] { text, header, null });
void Reject(string source)
{
    try { Parse(source); }
    catch (System.Reflection.TargetInvocationException e) when (e.InnerException is FormatException) { checks++; return; }
    throw new Exception("Accepted invalid metadata: " + source);
}
const string head = "// @whimtex-effect Tests/Example\n";
var parameters = Parse("\uFEFF" + head + "// @param float _Gain = 0.02 [0 .. 0.1]\n// @param float _Min = 1 [0 ..]\n// @param float _Max = -2 [.. 10]\n// @param float _Free = 10\n// @param float4 _Vector = (0, 0, 0.5, 1)\n// @param color _Tint = (1, 0.5, 2, 1)\n// @param texture2D _Mask\n// @param transform2D _Area\n");
Check(parameters.Count == 8, "All parameter types");
Check(parameters[0].hasMinimum && parameters[0].hasMaximum && parameters[0].floatValue == .02f, "Closed range/default");
Check(parameters[1].hasMinimum && !parameters[1].hasMaximum, "Minimum only");
Check(!parameters[2].hasMinimum && parameters[2].hasMaximum, "Maximum only");
Check(!parameters[3].hasMinimum && !parameters[3].hasMaximum, "Unbounded");
Check(parameters[5].type == DCFApixels.WhimTex.ShaderFXParameterType.Color && parameters[5].colorValue.b == 2, "HDR color metadata");
Check(parameters[7].transformValue.size == new DCFApixels.WhimTex.Double2(1, 1), "Transform defaults");
var renamed = Parse(head + "// @param transform2D _OtherArea");
metadata.GetMethod("PreserveValues", Hidden).Invoke(null, new object[] { renamed, new List<DCFApixels.WhimTex.ShaderFXParameter> { parameters[7] } });
Check(renamed[0].id == parameters[7].id, "Rename in place retains ID");
Reject("\n" + head); Reject("// License\n" + head); Reject(" " + head);
foreach (string invalid in new[] { "float _A = 1;", "float _A = NaN", "float _A = 1 [2 .. 0]", "float _A = 4 [0 .. 1]", "float _A = 1 [..]", "float4 _A = (1,2,3)", "float4 _A = (1,2,3,4) [0 .. 1]", "texture2D _A = 1", "transform2D _A = 1", "int _A = 1" })
    Reject(head + "// @param " + invalid);
var repeated = Parse(head + "// @param float _A = 1\n// @param float _A = 2");
Check(repeated.Count == 1 && repeated[0].controls.Count == 2 && repeated[0].floatValue == 2f, "Last explicit default wins for repeated declarations");
Check(Parse(head + "/*\n// @param float _Ignored = 1\n*/").Count == 0, "Ignore declarations inside block comments");
var toolType = typeof(DCFApixels.WhimTex.TextureCompositorWindow).GetNestedType("PreviewTransformManipulator", Hidden);
var hitTest = toolType.GetMethod("HitTest", Hidden);
var frame = DCFApixels.WhimTex.TextureTransform.Default;
int pivot = (int)hitTest.Invoke(null, new object[] { new Vector2(50, 25), frame, new Rect(0, 0, 100, 50), new Vector2(100, 50), true });
int move = (int)hitTest.Invoke(null, new object[] { new Vector2(50, 25), frame, new Rect(0, 0, 100, 50), new Vector2(100, 50), false });
Check(pivot == 10 && move == 8, "FX frame center moves instead of editing pivot");

var rowsMethod = typeof(DCFApixels.WhimTex.ShaderFXTransform).GetMethod("GetRows", Hidden);
var random = new System.Random(71);
for (int n = 0; n < 400; n++)
{
    float Next(float low, float high) => low + (float)random.NextDouble() * (high - low);
    var transform = new DCFApixels.WhimTex.ShaderFXTransform { position = new Vector2(Next(-1, 2), Next(-1, 2)), size = new Vector2(Next(.1f, 3), Next(.1f, 3)), rotation = Next(-360, 360) };
    if ((n & 1) != 0) transform.size.x *= -1;
    var args = new object[] { new Vector2(Next(8, 2000), Next(8, 2000)), null, null, null, null, null, null };
    rowsMethod.Invoke(transform, args);
    var uv = new Vector2(Next(-1, 2), Next(-1, 2));
    Vector2 Map(Vector2 v, Vector4 a, Vector4 b) => new Vector2(a.x*v.x+a.y*v.y+a.z, b.x*v.x+b.y*v.y+b.z);
    Vector2 local = Map(uv, (Vector4)args[1], (Vector4)args[2]);
    Vector2 restored = Map(local, (Vector4)args[4], (Vector4)args[5]);
    Check((restored - uv).magnitude < .002f, "Rectangular transform round trip " + n);
}

var document = ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = HideFlags.HideAndDontSave; document.width = 16; document.height = 8;
DCFApixels.WhimTex.ShaderFX fx = null, clone = null;
var output = RenderTexture.GetTemporary(16, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
var readback = new Texture2D(16, 8, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
RenderTexture previous = RenderTexture.active;
try
{
    string source = head + "// @param float _Amount = 0.25 [0 .. 1]\n// @param transform2D _Area\nfloat4 ApplyFX(float2 uv, float4 color) { float2 p = _Area_ToLocal(uv); return float4(p, _Amount, 1); }";
    fx = (DCFApixels.WhimTex.ShaderFX)typeof(DCFApixels.WhimTex.ShaderFX).GetMethod("CreateAgentDraft", Hidden).Invoke(null, new object[] { document, source, new List<DCFApixels.WhimTex.ShaderFXParameter>() });
    typeof(DCFApixels.WhimTex.ShaderFX).GetMethod("ApplyAgentDraft", Hidden).Invoke(fx, null);
    var list = (List<DCFApixels.WhimTex.ShaderFXParameter>)typeof(DCFApixels.WhimTex.ShaderFX).GetField("parameters", Hidden).GetValue(fx);
    Check(list.Count == 2, "Inline metadata parsed by actual Apply");
    var shaderField = typeof(DCFApixels.WhimTex.ShaderFX).GetField("compiledShader", Hidden);
    object shader = shaderField.GetValue(fx);
    string id = list[1].id;
    string built = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder").GetMethod("Build", Hidden).Invoke(null, new object[] { fx, "Assets/Test.hlsl" });
    Check(built.Contains("_WhimTex_Area_" + id + "_ToLocalRow0"), "Readable, stable generated names");
    var getMaterial = typeof(DCFApixels.WhimTex.ShaderFX).GetMethod("GetMaterial", Hidden);
    object context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 16, 8, 1f, true, true, null);
    Color Render()
    {
        var material = (Material)getMaterial.Invoke(fx, new[] { context });
        Graphics.Blit(Texture2D.whiteTexture, output, material);
        RenderTexture.active = output;
        readback.ReadPixels(new Rect(0, 0, 16, 8), 0, 0); readback.Apply();
        return readback.GetPixel(8, 4);
    }
    Color first = Render();
    Check(Mathf.Abs(first.b - .25f) < .001f && Mathf.Abs(first.a - 1) < .001f, "GPU values");
    list[0].floatValue = .75f;
    list[1].transformValue.position += new DCFApixels.WhimTex.Double2(.125, 0);
    Color second = Render();
    Check(Mathf.Abs(second.b - .75f) < .001f && Mathf.Abs(second.r - (first.r - .125f)) < .001f, "GPU live transform and float");
    Check(ReferenceEquals(shader, shaderField.GetValue(fx)), "Value edits reuse compiled shader");
    Check(id == list[1].id, "Stable ID after value edits");
    clone = (DCFApixels.WhimTex.ShaderFX)typeof(DCFApixels.WhimTex.ShaderFX).GetMethod("CloneForDocument", Hidden).Invoke(fx, new object[] { document });
    var cloneList = (List<DCFApixels.WhimTex.ShaderFXParameter>)typeof(DCFApixels.WhimTex.ShaderFX).GetField("parameters", Hidden).GetValue(clone);
    cloneList[0].floatValue = .5f;
    Check(list[0].floatValue == .75f, "Independent instance values");
    var editor = Editor.CreateEditor(fx);
    try
    {
        var view = editor.CreateInspectorGUI();
        Check(view != null, "Metadata inspector builds");
    }
    finally { UnityEngine.Object.DestroyImmediate(editor); }
    var catalogType = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
    var entries = (System.Collections.IEnumerable)catalogType.GetMethod("GetEntries", Hidden).Invoke(null, null);
    int builtIn = 0;
    foreach (object entry in entries)
    {
        string path = (string)entry.GetType().GetField("path", Hidden).GetValue(entry);
        if (!path.StartsWith("Packages/com.dcfapixels.whimtex/src/FXPresets/")) continue;
        string error = (string)entry.GetType().GetField("error", Hidden).GetValue(entry);
        // Entries are serialized, so a healthy entry holds an empty string rather than null.
        Check(string.IsNullOrEmpty(error), "Built-in catalog metadata: " + error);
        var instance = (DCFApixels.WhimTex.ShaderFX)typeof(DCFApixels.WhimTex.ShaderFX).GetMethod("FromCatalog", Hidden).Invoke(null, new object[] { document, entry });
        try { Check(shaderField.GetValue(instance) != null, "Catalog creates applied independent instance"); }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        builtIn++;
    }
    Check(builtIn == System.IO.Directory.GetFiles("Packages/com.dcfapixels.whimtex/src/FXPresets", "*.hlsl").Length,
        "Auto-discovered every package HLSL preset: " + builtIn);
    string json = EditorJsonUtility.ToJson(fx);
    var cloneShader = (UnityEngine.Object)shaderField.GetValue(clone);
    shaderField.SetValue(clone, null);
    if (cloneShader != null) UnityEngine.Object.DestroyImmediate(cloneShader);
    EditorJsonUtility.FromJsonOverwrite(json, clone);
    cloneList = (List<DCFApixels.WhimTex.ShaderFXParameter>)typeof(DCFApixels.WhimTex.ShaderFX).GetField("parameters", Hidden).GetValue(clone);
    Check(cloneList[1].id == id && cloneList[1].transformValue.position == list[1].transformValue.position, "Serialization preserves transform and ID");
    // Do not leave two transient objects owning the same shader after the serialization check.
    shaderField.SetValue(clone, null);
    return "Shader FX metadata/transform/GPU checks passed: " + checks;
}
finally
{
    RenderTexture.active = previous;
    if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
    if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
    UnityEngine.Object.DestroyImmediate(document);
    UnityEngine.Object.DestroyImmediate(readback);
    RenderTexture.ReleaseTemporary(output);
}
