using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class NormalSpecularTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int W = 64, H = 64;
    public static string Run() => TestContext.Run("Normal Lighting optional specular", t =>
    {
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T obj) where T : UnityEngine.Object { obj.hideFlags = HideFlags.HideAndDontSave; owned.Add(obj); return obj; }
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        RenderTexture output = null;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = W; doc.height = H;
            var input = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
            var read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
            output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var assembly = typeof(ShaderFX).Assembly;
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            ShaderFX Create(string code)
            {
                var fx = Own((ShaderFX)draft.Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() }));
                typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null); return fx;
            }
            ShaderFX lighting = null;
            var catalog = assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog", true);
            foreach (object entry in (IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
                if ((string)entry.GetType().GetField("menuPath", F).GetValue(entry) == "Normal Map/Lighting")
                    lighting = Own((ShaderFX)typeof(ShaderFX).GetMethod("FromCatalog", F).Invoke(null, new object[] { doc, entry }));
            t.True(lighting != null, "Imported catalog preset exists");
            ShaderFXParameter P(string name) => ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(lighting)).Find(p => p.name == name);
            void Set(string name, float value) { P(name).floatValue = value; Changed(); }
            void Changed() => typeof(ShaderFX).GetMethod("NotifyValuesChanged", F).Invoke(lighting, null);
            Color[] Read(RenderTexture rt)
            {
                RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply(); return read.GetPixels();
            }
            Color[] Render()
            {
                var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true), doc, null, W, H, 1f, true, true, null);
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(lighting, new[] { context });
                t.True(material != null && !ShaderUtil.ShaderHasError(material.shader), "Production shader compiles");
                material.SetTexture("_Normals", input); GL.sRGBWrite = false; Graphics.Blit(input, output, material); return Read(output);
            }
            Color[] Composite(WhimTexDocument model = null)
            {
                var active = RenderTexture.active; bool write = GL.sRGBWrite;
                var image = (model ?? doc).ComposeCanvas();
                try { t.True(active == RenderTexture.active && write == GL.sRGBWrite, "Caller GPU state restored"); return image.GetPixels(); }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            void Same(Color[] expected, Color[] actual, string label, double tolerance = .004)
            {
                t.Equal(expected.Length, actual.Length, label + " dimensions");
                double worst = 0; bool finite = true; int at = 0, channel = 0;
                for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                {
                    finite &= !float.IsNaN(actual[i][c]) && !float.IsInfinity(actual[i][c]);
                    // Vanishing pow tails may be flushed to zero on the GPU. Compare
                    // their visible contribution, not undefined straight RGB at zero alpha.
                    bool invisible = c < 3 && expected[i].a < 1e-6f && actual[i].a < 1e-6f;
                    double e = Math.Abs(invisible ? expected[i][c] * expected[i].a - actual[i][c] * actual[i].a : expected[i][c] - actual[i][c]);
                    if (e > worst) { worst = e; at = i; channel = c; }
                }
                t.True(finite, label + " finite"); t.Near(0, worst, tolerance, label + " at " + (at % W) + "," + (at / W) + " channel " + channel + ": " + expected[at] + " vs " + actual[at]);
            }
            Vector3 Unit(Vector3 v) => v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.forward;
            Vector3 RGB(Color c) => new Vector3(c.r, c.g, c.b);
            float Decode(float c) => c <= .04045f ? c / 12.92f : (float)Math.Pow((c + .055) / 1.055, 2.4);
            Color Linear(Color c) => new Color(Decode(c.r), Decode(c.g), Decode(c.b), c.a);
            var normals = new Vector3[W * H]; var pixels = new Color[W * H];
            void Upload(bool packed, Vector3? constant = null)
            {
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    normals[i] = constant ?? Unit(new Vector3((x + .5f) / W * 4 - 2, (y + .5f) / H * 4 - 2, 1));
                    Vector3 n = normals[i] * .5f + Vector3.one * .5f;
                    Color c = new Color(n.x, n.y, n.z, 1); if (packed) c = Linear(c);
                    c.a = x < 3 ? 0 : x < 6 ? .35f : 1; pixels[i] = c;
                }
                input.SetPixels(pixels); input.Apply(); Set("_PackedColor", packed ? 1 : 0);
            }
            Color[] Oracle()
            {
                var expected = new Color[pixels.Length];
                Vector4 direction = P("_LightDirection").vectorValue;
                Vector3 l = Unit(new Vector3(direction.x, direction.y, direction.z));
                Color b = Linear(P("_BaseColor").colorValue), light = Linear(P("_LightColor").colorValue), shadow = Linear(P("_ShadowColor").colorValue);
                Color spec = Linear(P("_SpecularColor").colorValue);
                float outputMode = P("_Output").floatValue, intensity = Mathf.Max(P("_Intensity").floatValue, 0);
                for (int i = 0; i < expected.Length; i++)
                {
                    Vector3 n = Unit(normals[i]); if (P("_FlipY").floatValue > .5f) n.y = -n.y;
                    float facing = Vector3.Dot(n, l), alpha = 0; Vector3 premul = Vector3.zero;
                    if (outputMode != 3)
                    {
                        // Independent CPU transcription of the original, pre-Specular lighting.
                        float delta = (facing - l.z) * intensity;
                        Color tint = delta >= 0 ? light : shadow;
                        float highlight = outputMode == 2 ? 0 : Mathf.Clamp01(delta), dark = outputMode == 1 ? 0 : Mathf.Clamp01(-delta);
                        float overlay = Mathf.Clamp01((highlight + dark) * tint.a), weight = Mathf.Clamp01(b.a);
                        Vector3 lit = Vector3.Lerp(RGB(shadow), RGB(light), Mathf.Clamp01(facing)) * intensity + Vector3.one * Mathf.Max(P("_Ambient").floatValue, 0);
                        alpha = Mathf.Lerp(overlay, pixels[i].a, weight);
                        premul = Vector3.Lerp(RGB(tint) * overlay, Vector3.Scale(RGB(b), lit) * pixels[i].a, weight);
                    }
                    if (P("_SpecularEnabled").floatValue > .5f && outputMode != 2)
                    {
                        Vector3 h = l + Vector3.forward;
                        double r = Math.Max(Mathf.Clamp01(P("_Roughness").floatValue), .01);
                        double lobe = h.sqrMagnitude <= 1e-12 || n.z <= 0 ? 0 : Math.Pow(Mathf.Clamp01(Vector3.Dot(n, h.normalized)), Math.Max(2 / (r * r) - 2, 1)) * Mathf.Clamp01(facing);
                        float strength = (float)lobe * Mathf.Max(P("_SpecularIntensity").floatValue, 0) * Mathf.Clamp01(spec.a);
                        alpha += Mathf.Clamp01(strength) * pixels[i].a * (1 - alpha);
                        premul += RGB(spec) * strength * pixels[i].a;
                    }
                    expected[i] = alpha > 0 ? new Color(premul.x / alpha, premul.y / alpha, premul.z / alpha, alpha) : Color.clear;
                }
                return expected;
            }
            t.Equal(0f, P("_SpecularEnabled").floatValue, "Specular defaults off");
            t.Equal(0f, P("_Output").floatValue, "Original Both default");
            t.True((bool)typeof(ShaderFX).GetProperty("IsPixelLocal", F).GetValue(lighting), "Lighting stays pixel-local");
            P("_SpecularColor").colorValue = new Color(1.5f, .6f, .2f, .7f); Changed();
            foreach (bool packed in new[] { false, true })
            {
                Upload(packed);
                foreach (float baseAlpha in new[] { 0f, .4f, 1f }) foreach (int mode in new[] { 0, 1, 2, 3 })
                {
                    P("_BaseColor").colorValue = new Color(.7f, .5f, .3f, baseAlpha); Set("_Output", mode); Set("_SpecularEnabled", 0);
                    var before = Render(); Same(Oracle(), before, "Original diffuse with disabled Specular", .0001);
                    foreach (float roughness in new[] { 0f, .25f, 1f })
                    {
                        Set("_SpecularEnabled", 1); Set("_Roughness", roughness); Set("_SpecularIntensity", 2);
                        // At the 19998 exponent, sub-ULP normal decode differences are
                        // amplified near the peak. Keep the ordinary-lobe tolerance strict.
                        Same(Oracle(), Render(), "Independent CPU specular, encoding " + packed + ", output " + mode + ", base alpha " + baseAlpha + ", roughness " + roughness, roughness == 0 ? .008 : .002);
                    }
                    Set("_SpecularIntensity", 0); Same(before, Render(), "Zero intensity preserves diffuse", .00001);
                }
            }
            Set("_Output", 3); Set("_SpecularEnabled", 1); Set("_SpecularIntensity", 3); Set("_Roughness", 0);
            P("_SpecularColor").colorValue = Color.white;
            P("_LightDirection").vectorValue = new Vector4(0, 0, 1, 0); Changed(); Upload(false, Vector3.forward);
            var frontal = Render(); t.Near(3, frontal[W / 2].r, .0001, "Frontal flat normal retains HDR specular");
            t.Near(0, frontal[0].a, 0, "Transparent input has no specular"); t.Near(.35, frontal[4].a, .0001, "Partial input coverage retained");
            foreach (Vector4 direction in new[] { new Vector4(0, 0, -1, 0), Vector4.zero, new Vector4(-.7f, .3f, 1, 0) })
            { P("_LightDirection").vectorValue = direction; Changed(); Same(Oracle(), Render(), "Opposite, degenerate and oblique light", .002); }
            Upload(false, Vector3.back); Same(Oracle(), Render(), "Back-facing normal", .002);
            Upload(false); Set("_Roughness", .3f); Set("_FlipY", 1); Same(Oracle(), Render(), "Flip Y applies to specular", .002); Set("_FlipY", 0);
            Set("_Roughness", .08f); var narrow = Render(); Set("_Roughness", .7f); var broad = Render();
            int narrowCount = 0, broadCount = 0;
            for (int i = 0; i < broad.Length; i++) { if (narrow[i].a > .01f) narrowCount++; if (broad[i].a > .01f) broadCount++; }
            t.True(broadCount > narrowCount * 2, "Roughness broadens the visible highlight");
            P("_SpecularColor").colorValue = new Color(1, 1, 1, 0); Changed();
            Same(new Color[W * H], Render(), "Zero tint alpha disables specular", 0);
            P("_SpecularColor").colorValue = new Color(.8f, .5f, .2f, 1); Set("_Roughness", .3f); Set("_SpecularIntensity", 1); Set("_Output", 0);

            var generator = Create("float4 ApplyFX(PinnedUV uv, float4 color) { float3 n = normalize(float3(uv.x * 4 - 2, uv.y * 4 - 2, 1)); return float4(n * .5 + .5, .65); }");
            Layer child = new ColorFillLayerBehaviour(); child.colorRange = LayerColorRange.HDR; child.fx.Add(generator); child.fx.Add(lighting); doc.layers.Add(child);
            var baseline = Composite(); child.fx.Remove(lighting);
            Layer group = new GroupLayerBehaviour(); group.colorRange = LayerColorRange.HDR; group.children.Add(child); group.fx.Add(lighting);
            doc.layers.Clear(); doc.layers.Add(group); Same(baseline, Composite(), "Group input");
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", F).Invoke(doc, new object[] { group, W });
            try { Same(baseline, Read(thumbnail), "Thumbnail"); }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            var export = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", F).Invoke(doc, new object[] { group });
            try { var encoded = (Color[])baseline.Clone(); for (int i = 0; i < encoded.Length; i++) encoded[i] = encoded[i].gamma; Same(encoded, export.GetPixels(), "Group export"); }
            finally { UnityEngine.Object.DestroyImmediate(export); }
            doc.layers.Add(new ColorFillLayerBehaviour { color = Color.white }); var overWhite = Composite();
            group.clippingMask = true; Same(overWhite, Composite(), "Clipped group over opaque base");
            group.clippingMask = false; doc.layers.RemoveAt(1); group.enabled = false;
            Layer target = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, radius = 0 };
            doc.layers.Insert(0, target); Same(baseline, Composite(), "Target input"); doc.layers.Clear(); group.enabled = true; doc.layers.Add(group);
            var json = WhimTexDocumentJson.Write(doc); t.Equal(0, json.Warnings.Count, "Preset JSON warnings");
            using (var restored = WhimTexDocumentJson.Read(json.Json))
            { t.Equal(0, restored.Warnings.Count, "Restored JSON warnings"); Same(baseline, Composite(restored.Document), "JSON render parity"); }
            Layer external = new ColorFillLayerBehaviour(); external.colorRange = LayerColorRange.HDR; external.enabled = false; external.fx.Add(generator); doc.layers.Add(external);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", F).Invoke(doc, null);
            P("_Normals").textureSource = ShaderFXTextureSource.Layer; P("_Normals").textureLayerId = external.Id; Changed();
            Same(baseline, Composite(), "Disabled external normal source");

            string folder = Path.Combine("Temp/WhimTex/normal-specular", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var preview = Own(new Texture2D(W * 3, H, TextureFormat.RGBA32, false, true));
            var panels = new[] { baseline, narrow, broad };
            for (int panel = 0; panel < 3; panel++) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            { Color c = panels[panel][y * W + x]; preview.SetPixel(panel * W + x, y, Color.Lerp(new Color(.12f, .12f, .12f, 1), new Color(c.r, c.g, c.b, 1), c.a).gamma); }
            preview.Apply(); File.WriteAllBytes(Path.Combine(folder, "lighting-tight-broad.png"), preview.EncodeToPNG());
            foreach (var message in ShaderUtil.GetShaderMessages((Shader)typeof(ShaderFX).GetField("compiledShader", F).GetValue(lighting)))
                t.True(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error && message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    });
}
