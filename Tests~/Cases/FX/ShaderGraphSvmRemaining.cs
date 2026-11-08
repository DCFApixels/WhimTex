using System;
using System.Linq;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;

public static partial class ShaderGraphEditingBench
{
    public static string SvmGrainChecks(string id) => RunSvmNodeChecks(id,false,remaining:1);
    public static string SvmSpotsChecks(string id) => RunSvmNodeChecks(id,false,remaining:2);
    public static string SvmTriangleChecks(string id) => RunSvmNodeChecks(id,false,remaining:3);
    public static string SvmBasicWarpChecks(string id) => RunSvmNodeChecks(id,false,remaining:4);
    public static string SvmSimplexWarpChecks(string id) => RunSvmNodeChecks(id,false,remaining:5);
    public static string SvmReducedWarpChecks(string id) => RunSvmNodeChecks(id,false,remaining:6);
    public static string SvmEdgeTransformChecks(string id) => RunSvmNodeChecks(id,false,remaining:7);
    public static string SvmRemainingWindowChecks(string id) => RunSvmWindowChecks(id,false,remainingChecks:true);
    static readonly ShaderGraphNodeKind[] RemainingVMKinds = {
        ShaderGraphNodeKind.WhiteNoise, ShaderGraphNodeKind.BlueNoise, ShaderGraphNodeKind.SpotsNoise,
        ShaderGraphNodeKind.TriangleVoronoiNoise, ShaderGraphNodeKind.BasicGridWarpNoise,
        ShaderGraphNodeKind.OpenSimplex2WarpNoise, ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise,
        ShaderGraphNodeKind.EdgeDetection, ShaderGraphNodeKind.ApplyTransform2D };

    static async Task CheckRemainingSvm(int suite, Fixture fixture, ShaderGraph graph,
        Func<ShaderGraphNodeKind,ShaderGraphNode> Add, Action<ShaderGraphNode,string,Vector4> Value,
        Func<string,ShaderGraphNode,string,bool,Task> Check)
    {
        Task C(string label,ShaderGraphNode n,string p="Out")=>Check(label,n,p,false);
        fixture.document.width=640;fixture.document.height=360;
        void Dimensions(ShaderGraphNode n,ShaderGraphNoiseDimensions d)
        {
            Static("ShaderGraphNodes","ChangeNoiseDimensions",graph,n,d);
            if(suite!=1) {
                Value(n,"Position",new Vector4(0,0,.371f,0));
                n.FindInput("Position").source=ShaderGraphInputSource.UVX;
                n.FindInput("Position").type=(ShaderGraphValueType)(int)d;
                Value(n,"Scale",new Vector4(7,5,3,0));
                Value(n,"Period",new Vector4(1.3f,.9f,1.1f,0));
            }
        }
        if(suite<=6)
        {
            var kinds=suite==1?RemainingVMKinds.Take(2):new[]{RemainingVMKinds[suite==2?2:suite==3?3:suite]};
            foreach(var kind in kinds)
            {
                var n=Add(kind);
                var dimensions=suite==3?new[]{ShaderGraphNoiseDimensions.TwoD}:
                    suite==1?new[]{ShaderGraphNoiseDimensions.OneD,ShaderGraphNoiseDimensions.TwoD}:
                    new[]{ShaderGraphNoiseDimensions.ThreeD,ShaderGraphNoiseDimensions.OneD,ShaderGraphNoiseDimensions.TwoD};
                foreach(var d in dimensions)
                {
                    Dimensions(n,d);n.noiseFiltering=n.noisePeriodic=false;
                    if(suite==1) {Value(n,"GrainSize",Vector4.one*2.7f);Value(n,"Direction",Vector4.one*37);}
                    if(suite==2) {Value(n,"Density",Vector4.one*1.7f);Value(n,"Elongation",Vector4.one*2.3f);Value(n,"Direction",new Vector4(.3f,.7f,.2f,0));}
                    if(suite==3) {Value(n,"Evolution",Vector4.one*.37f);Value(n,"Stretch",new Vector4(.6f,1.4f,0,0));}
                    if(suite>=4) Value(n,"Strength",Vector4.one*.23f);
                    string first=suite>=4?"Displacement":"Value";
                    await C(kind+" "+d+" default",n,first);
                    n.noiseInverted=true;n.noiseFiltering=n.noisePeriodic=true;
                    foreach(string p in (string[])Static("ShaderGraphNodes","Outputs",n)) await C(kind+" "+d+" configured "+p,n,p);
                    if(suite>=4)
                        foreach(var mode in new[]{ShaderGraphFractal.Progressive,ShaderGraphFractal.Independent})
                        {n.fractal=mode;await C(kind+" "+d+" "+mode,n,"Position");}
                    n.fractal=ShaderGraphFractal.None;
                }
                if(suite==1) {Value(n,"ImageSize",new Vector4(237,149,0,0));await C(kind+" custom dimensions",n,"Color");}
                if(suite==3) {
                    n.noiseDistribution=ShaderGraphNoiseDistribution.FixedRadius;
                    foreach(string p in (string[])Static("ShaderGraphNodes","Outputs",n))await C("Triangle FixedRadius "+p,n,p);
                }
                var f=Add(ShaderGraphNodeKind.Fractal);Value(f,"Octaves",Vector4.one*2);
                Connect(graph,n.id,suite>=4?"Displacement":suite==1?"Color":"Value",f.id,"In");
                await C(kind+" sampled Fractal",f);
            }
            return;
        }
        var uv=Add(ShaderGraphNodeKind.UV);var edge=Add(ShaderGraphNodeKind.EdgeDetection);
        Value(edge,"Strength",Vector4.one*3.2f);Connect(graph,uv.id,"Out",edge.id,"In");
        foreach(float radius in new[]{1.3f,0,-2f,1e-8f}) {Value(edge,"Radius",Vector4.one*radius);await C("Edge float2 radius "+radius,edge);}
        Value(edge,"Radius",Vector4.one*.7f);
        var rings=Add(ShaderGraphNodeKind.Rings);var ramp=Add(ShaderGraphNodeKind.SampleGradient);
        var resource=new ShaderParameter {type=ShaderParameterType.Gradient,gradientValue=new WhimTexGradient()};
        resource.gradientValue.SetKeys(new[]{new GradientColorKey(new Color(-.2f,2,.4f),0),new GradientColorKey(new Color(1.5f,.3f,3),1)},
            new[]{new GradientAlphaKey(.17f,0),new GradientAlphaKey(.83f,1)});
        ramp.inputs.Add(new ShaderGraphInput{port="Gradient",type=ShaderGraphValueType.Gradient,resource=resource});
        Connect(graph,rings.id,"Out",ramp.id,"T");
        var warpUV=Add(ShaderGraphNodeKind.TransformUV);Value(warpUV,"Rotation",Vector4.one*31);
        Connect(graph,warpUV.id,"Out",edge.id,"UV");
        foreach(var pair in new[]{(rings,"Out"),(ramp,"Out"),(uv,"Out")}) {
            graph.edges.RemoveAll(e=>e.inputNode==edge.id && e.inputPort=="In");Connect(graph,pair.Item1.id,pair.Item2,edge.id,"In");
            await C("Edge "+pair.Item1.kind+" transformed UV",edge);
            if(pair.Item1==ramp) {Value(edge,"Radius",Vector4.zero);await C("Edge Color zero radius retains alpha",edge);Value(edge,"Radius",Vector4.one*.7f);}
        }
        var vector=Add(ShaderGraphNodeKind.Add);Value(vector,"B",new Vector4(.2f,-.3f,2,.4f));vector.FindInput("B").type=ShaderGraphValueType.Float4;
        Connect(graph,uv.id,"Out",vector.id,"A");graph.edges.RemoveAll(e=>e.inputNode==edge.id && e.inputPort=="In");
        Connect(graph,vector.id,"Out",edge.id,"In");await C("Edge float4",edge);
        var fEdge=Add(ShaderGraphNodeKind.Fractal);Value(fEdge,"Octaves",Vector4.one*2);Connect(graph,edge.id,"Out",fEdge.id,"In");await C("Edge sampled Fractal",fEdge);
        var transform=Add(ShaderGraphNodeKind.ApplyTransform2D);
        await C("Default transform",transform,"Local");
        var parameter=new ShaderParameter{type=ShaderParameterType.Transform2D,name="_Transform"};
        transform.inputs.Add(new ShaderGraphInput {port="Transform",type=ShaderGraphValueType.Transform2D,resource=parameter});
        foreach(var value in new[]{
            new ShaderTransform{position=new Double2(.73,.29),size=new Double2(-.7,1.3),rotation=37},
            new ShaderTransform{storage=TransformStorage.Projective,matrix=new ProjectiveMatrix{m00=1.1,m01=.2,m02=.13,m10=-.1,m11=.9,m12=-.2,m20=.3,m21=-.1,m22=1}},
            new ShaderTransform{position=new Double2(.5,.5),size=new Double2(0,1)} }) {
            parameter.transformValue=value;
            foreach(string p in new[]{"Local","Input"})await C("Transform "+value.storage+" "+value.size.x+" "+p,transform,p);
        }
        parameter.transformValue=ShaderTransform.Default;graph.parameters.Add(parameter);
        var parameterNode=Add(ShaderGraphNodeKind.Parameter);parameterNode.parameterId=parameter.id;
        var reroute=Add(ShaderGraphNodeKind.Reroute);Connect(graph,parameterNode.id,"Out",reroute.id,"In");Connect(graph,reroute.id,"Out",transform.id,"Transform");
        await C("Transform parameter through Reroute",transform,"Local");
        fixture.document.width=360;fixture.document.height=640;parameter.transformValue.rotation=73;
        await C("Transform resized canvas",transform,"Local");
    }
}
