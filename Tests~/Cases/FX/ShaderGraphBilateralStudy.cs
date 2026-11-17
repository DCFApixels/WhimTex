using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Owns every graph/texture. Frozen sources precede production edits; shader variants
// isolate arithmetic from density. Readback timings include submission and synchronization.
public static class ShaderGraphBilateralStudy
{
    const int Size=128;
    const string Dir="Temp/WhimTex/BilateralStudy/";
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Type T(string name)=>typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex."+name,true);
    static object Call(Type t,object o,string name,params object[] args)=>t.GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
    static object Get(object o,string name)=>o.GetType().GetField(name,F)?.GetValue(o)??o.GetType().GetProperty(name,F)?.GetValue(o);
    static string Hash(string code){using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(code))).Replace("-","");}
    static void Set(ShaderGraphNode n,string port,float value)=>n.FindInput(port).value=Vector4.one*value;
    static ShaderGraph Make(int source,int mode,out ShaderGraphNode root)
    {
        var g=new ShaderGraph();
        ShaderGraphNode Node(ShaderGraphNodeKind k){var n=(ShaderGraphNode)Call(T("ShaderGraphNodes"),null,"Create",k,Vector2.zero);g.nodes.Add(n);return n;}
        void Edge(ShaderGraphNode a,ShaderGraphNode b,string input,string output="Out")=>g.edges.Add(new ShaderGraphEdge{outputNode=a.id,outputPort=output,inputNode=b.id,inputPort=input});
        ShaderGraphNode field;string port="Out";
        if(source==0)field=Node(ShaderGraphNodeKind.SampleInput);
        else {var noise=Node(ShaderGraphNodeKind.GaborNoise);Set(noise,"Scale",16);field=Node(ShaderGraphNodeKind.Fractal);field.fractal=ShaderGraphFractal.FBm;Set(field,"Octaves",3);Edge(noise,field,"In","Value");}
        if(source==2){var b=Node(ShaderGraphNodeKind.Buffer);b.bufferResolution=ShaderGraphBufferResolution.Canvas;b.wrap=ShaderGraphWrap.Clamp;Edge(field,b,"In",port);field=b;}
        root=Node(ShaderGraphNodeKind.BilateralBlur);root.option=mode;Set(root,"Tolerance",.2f);Edge(field,root,"In");
        if(mode==1){var uv=Node(ShaderGraphNodeKind.UV);var split=Node(ShaderGraphNodeKind.Split);Edge(uv,split,"In");Edge(split,root,"Guide","X");}
        Edge(root,Node(ShaderGraphNodeKind.Output),"RGBA");return g;
    }
    const string Weights="float rangeWeight=tolerance==0 ? (distance2==0 ? 1 : 0) : exp(-0.5*distance2/max(tolerance*tolerance,1e-30));\nfloat weight=exp(-2*dot(offset,offset))*rangeWeight;";
    static string Variant(string code,string name)
    {
        code=string.Join("\n",code.Replace("\r\n","\n").Split('\n').Select(line=>line.TrimStart()));
        if(!code.Contains(Weights))throw new Exception("Original bilateral weights missing");
        if(name is "high" or "reference")return code.Replace("int extent=(int)max(1,ceil(radius));","int extent="+(name=="high"?2:4)+"*(int)max(1,ceil(radius));");
        if(name=="fused")return code.Replace(Weights,"float weight=tolerance==0 ? (distance2==0 ? exp(-2*dot(offset,offset)) : 0) : exp(-2*dot(offset,offset)-0.5*distance2/max(tolerance*tolerance,1e-30));");
        if(name!="row")return code;
        return code.Replace("[loop] for(int y=-extent;y<=extent;y++) [loop] for(int x=-extent;x<=extent;x++) {",
            "float invExtent=rcp((float)extent); float rangeScale=-0.5/max(tolerance*tolerance,1e-30);\n[loop] for(int y=-extent;y<=extent;y++) { float offsetY=y*invExtent; float rowExponent=-2*offsetY*offsetY;\n[loop] for(int x=-extent;x<=extent;x++) {")
            .Replace("float2 offset=float2(x,y)/extent;","float2 offset=float2(x*invExtent,offsetY);")
            .Replace(Weights,"float spatialExponent=rowExponent-2*offset.x*offset.x; float weight=tolerance==0 ? (distance2==0 ? exp(spatialExponent) : 0) : exp(spatialExponent+rangeScale*distance2);")
            .Replace("total+=weight;\n}\nsum/=max(total,1e-20);","total+=weight;\n}}\nsum/=max(total,1e-20);");
    }
    public static async Task<object> Run(int source,bool after)
    {
        try{return await RunCore(source,after);}
        catch(Exception e){return TestContext.Result("failed",0,"Bilateral study",e.ToString());}
    }
    public static async Task<object> RunTiming(int source)
    {
        try{return await RunCore(source,true,true);}
        catch(Exception e){return TestContext.Result("failed",0,"Bilateral paired timings",e.ToString());}
    }
    static async Task<object> RunCore(int source,bool after,bool timingOnly=false)
    {
        var test=new TestContext();Directory.CreateDirectory(Dir);
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=Size;
        var owner=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph};Layer layer=owner;Call(typeof(Layer),layer,"AssignNewId");doc.layers.Add(layer);
        var ctx=Activator.CreateInstance(T("LayerRenderContext"),new object[]{doc,null,Size,Size,1f,false,false,false});
        var owned=new List<Object>();var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        RenderTexture RT(int size){var rt=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};rt.Create();owned.Add(rt);return rt;}
        var target=RT(Size);var fence=RT(1);var pixels=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);owned.Add(pixels);
        var sync=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);owned.Add(sync);
        var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};owned.Add(texture);
        var data=new Color[Size*Size];for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)data[y*Size+x]=new Color(x<Size/2?.1f:1.5f,((x/3+y/5)%2)*.5f-.2f,x%11==0?2:0,(y/(float)Size-.5f)*2);
        texture.SetPixels(data);texture.Apply();owner.inputMode=source==0?ShaderLayerInputMode.Texture:ShaderLayerInputMode.None;owner.inputTexture=source==0?texture:null;
        Texture input=source==0?texture:Texture2D.blackTexture;var rows=new List<object>();
        async Task Until(Func<bool> done,string label){var timer=Stopwatch.StartNew();while(!done()){if(timer.Elapsed.TotalSeconds>120)throw new TimeoutException(label);await Task.Delay(25);}}
        void Clean(IEnumerable diagnostics){foreach(var d in diagnostics)test.True((Get(d,"Severity")??Get(d,"severity"))?.ToString()=="Info","Diagnostic: "+d);}
        Color[] Read(){RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Size,Size),0,0);pixels.Apply();return pixels.GetPixels();}
        void Sync(){Graphics.Blit(target,fence);RenderTexture.active=fence;sync.ReadPixels(new Rect(0,0,1,1),0,0);sync.Apply();}
        double Draw(Material m){var timer=Stopwatch.StartNew();Graphics.Blit(input,target,m,0);Sync();return timer.Elapsed.TotalMilliseconds;}
        try
        {
            GL.sRGBWrite=false;
            for(int mode=0;mode<2;mode++)
            {
                string prefix=Dir+source+"-"+mode;ShaderGraph graph;ShaderGraphNode root;
                if(after||File.Exists(prefix+".before.shader")){graph=JsonUtility.FromJson<ShaderGraph>(File.ReadAllText(prefix+".graph.json"));root=graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.BilateralBlur);}
                else {graph=Make(source,mode,out root);File.WriteAllText(prefix+".graph.json",JsonUtility.ToJson(graph));}
                owner.draftGraph=graph;var runtimes=new List<IDisposable>();var mats=new List<Material>();var names=new List<string>();
                try
                {
                    for(int q=0;q<(after&&!timingOnly?2:1);q++)
                    {
                        root.filterQuality=(ShaderGraphFilterQuality)q;
                        var requestType=T("ShaderProgramRuntime+CompilationRequest");
                        using var request=(IDisposable)Activator.CreateInstance(requestType,F,null,new object[]{graph,root.id,"Out",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",true},null);
                        object[] result={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",result),"Compile runtime");
                        var runtime=(IDisposable)result[0];runtimes.Add(runtime);Clean((IEnumerable)Get(runtime,"DiagnosticMessages"));
                        Call(runtime.GetType(),runtime,"Bind",graph,ctx,null,true,false);Call(runtime.GetType(),runtime,"PrepareCoordinates",layer,ctx);Call(runtime.GetType(),runtime,"Render",input,target,ctx);Sync();
                        mats.Add((Material)Get(runtime,"Material"));names.Add(after?(q==0?"standard":"production-high"):"before");
                        if(!after)
                        {
                            string code=(string)Get(runtime,"ShaderSource");
                            if(File.Exists(prefix+".before.shader"))test.Equal(File.ReadAllText(prefix+".sha256"),Hash(code),"Refuse to replace different baseline");
                            else{File.WriteAllText(prefix+".before.shader",code);File.WriteAllText(prefix+".sha256",Hash(code));}
                        }
                    }
                    string frozen=File.ReadAllText(prefix+".before.shader");test.Equal(File.ReadAllText(prefix+".sha256"),Hash(frozen),"Baseline hash");
                    foreach(string name in timingOnly?new[]{"before"}:after?new[]{"before","high","reference"}:new[]{"fused","row","high","reference"})
                    {
                        var shader=ShaderUtil.CreateShaderAsset(Variant(frozen,name));shader.hideFlags=HideFlags.HideAndDontSave;owned.Add(shader);
                        var mat=new Material(shader){hideFlags=HideFlags.HideAndDontSave};owned.Add(mat);bool oldAsync=ShaderUtil.allowAsyncCompilation;
                        try{ShaderUtil.allowAsyncCompilation=true;ShaderUtil.CompilePass(mat,0,false);}finally{ShaderUtil.allowAsyncCompilation=oldAsync;}
                        await Until(()=>ShaderUtil.IsPassCompiled(mat,0),"Compile "+name);Clean(ShaderUtil.GetShaderMessages(shader));mats.Add(mat);names.Add(name);
                    }
                    foreach(float radius in new[]{2f,8f,16f})
                    {
                        Set(root,"Radius",radius);
                        for(int q=0;q<runtimes.Count;q++){root.filterQuality=(ShaderGraphFilterQuality)q;var rt=runtimes[q];Call(rt.GetType(),rt,"Bind",graph,ctx,null,true,false);Call(rt.GetType(),rt,"PrepareCoordinates",layer,ctx);Call(rt.GetType(),rt,"Render",input,target,ctx);}
                        for(int v=runtimes.Count;v<mats.Count;v++)mats[v].CopyPropertiesFromMaterial(mats[0]);
                        var images=new List<Color[]>();for(int v=0;v<mats.Count;v++){Draw(mats[v]);images.Add(Read());Draw(mats[v]);}
                        var times=mats.Select(_=>new List<double>()).ToArray();
                        for(int round=0;round<(timingOnly?31:7);round++)for(int k=0;k<mats.Count;k++){int v=(round+k)%mats.Count;times[v].Add(Draw(mats[v]));}
                        int baseline=names.IndexOf("before"),reference=names.IndexOf(timingOnly?"before":"reference");
                        for(int v=0;v<mats.Count;v++)
                        {
                            double sq=0,max=0,parity=0;int count=0;
                            for(int y=16;y<Size-16;y++)for(int x=16;x<Size-16;x++)for(int c=0;c<4;c++)
                            {int i=y*Size+x;float value=images[v][i][c];test.True(float.IsFinite(value),"Finite output");double e=value-images[reference][i][c];sq+=e*e;max=Math.Max(max,Math.Abs(e));count++;parity=Math.Max(parity,Math.Abs(value-images[baseline][i][c]));}
                            if(names[v] is "standard" or "fused" or "row")test.True(parity<.0001,"Unchanged standard result: "+parity);
                            if(names[v]=="production-high"){int h=names.IndexOf("high");float e=0;for(int i=0;i<images[v].Length;i++)for(int c=0;c<4;c++)e=Mathf.Max(e,Mathf.Abs(images[v][i][c]-images[h][i][c]));test.True(e<.0001,"High reference parity "+e);}
                            var sorted=times[v].OrderBy(x=>x).ToArray();rows.Add(new{mode,radius,name=names[v],medianMs=sorted[sorted.Length/2],minMs=sorted[0],maxMs=sorted[sorted.Length-1],samples=times[v],rmse=Math.Sqrt(sq/count),maxError=max,baselineDifference=parity});
                        }
                        if(!timingOnly && (after || radius==8))
                        {
                            int[] columns={baseline,after?0:names.IndexOf("row"),names.IndexOf(after?"production-high":"high"),reference};
                            var sheet=new Texture2D(Size*4,Size,TextureFormat.RGB24,false,true);owned.Add(sheet);for(int c=0;c<4;c++)sheet.SetPixels(c*Size,0,Size,Size,images[columns[c]]);sheet.Apply();File.WriteAllBytes(prefix+"-r"+radius+(after?".after.png":".trial.png"),sheet.EncodeToPNG());
                        }
                    }
                }
                finally{foreach(var runtime in runtimes)runtime.Dispose();}
            }
            var report=new{source,after,size=Size,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion,rows};
            string json=(string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json",true).GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report});File.WriteAllText(Dir+(timingOnly?"timing-":after?"after-":"trial-")+source+".json",json);
            return TestContext.Result("passed",test.Checks,"Bilateral study "+source+" after="+after);
        }
        finally{RenderTexture.active=active;GL.sRGBWrite=srgb;for(int i=owned.Count-1;i>=0;i--){if(owned[i] is RenderTexture rt)rt.Release();Object.DestroyImmediate(owned[i]);}Object.DestroyImmediate(doc);}
    }
}
