var type=typeof(DCFApixels.SpriteEditor.TextureCompositorWindow);
var f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
DCFApixels.SpriteEditor.TextureCompositorWindow window=null;
foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<DCFApixels.SpriteEditor.TextureCompositorWindow>())
    if(existing.name=="Preview footer smoke")window=existing;
if(window==null)throw new System.Exception("Run PreviewFooterSetup.cs first.");
var root=window.rootVisualElement;var previous=root.userData as UnityEditor.EditorWindow;
int checks=0;void Check(bool value,string label){if(!value)throw new System.Exception(label);checks++;}
UnityEngine.UIElements.VisualElement Find(UnityEngine.UIElements.VisualElement parent,string name)=>UnityEngine.UIElements.UQueryExtensions.Q(parent,name);
try
{
    foreach(int width in new[]{900,600,599,400,280,200})
    {
        var footer=Find(root,"footer"+width);
        var left=Find(footer,"previewFooterLeft");var right=Find(footer,"previewFooterRight");
        var updates=Find(left,"previewFooterUpdates");var context=Find(left,"previewFooterContext");
        var inspection=Find(right,"previewFooterInspection");var color=Find(right,"previewFooterColor");
        Check(footer.childCount==3&&left.childCount==2&&right.childCount==2,"Two groups per side and central hint");
        Check(updates.childCount==2&&updates[0].ClassListContains("sprite-editor-preview-quality")&&updates[1].ClassListContains("sprite-editor-live-output-button"),"Quality and Live Update left");
        Check(((UnityEngine.UIElements.Button)context[0]).text=="Post FX"&&((UnityEngine.UIElements.Button)context[1]).text=="UV","Viewing context left");
        Check(inspection.childCount==3&&((UnityEngine.UIElements.Button)inspection[0]).text=="HDR"&&inspection[1] is UnityEngine.UIElements.FloatField&&inspection[2].ClassListContains("sprite-editor-debug-button"),"HDR immediately before EV, then Debug");
        Check(color.childCount==4,"RGBA in separate group");
        for(int i=0;i<4;i++)Check(((UnityEngine.UIElements.Button)color[i]).text==new[]{"R","G","B","A"}[i],"RGBA order unchanged");
        Check(footer.ClassListContains("sprite-editor-preview-footer--compact")== (width<600),"Compact breakpoint");
        var hint=(UnityEngine.UIElements.Label)footer[1];
        var refreshHint=hint.GetType().GetMethod("RefreshVisibility");
        foreach(string text in new[]{"Add a layer to start",new string('W',300),"", "Ready"})
        {
            hint.text=text;refreshHint.Invoke(hint,null);
            float required=hint.MeasureTextSize(text,0,UnityEngine.UIElements.VisualElement.MeasureMode.Undefined,0,UnityEngine.UIElements.VisualElement.MeasureMode.Undefined).x;
            Check(hint.ClassListContains("sprite-editor-preview-status--hidden") == (text.Length==0||!(required<=hint.contentRect.width)),"Hide the entire hint only when empty or too wide");
        }
        foreach(var group in new[]{updates,context,inspection,color})
        {
            Check(group.worldBound.xMin>=footer.worldBound.xMin-.1f&&group.worldBound.xMax<=footer.worldBound.xMax+.1f,"Group fits horizontally at "+width+": "+group.name);
            Check(group.worldBound.yMax<=footer.worldBound.yMax+.1f,"Group fits vertically at "+width);
            for(int i=1;i<group.childCount;i++)Check(group[i].worldBound.xMin>=group[i-1].worldBound.xMax-.1f,"Controls do not overlap");
        }
        if(width>=600)
        {
            Check(System.Math.Abs(footer.resolvedStyle.height-26)<.1f,"Normal footer height unchanged");
            Check(left.worldBound.xMax<=right.worldBound.xMin,"Left/right separated");
            Check(System.Math.Abs(left.worldBound.center.y-right.worldBound.center.y)<.1f,"Sides on same row");
        }
    }
    return $"Preview footer: {checks} checks passed across 900, 600, 599, 400, 280 and 200 px; grouping, order, height and no clipping/overlap.";
}
finally
{
    typeof(UnityEditor.EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window,false);
    type.GetField("temporaryDocumentDirty",f).SetValue(window,false);window.Close();
    if(previous!=null)previous.Focus();
}
