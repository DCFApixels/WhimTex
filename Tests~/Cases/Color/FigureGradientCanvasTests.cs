using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class FigureGradientCanvasTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static object Call(object o, string n, params object[] args) => o.GetType().GetMethods(F)
        .Single(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(o, args);
    static void Check(bool ok, string message) => UnityBRun.Check(ok, message);
    public static string Start(string id) => UnityBRun.Start(id, "Shape/Vector/Boolean Group gradient handles", Run);
    public static string Poll(string id) => UnityBRun.Poll(id);
    public static Task<string> Cancel(string id) => UnityBRun.Cancel(id);
    public static string Cleanup(string id) => UnityBRun.Cleanup(id);
    static void Pointer(VisualElement canvas, Vector2 p, EventType type, bool control = false)
    {
        var source = new Event { type = type, mousePosition = canvas.LocalToWorld(p), button = 0, clickCount = 1,
            modifiers = control ? EventModifiers.Control : EventModifiers.None };
        if (type == EventType.MouseDown) { using var e = PointerDownEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
        else if (type == EventType.MouseUp) { using var e = PointerUpEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
        else { using var e = PointerMoveEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
    }
    static async Task Run()
    {
        var restore = new List<Action>();
        foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings" })
        {
            bool had = EditorPrefs.HasKey(key); string value = EditorPrefs.GetString(key);
            restore.Add(() => { if (had) EditorPrefs.SetString(key, value); else EditorPrefs.DeleteKey(key); });
        }
        const string scale = "DCFApixels.WhimTex.Canvas.PaintingScale";
        bool hadScale = EditorPrefs.HasKey(scale); float oldScale = EditorPrefs.GetFloat(scale);
        restore.Add(() => { if (hadScale) EditorPrefs.SetFloat(scale, oldScale); else EditorPrefs.DeleteKey(scale); });
        WhimTexWindow window = null;
        try
        {
            var document = UnityBRun.Create<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = 192; document.height = 128;
            window = UnityBRun.Create<WhimTexWindow>(); Call(window, "SetDocument", document);
            window.ShowUtility(); window.position = new Rect(120, 100, 1100, 780); window.CreateGUI();
            for (int i = 0; i < 6; i++) await UnityBRun.NextUpdate();
            var canvas = (VisualElement)Get(window, "toolkitCanvas"); var manip = Get(window, "gradientCanvasManipulator");
            bool Available() => (bool)window.GetType().GetProperty("IsGradientCanvasAvailable", F).GetValue(window);
            Vector2 Point(float t) => (Vector2)Call(manip, "AngularPoint", t);
            Vector2 Angle(float a) => (Vector2)Call(manip, "AngularPointAtAngle", a);
            Vector2 Center() => (Vector2)manip.GetType().GetProperty("AngularCenterPoint", F).GetValue(manip);
            void Endpoints(out Vector2 a, out Vector2 b)
            { object[] args = { null, null }; Call(manip, "Points", args); a = (Vector2)args[0]; b = (Vector2)args[1]; }
            void Drag(Vector2 a, Vector2 b, bool release = true, bool ctrl = false)
            {
                Pointer(canvas, a, EventType.MouseDown, ctrl); Check(canvas.HasPointerCapture(PointerId.mousePointerId), "Fill handle captures pointer");
                Pointer(canvas, b, EventType.MouseDrag, ctrl); if (release) Pointer(canvas, b, EventType.MouseUp, ctrl);
            }
            void Escape()
            { using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }); e.target = canvas; canvas.SendEvent(e); }
            foreach (string kind in new[] { "shape", "vector", "boolean" })
            {
                document.layers.Clear();
                var shape = new ShapeLayerBehaviour { fillMode = VectorFillMode.LinearGradient };
                var layer = shape.Owner; layer.transform.scaleF = new Vector2(.62f, .65f);
                if (kind == "vector") layer.SetBehaviour(ShapeToVector.Convert(shape, document));
                if (kind == "boolean")
                {
                    layer.SetBehaviour(new BooleanGroupLayerBehaviour { referenceSize = new Vector2(300, 175),
                        style = new VectorShape { fillMode = VectorFillMode.LinearGradient } });
                    var rectangle = new ShapeLayerBehaviour { fillMode = VectorFillMode.LinearGradient }.Owner;
                    rectangle.transform.scaleF = new Vector2(.7f, .7f);
                    var ellipse = new ShapeLayerBehaviour { kind = ShapeLayerBehaviour.ShapeKind.Ellipse }.Owner;
                    ellipse.transform.scaleF = new Vector2(.3f, .3f); ellipse.transform.positionF = new Vector2(35, 10);
                    ellipse.booleanOperation = VectorBooleanOperation.Subtract;
                    layer.children.Add(ellipse); layer.children.Add(rectangle);
                }
                var parent = new Layer(new GroupLayerBehaviour()); parent.transform.rotationF = 17;
                parent.children.Add(layer); document.layers.Add(parent); Call(document, "NormalizeModel");
                string id = layer.Id;
                Layer Current() => (Layer)Call(document, "FindLayer", id);
                object Model() => Current().Behaviour is BooleanGroupLayerBehaviour g ? g.style :
                    Current().Behaviour is VectorLayerBehaviour v ? v.shapes[0] : (object)Current().Behaviour;
                string Children() => Current().children == null ? "" : string.Join("|", Current().children.Select(child => JsonUtility.ToJson(child)));
                string originalChildren = Children();
                T Read<T>(string n) => (T)Model().GetType().GetField(n).GetValue(Model());
                void Write(string n, object value) => Model().GetType().GetField(n).SetValue(Model(), value);
                var palette = new WhimTexGradient { WrapMode = WhimTexGradientWrapMode.Repeat };
                palette.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.green, .4f), new GradientColorKey(Color.blue, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                Write("gradient", palette);
                var baseTool = Get(window, "canvasTool"); Call(window, "ChangeCanvasTool", Enum.Parse(baseTool.GetType(), "None"));
                Call(window, "SelectOnlyLayer", id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "None" && Available(), "Selecting a gradient-filled figure preserves the base tool and offers shared handles");
                Call(window, "SetCanvasTool", Enum.Parse(baseTool.GetType(), "GradientHandles"));
                Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "GradientHandles", "Manually selected figure handles survive refresh");
                var gradientLayer = new GradientLayerBehaviour().Owner;
                document.layers.Add(gradientLayer); Call(document, "NormalizeModel");
                Call(window, "ChangeCanvasTool", Enum.Parse(baseTool.GetType(), "None"));
                Call(window, "SelectOnlyLayer", gradientLayer.Id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "GradientHandles", "Selecting a Gradient layer still activates handles automatically");
                Call(window, "SelectOnlyLayer", id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "None", "Switching from Gradient to a figure restores the base tool");
                var otherFigure = new ShapeLayerBehaviour { fillMode = VectorFillMode.LinearGradient }.Owner;
                document.layers.Add(otherFigure); Call(document, "NormalizeModel");
                Call(window, "SetCanvasTool", Enum.Parse(baseTool.GetType(), "GradientHandles"));
                Call(window, "SelectOnlyLayer", otherFigure.Id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "GradientHandles", "Manual handles remain active on another gradient-filled figure");
                Call(window, "SelectOnlyLayer", gradientLayer.Id); Call(window, "RefreshToolkitInterface", true);
                Call(window, "SelectOnlyLayer", id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "GradientHandles", "Passing through a Gradient layer preserves manual handle selection");
                otherFigure.SetBehaviour(new ShapeLayerBehaviour());
                Call(window, "SelectOnlyLayer", otherFigure.Id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "None" && !Available(), "A figure without a gradient restores the base tool");
                Call(window, "SelectOnlyLayer", id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "None", "Unavailable handles end manual carry-over");
                Call(window, "SetCanvasTool", Enum.Parse(baseTool.GetType(), "GradientHandles"));
                Call(window, "SetCanvasTool", Enum.Parse(baseTool.GetType(), "None"));
                otherFigure.SetBehaviour(new ShapeLayerBehaviour { fillMode = VectorFillMode.LinearGradient });
                Call(window, "SelectOnlyLayer", otherFigure.Id); Call(window, "RefreshToolkitInterface", true);
                Check(Get(window, "canvasTool").ToString() == "None", "Choosing a base tool ends manual carry-over");
                Call(window, "SelectOnlyLayer", id); Call(window, "RefreshToolkitInterface", true);
                document.layers.Remove(otherFigure);
                document.layers.Remove(gradientLayer);
                for (int i = 0; i < 4; i++) await UnityBRun.NextUpdate();
                foreach (var mode in new[] { VectorFillMode.LinearGradient, VectorFillMode.RadialGradient, VectorFillMode.SquareGradient })
                {
                    Write("fillMode", mode); Write("gradientStart", new Vector2(.3f, .4f)); Write("gradientEnd", new Vector2(.7f, .6f));
                    Call(window, "RefreshToolkitInterface", true);
                    var tool = Get(window, "canvasTool"); Call(window, "ChangeCanvasTool", Enum.Parse(tool.GetType(), "GradientHandles"));
                    Check(Available(), "Fill handles available for " + mode);
                    string transform = JsonUtility.ToJson(Current().transform);
                    Endpoints(out var a, out var b); Vector2 d = (b - a).normalized, offset = new Vector2(-d.y, d.x) * 18;
                    Check((MouseCursor)Call(manip,"GetCursor",a)==MouseCursor.Pan,"Fill key has a hand cursor: "+mode);
                    Check((MouseCursor)Call(manip,"GetCursor",b+offset)==MouseCursor.Pan,"Fill endpoint handle has a hand cursor: "+mode);
                    Check((MouseCursor)Call(manip,"GetCursor",(Vector2)Call(manip,"KeyPosition",a,b,.8f))==MouseCursor.ArrowPlus,"Fill insertion area has a plus cursor: "+mode);
                    Vector2 delta = new Vector2(17, -11); var start = Read<Vector2>("gradientStart"); var end = Read<Vector2>("gradientEnd");
                    Drag(b + offset, b + offset + delta);
                    Endpoints(out var nextA, out var nextB);
                    Check(Vector2.Distance(nextB, b + delta) < .03f && Vector2.Distance(nextA, a) < .03f, "End moves in canvas space without moving center");
                    Check(JsonUtility.ToJson(Current().transform) == transform, "Fill editing preserves figure transform");
                    Undo.PerformUndo(); await UnityBRun.NextUpdate();
                    Check(Read<Vector2>("gradientStart") == start && Read<Vector2>("gradientEnd") == end, "Fill anchor Undo");
                    Endpoints(out a, out b); d = (b-a).normalized; offset = new Vector2(-d.y,d.x)*18;
                    Drag(a + offset, a + offset + delta, false);
                    Endpoints(out nextA, out nextB);
                    Check(Vector2.Distance(nextA,a+delta)<.03f,"Start/center tracks pointer under parent transform");
                    if(mode==VectorFillMode.LinearGradient) Check(Vector2.Distance(nextB,b)<.03f,"Linear opposite end remains fixed");
                    else Check((Read<Vector2>("gradientEnd")-Read<Vector2>("gradientStart")-(end-start)).sqrMagnitude<1e-8f,"Nonlinear center preserves local radius/direction");
                    Escape(); Check(Read<Vector2>("gradientStart")==start && Read<Vector2>("gradientEnd")==end,"Escape restores fill anchors exactly");
                }
                Write("fillMode", VectorFillMode.AngularGradient); Write("gradientStart", new Vector2(.5f,.5f)); Write("gradientEnd", new Vector2(.85f,.5f));
                Call(window,"RefreshToolkitInterface",true);
                var selectedTool=Get(window,"canvasTool");Call(window,"ChangeCanvasTool",Enum.Parse(selectedTool.GetType(),"GradientHandles"));
                string beforeTransform=JsonUtility.ToJson(Current().transform);
                var initialStart=Read<Vector2>("gradientStart");var initialEnd=Read<Vector2>("gradientEnd");
                string beforeGradient=JsonUtility.ToJson(Read<WhimTexGradient>("gradient"));
                Drag(Point(0),Angle(20),ctrl:true);
                Check(Read<float>("gradientRepetitions")==2,"Angular fill end snaps to integer repetitions");
                Check((Read<Vector2>("gradientEnd")-initialEnd).sqrMagnitude<1e-8f,"End boundary leaves starting ray fixed");
                Check(JsonUtility.ToJson(Current().transform)==beforeTransform,"Angular fill leaves figure transform untouched");
                Undo.PerformUndo();await UnityBRun.NextUpdate();
                Check(Read<float>("gradientRepetitions")==1 && Read<Vector2>("gradientEnd")==initialEnd,"Angular range Undo");
                Drag(Point(0),Angle(340),false,true);
                Check(Read<float>("gradientRepetitions")==2,"Angular fill start snaps to integer repetitions");
                Check(Vector2.Distance(Point(1),Angle(180))<.03f,"Angular fill opposite endpoint remains fixed");
                Escape();Check(Read<Vector2>("gradientEnd")==initialEnd && Read<float>("gradientRepetitions")==1,"Angular Escape restores exact direction and range");
                Drag(Vector2.Lerp(Center(),Point(0),.5f),Vector2.Lerp(Center(),Angle(215),.5f));
                Check(Read<float>("gradientRepetitions")==1 && (Read<Vector2>("gradientEnd")-initialEnd).sqrMagnitude>.001f,"Ray rotates fill without opening it");
                var center=Center();Drag(center,center+new Vector2(15,9));
                Check(Vector2.Distance(Center(),center+new Vector2(15,9))<.03f,"Angular center translates fill independently");
                Drag(Point(.4f),Point(.48f));
                Check(Mathf.Abs(Read<WhimTexGradient>("gradient").ColorKeys[1].time-.48f)<.001f,"Angular fill color key follows arc");
                Drag(Point(0),Angle(215-60));
                Call(window,"RefreshToolkitInterface",true);
                for(int i=0;i<5;i++)await UnityBRun.NextUpdate();
                var capture=new Capture {owner=window,path=UnityBRun.EvidencePath(kind+"-gradient-handles.png"),pickingMode=PickingMode.Ignore};
                capture.style.position=Position.Absolute;capture.style.width=capture.style.height=1;window.rootVisualElement.Add(capture);
                try {for(int i=0;i<40 && !capture.done.Task.IsCompleted;i++){window.Repaint();await UnityBRun.NextUpdate();}Check(capture.done.Task.IsCompleted,"Fill canvas capture");await capture.done.Task;}
                finally {capture.RemoveFromHierarchy();}
                var savedTransform = Current().transform;
                foreach (bool mirror in new[] { false, true })
                {
                    var projective = savedTransform.ToMatrix(document.width, document.height);
                    projective.m20 = .12; projective.m21 = -.08;
                    if (mirror) projective = projective * ProjectiveMatrix.Translate(1,0) * ProjectiveMatrix.Scale(-1,1);
                    Check(Current().transform.TrySetMatrix(projective), "Valid perspective fixture");
                    string geometry = JsonUtility.ToJson(Current().transform);
                    Write("fillMode",VectorFillMode.LinearGradient); Write("gradientStart",new Vector2(.3f,.4f)); Write("gradientEnd",new Vector2(.7f,.6f));
                    Call(window,"RefreshToolkitInterface",true);
                    Endpoints(out var a,out var b); Vector2 direction=(b-a).normalized,offset=new Vector2(-direction.y,direction.x)*18;
                    Vector2 delta=new Vector2(14,-8); Drag(b+offset,b+offset+delta);
                    Endpoints(out var na,out var nb);
                    Check(Vector2.Distance(nb,b+delta)<.04f && Vector2.Distance(na,a)<.04f,"Perspective endpoint tracks canvas pointer, mirror="+mirror);
                    var keyStart=(Vector2)Call(manip,"KeyPosition",na,nb,.48f);
                    var keyEnd=(Vector2)Call(manip,"KeyPosition",na,nb,.6f);
                    Drag(keyStart,keyEnd);
                    Check(Mathf.Abs(Read<WhimTexGradient>("gradient").ColorKeys[1].time-.6f)<.001f,"Perspective palette projection");
                    Write("fillMode",VectorFillMode.AngularGradient);Write("gradientStart",new Vector2(.5f,.5f));Write("gradientEnd",new Vector2(.85f,.5f));Write("gradientRepetitions",1f);
                    Call(window,"RefreshToolkitInterface",true);
                    Drag(Point(0),Angle(20),ctrl:true);
                    Check(Read<float>("gradientRepetitions")==2,"Perspective angular endpoint and Ctrl");
                    Check(JsonUtility.ToJson(Current().transform)==geometry,"Perspective fill editing leaves geometry unchanged");
                    // Restore the interior key for the next fixture, keeping endpoints independent.
                    Write("gradient",palette.Clone());
                    var keys=Read<WhimTexGradient>("gradient").ColorKeys; keys[1].time=.48f;
                    Read<WhimTexGradient>("gradient").SetKeys(keys,Read<WhimTexGradient>("gradient").AlphaKeys);
                }
                Current().transform=savedTransform;
                Write("fillMode",VectorFillMode.Solid);Call(window,"RefreshToolkitInterface",true);Check(!Available(),"Solid fill hides gradient handles");
                Write("fillMode",VectorFillMode.AngularGradient);
                if(kind=="boolean")
                {
                    Check(Children()==originalChildren,"Group fill edits preserve all child geometry and appearance");
                    var group=(BooleanGroupLayerBehaviour)Current().Behaviour;
                    foreach(VectorBooleanOperation operation in Enum.GetValues(typeof(VectorBooleanOperation)))
                    {
                        foreach(var child in Current().children)child.booleanOperation=operation;
                        group.appearance=BooleanGroupAppearance.Group;Call(window,"RefreshToolkitInterface",true);
                        Check(Available(),"Group appearance handles available for "+operation);
                        group.appearance=BooleanGroupAppearance.Layers;Call(window,"RefreshToolkitInterface",true);
                        Check(Available()==(operation!=VectorBooleanOperation.Combine),"Only Combine Layers hides group fill handles");
                    }
                    string childId=Current().children[1].Id;
                    Call(window,"SelectOnlyLayer",childId);Call(window,"RefreshToolkitInterface",true);
                    Check(Available(),"Combine Layers exposes child gradient handles");
                    group.appearance=BooleanGroupAppearance.Group;Call(window,"RefreshToolkitInterface",true);
                    Check(!Available(),"Combine Group suppresses child gradient handles");
                    Call(window,"SelectOnlyLayer",id);Call(window,"RefreshToolkitInterface",true);
                    Check(Available(),"Combine Group exposes common fill handles");
                }
                document.layers[0].SetBehaviour(new BooleanGroupLayerBehaviour());Call(window,"RefreshToolkitInterface",true);Check(!Available(),"Boolean-suppressed fill hides handles");
                document.layers[0].SetBehaviour(new GroupLayerBehaviour());Call(window,"RefreshToolkitInterface",true);Check(Available(),"Ordinary group allows child fill editing");
                Check(JsonUtility.ToJson(Current().transform)==beforeTransform,"All fill edits preserve figure geometry transform");
                Write("stroke",true); Write("strokeWidth",8f); Write("strokeMode",VectorFillMode.LinearGradient);
                Write("strokeGradient",palette.Clone());
                string strokeChildrenBefore=Children();
                string fillPaletteBefore=JsonUtility.ToJson(Read<WhimTexGradient>("gradient"));
                Vector2 fillStartBefore=Read<Vector2>("gradientStart"),fillEndBefore=Read<Vector2>("gradientEnd");
                float fillRangeBefore=Read<float>("gradientRepetitions");
                Call(window,"RefreshToolkitInterface",true);
                var channel=window.rootVisualElement.Q<PopupField<string>>("gradientHandleChannel");
                Check(channel!=null && channel.enabledInHierarchy,"Both gradient targets can be chosen");
                channel.value="Stroke";
                Call(window,"ChangeCanvasTool",Enum.Parse(Get(window,"canvasTool").GetType(),"GradientHandles"));
                foreach(var mode in new[]{VectorFillMode.LinearGradient,VectorFillMode.RadialGradient,VectorFillMode.SquareGradient})
                {
                    Write("strokeMode",mode); Write("strokeGradientStart",new Vector2(.3f,.4f)); Write("strokeGradientEnd",new Vector2(.7f,.6f));
                    Call(window,"RefreshToolkitInterface",true);
                    Endpoints(out var a,out var b);var d=(b-a).normalized;var offset=new Vector2(-d.y,d.x)*18;
                    Check((MouseCursor)Call(manip,"GetCursor",a)==MouseCursor.Pan,"Stroke key has a hand cursor: "+mode);
                    Check((MouseCursor)Call(manip,"GetCursor",b+offset)==MouseCursor.Pan,"Stroke endpoint handle has a hand cursor: "+mode);
                    Vector2 end=Read<Vector2>("strokeGradientEnd"),delta=new Vector2(19,-9);
                    Drag(b+offset,b+offset+delta);Endpoints(out _,out var moved);
                    Check(Vector2.Distance(moved,b+delta)<.04f,"Stroke endpoint follows canvas pointer: "+mode);
                    Undo.PerformUndo();await UnityBRun.NextUpdate();
                    Check(Read<Vector2>("strokeGradientEnd")==end,"Stroke anchor Undo");
                }
                Write("strokeMode",VectorFillMode.AngularGradient);Write("strokeGradientStart",new Vector2(.5f,.5f));Write("strokeGradientEnd",new Vector2(.85f,.5f));
                Call(window,"RefreshToolkitInterface",true);
                Drag(Point(0),Angle(20),ctrl:true);Check(Read<float>("strokeGradientRepetitions")==2,"Stroke angular Ctrl snaps range");
                Drag(Center(),Center()+new Vector2(12,8),false);Escape();
                Check(Read<Vector2>("strokeGradientStart")==new Vector2(.5f,.5f),"Stroke center Escape");
                Drag(Point(.4f),Point(.48f));Check(Mathf.Abs(Read<WhimTexGradient>("strokeGradient").ColorKeys[1].time-.48f)<.001f,"Stroke key follows arc");
                Check(Read<Vector2>("gradientStart")==fillStartBefore && Read<Vector2>("gradientEnd")==fillEndBefore &&
                    Read<float>("gradientRepetitions")==fillRangeBefore && JsonUtility.ToJson(Read<WhimTexGradient>("gradient"))==fillPaletteBefore,"Stroke handles never edit fill");
                Check(JsonUtility.ToJson(Current().transform)==beforeTransform && Children()==strokeChildrenBefore,"Stroke handles preserve geometry");
                Call(window,"RefreshToolkitInterface",true);
                for(int i=0;i<4;i++)await UnityBRun.NextUpdate();
                var strokeCapture=new Capture {owner=window,path=UnityBRun.EvidencePath(kind+"-stroke-gradient-handles.png"),pickingMode=PickingMode.Ignore};
                strokeCapture.style.position=Position.Absolute;strokeCapture.style.width=strokeCapture.style.height=1;window.rootVisualElement.Add(strokeCapture);
                try {for(int i=0;i<40 && !strokeCapture.done.Task.IsCompleted;i++){window.Repaint();await UnityBRun.NextUpdate();}Check(strokeCapture.done.Task.IsCompleted,"Stroke canvas capture");await strokeCapture.done.Task;}
                finally {strokeCapture.RemoveFromHierarchy();}
                channel.value="Fill";Call(window,"RefreshToolkitInterface",true);
                var selectedTarget=window.GetType().GetProperty("gradientCanvasLayer",F).GetValue(window);
                Check(!(bool)selectedTarget.GetType().GetField("IsStroke",F).GetValue(selectedTarget),"Fill channel selected independently");
                Write("fillMode",VectorFillMode.Solid);Call(window,"RefreshToolkitInterface",true);
                Check(Available(),"Stroke-only gradient stays editable");
                Write("stroke",false);Call(window,"RefreshToolkitInterface",true);Check(!Available(),"Disabled stroke has no handles");
                Write("stroke",true);Write("strokeWidth",0f);Call(window,"RefreshToolkitInterface",true);Check(!Available(),"Zero-width stroke has no handles");
                Undo.ClearUndo(document);
            }
        }
        finally {if(window!=null)UnityBRun.CloseOwned(window);foreach(var action in restore)action();}
    }
    sealed class Capture : ImmediateModeElement
    {
        public WhimTexWindow owner;public string path;public readonly TaskCompletionSource<bool> done=new();
        protected override void ImmediateRepaint()
        {
            if(done.Task.IsCompleted)return;Texture2D texture=null;
            try {var rt=RenderTexture.active;int w=rt!=null?rt.width:Mathf.RoundToInt(owner.position.width*EditorGUIUtility.pixelsPerPoint);
                int h=rt!=null?rt.height:Mathf.RoundToInt(owner.position.height*EditorGUIUtility.pixelsPerPoint);
                texture=new Texture2D(w,h,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,w,h),0,0,false);texture.Apply(false);
                System.IO.File.WriteAllBytes(path,texture.EncodeToPNG());done.TrySetResult(true);}
            catch(Exception error){done.TrySetException(error);}finally{if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
