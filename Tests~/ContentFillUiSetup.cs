// Temporary, unsaved fixture. Run ContentFillUiSmoke.cs to check and close it.
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
var type=typeof(DCFApixels.WhimTex.TextureCompositorWindow);
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
    if(item.name=="Content fill smoke")throw new System.Exception("Finish the previous ContentFillUiSmoke first.");
var previous=UnityEditor.EditorWindow.focusedWindow;
var window=UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
window.name="Content fill smoke";
var doc=(DCFApixels.WhimTex.TextureCompositor)type.GetField("compositor",f).GetValue(window);
doc.width=128;doc.height=96;
doc.layers.Add(new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour{color=new UnityEngine.Color(.125f,.5f,.25f,1)}));
type.GetMethod("SelectOnlyLayer",f).Invoke(window,new object[]{doc.layers[0].Id});
type.GetField("previewTool",f).SetValue(window,System.Enum.Parse(type.GetNestedType("PreviewTool",f),"RectangleSelect"));
window.position=new UnityEngine.Rect(80,100,1100,700);window.ShowUtility();window.CreateGUI();window.rootVisualElement.userData=previous;
var selection=type.GetMethod("GetAreaSelection",f).Invoke(window,null);
byte[] mask=new byte[128*96];
for(int y=12;y<68;y++)for(int x=22;x<104;x++)mask[y*128+x]=255;
selection.GetType().GetMethod("Set",f).Invoke(selection,new object[]{mask,System.Enum.Parse(type.Assembly.GetType("DCFApixels.WhimTex.SelectionCombine"),"Replace")});
type.GetMethod("OpenContentAwareFill",f).Invoke(window,null);
var fillType=type.GetNestedType("ContentFillWindow",f);
UnityEditor.EditorWindow fill=null;
foreach(var item in UnityEngine.Resources.FindObjectsOfTypeAll(fillType))if((object)fillType.GetField("owner",f).GetValue(item)==window)fill=(UnityEditor.EditorWindow)item;
if(fill==null)throw new System.Exception("Fill window missing");
fill.position=new UnityEngine.Rect(1200,130,460,630);
var area=(UnityEngine.UIElements.EnumField)UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.EnumField>(fill.rootVisualElement).ToList()[1];
area.value=(System.Enum)System.Enum.Parse(fillType.GetNestedType("FillArea",f),"InnerBorder");
fillType.GetMethod("Generate",f).Invoke(fill,null);
return "Opened temporary Content-Aware Fill fixture; waiting for asynchronous preview. Run ContentFillUiSmoke.cs next.";
