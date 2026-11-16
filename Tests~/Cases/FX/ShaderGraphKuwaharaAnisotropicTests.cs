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

// Owned transient graphs. Independent double precision sector integration checks the GPU result.
public static class ShaderGraphKuwaharaAnisotropicTests
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

    static Color Oracle(Color[] input,int size,int x,int y,float radius,float sharpness,int uniformity,float eccentricity)
    {
        Color Read(int px,int py)=>input[Mathf.Clamp(py,0,size-1)*size+Mathf.Clamp(px,0,size-1)];
        var center=Read(x,y);if(radius<=0)return center;
        double xx=0,xy=0,yy=0,normalizer=0;
        for(int j=-uniformity;j<=uniformity;j++)for(int i=-uniformity;i<=uniformity;i++)
        {
            double w=Math.Exp(-4.5*(i*i+j*j)/Math.Max(1.0,uniformity*uniformity));normalizer+=w;
            for(int c=0;c<4;c++)
            {
                double dx=.182*(Read(x+i+1,y+j+1)[c]+Read(x+i+1,y+j-1)[c]-Read(x+i-1,y+j+1)[c]-Read(x+i-1,y+j-1)[c])+.636*(Read(x+i+1,y+j)[c]-Read(x+i-1,y+j)[c]);
                double dy=.182*(Read(x+i-1,y+j+1)[c]+Read(x+i+1,y+j+1)[c]-Read(x+i-1,y+j-1)[c]-Read(x+i+1,y+j-1)[c])+.636*(Read(x+i,y+j+1)[c]-Read(x+i,y+j-1)[c]);
                xx+=w*dx*dx;xy+=w*dx*dy;yy+=w*dy*dy;
            }
        }
        xx/=normalizer;xy/=normalizer;yy/=normalizer;
        double d=Math.Sqrt((xx-yy)*(xx-yy)+4*xy*xy),angle=.5*Math.Atan2(2*xy,xx-yy)+Math.PI/2;
        double cs=Math.Cos(angle),sn=Math.Sin(angle),stretch=1+(xx+yy>0?d/(xx+yy):0)*Math.Max(.01,Math.Clamp(eccentricity,0,2));
        double a=radius*stretch,b=radius/stretch,zeta=2/radius,eta=(zeta+Math.Cos(3*Math.PI/16))/Math.Pow(Math.Sin(3*Math.PI/16),2);
        int bx=(int)Math.Ceiling(Math.Sqrt(a*a*cs*cs+b*b*sn*sn)),by=(int)Math.Ceiling(Math.Sqrt(a*a*sn*sn+b*b*cs*cs));
        var sum=new double[8,4];var squares=new double[8,4];var weights=new double[8];
        for(int j=-by;j<=by;j++)for(int i=-bx;i<=bx;i++)
        {
            double u=(i*cs+j*sn)/a,v=(-i*sn+j*cs)/b;if(u*u+v*v>1)continue;
            var values=Read(x+i,y+j);var ws=new double[8];double total=0;
            for(int k=0;k<8;k++){double theta=k*Math.PI/4,c=Math.Cos(theta),s=Math.Sin(theta),rx=c*u-s*v,ry=s*u+c*v;double w=Math.Max(0,ry+zeta-eta*rx*rx);ws[k]=w*w;total+=ws[k];}
            for(int k=0;k<8;k++)
            {
                double w=ws[k]/total*Math.Exp(-Math.PI*(u*u+v*v));weights[k]+=w;
                for(int c=0;c<4;c++){double delta=values[c]-center[c];sum[k,c]+=w*delta;squares[k,c]+=w*delta*delta;}
            }
        }
        var result=new double[4];double weightTotal=0,exponent=16*sharpness*sharpness;
        for(int k=0;k<8;k++)
        {
            double deviation=0;for(int c=0;c<4;c++)deviation+=Math.Sqrt(Math.Max(0,squares[k,c]/weights[k]-Math.Pow(sum[k,c]/weights[k],2)));
            double w=Math.Pow(Math.Max(.02,deviation),-exponent);weightTotal+=w;
            for(int c=0;c<4;c++)result[c]+=w*sum[k,c]/weights[k];
        }
        return new Color((float)(center.r+result[0]/weightTotal),(float)(center.g+result[1]/weightTotal),(float)(center.b+result[2]/weightTotal),(float)(center.a+result[3]/weightTotal));
    }
    public static async Task<object> Run(){try{return await Check(false);}catch(Exception e){return TestContext.Result("failed",0,"Anisotropic Kuwahara",e.ToString()).ToJson();}}
    public static async Task<object> Benchmark(){try{return await Check(true);}catch(Exception e){return TestContext.Result("failed",0,"Anisotropic benchmark",e.ToString()).ToJson();}}
    static async Task<object> Check(bool benchmark)
    {
        var t=new TestContext();int size=benchmark?384:48;
        var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        WhimTexDocument document=null;Texture2D texture=null,readback=null;RenderTexture target=null;
        var measurements=new List<string>();float cpuError=0,vmError=0,directError=0;
        var timer=Stopwatch.StartNew();
        async Task Until(Func<bool> ready,string label){while(!ready()){if(timer.Elapsed.TotalSeconds>52)throw new TimeoutException(label);await Task.Delay(25);}}
        try
        {
            document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=size;
            texture=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var random=new System.Random(815);var input=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(float)x/size,v=(float)y/size,n=(float)random.NextDouble()*.12f;
                float f=Mathf.Sin(18*u+7*Mathf.Sin(4*v)),g=Mathf.Cos(9*v+5*u),h=Mathf.Sqrt((u-.7f)*(u-.7f)+(v-.4f)*(v-.4f));
                input[y*size+x]=new Color(.5f+.42f*f+n,.5f+.35f*g+n,.1f+.7f*Mathf.Clamp01(h*2)+n,.8f+.15f*Mathf.Sin(14*u+v));
            }
            texture.SetPixels(input);texture.Apply();
            target=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};t.True(target.Create(),"Target created");
            readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            Color[] Read(){var old=RenderTexture.active;try{RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,size,size),0,0);readback.Apply();return readback.GetPixels();}finally{RenderTexture.active=old;}}
            var context=Activator.CreateInstance(Find("LayerRenderContext"),new object[]{document,null,size,size,1f,false,false,false});
            var graph=new ShaderGraph();var sample=Node(graph,ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;
            sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});
            var kuw=Node(graph,ShaderGraphNodeKind.Kuwahara);var output=Node(graph,ShaderGraphNodeKind.Output);Connect(graph,sample,kuw,"In");Connect(graph,kuw,output,"RGBA");
            t.Equal(0,kuw.option,"Classic default");t.True(!kuw.kuwaharaHighPrecision,"Precision default");
            t.True(!(bool)Static("ShaderGraphNodes","InputVisible",kuw,"Uniformity"),"Classic hides anisotropic ports");
            kuw.option=1;t.True((bool)Static("ShaderGraphNodes","InputVisible",kuw,"Uniformity"),"Anisotropic shows ports");
            Layer layer=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};Call(typeof(Layer),layer,"AssignNewId");document.layers.Add(layer);
            var requestType=Find("ShaderProgramRuntime+CompilationRequest");var vmType=Find("ShaderGraphPreviewVM");
            void Set(string name,float value)=>kuw.FindInput(name).value=Vector4.one*value;
            async Task<IDisposable> Compile(string previewNode=null)
            {
                using var request=(IDisposable)Activator.CreateInstance(requestType,BindingFlags.Instance|F,null,new object[]{graph,previewNode??kuw.id,previewNode==null?"Out":"Result",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",true},null);
                object[] done={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",done),"Native compile");
                foreach(var diagnostic in (IEnumerable)Get(done[0],"DiagnosticMessages"))t.True(Get(diagnostic,"Severity").ToString() is not ("Warning" or "Error"),diagnostic.ToString());
                return (IDisposable)done[0];
            }
            Color[] Render(IDisposable runtime)
            {
                var rt=runtime.GetType();Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"PrepareCoordinates",layer,context);
                GL.sRGBWrite=false;Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);return Read();
            }
            var settings=benchmark?new[]{new Vector4(4,1,4,1),new Vector4(8,1,4,1),new Vector4(16,1,4,1),new Vector4(32,1,4,1)}:
                new[]{new Vector4(0,1,4,1),new Vector4(2,1,4,1),new Vector4(8,.5f,4,1),new Vector4(6,0,0,0),new Vector4(9,1,8,2)};
            Color[] classicImage=null,anisoImage=null;
            foreach(var config in settings)foreach(int mode in benchmark?new[]{0,1}:new[]{1})
            {
                kuw.option=mode;Set("Radius",config.x);Set("Sharpness",config.y);Set("Uniformity",config.z);Set("Eccentricity",config.w);
                using var runtime=await Compile();var native=Render(runtime);
                if(config.x==16){if(mode==0)classicImage=native;else anisoImage=native;}
                if(mode==1)
                {
                    int[] points=benchmark?new[]{0,127,255,256,257,383}:new[]{0,1,12,24,46,47};
                    foreach(int y in points)foreach(int x in points)
                    {
                        var expected=Oracle(input,size,x,y,config.x,config.y,(int)config.z,config.w);
                        for(int c=0;c<4;c++){float e=Math.Abs(expected[c]-native[y*size+x][c]);cpuError=Math.Max(cpuError,e);t.Near(expected[c],native[y*size+x][c],.001,"CPU sector oracle "+config+" at "+x+","+y+" c"+c);}
                    }
                }
                int draws=(int)Get(runtime,"BufferDrawCount");Render(runtime);t.Equal(draws,(int)Get(runtime,"BufferDrawCount"),"Unchanged image cached");
                if(!benchmark)
                {
                    using var vm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,kuw.id,"Out",false,false,false);
                    t.True(vm!=null,"Hybrid exists");Call(vmType,vm,"ConfigureHybrid",document,layer,false,true);
                    await Until(()=>{if((bool)Get(vm,"Failed"))throw new InvalidOperationException("Hybrid failed");return (bool)Call(vmType,vm,"UpdatePreparation");},"Hybrid prepare");
                    t.True((bool)Call(vmType,vm,"RenderBoundaries",target,context),"Hybrid render");var actual=Read();
                    for(int i=0;i<native.Length;i++)for(int c=0;c<4;c++){vmError=Math.Max(vmError,Math.Abs(actual[i][c]-native[i][c]));t.Near(native[i][c],actual[i][c],.0001,"VM/native parity");}
                    if(config.x==2)
                    {
                        var uv=Node(graph,ShaderGraphNodeKind.UV);Connect(graph,uv,kuw,"UV");
                        using(var direct=await Compile())
                        {
                            var directPixels=Render(direct);
                            for(int i=0;i<native.Length;i++)for(int c=0;c<4;c++){directError=Math.Max(directError,Math.Abs(directPixels[i][c]-native[i][c]));t.Near(native[i][c],directPixels[i][c],.0005,"Prepared/procedural parity");}
                        }
                        graph.edges.RemoveAll(e=>e.outputNode==uv.id);graph.nodes.Remove(uv);
                        foreach(string control in new[]{"Sharpness","Uniformity","Eccentricity"})
                        {
                            float saved=kuw.FindInput(control).value.x;Set(control,saved==1?.3f:0);
                            int before=(int)Get(runtime,"BufferDrawCount");Render(runtime);t.True((int)Get(runtime,"BufferDrawCount")>before,control+" invalidates prepared image");Set(control,saved);
                        }
                    }
                }
                else
                {
                    var times=new List<double>();
                    for(int i=0;i<6;i++)
                    {
                        texture.SetPixel(0,0,input[0]);texture.Apply(false,false);int before=(int)Get(runtime,"BufferDrawCount");
                        var watch=Stopwatch.StartNew();Call(runtime.GetType(),runtime,"Render",Texture2D.blackTexture,target,context);
                        var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();watch.Stop();
                        t.True(!fence.hasError,"GPU fence");if(config.x>5||mode==1)t.True((int)Get(runtime,"BufferDrawCount")>before,"Real uncached render");
                        if(i>0)times.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();measurements.Add($"r{config.x} {(mode==0?"Classic":"Anisotropic")} {times[2]:F3} ms ({string.Join(",",times.Select(v=>v.ToString("F3")))})");
                    if(mode==1)
                    {
                        using var vm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,kuw.id,"Out",false,false,false);
                        t.True(vm!=null,"Benchmark hybrid exists");Call(vmType,vm,"ConfigureHybrid",document,layer,false,true);
                        await Until(()=>{if((bool)Get(vm,"Failed"))throw new InvalidOperationException("Benchmark hybrid failed");return (bool)Call(vmType,vm,"UpdatePreparation");},"Benchmark hybrid");
                        t.True((bool)Call(vmType,vm,"RenderBoundaries",target,context),"Benchmark hybrid render");
                        var vmPixels=Read();for(int i=0;i<native.Length;i+=19)for(int c=0;c<4;c++){vmError=Math.Max(vmError,Math.Abs(native[i][c]-vmPixels[i][c]));t.Near(native[i][c],vmPixels[i][c],.0001,"Benchmark SVM parity");}
                        var vmRuntime=Get(vm,"hybridRuntime");times.Clear();
                        for(int i=0;i<6;i++)
                        {
                            texture.SetPixel(0,0,input[0]);texture.Apply(false,false);int before=(int)Get(vmRuntime,"BufferDrawCount");
                            var watch=Stopwatch.StartNew();t.True((bool)Call(vmType,vm,"RenderBoundaries",target,context),"Timed hybrid render");
                            var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();watch.Stop();
                            t.True(!fence.hasError,"Hybrid GPU fence");t.True((int)Get(vmRuntime,"BufferDrawCount")>before,"Hybrid rerenders prepared image");
                            if(i>0)times.Add(watch.Elapsed.TotalMilliseconds);
                        }
                        times.Sort();measurements.Add($"r{config.x} SVM hybrid {times[2]:F3} ms ({string.Join(",",times.Select(v=>v.ToString("F3")))})");
                    }
                }
            }
            if(!benchmark)
            {
                // A scalar source still gets a typed result, rather than inheriting a Color tag.
                graph.edges.Find(e=>e.inputNode==kuw.id&&e.inputPort=="In").outputPort="R";
                Set("Radius",3);Set("Uniformity",2);Set("Sharpness",.5f);Set("Eccentricity",1);
                using var scalar=await Compile();var scalarNative=Render(scalar);
                using var scalarVm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,kuw.id,"Out",false,false,false);
                t.True(scalarVm!=null,"Scalar hybrid exists");Call(vmType,scalarVm,"ConfigureHybrid",document,layer,false,true);
                await Until(()=>{if((bool)Get(scalarVm,"Failed"))throw new InvalidOperationException("Scalar hybrid failed");return (bool)Call(vmType,scalarVm,"UpdatePreparation");},"Scalar hybrid");
                t.True((bool)Call(vmType,scalarVm,"RenderBoundaries",target,context),"Scalar hybrid render");var scalarActual=Read();
                for(int i=0;i<scalarNative.Length;i++)for(int c=0;c<4;c++)t.Near(scalarNative[i][c],scalarActual[i][c],.0001,"Scalar SVM/native");
                t.True((bool)Get(scalarVm,"HasBoundaries"),"Prepared image is consumed by SVM");
                // Radius reads the current loop index. The last iteration must match r=1,
                // so preparing a fixed image before entering the loop would fail this comparison.
                Set("Radius",1);Set("Uniformity",0);using var expectedRuntime=await Compile();var expectedLoop=Render(expectedRuntime);
                var begin=Node(graph,ShaderGraphNodeKind.ForBegin);var end=Node(graph,ShaderGraphNodeKind.ForEnd);
                begin.loopPartnerId=end.id;end.loopPartnerId=begin.id;begin.FindInput("Iterations").value=Vector4.one*2;
                Connect(graph,begin,kuw,"Radius","Index");Connect(graph,kuw,end,"Value");
                graph.edges.RemoveAll(e=>e.inputNode==output.id);Connect(graph,end,output,"RGBA","Result");
                using(var loop=await Compile(end.id))
                {var loopPixels=Render(loop);for(int i=0;i<loopPixels.Length;i++)for(int c=0;c<4;c++)t.Near(expectedLoop[i][c],loopPixels[i][c],.0005,"Anisotropic per-iteration radius");}
                graph.edges.RemoveAll(e=>e.outputNode==begin.id||e.inputNode==end.id||e.outputNode==end.id);
                graph.nodes.Remove(begin);graph.nodes.Remove(end);Connect(graph,kuw,output,"RGBA");
            }
            if(benchmark)
            {
                var strip=new Texture2D(size*3,size,TextureFormat.RGB24,false,true){hideFlags=HideFlags.HideAndDontSave};
                try{strip.SetPixels(0,0,size,size,input);strip.SetPixels(size,0,size,size,classicImage);strip.SetPixels(size*2,0,size,size,anisoImage);strip.Apply();
                    System.IO.Directory.CreateDirectory("Temp/WhimTex/anisotropic-work");System.IO.File.WriteAllBytes("Temp/WhimTex/anisotropic-work/comparison.png",strip.EncodeToPNG());}finally{Object.DestroyImmediate(strip);}
            }
            graph.nodes.Remove(sample);graph.edges.RemoveAll(e=>e.outputNode==sample.id);kuw.option=1;Set("Sharpness",.7f);Set("Uniformity",7);Set("Eccentricity",1.7f);
            using(var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false))
            {var copy=((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph.FindNode(kuw.id);t.Equal(1,copy.option,"Mode roundtrip");t.Near(.7f,copy.FindInput("Sharpness").value.x,0,"Sharpness roundtrip");t.Near(7,copy.FindInput("Uniformity").value.x,0,"Uniformity roundtrip");t.Near(1.7f,copy.FindInput("Eccentricity").value.x,0,"Eccentricity roundtrip");}
        }
        finally
        {
            RenderTexture.active=active;GL.sRGBWrite=srgb;
            if(document!=null)Object.DestroyImmediate(document);if(texture!=null)Object.DestroyImmediate(texture);if(readback!=null)Object.DestroyImmediate(readback);if(target!=null){target.Release();Object.DestroyImmediate(target);}
        }
        return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message=$"Anisotropic {size}x{size}; CPU {cpuError:R}, VM {vmError:R}, procedural {directError:R}. "+string.Join("; ",measurements)};
    }
}
