using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Same asynchronous native/SVM harness as ShaderGraphMathModesTests, with one small
// graph per case. Reflection below is restricted to WhimTex types, never Unity internals.
public static class ShaderGraphParallaxTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    const int Size = 32;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object target, string name) =>
        target.GetType().GetField(name, F | BindingFlags.Instance)?.GetValue(target) ??
        target.GetType().GetProperty(name, F | BindingFlags.Instance)?.GetValue(target);
    static object Static(string type, string name, params object[] args) => Call(Find(type), null, name, args);
    static string[] Ports(ShaderGraphNode node) => ((IEnumerable)Static("ShaderGraphNodes", "Inputs", node))
        .Cast<object>().Select(p => (string)Get(p, "name")).ToArray();

    sealed class GpuCase
    {
        internal ShaderGraph graph = new ShaderGraph();
        internal string label, port = "Out";
        internal ShaderGraphNode root;
        internal bool hybrid;
        internal float tolerance = .0006f;
        internal Func<Vector2, Color> expected;
        internal bool nativeOnly;
        internal ShaderGraphNode Node(ShaderGraphNodeKind kind)
        {
            var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
            graph.nodes.Add(n); return n;
        }
        internal void Connect(ShaderGraphNode from, ShaderGraphNode to, string input, string output = "Out") =>
            graph.edges.Add(new ShaderGraphEdge {outputNode=from.id,outputPort=output,inputNode=to.id,inputPort=input});
    }
    static void Set(ShaderGraphNode n, string port, Vector4 value)
    {
        var p = n.FindInput(port);
        if (p == null) {p=new ShaderGraphInput {port=port};n.inputs.Add(p);}
        p.value=value;
    }
    static void Scalar(ShaderGraphNode n, string port, float value) => Set(n,port,Vector4.one*value);
    static Vector2 Ray(Vector3 view,float amplitude,bool pom)
    {
        view.Normalize();
        return view.z<=0 ? Vector2.zero : new Vector2(view.x,view.y)*amplitude/(pom?Mathf.Max(view.z,.05f):view.z+.42f);
    }
    static Color UVColor(Vector2 uv) => new Color(uv.x,uv.y,0,1);
    static List<GpuCase> Cases(List<Texture2D> owned)
    {
        var result = new List<GpuCase>();
        foreach (bool pom in new[]{false,true})
        {
            GpuCase Make(string label, float slope, float intercept, float amplitude=.2f, Vector3? direction=null, float steps=16)
            {
                var c=new GpuCase {label=(pom?"POM/":"PM/")+label};result.Add(c);
                var n=c.Node(pom?ShaderGraphNodeKind.ParallaxOcclusionMapping:ShaderGraphNodeKind.ParallaxMapping);
                n.heightSource=ShaderGraphHeightSource.Value;n.inputs.RemoveAll(p=>p.port=="Height");
                Vector3 view=direction??new Vector3(.5f,.25f,1);Set(n,"ViewDirection",view);Scalar(n,"Amplitude",amplitude);
                if(pom) Scalar(n,"Steps",steps);
                Scalar(n,"Height",intercept);
                if(slope!=0)
                {
                    var uv=c.Node(ShaderGraphNodeKind.UV);var split=c.Node(ShaderGraphNodeKind.Split);
                    c.Connect(uv,split,"In");
                    var mul=c.Node(ShaderGraphNodeKind.Multiply);Scalar(mul,"B",slope);c.Connect(split,mul,"A","X");
                    var add=c.Node(ShaderGraphNodeKind.Add);Scalar(add,"B",intercept);c.Connect(mul,add,"A");c.Connect(add,n,"Height");
                }
                c.root=n;c.Connect(n,c.Node(ShaderGraphNodeKind.Output),"RGBA");
                var ray=Ray(view,amplitude,pom);
                c.expected=uv=>{
                    float height=pom?(intercept+slope*(uv.x-.5f*ray.x))/(1-slope*ray.x):intercept+slope*uv.x;
                    return UVColor(uv+ray*((pom?Mathf.Clamp01(height):height)-.5f));
                };
                return c;
            }
            Make("flat0",0,0); Make("flatMid",0,.5f);Make("flat1",0,1);
            Make("heightBelowRange",0,-1);Make("heightAboveRange",0,2);
            Make("ramp",.6f,.2f);Make("negativeAmplitude",.6f,.2f,-.2f);
            Make("zeroAmplitude",.6f,.2f,0);Make("backView",.6f,.2f,.2f,new Vector3(1,1,-1));
            Make("zeroView",.6f,.2f,.2f,Vector3.zero);
            if(pom)
            {
                foreach(float amplitude in new[]{0f,-.26f,.26f,8f})
                {
                    Make("adaptive/ramp/"+amplitude,.6f,.2f,amplitude,new Vector3(0,.5f,1),6);
                    var c=Make("adaptive/Shard/"+amplitude,0,.5f,amplitude,new Vector3(0,.5f,1),6);
                    var noise=c.Node(ShaderGraphNodeKind.ShardNoise);
                    c.Connect(noise,c.root,"Height","Value");c.expected=null;
                }
                Make("adaptive/diagonal",.6f,.2f,.2f,new Vector3(.8f,.6f,1),1);
                Make("adaptive/fine requested",.6f,.2f,.2f,new Vector3(.8f,.6f,1),64);
            }
            {
                var c=Make("safety/large finite direction",0,.8f,.2f,new Vector3(1e25f,.5e25f,1e25f));
                var ray=Ray(new Vector3(1,.5f,1),.2f,pom);c.expected=uv=>UVColor(uv+ray*.3f);
            }
            foreach(string port in new[]{"Height","Amplitude","ViewDirection","UV"}) foreach(float numerator in new[]{0f,1f,-1f})
            {
                var c=Make($"safety/{port}/{numerator}/0",0,.75f);
                var divide=c.Node(ShaderGraphNodeKind.Divide);Scalar(divide,"A",numerator);Scalar(divide,"B",0);
                c.Connect(divide,c.root,port);
                // Runtime NaN/Inf can come from any upstream branch, not only textures.
                c.expected=port=="UV"?uv=>UVColor(Vector2.zero+Ray(new Vector3(.5f,.25f,1),.2f,pom)*.25f):UVColor;
            }
            if(pom) foreach(float numerator in new[]{0f,1f,-1f})
            {
                var c=Make($"safety/Steps/{numerator}/0",.6f,.2f);
                var divide=c.Node(ShaderGraphNodeKind.Divide);Scalar(divide,"A",numerator);Scalar(divide,"B",0);c.Connect(divide,c.root,"Steps");
            }
            if(!pom)
            {
                var c=Make("safety/displacement overflow",0,3e38f,10);
                c.expected=UVColor;
            }
            foreach(var kind in Enum.GetValues(typeof(ShaderGraphNodeKind)).Cast<ShaderGraphNodeKind>().Where(k=>
                (bool)Static("ShaderGraphNodes","IsNoise",k) && k!=ShaderGraphNodeKind.GaborNoise))
            {
                var c=Make("source/"+kind,0,.5f);var source=c.Node(kind);
                var outputs=(string[])Static("ShaderGraphNodes","StoredOutputs",source);
                c.Connect(source,c.root,"Height",outputs[0]);c.expected=null;
            }
            foreach(var kind in new[]{ShaderGraphNodeKind.Rings,ShaderGraphNodeKind.Checkerboard,ShaderGraphNodeKind.Shape,
                ShaderGraphNodeKind.SampleGradient,ShaderGraphNodeKind.SampleCurve,ShaderGraphNodeKind.SampleTexture,ShaderGraphNodeKind.Fractal,
                ShaderGraphNodeKind.Blur,ShaderGraphNodeKind.CausticsNoise})
            {
                var c=Make("source/"+kind,0,.5f);var source=c.Node(kind);
                var outputs=(string[])Static("ShaderGraphNodes","StoredOutputs",source);
                c.Connect(source,c.root,"Height",outputs[0]);c.expected=null;
                if(kind==ShaderGraphNodeKind.Fractal||kind==ShaderGraphNodeKind.Blur)
                {
                    var sine=c.Node(ShaderGraphNodeKind.Sine);var uv=c.Node(ShaderGraphNodeKind.UV);
                    c.Connect(uv,sine,"In");c.Connect(sine,source,"In");
                }
                if(kind==ShaderGraphNodeKind.Blur) c.nativeOnly=true;
            }
            {
                var c=Make("source/Custom Function",0,.5f);var source=c.Node(ShaderGraphNodeKind.CustomFunction);
                var function=new ShaderGraphFunction {returnType="float",returnValueType=ShaderGraphValueType.Float,returnName="Result",
                    body="return 0.5 + 0.2 * sin(UV.x * 4.0);",draft="return 0.5 + 0.2 * sin(UV.x * 4.0);",
                    ports=new List<ShaderGraphFunctionPort>{new ShaderGraphFunctionPort {name="UV",type=ShaderGraphValueType.Float2,uvDefault=true}}};
                c.graph.functions.Add(function);source.functionId=function.id;source.inputs.Clear();
                c.Connect(source,c.root,"Height","Result");c.expected=null;c.nativeOnly=true;
            }
            if(pom){Make("stepsClampLow",.6f,.2f,steps:-4);Make("stepsClampHigh",.6f,.2f,steps:99);Make("fractionalSteps",.6f,.2f,steps:3.6f);}
            {
                var c=Make("nonlinear height",0,.5f);
                var uv=c.Node(ShaderGraphNodeKind.UV);var split=c.Node(ShaderGraphNodeKind.Split);c.Connect(uv,split,"In");
                var scale=c.Node(ShaderGraphNodeKind.Multiply);Scalar(scale,"B",4);c.Connect(split,scale,"A","X");
                var sine=c.Node(ShaderGraphNodeKind.Sine);c.Connect(scale,sine,"In");
                var strength=c.Node(ShaderGraphNodeKind.Multiply);Scalar(strength,"B",.2f);c.Connect(sine,strength,"A");
                var bias=c.Node(ShaderGraphNodeKind.Add);Scalar(bias,"B",.5f);c.Connect(strength,bias,"A");c.Connect(bias,c.root,"Height");
                var ray=Ray(new Vector3(.5f,.25f,1),.2f,pom);
                c.expected=uv=>{
                    float height=.5f+.2f*Mathf.Sin(uv.x*4);
                    if(pom){float low=0,high=1;for(int k=0;k<28;k++){float m=(low+high)*.5f;float surface=.5f+.2f*Mathf.Sin((uv.x+ray.x*(m-.5f))*4);if(m>surface)high=m;else low=m;}height=(low+high)*.5f;}
                    return UVColor(uv+ray*(height-.5f));
                };
            }
            {
                var c=Make("sampled derivative",0,.5f);
                var uv=c.Node(ShaderGraphNodeKind.UV);var split=c.Node(ShaderGraphNodeKind.Split);c.Connect(uv,split,"In");
                var dx=c.Node(ShaderGraphNodeKind.DDX);c.Connect(split,dx,"In","X");c.Connect(dx,c.root,"Height");
                var ray=Ray(new Vector3(.5f,.25f,1),.2f,pom);c.expected=uv=>UVColor(uv+ray*(1f/Size-.5f));
            }
            foreach(bool filtered in new[]{false,true}) foreach(int profile in new[]{0,1,2})
            {
                var c=Make($"Gabor Phasor/{profile}/Filtering={filtered}",0,.5f);
                var noise=c.Node(ShaderGraphNodeKind.GaborNoise);
                noise.noiseFiltering=filtered;noise.option=profile;
                c.Connect(noise,c.root,"Height","Phasor");
                // Procedural heights use native/SVM parity; analytic cases above
                // independently verify the ray equations and intersection search.
                c.expected=null;
            }
            foreach(var channel in new[]{ShaderGraphChannel.Red,ShaderGraphChannel.Green,ShaderGraphChannel.Blue,ShaderGraphChannel.Alpha,ShaderGraphChannel.Luminance})
            {
                float slope=channel==ShaderGraphChannel.Red?1:channel==ShaderGraphChannel.Blue?-1:channel==ShaderGraphChannel.Luminance?.2126f-.0722f:0;
                float intercept=channel==ShaderGraphChannel.Blue?1:channel==ShaderGraphChannel.Green?.4f:channel==ShaderGraphChannel.Alpha?.7f:channel==ShaderGraphChannel.Luminance?.7152f*.4f+.0722f:0;
                var c=Make("Texture/"+channel,slope,intercept);
                c.graph.edges.RemoveAll(e=>e.inputNode==c.root.id&&e.inputPort=="Height");
                c.root.inputs.RemoveAll(p=>p.port=="Height");c.root.heightSource=ShaderGraphHeightSource.Texture;c.root.channel=channel;
                var tex=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(tex);
                var colors=new Color[Size*Size];for(int i=0;i<colors.Length;i++){float x=(i%Size+.5f)/Size;colors[i]=new Color(x,.4f,1-x,.7f);}
                tex.SetPixels(colors);tex.Apply();
                c.root.inputs.Add(new ShaderGraphInput {port="Height",type=ShaderGraphValueType.Texture,
                    resource=new ShaderParameter {type=ShaderParameterType.Texture2D,textureValue=tex}});
                c.root.filter=ShaderGraphFilter.Bilinear;c.root.wrap=ShaderGraphWrap.Clamp;
            }
            {
                var c=Make("Buffer",.6f,.2f);var edge=c.graph.edges.Single(e=>e.inputNode==c.root.id&&e.inputPort=="Height");
                var source=c.graph.FindNode(edge.outputNode);c.graph.edges.Remove(edge);
                var buffer=c.Node(ShaderGraphNodeKind.Buffer);buffer.bufferSize=new Vector2Int(32,32);
                c.Connect(source,buffer,"In");c.Connect(buffer,c.root,"Height");c.hybrid=true;
            }
            {
                var c=Make("nested PM",0,.5f);var inner=c.Node(ShaderGraphNodeKind.ParallaxMapping);
                inner.heightSource=ShaderGraphHeightSource.Value;inner.inputs.RemoveAll(p=>p.port=="Height");Scalar(inner,"Height",.75f);
                Set(inner,"ViewDirection",new Vector4(.3f,0,1,0));Scalar(inner,"Amplitude",.2f);
                var split=c.Node(ShaderGraphNodeKind.Split);c.Connect(inner,split,"In");c.Connect(split,c.root,"Height","X");
                float offset=Ray(new Vector3(.3f,0,1),.2f,false).x*.25f;
                var ray=Ray(new Vector3(.5f,.25f,1),.2f,pom);
                c.expected=uv=>{float height=pom?(uv.x+offset-.5f*ray.x)/(1-ray.x):uv.x+offset;return UVColor(uv+ray*(height-.5f));};
            }
        }
        return result;
    }
    static void ModelChecks(TestContext t,List<GpuCase> cases)
    {
        foreach(var c in cases.Where(c=>c.label.EndsWith("flatMid")))
        {
            var dx=c.Node(ShaderGraphNodeKind.DDX);c.Connect(c.root,dx,"In");
            object[] build={c.graph,dx.id,"Out",null};
            t.True(!(bool)Static("ShaderGraphPreviewVM","TryCreate",build),"Hardware derivative outside parallax falls back safely");
            c.graph.edges.RemoveAll(e=>e.inputNode==dx.id);c.graph.nodes.Remove(dx);
        }
        var document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            foreach(var c in cases.Where(c=>c.root.heightSource==ShaderGraphHeightSource.Value)) AddLayer(document,c.graph);
            using var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false);
            t.Equal(document.layers.Count,loaded.Document.layers.Count,"JSON layers");
            for(int i=0;i<document.layers.Count;i++)
            {
                var before=((ShaderLayerBehaviour)document.layers[i].Behaviour).draftGraph;
                var after=((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph;
                foreach(var n in before.nodes.Where(n=>n.kind==ShaderGraphNodeKind.ParallaxMapping||n.kind==ShaderGraphNodeKind.ParallaxOcclusionMapping))
                {
                    var restored=after.FindNode(n.id);t.Equal(n.kind,restored.kind,"JSON kind");t.Equal(n.heightSource,restored.heightSource,"JSON Height Source");
                    t.True(Ports(n).SequenceEqual(Ports(restored)),"JSON input ports");
                    t.True(!Ports(n).Contains("Radius"),"Parallax has no derivative stencil radius");
                }
                t.Equal(before.edges.Count,after.edges.Count,"JSON connections");
            }
        }
        finally{Object.DestroyImmediate(document);}
    }
    static Layer AddLayer(WhimTexDocument document, ShaderGraph graph)
    {
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
        Call(typeof(Layer), layer, "AssignNewId"); document.layers.Add(layer); return layer;
    }
    static void CleanDiagnostics(TestContext t, IEnumerable diagnostics, string label)
    {
        t.True(diagnostics != null, label + " exposes compiler diagnostics");
        var messages = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            string severity = Get(diagnostic, "Severity").ToString();
            if (severity == "Warning" || severity == "Error") messages.Add(diagnostic.ToString());
        }
        t.True(messages.Count == 0, label + ": " + string.Join("\n", messages));
    }

    public static Task<object> Run() => RunCases(0);
    public static Task<object> RunGabor() => RunCases(1);
    public static Task<object> RunSafety() => RunCases(2);
    public static Task<object> RunSourcesPM() => RunCases(3);
    public static Task<object> RunSourcesPOM() => RunCases(4);
    public static Task<object> RunAdaptive() => RunCases(5);
    public static object RunIntegration()
    {
        var t = new TestContext();
        var c = new GpuCase(); var height = c.Node(ShaderGraphNodeKind.ShardNoise);
        var pom = c.Node(ShaderGraphNodeKind.ParallaxOcclusionMapping); pom.heightSource = ShaderGraphHeightSource.Value;
        pom.inputs.RemoveAll(p => p.port == "Height"); Set(pom,"ViewDirection",new Vector4(0,.5f,1,0));
        Scalar(pom,"Amplitude",-.26f); Scalar(pom,"Steps",6); c.Connect(height,pom,"Height","Value");
        var noise=c.Node(ShaderGraphNodeKind.ShardNoise); c.Connect(pom,noise,"Position");
        c.Connect(noise,c.Node(ShaderGraphNodeKind.Output),"RGBA","Value");
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags=HideFlags.HideAndDontSave; doc.width=doc.height=64;
        Layer layer=AddLayer(doc,c.graph);
        var pixels=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
        var sentinel=new RenderTexture(8,8,0){hideFlags=HideFlags.HideAndDontSave}; sentinel.Create();
        Texture2D exported=null; var old=RenderTexture.active; bool srgb=GL.sRGBWrite;
        try
        {
            Call(typeof(WhimTexDocument),doc,"NormalizeModel");
            t.True((bool)Call(typeof(ShaderLayerBehaviour),layer.Behaviour,"CompileGraph",doc),"Integration graph compiles");
            Color[] Canvas()
            {
                RenderTexture canvas=null;
                try
                {
                    RenderTexture.active=sentinel; GL.sRGBWrite=true;
                    canvas=(RenderTexture)Call(typeof(WhimTexDocument),doc,"RenderCanvas",64);
                    t.True(RenderTexture.active==sentinel&&GL.sRGBWrite,"Composite restores render state");
                    RenderTexture.active=canvas; pixels.ReadPixels(new Rect(0,0,64,64),0,0); pixels.Apply(); return pixels.GetPixels();
                }
                finally { if(canvas!=null)RenderTexture.ReleaseTemporary(canvas); RenderTexture.active=old; GL.sRGBWrite=srgb; }
            }
            void Equal(Color[] a,Color[] b,string label)
            {
                t.Equal(a.Length,b.Length,label+" length");
                for(int i=0;i<a.Length;i++) for(int k=0;k<4;k++) t.Near(a[i][k],b[i][k],.003,label);
            }
            var reference=Canvas();
            t.True(reference.Max(p=>p.r)-reference.Min(p=>p.r)>.2f,"Nonuniform POM output");
            var thumb=(Texture2D)Call(typeof(WhimTexDocument),doc,"GetLayerThumbnail",layer,64,false);
            t.True(thumb!=null&&thumb.GetPixels().All(p=>float.IsFinite(p.r))&&thumb.GetPixels().Max(p=>p.r)-thumb.GetPixels().Min(p=>p.r)>.2f,"POM thumbnail");
            var group=new Layer(new GroupLayerBehaviour()){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
            group.children.Add(layer); doc.layers.Clear();doc.layers.Add(group);Call(typeof(WhimTexDocument),doc,"NormalizeModel");
            foreach(var mode in new[]{GroupCompositing.PassThrough,GroupCompositing.Isolated})
            {group.compositing=mode;Equal(reference,Canvas(),"Group "+mode);}
            exported=(Texture2D)Call(typeof(WhimTexDocument),doc,"RasterizeLayer",group,true);Equal(reference,exported.GetPixels(),"Raster export");
            var reader=new ShaderLayerBehaviour{mode=ShaderProgramMode.Code,draftCode=ShaderLayerBehaviour.PassthroughCode,inputMode=ShaderLayerInputMode.Layer,inputLayerId=group.Id};
            var target=new Layer(reader){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
            doc.layers.Insert(0,target);group.enabled=false;Call(typeof(WhimTexDocument),doc,"NormalizeModel");
            t.True((bool)Call(typeof(ShaderLayerBehaviour),reader,"CompileCode",doc),"Layer input compiles");
            Equal(reference,Canvas(),"Hidden group as Layer input");
            target.enabled=false;group.enabled=true;group.clippingMask=true;
            doc.layers.Add(new Layer(new ColorFillLayerBehaviour{color=new Color(1,1,1,.4f)}));Call(typeof(WhimTexDocument),doc,"NormalizeModel");
            for(int i=0;i<reference.Length;i++)reference[i].a=.4f;
            Equal(reference,Canvas(),"Clipping");
            return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message="Adaptive POM composite, both group modes, hidden Layer input, clipping, thumbnail and raster export; render state restored."};
        }
        finally
        {
            RenderTexture.active=old;GL.sRGBWrite=srgb;
            if(exported!=null)Object.DestroyImmediate(exported);Object.DestroyImmediate(pixels);sentinel.Release();Object.DestroyImmediate(sentinel);Object.DestroyImmediate(doc);
        }
    }
    public static Task<object> RunSourceBatch(bool occlusion,int batch)
    {
        if(batch<0||batch>=5) throw new ArgumentOutOfRangeException(nameof(batch));
        return RunCases(occlusion?4:3,batch);
    }
    static async Task<object> RunCases(int suite,int batch=-1)
    {
        var t = new TestContext();
        var owned = new List<Texture2D>();
        var cases = Cases(owned);
        cases.RemoveAll(c=>suite switch {
            0=>c.label.Contains("safety/")||c.label.Contains("source/")||c.label.Contains("adaptive/"),
            1=>!c.label.Contains("Gabor Phasor"), 2=>!c.label.Contains("safety/"),
            3=>!c.label.StartsWith("PM/source/"), 4=>!c.label.StartsWith("POM/source/"),
            5=>!c.label.Contains("adaptive/"), _=>true});
        if(batch>=0) cases=cases.Skip(batch*5).Take(5).ToList();
        ModelChecks(t, cases);
        int budgetSeconds = suite>=3 && batch<0 ? 150 : 50;
        var timer = Stopwatch.StartNew();
        var previousActive = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        WhimTexDocument document = null; RenderTexture target = null; Texture2D pixels = null;
        float maxError = 0, maxDifference = 0;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = Size;
            Layer layer = AddLayer(document, cases[0].graph);
            var owner = (ShaderLayerBehaviour)layer.Behaviour;
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
            pixels = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            t.True(target.Create(), "Float target allocation");
            var context = Activator.CreateInstance(Find("LayerRenderContext"), new object[] { document, null, Size, Size, 1f, false, false, false });
            var requestType = Find("ShaderProgramRuntime+CompilationRequest"); var vmType = Find("ShaderGraphPreviewVM");
            async Task Until(Func<bool> condition, string label)
            {
                while (!condition())
                {
                    if (timer.Elapsed.TotalSeconds > budgetSeconds) throw new TimeoutException("Parallax suite budget: " + label);
                    await Task.Delay(25);
                }
            }
            Color[] Read()
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); pixels.Apply(); return pixels.GetPixels(); }
                finally { RenderTexture.active = active; }
            }
            foreach (var c in cases)
            {
                if (timer.Elapsed.TotalSeconds > budgetSeconds) throw new TimeoutException("Parallax suite budget before " + c.label);
                owner.draftGraph = c.graph;
                using var request = (IDisposable)Activator.CreateInstance(requestType, BindingFlags.Instance | F, null,
                    new object[] { c.graph, c.root.id, c.port, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                object[] completed = { null };
                await Until(() => (bool)Call(requestType, request, "TryComplete", completed), c.label + " HLSL");
                using var runtime = (IDisposable)completed[0]; var rt = runtime.GetType();
                CleanDiagnostics(t, (IEnumerable)Get(runtime, "DiagnosticMessages"), c.label + " HLSL");
                Call(rt, runtime, "Bind", c.graph, context, null, true, false);
                Call(rt, runtime, "PrepareCoordinates", layer, context);
                GL.sRGBWrite = false; Call(rt, runtime, "Render", Texture2D.blackTexture, target, context);
                Color[] native = Read();
                if(c.nativeOnly)
                {
                    object[] build={c.graph,c.root.id,c.port,null};
                    t.True(!(bool)Call(vmType,null,"TryCreate",build),c.label+" retains native fallback for unsupported replay");
                    foreach(var color in native) for(int channel=0;channel<4;channel++)
                        t.True(float.IsFinite(color[channel]),c.label+" finite fallback pixels");
                    continue;
                }
                IDisposable svm;
                if (c.hybrid)
                    svm = (IDisposable)Call(vmType, null, "BeginHybrid", c.graph, c.root.id, c.port, false, false, false);
                else
                {
                    object[] build = { c.graph, c.root.id, c.port, null };
                    t.True((bool)Call(vmType, null, "TryCreate", build), c.label + " pure SVM build"); svm = (IDisposable)build[3];
                }
                using (svm)
                {
                    t.True(svm != null, c.label + " preview VM");
                    Call(vmType, svm, "ConfigureHybrid", document, layer, false, true);
                    await Until(() => {
                        bool ready = (bool)Call(vmType, svm, "UpdatePreparation");
                        if ((bool)Get(svm, "Failed"))
                        {
                            var reason = vmType.GetProperty("LastFallbackReason", F | BindingFlags.Static)?.GetValue(null);
                            throw new InvalidOperationException(c.label + " SVM failed: " + reason);
                        }
                        return ready;
                    }, c.label + " SVM preparation");
                    t.True((bool)Call(vmType, svm, "RenderBoundaries", target, context), c.label + " SVM render");
                    var material = (Material)Get(svm, "material");
                    t.True(material != null, c.label + " SVM material");
                    CleanDiagnostics(t, (IEnumerable)Static("ShaderDiagnostics", "ReadCompiler", material.shader), c.label + " SVM");
                    if (c.hybrid) CleanDiagnostics(t, (IEnumerable)Get(Get(svm, "hybridRuntime"), "DiagnosticMessages"), c.label + " hybrid producer");
                    Color[] vm = Read();
                    for (int y = 4; y < Size - 4; y++) for (int x = 4; x < Size - 4; x++)
                    {
                        int sample = y * Size + x;
                        var expected = c.expected?.Invoke(new Vector2((x + .5f) / Size, (y + .5f) / Size)) ?? native[sample];
                        for (int channel = 0; channel < 4; channel++)
                        {
                            float error = Mathf.Max(Mathf.Abs(native[sample][channel] - expected[channel]), Mathf.Abs(vm[sample][channel] - expected[channel]));
                            float difference = Mathf.Abs(native[sample][channel] - vm[sample][channel]);
                            t.True(float.IsFinite(error) && error <= c.tolerance,
                                $"{c.label}[{x},{y},{channel}]: expected {expected[channel]:R}, HLSL {native[sample][channel]:R}, SVM {vm[sample][channel]:R}");
                            t.True(float.IsFinite(difference) && difference <= c.tolerance, c.label + " HLSL/SVM difference " + difference);
                            maxError = Mathf.Max(maxError, error); maxDifference = Mathf.Max(maxDifference, difference);
                        }
                    }

                }
            }
        }
        finally
        {
            RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (document != null) Object.DestroyImmediate(document);
            foreach (var texture in owned) Object.DestroyImmediate(texture);
        }
        string coverage = suite==1 ? "Gabor Phasor profiles with and without Filtering in both parallax nodes" :
            suite==2 ? "source-independent NaN/Inf and overflow guards" : suite==5 ? "adaptive UV strides, negative/zero/large rays, refinement frame state and Shard heights" : suite>=3 ? "noise, resource, nested and custom Height branches" :
            "independent ray intersections, Texture/Value, explicit Buffer, nested sampling, Gabor Phasor profiles";
        return new TestResult { status = "passed", checks = t.Checks, failures = Array.Empty<string>(),
            message = $"{cases.Count} GPU cases; {coverage}, clean HLSL/SVM diagnostics. Max error {maxError:R}, max difference {maxDifference:R}." };

    }

}
