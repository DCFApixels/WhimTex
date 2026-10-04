// Transient UI layout fixture; finish with CanvasViewFooterSmoke.cs. No saved assets.
var type=typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
    if(existing.name=="Canvas View footer smoke")throw new System.Exception("Finish the previous footer fixture first.");
var previous=UnityEditor.EditorWindow.focusedWindow;
var window=UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
window.name="Canvas View footer smoke";window.position=new UnityEngine.Rect(140,140,960,680);window.ShowUtility();window.CreateGUI();
var root=window.rootVisualElement;root.Clear();root.userData=previous;
foreach(int width in new[]{900,700,680,600,599,400,280,200})
{
    root.Add(new UnityEngine.UIElements.Label(width+" px"));
    var footer=(UnityEngine.UIElements.VisualElement)type.GetMethod("BuildCanvasViewFooter",f).Invoke(window,null);
    footer.name="footer"+width;footer.style.width=width;
    root.Add(footer);
}
return "Footer fixture opened. Run CanvasViewFooterSmoke.cs after layout settles.";
