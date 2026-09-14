// Transient UI layout fixture; finish with PreviewFooterSmoke.cs. No saved assets.
var type=typeof(DCFApixels.WhimTex.TextureCompositorWindow);
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
    if(existing.name=="Preview footer smoke")throw new System.Exception("Finish the previous footer fixture first.");
var previous=UnityEditor.EditorWindow.focusedWindow;
var window=UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositorWindow>();
window.name="Preview footer smoke";window.position=new UnityEngine.Rect(140,140,960,680);window.ShowUtility();window.CreateGUI();
var root=window.rootVisualElement;root.Clear();root.userData=previous;
foreach(int width in new[]{900,600,599,400,280,200})
{
    root.Add(new UnityEngine.UIElements.Label(width+" px"));
    var footer=(UnityEngine.UIElements.VisualElement)type.GetMethod("BuildPreviewFooter",f).Invoke(window,null);
    footer.name="footer"+width;footer.style.width=width;
    root.Add(footer);
}
return "Footer fixture opened. Run PreviewFooterSmoke.cs after layout settles.";
