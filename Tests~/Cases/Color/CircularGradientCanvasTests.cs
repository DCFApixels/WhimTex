using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class CircularGradientCanvasTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static object Call(object o, string n, params object[] args) => o.GetType().GetMethods(F)
        .Single(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(o, args);
    static void Check(bool ok, string message) => UnityBRun.Check(ok, message);
    public static string Start(string id) => UnityBRun.Start(id, "Circular gradient canvas", Run);
    public static string Poll(string id) => UnityBRun.Poll(id);
    public static Task<string> Cancel(string id) => UnityBRun.Cancel(id);
    public static string Cleanup(string id) => UnityBRun.Cleanup(id);
    static void Pointer(VisualElement canvas, Vector2 p, EventType type, int clicks = 1, bool control = false)
    {
        var source = new Event { type = type, mousePosition = canvas.LocalToWorld(p), button = 0, clickCount = clicks,
            modifiers = control ? EventModifiers.Control : EventModifiers.None };
        if (type == EventType.MouseDown)
        { using var e = PointerDownEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
        else if (type == EventType.MouseUp)
        { using var e = PointerUpEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
        else
        { using var e = PointerMoveEvent.GetPooled(source); e.target = canvas; canvas.SendEvent(e); }
    }
    static async Task Run()
    {
        if (Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0)
            throw new UnityBSkipException("User color picker is open");
        var restore = new List<Action>();
        foreach (string key in new[] {"DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool", "DCFApixels.WhimTex.Canvas.PaintToolSettings"})
        {
            bool had = EditorPrefs.HasKey(key); string value = EditorPrefs.GetString(key);
            restore.Add(() => { if (had) EditorPrefs.SetString(key, value); else EditorPrefs.DeleteKey(key); });
        }
        const string scale = "DCFApixels.WhimTex.Canvas.PaintingScale";
        bool hadScale = EditorPrefs.HasKey(scale); float oldScale = EditorPrefs.GetFloat(scale);
        restore.Add(() => { if (hadScale) EditorPrefs.SetFloat(scale, oldScale); else EditorPrefs.DeleteKey(scale); });
        WhimTexWindow window = null;
        WhimTexColorPicker picker = null;
        try
        {
            var document = UnityBRun.Create<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 128;
            var gradient = new GradientLayerBehaviour { gradientType = GradientLayerBehaviour.GradientType.Circular };
            gradient.gradient = new WhimTexGradient { Mode = WhimTexGradientMode.Linear };
            gradient.gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.green,.4f),new GradientColorKey(Color.blue,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            gradient.transform.scale = new Vector2(.55f,.55f);
            document.layers.Add(gradient.Owner); Call(document,"NormalizeModel");
            string id = gradient.Id;
            GradientLayerBehaviour Current() => (GradientLayerBehaviour)((Layer)Call(document,"FindLayer",id)).Behaviour;
            window = UnityBRun.Create<WhimTexWindow>(); Call(window,"SetDocument",document);
            window.ShowUtility(); window.position = new Rect(100,100,1050,760); window.CreateGUI();
            for(int i=0;i<6;i++) await UnityBRun.NextUpdate();
            Call(window,"SelectOnlyLayer",id); Call(window,"RefreshToolkitInterface",true);
            var tool=Get(window,"canvasTool");
            Call(window,"ChangeCanvasTool",Enum.Parse(tool.GetType(),"GradientHandles"));
            Call(window,"RefreshToolkitInterface",true);
            for(int i=0;i<3;i++) await UnityBRun.NextUpdate();
            var canvas=(VisualElement)Get(window,"toolkitCanvas");
            var manip=Get(window,"gradientCanvasManipulator");
            Check((bool)window.GetType().GetProperty("IsGradientCanvasEnabled",F).GetValue(window),"Circular tool available");
            Check(string.IsNullOrEmpty(((VisualElement)Get(window,"gradientCanvasOverlay")).tooltip),"No tooltip on canvas handles");
            Vector2 Point(float t) => (Vector2)Call(manip,"CircularPoint",t);
            Vector2 Angle(float a) => (Vector2)Call(manip,"CircularPointAtAngle",a);
            Vector2 Center() => (Vector2)manip.GetType().GetProperty("CircularCenterPoint",F).GetValue(manip);
            void Drag(Vector2 a,Vector2 b,bool release=true,bool control=false)
            {
                Pointer(canvas,a,EventType.MouseDown,control:control);
                Check(canvas.HasPointerCapture(PointerId.mousePointerId),"Gesture captures mouse");
                Pointer(canvas,b,EventType.MouseDrag,control:control);
                if(release)Pointer(canvas,b,EventType.MouseUp,control:control);
            }
            void Control(bool pressed)
            {
                var source=new Event {type=pressed?EventType.KeyDown:EventType.KeyUp,keyCode=KeyCode.LeftControl,
                    modifiers=pressed?EventModifiers.Control:EventModifiers.None};
                if(pressed){using var e=KeyDownEvent.GetPooled(source);e.target=canvas;canvas.SendEvent(e);}
                else {using var e=KeyUpEvent.GetPooled(source);e.target=canvas;canvas.SendEvent(e);}
            }
            async Task Preview(string leaf)
            {
                Call(window,"RefreshToolkitInterface",true);
                for(int i=0;i<6;i++)await UnityBRun.NextUpdate();
                var capture=new Capture { owner=window, path=UnityBRun.EvidencePath(leaf), pickingMode=PickingMode.Ignore };
                capture.style.position=Position.Absolute; capture.style.width=capture.style.height=1;
                window.rootVisualElement.Add(capture);
                try
                {
                    for(int i=0;i<40 && !capture.done.Task.IsCompleted;i++){window.Repaint();await UnityBRun.NextUpdate();}
                    Check(capture.done.Task.IsCompleted,"Actual canvas capture completed");await capture.done.Task;
                }
                finally {capture.RemoveFromHierarchy();}
            }
            await Preview("circular-closed.png");
            string palette=JsonUtility.ToJson(Current().gradient);
            // Each half has a separate hit target even though both endpoints coincide.
            foreach(bool first in new[]{true,false})
            {
                Vector2 tangent=(Angle(.1f)-Point(0)).normalized;
                Pointer(canvas,Point(0)+tangent*(first?4:-4),EventType.MouseDown,2);
                picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single();
                Check((int)Get(manip,"selected")== (first?0:2),"Double click selects the intended endpoint half");
                Check(palette==JsonUtility.ToJson(Current().gradient),"Opening color picker does not change either key");
                UnityBRun.CloseOwned(picker);picker=null;window.Focus();
            }
            Drag(Point(0),Angle(-60));
            Check(Mathf.Abs(Current().circularRepetitions-1.2f)<.001f && Mathf.Abs(Current().circularStartAngle)<.001f,"Clockwise drag opens end boundary");
            Check(palette==JsonUtility.ToJson(Current().gradient),"Separating handles preserves color/alpha keys");
            await Preview("circular-open.png");
            Undo.PerformUndo();await UnityBRun.NextUpdate();
            Check(Current().circularRepetitions==1 && Current().circularStartAngle==0,"Native Undo restores closed circle");
            Undo.PerformRedo();await UnityBRun.NextUpdate();
            Check(Mathf.Abs(Current().circularRepetitions-1.2f)<.001f,"Native Redo restores range");
            Drag(Point(1),Point(0));
            Check(Current().circularRepetitions==1,"Endpoints snap together");
            Drag(Point(0),Angle(60),false);
            Check(Mathf.Abs(Current().circularStartAngle-60)<.001f && Mathf.Abs(Current().circularRepetitions-1.2f)<.001f,"Counterclockwise opens start boundary");
            using(var e=KeyDownEvent.GetPooled(new Event {type=EventType.KeyDown,keyCode=KeyCode.Escape}))
            {e.target=canvas;canvas.SendEvent(e);}
            Check(Current().circularStartAngle==0 && Current().circularRepetitions==1 && !canvas.HasPointerCapture(PointerId.mousePointerId),"Escape restores range and releases capture");
            foreach(bool first in new[]{false,true})
            {
                float direction=first?1:-1;
                Drag(Point(0),Angle(direction*160),false,true);
                Check(Current().circularRepetitions==2 && Mathf.Abs(Current().circularStartAngle-(first?180:0))<.001f,
                    "Ctrl before drag snaps either boundary to two repetitions");
                Control(false);
                Check(Mathf.Abs(Current().circularRepetitions-1.8f)<.001f && Mathf.Abs(Current().circularStartAngle-(first?160:0))<.001f,
                    "Releasing Ctrl restores unsnapped pointer position without a mouse move");
                Control(true);
                Check(Current().circularRepetitions==2,"Pressing Ctrl during drag applies snap immediately");
                Pointer(canvas,Angle(direction*235),EventType.MouseDrag,control:true);
                Check(Current().circularRepetitions==3 && Mathf.Abs(Current().circularStartAngle-(first?240:0))<.001f,
                    "Next integer repetition keeps the opposite boundary fixed");
                Check(palette==JsonUtility.ToJson(Current().gradient),"Ctrl arc snapping preserves color and alpha keys");
                Pointer(canvas,Angle(direction*235),EventType.MouseUp,control:true);
                Check(!canvas.HasPointerCapture(PointerId.mousePointerId),"Ctrl drag releases capture");
                Undo.PerformUndo();await UnityBRun.NextUpdate();
                Check(Current().circularRepetitions==1 && Current().circularStartAngle==0,"One Undo restores the entire Ctrl gesture");
            }
            var transform=JsonUtility.ToJson(Current().transform);
            Drag(Vector2.Lerp(Center(),Point(0),.5f),Vector2.Lerp(Center(),Angle(45),.5f),control:true);
            Check(Mathf.Abs(Current().circularStartAngle-45)<.001f && Current().circularRepetitions==1,"Ray rotates closed circle without opening");
            Check(transform==JsonUtility.ToJson(Current().transform),"Angular rotation does not change layer transform");
            Vector2 originalCenter=Center(),delta=new Vector2(25,-18);
            Drag(originalCenter,originalCenter+delta,control:true);
            Check(Vector2.Distance(Center(),originalCenter+delta)<.02f,"Center translates the whole gradient");
            Check(Current().circularRepetitions==1 && Mathf.Abs(Current().circularStartAngle-45)<.001f,"Translation retains angle/range");
            Drag(Point(.4f),Point(.45f),control:true);
            Check(Mathf.Abs(Current().gradient.ColorKeys[1].time-.45f)<.001f,"Interior color key moves on the circle");
            float midpoint=Current().gradient.GetMidpoint(false,0);
            Drag(Point(.45f*midpoint),Point(.45f*.6f),control:true);
            Check(Mathf.Abs(Current().gradient.GetMidpoint(false,0)-.6f)<.001f,"Midpoint moves on the circle");
            Pointer(canvas,Point(.8f),EventType.MouseDown);Pointer(canvas,Point(.8f),EventType.MouseUp);
            Check(Current().gradient.ColorKeys.Length==4 && Mathf.Abs(Current().gradient.ColorKeys[2].time-.8f)<.001f,"Click arc adds a key");
            foreach(WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json=WhimTexDocumentJson.Write(document,new WhimTexJsonWriteOptions {Mode=mode}).Json;
                using var read=WhimTexDocumentJson.Read(json,false);
                var copy=(GradientLayerBehaviour)read.Document.layers[0].Behaviour;
                Check(copy.circularStartAngle==Current().circularStartAngle && copy.circularRepetitions==Current().circularRepetitions,"Angular settings JSON roundtrip "+mode);
                Check(copy.gradient.Equals(Current().gradient),"Endpoint colors remain independent after JSON "+mode);
            }
            Current().transform.scale=new Vector2(-.55f,.4f);
            Call(document,"RefreshTransformHierarchy");
            Check((bool)Call(manip,"CircularFirstHalf",Point(0)+(Angle(Current().circularStartAngle+.1f)-Point(0)).normalized*4),"Split hit testing follows mirrored geometry");
            Current().circularRepetitions=.5f;
            float before=Current().circularRepetitions;
            Drag(Point(0),Angle(Current().circularStartAngle+20),control:true);
            Check(Current().circularRepetitions==before,"Partial cycle rotates without clamping stored repetitions");
        }
        finally
        {
            if(picker!=null)UnityBRun.CloseOwned(picker);
            if(window!=null)UnityBRun.CloseOwned(window);
            foreach(var action in restore)action();
        }
    }
    sealed class Capture : ImmediateModeElement
    {
        public WhimTexWindow owner;
        public string path;
        public readonly TaskCompletionSource<bool> done=new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if(done.Task.IsCompleted)return;
            Texture2D texture=null;
            try
            {
                var rt=RenderTexture.active;
                int w=rt!=null?rt.width:Mathf.RoundToInt(owner.position.width*EditorGUIUtility.pixelsPerPoint);
                int h=rt!=null?rt.height:Mathf.RoundToInt(owner.position.height*EditorGUIUtility.pixelsPerPoint);
                texture=new Texture2D(w,h,TextureFormat.RGBA32,false);
                texture.ReadPixels(new Rect(0,0,w,h),0,0,false);texture.Apply(false);
                System.IO.File.WriteAllBytes(path,texture.EncodeToPNG());done.TrySetResult(true);
            }
            catch(Exception error){done.TrySetException(error);}
            finally {if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
