using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class SelectiveHsvTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    const string PresetPath = "Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl";

    public static string Run() => TestContext.Run("Hue & Saturation with weighted gray, Lightness and selective ranges", t =>
    {
        using (var fixture = new MigrationD())
        using (var images = new Images(t))
        {
            Metadata(t, images);
            CircularRanges(t, images);
            SaturationAndMasks(t, images);
            Corrections(t, images);
            MasterCorrections(t, images);
            MasterFastPath(t, images);
            LayerRenderPaths(t, images);
            StoredValues(t, images);
            t.True(ReferenceEquals(images.InitialShader,
                typeof(ShaderFX).GetField("compiledShader", Hidden).GetValue(images.Selective)),
                "All range, Enabled, Output and opacity edits reuse the compiled shader");
            foreach (var message in ShaderUtil.GetShaderMessages(images.InitialShader))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error &&
                    message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
        }
    });

    static void Metadata(TestContext t, Images images)
    {
        var parameters = images.Parameters;
        t.Equal(30, parameters.Count, "Four six-parameter ranges, three Master fields and three shared parameters");
        void Page(ShaderFXParameterControl control, string title)
        {
            t.Equal(2, control.containers.Length, "Tabs and page without a nested group");
            t.True(control.containers[0].kind == ShaderFXContainerKind.Tabs && string.IsNullOrEmpty(control.containers[0].title),
                "One tabs block without a shared heading");
            t.True(control.containers[1].kind == ShaderFXContainerKind.Tab && control.containers[1].title == title,
                "Parameter belongs to its own " + title + " page");
            t.True(!control.inGroup && string.IsNullOrEmpty(control.groupHeaderParameter), "Page fields do not use parameter-group headers");
        }
        foreach (string name in new[] { "_Opacity", "_SaturationRange", "_Output" })
            t.Equal(0, images.P(name).controls[0].containers.Length, "Shared field remains outside tabs: " + name);
        foreach (var suffix in new[] { "Hue", "Saturation", "Lightness" })
        {
            var master = images.P("_Master" + suffix);
            var control = master.controls[0];
            Page(control, "Master");
            t.Equal(ShaderFXParameterType.Float, control.type, "Master uses ordinary numeric fields");
            t.True(string.IsNullOrEmpty(control.groupTitle), "Master has no duplicate group title");
            t.True(!control.inGroup && !control.hidden && string.IsNullOrEmpty(control.visibleIfParameter) &&
                string.IsNullOrEmpty(control.groupHeaderParameter), "Master is unconditional, not an enabled hue range");
            t.Near(suffix == "Saturation" ? 1 : 0, master.floatValue, 0, "Neutral Master default");
            t.Equal("PreviewMasterCorrection", control.previewFunction, "Master controls share one preview function");
            t.Equal("_MasterHue,_MasterSaturation,_MasterLightness", string.Join(",", control.previewParameters), "Master binds its current correction parameters explicitly");
        }
        var opacity = images.P("_Opacity");
        t.True(opacity.controls[0].hidden && !opacity.controls[0].inGroup, "Opacity has no body row");
        t.Near(1, opacity.floatValue, 0, "Default opacity");
        t.Near(.05f, images.P("_SaturationRange").vectorValue.x, 0, "Default saturation cutoff");
        t.Near(.1f, images.P("_SaturationRange").vectorValue.y, 0, "Default saturation full strength");
        t.Near(0, images.P("_Output").floatValue, 0, "Default Result output");
        var output = images.P("_Output").controls[0];
        t.Equal(ShaderFXParameterType.Enum, output.type, "Output is an ordinary enum");
        string[] names = { "Result", "AllRangesMask", "Range1Mask", "Range2Mask", "Range3Mask", "Range4Mask" };
        t.Equal(names.Length, output.optionNames.Length, "Result, union and individual masks");
        for (int n = 0; n < names.Length; n++)
        {
            t.Equal(names[n], output.optionNames[n], "Output name");
            t.Near(n, output.optionValues[n], 0, "Output value");
        }
        float[] centers = { 0, 120, 240, 60 };
        for (int n = 1; n <= 4; n++)
        {
            string prefix = "_Range" + n;
            var enabled = images.P(prefix + "Enabled");
            Page(enabled.controls[0], "Range " + n);
            t.Equal(ShaderFXParameterType.Toggle, enabled.controls[0].type, "Range Enabled uses a checkbox");
            t.Equal(prefix + "Enabled", enabled.controls[0].containers[1].headerParameter, "Range Enabled is linked to its tab header");
            t.True(!enabled.controls[0].hidden && enabled.controls[0].enableIf.Length == 0 && string.IsNullOrEmpty(enabled.controls[0].visibleIfParameter),
                "Enabled remains available outside its availability block");
            t.Near(0, enabled.floatValue, 0, "All ranges start disabled so Master affects the whole image");
            t.Near(n == 1 ? 330 : centers[n - 1] - 30, images.P(prefix).vectorValue.x, 0, "Default interval start");
            t.Near(centers[n - 1] + 30, images.P(prefix).vectorValue.y, 0, "Default interval end");
            t.True(images.P(prefix).cyclicRange && images.P(prefix).minimum == 0 && images.P(prefix).maximum == 360,
                "Hue range spans one cyclic turn");
            t.Near(15, images.P(prefix + "Feather").floatValue, 0, "Default feather per side");
            t.True(string.IsNullOrEmpty(images.P(prefix + "Feather").controls[0].previewFunction), "Feather remains a plain slider");
            t.Near(0, images.P(prefix + "Hue").floatValue, 0, "Neutral hue shift");
            foreach (string suffix in new[] { "", "Feather", "Hue", "Saturation", "Lightness" })
            {
                var parameter = images.P(prefix + suffix);
                var control = parameter.controls[0];
                Page(control, "Range " + n);
                t.Equal(1, parameter.controls.Count, "One declaration per range field");
                t.Equal(enabled.controls[0].containers[1].id, control.containers[1].id, "Range fields share their tab page");
                t.True(string.IsNullOrEmpty(control.groupTitle) && string.IsNullOrEmpty(control.groupHeaderParameter), "Range has no nested group or linked header");
                t.True(string.IsNullOrEmpty(control.visibleIfParameter), "Disabled range fields remain visible");
                t.True(control.enableIf.Length == 1 && control.enableIf[0].parameter == prefix + "Enabled" &&
                    !control.enableIf[0].notEqual && control.enableIf[0].value == 1, "Body availability follows Enabled");
                bool multiplier = suffix == "Saturation";
                t.True(control.hasMinimum && control.hasMaximum && !control.softMinimum && control.softMaximum == multiplier,
                    "Only multiplier upper bounds are soft");
                if (multiplier) t.Near(1, parameter.floatValue, 0, "Neutral multiplier");
                if (suffix == "Lightness")
                {
                    t.Near(-100, control.minimum, 0, "Lightness has a hard black endpoint");
                    t.Near(100, control.maximum, 0, "Lightness has a hard white endpoint");
                    t.Near(0, parameter.floatValue, 0, "Neutral Lightness");
                }
                if (suffix == "")
                {
                    t.Equal("PreviewHueSelection", control.previewFunction, "Hue tracks share one selection preview function");
                    t.Equal(prefix + "," + prefix + "Feather", string.Join(",", control.previewParameters), "Selection binds only its own range and Feather");
                }
                else if (suffix != "Feather")
                {
                    t.Equal("PreviewRangeCorrection", control.previewFunction, "Correction tracks share one preview function");
                    t.Equal(prefix + "," + prefix + "Hue," + prefix + "Saturation," + prefix + "Lightness",
                        string.Join(",", control.previewParameters), "Correction preview binds its own range rather than another range");
                }
            }
        }
        string source = (string)typeof(ShaderFX).GetField("code", Hidden).GetValue(images.Selective);
        t.True(source.Replace("\r\n", "\n").StartsWith("// @whimtex-effect Color/Hue & Saturation\n// @control(_Opacity)\n", StringComparison.Ordinal),
            "One HSV preset retains the header control contract");
        int hsvEntries = 0;
        var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
        foreach (var entry in (IEnumerable)catalog.GetMethod("GetEntries", Hidden).Invoke(null, null))
        {
            string path = (string)entry.GetType().GetField("path", Hidden).GetValue(entry);
            if (!path.StartsWith("Packages/com.dcfapixels.whimtex/src/FXPresets/", StringComparison.Ordinal)) continue;
            string menu = (string)entry.GetType().GetField("menuPath", Hidden).GetValue(entry);
            t.True(menu != "Color/Selective HSV", "No second selective catalog entry");
            if (menu != "Color/Hue & Saturation") continue;
            hsvEntries++;
            t.Equal("c7a3baa679367ed449c56370276c2a57", (string)entry.GetType().GetField("guid", Hidden).GetValue(entry),
                "Unified HSV preserves the selective preset identity, not the removed compact preset identity");
        }
        t.Equal(1, hsvEntries, "Exactly one built-in HSV");
        t.True(!System.IO.File.Exists(PresetPath.Replace("/HueSaturation.hlsl", "/SelectiveHSV.hlsl")) &&
            !System.IO.File.Exists(PresetPath.Replace("/HueSaturation.hlsl", "/HSV.hlsl")), "No duplicate old production presets");
        t.True(parameters.Find(p => p.name == "_MasterValue") == null &&
            parameters.Find(p => p.name == "_Range1Value") == null, "Value is replaced, not silently aliased");
        t.Equal("Source Saturation (HSV)", images.P("_SaturationRange").controls[0].label,
            "The selection metric is distinguished from artistic Saturation");
    }

    static void CircularRanges(TestContext t, Images images)
    {
        images.Reset(); images.Set("_Output", 2); images.Range(1, center: 0, width: 20, feather: 10);
        Color[] seam = Hues(359, 0, 1, 10, 15, 20, 180);
        Mask(t, images.Render(seam), new[] { 1f, 1, 1, 1, .5f, 0, 0 }, "Circular red boundary");
        images.Range(1, center: 360, width: 20, feather: 10);
        Mask(t, images.Render(seam), new[] { 1f, 1, 1, 1, .5f, 0, 0 }, "Center 360 equals 0");

        images.Range(1, width: 0, feather: 0);
        Mask(t, images.Render(Hues(0, 1, 180, 359)), new[] { 0f, 0, 0, 0 }, "Zero width and feather are empty");
        images.Set("_Range1Feather", 20);
        Mask(t, images.Render(Hues(0, 5, 10, 15, 20, 180)), new[] { 1f, .84375f, .5f, .15625f, 0, 0 },
            "Zero width with feather is a smooth peak");

        images.Range(1, width: 360, feather: 180);
        Mask(t, images.Render(Hues(0, 60, 120, 180, 240, 300, 359)), new[] { 1f, 1, 1, 1, 1, 1, 1 },
            "Full width covers the antipode too");
        foreach (var pair in new[] { new Vector2(-330, 30), new Vector2(330, 690),
            new Vector2(30.1f - 360, 30.1f), new Vector2(30.1f, 30.1f + 360) })
        {
            images.Set("_Range1Feather", 0); images.P("_Range1").vectorValue = pair;
            Mask(t, images.Render(Hues(0, 30.1f, 120, 180, 210.1f, 240, 359)), new[] { 1f, 1, 1, 1, 1, 1, 1 },
                "Anchored full cycle covers every hue with zero Feather: " + pair);
        }
        images.Range(1, width: 300, feather: 180);
        float nearAntipode = 1 - (29f / 30) * (29f / 30) * (3 - 2 * 29f / 30);
        Mask(t, images.Render(Hues(150, 165, 179, 180, 181, 195, 210)),
            new[] { 1f, .5f, nearAntipode, 0, nearAntipode, .5f, 1 }, "Feather ends at the opposite hue");
        t.Near(180, images.P("_Range1Feather").floatValue, 0, "Effective feather limit does not rewrite stored feather");
        images.Range(1, width: 120, feather: 0);
        Mask(t, images.Render(Hues(59, 61, 301, 299)), new[] { 1f, 0, 1, 0 }, "Zero feather has a hard boundary");

        images.Range(1, width: 60, feather: 30);
        var sweep = new Color[361];
        var expected = new float[sweep.Length];
        for (int n = 0; n < sweep.Length; n++)
        {
            sweep[n] = Display(n);
            double distance = Math.Min(n, 360 - n);
            double x = Math.Max(0, Math.Min(1, (distance - 30) / 30));
            expected[n] = (float)(1 - x * x * (3 - 2 * x));
        }
        Mask(t, images.Render(sweep), expected, "Continuous full hue sweep with symmetric feather");
    }

    static void SaturationAndMasks(TestContext t, Images images)
    {
        images.Reset(); images.Range(1); images.Set("_Output", 1);
        images.Gate(.5f, 1);
        var saturations = new[] { Display(0, .25f), Display(0, .5f), Display(0, .75f), Display(0) };
        Mask(t, images.Render(saturations), new[] { 0f, 0, .5f, 1 }, "Saturation gate");
        images.Gate(1, 1);
        Mask(t, images.Render(saturations), new[] { 0f, 0, 0, 1 }, "Degenerate gate at saturation one");
        images.Gate(.5f, .5f);
        Mask(t, images.Render(new[] { Display(0, .49f), Display(0, .51f) }), new[] { 0f, 1 }, "Zero saturation feather is a step");

        images.Gate(0, 0);
        var undefined = new[] { Color.black, new Color(.18f, .18f, .18f, 1), Display(0, 1, 5e-6f),
            Display(0, 5e-6f, .8f), Display(0, 5e-6f, 4), new Color(-2, -1, -.5f, 1),
            new Color(-2, 1, 4, 0) };
        Mask(t, images.Render(undefined), new float[undefined.Length], "Unreliable hue and hidden RGB never select");
        images.Set("_Output", 0); images.Range(1, hue: 120, saturation: 0, lightness: 50);
        Same(t, undefined, images.Render(undefined), "Unreliable hue leaves every Result channel untouched", exact: true);

        images.Reset(); images.Range(1, hue: 120, saturation: 0, lightness: 50);
        var almostGray = new[] { Display(0, .01f), Display(140, .025f), Display(280, .04f) };
        Same(t, almostGray, images.Render(almostGray), "Default saturation gate protects nearly gray hue noise", exact: true);

        images.Reset(); images.Gate(0, 0);
        images.Range(1, center: 30, width: 0, feather: 60);
        images.Range(2, center: 30, width: 0, feather: 60);
        images.Set("_Output", 1);
        Mask(t, images.Render(Hues(0)), new[] { 1f }, "All Ranges Mask saturates summed hue coverage");
        images.Set("_Output", 2);
        Mask(t, images.Render(Hues(0)), new[] { .5f }, "Individual mask retains its own feather");
        images.Set("_Range1Enabled", 0);
        Mask(t, images.Render(Hues(0)), new[] { 0f }, "Disabled range has an empty individual mask");
        images.Set("_Output", 1);
        Mask(t, images.Render(Hues(0)), new[] { .5f }, "Disabled range does not contribute to total coverage");
        for (int n = 1; n <= 4; n++)
        {
            images.Reset(); images.Range(1, enabled: false); images.Range(n);
            images.Set("_Output", n + 1);
            Mask(t, images.Render(Hues(210)), new[] { 1f }, "Mask selector for range " + n);
        }

        images.Reset(); images.Range(1); images.Set("_Output", 1);
        var translucent = Display(0, alpha: .25f);
        Mask(t, images.Render(new[] { translucent, Display(0, alpha: 0) }), new[] { .25f, 0 },
            "Mask RGB is coverage times source alpha, mask alpha is one");
        images.Set("_Opacity", .4f);
        Same(t, new[] { Color.Lerp(translucent, new Color(.25f, .25f, .25f, 1), .4f) },
            images.Render(new[] { translucent }), "Partial mask opacity blends the entire RGBA");
        for (int mode = 0; mode <= 5; mode++)
        {
            images.Set("_Output", mode); images.Set("_Opacity", 0);
            Same(t, new[] { translucent, undefined[6] }, images.Render(new[] { translucent, undefined[6] }),
                "Zero opacity is exact in output " + mode, exact: true);
        }
    }

    static void Corrections(TestContext t, Images images)
    {
        var samples = new[] { Display(0), Display(120, .4f, 2, .25f), Display(240, .8f, 3),
            Display(359, .5f), new Color(-.2f, .3f, 4, .6f) };
        var unchanged = new[] { new Color(-.25f, .125f, 3, .37f), new Color(70000, .07f, .4f, .25f),
            new Color(-2, 1, 4, 0), Color.black, new Color(.18f, .18f, .18f, 1) };
        images.Reset();
        Same(t, unchanged, images.Render(unchanged), "Default preset is exact signed/HDR passthrough", exact: true);
        images.Range(1, enabled: false, hue: 120, saturation: 2, lightness: 50);
        Same(t, unchanged, images.Render(unchanged), "All ranges disabled is exact passthrough", exact: true);
        for (int n = 1; n <= 4; n++) images.Range(n);
        Same(t, unchanged, images.Render(unchanged), "Four neutral ranges are exact passthrough", exact: true);

        foreach (var settings in new[] { new Vector4(120, 1, 0, 1), new Vector4(37, .3f, 50, 1),
            new Vector4(0, 3, -25, 1), new Vector4(-179, 1.5f, 75, .5f), new Vector4(180, 0, 0, 1),
            new Vector4(0, 1, 100, 1), new Vector4(70, 2, -100, 1) })
        {
            images.Reset(); images.Range(1, hue: settings.x, saturation: settings.y, lightness: settings.z);
            images.Gate(0, 0); images.Set("_Opacity", settings.w);
            var expected = Array.ConvertAll(samples, c => Expected(c, settings.x, settings.y, settings.z, settings.w));
            Same(t, expected, images.Render(samples), "Full-weight range matches independent RGB correction " + settings);
        }

        images.Reset(); images.Range(1, hue: 120, saturation: .5f, lightness: 50);
        var single = images.Render(samples);
        images.Range(2, hue: 120, saturation: .5f, lightness: 50);
        Same(t, single, images.Render(samples), "Identical overlaps do not double correction");
        images.Range(2, hue: -120, saturation: 1.5f, lightness: -50);
        Same(t, samples, images.Render(samples), "Opposite signed corrections cancel exactly", exact: true);
        images.Range(1, hue: 179); images.Range(2, hue: -179);
        Same(t, samples, images.Render(samples), "Near-wrap signed shifts cancel exactly", exact: true);
        images.Range(1, hue: 120); images.Range(2);
        Same(t, new[] { Expected(samples[0], 60) }, images.Render(new[] { samples[0] }), "Enabled neutral overlap dilutes correction");
        images.Range(2, enabled: false);
        Same(t, new[] { Expected(samples[0], 120) }, images.Render(new[] { samples[0] }), "Disabling neutral range removes dilution");

        images.Reset(); images.Range(1, center: 30, width: 0, feather: 60, hue: 120, saturation: 0, lightness: 100);
        var feathered = Display(0, .5f);
        Same(t, new[] { Expected(feathered, 60, .5f, 50) }, images.Render(new[] { feathered }),
            "A single feather weakens hue, saturation offset and Lightness toward neutral");

        images.Reset(); images.Gate(.5f, .7f);
        images.Range(1, hue: 120, saturation: 2, lightness: 50);
        var gated = Display(0, .6f);
        single = images.Render(new[] { gated });
        images.Range(2, hue: 120, saturation: 2, lightness: 50);
        Same(t, single, images.Render(new[] { gated }), "Overlap normalization precedes saturation gating");
        Same(t, new[] { Expected(gated, 60, 1.5f, 25) }, images.Render(new[] { gated }), "Half gate weakens normalized correction");
        images.Set("_Output", 1);
        Mask(t, images.Render(new[] { gated }), new[] { .5f }, "Overlaps do not amplify saturation protection in masks");

        images.Reset(); images.Range(1, center: 30, width: 0, feather: 60, hue: 180);
        var plus = images.Render(Hues(0));
        Same(t, new[] { Display(90) }, plus, "+180 follows the positive direction at half weight");
        images.Set("_Range1Hue", -180);
        var minus = images.Render(Hues(0));
        Same(t, new[] { Display(270) }, minus, "-180 follows the negative direction at half weight");
        t.True(plus[0].g > .9f && minus[0].b > .9f, "Signed endpoint feather directions remain distinct");
        images.Range(1, hue: 180); plus = images.Render(Hues(0));
        images.Set("_Range1Hue", -180);
        Same(t, plus, images.Render(Hues(0)), "Signed endpoints reach the same hue at full weight");
        images.Range(1, hue: 179);
        Same(t, Hues(178), images.Render(Hues(359)), "Positive shift wraps through red");
        images.Set("_Range1Hue", -179);
        Same(t, Hues(182), images.Render(Hues(1)), "Negative shift wraps through red");

        images.Reset(); images.Range(1, width: 30, hue: 120);
        images.Range(2, center: 120, width: 30, hue: 120, lightness: 50);
        var source = Hues(0, 120);
        Same(t, new[] { Display(120), Expected(source[1], 120, 1, 50) }, images.Render(source),
            "Every range uses source hue, so correction cannot cascade into another range");
        var beforeSwap = images.Render(samples);
        images.Range(1, center: 120, width: 30, hue: 120, lightness: 50);
        images.Range(2, width: 30, hue: 120);
        Same(t, beforeSwap, images.Render(samples), "Swapping range settings preserves output");

        images.Range(1, center: 15, width: 30, feather: 30, hue: 95, saturation: .2f, lightness: 35);
        images.Range(2, center: 345, width: 20, feather: 60, hue: -35, saturation: 2, lightness: -30);
        beforeSwap = images.Render(samples);
        images.Range(1, center: 345, width: 20, feather: 60, hue: -35, saturation: 2, lightness: -30);
        images.Range(2, center: 15, width: 30, feather: 30, hue: 95, saturation: .2f, lightness: 35);
        Same(t, beforeSwap, images.Render(samples), "Swapping unequal overlapping feather ranges preserves output");

        images.Reset(); images.Range(1, hue: 120);
        var alpha = new[] { Display(0), Display(0, alpha: .25f), Display(0, alpha: 1e-7f), Display(0, alpha: 0) };
        Same(t, new[] { Display(120), Display(120, alpha: .25f), Display(120, alpha: 1e-7f), alpha[3] },
            images.Render(alpha), "Result preserves alpha and never scales correction by coverage");
        var actual = images.Render(alpha);
        for (int n = 0; n < alpha.Length; n++) t.Near(alpha[n].a, actual[n].a, 0, "Result alpha is exact");
        images.Range(1, width: 20, hue: 120);
        Same(t, new[] { samples[4] }, images.Render(new[] { samples[4] }), "Unselected signed/HDR input is exact", exact: true);
        images.Range(1, lightness: -25);
        actual = images.Render(new[] { Display(0, value: 3) });
        Same(t, new[] { Expected(Display(0, value: 3), 0, 1, -25) }, actual, "Lightness supports HDR without an SDR clamp");
        t.True(actual[0].r > 1, "Active correction has no SDR ceiling");
        images.Set("_Range1Lightness", 0); images.Set("_Range1Saturation", 100);
        actual = images.Render(new[] { new Color(70000, 10, 2, .2f) });
        for (int c = 0; c < 3; c++) t.True(!float.IsNaN(actual[0][c]) && !float.IsInfinity(actual[0][c]) &&
            actual[0][c] <= 65504, "Active HDR output stays finite within the working format");
        t.Near(65504, actual[0].r, .01, "HDR clamp uses the working-format limit");
    }

    static void MasterCorrections(TestContext t, Images images)
    {
        var samples = new[] { Display(0), Display(120, .4f, 2, .25f), Display(240, .8f, 3),
            new Color(.18f, .18f, .18f, .4f), Color.black, Display(200, .001f), Display(280, 1, 5e-6f),
            new Color(-.2f, .3f, 4, .6f) };
        foreach (var settings in new[] { new Vector4(120, 1, 0, 1), new Vector4(37, .3f, 50, 1),
            new Vector4(0, 3, -25, 1), new Vector4(-179, 1.5f, 75, .5f), new Vector4(180, 0, 0, 1),
            new Vector4(70, 2, -100, 1), new Vector4(0, 1, 100, 1) })
        {
            images.Reset(); images.Master(settings.x, settings.y, settings.z); images.Set("_Opacity", settings.w);
            images.Gate(1, 1);
            Same(t, Array.ConvertAll(samples, c => Expected(c, settings.x, settings.y, settings.z, settings.w)), images.Render(samples),
                "Master matches independent correction for gray/weak hues regardless of selection gate " + settings);
        }

        images.Reset(); images.Master(saturation: 0);
        var gray = images.Render(Hues(0, 120, 240));
        float[] luma = { .2126f, .7152f, .0722f };
        for (int n = 0; n < 3; n++)
            Same(t, new[] { new Color(Decode(luma[n]), Decode(luma[n]), Decode(luma[n]), 1) },
                new[] { gray[n] }, "Primary desaturation uses weighted gray, not HSV white");
        images.Master(lightness: 50);
        Same(t, new[] { new Color(Decode(.5f), Decode(.5f), Decode(.5f), 1) },
            images.Render(new[] { Color.black }), "Positive Lightness can lift black unlike a multiplier");
        images.Master(lightness: 100);
        Same(t, new[] { Color.white, Color.white }, images.Render(new[] { Color.black, new Color(4, 2, .5f, 1) }),
            "Positive Lightness reaches reference white, including HDR");
        images.Master(lightness: -100);
        Same(t, new[] { Color.black }, images.Render(new[] { Color.white }), "Negative Lightness reaches black");

        images.Reset(); images.Master(saturation: 0); images.Range(3, center: 240, width: 40, feather: 20);
        var palette = Hues(0, 60, 120, 180, 200, 210, 225, 240, 255, 270, 280, 300);
        var expected = new Color[palette.Length];
        float[] saturation = { 0, 0, 0, 0, 0, .5f, 1, 1, 1, .5f, 0, 0 };
        for (int n = 0; n < palette.Length; n++) expected[n] = Expected(palette[n], 0, saturation[n]);
        Same(t, expected, images.Render(palette), "Desaturate everything except blues, with smooth feather");
        Same(t, Hues(225, 240, 255), images.Render(Hues(225, 240, 255)),
            "Neutral full-weight blue exception preserves original RGBA exactly", exact: true);

        images.Master(hue: 120, saturation: .25f, lightness: -45);
        images.Range(3, center: 240, width: 360);
        var selected = new[] { samples[0], samples[1], samples[2], samples[7] };
        Same(t, selected, images.Render(selected), "Full-spectrum override replaces Master for selected hues", exact: true);
        // Gray/weak hue pixels never select a range and must still use Master.
        Same(t, new[] { Expected(samples[3], 120, .25f, -45), Expected(samples[5], 120, .25f, -45) },
            images.Render(new[] { samples[3], samples[5] }), "Unselected gray and low saturation use Master");

        images.Reset(); images.Master(hue: 120); images.Range(3, center: 240, width: 30);
        Same(t, Hues(120, 240, 240), images.Render(Hues(0, 120, 240)),
            "Ranges select the source hue, before Master hue changes");

        images.Reset(); images.Master(hue: 120, saturation: 0, lightness: -50);
        images.Range(3, center: 240, width: 20, feather: 20, hue: -60, saturation: 1, lightness: 100);
        var edge = Display(260, .8f, 2, .25f);
        var half = Expected(edge, 30, .5f, 25);
        Same(t, new[] { half }, images.Render(new[] { edge }), "Half feather interpolates corrections toward Master");
        images.Set("_Opacity", .4f);
        Same(t, new[] { Color.Lerp(edge, half, .4f) }, images.Render(new[] { edge }),
            "Output Opacity blends after Master and range overrides");

        images.Reset(); images.Master(saturation: 0); images.Range(3, center: 240);
        images.Gate(.5f, .7f);
        var gated = Display(240, .6f);
        Same(t, new[] { Expected(gated, 0, .5f) }, images.Render(new[] { gated }),
            "Half saturation gate blends the exception with Master, not with original");
        var neutralGray = new Color(.18f, .18f, .18f, .5f);
        images.Master(lightness: 50); images.Range(3, lightness: -100);
        Same(t, new[] { Expected(neutralGray, 0, 1, 50) }, images.Render(new[] { neutralGray }),
            "Gray Lightness uses Master and ignores unreliable hue ranges");

        images.Reset(); images.Master(hue: 180, saturation: 0, lightness: -45);
        images.Range(1, hue: 60, saturation: 1.5f, lightness: 50); images.Range(2, hue: -60, saturation: .5f, lightness: -50);
        var colored = Hues(0, 120, 240, 359);
        Same(t, colored, images.Render(colored), "Master is excluded from full overlap averaging", exact: true);
        var beforeSwap = images.Render(samples);
        images.Range(1, hue: -60, saturation: .5f, lightness: -50); images.Range(2, hue: 60, saturation: 1.5f, lightness: 50);
        Same(t, beforeSwap, images.Render(samples), "Range order remains independent with active Master");

        images.Reset(); images.Range(3, center: 240, width: 20, feather: 20);
        for (int output = 1; output <= 5; output++)
        {
            images.Master(); images.Set("_Output", output); var mask = images.Render(samples);
            images.Master(hue: 120, saturation: 0, lightness: 100);
            Same(t, mask, images.Render(samples), "Master never changes source range masks: " + output, exact: true);
        }
        images.Set("_Output", 0);
        var hidden = new Color(-2, 1, 4, 0);
        Same(t, new[] { hidden }, images.Render(new[] { hidden }), "Active Master preserves invisible signed/HDR RGB", exact: true);
        images.Set("_Opacity", 0);
        Same(t, samples, images.Render(samples), "Zero opacity bypasses active Master", exact: true);
    }

    static void MasterFastPath(TestContext t, Images images)
    {
        var samples = new[] { Display(0), Display(120, .4f, 2, .25f), Display(240, .8f, 3),
            new Color(.18f, .18f, .18f, .4f), Color.black, Display(200, .001f), Display(280, 1, 5e-6f),
            new Color(-.2f, .3f, 4, .6f), new Color(-2, 1, 4, 0), new Color(70000, .07f, .4f, .25f) };
        foreach (var settings in new[] { new Vector4(0, 1, 0, 1), new Vector4(37, .3f, 50, 1),
            new Vector4(-179, 1.5f, 75, .5f), new Vector4(180, 0, 0, 1), new Vector4(70, 2, -100, 1),
            new Vector4(0, .0001f, 0, 1), new Vector4(0, 1, .0001f, 1), new Vector4(70, 2, 100, 0) })
        {
            images.Reset(); images.Master(settings.x, settings.y, settings.z); images.Set("_Opacity", settings.w);
            var fast = images.Render(samples);
            images.Range(1, width: 0, feather: 0);
            Same(t, images.Render(samples), fast, "Fast Master matches the unchanged range path with empty coverage " + settings);
            Same(t, new[] { samples[8] }, new[] { fast[8] }, "Fast Master preserves hidden signed RGB exactly", exact: true);
        }
        for (int output = 1; output <= 5; output++)
        foreach (float opacity in new[] { 0f, .4f, 1f })
        {
            images.Reset(); images.Master(120, 0, 100); images.Set("_Output", output); images.Set("_Opacity", opacity);
            var fast = images.Render(samples);
            images.Range(1, width: 0, feather: 0);
            Same(t, images.Render(samples), fast, "No-range mask fast path preserves RGBA and opacity " + output, exact: true);
        }
        images.Reset();
    }

    static void LayerRenderPaths(TestContext t, Images images)
    {
        var document = images.Document;
        var layers = new List<Layer>();
        Layer Add(LayerBehaviour behaviour)
        {
            var layer = new Layer(behaviour);
            typeof(Layer).GetMethod("AssignNewId", Hidden).Invoke(layer, null);
            layers.Add(layer); return layer;
        }
        Color Encoded(Color linear) => new Color(Encode(linear.r), Encode(linear.g), Encode(linear.b), linear.a);
        void Pixel(Texture2D texture, Color expected, string message, bool destroy = true)
        {
            t.True(texture != null, message + " produces an image");
            try
            {
                var actual = texture.GetPixel(texture.width / 2, texture.height / 2);
                for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .008, message + " channel " + c);
            }
            finally { if (destroy && texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
        try
        {
            foreach (bool selective in new[] { false, true })
            foreach (float hue in new[] { 0f, 240f })
            {
                images.Reset(); images.Master(saturation: 0); images.Range(3, enabled: selective, center: 240, width: 40, feather: 20);
                var sourceColor = Display(hue, .8f, .8f);
                var expected = selective && hue == 240 ? sourceColor : Expected(sourceColor, 0, 0);
                var source = Add(new ColorFillLayerBehaviour { color = Encoded(sourceColor) });
                source.fx.Add(images.Selective); document.layers.Clear(); document.layers.Add(source);
                Pixel(document.ComposeCanvas(), expected, "Composite with Master/blue exception " + hue);
                var psd = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels", Hidden)
                    .Invoke(document, new object[] { source, false });
                Pixel(psd, Encoded(expected), "Full-resolution PSD pixel source " + hue);

                source.fx.Clear(); var group = Add(new GroupLayerBehaviour()); group.children.Add(source);
                group.fx.Add(images.Selective); document.layers.Clear(); document.layers.Add(group);
                Pixel(document.ComposeCanvas(), expected, "Group FX " + hue);

                group.children.Clear(); source.enabled = false;
                var target = Add(new BlurLayerBehaviour { radius = 0, inputMode = EffectInputMode.Specific, TargetLayerId = source.Id });
                target.fx.Add(images.Selective); document.layers.Clear(); document.layers.Add(target); document.layers.Add(source);
                Pixel(document.ComposeCanvas(), expected, "Target input FX " + hue);
                typeof(WhimTexDocument).GetMethod("RefreshThumbnailStructure", Hidden).Invoke(document, null);
                var thumbnail = (Texture2D)typeof(WhimTexDocument).GetMethod("GetLayerThumbnail", Hidden)
                    .Invoke(document, new object[] { target, 32, false });
                Pixel(thumbnail, Encoded(expected), "Effect thumbnail " + hue, destroy: false);

                source.enabled = true; var processor = Add(new ShaderProcessorLayerBehaviour());
                processor.fx.Add(images.Selective); document.layers.Clear(); document.layers.Add(processor); document.layers.Add(source);
                Pixel(document.ComposeCanvas(), expected, "Shader Processor FX " + hue);

                var baseLayer = Add(new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .5f) });
                source.clippingMask = true; source.fx.Add(images.Selective);
                document.layers.Clear(); document.layers.Add(source); document.layers.Add(baseLayer);
                expected.a = .5f;
                Pixel(document.ComposeCanvas(), expected, "Clipping retains corrected RGB and base alpha " + hue);
            }
        }
        finally
        {
            document.layers.Clear();
            foreach (var layer in layers) typeof(Layer).GetMethod("ReleaseTransientResources", Hidden).Invoke(layer, null);
        }
    }

    static void StoredValues(TestContext t, Images images)
    {
        images.Reset(); images.Range(4, enabled: false, center: 359, width: 17, feather: 180, hue: -179, saturation: 9, lightness: -75);
        images.Master(hue: 120, saturation: .3f, lightness: 25);
        images.P("_Range1").vectorValue = new Vector4(30.1f - 360, 30.1f, 0, 0);
        images.P("_Range2").vectorValue = new Vector4(330, 690, 0, 0);
        images.Set("_Opacity", .35f); images.Set("_Output", 5);
        images.Render(Hues(359));
        images.Set("_Range4Enabled", 1); images.Set("_Output", 0); images.Render(Hues(359));
        images.Set("_Range4Enabled", 0); images.Set("_Output", 5);
        var assembly = typeof(ShaderFX).Assembly;
        string exported = (string)assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter", true)
            .GetMethod("BuildSource", Hidden).Invoke(null, new object[] { images.Selective, "Color/Hue & Saturation" });
        var parsed = (List<ShaderFXParameter>)assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata", true)
            .GetMethod("Parse", Hidden).Invoke(null, new object[] { exported, true, null });
        Stored(t, images.Parameters, parsed, "Preset export");
        t.True(exported.Replace("\r\n", "\n").StartsWith("// @whimtex-effect Color/Hue & Saturation\n// @control(_Opacity)\n", StringComparison.Ordinal),
            "Preset export retains the opacity header binding");
        t.Equal("_Range4Enabled", parsed.Find(p => p.name == "_Range4Hue").controls[0].enableIf[0].parameter,
            "Preset export retains range availability conditions");
        t.True(!parsed.Find(p => p.name == "_Range4Enabled").controls[0].hidden &&
            string.IsNullOrEmpty(parsed.Find(p => p.name == "_Range4Hue").controls[0].groupHeaderParameter),
            "Preset export retains Enabled without a nested group header");
        t.Equal("_Range4Enabled", parsed.Find(p => p.name == "_Range4Enabled").controls[0].containers[1].headerParameter,
            "Preset export retains the Enabled tab checkbox binding");

        Layer layer = new ColorFillLayerBehaviour();
        typeof(Layer).GetMethod("AssignNewId", Hidden).Invoke(layer, null);
        layer.fx.Add(images.Selective); images.Document.layers.Add(layer);
        try
        {
            var json = WhimTexDocumentJson.Write(images.Document);
            t.Equal(0, json.Warnings.Count, "Portable JSON write retains the complete FX");
            using (var restored = WhimTexDocumentJson.Read(json.Json))
            {
                t.Equal(0, restored.Warnings.Count, "Portable JSON reads the preset without warnings");
                var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden)
                    .GetValue(restored.Document.layers[0].fx[0]);
                Stored(t, images.Parameters, values, "Portable JSON");
            }
        }
        finally { images.Document.layers.Clear(); }
    }

    static void Stored(TestContext t, List<ShaderFXParameter> expected, List<ShaderFXParameter> actual, string message)
    {
        t.Equal(expected.Count, actual.Count, message + " parameter count");
        foreach (var parameter in expected)
        {
            var value = actual.Find(p => p.name == parameter.name);
            t.True(value != null, message + " retains " + parameter.name);
            if (parameter.type != ShaderFXParameterType.Vector2)
                t.Near(parameter.floatValue, value.floatValue, 0, message + " retains the stored value of " + parameter.name);
            t.Equal(parameter.vectorValue, value.vectorValue, message + " retains endpoints of " + parameter.name);
        }
    }

    // Independent display/linear conversion and Unity's HSV functions provide expected colors.
    static float Decode(float value) => value <= .04045f ? value / 12.92f : (float)Math.Pow((value + .055) / 1.055, 2.4);
    static float Encode(float value) => value <= .0031308f ? value * 12.92f : (float)(1.055 * Math.Pow(value, 1 / 2.4) - .055);
    static Color Display(float degrees, float saturation = 1, float value = 1, float alpha = 1)
    {
        Color color = Color.HSVToRGB(Mathf.Repeat(degrees / 360, 1), saturation, value, true);
        return new Color(Decode(color.r), Decode(color.g), Decode(color.b), alpha);
    }
    static Color[] Hues(params float[] degrees)
    {
        var result = new Color[degrees.Length];
        for (int n = 0; n < degrees.Length; n++) result[n] = Display(degrees[n]);
        return result;
    }
    static Color Expected(Color input, float hue, float saturation = 1, float lightness = 0, float opacity = 1)
    {
        if (input.a <= 0 || opacity == 0 || (hue == 0 && saturation == 1 && lightness == 0)) return input;
        var display = new Color(Encode(Mathf.Max(input.r, 0)), Encode(Mathf.Max(input.g, 0)), Encode(Mathf.Max(input.b, 0)));
        if (hue != 0)
        {
            Color.RGBToHSV(display, out float h, out float s, out float v);
            display = Color.HSVToRGB(Mathf.Repeat(h + hue / 360, 1), s, v, true);
        }
        double gray = .2126 * display.r + .7152 * display.g + .0722 * display.b;
        var corrected = input;
        double lift = Math.Max(-1, Math.Min(1, lightness * .01));
        for (int c = 0; c < 3; c++)
        {
            double channel = Math.Max(0, gray + (display[c] - gray) * Math.Max(0, saturation));
            channel = lift < 0 ? channel * (1 + lift) : channel + (1 - channel) * lift;
            corrected[c] = Math.Min(65504, Decode((float)channel));
        }
        return Color.Lerp(input, corrected, opacity);
    }
    static void Mask(TestContext t, Color[] actual, float[] weights, string message)
    {
        var expected = new Color[weights.Length];
        for (int n = 0; n < weights.Length; n++) expected[n] = new Color(weights[n], weights[n], weights[n], 1);
        Same(t, expected, actual, message);
    }
    static void Same(TestContext t, Color[] expected, Color[] actual, string message, bool exact = false)
    {
        t.Equal(expected.Length, actual.Length, message + " pixel count");
        for (int n = 0; n < expected.Length; n++) for (int c = 0; c < 4; c++)
            t.Near(expected[n][c], actual[n][c], exact ? 0 : Math.Max(.0005, Math.Abs(expected[n][c]) * .003),
                message + ": pixel " + n + ", channel " + c);
    }

    sealed class Images : IDisposable
    {
        const int Width = 384;
        readonly TestContext context;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly RenderTexture previous = RenderTexture.active;
        readonly bool previousSrgb = GL.sRGBWrite;
        readonly Dictionary<string, float> defaults = new Dictionary<string, float>();
        readonly Dictionary<string, Vector4> vectorDefaults = new Dictionary<string, Vector4>();
        RenderTexture target;
        Texture2D input, readback;
        object renderContext;
        public WhimTexDocument Document { get; private set; }
        public ShaderFX Selective { get; private set; }
        public Shader InitialShader { get; private set; }
        public List<ShaderFXParameter> Parameters { get; private set; }

        public Images(TestContext context)
        {
            this.context = context;
            try
            {
                Document = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
                Document.width = Width; Document.height = 1;
                Selective = Catalog(PresetPath, "Color/Hue & Saturation");
                Parameters = Values(Selective);
                foreach (var parameter in Parameters) { defaults.Add(parameter.name, parameter.floatValue); vectorDefaults.Add(parameter.name, parameter.vectorValue); }
                InitialShader = (Shader)typeof(ShaderFX).GetField("compiledShader", Hidden).GetValue(Selective);
                context.True(InitialShader != null && !ShaderUtil.ShaderHasError(InitialShader), "Selective HSV compiles");
                input = Own(new Texture2D(Width, 1, TextureFormat.RGBAFloat, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
                readback = Own(new Texture2D(Width, 1, TextureFormat.RGBAFloat, false, true));
                target = Own(new RenderTexture(Width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                context.True(target.Create(), "Owned float render target is created");
                renderContext = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true),
                    Document, null, Width, 1, 1f, true, true, null);
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Selective HSV setup and cleanup failed", primary, cleanup); }
                throw;
            }
        }
        T Own<T>(T value) where T : UnityEngine.Object
        {
            owned.Add(value); value.hideFlags = HideFlags.HideAndDontSave; return value;
        }
        ShaderFX Catalog(string path, string menu)
        {
            object selected = null;
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            foreach (var entry in (IEnumerable)catalog.GetMethod("GetEntries", Hidden).Invoke(null, null))
                if ((string)entry.GetType().GetField("path", Hidden).GetValue(entry) == path &&
                    (string)entry.GetType().GetField("menuPath", Hidden).GetValue(entry) == menu) selected = entry;
            context.True(selected != null, "Imported built-in preset is discovered: " + menu);
            return Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", Hidden).Invoke(null, new[] { (object)Document, selected }));
        }
        static List<ShaderFXParameter> Values(ShaderFX fx)
            => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden).GetValue(fx);
        public ShaderFXParameter P(string name)
            => Parameters.Find(p => p.name == name) ?? throw new InvalidOperationException("Missing parameter: " + name);
        public void Set(string name, float value) => P(name).floatValue = value;
        public void Gate(float start, float end) => P("_SaturationRange").vectorValue = new Vector4(start, end, 0, 0);
        public void Reset() { foreach (var parameter in Parameters) { parameter.floatValue = defaults[parameter.name]; parameter.vectorValue = vectorDefaults[parameter.name]; } }
        public void Master(float hue = 0, float saturation = 1, float lightness = 0)
        { Set("_MasterHue", hue); Set("_MasterSaturation", saturation); Set("_MasterLightness", lightness); }
        public void Range(int number, bool enabled = true, float center = 0, float width = 360, float feather = 0,
            float hue = 0, float saturation = 1, float lightness = 0)
        {
            string prefix = "_Range" + number;
            Set(prefix + "Enabled", enabled ? 1 : 0);
            P(prefix).vectorValue = width >= 360 ? new Vector4(0, 360, 0, 0) :
                new Vector4(Mathf.Repeat(center - width * .5f, 360), Mathf.Repeat(center + width * .5f, 360), 0, 0);
            Set(prefix + "Feather", feather); Set(prefix + "Hue", hue);
            Set(prefix + "Saturation", saturation); Set(prefix + "Lightness", lightness);
        }
        public Color[] Render(Color[] pixels, ShaderFX effect = null)
        {
            context.True(pixels.Length > 0 && pixels.Length <= Width, "Input fits the owned strip");
            var buffer = new Color[Width]; Array.Copy(pixels, buffer, pixels.Length);
            input.SetPixels(buffer); input.Apply();
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", Hidden)
                    .Invoke(effect != null ? effect : Selective, new[] { renderContext });
                context.True(material != null, "FX has a usable material");
                GL.sRGBWrite = false; Graphics.Blit(input, target, material); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, 1), 0, 0); readback.Apply();
                var result = new Color[pixels.Length]; Array.Copy(readback.GetPixels(), result, result.Length); return result;
            }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        public void Dispose()
        {
            var failures = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
            Attempt(() => RenderTexture.active = previous); Attempt(() => GL.sRGBWrite = previousSrgb);
            if (target != null) Attempt(() => target.Release());
            for (int n = owned.Count - 1; n >= 0; n--)
            {
                var value = owned[n];
                if (value != null) Attempt(() => UnityEngine.Object.DestroyImmediate(value));
            }
            owned.Clear();
            if (failures.Count != 0) throw new AggregateException("Selective HSV owned resource cleanup failed", failures);
        }
    }
}
