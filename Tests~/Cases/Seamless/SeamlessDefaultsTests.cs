using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Mode=DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.SeamlessMode;
using Edges=DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.PoissonEdges;

public static class SeamlessDefaultsTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool condition,string message){ WhimTex.Tests.UnityC.FixtureContext.Context.True(condition, message); checks++; }
    static void Defaults(MakeSeamlessLayerBehaviour layer)
    {
        Check(layer.mode==Mode.OffsetBlend,"Default method");
        Check(layer.blendWidth==.2f&&layer.edgeWidth==.2f,"Both blend widths");
        Check(layer.mirrorTransitionStart==-.25f&&layer.offsetTransitionStart==-.25f,"Both transition starts");
        Check(layer.mirrorSeamCorrection&&layer.offsetSeamCorrection,"Both corrections on");
        Check(layer.mirrorPoissonEdges==Edges.AllEdges&&layer.offsetPoissonEdges==Edges.AllEdges,"All correction edges");
        Check(layer.leftEdge&&layer.rightEdge&&layer.topEdge&&layer.bottomEdge,"All Offset copy edges");
        Check(layer.horizontal==MakeSeamlessLayerBehaviour.HorizontalDirection.LeftToRight&&layer.vertical==MakeSeamlessLayerBehaviour.VerticalDirection.BottomToTop,"Both reflection axes");
        Check(layer.processRed&&layer.processGreen&&layer.processBlue&&layer.processAlpha,"All channels");
    }
    static string ExecuteMain()
    {
        checks=0;var assembly=typeof(TextureCompositor).Assembly;
        var model=MakeSeamlessLayerBehaviour.CreateDefault();Defaults(model);
        var registry=assembly.GetType("DCFApixels.WhimTex.LayerTypeRegistry");
        var entry=registry.GetMethod("Find",F,null,new[]{typeof(string)},null).Invoke(null,new object[]{"makeSeamless"});
        Defaults((MakeSeamlessLayerBehaviour)entry.GetType().GetMethod("CreateBehaviour",F).Invoke(entry,null));
        Defaults(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(model)));
        var old=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{\"mode\":0,\"mirrorTransitionStart\":0.5,\"offsetTransitionStart\":0.325,\"mirrorSeamCorrection\":false,\"offsetSeamCorrection\":false,\"blendWidth\":0.1,\"edgeWidth\":0.4}");
        Check(old.mode==Mode.Mirror&&old.mirrorTransitionStart==.5f&&old.offsetTransitionStart==.325f&&!old.mirrorSeamCorrection&&!old.offsetSeamCorrection&&old.blendWidth==.1f&&old.edgeWidth==.4f,"Saved settings not overwritten");
        var legacy=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{}");
        Check(legacy.mode==Mode.Mirror&&legacy.mirrorTransitionStart==0&&legacy.offsetTransitionStart==.325f&&!legacy.mirrorSeamCorrection,"Missing legacy fields retain interpretation");
        var window=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());var focus=EditorWindow.focusedWindow;
        try
        {
            window.Show();var root=window.rootVisualElement;
            var bindings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            void Refresh()=>bindings.GetType().GetMethod("Refresh",F).Invoke(bindings,new object[]{true});
            int changes=0;Action<string,Action> apply=(label,change)=>{changes++;change();Refresh();};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",F).Invoke(null,new object[]{root,model,null,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var popup=root.Q<PopupField<Mode>>("seamlessMethod");
            var expected=new[]{Mode.OffsetBlend,Mode.Mirror,Mode.ScreenedPoisson,Mode.PatchQuilting};
            var labels=new[]{"Offset Blend","Mirror","Screened Poisson","Patch Quilting"};
            Check(popup!=null&&popup.choices.Count==4,"Four ordered methods");
            for(int i=0;i<4;i++)
            {
                Check(popup.choices[i]==expected[i],"Choice order");
                popup.value=expected[i];Check(model.mode==expected[i],"Callback mapping");
                Check(popup.formatListItemCallback(expected[i])==labels[i],"Display label");
            }
            Check(changes==3,"One change per distinct selection");
            model.mode=Mode.OffsetBlend;Refresh();Check(popup.value==Mode.OffsetBlend,"Binding refresh");Defaults(model);
            Check(root.Q<Slider>("offsetTransitionStart").value==-25&&root.Q<Slider>("mirrorTransitionStart").value==-25,"Percent controls");
            Check(root.Q<Toggle>("offsetSeamCorrection").value&&root.Q<Toggle>("mirrorSeamCorrection").value,"Correction controls");
            foreach(string id in new[]{"offsetPoissonEdges","mirrorPoissonEdges"})foreach(string edge in new[]{"left","right","top","bottom"})
                Check(root.Q<Button>(id+"-"+edge).ClassListContains("whimtex-seamless-edge--selected"),"All correction edges highlighted");
            Check(root.Q<Button>("seamlessEdge-right").ClassListContains("whimtex-seamless-edge--selected")&&root.Q<Button>("seamlessEdge-top").ClassListContains("whimtex-seamless-edge--selected"),"Mirror directions highlighted");
            foreach(string edge in new[]{"left","right","top","bottom"})
                Check(root.Q<Button>("seamlessProcessing-"+edge).ClassListContains("whimtex-seamless-edge--selected"),"All Offset copy edges highlighted");
        }
        finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window);if(focus!=null)focus.Focus();}
        return $"Seamless defaults: {checks} checks passed (factory, registry, persistence, ordered UI, binding, labels, independent settings and edge highlights).";
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("SeamlessDefaultsTests.Main", () => { ExecuteMain(); });
}
