// Uses only the disposable window created by LayerPersistenceSetup, after layout has settled.
var window = Resources.FindObjectsOfTypeAll<DCFApixels.WhimTex.TextureCompositorWindow>()
    .Single(w => w.titleContent.text == "WhimTex verification b4f21b35");
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var scroll=(UnityEngine.UIElements.ScrollView)window.GetType().GetField("toolkitSettingsScroll",flags).GetValue(window);
var type=window.GetType().GetNestedType("LayerDragAutoScrollManipulator",System.Reflection.BindingFlags.NonPublic);
var clamp=type.GetMethod("ClampOffset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
float Bound(float x,float low,float high)=>(float)clamp.Invoke(null,new object[]{x,low,high});
if(Bound(8,0,-300)!=0 || Bound(-8,0,-300)!=0 || Bound(900,0,300)!=300 || Bound(100,0,300)!=100)
    throw new Exception("Invalid scroll clamp");
float old=Mathf.Clamp(8,scroll.verticalScroller.lowValue,scroll.verticalScroller.highValue);
float corrected=Bound(8,scroll.verticalScroller.lowValue,scroll.verticalScroller.highValue);
return new { viewport=scroll.contentViewport.layout.height, content=scroll.contentContainer.layout.height,
    low=scroll.verticalScroller.lowValue, high=scroll.verticalScroller.highValue, oldOffset=old, correctedOffset=corrected };
