using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class WhimTexGradientWindowSmoke
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(object w, string name) => w.GetType().GetField(name, Flags).GetValue(w);
    static object Call(object w, string name, params object[] args) => w.GetType().GetMethod(name, Flags).Invoke(w,args);
    static void Check(bool v,string m) { if(!v) throw new Exception(m); }
    public static string Main()
    {
        var w=ScriptableObject.CreateInstance<WhimTexGradientWindow>();
        try
        {
            w.Show();
            w.CreateGUI();
            Check(w.rootVisualElement.styleSheets.count>0,"Stylesheet missing");
            var c=(ColorField)Field(w,"color");
            Check(!c.hdr,"Color picker should start in LDR");
            Call(w,"ToggleHdr");
            Check(c.hdr,"HDR toggle failed");
            c.value=new Color(3,2,1,1);
            var g=(WhimTexGradient)Field(w,"gradient");
            Check(g.ColorKeys[0].color.r==3,"HDR edit lost");
            Call(w,"ToggleHdr");
            Check(!c.hdr && g.ColorKeys[0].color.r==3,"LDR toggle lost HDR data");
            c.value=new Color(.5f,1,0,1);
            Check(g.ColorKeys[0].color.g==3 && g.ColorKeys[0].color.r==1.5f,"LDR edit lost intensity");
            ((FloatField)Field(w,"intensity")).value=1;
            Check(g.ColorKeys[0].color.g==1,"Intensity reset failed");
            ((FloatField)Field(w,"location")).value=20;
            Check(Mathf.Abs(g.ColorKeys[0].time-.2f)<.00001f,"Location edit failed");
            Call(w,"AddKey",.5f); Call(w,"Refresh");
            Check(g.ColorKeys.Length==3,"Color key insertion failed");
            Call(w,"MoveKey",.1f,false);
            Check((int)Field(w,"selected")==0 && g.ColorKeys[0].time==.1f,"Key crossing left failed");
            Color movingColor=g.ColorKeys[0].color;
            Call(w,"MoveKey",.9f,false);
            Check((int)Field(w,"selected")==1 && g.ColorKeys[1].color==movingColor,"Key crossing right lost selection");
            Call(w,"DeleteKey");
            Check(g.ColorKeys.Length==2,"Delete failed");
            Call(w,"DeleteKey");
            Check(g.ColorKeys.Length==1,"Delete down to one failed");
            Call(w,"DeleteKey");
            Check(g.ColorKeys.Length==1,"Minimum keys broken");
            Check(!(bool)Call(w,"IsRemovalPosition",new Vector2(-100,-100)),"Last color key can be dragged away");
            w.GetType().GetField("alphaTrack",Flags).SetValue(w,true);
            w.GetType().GetField("selected",Flags).SetValue(w,0);
            Call(w,"Refresh");
            ((Slider)Field(w,"alpha")).value=35;
            Check(Mathf.Abs(g.AlphaKeys[0].alpha-.35f)<.00001f,"Alpha edit failed");
            Check((bool)Call(w,"IsRemovalPosition",new Vector2(-100,-100)),"Drag removal unavailable");
            Check(!(bool)Call(w,"IsRemovalPosition",new Vector2(-10000,0)),"Horizontal drag left must not delete");
            Check(!(bool)Call(w,"IsRemovalPosition",new Vector2(10000,0)),"Horizontal drag right must not delete");
            Call(w,"RemoveKey",false);
            Check(g.AlphaKeys.Length==1,"Alpha drag removal failed");
            Call(w,"DeleteKey");
            Check(g.AlphaKeys.Length==1,"Last alpha removed");
            foreach(WhimTexGradientMode m in Enum.GetValues(typeof(WhimTexGradientMode)))
            {
                g.Mode=m;
                Color expected=g.Evaluate(0);
                for(int i=1;i<=10;i++)
                    Check(g.Evaluate(i/10f)==expected,"Single key must be constant in "+m);
            }
            g.Mode=WhimTexGradientMode.Classic;
            Call(w,"Refresh");
            ((EnumField)Field(w,"mode")).value=WhimTexGradientMode.Fixed;
            Check(!((Slider)Field(w,"smoothness")).enabledSelf,"Fixed smoothing enabled");
            Check(((Texture2D)Field(w,"preview")).width==512,"Preview missing");
            Undo.PerformUndo();
            Check(((WhimTexGradient)Field(w,"gradient")).Mode!=WhimTexGradientMode.Fixed,"Undo failed");
            Undo.PerformRedo();
            Check(((WhimTexGradient)Field(w,"gradient")).Mode==WhimTexGradientMode.Fixed,"Redo failed");
        }
        finally { Undo.ClearUndo(w); UnityEngine.Object.DestroyImmediate(w); }
        WhimTexGradientTestWindow.Open();
        return "Gradient window checks passed: HDR, keys, alpha, modes, preview, Undo/Redo. Standalone window opened.";
    }
}
