using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class LightingBevelSmoke
{
    public static string Main()
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.width=doc.height=128;
        var effects=new List<ShaderFX>(); int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        ShaderFX FX(string code)
        {
            var fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            effects.Add(fx); typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null); return fx;
        }
        ShaderFXParameter P(ShaderFX fx,string name) => ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(fx)).Find(p=>p.name==name);
        Color[] Render()
        {
            var image=doc.Compose();
            try {var pixels=image.GetPixels(); foreach(var p in pixels)Check(!float.IsNaN(p.r)&&!float.IsInfinity(p.r),"finite output");return pixels;}
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        try
        {
            var lighting=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"));
            var bevel=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/SdfBevel.hlsl"));
            Check(P(lighting,"_BaseColor").colorValue.a==0 && P(bevel,"_BaseColor").colorValue.a==0,"transparent lighting defaults");
            P(lighting,"_BaseColor").colorValue=Color.white;
            Layer normal=new ColorFillLayerBehaviour(); doc.layers.Add(normal);
            normal.modifiers.Add(FX("float4 ApplyFX(float2 uv,float4 c){return float4(.5,.5,1,.6);}"));
            normal.modifiers.Add(lighting);
            P(lighting,"_PackedColor").floatValue=0; P(lighting,"_Ambient").floatValue=0;
            P(lighting,"_LightDirection").vectorValue=new Vector4(0,0,1,0);
            var lit=Render()[8256];
            Check(Mathf.Abs(lit.r-1)<.005 && Mathf.Abs(lit.a-.6f)<.005,"flat normal and alpha");
            P(lighting,"_LightDirection").vectorValue=new Vector4(0,0,-1,0);
            Check(Render()[8256].r<.01,"back lighting");
            normal.modifiers.Clear();
            normal.modifiers.Add(FX("#include \"Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc\"\nfloat4 ApplyFX(float2 uv,float4 c){return float4(SpriteDecode(float3(.5,.5,1)),.6);}"));
            normal.modifiers.Add(lighting); P(lighting,"_PackedColor").floatValue=1; P(lighting,"_LightDirection").vectorValue=new Vector4(0,0,1,0);
            Check(Mathf.Abs(Render()[8256].r-1)<.005,"packed color decode");
            normal.modifiers.Clear();
            normal.modifiers.Add(FX("float4 ApplyFX(float2 uv,float4 c){float h=saturate(1-length(uv-.5)*3);return float4(h,h,h,1);}"));
            normal.modifiers.Add(bevel);
            var both=Render();
            P(bevel,"_Output").floatValue=1; var highlight=Render();
            P(bevel,"_Output").floatValue=2; var shadow=Render();
            float lightSum=0, shadowSum=0;
            for(int i=0;i<both.Length;i++)
            {
                Check(Mathf.Abs(both[i].a-highlight[i].a-shadow[i].a)<.005,"modes partition alpha");
                Check(highlight[i].a*shadow[i].a<.00001,"light and shadow do not overlap");
                lightSum+=highlight[i].a;shadowSum+=shadow[i].a;
            }
            Check(lightSum>1 && shadowSum>1,"both lighting lobes on ordinary layer");
            Check(both[0].a<.001,"flat background transparent");
            P(bevel,"_Depth").floatValue=0;
            foreach(var c in Render())Check(c.a<.001,"zero depth transparent");
            P(bevel,"_Depth").floatValue=-6;P(bevel,"_Output").floatValue=0;
            var recessed=Render();float difference=0;
            for(int i=0;i<both.Length;i++)difference+=Mathf.Abs(both[i].a-recessed[i].a);
            Check(difference>1,"negative depth changes relief");
            var preview=new Texture2D(384,128,TextureFormat.RGBA32,false);
            try
            {
                var images=new[]{both,highlight,shadow};
                for(int panel=0;panel<3;panel++)for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {
                    Color c=images[panel][y*128+x];
                    Color bg=new Color(.22f,.22f,.22f,1);
                    Color display=Color.Lerp(bg,new Color(c.r,c.g,c.b,1),c.a);
                    preview.SetPixel(panel*128+x,y,display.gamma);
                }
                preview.Apply();Directory.CreateDirectory("Temp/WhimTex");
                File.WriteAllBytes("Temp/WhimTex/bevel-smoke.png",preview.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(preview);}
            Layer heightLayer=new ColorFillLayerBehaviour();
            heightLayer.enabled=false;
            heightLayer.modifiers.Add(normal.modifiers[0]);
            doc.layers.Add(heightLayer);
            typeof(TextureCompositor).GetMethod("NormalizeModel",F).Invoke(doc,null);
            normal.modifiers[0]=FX("float4 ApplyFX(float2 uv,float4 c){return 0;}");
            P(bevel,"_HeightMap").textureSource=ShaderFXTextureSource.Layer;
            P(bevel,"_HeightMap").textureLayerId=heightLayer.Id;
            P(bevel,"_Depth").floatValue=6;
            var external=Render();
            for(int i=0;i<both.Length;i++)Check(Mathf.Abs(external[i].a-both[i].a)<.005,"disabled height layer on transparent host");
            P(bevel,"_HeightChannel").floatValue=4;
            foreach(var c in Render())Check(c.a<.001,"constant alpha channel gives no relief");
            P(bevel,"_HeightChannel").floatValue=1;
            var red=Render();
            for(int i=0;i<both.Length;i++)Check(Mathf.Abs(red[i].a-both[i].a)<.005,"red channel selection");
            Check(typeof(SDFLayerBehaviour).GetField("activeDistanceTexture",F)==null,"no SDF-specific output");
            return "PASS: "+checks+" lighting, transparent bevel modes, flat/zero depth and generic layer checks.";
        }
        finally {foreach(var fx in effects)UnityEngine.Object.DestroyImmediate(fx);UnityEngine.Object.DestroyImmediate(doc);}
    }
}
