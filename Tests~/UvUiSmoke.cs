var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var type=typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
DCFApixels.SpriteEditor.TextureCompositorWindow window=null;
foreach(var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>()) if(candidate.name=="WhimTex UV smoke") window=candidate;
if(window==null) throw new System.Exception("Run UvUiSetup.cs first.");
var doc=(DCFApixels.SpriteEditor.TextureCompositor)type.GetField("compositor",flags).GetValue(window);
var mesh=(UnityEngine.Mesh)typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceMesh",flags).GetValue(doc);
var previous=window.rootVisualElement.userData as UnityEditor.EditorWindow;
int checks=0;
void Check(bool value,string label){if(!value)throw new System.Exception(label);checks++;}
object Read(string name)=>type.GetField(name,flags).GetValue(window);
void Write(string name,object value)=>type.GetField(name,flags).SetValue(window,value);
object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(window,args);
try
{
    var canvas=(UnityEngine.UIElements.VisualElement)Read("toolkitPreviewCanvas");
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
    var combine=type.Assembly.GetType("DCFApixels.SpriteEditor.SelectionCombine");
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
    var viewport=Read("previewViewport");
    viewport.GetType().GetMethod("SetRotation",flags).Invoke(viewport,new object[]{37f,false});
    canvas.GetType().GetMethod("UpdateImageLayout").Invoke(canvas,null);
    Check((int)Call("PickUvIsland",Point(.3f,.4f))==0,"Picking uses inverse rotated view mapping");
    Call("SelectUvIsland",Point(.3f,.4f),System.Enum.Parse(combine,"Replace"));
    byte[] before=(byte[])Pixels().Clone();
    Write("previewTool",System.Enum.Parse(type.GetNestedType("PreviewTool",flags),"Brush"));
    Call("RefreshPreviewToolToolbar");
    Check(!(bool)type.GetProperty("IsUvSelectionTool",flags).GetValue(window),"Brush is not UV selection tool");
    Check((int)Read("uvHoveredIsland")==-1,"Brush clears UV hover marker");
    Write("brushesExpanded",true);Write("uvExpanded",false); Call("RefreshPostFxPanel");
    Check(drawer.ClassListContains("sprite-editor-post-fx-drawer--hidden"),"Brush drawer hides UV drawer");
    Write("uvEnabled",false); Call("RefreshPostFxPanel");
    for(int i=0;i<before.Length;i++) Check(before[i]==Pixels()[i],"Hiding UV/switching tool preserves selection");
    return $"UV UI: {checks} checks passed: panel, cache, rotated picking, selection operations, paint isolation. Fixture closed.";
}
finally
{
    typeof(UnityEditor.EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window,false);
    Write("temporaryDocumentDirty",false);
    window.Close();
    if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
    if(previous!=null)previous.Focus();
}
