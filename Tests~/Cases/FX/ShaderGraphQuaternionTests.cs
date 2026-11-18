using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class ShaderGraphQuaternionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) => type.GetMethods(F)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static ShaderGraphNode Node(ShaderGraph graph, ShaderGraphNodeKind kind)
    { var node = (ShaderGraphNode)Call(T("ShaderGraphNodes"), null, "Create", kind, Vector2.zero); graph.nodes.Add(node); return node; }
    static Vector4 V(Quaternion q) => new Vector4(q.x, q.y, q.z, q.w);
    static Quaternion Q(Color value) => new Quaternion(value.r, value.g, value.b, value.a);
    static Quaternion Euler(Vector3 degrees, ShaderGraphRotationOrder order)
    {
        var result = Quaternion.identity;
        foreach (char axis in order.ToString())
            result = Quaternion.AngleAxis(degrees[axis - 'X'], axis == 'X' ? Vector3.right : axis == 'Y' ? Vector3.up : Vector3.forward) * result;
        return result;
    }
    static readonly ShaderGraphNodeKind[] kinds = { ShaderGraphNodeKind.EulerToQuaternion, ShaderGraphNodeKind.QuaternionToEuler,
        ShaderGraphNodeKind.RotateByQuaternion, ShaderGraphNodeKind.QuaternionMultiply, ShaderGraphNodeKind.QuaternionInverse, ShaderGraphNodeKind.QuaternionSlerp };

    public static async Task<object> Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
        var graph = new ShaderGraph();
        var cases = new List<(string label, ShaderGraphNode node, Vector4 expected, int interpretation)>();
        void Set(ShaderGraphNode n, string port, Vector4 value) { n.FindInput(port).value = value; }
        void Case(string label, ShaderGraphNode n, Vector4 expected, int interpretation = 0) => cases.Add((label, n, expected, interpretation));
        foreach (var kind in kinds)
        {
            var n = Node(graph, kind);
            Check((string)Call(T("ShaderGraphNodes"), null, "Category", kind) == "Math/Quaternion", "Category " + kind);
            foreach (var input in n.inputs.Where(i => i.type == ShaderGraphValueType.Float4)) Check(input.value == new Vector4(0,0,0,1), "Identity default " + kind);
            if (kind is ShaderGraphNodeKind.EulerToQuaternion or ShaderGraphNodeKind.QuaternionToEuler)
                Check(n.option == 1 && n.rotationOrder == ShaderGraphRotationOrder.ZXY, "Degree/ZXY defaults " + kind);
        }
        var samples = new[] { Vector3.zero, new Vector3(23,-47,61), new Vector3(-135,38,270), new Vector3(420,-210,750) };
        foreach (ShaderGraphRotationOrder order in Enum.GetValues(typeof(ShaderGraphRotationOrder)))
        foreach (Vector3 degrees in samples)
        for (int unit = 0; unit < 3; unit++)
        {
            var q = Euler(degrees, order);
            var from = Node(graph, ShaderGraphNodeKind.EulerToQuaternion); from.option = unit; from.rotationOrder = order;
            Set(from, "Euler", degrees * (unit == 0 ? Mathf.Deg2Rad : unit == 2 ? 1f/360 : 1));
            Case($"Euler/{order}/{unit}/{degrees}", from, V(q), 1);
            var to = Node(graph, ShaderGraphNodeKind.QuaternionToEuler); to.option = unit; to.rotationOrder = order;
            Set(to, "Quaternion", V(q) * 3.7f); Case($"InverseEuler/{order}/{unit}/{degrees}", to, V(q), 2);
        }
        foreach (ShaderGraphRotationOrder order in Enum.GetValues(typeof(ShaderGraphRotationOrder)))
        foreach (float middle in new[] { -90f, -89.999f, 89.999f, 90f })
        {
            var degrees = new Vector3(25, -37, 49); degrees[order.ToString()[1] - 'X'] = middle;
            var to = Node(graph, ShaderGraphNodeKind.QuaternionToEuler); to.rotationOrder = order;
            Set(to, "Quaternion", V(Euler(degrees, order))); Case("Gimbal/" + order + "/" + middle, to, V(Euler(degrees, order)), 2);
        }
        var qa = Quaternion.Euler(23, -47, 61); var qb = Quaternion.Euler(-19, 32, 77);
        var vector = new Vector3(2, -3, 4);
        foreach (float scale in new[] { 1f, -3f, 1e20f, 1e-20f })
        {
            var rotate = Node(graph, ShaderGraphNodeKind.RotateByQuaternion); Set(rotate, "In", vector); Set(rotate, "Quaternion", V(qa) * scale);
            Case("Rotate/" + scale, rotate, (Vector4)(qa * vector));
            var inverse = Node(graph, ShaderGraphNodeKind.QuaternionInverse); Set(inverse, "Quaternion", V(qa) * scale);
            Case("Inverse/" + scale, inverse, V(Quaternion.Inverse(qa)) / scale);
        }
        var multiply = Node(graph, ShaderGraphNodeKind.QuaternionMultiply); Set(multiply, "A", V(qa) * 2); Set(multiply, "B", V(qb) * 3);
        Case("Hamilton product retains magnitude", multiply, V(qa * qb) * 6);
        var rotated = Node(graph, ShaderGraphNodeKind.RotateByQuaternion); Set(rotated, "In", vector);
        graph.edges.Add(new ShaderGraphEdge { outputNode = multiply.id, outputPort = "Out", inputNode = rotated.id, inputPort = "Quaternion" });
        Case("Connected product applies B before A", rotated, (Vector4)(qa * (qb * vector)));
        var ordinary = Node(graph, ShaderGraphNodeKind.Multiply);
        ordinary.FindInput("A").type = ordinary.FindInput("B").type = ShaderGraphValueType.Float4;
        Set(ordinary, "A", V(qa)); Set(ordinary, "B", V(qb)); Case("Ordinary Multiply is component-wise", ordinary, Vector4.Scale(V(qa), V(qb)));
        foreach (float t in new[] { -.25f, 0, .35f, 1, 1.25f })
        {
            var n = Node(graph, ShaderGraphNodeKind.QuaternionSlerp); Set(n,"A",V(qa)*2); Set(n,"B",V(qb)*-3); Set(n,"T",Vector4.one*t);
            Case("Slerp/extrapolation/" + t,n,V(Quaternion.SlerpUnclamped(qa,qb,t)),1);
        }
        foreach (var other in new[] { qa, Quaternion.Euler(23.001f,-47,61), new Quaternion(-qa.x,-qa.y,-qa.z,-qa.w) })
        {
            var n = Node(graph,ShaderGraphNodeKind.QuaternionSlerp); Set(n,"A",V(qa));Set(n,"B",V(other));Case("Slerp/coincident",n,V(Quaternion.SlerpUnclamped(qa,other,.5f)),1);
        }
        foreach (var kind in new[] { ShaderGraphNodeKind.QuaternionToEuler, ShaderGraphNodeKind.RotateByQuaternion, ShaderGraphNodeKind.QuaternionInverse })
        {
            var n = Node(graph,kind);Set(n,"Quaternion",Vector4.zero);
            if(kind==ShaderGraphNodeKind.RotateByQuaternion)Set(n,"In",vector);
            Case("Zero quaternion/"+kind,n,kind==ShaderGraphNodeKind.QuaternionInverse?new Vector4(0,0,0,1):kind==ShaderGraphNodeKind.RotateByQuaternion?(Vector4)vector:Vector4.zero);
        }
        var fingerprint = Node(graph,ShaderGraphNodeKind.EulerToQuaternion);
        var before = Call(T("ShaderGraphCompiler"),null,"CodeFingerprint",graph);
        fingerprint.rotationOrder = ShaderGraphRotationOrder.XYZ;
        Check(!before.Equals(Call(T("ShaderGraphCompiler"),null,"CodeFingerprint",graph)),"Rotation order changes code fingerprint");
        var output = Node(graph,ShaderGraphNodeKind.Output);
        graph.edges.Add(new ShaderGraphEdge {outputNode=multiply.id,outputPort="Out",inputNode=output.id,inputPort="RGBA"});
        var document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=8;
        Layer layer = new ShaderLayerBehaviour {mode=ShaderProgramMode.Graph,draftGraph=graph};Call(typeof(Layer),layer,"AssignNewId");document.layers.Add(layer);
        var target = new RenderTexture(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var pixel = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var active = RenderTexture.active; bool srgb=GL.sRGBWrite;target.Create();
        try
        {
            using(var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false))
            {
                var saved=((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
                foreach(var n in graph.nodes)Check(saved.FindNode(n.id).rotationOrder==n.rotationOrder&&saved.FindNode(n.id).option==n.option,"JSON settings " + n.kind);
            }
            var requestType=T("ShaderProgramRuntime+CompilationRequest");var vmType=T("ShaderGraphPreviewVM");
            var context=Activator.CreateInstance(T("LayerRenderContext"),new object[]{document,null,8,8,1f,false,false,false});
            async Task Until(Func<bool> done,string label){var clock=Stopwatch.StartNew();while(!done()){if(clock.Elapsed.TotalSeconds>60)throw new TimeoutException(label);await Task.Delay(20);}}
            Color Read(){var old=RenderTexture.active;try{RenderTexture.active=target;pixel.ReadPixels(new Rect(2,2,1,1),0,0);pixel.Apply();return pixel.GetPixel(0,0);}finally{RenderTexture.active=old;}}
            foreach(var c in cases)
            {
                using var request=(IDisposable)Activator.CreateInstance(requestType,F,null,new object[]{graph,c.node.id,"Out",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",false},null);
                object[] completed={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",completed),c.label+" HLSL");
                using var runtime=(IDisposable)completed[0];var rt=runtime.GetType();
                foreach(var diagnostic in (System.Collections.IEnumerable)rt.GetProperty("DiagnosticMessages",F).GetValue(runtime))
                    Check(!new[]{"Warning","Error"}.Contains(diagnostic.GetType().GetField("Severity",F).GetValue(diagnostic).ToString()),c.label+": "+diagnostic);
                Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"PrepareCoordinates",layer,context);
                GL.sRGBWrite=false;Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);Color native=Read();
                object[] build={graph,c.node.id,"Out",null};Check((bool)Call(vmType,null,"TryCreate",build),c.label+" uses SVM");
                using var vm=(IDisposable)build[3];await Until(()=>
                {
                    bool ready=(bool)Call(vmType,vm,"UpdatePreparation");
                    if((bool)vmType.GetProperty("Failed",F).GetValue(vm))
                    {
                        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/GraphPreviewVM.shader");
                        throw new Exception(c.label+" SVM failed: "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
                    }
                    return ready;
                },c.label+" SVM");
                Check((bool)Call(vmType,vm,"Render",target,0f),c.label+" SVM render");Color svm=Read();
                foreach(Color value in new[]{native,svm})
                {
                    for(int lane=0;lane<4;lane++)Check(float.IsFinite(value[lane]),c.label+" finite");
                    if(c.interpretation!=0)
                    {
                        var q=c.interpretation==1?Q(value):Euler(new Vector3(value.r,value.g,value.b)*(c.node.option==0?Mathf.Rad2Deg:c.node.option==2?360:1),c.node.rotationOrder);
                        Check(1-Mathf.Abs(Quaternion.Dot(q.normalized,new Quaternion(c.expected.x,c.expected.y,c.expected.z,c.expected.w).normalized))<.00001f,c.label+" orientation "+value);
                        if(c.interpretation==1)Check(Mathf.Abs(Vector4.Dot((Vector4)value,(Vector4)value)-1)<.00001f,c.label+" normalized");
                    }
                    else for(int lane=0;lane<(c.node.kind==ShaderGraphNodeKind.RotateByQuaternion||c.node.kind==ShaderGraphNodeKind.QuaternionToEuler?3:4);lane++)
                        Check(Mathf.Abs(value[lane]-c.expected[lane])<.0001f*Mathf.Max(1,Mathf.Abs(c.expected[lane])),c.label+" expected "+c.expected+", got "+value);
                }
                for(int lane=0;lane<4;lane++)Check(Mathf.Abs(native[lane]-svm[lane])<.0001f*Mathf.Max(1,Mathf.Abs(native[lane])),c.label+" HLSL/SVM parity");
            }
            return new{status="passed",checks,cases=cases.Count,failures=Array.Empty<string>(),message="Six rotation orders, three units, singular angles, non-unit/zero quaternions, composition, inverse, shortest-path/extrapolated Slerp, native/SVM and JSON."};
        }
        catch(Exception e){return new{status="failed",checks,failures=new[]{e.ToString()},message=e.Message};}
        finally{RenderTexture.active=active;GL.sRGBWrite=srgb;Object.DestroyImmediate(pixel);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(document);}
    }

    public static async Task<object> UI()
    {
        WhimTexDocument document=null;ShaderGraphWindow window=null;
        try
        {
            document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=8;
            var shader=new ShaderLayerBehaviour();document.layers.Add(shader);Call(typeof(WhimTexDocument),document,"NormalizeModel");
            var graph=shader.draftGraph;var ids=new List<string>();
            foreach(var kind in kinds){var n=Node(graph,kind);n.position=new Vector2(ids.Count%3*330,ids.Count/3*350);n.previewExpanded=false;ids.Add(n.id);}
            window=ScriptableObject.CreateInstance<ShaderGraphWindow>();typeof(ShaderGraphWindow).GetField("document",F).SetValue(window,document);typeof(ShaderGraphWindow).GetField("layerId",F).SetValue(window,document.layers[0].Id);
            window.ShowUtility();window.position=new Rect(60,60,1100,800);window.CreateGUI();await Task.Delay(250);
            var canvas=(GraphView)typeof(ShaderGraphWindow).GetField("canvas",F).GetValue(window);
            int checks=0;
            foreach(string id in ids.Take(2))
            {
                var view=canvas.nodes.Single(n=>n.viewDataKey==id);
                var order=view.Query<DropdownField>().ToList().Single(f=>f.label=="Rotation Order");
                if(order.value!="ZXY"||order.choices.Count!=6)throw new Exception("Rotation order UI defaults");checks++;
                if(view.Query<DropdownField>().ToList().Single(f=>f.label=="Unit").value!="Degrees")throw new Exception("Unit UI defaults");checks++;
                Undo.FlushUndoRecordObjects();Undo.ClearUndo(document);Undo.IncrementCurrentGroup();order.value="XYZ";Undo.FlushUndoRecordObjects();await Task.Delay(50);
                if(graph.FindNode(id).rotationOrder!=ShaderGraphRotationOrder.XYZ)throw new Exception("Dropdown updates node");checks++;
                Undo.PerformUndo();await Task.Delay(70);graph=((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
                if(graph.FindNode(id).rotationOrder!=ShaderGraphRotationOrder.ZXY)throw new Exception("Undo rotation order");checks++;
                Undo.PerformRedo();await Task.Delay(70);graph=((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
                if(graph.FindNode(id).rotationOrder!=ShaderGraphRotationOrder.XYZ)throw new Exception("Redo rotation order");checks++;
            }
            return new{status="passed",checks,failures=Array.Empty<string>(),message="Both conversion node dropdowns and owned Undo/Redo."};
        }
        finally{if(window!=null)window.Close();if(document!=null){Undo.ClearUndo(document);Object.DestroyImmediate(document);}}
    }
}
