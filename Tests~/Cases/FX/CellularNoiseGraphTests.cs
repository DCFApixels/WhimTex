using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using Object = UnityEngine.Object;

// Detached graph integration: no windows, Undo changes, imported assets or user documents.
public static class CellularNoiseGraphTests
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool ok,string message) { checks++; if(!ok)throw new Exception(message); }
    static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Flags).Invoke(target,args);
    static string Source(ShaderGraph graph)
    {
        var type=typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphCompiler");
        var compiled=type.GetMethod("Compile",Flags).Invoke(null,new object[]{graph,null,"Out",false});
        return (string)compiled.GetType().GetField("source",Flags).GetValue(compiled);
    }
    static Color[] Pixels(WhimTexDocument document)
    {
        Call(document,"MarkChanged");
        var previous=RenderTexture.active;
        var rt=(RenderTexture)Call(document,"RenderCanvas",32);
        var cpu=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try { RenderTexture.active=rt; cpu.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false); return cpu.GetPixels(); }
        finally { RenderTexture.active=previous; Object.DestroyImmediate(cpu); RenderTexture.ReleaseTemporary(rt); }
    }
    static void Reject(ShaderGraph graph,string label)
    {
        try { Source(graph); } catch(TargetInvocationException e) { Check(e.InnerException.Message.Contains("Unknown noise option")||e.InnerException.Message.Contains("Unsupported Shader node kind"),label+" diagnostic: "+e.InnerException.Message); return; }
        Check(false,label+" must not be silently reinterpreted");
    }
    public static string Run()
    {
        checks=0; var previous=RenderTexture.active; bool srgb=GL.sRGBWrite;
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            doc.width=doc.height=32;
            var graph=new ShaderGraph();
            var cell=new ShaderGraphNode{kind=ShaderGraphNodeKind.CellularNoise,noiseFiltering=false};
            var output=new ShaderGraphNode{kind=ShaderGraphNodeKind.Output};
            graph.nodes.Add(cell); graph.nodes.Add(output);
            var edge=new ShaderGraphEdge{outputNode=cell.id,outputPort="Value",inputNode=output.id,inputPort="RGBA"}; graph.edges.Add(edge);
            var smooth=new ShaderGraphInput{port="Smoothness",type=ShaderGraphValueType.Float,value=new Vector4(.5f,0,0,0)};cell.inputs.Add(smooth);
            var shader=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph}; doc.layers.Add(new Layer(shader));
            Call(doc,"NormalizeModel");
            var kinds=Enum.GetNames(typeof(ShaderGraphNodeKind));
            Check(!kinds.Contains("VoronoiEdgeNoise")&&!kinds.Contains("VoronoiseNoise"),"Single cellular node kind");
            foreach(var dim in new[]{ShaderGraphNoiseDimensions.OneD,ShaderGraphNoiseDimensions.TwoD,ShaderGraphNoiseDimensions.ThreeD})
            foreach(ShaderGraphNoiseOutput mode in Enum.GetValues(typeof(ShaderGraphNoiseOutput)))
            {
                cell.noiseDimensions=dim;cell.noiseOutput=mode;
                string source=Source(graph);
                Check(source.Contains("WTNoiseCellular(")&&source.Contains(".smoothness ="),dim+"/"+mode+" uses unified API");
                if(dim!=ShaderGraphNoiseDimensions.TwoD)continue;
                Check((bool)Call(shader,"CompileGraph",doc),"GPU graph compile: "+dim+"/"+mode);
                var pixels=Pixels(doc);
                Check(pixels.All(c=>float.IsFinite(c.r)&&c.r>=-1e-5f&&c.r<=1.0001f&&Math.Abs(c.a-1)<1e-5f),"Graph renders finite normalized pixels: "+mode);
            }
            cell.noiseDimensions=ShaderGraphNoiseDimensions.TwoD;cell.noiseOutput=ShaderGraphNoiseOutput.DistanceToEdge;
            Color[] reference=null;
            foreach(ShaderGraphNoiseMetric metric in Enum.GetValues(typeof(ShaderGraphNoiseMetric)))
            {
                cell.noiseMetric=metric;Check((bool)Call(shader,"CompileGraph",doc),"Edge with stored Metric");var pixels=Pixels(doc);
                if(metric==ShaderGraphNoiseMetric.Euclidean)reference=pixels;
                Check(pixels.Select((c,i)=>Math.Abs(c.r-reference[i].r)).Max()<1e-6f,"Edge ignores Metric "+metric);
            }
            cell.noiseOutput=ShaderGraphNoiseOutput.CellValue;
            foreach(string port in new[]{"F1","F2","CellValue","ToPoint"})
            {
                edge.outputPort=port;Check((bool)Call(shader,"CompileGraph",doc),"Auxiliary port compiles: "+port);
                var before=Pixels(doc);smooth.value=Vector4.zero;
                Check((bool)Call(shader,"CompileGraph",doc),"Auxiliary zero compiles");var after=Pixels(doc);
                Check(before.Select((c,i)=>(new Vector4(c.r,c.g,c.b,c.a)-new Vector4(after[i].r,after[i].g,after[i].b,after[i].a)).magnitude).Max()<1e-6f,"Auxiliary remains hard: "+port);
                smooth.value=new Vector4(.5f,0,0,0);
            }
            edge.outputPort="Value";cell.noiseOutput=ShaderGraphNoiseOutput.DistanceToEdge;
            using(var read=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc).Json,false))
            {
                var restored=(ShaderLayerBehaviour)read.Document.layers[0].Behaviour;
                var node=restored.draftGraph.FindNode(cell.id);
                Check(node.noiseOutput==cell.noiseOutput&&node.noiseMetric==cell.noiseMetric,"Output and hidden Metric round trip");
                Check(node.FindInput("Smoothness").value==smooth.value,"Smoothness round trip");
            }
            var api=typeof(WhimTexApi).GetMethod("ShaderNodeCatalog",Flags).Invoke(null,null).ToString();
            Check(api.Contains("DistanceToEdge")&&api.Contains("metricApplicable")&&!api.Contains("VoronoiseNoise")&&!api.Contains("VoronoiEdgeNoise"),"Agent catalog matches merged contract");
            cell.noiseOutput=(ShaderGraphNoiseOutput)7;Reject(graph,"Removed SmoothF1");cell.noiseOutput=ShaderGraphNoiseOutput.F1;
            foreach(int removed in new[]{140,141}) { cell.kind=(ShaderGraphNodeKind)removed;Reject(graph,"Removed kind "+removed); }
            return "{\"status\":\"passed\",\"checks\":"+checks+",\"message\":\"Unified graph compilation, rendering, auxiliary geometry, JSON and agent catalog passed.\",\"failures\":[]}";
        }
        finally { Object.DestroyImmediate(doc);RenderTexture.active=previous;GL.sRGBWrite=srgb; }
    }
}
