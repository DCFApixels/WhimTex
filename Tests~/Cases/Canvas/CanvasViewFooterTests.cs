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
    footer.style.width=650;hint.userData=1;
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
int checks=0;void Check(bool value,string label){ T.True(value, label); }
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
        if(width<=600)Check(compact,"Compact layout follows the actual control widths at "+width);
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

private static async Task<string> BodyFooterScenario() {
SetupFixture(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); HintResize(); FooterAssertions();
return null;
}

public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyFooterScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
