using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class PointLightDirectionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, F).Invoke(owner, args);
    static ShaderParameter P(ShaderFX fx, string name) => ((List<ShaderParameter>)typeof(ShaderFX).GetProperty("parameters", F).GetValue(fx)).Find(p => p.name == name);
    static Color[] Read(RenderTexture rt)
    {
        var active = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
        finally { RenderTexture.active = active; Object.DestroyImmediate(cpu); }
    }
    static Color[] Full(WhimTexDocument doc)
    {
        var cpu = doc.ComposeCanvas();
        try { return cpu.GetPixels(); } finally { Object.DestroyImmediate(cpu); }
    }
    static Color[] Region(WhimTexDocument doc, IDisposable cache, RectInt rect)
    {
        var region = Activator.CreateInstance(T("CanvasRenderRegion"), F, null, new object[] { rect, 1 }, null);
        var rt = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, false, null);
        try { return Read(rt); } finally { RenderTexture.ReleaseTemporary(rt); }
    }

    public static string Run() => TestContext.Run("Point/Directional light direction and normal-map coordinate audit", t =>
    {
        var owned = new List<Object>();
        TItem Own<TItem>(TItem value) where TItem : Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        using var cache = (IDisposable)Activator.CreateInstance(T("EffectRenderCache"), true);
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = 64; doc.height = 48;
            ShaderFX FX(string source)
            {
                var fx = Own((ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                    new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null)
                    .Invoke(null, new object[] { doc, source, new List<ShaderParameter>() }));
                Call(fx, "CompileAgentDraft");
                t.Equal("Compiled successfully.", (string)typeof(ShaderFX).GetProperty("Diagnostics", F).GetValue(fx), "Shader compiles");
                return fx;
            }
            var lighting = FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"));
            P(lighting, "_LightingMode").floatValue = 1;
            P(lighting, "_BaseColor").colorValue = Color.white;
            P(lighting, "_ShadowColor").colorValue = Color.black;
            P(lighting, "_Ambient").floatValue = 0;
            P(lighting, "_Roughness").floatValue = .4f;
            var height = FX("float4 ApplyFX(PinnedUV uv,float4 c){float2 p=((float2)uv*float2(64,48)-float2(27,21))/32; float h=.8-.3*p.x*p.x-.2*p.y*p.y; return float4(h,h,h,1);}");
            var source = new Layer(new ColorFillLayerBehaviour()) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            source.fx.Add(height);
            var normal = new NormalMapLayerBehaviour { inputSpace = NormalMapLayerBehaviour.InputSpace.Linear, smoothing = 0, strength = 64 };
            var layer = new Layer(normal) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            doc.layers.Add(layer); doc.layers.Add(source);
            var light = doc.AddLight(WhimTexLightType.Point); light.range = 160; light.falloff = 1.3f;
            light.color = new Color(3, 2, .5f); light.intensity = 2;
            foreach (bool gamma in new[] { false, true })
            {
                normal.encoding = gamma ? NormalMapLayerBehaviour.OutputEncoding.Gamma : NormalMapLayerBehaviour.OutputEncoding.Linear;
                P(lighting, "_InputEncoding").floatValue = gamma ? 1 : 0;
                var packed = Full(doc);
                Vector3 Decode(Color c)
                {
                    if (gamma) c = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b));
                    return new Vector3(c.r * 2 - 1, c.g * 2 - 1, c.b * 2 - 1).normalized;
                }
                var left = Decode(packed[21 * 64 + 12]); var top = Decode(packed[35 * 64 + 27]);
                t.True(left.x < -.3f && top.y > .3f, "Convex height normal points outward, encoding " + gamma);
                layer.fx.Add(lighting);
                foreach (var type in new[] { WhimTexLightType.Point, WhimTexLightType.Directional })
                foreach (var position in new[] { new Vector3(-12, 63, 24), new Vector3(75, 11, 32), new Vector3(13, -17, 25), new Vector3(27, 21, 32), Vector3.forward, Vector3.back })
                foreach (bool specular in new[] { false, true })
                {
                    light.type = type;
                    light.position = position;
                    light.direction = position;
                    P(lighting, "_SpecularEnabled").floatValue = specular ? 1 : 0;
                    Call(lighting, "NotifyValuesChanged");
                    var actual = Full(doc);
                    float worst = 0; string location = "";
                    for (int y = 5; y < 43; y++) for (int x = 5; x < 59; x++)
                    {
                        var n = Decode(packed[y * 64 + x]);
                        var delta = position - new Vector3(x + .5f, y + .5f, 0);
                        var l = type == WhimTexLightType.Directional ? position.normalized : delta.normalized;
                        float diffuse = Mathf.Max(0, Vector3.Dot(n, l));
                        float spec = specular ? Mathf.Pow(Mathf.Max(0, Vector3.Dot(n, (l + Vector3.forward).normalized)), 2 / (.4f * .4f) - 2) * diffuse : 0;
                        float power = light.intensity * (type == WhimTexLightType.Directional ? 1 : Mathf.Pow(Mathf.Clamp01(1 - delta.magnitude / light.range), light.falloff));
                        Color expected = light.color * ((diffuse + spec) * power); expected.a = 1;
                        for (int c = 0; c < 4; c++)
                        {
                            float error = Mathf.Abs(expected[c] - actual[y * 64 + x][c]);
                            if (error > worst) { worst = error; location = x + "," + y + " channel " + c + " expected " + expected + " actual " + actual[y * 64 + x]; }
                        }
                    }
                    t.Near(0, worst, .015, type + " lighting axis/encoding/specular at " + position + ", gamma=" + gamma + ", spec=" + specular + ": " + location);
                    var rect = new RectInt(9, 13, 27, 19); var region = Region(doc, cache, rect);
                    worst = 0;
                    for (int y = 0; y < rect.height; y++) for (int x = 0; x < rect.width; x++) for (int c = 0; c < 4; c++)
                        worst = Mathf.Max(worst, Mathf.Abs(actual[(y + rect.y) * 64 + x + rect.x][c] - region[y * rect.width + x][c]));
                    t.Near(0, worst, .015, "Regional/full " + type + " Lighting agreement");
                }
                layer.fx.Clear();
            }

            // A point source must use document coordinates, not cropped texture UVs.
            // Keep the fixture regional: never allocate or read a complete 8K image.
            doc.layers.Clear(); doc.width = doc.height = 8192;
            var packedNormal = FX("float4 ApplyFX(PinnedUV uv,float4 c){return float4(normalize(float3(.6,-.3,1))*.5+.5,1);}");
            source.fx.Clear(); source.fx.Add(packedNormal); source.fx.Add(lighting); doc.layers.Add(source);
            P(lighting, "_InputEncoding").floatValue = 0; P(lighting, "_SpecularEnabled").floatValue = 0;
            Call(lighting, "NotifyValuesChanged");
            light.type = WhimTexLightType.Point;
            light.range = 8192; light.position = new Vector3(3072, 4608, 1024);
            var nativeRect = new RectInt(3993, 3217, 96, 80);
            var native = Region(doc, cache, nativeRect);
            var nativeNormal = new Vector3(.6f, -.3f, 1).normalized;
            float nativeError = 0;
            for (int y = 0; y < nativeRect.height; y++) for (int x = 0; x < nativeRect.width; x++)
            {
                var delta = light.position - new Vector3(nativeRect.x + x + .5f, nativeRect.y + y + .5f, 0);
                float power = light.intensity * Mathf.Pow(Mathf.Clamp01(1 - delta.magnitude / light.range), light.falloff);
                Color expected = light.color * (Mathf.Max(0, Vector3.Dot(nativeNormal, delta.normalized)) * power); expected.a = 1;
                for (int c = 0; c < 4; c++) nativeError = Mathf.Max(nativeError, Mathf.Abs(native[y * nativeRect.width + x][c] - expected[c]));
            }
            t.Near(0, nativeError, .015, "8K regional Point Lighting preserves direction, position and HDR");

            doc.width = 64; doc.height = 48;
            light.type = WhimTexLightType.Directional; light.direction = new Vector3(-.8f, .3f, 1);
            light.color = Color.white; light.intensity = 1;
            P(lighting, "_LightDirection").vectorValue = light.direction;
            foreach (float baseAlpha in new[] { 0f, .4f, 1f })
            foreach (bool specular in new[] { false, true })
            {
                P(lighting, "_BaseColor").colorValue = new Color(1, 1, 1, baseAlpha);
                P(lighting, "_SpecularEnabled").floatValue = specular ? 1 : 0;
                P(lighting, "_LightingMode").floatValue = 1; Call(lighting, "NotifyValuesChanged");
                var global = Full(doc);
                P(lighting, "_LightingMode").floatValue = 0; Call(lighting, "NotifyValuesChanged");
                var local = Full(doc);
                float error = 0;
                for (int i = 0; i < global.Length; i++) for (int c = 0; c < 4; c++)
                    error = Mathf.Max(error, Mathf.Abs(global[i][c] - local[i][c]));
                t.Near(0, error, .002, "Global Directional / Local agreement, base alpha=" + baseAlpha + ", specular=" + specular);
            }
            light.type = WhimTexLightType.Point; P(lighting, "_LightingMode").floatValue = 1;
            P(lighting, "_BaseColor").colorValue = Color.white;
            var flatNormal = FX("float4 ApplyFX(PinnedUV uv,float4 c){return float4(.5,.5,1,1);}");
            source.fx[0] = flatNormal;
            P(lighting, "_Output").floatValue = 3; P(lighting, "_SpecularEnabled").floatValue = 1;
            P(lighting, "_SpecularIntensity").floatValue = 3;
            light.color = Color.white; light.intensity = 1; light.range = 128; light.falloff = 1;
            foreach (float z in new[] { 16f, -16f }) foreach (float roughness in new[] { 0f, .25f, 1f })
            {
                light.position = new Vector3(23.5f, 19.5f, z);
                P(lighting, "_Roughness").floatValue = roughness; Call(lighting, "NotifyValuesChanged");
                var pixels = Full(doc); float worst = 0, peak = 0; int peakIndex = -1;
                for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++)
                {
                    // Closed form for a flat plane. The half angle satisfies
                    // cos(theta/2)^2=(1+cos(theta))/2; no shader helper is reused.
                    double distance = Math.Sqrt((x - 23) * (x - 23) + (y - 19) * (y - 19) + z * z);
                    double facing = z / distance, r = Math.Max(.01, roughness);
                    double expected = z < 0 ? 0 : 3 * (1 - distance / 128) * facing * Math.Pow((1 + facing) * .5, Math.Max(2 / (r * r) - 2, 1) * .5);
                    Color c = pixels[y * 64 + x];
                    for (int channel = 0; channel < 4; channel++) t.True(float.IsFinite(c[channel]), "Finite specular result");
                    for (int channel = 0; channel < 3; channel++) worst = Mathf.Max(worst, (float)Math.Abs(c[channel] * c.a - expected));
                    worst = Mathf.Max(worst, (float)Math.Abs(c.a - Math.Min(1, expected)));
                    if (c.r * c.a > peak) { peak = c.r * c.a; peakIndex = y * 64 + x; }
                }
                t.Near(0, worst, .002, "Independent flat-plane specular, z=" + z + ", roughness=" + roughness);
                if (z > 0) t.Equal(19 * 64 + 23, peakIndex, "Flat-plane highlight sits under the point source, without mirrored axes");
                else t.Near(0, peak, 0, "Backlight produces no front-side specular");
            }
            t.Equal(active, RenderTexture.active, "Caller render target"); t.Equal(srgb, GL.sRGBWrite, "Caller color space state");
        }
        finally { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });
}
