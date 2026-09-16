using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ColorPresetsSmoke
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = doc.height = 8;
        var input = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        var output = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        int checks = 0;
        try
        {
            foreach (string name in new[] { "ColorBalance", "ColorFilter", "Levels", "Posterize", "Threshold" })
            {
                ShaderFX fx = null;
                try
                {
                    string code = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/" + name + ".hlsl");
                    fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null, new object[] { doc, code, new List<ShaderFXParameter>() });
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
                    var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
                    void Set(string key, float value) { var p = parameters.Find(x => x.name == key); if (p != null) p.floatValue = value; }
                    var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), doc, null, 8, 8, 1f, true, true);
                    for (int scenario = 0; scenario < 3; scenario++)
                    {
                        if (scenario == 1)
                        {
                            Set("_Dither", 1); Set("_Gamma", 2.3f); Set("_ShadowRange", 0); Set("_HighlightRange", 0);
                            Set("_OutBlack", .25f); Set("_Density", 1);
                            foreach (var p in parameters)
                            {
                                if (p.name == "_Midtones") p.vectorValue = new Vector4(-1, .5f, -2, 0);
                                if (p.name == "_FilterColor") p.colorValue = Color.black;
                            }
                        }
                        if (scenario == 2) { Set("_PreserveColor", 0); Set("_InBlack", .5f); Set("_InWhite", .5f); }
                        foreach (Color source in new[] { new Color(0,0,0,.37f), new Color(1,1,1,.37f), new Color(.2f,.4f,.8f,.37f), new Color(4,2,1,.37f) })
                        {
                            var pixels = new Color[64]; for (int i=0;i<64;i++) pixels[i]=source;
                            input.SetPixels(pixels); input.Apply();
                            var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", flags).Invoke(fx, new[] { context });
                            if (material == null || UnityEditor.ShaderUtil.ShaderHasError(material.shader)) throw new Exception(name + " compilation failed");
                            GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                            RenderTexture.active = output; read.ReadPixels(new Rect(0,0,8,8),0,0); read.Apply();
                            foreach (Color c in read.GetPixels())
                            {
                                for (int channel=0;channel<4;channel++) if (float.IsNaN(c[channel]) || float.IsInfinity(c[channel])) throw new Exception(name + " non-finite output");
                                if (Mathf.Abs(c.a-source.a)>.001f) throw new Exception(name + " changed alpha");
                                if (name == "Levels" && scenario == 1 && source.r == 0 && Mathf.Abs(c.r-.25f)>.001f) throw new Exception("Levels black lift failed");
                                if (name == "Posterize" && (c.r < 0 || c.r > 1)) throw new Exception("Posterize out of range");
                                checks++;
                            }
                        }
                    }
                }
                finally { if (fx != null) UnityEngine.Object.DestroyImmediate(fx); }
            }
            return "PASS: five presets compiled/rendered, " + checks + " pixel checks; finite output, preserved alpha, Levels black lift.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
