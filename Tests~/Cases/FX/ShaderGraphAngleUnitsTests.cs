using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using Object = UnityEngine.Object;

public static class ShaderGraphAngleUnitsTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }


    public static async Task<object> RunUI()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        WhimTexDocument document=null;ShaderGraphWindow window=null;
        try {
            document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=16;
            var shader=new ShaderLayerBehaviour();document.layers.Add(shader);Call(typeof(WhimTexDocument),document,"NormalizeModel");
            var graph=shader.draftGraph;var model=Find("ShaderGraphNodes");
            var kinds=new[]{ShaderGraphNodeKind.ConvertAngle,ShaderGraphNodeKind.RotateAboutAxis,ShaderGraphNodeKind.Scatter,ShaderGraphNodeKind.Hue,ShaderGraphNodeKind.PolarCoordinates,ShaderGraphNodeKind.HueRange};
            var ids=new List<string>();
            for(int i=0;i<kinds.Length;i++) {
                var n=(ShaderGraphNode)Call(model,null,"Create",kinds[i],new Vector2((i%3)*330,(i/3)*400));n.previewExpanded=false;graph.nodes.Add(n);ids.Add(n.id);
            }
            window=ScriptableObject.CreateInstance<ShaderGraphWindow>();window.titleContent=new GUIContent("Angle Units Check");
            typeof(ShaderGraphWindow).GetField("document",flags).SetValue(window,document);
            typeof(ShaderGraphWindow).GetField("layerId",flags).SetValue(window,document.layers[0].Id);
            window.ShowUtility();window.position=new Rect(60,80,1120,800);window.CreateGUI();await Task.Delay(300);
            var canvas=(GraphView)typeof(ShaderGraphWindow).GetField("canvas",flags).GetValue(window);
            Node View(int index)=>canvas.nodes.Single(n=>n.viewDataKey==ids[index]);
            DropdownField Field(int i,string name)=>View(i).Query<DropdownField>().ToList().Single(f=>f.label==name);
            Check(Field(0,"From").value=="Degrees"&&Field(0,"To").value=="Radians","Conversion UI defaults");
            Undo.FlushUndoRecordObjects(); Undo.ClearUndo(document); Undo.IncrementCurrentGroup();
            Field(0,"From").value="Turns";Undo.FlushUndoRecordObjects();await Task.Delay(80);
            Undo.IncrementCurrentGroup(); Field(0,"To").value="Degrees";Undo.FlushUndoRecordObjects();await Task.Delay(80);
            Check(graph.FindNode(ids[0]).inputAngleUnit==ShaderGraphAngleUnit.Turns&&graph.FindNode(ids[0]).outputAngleUnit==ShaderGraphAngleUnit.Degrees,"UI stores both units");
            Undo.PerformUndo();await Task.Delay(100);graph=((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Check(graph.FindNode(ids[0]).outputAngleUnit==ShaderGraphAngleUnit.Radians,"Undo destination unit");
            Undo.PerformRedo();await Task.Delay(100);graph=((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Check(graph.FindNode(ids[0]).outputAngleUnit==ShaderGraphAngleUnit.Degrees,"Redo destination unit");
            Field(1,"Unit").value="Turns";await Task.Delay(80);
            Check(graph.FindNode(ids[1]).option==2,"Axis supports Turns");
            int labels=0;
            foreach(var view in canvas.nodes)foreach(var port in view.Query<Port>().ToList()) {
                var label=port.Q<Label>("type");if(label==null)continue;
                if(!label.text.Contains("turn")&&!label.text.Contains("rad")&&!label.text.Contains("\u00b0"))continue;
                labels++;
                Check(label.resolvedStyle.display!=DisplayStyle.None&&label.worldBound.width>0,"Unit label visible "+label.text);
                Check(label.MeasureTextSize(label.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x<=label.worldBound.width+1,"Unit label fits "+label.text);
            }
            Check(labels>=9,"Angular units shown on node ports");
            using var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false);
            var saved=((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph.FindNode(ids[0]);
            Check(saved.inputAngleUnit==ShaderGraphAngleUnit.Turns&&saved.outputAngleUnit==ShaderGraphAngleUnit.Degrees,"UI settings roundtrip");
            return new{status="passed",checks=labels+8,failures=Array.Empty<string>(),message="Angle dropdowns, unit labels, Undo/Redo and current JSON."};
        } finally {if(window!=null)window.Close();if(document!=null){Undo.ClearUndo(document);Object.DestroyImmediate(document);}}
    }

    public static async Task<object> Run(int batch = 0)
    {
        try {return await RunCore(batch);}
        catch(Exception e){return new{status="failed",checks=0,failures=new[]{e.ToString()},message=e.Message};}
    }
    static async Task<object> RunCore(int batch)
    {
        var nodes = Find("ShaderGraphNodes");
        var graph = new ShaderGraph();
        var cases = new List<(string label, ShaderGraphNode node, string port, Color expected)>();
        ShaderGraphNode Node(ShaderGraphNodeKind kind)
        {
            var n = (ShaderGraphNode)Call(nodes, null, "Create", kind, Vector2.zero);
            graph.nodes.Add(n); return n;
        }
        void Set(ShaderGraphNode n, string port, Vector4 value, ShaderGraphValueType? type = null)
        {
            var input = n.FindInput(port);
            if(input==null) {
                var definitions=(System.Collections.IEnumerable)Call(nodes,null,"Inputs",n);
                var definition=definitions.Cast<object>().Single(x=>(string)x.GetType().GetField("name",F|BindingFlags.Instance).GetValue(x)==port);
                var defaultType=(ShaderGraphValueType?)definition.GetType().GetField("type",F|BindingFlags.Instance).GetValue(definition);
                input=new ShaderGraphInput {port=port,type=defaultType??ShaderGraphValueType.Float};n.inputs.Add(input);
            }
            input.value = value; input.source = ShaderGraphInputSource.Value;
            if (type.HasValue) input.type = type.Value;
        }
        void Scalar(ShaderGraphNode n, string port, float value) => Set(n, port, Vector4.one * value);
        void Case(string label, ShaderGraphNode n, Color expected, string port = "Out") => cases.Add((label, n, port, expected));
        Color Gray(float v) => new Color(v, v, v, 1);


        Check(!Enum.GetNames(typeof(ShaderGraphNodeKind)).Contains("DegreesToRadians"), "Old converter removed");
        Check(!Enum.GetNames(typeof(ShaderGraphNodeKind)).Contains("RadiansToDegrees"), "Old converter removed");
        double Unit(ShaderGraphAngleUnit u) => u == ShaderGraphAngleUnit.Turns ? 2*Math.PI : u == ShaderGraphAngleUnit.Degrees ? Math.PI/180 : 1;
        void Link(ShaderGraphNode from, ShaderGraphNode to, string input, string port="Out") =>
            graph.edges.Add(new ShaderGraphEdge {outputNode=from.id,outputPort=port,inputNode=to.id,inputPort=input});
        if(batch==0)
        {
            foreach(ShaderGraphAngleUnit from in Enum.GetValues(typeof(ShaderGraphAngleUnit)))
            foreach(ShaderGraphAngleUnit to in Enum.GetValues(typeof(ShaderGraphAngleUnit)))
            for(int width=1;width<=4;width++)
            {
                var n=Node(ShaderGraphNodeKind.ConvertAngle); n.inputAngleUnit=from;n.outputAngleUnit=to;
                var values=new Vector4(-.25f,0,1.25f,2.5f);var expected=new Vector4(0,0,0,1);
                for(int k=0;k<width;k++)expected[k]=(float)(values[k]*Unit(from)/Unit(to));
                Set(n,"In",values,(ShaderGraphValueType)(width-1));
                if(width==3){var value=Node(ShaderGraphNodeKind.Value);value.literal.type=ShaderParameterType.Vector3;value.literal.vectorValue=values;Link(value,n,"In");}
                Case($"Convert/{from}/{to}/{width}",n,width==1?Gray(expected.x):(Color)expected);
            }
            var control=Node(ShaderGraphNodeKind.ConvertAngle);
            Check(control.inputAngleUnit==ShaderGraphAngleUnit.Degrees&&control.outputAngleUnit==ShaderGraphAngleUnit.Radians,"Conversion defaults");
            var previous=Call(Find("ShaderGraphCompiler"),null,"CodeFingerprint",graph);
            control.inputAngleUnit=ShaderGraphAngleUnit.Turns;
            Check(!previous.Equals(Call(Find("ShaderGraphCompiler"),null,"CodeFingerprint",graph)),"Source unit invalidates code fingerprint");
            previous=Call(Find("ShaderGraphCompiler"),null,"CodeFingerprint",graph);control.outputAngleUnit=ShaderGraphAngleUnit.Turns;
            Check(!previous.Equals(Call(Find("ShaderGraphCompiler"),null,"CodeFingerprint",graph)),"Destination unit invalidates code fingerprint");
        }
        else
        {
            for(int mode=0;mode<3;mode++) {
                var n=Node(ShaderGraphNodeKind.GradientFactor);n.option=mode;Set(n,"UV",new Vector4(.75f,.5f));Scalar(n,"Angle",90);
                Case("GradientFactor/degrees/"+mode,n,Gray(mode==2?.75f:.5f));
            }
            foreach(float strength in new[]{0f,.25f,-.25f,1f,2.25f})
            {
                var n=Node(ShaderGraphNodeKind.Twirl);Check(n.FindInput("Strength").value.x==.25f,"Twirl default");
                Set(n,"UV",new Vector4(.75f,.5f));Scalar(n,"Strength",strength);
                double angle=strength*.5*2*Math.PI;
                Case("Twirl/"+strength,n,new Color(.5f+.25f*(float)Math.Cos(angle),.5f+.25f*(float)Math.Sin(angle),0,1));
            }
            foreach(float offset in new[]{0f,.25f,-.25f,1.25f})
            {
                var n=Node(ShaderGraphNodeKind.PolarCoordinates);Set(n,"UV",new Vector4(.75f,.5f));Scalar(n,"Angle",offset);
                Case("Polar/to/"+offset,n,new Color(Mathf.Repeat(-offset,1),.5f,0,1));
                var inverse=Node(ShaderGraphNodeKind.PolarCoordinates);inverse.option=1;Scalar(inverse,"Angle",offset);Link(n,inverse,"UV");
                Case("Polar/roundtrip/"+offset,inverse,new Color(.75f,.5f,0,1));
            }
            foreach(float offset in new[]{0f,1f/3,-1f/3,4f/3})
            {
                var n=Node(ShaderGraphNodeKind.Hue);Check(n.FindInput("Degrees")==null,"Hue uses Offset");Set(n,"Color",new Vector4(1,0,0,.3f));Scalar(n,"Offset",offset);
                var color=Mathf.Abs(offset)<.01f?Color.red:offset<0?Color.blue:Color.green;color.a=.3f;Case("Hue/"+offset,n,color);
            }
            foreach(var range in new[]{new Vector2(11f/12,1f/12),new Vector2(0,0),new Vector2(-1,0),new Vector2(0,2)})
            {
                var n=Node(ShaderGraphNodeKind.HueRange);Set(n,"Color",new Vector4(1,0,0,.1f));Set(n,"Range",range);Scalar(n,"Feather",0);
                Case("HueRange/"+range,n,Gray(range==Vector2.zero?0:1));
            }
            var feather=Node(ShaderGraphNodeKind.HueRange);Set(feather,"Color",new Vector4(1,0,0,1));Set(feather,"Range",new Vector4(0,0));Scalar(feather,"Feather",1f/24);
            Case("HueRange/equal-feather",feather,Gray(1));
            foreach(float phase in new[]{-.25f,0,.25f,1.25f,2.75f})
            {
                var n=Node(ShaderGraphNodeKind.NoiseSineWave);Scalar(n,"In",phase);Set(n,"MinMax",Vector4.zero);Case("Wave/"+phase,n,Gray((float)Math.Sin(phase*2*Math.PI)));
            }
            foreach(int unit in new[]{0,1,2})
            {
                var n=Node(ShaderGraphNodeKind.RotateAboutAxis);n.option=unit;Set(n,"In",new Vector4(2,0,0));Set(n,"Axis",new Vector4(0,0,3));
                Scalar(n,"Rotation",unit==0?Mathf.PI/2:unit==1?90:.25f);Case("Axis/"+unit,n,new Color(0,2,0,1));
            }
            uint Hash(uint v){unchecked{v^=v>>16;v*=0x7feb352du;v^=v>>15;v*=0x846ca68bu;return v^(v>>16);}}
            foreach(float rotation in new[]{-.25f,.5f,1.25f})
            {
                var n=Node(ShaderGraphNodeKind.TileUV);Set(n,"UV",new Vector4(.7f,.6f));Set(n,"Count",Vector4.one);Scalar(n,"RandomRotation",rotation);
                // Cell (0,0): GraphHashPoint = Hash(0 ^ Hash(0) ^ Hash(seedBits)).
                uint h=Hash(Hash(0)^Hash(unchecked((uint)BitConverter.SingleToInt32Bits(1337))));
                double angle=-((Hash(h^0x9e3779b9u)>>8)/16777216.0*2-1)*rotation*2*Math.PI;
                Case("Tile/"+rotation,n,new Color(.5f+(float)(.2*Math.Cos(angle)-.1*Math.Sin(angle)),.5f+(float)(.2*Math.Sin(angle)+.1*Math.Cos(angle)),0,1),"UV");
            }
            foreach(bool map in new[]{false,true})
            {
                var n=Node(ShaderGraphNodeKind.Scatter);Check(n.FindInput("RotationRandom").value.x==.5f,"Scatter random default");
                Set(n,"UV",new Vector4(.75f,.5f));Set(n,"Count",Vector4.one);Set(n,"Size",Vector4.one);
                Scalar(n,"Jitter",0);Scalar(n,"SizeRandom",0);Scalar(n,"RotationRandom",0);
                Scalar(n,map?"RotationMap":"Rotation",1.25f);
                if(map){var v=Node(ShaderGraphNodeKind.Value);v.literal.floatValue=1.25f;Link(v,n,"RotationMap");}
                Link(Node(ShaderGraphNodeKind.UV),n,"Source");
                Case("Scatter/"+map,n,new Color(.5f,.25f,0,1));
            }
        }
        var outputNode=Node(ShaderGraphNodeKind.Output);
        Link(cases[0].node,outputNode,"RGBA",cases[0].port);
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 16;
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
        Call(typeof(Layer), layer, "AssignNewId"); document.layers.Add(layer);
        var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var previousActive = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        target.Create();
        try
        {
            string json = WhimTexDocumentJson.Write(document).Json;
            using (var loaded = WhimTexDocumentJson.Read(json, false))
            {
                var saved = ((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
                Check(saved.nodes.Count == graph.nodes.Count && saved.edges.Count == graph.edges.Count, "JSON graph roundtrip");
                foreach (var node in graph.nodes)
                    Check(saved.FindNode(node.id).option == node.option, "Saved mode " + node.id);
                foreach(var node in graph.nodes) {
                    Check(saved.FindNode(node.id).inputAngleUnit==node.inputAngleUnit,"Saved source unit");
                    Check(saved.FindNode(node.id).outputAngleUnit==node.outputAngleUnit,"Saved destination unit");
                }
            }
            var requestType = Find("ShaderProgramRuntime+CompilationRequest");
            var vmType = Find("ShaderGraphPreviewVM");
            var context = Activator.CreateInstance(Find("LayerRenderContext"), new object[] { document, null, 16, 16, 1f, false, false, false });
            float maxError = 0, maxDifference = 0;
            async Task Until(Func<bool> condition, string label)
            {
                var timer = Stopwatch.StartNew();
                while (!condition()) { if (timer.Elapsed.TotalSeconds > 45) throw new TimeoutException(label); await Task.Delay(25); }
            }
            Color Read()
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = target; pixel.ReadPixels(new Rect(3, 3, 1, 1), 0, 0); pixel.Apply(); return pixel.GetPixel(0, 0); }
                finally { RenderTexture.active = active; }
            }
            foreach (var c in cases)
            {
                using var request = (IDisposable)Activator.CreateInstance(requestType, BindingFlags.Instance | F, null,
                    new object[] { graph, c.node.id, c.port, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false }, null);
                object[] completed = { null };
                await Until(() => (bool)Call(requestType, request, "TryComplete", completed), c.label + " HLSL");
                using var runtime = (IDisposable)completed[0];
                var runtimeType = runtime.GetType();
                var diagnostics=(System.Collections.IEnumerable)runtimeType.GetProperty("DiagnosticMessages",F|BindingFlags.Instance).GetValue(runtime);
                foreach(var diagnostic in diagnostics) {
                    string severity=diagnostic.GetType().GetField("Severity",F|BindingFlags.Instance).GetValue(diagnostic).ToString();
                    Check(severity!="Warning"&&severity!="Error",c.label+": "+diagnostic);
                }
                Call(runtimeType, runtime, "Bind", graph, context, null, true, false);
                Call(runtimeType, runtime, "PrepareCoordinates", layer, context);
                bool srgb = GL.sRGBWrite;
                try { GL.sRGBWrite = false; Call(runtimeType, runtime, "Render", Texture2D.blackTexture, target, context); }
                finally { GL.sRGBWrite = srgb; }
                Color native = Read();
                object[] build = { graph, c.node.id, c.port, null };
                bool built=(bool)Call(vmType, null, "TryCreate", build);
                if(c.label.StartsWith("Scatter/")) {
                    Check(!built,"Scatter keeps native fallback");
                    for(int k=0;k<4;k++)Check(Mathf.Abs(native[k]-c.expected[k])<.0005f,c.label+" native expected "+native);
                    continue;
                }
                Check(built,c.label+" SVM build");
                using var vm = (IDisposable)build[3];
                await Until(() => (bool)Call(vmType, vm, "UpdatePreparation"), c.label + " SVM preparation");
                Check((bool)Call(vmType, vm, "Render", target, 0f), c.label + " SVM render");
                Color svm = Read();
                for (int channel = 0; channel < 4; channel++)
                {
                    float error = Mathf.Max(Mathf.Abs(native[channel] - c.expected[channel]), Mathf.Abs(svm[channel] - c.expected[channel]));
                    float difference = Mathf.Abs(native[channel] - svm[channel]);
                    Check(float.IsFinite(error) && error < 0.0005f, $"{c.label}[{channel}] expected {c.expected[channel]:R}, HLSL {native[channel]:R}, SVM {svm[channel]:R}");
                    Check(difference < 0.0001f, c.label + " HLSL/SVM difference " + difference);
                    maxError = Mathf.Max(maxError, error); maxDifference = Mathf.Max(maxDifference, difference);
                }
            }
            return new {
                status = "passed", checks = cases.Count, failures = Array.Empty<string>(),
                message = $"{cases.Count} GPU cases, HLSL/SVM and independent numeric expectations; mode/default/JSON checks. Max error {maxError:R}, max HLSL/SVM difference {maxDifference:R}."
            };
        }
        finally { RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb; Object.DestroyImmediate(pixel); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(document); }
    }
}
