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

// Pixel comparisons plus independent flat/ramp/polynomial and alpha expectations.
// Reflection is restricted to WhimTex types, never Unity internals.
public static class ShaderGraphImageFiltersTests
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
        internal bool hybrid = false;
        internal float tolerance = .0006f;
        internal Func<Vector2, Color> expected;
        internal bool nativeOnly = false;
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
        if (p == null) {
            var definition=((IEnumerable)Static("ShaderGraphNodes","Inputs",n)).Cast<object>().Single(d=>(string)Get(d,"name")==port);
            p=new ShaderGraphInput {port=port,type=(ShaderGraphValueType?)Get(definition,"type") ?? ShaderGraphValueType.Float};n.inputs.Add(p);
        }
        p.value=value;
    }
    static void Scalar(ShaderGraphNode n, string port, float value) => Set(n,port,Vector4.one*value);

    static Color Gray(float value) => new Color(value,value,value,1);
    static ShaderGraphNode X(GpuCase c)
    {
        var uv=c.Node(ShaderGraphNodeKind.UV); var split=c.Node(ShaderGraphNodeKind.Split); c.Connect(uv,split,"In");
        var add=c.Node(ShaderGraphNodeKind.Add); c.Connect(split,add,"A","X"); return add;
    }
    static ShaderGraphNode Square(GpuCase c)
    { var x=X(c);var n=c.Node(ShaderGraphNodeKind.Multiply);c.Connect(x,n,"A");c.Connect(x,n,"B");return n; }
    static List<GpuCase> Cases(int batch,List<Texture2D> owned)
    {
        var cases=new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind,string name,string port="Out")
        {
            var c=new GpuCase{label=kind+"/"+name,port=port};c.root=c.Node(kind);
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA",port);cases.Add(c);return c;
        }
        if(batch==0)
        {
            foreach(var port in new[]{"Convex","Concave","Curvature"})
            {
                var flat=Make(ShaderGraphNodeKind.HeightCurvature,"flat",port);Scalar(flat.root,"Height",.7f);flat.expected=_=>Gray(0);
                var c=Make(ShaderGraphNodeKind.HeightCurvature,"quadratic",port);c.Connect(Square(c),c.root,"Height");
                float v=port=="Convex"?0:port=="Concave"?.015625f:-.015625f;c.expected=_=>Gray(v);
            }
            var zero=Make(ShaderGraphNodeKind.HeightCurvature,"radius0","Curvature");zero.Connect(Square(zero),zero.root,"Height");Scalar(zero.root,"Radius",0);zero.expected=_=>Gray(0);
            var noise=Make(ShaderGraphNodeKind.HeightCurvature,"Perlin","Convex");noise.Connect(noise.Node(ShaderGraphNodeKind.PerlinNoise),noise.root,"Height","Value");
        }
        if(batch==1)
        {
            var flat=Make(ShaderGraphNodeKind.HeightToAO,"flat");Scalar(flat.root,"Height",.7f);flat.expected=_=>Gray(1);
            var ramp=Make(ShaderGraphNodeKind.HeightToAO,"ramp");ramp.Connect(X(ramp),ramp.root,"Height");ramp.expected=_=>Gray(1);
            var zero=Make(ShaderGraphNodeKind.HeightToAO,"radius0");zero.Connect(Square(zero),zero.root,"Height");Scalar(zero.root,"Radius",0);zero.expected=_=>Gray(1);
            var bowl=Make(ShaderGraphNodeKind.HeightToAO,"bowl");bowl.Connect(Square(bowl),bowl.root,"Height");
            bowl.expected=uv=> {
                float total=0;
                for(int i=0;i<8;i++) {
                    float dx=Mathf.Cos(2*Mathf.PI*(i+.5f)/8);float tangent=uv.x*dx;
                    float horizon=tangent;
                    for(int k=1;k<=4;k++) {float d=4*k;float x=uv.x+dx*d/Size;horizon=Mathf.Max(horizon,((x*x-uv.x*uv.x)*16-.05f)/d);}
                    total+=(horizon/Mathf.Sqrt(1+horizon*horizon)-tangent/Mathf.Sqrt(1+tangent*tangent))/8;
                } return Gray(1-total);
            };
            foreach(float strength in new[]{0f,2f}) {
                var c=Make(ShaderGraphNodeKind.HeightToAO,"noise/"+strength);c.Connect(c.Node(ShaderGraphNodeKind.PerlinNoise),c.root,"Height","Value");Scalar(c.root,"Strength",strength);
                if(strength==0)c.expected=_=>Gray(1);
            }
            var counts=Make(ShaderGraphNodeKind.HeightToAO,"connected counts");counts.Connect(Square(counts),counts.root,"Height");
            var number=counts.Node(ShaderGraphNodeKind.Value);number.literal.floatValue=2;counts.Connect(number,counts.root,"Directions");counts.Connect(number,counts.root,"Steps");
        }
        if(batch==2)
        {
            foreach(var d in new[]{new Vector4(.5f,.5f,1,0),Vector4.zero,new Vector4(0,0,1,0),new Vector4(0,0,-1,0)}) {
                var c=Make(ShaderGraphNodeKind.HeightShadow,"flat/"+d);Set(c.root,"LightDirection",d);c.expected=_=>Gray(d.z<0?0:1);
            }
            var wall=Make(ShaderGraphNodeKind.HeightShadow,"wall");var step=wall.Node(ShaderGraphNodeKind.Step);wall.Connect(X(wall),step,"In");Scalar(step,"Edge",.5f);wall.Connect(step,wall.root,"Height");
            Set(wall.root,"LightDirection",new Vector4(1,0,.1f,0));Scalar(wall.root,"Distance",32);Scalar(wall.root,"Steps",64);Scalar(wall.root,"Softness",0);wall.expected=uv=>Gray(uv.x<.5f?0:1);
            var noise=Make(ShaderGraphNodeKind.HeightShadow,"noise");noise.Connect(noise.Node(ShaderGraphNodeKind.PerlinNoise),noise.root,"Height","Value");
        }
        if(batch==3)
        {
            foreach(int option in new[]{0,1,2}) {
                var c=Make(ShaderGraphNodeKind.Supersample,"quadratic/"+option);c.root.option=option;c.Connect(Square(c),c.root,"In");int n=option+2;
                float variance=(n*n-1f)/(12*n*n*Size*Size);c.expected=uv=>Gray(uv.x*uv.x+variance);
            }
            foreach(var type in new[]{ShaderGraphValueType.Float,ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4,ShaderGraphValueType.Color}) {
                var c=Make(ShaderGraphNodeKind.Supersample,"constant/"+type);c.root.FindInput("In").type=type;Set(c.root,"In",new Vector4(.2f,.4f,.6f,.3f));
                c.expected=_=> type==ShaderGraphValueType.Float?Gray(.2f):new Color(.2f,.4f,type==ShaderGraphValueType.Float2?0:.6f,type is ShaderGraphValueType.Float4 or ShaderGraphValueType.Color?.3f:1);
            }
            var alpha=Make(ShaderGraphNodeKind.Supersample,"alpha weighted");var set=alpha.Node(ShaderGraphNodeKind.SetAlpha);alpha.Connect(X(alpha),set,"Color");alpha.Connect(X(alpha),set,"Alpha");alpha.Connect(set,alpha.root,"In");
            alpha.expected=uv=>new Color(uv.x+1f/(16*Size*Size*uv.x),uv.x+1f/(16*Size*Size*uv.x),uv.x+1f/(16*Size*Size*uv.x),uv.x);
            var fractal=Make(ShaderGraphNodeKind.Supersample,"nested Fractal");var fbm=fractal.Node(ShaderGraphNodeKind.Fractal);Scalar(fbm,"Octaves",2);
            var field=fractal.Node(ShaderGraphNodeKind.PerlinNoise);field.noiseFiltering=true;fractal.Connect(field,fbm,"In","Value");fractal.Connect(fbm,fractal.root,"In");
            var nested=Make(ShaderGraphNodeKind.Supersample,"nested derivative");var dx=nested.Node(ShaderGraphNodeKind.DDX);nested.Connect(Square(nested),dx,"In");nested.Connect(dx,nested.root,"In");nested.expected=uv=>Gray(2*uv.x/Size);
        }
        if(batch==4)
        {
            var c=Make(ShaderGraphNodeKind.MakeTileable,"quadratic");c.Connect(Square(c),c.root,"In");
            float Tile(float x) {x-=Mathf.Floor(x);float d=2*Mathf.Min(x,1-x);float f=d*d*d*(d*(d*6-15)+10);float shifted=Mathf.Repeat(x+.5f,1);return Mathf.Lerp(shifted*shifted,x*x,f);}
            c.expected=uv=>Gray(Tile(uv.x));
            var shifted=Make(ShaderGraphNodeKind.MakeTileable,"shifted outside unit");shifted.Connect(Square(shifted),shifted.root,"In");var add=shifted.Node(ShaderGraphNodeKind.Add);shifted.Connect(shifted.Node(ShaderGraphNodeKind.UV),add,"A");Scalar(add,"B",-2);shifted.Connect(add,shifted.root,"UV");shifted.expected=uv=>Gray(Tile(uv.x));
            var seam=Make(ShaderGraphNodeKind.MakeTileable,"boundary");seam.Connect(Square(seam),seam.root,"In");Set(seam.root,"UV",new Vector4(1,1,0,0));seam.expected=_=>Gray(.25f);
            var color=Make(ShaderGraphNodeKind.MakeTileable,"transparent");color.root.FindInput("In").type=ShaderGraphValueType.Color;Set(color.root,"In",new Vector4(1,0,0,0));color.expected=_=>Color.clear;
            foreach(var wrap in new[]{ShaderGraphWrap.Clamp,ShaderGraphWrap.Repeat}) {
                var sample=Make(ShaderGraphNodeKind.MakeTileable,"texture/"+wrap);var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(texture);
                var colors=new Color[Size*Size];for(int i=0;i<colors.Length;i++){float x=(i%Size+.5f)/Size;colors[i]=Gray(x*x);}texture.SetPixels(colors);texture.Apply();
                var input=sample.Node(ShaderGraphNodeKind.SampleTexture);input.wrap=wrap;input.filter=ShaderGraphFilter.Bilinear;
                input.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});
                sample.Connect(input,sample.root,"In","R");sample.expected=uv=>Gray(Tile(uv.x));
            }
            foreach(bool filtering in new[]{false,true}) {
                var noise=Make(ShaderGraphNodeKind.MakeTileable,"Gabor/"+filtering);var gabor=noise.Node(ShaderGraphNodeKind.GaborNoise);gabor.noiseFiltering=filtering;
                noise.Connect(gabor,noise.root,"In","Value");
            }
        }
        if(batch==5)
        {
            foreach(float count in new[]{1,2,3,8,64}) {
                var c=Make(ShaderGraphNodeKind.PaletteQuantize,"count/"+count);Scalar(c.root,"Colors",count);Set(c.root,"Color",new Vector4(.31f,.31f,.31f,.35f));
                var gradient=new WhimTexGradient();gradient.SetKeys(new[]{new GradientColorKey(Color.black,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                c.root.inputs.Add(new ShaderGraphInput{port="Palette",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter{type=ShaderParameterType.Gradient,gradientValue=gradient}});
                // 1 and 2 candidates have endpoint colors independent of interpolation space.
                if(count<=2)c.expected=_=>new Color(0,0,0,.35f);
            }
        }
        if(batch==6)
        {
            foreach(float strength in new[]{0f,16f,-16f}) {
                var c=Make(ShaderGraphNodeKind.FlowWarp,"constant/"+strength);Set(c.root,"Flow",new Vector4(.2f,-.1f,0,0));Scalar(c.root,"Strength",strength);
                c.expected=uv=>new Color(uv.x+.2f*strength/Size,uv.y-.1f*strength/Size,0,1);
            }
            foreach(int steps in new[]{1,8,32}) {
                var c=Make(ShaderGraphNodeKind.FlowWarp,"linear field/"+steps);c.Connect(c.Node(ShaderGraphNodeKind.UV),c.root,"Flow");Scalar(c.root,"Strength",4);Scalar(c.root,"Steps",steps);
                float h=4f/(Size*steps),factor=Mathf.Pow(1+h+h*h/2,steps);c.expected=uv=>new Color(uv.x*factor,uv.y*factor,0,1);
            }
            var zero=Make(ShaderGraphNodeKind.FlowWarp,"zero field");zero.expected=uv=>new Color(uv.x,uv.y,0,1);
        }
        if(batch==7 || batch==8)
        {
            var kind=batch==7?ShaderGraphNodeKind.Median:ShaderGraphNodeKind.Kuwahara;
            foreach(var type in new[]{ShaderGraphValueType.Float,ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4,ShaderGraphValueType.Color}) {
                var c=Make(kind,"constant/"+type);c.root.FindInput("In").type=type;Set(c.root,"In",new Vector4(.2f,.4f,.6f,.3f));
                c.expected=_=>type==ShaderGraphValueType.Float?Gray(.2f):new Color(.2f,.4f,type==ShaderGraphValueType.Float2?0:.6f,type is ShaderGraphValueType.Float4 or ShaderGraphValueType.Color?.3f:1);
            }
            var edge=Make(kind,"step preservation");var step=edge.Node(ShaderGraphNodeKind.Step);edge.Connect(X(edge),step,"In");Scalar(step,"Edge",.5f);edge.Connect(step,edge.root,"In");edge.expected=uv=>Gray(uv.x>=.5f?1:0);
            var zero=Make(kind,"radius0");zero.Connect(Square(zero),zero.root,"In");Scalar(zero.root,"Radius",0);zero.expected=uv=>Gray(uv.x*uv.x);
            var color=Make(kind,"transparent");color.root.FindInput("In").type=ShaderGraphValueType.Color;Set(color.root,"In",new Vector4(1,0,0,0));color.expected=_=>Color.clear;
            if(batch==7) {
                var c=Make(kind,"independent sorted texture");var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(texture);
                var pixels=new Color[Size*Size];var random=new System.Random(247);for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble());texture.SetPixels(pixels);texture.Apply();
                var sample=c.Node(ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});c.Connect(sample,c.root,"In","Out");
                c.expected=uv=>{int x=(int)(uv.x*Size),y=(int)(uv.y*Size);var result=new Color();for(int k=0;k<4;k++){var values=new float[9];int i=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){var v=pixels[Mathf.Clamp(y+dy,0,Size-1)*Size+Mathf.Clamp(x+dx,0,Size-1)];values[i++]=k==3?v.a:v[k]*v.a;}Array.Sort(values);result[k]=values[4];}if(result.a>0){result.r/=result.a;result.g/=result.a;result.b/=result.a;}return result;};
            }
        }
        if(batch==9)
        {
            foreach(var encoding in new[]{ShaderGraphNormalEncoding.PackedRGB,ShaderGraphNormalEncoding.SignedXYZ}) foreach(string port in new[]{"Convex","Concave","Curvature"}) {
                var c=Make(ShaderGraphNodeKind.NormalCurvature,"flat/"+encoding,port);Static("ShaderGraphNodes","ChangeNormalEncoding",c.root,encoding);c.expected=_=>Gray(0);
            }
            var curved=Make(ShaderGraphNodeKind.NormalCurvature,"varying normal","Curvature");curved.root.normalEncoding=ShaderGraphNormalEncoding.SignedXYZ;
            var combine=curved.Node(ShaderGraphNodeKind.Combine);combine.valueType=ShaderGraphValueType.Float3;curved.Connect(X(curved),combine,"X");Scalar(combine,"Z",1);curved.Connect(combine,curved.root,"Normal");
            curved.expected=uv=>{float a=uv.x+1f/Size,b=uv.x-1f/Size;return Gray((a/Mathf.Sqrt(a*a+1)-b/Mathf.Sqrt(b*b+1))*.5f);};
        }
        if(batch==10)
        {
            foreach(int mode in new[]{0,1}) {
                var pattern=Make(ShaderGraphNodeKind.Truchet,"pattern/"+mode);pattern.root.option=mode;
                var zero=Make(ShaderGraphNodeKind.Truchet,"zero width/"+mode);zero.root.option=mode;Scalar(zero.root,"Width",0);zero.expected=_=>Gray(0);
                var edge=Make(ShaderGraphNodeKind.Truchet,"shared endpoints/"+mode);edge.root.option=mode;Set(edge.root,"UV",new Vector4(.125f,.0625f,0,0));edge.expected=_=>Gray(1);
                var middle=Make(ShaderGraphNodeKind.Truchet,"empty center/"+mode);middle.root.option=mode;Set(middle.root,"UV",new Vector4(.0625f,.0625f,0,0));middle.expected=_=>Gray(0);
            }
        }
        if(batch==10) {
            var c=Make(ShaderGraphNodeKind.DDX,"Truchet hardware derivative");c.Connect(c.Node(ShaderGraphNodeKind.Truchet),c.root,"In");
        }
        if(batch==11)
        {
            foreach(float strength in new[]{0f,1f,8f}) {
                var c=Make(ShaderGraphNodeKind.Sharpen,"adaptive ramp/"+strength);c.root.adaptiveSharpen=true;Scalar(c.root,"Strength",strength);c.Connect(X(c),c.root,"In");c.expected=uv=>Gray(uv.x);
            }
            var poly=Make(ShaderGraphNodeKind.Sharpen,"adaptive quadratic");poly.root.adaptiveSharpen=true;poly.Connect(Square(poly),poly.root,"In");
            var transparent=Make(ShaderGraphNodeKind.Sharpen,"adaptive alpha");transparent.root.adaptiveSharpen=true;transparent.root.FindInput("In").type=ShaderGraphValueType.Color;Set(transparent.root,"In",new Vector4(.2f,.5f,.9f,.3f));transparent.expected=_=>new Color(.2f,.5f,.9f,.3f);
            var nested=Make(ShaderGraphNodeKind.Median,"nested Kuwahara");var ku=nested.Node(ShaderGraphNodeKind.Kuwahara);nested.Connect(Square(nested),ku,"In");nested.Connect(ku,nested.root,"In");
        }
        if(batch==12)
        {
            foreach(int mode in new[]{0,1}) foreach(float input in new[]{.1f,.31f,.9f}) {
                var c=Make(ShaderGraphNodeKind.PaletteQuantize,"distance/"+mode+"/"+input);c.root.option=mode;Scalar(c.root,"Colors",2);Set(c.root,"Color",new Vector4(input,input,input,.37f));
                var gradient=new WhimTexGradient();gradient.SetKeys(new[]{new GradientColorKey(Color.black,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                c.root.inputs.Add(new ShaderGraphInput{port="Palette",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter{type=ShaderParameterType.Gradient,gradientValue=gradient}});
                float expected=input>(mode==0?.5f:.125f)?1:0;c.expected=_=>new Color(expected,expected,expected,.37f);
            }
        }
        return cases;
    }
    static void ModelChecks(TestContext t,List<GpuCase> cases)
    {
        var document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;
        try {
            var stored=cases.Where(c=>!c.graph.nodes.Any(n=>n.inputs.Any(v=>v.resource?.textureValue!=null))).ToList();
            foreach(var c in stored) AddLayer(document,c.graph);
            using var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false);
            t.Equal(stored.Count,loaded.Document.layers.Count,"JSON layers");
            for(int i=0;i<stored.Count;i++) {
                var before=stored[i].root;var after=((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph.FindNode(before.id);
                t.Equal(before.adaptiveSharpen,after.adaptiveSharpen,"JSON adaptive sharpen");t.Equal(before.normalEncoding,after.normalEncoding,"JSON normal encoding");t.Equal(before.kind,after.kind,"JSON kind");t.Equal(before.option,after.option,"JSON quality");t.True(Ports(before).SequenceEqual(Ports(after)),"JSON ports");
            }
        } finally {Object.DestroyImmediate(document);}
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

    public static object RunIntegration()
    {
        var t = new TestContext();
        var c=new GpuCase();var noise=c.Node(ShaderGraphNodeKind.PerlinNoise);var ss=c.Node(ShaderGraphNodeKind.Supersample);
        var tile=c.Node(ShaderGraphNodeKind.MakeTileable);var palette=c.Node(ShaderGraphNodeKind.PaletteQuantize);
        var flow=c.Node(ShaderGraphNodeKind.FlowWarp);Set(flow,"Flow",new Vector4(.2f,-.1f,0,0));
        var pattern=c.Node(ShaderGraphNodeKind.Truchet);Scalar(pattern,"Scale",4);Scalar(pattern,"Width",.35f);c.Connect(flow,pattern,"UV");
        var median=c.Node(ShaderGraphNodeKind.Median);c.Connect(pattern,median,"In");
        var kuwahara=c.Node(ShaderGraphNodeKind.Kuwahara);c.Connect(median,kuwahara,"In");
        var sharpen=c.Node(ShaderGraphNodeKind.Sharpen);sharpen.adaptiveSharpen=true;c.Connect(kuwahara,sharpen,"In");
        c.Connect(median,palette,"Color");palette.option=1;
        c.Connect(palette,c.Node(ShaderGraphNodeKind.Output),"RGBA");
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
            t.True(reference.Max(p=>p.r)-reference.Min(p=>p.r)>.2f,"Nonuniform image filters output");
            var thumb=(Texture2D)Call(typeof(WhimTexDocument),doc,"GetLayerThumbnail",layer,64,false);
            t.True(thumb!=null&&thumb.GetPixels().All(p=>float.IsFinite(p.r))&&thumb.GetPixels().Max(p=>p.r)-thumb.GetPixels().Min(p=>p.r)>.2f,"image filters thumbnail");
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
            return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message="Image filters composite, both group modes, hidden Layer input, clipping, thumbnail and raster export; render state restored."};
        }
        finally
        {
            RenderTexture.active=old;GL.sRGBWrite=srgb;
            if(exported!=null)Object.DestroyImmediate(exported);Object.DestroyImmediate(pixels);sentinel.Release();Object.DestroyImmediate(sentinel);Object.DestroyImmediate(doc);
        }
    }
    public static async Task<object> RunBatch(int suite)
    {
        var t = new TestContext();
        var owned=new List<Texture2D>();
        var cases = Cases(suite,owned);
        ModelChecks(t, cases);
        int budgetSeconds = 50;
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
                    if (timer.Elapsed.TotalSeconds > budgetSeconds) throw new TimeoutException("Image filter suite budget: " + label);
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
                if (timer.Elapsed.TotalSeconds > budgetSeconds) throw new TimeoutException("Image filter suite budget before " + c.label);
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
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
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
            foreach(var texture in owned) Object.DestroyImmediate(texture);
        }
        return new TestResult {status="passed",checks=t.Checks,failures=Array.Empty<string>(), message=$"{cases.Count} image filter GPU cases, JSON, pure SVM, clean diagnostics. Max error {maxError:R}, max difference {maxDifference:R}."};
    }
}
