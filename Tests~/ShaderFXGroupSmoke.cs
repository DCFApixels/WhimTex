using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXGroupSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        var parse = metadata.GetMethod("Parse", F);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[] { source, false, null });
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        const string declarations =
            "// @group(Tint; _EnableTint)\n" +
            "// @param bool _EnableTint = true\n" +
            "// @param color _Tint = #80C0FFFF\n" +
            "// @if _EnableTint == 1\n" +
            "// @param label(Tint Strength) float _Strength = 0.75 [0 .. 1]\n" +
            "// @endif\n" +
            "// @endgroup\n" +
            "// @group(Advanced; _Mode)\n" +
            "// @param enum _Mode = 1 {Off: 0, On: 1}\n" +
            "// @param float _Offset = 0.1 [0 .. 2]\n" +
            "// @endgroup\n" +
            "// @group(Offset; _Amount)\n" +
            "// @param float _Amount = 0.1 [0 .. 2]\n" +
            "// @param float _AmountScale = 1\n" +
            "// @endgroup\n" +
            "// @group(Internal; _Internal)\n" +
            "// @param hidden label(\"Internal (Advanced) Toggle\") bool _Internal = true\n" +
            "// @param float _Extra = 2\n" +
            "// @endgroup\n" +
            "// @group(Color; _Color)\n" +
            "// @param color _Color = #80C0FFFF\n" +
            "// @endgroup\n" +
            "// @group(Direction; _Direction)\n" +
            "// @param float3 _Direction = (0, 0, 1)\n" +
            "// @endgroup\n" +
            "// @group(Input; _Input)\n" +
            "// @param texture2D _Input = none\n" +
            "// @param float _InputStrength = 1\n" +
            "// @endgroup\n" +
            "// @group(Optional; _OptionalMode)\n" +
            "// @param label(Optional Mode) hidden enum _OptionalMode = Off {Off: 0, On: 1}\n" +
            "// @if _OptionalMode == 1\n" +
            "// @param float _OptionalValue = 1\n" +
            "// @endif\n" +
            "// @endgroup\n" +
            "// @group\n" +
            "// @param float _PlainGroup = 2\n" +
            "// @endgroup\n";
        var values = Parse(declarations);
        var enabled = values.Find(p => p.name == "_EnableTint");
        Check(values.Count == 16 && enabled.type == ShaderFXParameterType.Bool, "Group header bool remains a shader parameter");
        Check(enabled.controls.Count == 1 && !enabled.controls[0].hidden && enabled.controls[0].inGroup &&
            enabled.controls[0].groupTitle == "Tint" && enabled.controls[0].groupHeaderParameter == "_EnableTint", "Group header parameter metadata parse");
        Check(values.Find(p => p.name == "_Mode").controls[0].groupHeaderParameter == "_Mode" &&
            values.Find(p => p.name == "_Amount").controls[0].groupHeaderParameter == "_Amount", "Enum and float header references parse");
        Check(values.Find(p => p.name == "_Strength").controls[0].visibleIfParameter == "_EnableTint", "Conditional controls inside groups");

        foreach (string invalid in new[]
        {
            "// @group(A)\n// @group(B)\n// @param float _X\n// @endgroup\n// @endgroup",
            "// @endgroup",
            "// @group(A)\n// @param float _X",
            "// @group()\n// @param float _X\n// @endgroup",
            "// @group(A; _Missing)\n// @param float _X\n// @endgroup",
            "// @param float _External\n// @group(A; _External)\n// @param float _X\n// @endgroup",
            "// @group(A; _Enabled)\n// @param bool _Enabled = true\n// @param bool _Enabled = false\n// @param float _X\n// @endgroup",
            "// @group(A; _Enabled)\n// @if _Enabled == 1\n// @param bool _Enabled = true\n// @endif\n// @param float _X\n// @endgroup",
            "// @param hidden label(First) label(Second) float _X",
            "// @param label() float _X"
        })
        {
            bool rejected = false;
            try { Parse(invalid); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            Check(rejected, "Accepted invalid group metadata: " + invalid);
        }

        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        try
        {
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            fx = (ShaderFX)create.Invoke(null, new object[]
            {
                document,
                declarations + "float4 ApplyFX(float2 uv, float4 color) { return float4(color.rgb * lerp(1.0, _Tint.rgb, _EnableTint * _Strength), color.a); }\n",
                new List<ShaderFXParameter>()
            });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            Check(typeof(ShaderFX).GetField("compiledShader", F).GetValue(fx) != null, "Grouped shader compiles");
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            var enabledParameter = parameters.Find(p => p.name == "_EnableTint");
            var internalParameter = parameters.Find(p => p.name == "_Internal");
            var view = (VisualElement)Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView"),
                F, null, new object[] { fx }, null);
            var groups = view.Query<VisualElement>(className: "whimtex-fx-parameter-group").ToList();
            Check(groups.Count == 9, "All group visual containers are built");
            Check(groups[0].Q<Label>()?.text == "Tint", "Group title displayed");
            Check(groups[0].Q<ColorField>() != null && groups[0].Q<Slider>() != null, "Group parameters displayed");
            Check(groups[0].Q<Slider>()?.label == "Tint Strength", "Manual parameter label is used instead of the generated name");
            Check(view.Q<Toggle>() != null && view.Q<Toggle>().value, "Bool parameter rendered as the group header checkbox");
            Check(groups[0].Query<Toggle>().ToList().Count == 1 &&
                groups[0].Query<Label>().ToList().TrueForAll(label => label.text != "Enable Tint"),
                "Toggle is not duplicated as a normal parameter field");
            Check(groups[1].Q<Label>()?.text == "Advanced" && string.IsNullOrEmpty(groups[1].Q<DropdownField>()?.label) && groups[1].Q<DropdownField>()?.value == "On",
                "Enum header title labels its unlabeled dropdown");
            Check(groups[2].Q<Label>()?.text == "Offset" && string.IsNullOrEmpty(groups[2].Q<FloatField>()?.label) && groups[2].Q<FloatField>()?.value == .1f &&
                groups[2].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").Q<FloatField>()?.label == "Amount Scale",
                "Float header title labels its unlabeled field while other parameters remain in the body");
            Check(groups[2].Q<Label>()?.ClassListContains("whimtex-fx-parameter-group-title--draggable") == true &&
                groups[2].Q<Label>()?.tooltip.Contains("Drag horizontally") == true,
                "Float-linked group title advertises direct horizontal drag editing");
            Check(groups[3].Q<Label>()?.text == "Internal" && groups[3].Query<Toggle>().ToList().Count == 1 &&
                groups[3].Q<Toggle>()?.value == true &&
                groups[3].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").Q<FloatField>()?.label == "Extra" &&
                internalParameter.controls[0].hidden && internalParameter.controls[0].label == "Internal (Advanced) Toggle",
                "Hidden linked bool is promoted to the header and omitted from the group body");
            Check(groups[4].Q<Label>()?.text == "Color" && string.IsNullOrEmpty(groups[4].Q<ColorField>()?.label) &&
                groups[4].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").childCount == 0 &&
                groups[5].Q<Label>()?.text == "Direction" && string.IsNullOrEmpty(groups[5].Q<Vector3Field>()?.label) &&
                groups[5].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").childCount == 0,
                "Color and vector values use the group title as their label and are not duplicated in the body");
            Check(groups[6].Q<Label>()?.text == "Input" && groups[6].Q<VisualElement>(className: "whimtex-fx-group-header-field") == null &&
                groups[6].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").childCount == 2,
                "Unsupported multi-row values remain in the body under a plain title");
            Check(!groups[0].ClassListContains("whimtex-fx-parameter-group--empty"),
                "Groups with visible conditional rows keep their content area");
            Check(groups[7].ClassListContains("whimtex-fx-parameter-group--empty") &&
                string.IsNullOrEmpty(groups[7].Q<DropdownField>()?.label) &&
                groups[7].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").childCount == 1,
                "A hidden linked enum is in the header and an inactive conditional body collapses");

            var strength = parameters.Find(p => p.name == "_Strength");
            var tint = parameters.Find(p => p.name == "_Tint");
            view.GetType().GetMethod("Change", F).Invoke(view, new object[] { tint.id, (Action<ShaderFXParameter>)(p => p.colorValue = Color.magenta) });
            view.GetType().GetMethod("Change", F).Invoke(view, new object[] { enabledParameter.id, (Action<ShaderFXParameter>)(p => p.floatValue = 0f) });
            Check(!groups[0].ClassListContains("whimtex-fx-parameter-group--empty") &&
                groups[0].Q<VisualElement>(className: "whimtex-fx-conditional-parameter").ClassListContains("whimtex-fx-conditional-parameter--hidden") &&
                groups[0].Q<VisualElement>(className: "whimtex-fx-parameter-group-content").Q<ColorField>() != null,
                "Changing the group checkbox does not implicitly hide its body; @if owns conditional visibility");
            Check(Mathf.Approximately(tint.colorValue.r, 1f) && Mathf.Approximately(tint.colorValue.b, 1f) && strength.floatValue == .75f,
                "Hiding group controls preserves their values");
            var optionalMode = parameters.Find(p => p.name == "_OptionalMode");
            view.GetType().GetMethod("Change", F).Invoke(view, new object[] { optionalMode.id, (Action<ShaderFXParameter>)(p => p.floatValue = 1f) });
            Check(!groups[7].ClassListContains("whimtex-fx-parameter-group--empty"), "Showing an @if row expands the group body");
            view.GetType().GetMethod("Change", F).Invoke(view, new object[] { optionalMode.id, (Action<ShaderFXParameter>)(p => p.floatValue = 0f) });
            Check(groups[7].ClassListContains("whimtex-fx-parameter-group--empty"), "Hiding the last @if row collapses the group body without a gap");

            string saved = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource", F)
                .Invoke(null, new object[] { fx, "Test/Grouped" });
            Check(saved.Contains("// @group(Tint; _EnableTint)") && saved.Contains("// @param bool _EnableTint = false") &&
                saved.Contains("hidden label(\"Internal (Advanced) Toggle\")") &&
                saved.Contains("hidden label(\"Optional Mode\") enum") &&
                saved.Contains("// @group(Advanced; _Mode)") && saved.Contains("// @group(Offset; _Amount)") &&
                saved.Contains("// @group") && saved.Contains("// @endgroup"),
                "Preset export retains group references and declarations");
            var restored = Parse(saved);
            Check(restored.Find(p => p.name == "_EnableTint").controls[0].groupHeaderParameter == "_EnableTint" &&
                !restored.Find(p => p.name == "_EnableTint").controls[0].hidden && restored.Find(p => p.name == "_Strength").floatValue == .75f &&
                restored.Find(p => p.name == "_Strength").controls[0].label == "Tint Strength" &&
                restored.Find(p => p.name == "_Internal").controls[0].label == "Internal (Advanced) Toggle" &&
                restored.Find(p => p.name == "_OptionalMode").controls[0].hidden &&
                restored.Find(p => p.name == "_OptionalMode").controls[0].label == "Optional Mode",
                "Group metadata, custom labels and values survive preset roundtrip");

            foreach (string presetName in new[]
            {
                "ChromaticAberration", "ColorBalance", "ColorFilter", "CRT", "DigitalGlitch", "DisplacementMap",
                "FromPolar", "Gain", "GradientMap", "Halftone", "HSV", "Levels", "Mask", "Negative", "Normalize",
                "NormalLighting", "Pixelate", "Posterize", "RadialShear", "SdfBevel", "Spherize", "Step",
                "Threshold", "ToPolar", "Twirl", "UVTransform", "VHS"
            })
            {
                string presetCode = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + presetName + ".hlsl");
                var presetParameters = Parse(presetCode);
                Check(presetParameters.Count > 0, presetName + " metadata parses");
                ShaderFX presetFx = null;
                try
                {
                    presetFx = (ShaderFX)create.Invoke(null, new object[] { document, presetCode, new List<ShaderFXParameter>() });
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(presetFx, null);
                    Check(typeof(ShaderFX).GetField("compiledShader", F).GetValue(presetFx) != null, presetName + " grouped preset compiles");
                }
                finally { if (presetFx != null) UnityEngine.Object.DestroyImmediate(presetFx); }
            }

            List<ShaderFXParameter> PresetParameters(string presetName) => Parse(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + presetName + ".hlsl"));
            ShaderFXParameter Parameter(List<ShaderFXParameter> parameters, string name) => parameters.Find(p => p.name == name);
            ShaderFXParameterControl Control(List<ShaderFXParameter> parameters, string name) => Parameter(parameters, name).controls[0];
            void CheckTitle(string presetName, string parameterName, string expected)
            {
                var parameters = PresetParameters(presetName);
                Check(Control(parameters, parameterName).groupTitle == expected,
                    presetName + " uses its group title as the " + parameterName + " label");
            }
            void CheckCondition(string presetName, string parameterName, string driver)
            {
                var parameters = PresetParameters(presetName);
                var control = Control(parameters, parameterName);
                Check(control.visibleIfParameter == driver && control.visibleIfNotEqual && control.visibleIfValue == 0f,
                    presetName + " conditionally displays " + parameterName + " from " + driver);
            }
            void CheckLabel(string presetName, string parameterName, string expected)
            {
                var parameters = PresetParameters(presetName);
                Check(Control(parameters, parameterName).label == expected,
                    presetName + " labels " + parameterName + " as " + expected);
            }
            CheckCondition("ColorBalance", "_Shadows", "_ShadowRange");
            CheckCondition("ColorBalance", "_Highlights", "_HighlightRange");
            CheckCondition("ColorFilter", "_FilterColor", "_Density");
            CheckCondition("ColorFilter", "_PreserveLuminosity", "_Density");
            CheckCondition("HSV", "_Hue", "_Amount");
            CheckCondition("HSV", "_Saturation", "_Amount");
            CheckCondition("HSV", "_Value", "_Amount");
            CheckCondition("Twirl", "_Area", "_Angle");
            CheckCondition("RadialShear", "_Center", "_Strength");
            Check(Control(PresetParameters("Halftone"), "_PaperColor").inGroup &&
                PresetParameters("Halftone").TrueForAll(parameter => parameter.controls.TrueForAll(control => control.inGroup)),
                "Halftone keeps shared and mode-specific controls in the Screen Mode group");
            CheckTitle("ChromaticAberration", "_Mode", "Separation Mode");
            CheckTitle("DigitalGlitch", "_AlphaFollowChance", "Alpha Follow Chance");
            CheckTitle("DisplacementMap", "_MapWrap", "Map Wrap");
            CheckTitle("DisplacementMap", "_Mode", "Displacement Mode");
            CheckTitle("DisplacementMap", "_MaskSource", "Strength Mask Source");
            CheckTitle("DisplacementMap", "_Mix", "Output Mix");
            CheckTitle("GradientMap", "_Amount", "Map Amount");
            CheckTitle("Halftone", "_Mode", "Screen Mode");
            CheckTitle("NormalLighting", "_Output", "Lighting Output");
            CheckTitle("Mask", "_MaskChannel", "Mask Channel");
            CheckTitle("Mask", "_ApplyMode", "Apply To");
            CheckTitle("Mask", "_Profile", "Mask Shape");
            var maskParameters = PresetParameters("Mask");
            Check(Array.TrueForAll(new[] { "_ApplyAlpha", "_ApplyRed", "_ApplyGreen", "_ApplyBlue" }, name =>
                Parameter(maskParameters, name).type == ShaderFXParameterType.Bool && Control(maskParameters, name).groupTitle == "Apply To") &&
                Parameter(maskParameters, "_ApplyAlpha").floatValue == 1f &&
                Parameter(maskParameters, "_ApplyRed").floatValue == 0f &&
                Parameter(maskParameters, "_ApplyGreen").floatValue == 0f &&
                Parameter(maskParameters, "_ApplyBlue").floatValue == 0f,
                "Mask exposes four independent target-channel toggles, with Alpha enabled by default");
            Check(Control(maskParameters, "_ApplyRed").visibleIfParameter == "_ApplyMode" &&
                Control(maskParameters, "_ApplyRed").visibleIfValue == 0f &&
                Control(maskParameters, "_ApplyColor").visibleIfParameter == "_ApplyMode" &&
                Control(maskParameters, "_ApplyColor").visibleIfValue == 1f &&
                Control(maskParameters, "_Amount").visibleIfParameter == null,
                "Mask switches between channel toggles and the color blend field");
            string maskSource = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Mask.hlsl");
            Check(maskSource.Contains("float4 applyWeights = lerp(channelMask, 1 - _ApplyColor, _ApplyMode);") &&
                maskSource.Contains("color *= lerp(float4(1.0, 1.0, 1.0, 1.0), float4(multiplier, multiplier, multiplier, multiplier), applyWeights);") &&
                !maskSource.Contains("color = lerp(color, _ApplyColor"),
                "Mask inverts the selected color into per-channel mask application weights");
            CheckTitle("Step", "_ApplyMode", "Apply To");
            var stepParameters = PresetParameters("Step");
            Check(Array.TrueForAll(new[] { "_Red", "_Green", "_Blue", "_Alpha" }, name =>
                Parameter(stepParameters, name).type == ShaderFXParameterType.Bool && Control(stepParameters, name).groupTitle == "Apply To") &&
                Control(stepParameters, "_ApplyColor").visibleIfParameter == "_ApplyMode" &&
                Control(stepParameters, "_ApplyColor").visibleIfValue == 1f,
                "Step switches between channel toggles and a per-channel color weight");
            string stepSource = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Step.hlsl");
            Check(stepSource.Contains("float4 applyWeights = lerp(channelMask, _ApplyColor, _ApplyMode);") &&
                stepSource.Contains("return lerp(color, stepped, applyWeights);") &&
                !stepSource.Contains("if (_Red") && !stepSource.Contains("if (_Green") &&
                !stepSource.Contains("if (_Blue") && !stepSource.Contains("if (_Alpha"),
                "Step applies selected channels or the color weight vector with one lerp");
            CheckTitle("Pixelate", "_AlphaClip", "Alpha Clip");
            CheckTitle("Spherize", "_Mode", "Spherize Mode");
            CheckTitle("VHS", "_TrackingStrength", "Tracking Strength");
            CheckLabel("Levels", "_InBlack", "Input Black");
            CheckLabel("Levels", "_InWhite", "Input White");
            CheckLabel("Levels", "_OutBlack", "Output Black");
            CheckLabel("Levels", "_OutWhite", "Output White");
            CheckLabel("Pixelate", "_Amount", "Dither Strength");
            CheckLabel("Posterize", "_Amount", "Dither Strength");
            CheckLabel("ChromaticAberration", "_Amount", "Channel Offset (px)");
            CheckLabel("Threshold", "_Smooth", "Transition Width");
            CheckLabel("SdfBevel", "_Smoothing", "Normal Radius (px)");
            CheckLabel("Mask", "_Amount", "Amount");
            CheckLabel("Mask", "_Invert", "Invert");
            return "PASS: group parsing/validation, all 27 built-in FX metadata/shaders, linked-header labels, conditional rows and preset roundtrips.";
        }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        finally
        {
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
