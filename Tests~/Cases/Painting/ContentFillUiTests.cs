using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class ContentFillUiTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

private static DCFApixels.WhimTex.TextureCompositorWindow SetupFixture(){
// Temporary, unsaved fixture. Run ContentFillUiTests.cs to check and close it.
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var type=typeof(DCFApixels.WhimTex.TextureCompositorWindow);
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
    T.True(!(item.name==(Scope.Tag + "-fill")), "Finish the previous ContentFillUiTests first.");
var previous=UnityEditor.EditorWindow.focusedWindow;
var window=Scope.OwnWindow(UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>());
window.name=(Scope.Tag + "-fill");
var doc=(DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor",f).GetValue(window);
doc.width=128;doc.height=96;
doc.layers.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour{color=new UnityEngine.Color(.125f,.5f,.25f,1)}));
type.GetMethod("SelectOnlyLayer",f).Invoke(window,new object[]{doc.layers[0].Id});
type.GetField("canvasTool",f).SetValue(window,System.Enum.Parse(type.GetNestedType("CanvasTool",f),"RectangleSelect"));
window.position=new UnityEngine.Rect(80,100,1100,700);window.ShowUtility();window.CreateGUI();window.rootVisualElement.userData=previous;
var selection=type.GetMethod("GetAreaSelection",f).Invoke(window,null);
byte[] mask=new byte[128*96];
for(int y=12;y<68;y++)for(int x=22;x<104;x++)mask[y*128+x]=255;
selection.GetType().GetMethod("Set",f).Invoke(selection,new object[]{mask,System.Enum.Parse(type.Assembly.GetType("DCFApixels.WhimTex.SelectionCombine"),"Replace")});
type.GetMethod("OpenContentAwareFill",f).Invoke(window,null);
var fillType=type.GetNestedType("ContentFillWindow",f);
UnityEditor.EditorWindow fill=null;
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=OwnFill((UnityEditor.EditorWindow)item);
T.True(!(fill==null), "Fill window missing");
fill.position=new UnityEngine.Rect(1200,130,460,630);
var area=(UnityEngine.UIElements.EnumField)UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.EnumField>(fill.rootVisualElement).ToList()[1];
area.value=(System.Enum)System.Enum.Parse(fillType.GetNestedType("FillArea",f),"InnerBorder");
fillType.GetMethod("Generate",f).Invoke(fill,null);
return window;


}
private static async Task<string> FillAssertions(){
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static;
var type=typeof(DCFApixels.WhimTex.TextureCompositorWindow);var fillType=type.GetNestedType("ContentFillWindow",f);
DCFApixels.WhimTex.TextureCompositorWindow window=null;
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())if(item.name==(Scope.Tag + "-fill"))window=item;
T.True(!(window==null), "Run ContentFillUiSetup first.");
UnityEditor.EditorWindow fill=null;
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=OwnFill((UnityEditor.EditorWindow)item);
var previous=window.rootVisualElement.userData as UnityEditor.EditorWindow;
var doc=(DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor",f).GetValue(window);
int checks=0;void Check(bool value,string label){ T.True(value, label); }
object Read(string name)=>fillType.GetField(name,f).GetValue(fill);
object Call(string name,params object[] args)=>fillType.GetMethod(name,f).Invoke(fill,args);
try
{
    Check(fill!=null,"Fill window opened");
    await WaitForWorker((Task)Read("task")); Call("Update");
    Check(Read("task")==null&&Read("result")!=null,"Async preview completed");
    Check(doc.layers.Count==1,"Preview never inserts layers");
    Check(((UnityEngine.UIElements.Button)Read("applyButton")).enabledSelf,"Apply enabled");
    Check(((UnityEngine.UIElements.Image)Read("image")).resolvedStyle.height>=140,"Preview has flexible visible layout");
    Check(((UnityEngine.UIElements.Button)Read("applyButton")).worldBound.yMax<=fill.rootVisualElement.worldBound.yMax,"Footer not clipped");
    Check(((UnityEngine.UIElements.IntegerField)Read("widthField")).enabledSelf,"Inner border width editable");
    // Selection changes must not change the captured target.
    var selection=type.GetMethod("GetAreaSelection",f).Invoke(window,null);
    selection.GetType().GetMethod("All",f).Invoke(selection,null);
    Check(Read("result")!=null,"Changing selection keeps generated target");
    Call("Apply");
    Check(doc.layers.Count==2,"Apply inserts exactly one new layer");
    var added=doc.layers[0];var drawing=added.Behaviour;
    var texture=(UnityEngine.Texture2D)drawing.GetType().GetProperty("StoredTexture",f).GetValue(drawing);
    Check(texture.width==82&&texture.height==56,"Result stored cropped at native resolution");
    Check(texture.format==UnityEngine.TextureFormat.RGBAHalf,"HDR storage");
    var pixels=texture.GetPixels();
    for(int y=0;y<56;y++)for(int x=0;x<82;x++)
    {
        bool strip=x<16||x>=66||y<16||y>=40;
        Check(strip?pixels[y*82+x].a>.999f:pixels[y*82+x].a==0,"Only inward strip written");
    }
    Check(((UnityEngine.Vector2)added.transform.position)==new UnityEngine.Vector2(-1,-8),"Cropped image correct canvas-space placement");
    Check(((UnityEngine.Vector2)added.transform.scale)==new UnityEngine.Vector2(82f/128,56f/96),"Cropped pixels retain native size");
    // Render the actual new layer through the compositor, not just the CPU result.
    var source=typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("RenderAreaSelectionSource",f);
    var rendered=(UnityEngine.Texture2D)source.Invoke(doc,new object[]{added});
    try
    {
        var canvas=rendered.GetPixels();
        for(int y=0;y<96;y++)for(int x=0;x<128;x++)
        {
            bool inside=x>=22&&x<104&&y>=12&&y<68;
            bool strip=inside&&(x<38||x>=88||y<28||y>=52);
            Check(strip?canvas[y*128+x].a>.99f:canvas[y*128+x].a<.001f,"GPU rendering exact placement and unchanged center/outside");
        }
    }
    finally { UnityEngine.Object.DestroyImmediate(rendered); }
    UnityEditor.Undo.PerformUndo();Check(doc.layers.Count==1,"Single Undo removes the result");
    UnityEditor.Undo.PerformRedo();Check(doc.layers.Count==2,"Redo restores result");
    type.GetMethod("OpenContentAwareFill",f).Invoke(window,null);
    foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=OwnFill((UnityEditor.EditorWindow)item);
    // With Entire Selection, an all-canvas target has no donors: useful no-source diagnostic.
    Call("Generate");var task=(System.Threading.Tasks.Task)Read("task");
    try{await WaitForWorker(task);}catch(System.AggregateException){}
    Call("Update");
    Check(Read("result")==null&&((UnityEngine.UIElements.Label)Read("message")).text.Contains("source"),"No donors reports actionable message");
    Check(!(bool)((UnityEngine.UIElements.Button)Read("applyButton")).enabledSelf,"Invalid result cannot apply");
    await WhimTex.Tests.UnityA.UnityAFillWorker.Stop(fill); fill.Close();
    byte[] target=new byte[128*96];for(int y=20;y<40;y++)for(int x=40;x<60;x++)target[y*128+x]=255;
    selection.GetType().GetMethod("Set",f).Invoke(selection,new object[]{target,System.Enum.Parse(type.Assembly.GetType("DCFApixels.WhimTex.SelectionCombine"),"Replace")});
    type.GetMethod("OpenContentAwareFill",f).Invoke(window,null);
    foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=OwnFill((UnityEditor.EditorWindow)item);
    var captured=(byte[])Read("selection");
    selection.GetType().GetMethod("All",f).Invoke(selection,null);
    Call("CaptureSourceSelection");
    Check(object.ReferenceEquals(captured,Read("selection"))&&captured[0]==0,"Capturing custom source preserves original target");
    Check(((byte[])Read("customSelection"))[0]==255,"Custom source uses new selection");
    fillType.GetField("sampling",f).SetValue(fill,System.Enum.Parse(fillType.GetNestedType("Sampling",f),"CustomSelection"));
    Call("Generate");task=(System.Threading.Tasks.Task)Read("task");await WaitForWorker(task);Call("Update");
    Check(Read("result")!=null,"Custom sampling generates result");
    typeof(DCFApixels.WhimTex.TextureCompositor).GetMethod("MarkChanged",f).Invoke(doc,null);
    Check(Read("result")==null&&!((UnityEngine.UIElements.Button)Read("applyButton")).enabledSelf,"Source edit invalidates result");
    int previousCount=doc.layers.Count;Call("Apply");Check(doc.layers.Count==previousCount,"Stale apply cannot add layer");
    Call("Generate");Call("Cancel");task=(System.Threading.Tasks.Task)Read("task");
    try{await WaitForWorker(task);}catch(System.AggregateException){}
    Call("Update");
    Check(Read("result")==null&&Read("task")==null&&doc.layers.Count==previousCount,"Cancellation discards result without mutation");
    Check(fillType.GetField("activeTask",f).GetValue(null)==null,"Completed worker releases global task/result reference");
    doc.width=129;Call("Update");Check(!((UnityEngine.UIElements.Button)Read("generateButton")).enabledSelf,"Canvas resize prevents using captured mask");doc.width=128;
    var invertToggle=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Toggle>(fill.rootVisualElement,"invertSelection");
    Check(invertToggle!=null&&!invertToggle.value,"Inversion defaults off");
    captured[20*128+40]=64;
    invertToggle.value=true;
    Call("Generate");task=(System.Threading.Tasks.Task)Read("task");await WaitForWorker(task);Call("Update");
    Check(Read("result")!=null,"Inverse selection generates a result");
    Check((UnityEngine.RectInt)Read("fillBounds")==new UnityEngine.RectInt(0,0,128,96),"Inverse bounds include outside original rectangle");
    byte[] ResultMask()=> (byte[])Read("result").GetType().GetField("target",f).GetValue(Read("result"));
    var inverseMask=ResultMask();
    for(int i=0;i<captured.Length;i++)Check(inverseMask[i]==255-captured[i],"Inversion preserves complementary soft coverage");
    Check(((byte[])Read("customSelection"))[0]==255&&captured[0]==0,"Inversion does not mutate either captured selection");
    fillType.GetField("area",f).SetValue(fill,System.Enum.Parse(fillType.GetNestedType("FillArea",f),"InnerBorder"));
    fillType.GetField("borderWidth",f).SetValue(fill,2);captured[20*128+40]=255;
    Call("Generate");task=(System.Threading.Tasks.Task)Read("task");await WaitForWorker(task);Call("Update");
    var borderMask=ResultMask();
    Check(borderMask[30*128+39]==255&&borderMask[30*128+38]==255&&borderMask[30*128+37]==0,"Inner Border applied after inversion, outside original edge");
    Check(borderMask[30*128+40]==0&&borderMask[0]==255,"Original interior excluded; canvas boundary included");
    invertToggle.value=false;Check(Read("result")==null,"Toggling inversion invalidates preview");invertToggle.value=true;
    fillType.GetField("area",f).SetValue(fill,System.Enum.Parse(fillType.GetNestedType("FillArea",f),"EntireSelection"));
    Call("Generate");task=(System.Threading.Tasks.Task)Read("task");await WaitForWorker(task);Call("Update");
    Call("Apply");Check(doc.layers.Count==previousCount+1,"Inverse result adds a new layer");
    var inverseLayer=doc.layers[0];
    var inverseTexture=(UnityEngine.Texture2D)inverseLayer.Behaviour.GetType().GetProperty("StoredTexture",f).GetValue(inverseLayer.Behaviour);
    Check(inverseTexture.width==128&&inverseTexture.height==96&&((UnityEngine.Vector2)inverseLayer.transform.position)==UnityEngine.Vector2.zero,"Inverse result not cropped to old bounds");
    var inversePixels=inverseTexture.GetPixels();
    for(int i=0;i<captured.Length;i++)Check(captured[i]==255?inversePixels[i].a==0:inversePixels[i].a>.999f,"Applied pixels match inverted region");
    var currentMask=(byte[])selection.GetType().GetProperty("Coverage",f).GetValue(selection);
    foreach(byte coverage in currentMask)Check(coverage==255,"Main window selection remains untouched");
    type.GetMethod("OpenContentAwareFill",f).Invoke(window,null);
    foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=OwnFill((UnityEditor.EditorWindow)item);
    UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Toggle>(fill.rootVisualElement,"invertSelection").value=true;
    Call("Generate");
    Check(Read("task")==null&&Read("result")==null&&((UnityEngine.UIElements.Label)Read("message")).text.Contains("No pixels"),"Full-canvas inversion reports empty before processing");
    return null;
}
finally
{
    if(fill!=null) { await WhimTex.Tests.UnityA.UnityAFillWorker.Stop(fill); fill.Close(); }
    UnityEditor.Undo.ClearUndo(doc);
    foreach(var layer in doc.layers)
        if(layer?.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour drawing)
        {var texture=(UnityEngine.Texture2D)drawing.GetType().GetProperty("StoredTexture",f).GetValue(drawing);if(texture!=null)UnityEditor.Undo.ClearUndo(texture);}
    window.DiscardChanges();type.GetField("temporaryDocumentDirty",f).SetValue(window,false);WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
    if(previous!=null)previous.Focus();
}


}
static void CaptureScreen(EditorWindow window)
{
    var rect = window.position;
    int w = (int)rect.width, h = (int)rect.height;
    var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
    try
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditorInternal.InternalEditorUtility", true);
        var read = type.GetMethod("ReadScreenPixel", flags, null, new[] { typeof(Vector2), typeof(int), typeof(int) }, null);
        texture.SetPixels((Color[])read.Invoke(null, new object[] { rect.position, w, h })); texture.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Scope.Temp, "WhimTexContentFillUi.png"), UnityEngine.ImageConversion.EncodeToPNG(texture));
    }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
static EditorWindow OwnFill(EditorWindow fill) { Scope.OwnWindow(fill); Scope.FinallyAsync(() => WhimTex.Tests.UnityA.UnityAFillWorker.Stop(fill)); return fill; }
static async Task WaitForWorker(Task task) { if(task==null)return; while(!task.IsCompleted) await WhimTex.Tests.UnityA.UnityAAsync.Delay(25,Cancellation); }
private static async Task<string> BodyFillScenario() { const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic; var fillType=typeof(TextureCompositorWindow).GetNestedType("ContentFillWindow",flags); var existing=(Task)fillType.GetField("activeTask",flags).GetValue(null); T.True(existing==null || existing.IsCompleted,"Do not borrow a running Content Fill worker"); var owner = SetupFixture(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); EditorWindow fill=null; foreach(var item in Resources.FindObjectsOfTypeAll(fillType)) if((object)fillType.GetField("owner",flags).GetValue(item)==owner) fill=(EditorWindow)item; T.True(fill!=null,"Owned fill fixture is present"); await WaitForWorker((Task)fillType.GetField("task",flags).GetValue(fill)); await WhimTex.Tests.UnityA.UnityACapture.Capture(fill,Scope.Temp+"/content-fill-ui.png",Cancellation); CaptureScreen(fill); await FillAssertions(); return null; }
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyFillScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
