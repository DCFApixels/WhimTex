using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class HSVSmoke
{
    public static string Main()
    {
        float Encode(float x) => x <= .0031308f ? x * 12.92f : 1.055f * Mathf.Pow(x, 1f / 2.4f) - .055f;
        float Decode(float x) => x <= .04045f ? x / 12.92f : Mathf.Pow((x + .055f) / 1.055f, 2.4f);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string path = "Packages/com.dcfapixels.whimtex/src/FXPresets/HSV.hlsl";
        AssetDatabase.ImportAsset(path);
        var owner = ScriptableObject.CreateInstance<TextureCompositor>();
        owner.hideFlags = HideFlags.HideAndDontSave;
        ShaderFX fx = null;
        Texture2D input = null, output = null;
        RenderTexture target = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", flags).Invoke(null, new object[] { owner, File.ReadAllText(path), new List<ShaderFXParameter>() });
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", flags).Invoke(fx, null);
            var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", flags).GetValue(fx);
            var colors = new[] { Color.black, Color.white, Color.red, Color.green, Color.blue, new Color(.18f,.18f,.18f), new Color(4,.6f,.02f), new Color(-.2f,.3f,.1f) };
            input = new Texture2D(8,2,TextureFormat.RGBAFloat,false,true) { filterMode = FilterMode.Point };
            for (int x=0;x<8;x++) { colors[x].a=x/7f; input.SetPixel(x,0,colors[x]); input.SetPixel(x,1,colors[x]); } input.Apply();
            target = new RenderTexture(8,2,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create();
            output = new Texture2D(8,2,TextureFormat.RGBAFloat,false,true);
            var context = Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"), owner, null, 8, 2, 1f, true, true);
            foreach (var settings in new[] { new Vector4(0,1,1,1), new Vector4(120,1,1,1), new Vector4(-120,1,1,1), new Vector4(180,0,1,1), new Vector4(37,2,.5f,1), new Vector4(-180,.5f,2,.5f), new Vector4(70,2,0,1), new Vector4(120,2,2,0) })
            {
                for(int i=0;i<4;i++) values[i].floatValue=settings[i];
                var material=(Material)typeof(ShaderFX).GetMethod("GetMaterial",flags).Invoke(fx,new[]{context});
                GL.sRGBWrite=false; Graphics.Blit(input,target,material); RenderTexture.active=target;
                output.ReadPixels(new Rect(0,0,8,2),0,0); output.Apply();
                for(int x=0;x<8;x++)
                {
                    var c=colors[x]; var expected=c;
                    if(settings.w!=0 && !(settings.x==0 && settings.y==1 && settings.z==1))
                    {
                        var encoded=new Color(Encode(Mathf.Max(c.r,0)),Encode(Mathf.Max(c.g,0)),Encode(Mathf.Max(c.b,0)));
                        Color.RGBToHSV(encoded,out float h,out float s,out float v);
                        var corrected=Color.HSVToRGB(Mathf.Repeat(h+settings.x/360,1),Mathf.Clamp01(s*settings.y),v*settings.z,true);
                        corrected=new Color(Decode(corrected.r),Decode(corrected.g),Decode(corrected.b));
                        expected=Color.Lerp(c,corrected,settings.w); expected.a=c.a;
                    }
                    var actual=output.GetPixel(x,0);
                    for(int i=0;i<4;i++) if(float.IsNaN(actual[i]) || float.IsInfinity(actual[i]) || Mathf.Abs(actual[i]-expected[i])>Mathf.Max(.0005f,Mathf.Abs(expected[i])*.003f))
                        throw new Exception($"Pixel {x}, settings {settings}: {actual}, expected {expected}");
                }
            }
            return "PASS: HSV compilation, neutral bypass, positive/negative hue, grayscale, saturation/value, HDR, negative inputs, amount and alpha.";
        }
        finally
        {
            RenderTexture.active=previous; GL.sRGBWrite=srgb;
            if(input!=null) UnityEngine.Object.DestroyImmediate(input);
            if(output!=null) UnityEngine.Object.DestroyImmediate(output);
            if(target!=null) {target.Release(); UnityEngine.Object.DestroyImmediate(target);}
            if(fx!=null) UnityEngine.Object.DestroyImmediate(fx);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
