using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Opt-in through Pipeline run_script; not part of the shipped Editor assembly.
public static class ScreenedSeamlessTests
{
    static int checks;
    static double maxError;
    static MakeSeamlessLayerBehaviour.PoissonEdges edges;
    static bool WrapX => edges != MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom;
    static bool WrapY => edges != MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight;
    static void Check(bool ok,string text) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, text); checks++; }
    static RenderTexture Render(Texture t) => (RenderTexture)typeof(MakeSeamlessLayerBehaviour).Assembly
        .GetType("DCFApixels.WhimTex.ScreenedSeamless").GetMethod("RenderConfigured",BindingFlags.Static|BindingFlags.NonPublic)
        .Invoke(null,new object[]{t,t.width,t.height,edges,.05f});
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try { RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels(); }
        finally { RenderTexture.active=old;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t); }
    }
    static double[] P(double[] a,int w,int h)
    {
        var b=(double[])a.Clone();
        for(int axis=0;axis<2;axis++)
        {
            if(axis==0 ? !WrapX : !WrapY)continue;
            int n=axis==0?w:h,lines=axis==0?h:w;
            if(n==1)continue;
            int Index(int k,int line)=>axis==0?line*w+k:k*w+line;
            for(int line=0;line<lines;line++)
            {
                if(n<4) { double sum=0;for(int k=0;k<n;k++)sum+=b[Index(k,line)];for(int k=0;k<n;k++)b[Index(k,line)]=sum/n;continue; }
                var v=new double[4];for(int k=0;k<4;k++)v[k]=b[Index(k<2?n-2+k:k-2,line)];
                for(int k=0;k<4;k++) { double sum=0;for(int j=0;j<4;j++)sum+=v[j]*(.25+(k-1.5)*(j-1.5)/5);b[Index(k<2?n-2+k:k-2,line)]=sum; }
            }
        }
        return b;
    }
    static double[] Lap(double[] a,int w,int h,double lambda)
    {
        var b=new double[a.Length];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)b[y*w+x]=(4+lambda)*a[y*w+x]
            -a[y*w+(WrapX?(x+1)%w:Math.Min(x+1,w-1))]-a[y*w+(WrapX?(x+w-1)%w:Math.Max(x-1,0))]
            -a[(WrapY?(y+1)%h:Math.Min(y+1,h-1))*w+x]-a[(WrapY?(y+h-1)%h:Math.Max(y-1,0))*w+x];
        return b;
    }
    static double Dot(double[] a,double[] b) { double sum=0;for(int i=0;i<a.Length;i++)sum+=a[i]*b[i];return sum; }
    // Independent double-precision UNPRECONDITIONED CG, no GPU or FFT reference.
    static double[] Solve(double[] u,int w,int h,double screening = -1)
    {
        double lambda=screening>0?screening:1/Math.Pow(.05*Math.Min(w,h),2);
        var rhs=Lap(u,w,h,lambda);
        for(int y=0;y<h;y++)if(w>1&&WrapX)
        {
            int o=y*w;double d=.5*((u[o+1]-u[o])+(u[o+w-1]-u[o+w-2]))-(u[o]-u[o+w-1]);
            rhs[o]+=d;rhs[o+w-1]-=d;
        }
        for(int x=0;x<w;x++)if(h>1&&WrapY)
        {
            double d=.5*((u[w+x]-u[x])+(u[(h-1)*w+x]-u[(h-2)*w+x]))-(u[x]-u[(h-1)*w+x]);
            rhs[x]+=d;rhs[(h-1)*w+x]-=d;
        }
        rhs=P(rhs,w,h);var result=P(u,w,h);var ar=P(Lap(result,w,h,lambda),w,h);
        var r=new double[u.Length];for(int i=0;i<u.Length;i++)r[i]=rhs[i]-ar[i];
        var p=(double[])r.Clone();double rr=Dot(r,r);
        for(int step=0;step<4000 && rr>1e-22;step++)
        {
            var ap=P(Lap(p,w,h,lambda),w,h);double alpha=rr/Dot(p,ap);
            for(int i=0;i<u.Length;i++){result[i]+=alpha*p[i];r[i]-=alpha*ap[i];}
            double next=Dot(r,r);for(int i=0;i<u.Length;i++)p[i]=r[i]+next/rr*p[i];rr=next;
        }
        Check(rr<1e-18,"CPU reference did not converge");return P(result,w,h);
    }
    static Color[] Reference(Color[] u,int w,int h)
    {
        var result=new Color[u.Length];
        for(int c=0;c<4;c++)
        {
            var a=new double[u.Length];for(int i=0;i<a.Length;i++)a[i]=c==3?u[i].a:u[i][c]*u[i].a;
            var b=Solve(a,w,h);for(int i=0;i<a.Length;i++)result[i][c]=(float)b[i];
        }
        for(int i=0;i<u.Length;i++){var c=result[i];result[i]=c.a>.000001f?new Color(c.r/c.a,c.g/c.a,c.b/c.a,Mathf.Clamp01(c.a)):Color.clear;}
        return result;
    }
    static string ExecuteMain()
    {
        checks=0;maxError=0;
        var shader=Shader.Find("Hidden/TextureCompositor/ScreenedSeamless");Check(shader!=null&&shader.isSupported,"Shader unavailable");
        var rng=new System.Random(312);
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(1,7),new Vector2Int(8,1),new Vector2Int(2,3),
            new Vector2Int(4,8),new Vector2Int(6,10),new Vector2Int(17,13),new Vector2Int(32,32),new Vector2Int(64,48)})
        for(int direction=0;direction<3;direction++)for(int variant=0;variant<4;variant++)
        {
            edges=(MakeSeamlessLayerBehaviour.PoissonEdges)direction;
            int w=size.x,h=size.y;var input=new Color[w*h];
            for(int i=0;i<input.Length;i++)input[i]=variant==0?new Color(.3f,.6f,1.5f,.7f):
                new Color((float)rng.NextDouble()*2-.3f,(float)rng.NextDouble(),(float)rng.NextDouble()*3,
                    variant==1?1:variant==3?0:.3f+.7f*(float)rng.NextDouble());
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));RenderTexture rt=null;
            try
            {
                t.SetPixels(input);t.Apply(false,false);var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
                rt=Render(t);Check(RenderTexture.active==active&&GL.sRGBWrite==srgb,"Render state leak");
                var actual=Read(rt);var expected=Reference(input,w,h);
                for(int i=0;i<input.Length;i++)for(int c=0;c<4;c++)
                {
                    double error=Math.Abs(actual[i][c]-expected[i][c]);maxError=Math.Max(maxError,error);
                    Check(!float.IsNaN(actual[i][c])&&error<.00002*Math.Max(1,Math.Abs(expected[i][c])),
                        $"Mismatch {w}x{h}, variant {variant}, index {i}, channel {c}: {actual[i][c]} vs {expected[i][c]}");
                }
            }
            finally{if(rt!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        foreach(var msg in ShaderUtil.GetShaderMessages(shader))Check(msg.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,msg.message);
        var layer=new MakeSeamlessLayerBehaviour{mode=MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson};
        Check(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(layer)).mode==layer.mode,"Mode roundtrip");
        return $"Screened Poisson: {checks} checks, maximum absolute reference error {maxError:G6}.";
    }
    static string ExecutePreview(byte[] input)
    {
        string dir=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temp+"/";var t=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(2,2,TextureFormat.RGBA32,false,false));RenderTexture rt=null;
        try
        {
            t.LoadImage(input,false);
            var decoded=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(t.width,t.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
            var material=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(Shader.Find("Hidden/TextureCompositor/ScreenedSeamless")));
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=false;Graphics.Blit(t,decoded,material,0);
                using(var writer=new BinaryWriter(File.Create(Path.Combine(dir,"05_unity_input_float.bin"))))
                    foreach(var p in Read(decoded)){writer.Write(p.r);writer.Write(p.g);writer.Write(p.b);writer.Write(p.a);}
            }
            finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(decoded);global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);}
            rt=Render(t);var pixels=Read(rt);
            using(var writer=new BinaryWriter(File.Create(Path.Combine(dir,"05_unity_gpu_float.bin"))))
                foreach(var p in pixels){writer.Write(p.r);writer.Write(p.g);writer.Write(p.b);writer.Write(p.a);}
            var png=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,false));
            try {for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;png.SetPixels(pixels);png.Apply(false,false);File.WriteAllBytes(Path.Combine(dir,"05_unity_gpu.png"),png.EncodeToPNG());}
            finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(png);}
            return "GPU preview written: "+dir;
        }
        finally {if(rt!=null)global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static string ExecuteLarge()
    {
        edges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;
        checks=0;maxError=0;
        foreach(var size in new[]{new Vector2Int(600,600),new Vector2Int(257,511),new Vector2Int(1024,512),new Vector2Int(4,1024)})
        {
            int w=size.x,h=size.y;double lambda=1/Math.Pow(.05*Math.Min(w,h),2);
            var xx=new double[w];var yy=new double[h];
            for(int i=0;i<w;i++)xx[i]=(double)i/w;
            for(int i=0;i<h;i++)yy[i]=(double)i/h;
            var ex=Solve(xx,w,1,lambda);var ey=Solve(yy,1,h,lambda);
            var values=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)values[y*w+x]=new Color((float)(.15+.7*xx[x]),(float)(.1+.5*yy[y]),(float)(.2+.2*xx[x]+.3*yy[y]),1);
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));RenderTexture rt=null;
            try
            {
                t.SetPixels(values);t.Apply(false,false);rt=Render(t);var pixels=Read(rt);
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    var expected=new Color((float)(.15+.7*ex[x]),(float)(.1+.5*ey[y]),(float)(.2+.2*ex[x]+.3*ey[y]),1);
                    for(int c=0;c<4;c++)
                    {
                        double error=Math.Abs(pixels[y*w+x][c]-expected[c]);maxError=Math.Max(maxError,error);
                        Check(error<.0001,$"Large reference mismatch {w}x{h}, {x},{y}, channel {c}: {error}");
                    }
                }
            }
            finally{if(rt!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        return $"Screened large/NPOT: {checks} checks, maximum error {maxError:G6}.";
    }
    static string ExecuteBenchmark()
    {
        edges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;
        var text=new System.Text.StringBuilder("GPU render + sync float readback, median 3: ");
        foreach(int n in new[]{256,512,1024})
        {
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(n,n,TextureFormat.RGBAFloat,false,true));
            try
            {
                var pixels=new Color[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++)pixels[y*n+x]=new Color((float)x/n,(float)y/n,.3f,1);
                t.SetPixels(pixels);t.Apply(false,false);var times=new double[3];
                for(int k=-1;k<3;k++){var timer=Stopwatch.StartNew();var rt=Render(t);try{Read(rt);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}timer.Stop();if(k>=0)times[k]=timer.Elapsed.TotalMilliseconds;}
                Array.Sort(times);text.Append($"{n}: {times[1]:F2} ms; ");
            }
            finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        return text.ToString();
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("ScreenedSeamlessTests.Main", () => { ExecuteMain(); });

    public static string Preview() => global::WhimTex.Tests.UnityC.ReviewedOracle.Image("ScreenedSeamlessTests.Preview", "screened-poisson-original", ExecutePreview);

    public static string Large() => WhimTex.Tests.UnityC.FixtureContext.Run("ScreenedSeamlessTests.Large", () => { ExecuteLarge(); });

    public static string Benchmark() => WhimTex.Tests.UnityC.FixtureContext.Diagnostic("ScreenedSeamlessTests.Benchmark", ExecuteBenchmark);
}
