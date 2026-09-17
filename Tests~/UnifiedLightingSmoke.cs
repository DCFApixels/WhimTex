using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class UnifiedLightingSmoke
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    public static string Main()
    {
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.width=doc.height=128;
        var effects=new List<ShaderFX>();int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        ShaderFX FX(string code)
        {
            var fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",F).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            effects.Add(fx);
            try { typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null); }
            catch(TargetInvocationException e) { throw new Exception(e.InnerException?.ToString() ?? e.ToString()); }
            return fx;
        }
        ShaderFXParameter P(ShaderFX fx,string name)=>((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(fx)).Find(p=>p.name==name);
        Color[] Render(){var t=doc.Compose();try{return t.GetPixels();}finally{UnityEngine.Object.DestroyImmediate(t);}}
        try
        {
            var normal=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"));
            var bevel=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/SdfBevel.hlsl"));
            var ramp=FX("float4 ApplyFX(float2 uv,float4 c){float h=.25+.5*uv.x;return float4(h,h,h,1);}");
            var packed=FX("float4 ApplyFX(float2 uv,float4 c){return float4(normalize(float3(-3.0/128,0,1))*.5+.5,1);}");
            Layer host=new ColorFillLayerBehaviour();doc.layers.Add(host);
            P(normal,"_PackedColor").floatValue=0;
            foreach(var fx in new[]{normal,bevel})
            {
                P(fx,"_Intensity").floatValue=3;
                P(fx,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,.4f);
            }
            foreach(float weight in new[]{0f,.25f,.5f,1f})for(int output=0;output<3;output++)
            {
                foreach(var fx in new[]{normal,bevel}){P(fx,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,weight);P(fx,"_Output").floatValue=output;}
                host.modifiers.Clear();host.modifiers.Add(ramp);host.modifiers.Add(bevel);var a=Render();
                host.modifiers.Clear();host.modifiers.Add(packed);host.modifiers.Add(normal);var b=Render();
                for(int y=8;y<120;y++)for(int x=8;x<120;x++)for(int c=0;c<4;c++)
                    Check(Mathf.Abs(a[y*128+x][c]-b[y*128+x][c])<.004,"identical normals produce identical lighting");
                if(weight==1)Check(Mathf.Abs(a[8256].a-1)<.004,"surface alpha is not multiplied twice");
            }
            host.modifiers.Clear();host.modifiers.Add(ramp);host.modifiers.Add(bevel);
            P(bevel,"_Output").floatValue=0;
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,0);var overlay=Render();
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,1);var surface=Render();
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,.5f);var blend=Render();
            for(int i=0;i<blend.Length;i++)
            {
                Check(Mathf.Abs(blend[i].a-(overlay[i].a+surface[i].a)*.5f)<.004,"alpha crossfade");
                for(int c=0;c<3;c++)Check(Mathf.Abs(blend[i][c]*blend[i].a-(overlay[i][c]*overlay[i].a+surface[i][c]*surface[i].a)*.5f)<.006,"premultiplied crossfade without dark fringe");
            }
            // Shared helper must survive portable include expansion, not require new host support.
            var writer=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            foreach(var fx in new[]{normal,bevel})
            {
                string source=(string)writer.GetMethod("BuildPortableSource",F).Invoke(null,new object[]{fx});
                Check(source.Contains("float4 WhimTexSurfaceLighting("),"portable helper definition retained");
                FX(source);
            }
            return "PASS: "+checks+" unified lighting, both render modes, component selection and portable include checks.";
        }
        finally{foreach(var fx in effects)UnityEngine.Object.DestroyImmediate(fx);UnityEngine.Object.DestroyImmediate(doc);}
    }
}
