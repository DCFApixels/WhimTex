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

// Owned transient graphs. Baseline capture must precede changes to the path-blur compiler.
// Timing includes CPU submission and synchronized GPU completion, not GPU timestamps.
public static class ShaderGraphPathBlurBenchmark
{
    const int Size = 256;
    const string Dir = "Temp/WhimTex/PathBlurStudy/";
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type t, object o, string name, params object[] args) => t.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o,args);
    static object Get(object o,string name) => o.GetType().GetProperty(name,F)?.GetValue(o) ?? o.GetType().GetField(name,F)?.GetValue(o);
    static void Set(ShaderGraphNode n,string port,float value) => n.FindInput(port).value=Vector4.one*value;
    static string Hash(string text) { using var sha=SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-",""); }
    static ShaderGraph Make(int source,int mode,out ShaderGraphNode root)
    {
        var g=new ShaderGraph();
        ShaderGraphNode Node(ShaderGraphNodeKind kind) { var n=(ShaderGraphNode)Call(T("ShaderGraphNodes"),null,"Create",kind,Vector2.zero);g.nodes.Add(n);return n; }
        void Edge(ShaderGraphNode a,ShaderGraphNode b,string input,string output="Out") => g.edges.Add(new ShaderGraphEdge{outputNode=a.id,outputPort=output,inputNode=b.id,inputPort=input});
        ShaderGraphNode input;
        string port="Out";
        if(source<2) { input=Node(ShaderGraphNodeKind.SampleInput); }
        else
        {
            var noise=Node(ShaderGraphNodeKind.GaborNoise);Set(noise,"Scale",24);noise.noiseFiltering=false;
            input=Node(ShaderGraphNodeKind.Fractal);input.fractal=ShaderGraphFractal.FBm;Set(input,"Octaves",3);Set(input,"Lacunarity",2);Set(input,"Gain",.5f);
            Edge(noise,input,"In","Value");
        }
        if(source%2==1) { var b=Node(ShaderGraphNodeKind.Buffer);b.bufferResolution=ShaderGraphBufferResolution.Canvas;b.wrap=ShaderGraphWrap.Clamp;Edge(input,b,"In",port);input=b;port="Out"; }
        root=Node(mode==0?ShaderGraphNodeKind.DirectionalBlur:ShaderGraphNodeKind.RadialBlur);
        root.option=mode==2?1:0;
        if(mode==0) { Set(root,"Radius",32);Set(root,"Angle",27); }
        else if(mode==1) Set(root,"Angle",20);
        else Set(root,"Strength",.3f);
        Edge(input,root,"In",port);Edge(root,Node(ShaderGraphNodeKind.Output),"RGBA");return g;
    }
    static string Extent(string source,int mode,int density)
    {
        const string old="int extent=(int)clamp(ceil((samples-1)*0.5),1,64);";
        if(!source.Contains(old))throw new Exception("Frozen baseline sampling loop missing");
        string length=mode==0?"abs(amount)":mode==1?"length((uv-center)*_CanvasSize.xy)*abs(radians(amount))":"length((uv-center)*_CanvasSize.xy)*abs(amount)";
        return source.Replace(old,"int extent="+density+"*(int)max(1,ceil("+length+"));");
    }
    public static async Task<object> Run(bool capture,int source)
    {
        var test=new TestContext();Directory.CreateDirectory(Dir);
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=Size;
        var owner=new ShaderLayerBehaviour {mode=ShaderProgramMode.Graph};Layer layer=owner;Call(typeof(Layer),layer,"AssignNewId");doc.layers.Add(layer);
        var context=Activator.CreateInstance(T("LayerRenderContext"),new object[]{doc,null,Size,Size,1f,false,false,false});
        var owned=new List<Object>();var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        RenderTexture RT(int size) {var rt=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};rt.Create();owned.Add(rt);return rt;}
        var target=RT(Size);var fence=RT(1);
        var read=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);owned.Add(read);
        var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);owned.Add(pixel);
        var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};owned.Add(texture);
        var data=new Color[Size*Size];
        for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)data[y*Size+x]=new Color(x%19==0?2:0,((x/3+y/5)%2)*1.5f-.25f,Mathf.Exp(-Mathf.Pow((x-128)/2f,2)),y/(float)Size-.2f);
        texture.SetPixels(data);texture.Apply();owner.inputMode=source<2?ShaderLayerInputMode.Texture:ShaderLayerInputMode.None;owner.inputTexture=source<2?texture:null;
        Texture input=source<2?texture:Texture2D.blackTexture;
        var rows=new List<object>();
        async Task Until(Func<bool> condition,string label) {var timer=Stopwatch.StartNew();while(!condition()){if(timer.Elapsed.TotalSeconds>90)throw new TimeoutException(label);await Task.Delay(25);}}
        Color[] Read(){RenderTexture.active=target;read.ReadPixels(new Rect(0,0,Size,Size),0,0);read.Apply();return read.GetPixels();}
        void Sync(){Graphics.Blit(target,fence);RenderTexture.active=fence;pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply();}
        double Draw(Material m,int repeats) {var timer=Stopwatch.StartNew();for(int k=0;k<repeats;k++)Graphics.Blit(input,target,m,0);Sync();return timer.Elapsed.TotalMilliseconds/repeats;}
        void Clean(IEnumerable messages,string label){foreach(var m in messages)test.True((Get(m,"Severity")??Get(m,"severity"))?.ToString()=="Info",label+": "+m);}
        try
        {
            GL.sRGBWrite=false;
            for(int mode=0;mode<3;mode++)
            {
                string id=source+"-"+mode,prefix=Dir+id;
                ShaderGraphNode root;ShaderGraph graph;
                if(capture) {test.True(!File.Exists(prefix+".before.shader"),"Refuse overwriting baseline "+id);graph=Make(source,mode,out root);File.WriteAllText(prefix+".graph.json",JsonUtility.ToJson(graph));}
                else {graph=JsonUtility.FromJson<ShaderGraph>(File.ReadAllText(prefix+".graph.json"));root=graph.nodes.Single(n=>n.kind is ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur);root.inputs.RemoveAll(p=>p.port=="Samples");}
                owner.draftGraph=graph;
                var runtimes=new List<IDisposable>();var materials=new List<Material>();var names=new List<string>();var images=new List<Color[]>();
                try
                {
                    for(int quality=0;quality<(capture?1:2);quality++)
                    {
                        if(!capture){var field=typeof(ShaderGraphNode).GetField("filterQuality");test.True(field!=null,"Quality field present");field.SetValue(root,Enum.ToObject(field.FieldType,quality));}
                        var requestType=T("ShaderProgramRuntime+CompilationRequest");
                        using var request=(IDisposable)Activator.CreateInstance(requestType,F,null,new object[]{graph,root.id,"Out",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",true},null);
                        object[] completed={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",completed),"Runtime "+id);
                        var runtime=(IDisposable)completed[0];runtimes.Add(runtime);Clean((IEnumerable)Get(runtime,"DiagnosticMessages"),id);
                        Call(runtime.GetType(),runtime,"Bind",graph,context,null,true,false);Call(runtime.GetType(),runtime,"PrepareCoordinates",layer,context);
                        var first=Stopwatch.StartNew();Call(runtime.GetType(),runtime,"Render",input,target,context);Sync();first.Stop();
                        var mat=(Material)Get(runtime,"Material");materials.Add(mat);names.Add(capture?"before17":quality==0?"standard":"high");images.Add(Read());
                        string code=(string)Get(runtime,"ShaderSource");
                        if(capture){File.WriteAllText(prefix+".before.shader",code);File.WriteAllText(prefix+".sha256",Hash(code));}
                        rows.Add(new {id,name=names.Last(),initialRenderMs=first.Elapsed.TotalMilliseconds});
                    }
                    if(!capture)
                    {
                        string frozen=File.ReadAllText(prefix+".before.shader");test.Equal(File.ReadAllText(prefix+".sha256"),Hash(frozen),"Authenticated baseline");
                        var variants=new List<(string name,string code)>{("before17",frozen.Replace("int extent=(int)clamp(ceil((samples-1)*0.5),1,64);","int extent=8;")),("reference",Extent(frozen,mode,4))};
                        if(mode==1){variants.Add(("trig-standard",Extent(frozen,mode,1)));variants.Add(("trig-high",Extent(frozen,mode,2)));}
                        foreach(var v in variants)
                        {
                            var shader=ShaderUtil.CreateShaderAsset(v.code);shader.hideFlags=HideFlags.HideAndDontSave;owned.Add(shader);
                            var m=new Material(shader){hideFlags=HideFlags.HideAndDontSave};owned.Add(m);m.CopyPropertiesFromMaterial(materials[0]);
                            bool async=ShaderUtil.allowAsyncCompilation;
                            try{ShaderUtil.allowAsyncCompilation=true;ShaderUtil.CompilePass(m,0,false);}finally{ShaderUtil.allowAsyncCompilation=async;}
                            await Until(()=>ShaderUtil.IsPassCompiled(m,0),"Variant "+id+"/"+v.name);Clean(ShaderUtil.GetShaderMessages(shader),v.name);
                            Graphics.Blit(input,target,m,0);materials.Add(m);names.Add(v.name);images.Add(Read());
                        }
                    }
                    for(int i=0;i<materials.Count;i++)for(int w=0;w<2;w++)Draw(materials[i],1);
                    var times=materials.Select(_=>new List<double>()).ToArray();
                    for(int round=0;round<9;round++)for(int order=0;order<materials.Count;order++){int v=(order+round)%materials.Count;times[v].Add(Draw(materials[v],2));}
                    int reference=names.IndexOf("reference");
                    if(!capture)
                    {
                        var sheet=new Texture2D(Size*4,Size,TextureFormat.RGB24,false,true);owned.Add(sheet);
                        int[] columns={names.IndexOf("before17"),0,1,reference};
                        for(int c=0;c<columns.Length;c++)sheet.SetPixels(c*Size,0,Size,Size,images[columns[c]]);
                        sheet.Apply();File.WriteAllBytes(prefix+".comparison.png",sheet.EncodeToPNG());
                    }
                    for(int v=0;v<runtimes.Count;v++)
                    {
                        var refresh=new List<double>();
                        for(int k=0;k<5;k++)
                        {
                            var runtime=runtimes[v];
                            if(Get(runtime,"bufferImages") is IEnumerable buffers)foreach(var b in buffers)
                            {var shared=Get(b,"shared");if(shared!=null)shared.GetType().GetField("valid",F).SetValue(shared,false);}
                            var timer=Stopwatch.StartNew();Call(runtime.GetType(),runtime,"Render",input,target,context);Sync();refresh.Add(timer.Elapsed.TotalMilliseconds);
                        }
                        rows.Add(new{id,name=names[v]+"-refresh",medianMs=refresh.OrderBy(x=>x).ElementAt(2),samples=refresh});
                    }
                    for(int v=0;v<materials.Count;v++)
                    {
                        double squared=0,max=0;int count=0;
                        for(int y=32;y<Size-32;y++)for(int x=32;x<Size-32;x++)for(int c=0;c<4;c++)
                        {
                            float value=images[v][y*Size+x][c];test.True(float.IsFinite(value),"Finite output "+id+names[v]);
                            if(reference>=0){double d=value-images[reference][y*Size+x][c];squared+=d*d;max=Math.Max(max,Math.Abs(d));count++;}
                        }
                        var sorted=times[v].OrderBy(x=>x).ToArray();rows.Add(new{id,name=names[v],medianMs=sorted[4],p10Ms=sorted[0],p90Ms=sorted[8],samples=times[v],rmse=count>0?Math.Sqrt(squared/count):0,maxError=max});
                        var png=new Texture2D(Size,Size,TextureFormat.RGB24,false,true);owned.Add(png);png.SetPixels(images[v]);png.Apply();File.WriteAllBytes(prefix+"."+names[v]+".png",png.EncodeToPNG());
                        if(names[v].StartsWith("trig-")) {int q=names[v]=="trig-high"?1:0;float difference=0;for(int i=0;i<images[v].Length;i++)for(int c=0;c<4;c++)difference=Mathf.Max(difference,Mathf.Abs(images[v][i][c]-images[q][i][c]));test.True(difference<.002f,"Spin recurrence parity: "+difference);rows.Add(new{id,name="spin-parity-"+q,maxDifference=difference});}
                    }
                }
                finally {foreach(var runtime in runtimes)runtime.Dispose();}
            }
            var report=new {status="passed",checks=test.Checks,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),unity=Application.unityVersion,source,size=Size,rows};
            string json=(string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json",true).GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new object[]{report});File.WriteAllText(Dir+(capture?"before-":"after-")+source+".json",json);
            return TestContext.Result("passed",test.Checks,"Path blur "+(capture?"baseline":"comparison")+" source "+source+"; report "+Dir);
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;for(int i=owned.Count-1;i>=0;i--){if(owned[i] is RenderTexture rt)rt.Release();Object.DestroyImmediate(owned[i]);}Object.DestroyImmediate(doc);}
    }
}
