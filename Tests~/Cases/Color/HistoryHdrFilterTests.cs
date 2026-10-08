using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class HistoryHdrFilterTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static object Get(object o,string n) => o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object value) => o.GetType().GetField(n,F).SetValue(o,value);
    static object Call(object o,string n,params object[] args) => o.GetType().GetMethods(F).Single(m => m.Name==n && m.GetParameters().Length==args.Length).Invoke(o,args);
    static void Check(bool ok,string message) { checks++; UnityBRun.Check(!(!ok), message); }
    static int[] Indices(VisualElement grid) => grid.Children().Where(c => c.userData is int).Select(c => (int)c.userData).ToArray();
    static void Key(VisualElement target,KeyCode key)
    { using(var e=KeyDownEvent.GetPooled(new Event {type=EventType.KeyDown,keyCode=key})) { e.target=target; target.SendEvent(e); } }
    static string ExecuteMain()
    {
        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0 || Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length!=0)
            throw new UnityBSkipException("BLOCKED: close active picker/gradient windows first.");
        checks=0; var focus=EditorWindow.focusedWindow;
        var doc=UnityBRun.Create<WhimTexDocument>();
        WhimTexColorPicker picker=null; WhimTexGradientWindow window=null; ScriptableObject session=null;
        var list=new List<Color> {new Color(2,0,0),Color.red,new Color(0,-.1f,0),Color.green,new Color(0,0,1,.2f)};
        Set(doc,"colorHistory",list);
        try
        {
            int changed=0;
            picker=(WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[] {Color.white,false,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>changed++),null,null});
            var grid=(VisualElement)Get(picker,"history"); var hdr=(Toggle)Get(picker,"hdrControl");
            Check(Indices(grid).SequenceEqual(new[]{1,3,4}),"Standard history hides high/negative RGB; original indices retained");
            Check(grid[0].ClassListContains("whimtex-picker-add-color"),"Plus retained");
            hdr.value=true; Check(Indices(grid).SequenceEqual(new[]{0,1,2,3,4}),"HDR restores all colors");
            hdr.value=false; Check(list.Count==5 && changed==0,"Toggling is display-only");
            Call(picker,"SelectHistoryColor",0,list[0]); Check(changed==0 && list[0].r==2,"Hidden HDR cannot be selected by stale callback");
            var green=grid.Children().Single(c=>c.userData is int i && i==3);
            Key(green,KeyCode.Return);
            Check(list[0]==Color.green && (Color)Get(picker,"color")==Color.green,"Filtered selection promotes actual color");
            Check(list.Count==5 && list[1].r==2 && list[3].g<0,"Hidden colors preserved");
            var blue=grid.Children().Single(c=>c.userData is int i && i==4);
            Key(blue,KeyCode.Delete);
            Check(list.Count==4 && list[1].r==2 && list[3].g<0,"Filtered deletion targets correct entry");
            foreach(var chip in grid.Children().Where(c=>c.userData is int))
                Check((int)chip.userData==list.IndexOf((Color)((Func<Color>)Get(chip,"color"))()),"Visible tile/drag donor uses document index");
            Call(picker,"Finish",false);picker=null;
            var type=typeof(WhimTexGradientWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientSession");
            session=UnityBRun.Create(type);Set(session,"document",doc);
            var gradient=new WhimTexGradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(4,1,0),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            Set(session,"gradient",gradient);
            window=WhimTexGradientWindow.Open(session,"gradient"); window.name="HDR history filter test";
            grid=(VisualElement)Get(window,"historyGrid");
            Check(Indices(grid).SequenceEqual(new[]{0,2}),"Standard gradient key filters history");
            Set(window,"selected",1);Call(window,"Refresh");
            Check(Indices(grid).SequenceEqual(new[]{0,1,2,3}),"HDR key restores all colors");
            Call(window,"ToggleHdr");Check(Indices(grid).SequenceEqual(new[]{0,2}),"Gradient HDR-off refreshes history");
            string before=JsonUtility.ToJson(Get(window,"gradient"));
            Call(window,"SelectHistoryColor",1,list[1]);
            Check(JsonUtility.ToJson(Get(window,"gradient"))==before,"Gradient rejects hidden HDR selection");
            Call(window,"ToggleHdr");Check(Indices(grid).Length==4,"Gradient HDR-on restores history");
            Set(window,"selected",0);Call(window,"Refresh");Check(Indices(grid).Length==2,"Switching back to standard key filters again");
            var first=grid[0];Call(window,"RefreshColorHistory");Check(ReferenceEquals(first,grid[0]),"Filtered snapshot avoids needless rebuilding");
            list.Clear();list.Add(new Color(3,1,0));Call(window,"RefreshColorHistory");
            Check(Indices(grid).Length==0 && grid.Q<Label>()!=null,"All-HDR history has a standard-mode empty state");
            Call(window,"ToggleHdr");Check(Indices(grid).SequenceEqual(new[]{0}),"Hidden entry survives empty state");
            return "";
        }
        finally
        {
            if(picker!=null)Call(picker,"Finish",false);
            if(window!=null)UnityBRun.CloseOwned(window);
            if(session!=null){Undo.ClearUndo(session);UnityEngine.Object.DestroyImmediate(session);}
            Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);
            if(focus!=null)focus.Focus();
        }
    }
    public static string Main() => UnityBRun.Run("HistoryHdrFilterSmoke.Main", () => ExecuteMain());
}
