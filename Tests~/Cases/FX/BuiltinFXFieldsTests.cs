using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class BuiltinFXFieldsTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    private static string BodyRun()
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var doc = Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>()); doc.width = doc.height = 16;
        var effects = new List<ShaderFX>(); int checks = 0;
        void Check(bool ok, string why) { T.True(ok, why); }
        ShaderFX FX(string source)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null, new[] { typeof(DCFApixels.WhimTex.TextureCompositor), typeof(string), typeof(List<DCFApixels.WhimTex.ShaderFXParameter>) }, null).Invoke(null,new object[]{doc,source,new List<ShaderFXParameter>()});
            effects.Add(fx); typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(fx,null); return fx;
        }
        ShaderFX Preset(string file) => FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/"+file+".hlsl"));
        List<ShaderFXParameter> Parameters(ShaderFX fx) => (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters",F).GetValue(fx);
        ShaderFXParameter P(ShaderFX fx,string name) => Parameters(fx).Find(p=>p.name==name);
        float Render()
        {
            var image=doc.ComposeCanvas();
            try { return image.GetPixel(8,8).r; } finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        try
        {
            Layer layer=new ColorFillLayerBehaviour(); doc.layers.Add(layer);
            var source=FX("float4 ApplyFX(float2 uv,float4 color){return float4(.37,.37,.37,1);}");
            foreach(string name in new[]{"Pixelate","Posterize"})
            {
                var fx=Preset(name); layer.fx.Clear();layer.fx.Add(source);layer.fx.Add(fx);
                P(fx,"_Levels").floatValue=64; float a=Render();
                P(fx,"_Levels").floatValue=128; float b=Render();
                Check(Mathf.Abs(a-b)>.0001f,name+" levels beyond 64 work");
                P(fx,"_Levels").floatValue=1024;P(fx,"_Gamma").floatValue=5;a=Render();
                P(fx,"_Gamma").floatValue=8;b=Render();
                Check(Mathf.Abs(a-b)>.001f,name+" gamma beyond 5 works");
            }
            var map=Preset("GradientMap");layer.fx.Clear();layer.fx.Add(source);layer.fx.Add(map);
            float normal=Render();P(map,"_Mapping").curveValue=AnimationCurve.Linear(0,1,1,1);
            Check(Render()>normal+.1f,"gradient mapping curve affects GPU output");
            var balance=Preset("ColorBalance");
            var levels=Preset("Levels");
            layer.fx.Clear(); layer.fx.Add(source); layer.fx.Add(levels);
            Check(Mathf.Abs(Render()-.37f)<.001f,"Levels default curve is neutral");
            P(levels,"_Curve").curveValue=AnimationCurve.Linear(0,1,1,0);
            Check(Mathf.Abs(Render()-.63f)<.001f,"Levels luminance curve");
            P(levels,"_PreserveColor").floatValue=0;
            P(levels,"_Gamma").floatValue=2;
            P(levels,"_OutBlack").floatValue=.2f;
            P(levels,"_OutWhite").floatValue=.8f;
            Check(Mathf.Abs(Render()-(.2f+.6f*(1-Mathf.Sqrt(.37f))))<.001f,"Levels Gamma/Curve/output order");
            var channels=FX("float4 ApplyFX(float2 uv,float4 c){return float4(.1,.3,.7,.4);}");
            layer.fx[0]=channels;
            P(levels,"_Gamma").floatValue=1; P(levels,"_OutBlack").floatValue=0; P(levels,"_OutWhite").floatValue=1;
            var result=doc.ComposeCanvas();
            try
            {
                var pixel=result.GetPixel(8,8);
                Check(Mathf.Abs(pixel.r-.9f)<.002f && Mathf.Abs(pixel.g-.7f)<.002f && Mathf.Abs(pixel.b-.3f)<.002f && Mathf.Abs(pixel.a-.4f)<.002f,"Levels independent RGB and alpha");
            }
            finally { UnityEngine.Object.DestroyImmediate(result); }
            var old=new List<ShaderFXParameter>();
            foreach(var p in Parameters(balance))
            {
                if(p.type!=ShaderFXParameterType.Vector3)continue;
                old.Add(new ShaderFXParameter{name=p.name,type=ShaderFXParameterType.Vector,vectorValue=new Vector4(.2f,-.3f,.4f,99)});
            }
            typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata").GetMethod("PreserveValues",F)
                .Invoke(null,new object[]{Parameters(balance),old});
            foreach(var p in old)Check(P(balance,p.name).vectorValue==p.vectorValue,"RGB values retained when removing unused W");
            return null;
        }
        finally { foreach(var fx in effects) UnityEngine.Object.DestroyImmediate(fx); UnityEngine.Object.DestroyImmediate(doc); }
    }
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}

