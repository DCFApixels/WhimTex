// Self-contained UI regression, including the complete former optional UV fixture.
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
public static class UvUiTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);
    static async Task Execute(TestContext context, System.Threading.CancellationToken token)
    {
        DCFApixels.WhimTex.TextureCompositorWindow window = null;
        DCFApixels.WhimTex.TextureCompositor document = null;
        UnityEngine.Mesh mesh = null;
        var previousFocus = EditorWindow.focusedWindow;
        try
        {
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var type = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var previous = UnityEditor.EditorWindow.focusedWindow;
window = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
window.name = "WhimTex UV " + Guid.NewGuid().ToString("N");
document = (DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor",flags).GetValue(window);
document.width=512; document.height=512;
document.layers.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = new UnityEngine.Color(.24f,.25f,.3f,1) }));
mesh=new UnityEngine.Mesh {name="UV smoke mesh",hideFlags=UnityEngine.HideFlags.HideAndDontSave};
mesh.vertices=new[]{new UnityEngine.Vector3(.08f,.1f,0),new UnityEngine.Vector3(.48f,.1f,0),new UnityEngine.Vector3(.48f,.85f,0),new UnityEngine.Vector3(.08f,.85f,0),
    new UnityEngine.Vector3(.6f,.18f,0),new UnityEngine.Vector3(.92f,.25f,0),new UnityEngine.Vector3(.85f,.55f,0),new UnityEngine.Vector3(.62f,.5f,0),
    new UnityEngine.Vector3(.61f,.7f,0),new UnityEngine.Vector3(.9f,.65f,0),new UnityEngine.Vector3(.85f,.9f,0)};
var uvs=new UnityEngine.Vector2[mesh.vertexCount];
for(int i=0;i<uvs.Length;i++) uvs[i]=new UnityEngine.Vector2(mesh.vertices[i].x,mesh.vertices[i].y);
mesh.uv=uvs; mesh.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7,8,9,10};
typeof(DCFApixels.WhimTex.TextureCompositor).GetField("uvReferenceMesh",flags).SetValue(document,mesh);
type.GetField("uvEnabled",flags).SetValue(window,true);
type.GetField("uvExpanded",flags).SetValue(window,true);
type.GetField("postFxExpanded",flags).SetValue(window,false);
type.GetField("brushesExpanded",flags).SetValue(window,false);
type.GetField("canvasTool",flags).SetValue(window,System.Enum.Parse(type.GetNestedType("CanvasTool",flags),"UvIslandSelect"));
type.GetField("marqueeShape",flags).SetValue(window,System.Enum.Parse(type.GetNestedType("MarqueeShape",flags),"UvIsland"));
window.position=new UnityEngine.Rect(180,150,1100,760);
window.ShowUtility(); window.CreateGUI();

type.GetMethod("RefreshUvReference",flags).Invoke(window,null);

            await Task.Delay(200, token);
            var doc = document;
int checks=0;
void Check(bool value,string label){ context.True(value, label); }
object Read(string name)=>type.GetField(name,flags).GetValue(window);
void Write(string name,object value)=>type.GetField(name,flags).SetValue(window,value);
object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(window,args);
{
    var canvas=(UnityEngine.UIElements.VisualElement)Read("toolkitCanvas");
    var overlay=(UnityEngine.UIElements.VisualElement)Read("uvOverlay");
    var drawer=(UnityEngine.UIElements.VisualElement)Read("uvDrawer");
    Check(canvas.contentRect.width>100 && canvas.contentRect.height>100,"Preview has layout");
    Check(overlay.pickingMode==UnityEngine.UIElements.PickingMode.Ignore,"UV overlay never intercepts pointer events");
    Check(drawer.resolvedStyle.display!=UnityEngine.UIElements.DisplayStyle.None,"UV drawer visible");
    Check(((UnityEngine.UIElements.VisualElement)Read("brushDrawer")).resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None,"Brush drawer mutually exclusive");
    Check(((UnityEngine.UIElements.VisualElement)Read("postFxDrawer")).resolvedStyle.display==UnityEngine.UIElements.DisplayStyle.None,"Post FX drawer mutually exclusive");
    var map=Read("uvMap"); Check(map!=null,"Mesh topology loaded");
    Call("RefreshUvReference"); Check(object.ReferenceEquals(map,Read("uvMap")),"Idle reference refresh reuses geometry");
    var image=(UnityEngine.Rect)canvas.GetType().GetProperty("ImageRect").GetValue(canvas);
    UnityEngine.Vector2 Point(float u,float v)=> (UnityEngine.Vector2)canvas.GetType().GetMethod("ToView").Invoke(canvas,new object[]{new UnityEngine.Vector2(image.x+u*image.width,image.yMax-v*image.height)});
    var combine=type.Assembly.GetType("DCFApixels.WhimTex.SelectionCombine");
    Check((int)Call("PickUvIsland",Point(.3f,.4f))==0,"Pick main island");
    Check((int)Call("PickUvIsland",Point(.75f,.35f))==1,"Pick second island");
    Call("SelectUvIsland",Point(.3f,.4f),System.Enum.Parse(combine,"Replace"));
    var selection=Read("areaSelection");
    byte[] Pixels()=>(byte[])selection.GetType().GetProperty("Coverage",flags).GetValue(selection);
    Check(Pixels()[200*512+150]==255 && Pixels()[180*512+380]==0,"UV selection fills only chosen island");
    Call("SelectUvIsland",Point(.75f,.35f),System.Enum.Parse(combine,"Add"));
    Check(Pixels()[200*512+150]==255 && Pixels()[180*512+380]==255,"Add island");
    Call("SelectUvIsland",Point(.3f,.4f),System.Enum.Parse(combine,"Subtract"));
    Check(Pixels()[200*512+150]==0 && Pixels()[180*512+380]==255,"Subtract island");
    Call("SelectUvIsland",Point(.3f,.4f),System.Enum.Parse(combine,"Intersect"));
    Check(Pixels()[180*512+380]==0,"Intersect disjoint islands");
    var viewport=Read("canvasViewport");
    viewport.GetType().GetMethod("SetRotation",flags).Invoke(viewport,new object[]{37f,false});
    canvas.GetType().GetMethod("UpdateImageLayout").Invoke(canvas,null);
    Check((int)Call("PickUvIsland",Point(.3f,.4f))==0,"Picking uses inverse rotated view mapping");
    Call("SelectUvIsland",Point(.3f,.4f),System.Enum.Parse(combine,"Replace"));
    byte[] before=(byte[])Pixels().Clone();
    Write("canvasTool",System.Enum.Parse(type.GetNestedType("CanvasTool",flags),"Brush"));
    Call("RefreshCanvasToolToolbar");
    Check(!(bool)type.GetProperty("IsUvSelectionTool",flags).GetValue(window),"Brush is not UV selection tool");
    Check((int)Read("uvHoveredIsland")==-1,"Brush clears UV hover marker");
    Write("brushesExpanded",true);Write("uvExpanded",false); Call("RefreshPostFxPanel");
    Check(drawer.ClassListContains("whimtex-post-fx-drawer--hidden"),"Brush drawer hides UV drawer");
    Write("uvEnabled",false); Call("RefreshPostFxPanel");
    for(int i=0;i<before.Length;i++) Check(before[i]==Pixels()[i],"Hiding UV/switching tool preserves selection");
    return;
}

        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if (window != null) window.DiscardChanges(); },
                () => { if (window != null) window.Close(); },
                () => { if (window != null) UnityEngine.Object.DestroyImmediate(window); },
                () => { if (document != null) Undo.ClearUndo(document); },
                () => { if (document != null) UnityEngine.Object.DestroyImmediate(document); },
                () => { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); },
                () => { if (previousFocus != null) previousFocus.Focus(); });
        }
    }
}
