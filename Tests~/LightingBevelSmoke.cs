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
        WhimTexGradient Constant(Color c)
        {
            var g=new WhimTexGradient(); g.SetKeys(new[]{new GradientColorKey(c,0),new GradientColorKey(c,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)}); return g;
        }
        try
        {
            var lighting=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"));
            var bevel=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/SdfBevel.hlsl"));
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
            normal.modifiers.Clear(); normal.modifiers.Add(bevel);
            Check(Render()[8256].r>.99,"non SDF bypass");
            doc.layers.Clear();
            var sdf=new SDFLayerBehaviour(); Layer sdfLayer=sdf;
            var shape=new ShapeLayerBehaviour{kind=ShapeLayerBehaviour.ShapeKind.Ellipse}; Layer source=shape;
            source.enabled=false; source.transform.scale=new Double2(.7,.7);
            doc.layers.Add(sdfLayer);doc.layers.Add(source);
            typeof(TextureCompositor).GetMethod("NormalizeModel",F).Invoke(doc,null);
            sdf.inputMode=EffectInputMode.Specific; sdf.TargetLayerId=source.Id;
            sdf.gradient=Constant(new Color(.5f,.5f,.5f,1));
            sdfLayer.modifiers.Add(bevel);
            var raised=Render();
            float min=1,max=0;foreach(var c in raised){min=Mathf.Min(min,c.r);max=Mathf.Max(max,c.r);}
            Check(max-min>.15,"raw SDF bevel works despite constant gradient");
            P(bevel,"_Depth").floatValue=-6;
            var recessed=Render();float difference=0;
            for(int i=0;i<raised.Length;i++)difference+=Mathf.Abs(raised[i].r-recessed[i].r);
            Check(difference>10,"negative depth changes relief");
            var preview=new Texture2D(256,128,TextureFormat.RGBA32,false);
            try
            {
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                {preview.SetPixel(x,y,raised[y*128+x].gamma);preview.SetPixel(x+128,y,recessed[y*128+x].gamma);}
                preview.Apply(); Directory.CreateDirectory("Temp/WhimTex");
                File.WriteAllBytes("Temp/WhimTex/bevel-smoke.png",preview.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(preview);}
            P(bevel,"_Depth").floatValue=0;var flat=Render();
            Check(Mathf.Abs(flat[8256].r-flat[128*64+25].r)<.005,"zero depth neutral");
            P(bevel,"_Depth").floatValue=6;
            P(bevel,"_Surface").textureSource=ShaderFXTextureSource.Texture;
            var a=Render();sdf.gradient=Constant(Color.red);var b=Render();
            for(int i=0;i<a.Length;i++)Check(Mathf.Abs(a[i].r-b[i].r)<.0001,"gradient independent relief");
            P(bevel,"_Surface").textureSource=ShaderFXTextureSource.Self;
            sdf.gradient=Constant(new Color(.65f,.3f,.1f,1));
            sdfLayer.transform.rotation=20; sdfLayer.transform.scale=new Double2(.8,.6);
            Render();
            var active=typeof(SDFLayerBehaviour).GetField("activeDistanceTexture",F);
            Check(active.GetValue(sdf)==null,"raw temporary released");
            return "PASS: "+checks+" lighting, alpha, SDF profile, gradient independence, transforms and lifetime checks.";
        }
        finally {foreach(var fx in effects)UnityEngine.Object.DestroyImmediate(fx);UnityEngine.Object.DestroyImmediate(doc);}
    }
}
