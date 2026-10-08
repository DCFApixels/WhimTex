using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

// Own in-memory document and textures. No scene changes or Unity internal reflection.
public static class BrightnessContrastTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static string BodyRun()
    {
        const int w = 256, h = 4;
        string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/BrightnessContrast.hlsl";
        // Preset source is read-only; do not reimport a production asset.
        var doc = Scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = w; doc.height = h;
        var input = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
        var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        var output = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        ShaderFX fx = null, sourceFx = null; int checks = 0;
        try
        {
            var draft = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            fx = (ShaderFX)draft.Invoke(null, new object[] { doc, File.ReadAllText(path), new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(fx);
            T.True(!(parameters.Count != 3 || parameters.Find(p => p.name == "_Opacity")?.floatValue != 1f), "Expected two tone controls and neutral opacity.");
            foreach (var p in parameters.FindAll(p => p.name != "_Opacity"))
                T.True(!(!p.softMinimum || !p.softMaximum || p.floatValue != 0), "Expected neutral defaults and both bounds soft.");
            var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
                doc, null, w, h, 1f, true, true, null);
            var pixels = new Color[w * h];
            for (int x = 0; x < w; x++)
            {
                float t = x / 255f;
                pixels[x] = new Color(t, t, t, .37f);
                pixels[w + x] = new Color(t, 1 - t, .5f, 0);
                pixels[2 * w + x] = new Color(-2, 4, 1, .73f);
                pixels[3 * w + x] = new Color(t, .25f, .75f, 1);
            }
            input.SetPixels(pixels); input.Apply();
            var preview = new Color[w * 7 * 48]; int column = 0;
            foreach (var setting in new[] { Vector2.zero, new Vector2(-100, 0), new Vector2(100, 0),
                new Vector2(0, -100), new Vector2(0, 100), new Vector2(-300, 200), new Vector2(300, -200),
                new Vector2(-10000, -10000), new Vector2(10000, 10000),
                new Vector2(float.MaxValue, float.MaxValue), new Vector2(-float.MaxValue, -float.MaxValue),
                new Vector2(float.MaxValue, -float.MaxValue), new Vector2(-float.MaxValue, float.MaxValue) })
            {
                parameters.Find(p => p.name == "_Brightness").floatValue = setting.x;
                parameters.Find(p => p.name == "_Contrast").floatValue = setting.y;
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", F).Invoke(fx, new[] { context });
                T.True(!(material == null || ShaderUtil.ShaderHasError(material.shader)), "Compilation failed.");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    T.True(!(message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning ||
                        message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), message.message);
                GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply();
                var result = read.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color a = pixels[i], b = result[i];
                    for (int c = 0; c < 4; c++)
                    {
                        T.True(!(float.IsNaN(b[c]) || float.IsInfinity(b[c])), "Nonfinite output at " + setting);
                        float expected = c == 3 ? a[c] : Tone(a[c], setting.x, setting.y);
                        // At float extrema GPU denormal flushing can collapse tiny intermediate
                        // tones. Require finite/monotonic output, not subnormal CPU parity.
                        bool referenceComparable = Math.Abs(setting.x) <= 10000 && Math.Abs(setting.y) <= 10000 || c == 3 || a[c] <= 0 || a[c] >= 1;
                        T.True(!(referenceComparable && Math.Abs(b[c] - expected) > .0001f), $"Reference mismatch {setting}, {i}, {c}: {b[c]} != {expected}");
                        
                    }
                    if (i > 0 && i < w)
                        T.True(!(b.r + .00001f < result[i - 1].r), $"Non-monotonic ramp at {setting}, {i}: {result[i-1].r} -> {b.r}.");
                }
                if (column < 7)
                    for (int y = 0; y < 48; y++) for (int x = 0; x < w; x++)
                        preview[y * w * 7 + column * w + x] = new Color(result[x].r, result[x].g, result[x].b, 1);
                column++;
            }
            var image = new Texture2D(w * 7, 48, TextureFormat.RGBA32, false, true);
            try
            {
                image.SetPixels(preview); image.Apply(); Directory.CreateDirectory(Scope.Temp);
                File.WriteAllBytes(System.IO.Path.Combine(Scope.Temp, "BrightnessContrast.png"), image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            var catalog = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCatalog");
            bool found = false;
            foreach (var entry in (System.Collections.IEnumerable)catalog.GetMethod("GetEntries", F).Invoke(null, null))
                found |= (string)entry.GetType().GetField("menuPath", F).GetValue(entry) == "Color/Brightness Contrast";
            T.True(!(!found), "Preset missing from catalog.");
            parameters.Find(p => p.name == "_Brightness").floatValue = 100;
            parameters.Find(p => p.name == "_Contrast").floatValue = 0;
            sourceFx = (ShaderFX)draft.Invoke(null, new object[] { doc,
                "float4 ApplyFX(float2 uv, float4 color) { return float4(.25,.5,.75,.37); }", new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", F).Invoke(sourceFx, null);
            Layer child = new ColorFillLayerBehaviour(); child.fx.Add(sourceFx);
            Layer group = new GroupLayerBehaviour(); group.children.Add(child); group.fx.Add(fx); doc.layers.Add(group);
            Color expectedColor = new Color(Tone(.25f, 100, 0), Tone(.5f, 100, 0), Tone(.75f, 100, 0), .37f);
            void Near(Color actual, Color expected, string label)
            {
                for (int c = 0; c < 4; c++) T.True(!(Math.Abs(actual[c] - expected[c]) > .01f), label + ": " + actual + " != " + expected);
                
            }
            var composite = doc.ComposeCanvas();
            try { Near(composite.GetPixel(w / 2, h / 2), expectedColor, "Group composite"); }
            finally { UnityEngine.Object.DestroyImmediate(composite); }
            var thumbnail = (RenderTexture)typeof(WhimTexDocument).GetMethod("RenderAgentLayerPreview", F).Invoke(doc, new object[] { group, w });
            try
            {
                RenderTexture.active = thumbnail; read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply();
                Near(read.GetPixel(w / 2, h / 2), expectedColor, "Group preview");
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(thumbnail); }
            var exported = (Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdGroupContent", F).Invoke(doc, new object[] { group });
            try { Near(exported.GetPixel(w / 2, h / 2), expectedColor.gamma, "Layered export (display RGB)"); }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            group.clippingMask = true;
            doc.layers.Add(new ColorFillLayerBehaviour { color = Color.black });
            composite = doc.ComposeCanvas();
            Color display = expectedColor.gamma;
            try { Near(composite.GetPixel(w / 2, h / 2), new Color(display.r * .37f, display.g * .37f, display.b * .37f, 1).linear, "Clipped group (standard blend)"); }
            finally { UnityEngine.Object.DestroyImmediate(composite); }
            return null;
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(output);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            if (sourceFx != null) UnityEngine.Object.DestroyImmediate(sourceFx);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(doc);
        }
    }
    static float Tone(float x, float b, float c)
    {
        if (x <= 0 || x >= 1 || b == 0 && c == 0) return x;
        // Match float stage rounding; use doubles for ratios to avoid reference overflow.
        double gain = 1 + Math.Abs((double)b) / 50;
        double low = b < 0 ? x / gain : x, high = b >= 0 ? (1d - x) / gain : 1d - x;
        bool lower = low <= high;
        double edge = 2 * Math.Min(low, high), middle = Math.Abs(high - low);
        gain = 1 + Math.Abs((double)c) / 50;
        if (c >= 0) edge /= gain; else middle /= gain;
        float mapped = (float)(.5 * edge / (edge + middle));
        return lower ? mapped : 1 - mapped;
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
