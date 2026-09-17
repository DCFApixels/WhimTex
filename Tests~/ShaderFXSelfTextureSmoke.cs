using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ShaderFXSelfTextureSmoke
{
    public static string Main()
    {
        const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.width=doc.height=16;
        var effects=new List<ShaderFX>();
        int checks=0;
        ShaderFX Create(string code)
        {
            var fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            effects.Add(fx); typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null); return fx;
        }
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        void Pixel(Color expected)
        {
            var image=doc.Compose();
            try { var p=image.GetPixel(8,8); Check(Mathf.Abs(p.r-expected.r)<.005 && Mathf.Abs(p.g-expected.g)<.005 && Mathf.Abs(p.b-expected.b)<.005 && Mathf.Abs(p.a-expected.a)<.005,"Pixel "+p+" expected "+expected); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        try
        {
            Layer layer=new ColorFillLayerBehaviour(); doc.layers.Add(layer);
            var before=Create("float4 ApplyFX(float2 uv,float4 color){return float4(0.2,0.4,0.6,1);}");
            var sample=Create("// @param texture2D _Source = self\nfloat4 ApplyFX(float2 uv,float4 color){return tex2D(_Source,uv);}");
            layer.modifiers.Add(before); layer.modifiers.Add(sample);
            Pixel(new Color(.2f,.4f,.6f,1));
            var p=((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(sample))[0];
            Check(p.textureSource==ShaderFXTextureSource.Self,"self parsed");
            p.textureSource=ShaderFXTextureSource.None; Pixel(Color.clear);
            p.textureSource=ShaderFXTextureSource.Texture; Pixel(Color.white);
            p.textureSource=ShaderFXTextureSource.Self;
            layer.modifiers.Reverse(); Pixel(new Color(.2f,.4f,.6f,1));
            layer.modifiers.Reverse();
            var export=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter").GetMethod("BuildSource",F);
            foreach(var mode in new[]{ShaderFXTextureSource.Self,ShaderFXTextureSource.None})
            {
                p.textureSource=mode;
                string code=(string)export.Invoke(null,new object[]{sample,"Tests/Source"});
                var copy=Create(code);
                var parameters=(List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(copy);
                Check(parameters[0].textureSource==mode,"preset roundtrip");
            }
            p.textureSource=ShaderFXTextureSource.Self;
            Layer group=new GroupLayerBehaviour();
            layer.modifiers.Clear(); layer.modifiers.Add(before);
            group.children.Add(layer); group.modifiers.Add(sample);
            doc.layers.Clear(); doc.layers.Add(group);
            Pixel(new Color(.2f,.4f,.6f,1));
            return "PASS: "+checks+" Self/None, FX ordering, legacy white, group input and preset checks.";
        }
        finally { foreach(var fx in effects)UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(doc); }
    }
}
