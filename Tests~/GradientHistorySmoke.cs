using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class GradientHistorySmoke
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    const string Name="Gradient history smoke";
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static void Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(o,a);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static WhimTexGradientWindow Window()=>Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Single(w=>w.name==Name);
    public static string Setup()
    {
        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0||Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length!=0)return "BLOCKED: close user picker/gradient windows first.";
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.name=Name;
        for(int i=0;i<40;i++)Call(doc,"RememberColor",Color.HSVToRGB(i/40f,.7f,.8f));
        Call(doc,"RememberColor",new Color(4,2,.5f,.2f));
        var type=typeof(WhimTexGradientWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientSession");
        var session=ScriptableObject.CreateInstance(type); Set(session,"document",doc);
        var w=WhimTexGradientWindow.Open(session,"gradient");w.name=Name;w.position=new Rect(100,100,560,420);
        Call(w,"ToggleHdr");
        return "Temporary gradient/history ready; run Verify after layout, then Cleanup.";
    }
    public static string Verify()
    {
        var w=Window(); var session=Get(w,"owner"); var doc=(TextureCompositor)Get(session,"document");
        var list=(List<Color>)Get(doc,"colorHistory"); var grid=(VisualElement)Get(w,"historyGrid");
        Check(grid.childCount==list.Count&&grid.Q(className:"whimtex-picker-add-color")==null,"History without plus");
        var heading=w.rootVisualElement.Q("gradientColorHistory");
        Check(heading.worldBound.yMax<=w.rootVisualElement.Q(className:"whimtex-gradient-presets-header").worldBound.yMin,"History above presets");
        foreach(var space in new[]{ColorSpace.Gamma,ColorSpace.Linear})
        {
            var gradient=new WhimTexGradient{ColorSpace=space};
            gradient.SetKeys(new[]{new GradientColorKey(new Color(.2f,.3f,.4f,.6f),0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(.3f,0),new GradientAlphaKey(.9f,1)});
            Set(w,"gradient",gradient);Set(w,"selected",0);Set(w,"alphaTrack",false);Set(w,"midpointSelected",false);Call(w,"Refresh");
            if(!((UnityEditor.UIElements.ColorField)Get(w,"color")).hdr)Call(w,"ToggleHdr");
            Color expected=list[0]; if(space==ColorSpace.Linear)expected=expected.linear;expected.a=.6f;
            using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Return}))
            {e.target=grid[0];grid[0].SendEvent(e);}
            var actual=((WhimTexGradient)Get(w,"gradient")).ColorKeys[0].color;
            Check((actual-expected).maxColorComponent<.0001f&&(expected-actual).maxColorComponent<.0001f,"Exact HDR/history color and color space");
            Check(gradient.AlphaKeys[0].alpha==.3f&&gradient.ColorKeys[0].time==0,"Alpha track and key time unchanged");
            Check(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length==0,"No picker opened");
            string saved=JsonUtility.ToJson(gradient); Set(w,"alphaTrack",true);Call(w,"Refresh");
            Check(!grid.enabledSelf,"Alpha key disables history");Call(w,"SelectHistoryColor",1,list[1]);Check(JsonUtility.ToJson(gradient)==saved,"Alpha key guarded");
            Set(w,"alphaTrack",false);Set(w,"midpointSelected",true);Call(w,"Refresh");
            Check(!grid.enabledSelf,"Midpoint disables history");Call(w,"SelectHistoryColor",1,list[1]);Check(JsonUtility.ToJson(gradient)==saved,"Midpoint guarded");
        }
        Set(w,"midpointSelected",false);Call(w,"Refresh");
        Color reused=list[3];int count=list.Count;Call(doc,"RememberColor",reused);Call(w,"RefreshColorHistory");
        Check(list.Count==count&&list[0]==reused,"Matching color promoted without duplication");
        Check(grid.childCount==count,"External history change synchronized");
        Call(w,"SelectHistoryColor",2,list[2]);Check(list.Count==count,"Direct selection avoids duplicates");
        return "PASS: ordering, no plus, HDR/Gamma/Linear, selected-key application, alpha/midpoint exclusion, no picker, live history and deduplication.";
    }
    public static string PickerRecency()
    {
        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0)return "BLOCKED: close user picker first.";
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); WhimTexColorPicker picker=null;
        try
        {
            Call(doc,"RememberColor",Color.red);Call(doc,"RememberColor",Color.green);
            var list=(List<Color>)Get(doc,"colorHistory");
            picker=(WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[]{Color.blue,false,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>{}),null,null});
            Call(picker,"SetColor",Color.red,true);Check(list[0]==Color.green,"Intermediate match does not reorder");
            picker.Close();picker=null;Check(list.Count==2&&list[0]==Color.red,"Confirmed matching color moves first");
            picker=(WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[]{Color.blue,false,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>{}),null,null});
            Call(picker,"SetColor",Color.green,true);Call(picker,"Finish",false);picker=null;
            Check(list.Count==2&&list[0]==Color.red,"Canceled match does not reorder");
            return "PASS: confirmed exact match promoted; intermediate/canceled matches do not reorder.";
        }
        finally {if(picker!=null)picker.Close();Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);}
    }
    public static string Cleanup()
    {
        foreach(var w in Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>())if(w.name==Name)w.Close();
        foreach(var d in Resources.FindObjectsOfTypeAll<TextureCompositor>())if(d.name==Name){Undo.ClearUndo(d);UnityEngine.Object.DestroyImmediate(d);}
        return "Temporary gradient history windows/documents cleaned.";
    }
    public static string Capture()
    {
        var w=Window();
        w.rootVisualElement.Q("history-capture")?.RemoveFromHierarchy();
        var probe=new CaptureProbe(w){name="history-capture",pickingMode=PickingMode.Ignore};
        probe.style.position=Position.Absolute;probe.style.left=0;probe.style.top=0;probe.style.width=1;probe.style.height=1;
        w.rootVisualElement.Add(probe);w.Repaint();return "Capture queued.";
    }
    sealed class CaptureProbe:ImmediateModeElement
    {
        readonly EditorWindow window;bool done;
        internal CaptureProbe(EditorWindow window){this.window=window;}
        protected override void ImmediateRepaint()
        {
            if(done)return;done=true;
            var rt=RenderTexture.active;
            var image=new Texture2D(rt!=null?rt.width:Mathf.RoundToInt(window.position.width*EditorGUIUtility.pixelsPerPoint),
                rt!=null?rt.height:Mathf.RoundToInt(window.position.height*EditorGUIUtility.pixelsPerPoint),TextureFormat.RGBA32,false);
            try
            {
                image.ReadPixels(new Rect(0,0,image.width,image.height),0,0,false);image.Apply(false);
                System.IO.Directory.CreateDirectory("Temp/WhimTex");
                System.IO.File.WriteAllBytes("Temp/WhimTex/gradient-history.png",image.EncodeToPNG());
            }
            finally{UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
