// Run three times after PreviewFooterSetup.cs; finish with PreviewFooterSmoke.cs.
DCFApixels.WhimTex.TextureCompositorWindow window=null;
foreach(var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>())
    if(candidate.name=="Preview footer smoke")window=candidate;
if(window==null)throw new System.Exception("Run PreviewFooterSetup.cs first.");
var footer=UnityEngine.UIElements.UQueryExtensions.Q(window.rootVisualElement,"footer900");
var hint=(UnityEngine.UIElements.Label)footer[1];
int stage=hint.userData is int value?value:0;
if(stage==0)
{
    hint.text="Drag move • handles scale • circle rotate";
    hint.GetType().GetMethod("RefreshVisibility").Invoke(hint,null);
    if(hint.ClassListContains("whimtex-preview-status--hidden"))throw new System.Exception("Hint should fit at 900 px.");
    footer.style.width=650;hint.userData=1;
    return "Shrink to 650 px; run again after layout.";
}
if(stage==1)
{
    if(hint.resolvedStyle.visibility!=UnityEngine.UIElements.Visibility.Hidden)throw new System.Exception("Resize must hide overflowing text automatically.");
    footer.style.width=900;hint.userData=2;
    return "Expand to 900 px; run again after layout.";
}
if(hint.resolvedStyle.visibility!=UnityEngine.UIElements.Visibility.Visible)throw new System.Exception("Expanding must restore text automatically.");
return "PASS: hint visible at 900, hidden at 650, restored at 900. Run PreviewFooterSmoke.cs to finish and close fixture.";
