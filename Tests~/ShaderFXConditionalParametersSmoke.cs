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
        var createDraft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
        var parse = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", F);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[] { source, false, null });
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }

        const string declarations = "// @param enum _Mode = 0 { Off: 0, On: 1 }\n// @if _Mode == 1\n// @header(Enabled only)\n// @helpbox(Only shown while this mode is active.)\n// @param float _Amount = 0.5 [0 .. 1]\n// @endif\n// @if _Mode != 0\n// @param bool _Extra = true\n// @endif\n";
        var values = Parse(declarations);
        Check(values.Count == 3 && values[1].controls[0].visibleIfParameter == "_Mode" && values[1].controls[0].visibleIfValue == 1, "Conditional metadata parse");
        Check(values[1].controls[0].helpBoxes.Length == 1 && values[1].controls[0].helpBoxes[0] == "Only shown while this mode is active.", "Conditional helpbox metadata parse");
        Check(values[2].controls[0].visibleIfNotEqual && values[2].controls[0].visibleIfValue == 0, "Not-equal condition parse");
        Check(Parse("/*\n// @if _Mode == 1\n*/\n// @param float _Mode\n// @param float _Hidden")[1].controls[0].visibleIfParameter == null, "Block comments ignore directives");

        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        var preserveValues = metadata.GetMethod("PreserveValues", F);
        string ScalarDeclaration(ShaderFXParameterType type, string value) => type == ShaderFXParameterType.Enum
            ? "// @param enum _Value = " + (value == "1" ? "On" : "Off") + " { Off: 0, On: 1 }\n"
            : "// @param " + (type == ShaderFXParameterType.Bool ? "bool" : "float") + " _Value = " + value + "\n";
        foreach (var transition in new[] {
            (ShaderFXParameterType.Float, "1", ShaderFXParameterType.Bool),
            (ShaderFXParameterType.Bool, "true", ShaderFXParameterType.Float),
            (ShaderFXParameterType.Float, "1", ShaderFXParameterType.Enum),
            (ShaderFXParameterType.Enum, "1", ShaderFXParameterType.Float),
            (ShaderFXParameterType.Bool, "true", ShaderFXParameterType.Enum),
            (ShaderFXParameterType.Enum, "1", ShaderFXParameterType.Bool)
        })
        {
            var previous = Parse(ScalarDeclaration(transition.Item1, transition.Item2));
            var next = Parse(ScalarDeclaration(transition.Item3, "1"));
            preserveValues.Invoke(null, new object[] { next, previous });
            Check(next[0].floatValue == 1f && next[0].id == previous[0].id,
                "Scalar value and identity migrate from " + transition.Item1 + " to " + transition.Item3);
        }

        var fractionalScalar = Parse(ScalarDeclaration(ShaderFXParameterType.Float, "0.375"));
        var fractionalBool = Parse(ScalarDeclaration(ShaderFXParameterType.Bool, "false"));
        preserveValues.Invoke(null, new object[] { fractionalBool, fractionalScalar });
        Check(Mathf.Approximately(fractionalBool[0].floatValue, .375f) && fractionalBool[0].id == fractionalScalar[0].id,
            "Scalar migration preserves a fractional numeric value when the destination UI type is bool");

        ShaderFX serializedSource = null, serializedCopy = null;
        try
        {
            string boolSource = "// @param bool _Mode = true\nfloat4 ApplyFX(float2 uv, float4 color) { return color * _Mode; }\n";
            string enumSource = "// @param enum _Mode = On { Off: 0, On: 1 }\nfloat4 ApplyFX(float2 uv, float4 color) { return color * _Mode; }\n";
            serializedSource = (ShaderFX)createDraft.Invoke(null, new object[] { null, boolSource, Parse(boolSource) });
            string serialized = UnityEditor.EditorJsonUtility.ToJson(serializedSource);
            serializedCopy = (ShaderFX)createDraft.Invoke(null, new object[] { null, string.Empty, new List<ShaderFXParameter>() });
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(serialized, serializedCopy);
            typeof(ShaderFX).GetField("code", F).SetValue(serializedCopy, enumSource);
            typeof(ShaderFX).GetMethod("PrepareParameterDeclarations", F).Invoke(serializedCopy, null);
            var migratedParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(serializedCopy);
            var serializedMode = migratedParameters.Find(p => p.name == "_Mode");
            var originalParameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(serializedSource);
            Check(serializedMode != null && serializedMode.floatValue == 1f && serializedMode.id == originalParameters[0].id &&
                serializedMode.controls[0].type == ShaderFXParameterType.Enum,
                "Serialized bool parameter reloads and migrates to enum while preserving its value and ID");
            string presetRoundtrip = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter")
                .GetMethod("BuildSource", F).Invoke(null, new object[] { serializedCopy, "Test/ScalarMigration" });
            var writtenMode = Parse(presetRoundtrip).Find(p => p.name == "_Mode");
            Check(writtenMode != null && writtenMode.floatValue == 1f && writtenMode.controls[0].type == ShaderFXParameterType.Enum,
                "Preset serialization and reparsing retain the migrated enum value");
        }
        finally
        {
            if (serializedCopy != null) UnityEngine.Object.DestroyImmediate(serializedCopy);
            if (serializedSource != null) UnityEngine.Object.DestroyImmediate(serializedSource);
        }

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
            fx = (ShaderFX)createDraft.Invoke(null, new object[] { document, source, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var ui = (VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"), F, null, new object[] { fx }, null);
            var conditionalRows = ui.Query<VisualElement>(className: "whimtex-fx-conditional-parameter").ToList();
            Check(conditionalRows.Count == 2 && conditionalRows[0].style.display == DisplayStyle.None, "Conditional UI initial visibility");
            Check(conditionalRows[0].Q<HelpBox>()?.text == "Only shown while this mode is active.", "HelpBox belongs to its conditional row");
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            Check(parameters[0].floatValue == 1f && conditionalRows[0].style.display == DisplayStyle.Flex && conditionalRows[1].style.display == DisplayStyle.Flex,
                "Conditional UI refresh on driver edit: mode=" + parameters[0].floatValue + ", rows=" + conditionalRows[0].style.display + "," + conditionalRows[1].style.display);
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[1].id, (Action<ShaderFXParameter>)(p => p.floatValue = .73f) });
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 0f) });
            Check(conditionalRows[0].style.display == DisplayStyle.None && parameters[1].floatValue == .73f,
                "Hidden control value is retained when its condition becomes false");
            ui.GetType().GetMethod("Change", F).Invoke(ui, new object[] { parameters[0].id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            string preset = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", F).Invoke(null, new object[] { fx, "Test/Conditional" });
            Check(preset.Contains("// @if _Mode == 1") && preset.Contains("// @if _Mode != 0") && preset.Contains("// @helpbox(Only shown while this mode is active.)") && preset.Contains("// @endif"), "Preset writer preserves conditions and helpbox metadata");
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
                    var colorMode = Control("_OneBit");
                    Check(colorMode.type == ShaderFXParameterType.Enum && colorMode.hidden && colorMode.groupTitle == "Color" &&
                        colorMode.groupHeaderParameter == "_OneBit" && colorMode.optionNames.Length == 2 &&
                        colorMode.optionNames[0] == "Quantization" && colorMode.optionNames[1] == "OneBit" &&
                        colorMode.visibleIfParameter == null,
                        "Pixelate exposes the existing _OneBit parameter as a two-choice group-header selector");
                    Check(Control("_Levels").visibleIfParameter == "_OneBit" && !Control("_Levels").visibleIfNotEqual &&
                        Control("_Levels").visibleIfValue == 0 && Control("_Gamma").visibleIfParameter == "_OneBit" &&
                        !Control("_Gamma").visibleIfNotEqual && Control("_Gamma").visibleIfValue == 0,
                        "Pixelate shows color quantization controls only in Quantization mode");
                    Check(Control("_LowColor").visibleIfParameter == "_OneBit" && !Control("_LowColor").visibleIfNotEqual &&
                        Control("_LowColor").visibleIfValue == 1 && Control("_HighColor").visibleIfParameter == "_OneBit" &&
                        !Control("_HighColor").visibleIfNotEqual && Control("_HighColor").visibleIfValue == 1,
                        "Pixelate shows two palette colors only in One Bit mode");
                    Check(Parameter("_OneBit").type == ShaderFXParameterType.Float,
                        "Pixelate keeps the existing numeric storage for _OneBit");
                    foreach (string legacyValue in new[] { "false", "true" })
                    {
                        var previousOneBit = Parse("// @param bool _OneBit = " + legacyValue + "\n");
                        var nextColorMode = Parse(presetCode);
                        preserveValues.Invoke(null, new object[] { nextColorMode, previousOneBit });
                        var migratedOneBit = nextColorMode.Find(p => p.name == "_OneBit");
                        float expected = legacyValue == "true" ? 1f : 0f;
                        Check(migratedOneBit.floatValue == expected && migratedOneBit.id == previousOneBit[0].id,
                            "Existing _OneBit=" + legacyValue + " value and parameter identity survive conversion to the two-choice enum");
                    }
                    Check(Control("_AlphaClip").hidden && Control("_AlphaClip").groupHeaderParameter == "_AlphaClip" &&
                        Control("_AlphaCutoff").inGroup && Control("_AlphaCutoff").visibleIfParameter == "_AlphaClip" &&
                        !Control("_AlphaCutoff").visibleIfNotEqual,
                        "Pixelate promotes the hidden alpha toggle into its header and gates cutoff with @if");
                }

                ShaderFX presetFX = null;
                try
                {
                    presetFX = (ShaderFX)createDraft.Invoke(null, new object[] { document, presetCode, new List<ShaderFXParameter>() });
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
                        VisualElement Row(string label) => pixelRows.Find(row =>
                            row.Query<Label>().ToList().Exists(value => value.text == label));
                        ShaderFXParameter colorMode = actualParameters.Find(p => p.name == "_OneBit");
                        Check(pixelateView.Query<DropdownField>().ToList().Exists(field =>
                            string.IsNullOrEmpty(field.label) && field.choices.Count == 2),
                            "Pixelate color mode appears as an unlabeled two-choice selector beside the Color title");
                        Check(Row("Low Color")?.style.display == DisplayStyle.None && Row("High Color")?.style.display == DisplayStyle.None,
                            "Pixelate hides one-bit colors while Color Quantization is selected");
                        pixelateView.GetType().GetMethod("Change", F).Invoke(pixelateView, new object[] { colorMode.id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
                        Check(Row("Levels")?.style.display == DisplayStyle.None && Row("Gamma")?.style.display == DisplayStyle.None &&
                            Row("Low Color")?.style.display == DisplayStyle.Flex && Row("High Color")?.style.display == DisplayStyle.Flex,
                            "Pixelate switches between quantization controls and the One Bit palette");
                        var lowColor = Row("Low Color").Q<ColorField>();
                        var highColor = Row("High Color").Q<ColorField>();
                        Check(lowColor != null && highColor != null && lowColor.label == "Low Color" && highColor.label == "High Color",
                            "Both conditional Pixelate rows contain separately labeled color controls");
                        Check(lowColor.value != highColor.value,
                            "Pixelate low/high color controls retain their distinct black/white values");
                    }
                }
                finally { if (presetFX != null) UnityEngine.Object.DestroyImmediate(presetFX); }
            }

            string halftoneSource = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Halftone.hlsl");
            var halftoneParameters = Parse(halftoneSource);
            ShaderFXParameter HalftoneParameter(string name) => halftoneParameters.Find(p => p.name == name);
            Check(HalftoneParameter("_Mode").type == ShaderFXParameterType.Float &&
                HalftoneParameter("_Mode").controls[0].type == ShaderFXParameterType.Enum &&
                HalftoneParameter("_Mode").controls[0].optionNames.Length == 6 &&
                HalftoneParameter("_Mode").controls[0].optionNames[1] == "CMYKTriangle" &&
                HalftoneParameter("_Mode").controls[0].optionNames[2] == "CMYKSquare" &&
                HalftoneParameter("_Mode").controls[0].optionNames[3] == "CMYKManual" &&
                HalftoneParameter("_Mode").controls[0].optionNames[4] == "RGBTriangle" &&
                HalftoneParameter("_Mode").controls[0].optionNames[5] == "RGBManual" &&
                HalftoneParameter("_Mode").floatValue == 1f,
                "Halftone offers CMYK and RGB plate modes while preserving the legacy CMYK default");
            ShaderFXParameter spread = HalftoneParameter("_PlateSpread");
            Check(spread != null && spread.controls.Count == 3 &&
                spread.controls[0].visibleIfParameter == "_Mode" && !spread.controls[0].visibleIfNotEqual && spread.controls[0].visibleIfValue == 2f &&
                spread.controls[1].visibleIfParameter == "_Mode" && !spread.controls[1].visibleIfNotEqual && spread.controls[1].visibleIfValue == 3f &&
                spread.controls[2].visibleIfParameter == "_Mode" && !spread.controls[2].visibleIfNotEqual && spread.controls[2].visibleIfValue == 4f,
                "Halftone shows the shared spacing control in all automatic modes");
            ShaderFXParameter rotation = HalftoneParameter("_PlateRotation");
            Check(rotation != null && rotation.controls.Count == 3 &&
                rotation.controls[0].visibleIfParameter == "_Mode" && !rotation.controls[0].visibleIfNotEqual && rotation.controls[0].visibleIfValue == 2f &&
                rotation.controls[1].visibleIfParameter == "_Mode" && !rotation.controls[1].visibleIfNotEqual && rotation.controls[1].visibleIfValue == 3f &&
                rotation.controls[2].visibleIfParameter == "_Mode" && !rotation.controls[2].visibleIfNotEqual && rotation.controls[2].visibleIfValue == 4f,
                "Halftone shows the shared layout rotation control only in automatic modes");
            ShaderFXParameter cyanAngle = HalftoneParameter("_CyanAngle");
            Check(cyanAngle.controls.Count == 3 && cyanAngle.controls[0].visibleIfValue == 1f &&
                cyanAngle.controls[1].visibleIfValue == 2f && cyanAngle.controls[2].visibleIfValue == 3f,
                "Halftone shows CMYK angle controls only for CMYK modes");
            ShaderFXParameter redAngle = HalftoneParameter("_RedAngle");
            Check(redAngle.controls.Count == 2 && redAngle.controls[0].visibleIfValue == 4f && redAngle.controls[1].visibleIfValue == 5f,
                "Halftone shows RGB angle controls only for RGB modes");
            ShaderFXParameter paperColor = HalftoneParameter("_PaperColor");
            Check(paperColor.controls.Count == 4 && paperColor.controls[0].visibleIfValue == 0f &&
                paperColor.controls[1].visibleIfValue == 1f && paperColor.controls[2].visibleIfValue == 2f && paperColor.controls[3].visibleIfValue == 3f,
                "Halftone hides the paper tint from additive RGB output");
            foreach (string angle in new[] { "_Angle", "_CyanAngle", "_MagentaAngle", "_YellowAngle", "_BlackAngle", "_RedAngle", "_GreenAngle", "_BlueAngle", "_PlateRotation" })
                Check(HalftoneParameter(angle).floatValue == 0f, "Halftone defaults " + angle + " to zero rotation");
            foreach (string plateOffset in new[] { "_CyanOffset", "_MagentaOffset", "_YellowOffset", "_BlackOffset" })
                Check(HalftoneParameter(plateOffset).controls[0].visibleIfParameter == "_Mode" &&
                    !HalftoneParameter(plateOffset).controls[0].visibleIfNotEqual && HalftoneParameter(plateOffset).controls[0].visibleIfValue == 1f,
                    "Halftone shows " + plateOffset + " only in Manual mode");
            foreach (string plateOffset in new[] { "_RedOffset", "_GreenOffset", "_BlueOffset" })
                Check(HalftoneParameter(plateOffset).controls[0].visibleIfParameter == "_Mode" &&
                    !HalftoneParameter(plateOffset).controls[0].visibleIfNotEqual && HalftoneParameter(plateOffset).controls[0].visibleIfValue == 5f,
                    "Halftone shows " + plateOffset + " only in RGB Manual mode");

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
