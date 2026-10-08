using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerPreviewTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static void Check(bool ok, string message) { T.True(ok, message); }
    static void Near(Color a, Color b, string message) => Check(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a) < .0001f, message);
    static WhimTexColorPicker Open(WhimTexDocument doc, Action<Color> changed) => Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open", F).Invoke(null,
        new object[] { new Color(2.5f,.6f,.2f,.7f), true, true, WhimTexColorRange.Switchable, doc, changed, null, null }));
    static WhimTexDocument Document()
    {
        var doc = Scope.OwnObject(ScriptableObject.CreateInstance<WhimTexDocument>());
        doc.name = "Color picker EV test document"; doc.hideFlags = HideFlags.HideAndDontSave;
        var history = new List<Color>();
        for (int i=0;i<48;i++) history.Add(Color.HSVToRGB(i/48f,.7f,.8f));
        typeof(WhimTexDocument).GetField("colorHistory", F).SetValue(doc, history);
        return doc;
    }
    private static string BodyRun()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        var focus = EditorWindow.focusedWindow;
        var doc = Document(); WhimTexColorPicker picker = null;
        try
        {
            int changes = 0; picker = Open(doc, _ => changes++);
            var ev = picker.rootVisualElement.Q<Slider>("previewEV");
            Check(ev != null && ev.lowValue == -10 && ev.highValue == 10 && ev.showInputField && ev.value == 0, "Gradient-style EV control/default");
            var source = (Color)Get(picker, "color");
            string historyBefore = JsonUtility.ToJson(doc);
            float exposure = (float)Get(picker, "exposure");
            string hex = ((TextField)Get(picker, "hex")).value;
            var fields = (Slider[])Get(picker, "channels");
            var numbers = new[] {fields[0].value, fields[1].value, fields[2].value};
            foreach (float value in new[] {-10f, -3f, 0f, 2f, 10f})
            {
                ev.value = value;
                var expected = (source.linear * Mathf.Pow(2,value)).gamma;
                expected = new Color(Mathf.Clamp01(expected.r),Mathf.Clamp01(expected.g),Mathf.Clamp01(expected.b),source.a);
                Near((Color)Call(picker,"PreviewColor",source), expected, "Same linear-light EV transform as gradient");
                Near((Color)Get(picker,"color"),source,"Stored RGBA unchanged");
                Check(changes == 0 && (float)Get(picker,"exposure") == exposure && ((TextField)Get(picker,"hex")).value == hex, "No callbacks or HDR/Hex edits");
                for(int i=0;i<3;i++) Check(fields[i].value == numbers[i], "Numeric channels unchanged");
                Check(JsonUtility.ToJson(doc) == historyBefore, "Document/history unchanged");
            }
            var history = (VisualElement)Get(picker,"history");
            Check(Get(history[0],"preview") == null,"Plus remains ordinary gray");
            Check(Get(history[1],"preview") is Func<Color,Color>,"History uses preview transform");
            Check(Get(Get(picker,"before"),"preview") is Func<Color,Color> && Get(Get(picker,"after"),"preview") is Func<Color,Color>,"Both swatches use preview transform");
            Check(Get(Get(picker,"plane"),"preview") is Func<Color,Color>,"SV square uses preview transform");
            Check(Get(picker,"hueRing").GetType().GetField("preview",F) == null,"Hue ring stays independent of EV");
            var channelType = typeof(WhimTexColorPicker).Assembly.GetType("DCFApixels.WhimTex.WhimTexColorChannels");
            var enabled = channelType.GetProperty("Enabled",F); bool previousEnabled = (bool)enabled.GetValue(null);
            try
            {
                enabled.SetValue(null,true);
                Call(picker,"SetChannelSource",(Func<int>)(() => 8));
                var ramps = (List<VisualElement>)Get(picker,"ramps");
                var alphaSample = (Func<float,Color>)Get(ramps[3],"sample");
                foreach(float value in new[] {-3f,2f})
                { ev.value=value; Near(alphaSample(.35f),new Color(.35f,.35f,.35f,1),"Alpha-only view ignores RGB exposure"); }
                Call(picker,"SetChannelSource",(Func<int>)(() => 1));
                Color expected=(Color)Call(picker,"PreviewColor",source);
                Near(alphaSample(.35f),new Color(expected.r,expected.r,expected.r,1),"Single channel is exposed then shown in grayscale");
            }
            finally { enabled.SetValue(null,previousEnabled); Call(picker,"SetChannelSource",new object[] {null}); }
            ev.value = -3;
            picker.CreateGUI();
            Check(picker.rootVisualElement.Q<Slider>("previewEV").value == -3,"UI rebuild keeps local EV");
            Call(picker,"Finish",true); picker = Open(null,_ => changes++);
            Check(picker.rootVisualElement.Q<Slider>("previewEV").value == 0,"New picker starts at zero");
            return null;
        }
        finally
        {
            if(picker != null) Call(picker,"Finish",false);
            Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc);
            if(focus != null) focus.Focus();
        }
    }
    private static string Setup()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        var picker = Open(Document(), _ => {});
        picker.name = (Scope.Tag + "-picker-layout");
        picker.rootVisualElement.userData = "Preview EV test";
        picker.position = new Rect(picker.position.position,picker.minSize);
        picker.rootVisualElement.Q<Slider>("previewEV").value = -3;
        return null;
    }
    static WhimTexColorPicker Find()
    {
        foreach(var picker in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>())
            if(picker.name == (Scope.Tag + "-picker-layout") && (string)picker.rootVisualElement.userData == "Preview EV test") return picker;
        throw new Exception("Run Setup first.");
    }
    private static string Layout()
    {
        var picker = Find(); var root = picker.rootVisualElement;
        var ev = root.Q<Slider>("previewEV"); var history = (VisualElement)Get(picker,"history");
        Check(ev.worldBound.yMax <= root.worldBound.yMax + .1f && ev.worldBound.xMax <= root.worldBound.xMax, "EV fits minimum window");
        Check(ev.labelElement.worldBound.width >= 65 && ev.Q<TextField>()?.worldBound.width > 20,"Label and numeric entry fit");
        Check(((ScrollView)Get(picker,"historyScroll")).worldBound.yMax <= ev.worldBound.yMin,"History stays above footer");
        Check(history[0].worldBound.height >= 12,"History swatches remain visible");
        return null;
    }
    private static string CleanupFixture()
    {
        var picker = Find(); var doc = (WhimTexDocument)Get(picker,"document");
        Call(picker,"Finish",false); Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc);
        return null;
    }

private static async Task<string> BodyLayoutScenario() {
Setup(); try { await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); Layout(); } finally { CleanupFixture(); }
return null;
}

public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLayoutScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
