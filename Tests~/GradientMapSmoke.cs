using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GradientMapSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/GradientMap.hlsl";
        AssetDatabase.ImportAsset(path);
        string code = File.ReadAllText(path);
        var assembly = typeof(ShaderFX).Assembly;
        var args = new object[] { code, true, null };
        assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("Parse", flags).Invoke(null, args);
        if ((string)args[2] != "Color/Gradient Map") throw new Exception("Catalog header");
        var owner = ScriptableObject.CreateInstance<TextureCompositor>();
        owner.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        Texture2D input = null, output = null;
        RenderTexture target = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null, new object[] { owner, code, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var gradient = values[0].gradientValue;
            gradient.Mode = WhimTexGradientMode.Linear;
            gradient.ColorSpace = ColorSpace.Linear;
            gradient.Smoothness = 0;
            gradient.SetKeys(new[] { new GradientColorKey(Color.red * 2, 0), new GradientColorKey(Color.blue, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.5f, 1) });
            var colors = new[] { Color.black, Color.white, new Color(.18f,.18f,.18f), Color.red, Color.green, Color.blue, new Color(4,4,4), new Color(-1,-1,-1) };
            input = new Texture2D(8,2,TextureFormat.RGBAFloat,false,true) { filterMode = FilterMode.Point };
            for (int x=0;x<8;x++) { colors[x].a = x / 7f; input.SetPixel(x,0,colors[x]); input.SetPixel(x,1,colors[x]); }
            input.Apply();
            target = new RenderTexture(8,2,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create();
            output = new Texture2D(8,2,TextureFormat.RGBAFloat,false,true);
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), owner, null, 8, 2, 1f, true, true);
            foreach (float reverse in new[] { 0f, 1f })
            foreach (float amount in new[] { 0f, .5f, 1f })
            {
                values[1].floatValue = amount; values[2].floatValue = reverse;
                var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
                GL.sRGBWrite = false; Graphics.Blit(input,target,material); RenderTexture.active = target;
                output.ReadPixels(new Rect(0,0,8,2),0,0); output.Apply();
                for (int x=0;x<8;x++)
                {
                    Color c = colors[x];
                    float luminance = Mathf.Clamp01(Mathf.Max(c.r,0)*.2126f + Mathf.Max(c.g,0)*.7152f + Mathf.Max(c.b,0)*.0722f);
                    float t = Mathf.LinearToGammaSpace(luminance); if (reverse > .5f) t = 1-t;
                    Color expected = Color.Lerp(c,gradient.Evaluate(t),amount); expected.a = c.a;
                    Color actual = output.GetPixel(x,0);
                    if (Mathf.Abs(expected.r-actual.r)>.01f || Mathf.Abs(expected.g-actual.g)>.01f || Mathf.Abs(expected.b-actual.b)>.01f || Mathf.Abs(expected.a-actual.a)>.001f)
                        throw new Exception($"Pixel {x}, reverse {reverse}, amount {amount}: {actual}, expected {expected}");
                }
            }
            return "PASS: preset header, shader compilation, luminance mapping, HDR endpoints, reverse, amount 0/0.5/1 and source-alpha preservation.";
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
}
