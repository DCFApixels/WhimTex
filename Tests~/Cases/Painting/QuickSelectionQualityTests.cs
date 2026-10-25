using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

// Analytical image boundaries are independent of the segmentation implementation.
public static class QuickSelectionQualityTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Type T(string n)=>typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex."+n,true);
    static object New(string n,params object[] a)=>Activator.CreateInstance(T(n),F,null,a,null);
    static object Call(object o,string n,params object[] a)
    {try{return o.GetType().GetMethod(n,F).Invoke(o,a);}catch(TargetInvocationException e){throw e.InnerException;}}
    static object Field(object o,string n)=>o.GetType().GetField(n,F)?.GetValue(o);
    static object Prop(object o,string n)=>o.GetType().GetProperty(n,F).GetValue(o);
    [Serializable] public class Row
    {
        public string name; public int width,height,requests,falsePositive,falseNegative,intersection,union;
        public double workerMs,totalMs,maxWorkerMs,iou; public long hintsBytes,solverBytes;
        public double solveMs,buildMs,refineMs,publishMs,firstPreviewMs; public int maxNodes;
    }
    [Serializable] public class Report { public List<Row> rows=new List<Row>(); }
    static Action<string> Progress;
    public static string Begin(string id)=>AsyncD.Start(id,async(t,token)=>
    {
        string dir=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Temp/WhimTex/quick-selection");
        Directory.CreateDirectory(dir);var report=new Report();
        Progress=s=>File.AppendAllText(Path.Combine(dir,id+".progress"),s+Environment.NewLine);
        try {await Task.Run(()=>Run(t,report,token),token);}
        finally
        {
            var lines=new List<string>();
            foreach(var row in report.rows)
            {
                var fields=new List<string>();
                foreach(var f in typeof(Row).GetFields())
                {var v=f.GetValue(row);fields.Add("\""+f.Name+"\":"+(v is string?"\""+v+"\"":Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)));}
                lines.Add("{"+string.Join(",",fields)+"}");
            }
            File.WriteAllText(Path.Combine(dir,id+"-quality.json"),"{\"rows\":["+string.Join(",\n",lines)+"]}");
        }
    });
    public static string Poll(string id)=>AsyncD.Poll(id);
    public static Task<string> Cancel(string id)=>AsyncD.Cancel(id);
    public static Task<string> Cleanup(string id)=>AsyncD.Cleanup(id);

    static void Run(TestContext t,Report report,CancellationToken token)
    {
        One(t,report,"flat",513,513,(x,y)=>Color.gray,(x,y)=>(x-256)*(x-256)+(y-256)*(y-256)<=24*24,
            new Vector2(256,256),24,token);
        bool Weak(int x,int y)=>x>=113&&x<633&&y>=91&&y<653;
        One(t,report,"weak-boundary",769,769,(x,y)=>Weak(x,y)?new Color(.42f,.42f,.42f,1):new Color(.3f,.3f,.3f,1),Weak,
            new Vector2(370,360),120,token);
        bool Thin(int x,int y)=>(x>=80&&x<360&&y>=160&&y<500)||(x>=350&&x<701&&y>=327&&y<329);
        One(t,report,"two-pixel-branch",773,659,(x,y)=>Thin(x,y)?Color.white:Color.black,Thin,new Vector2(240,327),100,token);
        bool Hair(int x,int y)=>(x>=80&&x<360&&y>=160&&y<500)||(x>=350&&x<701&&y==328);
        One(t,report,"one-pixel-branch",773,659,(x,y)=>Hair(x,y)?Color.white:Color.black,Hair,new Vector2(240,327),100,token);
        bool TextureShape(int x,int y)=>x>=83&&x<601&&y>=47&&y<621;
        Color TextureImage(int x,int y)
        {float v=(TextureShape(x,y)?.6f:.25f)+.035f*(float)Math.Sin(x*.31)* (float)Math.Cos(y*.23);return new Color(v,v*.8f,v*.7f,1);}
        One(t,report,"textured-boundary",701,677,TextureImage,TextureShape,new Vector2(177,260),90,token);
        bool Hdr(int x,int y)=>x>=83&&x<601&&y>=47&&y<621&&!(x>=259&&x<351&&y>=293&&y<369);
        One(t,report,"hdr-hole",701,677,(x,y)=>Hdr(x,y)?new Color(6,-2,2,1):new Color(2,-.5f,1,1),Hdr,new Vector2(177,260),90,token);
        One(t,report,"hdr-hole-clear-hint",701,677,(x,y)=>Hdr(x,y)?new Color(6,-2,2,1):new Color(2,-.5f,1,1),Hdr,new Vector2(155,260),65,token);
        const int size=8192;
        bool Large(int x,int y)=>x>=1793&&x<6407&&y>=2003&&y<6201&&!(x>=3901&&x<4297&&y>=3803&&y<4399);
        One(t,report,"8k-weak-boundary",size,size,(x,y)=>Large(x,y)?new Color(.42f,.42f,.42f,1):new Color(.3f,.3f,.3f,1),Large,
            new Vector2(3000,4096),900,token);
        bool Frame(int x,int y)=>x>=96&&x<8096&&y>=96&&y<8096&&(x<112||x>=8080||y<112||y>=8080);
        One(t,report,"8k-thin-frame",size,size,(x,y)=>Frame(x,y)?new Color(8,-2,1,1):new Color(0,0,0,1),Frame,
            new Vector2(104,104),7,token);
        bool NativeHair(int x,int y)=>(x>=3713&&x<4013&&y>=3949&&y<4349)||(x>=4000&&x<4501&&y==4184);
        One(t,report,"8k-one-pixel-hdr-branch",size,size,(x,y)=>NativeHair(x,y)?new Color(8,-2,1,1):Color.black,NativeHair,
            new Vector2(3860,4140),100,token);
    }
    static void One(TestContext t,Report report,string name,int w,int h,Func<int,int,Color> source,Func<int,int,bool> truth,
        Vector2 point,float radius,CancellationToken token)
    {
        Progress?.Invoke("begin "+name);
        var row=new Row{name=name,width=w,height=h};report.rows.Add(row);var watch=Stopwatch.StartNew();
        object selection=New("CanvasSelection",w,h),engine=New("QuickSelection",w,h,.18f,false);
        try
        {
            engine.GetType().GetProperty("Progressive",F).SetValue(engine,true);
            engine.GetType().GetProperty("RefinementAllowed",F).SetValue(engine,false);
            Call(engine,"Initialize",null,token);Call(engine,"AddWeightedBrush",point,radius,false,false,1f);
            while(true)
            {
                token.ThrowIfCancellationRequested();object request=Call(engine,"Take");
                if(request==null){if((bool)Prop(engine,"IsFinalized"))break;Call(engine,"FinishInput",token);continue;}
                RectInt r=(RectInt)Field(request,"source");int step=(int)(Field(request,"step")??1);
                int rw=(r.width+step-1)/step,rh=(r.height+step-1)/step;
                Color[] pixels=(bool)Field(request,"cleanup")?Array.Empty<Color>():new Color[rw*rh];
                for(int y=0;y<rh&&pixels.Length>0;y++)for(int x=0;x<rw;x++)
                    pixels[y*rw+x]=source(Math.Min(w-1,r.x+x*step+step/2),Math.Min(h-1,r.y+y*step+step/2));
                var worker=Stopwatch.StartNew();object result=Call(engine,"GrowRaw",request,pixels,Prop(selection,"Coverage"),token);
                row.workerMs+=worker.Elapsed.TotalMilliseconds;row.maxWorkerMs=Math.Max(row.maxWorkerMs,worker.Elapsed.TotalMilliseconds);
                if((bool)Field(result,"preview"))
                {
                    row.firstPreviewMs=watch.Elapsed.TotalMilliseconds;
                    t.True(Prop(selection,"Coverage")==null,"Preview does not modify the consumer mask");
                    t.True(((byte[])Field(result,"coverage")).Length<=128*128,"Preview stays bounded at 8K");
                    t.True(Call(engine,"Take")==null,"Refinement waits while the brush is moving");
                    engine.GetType().GetProperty("RefinementAllowed",F).SetValue(engine,true);
                }
                Call(selection,"ApplyQuickSelection",result,Prop(engine,"InitialCoverage"));Call(engine,"Accept",result);row.requests++;
                t.True(row.requests<30000,"Bounded work terminates");
            }
            row.totalMs=watch.Elapsed.TotalMilliseconds;row.hintsBytes=(long)Prop(engine,"MarkerBytes");row.solverBytes=(long)Prop(engine,"DistanceBytes");
            t.True(row.firstPreviewMs>0&&row.firstPreviewMs<row.totalMs,"Preview precedes final native mask");
            double Metric(string n)=>Convert.ToDouble(engine.GetType().GetProperty(n,F)?.GetValue(engine)??0);
            row.solveMs=Metric("SolveMs");row.buildMs=Metric("BuildMs");row.refineMs=Metric("RefineMs");row.publishMs=Metric("PublishMs");row.maxNodes=(int)Metric("MaxNodes");
            var mask=(byte[])Prop(selection,"Coverage");
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {bool got=mask!=null&&mask[y*w+x]>=128,want=truth(x,y);if(got&&want)row.intersection++;if(got||want)row.union++;if(got&&!want)row.falsePositive++;if(!got&&want)row.falseNegative++;}
            row.iou=row.union==0?1:row.intersection/(double)row.union;
            Progress?.Invoke(name+" ms="+row.totalMs+" iou="+row.iou+" FP="+row.falsePositive+" FN="+row.falseNegative);
            t.True(mask!=null&&mask[(int)point.y*w+(int)point.x]==255,"Hard foreground is preserved");
            if(name!="hdr-hole")t.True(row.falsePositive==0&&row.falseNegative==0,"Analytical native boundary matches exactly: "+name);
        }
        finally{((IDisposable)engine).Dispose();}
    }
}
