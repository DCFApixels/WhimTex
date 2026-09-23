using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXConditionalParametersSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", F);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[] { source, false, null });
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }

        const string declarations = "// @param enum _Mode = 0 { Off: 0, On: 1 }\n// @if _Mode == 1\n// @header(Enabled only)\n// @param float _Amount = 0.5 [0 .. 1]\n// @endif\n// @if _Mode != 0\n// @param bool _Extra = true\n// @endif\n";
        var values = Parse(declarations);
        Check(values.Count == 3 && values[1].controls[0].visibleIfParameter == "_Mode" && values[1].controls[0].visibleIfValue == 1, "Conditional metadata parse");
        Check(values[2].controls[0].visibleIfNotEqual && values[2].controls[0].visibleIfValue == 0, "Not-equal condition parse");
        Check(Parse("/*\n// @if _Mode == 1\n*/\n// @param float _Mode\n// @param float _Hidden")[1].controls[0].visibleIfParameter == null, "Block comments ignore directives");
        foreach (string invalid in new[] {
            "// @if _Mode == 1\n// @param float _X",
            "// @endif",
            "// @if _Mode > 1\n// @endif",
            "// @if _Missing == 1\n// @param float _X\n// @endif",
            "// @param float2 _Vector\n// @if _Vector == 1\n// @param float _X\n// @endif",
            "// @if _Mode == 1\n// @if _Mode == 0\n// @endif\n// @endif",
            "// @param float _Mode\n// @if _Mode == 1\n// @param float _Mode\n// @endif"
        })
        {
            bool rejected = false;
            try { Parse(invalid); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Accepted invalid conditional declarations: " + invalid);
        }

        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        try
        {
            string source = declarations + "float4 ApplyFX(float2 uv, float4 color) { return color * (_Mode == 1 ? _Amount : 1); }\n";
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { document, source, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var ui = (VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"), F, null, new object[] { fx }, null);
            var conditionalRows = ui.Query<VisualElement>(className: "whimtex-fx-conditional-parameter").ToList();
            Check(conditionalRows.Count == 2 && conditionalRows[0].style.display == DisplayStyle.None, "Conditional UI initial visibility");
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            Check(parameters[0].floatValue == 1f && conditionalRows[0].style.display == DisplayStyle.Flex && conditionalRows[1].style.display == DisplayStyle.Flex,
                "Conditional UI refresh on driver edit: mode=" + parameters[0].floatValue + ", rows=" + conditionalRows[0].style.display + "," + conditionalRows[1].style.display);
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[1].id, (Action<ShaderFXParameter>)(p => p.floatValue = .73f) });
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 0f) });
            Check(conditionalRows[0].style.display == DisplayStyle.None && parameters[1].floatValue == .73f,
                "Hidden control value is retained when its condition becomes false");
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            string preset = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { fx, "Test/Conditional" });
            Check(preset.Contains("// @if _Mode == 1") && preset.Contains("// @if _Mode != 0") && preset.Contains("// @endif"), "Preset writer preserves condition blocks");
            var restored = Parse(preset);
            Check(restored.Count == 3 && restored[1].controls[0].visibleIfParameter == "_Mode" && restored[2].controls[0].visibleIfNotEqual, "Preset conditional roundtrip");

            foreach (string presetName in new[] { "Pixelate", "Posterize" })
            {
                string presetCode = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + presetName + ".hlsl");
                var presetParameters = Parse(presetCode);
                ShaderFXParameter Parameter(string name) => presetParameters.Find(p => p.name == name);
                ShaderFXParameterControl Control(string name) => Parameter(name).controls[0];
                Check(Control("_Amount").visibleIfParameter == "_Dither" && Control("_Amount").visibleIfNotEqual,
                    presetName + " shows dither strength only when dithering is enabled");
                if (presetName == "Pixelate")
                {
                    Check(Control("_Levels").visibleIfParameter == "_OneBit" && Control("_Levels").visibleIfNotEqual &&
                        Control("_Gamma").visibleIfParameter == "_OneBit" && Control("_Gamma").visibleIfNotEqual,
                        "Pixelate hides channel quantization controls in one-bit mode");
                    Check(Control("_LowColor").visibleIfParameter == "_OneBit" && !Control("_LowColor").visibleIfNotEqual &&
                        Control("_HighColor").visibleIfParameter == "_OneBit" && !Control("_HighColor").visibleIfNotEqual,
                        "Pixelate shows one-bit colors only in one-bit mode");
                }

                ShaderFX presetFX = null;
                try
                {
                    presetFX = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F).Invoke(null, new object[] { document, presetCode, new List<ShaderFXParameter>() });
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(presetFX, null);
                    Check(typeof(ShaderFX).GetField("compiledShader", F).GetValue(presetFX) != null, presetName + " preset compile");
                    if (presetName == "Pixelate")
                    {
                        string diagnostics = (string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(presetFX);
                        Check(diagnostics.IndexOf("uninitialized variable (SampleBlock)", StringComparison.OrdinalIgnoreCase) < 0,
                            "Pixelate has no potentially-uninitialized SampleBlock warning: " + diagnostics);
                        var actualParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(presetFX);
                        var pixelateView = (VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"), F, null, new object[] { presetFX }, null);
                        var pixelRows = pixelateView.Query<VisualElement>(className: "whimtex-fx-conditional-parameter").ToList();
                        ShaderFXParameter oneBit = actualParameters.Find(p => p.name == "_OneBit");
                        pixelateView.GetType().GetMethod("Change", F).Invoke(pixelateView, new object[] { oneBit.id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
                        Check(pixelRows.Count == 5 && pixelRows[1].style.display == DisplayStyle.None &&
                            pixelRows[2].style.display == DisplayStyle.None && pixelRows[3].style.display == DisplayStyle.Flex &&
                            pixelRows[4].style.display == DisplayStyle.Flex, "Pixelate toggles both colors together with the bool driver");
                        var lowColor = pixelRows[3].Q<ColorField>();
                        var highColor = pixelRows[4].Q<ColorField>();
                        Check(lowColor != null && highColor != null && lowColor.label == "Low Color" && highColor.label == "High Color",
                            "Both conditional Pixelate rows contain separately labeled color controls");
                        Check(lowColor.value != highColor.value,
                            "Pixelate low/high color controls retain their distinct black/white values");
                    }
                }
                finally { if (presetFX != null) UnityEngine.Object.DestroyImmediate(presetFX); }
            }

            return "PASS: " + checks + " parser validation, conditional UI, scalar-driver edits and preset roundtrip checks.";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        finally
        {
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
