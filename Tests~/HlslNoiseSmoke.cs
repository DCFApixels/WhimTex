using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class HlslNoiseSmoke
{
    public static string Main()
    {
        var stat=BindingFlags.Static|BindingFlags.NonPublic;
        var inst=BindingFlags.Instance|BindingFlags.NonPublic;
        var programType=typeof(WhimTexApi).Assembly.GetType("DCFApixels.WhimTex.BrushTipProgram");
        var program=Activator.CreateInstance(programType,true);
        int checks=0;
        string body="fnl_state s=fnlCreateState(123); s.frequency=1; s.noise_type=TYPE; s.fractal_type=FNL_FRACTAL_FBM; s.octaves=3; float2 p=uv*8; s.domain_warp_amp=0.5; fnlDomainWarp2D(s,p.x,p.y); float n=fnlGetNoise2D(s,p.x,p.y)*0.5+0.5; float z=0.5; fnlDomainWarp3D(s,p.x,p.y,z); float m=fnlGetNoise3D(s,p.x,p.y,z)*0.5+0.5; return float4(n,m,n,1);";
        try
        {
            foreach(string type in new[]{"FNL_NOISE_OPENSIMPLEX2","FNL_NOISE_OPENSIMPLEX2S","FNL_NOISE_CELLULAR","FNL_NOISE_PERLIN","FNL_NOISE_VALUE_CUBIC","FNL_NOISE_VALUE"})
            {
                string code=body.Replace("TYPE",type);
                Texture2D tip=null;
                ShaderFX fx=null;
                try
                {
                    tip=(Texture2D)programType.GetMethod("Bake",inst).Invoke(program,new object[]{
                        "// @whimtex-brush Test/Noise\nfloat4 BrushTip(float2 uv){"+code+"}",
                        new List<ShaderFXParameter>(),32});
                    var a=tip.GetPixel(3,7);var b=tip.GetPixel(20,15);
                    if(float.IsNaN(a.r)||float.IsInfinity(a.r)||a==b)throw new Exception("Invalid noise pixels: "+type);
                    checks++;
                    string source="#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl\"\nfloat4 ApplyFX(float2 uv,float4 color){"+code+"}";
                    fx=(ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft",stat).Invoke(null,new object[]{null,source,new List<ShaderFXParameter>()});
                    typeof(ShaderFX).GetMethod("ApplyAgentDraft",inst).Invoke(fx,null);
                    checks++;
                }
                finally
                {
                    if(tip!=null)UnityEngine.Object.DestroyImmediate(tip);
                    if(fx!=null)UnityEngine.Object.DestroyImmediate(fx);
                }
            }
        }
        finally{((IDisposable)program).Dispose();}
        return "HLSL noise checks: "+checks+" (6 noise types, 2D/3D, warp, brushes, FX, duplicate include)";
    }
}
