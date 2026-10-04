using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEditor;
public static class NoiseLatticePrecisionTests
{
    static string ExecuteRun()
    {
        string include=Path.GetFullPath("Packages/com.dcfapixels.whimtex/src/Shaders/NoiseLattice.cginc").Replace('\\','/');
        var shader=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ShaderUtil.CreateShaderAsset("Shader \"Hidden/WhimTex/TestLatticePrecision\" {SubShader {Pass {ZTest Always ZWrite Off Cull Off HLSLPROGRAM\n#pragma target 4.5\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n#include \""+include+"\"\nint _Octave; float4 frag(v2f_img i):SV_Target {return float4(WtPosition(_Octave,i.uv,0),1);}\nENDHLSL}}}",true));
        var material=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(shader));
        var rt=WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
        var read=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(16,16,TextureFormat.RGBAFloat,false,true));
        var previous=RenderTexture.active;
        var type=typeof(NoiseLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.NoiseLatticeSettings");
        var settings=Activator.CreateInstance(type,true);
        var method=type.GetMethod("Apply",BindingFlags.NonPublic|BindingFlags.Instance);
        double max=0;int checks=0;
        try
        {
            foreach(int dimension in new[]{2,3})foreach(int kind in new[]{0,3})
            {
                var n=new NoiseLayerBehaviour {noiseType=(NoiseLayerBehaviour.NoiseType)kind,
                    dimensions=dimension==3?NoiseLayerBehaviour.NoiseDimensions.ThreeD:NoiseLayerBehaviour.NoiseDimensions.TwoD,
                    periodic=NoiseLayerBehaviour.PeriodicAxes.X,Scale=new Vector2(.01f,.1f),offset=new Vector3(10000,-10000,10000),octaves=8,lacunarity=4,gain=1};
                method.Invoke(settings,new object[]{material,n,(double)n.Scale.x,(double)n.Scale.y});
                var data=material.GetVectorArray("_NoiseLattice");
                for(int octave=0;octave<8;octave++)
                {
                    material.SetInteger("_Octave",octave);Graphics.Blit(null,rt,material);RenderTexture.active=rt;
                    read.ReadPixels(new Rect(0,0,16,16),0,0,false);var actual=read.GetPixel(2,1);
                    for(int axis=0;axis<3;axis++)
                    {
                        int s=octave*16;
                        double value=((double)data[s+10][axis]+data[s+11][axis])+(((double)data[s][axis]+data[s+1][axis])*2.5/16)+(((double)data[s+2][axis]+data[s+3][axis])*1.5/16);
                        double expected=value-Math.Floor(value),diff=Math.Abs(actual[axis]-expected);
                        diff=Math.Min(diff,1-diff);max=Math.Max(max,diff);checks++;
                        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(diff>.0001), "Precision dimension="+dimension+" kind="+kind+" octave="+octave+" axis="+axis+" coordinate="+value+" expected="+expected+" actual="+actual[axis]);
                    }
                }
            }
            return "PASS precision checks="+checks+" max="+max;
        }
        finally {RenderTexture.active=previous;WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(shader);}
    }

    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseLatticePrecisionTests.Run", () => { ExecuteRun(); });
}
