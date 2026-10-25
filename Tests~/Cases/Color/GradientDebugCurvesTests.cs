using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class GradientDebugCurvesTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Read(object window, string name) => window.GetType().GetField(name, Flags).GetValue(window);
    static void Write(object window, string name, object value) => window.GetType().GetField(name, Flags).SetValue(window, value);
    static void Refresh(object window) => window.GetType().GetMethod("Refresh", Flags).Invoke(window, null);
    static void Toggle(object window, int channel) => window.GetType().GetMethod("ToggleDebugCurve", Flags).Invoke(window, new object[] { channel });

    public static string Run() => TestContext.Run("GradientDebugCurvesTests.Run", context =>
    {
        var window = ScriptableObject.CreateInstance<WhimTexGradientWindow>();
        try
        {
            window.CreateGUI();
            var root = window.rootVisualElement;
            var target = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            bool enabled = Array.IndexOf(PlayerSettings.GetScriptingDefineSymbols(target).Split(';'), "WHIMTEX_DEEP_DEBUG") >= 0;
            context.Equal(enabled, root.Q("gradientDebugCurves") != null, "Compile symbol controls graph presence");
            context.Equal(enabled, typeof(WhimTexGradientWindow).GetField("debugCurveValues", Flags) != null,
                "Debug allocations are compiled out without the symbol");
            if (!enabled) return;

            var gradient = new WhimTexGradient { ColorSpace = ColorSpace.Linear, ColorMode = WhimTexGradientColorMode.Linear };
            gradient.SetKeys(new[] {
                new GradientColorKey(new Color(8, -2, 4, 1), 0),
                new GradientColorKey(new Color(1, .5f, .25f, 1), .37f),
                new GradientColorKey(new Color(0, 1, 0, 1), 1)
            }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .61f) });
            Write(window, "gradient", gradient);
            Refresh(window);
            var times = (List<float>)Read(window, "debugCurveTimes");
            var values = (List<Color>)Read(window, "debugCurveValues");
            context.Equal(8f, values[0].r, "Transparent HDR RGB is not premultiplied or clipped");
            context.Equal(0f, values[0].a, "Alpha remains independent");
            context.True((float)Read(window, "debugCurveMax") >= 8, "HDR range included");
            context.True((float)Read(window, "debugCurveMin") <= -2, "Negative range included");
            context.True(times.Contains(.37f) && times.Contains(.61f), "Both tracks include exact key positions");
            string before = JsonUtility.ToJson(gradient);
            Write(window, "exposure", -5f);
            Refresh(window);
            context.Equal(8f, values[0].r, "Exposure does not change raw graph samples");
            for (int channel = 0; channel < 3; channel++) Toggle(window, channel);
            context.Equal(8, (int)Read(window, "debugCurveChannels"), "Per-channel buttons hide RGB independently");
            context.Equal(1f, (float)Read(window, "debugCurveMax"), "Alpha-only scale");
            context.Equal(0f, (float)Read(window, "debugCurveMin"), "Hidden negative RGB does not affect scale");
            Toggle(window, 3);
            context.Equal(0, (int)Read(window, "debugCurveChannels"), "All curves can be hidden");
            context.Equal(before, JsonUtility.ToJson(gradient), "Debug controls do not modify gradient data");
            for (int channel = 0; channel < 4; channel++)
            {
                var button = root.Q<Button>("gradientCurve" + "RGBA"[channel]);
                context.True(button != null && button.enabledSelf, "Channel button exists and is enabled");
                Toggle(window, channel);
            }

            foreach (var mode in new[] {
                (color: WhimTexGradientColorMode.Gamma, interpolation: WhimTexGradientInterpolation.Fixed),
                (color: WhimTexGradientColorMode.Gamma, interpolation: WhimTexGradientInterpolation.Balanced),
                (color: WhimTexGradientColorMode.Linear, interpolation: WhimTexGradientInterpolation.Balanced),
                (color: WhimTexGradientColorMode.Perceptual, interpolation: WhimTexGradientInterpolation.Balanced)
            })
            {
                gradient.ColorMode = mode.color; gradient.Interpolation = mode.interpolation;
                gradient.Smoothness = .73f;
                Refresh(window);
                context.True(ReferenceEquals(values, Read(window, "debugCurveValues")), "Sample buffer reused");
                for (int i = 0; i < times.Count; i++)
                    context.Equal(gradient.Evaluate(times[i]), values[i], "Current interpolation samples " + mode);
            }
            gradient.Interpolation = WhimTexGradientInterpolation.Fixed;
            Refresh(window);
            int atKey = times.IndexOf(.37f);
            context.Equal(.37f, times[atKey], "Fixed step exact location");
            context.True(times[atKey + 1] > .37f && times[atKey + 1] - .37f < 1e-6f,
                "Fixed jump represented immediately after its key");
            context.True(values[atKey] != values[atKey + 1], "Fixed discontinuity is retained");
            foreach (WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
            {
                gradient.WrapMode = wrap;
                Refresh(window);
                context.Equal(gradient.Evaluate(1), values[values.Count - 1], "Wrap endpoint " + wrap);
            }
            context.True(root.Q<Button>("gradientCurveR").ClassListContains("whimtex-gradient-curve-toggle--active"),
                "Enabled channel has selected styling");
        }
        finally { Undo.ClearUndo(window); UnityEngine.Object.DestroyImmediate(window); }
    });
}
