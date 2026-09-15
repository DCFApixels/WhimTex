using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class NormalNormalizeSmoke
{
    public static string Main()
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var assembly = typeof(ShaderFX).Assembly;
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        document.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        var output = RenderTexture.GetTemporary(2, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var input = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        var pixels = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        int checks = 0;
        try
        {
            string source = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Normalize.hlsl");
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", hidden).Invoke(null, new object[] { document, source, new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", hidden).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", hidden).GetValue(fx);
            if (parameters.Count != 1 || parameters[0].floatValue != 1) throw new Exception("Packed Color must default to true");
            var context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), document, null, 2, 2, 1f, true, true);
            var decode = assembly.GetType("DCFApixels.WhimTex.HdrUtility").GetMethod("Decode", hidden);
            Color EncodeNormal(Vector3 n, bool packed)
            {
                Color c = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, .37f);
                return packed ? (Color)decode.Invoke(null, new object[] { c }) : c;
            }
            foreach (bool packed in new[] { true, false })
            {
                parameters[0].floatValue = packed ? 1 : 0;
                foreach (Vector3 n in new[] { Vector3.forward, new Vector3(.6f,0,.8f), new Vector3(-.6f,0,.8f),
                    Vector3.down, Vector3.zero, new Vector3(.2f,.3f,.4f) })
                {
                    Color c = EncodeNormal(n, packed);
                    input.SetPixels(new[] { c, c, c, c }); input.Apply();
                    var material = (Material)typeof(ShaderFX).GetMethod("GetMaterial", hidden).Invoke(fx, new[] { context });
                    GL.sRGBWrite = false;
                    Graphics.Blit(input, output, material);
                    RenderTexture.active = output;
                    pixels.ReadPixels(new Rect(0,0,2,2),0,0); pixels.Apply();
                    Color result = pixels.GetPixel(0,0);
                    Color expected = EncodeNormal(n == Vector3.zero ? Vector3.forward : n.normalized, packed);
                    for (int channel = 0; channel < 4; channel++)
                        if (float.IsNaN(result[channel]) || Mathf.Abs(result[channel] - expected[channel]) > .0001f)
                            throw new Exception("Normal mismatch: packed=" + packed + " n=" + n + " result=" + result + " expected=" + expected);
                    checks++;
                }
            }
            return "PASS: " + checks + " GPU normal cases, Packed Color/Linear Data, neutral fallback and alpha preservation.";
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(input); UnityEngine.Object.DestroyImmediate(pixels);
            if (fx != null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(document);
        }
    }
}
