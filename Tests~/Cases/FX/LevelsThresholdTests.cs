using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class LevelsThresholdTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly string[] Curves = { "_Curve", "_RedCurve", "_GreenCurve", "_BlueCurve", "_AlphaCurve" };

    public static string Run() => TestContext.Run("Levels and Threshold channels", t =>
    {
        using var fixture = new MigrationD();
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.width = doc.height = 8;
        doc.outputSrgb = false;
        var effects = new List<ShaderFX>();
        var input = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        var output = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var renderContext = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
            doc, null, 8, 8, 1f, true, true, null);
        var viewType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
        ShaderFX Create(string code)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() });
            effects.Add(fx);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            return fx;
        }
        List<ShaderFXParameter> Parameters(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
        ShaderFXParameter P(ShaderFX fx, string name) => Parameters(fx).Single(p => p.name == name);
        Color Render(ShaderFX fx, Color source)
        {
            var active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                input.SetPixels(Enumerable.Repeat(source, 64).ToArray()); input.Apply();
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(fx, new[] { renderContext });
                t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "FX compiles without shader errors");
                GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                RenderTexture.active = output;
                read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0); read.Apply();
                return read.GetPixel(4, 4);
            }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        void Near(Color expected, Color actual, string message)
        {
            for (int c = 0; c < 4; c++) t.Near(expected[c], actual[c], .002, message + " channel " + c);
        }
        void ResetLevels(ShaderFX fx)
        {
            foreach (string name in Curves) P(fx, name).curveValue = AnimationCurve.Linear(0, 0, 1, 1);
            P(fx, "_InBlack").floatValue = P(fx, "_OutBlack").floatValue = 0;
            P(fx, "_InWhite").floatValue = P(fx, "_OutWhite").floatValue = P(fx, "_Gamma").floatValue = 1;
            P(fx, "_Opacity").floatValue = P(fx, "_PreserveColor").floatValue = 1;
        }
        try
        {
            var levels = Create(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Levels.hlsl"));
            var source = new Color(.2f, .4f, .7f, .3f);
            foreach (string name in Curves)
            {
                t.Equal(ShaderFXParameterType.Curve, P(levels, name).type, name + " is a declared curve");
                t.Near(.37, P(levels, name).curveValue.Evaluate(.37f), .00001, name + " defaults to linear");
            }
            t.Equal("RGB,R,G,B,Alpha", string.Join(",", P(levels, "_CurveChannel").controls[0].optionNames), "Curve selector choices");
            Near(source, Render(levels, source), "Neutral Levels");
            var shader = typeof(ShaderFX).GetField("compiledShader", F).GetValue(levels);
            for (int c = 0; c < 4; c++)
            {
                ResetLevels(levels);
                P(levels, Curves[c + 1]).curveValue = AnimationCurve.Linear(0, 1, 1, 0);
                var expected = source; expected[c] = 1 - source[c];
                Near(expected, Render(levels, source), "Independent " + Curves[c + 1]);
            }
            ResetLevels(levels);
            P(levels, "_RedCurve").curveValue = AnimationCurve.Linear(0, 1, 1, 0);
            P(levels, "_GreenCurve").curveValue = AnimationCurve.Linear(0, .1f, 1, .6f);
            P(levels, "_BlueCurve").curveValue = AnimationCurve.Linear(0, .25f, 1, .25f);
            P(levels, "_AlphaCurve").curveValue = AnimationCurve.Linear(0, 1, 1, 0);
            var corrected = new Color(.8f, .3f, .25f, .7f);
            Near(corrected, Render(levels, source), "Four channel curves act together");
            var view = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { levels }, null);
            var selector = view.Q<DropdownField>();
            t.True(selector != null && selector.choices.Count == 5, "Compact curve selector in standard FX view");
            for (int c = 0; c < 5; c++)
            {
                // Detached fields do not emit value-change events; refresh from the model.
                P(levels, "_CurveChannel").floatValue = c;
                viewType.GetMethod("Refresh", F).Invoke(view, null);
                t.Equal(selector.choices[c], selector.value, "UI displays the selected curve");
                var fields = view.Query<CurveField>().ToList();
                t.Equal(5, fields.Count, "All curve fields retain their values");
                for (int i = 0; i < 5; i++)
                    t.Equal(i == c ? DisplayStyle.Flex : DisplayStyle.None, fields[i].parent.style.display.value,
                        "Only selected curve is shown");
                Near(corrected, Render(levels, source), "Curve selector does not disable other curves");
            }
            foreach (float opacity in new[] { 0f, .5f, 1f })
            {
                P(levels, "_Opacity").floatValue = opacity;
                Near(Color.LerpUnclamped(source, corrected, opacity), Render(levels, source), "Opacity includes alpha");
            }
            t.True(ReferenceEquals(shader, typeof(ShaderFX).GetField("compiledShader", F).GetValue(levels)), "Curve edits do not recompile shaders");

            ResetLevels(levels); P(levels, "_PreserveColor").floatValue = 0;
            P(levels, "_Gamma").floatValue = 2;
            P(levels, "_Curve").curveValue = AnimationCurve.Linear(0, 1, 1, 0);
            P(levels, "_OutBlack").floatValue = .2f; P(levels, "_OutWhite").floatValue = .8f;
            P(levels, "_RedCurve").curveValue = AnimationCurve.Linear(0, 0, 1, .5f);
            Near(new Color((.2f + .6f * (1 - Mathf.Sqrt(.2f))) * .5f,
                .2f + .6f * (1 - Mathf.Sqrt(.4f)), .2f + .6f * (1 - Mathf.Sqrt(.7f)), .3f),
                Render(levels, source), "Input/Gamma/shared curve/output/channel order; alpha independent");
            ResetLevels(levels); P(levels, "_PreserveColor").floatValue = 0;
            P(levels, "_OutWhite").floatValue = 4;
            Near(new Color(.8f, 1.6f, 2.8f, .3f), Render(levels, source), "Neutral channel curves keep HDR output");
            P(levels, "_RedCurve").curveValue = AnimationCurve.Linear(0, 0, 1, .5f);
            Near(new Color(1.5f, 4, 4, .3f), Render(levels, new Color(.5f, 1, 2, .3f)), "HDR keeps excess above curve endpoint");
            ResetLevels(levels);
            P(levels, "_AlphaCurve").curveValue = AnimationCurve.Linear(0, -2, 1, 3);
            t.Near(0, Render(levels, new Color(.2f, .4f, .7f, .1f)).a, .002, "Alpha curve lower clamp");
            t.Near(1, Render(levels, new Color(.2f, .4f, .7f, .9f)).a, .002, "Alpha curve upper clamp");

            var threshold = Create(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Threshold.hlsl"));
            t.Equal("Luminance,R,G,B,Alpha", string.Join(",", P(threshold, "_SourceChannel").controls[0].optionNames), "Threshold choices");
            t.True(Parameters(threshold).All(p => p.name != "_UseAlpha"), "No superseded source toggle");
            var low = new Color(.1f, .2f, .3f, .05f); var high = new Color(.8f, .6f, 1.5f, .95f);
            P(threshold, "_LowColor").colorValue = low; P(threshold, "_HighColor").colorValue = high;
            // Color parameters are display-encoded, unlike the linear source pixels.
            float Decode(float value) => value <= .04045f ? value / 12.92f : Mathf.Pow((value + .055f) / 1.055f, 2.4f);
            var linearLow = new Color(Decode(low.r), Decode(low.g), Decode(low.b), low.a);
            var linearHigh = new Color(Decode(high.r), Decode(high.g), Decode(high.b), high.a);
            foreach (Color sample in new[] { source, new Color(-.2f, .8f, .1f, .6f), new Color(2, 1.5f, .5f, .37f) })
            {
                for (int c = 0; c < 5; c++)
                {
                    P(threshold, "_SourceChannel").floatValue = c;
                    float value = c == 0 ? sample.r * .2126f + sample.g * .7152f + sample.b * .0722f : sample[c - 1];
                    foreach (float width in new[] { 0f, .2f })
                    {
                        P(threshold, "_Threshold").floatValue = .5f; P(threshold, "_Smooth").floatValue = width;
                        float blend = width == 0 ? (value >= .5f ? 1 : 0) : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .7f, value));
                        var expected = Color.LerpUnclamped(linearLow, linearHigh, blend); expected.a = sample.a;
                        Near(expected, Render(threshold, sample), "Threshold channel " + c + " width " + width);
                    }
                }
            }
            P(threshold, "_SourceChannel").floatValue = 1;
            P(threshold, "_Smooth").floatValue = 0; P(threshold, "_Threshold").floatValue = 1.5f;
            var hdrHigh = linearHigh; hdrHigh.a = .37f;
            Near(hdrHigh, Render(threshold, new Color(1.5f, 0, 0, .37f)), "HDR threshold equality selects high");
            var thresholdView = (VisualElement)Activator.CreateInstance(viewType, F, null, new object[] { threshold }, null);
            var sourceSelector = thresholdView.Q<DropdownField>();
            t.Equal("Source Channel", sourceSelector.label, "Source selection uses standard labelled field");
            P(threshold, "_SourceChannel").floatValue = 4;
            viewType.GetMethod("Refresh", F).Invoke(thresholdView, null);
            t.Equal(sourceSelector.choices[4], sourceSelector.value, "Threshold selector displays Alpha mode");

            ResetLevels(levels);
            foreach (string name in Curves) P(levels, name).curveValue = AnimationCurve.Linear(0, .2f, 1, .8f);
            P(levels, "_CurveChannel").floatValue = 4;
            Layer layer = new ColorFillLayerBehaviour(); doc.layers.Add(layer); layer.fx.Add(levels); layer.fx.Add(threshold);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                var saved = WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode });
                t.Equal(0, saved.Warnings.Count, "JSON writes without warnings: " + mode);
                using var restored = WhimTexDocumentJson.Read(saved.Json);
                t.Equal(0, restored.Warnings.Count, "JSON reads without warnings: " + mode);
                var restoredLevels = (ShaderFX)restored.Document.layers[0].fx[0];
                foreach (string name in Curves)
                    t.Near(.35, P(restoredLevels, name).curveValue.Evaluate(.25f), .00001, "JSON keeps nonselected " + name + ": " + mode);
                t.Near(4, P(restoredLevels, "_CurveChannel").floatValue, 0, "JSON keeps editor selection");
                var restoredThreshold = (ShaderFX)restored.Document.layers[0].fx[1];
                t.Near(4, P(restoredThreshold, "_SourceChannel").floatValue, 0, "JSON keeps Threshold source");
                Near(Render(levels, source), Render(restoredLevels, source), "Levels JSON GPU parity: " + mode);
                Near(Render(threshold, source), Render(restoredThreshold, source), "Threshold JSON GPU parity: " + mode);
                var composed = restored.Document.ComposeCanvas();
                try { t.True(composed != null && composed.width == 8, "Restored FX compose through document pipeline"); }
                finally { UnityEngine.Object.DestroyImmediate(composed); }
            }
            var writer = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            foreach (var fx in new[] { levels, threshold })
            {
                string code = (string)writer.GetMethod("BuildSource", F).Invoke(null, new object[] { fx, "Tests/Tone Channels" });
                var restored = Create(code);
                Near(Render(fx, source), Render(restored, source), "Saved HLSL preset GPU parity");
                if (fx == levels)
                    foreach (string name in Curves)
                        t.Equal("_CurveChannel", P(restored, name).controls[0].visibleIfParameter, "Preset keeps curve conditions");
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(read);
            foreach (var fx in effects) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc);
        }
    });
}
