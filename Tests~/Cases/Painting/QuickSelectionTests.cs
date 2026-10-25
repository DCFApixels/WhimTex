using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

// No user windows, assets, documents, preferences or input are changed.
public static class QuickSelectionTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Type(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex."+name,true);
    static object Call(object value,string name,params object[] args)
    { try{return value.GetType().GetMethod(name,F).Invoke(value,args);}catch(TargetInvocationException e){throw e.InnerException;} }
    static object Field(object value,string name) => value.GetType().GetField(name,F).GetValue(value);
    static object Property(object value,string name) => value.GetType().GetProperty(name,F).GetValue(value);
    static object New(string name,params object[] args) => Activator.CreateInstance(Type(name),F,null,args,null);
    static readonly MethodInfo FeatureMethod = Type("QuickSelection").GetMethod("Feature",F);
    static readonly Func<Color,Color> Feature = (Func<Color,Color>)Delegate.CreateDelegate(typeof(Func<Color,Color>),FeatureMethod);
    static Action<string> Log;
    [Serializable] public class Timing { public string name; public int tiles; public double totalMs, workerMs, maxTileMs, publishMs; public long selectionBytes, markerBytes, distanceBytes; }
    [Serializable] public class Report { public List<Timing> timings = new List<Timing>(); public double captureMaxMs, readbackMaxMs, inputMs, updateMaxMs, updateWarmMaxMs, interactiveMs, firstPreviewMs, interruptionMs, broadInputMs, broadUpdateMaxMs, broadUpdateWarmMs; public int gpuTiles, interactiveUpdates, broadTiles; }
    public static string Begin(string runId) => AsyncD.Start(runId,async (t,token) =>
    {
        var report = new Report();
        string path=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Temp/WhimTex/quick-selection",runId+".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Log = text => File.AppendAllText(path+".progress",text+Environment.NewLine);
        try { await Task.Run(()=>Cpu(t,report,token),token); await Gpu(t,report,token); await Interaction(t,report,token); }
        finally
        {
            File.WriteAllText(path,ReportJson(report));
        }
    });
    public static string Poll(string id)=>AsyncD.Poll(id);
    static string ReportJson(object value)
    {
        if(value==null)return "null";
        if(value is string s)return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        if(value.GetType().IsPrimitive)return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
        var parts=new List<string>();
        if(value is System.Collections.IEnumerable sequence){foreach(var v in sequence)parts.Add(ReportJson(v));return "["+string.Join(",",parts)+"]";}
        foreach(var field in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))parts.Add("\""+field.Name+"\":"+ReportJson(field.GetValue(value)));
        return "{"+string.Join(",",parts)+"}";
    }
    public static Task<string> Cancel(string id)=>AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id)=>AsyncD.Cleanup(id);
    public static string BeginRefinement(string id)=>AsyncD.Start(id,async(t,token)=>
    {var report=new Report();Log=null;await Task.Run(()=>Refinement(t,report,token),token);await Interaction(t,report,token);});

    static object Solve(TestContext t,Report report,string name,int w,int h,Func<int,int,Color> source,
        Vector2 point,float radius,bool subtract,object selection,CancellationToken token,object previous=null,bool wrap=false,float strength=1,int graphBudget=0)
    {
        var timer=Stopwatch.StartNew();var timing=new Timing{name=name};report.timings.Add(timing);
        Log?.Invoke("Begin "+name);
        var engine=New("QuickSelection",w,h,.18f,subtract);
        try
        {
        engine.GetType().GetProperty("Wrap",F).SetValue(engine,wrap);
        if(graphBudget>0)engine.GetType().GetProperty("GraphNodeBudget",F).SetValue(engine,graphBudget);
        if(previous!=null)Call(engine,"ReuseLayout",previous);
        byte[] Mask()=>(byte[])Property(selection,"Coverage");
        Call(engine,"Initialize",Mask(),token);Call(engine,"AddWeightedBrush",point,radius,subtract,wrap,strength);
        object request;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            request=Call(engine,"Take");
            if(request==null)
            {if((bool)Property(engine,"IsFinalized"))break;Call(engine,"FinishInput",token);continue;}
            var region=(RectInt)Field(request,"source");
            int step=(int)Field(request,"step"),rw=(region.width+step-1)/step,rh=(region.height+step-1)/step;
            t.True(rw<=528 && rh<=528,"Bounded source readback at every pyramid level");
            var pixels=(bool)Field(request,"cleanup")?Array.Empty<Color>():new Color[rw*rh];
            if(pixels.Length>0)for(int y=0;y<rh;y++)for(int x=0;x<rw;x++)pixels[y*rw+x]=source(Math.Min(w-1,region.x+x*step+step/2),Math.Min(h-1,region.y+y*step+step/2));
            var tile=Stopwatch.StartNew();var result=Call(engine,"GrowRaw",request,pixels,Mask(),token);
            double worker=tile.Elapsed.TotalMilliseconds;timing.workerMs+=worker;
            timing.maxTileMs=Math.Max(timing.maxTileMs,worker);
            tile.Restart();Call(selection,"ApplyQuickSelection",result,Property(engine,"InitialCoverage"));
            timing.publishMs=Math.Max(timing.publishMs,tile.Elapsed.TotalMilliseconds);
            Call(engine,"Accept",result);timing.tiles++;
            if(timing.tiles%128==0)Log?.Invoke(name+" tiles="+timing.tiles+" ms="+timer.Elapsed.TotalMilliseconds.ToString("F1"));
            t.True(timing.tiles<20000,"Propagation terminates without cycling at tile seams");
        }
        timing.totalMs=timer.Elapsed.TotalMilliseconds;timing.selectionBytes=Mask()?.LongLength??0;
        timing.markerBytes=(long)Property(engine,"MarkerBytes");timing.distanceBytes=(long)Property(engine,"DistanceBytes");
        Log?.Invoke(name+" done tiles="+timing.tiles+" ms="+timing.totalMs.ToString("F1")+" worker="+timing.workerMs.ToString("F1")+
            " max="+timing.maxTileMs.ToString("F2")+" publish="+timing.publishMs.ToString("F3"));
        return engine;
        }
        finally { ((IDisposable)engine).Dispose(); }
    }
    static void Cpu(TestContext t,Report report,CancellationToken token)=>Refinement(t,report,token);
    static void Refinement(TestContext t,Report report,CancellationToken token)
    {
        const int w=513,h=259;
        bool Shape(int x,int y)=>x>=47&&x<451&&y>=35&&y<231;
        Color Source(int x,int y)=>Shape(x,y)?new Color(6,-2,2,1):new Color(2,-.5f,1,1);
        var selection=New("CanvasSelection",w,h);
        var engine=Solve(t,report,"HDR Add",w,h,Source,new Vector2(180,130),70,false,selection,token);
        var mask=(byte[])Property(selection,"Coverage");
        int errors=0;for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(mask[y*w+x]!=(Shape(x,y)?255:0))errors++;
        t.Equal(0,errors,"HDR object is bounded at native pixels without SDR clipping");
        var partitioned=New("CanvasSelection",w,h);
        var blockEngine=Solve(t,report,"Bounded block refinement",w,h,Source,new Vector2(180,130),70,false,partitioned,token,graphBudget:2048);
        t.True((int)Property(blockEngine,"BlockSweeps")>0,"Forced low graph budget exercises coupled block sweeps");
        var splitMask=(byte[])Property(partitioned,"Coverage");errors=0;for(int i=0;i<mask.Length;i++)if(mask[i]!=splitMask[i])errors++;
        t.Equal(0,errors,"Converged block boundaries match the whole-band analytical object");
        engine=Solve(t,report,"Subtract replaces Add",w,h,Source,new Vector2(180,130),3,true,selection,token,engine);
        t.Equal(0,(int)Call(engine,"MarkerAt",180,130),"Last Subtract replaces a hard positive hint");
        t.Equal((byte)0,mask[130*w+180],"Hard background survives the global cut");
        t.Equal((byte)255,mask[130*w+170],"Unpainted earlier foreground remains hard");
        engine=Solve(t,report,"Add replaces Subtract",w,h,Source,new Vector2(180,130),3,false,selection,token,engine);
        t.Equal((byte)255,mask[130*w+180],"Latest Add restores its pixel");
        engine=Solve(t,report,"Clear hints",w,h,Source,new Vector2(180,130),71,false,selection,token,engine,strength:0);
        t.Equal(0L,(long)Property(engine,"MarkerBytes"),"Neutral tiles are released");
        foreach(byte value in mask)t.Equal((byte)0,value,"Erasing all hints restores initial empty coverage");
        var flat=New("CanvasSelection",65,65);object previous=null;int last=-1;
        foreach(float strength in new[]{.1f,.3f,.6f,1f})
        {
            previous=Solve(t,report,"Soft strength "+strength,65,65,(x,y)=>Color.gray,new Vector2(32,32),8,false,flat,token,previous,strength:strength);
            int count=0;var m=(byte[])Property(flat,"Coverage");if(m!=null)foreach(byte v in m)if(v!=0)count++;
            t.True(count>=last,"Increasing a positive unary hint cannot shrink its selection");last=count;
            t.Equal(128+(int)Math.Round(127*strength,MidpointRounding.ToEven),(int)Call(previous,"MarkerAt",32,32),"Strength byte is retained exactly");
        }
        t.True(last<65*65&&last>0,"Flat Add stays bounded without a negative stroke");
        previous=Solve(t,report,"Weakening",65,65,(x,y)=>Color.gray,new Vector2(32,32),8,false,flat,token,previous,strength:.01f);
        t.Equal(129,(int)Call(previous,"MarkerAt",32,32),"Weaker hints replace stronger hints");
        var soft=New("CanvasSelection",64,64);var original=new byte[4096];original[4095]=73;
        Call(soft,"Set",original,Enum.Parse(Type("SelectionCombine"),"Replace"));
        Solve(t,report,"Unrelated fractional selection",64,64,(x,y)=>x<32?Color.white:Color.black,new Vector2(5,5),2,false,soft,token);
        t.Equal((byte)73,((byte[])Property(soft,"Coverage"))[4095],"Unrelated fractional coverage is preserved");
        var wrapped=New("CanvasSelection",64,64);
        var repeated=Solve(t,report,"Repeat hard hint",64,64,(x,y)=>(x<8||x>=56)&&y>=7&&y<48?Color.white:Color.black,new Vector2(63,20),3,false,wrapped,token,wrap:true);
        t.Equal(255,(int)Call(repeated,"MarkerAt",0,20),"Hints wrap at the canvas edge");
        t.Equal((byte)255,((byte[])Property(wrapped,"Coverage"))[20*64],"Cut joins the repeated edge");
        t.Equal((byte)0,((byte[])Property(wrapped,"Coverage"))[20*64+32],"Repeated selection does not fill unrelated middle");
        var flatRepeat=New("CanvasSelection",65,65);
        Solve(t,report,"Flat repeated canvas",65,65,(x,y)=>Color.gray,new Vector2(64,32),3,false,flatRepeat,token,wrap:true);
        int repeatedCount=0;foreach(byte v in (byte[])Property(flatRepeat,"Coverage"))if(v!=0)repeatedCount++;
        t.True(repeatedCount>0&&repeatedCount<65*65,"A constant periodic canvas does not flood without Subtract");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool stopped=false;try{Call(New("QuickSelection",8192,8192,.18f,false),"Initialize",null,cancel.Token);}catch(OperationCanceledException){stopped=true;}
            t.True(stopped,"Cancellation precedes allocation");
        }
        CutOracle(t,token);
        Contours(t,token);
        Progressive(t,report,token);
    }
    static void Contours(TestContext t,CancellationToken token)
    {
        void Closed(Vector4[] edges)
        {
            t.True(edges.Length<=16384,"Complete contour fits the display budget");
            var degree=new Dictionary<Vector2Int,int>();
            foreach(var edge in edges)
            {
                var a=new Vector2Int((int)edge.x,(int)edge.y);var b=new Vector2Int((int)edge.z,(int)edge.w);
                t.True(a!=b&&(a.x==b.x||a.y==b.y),"Contour segment is nonzero and axis aligned");
                degree.TryGetValue(a,out int da);degree[a]=da+1;degree.TryGetValue(b,out int db);degree[b]=db+1;
            }
            int open=0;foreach(int n in degree.Values)if(n%2!=0)open++;
            t.Equal(0,open,"No dangling endpoints within curves, canvas edges or tile seams");
        }
        foreach(int size in new[]{769,2049,8192})
        {
            int h=size==2049?1537:size;var values=new byte[size*h];
            for(int y=0;y<h;y++)for(int x=0;x<size;x++)
            {
                float dx=x-size*.5f,dy=y-h*.5f,r=Math.Min(size,h)*.42f,d=dx*dx+dy*dy;
                bool selected=d<r*r&&d>r*r*.48f;
                if(size==2049){int px=x%81-40,py=y%81-40;selected=px*px+py*py<38*38;}
                if(selected)values[y*size+x]=255;
            }
            var selection=New("CanvasSelection",size,h);var engine=New("QuickSelection",size,h,.18f,false);
            try
            {
                Call(engine,"Initialize",null,token);Set(engine,"labels",values);Call(engine,"QueuePublish");
                object queue=Field(engine,"requests");Vector4[] last=null;int publications=0;
                while((int)Property(queue,"Count")>0)
                {
                    token.ThrowIfCancellationRequested();var request=Call(queue,"Dequeue");
                    var result=Call(engine,"Publish",(int)Field(request,"index"),Property(selection,"Coverage"),token);
                    Call(selection,"ApplyQuickSelection",result,Property(engine,"InitialCoverage"));Call(engine,"Accept",result);
                    last=(Vector4[])Field(result,"edges");
                    if(publications++%32==0)Closed(last);
                }
                t.True(last!=null&&last.Length>0,"Curved selection keeps a visible outline");Closed(last);
                if(size>=2049)t.True((int)Field(engine,"outlineStep")>1,"Dense contours exercise a shared coarse grid");
                var actual=(byte[])Property(selection,"Coverage");int errors=0;
                for(int i=0;i<values.Length;i++)if(actual[i]!=values[i])errors++;
                t.Equal(0,errors,"Display simplification leaves native selection bytes unchanged");
                Call(engine,"Initialize",actual,token);
                var restored=new List<Vector4>();
                foreach(var list in (List<Vector4>[])Field(engine,"outlines"))if(list!=null)restored.AddRange(list);
                foreach(var list in (List<Vector4>[])Field(engine,"seams"))if(list!=null)restored.AddRange(list);
                Closed(restored.ToArray());
            }
            finally{((IDisposable)engine).Dispose();}
        }
    }
    static void Progressive(TestContext t,Report report,CancellationToken token)
    {
        const int size=8192;
        bool Shape(int x,int y)=>x>=3713&&x<4213&&y>=3949&&y<4449;
        var selection=New("CanvasSelection",size,size);var engine=New("QuickSelection",size,size,.18f,false);
        Task<object> active=null;
        void SetProp(string n,object value)=>engine.GetType().GetProperty(n,F).SetValue(engine,value);
        object Step(object request)
        {
            var r=(RectInt)Field(request,"source");int step=(int)Field(request,"step"),w=(r.width+step-1)/step,h=(r.height+step-1)/step;
            var pixels=(bool)Field(request,"cleanup")?Array.Empty<Color>():new Color[w*h];
            for(int y=0;y<h&&pixels.Length>0;y++)for(int x=0;x<w;x++)
                pixels[y*w+x]=Shape(r.x+x*step+step/2,r.y+y*step+step/2)?new Color(6,-2,2,1):new Color(2,-.5f,1,1);
            return Call(engine,"GrowRaw",request,pixels,Property(selection,"Coverage"),token);
        }
        void Drain()
        {
            int count=0;
            while(true)
            {
                token.ThrowIfCancellationRequested();t.True(++count<10000,"Progressive work terminates");var request=Call(engine,"Take");
                if(request==null){if((bool)Property(engine,"IsFinalized"))return;Call(engine,"FinishInput",token);continue;}
                var result=Step(request);
                if((bool)Call(engine,"IsCurrent",result))Call(selection,"ApplyQuickSelection",result,Property(engine,"InitialCoverage"));
                Call(engine,"Accept",result);
            }
        }
        try
        {
            SetProp("Progressive",true);SetProp("RefinementAllowed",false);
            Call(engine,"Initialize",null,token);Call(engine,"AddWeightedBrush",new Vector2(3900,4200),100f,false,false,1f);
            Step(Call(engine,"Take"));Step(Call(engine,"Take"));
            // Input arrives before the queued preview solve. It must still produce a
            // bounded snapshot, while the newer hints schedule its successor.
            Call(engine,"AddWeightedBrush",new Vector2(3900,4200),3f,true,false,1f);
            var oldPreview=Step(Call(engine,"Take"));
            t.True((bool)Field(oldPreview,"preview"),"Continuous stamps cannot starve the bounded preview");
            t.True(!(bool)Call(engine,"IsCurrent",oldPreview),"Old preview retains its original revision");
            t.True(Property(selection,"Coverage")==null,"Provisional mask never enters selection consumers");
            object latest=null;
            for(int i=0;i<20;i++){var r=Call(engine,"Take");if(r==null)break;var result=Step(r);if((bool)Field(result,"preview"))latest=result;}
            t.True(latest!=null&&(bool)Call(engine,"IsCurrent",latest),"Queued negative hint gets the next preview");
            SetProp("RefinementAllowed",true);Step(Call(engine,"Take"));
            var refining=Call(engine,"Take");
            var task=active=Task.Run(()=>Step(refining),token);
            bool entered=SpinWait.SpinUntil(()=>(bool)Property(engine,"IsSolving")||task.IsCompleted,10000);
            t.True(entered&&!task.IsCompleted,"Real refinement solve is running before new input");
            var watch=Stopwatch.StartNew();Call(engine,"AddWeightedBrush",new Vector2(3900,4200),3f,false,false,1f);
            var stale=task.GetAwaiter().GetResult();report.interruptionMs=watch.Elapsed.TotalMilliseconds;
            t.True(!(bool)Field(stale,"changed")&&(int)Property(engine,"InterruptedSolves")>0,"New input cancels an in-flight graph solve without publishing it");
            Drain();var mask=(byte[])Property(selection,"Coverage");
            t.Equal((byte)255,mask[4200*size+3900],"Latest Add wins after interrupted Subtract");
            Call(engine,"AddWeightedBrush",new Vector2(3900,4200),3f,true,false,1f);Drain();
            t.Equal((byte)0,mask[4200*size+3900],"8K Subtract replaces the positive hint");
            t.Equal((byte)255,mask[4200*size+3890],"8K Subtract preserves adjacent unpainted positive hints");
            Call(engine,"AddWeightedBrush",new Vector2(3900,4200),3f,false,false,1f);Drain();
            int errors=0;for(int y=0;y<size;y++)for(int x=0;x<size;x++)if(mask[y*size+x]!=(Shape(x,y)?255:0))errors++;
            t.Equal(0,errors,"Progressive Add/Subtract/Add retains exact native HDR boundary on 8K");
        }
        finally{try{active?.GetAwaiter().GetResult();}finally{((IDisposable)engine).Dispose();}}
    }
    static void CutOracle(TestContext t,CancellationToken token)
    {
        var random=new System.Random(2817);
        for(int trial=0;trial<80;trial++)
        {
            const int n=6;var cut=New("QuickSelectionCut",n,80);var a=new float[n];var b=new float[n];var links=new List<(int a,int b,float w)>();
            for(int i=0;i<n;i++){a[i]=random.Next(10)/3f;b[i]=random.Next(10)/3f;Call(cut,"Terminal",i,a[i],b[i]);}
            for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)if(j==i+1||random.Next(3)==0)
            {float weight=random.Next(10)/4f;links.Add((i,j,weight));Call(cut,"Link",i,j,weight);}
            var result=(bool[])Call(cut,"Solve",token,null);double best=double.PositiveInfinity,actual=0;
            for(int bits=0;bits<(1<<n);bits++)
            {
                double e=0;for(int i=0;i<n;i++)e+=(bits&(1<<i))!=0?a[i]:b[i];
                foreach(var link in links)if(((bits>>link.a)&1)!=((bits>>link.b)&1))e+=link.w;
                best=Math.Min(best,e);
            }
            for(int i=0;i<n;i++)actual+=result[i]?a[i]:b[i];foreach(var link in links)if(result[link.a]!=result[link.b])actual+=link.w;
            t.Near(best,actual,.0001,"Minimum cut agrees with exhaustive independent binary labeling");
        }
        var cancelled=New("QuickSelectionCut",100,1000);
        for(int i=0;i<100;i++){Call(cancelled,"Terminal",i,.1f,.2f);if(i>0)Call(cancelled,"Link",i-1,i,1f);}
        int checks=0;bool stopped=false;
        try{Call(cancelled,"Solve",token,(Func<bool>)(()=>++checks>=2));}catch(OperationCanceledException){stopped=true;}
        t.True(stopped&&checks>=2,"Superseded max-flow stops inside the solve, not only between requests");
    }
    static async Task Gpu(TestContext t,Report report,CancellationToken token)
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=8192;
        var cache=(IDisposable)New("EffectRenderCache");
        var behavior=new ColorFillLayerBehaviour{mode=ColorFillLayerBehaviour.FillMode.UV};var layer=new Layer(behavior);doc.layers.Add(layer);Call(doc,"NormalizeModel");
        var before=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            t.True(SystemInfo.supportsAsyncGPUReadback,"Native async readback available for integration check");
            foreach(var rect in new[]{new RectInt(0,0,257,257),new RectInt(4095,4095,258,258),new RectInt(7935,7935,257,257)})
            foreach(bool selected in new[]{false,true})
            {
                token.ThrowIfCancellationRequested();
                var currentTarget=RenderTexture.active;bool currentSrgb=GL.sRGBWrite;
                var timer=Stopwatch.StartNew();
                var rt=(RenderTexture)Call(doc,"RenderSelectionRegion",New("CanvasRenderRegion",rect,1),cache,selected?layer:null);
                report.captureMaxMs=Math.Max(report.captureMaxMs,timer.Elapsed.TotalMilliseconds);
                t.True(RenderTexture.active==currentTarget && GL.sRGBWrite==currentSrgb,"Capture restores borrowed GPU state before yielding");
                try
                {
                    t.Equal(rect.width,rt.width,"Regional source width at 8K");t.Equal(rect.height,rt.height,"Regional source height at 8K");
                    var completion=new TaskCompletionSource<Color[]>();
                    var read=AsyncGPUReadback.Request(rt,0,TextureFormat.RGBAFloat, result=>
                    {
                        if(result.hasError)completion.TrySetException(new Exception("Regional async GPU readback failed"));
                        else completion.TrySetResult(result.GetData<Color>().ToArray());
                    });
                    read.forcePlayerLoopUpdate=true;GL.Flush();timer.Restart();
                    Color[] data=await completion.Task;
                    token.ThrowIfCancellationRequested();
                    t.Equal(rect.width*rect.height,data.Length,"Native source pixels, no full 8K readback");
                    Color pixel=data[0];
                    t.Near((rect.x+.5f)/8192f,pixel.r,.001f,"Regional source retains global X coordinates");
                    t.Near((rect.y+.5f)/8192f,pixel.g,.001f,"Regional source retains global Y coordinates");
                    report.readbackMaxMs=Math.Max(report.readbackMaxMs,timer.Elapsed.TotalMilliseconds);report.gpuTiles++;
                }
                finally{CallStatic("ProfiledRenderTexture","ReleaseTemporary",rt);}
            }
        }
        finally{cache.Dispose();Call(behavior,"ReleaseTransientResources");Object.DestroyImmediate(doc);RenderTexture.active=before;GL.sRGBWrite=srgb;}
    }
    static void CallStatic(string type,string name,params object[] args)=>Type(type).GetMethod(name,F).Invoke(null,args);

    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,F).SetValue(owner,value);
    static async Task Interaction(TestContext t,Report report,CancellationToken token)
    {
        Log?.Invoke("Begin detached 8K tool workflow");
        var window=ScriptableObject.CreateInstance<WhimTexWindow>();
        var original=(WhimTexDocument)Field(window,"activeDocument");
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=8192;
        var texture=new Texture2D(16,16,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point};
        var data=new Color[256];for(int i=0;i<data.Length;i++)data[i]=new Color(0,0,0,1);data[8*16+8]=new Color(4,.5f,-2,1);
        texture.SetPixels(data);texture.Apply(false,false);doc.outputFilter=FilterMode.Point;
        var behavior=new FileLayerBehaviour{sourceTexture=texture};var layer=new Layer(behavior){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
        doc.layers.Add(layer);Call(doc,"NormalizeModel");
        var update=(EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction),window,
            typeof(WhimTexWindow).GetMethod("UpdateQuickSelection",F));
        try
        {
            Call(window,"SetDocument",doc);
            var canvasType=typeof(WhimTexWindow).GetNestedType("CanvasElement",F);
            var canvas=Activator.CreateInstance(canvasType,F,null,new[]{Field(window,"canvasViewport")},null);
            Set(window,"toolkitCanvas",canvas);Set(window,"toolkitCanvasViewHeader",new VisualElement());
            Set(window,"canvasTool",Enum.Parse(typeof(WhimTexWindow).GetNestedType("CanvasTool",F),"QuickSelection"));
            Set(window,"quickSelectionSource",Enum.Parse(typeof(WhimTexWindow).GetNestedType("QuickSelectionSource",F),"VisibleLayers"));
            Set(window,"quickSelectionSize",64f);Set(window,"quickSelectionTolerance",.18f);Set(window,"quickSelectionStrength",1f);
            Call(window,"BuildAreaSelectionTools");Call(window,"AddQuickSelectionSettings");
            t.True(((VisualElement)Field(window,"toolkitCanvasViewHeader")).childCount>0,"Tool settings built without opening a window");
            var timer=Stopwatch.StartNew();
            object heldStroke=null;
            foreach(bool subtract in new[]{true,false,true,false})
            {
                object stroke=Call(window,"BeginQuickSelection",subtract,new Vector2(4352,4352));
                t.True(stroke!=null,"Add/subtract can queue before first publication");
                Set(window,"quickSelectionStrength",.25f);
                t.Equal(1f,(float)Field(stroke,"strength"),"Changing settings does not change an already-started stroke");
                Set(window,"quickSelectionStrength",1f);
                ((Queue<Vector2>)Field(stroke,"points")).Enqueue(new Vector2(4380,4360));Set(stroke,"ended",true);
                heldStroke=stroke;
            }
            Set(heldStroke,"ended",false);
            report.inputMs=timer.Elapsed.TotalMilliseconds;
            EditorApplication.update-=update;
            t.True((bool)typeof(WhimTexWindow).GetMethod("IsDocumentBusyForLiveApi",F).Invoke(null,new object[]{doc}),"Live capture waits for queued selection");
            timer.Restart();
            bool Pending()=>Field(window,"quickSelection")!=null || (int)Property(Field(window,"quickSelectionStrokes"),"Count")!=0;
            int heldUpdates=0;bool pauseRefined=false,deferred=false,actionRan=false;
            while(Pending())
            {
                token.ThrowIfCancellationRequested();t.True(timer.Elapsed.TotalSeconds<30,"Detached tool workflow finishes without blocking input");
                if(report.firstPreviewMs==0||heldUpdates<3)Set(window,"quickSelectionLastInput",EditorApplication.timeSinceStartup);
                var frame=Stopwatch.StartNew();Call(window,"UpdateQuickSelection");
                if(Field(window,"quickSelectionPreviewEdges")!=null)
                {
                    if(report.firstPreviewMs==0)report.firstPreviewMs=timer.Elapsed.TotalMilliseconds;
                    if(heldUpdates++<3)t.True(Property(Call(window,"GetAreaSelection"),"Coverage")==null,"Moving brush shows preview without committing native coverage");
                }
                var live=Field(window,"quickSelection");
                if(live!=null&&(bool)Property(live,"IsRefined")&&Field(window,"quickSelectionTask")==null&&Field(window,"quickSelectionRequest")==null&&!deferred)
                {
                    pauseRefined=true;
                    t.True(!(bool)Field(heldStroke,"ended")&&Field(window,"quickSelectionPreviewEdges")==null,"A held-brush pause finishes refinement and removes provisional contour");
                    deferred=(bool)Call(window,"DeferUntilQuickSelectionReady",(Action)(()=>actionRan=true));
                    t.True(deferred&&!actionRan,"Consumer action waits asynchronously for workflow completion");
                }
                double elapsed=frame.Elapsed.TotalMilliseconds;report.updateMaxMs=Math.Max(report.updateMaxMs,elapsed);
                if(report.interactiveUpdates>5)report.updateWarmMaxMs=Math.Max(report.updateWarmMaxMs,elapsed);
                report.interactiveUpdates++;await Task.Delay(1);
            }
            report.interactiveMs=timer.Elapsed.TotalMilliseconds;
            t.True(pauseRefined&&report.firstPreviewMs>0&&report.firstPreviewMs<report.interactiveMs,"A pause refines the preview before pointer release");
            t.True(actionRan,"Deferred consumer action runs after final publication");
            object selection=Call(window,"GetAreaSelection");byte[] mask=(byte[])Property(selection,"Coverage");
            t.True(mask!=null&&mask.Length==8192*8192,"Window publishes native 8K mask");
            t.Equal((byte)255,mask[4352*8192+4352],"Queued add/subtract/add retains final add");
            t.Equal((byte)0,mask[200*8192+200],"Selection stops at HDR source colour boundary");
            Texture gpu=(Texture)Call(window,"GetAreaSelectionTexture");
            var completion=new TaskCompletionSource<byte[]>();
            var request=AsyncGPUReadback.Request(gpu,0,4340,32,4340,32,0,1,TextureFormat.R8,r=>
            {if(r.hasError)completion.TrySetException(new Exception("Mask readback failed"));else completion.TrySetResult(r.GetData<byte>().ToArray());});
            request.forcePlayerLoopUpdate=true;GL.Flush();
            byte[] actual=await completion.Task;int errors=0;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)if(actual[y*32+x]!=mask[(4340+y)*8192+4340+x])errors++;
            t.Equal(0,errors,"Partial GPU mask uploads match CPU selection exactly");
            Set(window,"quickSelectionSize",8f);
            object released=Call(window,"BeginQuickSelection",true,new Vector2(4366,4366));Set(released,"ended",true);EditorApplication.update-=update;
            timer.Restart();
            while(Pending())
            {
                token.ThrowIfCancellationRequested();t.True(timer.Elapsed.TotalSeconds<30,"Released stroke completes native refinement");
                Call(window,"UpdateQuickSelection");await Task.Delay(1);
            }
            t.Equal((byte)0,mask[4366*8192+4366],"Released Subtract refines the real 8K consumer mask");
            t.Equal((byte)255,mask[4352*8192+4352],"Released Subtract preserves neighbouring Add");
            Set(window,"quickSelectionSize",64f);
            int revision=(int)Property(selection,"Revision");
            object cancelled=Call(window,"BeginQuickSelection",false,new Vector2(900,900));Set(cancelled,"ended",true);EditorApplication.update-=update;
            for(int i=0;i<50 && Field(window,"quickSelectionRequest")==null;i++){Call(window,"UpdateQuickSelection");await Task.Delay(1);}
            Call(window,"CancelQuickSelection");await Task.Delay(30);
            t.Equal(revision,(int)Property(selection,"Revision"),"Cancelled late GPU work cannot change selection");
            t.True(!(bool)typeof(WhimTexWindow).GetMethod("IsDocumentBusyForLiveApi",F).Invoke(null,new object[]{doc}),"Stopped selection releases live API busy state");
            object closing=Call(window,"BeginQuickSelection",false,new Vector2(1500,1500));Set(closing,"ended",true);EditorApplication.update-=update;
            for(int i=0;i<100&&Field(window,"quickSelectionRequest")==null;i++)
            {Call(window,"UpdateQuickSelection");if(Field(window,"quickSelectionRequest")!=null)break;await Task.Delay(1);}
            Call(window,"DrainQuickSelection");
            t.Equal(0,(int)Property(Field(window,"quickSelectionReadbacks"),"Count"),"Window teardown drains owned readbacks");
            t.True(Field(window,"quickSelectionRetirement")==null && Field(window,"quickSelectionTask")==null,"Window teardown drains worker ownership");
            t.Equal(revision,(int)Property(selection,"Revision"),"Teardown never publishes a partial cancelled tile");
            var retained=Field(window,"quickSelectionCompleted");
            t.True(retained!=null,"Stop/teardown retains the session's marker data until selection reset");
            t.Equal(255,(int)Call(Property(retained,"History"),"MarkerAt",4352,4352),"Latest hint survives stopped later work");
            Call(window,"ResetAreaSelection");
            t.True(Field(window,"quickSelectionCompleted")==null,"Clearing the selection discards accumulated hints");
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)data[y*16+x]=x<8?new Color(.36f,.36f,.36f,1):new Color(.3f,.3f,.3f,1);
            texture.SetPixels(data);texture.Apply(false,false);
            Set(window,"quickSelectionSize",2048f);
            object broad=Call(window,"BeginQuickSelection",false,new Vector2(2048,4096));Set(broad,"ended",true);EditorApplication.update-=update;
            timer.Restart();bool sent=false,negativeReached=false;int iterations=0;
            while(timer.Elapsed.TotalSeconds<25)
            {
                token.ThrowIfCancellationRequested();var frame=Stopwatch.StartNew();Call(window,"UpdateQuickSelection");
                double elapsed=frame.Elapsed.TotalMilliseconds;report.broadUpdateMaxMs=Math.Max(report.broadUpdateMaxMs,elapsed);
                if(iterations++>5)report.broadUpdateWarmMs=Math.Max(report.broadUpdateWarmMs,elapsed);
                var current=Field(window,"quickSelection");
                int tiles=current==null?0:(int)Property(current,"Processed");report.broadTiles=Math.Max(report.broadTiles,tiles);
                if(tiles>=2&&!sent)
                {
                    frame.Restart();
                    for(int i=0;i<8;i++)
                    {object stroke=Call(window,"BeginQuickSelection",i%2==0,new Vector2(i%2==0?6144:2048,4096+i*30));Set(stroke,"ended",true);}
                    report.broadInputMs=frame.Elapsed.TotalMilliseconds;EditorApplication.update-=update;sent=true;
                }
                if(current!=null && Field(window,"quickSelectionTask")==null && Field(window,"quickSelectionInitialize")==null)
                    negativeReached|=(int)Call(current,"MarkerAt",6144,4096)==0;
                if(negativeReached&&tiles>=24)break;
                await Task.Delay(1);
            }
            t.True(negativeReached,"New negative strokes are ingested before the first broad 8K selection finishes");
            // Stop before inspecting worker-owned marker arrays.
            Call(window,"DrainQuickSelection");
            t.Equal(128,(int)Call(Property(Field(window,"quickSelectionCompleted"),"History"),"MarkerAt",4352,4352),"Cleared hints do not leak into a new session");
            t.True(report.broadTiles<1024,"Correction did not wait for an entire canvas traversal");
            t.True(report.broadInputMs<100 && report.broadUpdateWarmMs<150,"Broad-stroke input and bounded UI updates have no long blocking wait");
            Log?.Invoke("Broad live ms input="+report.broadInputMs+" updateMax="+report.broadUpdateMaxMs+" warm="+report.broadUpdateWarmMs+" tiles="+report.broadTiles);
            Log?.Invoke("Detached workflow ms="+report.interactiveMs+" input="+report.inputMs+" updateMax="+report.updateMaxMs+" warm="+report.updateWarmMaxMs);
        }
        finally
        {
            EditorApplication.update-=update;Call(window,"CancelQuickSelection");
            Object.DestroyImmediate(window);Call(behavior,"ReleaseTransientResources");
            if(original!=null && original!=doc)Object.DestroyImmediate(original);
            if(doc!=null)Object.DestroyImmediate(doc);Object.DestroyImmediate(texture);
        }
    }
}
