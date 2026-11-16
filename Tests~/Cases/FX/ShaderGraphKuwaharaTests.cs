using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.Rendering;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Owned transient graphs only. The oracle enumerates complete quadrants in double precision.
public static class ShaderGraphKuwaharaTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) => type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target,args);
    static object Get(object target,string name) => target.GetType().GetField(name,F|BindingFlags.Instance)?.GetValue(target) ?? target.GetType().GetProperty(name,F|BindingFlags.Instance)?.GetValue(target);
    static object Static(string type,string name,params object[] args) => Call(Find(type),null,name,args);
    static ShaderGraphNode Node(ShaderGraph graph,ShaderGraphNodeKind kind)
    { var node=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,Vector2.zero); graph.nodes.Add(node);return node; }
    static void Connect(ShaderGraph graph,ShaderGraphNode from,ShaderGraphNode to,string input,string output="Out")
        => graph.edges.Add(new ShaderGraphEdge{outputNode=from.id,outputPort=output,inputNode=to.id,inputPort=input});
    static Color Oracle(Color[] input,int size,int x,int y,int r)
    {
        double best=double.PositiveInfinity; var result=new Color();
        for(int q=0;q<4;q++)
        {
            var sum=new double[4];var squares=new double[4];int count=(r+1)*(r+1);
            for(int dy=0;dy<=r;dy++)for(int dx=0;dx<=r;dx++)
            {
                var c=input[Mathf.Clamp(y+((q&2)==0?-dy:dy),0,size-1)*size+Mathf.Clamp(x+((q&1)==0?-dx:dx),0,size-1)];
                for(int k=0;k<4;k++){double v=c[k];sum[k]+=v;squares[k]+=v*v;}
            }
            double variance=0;for(int k=0;k<4;k++)variance+=Math.Max(0,squares[k]/count-sum[k]*sum[k]/count/count);
            if(variance<best){best=variance;for(int k=0;k<4;k++)result[k]=(float)(sum[k]/count);}
        }
        return result;
    }
    public static async Task<object> Run()
    {
        try{return await Check(false);}catch(Exception e){return TestContext.Result("failed",0,"Kuwahara tables",e.ToString()).ToJson();}
    }
    public static async Task<object> Benchmark()
    {
        try{return await Check(true);}catch(Exception e){return TestContext.Result("failed",0,"Kuwahara benchmark",e.ToString()).ToJson();}
    }
    static async Task<object> Check(bool benchmark)
    {
        var t=new TestContext();int size=benchmark?384:64;
        var originalActive=RenderTexture.active;bool originalSrgb=GL.sRGBWrite;
        WhimTexDocument document=null;Texture2D texture=null,readback=null;RenderTexture target=null;
        var measurements=new List<string>();float maximumError=0,maximumVmError=0;
        var timeout=Stopwatch.StartNew();
        async Task Until(Func<bool> ready,string label)
        {while(!ready()){if(timeout.Elapsed.TotalSeconds>50)throw new TimeoutException(label);await Task.Delay(25);}}
        try
        {
            document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=size;
            texture=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var random=new System.Random(71253);var input=new Color[size*size];
            for(int i=0;i<input.Length;i++)input[i]=new Color((float)random.NextDouble()*2-1,(float)random.NextDouble()*3,(float)random.NextDouble(),(float)random.NextDouble());
            texture.SetPixels(input);texture.Apply();
            target=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};t.True(target.Create(),"Target allocated");
            readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            Color[] Read(){var active=RenderTexture.active;try{RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,size,size),0,0);readback.Apply();return readback.GetPixels();}finally{RenderTexture.active=active;}}
            var context=Activator.CreateInstance(Find("LayerRenderContext"),new object[]{document,null,size,size,1f,false,false,false});
            var graph=new ShaderGraph();var sample=Node(graph,ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;
            sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});
            var kuw=Node(graph,ShaderGraphNodeKind.Kuwahara);var output=Node(graph,ShaderGraphNodeKind.Output);Connect(graph,sample,kuw,"In");Connect(graph,kuw,output,"RGBA");
            t.True(!kuw.kuwaharaHighPrecision,"High Precision defaults off");
            Layer layer=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};Call(typeof(Layer),layer,"AssignNewId");document.layers.Add(layer);
            var requestType=Find("ShaderProgramRuntime+CompilationRequest");var vmType=Find("ShaderGraphPreviewVM");
            foreach(int radius in benchmark?new[]{8,16,32,64}:new[]{0,2,6,13})
            foreach(bool precise in new[]{false,true})
            {
                kuw.FindInput("Radius").value=Vector4.one*radius;kuw.kuwaharaHighPrecision=precise;
                using var request=(IDisposable)Activator.CreateInstance(requestType,BindingFlags.Instance|F,null,
                    new object[]{graph,kuw.id,"Out",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",true},null);
                object[] completed={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",completed),"Native compilation");
                using var runtime=(IDisposable)completed[0];var rt=runtime.GetType();
                foreach(var diagnostic in (IEnumerable)Get(runtime,"DiagnosticMessages"))
                    t.True(Get(diagnostic,"Severity").ToString() is not ("Warning" or "Error"),diagnostic.ToString());
                Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"PrepareCoordinates",layer,context);
                GL.sRGBWrite=false;Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);var native=Read();
                int[] coordinates=benchmark?new[]{0,1,128,254,255,256,257,382,383}:Enumerable.Range(0,size).ToArray();
                foreach(int y in coordinates)foreach(int x in coordinates)
                {
                    var expected=Oracle(input,size,x,y,radius);
                    for(int k=0;k<4;k++){float e=Math.Abs(expected[k]-native[y*size+x][k]);maximumError=Math.Max(maximumError,e);t.Near(expected[k],native[y*size+x][k],.0008f,$"r{radius}/{precise}/{x},{y}/{k}");}
                }
                int draws=(int)Get(runtime,"BufferDrawCount");Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                if(radius>5)t.Equal(draws,(int)Get(runtime,"BufferDrawCount"),"Unchanged source reuses filtered image");
                if(!benchmark && radius==6 && !precise)
                {
                    kuw.FindInput("Radius").value=Vector4.one*7;
                    Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                    var changed=Read();t.True((int)Get(runtime,"BufferDrawCount")>draws,"Radius edit invalidates cached result without recompilation");
                    foreach(int p in new[]{0,17,32,63})
                    {
                        var expected=Oracle(input,size,p,p,7);
                        for(int k=0;k<4;k++)t.Near(expected[k],changed[p*size+p][k],.0008f,"Updated radius pixels");
                    }
                    kuw.FindInput("Radius").value=Vector4.one*radius;
                }
                if(!benchmark)
                {
                    IDisposable vm;
                    if(radius>5)vm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,kuw.id,"Out",false,false,false);
                    else{object[] args={graph,kuw.id,"Out",null};t.True((bool)Call(vmType,null,"TryCreate",args),"Direct SVM build");vm=(IDisposable)args[3];}
                    using(vm)
                    {
                        t.True(vm!=null,"VM exists");Call(vmType,vm,"ConfigureHybrid",document,layer,false,true);
                        await Until(()=>{if((bool)Get(vm,"Failed"))throw new InvalidOperationException("SVM failed");return (bool)Call(vmType,vm,"UpdatePreparation");},"SVM preparation");
                        t.True((bool)Call(vmType,vm,"RenderBoundaries",target,context),"SVM render");var actual=Read();
                        for(int i=0;i<actual.Length;i++)for(int k=0;k<4;k++){float e=Math.Abs(actual[i][k]-native[i][k]);maximumVmError=Math.Max(maximumVmError,e);t.Near(native[i][k],actual[i][k],.0008f,"SVM/native");}
                    }
                }
                else
                {
                    var times=new List<double>();
                    for(int iteration=0;iteration<6;iteration++)
                    {
                        texture.SetPixel(0,0,input[0]);texture.Apply(false,false); // Invalidate outside the timed region, preserving pixels.
                        int before=(int)Get(runtime,"BufferDrawCount");
                        var timer=Stopwatch.StartNew();Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                        var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();
                        timer.Stop();t.True(!fence.hasError,"GPU completion");
                        t.True((int)Get(runtime,"BufferDrawCount")>before,"Benchmark performs filtering rather than returning a cached image");
                        if(iteration>0)times.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();measurements.Add($"r{radius} {(precise?"direct":"tables")}: {times[2]:F3} ms median ({string.Join(",",times.Select(v=>v.ToString("F3")))})");
                }
            }
            kuw.FindInput("Radius").value=Vector4.one*5;ulong small=(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph);
            kuw.FindInput("Radius").value=Vector4.one*6;t.True(small!=(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph),"Crossing radius threshold changes executable fingerprint");
            object Compile()=>Static("ShaderGraphCompiler","Compile",graph,null,"Out",false);
            t.Equal(2,((ICollection)Get(Compile(),"buffers")).Count,"Large default radius has source and result passes");
            var dynamicRadius=Node(graph,ShaderGraphNodeKind.Value);Connect(graph,dynamicRadius,kuw,"Radius");
            t.Equal(0,((ICollection)Get(Compile(),"buffers")).Count,"Connected radius remains direct");
            graph.edges.RemoveAll(e=>e.outputNode==dynamicRadius.id);graph.nodes.Remove(dynamicRadius);
            var fractal=Node(graph,ShaderGraphNodeKind.Fractal);Connect(graph,kuw,fractal,"In");
            graph.edges.RemoveAll(e=>e.inputNode==output.id);Connect(graph,fractal,output,"RGBA");
            t.Equal(0,((ICollection)Get(Compile(),"buffers")).Count,"Fractal preserves per-iteration branch evaluation");
            graph.edges.RemoveAll(e=>e.inputNode==fractal.id||e.outputNode==fractal.id);graph.nodes.Remove(fractal);Connect(graph,kuw,output,"RGBA");
            graph.nodes.Remove(sample); graph.edges.RemoveAll(e=>e.outputNode==sample.id);
            kuw.kuwaharaHighPrecision=true;string json=WhimTexDocumentJson.Write(document).Json;
            using(var loaded=WhimTexDocumentJson.Read(json,false))t.True(((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph.FindNode(kuw.id).kuwaharaHighPrecision,"High Precision JSON roundtrip");
        }
        finally
        {
            RenderTexture.active=originalActive;GL.sRGBWrite=originalSrgb;
            if(document!=null)Object.DestroyImmediate(document);if(texture!=null)Object.DestroyImmediate(texture);if(readback!=null)Object.DestroyImmediate(readback);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}
        }
        return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message=$"Kuwahara {size}x{size}, CPU error {maximumError:R}, VM error {maximumVmError:R}. "+string.Join("; ",measurements)};
    }
}
