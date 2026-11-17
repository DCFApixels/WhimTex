using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class ShaderGraphMedianTests
{
    public static async Task<object> UniformRun()
    {
        var t=new TestContext();float cpuError=0,vmError=0;
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        WhimTexDocument doc=null;Texture2D input=null,readback=null;RenderTexture target=null;
        try
        {
            const int size=32;
            doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=size;
            input=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];var rng=new System.Random(91738);
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((float)rng.NextDouble()*20-10,(float)rng.NextDouble()*1000,i%4*.25f,i%3==0?0:(float)rng.NextDouble()*6-2);
            input.SetPixels(pixels);input.Apply();
            target=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};t.True(target.Create(),"Target");
            readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            Color[] Read(){RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,size,size),0,0);return readback.GetPixels();}
            var baseContext=Activator.CreateInstance(Find("LayerRenderContext"),new object[]{doc,null,size,size,1f,false,false,false});
            object Context(float time)=>Call(baseContext.GetType(),baseContext,"WithShaderGraphPreviewTime",(float?)time);
            async Task Wait(Func<bool> ready)
            { var timer=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(timer.Elapsed.TotalSeconds>45)throw new TimeoutException("Median preparation");await Task.Delay(20);}}
            void CheckPixels(Color[] actual,Func<int,int,int> radius)
            {
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    var expected=Oracle(pixels,size,x,y,radius(x,y));
                    for(int c=0;c<4;c++){cpuError=Math.Max(cpuError,Math.Abs(expected[c]-actual[y*size+x][c]));t.Near(expected[c],actual[y*size+x][c],.0008,"CPU median");}
                }
            }
            foreach(string kind in new[]{"Value","Parameter","Math","Time","Int","Pixels","UV","Expression"})
            {
                doc.pixelScale=kind=="Pixels"?2:1;
                baseContext=Activator.CreateInstance(Find("LayerRenderContext"),new object[]{doc,null,size,size,1f,false,false,false});
                var graph=new ShaderGraph();var sample=Node(graph,ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;
                sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=input}});
                var median=Node(graph,ShaderGraphNodeKind.Median);Connect(graph,sample,median,"In");Connect(graph,median,Node(graph,ShaderGraphNodeKind.Output),"RGBA");
                var parameter=new ShaderParameter{id=Guid.NewGuid().ToString("N"),name="Radius",type=ShaderParameterType.Float,floatValue=4};graph.parameters.Add(parameter);
                var number=Node(graph,ShaderGraphNodeKind.Value);number.literal.floatValue=4;
                var source=Node(graph,ShaderGraphNodeKind.Parameter);source.parameterId=parameter.id;
                if(kind=="Value")source=number;
                if(kind=="Int"){parameter.valueType="int";parameter.integerValues[0]=4;}
                if(kind=="Pixels")parameter.pixelUnits=true;
                if(kind=="Math")
                {
                    var multiply=Node(graph,ShaderGraphNodeKind.Multiply);multiply.FindInput("B").value=Vector4.one*2;Connect(graph,source,multiply,"A");
                    var add=Node(graph,ShaderGraphNodeKind.Add);add.FindInput("B").value=Vector4.one*.25f;Connect(graph,multiply,add,"A");
                    var reroute=Node(graph,ShaderGraphNodeKind.Reroute);Connect(graph,add,reroute,"In");source=reroute;
                }
                if(kind=="Time")source=Node(graph,ShaderGraphNodeKind.Time);
                if(kind=="UV")
                {
                    var uv=Node(graph,ShaderGraphNodeKind.UV);source=Node(graph,ShaderGraphNodeKind.Multiply);source.FindInput("B").value=Vector4.one*8;Connect(graph,uv,source,"A");
                }
                if(kind=="Expression")
                {
                    source=Node(graph,ShaderGraphNodeKind.Expression);source.expression="4";
                }
                Connect(graph,source,median,"Radius");
                Layer layer=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};Call(typeof(Layer),layer,"AssignNewId");doc.layers.Add(layer);
                bool uniform=kind is not ("UV" or "Expression");
                object compiled=Static("ShaderGraphCompiler","Compile",graph,median.id,"Out",false);
                var passes=((IEnumerable)Get(compiled,"buffers")).Cast<object>().ToArray();
                t.Equal(uniform?3:0,passes.Length,kind+" boundary classification");
                if(uniform)t.Equal(1,passes.Count(p=>Get(p,"scalarBinding")!=null),"One scalar evaluation");
                using var runtime=(IDisposable)Static("ShaderProgramRuntime","Create",graph,median.id,"Out",false);
                var rt=runtime.GetType();
                foreach(var diagnostic in (IEnumerable)Get(runtime,"DiagnosticMessages"))t.True(Get(diagnostic,"Severity").ToString() is not ("Warning" or "Error"),diagnostic.ToString());
                IDisposable vm=null;
                try
                {
                    if(uniform)
                    {
                        vm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,median.id,"Out",false,false,false);t.True(vm!=null,"Hybrid VM exists");
                        Call(vm.GetType(),vm,"ConfigureHybrid",doc,layer,false,true);
                        await Wait(()=>{if((bool)Get(vm,"Failed"))throw new Exception("VM preparation failed");return (bool)Call(vm.GetType(),vm,"UpdatePreparation");});
                    }
                    ulong code=(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph);
                    foreach(float value in uniform?new[]{-1f,0,1,2,3,8,4}:new[]{4f})
                    {
                        number.literal.floatValue=value;parameter.floatValue=value;parameter.integerValues[0]=(long)value;
                        var context=Context(value);
                        t.Equal(code,(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph),"Uniform value edits keep executable fingerprint");
                        Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"PrepareCoordinates",layer,context);
                        GL.sRGBWrite=false;Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);var native=Read();
                        // Pixel-valued parameters use the document pixel multiplier.
                        float effective=kind=="Math"?value*2+.25f:kind=="Pixels"?value*2:kind=="Expression"?4:value;
                        int radius=Math.Max(0,(int)Math.Floor(effective));
                        CheckPixels(native,(x,y)=>kind=="UV"?(int)((x+.5f)/size*8):radius);
                        int draws=(int)Get(runtime,"BufferDrawCount"),reads=(int)Get(runtime,"UniformReadbackCount");
                        Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                        t.Equal(draws,(int)Get(runtime,"BufferDrawCount"),"Unchanged graph reuses result");t.Equal(reads,(int)Get(runtime,"UniformReadbackCount"),"Unchanged Radius does not read back again");
                        if(vm!=null)
                        {
                            t.True((bool)Call(vm.GetType(),vm,"TryUpdateValues",graph,null),"Update SVM values without rebuilding");
                            t.True((bool)Call(vm.GetType(),vm,"RenderBoundaries",target,context),"SVM renders prepared Median");var actual=Read();
                            t.Equal(0,(int)Get(Get(vm,"hybridRuntime"),"UniformReadbackCount"),"SVM reuses the document's scalar readback");
                            for(int i=0;i<actual.Length;i++)for(int c=0;c<4;c++){vmError=Math.Max(vmError,Math.Abs(native[i][c]-actual[i][c]));t.Near(native[i][c],actual[i][c],.0008,"HLSL/SVM");}
                        }
                    }
                    if(uniform)
                    {
                        int reads=(int)Get(runtime,"UniformReadbackCount"),draws=(int)Get(runtime,"BufferDrawCount");
                        input.SetPixel(0,0,pixels[0]);input.Apply();var context=Context(4);
                        Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                        t.Equal(reads,(int)Get(runtime,"UniformReadbackCount"),"Source-only changes retain scalar result");t.True((int)Get(runtime,"BufferDrawCount")>draws,"Source-only changes recompute Median");
                    }
                }
                finally{vm?.Dispose();doc.layers.Remove(layer);}
            }
            return new TestResult{status="passed",checks=t.Checks,message=$"Connected uniform Median, CPU error {cpuError:R}, HLSL/SVM {vmError:R}; Value/Parameter/math/Time/int/pixel units, fallback, cache and shared readback.",failures=Array.Empty<string>()};
        }
        catch(Exception e){return TestContext.Result("failed",t.Checks,"Connected Median",e.ToString()).ToJson();}
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            if(doc!=null)Object.DestroyImmediate(doc);if(input!=null)Object.DestroyImmediate(input);if(readback!=null)Object.DestroyImmediate(readback);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}
        }
    }
}
