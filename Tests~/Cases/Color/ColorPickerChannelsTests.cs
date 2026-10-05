using System.Threading.Tasks;
using System;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerChannelsTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Logic = typeof(WhimTexColorPicker).Assembly.GetType("DCFApixels.WhimTex.WhimTexColorChannels");
    static object Get(object o, string n) => o.GetType().GetField(n, F).GetValue(o);
    static void Call(object o, string n, params object[] args) => o.GetType().GetMethod(n, F).Invoke(o, args);
    static bool Enabled { get => (bool)Logic.GetProperty("Enabled", F).GetValue(null); set => Logic.GetProperty("Enabled", F).SetValue(null, value); }
    static void Source(VisualElement root, Func<int> read) => Logic.GetMethod("SetSource", F).Invoke(null, new object[] { root, read });
    static int checks;
    static void Check(bool ok, string message) { T.True(ok, message); }
    static WhimTexColorPicker Open(Action<Color> changed) => Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
        new object[] { new Color(.8f, .35f, .15f, .4f), false, true, WhimTexColorRange.Switchable, null, changed, null, null }));
    static void Close(WhimTexColorPicker p) { if (p != null) Call(p, "Finish", false); }
    private static string BodyRun()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        bool previous = Enabled;
        var focus = EditorWindow.focusedWindow;
        WhimTexColorPicker p = null;
        checks = 0;
        try
        {
            var c = new Color(.8f,.35f,.15f,.4f);
            var apply = Logic.GetMethod("Apply", F);
            for (int mask = -1; mask < 16; mask++)
            {
                Color expected = c;
                if (mask >= 0)
                {
                    int count = ((mask&1)!=0?1:0)+((mask&2)!=0?1:0)+((mask&4)!=0?1:0);
                    float alpha = (mask&8)!=0?c.a:1;
                    if (count == 0) { float v = (mask&8)!=0?c.a:0; expected = new Color(v,v,v,1); }
                    else if (count == 1) { float v=(mask&1)!=0?c.r:(mask&2)!=0?c.g:c.b; expected=new Color(v,v,v,alpha); }
                    else expected=new Color((mask&1)!=0?c.r:0,(mask&2)!=0?c.g:0,(mask&4)!=0?c.b:0,alpha);
                }
                Check((Color)apply.Invoke(null,new object[]{c,mask}) == expected, "Mask " + mask);
            }
            int changes = 0, liveMask = 3;
            p = Open(_ => changes++);
            var saved = (Color)Get(p,"color");
            string hex = ((TextField)Get(p,"hex")).value;
            var channels = (Slider[])Get(p,"channels");
            var values = channels.Select(x=>x.value).ToArray();
            Call(p,"SetChannelSource",(Func<int>)(()=>liveMask));
            var toggle = (Toggle)Get(p,"channelControl");
            Check(!toggle.ClassListContains("whimtex-picker-hidden"),"Eligible picker toggle visible");
            for(int mask=0;mask<16;mask++)
            {
                liveMask=mask;
                foreach(bool mode in new[]{false,true})
                {
                    toggle.value=mode; Call(p,"UpdateColorChannels");
                    Check((Color)Get(p,"color")==saved && ((TextField)Get(p,"hex")).value==hex && channels.Select(x=>x.value).SequenceEqual(values) && changes==0,"View-only mode/mask "+mask);
                    Check((int)typeof(WhimTexColorPicker).GetProperty("PickerChannelMask",F).GetValue(p)==(mode?mask:-1),"Live channel source");
                }
            }
            Call(p,"SetChannelSource",new object[]{null});
            Check(toggle.ClassListContains("whimtex-picker-hidden"),"Service picker hides channel toggle");
            var rootA=new VisualElement(); var rootB=new VisualElement();
            Source(rootA,()=>1); Source(rootB,()=>6);
            var field=new WhimTexColorField { UseCanvasChannels=true }; rootA.Add(field);
            Func<int> Read()=> (Func<int>)typeof(WhimTexColorField).GetMethod("ResolveChannelSource",F).Invoke(field,null);
            Check(Read()()==1,"Source from owner A"); rootB.Add(field); Check(Read()()==6,"Reparent resolves owner B");
            field.UseCanvasChannels=false; Check(Read()==null,"Service field opt-out");
            p.CreateGUI();
            Check(((Toggle)Get(p,"channelControl")).ClassListContains("whimtex-picker-hidden"),"Recreated service picker hides toggle");
            return null;
        }
        finally { Close(p); Enabled=previous; if(focus!=null)focus.Focus(); }
    }
    private static string Setup()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0), "Close the borrowed active picker before this case");
        var p=Open(_=>{}); p.name=(Scope.Tag + "-picker-layout");
        p.rootVisualElement.userData=Enabled; Enabled=true;
        Source(p.rootVisualElement,()=>3);
        Call(p,"SetChannelSource",(Func<int>)(()=>3));
        foreach(string name in new[]{"Channels","Ordinary","HDR","Mixed","Disabled"})
        {
            var field=new WhimTexColorField(name) { name="test-"+name, UseCanvasChannels=name!="Ordinary", hdr=name=="HDR", showAlpha=true, showEyeDropper=false };
            field.SetValueWithoutNotify(new Color(.8f,.35f,.15f,.4f)*(name=="HDR"?2:1));
            field.showMixedValue=name=="Mixed"; field.SetEnabled(name!="Disabled");
            p.rootVisualElement.Add(field);
        }
        p.position=new Rect(200,150,260,680);
        return null;
    }
    private static string Layout()
    {
        var p=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var field=p.rootVisualElement.Q<WhimTexColorField>("test-Channels");
        var overlay=field.Q(className:"whimtex-color-channel-swatch");
        Check(overlay!=null && overlay.worldBound.width>0 && overlay.pickingMode==PickingMode.Ignore,"Overlay layout and picking");
        var hdr=field.Q(className:UnityEditor.UIElements.ColorField.hdrLabelUssClassName);
        Check(overlay.parent==hdr.parent && overlay.parent.IndexOf(overlay)<overlay.parent.IndexOf(hdr),"Native HDR label above overlay");
        var alpha=field.Q<ProgressBar>(); Check(alpha!=null && Mathf.Abs(alpha.value-40)<.001f,"Single native actual-alpha bar");
        Check(p.rootVisualElement.Q<WhimTexColorField>("test-Ordinary").Q(className:"whimtex-color-channel-swatch")==null,"Ordinary field untouched");
        var toolbar=(VisualElement)Get(p,"channelControl");
        Check(toolbar.worldBound.xMax<=((VisualElement)Get(p,"before")).worldBound.xMin,"Toggle does not overlap original swatch");
        return null;
    }
    private static string CleanupFixture()
    {
        foreach(var p in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>())
            if(p.name==(Scope.Tag + "-picker-layout") && p.rootVisualElement.Q("test-Channels")!=null)
            { bool previous=(bool)p.rootVisualElement.userData; Close(p); Enabled=previous; }
        return null;
    }
    private static string HdrSetup()
    {
        var p=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var field=p.rootVisualElement.Q<WhimTexColorField>("test-HDR");
        field.value=new Color(8,2,.2f,.4f);
        var native=p.rootVisualElement.Q<WhimTexColorField>("test-HDR-native");
        if(native==null)
        {
            native=new WhimTexColorField("Native HDR"){name="test-HDR-native",hdr=true,showAlpha=true,showEyeDropper=false};
            p.rootVisualElement.Add(native);
        }
        native.value=field.value;
        return null;
    }
    private static string HdrPixels()
    {
        var p=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var field=p.rootVisualElement.Q<WhimTexColorField>("test-HDR");
        var native=p.rootVisualElement.Q<WhimTexColorField>("test-HDR-native");
        Rect Bounds(WhimTexColorField f)=>f.Q(className:UnityEditor.UIElements.ColorField.colorContainerUssClassName).worldBound;
        var a=Bounds(field); var b=Bounds(native);
        float scale=EditorGUIUtility.pixelsPerPoint;
        var image=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            image.LoadImage(System.IO.File.ReadAllBytes(Scope.Temp + "/color-picker-ring.png"));
            Color Sample(Rect r,float x,float y)=>image.GetPixel(Mathf.FloorToInt((r.xMin+r.width*x)*scale), image.height-1-Mathf.FloorToInt((r.yMin+r.height*y)*scale));
            float Difference(Color x,Color y)=>Mathf.Max(Mathf.Abs(x.r-y.r),Mathf.Abs(x.g-y.g),Mathf.Abs(x.b-y.b));
            foreach(float x in new[]{.07f,.17f,.3f,.65f})
                Check(Difference(Sample(a,x,.12f),Sample(b,x,.12f))<.015f,"Native original HDR triangle retained at "+x);
            foreach(float x in new[]{.76f,.85f,.94f})
            {
                Color actual=Sample(a,x,.65f),expected=Sample(b,x,.65f);
                int mask=(int)Get(field,"lastChannelMask");
                if(mask==1) expected=new Color(expected.r,expected.r,expected.r,1);
                else expected.b=0;
                Check(Difference(actual,expected)<.025f,"Adapted HDR ramp matches native ramp at "+x+": "+actual+" / "+expected);
            }
            Check(Difference(Sample(a,.76f,.65f),Sample(a,.94f,.65f))>.04f,"HDR adaptation is a gradient, not a flat color");
            Check(Mathf.Abs(field.Q<ProgressBar>().value-40)<.001f && field.value==native.value,"Source and actual alpha retained");
            return null;
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    private static string HdrSingleChannel()
    {
        var p=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var field=p.rootVisualElement.Q<WhimTexColorField>("test-HDR");
        typeof(WhimTexColorField).GetField("ReadCanvasChannels",F).SetValue(field,(Func<int>)(()=>1));
        Call(field,"RefreshChannelSwatch");
        return null;
    }
    private static string HdrModeSwitch()
    {
        var p=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var field=p.rootVisualElement.Q<WhimTexColorField>("test-HDR");
        Color saved=field.value; int mask=(int)Get(field,"lastChannelMask");
        foreach(bool hdr in new[]{false,true})
        {
            field.hdr=hdr; Call(field,"RefreshChannelSwatch");
            Check((bool)Get(field,"lastHdr")==hdr && (int)Get(field,"lastChannelMask")==mask && field.value==saved,"HDR mode invalidates unchanged-mask overlay without changing value");
        }
        return null;
    }

private static async Task<string> BodyLayoutScenario() {
Setup(); try { await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); Layout(); HdrSetup(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); var picker = Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x => x.name == (Scope.Tag + "-picker-layout")); await WhimTex.Tests.UnityA.UnityACapture.Capture(picker, Scope.Temp + "/color-picker-ring.png", Cancellation); HdrPixels(); HdrSingleChannel(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); await WhimTex.Tests.UnityA.UnityACapture.Capture(picker, Scope.Temp + "/color-picker-ring.png", Cancellation); HdrPixels(); HdrModeSwitch(); } finally { CleanupFixture(); }
return null;
}

public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLayoutScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
