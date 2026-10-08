using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GradientMapTests
{
    static string ExecuteMain()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/GradientMap.hlsl";

        string code = File.ReadAllText(path);
        var assembly = typeof(ShaderFX).Assembly;
        var args = new object[] { code, true, null };
        assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", flags).Invoke(null, args);
        UnityBRun.Check(!((string)args[2] != "Color/Gradient Map"), "Catalog header");
        var owner = UnityBRun.Create<WhimTexDocument>();
        owner.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        Texture2D input = null, output = null;
        RenderTexture target = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { owner, code, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var source = values.Find(p => p.name == "_SourceChannel");
            UnityBRun.Check(!(source == null || source.floatValue != 0), "Luminance remains the default source");
            var gradient = values.Find(p => p.name == "_Gradient").gradientValue;
            gradient.Mode = WhimTexGradientMode.Linear;
            gradient.ColorSpace = ColorSpace.Linear;
            gradient.Smoothness = 0;
            gradient.SetKeys(new[] { new GradientColorKey(Color.red * 2, 0), new GradientColorKey(Color.blue, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.5f, 1) });
            var colors = new[] { Color.black, Color.white, new Color(.18f,.18f,.18f), Color.red, Color.green, Color.blue, new Color(4,4,4), new Color(-1,-1,-1) };
            input = UnityBRun.Track(new Texture2D(8,2,TextureFormat.RGBAFloat,false,true) { filterMode = FilterMode.Point });
            for (int x=0;x<8;x++) { colors[x].a = x / 7f; input.SetPixel(x,0,colors[x]); input.SetPixel(x,1,colors[x]); }
            input.Apply();
            target = UnityBRun.Track(new RenderTexture(8,2,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear)); target.Create();
            output = UnityBRun.Track(new Texture2D(8,2,TextureFormat.RGBAFloat,false,true));
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), owner, null, 8, 2, 1f, true, true, null);
            int checks = 0;
            foreach (int channel in new[] { 0, 1, 2, 3, 4 })
            foreach (int mapping in new[] { 0, 1 })
            foreach (int alphaMode in new[] { 0, 1, 2 })
            foreach (float reverse in new[] { 0f, 1f })
            foreach (float amount in new[] { 0f, .5f, 1f })
            {
                source.floatValue = channel;
                var curve = values.Find(p => p.name == "_Mapping");
                curve.curveValue = mapping == 0 ? AnimationCurve.Linear(0,0,1,1) : AnimationCurve.Linear(0,.1f,1,.8f);
                gradient.SetKeys(gradient.ColorKeys, alphaMode == 0
                    ? new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.5f, 1) }
                    : new[] { new GradientAlphaKey(alphaMode == 1 ? 1 : 0, 0), new GradientAlphaKey(alphaMode == 1 ? 1 : 0, 1) });
                values.Find(p => p.name == "_Opacity").floatValue = amount;
                values.Find(p => p.name == "_Reverse").floatValue = reverse;
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
                GL.sRGBWrite = false; Graphics.Blit(input,target,material); RenderTexture.active = target;
                output.ReadPixels(new Rect(0,0,8,2),0,0); output.Apply();
                for (int x=0;x<8;x++)
                {
                    Color c = colors[x];
                    float luminance = Mathf.Clamp01(Mathf.Max(c.r,0)*.2126f + Mathf.Max(c.g,0)*.7152f + Mathf.Max(c.b,0)*.0722f);
                    float t = Mathf.LinearToGammaSpace(luminance); if (reverse > .5f) t = 1-t;
                    if (channel != 0)
                    {
                        t = channel == 4 ? Mathf.Clamp01(c.a) : Mathf.LinearToGammaSpace(Mathf.Clamp01(c[channel-1]));
                        if (reverse > .5f) t = 1-t;
                    }
                    t = curve.curveValue.Evaluate(t);
                    Color mapped = gradient.Evaluate(t);
                    Color expected = Color.Lerp(c,mapped,amount);
                    expected.a = c.a * Mathf.Lerp(1, mapped.a, amount);
                    Color actual = output.GetPixel(x,0);
                    UnityBRun.Check(!(Mathf.Abs(expected.r-actual.r)>.01f || Mathf.Abs(expected.g-actual.g)>.01f || Mathf.Abs(expected.b-actual.b)>.01f || Mathf.Abs(expected.a-actual.a)>.001f), $"Pixel {x}, channel {channel}, mapping {mapping}, alpha mode {alphaMode}, reverse {reverse}, amount {amount}: {actual}, expected {expected}");
                    checks++;
                }
            }
            return "";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (input != null) UnityEngine.Object.DestroyImmediate(input);
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
    public static string Main() => UnityBRun.Run("GradientMapSmoke.Main", () => ExecuteMain());
}

