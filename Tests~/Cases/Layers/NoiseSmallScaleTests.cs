using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // CPU-prepared lattice bases. Each of eight octaves and the warp has its own
    // integer period; non-integral lacunarity therefore cannot break the tile.
    internal sealed class PrototypeNoiseLatticeSettings
    {
        private const int Stride = 16;
        private static float Limit(float v, float lo, float hi, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, lo, hi);
        private readonly Vector4[] data = new Vector4[9 * Stride];
        private static readonly int DataId = Shader.PropertyToID("_NoiseLattice");
        private static readonly int InverseId = Shader.PropertyToID("_NoiseWarpInverse");

        internal void Apply(Material material, NoiseLayerBehaviour noise, double width, double height, bool improved)
        {
            double frequency = 1;
            bool three = noise.dimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD;
            Vector2 warpScale = noise.WarpScale;
            for (int octave = 0; octave < 9; octave++)
            {
                bool warp = octave == 8;
                double f = warp ? 1 : frequency;
                double fx = warp ? warpScale.x : f;
                double fy = warp ? warpScale.y : f;
                frequency *= Limit(noise.lacunarity, 1, 4, 2);
                int count = noise.fractal == NoiseLayerBehaviour.FractalType.None ? 1 : Mathf.Clamp(noise.octaves, 1, 8);
                if (!warp && octave >= count) continue;
                bool simplex = warp ? noise.warp != NoiseLayerBehaviour.WarpType.BasicGrid : (int)noise.noiseType < 2;
                int layout = simplex ? (three ? 3 : 2) : 1;
                double unitX = simplex ? (three ? 3 : Math.Sqrt(2.0 / 3.0)) : 1;
                double unitY = simplex ? (three ? 3 : Math.Sqrt(2)) : 1;
                bool repeatX = ((int)noise.periodic & 1) != 0;
                bool repeatY = ((int)noise.periodic & 2) != 0;
                // Compare production's base rounding with per-octave-only rounding.
                double cellsX = !improved && !warp && simplex && !three ? Math.Max(1, Math.Round(width / unitX)) * f : width * fx / unitX;
                double cellsY = !improved && !warp && simplex && !three ? Math.Max(1, Math.Round(height / unitY)) * f : height * fy / unitY;
                double countX = Math.Max(1, Math.Round(cellsX, MidpointRounding.AwayFromZero));
                double countY = Math.Max(1, Math.Round(cellsY, MidpointRounding.AwayFromZero));
                double sx = repeatX ? countX * unitX / width : fx;
                double sy = repeatY ? countY * unitY / height : fy;
                double px = repeatX ? countX * (simplex && three ? 3 : 1) : 0;
                double py = repeatY ? countY * (simplex && three ? 3 : 1) : 0;
                // Protect integer lattice arithmetic on extremely elongated canvases.
                if (Math.Max(width * sx, height * sy) > 100000000 || Math.Max(px, py) > 100000000)
                    throw new InvalidOperationException("Noise lattice is too large. Reduce Scale, Octaves, Lacunarity or canvas aspect ratio.");
                int start = octave * Stride;
                void Basis(double x, double y, double z, int slot)
                {
                    Transform(ref x, ref y, ref z, layout, sx, sy, f);
                    if (slot == 5)
                    {
                        var integers = new Vector4();
                        var tails = new Vector4();
                        for (int axis = 0; axis < 3; axis++)
                        {
                            double value = axis == 0 ? x : axis == 1 ? y : z;
                            if (Math.Abs(value) > 500000000)
                                throw new InvalidOperationException("Noise coordinates are too large. Reduce Offset, Scale or fractal detail.");
                            int whole = (int)Math.Floor(value);
                            integers[axis] = whole >> 12; tails[axis] = whole & 4095;
                            if (axis == 0) x -= whole; else if (axis == 1) y -= whole; else z -= whole;
                        }
                        data[start + 13] = integers; data[start + 14] = tails;
                    }
                    Split(start + slot * 2, x, y, z);
                }
                Basis(width, 0, 0, 0);
                Basis(0, height, 0, 1);
                Basis(1, 0, 0, 2);
                Basis(0, 1, 0, 3);
                Basis(0, 0, 1, 4);
                double ox = Limit(noise.offset.x, -10000, 10000, 0);
                double oy = Limit(noise.offset.y, -10000, 10000, 0);
                if (repeatX) ox %= width;
                if (repeatY) oy %= height;
                Basis(-width * .5 + ox, -height * .5 + oy,
                    three ? Limit(noise.offset.z, -10000, 10000, 0) : 0, 5);
                // Normal floats only: subnormal bit-casts can flush to zero in dynamic GPU loads.
                int ix = (int)px, iy = (int)py;
                data[start + 12] = new Vector4(ix >> 12, iy >> 12, layout, (ix & 4095) | ((iy & 4095) << 12));
                if (warp) material.SetVector(InverseId, new Vector4((float)(fx / sx), (float)(fy / sy), 1, layout));
            }
            material.SetVectorArray(DataId, data);
        }

        private void Split(int index, double x, double y, double z)
        {
            var high = new Vector4((float)x, (float)y, (float)z, 0);
            data[index] = high;
            data[index + 1] = new Vector4((float)(x - high.x), (float)(y - high.y), (float)(z - high.z), 0);
        }

        private static void Transform(ref double x, ref double y, ref double z, int layout, double sx, double sy, double sz)
        {
            x *= sx; y *= sy; z *= sz;
            if (layout == 2)
            {
                double a = (x - y) / Math.Sqrt(2), b = (x + y) / Math.Sqrt(2);
                double skew = (a + b) * ((Math.Sqrt(3) - 1) * .5);
                x = a + skew; y = b + skew;
            }
            else if (layout == 3)
            {
                double r = (x + y + z) * (2.0 / 3.0);
                x = r - x; y = r - y; z = r - z;
            }
        }
    }
}


public static class NoiseSmallScaleTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Output => WhimTex.Tests.UnityC.FixtureContext.Scope.Temp;
    static Material Prepare(WhimTexDocument doc)
    {
        var image = doc.ComposeCanvas();
        WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image);
        var type = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
        var shared = (Material)type.GetProperty("Noise", Flags).GetValue(null);
        var material = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(shared) { hideFlags = HideFlags.HideAndDontSave });
        foreach(var key in new[]{"_NoiseOneD","_NoisePeriodic","_NoiseSeed","_NoiseType",
            "_NoiseFractal","_NoiseOctaves","_NoiseCellularDistance","_NoiseCellularReturn","_NoiseWarp",
            "_NoiseEncoding","_NoiseInverted","_NoiseField","_NoiseVectorOutput","_NoiseNormalize","_NoiseWarpSeed","_GrainColor","_UseGradient","_GradientWrapMode"})
            material.SetInteger(key,shared.GetInteger(key));
        foreach(var key in new[]{"_NoiseDomain","_NoiseFractalSettings","_NoiseAxis","_NoiseScale","_GrainGrid","_NoiseWarpInverse","_NoiseWarpScale"})
            material.SetVector(key,shared.GetVector(key));
        foreach(var key in new[]{"_NoiseZ","_NoiseCellularJitter","_NoiseWarpStrength","_NoiseStrength"})
            material.SetFloat(key,shared.GetFloat(key));
        // The ordinary-noise path does not upload the periodic lattice anymore.
        var lattice = shared.GetVectorArray("_NoiseLattice");
        if (lattice != null && lattice.Length != 0) material.SetVectorArray("_NoiseLattice", lattice);
        material.SetInteger("_UnboundedUv", 1);
        return material;
    }
    static Texture2D Render(Material material, int size, Vector4 x, Vector4 y)
    {
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var rt = WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var image = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
        try
        {
            material.SetVector("_UvRow0", x); material.SetVector("_UvRow1", y);
            material.SetVector("_UvRow2", new Vector4(0, 0, 1, 0));
            GL.sRGBWrite = false; Graphics.Blit(null, rt, material); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0, false); image.Apply(false);
            return image;
        }
        catch { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(image); throw; }
        finally { RenderTexture.active = active; GL.sRGBWrite = srgb; WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); }
    }
    static Texture2D Tile(Material material, int size) =>
        Render(material, size, new Vector4(1,0,0,0), new Vector4(0,1,0,0));
    static void Save(Texture2D image, string path)
    {
        var pixels = image.GetPixels();
        for (int i=0;i<pixels.Length;i++) { pixels[i] = pixels[i].gamma; pixels[i].a = 1; }
        var png = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(image.width,image.height,TextureFormat.RGBA32,false));
        try { png.SetPixels(pixels); png.Apply(); File.WriteAllBytes(path,png.EncodeToPNG()); }
        finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(png); }
    }
    static string ExecuteRun(int kind = 0)
    {
        Directory.CreateDirectory(Output);
        var doc = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width=doc.height=32;
        var n = new NoiseLayerBehaviour {
            noiseType=(NoiseLayerBehaviour.NoiseType)kind, seed=-139361330,
            offset=new Vector3(723.9f,-340.202026f,0), fractal=NoiseLayerBehaviour.FractalType.PingPong,
            octaves=3,lacunarity=2.84f,gain=1,weightedStrength=.61f,pingPongStrength=1,
            warp=NoiseLayerBehaviour.WarpType.BasicGrid,warpStrength=.5f,
            encoding=NoiseLayerBehaviour.OutputEncoding.ColorValues };
        doc.layers.Add(n);
        typeof(WhimTexDocument).GetMethod("NormalizeModel",Flags).Invoke(doc,null);
        var candidate = new PrototypeNoiseLatticeSettings();
        var log = new StringBuilder("scale,octave,oldX,oldY,newX,newY\n");
        Material material=null; Texture2D sheet=null;
        float maxBoundary=0,maxCurvature=0,maxBaseline=0; int configs=0;
        try
        {
            sheet = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(256*3,256*4,TextureFormat.RGBAFloat,false,true));
            float[] scales={.25f,.5f,.68f,1f};
            for(int row=0;row<scales.Length;row++)
            {
                float scale=scales[row]; n.Scale=new Vector2(scale,scale);
                for(int col=0;col<3;col++)
                {
                    n.periodic=col==0?NoiseLayerBehaviour.PeriodicAxes.None:NoiseLayerBehaviour.PeriodicAxes.XY;
                    material=Prepare(doc);
                    if(col==1)candidate.Apply(material,n,scale,scale,false);
                    if(col==2)candidate.Apply(material,n,scale,scale,true);
                    var tile=Tile(material,256);
                    sheet.SetPixels(col*256,(3-row)*256,256,256,tile.GetPixels());
                    Save(tile,Output+"/type"+kind+"-scale"+scale.ToString("0.00",CultureInfo.InvariantCulture)+"-"+new[]{"ordinary","current","candidate"}[col]+".png");
                    if(row==2 && col==2)
                    {
                        var repeat=Render(material,512,new Vector4(2,0,0,0),new Vector4(0,2,0,0));
                        Save(repeat,Output+"/type"+kind+"-candidate-068-repeat.png");
                        WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(repeat);
                    }
                    if(col==2)
                    {
                        // Recreate the production material and compare against the frozen candidate.
                        WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);material=Prepare(doc);
                        var old=Tile(material,256); var a=old.GetPixels();var b=tile.GetPixels();
                        for(int i=0;i<a.Length;i++) maxBaseline=Mathf.Max(maxBaseline,Mathf.Abs(a[i].r-b[i].r));
                        WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(old);
                    }
                    WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(tile); WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material); material=null;
                }
                for(int octave=0;octave<3;octave++)
                {
                    double f=Math.Pow(n.lacunarity,octave);
                    double ux=Math.Sqrt(2.0/3.0),uy=Math.Sqrt(2);
                    Func<double,int> round=v=>(int)Math.Max(1,Math.Round(v,MidpointRounding.AwayFromZero));
                    log.AppendLine(scale.ToString(CultureInfo.InvariantCulture)+","+octave+","+
                        round(Math.Max(1,Math.Round(scale/ux))*f)+","+round(Math.Max(1,Math.Round(scale/uy))*f)+","+
                        round(scale*f/ux)+","+round(scale*f/uy));
                }
            }
            Save(sheet,Output+"/type"+kind+"-comparison.png");
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);sheet=null;
            n.encoding=NoiseLayerBehaviour.OutputEncoding.LinearData;
            foreach(float scale in new[]{.25f,.68f,1f})
            foreach(int axes in new[]{1,2,3})
            foreach(int fractal in new[]{0,1,2,3})
            foreach(int warp in new[]{0,1,2,3})
            {
                n.Scale=new Vector2(scale,scale*.73f);n.periodic=(NoiseLayerBehaviour.PeriodicAxes)axes;
                n.fractal=(NoiseLayerBehaviour.FractalType)fractal;n.warp=(NoiseLayerBehaviour.WarpType)warp;
                material=Prepare(doc);
                foreach(bool vertical in new[]{false,true})
                {
                    if((axes&(vertical?2:1))==0)continue;
                    // Sample on both sides of the actual boundary, not only at wrapped endpoints.
                    const float epsilon=1f/65536; const int size=33;
                    var across=new Vector4(size*epsilon,0,-size*.5f*epsilon,0);
                    var along=new Vector4(0,size/32f,-1f/64,0);
                    var strip=Render(material,size,vertical?new Vector4(size/32f,0,-1f/64,0):across,
                        vertical?new Vector4(0,size*epsilon,-size*.5f*epsilon,0):along);
                    for(int p=0;p<size;p++)
                    {
                        float left=vertical?strip.GetPixel(p,15).r:strip.GetPixel(15,p).r;
                        float center=vertical?strip.GetPixel(p,16).r:strip.GetPixel(16,p).r;
                        float right=vertical?strip.GetPixel(p,17).r:strip.GetPixel(17,p).r;
                        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(float.IsNaN(center)||float.IsInfinity(center)), "Non-finite sample");
                        maxBoundary=Mathf.Max(maxBoundary,Mathf.Abs(left-right));
                        maxCurvature=Mathf.Max(maxCurvature,Mathf.Abs(left+right-2*center));
                    }
                    WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(strip);
                }
                WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);material=null;configs++;
            }
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(maxBaseline>1e-6f), "Production differs from the approved candidate: "+maxBaseline);
            WhimTex.Tests.UnityC.FixtureContext.Context.True(!(maxCurvature>.003f), "Boundary curvature outlier: "+maxCurvature);
            File.WriteAllText(Output+"/type"+kind+"-periods.csv",log.ToString());
            return "PASS configs="+configs+" baselineError="+maxBaseline+" maxBoundaryStep="+maxBoundary+
                " maxSecondDifference="+maxCurvature+" comparison="+Path.GetFullPath(Output+"/type"+kind+"-comparison.png");
        }
        finally
        {
            if(sheet!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);
            if(material!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);
            WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);
        }
    }

    public static string Run(int kind = 0) => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseSmallScaleTests.Run", () => { ExecuteRun(kind); });
}
