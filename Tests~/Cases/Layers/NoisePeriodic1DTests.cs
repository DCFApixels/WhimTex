// Pipeline run_script: NoisePeriodic1DTests.Run, args [noiseTypeIndex]. Temporary objects only.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoisePeriodic1DTests
{
    const int Size = 17;
    static Material Prepare(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas(); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image);
        var type = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
        var shared = (Material)type.GetProperty("Noise", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
        var m = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(shared));
        foreach (var k in new[]{"_NoiseOneD","_NoiseThreeD","_NoisePeriodic","_NoiseSeed","_NoiseType",
            "_NoiseFractal","_NoiseOctaves","_NoiseCellularDistance","_NoiseCellularReturn","_NoiseWarp",
            "_NoiseEncoding","_NoiseInverted","_UseGradient"}) m.SetInteger(k, shared.GetInteger(k));
        foreach (var k in new[]{"_NoiseDomain","_NoiseScale","_NoiseAxis","_NoiseFractalSettings","_NoiseWarpInverse","_NoiseWarpScale"})
            m.SetVector(k, shared.GetVector(k));
        foreach (var k in new[]{"_NoiseZ","_NoiseCellularJitter","_NoiseWarpStrength"}) m.SetFloat(k, shared.GetFloat(k));
        m.SetVectorArray("_NoiseLattice", shared.GetVectorArray("_NoiseLattice")); m.SetInteger("_UnboundedUv", 1);
        return m;
    }
    static Color[] Read(Material m, RenderTexture rt, Texture2D read, float phaseStart, float phaseSpan, float transverse)
    {
        var a = m.GetVector("_NoiseAxis"); var w = new Vector2(a.z, a.w);
        var along = w / w.sqrMagnitude; var across = new Vector2(-w.y, w.x);
        var start = Vector2.one * .5f + along * (phaseStart - .5f) + across * transverse;
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            m.SetVector("_UvRow0", new Vector4(along.x * phaseSpan, 0, start.x, 0));
            m.SetVector("_UvRow1", new Vector4(along.y * phaseSpan, 0, start.y, 0));
            m.SetVector("_UvRow2", new Vector4(0,0,1,0));
            GL.sRGBWrite = false; Graphics.Blit(null, rt, m); RenderTexture.active = rt;
            read.ReadPixels(new Rect(0,0,Size,Size),0,0,false); return read.GetPixels();
        }
        finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
    }
    static string ExecuteRun(int kind = 0)
    {
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = 48; doc.height = 32;
        var n = new NoiseLayerBehaviour { noiseType = (NoiseLayerBehaviour.NoiseType)kind,
            dimensions = NoiseLayerBehaviour.NoiseDimensions.OneD, periodic1D = true,
            periodic = NoiseLayerBehaviour.PeriodicAxes.Y, seed = -139361330,
            offset = new Vector3(.371f,-.619f,2.371f), octaves = 3, lacunarity = 2.84f,
            gain = .7f, weightedStrength = .61f, warpStrength = 1.4f, pingPongStrength = 2.3f };
        doc.layers.Add(n);
        var rt = WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(Size,Size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
        var read = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true));
        Material m = null; int configs = 0; float maxRepeat = 0, maxAcross = 0, maxSeam = 0;
        void Compare(Color[] a, Color[] b, ref float maximum, string label)
        {
            for(int i=0;i<a.Length;i++)
            {
                float d = Mathf.Abs(a[i].r-b[i].r); maximum = Mathf.Max(maximum,d);
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(d) || float.IsInfinity(d) || d > .0002f), label+" type="+kind+" angle="+n.direction+" scale="+n.Scale+" warp="+n.warp+" fractal="+n.fractal+": "+d);
            }
        }
        try
        {
            foreach(var scale in new[]{new Vector2(.25f,.68f),new Vector2(6.3f,10.7f)})
            foreach(float angle in new[]{0f,90f,37f,-125f,180f})
            foreach(int fractal in new[]{0,1,2,3})
            foreach(int warp in new[]{0,1,2,3})
            {
                n.Scale = scale; n.direction = angle; n.WarpScale = new Vector2(3,.7f);
                n.fractal = (NoiseLayerBehaviour.FractalType)fractal; n.warp = (NoiseLayerBehaviour.WarpType)warp;
                m = Prepare(doc);
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(m.GetInteger("_NoisePeriodic")!=1 || m.GetInteger("_NoiseOneD")!=1), "1D must use its own scalar period, independently of 2D edge selection");
                var baseline = Read(m,rt,read,-.28125f,17f/16,0);
                Compare(baseline,Read(m,rt,read,.71875f,17f/16,0),ref maxRepeat,"Period");
                Compare(baseline,Read(m,rt,read,-.28125f,17f/16,.371f),ref maxAcross,"Transverse invariance");
                const float e = 1f/65536;
                var seam = Read(m,rt,read,-8.5f*e,Size*e,0);
                float curvature = Mathf.Abs(seam[7].r+seam[9].r-2*seam[8].r);
                WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(curvature) || float.IsInfinity(curvature) || (kind!=2 && fractal<2 && curvature>.005f)), "Seam curvature "+curvature);
                maxSeam = Mathf.Max(maxSeam,curvature);
                WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(m); m = null; configs++;
            }
            // A stored 2D edge selection must have no effect while the 1D checkbox is off.
            n.periodic1D=false; m=Prepare(doc);
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(m.GetInteger("_NoisePeriodic")!=0 || m.IsKeywordEnabled("WT_NOISE_PERIODIC")), "Disabled 1D Seamless leaked 2D periodicity");
            return "PASS 1D type="+kind+" configs="+configs+" repeat="+maxRepeat+" across="+maxAcross+" seam="+maxSeam;
        }
        finally
        {
            if(m!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(m);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(read);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
        }
    }

    public static string Run(int kind = 0) => WhimTex.Tests.UnityC.FixtureContext.Run("NoisePeriodic1DTests.Run", () => { ExecuteRun(kind); });
}
