// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class UnifiedLightingTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("UnifiedLightingTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static void ExecuteMain()
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.width=doc.height=128;
        var effects=new List<ShaderFX>();
        Texture2D heightTexture=null;
        void Check(bool value,string message){ context.True(value, message); }
        ShaderFX FX(string code)
        {
            var fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(DCFApixels.WhimTex.WhimTexDocument), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null,new object[]{doc,code,new List<ShaderFXParameter>()});
            effects.Add(fx);
            try { typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null); }
            catch(TargetInvocationException e) { throw new Exception(e.InnerException?.ToString() ?? e.ToString()); }
            return fx;
        }
        ShaderFXParameter P(ShaderFX fx,string name)=>((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(fx)).Find(p=>p.name==name);
        Color[] Render(){var t=doc.ComposeCanvas();try{return t.GetPixels();}finally{UnityEngine.Object.DestroyImmediate(t);}}
        try
        {
            var normal=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"));
            var bevel=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/BevelEmboss.hlsl"));
            context.Equal("spatial", (string)typeof(ShaderFX).GetProperty("AppliedLocalityStatus",F).GetValue(bevel), "Bevel uses verified spatial sampling");
            context.Equal("Applied successfully.", (string)typeof(ShaderFX).GetProperty("Diagnostics",F).GetValue(bevel), "Bevel compiles without locality warnings");
            var ramp=FX("float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;float h=.25+.5*uv.x;return float4(h,h,h,1);}");
            var packed=FX("float4 ApplyFX(PinnedUV _pinnedUv, float4 c){ float2 uv = _pinnedUv;return float4(normalize(float3(-3.0/128,0,1))*.5+.5,1);}");
            Layer host=new ColorFillLayerBehaviour();doc.layers.Add(host);
            P(normal,"_InputEncoding").floatValue=0;
            foreach(var fx in new[]{normal,bevel})
            {
                P(fx,"_Intensity").floatValue=3;
                P(fx,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,.4f);
            }
            foreach(float weight in new[]{0f,.25f,.5f,1f})for(int output=0;output<3;output++)
            {
                foreach(var fx in new[]{normal,bevel}){P(fx,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,weight);P(fx,"_Output").floatValue=output;}
                host.fx.Clear();host.fx.Add(ramp);host.fx.Add(bevel);var a=Render();
                host.fx.Clear();host.fx.Add(packed);host.fx.Add(normal);var b=Render();
                for(int y=8;y<120;y++)for(int x=8;x<120;x++)for(int c=0;c<4;c++)
                    Check(Mathf.Abs(a[y*128+x][c]-b[y*128+x][c])<.004,"identical normals produce identical lighting");
                if(weight==1)Check(Mathf.Abs(a[8256].a-1)<.004,"surface alpha is not multiplied twice");
            }
            host.fx.Clear();host.fx.Add(ramp);host.fx.Add(bevel);
            P(bevel,"_Output").floatValue=0;
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,0);var overlay=Render();
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,1);var surface=Render();
            P(bevel,"_BaseColor").colorValue=new Color(.3f,.5f,.7f,.5f);var blend=Render();
            for(int i=0;i<blend.Length;i++)
            {
                Check(Mathf.Abs(blend[i].a-(overlay[i].a+surface[i].a)*.5f)<.004,"alpha crossfade");
                for(int c=0;c<3;c++)Check(Mathf.Abs(blend[i][c]*blend[i].a-(overlay[i][c]*overlay[i].a+surface[i][c]*surface[i].a)*.5f)<.006,"premultiplied crossfade without dark fringe");
            }
            // Shared helper must survive preset include expansion, not require new host support.
            var writer=typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            foreach(var fx in new[]{normal,bevel})
            {
                string source=(string)writer.GetMethod("BuildSource",F).Invoke(null,new object[]{fx,"Lighting Test"});
                Check(source.Contains("float4 WhimTexSurfaceLighting("),"preset helper definition retained");
                FX(source);
            }

            // A/B control: the same preset with its former direct texture read.
            string bevelSource=File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/BevelEmboss.hlsl");
            const string sample="SampleTexture(_HeightMap, uv)";
            Check(bevelSource.Contains(sample) && bevelSource.IndexOf(sample,StringComparison.Ordinal)==bevelSource.LastIndexOf(sample,StringComparison.Ordinal), "One height sampling site in A/B comparison");
            var direct=FX(bevelSource.Replace(sample,"tex2D(_HeightMap, uv)"));
            var patterned=FX("float4 ApplyFX(PinnedUV p, float4 c) { float2 uv=p; return float4(2*uv.x-.25, sin(uv.y*30)*.7+.5, cos(uv.x*17+uv.y*11), uv.x<.2?0:.35+.3*sin(uv.y*19)); }");
            host.colorRange=LayerColorRange.HDR;
            heightTexture=new Texture2D(256,256,TextureFormat.RGBAFloat,true,true) { hideFlags=HideFlags.HideAndDontSave, wrapMode=TextureWrapMode.Repeat };
            var heightPixels=new Color[256*256];
            for(int y=0;y<256;y++) for(int x=0;x<256;x++)
                heightPixels[y*256+x]=new Color(x/100f-.4f, Mathf.Sin(x*.4f+y*.7f), y/120f, (x%11)/10f);
            heightTexture.SetPixels(heightPixels); heightTexture.Apply(true,false);
            foreach(var effect in new[]{bevel,direct})
            {
                P(effect,"_Intensity").floatValue=3;
                P(effect,"_LightColor").colorValue=new Color(3,.7f,.2f,.8f);
                P(effect,"_ShadowColor").colorValue=new Color(.1f,.4f,2,.6f);
                P(effect,"_Profile").curveValue=AnimationCurve.EaseInOut(0,0,1,1);
            }
            foreach(int inputMode in new[]{0,1,2,3})
            {
                heightTexture.filterMode=inputMode==1?FilterMode.Point:inputMode==2?FilterMode.Bilinear:FilterMode.Trilinear;
                foreach(var effect in new[]{bevel,direct})
                {
                    P(effect,"_HeightMap").textureSource=inputMode==0?ShaderFXTextureSource.Self:ShaderFXTextureSource.Texture;
                    P(effect,"_HeightMap").textureValue=heightTexture;
                }
                for(int channel=0;channel<5;channel++) for(int output=0;output<3;output++)
                foreach(float depth in new[]{-6f,0f,10f})
                {
                    foreach(var effect in new[]{bevel,direct})
                    {
                        P(effect,"_HeightChannel").floatValue=channel;
                        P(effect,"_Output").floatValue=output;
                        P(effect,"_Depth").floatValue=depth;
                        P(effect,"_Smoothing").floatValue=channel%2==0?0:3.25f;
                        P(effect,"_BaseColor").colorValue=new Color(.3f,1.5f,.7f,output==0?0:output==1?.35f:1);
                        typeof(ShaderFX).GetMethod("NotifyValuesChanged",F).Invoke(effect,null);
                    }
                    host.fx.Clear(); host.fx.Add(patterned); host.fx.Add(direct); var before=Render();
                    host.fx[1]=bevel; var after=Render();
                    double maximum=0;
                    for(int i=0;i<before.Length;i++) for(int c=0;c<4;c++)
                        maximum=Math.Max(maximum,Math.Abs((double)before[i][c]-after[i][c]));
                    context.Near(0,maximum,0,$"Exact RGBA sampling parity: input {inputMode}, channel {channel}, output {output}, depth {depth}");
                }
            }
            return;
        }
        finally{foreach(var fx in effects)UnityEngine.Object.DestroyImmediate(fx);if(heightTexture!=null)UnityEngine.Object.DestroyImmediate(heightTexture);UnityEngine.Object.DestroyImmediate(doc);}
    }
}
