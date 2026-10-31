using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class BuiltinFXInspectorTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Presets = "Packages/com.dcfapixels.whimtex/src/FXPresets/";
    static readonly string[] Names = { "NormalLighting", "Halftone", "DigitalGlitch", "BevelEmboss", "ChromaKey", "Pixelate", "Threshold", "ColorFilter" };
    static Type Type(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static List<ShaderFXParameter> Values(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
    static ShaderFXParameter P(ShaderFX fx, string name) => Values(fx).Single(p => p.name == name);
    static void Refresh(VisualElement view) => view.GetType().GetMethod("Refresh", F).Invoke(view, null);
    static void Preserve(ShaderFX next, ShaderFX previous) => Type("ShaderFXMetadata").GetMethod("PreserveValues", F).Invoke(null, new object[] { Values(next), Values(previous) });

    sealed class Fixture : IDisposable
    {
        public readonly WhimTexDocument doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        readonly List<ShaderFX> effects = new List<ShaderFX>();
        readonly RenderTexture active = RenderTexture.active;
        readonly bool srgb = GL.sRGBWrite;
        public Fixture() { doc.width = doc.height = 64; }
        public ShaderFX Create(string code)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() });
            effects.Add(fx);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            return fx;
        }
        public ShaderFX Preset(string name) => Create(File.ReadAllText(Presets + name + ".hlsl"));
        public void Dispose()
        {
            doc.layers.Clear();
            foreach (var effect in effects) UnityEngine.Object.DestroyImmediate(effect);
            UnityEngine.Object.DestroyImmediate(doc);
            RenderTexture.active = active; GL.sRGBWrite = srgb;
        }
    }

    public static string Run() => TestContext.Run("Built-in FX inspector behavior", t =>
    {
        using var f = new Fixture();
        VisualElement View(ShaderFX fx) => (VisualElement)Activator.CreateInstance(Type("ShaderFXParameterView"), F, null, new object[] { fx }, null);
        bool Enabled(VisualElement view, string label) => view.Query<Label>().ToList().First(l => l.text == label).enabledInHierarchy;
        foreach (string name in Names)
        {
            var fx = f.Preset(name);
            t.Equal("Applied successfully.", (string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(fx), name + " compiles without warnings");
        }
        var normal = f.Preset("NormalLighting"); var normalView = View(normal);
        P(normal, "_Output").floatValue = 3; P(normal, "_SpecularEnabled").floatValue = 1; Refresh(normalView);
        t.True(!Enabled(normalView, "Intensity") && !Enabled(normalView, "Ambient") && Enabled(normalView, "Specular Intensity"), "Specular Only disables diffuse settings but keeps specular active");
        P(normal, "_Output").floatValue = 2; Refresh(normalView);
        t.True(Enabled(normalView, "Intensity") && !Enabled(normalView, "Specular Intensity"), "Shadow Only skips specular settings");
        P(normal, "_BaseColor").colorValue = new Color(.2f, .3f, .4f, .5f); Refresh(normalView);
        t.True(Enabled(normalView, "Ambient"), "Filled surface enables ambient");
        var bevel = f.Preset("BevelEmboss"); var bevelView = View(bevel);
        t.Equal(0, bevelView.Query<Foldout>().ToList().Count, "Bevel adds no foldouts");
        t.Equal(3, bevelView.Query<Label>(className: "whimtex-fx-parameter-group-title").ToList().Count, "Bevel keeps three open sections");
        t.True(!Enabled(bevelView, "Ambient") && Enabled(bevelView, "Output"), "Lighting-only mode retains output and disables ambient");
        P(bevel, "_BaseColor").colorValue = Color.white; Refresh(bevelView);
        t.True(Enabled(bevelView, "Ambient") && !Enabled(bevelView, "Output"), "Filled surface ignores output selector");
        var half = f.Preset("Halftone");
        var glitch = f.Preset("DigitalGlitch"); var glitchView = View(glitch);
        P(glitch, "_BlockDensity").floatValue = P(glitch, "_TearDensity").floatValue = 0; Refresh(glitchView);
        t.True(Enabled(glitchView, "Tear Jitter") && Enabled(glitchView, "Tear Line Height") && Enabled(glitchView, "Block Width"), "Shared grid and jitter remain editable at zero corruption density");
        t.True(!Enabled(glitchView, "Tear Shift") && !Enabled(glitchView, "Block Shift X"), "Inactive displacement settings stay visible and disabled");
        var chroma = f.Preset("ChromaKey"); var chromaView = View(chroma);
        P(chroma, "_Despill").floatValue = .5f; P(chroma, "_Output").floatValue = 1; Refresh(chromaView);
        t.True(!Enabled(chromaView, "Despill") && !Enabled(chromaView, "Spill Range"), "Matte output disables color spill controls");
        foreach (var fx in new[] { half, chroma, f.Preset("Threshold"), f.Preset("Pixelate") })
            foreach (var p in Values(fx).Where(p => p.type == ShaderFXParameterType.Color))
                t.Equal("float3", p.valueType, p.name + " exposes RGB only");
        var filter = f.Preset("ColorFilter");
        t.Equal("float4", P(filter, "_FilterColor").valueType, "Color Filter retains meaningful alpha");
        t.True(!P(filter, "_FilterColor").controls[0].tooltip.Contains("ignored"), "Color Filter tooltip describes active alpha");
        var rgba = f.Create("// @param color _Tint = (1, 1, 1, 1)\nfloat4 ApplyFX(float4 c) { return float4(_Tint.rgb, c.a); }");
        var rgb = f.Create("// @param color(float3) _Tint = (0, 0, 0)\nfloat4 ApplyFX(float4 c) { return float4(_Tint.rgb, c.a); }");
        P(rgba, "_Tint").colorValue = new Color(2.5f, .25f, -.1f, .6f);
        Preserve(rgb, rgba);
        t.Equal(P(rgba, "_Tint").id, P(rgb, "_Tint").id, "Removing unused alpha retains color identity");
        for (int c = 0; c < 3; c++) t.Near(P(rgba, "_Tint").colorValue[c], P(rgb, "_Tint").colorValue[c], 0, "Removing unused alpha retains HDR RGB");
    });

    public static string Compare(string baselineDirectory) => TestContext.Run("FX output before/after inspector edits", t =>
    {
        using var f = new Fixture();
        Layer host = new ColorFillLayerBehaviour(); host.colorRange = LayerColorRange.HDR; f.doc.layers.Add(host);
        var source = f.Create("float4 ApplyFX(PinnedUV p, float4 c) { float2 uv=p; return float4(uv.x*2.7-.2, .5+.65*sin(uv.y*24), uv.y*1.8, uv.x<.15?0:.3+.7*uv.y); }");
        Color[] Render(ShaderFX fx)
        {
            typeof(ShaderFX).GetMethod("NotifyValuesChanged", F).Invoke(fx, null);
            host.fx.Clear(); host.fx.Add(source); host.fx.Add(fx);
            var image = f.doc.ComposeCanvas();
            try { return image.GetPixels(); } finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        foreach (string name in Names)
        {
            string beforeCode = File.ReadAllText(Path.Combine(baselineDirectory, name + ".hlsl"));
            string afterCode = File.ReadAllText(Presets + name + ".hlsl");
            string Body(string code) => Regex.Replace(code.Replace("\r\n", "\n"), @"(?m)^//\s*@[^\n]*\n", "");
            t.Equal(Body(beforeCode), Body(afterCode), name + " executable HLSL is unchanged");
            var before = f.Create(beforeCode); var after = f.Create(afterCode);
            for (int scenario = 0; scenario < 7; scenario++)
            {
                foreach (var p in Values(before))
                {
                    if (scenario > 0 && p.type == ShaderFXParameterType.Color) p.colorValue = new Color(.23f + scenario * .3f, .7f, 1.4f, scenario % 3 * .5f);
                    if (p.name == "_Output") p.floatValue = scenario % (name == "NormalLighting" ? 4 : name == "ChromaKey" ? 2 : 3);
                    if (p.name == "_Mode") p.floatValue = scenario % 6;
                    if (p.name == "_ColorMode") p.floatValue = scenario % 2;
                    if (p.name == "_SpecularEnabled") p.floatValue = scenario % 2;
                    if (p.name == "_FixedShape") p.floatValue = scenario % 2;
                    if (p.name == "_PlateSpread") p.floatValue = scenario * 2;
                    if (p.name == "_PlateRotation") p.floatValue = scenario * 13;
                    if (p.name == "_BlockDensity" || p.name == "_TearDensity") p.floatValue = scenario % 2 == 0 ? 0 : .25f;
                    if (p.name == "_GradientDensity" || p.name == "_Despill") p.floatValue = scenario % 2 == 0 ? 0 : .7f;
                }
                Preserve(after, before);
                var a = Render(before); var b = Render(after);
                double maximum = 0;
                for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++) maximum = Math.Max(maximum, Math.Abs((double)a[i][c] - b[i][c]));
                t.Near(0, maximum, 0, name + " exact RGBA parity, scenario " + scenario);
            }
        }
    });
}
