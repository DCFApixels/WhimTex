using System;
using System.Threading.Tasks;
using System.Linq;
using DCFApixels.WhimTex;
using UnityEngine;

public static partial class ShaderGraphEditingBench
{
    public static string SvmImageWindowChecks(string id) => RunSvmWindowChecks(id,false,imageChecks:1);
    public static string SvmFxImageWindowChecks(string id) => RunSvmWindowChecks(id,false,imageChecks:2);
    sealed class SvmImageWindowFixture : IDisposable
    {
        readonly Fixture fixture;
        readonly ShaderFX effect;
        internal SvmImageWindowFixture(Fixture fixture,bool fx)
        {
            this.fixture=fixture;var graph=new ShaderGraph();fixture.behaviour.draftGraph=graph;
            ShaderGraphNode Add(ShaderGraphNodeKind kind) {
                var n=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,new Vector2(graph.nodes.Count%3*340,graph.nodes.Count/3*380));graph.nodes.Add(n);return n;
            }
            Add(ShaderGraphNodeKind.SampleTexture);Add(ShaderGraphNodeKind.SampleInput);
            Add(ShaderGraphNodeKind.HeightToNormal);Add(ShaderGraphNodeKind.HeightToDistortion);
            var size=Add(ShaderGraphNodeKind.TextureSize);var math=Add(ShaderGraphNodeKind.Add);Connect(graph,size.id,"Texel",math.id,"A");
            Add(ShaderGraphNodeKind.Output);
            fixture.behaviour.inputMode=ShaderLayerInputMode.Texture;fixture.behaviour.inputTexture=Texture2D.whiteTexture;
            if(fx) {
                effect=ScriptableObject.CreateInstance<ShaderFX>();effect.hideFlags=HideFlags.HideAndDontSave;
                var program=Get(effect,"Program");Set(program,"mode",ShaderProgramMode.Graph);Set(program,"draftGraph",graph);
                ((Layer)fixture.behaviour).fx.Add(effect);
                fixture.behaviour.draftGraph=new ShaderGraph();fixture.behaviour.draftGraph.nodes.Add((ShaderGraphNode)Static("ShaderGraphNodes","Create",ShaderGraphNodeKind.Output,Vector2.zero));
                Set(fixture.window,"effect",effect);Set(fixture.window,"effectTarget",true);Set(fixture.window,"target",null);
            }
        }
        public void Dispose() {if(effect!=null) {fixture.window.Close();((Layer)fixture.behaviour).fx.Remove(effect);UnityEngine.Object.DestroyImmediate(effect);}}
    }

    public static string SvmImageChecks(string id) => RunSvmNodeChecks(id,false,remaining:8);
    public static string SvmTextureHeightChecks(string id) => RunSvmNodeChecks(id,false,remaining:9);
    public static string SvmEffectImageChecks(string id) => RunSvmNodeChecks(id,false,remaining:11);
    public static string SvmInputChecks(string id) => RunSvmNodeChecks(id,false,remaining:10);
    static async Task CheckImageSvm(int suite, Fixture fixture, ShaderGraph graph,
        Func<ShaderGraphNodeKind,ShaderGraphNode> Add, Action<ShaderGraphNode,string,Vector4> Value,
        Func<string,ShaderGraphNode,string,bool,Task> Check)
    {
        Task C(string label,ShaderGraphNode n,string port="Out")=>Check(label,n,port,false);
        var texture=new Texture2D(13,9,TextureFormat.RGBAFloat,false,true) {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Mirror};
        var pixels=new Color[117];
        for(int y=0;y<9;y++) for(int x=0;x<13;x++) pixels[y*13+x]=new Color(Mathf.Sin(x*.7f)*1.4f,(x+y)%4*.43f,Mathf.Cos(y*.9f),x/12f);
        texture.SetPixels(pixels);texture.Apply();
        void Resource(ShaderGraphNode n,string port,ShaderParameter p)
        {
            var input=n.FindInput(port);
            if(input==null) {input=new ShaderGraphInput{port=port,type=ShaderGraphValueType.Texture};n.inputs.Add(input);}
            input.type=ShaderGraphValueType.Texture;
            input.resource=new ShaderParameter {type=ShaderParameterType.Texture2D,textureValue=p.textureValue};
            if(p.textureSource!=ShaderTextureSource.Texture) {
                if(!graph.parameters.Contains(p)) graph.parameters.Add(p);
                var pn=graph.nodes.FirstOrDefault(n=>n.kind==ShaderGraphNodeKind.Parameter&&n.parameterId==p.id);
                if(pn==null){pn=Add(ShaderGraphNodeKind.Parameter);pn.parameterId=p.id;}
                graph.edges.RemoveAll(e=>e.inputNode==n.id&&e.inputPort==port);Connect(graph,pn.id,"Out",n.id,port);
            }
        }
        var parameter=new ShaderParameter {name="Image",type=ShaderParameterType.Texture2D,textureValue=texture};
        try
        {
            var uv=Add(ShaderGraphNodeKind.Multiply);var coordinates=Add(ShaderGraphNodeKind.UV);
            Connect(graph,coordinates.id,"Out",uv.id,"A");Value(uv,"B",new Vector4(3.3f,2.3f,0,0));uv.FindInput("B").type=ShaderGraphValueType.Float2;
            if(suite==8)
            {
                var n=Add(ShaderGraphNodeKind.SampleTexture);Resource(n,"Texture",parameter);Connect(graph,uv.id,"Out",n.id,"UV");
                foreach(ShaderGraphWrap wrap in Enum.GetValues(typeof(ShaderGraphWrap)))
                foreach(ShaderGraphFilter filter in Enum.GetValues(typeof(ShaderGraphFilter)))
                {n.wrap=wrap;n.filter=filter;await C("Texture "+wrap+" "+filter,n);}
                foreach(string port in new[]{"R","G","B","A"})await C("Texture channel "+port,n,port);
                var size=Add(ShaderGraphNodeKind.TextureSize);Resource(size,"Texture",parameter);
                await C("Texture dimensions",size,"Size");await C("Texture texel",size,"Texel");
                var pn=Add(ShaderGraphNodeKind.Parameter);pn.parameterId=parameter.id;graph.parameters.Add(parameter);
                var rr=Add(ShaderGraphNodeKind.Reroute);Connect(graph,pn.id,"Out",rr.id,"In");Connect(graph,rr.id,"Out",n.id,"Texture");
                await C("Texture parameter through reroute",n);
                graph.edges.RemoveAll(e=>e.inputNode==n.id&&e.inputPort=="Texture");
                Resource(n,"Texture",new ShaderParameter {type=ShaderParameterType.Texture2D});await C("Unassigned white texture",n);
                parameter.textureSource=ShaderTextureSource.None;Resource(n,"Texture",parameter);await C("None resource is transparent",n);
                parameter.textureSource=ShaderTextureSource.Texture;
                var buffer=Add(ShaderGraphNodeKind.Buffer);Connect(graph,uv.id,"Out",buffer.id,"In");
                var sum=Add(ShaderGraphNodeKind.Add);Connect(graph,n.id,"Out",sum.id,"A");Connect(graph,buffer.id,"Out",sum.id,"B");
                await C("Texture and native Buffer use independent slots",sum);
                var light=Add(ShaderGraphNodeKind.NormalLighting);light.option=1;
                graph.edges.RemoveAll(e=>e.inputNode==sum.id&&e.inputPort=="B");Connect(graph,light.id,"Out",sum.id,"B");
                await C("Texture and exact Global Lighting slots",sum);
                var mapped=Add(ShaderGraphNodeKind.GradientMap);Connect(graph,n.id,"Out",mapped.id,"Color");
                await C("Texture and palette samplers",mapped);
                var f=Add(ShaderGraphNodeKind.Fractal);Connect(graph,n.id,"Out",f.id,"In");Value(f,"Octaves",Vector4.one*2);await C("Texture inside Fractal",f);
            }
            else if(suite==9)
            {
                foreach(var kind in new[]{ShaderGraphNodeKind.HeightToNormal,ShaderGraphNodeKind.HeightToDistortion})
                {
                    var n=Add(kind);n.heightSource=ShaderGraphHeightSource.Texture;Resource(n,"Height",parameter);Connect(graph,uv.id,"Out",n.id,"UV");
                    Value(n,"Strength",Vector4.one*2.3f);Value(n,"Radius",Vector4.one*1.4f);Value(n,"FlipX",Vector4.one);Value(n,"FlipY",Vector4.one);
                    foreach(ShaderGraphDerivative derivative in Enum.GetValues(typeof(ShaderGraphDerivative)))
                    {n.derivative=derivative;await C(kind+" "+derivative,n);}
                    foreach(ShaderGraphChannel channel in Enum.GetValues(typeof(ShaderGraphChannel)))
                    {n.channel=channel;n.wrap=ShaderGraphWrap.Repeat;await C(kind+" channel "+channel,n);}
                    n.option=1;n.filter=ShaderGraphFilter.Point;
                    foreach(float radius in new[]{0f,-1f,.3f}) {Value(n,"Radius",Vector4.one*radius);await C(kind+" radius "+radius,n);}
                    Value(n,"Radius",Vector4.one);
                    var f=Add(ShaderGraphNodeKind.Fractal);Connect(graph,n.id,"Out",f.id,"In");Value(f,"Octaves",Vector4.one*2);await C(kind+" inside Fractal",f);
                }
            }
            else if(suite==11)
            {
                fixture.behaviour.inputTexture=texture;
                var n=Add(ShaderGraphNodeKind.SampleInput);Connect(graph,uv.id,"Out",n.id,"UV");
                await C("FX Sample Input",n);
                var sample=Add(ShaderGraphNodeKind.SampleTexture);parameter.textureSource=ShaderTextureSource.Self;Resource(sample,"Texture",parameter);
                Connect(graph,uv.id,"Out",sample.id,"UV");await C("FX Self texture",sample);
                var sum=Add(ShaderGraphNodeKind.Add);Connect(graph,n.id,"Out",sum.id,"A");Connect(graph,sample.id,"Out",sum.id,"B");await C("FX Self and Input",sum);
                Set(fixture.document,"renderingViewport",true);
                var region=ProjectiveMatrix.Identity;region.m00=.53;region.m11=.47;region.m02=.2;region.m12=.17;
                Set(fixture.document,"viewportToCanvas",region);
                await C("FX cropped input logical dimensions",sum);
                Set(fixture.document,"renderingViewport",false);
            }
            else
            {
                var n=Add(ShaderGraphNodeKind.SampleInput);Connect(graph,uv.id,"Out",n.id,"UV");
                fixture.behaviour.inputMode=ShaderLayerInputMode.None;await C("Sample Input None",n);
                fixture.behaviour.inputMode=ShaderLayerInputMode.Texture;fixture.behaviour.inputTexture=texture;
                foreach(ShaderGraphWrap wrap in Enum.GetValues(typeof(ShaderGraphWrap)))
                {n.wrap=wrap;await C("Sample Input Texture "+wrap,n);}

                var source=new ColorFillLayerBehaviour {mode=ColorFillLayerBehaviour.FillMode.UV}; Layer layer=source;Call(layer,"AssignNewId");fixture.document.layers.Add(layer);
                fixture.behaviour.inputMode=ShaderLayerInputMode.Layer;fixture.behaviour.inputLayerId=layer.Id;await C("Input Layer",n);
                fixture.behaviour.inputMode=ShaderLayerInputMode.CompositeBelow;await C("Input Composite Below",n);
                var sample=Add(ShaderGraphNodeKind.SampleTexture);parameter.textureSource=ShaderTextureSource.Layer;parameter.textureLayerId=layer.Id;Resource(sample,"Texture",parameter);
                await C("Layer texture resource",sample);
                parameter.textureLayerId="missing";await C("Missing layer texture",sample);
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(texture);}
    }
}
