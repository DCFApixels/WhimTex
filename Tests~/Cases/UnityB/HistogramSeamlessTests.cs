using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static class HistogramSeamlessTests
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly MethodInfo Method = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.HistogramSeamless").GetMethod("Render",Flags);
    static RenderTexture Render(Texture t) => (RenderTexture)Method.Invoke(null,new object[]{t,t.width,t.height});
    static int checks;
    static void Check(bool b,string s) { UnityBRun.Check(!(!b), s);checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;
        var t=UnityBRun.Track(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try {RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally {RenderTexture.active=old;Object.DestroyImmediate(t);}
    }
    static string ExecuteMain()
    {
        checks=0;
        var shader=Shader.Find("Hidden/TextureCompositor/HistogramSeamless");
        Check(shader!=null && shader.isSupported,"Shader support");
        foreach(var msg in ShaderUtil.GetShaderMessages(shader))Check(msg.severity.ToString()!="Error",msg.message);
        var rng=new System.Random(928);
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(1,9),new Vector2Int(7,1),new Vector2Int(2,3),new Vector2Int(17,13),new Vector2Int(64,48),new Vector2Int(257,259)})
        for(int scenario=0;scenario<5;scenario++)
        {
            int w=size.x,h=size.y;
            var t=UnityBRun.Track(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));RenderTexture rt=null;
            try
            {
                var input=new Color[w*h];
                for(int i=0;i<input.Length;i++)
                {
                    float v=(float)rng.NextDouble();
                    input[i]=scenario==0?new Color(-.25f,2,.4f,.3f):scenario==1?new Color(5,7,9,0):
                        scenario==2?new Color(v,v,v,1):scenario==3?new Color(v,(float)rng.NextDouble(),(float)rng.NextDouble(),1):
                        new Color(v*3,(float)rng.NextDouble()*2-1,(float)rng.NextDouble(),(float)rng.NextDouble());
                }
                t.SetPixels(input);t.Apply(false,false);
                var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
                rt=Render(t);
                Check(RenderTexture.active==old && GL.sRGBWrite==srgb,"Render state");
                var result=Read(rt);
                for(int i=0;i<result.Length;i++)
                {
                    var c=result[i];int x=i%w,y=i/w;
                    for(int channel=0;channel<4;channel++)Check(!float.IsNaN(c[channel])&&!float.IsInfinity(c[channel]),"Finite");
                    Check(c.a>=0 && c.a<=1,"Alpha bounds");
                    if(scenario<2)for(int channel=0;channel<4;channel++)Check(Math.Abs(c[channel]-(scenario==0?input[i][channel]:0))<2e-5,"Constant/transparent");
                    if(scenario==2)Check(Math.Abs(c.r-c.g)<1e-6 && Math.Abs(c.g-c.b)<1e-6,"Gray channels identical");
                    if(x>=w*.2 && x<(w-1)-w*.2 && y>=h*.2 && y<(h-1)-h*.2 && scenario!=1)
                        for(int channel=0;channel<4;channel++)Check(Math.Abs(c[channel]-input[i][channel])<2e-5,"Exact center");
                }
            }
            finally {if(rt!=null)RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(t);}
        }
        return $"Offset Blend: {checks} checks passed (shader, constants, HDR, alpha, grayscale, center, odd and degenerate sizes, render state).";
    }
    static void Dump(string path,Color[] data,int w,int h)
    {
        using(var writer=new BinaryWriter(File.Create(path)))
        {writer.Write(w);writer.Write(h);foreach(var c in data){writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a);}}
    }
    static string EvidenceDirectory(string runId, string entry)
    {
        if (runId == null) runId = Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(runId, "N", out _)) throw new ArgumentException("N-format owned GUID required.");
        if (entry != "preview" && entry != "reference-fixtures") throw new ArgumentException("Unknown owned evidence entry.");
        string folder = Path.GetFullPath("Temp/WhimTex/diagnostics/" + runId + "/histogram-seamless/" + entry);
        if (File.Exists(folder) || Directory.Exists(folder)) throw new IOException("Evidence GUID/entry already exists; never overwrite a previous entry.");
        for (string parent = Path.GetDirectoryName(folder); parent != null; parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Evidence output cannot traverse a link.");
        Directory.CreateDirectory(folder);
        return folder; // Evidence intentionally survives UnityBRun transient-resource cleanup.
    }
    static string Diagnostic(string name, Func<string> body)
    {
        string report = null;
        var result = JsonUtility.FromJson<WhimTex.Tests.TestResult>(UnityBRun.Run(name, () => report = body()));
        if (result.status == "passed") result.message += "\n" + report;
        return result.ToJson();
    }
    public static string Preview(string runId = null, string source = "output/copy-blend-lab-2026-09-28/00_source.png")
        => Diagnostic("HistogramSeamlessSmoke.Preview", () =>
    {
        if (!File.Exists(source)) throw new UnityBSkipException("Original Preview prerequisite missing: " + source + "; supply the original 00_source.png; no substitute/golden generated.");
        byte[] sourceBytes = File.ReadAllBytes(source);
        string dir = EvidenceDirectory(runId == null ? null : Guid.Parse(runId).ToString("N"), "preview");
        var input = UnityBRun.Track(new Texture2D(2,2,TextureFormat.RGBA32,false,false));
        RenderTexture result = null, linear = null;
        try
        {
            Check(input.LoadImage(sourceBytes,false), "Original Preview PNG decodes");
            linear=RenderTexture.GetTemporary(input.width,input.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try {GL.sRGBWrite=false;Graphics.Blit(input,linear);}finally {GL.sRGBWrite=srgb;RenderTexture.active=old;}
            Dump(Path.Combine(dir,"09_unity_input.bin"),Read(linear),input.width,input.height);
            result=Render(linear);var pixels=Read(result);
            Dump(Path.Combine(dir,"09_unity_histogram.bin"),pixels,input.width,input.height);
            var encoded=UnityBRun.Track(new Texture2D(input.width,input.height,TextureFormat.RGBA32,false,true));
            try {for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;encoded.SetPixels(pixels);encoded.Apply();File.WriteAllBytes(Path.Combine(dir,"09_unity_histogram.png"),encoded.EncodeToPNG());}
            finally {Object.DestroyImmediate(encoded);}
            Check(sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(source)), "Preview preserves original input bytes");
            return Path.Combine(dir,"09_unity_histogram.png");
        }
        finally {if(result!=null)RenderTexture.ReleaseTemporary(result);if(linear!=null)RenderTexture.ReleaseTemporary(linear);Object.DestroyImmediate(input);}
    });
    public static string Benchmark() => Diagnostic("HistogramSeamlessSmoke.Benchmark", () =>
    {
        var report=new System.Text.StringBuilder("Offset Blend, render + synchronous readback, median 3: ");
        foreach(int size in new[]{256,512,1024})
        {
            var t=UnityBRun.Track(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            try
            {
                var data=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)data[y*size+x]=new Color((float)x/size,(float)y/size,.3f,1);
                t.SetPixels(data);t.Apply();var times=new double[3];
                for(int i=-1;i<3;i++)
                {
                    var watch=Stopwatch.StartNew();var rt=Render(t);
                    try {Read(rt);}finally {RenderTexture.ReleaseTemporary(rt);}
                    watch.Stop();if(i>=0)times[i]=watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);report.Append($"{size}: {times[1]:F2} ms; ");
                Check(!double.IsNaN(times[1])&&!double.IsInfinity(times[1])&&times[1]>=0, "Finite median measurement; no performance threshold/golden imposed");
            }
            finally {Object.DestroyImmediate(t);}
        }
        return report.ToString();
    });
    public static string ReferenceFixtures(string runId = null) => Diagnostic("HistogramSeamlessSmoke.ReferenceFixtures", () =>
    {
        string dir=EvidenceDirectory(runId == null ? null : Guid.Parse(runId).ToString("N"), "reference-fixtures");
        var rng=new System.Random(391);
        foreach(var size in new[]{new Vector2Int(64,48),new Vector2Int(17,13),new Vector2Int(128,128)})
        {
            var t=UnityBRun.Track(new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true));RenderTexture rt=null;
            try
            {
                var pixels=new Color[size.x*size.y];
                for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((float)rng.NextDouble()*3-1,(float)rng.NextDouble()*2,(float)rng.NextDouble(),(float)rng.NextDouble());
                t.SetPixels(pixels);t.Apply(false,false);rt=Render(t);
                string prefix="reference_"+size.x+"x"+size.y;
                Dump(Path.Combine(dir,prefix+"_input.bin"),pixels,size.x,size.y);
                Dump(Path.Combine(dir,prefix+"_output.bin"),Read(rt),size.x,size.y);
                Check(new FileInfo(Path.Combine(dir,prefix+"_input.bin")).Length==8L+size.x*size.y*16L &&
                    new FileInfo(Path.Combine(dir,prefix+"_output.bin")).Length==8L+size.x*size.y*16L, "Original dimensions and float RGBA dump layout");
            }
            finally {if(rt!=null)RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(t);}
        }
        return "Independent diagnostic fixtures (not goldens/pass oracles) retained at " + dir;
    });
    public static string Main() => UnityBRun.Run("HistogramSeamlessSmoke.Main", () => ExecuteMain());
}
