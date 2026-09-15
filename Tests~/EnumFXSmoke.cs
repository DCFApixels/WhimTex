using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class EnumFXSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", flags);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[] { source, false, null });
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        string declarations = "// @param float _Strength = 0.63 [0 .. 1]\n// @param enum _Strength { Low: 0.2, Medium: 0.5, High: 2 }\n";
        var p = Parse(declarations);
        Check(p.Count == 1 && p[0].controls.Count == 2 && p[0].floatValue == .63f, "Shared optional default");
        Check(Parse(declarations + "// @param bool _Strength = true")[0].floatValue == 1, "Last explicit default");
        Check(Parse("// @param enum _Mode = SoftLight { SoftLight: 0.25, HardLight: 1 }")[0].floatValue == .25f, "Named default");
        Check(Parse("// @param enum _Mode = 0.7 { SoftLight: 0.25, HardLight: 1 }")[0].floatValue == .7f, "Custom default");
        Check(Parse("// @param float _X [1 .. 2]\n// @param color _C\n// @param float4 _V\n// @param bool _B")[0].floatValue == 0, "Zero default");
        foreach (var invalid in new[] { "// @param enum _X { A, B }", "// @param enum _X { A: 0, A: 1 }", "// @param enum _X { A: 0, B: 0 }", "// @param float _X\n// @param float4 _X" })
        {
            bool rejected = false;
            try { Parse(invalid); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Accepted invalid declaration: " + invalid);
        }
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null, new object[] { document, declarations + "float4 ApplyFX(float2 uv, float4 color) { return _Strength; }", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var list = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { fx }, null);
            var dropdown = view.Q<DropdownField>();
            Check(view.Q<Slider>() != null && dropdown != null && dropdown.value.StartsWith("Custom"), "Linked UI controls");
            var shader = typeof(ShaderFX).GetField("compiledShader", flags).GetValue(fx);
            viewType.GetMethod("Change", flags).Invoke(view, new object[] { list[0].id, (Action<ShaderFXParameter>)(v => v.floatValue = 2) });
            Check(dropdown.value == "High" && list[0].floatValue == 2, "Enum outside slider range");
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 4, 4, 1f, true, true);
            var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
            Check(material.GetFloat("_Strength") == 2, "Shader value clamped by another control");
            Check(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", flags).GetValue(fx)), "Unexpected recompile");
            string saved = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", flags).Invoke(null, new object[] { fx, "Test/Enum" });
            var roundtrip = Parse(saved);
            Check(roundtrip.Count == 1 && roundtrip[0].floatValue == 2 && roundtrip[0].controls.Count == 2, "Preset roundtrip");
            Check(saved.Contains("enum _Strength {"), "Duplicate exported default");
            var copy = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(list[0]));
            Check(copy.controls.Count == 2 && copy.controls[1].optionValues[2] == 2, "Serialization");
            return "PASS: optional defaults, precedence, enum validation, linked controls, out-of-range values, shader compilation/reuse and preset/serialization roundtrip.";
        }
        finally { if (fx != null) UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(document); }
    }
}
