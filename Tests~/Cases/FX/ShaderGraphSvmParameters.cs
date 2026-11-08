using System;
using System.Linq;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;

public static partial class ShaderGraphEditingBench
{
    public static string SvmParameterChecks(string id) => RunSvmNodeChecks(id,false,remaining:12);
    public static string SvmSubGraphChecks(string id) => RunSvmNodeChecks(id,false,remaining:13);
    public static string SvmParameterWindowChecks(string id) => RunSvmWindowChecks(id,false,imageChecks:3);
    public static string SvmFxParameterWindowChecks(string id) => RunSvmWindowChecks(id,false,imageChecks:4);

    static ShaderGraphSubGraph SvmDefinition(ShaderGraph graph)
    {
        var sub=new ShaderGraphSubGraph {name="Coordinate scaling"};
        sub.inputs.Add(new ShaderGraphInterfacePort {name="Position",type=ShaderGraphValueType.Float2,uvDefault=true});
        sub.inputs.Add(new ShaderGraphInterfacePort {name="Scale",type=ShaderGraphValueType.Float,value=Vector4.one*.7f});
        sub.outputs.Add(new ShaderGraphInterfacePort {name="Position",type=ShaderGraphValueType.Float2});
        sub.outputs.Add(new ShaderGraphInterfacePort {name="Color",type=ShaderGraphValueType.Color,value=new Vector4(.2f,.4f,.6f,.8f)});
        var uv=new ShaderGraphNode {kind=ShaderGraphNodeKind.SubGraphInput,parameterId=sub.inputs[0].id};
        var scale=new ShaderGraphNode {kind=ShaderGraphNodeKind.SubGraphInput,parameterId=sub.inputs[1].id};
        var multiply=(ShaderGraphNode)Static("ShaderGraphNodes","Create",ShaderGraphNodeKind.Multiply,Vector2.zero);
        var output=new ShaderGraphNode {kind=ShaderGraphNodeKind.SubGraphOutput};
        sub.nodes.AddRange(new[]{uv,scale,multiply,output});
        sub.edges.Add(new ShaderGraphEdge {outputNode=uv.id,outputPort="Out",inputNode=multiply.id,inputPort="A"});
        sub.edges.Add(new ShaderGraphEdge {outputNode=scale.id,outputPort="Out",inputNode=multiply.id,inputPort="B"});
        sub.edges.Add(new ShaderGraphEdge {outputNode=multiply.id,outputPort="Out",inputNode=output.id,inputPort=sub.outputs[0].id});
        graph.subGraphs.Add(sub);return sub;
    }

    static async Task CheckParameterSvm(int suite,Fixture fixture,ShaderGraph graph,
        Func<ShaderGraphNodeKind,ShaderGraphNode> Add,Action<ShaderGraphNode,string,Vector4> Value,
        Func<string,ShaderGraphNode,string,bool,Task> Check)
    {
        Task C(string label,ShaderGraphNode node,string port="Out")=>Check(label,node,port,false);
        ShaderGraphNode Parameter(ShaderParameter p) {graph.parameters.Add(p);var n=Add(ShaderGraphNodeKind.Parameter);n.parameterId=p.id;return n;}
        if(suite==12)
        {
            foreach(var kind in new[]{ShaderParameterType.Float,ShaderParameterType.Vector2,ShaderParameterType.Vector3,ShaderParameterType.Vector})
            {
                var p=new ShaderParameter {name="Cast"+kind,type=kind,floatValue=-.2f,vectorValue=new Vector4(-.2f,.4f,1.6f,.35f)};
                var n=Parameter(p);await C(p.name+" to preview Color",n);
                var add=Add(ShaderGraphNodeKind.Add);Value(add,"B",new Vector4(.1f,-.1f,.2f,.125f));
                add.FindInput("B").type=ShaderGraphValueType.Float4;
                Connect(graph,n.id,"Out",add.id,"A");await C(p.name+" to Float4",add);
                var saturation=Add(ShaderGraphNodeKind.Saturation);Value(saturation,"Amount",Vector4.one*.4f);
                Connect(graph,n.id,"Out",saturation.id,"Color");await C(p.name+" to Color Saturation",saturation);
                graph.edges.RemoveAll(e=>e.inputNode==add.id&&e.inputPort=="A");
                Connect(graph,saturation.id,"Out",add.id,"A");await C(p.name+" Color to Float4",add);
            }
            foreach(string basis in new[]{"int","uint"})
            for(int count=1;count<=4;count++)
            {
                var p=new ShaderParameter {name=basis+count,valueType=basis+(count==1?"":count),
                    type=count==1?ShaderParameterType.Float:count==2?ShaderParameterType.Vector2:count==3?ShaderParameterType.Vector3:ShaderParameterType.Vector};
                for(int c=0;c<count;c++)Call(p,"SetNumber",c,(double)(basis=="int"?-(c+2)*7:(c+2)*11));
                var n=Parameter(p);await C(p.name,n);
                Call(p,"SetNumber",0,basis=="int"?(double)int.MinValue:(double)uint.MaxValue);await C(p.name+" boundary",n);
            }
            var color=new ShaderParameter {name="IntegerColor",type=ShaderParameterType.Color,valueType="int4"};
            for(int c=0;c<4;c++)Call(color,"SetNumber",c,(double)(c-1)*3);
            await C("Integer color does not decode HDR",Parameter(color));
            var toggle=new ShaderParameter {name="Bool",type=ShaderParameterType.Toggle,valueType="bool"};
            var boolean=Parameter(toggle);var select=Add(ShaderGraphNodeKind.Select);Connect(graph,boolean.id,"Out",select.id,"Condition");
            Value(select,"True",Vector4.one*.7f);Value(select,"False",Vector4.one*.2f);
            await C("Boolean false",select);Call(toggle,"SetNumber",0,1d);await C("Boolean true",select);
            foreach(var kind in new[]{ShaderParameterType.Float,ShaderParameterType.Vector2,ShaderParameterType.Vector3,ShaderParameterType.Vector,ShaderParameterType.Point})
            {
                var p=new ShaderParameter {name="Pixels"+kind,type=kind,pixelUnits=true,floatValue=3.25f,vectorValue=new Vector4(2.5f,-.5f,1.25f,4.5f)};
                var n=Parameter(p);fixture.document.pixelScale=2.5f;await C(p.name+" scale 2.5",n);
                fixture.document.pixelScale=.75f;await C(p.name+" scale .75",n);
            }
            var hdr=new ShaderParameter {name="HDR",type=ShaderParameterType.Color,colorValue=new Color(.7f,.3f,.2f,1)};
            await C("Floating color still decodes HDR",Parameter(hdr));
            return;
        }
        var sub=SvmDefinition(graph);
        var first=Add(ShaderGraphNodeKind.SubGraph);first.subGraphId=sub.id;
        var second=Add(ShaderGraphNodeKind.SubGraph);second.subGraphId=sub.id;
        Value(second,sub.inputs[1].id,Vector4.one*1.7f);
        await C("Sub Graph UV and scalar defaults",first,sub.outputs[0].id);
        await C("Sub Graph default Color output",first,sub.outputs[1].id);
        await C("Sub Graph independent instance value",second,sub.outputs[0].id);
        var sum=Add(ShaderGraphNodeKind.Add);Connect(graph,first.id,sub.outputs[0].id,sum.id,"A");Connect(graph,second.id,sub.outputs[0].id,sum.id,"B");
        await C("Two instances combined",sum);
        var parameter=new ShaderParameter {name="PixelScaleInput",type=ShaderParameterType.Float,pixelUnits=true,floatValue=.13f};
        fixture.document.pixelScale=3;Connect(graph,Parameter(parameter).id,"Out",first.id,sub.inputs[1].id);
        await C("Sub Graph pixel parameter input",sum);
        var outer=new ShaderGraphSubGraph {name="Nested"};outer.outputs.Add(new ShaderGraphInterfacePort {name="Result",type=ShaderGraphValueType.Color});
        var inner=new ShaderGraphNode {kind=ShaderGraphNodeKind.SubGraph,subGraphId=sub.id};
        var end=new ShaderGraphNode {kind=ShaderGraphNodeKind.SubGraphOutput};outer.nodes.AddRange(new[]{inner,end});
        outer.edges.Add(new ShaderGraphEdge {outputNode=inner.id,outputPort=sub.outputs[0].id,inputNode=end.id,inputPort=outer.outputs[0].id});graph.subGraphs.Add(outer);
        var nested=Add(ShaderGraphNodeKind.SubGraph);nested.subGraphId=outer.id;await C("Nested numeric to Color cast",nested,outer.outputs[0].id);
        var fractal=Add(ShaderGraphNodeKind.Fractal);Value(fractal,"Octaves",Vector4.one*2);Connect(graph,nested.id,outer.outputs[0].id,fractal.id,"In");
        await C("Sub Graph reevaluated in Fractal",fractal);
        Connect(graph,nested.id,outer.outputs[0].id,graph.nodes.First(n=>n.kind==ShaderGraphNodeKind.Output).id,"RGBA");
        await C("Sub Graph final output",null);
        var buffer=(ShaderGraphNode)Static("ShaderGraphNodes","Create",ShaderGraphNodeKind.Buffer,Vector2.zero);buffer.bufferResolution=ShaderGraphBufferResolution.Custom;buffer.bufferSize=new Vector2Int(32,32);
        outer.nodes.Add(buffer);outer.edges.Clear();outer.edges.Add(new ShaderGraphEdge {outputNode=inner.id,outputPort=sub.outputs[0].id,inputNode=buffer.id,inputPort="In"});
        outer.edges.Add(new ShaderGraphEdge {outputNode=buffer.id,outputPort="Out",inputNode=end.id,inputPort=outer.outputs[0].id});
        graph.edges.RemoveAll(e=>e.inputNode==fractal.id);await C("Sub Graph explicit Buffer continuation",nested,outer.outputs[0].id);
        await C("Sub Graph Buffer final output",null);
    }
}
