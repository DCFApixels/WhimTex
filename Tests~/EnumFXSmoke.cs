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
        System.Reflection.MethodInfo Method(Type type, string name, int parameterCount)
        {
            foreach (var method in type.GetMethods(flags))
                if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
            throw new MissingMethodException(type.FullName, name);
        }
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        string declarations = "// @param float _Strength = 0.63 [0 .. 1] // Точная сила эффекта\n// @param enum _Strength { Low: 0.2, Medium: 0.5, High: 2 } // Quick values // keep this\n";
        declarations = "// @header(Strength settings)\n" + declarations.Replace("// @param enum", "// @ header(Quick choices)\n// @param enum");
        declarations = declarations.Replace("// @param float _Strength", "// @helpbox(Adjust strength before choosing a preset.)\n// @param float _Strength");
        var p = Parse(declarations);
        Check(p[0].controls[0].headers[0] == "Strength settings" && p[0].controls[1].headers[0] == "Quick choices", "Headers on linked controls");
        Check(p[0].controls[0].helpBoxes[0] == "Adjust strength before choosing a preset.", "HelpBox metadata parse");
        Check(Parse("/*\n// @header(Ignored)\n*/\n// @param float _X")[0].controls[0].headers.Length == 0, "Block-comment header");
        Check(Parse("// @header(One)\n// @header(Two)\n// @param float _X")[0].controls[0].headers.Length == 2, "Consecutive headers");
        foreach (var invalidHeader in new[] { "// @header()", "// @header(   )", "// @header Missing" })
        {
            bool rejected = false;
            try { Parse(invalidHeader); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Invalid header accepted");
        }
        foreach (var invalidHelpBox in new[] { "// @helpbox()", "// @helpbox(   )", "// @helpbox Missing" })
        {
            bool rejected = false;
            try { Parse(invalidHelpBox); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Invalid helpbox accepted");
        }
        Check(p.Count == 1 && p[0].controls.Count == 2 && p[0].floatValue == .63f, "Shared optional default");
        Check(p[0].controls[0].tooltip == "Точная сила эффекта" && p[0].controls[1].tooltip == "Quick values // keep this", "Per-control tooltip parsing");
        foreach(string declaration in new[]{ "bool _B", "float4 _V", "color _C", "texture2D _T", "transform2D _Area" })
            Check(Parse("// @param " + declaration + " // Help [0 .. 1]; {text}")[0].controls[0].tooltip == "Help [0 .. 1]; {text}", "Tooltip punctuation/type");
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
            fx = (ShaderFX)Method(typeof(ShaderFX), "CreateAgentDraft", 3).Invoke(null, new object[] { document, declarations + "float4 ApplyFX(float2 uv, float4 color) { return _Strength; }", new List<ShaderFXParameter>() });
            Method(typeof(ShaderFX), "ApplyAgentDraft", 0).Invoke(fx, null);
            var list = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var viewType = assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            var view = (VisualElement)Activator.CreateInstance(viewType, flags, null, new object[] { fx }, null);
            var dropdown = view.Q<DropdownField>();
            Check(view[0] is Label title && title.text == "Strength settings" && view[1] is HelpBox note && note.text == "Adjust strength before choosing a preset." && view[3] is Label second && second.text == "Quick choices", "UI metadata order");
            Check(view.Q<Slider>() != null && dropdown != null && dropdown.value.StartsWith("Custom"), "Linked UI controls");
            Check(view.Q<Slider>().tooltip == "Точная сила эффекта" && dropdown.tooltip == "Quick values // keep this", "UI tooltip binding");
            var shader = typeof(ShaderFX).GetField("compiledShader", flags).GetValue(fx);
            viewType.GetMethod("Change", flags).Invoke(view, new object[] { list[0].id, (Action<ShaderFXParameter>)(v => v.floatValue = 2) });
            Check(dropdown.value == "High" && list[0].floatValue == 2, "Enum outside slider range");
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 4, 4, 1f, true, true, null);
            var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
            Check(material.GetFloat("_Strength") == 2, "Shader value clamped by another control");
            Check(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", flags).GetValue(fx)), "Unexpected recompile");
            list[0].floatValue = .5f;
            string saved = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", flags).Invoke(null, new object[] { fx, "Test/Enum" });
            var roundtrip = Parse(saved);
            Check(roundtrip[0].controls[0].headers[0] == "Strength settings" && roundtrip[0].controls[0].helpBoxes[0] == "Adjust strength before choosing a preset." && roundtrip[0].controls[1].headers[0] == "Quick choices", "Header/helpbox export roundtrip");
            var copied = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", flags).Invoke(list[0], null);
            Check(copied.controls[0].headers[0] == "Strength settings" && !ReferenceEquals(copied.controls[0].headers, list[0].controls[0].headers), "Independent header copy");
            Check(copied.controls[0].helpBoxes[0] == "Adjust strength before choosing a preset." && !ReferenceEquals(copied.controls[0].helpBoxes, list[0].controls[0].helpBoxes), "Independent helpbox copy");
            Check(roundtrip.Count == 1 && roundtrip[0].floatValue == .5f && roundtrip[0].controls.Count == 2, "Preset roundtrip");
            Check(roundtrip[0].controls[0].tooltip == p[0].controls[0].tooltip && roundtrip[0].controls[1].tooltip == p[0].controls[1].tooltip, "Tooltip export roundtrip");
            Check(saved.Contains("enum _Strength {"), "Duplicate exported default");
            var copy = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(list[0]));
            Check(copy.controls.Count == 2 && copy.controls[1].optionValues[2] == 2, "Serialization");
            return "PASS: optional defaults, precedence, enum validation, linked controls, out-of-range values, shader compilation/reuse and preset/serialization roundtrip.";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        finally { if (fx != null) UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(document); }
    }
}
