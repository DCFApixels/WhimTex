// Optional visual fixture. Run UvUiSmoke.cs afterwards to verify and close it.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var type = typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
foreach (var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>())
    if (existing.name == "WhimTex UV smoke") throw new System.Exception("UV smoke window already exists; finish it first.");
var previous = UnityEditor.EditorWindow.focusedWindow;
var window = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositorWindow>();
window.name = "WhimTex UV smoke";
var document = (DCFApixels.SpriteEditor.TextureCompositor)type.GetField("compositor",flags).GetValue(window);
document.width=512; document.height=512;
document.layers.Add(new DCFApixels.SpriteEditor.Layer(new DCFApixels.SpriteEditor.ColorFillLayerBehaviour { color = new UnityEngine.Color(.24f,.25f,.3f,1) }));
var mesh=new UnityEngine.Mesh {name="UV smoke mesh",hideFlags=UnityEngine.HideFlags.HideAndDontSave};
mesh.vertices=new[]{new UnityEngine.Vector3(.08f,.1f,0),new UnityEngine.Vector3(.48f,.1f,0),new UnityEngine.Vector3(.48f,.85f,0),new UnityEngine.Vector3(.08f,.85f,0),
    new UnityEngine.Vector3(.6f,.18f,0),new UnityEngine.Vector3(.92f,.25f,0),new UnityEngine.Vector3(.85f,.55f,0),new UnityEngine.Vector3(.62f,.5f,0),
    new UnityEngine.Vector3(.61f,.7f,0),new UnityEngine.Vector3(.9f,.65f,0),new UnityEngine.Vector3(.85f,.9f,0)};
var uvs=new UnityEngine.Vector2[mesh.vertexCount];
for(int i=0;i<uvs.Length;i++) uvs[i]=new UnityEngine.Vector2(mesh.vertices[i].x,mesh.vertices[i].y);
mesh.uv=uvs; mesh.triangles=new[]{0,1,2,0,2,3,4,5,6,4,6,7,8,9,10};
typeof(DCFApixels.SpriteEditor.TextureCompositor).GetField("uvReferenceMesh",flags).SetValue(document,mesh);
type.GetField("uvEnabled",flags).SetValue(window,true);
type.GetField("uvExpanded",flags).SetValue(window,true);
type.GetField("postFxExpanded",flags).SetValue(window,false);
type.GetField("brushesExpanded",flags).SetValue(window,false);
type.GetField("previewTool",flags).SetValue(window,System.Enum.Parse(type.GetNestedType("PreviewTool",flags),"RectangleSelect"));
type.GetField("marqueeShape",flags).SetValue(window,System.Enum.Parse(type.GetNestedType("MarqueeShape",flags),"UvIsland"));
window.position=new UnityEngine.Rect(180,150,1100,760);
window.ShowUtility(); window.CreateGUI();
window.rootVisualElement.userData=previous;
type.GetMethod("RefreshUvReference",flags).Invoke(window,null);
return "Opened temporary UV fixture; no scene/assets changed. Run UvUiSmoke.cs to verify/close.";
