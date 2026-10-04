// run_script entry NativeManualFxFileSmoke.Run. Own unique FX asset and cached shader only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class NativeManualFxFileSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static object Get(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    public static string Run()
    {
        string folder = "Assets/WhimTexNativeManual_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        string path = folder + "/Preset.asset";
        ShaderFX fx = null;
        Shader shader = null;
        ShaderFX clone = null;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = doc.height = 8;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.white }));
        try
        {
            fx = ScriptableObject.CreateInstance<ShaderFX>();
            string code = "float4 ApplyFX(float2 uv,float4 color){ return color * _Gain; }";
            var p = new ShaderFXParameter { name = "_Gain", type = ShaderFXParameterType.Float, floatValue = .625f,
                hasMinimum = true, hasMaximum = true, minimum = .8f, maximum = 1 };
            string id = p.id;
            Set(fx, "code", code); Set(fx, "parameters", new List<ShaderFXParameter> { p });
            string source = (string)typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder")
                .GetMethod("Build", F).Invoke(null, new object[] { fx, path });
            shader = ShaderUtil.CreateShaderAsset(source, true);
            Check(shader != null, "Cached shader created");
            Set(fx, "compiledShader", shader); Set(fx, "appliedCode", code); Set(fx, "appliedSource", source);
            Set(fx, "appliedParameters", new List<ShaderFXParameter> { p });
            AssetDatabase.CreateAsset(fx, path); AssetDatabase.AddObjectToAsset(shader, fx);
            EditorUtility.SetDirty(fx); AssetDatabase.SaveAssetIfDirty(fx);
            byte[] original = File.ReadAllBytes(path);
            // Test real native deserialization, not just an explicit call to the adapter.
            Resources.UnloadAsset(fx); fx = null;
            fx = AssetDatabase.LoadAssetAtPath<ShaderFX>(path);
            Check(((string)Get(fx, "code")).Contains("// @param float _Gain"), "Native read normalizes before first use");
            var next = (List<ShaderFXParameter>)Get(fx, "parameters");
            var applied = (List<ShaderFXParameter>)Get(fx, "appliedParameters");
            Check(next.Count == 1 && next[0].id == id && next[0].controls.Count == 1, "Draft identity and declaration");
            Check(applied.Count == 1 && applied[0].id == id && applied[0].controls.Count == 1, "Applied snapshot normalized");
            Check(next[0].floatValue == .8f && applied[0].floatValue == .8f, "Effective saved hard-clamped value retained");
            Check(!(bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Clean cached preset remains applied");
            clone = (ShaderFX)typeof(ShaderFX).GetMethod("CloneForDocument", F).Invoke(fx, new object[] { doc });
            doc.layers[0].modifiers.Add(clone);
            var image = doc.ComposeCanvas();
            try { Check(Math.Abs(image.GetPixel(4, 4).r - .8f) < .002f, "Cached native preset renders its original effective value"); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "Read does not rewrite preset file");
            Check(!EditorUtility.IsDirty(fx), "Read does not dirty preset");
            // An unapplied source edit must not resurrect a deliberately removed declaration.
            string edited = "float4 ApplyFX(float2 uv,float4 color){ return color; }";
            Set(fx, "code", edited);
            typeof(ShaderFX).GetMethod("NormalizeFileParameters", F).Invoke(fx, null);
            Check((string)Get(fx, "code") == edited, "Removed code declaration is not resurrected by file normalization");
            Check((bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Unapplied source edit stays pending");
            Check(((List<ShaderFXParameter>)Get(fx, "appliedParameters")).Count == 1, "Previous applied snapshot stays intact");
            return "PASS: 12 native manual FX checks; real asset read, draft/applied identity, cached render, hard range, clean state, unchanged file and unapplied declaration removal.";
        }
        finally
        {
            doc.layers[0].modifiers.Clear();
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(doc);
            Check(folder.StartsWith("Assets/WhimTexNativeManual_", StringComparison.Ordinal) && Path.GetFileName(folder).Length == "WhimTexNativeManual_".Length + 32, "Owned folder");
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
