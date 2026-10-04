// Pipeline run_script, EdgeSelectorSmoke.Run. Uses a temporary window and document only.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

public static class EdgeSelectorSmoke
{
    public static string Run()
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        int checks=0,changes=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        var noise=new NoiseLayerBehaviour();var seamless=new MakeSeamlessLayerBehaviour();
        doc.layers.Add(noise);doc.layers.Add(seamless);
        var window=ScriptableObject.CreateInstance<EditorWindow>();window.titleContent=new GUIContent("Edge controls check");
        var noiseRoot=new VisualElement();var seamRoot=new VisualElement();
        window.rootVisualElement.Add(noiseRoot);window.rootVisualElement.Add(seamRoot);
        var style=AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/WhimTexSplitView.uss");
        window.rootVisualElement.styleSheets.Add(style);window.ShowUtility();
        var ui=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI");
        var bindingsType=ui.GetNestedType("ValueBindings",BindingFlags.NonPublic);
        var bindings=Activator.CreateInstance(bindingsType,true);
        Action<string,Action> change=(label,action)=>{changes++;action();};
        void Refresh()=>bindingsType.GetMethod("Refresh").Invoke(bindings,new object[]{true});
        try
        {
            typeof(NoiseLayerEditorWindow).GetMethod("BuildFields",flags).Invoke(null,new object[]{noiseRoot,noise,doc,change,bindings});
            Action<VisualElement,TargetedLayerBehaviour> target=(root,layer)=>{};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",flags).Invoke(null,new object[]{seamRoot,seamless,doc,change,bindings,target});
            void Click(string name)
            {
                var button=window.rootVisualElement.Q<Button>(name);Check(button!=null,"Button exists: "+name);
                int previous=changes;
                using var evt=NavigationSubmitEvent.GetPooled();evt.target=button;button.SendEvent(evt);Refresh();
                Check(changes==previous+1,"One change transaction: "+name);
            }
            void Hover(string prefix,string edge,string opposite,bool linked)
            {
                var button=window.rootVisualElement.Q<Button>(prefix+edge);
                using(var evt=PointerEnterEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
                foreach(string side in new[]{"top","bottom","left","right"})
                {
                    var other=window.rootVisualElement.Q<Button>(prefix+side);
                    Check(other.ClassListContains("whimtex-seamless-edge--hovered")==(side==edge||linked&&side==opposite),"Hover pairing: "+prefix+side);
                }
                using(var evt=PointerLeaveEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
                foreach(string side in new[]{"top","bottom","left","right"})
                    Check(!window.rootVisualElement.Q<Button>(prefix+side).ClassListContains("whimtex-seamless-edge--hovered"),"Hover cleared");
            }
            for(int mask=0;mask<4;mask++)
            {
                noise.periodic=(NoiseLayerBehaviour.PeriodicAxes)mask;Refresh();
                Hover("periodic-","left","right",true);Hover("periodic-","top","bottom",true);
                Click("periodicEdges-center");Check((int)noise.periodic==(mask^3),"Noise inversion");
            }
            foreach(string fieldName in new[]{"poissonEdges","offsetPoissonEdges","mirrorPoissonEdges","quiltingEdges","quiltingPoissonEdges"})
            {
                seamless.mode=fieldName.StartsWith("mirror")?MakeSeamlessLayerBehaviour.SeamlessMode.Mirror
                    :fieldName.StartsWith("quilting")?MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting
                    :fieldName.StartsWith("offset")?MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend:MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson;
                seamless.quiltingSeamCorrection=true;
                var field=typeof(MakeSeamlessLayerBehaviour).GetField(fieldName);
                for(int value=0;value<4;value++)
                {
                    field.SetValue(seamless,(MakeSeamlessLayerBehaviour.PoissonEdges)value);Refresh();
                    Hover(fieldName+"-","top","bottom",true);Hover(fieldName+"-","right","left",true);
                    Click(fieldName+"Selector-center");
                    Check((int)(MakeSeamlessLayerBehaviour.PoissonEdges)field.GetValue(seamless)==3-value,"Pair inversion: "+fieldName);
                }
            }
            seamless.mode=MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend;
            for(int mask=0;mask<16;mask++)
            {
                seamless.leftEdge=(mask&1)!=0;seamless.rightEdge=(mask&2)!=0;
                seamless.topEdge=(mask&4)!=0;seamless.bottomEdge=(mask&8)!=0;Refresh();
                Hover("seamlessProcessing-","left","right",false);Click("seamlessProcessingEdges-center");
                int result=(seamless.leftEdge?1:0)|(seamless.rightEdge?2:0)|(seamless.topEdge?4:0)|(seamless.bottomEdge?8:0);
                Check(result==(mask^15),"Independent edge inversion");
            }
            seamless.mode=MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
            for(int x=0;x<3;x++)for(int y=0;y<3;y++)
            {
                seamless.horizontal=(MakeSeamlessLayerBehaviour.HorizontalDirection)x;
                seamless.vertical=(MakeSeamlessLayerBehaviour.VerticalDirection)y;Refresh();
                Hover("seamlessEdge-","left","right",false);Click("seamlessEdges-center");
                bool enabled=x==0&&y==0;
                Check((seamless.horizontal!=MakeSeamlessLayerBehaviour.HorizontalDirection.Off)==enabled&&
                    (seamless.vertical!=MakeSeamlessLayerBehaviour.VerticalDirection.Off)==enabled,"Mirror global toggle");
            }
            return "PASS shared edge controls: "+checks;
        }
        finally{window.Close();bindingsType.GetMethod("Clear").Invoke(bindings,null);UnityEngine.Object.DestroyImmediate(doc);}
    }
}
