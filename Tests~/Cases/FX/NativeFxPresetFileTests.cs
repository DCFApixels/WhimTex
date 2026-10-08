// run_script entry NativeFxPresetFileTests.Run. Own unique current FX preset and cached shader only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class NativeFxPresetFileTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static object Get(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    static void Check(bool value, string name) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, name); }
    static string ExecuteRun()
    {
        string folder = WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder();
        // WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() created this unique owned folder.
        string path = folder + "/Preset.asset";
        ShaderFX fx = null;
        Shader shader = null;
        ShaderFX clone = null;
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.width = doc.height = 8;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.white }));
        try
        {
            fx = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<ShaderFX>());
            string code = "// @param float _Gain = 0.8 [0.8 .. 1]\nfloat4 ApplyFX(float2 uv,float4 color){ return color * _Gain; }";
            var parsed = (List<ShaderFXParameter>)typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true)
                .GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
            var p = parsed[0]; p.floatValue = .625f;
            string id = p.id;
            Set(fx, "code", code); Set(fx, "parameters", new List<ShaderFXParameter> { p });
            string source = (string)typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXSourceBuilder")
                .GetMethod("Build", F).Invoke(null, new object[] { fx, path });
            shader = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ShaderUtil.CreateShaderAsset(source, true));
            Check(shader != null, "Cached shader created");
            Set(fx, "compiledShader", shader); Set(fx, "appliedCode", code); Set(fx, "appliedSource", source);
            Set(fx, "appliedParameters", new List<ShaderFXParameter> { p });
            AssetDatabase.CreateAsset(fx, path); AssetDatabase.AddObjectToAsset(shader, fx);
            EditorUtility.SetDirty(fx); AssetDatabase.SaveAssetIfDirty(fx);
            byte[] original = File.ReadAllBytes(path);
            // Test real native deserialization of declared parameters, not a synthetic file adapter.
            Resources.UnloadAsset(fx); fx = null;
            fx = AssetDatabase.LoadAssetAtPath<ShaderFX>(path);
            Check((string)Get(fx, "code") == code, "Native read keeps current source unchanged");
            var next = (List<ShaderFXParameter>)Get(fx, "parameters");
            var applied = (List<ShaderFXParameter>)Get(fx, "appliedParameters");
            Check(next.Count == 1 && next[0].id == id && next[0].controls.Count == 1, "Draft identity and declaration");
            Check(applied.Count == 1 && applied[0].id == id && applied[0].controls.Count == 1, "Applied declaration retained");
            Check(next[0].floatValue == .625f && applied[0].floatValue == .625f, "Read does not rewrite stored values");
            Check(!(bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Clean cached preset remains applied");
            clone = (ShaderFX)typeof(ShaderFX).GetMethod("CloneForDocument", F).Invoke(fx, new object[] { doc });
            doc.layers[0].fx.Add(clone);
            var image = doc.ComposeCanvas();
            try { Check(Math.Abs(image.GetPixel(4, 4).r - .625f) < .002f, "Cached native preset renders its current stored value without a migration clamp"); }
            finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image); }
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "Read does not rewrite preset file");
            Check(!EditorUtility.IsDirty(fx), "Read does not dirty preset");
            // An unapplied source edit must not resurrect a deliberately removed declaration.
            string edited = "float4 ApplyFX(float2 uv,float4 color){ return color; }";
            Set(fx, "code", edited);
            Check(typeof(ShaderFX).GetMethod("NormalizeFileParameters", F) == null, "No manual-parameter file adapter");
            Check((string)Get(fx, "code") == edited, "Unapplied source remains unchanged");
            Check((bool)typeof(ShaderFX).GetProperty("HasPendingChanges", F).GetValue(fx), "Unapplied source edit stays pending");
            Check(((List<ShaderFXParameter>)Get(fx, "appliedParameters")).Count == 1, "Previous applied snapshot stays intact");
            return "Current native FX preset: draft/applied identity, cached render, hard range, clean state, unchanged file and pending source.";
        }
        finally
        {
            doc.layers[0].fx.Clear();
            if (clone != null) WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(clone);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
            Check(folder == WhimTex.Tests.UnityC.FixtureContext.Scope.Assets && Path.GetFileName(folder).Length == "UnityC-".Length + 32, "Owned folder");
            WhimTex.Tests.UnityC.FixtureContext.Scope.DeleteAsset(folder);
        }
    }

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NativeFxPresetFileTests.Run", () => { ExecuteRun(); });
}
