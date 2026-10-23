using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class CanvasViewFooterTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

private static string SetupFixture(){
// Transient UI layout fixture; finish with CanvasViewFooterTests.cs. No saved assets.
var type=typeof(DCFApixels.WhimTex.WhimTexWindow);
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>())
    T.True(!(existing.name==(Scope.Tag + "-footer")), "Finish the previous footer fixture first.");
var previous=UnityEditor.EditorWindow.focusedWindow;
var window=Scope.OwnWindow(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexWindow>());
window.name=(Scope.Tag + "-footer");window.position=new UnityEngine.Rect(140,140,960,680);window.ShowUtility();window.CreateGUI();
var root=window.rootVisualElement;root.Clear();root.userData=previous;
foreach(int width in new[]{900,700,680,600,599,400,280,200})
{
    root.Add(new UnityEngine.UIElements.Label(width+" px"));
    var footer=(UnityEngine.UIElements.VisualElement)type.GetMethod("BuildCanvasViewFooter",f).Invoke(window,null);
    footer.name="footer"+width;footer.style.width=width;
    root.Add(footer);
}
return null;


}
private static string HintResize(){
// Run three times after CanvasViewFooterSetup.cs; finish with CanvasViewFooterTests.cs.
DCFApixels.WhimTex.WhimTexWindow window=null;
foreach(var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>())
    if(candidate.name==(Scope.Tag + "-footer"))window=candidate;
T.True(!(window==null), "Run CanvasViewFooterSetup.cs first.");
var footer=UnityEngine.UIElements.UQueryExtensions.Q(window.rootVisualElement,"footer900");
var hint=(UnityEngine.UIElements.Label)footer[1];
int stage=hint.userData is int value?value:0;
if(stage==0)
{
    hint.text="Drag move • handles scale • circle rotate";
    hint.GetType().GetMethod("RefreshVisibility").Invoke(hint,null);
    T.True(!(hint.ClassListContains("whimtex-canvas-view-status--hidden")), "Hint should fit at 900 px.");
    footer.style.width=620;hint.userData=1;
    return null;
}
if(stage==1)
{
    T.True(!(hint.resolvedStyle.visibility!=UnityEngine.UIElements.Visibility.Hidden), "Resize must hide overflowing text automatically.");
    footer.style.width=900;hint.userData=2;
    return null;
}
T.True(!(hint.resolvedStyle.visibility!=UnityEngine.UIElements.Visibility.Visible), "Expanding must restore text automatically.");
return null;


}
private static string FooterAssertions(){
var type=typeof(DCFApixels.WhimTex.WhimTexWindow);
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
DCFApixels.WhimTex.WhimTexWindow window=null;
foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.WhimTexWindow>())
    if(existing.name==(Scope.Tag + "-footer"))window=existing;
T.True(!(window==null), "Run CanvasViewFooterSetup.cs first.");
var root=window.rootVisualElement;var previous=root.userData as UnityEditor.EditorWindow;
void Check(bool value,string label){ T.True(value, label); }
UnityEngine.UIElements.VisualElement Find(UnityEngine.UIElements.VisualElement parent,string name)=>UnityEngine.UIElements.UQueryExtensions.Q(parent,name);
try
{
    foreach(int width in new[]{900,700,680,600,599,400,280,200})
    {
        var footer=Find(root,"footer"+width);
        var left=Find(footer,"canvasFooterLeft");var right=Find(footer,"canvasFooterRight");
        var updates=Find(left,"canvasFooterUpdates");var context=Find(left,"canvasFooterContext");
        var inspection=Find(right,"canvasFooterInspection");var color=Find(right,"canvasFooterColor");
        Check(footer.childCount==3&&left.childCount==2&&right.childCount==2,"Two groups per side and central hint");
        Check(updates.childCount==2&&updates[0].ClassListContains("whimtex-canvas-view-quality")&&updates[1].ClassListContains("whimtex-live-output-button"),"Quality and Live Update left");
        Check(((UnityEngine.UIElements.Button)context[0]).text=="Post FX"&&((UnityEngine.UIElements.Button)context[1]).text=="UV","Viewing context left");
        Check(inspection.childCount==3&&((UnityEngine.UIElements.Button)inspection[0]).text=="HDR"&&inspection[1] is UnityEngine.UIElements.FloatField&&inspection[2].ClassListContains("whimtex-debug-button"),"HDR immediately before EV, then Debug");
        Check(color.childCount==4,"RGBA in separate group");
        for(int i=0;i<4;i++)Check(((UnityEngine.UIElements.Button)color[i]).text==new[]{"R","G","B","A"}[i],"RGBA order unchanged");
        bool compact=footer.ClassListContains("whimtex-canvas-view-footer--compact");
        if(width>=700)Check(!compact,"Wide footer stays on one row at "+width);
        if(width<=400)Check(compact,"Narrow footer wraps its control groups at "+width);
        var quality=updates[0].Q<WhimTexSliderField>();
        Check(quality!=null&&updates[0].childCount==2,"Quality combines percentage and slider in one field");
        Check(quality.worldBound.width>48f&&quality.worldBound.width<72f,"Quality width is between layer opacity and the previous footer slider");
        Check(Math.Abs(quality.worldBound.height-20f)<.1f,"Quality retains the standard input height");
        var hint=(UnityEngine.UIElements.Label)footer[1];
        var refreshHint=hint.GetType().GetMethod("RefreshVisibility");
        foreach(string text in new[]{"Add a layer to start",new string('W',300),"", "Ready"})
        {
            hint.text=text;refreshHint.Invoke(hint,null);
            float required=hint.MeasureTextSize(text,0,UnityEngine.UIElements.VisualElement.MeasureMode.Undefined,0,UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
            Check(hint.ClassListContains("whimtex-canvas-view-status--hidden") == (text.Length==0||!(required<=hint.contentRect.width)),"Hide the entire hint only when empty or too wide");
        }
        foreach(var group in new[]{updates,context,inspection,color})
        {
            Check(group.worldBound.xMin>=footer.worldBound.xMin-.1f&&group.worldBound.xMax<=footer.worldBound.xMax+.1f,"Group fits horizontally at "+width+": "+group.name);
            Check(group.worldBound.yMax<=footer.worldBound.yMax+.1f,"Group fits vertically at "+width);
            Check(group.worldBound.yMin>=footer.worldBound.yMin-.1f,"Group starts inside the footer at "+width);
            for(int i=1;i<group.childCount;i++)Check(group[i].worldBound.xMin>=group[i-1].worldBound.xMax-.1f,"Controls do not overlap");
        }
        Check(left.worldBound.xMax<=right.worldBound.xMin+.1f || left.worldBound.yMax<=right.worldBound.yMin+.1f,
            "Sides do not overlap in either layout at "+width);
        if(!compact)
        {
            Check(System.Math.Abs(footer.resolvedStyle.height-26)<.1f,"Normal footer height unchanged");
            Check(left.worldBound.xMax<=right.worldBound.xMin+.1f,"Left/right separated at "+width+": left="+left.worldBound.xMax+", right="+right.worldBound.xMin);
            Check(System.Math.Abs(left.worldBound.center.y-right.worldBound.center.y)<.1f,"Sides on same row");
        }
    }
    return null;
}
finally
{
    window.DiscardChanges();
    type.GetField("temporaryDocumentDirty",f).SetValue(window,false);WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
    if(previous!=null)previous.Focus();
}


}

private static void Pointer(VisualElement target, EventType type, Vector2 position, int clicks=1) {
    var input=new Event { type=type, button=0, mousePosition=position, clickCount=clicks };
    if(type==EventType.MouseDown) { using var evt=PointerDownEvent.GetPooled(input);evt.target=target;target.SendEvent(evt); }
    else if(type==EventType.MouseDrag) { using var evt=PointerMoveEvent.GetPooled(input);evt.target=target;target.SendEvent(evt); }
    else { using var evt=PointerUpEvent.GetPooled(input);evt.target=target;target.SendEvent(evt); }
}
private static void Key(VisualElement target, KeyCode key, char character='\0') {
    using var evt=KeyDownEvent.GetPooled(new Event { type=EventType.KeyDown, keyCode=key, character=character });
    evt.target=target;target.SendEvent(evt);
}
private static async Task QualityInteraction() {
    var window=Resources.FindObjectsOfTypeAll<WhimTexWindow>().Single(w=>w.name==Scope.Tag+"-footer");
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    var type=typeof(WhimTexWindow);
    var sliderField=type.GetField("canvasQualitySlider",flags);
    var scaleField=type.GetField("paintingCanvasScale",flags);
    var toolField=type.GetField("canvasTool",flags);
    var refresh=type.GetMethod("RefreshCanvasQualityControl",flags);
    var quality=window.rootVisualElement.Q<VisualElement>("footer900").Q<WhimTexSliderField>();
    var previousSlider=sliderField.GetValue(window);
    var previousScale=scaleField.GetValue(window);
    var previousTool=toolField.GetValue(window);
    const string pref="DCFApixels.WhimTex.Canvas.PaintingScale";
    bool hadPreference=EditorPrefs.HasKey(pref);
    float previousPreference=EditorPrefs.GetFloat(pref);
    try {
        window.Focus();
        sliderField.SetValue(window,quality);
        toolField.SetValue(window,Enum.Parse(toolField.FieldType,"Brush"));
        scaleField.SetValue(window,.5f);refresh.Invoke(window,null);
        var display=quality.Q<Label>(className:"whimtex-slider-field__value");
        T.Equal("50%",display.text,"Display uses literal percent without multiplying the UI value");
        T.Near(12.5f,quality.lowValue,0,"Quality preserves the 12.5 percent minimum");
        T.Near(100f,quality.highValue,0,"Quality preserves the 100 percent maximum");
        var input=quality.Q<VisualElement>(className:"whimtex-slider-field__input");
        Vector2 press=input.worldBound.center;
        Pointer(input,EventType.MouseDown,press);
        Pointer(input,EventType.MouseDrag,press+new Vector2(input.worldBound.width*2f,0));
        Pointer(input,EventType.MouseUp,press+new Vector2(input.worldBound.width*2f,0));
        T.Near(1f,(float)scaleField.GetValue(window),.00001,"Dragging reaches full resolution");
        T.Near(1f,EditorPrefs.GetFloat(pref),.00001,"Dragging saves the quality preference");
        scaleField.SetValue(window,.5f);refresh.Invoke(window,null);
        var editor=quality.Q<FloatField>(className:"whimtex-slider-field__editor");
        int focusEntries=0;editor.RegisterCallback<FocusInEvent>(_=>focusEntries++);
        Pointer(input,EventType.MouseDown,press,2);Pointer(input,EventType.MouseUp,press,2);
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(100,Cancellation);
        T.True(quality.isEditing,"Double click opens quality keyboard editing");
        var focused=editor.focusController.focusedElement as VisualElement;
        T.True(focusEntries>0&&focused!=null&&(focused==quality||focused==editor||editor.Contains(focused)),"Quality editor receives keyboard focus");
        var editingTarget=focused==editor||editor.Contains(focused)?focused:
            editor.Q<VisualElement>(className:"unity-base-text-field__input").Q<TextElement>();
        editor.textSelection.SelectAll();
        foreach(char character in "37.5")Key(editingTarget,KeyCode.None,character);
        refresh.Invoke(window,null);
        T.True(quality.isEditing&&editor.text=="37.5","Quality refresh preserves the pending keyboard draft");
        T.Near(.5f,(float)scaleField.GetValue(window),0,"Pending text leaves the quality model unchanged");
        Key(editingTarget,KeyCode.Return,'\n');
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(100,Cancellation);
        T.Near(.375f,(float)scaleField.GetValue(window),.00001,"Enter commits the typed percentage to the quality scale");
        T.Near(.375f,EditorPrefs.GetFloat(pref),.00001,"Keyboard commit saves the quality preference");
        T.True(!quality.isEditing&&display.text=="37.5%","Enter returns to the combined percentage display");
        toolField.SetValue(window,Enum.Parse(toolField.FieldType,"Pencil"));refresh.Invoke(window,null);
        T.True(!quality.enabledSelf&&display.text=="100%","Pencil locks the combined field at full resolution");
        T.Near(.375f,(float)scaleField.GetValue(window),.00001,"Pencil preserves the preferred scale");
        T.Near(.375f,EditorPrefs.GetFloat(pref),.00001,"Pencil does not overwrite the saved preference");
        quality.BeginEditing();T.True(!quality.isEditing,"Pencil prevents keyboard editing");
        toolField.SetValue(window,Enum.Parse(toolField.FieldType,"Brush"));refresh.Invoke(window,null);
        T.True(quality.enabledSelf&&display.text=="37.5%","Returning to Brush restores the preferred quality");
        await WhimTex.Tests.UnityA.UnityAAsync.Delay(100,Cancellation);
        string path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),"Temp","WhimTex","canvas-view-footer-preview-"+Guid.NewGuid().ToString("N")+".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        await WhimTex.Tests.UnityA.UnityACapture.Capture(window,path,Cancellation);
    } finally {
        sliderField.SetValue(window,previousSlider);scaleField.SetValue(window,previousScale);toolField.SetValue(window,previousTool);
        if(hadPreference)EditorPrefs.SetFloat(pref,previousPreference);else EditorPrefs.DeleteKey(pref);
        refresh.Invoke(window,null);
    }
}
private static async Task<string> BodyFooterScenario() {
SetupFixture(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); await QualityInteraction(); FooterAssertions();
return null;
}

public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyFooterScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
