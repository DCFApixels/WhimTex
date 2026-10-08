// Pipeline run_script, NoiseWarpPeriodTests.Run, arguments [noiseTypeIndex, dimensions].
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoiseWarpPeriodTests
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    const int Size = 17;
    static Material Prepare(WhimTexDocument doc)
    {
        var image=doc.ComposeCanvas(); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image);
        var t=typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
        var shared=(Material)t.GetProperty("Noise",F).GetValue(null);
        var m=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(shared));
        foreach(var k in new[]{"_NoiseOneD","_NoiseThreeD","_NoisePeriodic","_NoiseSeed","_NoiseType",
            "_NoiseFractal","_NoiseOctaves","_NoiseCellularDistance","_NoiseCellularReturn","_NoiseWarp",
            "_NoiseEncoding","_NoiseInverted","_UseGradient"}) m.SetInteger(k,shared.GetInteger(k));
        foreach(var k in new[]{"_NoiseDomain","_NoiseScale","_NoiseFractalSettings","_NoiseWarpInverse","_NoiseWarpScale"})
            m.SetVector(k,shared.GetVector(k));
        foreach(var k in new[]{"_NoiseZ","_NoiseCellularJitter","_NoiseWarpStrength"})m.SetFloat(k,shared.GetFloat(k));
        m.SetVectorArray("_NoiseLattice",shared.GetVectorArray("_NoiseLattice"));m.SetInteger("_UnboundedUv",1);
        return m;
    }
    static Color[] Read(Material m, RenderTexture rt, Texture2D read, Vector4 x, Vector4 y)
    {
        var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            m.SetVector("_UvRow0",x);m.SetVector("_UvRow1",y);m.SetVector("_UvRow2",new Vector4(0,0,1,0));
            GL.sRGBWrite=false;Graphics.Blit(null,rt,m);RenderTexture.active=rt;
            read.ReadPixels(new Rect(0,0,Size,Size),0,0,false);return read.GetPixels();
        }
        finally {RenderTexture.active=active;GL.sRGBWrite=srgb;}
    }
    static string ExecuteRun(int kind=0,int dimensions=2)
    {
        var doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());doc.width=48;doc.height=32;
        var n=new NoiseLayerBehaviour {noiseType=(NoiseLayerBehaviour.NoiseType)kind,
            dimensions=dimensions==3?NoiseLayerBehaviour.NoiseDimensions.ThreeD:NoiseLayerBehaviour.NoiseDimensions.TwoD,
            seed=-139361330,offset=new Vector3(.371f,-.619f,2.371f),octaves=3,lacunarity=2.84f,
            gain=.7f,weightedStrength=.61f,warpStrength=1.4f,pingPongStrength=2.3f};
        doc.layers.Add(n);
        var rt=WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(Size,Size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
        var read=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
        Material m=null;int configs=0,samples=0;float maxRepeat=0,maxCurvature=0;
        try
        {
            foreach(var scale in new[]{new Vector2(.25f,.68f),new Vector2(.68f,.43f),new Vector2(6.3f,10.7f)})
            foreach(var multiplier in new[]{new Vector2(.25f,4),new Vector2(3,.7f),new Vector2(8,3)})
            foreach(int axes in new[]{1,2,3})
            foreach(int fractal in new[]{0,1,2,3})
            foreach(int warp in new[]{1,2,3})
            {
                n.Scale=scale;n.WarpScale=multiplier;n.periodic=(NoiseLayerBehaviour.PeriodicAxes)axes;
                n.fractal=(NoiseLayerBehaviour.FractalType)fractal;n.warp=(NoiseLayerBehaviour.WarpType)warp;
                m=Prepare(doc);
                var x=new Vector4(17f/16,0,-.28125f,0);var y=new Vector4(0,17f/16,-.15625f,0);
                var baseline=Read(m,rt,read,x,y);
                foreach(bool vertical in new[]{false,true})
                {
                    if((axes&(vertical?2:1))==0)continue;
                    var tx=x;var ty=y;if(vertical)ty.z+=1;else tx.z+=1;
                    var repeat=Read(m,rt,read,tx,ty);
                    for(int i=0;i<repeat.Length;i++)
                    {
                        float d=Mathf.Abs(repeat[i].r-baseline[i].r);
                        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(d)||float.IsInfinity(d)||d>.0001f), "Period mismatch "+kind+"/"+dimensions+"/"+scale+"/"+multiplier+"/"+axes+"/"+fractal+"/"+warp+": "+d);
                        maxRepeat=Mathf.Max(maxRepeat,d);samples++;
                    }
                    const float e=1f/65536;
                    tx=vertical?new Vector4(17f/16,0,-1f/32,0):new Vector4(Size*e,0,-8.5f*e,0);
                    ty=vertical?new Vector4(0,Size*e,-8.5f*e,0):new Vector4(0,17f/16,-1f/32,0);
                    var strip=Read(m,rt,read,tx,ty);
                    for(int p=0;p<Size;p++)
                    {
                        int center=vertical?8*Size+p:p*Size+8, step=vertical?Size:1;
                        float d=Mathf.Abs(strip[center-step].r+strip[center+step].r-2*strip[center].r);
                        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(d)||float.IsInfinity(d)), "Non-finite seam sample");
                        // Cellular boundaries and Ridged/PingPong folds may have intrinsic cusps anywhere.
                        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(kind!=2 && fractal<2 && d>.005f), "Smooth seam curvature: "+d);
                        maxCurvature=Mathf.Max(maxCurvature,d);samples++;
                    }
                }
                WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(m);m=null;configs++;
            }
            return "PASS type="+kind+" dim="+dimensions+" configs="+configs+" samples="+samples+" repeat="+maxRepeat+" curvature="+maxCurvature;
        }
        finally
        {
            if(m!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(m);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
        }
    }

    public static string Run(int kind=0,int dimensions=2) => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseWarpPeriodTests.Run", () => { ExecuteRun(kind, dimensions); });
}
