using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GradientSoftFXSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        int checks = 0;
        try
        {
            const string code = "// @param gradient _Ramp\nfloat4 ApplyFX(float2 uv, float4 color) { return _Ramp_Sample(uv.x); }";
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags, null,
                new[] {typeof(TextureCompositor), typeof(string), typeof(List<ShaderFXParameter>)}, null).Invoke(null,
                new object[] {doc, code, new List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var g = values[0].gradientValue;
            g.ColorSpace = ColorSpace.Linear;
            g.SetKeys(new[] {new GradientColorKey(Color.white, 0), new GradientColorKey(Color.black, 1)},
                new[] {new GradientAlphaKey(.8f, 0), new GradientAlphaKey(.1f, 1)});
            g.SetMidpoint(false, 0, .72700745f); g.SetMidpoint(true, 0, .25f);
            var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
                new object[] {doc, null, 257, 2, 1f, true, true, null});
            target = new RenderTexture(257, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create(); pixels = new Texture2D(257, 2, TextureFormat.RGBAFloat, false, true);
            Shader shader = null;
            for (int pass=0; pass<2; pass++)
            foreach (var mode in new[] {WhimTexGradientMode.Classic, WhimTexGradientMode.Linear, WhimTexGradientMode.Perceptual})
            foreach (bool colored in new[] {false, true})
            {
                g.SetKeys(new[] {new GradientColorKey(colored ? Color.red : Color.white, 0),
                    new GradientColorKey(colored ? Color.cyan : Color.black, 1)}, g.AlphaKeys);
                g.Mode = mode;
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] {context});
                if (shader != null && shader != material.shader) throw new Exception("Gradient edit recompiled shader");
                shader = material.shader;
                GL.sRGBWrite = false; Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 257, 2), 0, 0); pixels.Apply();
                for (int x = 0; x < 257; x++)
                {
                    var expected = g.Evaluate((x+.5f)/257); var actual = pixels.GetPixel(x, 0);
                    for (int c = 0; c < 4; c++)
                    {
                        checks++;
                        if (Mathf.Abs(expected[c]-actual[c]) > .002f)
                            throw new Exception("Rounded/" + mode + " pixel " + x + " channel " + c);
                    }
                }
            }
            g.Mode = WhimTexGradientMode.Classic; g.ColorSpace = ColorSpace.Gamma;
            g.SetMidpoint(false, 0, .5f); g.SetMidpoint(true, 0, .5f);
            if (!GradientUtility.IsTwoColorGradient(g, out _, out _)) throw new Exception("Rounded two-color default export rejected");
            g.Smoothness = .5f;
            if (GradientUtility.IsTwoColorGradient(g, out _, out _)) throw new Exception("Lossy custom-smoothness export accepted");
            return "Rounded FX sampling, gradient rebinding and export guard passed: " + checks + " channel checks.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); }
            UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
