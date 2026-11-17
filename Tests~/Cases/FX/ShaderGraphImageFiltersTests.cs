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
        internal Func<Vector2, Vector2> coverageBounds;
        internal bool nativeOnly = false;
        internal Action afterFirstRender;
        internal Color updatedColor;
        internal Texture2D inputImage;
        internal bool noInput;
        internal int imageSamples = -1;
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
        if (batch is 36 or 37) return PathBlurQualityCases(batch - 36);
        if (batch is 34 or 35) return FilterQualityCases(batch - 34);
        if (batch >= 30) return SignedNormalCases(batch,owned);
        if (batch >= 26) return ColorValueCases(batch);
        if (batch == 25) return SampleInputCases(owned);
        if (batch == 24) return SoftSignCases();
        if (batch >= 19) return ChannelCases(batch,owned);
        if (batch >= 15) return ArtistControlCases(batch);
        if (batch >= 13) return ColorComparisonCases(batch);
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
            var alpha=Make(ShaderGraphNodeKind.Supersample,"independent alpha");var set=alpha.Node(ShaderGraphNodeKind.SetAlpha);alpha.Connect(X(alpha),set,"Color");alpha.Connect(X(alpha),set,"Alpha");alpha.Connect(set,alpha.root,"In");
            alpha.expected=uv=>new Color(uv.x,uv.x,uv.x,uv.x);
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
            var color=Make(ShaderGraphNodeKind.MakeTileable,"transparent");color.root.FindInput("In").type=ShaderGraphValueType.Color;Set(color.root,"In",new Vector4(1,0,0,0));color.expected=_=>new Color(1,0,0,0);
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
                var c=Make(ShaderGraphNodeKind.PaletteQuantize,"count/"+count);c.root.option=0;Scalar(c.root,"Colors",count);Set(c.root,"Color",new Vector4(.31f,.31f,.31f,.35f));
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
            if(batch==7) foreach(var type in new[]{ShaderGraphValueType.Float,ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4,ShaderGraphValueType.Color}) {
                var c=Make(kind,"prepared constant/"+type);c.hybrid=true;Scalar(c.root,"Radius",8);c.root.FindInput("In").type=type;
                Set(c.root,"In",new Vector4(-.2f,4f,.6f,0));
                c.expected=_=>type==ShaderGraphValueType.Float?Gray(-.2f):new Color(-.2f,4f,type==ShaderGraphValueType.Float2?0:.6f,type is ShaderGraphValueType.Float4 or ShaderGraphValueType.Color?0:1);
            }
            var edge=Make(kind,"step preservation");var step=edge.Node(ShaderGraphNodeKind.Step);edge.Connect(X(edge),step,"In");Scalar(step,"Edge",.5f);edge.Connect(step,edge.root,"In");edge.expected=uv=>Gray(uv.x>=.5f?1:0);
            var zero=Make(kind,"radius0");zero.Connect(Square(zero),zero.root,"In");Scalar(zero.root,"Radius",0);zero.expected=uv=>Gray(uv.x*uv.x);
            var color=Make(kind,"transparent");color.root.FindInput("In").type=ShaderGraphValueType.Color;Set(color.root,"In",new Vector4(1,0,0,0));color.expected=_=>new Color(1,0,0,0);
            if(batch==7) {
                var c=Make(kind,"independent sorted texture");var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(texture);
                var pixels=new Color[Size*Size];var random=new System.Random(247);for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble());texture.SetPixels(pixels);texture.Apply();
                var sample=c.Node(ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});c.Connect(sample,c.root,"In","Out");
                c.expected=uv=>{int x=(int)(uv.x*Size),y=(int)(uv.y*Size);var result=new Color();for(int k=0;k<4;k++){var values=new float[9];int i=0;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){var v=pixels[Mathf.Clamp(y+dy,0,Size-1)*Size+Mathf.Clamp(x+dx,0,Size-1)];values[i++]=v[k];}Array.Sort(values);result[k]=values[4];}return result;};
            }
        }
        if(batch==9)
        {
            foreach(string port in new[]{"Convex","Concave","Curvature"}) {
                var c=Make(ShaderGraphNodeKind.NormalCurvature,"flat signed",port);c.expected=_=>Gray(0);
            }
            var curved=Make(ShaderGraphNodeKind.NormalCurvature,"varying normal","Curvature");
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
    static List<GpuCase> ColorComparisonCases(int batch)
    {
        var cases = new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind, int mode, string label, string port = "Out")
        {
            var c = new GpuCase { label = label + "/" + mode, port = port };
            c.root = c.Node(kind);
            if(kind is ShaderGraphNodeKind.ColorMask or ShaderGraphNodeKind.ReplaceColor) c.root.option = mode;
            c.Connect(c.root, c.Node(ShaderGraphNodeKind.Output), "RGBA", port);
            cases.Add(c); return c;
        }
        ShaderGraphNode Source(GpuCase c)
        {
            var set = c.Node(ShaderGraphNodeKind.SetAlpha);
            c.Connect(X(c), set, "Color"); Scalar(set, "Alpha", .37f); return set;
        }
        foreach (int mode in new[] { 0, 1, 2 })
        {
            if (batch == 13)
            {
                foreach (string output in new[] { "ColorMask", "Out", "Mask" })
                {
                    var kind = output == "ColorMask" ? ShaderGraphNodeKind.ColorMask : ShaderGraphNodeKind.ReplaceColor;
                    var c = Make(kind, mode, "ramp/" + output, output == "Mask" ? "Mask" : "Out");
                    c.Connect(Source(c), c.root, "Color");
                    Set(c.root, kind == ShaderGraphNodeKind.ColorMask ? "KeyColor" : "From", new Vector4(.2f, .3f, .4f, .9f));
                    Scalar(c.root, "Range", .1f); Scalar(c.root, "Fuzziness", .4f);
                    if (kind == ShaderGraphNodeKind.ReplaceColor) Set(c.root, "To", new Vector4(1.4f, -.2f, .7f, .01f));
                    c.expected = uv => {
                        var color = new Color(uv.x, uv.x, uv.x, .37f);
                        float mask = ReferenceMask(color, new Color(.2f,.3f,.4f), .1f, .4f, mode);
                        if (output != "Out") return Gray(mask);
                        var replaced = Color.LerpUnclamped(color, new Color(1.4f,-.2f,.7f,.01f), mask);
                        replaced.a = color.a; return replaced;
                    };
                }
                var shared = Make(ShaderGraphNodeKind.SetAlpha, mode, "both outputs one mask");
                var replacement = shared.Node(ShaderGraphNodeKind.ReplaceColor); replacement.option = mode;
                shared.Connect(Source(shared), replacement, "Color");
                shared.Connect(replacement, shared.root, "Color"); shared.Connect(replacement, shared.root, "Alpha", "Mask");
                shared.expected = uv => {
                    var color = new Color(uv.x,uv.x,uv.x,.37f);
                    float mask = ReferenceMask(color,Color.black,.1f,.05f,mode);
                    var result = Color.LerpUnclamped(color,Color.white,mask);result.a=mask;return result;
                };
            }
            if (batch == 14)
            {
                foreach (var values in new[] { (0f,0f,0f), (.2f,.2f,0f), (.25f,.2f,0f), (.3f,-1f,-1f), (2f,2.1f,.6f), (-.4f,.2f,2f) })
                {
                    var c = Make(ShaderGraphNodeKind.ColorMask, mode, "edge HDR/" + values);
                    Set(c.root,"Color",new Vector4(values.Item1,0,0,0));Set(c.root,"KeyColor",new Vector4(0,0,0,1));
                    Scalar(c.root,"Range",values.Item2);Scalar(c.root,"Fuzziness",values.Item3);
                    float mask=ReferenceMask(new Color(values.Item1,0,0),Color.black,values.Item2,values.Item3,mode);
                    c.expected=_=>Gray(mask);
                }
                var pair = Make(ShaderGraphNodeKind.Subtract, mode, "mask equivalence");
                var maskNode = pair.Node(ShaderGraphNodeKind.ColorMask); maskNode.option=mode;
                var replacement = pair.Node(ShaderGraphNodeKind.ReplaceColor); replacement.option=mode;
                var source=Source(pair);pair.Connect(source,maskNode,"Color");pair.Connect(source,replacement,"Color");
                Set(maskNode,"KeyColor",new Vector4(.3f,.1f,.2f,.8f));Set(replacement,"From",new Vector4(.3f,.1f,.2f,0));
                pair.Connect(maskNode,pair.root,"A");pair.Connect(replacement,pair.root,"B","Mask");pair.expected=_=>Gray(0);
            }
        }
        return cases;
    }
    static List<GpuCase> ArtistControlCases(int batch)
    {
        var cases = new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind, string name, string port = "Out")
        {
            var c = new GpuCase { label = kind + "/" + name, port = port };
            c.root = c.Node(kind); c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA",port); cases.Add(c); return c;
        }
        if (batch == 15)
        {
            foreach (int mode in new[] {0,1}) foreach (var space in new[] {ColorSpace.Linear,ColorSpace.Gamma})
                foreach (var interpolation in new[] {WhimTexGradientInterpolation.Blend,WhimTexGradientInterpolation.Fixed})
            {
                var c=Make(ShaderGraphNodeKind.PaletteQuantize,$"keys/{mode}/{space}/{interpolation}");
                c.root.option=mode;c.root.paletteKeys=true;
                var authored=new Color(.3f,.6f,.1f,1); var expected=space==ColorSpace.Gamma?authored.linear:authored;
                expected.a=.37f; Set(c.root,"Color",expected);
                var gradient=new WhimTexGradient {ColorSpace=space,Interpolation=interpolation};
                gradient.SetKeys(new[]{new GradientColorKey(Color.black,0),new GradientColorKey(authored,.12345f),
                    new GradientColorKey(Color.blue,.12346f),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.2f,1)});
                c.root.inputs.Add(new ShaderGraphInput {port="Palette",type=ShaderGraphValueType.Gradient,
                    resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=gradient}});
                Scalar(c.root,"Colors",1);c.expected=_=>expected;
                var ignored=c.Node(ShaderGraphNodeKind.Value);ignored.valueType=ShaderGraphValueType.Float;
                c.Connect(ignored,c.root,"Colors");
                if(mode==0&&space==ColorSpace.Linear&&interpolation==WhimTexGradientInterpolation.Blend)
                {
                    c.updatedColor=new Color(.19f,.53f,.04f,.37f);
                    c.afterFirstRender=()=>gradient.SetKeys(new[]{new GradientColorKey(c.updatedColor,0),new GradientColorKey(c.updatedColor,1)},gradient.AlphaKeys);
                }
            }
            var many=Make(ShaderGraphNodeKind.PaletteQuantize,"all 64 authored keys");many.root.paletteKeys=true;
            var keys=Enumerable.Range(0,64).Select(i=>new GradientColorKey(i==63?Color.blue:Color.black,i/63f)).ToArray();
            var large=new WhimTexGradient {ColorSpace=ColorSpace.Linear};large.SetKeys(keys,new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            many.root.inputs.Add(new ShaderGraphInput {port="Palette",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=large}});
            Set(many.root,"Color",new Vector4(0,0,1,.37f));many.expected=_=>new Color(0,0,1,.37f);
            var hdr=Make(ShaderGraphNodeKind.PaletteQuantize,"HDR negative key");hdr.root.paletteKeys=true;hdr.root.option=0;
            var hdrColor=new Color(-.2f,2,.4f,.5f);var hdrGradient=new WhimTexGradient {ColorSpace=ColorSpace.Linear};
            hdrGradient.SetKeys(new[]{new GradientColorKey(hdrColor,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            hdr.root.inputs.Add(new ShaderGraphInput {port="Palette",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=hdrGradient}});
            Set(hdr.root,"Color",hdrColor);hdr.expected=_=>hdrColor;
            foreach(var interpolation in new[]{WhimTexGradientInterpolation.Blend,WhimTexGradientInterpolation.Fixed,WhimTexGradientInterpolation.Custom})
            {
                var c=Make(ShaderGraphNodeKind.SampleGradient,"ramp preserved/"+interpolation);
                var gradient=new WhimTexGradient {ColorSpace=ColorSpace.Gamma};
                gradient.SetKeys(new[]{new GradientColorKey(new Color(.1f,.7f,.2f),0),new GradientColorKey(new Color(.7f,.2f,.9f),.3f),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(.2f,0),new GradientAlphaKey(.8f,1)});
                gradient.Interpolation=interpolation;
                c.root.inputs.Add(new ShaderGraphInput {port="Gradient",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=gradient}});
                c.Connect(X(c),c.root,"T");
                using var oldLut=new WhimTexGradientTexture();var texture=oldLut.GetTexture(gradient);var colors=texture.GetPixels();
                c.expected=uv=>{float x=uv.x*511;int left=Mathf.FloorToInt(x);return interpolation==WhimTexGradientInterpolation.Fixed?
                    colors[Mathf.FloorToInt(x+.5f)]:Color.LerpUnclamped(colors[left],colors[Mathf.Min(left+1,511)],x-left);};
                if(interpolation==WhimTexGradientInterpolation.Custom)
                {
                    var key=Make(ShaderGraphNodeKind.PaletteQuantize,"Custom marker colors");key.root.paletteKeys=true;key.root.option=0;
                    key.root.inputs.Add(new ShaderGraphInput {port="Palette",type=ShaderGraphValueType.Gradient,resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=gradient}});
                    var expected=gradient.ColorKeys[1].color.linear;expected.a=.6f;Set(key.root,"Color",expected);key.expected=_=>expected;
                }
            }
            var composed=Make(ShaderGraphNodeKind.PaletteQuantize,"Truchet input");composed.root.paletteKeys=true;composed.Connect(composed.Node(ShaderGraphNodeKind.Truchet),composed.root,"Color");
        }
        if (batch == 16)
        {
            foreach(float width in new[]{0f,-1f,.08f,.2f,.5f,1f})
            {
                var c=Make(ShaderGraphNodeKind.MakeTileable,"width/"+width);Scalar(c.root,"BlendWidth",width);c.Connect(Square(c),c.root,"In");
                c.expected=uv=>{
                    float x=Mathf.Repeat(uv.x,1),d=width<=0?1:Mathf.Clamp01(Mathf.Min(x,1-x)/Mathf.Min(width,.5f));
                    float weight=d*d*d*(d*(d*6-15)+10),other=Mathf.Repeat(x+.5f,1);
                    return Gray(Mathf.Lerp(other*other,x*x,weight));
                };
            }
            foreach(float u in new[]{0f,1f,-1f})
            {
                var c=Make(ShaderGraphNodeKind.MakeTileable,"corner/"+u);Scalar(c.root,"BlendWidth",.1f);Set(c.root,"UV",new Vector4(u,u,0,0));c.Connect(Square(c),c.root,"In");c.expected=_=>Gray(.25f);
            }
            var alpha=Make(ShaderGraphNodeKind.MakeTileable,"narrow independent alpha");Scalar(alpha.root,"BlendWidth",.15f);
            var set=alpha.Node(ShaderGraphNodeKind.SetAlpha);alpha.Connect(X(alpha),set,"Color");alpha.Connect(X(alpha),set,"Alpha");alpha.Connect(set,alpha.root,"In");
            alpha.expected=uv=>{float x=uv.x,z=Mathf.Repeat(x+.5f,1),d=Mathf.Clamp01(Mathf.Min(x,1-x)/.15f),w=d*d*d*(d*(d*6-15)+10);
                float a=Mathf.Lerp(z,x,w);return new Color(a,a,a,a);};
        }
        if (batch == 17)
        {
            foreach(int shape in new[]{0,1}) foreach(float scale in new[]{3f,16f})
            {
                var c=Make(ShaderGraphNodeKind.Truchet,$"AA/{shape}/{scale}");c.root.option=shape;Scalar(c.root,"Scale",scale);Scalar(c.root,"Width",.17f);
                c.coverageBounds=uv=>{
                    int x=(int)(uv.x*Size),y=(int)(uv.y*Size);
                    float Field(int px,int py)=>TruchetDistance(new Vector2((px+.5f)/Size,(py+.5f)/Size),scale,.17f,shape);
                    float minimum=1,maximum=0;
                    // Hardware may share either row/column derivative within the pixel quad.
                    for(int row=0;row<2;row++)for(int col=0;col<2;col++)
                    {
                        float fw=Mathf.Abs(Field(x|1,(y&~1)+row)-Field(x&~1,(y&~1)+row))+
                            Mathf.Abs(Field((x&~1)+col,y|1)-Field((x&~1)+col,y&~1));
                        float edge=Mathf.Max(fw*.5f,1e-7f),t=Mathf.Clamp01((Field(x,y)+edge)/(2*edge));
                        float mask=1-t*t*(3-2*t);minimum=Mathf.Min(minimum,mask);maximum=Mathf.Max(maximum,mask);
                    }
                    return new Vector2(minimum,maximum);
                };
            }
            foreach(int shape in new[]{0,1})
            {
                var c=Make(ShaderGraphNodeKind.Truchet,"nonlinear UV/"+shape);c.root.option=shape;
                var multiply=c.Node(ShaderGraphNodeKind.Multiply);var uv=c.Node(ShaderGraphNodeKind.UV);c.Connect(uv,multiply,"A");c.Connect(uv,multiply,"B");c.Connect(multiply,c.root,"UV");
                var sampled=Make(ShaderGraphNodeKind.Supersample,"Truchet replay/"+shape);var tr=sampled.Node(ShaderGraphNodeKind.Truchet);tr.option=shape;sampled.Connect(tr,sampled.root,"In");
                var fractal=Make(ShaderGraphNodeKind.Fractal,"Truchet replay/"+shape);Scalar(fractal.root,"Octaves",2);tr=fractal.Node(ShaderGraphNodeKind.Truchet);tr.option=shape;fractal.Connect(tr,fractal.root,"In");
            }
        }
        if (batch == 18)
        {
            float Mask(float d,float feather) {float t=Mathf.Clamp01(.5f+d/feather);return 1-t*t*(3-2*t);}
            foreach(int shape in new[]{0,1})
            {
                ShaderGraphNode Pattern(GpuCase c)
                {
                    var n=c.root.kind==ShaderGraphNodeKind.Truchet?c.root:c.Node(ShaderGraphNodeKind.Truchet);
                    n.option=shape;Scalar(n,"Scale",3);Scalar(n,"Width",.2f);return n;
                }
                foreach(float feather in new[]{0f,1f})
                {
                    var c=Make(ShaderGraphNodeKind.Truchet,$"signed distance/{shape}/{feather}","Distance");
                    var n=Pattern(c);Scalar(n,"Feather",feather);
                    c.expected=uv=>Gray(TruchetDistance(uv,3,.2f,shape));
                }
                foreach(float offset in new[]{0f,1f,2f})
                {
                    var c=Make(ShaderGraphNodeKind.Truchet,$"centered feather/{shape}/{offset}");var n=Pattern(c);
                    float y=.5f+offset*.1f*(shape==0?1:Mathf.Sqrt(2));var uv=new Vector2(0,y/3);
                    Set(n,"UV",uv);Scalar(n,"Feather",.4f);
                    c.expected=_=>Gray(Mask((offset-1)*.1f,.4f));
                }
                var empty=Make(ShaderGraphNodeKind.Truchet,"empty feather/"+shape);Pattern(empty);Scalar(empty.root,"Width",0);Scalar(empty.root,"Feather",1);empty.expected=_=>Gray(0);
                var negative=Make(ShaderGraphNodeKind.Subtract,"negative feather/"+shape);var a=Pattern(negative);var b=Pattern(negative);
                Scalar(a,"Feather",-1);negative.Connect(a,negative.root,"A");negative.Connect(b,negative.root,"B");negative.expected=_=>Gray(0);
                var dynamic=Make(ShaderGraphNodeKind.Truchet,"connected feather/"+shape);Pattern(dynamic);
                var amount=X(dynamic);Scalar(amount,"B",1);dynamic.Connect(amount,dynamic.root,"Feather");
                dynamic.expected=uv=>Gray(Mask(TruchetDistance(uv,3,.2f,shape),1+uv.x));
                var both=Make(ShaderGraphNodeKind.Add,"both outputs/"+shape);var tr=Pattern(both);Scalar(tr,"Feather",1);
                both.Connect(tr,both.root,"A");both.Connect(tr,both.root,"B","Distance");
                both.expected=uv=>{float d=TruchetDistance(uv,3,.2f,shape);return Gray(d+Mask(d,1));};
                foreach(var kind in new[]{ShaderGraphNodeKind.Supersample,ShaderGraphNodeKind.Fractal})
                {
                    var c=Make(kind,"feathered Truchet replay/"+shape);tr=Pattern(c);Scalar(tr,"Feather",.3f);
                    c.Connect(tr,c.root,"In");if(kind==ShaderGraphNodeKind.Fractal)Scalar(c.root,"Octaves",2);
                }
            }
        }
        return cases;
    }
    static List<GpuCase> SampleInputCases(List<Texture2D> owned)
    {
        var cases=new List<GpuCase>();
        Color Pixel(Vector2 uv)=>new Color(uv.x*3-1,uv.y*2,.25f+uv.x*.5f,uv.y*.75f);
        Texture2D Image(bool empty)
        {
            var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(image);
            var pixels=new Color[Size*Size];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)pixels[y*Size+x]=empty?Color.clear:Pixel(new Vector2((x+.5f)/Size,(y+.5f)/Size));
            image.SetPixels(pixels);image.Apply();return image;
        }
        var source=Image(false);var clear=Image(true);
        foreach(bool empty in new[]{false,true})foreach(var filter in empty?new[]{ShaderGraphFilter.Point}:new[]{ShaderGraphFilter.Point,ShaderGraphFilter.Bilinear})
        foreach(string port in new[]{"Out","R","G","B","A"})
        {
            var c=new GpuCase{label="Sample Input/"+(empty?"None":filter.ToString())+"/"+port,port=port,inputImage=empty?clear:source,noInput=empty,imageSamples=1};
            c.root=c.Node(ShaderGraphNodeKind.SampleInput);c.root.filter=filter;c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA",port);
            c.expected=uv=>{var color=empty?Color.clear:Pixel(uv);return port=="Out"?color:Gray(color["RGBA".IndexOf(port,StringComparison.Ordinal)]);};
            var cache=Activator.CreateInstance(Find("ShaderGraphTypeCache"),true);Call(cache.GetType(),cache,"Prepare",c.graph);
            if((ShaderGraphValueType)Call(cache.GetType(),cache,"Output",c.root,port)!=(port=="Out"?ShaderGraphValueType.Float4:ShaderGraphValueType.Float))throw new Exception("Sample Input output type");
            if((string)Static("ShaderGraphNodes","PortDisplayName",c.root,port,true)!=(port=="Out"?"RGBA":port))throw new Exception("Sample Input output label");
            if(!((string[])Static("ShaderGraphNodes","Outputs",c.root)).SequenceEqual(new[]{"Out","R","G","B","A"}))throw new Exception("Sample Input output order");
            cases.Add(c);
        }
        foreach(var kind in new[]{ShaderGraphNodeKind.SampleInput,ShaderGraphNodeKind.SampleTexture})
        {
            var c=new GpuCase{label=kind+"/shared sample",inputImage=source,imageSamples=1,expected=Pixel};
            var sample=c.Node(kind);
            if(kind==ShaderGraphNodeKind.SampleTexture) sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=source}});
            c.root=c.Node(ShaderGraphNodeKind.Combine);c.root.valueType=ShaderGraphValueType.Float4;
            for(int k=0;k<4;k++)c.Connect(sample,c.root,new[]{"X","Y","Z","W"}[k],new[]{"R","G","B","A"}[k]);
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA");cases.Add(c);
        }
        return cases;
    }
    // Color is input padding only. These independent expectations deliberately cover
    // hidden RGB and alpha as data, with and without a neutral math/reroute node.
    static List<GpuCase> ColorValueCases(int batch)
    {
        var cases=new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind,string label,bool native=false)
        {
            var c=new GpuCase{label=kind+"/"+label,nativeOnly=native};c.root=c.Node(kind);
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA");cases.Add(c);return c;
        }
        ShaderGraphNode Source(GpuCase c,int route,bool varying=false)
        {
            var color=c.Node(ShaderGraphNodeKind.SetAlpha);Set(color,"Color",new Vector4(1.2f,-.3f,.7f,.9f));Scalar(color,"Alpha",0);
            if(varying) {c.Connect(Square(c),color,"Color");c.Connect(X(c),color,"Alpha");}
            if(route==1) {var mul=c.Node(ShaderGraphNodeKind.Multiply);c.Connect(color,mul,"A");Scalar(mul,"B",1);return mul;}
            if(route==2) {var pass=c.Node(ShaderGraphNodeKind.Reroute);c.Connect(color,pass,"In");return pass;}
            return color;
        }
        if(batch==26)
        {
            var input=new Vector4(.25f,-.5f,2,.125f);
            foreach(var type in new[]{ShaderGraphValueType.Float,ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4,ShaderGraphValueType.Color})
            {
                var col=Make(ShaderGraphNodeKind.Premultiply,"col conversion/"+type);
                col.root.FindInput("Color").type=type;Set(col.root,"Color",input);
                Color padded=type==ShaderGraphValueType.Float?new Color(input.x,input.x,input.x,1):
                    new Color(input.x,input.y,type==ShaderGraphValueType.Float2?0:input.z,type is ShaderGraphValueType.Float4 or ShaderGraphValueType.Color?input.w:1);
                col.expected=_=>new Color(padded.r*padded.a,padded.g*padded.a,padded.b*padded.a,padded.a);
                var numeric=Make(ShaderGraphNodeKind.Multiply,"numeric conversion/"+type);
                numeric.root.FindInput("A").type=type;Set(numeric.root,"A",input);
                numeric.root.FindInput("B").type=ShaderGraphValueType.Float4;Set(numeric.root,"B",Vector4.one);
                numeric.expected=_=>type==ShaderGraphValueType.Float?new Color(input.x,input.x,input.x,input.x):
                    new Color(input.x,input.y,type==ShaderGraphValueType.Float2?0:input.z,type is ShaderGraphValueType.Float4 or ShaderGraphValueType.Color?input.w:0);
            }
            foreach(float alpha in new[]{0f,1f,.25f,-.5f,2f,.00001f})foreach(bool inverse in new[]{false,true})
            {
                var c=Make(inverse?ShaderGraphNodeKind.Unpremultiply:ShaderGraphNodeKind.Premultiply,"signed HDR alpha/"+alpha);
                Set(c.root,"Color",new Vector4(2,-1,.5f,alpha));
                float factor=inverse?(alpha==0?0:1/alpha):alpha;
                c.expected=_=>new Color(2*factor,-factor,.5f*factor,alpha);c.tolerance=inverse&&alpha==.00001f?.02f:.00001f;
            }
            var roundtrip=Make(ShaderGraphNodeKind.Unpremultiply,"explicit roundtrip");
            var pre=roundtrip.Node(ShaderGraphNodeKind.Premultiply);Set(pre,"Color",new Vector4(2,-1,.5f,-.25f));roundtrip.Connect(pre,roundtrip.root,"Color");
            roundtrip.expected=_=>new Color(2,-1,.5f,-.25f);
            var later=Make(ShaderGraphNodeKind.Premultiply,"col does not reset existing alpha");
            var multiply=later.Node(ShaderGraphNodeKind.Multiply);var rgba=later.Node(ShaderGraphNodeKind.SetAlpha);
            Set(rgba,"Color",new Vector4(.8f,.4f,.2f,1));Scalar(rgba,"Alpha",.6f);later.Connect(rgba,multiply,"A");Scalar(multiply,"B",.5f);later.Connect(multiply,later.root,"Color");
            later.expected=_=>new Color(.12f,.06f,.03f,.3f);
        }
        if(batch==27 || batch==28)
        {
            var kinds=batch==27?new[]{ShaderGraphNodeKind.Supersample,ShaderGraphNodeKind.MakeTileable,ShaderGraphNodeKind.Sharpen,ShaderGraphNodeKind.Median,ShaderGraphNodeKind.Kuwahara,ShaderGraphNodeKind.EdgeDetection}:
                new[]{ShaderGraphNodeKind.Blur,ShaderGraphNodeKind.Sharpen,ShaderGraphNodeKind.DirectionalBlur,ShaderGraphNodeKind.RadialBlur,ShaderGraphNodeKind.SlopeBlur,ShaderGraphNodeKind.BilateralBlur};
            foreach(var kind in kinds)foreach(int route in new[]{0,1,2})
            {
                var c=Make(kind,"hidden RGB/route "+route,batch==28);if(kind==ShaderGraphNodeKind.Sharpen)c.root.adaptiveSharpen=batch==27;
                c.Connect(Source(c,route),c.root,"In");c.expected=_=>kind==ShaderGraphNodeKind.EdgeDetection?Color.clear:new Color(1.2f,-.3f,.7f,0);
                if(kind==ShaderGraphNodeKind.SlopeBlur)c.Connect(X(c),c.root,"Slope");
            }
        }
        if(batch==29)
        {
            foreach(int route in new[]{0,1})
            {
                var edge=Make(ShaderGraphNodeKind.EdgeDetection,"RGBA ramp/route "+route);edge.Connect(Source(edge,route,true),edge.root,"In");
                edge.expected=uv=>new Color(2*uv.x/Size,2*uv.x/Size,2*uv.x/Size,1f/Size);
                var ss=Make(ShaderGraphNodeKind.Supersample,"RGBA quadratic/route "+route);ss.Connect(Source(ss,route,true),ss.root,"In");
                ss.expected=uv=>new Color(uv.x*uv.x+1f/(16*Size*Size),uv.x*uv.x+1f/(16*Size*Size),uv.x*uv.x+1f/(16*Size*Size),uv.x);
                var blur=Make(ShaderGraphNodeKind.Blur,"RGBA quadratic/route "+route,true);blur.Connect(Source(blur,route,true),blur.root,"In");Scalar(blur.root,"Radius",1);
                float variance=2*Mathf.Exp(-2)/(1+2*Mathf.Exp(-2))/(Size*Size);
                blur.expected=uv=>new Color(uv.x*uv.x+variance,uv.x*uv.x+variance,uv.x*uv.x+variance,uv.x);
            }
            foreach(bool native in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.Unpremultiply,"explicit alpha weighted "+(native?"Blur":"Supersample"));c.nativeOnly=native;
                var pre=c.Node(ShaderGraphNodeKind.Premultiply);c.Connect(Source(c,0,true),pre,"Color");
                var blur=c.Node(native?ShaderGraphNodeKind.Blur:ShaderGraphNodeKind.Supersample);if(native)Scalar(blur,"Radius",1);
                c.Connect(pre,blur,"In");c.Connect(blur,c.root,"Color");
                float variance=native?2*Mathf.Exp(-2)/(1+2*Mathf.Exp(-2))/(Size*Size):1f/(16*Size*Size);
                c.expected=uv=>new Color(uv.x*uv.x+3*variance,uv.x*uv.x+3*variance,uv.x*uv.x+3*variance,uv.x);
            }
        }
        var test=new TestContext();
        foreach(var c in cases)
        {
            var cache=Activator.CreateInstance(Find("ShaderGraphTypeCache"),true);Call(cache.GetType(),cache,"Prepare",c.graph);
            foreach(var node in c.graph.nodes)foreach(string port in (string[])Static("ShaderGraphNodes","Outputs",node))
                test.True((ShaderGraphValueType)Call(cache.GetType(),cache,"Output",node,port)!=ShaderGraphValueType.Color,"No Color value propagates: "+node.kind);
            test.Equal(ShaderGraphValueType.Float4,(ShaderGraphValueType)Call(cache.GetType(),cache,"Output",c.root,"Out"),c.label+" numeric output");
        }
        return cases;
    }
    static List<GpuCase> SoftSignCases()
    {
        var cases=new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind,string label)
        {
            var c=new GpuCase{label=kind+"/"+label};c.root=c.Node(kind);
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA");cases.Add(c);return c;
        }
        float Eval(float x,bool inverse)=>(float)((double)x/(1+(inverse?-1:1)*Math.Abs((double)x)));
        foreach(var kind in new[]{ShaderGraphNodeKind.SoftSign,ShaderGraphNodeKind.InverseSoftSign})
        {
            bool inverse=kind==ShaderGraphNodeKind.InverseSoftSign;
            if((string)Static("ShaderGraphNodes","Name",kind)!=(inverse?"Inverse Soft Sign":"Soft Sign")||
                (string)Static("ShaderGraphNodes","Category",kind)!="Math/Advanced")throw new Exception("Soft Sign catalog metadata");
            var zero=Make(kind,"default");zero.expected=_=>Gray(0);
            foreach(var type in new[]{ShaderGraphValueType.Float,ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4,ShaderGraphValueType.Color})
            {
                int width=type==ShaderGraphValueType.Color?4:(int)type+1;
                var c=Make(kind,"width/"+type);var input=inverse?new Vector4(-.875f,-.5f,.25f,.8f):new Vector4(-3,-.5f,0,2);
                c.root.FindInput("In").type=type;Set(c.root,"In",input);
                var result=new Color(0,0,0,1);for(int k=0;k<width;k++)result[k]=Eval(input[k],inverse);
                c.expected=_=>width==1?Gray(result.r):result;
                var cache=Activator.CreateInstance(Find("ShaderGraphTypeCache"),true);Call(cache.GetType(),cache,"Prepare",c.graph);
                if((ShaderGraphValueType)Call(cache.GetType(),cache,"Output",c.root,"Out")!=(ShaderGraphValueType)(width-1))throw new Exception("Soft Sign output width");
            }
            foreach(float x in inverse?new[]{-.999f,-.75f,0f,.75f,.999f,-2f,2f}:new[]{-10000f,-1f,0f,1f,10000f})
            {var c=Make(kind,"scalar/"+x);Scalar(c.root,"In",x);c.expected=_=>Gray(Eval(x,inverse));}
        }
        var ramp=Make(ShaderGraphNodeKind.SoftSign,"UV ramp");var mul=ramp.Node(ShaderGraphNodeKind.Multiply);ramp.Connect(X(ramp),mul,"A");Scalar(mul,"B",8);
        var sub=ramp.Node(ShaderGraphNodeKind.Subtract);ramp.Connect(mul,sub,"A");Scalar(sub,"B",4);ramp.Connect(sub,ramp.root,"In");
        ramp.expected=uv=>Gray(Eval(uv.x*8-4,false));
        var undo=Make(ShaderGraphNodeKind.InverseSoftSign,"connected roundtrip");var uv=undo.Node(ShaderGraphNodeKind.UV);
        var scale=undo.Node(ShaderGraphNodeKind.Multiply);undo.Connect(uv,scale,"A");Scalar(scale,"B",8);
        var offset=undo.Node(ShaderGraphNodeKind.Subtract);undo.Connect(scale,offset,"A");Scalar(offset,"B",4);
        var soft=undo.Node(ShaderGraphNodeKind.SoftSign);undo.Connect(offset,soft,"In");undo.Connect(soft,undo.root,"In");
        undo.expected=p=>new Color(p.x*8-4,p.y*8-4,0,1);
        return cases;
    }
    static List<GpuCase> ChannelCases(int batch,List<Texture2D> owned)
    {
        var cases=new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind,string label,string port="Out")
        {
            var c=new GpuCase {label=kind+"/"+label,port=port};c.root=c.Node(kind);
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA",port);cases.Add(c);return c;
        }
        ShaderGraphNode ColorField(GpuCase c)
        {
            var combine=c.Node(ShaderGraphNodeKind.Combine);combine.valueType=ShaderGraphValueType.Float4;
            var x=X(c);var g=c.Node(ShaderGraphNodeKind.Multiply);Scalar(g,"B",2);c.Connect(x,g,"A");
            var green=c.Node(ShaderGraphNodeKind.Add);Scalar(green,"B",.25f);c.Connect(g,green,"A");
            var blue=c.Node(ShaderGraphNodeKind.OneMinus);c.Connect(x,blue,"In");
            c.Connect(x,combine,"X");c.Connect(green,combine,"Y");c.Connect(blue,combine,"Z");Scalar(combine,"W",.6f);
            var color=c.Node(ShaderGraphNodeKind.SetAlpha);c.Connect(combine,color,"Color");Scalar(color,"Alpha",.6f);return color;
        }
        float Height(float x,int channel)=>channel switch {1=>x,2=>.25f+2*x,3=>1-x,4=>.6f,_=>.2126f*x+.7152f*(.25f+2*x)+.0722f*(1-x)};
        ShaderGraphNode Extract(GpuCase c,ShaderGraphNode color,int channel,out string port)
        {
            var n=c.Node(channel==0?ShaderGraphNodeKind.Luminance:ShaderGraphNodeKind.Split);
            c.Connect(color,n,channel==0?"Color":"In");port=channel==0?"Out":new[]{"X","Y","Z","W"}[channel-1];return n;
        }
        if(batch==19) foreach(int channel in Enumerable.Range(0,5))
        {
            foreach(var kind in new[]{ShaderGraphNodeKind.HeightToNormal,ShaderGraphNodeKind.HeightToDistortion,ShaderGraphNodeKind.ParallaxMapping,ShaderGraphNodeKind.ParallaxOcclusionMapping})
            {
                var c=Make(kind,"Value Channel/"+channel);c.root.heightSource=ShaderGraphHeightSource.Value;
                c.root.channel=(ShaderGraphChannel)channel;c.root.inputs.RemoveAll(v=>v.port=="Height");c.Connect(ColorField(c),c.root,"Height");
                bool height=kind==ShaderGraphNodeKind.HeightToNormal||kind==ShaderGraphNodeKind.HeightToDistortion;
                if(height)
                {
                    c.root.derivative=ShaderGraphDerivative.CentralDifference;
                    float slope=(Height(1,channel)-Height(0,channel))*8/Size;
                    var normal=new Vector3(-slope,0,1).normalized;
                    c.expected=_=>kind==ShaderGraphNodeKind.HeightToNormal?new Color(normal.x,normal.y,normal.z,1):new Color(slope,0,0,1);
                }
                else
                {
                    Set(c.root,"ViewDirection",new Vector4(.5f,0,1,0));Scalar(c.root,"Amplitude",.2f);
                    bool pom=kind==ShaderGraphNodeKind.ParallaxOcclusionMapping;if(pom)Scalar(c.root,"Steps",32);
                    var view=new Vector3(.5f,0,1).normalized;float ray=.2f*view.x/(view.z+(pom?0:.42f));
                    float slope=Height(1,channel)-Height(0,channel),intercept=Height(0,channel);
                    c.expected=uv=>{float h=pom?Mathf.Clamp01((intercept+slope*(uv.x-.5f*ray))/(1-slope*ray)):Height(uv.x,channel);return new Color(uv.x+ray*(h-.5f),uv.y,0,1);};
                }
            }
        }
        if(batch==20) foreach(int channel in Enumerable.Range(0,5))
        {
            foreach(var kind in new[]{ShaderGraphNodeKind.SetAlpha,ShaderGraphNodeKind.MixColor,ShaderGraphNodeKind.Blend})
            {
                var c=Make(kind,"mask Channel/"+channel);c.root.channel=(ShaderGraphChannel)channel;
                string input=kind==ShaderGraphNodeKind.SetAlpha?"Alpha":kind==ShaderGraphNodeKind.MixColor?"T":"Opacity";
                c.Connect(ColorField(c),c.root,input);
                if(kind==ShaderGraphNodeKind.SetAlpha) c.expected=uv=>new Color(1,1,1,Height(uv.x,channel));
                else if(kind==ShaderGraphNodeKind.MixColor) {c.root.option=1;c.expected=uv=>Gray(Height(uv.x,channel));}
                else {Set(c.root,"Background",new Vector4(0,0,0,1));c.expected=uv=>Gray(Mathf.Clamp01(Height(uv.x,channel)));}
            }
            var scalar=Make(ShaderGraphNodeKind.SetAlpha,"scalar bypass/"+channel);scalar.root.channel=(ShaderGraphChannel)channel;
            scalar.Connect(X(scalar),scalar.root,"Alpha");scalar.expected=uv=>new Color(1,1,1,uv.x);
        }
        if(batch==21)
        {
            foreach(bool luminance in new[]{false,true}) foreach(int falloff in new[]{0,1})
            {
                var c=Make(ShaderGraphNodeKind.RangeMask,$"Input/{luminance}/{falloff}");c.root.rangeLuminance=luminance;c.root.option=falloff;
                c.Connect(ColorField(c),c.root,"In");Scalar(c.root,"Center",.5f);Scalar(c.root,"Width",.6f);Scalar(c.root,"Feather",.1f);
                float Mask(float value) {float x=Mathf.Clamp01((.3f-Mathf.Abs(value-.5f))/.1f);return falloff==0?x:x*x*(3-2*x);}
                c.expected=uv=>luminance?Gray(Mask(Height(uv.x,0))):new Color(Mask(uv.x),Mask(.25f+2*uv.x),Mask(1-uv.x),Mask(.6f));
            }
            foreach(bool luminance in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.RangeMask,"scalar/"+luminance);c.root.rangeLuminance=luminance;c.Connect(X(c),c.root,"In");
                c.expected=uv=>Gray(Mathf.Abs(uv.x-.5f)<=.1f?1:0);
            }
            foreach(var type in new[]{ShaderGraphValueType.Float2,ShaderGraphValueType.Float3,ShaderGraphValueType.Float4}) foreach(var channel in new[]{ShaderGraphChannel.Luminance,ShaderGraphChannel.Alpha})
            {
                var c=Make(ShaderGraphNodeKind.SetAlpha,$"vector/{type}/{channel}");c.root.channel=channel;
                var value=c.Node(ShaderGraphNodeKind.Combine);value.valueType=type;
                Scalar(value,"X",.1f);Scalar(value,"Y",.2f);Scalar(value,"Z",.3f);Scalar(value,"W",.4f);c.Connect(value,c.root,"Alpha");
                float expected=channel==ShaderGraphChannel.Alpha?(type==ShaderGraphValueType.Float4?.4f:1):.1f*.2126f+.2f*.7152f+(type==ShaderGraphValueType.Float2?0:.3f*.0722f);
                c.expected=_=>new Color(1,1,1,expected);
            }
            var math=Make(ShaderGraphNodeKind.Combine,"ordinary scalar cast");math.root.valueType=ShaderGraphValueType.Float2;
            math.Connect(ColorField(math),math.root,"X");math.expected=uv=>new Color(uv.x,0,0,1);
            var shared=Make(ShaderGraphNodeKind.Subtract,"same source different channels");var color=ColorField(shared);
            foreach(int channel in new[]{1,2})
            {
                var n=shared.Node(ShaderGraphNodeKind.HeightToDistortion);n.heightSource=ShaderGraphHeightSource.Value;n.inputs.RemoveAll(p=>p.port=="Height");n.channel=(ShaderGraphChannel)channel;
                shared.Connect(color,n,"Height");shared.Connect(n,shared.root,channel==1?"A":"B");
            }
            shared.expected=_=>new Color(-8f/Size,0,0,1);
        }
        if(batch==22)
        {
            foreach(var item in new[]{(ShaderGraphNodeKind.HeightCurvature,"Height","Curvature"),(ShaderGraphNodeKind.HeightToAO,"Height","Out"),
                (ShaderGraphNodeKind.HeightShadow,"Height","Out"),(ShaderGraphNodeKind.SlopeBlur,"Slope","Out"),
                (ShaderGraphNodeKind.FadeTransition,"NoiseValue","Out"),(ShaderGraphNodeKind.HeightBlend,"Bottom","Height"),
                (ShaderGraphNodeKind.Scatter,"Mask","Out"),(ShaderGraphNodeKind.Scatter,"Density","Out")})
            foreach(int channel in new[]{0,3})
            {
                var c=Make(ShaderGraphNodeKind.Subtract,$"explicit selection/{item}/{channel}");c.nativeOnly=item.Item1 is ShaderGraphNodeKind.SlopeBlur or ShaderGraphNodeKind.Scatter;
                ShaderGraphNode color;
                if(item.Item1==ShaderGraphNodeKind.HeightToAO)
                { color=c.Node(ShaderGraphNodeKind.SetAlpha);c.Connect(c.Node(ShaderGraphNodeKind.UV),color,"Color");Scalar(color,"Alpha",.6f); }
                else color=ColorField(c);
                var a=c.Node(item.Item1);var b=c.Node(item.Item1);a.channel=(ShaderGraphChannel)channel;b.channel=ShaderGraphChannel.Alpha;
                if(item.Item1==ShaderGraphNodeKind.HeightToAO) {Scalar(a,"Directions",2);Scalar(b,"Directions",2);Scalar(a,"Steps",2);Scalar(b,"Steps",2);}
                if(item.Item1==ShaderGraphNodeKind.HeightShadow) {Scalar(a,"Steps",4);Scalar(b,"Steps",4);}
                if(item.Item1==ShaderGraphNodeKind.HeightBlend) {Scalar(a,"Top",.5f);Scalar(b,"Top",.5f);}
                if(item.Item1==ShaderGraphNodeKind.Scatter) {Scalar(a,"Source",1);Scalar(b,"Source",1);}
                if(item.Item1==ShaderGraphNodeKind.SlopeBlur) {c.Connect(Square(c),a,"In");c.Connect(Square(c),b,"In");}
                c.Connect(color,a,item.Item2);var explicitChannel=Extract(c,color,channel,out var output);
                c.Connect(explicitChannel,b,item.Item2,output);c.Connect(a,c.root,"A",item.Item3);c.Connect(b,c.root,"B",item.Item3);c.expected=_=>Gray(0);
            }
        }
        if(batch==23)
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(texture);
            var pixels=new Color[Size*Size];for(int i=0;i<pixels.Length;i++){float x=(i%Size+.5f)/Size;pixels[i]=new Color(x,.25f+2*x,1-x,.6f);}texture.SetPixels(pixels);texture.Apply();
            foreach(var kind in new[]{ShaderGraphNodeKind.HeightToNormal,ShaderGraphNodeKind.HeightToDistortion,ShaderGraphNodeKind.ParallaxMapping,ShaderGraphNodeKind.ParallaxOcclusionMapping})
            foreach(var channel in new[]{ShaderGraphChannel.Luminance,ShaderGraphChannel.Green,ShaderGraphChannel.Alpha})
            {
                var c=Make(ShaderGraphNodeKind.Subtract,$"Texture/Value equivalent/{kind}/{channel}");
                var a=c.Node(kind);var b=c.Node(kind);a.channel=b.channel=channel;b.heightSource=ShaderGraphHeightSource.Value;
                a.inputs.RemoveAll(p=>p.port=="Height");b.inputs.RemoveAll(p=>p.port=="Height");
                void Bind(ShaderGraphNode node,string port)=>node.inputs.Add(new ShaderGraphInput {port=port,type=ShaderGraphValueType.Texture,resource=new ShaderParameter {type=ShaderParameterType.Texture2D,textureValue=texture}});
                Bind(a,"Height");a.wrap=ShaderGraphWrap.Clamp;
                var sample=c.Node(ShaderGraphNodeKind.SampleTexture);Bind(sample,"Texture");sample.wrap=ShaderGraphWrap.Clamp;
                c.Connect(sample,b,"Height");c.Connect(a,c.root,"A");c.Connect(b,c.root,"B");
                c.expected=_=>new Color(0,0,0,1);
            }
        }
        return cases;
    }
    static float TruchetDistance(Vector2 uv,float scale,float width,int shape)
    {
        uint Hash(uint x) { unchecked {x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);} }
        uint Bits(float x)=>(uint)BitConverter.SingleToInt32Bits(x==0?0:x);
        Vector2 p=uv*scale,cell=new Vector2(Mathf.Floor(p.x),Mathf.Floor(p.y)),f=p-cell;
        uint hash=Hash(Bits(cell.x)^Hash(Bits(cell.y))^Hash(Bits(1337)));
        if((hash>>8)<8388608)f.x=1-f.x;
        float distance=shape==0?Mathf.Min(Mathf.Abs(f.magnitude-.5f),Mathf.Abs((f-Vector2.one).magnitude-.5f)):
            Mathf.Min(Mathf.Abs(f.x+f.y-.5f),Mathf.Abs(f.x+f.y-1.5f))/Mathf.Sqrt(2);
        return distance-width*.5f;
    }
    static float ReferenceMask(Color color,Color key,float range,float fuzziness,int mode)
    {
        double[] Coordinates(Color c)
        {
            if(mode==0)return new double[]{c.r,c.g,c.b};
            double Cbrt(double x)=>Math.Sign(x)*Math.Pow(Math.Abs(x),1.0/3.0);
            double l=Cbrt(.4122214708*c.r+.5363325363*c.g+.0514459929*c.b);
            double m=Cbrt(.2119034982*c.r+.6806995451*c.g+.1073969566*c.b);
            double s=Cbrt(.0883024619*c.r+.2817188376*c.g+.6299787005*c.b);
            return new[]{mode==2?0:.2104542553*l+.7936177850*m-.0040720468*s,
                1.9779984951*l-2.4285922050*m+.4505937099*s,
                .0259040371*l+.7827717662*m-.8086757660*s};
        }
        var a=Coordinates(color);var b=Coordinates(key);double d=0;
        for(int i=0;i<3;i++)d+=(a[i]-b[i])*(a[i]-b[i]);
        d=Math.Sqrt(d);range=Math.Max(0,range);
        return fuzziness<=0?(d<=range?1:0):(float)Math.Clamp((range+fuzziness-d)/fuzziness,0,1);
    }
    static List<GpuCase> SignedNormalCases(int batch,List<Texture2D> owned)
    {
        var cases = new List<GpuCase>();
        GpuCase Make(ShaderGraphNodeKind kind, string label)
        {
            var c = new GpuCase { label = "Signed XYZ/" + kind + "/" + label };
            c.root = c.Node(kind); c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA"); cases.Add(c); return c;
        }
        Color XYZ(Vector3 n) => new Color(n.x,n.y,n.z,1);
        var signed = new Vector4(-.6f,.2f,.75f,0);
        var unit = ((Vector3)signed).normalized;
        if(batch == 30)
        {
            foreach(var kind in new[]{ShaderGraphNodeKind.HeightToNormal,ShaderGraphNodeKind.NormalStrength,ShaderGraphNodeKind.NormalBlend,ShaderGraphNodeKind.NormalReconstructZ})
            {
                var c=Make(kind,"neutral preview"); if(kind==ShaderGraphNodeKind.HeightToNormal)c.root.heightSource=ShaderGraphHeightSource.Value; c.expected=_=>new Color(0,0,1,1);
            }
            foreach(float strength in new[]{0f,1f,-2f})
            {
                var c=Make(ShaderGraphNodeKind.NormalStrength,"negative XY/"+strength);
                Set(c.root,"Normal",signed); Scalar(c.root,"Strength",strength);
                var expected=new Vector3(unit.x*strength,unit.y*strength,Mathf.Lerp(1,unit.z,Mathf.Clamp01(Mathf.Abs(strength)))).normalized;
                c.expected=_=>XYZ(expected);
            }
            foreach(int mode in new[]{0,1}) foreach(bool detail in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.NormalBlend,"neutral operand/"+mode+"/"+detail); c.root.option=mode;
                Set(c.root,detail?"Detail":"Base",signed); c.expected=_=>XYZ(unit);
            }
            foreach(int mode in new[]{0,1})
            {
                var c=Make(ShaderGraphNodeKind.NormalToDistortion,"raw XY/"+mode); c.root.option=mode;
                Set(c.root,"Normal",new Vector4(-2,.4f,3,0));Set(c.root,"Strength",new Vector4(.5f,2,0,0));Scalar(c.root,"FlipY",1);
                c.expected=_=>mode==0?new Color(-1,-.8f,0,1):new Color(0,.1f,0,1);
            }
            var encode=Make(ShaderGraphNodeKind.NormalEncode,"explicit packing");Set(encode.root,"Normal",signed);
            encode.expected=_=>XYZ((Vector3)signed*.5f+Vector3.one*.5f);
            var decode=Make(ShaderGraphNodeKind.NormalDecode,"explicit decoding");Set(decode.root,"Normal",signed*.5f+Vector4.one*.5f);decode.expected=_=>XYZ(signed);
            var round=Make(ShaderGraphNodeKind.NormalDecode,"encode decode roundtrip");var enc=round.Node(ShaderGraphNodeKind.NormalEncode);Set(enc,"Normal",signed);round.Connect(enc,round.root,"Normal");round.expected=_=>XYZ(signed);
            var unpack=Make(ShaderGraphNodeKind.NormalUnpack,"object packing");unpack.root.option=1;Set(unpack.root,"In",signed*.5f+Vector4.one*.5f);unpack.expected=_=>XYZ(signed);
            var tangent=Make(ShaderGraphNodeKind.NormalUnpack,"tangent packing");
            Set(tangent.root,"In",new Vector4(.65f,.3f,1,.8f));
        }
        else if(batch==32)
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};owned.Add(texture);
            var pixels=new Color[Size*Size];for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)pixels[y*Size+x]=new Color((x+.5f)/Size,(y+.5f)/Size,0,1);
            texture.SetPixels(pixels);texture.Apply();
            foreach(var derivative in new[]{ShaderGraphDerivative.Scharr,ShaderGraphDerivative.Sobel,ShaderGraphDerivative.CentralDifference}) foreach(bool flipY in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.HeightToNormal,"texture ramp/"+derivative+"/"+flipY);c.root.derivative=derivative;c.root.channel=ShaderGraphChannel.Green;
                Scalar(c.root,"FlipY",flipY?1:0);Set(c.root,"UV",new Vector4(.5f,.5f,0,0));
                c.root.inputs.Add(new ShaderGraphInput{port="Height",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});
                c.expected=_=>XYZ(new Vector3(0,flipY?.25f:-.25f,1).normalized);
            }
        }
        else
        {
            foreach(var derivative in new[]{ShaderGraphDerivative.Scharr,ShaderGraphDerivative.Sobel,ShaderGraphDerivative.CentralDifference}) foreach(bool flip in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.HeightToNormal,"ramp/"+derivative+"/"+flip);
                c.root.heightSource=ShaderGraphHeightSource.Value;c.root.derivative=derivative;Scalar(c.root,"FlipX",flip?1:0);Scalar(c.root,"Strength",8);
                c.Connect(X(c),c.root,"Height");c.expected=_=>XYZ(new Vector3(flip?.25f:-.25f,0,1).normalized);
            }
            var chain=Make(ShaderGraphNodeKind.NormalBlend,"height strength blend");
            var height=chain.Node(ShaderGraphNodeKind.HeightToNormal);height.heightSource=ShaderGraphHeightSource.Value;chain.Connect(X(chain),height,"Height");
            var strengthNode=chain.Node(ShaderGraphNodeKind.NormalStrength);Scalar(strengthNode,"Strength",2);chain.Connect(height,strengthNode,"Normal");chain.Connect(strengthNode,chain.root,"Detail");
            chain.expected=_=>XYZ(new Vector3(-.5f,0,1).normalized);
            foreach(bool global in new[]{false,true}) foreach(bool varied in new[]{false,true})
            {
                var c=Make(ShaderGraphNodeKind.NormalLighting,(global?"Global":"Local")+"/"+varied);c.root.option=global?1:0;c.hybrid=global;
                Scalar(c.root,"Ambient",.2f);Set(c.root,"Color",new Vector4(.2f,.4f,.8f,.35f));
                if(varied)Set(c.root,"Normal",signed);
                // Global is checked against both native and SVM surface preparation;
                // an owned directional light is installed by the runner for this suite.
                var light=new Vector3(-.5f,.5f,1).normalized;
                float lit=.2f+Mathf.Max(0,Vector3.Dot(varied?unit:Vector3.forward,light));
                c.expected=_=>new Color(.2f*lit,.4f*lit,.8f*lit,.35f);
            }
        }
        return cases;
    }

    static List<GpuCase> FilterQualityCases(int quality)
    {
        var cases = new List<GpuCase>();
        var t = new TestContext();
        foreach (var kind in new[] { ShaderGraphNodeKind.Blur, ShaderGraphNodeKind.Sharpen, ShaderGraphNodeKind.Dilate, ShaderGraphNodeKind.Erode })
        foreach (float radius in new[] { 0f, .25f, 1f, 1.25f, -1.25f, 4f })
        {
            var c = new GpuCase { label = $"{kind}/quality {quality}/radius {radius}", nativeOnly = true, tolerance = .0015f };
            var filter = c.Node(kind); c.root = filter;
            t.Equal(0, filter.option, kind + " Standard default");
            t.True(((string[])Static("ShaderGraphNodes", "ModeNames", kind)).SequenceEqual(new[] { "Standard", "High" }), kind + " quality discovery");
            t.Equal("Quality", (string)Static("ShaderGraphNodes", "ModeLabel", kind), kind + " label");
            filter.option = quality; Scalar(filter, "Radius", radius);
            if (radius == 1.25f)
            {
                var r = c.Node(ShaderGraphNodeKind.Value); r.literal.floatValue = radius;
                c.Connect(r, filter, "Radius");
            }
            var x = X(c); var square = c.Node(ShaderGraphNodeKind.Multiply); c.Connect(x, square, "A"); c.Connect(x, square, "B");
            var negative = c.Node(ShaderGraphNodeKind.Multiply); Scalar(negative, "B", -2); c.Connect(square, negative, "A");
            var phase = c.Node(ShaderGraphNodeKind.Multiply); Scalar(phase, "B", 2 * Mathf.PI * Size); c.Connect(x, phase, "A");
            var cosine = c.Node(ShaderGraphNodeKind.Cosine); c.Connect(phase, cosine, "In");
            var wave = c.Node(ShaderGraphNodeKind.Multiply); Scalar(wave, "B", 3); c.Connect(cosine, wave, "A");
            var twice = c.Node(ShaderGraphNodeKind.Multiply); Scalar(twice, "B", 2); c.Connect(x, twice, "A");
            var alpha = c.Node(ShaderGraphNodeKind.Subtract); Scalar(alpha, "B", .5f); c.Connect(twice, alpha, "A");
            var rgba = c.Node(ShaderGraphNodeKind.Combine); rgba.valueType = ShaderGraphValueType.Float4;
            c.Connect(square, rgba, "X"); c.Connect(negative, rgba, "Y"); c.Connect(wave, rgba, "Z"); c.Connect(alpha, rgba, "W");
            c.Connect(rgba, filter, "In");
            if (radius == 4)
            {
                var buffer = c.Node(ShaderGraphNodeKind.Buffer);
                buffer.bufferResolution = ShaderGraphBufferResolution.Custom; buffer.bufferSize = new Vector2Int(Size, Size);
                c.Connect(filter, buffer, "In");
                c.root = c.Node(ShaderGraphNodeKind.Add); c.Connect(buffer, c.root, "A");
                c.nativeOnly = false; c.hybrid = true;
            }
            c.Connect(c.root, c.Node(ShaderGraphNodeKind.Output), "RGBA");
            double rabs = Math.Abs(radius), extent = Math.Max(1, Math.Ceiling(rabs)) * (quality == 1 ? 2 : 1);
            double variance = 0, response = 0, weights = 0;
            // A separable kernel on a 1D polynomial has an exact variance identity;
            // the sinusoid's response is its weighted cosine transform.
            for (int i = -(int)extent; i <= extent; i++)
            {
                double d = i * rabs / extent / Size, w = Math.Exp(-2 * i * i / (extent * extent));
                weights += w; variance += d * d * w; response += Math.Cos(2 * Math.PI * Size * d) * w;
            }
            variance /= weights; response /= weights;
            Color Field(double u) => new Color((float)(u*u), (float)(-2*u*u), (float)(3*Math.Cos(2*Math.PI*Size*u)), (float)(2*u-.5));
            c.expected = uv =>
            {
                Color center = Field(uv.x);
                if (radius == 0) return center;
                if (kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen)
                {
                    Color blurred = new Color((float)(uv.x*uv.x+variance), (float)(-2*(uv.x*uv.x+variance)), (float)(center.b*response), center.a);
                    return kind == ShaderGraphNodeKind.Blur ? blurred : center * 2 - blurred;
                }
                var samples = Enumerable.Range(0, (int)extent*2+1).Select(i => Field(uv.x+(i-extent)*rabs/extent/Size)).ToArray();
                var value = new Color();
                for (int k=0;k<4;k++) value[k] = kind == ShaderGraphNodeKind.Dilate ? samples.Max(s=>s[k]) : samples.Min(s=>s[k]);
                return value;
            };
            cases.Add(c);
        }
        return cases;
    }

    static List<GpuCase> PathBlurQualityCases(int quality)
    {
        var cases=new List<GpuCase>();
        foreach(int mode in new[]{0,1,2}) foreach(float amount in new[]{0f,.25f,-1.25f,8f})
        {
            var c=new GpuCase{label=$"Path blur/{mode}/{quality}/{amount}",nativeOnly=true,tolerance=.0003f};
            var filter=c.Node(mode==0?ShaderGraphNodeKind.DirectionalBlur:ShaderGraphNodeKind.RadialBlur);c.root=filter;
            filter.option=mode==2?1:0;filter.filterQuality=(ShaderGraphFilterQuality)quality;
            if(amount==0)
            {
                var first=Static("ShaderGraphCompiler","CodeFingerprint",c.graph);
                filter.filterQuality=(ShaderGraphFilterQuality)(1-quality);
                if(first.Equals(Static("ShaderGraphCompiler","CodeFingerprint",c.graph)))throw new Exception("Path quality must invalidate compiler fingerprint");
                filter.filterQuality=(ShaderGraphFilterQuality)quality;
                if(Ports(filter).Contains("Samples"))throw new Exception("Removed Samples port still exposed");
                if(!Static("WhimTexApi","ShaderNodeSettings",filter).ToString().Contains("filterQuality"))throw new Exception("Quality missing from discovery");
            }
            string input=mode==0?"Radius":mode==1?"Angle":"Strength";
            float actual=mode==1?amount*20:mode==2?amount*.05f:amount;Scalar(filter,input,actual);
            if(mode==0)Scalar(filter,"Angle",31);
            if(amount==-1.25f){var value=c.Node(ShaderGraphNodeKind.Value);value.literal.floatValue=actual;c.Connect(value,filter,input);}
            var uv=c.Node(ShaderGraphNodeKind.UV);var split=c.Node(ShaderGraphNodeKind.Split);c.Connect(uv,split,"In");
            var square=c.Node(ShaderGraphNodeKind.Multiply);c.Connect(split,square,"A","X");c.Connect(split,square,"B","X");
            var cross=c.Node(ShaderGraphNodeKind.Multiply);c.Connect(split,cross,"A","X");c.Connect(split,cross,"B","Y");
            var signed=c.Node(ShaderGraphNodeKind.Subtract);c.Connect(split,signed,"A","Y");Scalar(signed,"B",.5f);
            var rgba=c.Node(ShaderGraphNodeKind.Combine);rgba.valueType=ShaderGraphValueType.Float4;
            c.Connect(square,rgba,"X");c.Connect(signed,rgba,"Y");Scalar(rgba,"Z",2);c.Connect(cross,rgba,"W");c.Connect(rgba,filter,"In");
            if(amount==8){var buffer=c.Node(ShaderGraphNodeKind.Buffer);c.Connect(filter,buffer,"In");c.root=c.Node(ShaderGraphNodeKind.Add);c.Connect(buffer,c.root,"A");c.nativeOnly=false;c.hybrid=true;}
            c.Connect(c.root,c.Node(ShaderGraphNodeKind.Output),"RGBA");
            c.expected=p=>
            {
                double px=(p.x-.5)*Size,py=(p.y-.5)*Size,a=mode==1?actual*Math.PI/180:Math.Abs(actual);
                double distance=mode==0?a:Math.Sqrt(px*px+py*py)*Math.Abs(a);
                int extent=(int)Math.Max(1,Math.Ceiling(distance))*(quality==1?2:1);double weight=0,r=0,g=0,alpha=0;
                for(int i=-extent;i<=extent;i++)
                {
                    double t=(double)i/extent,x=p.x,y=p.y;
                    if(mode==0){x+=Math.Cos(31*Math.PI/180)*a*t/Size;y+=Math.Sin(31*Math.PI/180)*a*t/Size;}
                    else if(mode==1){double cs=Math.Cos(a*t),sn=Math.Sin(a*t);x=.5+(cs*px-sn*py)/Size;y=.5+(sn*px+cs*py)/Size;}
                    else{x=.5+px*(1+a*t)/Size;y=.5+py*(1+a*t)/Size;}
                    double w=Math.Exp(-2*t*t);weight+=w;r+=x*x*w;g+=(y-.5)*w;alpha+=x*y*w;
                }
                return new Color((float)(r/weight),(float)(g/weight),2,(float)(alpha/weight));
            };
            cases.Add(c);
        }
        return cases;
    }

    public static async Task<object> RunChecked(int batch)
    {
        try { return await RunBatch(batch); }
        catch(Exception error) { return TestContext.Result("failed",0,"Image filter checks",error.ToString()).ToJson(); }
    }
    static void ModelChecks(TestContext t,List<GpuCase> cases)
    {
        var document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;
        t.True(typeof(ShaderGraphNode).GetField("normalEncoding")==null,"No per-node normal encoding setting");
        try {
            var stored=cases.Where(c=>!c.graph.nodes.Any(n=>n.inputs.Any(v=>v.resource?.textureValue!=null))).ToList();
            foreach(var c in stored) AddLayer(document,c.graph);
            string json=WhimTexDocumentJson.Write(document).Json;
            t.True(!json.Contains("normalEncoding"),"Stored graph has no encoding field");
            using var loaded=WhimTexDocumentJson.Read(json,false);
            t.Equal(stored.Count,loaded.Document.layers.Count,"JSON layers");
            for(int i=0;i<stored.Count;i++) {
                var before=stored[i].root;var after=((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph.FindNode(before.id);
                foreach(var node in stored[i].graph.nodes)
                {
                    var restored=((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph.FindNode(node.id);
                    t.Equal(node.channel,restored.channel,"JSON source channel");
                    t.Equal(node.rangeLuminance,restored.rangeLuminance,"JSON Range Mask input mode");
                }
                t.Equal(before.paletteKeys,after.paletteKeys,"JSON palette source");
                foreach(var node in stored[i].graph.nodes.Where(n=>n.kind==ShaderGraphNodeKind.Truchet))
                {
                    var restored=((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph.FindNode(node.id);
                    t.Near(node.FindInput("Feather").value.x,restored.FindInput("Feather").value.x,0,"JSON Truchet Feather");
                    t.True(((string[])Static("ShaderGraphNodes","Outputs",restored)).SequenceEqual(new[]{"Out","Distance"}),"Truchet outputs");
                }
                if(before.kind==ShaderGraphNodeKind.PaletteQuantize)
                {
                    t.Equal(!before.paletteKeys,(bool)Static("ShaderGraphNodes","InputVisible",after,"Colors"),"Palette Colors visibility");
                    t.Equal(stored[i].graph.edges.Count,((ShaderLayerBehaviour)loaded.Document.layers[i].Behaviour).draftGraph.edges.Count,"Inactive wire retained");
                }
                t.Equal(before.filterQuality,after.filterQuality,"JSON path blur quality");
                t.Equal(before.adaptiveSharpen,after.adaptiveSharpen,"JSON adaptive sharpen");t.Equal(before.kind,after.kind,"JSON kind");t.Equal(before.option,after.option,"JSON quality");t.True(Ports(before).SequenceEqual(Ports(after)),"JSON ports");
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

    public static object RunIntegration() => Integration(false);
    public static object RunSoftSignIntegration() => Integration(false,false,true);
    public static object RunSampleInputIntegration() => Integration(false,inputChannels:true);
    public static object RunColorIntegration() => Integration(true);
    public static string RunNormalModel() => TestContext.Run("Signed normal defaults, discovery, storage and editing workload",t=>
    {
        foreach(var kind in new[]{ShaderGraphNodeKind.HeightToNormal,ShaderGraphNodeKind.NormalBlend,ShaderGraphNodeKind.NormalStrength,ShaderGraphNodeKind.NormalToDistortion,ShaderGraphNodeKind.NormalLighting,ShaderGraphNodeKind.NormalCurvature})
        {
            var node=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,Vector2.zero);
            var settings=Static("WhimTexApi","ShaderNodeSettings",node);
            t.True(!settings.ToString().Contains("normalEncoding"),kind+" discovery has no encoding");
            foreach(var input in node.inputs.Where(p=>p.port is "Normal" or "Base" or "Detail"))
                t.Equal(new Vector4(0,0,1,0),input.value,kind+" signed neutral "+input.port);
            if(kind==ShaderGraphNodeKind.NormalLighting)
            {
                node.option=1;
                var ports=((IEnumerable)Static("ShaderGraphNodes","StoredInputs",node)).Cast<object>().ToList();
                t.Equal(new Vector4(0,0,1,0),(Vector4)Get(ports.Single(p=>(string)Get(p,"name")=="Normal"),"value"),"Global signed neutral");
                t.True(ports.Any(p=>(string)Get(p,"name")=="LightDirection")&&ports.Any(p=>(string)Get(p,"name")=="Height"),"Both lighting layouts remain stored");
            }
        }
        string path="Packages/com.dcfapixels.whimtex/Tests~/Fixtures/ShaderGraphEditing/";
        byte[] bytes=System.IO.File.ReadAllBytes(path+"graph.json");
        string manifest=System.IO.File.ReadAllText(path+"manifest.json");
        string hash=System.Text.RegularExpressions.Regex.Match(manifest,@"""graph\.json""\s*:\s*""([^""]+)""").Groups[1].Value;
        using(var sha=System.Security.Cryptography.SHA256.Create())
            t.Equal(hash,BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(),"Workload provenance hash");
        object Parse(string text)=>Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json",true).GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{text});
        var graph=(ShaderGraph)Static("WhimTexDocumentJson","ReadShaderGraph",Parse(System.Text.Encoding.UTF8.GetString(bytes)));
        t.Equal(37,graph.nodes.Count,"Workload node count");t.Equal(42,graph.edges.Count,"Workload connections");
        var snapshot=Static("WhimTexDocumentJson","ShaderGraphSnapshot",graph,null);
        var restored=(ShaderGraph)Static("WhimTexDocumentJson","ReadShaderGraph",snapshot);
        t.Equal(graph.nodes.Count,restored.nodes.Count,"Current graph roundtrip");
        var inject=new System.Text.RegularExpressions.Regex(@"(""nodes""\s*:\s*\[\s*\{)");
        var obsolete=Parse(inject.Replace(snapshot.ToString(),"$1\"normalEncoding\":\"PackedRGB\",",1));
        bool rejected=false;
        try {Static("WhimTexDocumentJson","ReadShaderGraph",obsolete);}
        catch(TargetInvocationException e){rejected=e.InnerException is WhimTexDocumentException&&e.InnerException.Message.Contains("normalEncoding");}
        t.True(rejected,"Removed encoding receives an explicit unknown-field error");
    });
    public static object RunNormalIntegration()
    {
        try { return Integration(false,signedNormals:true); }
        catch(Exception error) { return TestContext.Result("failed",0,"Signed normal integration",error.ToString()).ToJson(); }
    }
    public static object RunControlsIntegration() => Integration(false,true);
    public static object RunKuwaharaIntegration()
    {
        try { return Integration(false, kuwaharaTables:true); }
        catch(Exception error) { return TestContext.Result("failed",0,"Kuwahara integration",error.ToString()).ToJson(); }
    }
    public static object RunKuwaharaAnisotropicIntegration()
    {
        try { return Integration(false, anisotropicKuwahara:true); }
        catch(Exception e) { return TestContext.Result("failed",0,"Anisotropic integration",e.ToString()).ToJson(); }
    }
    public static object RunMedianIntegration()
    {
        try { return Integration(false, medianDense:true); }
        catch(Exception e) { return TestContext.Result("failed",0,"Median integration",e.ToString()).ToJson(); }
    }
    public static object RunFilterQualityIntegration() => Integration(false, filterQuality:true);
    public static object RunPathBlurIntegration() => Integration(false, pathBlur:true);
    static object Integration(bool colorComparison, bool controls=false, bool softSign=false, bool inputChannels=false, bool signedNormals=false, bool kuwaharaTables=false, bool anisotropicKuwahara=false, bool medianDense=false, bool filterQuality=false, bool pathBlur=false)
    {
        var t = new TestContext();
        var c=new GpuCase();var noise=c.Node(ShaderGraphNodeKind.PerlinNoise);var ss=c.Node(ShaderGraphNodeKind.Supersample);
        var tile=c.Node(ShaderGraphNodeKind.MakeTileable);var palette=c.Node(ShaderGraphNodeKind.PaletteQuantize);
        var flow=c.Node(ShaderGraphNodeKind.FlowWarp);Set(flow,"Flow",new Vector4(.2f,-.1f,0,0));
        var pattern=c.Node(ShaderGraphNodeKind.Truchet);Scalar(pattern,"Scale",4);Scalar(pattern,"Width",.35f);c.Connect(flow,pattern,"UV");
        var median=c.Node(ShaderGraphNodeKind.Median);c.Connect(pattern,median,"In");
        var kuwahara=c.Node(ShaderGraphNodeKind.Kuwahara);c.Connect(median,kuwahara,"In");
        var sharpen=c.Node(ShaderGraphNodeKind.Sharpen);sharpen.adaptiveSharpen=true;c.Connect(kuwahara,sharpen,"In");
        if(controls)
        {
            Scalar(pattern,"Feather",.3f);
            var green=c.Node(ShaderGraphNodeKind.Multiply);Set(green,"B",new Vector4(0,1,0,1));green.FindInput("B").type=ShaderGraphValueType.Float4;c.Connect(pattern,green,"A");
            var range=c.Node(ShaderGraphNodeKind.RangeMask);range.rangeLuminance=true;c.Connect(green,range,"In");
            c.graph.edges.RemoveAll(e=>e.inputNode==median.id&&e.inputPort=="In");c.Connect(range,median,"In");
            c.Connect(median,tile,"In");Scalar(tile,"BlendWidth",.15f);c.Connect(tile,palette,"Color");palette.paletteKeys=true;
        }
        else if(softSign)
        {
            var soft=c.Node(ShaderGraphNodeKind.SoftSign);c.Connect(median,soft,"In");
            var inverse=c.Node(ShaderGraphNodeKind.InverseSoftSign);c.Connect(soft,inverse,"In");c.Connect(inverse,palette,"Color");
        }
        else c.Connect(median,palette,"Color");
        palette.option=1;
        if (colorComparison)
        {
            foreach (var kind in new[] { ShaderGraphNodeKind.ColorMask, ShaderGraphNodeKind.ReplaceColor, ShaderGraphNodeKind.PaletteQuantize })
            {
                var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
                t.Equal(1, n.option, kind + " defaults to Perceptual");
                var names = (string[])Static("ShaderGraphNodes", "ModeNames", kind);
                t.True(names.SequenceEqual(kind == ShaderGraphNodeKind.PaletteQuantize ?
                    new[] { "Linear", "Perceptual" } : new[] { "Linear", "Perceptual", "Chroma" }), kind + " distance glossary");
                if (kind != ShaderGraphNodeKind.PaletteQuantize)
                {
                    t.Near(.1f,n.FindInput("Range").value.x,0,"Range default");
                    t.Near(.05f,n.FindInput("Fuzziness").value.x,0,"Fuzziness default");
                }
            }
            object[] outputType = { ShaderGraphNodeKind.ReplaceColor, ShaderGraphValueType.Color, "Mask" };
            t.True((bool)Static("ShaderGraphNodes","TryUtilityOutputType",outputType),"Replace Mask type available");
            t.Equal(ShaderGraphValueType.Float,(ShaderGraphValueType)outputType[1],"Replace Mask is Float");
            var mask=c.Node(ShaderGraphNodeKind.ColorMask);c.Connect(palette,mask,"Color");
            var replace=c.Node(ShaderGraphNodeKind.ReplaceColor);c.Connect(palette,replace,"Color");
            c.Connect(mask,replace,"Fuzziness");Set(replace,"From",Vector4.one);
            Set(replace,"To",new Vector4(.2f,.4f,.7f,.01f));
            c.Connect(replace,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(inputChannels)
        {
            var sample=c.Node(ShaderGraphNodeKind.SampleInput);var combine=c.Node(ShaderGraphNodeKind.Combine);combine.valueType=ShaderGraphValueType.Float4;
            for(int k=0;k<4;k++)c.Connect(sample,combine,new[]{"X","Y","Z","W"}[k],new[]{"R","G","B","A"}[k]);
            c.Connect(combine,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(signedNormals)
        {
            var height=c.Node(ShaderGraphNodeKind.HeightToNormal);height.heightSource=ShaderGraphHeightSource.Value;Scalar(height,"Strength",32);c.Connect(Square(c),height,"Height");
            var strength=c.Node(ShaderGraphNodeKind.NormalStrength);Scalar(strength,"Strength",2);c.Connect(height,strength,"Normal");
            var blend=c.Node(ShaderGraphNodeKind.NormalBlend);c.Connect(strength,blend,"Detail");c.Connect(blend,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(medianDense)
        {
            Scalar(pattern,"Scale",2);Scalar(median,"Radius",4);
            var radius=c.Node(ShaderGraphNodeKind.Value);radius.literal.floatValue=4;c.Connect(radius,median,"Radius");
            c.Connect(median,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(kuwaharaTables || anisotropicKuwahara)
        {
            Scalar(pattern,"Scale",2);Scalar(kuwahara,"Radius",6);
            if(anisotropicKuwahara) { kuwahara.option=1;Scalar(kuwahara,"Sharpness",.6f); }
            c.Connect(kuwahara,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(pathBlur)
        {
            var directional=c.Node(ShaderGraphNodeKind.DirectionalBlur);Scalar(directional,"Radius",3.5f);Scalar(directional,"Angle",31);directional.filterQuality=ShaderGraphFilterQuality.High;c.Connect(pattern,directional,"In");
            var radial=c.Node(ShaderGraphNodeKind.RadialBlur);Scalar(radial,"Angle",12);radial.filterQuality=ShaderGraphFilterQuality.High;c.Connect(directional,radial,"In");
            c.Connect(radial,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else if(filterQuality)
        {
            var blur=c.Node(ShaderGraphNodeKind.Blur);blur.option=1;Scalar(blur,"Radius",1.25f);
            c.Connect(pattern,blur,"In");c.Connect(blur,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        }
        else c.Connect(palette,c.Node(ShaderGraphNodeKind.Output),"RGBA");
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags=HideFlags.HideAndDontSave; doc.width=doc.height=64;
        Layer layer=AddLayer(doc,c.graph);
        Texture2D inputImage=null;
        if(inputChannels)
        {
            inputImage=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            inputImage.SetPixels(new[]{new Color(.1f,.2f,.3f,1),new Color(.9f,.4f,.1f,1),new Color(.2f,.8f,.6f,1),new Color(.8f,.6f,.9f,1)});inputImage.Apply();
            var shader=(ShaderLayerBehaviour)layer.Behaviour;shader.inputMode=ShaderLayerInputMode.Texture;shader.inputTexture=inputImage;
        }
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
            if(signedNormals)t.True(reference.Min(p=>p.r)<-.2f,"Signed normal components survive composition without packing");
            t.True(reference.Max(p=>p.r)-reference.Min(p=>p.r)>.2f,"Nonuniform image filters output");
            var thumb=(Texture2D)Call(typeof(WhimTexDocument),doc,"GetLayerThumbnail",layer,64,false);
            t.True(thumb!=null&&thumb.GetPixels().All(p=>float.IsFinite(p.r)),"Finite thumbnail");
            var thumbPixels=thumb.GetPixels();
            t.True(thumbPixels.Max(p=>signedNormals?p.b:p.r)-thumbPixels.Min(p=>signedNormals?p.b:p.r)>.2f,"Thumbnail retains visible detail");
            if(signedNormals)t.True(thumbPixels.Max(p=>p.r)<.001f,"Negative normal X displays black without packing");
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
            return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message=(signedNormals ? "Signed XYZ normals " : colorComparison ? "Shared color comparison defaults, types, glossary, " : "Image filters ")+"composite, both group modes, hidden Layer input, clipping, thumbnail and raster export; render state restored."};
        }
        finally
        {
            RenderTexture.active=old;GL.sRGBWrite=srgb;
            if(exported!=null)Object.DestroyImmediate(exported);Object.DestroyImmediate(pixels);sentinel.Release();Object.DestroyImmediate(sentinel);Object.DestroyImmediate(doc);
            if(inputImage!=null)Object.DestroyImmediate(inputImage);
        }
    }
    public static async Task<object> RunBatch(int suite)
    {
        var t = new TestContext();
        var owned=new List<Texture2D>();
        List<GpuCase> cases = null;
        int budgetSeconds = 50;
        var timer = Stopwatch.StartNew();
        var previousActive = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        WhimTexDocument document = null; RenderTexture target = null; Texture2D pixels = null;
        float maxError = 0, maxDifference = 0;
        try
        {
            cases = Cases(suite,owned);
            ModelChecks(t, cases);
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = Size;
            if(suite>=30) document.lights.Add(new WhimTexLight());
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
                owner.inputMode=c.inputImage!=null&&!c.noInput?ShaderLayerInputMode.Texture:ShaderLayerInputMode.None;owner.inputTexture=c.inputImage;
                using var request = (IDisposable)Activator.CreateInstance(requestType, BindingFlags.Instance | F, null,
                    new object[] { c.graph, c.root.id, c.port, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                object[] completed = { null };
                await Until(() => (bool)Call(requestType, request, "TryComplete", completed), c.label + " HLSL");
                using var runtime = (IDisposable)completed[0]; var rt = runtime.GetType();
                CleanDiagnostics(t, (IEnumerable)Get(runtime, "DiagnosticMessages"), c.label + " HLSL");
                Call(rt, runtime, "Bind", c.graph, context, null, true, false);
                Call(rt, runtime, "PrepareCoordinates", layer, context);
                GL.sRGBWrite = false; Call(rt, runtime, "Render", c.inputImage??Texture2D.blackTexture, target, context);
                Color[] native = Read();
                if(c.nativeOnly)
                {
                    object[] build={c.graph,c.root.id,c.port,null};
                    t.True(!(bool)Call(vmType,null,"TryCreate",build),c.label+" retains native fallback for unsupported replay");
                    for(int y=0;y<Size;y++) for(int x=0;x<Size;x++) for(int channel=0;channel<4;channel++)
                    {
                        float value=native[y*Size+x][channel];
                        t.True(float.IsFinite(value),c.label+" finite fallback pixels");
                        if(c.expected!=null)t.Near(c.expected(new Vector2((x+.5f)/Size,(y+.5f)/Size))[channel],value,c.tolerance,c.label+" independent fallback pixels");
                    }
                    continue;
                }
                IDisposable svm;
                if (c.hybrid)
                    svm = (IDisposable)Call(vmType, null, "BeginHybrid", c.graph, c.root.id, c.port, false, false, false);
                else
                {
                    object[] build = { c.graph, c.root.id, c.port, null };
                    bool built=(bool)Call(vmType, null, "TryCreate", build);
                    t.True(built, c.label + " pure SVM build: " + vmType.GetProperty("LastFallbackReason",F|BindingFlags.Static)?.GetValue(null)); svm = (IDisposable)build[3];
                }
                using (svm)
                {
                    t.True(svm != null, c.label + " preview VM");
                    if(c.imageSamples>=0)t.Equal(c.imageSamples,((IEnumerable)Get(svm,"instructions")).Cast<object>().Count(op=>(int)Get(op,"op")==41),c.label+" shared image sample");
                    Call(vmType, svm, "ConfigureHybrid", document, layer, false, true);
                    await Until(() => {
                        bool ready = (bool)Call(vmType, svm, "UpdatePreparation");
                        if ((bool)Get(svm, "Failed"))
                        {
                            var reason = vmType.GetProperty("LastFallbackReason", F | BindingFlags.Static)?.GetValue(null);
                            var failedMaterial=(Material)Get(svm,"material");
                            if(failedMaterial!=null) CleanDiagnostics(t,(IEnumerable)Static("ShaderDiagnostics","ReadCompiler",failedMaterial.shader),c.label+" failed SVM");
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
                    if(c.afterFirstRender!=null)
                    {
                        c.afterFirstRender();
                        Call(rt,runtime,"Bind",c.graph,context,null,true,false);
                        Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                        foreach(var value in Read())for(int k=0;k<4;k++)t.Near(c.updatedColor[k],value[k],.00001,c.label+" live HLSL keys");
                        t.True((bool)Call(vmType,svm,"RenderBoundaries",target,context),c.label+" live SVM render");
                        foreach(var value in Read())for(int k=0;k<4;k++)t.Near(c.updatedColor[k],value[k],.00001,c.label+" live SVM keys");
                    }
                    for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                    {
                        int sample = y * Size + x;
                        if(c.coverageBounds!=null)
                        {
                            var bounds=c.coverageBounds(new Vector2((x+.5f)/Size,(y+.5f)/Size));
                            t.True(native[sample].r>=bounds.x-.0001f&&native[sample].r<=bounds.y+.0001f,c.label+" independent AA coverage");
                        }
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
        return new TestResult {status="passed",checks=t.Checks,failures=Array.Empty<string>(), message=$"{cases.Count} image filter GPU cases ({cases.Count(c=>!c.nativeOnly&&!c.hybrid)} pure SVM, {cases.Count(c=>c.hybrid&&!c.nativeOnly)} hybrid, {cases.Count(c=>c.nativeOnly)} native fallback), JSON, clean diagnostics. Max error {maxError:R}, max difference {maxDifference:R}."};
    }
}
