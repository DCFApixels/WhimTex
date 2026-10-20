using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class FreeformGradientCanvasTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object o,string n) => o.GetType().GetField(n,F).GetValue(o);
    static object Call(object o,string n,params object[] args) => o.GetType().GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(o,args);
    static void Check(bool value,string message) => UnityBRun.Check(value,message);
    public static string Start(string id) => UnityBRun.Start(id,"Freeform canvas editing and field synchronization",Run);
    public static string Poll(string id) => UnityBRun.Poll(id);
    public static Task<string> Cancel(string id) => UnityBRun.Cancel(id);
    public static string Cleanup(string id) => UnityBRun.Cleanup(id);
    static void Pointer(VisualElement canvas,Vector2 p,EventType type,int clicks=1)
    {
        var source=new Event {type=type,mousePosition=canvas.LocalToWorld(p),button=0,clickCount=clicks};
        if(type==EventType.MouseDown){using var e=PointerDownEvent.GetPooled(source);e.target=canvas;canvas.SendEvent(e);}
        else if(type==EventType.MouseUp){using var e=PointerUpEvent.GetPooled(source);e.target=canvas;canvas.SendEvent(e);}
        else {using var e=PointerMoveEvent.GetPooled(source);e.target=canvas;canvas.SendEvent(e);}
    }
    static async Task Run()
    {
        var restore=new List<Action>();
        foreach(string key in new[]{"DCFApixels.WhimTex.Canvas.Tool","DCFApixels.WhimTex.Canvas.TransformReturnTool","DCFApixels.WhimTex.Canvas.PaintToolSettings"})
        {
            bool had=EditorPrefs.HasKey(key);string value=EditorPrefs.GetString(key);
            restore.Add(()=>{if(had)EditorPrefs.SetString(key,value);else EditorPrefs.DeleteKey(key);});
        }
        var previousFocus=EditorWindow.focusedWindow;
        WhimTexWindow window=null;
        WhimTexColorPicker ownedPicker=null;
        try
        {
            var doc=UnityBRun.Create<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=512;doc.height=384;
            var g=new GradientLayerBehaviour {gradientType=GradientLayerBehaviour.GradientType.Freeform};doc.layers.Add(g.Owner);Call(doc,"NormalizeModel");
            string id=g.Id;
            window=UnityBRun.Create<WhimTexWindow>();Call(window,"SetDocument",doc);
            window.ShowUtility();window.position=new Rect(100,100,1100,780);window.CreateGUI();
            Call(window,"ChangeCanvasTool",Enum.Parse(Get(window,"canvasTool").GetType(),"None"));
            Call(window,"SelectOnlyLayer",(object)null);Call(window,"RefreshToolkitInterface",true);
            Call(window,"SelectOnlyLayer",id);Call(window,"RefreshToolkitInterface",true);
            for(int i=0;i<6;i++)await UnityBRun.NextUpdate();
            var canvas=(VisualElement)Get(window,"toolkitCanvas");var manip=Get(window,"gradientCanvasManipulator");
            Vector2 View(Vector2 uv)=>(Vector2)Call(manip,"FreeformView",uv);
            void Drag(Vector2 a,Vector2 b,bool release=true)
            {
                Pointer(canvas,a,EventType.MouseDown);Check(canvas.HasPointerCapture(PointerId.mousePointerId),"Point/radius captures pointer");
                Pointer(canvas,b,EventType.MouseDrag);if(release)Pointer(canvas,b,EventType.MouseUp);
            }
            void Key(KeyCode code){using var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=code});e.target=canvas;canvas.SendEvent(e);}
            Check(Get(window,"canvasTool").ToString()=="GradientHandles","Freeform uses the existing automatic gradient handles");
            int count=g.freeform.points.Count;
            var empty=View(new Vector2(.15f,.8f));
            var pointEdge=View(g.freeform.points[0].position)+Vector2.up*11;
            Check((MouseCursor)Call(manip,"GetFreeformCursor",empty)==MouseCursor.ArrowPlus,"Empty canvas offers point creation");
            Check((MouseCursor)Call(manip,"GetCursor",pointEdge)==MouseCursor.Pan,"Hand cursor uses the enlarged point hit area");
            Pointer(canvas,empty,EventType.MouseMove);
            Check(canvas.ClassListContains("whimtex-canvas-cursor--add")&&!canvas.ClassListContains("whimtex-canvas-cursor--move"),"Hover selects the add cursor style");
            Pointer(canvas,pointEdge,EventType.MouseMove);
            Check(canvas.ClassListContains("whimtex-canvas-cursor--pan")&&!canvas.ClassListContains("whimtex-canvas-cursor--add"),"Point hover replaces add with hand");
            var pointBefore=g.freeform.points[0];
            var radius=View(pointBefore.position+Vector2.right*pointBefore.influence);
            Check((MouseCursor)Call(manip,"GetCursor",radius)==MouseCursor.Pan,"Influence handle offers a hand cursor");
            Call(window,"UpdateCanvasCursor",pointEdge,true);
            Check(!canvas.ClassListContains("whimtex-canvas-cursor--move")&&!canvas.ClassListContains("whimtex-canvas-cursor--add"),"Alt navigation does not retain the editing cursor");
            var originalPoints=g.freeform.Clone();
            while(g.freeform.points.Count<FreeformGradient.MaxPoints)g.freeform.points.Add(pointBefore);
            Check((MouseCursor)Call(manip,"GetFreeformCursor",empty)==MouseCursor.Arrow,"Point limit does not advertise creation");
            g.freeform=originalPoints;
            Drag(View(new Vector2(.15f,.8f)),View(new Vector2(.23f,.72f)));
            Check(g.freeform.points.Count==count+1,"Click adds exactly one point");
            Check(Vector2.Distance(g.freeform.points[count].position,new Vector2(.23f,.72f))<.0001f,"New point immediately drags without releasing");
            Undo.PerformUndo();g=(GradientLayerBehaviour)doc.layers[0].Behaviour;
            Check(g.freeform.points.Count==count,"Undo groups addition and drag");
            Undo.PerformRedo();g=(GradientLayerBehaviour)doc.layers[0].Behaviour;
            Check(g.freeform.points.Count==count+1,"Redo restores added point");
            Call(window,"RefreshToolkitInterface",true);
            for(int i=0;i<2;i++)await UnityBRun.NextUpdate();
            var initial=g.freeform.points[0];
            Drag(View(initial.position),View(initial.position+new Vector2(.06f,.07f)));
            Check(Vector2.Distance(g.freeform.points[0].position,initial.position+new Vector2(.06f,.07f))<.0001f,"Existing point moves");
            Check(g.freeform.points.Count==count+1,"Dragging an existing point adds none");
            initial=g.freeform.points[0];
            Drag(View(initial.position+Vector2.right*initial.influence),View(initial.position+Vector2.right*.45f));
            Check(Mathf.Abs(g.freeform.points[0].influence-.45f)<.0001f,"Radius handle adjusts influence");
            string snapshot=JsonUtility.ToJson(g.freeform);
            Drag(View(g.freeform.points[0].position),View(g.freeform.points[0].position+Vector2.up*.1f),false);Key(KeyCode.Escape);
            Check(JsonUtility.ToJson(g.freeform)==snapshot,"Escape restores the complete point data");
            Check(canvas.ClassListContains("whimtex-canvas-cursor--add"),"Escape refreshes the cursor at the released position");
            Drag(View(new Vector2(.9f,.85f)),View(new Vector2(.85f,.85f)),false);Key(KeyCode.Escape);
            Check(JsonUtility.ToJson(g.freeform)==snapshot,"Escape removes an uncommitted new point");
            Call(window,"RefreshToolkitInterface",true);
            for(int i=0;i<2;i++)await UnityBRun.NextUpdate();
            var selector=window.rootVisualElement.Q<PopupField<int>>("freeformPoint");
            Check(selector!=null,"Point inspector is present");selector.value=1;
            var opacity=window.rootVisualElement.Q<Slider>("freeformOpacity");opacity.value=23;
            Check(Mathf.Abs(g.freeform.points[1].color.a-.23f)<.00001f,"Opacity edits selected point only");
            var influence=window.rootVisualElement.Q<FloatField>("freeformInfluence");influence.value=75;
            Check(Mathf.Abs(g.freeform.points[1].influence-.75f)<.00001f,"Influence percent edits selected point");
            var color=window.rootVisualElement.Q<WhimTexColorField>("freeformColor");color.value=new Color(3,-.2f,2,.4f);
            Check(g.freeform.points[1].color==new Color(3,-.2f,2,.4f),"Color field preserves signed HDR and alpha");
            Check(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length==0,"Picker test requires no existing user picker");
            Pointer(canvas,View(g.freeform.points[1].position),EventType.MouseDown,2);
            ownedPicker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single();
            Check(ReferenceEquals(Get(ownedPicker,"document"),doc),"Double-click opens a picker for the owned document");
            Call(ownedPicker,"SetColor",new Color(2,.5f,1,.6f),true);
            Check(g.freeform.points[1].color==new Color(2,.5f,1,.6f),"Canvas picker changes HDR and alpha");
            selector.value=0;snapshot=JsonUtility.ToJson(g.freeform);
            Call(ownedPicker,"SetColor",new Color(.5f,.2f,.7f,1),true);
            Check(JsonUtility.ToJson(g.freeform)==snapshot,"A stale picker cannot edit a newly selected point");
            ownedPicker.Close();ownedPicker=null;selector.value=1;
            canvas.Focus();Key(KeyCode.Delete);Check(g.freeform.points.Count==count,"Delete removes the selected point");
            // Local coordinates continue to map correctly through a transformed parent.
            var parent=new Layer(new GroupLayerBehaviour());doc.layers.Clear();parent.children.Add(g.Owner);doc.layers.Add(parent);
            parent.transform.rotation=23;parent.transform.scale=new Vector2(.8f,1.1f);Call(doc,"NormalizeModel");
            Call(window,"RefreshToolkitInterface",true);
            initial=g.freeform.points[0];Drag(View(initial.position),View(initial.position+new Vector2(.03f,-.04f)));
            Check(Vector2.Distance(g.freeform.points[0].position,initial.position+new Vector2(.03f,-.04f))<.0001f,"Parent-transformed point drag retains local coordinates");
            foreach(var kind in new[]{GradientLayerBehaviour.GradientType.Linear,
                GradientLayerBehaviour.GradientType.Radial,GradientLayerBehaviour.GradientType.Square})
            {
                g.gradientType=kind;
                g.gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.green,.4f),new GradientColorKey(Color.blue,1)},
                    new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                Call(window,"RefreshToolkitInterface",true);
                object[] ends={null,null};Call(manip,"Points",ends);var a=(Vector2)ends[0];var b=(Vector2)ends[1];
                Vector2 key=(Vector2)Call(manip,"KeyPosition",a,b,.4f),add=(Vector2)Call(manip,"KeyPosition",a,b,.8f);
                Check((MouseCursor)Call(manip,"GetCursor",key)==MouseCursor.Pan,kind+" key has a hand cursor");
                Check((MouseCursor)Call(manip,"GetCursor",add)==MouseCursor.ArrowPlus,kind+" line offers key creation");
                Pointer(canvas,add,EventType.MouseDown);Pointer(canvas,add,EventType.MouseUp);
                Check(g.gradient.ColorKeys.Length==4,kind+" plus cursor corresponds to actual key insertion");
                Check(canvas.ClassListContains("whimtex-canvas-cursor--pan")&&!canvas.ClassListContains("whimtex-canvas-cursor--add"),kind+" newly added key immediately shows a hand");
            }
            // Shared Freeform editing on transformed Shape and Vector, independently for fill/stroke.
            foreach(bool vectorMode in new[]{false,true})
            {
                var shape=new ShapeLayerBehaviour {fillMode=VectorFillMode.Freeform,stroke=true,strokeWidth=6,strokeMode=VectorFillMode.Freeform};
                var figure=vectorMode ? ShapeToVector.Convert(shape,doc) : null;
                var item=new Layer(vectorMode ? (LayerBehaviour)figure : shape);
                parent.children.Clear();parent.children.Add(item);Call(doc,"NormalizeModel");
                Call(window,"SelectOnlyLayer",item.Id);
                Call(window,"ChangeCanvasTool",Enum.Parse(Get(window,"canvasTool").GetType(),"None"));
                Call(window,"SetCanvasTool",Enum.Parse(Get(window,"canvasTool").GetType(),"GradientHandles"));
                Call(window,"RefreshToolkitInterface",true);
                for(int i=0;i<3;i++)await UnityBRun.NextUpdate();
                foreach(bool stroke in new[]{false,true})
                {
                    var channel=window.rootVisualElement.Q<PopupField<string>>("gradientHandleChannel");
                    channel.value=stroke ? "Stroke" : "Fill";
                    Call(window,"RefreshToolkitInterface",true);
                    FreeformGradient Data()=>vectorMode ? (stroke ? figure.shapes[0].strokeFreeform : figure.shapes[0].freeform) : (stroke ? shape.strokeFreeform : shape.freeform);
                    var saved=Data().Clone();var p=saved.points[0];string label=(vectorMode?"Vector":"Shape")+(stroke?" stroke":" fill");
                    Check((MouseCursor)Call(manip,"GetCursor",View(p.position))==MouseCursor.Pan,label+" hand cursor");
                    Drag(View(p.position),View(p.position+new Vector2(.025f,.035f)));
                    Check(Vector2.Distance(Data().points[0].position,p.position+new Vector2(.025f,.035f))<.0001f,label+" transformed point drag");
                    int n=Data().points.Count;
                    Drag(View(new Vector2(.16f,.81f)),View(new Vector2(.21f,.79f)));
                    Check(Data().points.Count==n+1,label+" click creates and drags");
                    Call(window,"RefreshToolkitInterface",true);
                    var panel=window.rootVisualElement.Q<VisualElement>(stroke?"figureStrokeFreeform":"figureFillFreeform");
                    Check(panel!=null&&panel.resolvedStyle.display!=DisplayStyle.None,label+" inspector present");
                    panel.Q<PopupField<int>>("freeformPoint").value=0;
                    panel.Q<Slider>("freeformOpacity").value=31;
                    Check(Mathf.Abs(Data().points[0].color.a-.31f)<.00001f,label+" opacity binds selected point, got "+Data().points[0].color.a+" all="+string.Join(",",Data().points.Select(x=>x.color.a)));
                    panel.Q<FloatField>("freeformInfluence").value=47;
                    Check(Mathf.Abs(Data().points[0].influence-.47f)<.00001f,label+" influence binds selected point");
                    panel.Q<WhimTexColorField>("freeformColor").value=new Color(2,.3f,1,.6f);
                    Check(Data().points[0].color==new Color(2,.3f,1,.6f),label+" HDR field");
                    string beforeEscape=JsonUtility.ToJson(Data());
                    Drag(View(Data().points[0].position),View(Data().points[0].position+Vector2.up*.04f),false);Key(KeyCode.Escape);
                    Check(JsonUtility.ToJson(Data())==beforeEscape,label+" Escape restores points");
                    canvas.Focus();Key(KeyCode.Delete);Check(Data().points.Count==n,label+" Delete removes point");
                }
            }
            Undo.ClearUndo(doc);
        }
        finally
        {
            if(ownedPicker!=null)ownedPicker.Close();
            if(window!=null)UnityBRun.CloseOwned(window);
            foreach(var reset in restore)reset();
            if(previousFocus!=null)previousFocus.Focus();
        }
    }
}
