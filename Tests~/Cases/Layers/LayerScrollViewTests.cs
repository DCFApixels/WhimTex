using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class LayerScrollViewTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("LayerScrollViewTests", Body);
    static void Body()
    {
        // Clamp arithmetic is independent of layout and the legacy persistence fixture.
        var window = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<DCFApixels.WhimTex.WhimTexWindow>());
        try
        {
        window.titleContent = new GUIContent("WhimTex isolated scroll regression " + Guid.NewGuid().ToString("N"));
        window.Show();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var scroll=(UnityEngine.UIElements.ScrollView)window.GetType().GetField("toolkitSettingsScroll",flags).GetValue(window);
        var type=window.GetType().GetNestedType("LayerDragAutoScrollManipulator",System.Reflection.BindingFlags.NonPublic);
        var clamp=type.GetMethod("ClampOffset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        float Bound(float x,float low,float high)=>(float)clamp.Invoke(null,new object[]{x,low,high});
        WhimTex.Tests.UnityC.FixtureContext.Context.True(!(Bound(8,0,-300)!=0 || Bound(-8,0,-300)!=0 || Bound(900,0,300)!=300 || Bound(100,0,300)!=100), "Invalid scroll clamp");
        float old=Mathf.Clamp(8,scroll.verticalScroller.lowValue,scroll.verticalScroller.highValue);
        float corrected=Bound(8,scroll.verticalScroller.lowValue,scroll.verticalScroller.highValue);
        return;
        }
        finally { global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window); }
        
    }
}
