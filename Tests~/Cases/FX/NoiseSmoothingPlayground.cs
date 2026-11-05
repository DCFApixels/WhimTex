using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Standalone opt-in experiment. No graph/document changes, imported assets,
// windows, profiler state changes, native binaries or full-size CPU readbacks.
public static class NoiseSmoothingPlayground
{
    // Resolve the public canonical serializer: Pipeline also references the
    // localization package's duplicate Newtonsoft types. No Unity internals.
    static readonly Type JsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
    static T FromJson<T>(string value) => (T)JsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{value,typeof(T)});
    static string ToJson(object value) => (string)JsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/WhimTex/noise-smoothing"));
    static string Package => Path.GetFullPath(Path.Combine(Application.dataPath,"../Packages/com.dcfapixels.whimtex"));
    [Serializable] public class Verdict { public string status; public int checks; public string message; public string[] failures; }
    [Serializable] public class Case
    {
        public string name; public int kind, dimensions=2, metric, output=1, distribution, side=1024;
        public float smoothness=.5f, evolution, jitter=1, period, footprint, offset; public int fractal;
        public bool staticDomain;
        public string Key => kind+"/"+dimensions+"/"+metric+"/"+output+"/"+distribution+"/"+staticDomain;
    }
    [Serializable] public class Request { public string phase="smoke"; public Case[] cases; public int warmups=1, repetitions=5, maxPixelsPerDraw=1048576; }
    [Serializable] public class Sample { public string name; public int side, repetition, tiles; public double submitMs, waitMs, totalMs; }
    [Serializable] public class Report
    {
        public string phase, state, message, gpu, api, unity, sourceHash, prototypeHash;
        public int gpuMemoryMiB, checks, completed, total; public bool cleanup;
        public string timing="Warm raster submission + GPU completion wall time. Each draw fences through a separate 1x1 RT. Includes submission, tiny blit and readback; not isolated GPU timestamps or interactive FPS.";
        public int maxPixelsPerDraw;
        public List<Case> cases=new List<Case>(); public List<Sample> samples=new List<Sample>();
        public List<string> validations=new List<string>(); public List<string> diagnostics=new List<string>();
    }
    public static string Start()
    {
        Directory.CreateDirectory(Root);
        string status=Path.Combine(Root,"status.json");
        if(File.Exists(status) && FromJson<Verdict>(File.ReadAllText(status)).status=="running")
            return ToJson(new Verdict{status="failed",message="An experiment is already pending; inspect its report before retrying."});
        var request=FromJson<Request>(File.ReadAllText(Path.Combine(Root,"request.json")));
        if(request.cases==null || request.cases.Length==0 || request.repetitions<1 || request.repetitions>15 || request.maxPixelsPerDraw<65536 || request.maxPixelsPerDraw>67108864)
            throw new ArgumentException("Invalid experiment request.");
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Editor must be idle and support rendering.");
        foreach(var c in request.cases)
            if(c.side<32 || c.side>8192 || c.dimensions<1 || c.dimensions>3 || c.metric<0 || c.metric>3 || c.kind<0 || c.kind>7 || c.output<0 || c.output>8)
                throw new ArgumentException("Invalid case: "+c.name);
        if(File.Exists(Path.Combine(Root,"cancel"))) File.Delete(Path.Combine(Root,"cancel"));
        var job=new Job(request); job.Start();
        return ReadStatus();
    }
    public static string ReadStatus() => File.ReadAllText(Path.Combine(Root,"status.json"));
    public static string Cancel()
    {
        File.WriteAllText(Path.Combine(Root,"cancel"),"cancel");
        return ToJson(new Verdict{status="cancelled",checks=1,message="Cancellation requested; update callback owns cleanup."});
    }
    static string Hash(string file)
    { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant(); }
    sealed class Program : IDisposable
    {
        public Shader shader; public Material material;
        public void Dispose() { if(material)Object.DestroyImmediate(material); if(shader)Object.DestroyImmediate(shader); }
    }
    sealed class Job
    {
        readonly Request request; readonly Report report=new Report();
        readonly Dictionary<string,Program> programs=new Dictionary<string,Program>();
        readonly List<Case> unique=new List<Case>(); readonly List<Tuple<Case,int>> queue=new List<Tuple<Case,int>>();
        readonly Stopwatch lifetime=Stopwatch.StartNew();
        readonly string source, library, reportPath;
        RenderTexture target,fenceTarget; Texture2D fence; CommandBuffer commands;
        int compileIndex, queueIndex, row; Sample active; bool stopped;
        public Job(Request r)
        {
            request=r; report.phase=r.phase; report.state="compiling";
            report.gpu=SystemInfo.graphicsDeviceName; report.api=SystemInfo.graphicsDeviceType.ToString();
            report.unity=Application.unityVersion; report.gpuMemoryMiB=SystemInfo.graphicsMemorySize;
            source=File.ReadAllText(Path.Combine(Package,"Tests~/Cases/FX/NoiseSmoothingPlayground.hlsl"));
            report.prototypeHash=Hash(Path.Combine(Package,"Tests~/Cases/FX/NoiseSmoothingPlayground.hlsl"));
            report.maxPixelsPerDraw=r.maxPixelsPerDraw;
            library=Path.Combine(Package,"src/Shaders/WTNoiseCellular.hlsl"); report.sourceHash=Hash(library);
            reportPath=Path.Combine(Root,r.phase+"-report.json"); report.cases.AddRange(r.cases);
            foreach(var c in r.cases) if(!unique.Exists(x=>x.Key==c.Key)) unique.Add(c);
            var random=new System.Random(7319);
            for(int repetition=-r.warmups;repetition<r.repetitions;repetition++)
                foreach(var c in r.cases.OrderBy(x=>random.Next())) queue.Add(Tuple.Create(c,repetition));
            report.total=queue.Count;
        }
        public void Start()
        {
            Write("pending","Compiling isolated experiment shaders.");
            EditorApplication.update+=Tick; AssemblyReloadEvents.beforeAssemblyReload+=Reload; EditorApplication.quitting+=Reload;
        }
        void Reload() => Stop("failed","Editor reload/quit interrupted the experiment.");
        void Check(bool ok,string message)
        { report.checks++; if(!ok)throw new InvalidOperationException(message); }
        Program Compile(Case c)
        {
            var p=new Program();
            try
            {
                string header="Shader \"Hidden/WhimTex/NoiseSmoothingPlayground/"+Guid.NewGuid().ToString("N")+"\" { SubShader { Pass { Cull Off ZWrite Off ZTest Always HLSLPROGRAM\n#pragma target 4.5\n#pragma vertex LabVert\n#pragma fragment LabFrag\n";
                string defines="#define LAB_KIND "+c.kind+"\n#define LAB_DIM "+c.dimensions+"\n#define LAB_METRIC "+c.metric+"\n#define LAB_OUTPUT "+c.output+"\n#define LAB_DISTRIBUTION "+c.distribution+"\n#define LAB_STATIC_DOMAIN "+(c.staticDomain?1:0)+"\n";
                p.shader=ShaderUtil.CreateShaderAsset(header+defines+source+"\nENDHLSL } } }",true);
                Check(p.shader!=null,"Shader creation failed: "+c.name);
                p.shader.hideFlags=HideFlags.HideAndDontSave;
                p.material=new Material(p.shader){hideFlags=HideFlags.HideAndDontSave};
                Set(c,p.material,1337);
                EnsureTarget(64); Draw(p.material,0,64,64); Fence();
                foreach(var m in ShaderUtil.GetShaderMessages(p.shader))
                {
                    report.diagnostics.Add(c.Key+": "+m.severity+": "+m.message);
                    Check(m.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Shader error: "+m.message);
                }
                Check(p.shader.isSupported,"Unsupported shader: "+c.Key);
                return p;
            }
            catch { p.Dispose(); throw; }
        }
        void Set(Case c,Material m,int seed)
        { m.SetFloat("_Smoothness",c.smoothness); m.SetFloat("_Evolution",c.evolution); m.SetFloat("_Jitter",c.jitter); m.SetInt("_Seed",seed); m.SetFloat("_Period",c.period); m.SetFloat("_Footprint",c.footprint); m.SetFloat("_Offset",c.offset); m.SetFloat("_Fractal",c.fractal); }
        void EnsureTarget(int side)
        {
            if(commands==null) commands=new CommandBuffer{name="Noise smoothing isolated experiment"};
            if(!fenceTarget)
            {
                fenceTarget=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave}; fenceTarget.Create();
                fence=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            }
            if(target && target.width==side) return;
            if(target) { target.Release(); Object.DestroyImmediate(target); }
            target=new RenderTexture(side,side,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};
            Check(target.Create(),"Target allocation failed: "+side);
            RenderTexture.active=target; GL.Clear(false,true,Color.black); Fence();
        }
        void Draw(Material m,int y,int height,int side)
        {
            m.SetVector("_Region",new Vector4(0,(float)y/side,1,(float)height/side));
            commands.Clear(); commands.SetRenderTarget(target); commands.SetViewport(new Rect(0,y,side,height));
            commands.DrawProcedural(Matrix4x4.identity,m,0,MeshTopology.Triangles,3,1);
            Graphics.ExecuteCommandBuffer(commands);
        }
        void Fence()
        {
            Graphics.Blit(target,fenceTarget,Vector2.zero,new Vector2(.37f,.43f));
            RenderTexture.active=fenceTarget; fence.ReadPixels(new Rect(0,0,1,1),0,0,false);
        }
        void Tick()
        {
            if(stopped) return;
            var previous=RenderTexture.active; bool srgb=GL.sRGBWrite; GL.sRGBWrite=false;
            try
            {
                if(File.Exists(Path.Combine(Root,"cancel"))) { Stop("cancelled","Cancelled; owned resources released."); return; }
                if(lifetime.Elapsed.TotalMinutes>18) throw new TimeoutException("Experiment deadline exceeded.");
                if(compileIndex<unique.Count)
                {
                    var c=unique[compileIndex++]; programs.Add(c.Key,Compile(c));
                    Write("pending","Compiled "+compileIndex+"/"+unique.Count+" shader variants."); return;
                }
                if(report.state=="compiling")
                {
                    Validate(); report.state="measuring"; Write("pending","Validation completed; measuring."); return;
                }
                if(queueIndex>=queue.Count)
                {
                    Check(Hash(library)==report.sourceHash,"Production WTNoise changed during measurement.");
                    Stop("passed","Completed isolated noise measurements and numeric checks."); return;
                }
                var tick=Stopwatch.StartNew(); int steps=0;
                while(queueIndex<queue.Count && steps++<8 && tick.Elapsed.TotalMilliseconds<45)
                {
                    var work=queue[queueIndex]; var c=work.Item1; int repetition=work.Item2;
                    if(active==null)
                    {
                        EnsureTarget(c.side); Set(c,programs[c.Key].material,1337+Math.Max(0,repetition)*101);
                        Fence(); row=0; active=new Sample{name=c.name,side=c.side,repetition=repetition};
                    }
                    int height=Math.Min(Math.Max(1,request.maxPixelsPerDraw/c.side),c.side-row);
                    long start=Stopwatch.GetTimestamp(); Draw(programs[c.Key].material,row,height,c.side);
                    long submitted=Stopwatch.GetTimestamp(); Fence(); long finished=Stopwatch.GetTimestamp();
                    active.submitMs+=(submitted-start)*1000d/Stopwatch.Frequency;
                    active.waitMs+=(finished-submitted)*1000d/Stopwatch.Frequency;
                    active.totalMs+=(finished-start)*1000d/Stopwatch.Frequency; active.tiles++;
                    row+=height;
                    if(row==c.side)
                    {
                        float value=fence.GetPixel(0,0).r; Check(!float.IsNaN(value)&&!float.IsInfinity(value),"Nonfinite benchmark pixel: "+c.name);
                        if(repetition>=0) report.samples.Add(active);
                        active=null; queueIndex++; report.completed=queueIndex;
                        Write("pending","Measured "+queueIndex+"/"+queue.Count+": "+c.name);
                    }
                }
            }
            catch(Exception e) { Stop("failed",e.ToString()); }
            finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; }
        }
        Color[] Pixels(Case c,float smoothness)
        {
            var rt=RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var tex=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            var saved=target;
            try
            {
                target=rt; var p=programs[c.Key]; Set(c,p.material,1337); p.material.SetFloat("_Smoothness",smoothness);
                Draw(p.material,0,64,64); RenderTexture.active=rt; tex.ReadPixels(new Rect(0,0,64,64),0,0,false);
                return tex.GetPixels();
            }
            finally { target=saved; Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt); }
        }
        Case Reference(Case c,int kind,int output)
        {
            var result=FromJson<Case>(ToJson(c)); result.kind=kind; result.output=output;
            return programs.ContainsKey(result.Key)?result:null;
        }
        void Validate()
        {
            foreach(var c in request.cases)
            {
                var pixels=Pixels(c,c.smoothness);
                foreach(var p in pixels)
                {
                    Check(!float.IsNaN(p.r)&&!float.IsInfinity(p.r)&&p.r>=-1e-5f&&p.r<=1.0001f,"Invalid scalar: "+c.name);
                    if(c.kind==4) Check(p.g<=p.b+1e-4f && p.g>=0,"Soft F1/F2 ordering: "+c.name);
                }
                if(c.kind==7)
                {
                    foreach(float strength in new[]{.05f,1f})
                        Check(Pixels(c,strength).All(p=>float.IsFinite(p.r)&&p.r>=-1e-5f&&p.r<=1.0001f),"Smoothness range "+strength+": "+c.name);
                    var reference=Reference(c,c.output==8?5:1,c.output==8?1:c.output);
                    Check(reference!=null,"Missing frozen baseline for "+c.name);
                    var zero=Pixels(c,0); var expected=Pixels(reference,0); float max=0;
                    for(int i=0;i<zero.Length;i++) max=Math.Max(max,Math.Abs(zero[i].r-expected[i].r));
                    Check(max<2e-5f,"Unified zero parity: "+c.name+" error="+max);
                    report.validations.Add(c.name+" zero max error="+max);
                    if(c.period>0)
                    {
                        float offset=c.offset; c.offset+=c.period;
                        var repeated=Pixels(c,c.smoothness); c.offset=offset; max=0;
                        for(int i=0;i<pixels.Length;i++) max=Math.Max(max,Math.Abs(pixels[i].r-repeated[i].r));
                        Check(max<2e-3f,"Periodic seam: "+c.name+" error="+max);
                        report.validations.Add(c.name+" periodic max error="+max);
                    }
                    if(c.period==0 && c.offset==0 && c.footprint==0 && c.fractal==0)
                    {
                        var prototype=Reference(c,c.output==0?3:c.output==8?6:4,c.output==8?1:c.output);
                        if(prototype!=null)
                        {
                            var other=Pixels(prototype,c.smoothness); max=0;
                            for(int i=0;i<pixels.Length;i++)max=Math.Max(max,Math.Abs(pixels[i].r-other[i].r));
                            Check(max<2e-4f,"Prototype parity: "+c.name+" error="+max);
                            report.validations.Add(c.name+" prototype max error="+max);
                        }
                    }
                }
                if(c.kind==3 || c.kind==4 || c.kind==6)
                {
                    int referenceKind=c.kind==6?5:1,referenceOutput=c.kind==3?0:c.output;
                    var reference=Reference(c,referenceKind,referenceOutput);
                    if(reference!=null)
                    {
                        var zero=Pixels(c,0); var expected=Pixels(reference,0); float max=0;
                        for(int i=0;i<zero.Length;i++) max=Math.Max(max,Math.Abs(zero[i].r-expected[i].r));
                        Check(max<2e-4f,"Zero-smoothing parity: "+c.name+" error="+max);
                        report.validations.Add(c.name+" zero/reference max error="+max);
                    }
                }
                if(c.kind==3&&c.metric==0)
                {
                    var reference=Reference(c,2,0);
                    if(reference!=null)
                    {
                        var expected=Pixels(reference,c.smoothness); float max=0;
                        for(int i=0;i<pixels.Length;i++) max=Math.Max(max,Math.Abs(pixels[i].r-expected[i].r));
                        Check(max<2e-4f,"Euclidean Voronoise parity: "+c.name+" error="+max);
                        report.validations.Add(c.name+" Voronoise max error="+max);
                    }
                }
            }
            // Render evidence at bounded size, outside timing. RGB channels show
            // the scalar result only; G/B auxiliary order statistics are not colors.
            var captures=request.cases.Where(x=>x.dimensions==2&&x.metric==0).ToArray();
            if(captures.Length>0)
            {
                int columns=4,rows=(captures.Length+columns-1)/columns;
                var sheet=new Texture2D(columns*64,rows*64,TextureFormat.RGBA32,false,true){hideFlags=HideFlags.HideAndDontSave};
                try
                {
                    for(int n=0;n<captures.Length;n++)
                    {
                        var pixels=Pixels(captures[n],captures[n].smoothness);
                        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(pixels[i].r,pixels[i].r,pixels[i].r,1);
                        sheet.SetPixels(n%columns*64,(rows-1-n/columns)*64,64,64,pixels);
                    }
                    sheet.Apply(); File.WriteAllBytes(Path.Combine(Root,request.phase+"-preview.png"),sheet.EncodeToPNG());
                    File.WriteAllLines(Path.Combine(Root,request.phase+"-preview-labels.txt"),captures.Select(c=>c.name));
                }
                finally { Object.DestroyImmediate(sheet); }
            }
        }
        void Write(string status,string message)
        {
            report.message=message;
            File.WriteAllText(reportPath,ToJson(report));
            File.WriteAllText(Path.Combine(Root,"status.json"),ToJson(new Verdict{status=status=="pending"?"running":status,checks=report.checks,message=message,failures=status=="failed"?new[]{message}:new string[0]}));
        }
        void Stop(string status,string message)
        {
            if(stopped)return; stopped=true;
            EditorApplication.update-=Tick; AssemblyReloadEvents.beforeAssemblyReload-=Reload; EditorApplication.quitting-=Reload;
            foreach(var p in programs.Values)p.Dispose(); programs.Clear();
            if(commands!=null)commands.Dispose();
            if(target) { target.Release(); Object.DestroyImmediate(target); }
            if(fenceTarget) { fenceTarget.Release(); Object.DestroyImmediate(fenceTarget); }
            if(fence)Object.DestroyImmediate(fence);
            report.cleanup=true; report.state=status; Write(status,message);
        }
    }
}
