// Unity Pipeline run_script, NoiseControlsSmoke.Run. Temporary utility window; no user documents modified.
using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

public static class NoiseControlsSmoke
{
    public static string Run()
    {
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var document=ScriptableObject.CreateInstance<TextureCompositor>(); document.width=document.height=32;
        var noise=new NoiseLayerBehaviour();document.layers.Add(noise);
        var root=new VisualElement();
        var window=ScriptableObject.CreateInstance<EditorWindow>();
        window.titleContent=new GUIContent("Noise controls check");
        window.rootVisualElement.Add(root);window.ShowUtility();
        var ui=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI");
        var bindingsType=ui.GetNestedType("ValueBindings",BindingFlags.NonPublic);
        var bindings=Activator.CreateInstance(bindingsType,true);
        Action<string,Action> change=(name,action)=>action();
        typeof(NoiseLayerEditorWindow).GetMethod("BuildFields",flags).Invoke(null,new object[]{root,noise,document,change,bindings});
        void Refresh()=>bindingsType.GetMethod("Refresh").Invoke(bindings,new object[]{true});
        T Find<T>(string label) where T:VisualElement => root.Query<T>().Where(v=>v is BaseField<Vector2> v2?v2.label==label:
            v is BaseField<Vector3> v3?v3.label==label:v is BaseField<string> text?text.label==label:false).First();
        Texture2D Compose()=>document.Compose();
        try
        {
            Refresh();
            var scale=Find<Vector2Field>("Scale");var xy=Find<Vector2Field>("Offset");var xyz=Find<Vector3Field>("Offset");
            var dimensions=Find<PopupField<string>>("Dimensions");
            Check(scale!=null&&xy!=null&&xyz!=null&&dimensions!=null,"All controls created");
            Check(root.Q<Button>("linkScale")!=null,"Scale chain exists");
            Check(!xy.ClassListContains("whimtex-hidden")&&xyz.ClassListContains("whimtex-hidden"),"2D shows XY only");
            var periodicControl=root.Q("periodic");
            void ClickEdge(string edge)
            {
                var button=root.Q<Button>("periodic-"+edge);
                using var evt=NavigationSubmitEvent.GetPooled();evt.target=button;button.SendEvent(evt);Refresh();
            }
            void CheckEdges(int expected)
            {
                Check((int)noise.periodic==expected,"Periodic model: "+expected);
                foreach(string edge in new[]{"left","right","top","bottom"})
                {
                    var button=root.Q<Button>("periodic-"+edge);
                    int bit=edge=="left"||edge=="right"?1:2;
                    Check(button.ClassListContains("whimtex-seamless-edge--selected")==((expected&bit)!=0),"Paired selection: "+edge);
                    Check(button.enabledInHierarchy,"Edge remains enabled: "+edge);
                }
            }
            CheckEdges(0);ClickEdge("left");CheckEdges(1);ClickEdge("bottom");CheckEdges(3);
            ClickEdge("right");CheckEdges(2);ClickEdge("top");CheckEdges(0);
            noise.periodic=NoiseLayerBehaviour.PeriodicAxes.XY;Refresh();CheckEdges(3);
            dimensions.value="1D";Refresh();Check(periodicControl.ClassListContains("whimtex-hidden"),"Periodic hidden in 1D");
            var oneD=root.Q<Toggle>("periodic1D");
            Check(oneD!=null&&!oneD.ClassListContains("whimtex-hidden")&&!oneD.value,"1D Seamless checkbox visible, default off");
            oneD.value=true;Refresh();Check(noise.periodic1D&&noise.periodic==NoiseLayerBehaviour.PeriodicAxes.XY,"1D checkbox preserves 2D edge selection");
            dimensions.value="2D";Refresh();Check(!periodicControl.ClassListContains("whimtex-hidden"),"Periodic restored in 2D");
            Check(oneD.ClassListContains("whimtex-hidden")&&noise.periodic1D,"1D checkbox hidden but retained in 2D");
            noise.Scale=new Vector2(4,8);Refresh();scale.value=new Vector2(8,8);
            Check(noise.Scale==new Vector2(8,16),"Linked X edit preserves ratio");
            noise.linkScale=false;Refresh();scale.value=new Vector2(8,3);
            Check(noise.Scale==new Vector2(8,3),"Unlinked Y edit");
            noise.linkScale=true;noise.Scale=new Vector2(500,1000);Refresh();scale.value=new Vector2(1000,1000);
            Check(noise.Scale==new Vector2(500,1000),"Linked clamp preserves ratio");
            dimensions.value="3D";Refresh();
            Check(xy.ClassListContains("whimtex-hidden")&&!xyz.ClassListContains("whimtex-hidden"),"3D switches Offset to XYZ");
            xyz.value=new Vector3(1,2,3);Check(noise.offset==new Vector3(1,2,3),"Z edits model");
            dimensions.value="2D";Refresh();xy.value=new Vector2(4,5);Check(noise.offset.z==3,"XY retains Z");
            dimensions.value="3D";noise.periodic=NoiseLayerBehaviour.PeriodicAxes.XY;
            noise.noiseType=NoiseLayerBehaviour.NoiseType.BlueNoise;Refresh();
            Check(dimensions.value=="2D"&&!dimensions.choices.Contains("3D"),"Grain offers 1D/2D only");
            Check(periodicControl.ClassListContains("whimtex-hidden"),"Periodic hidden for grain");
            Check(oneD.ClassListContains("whimtex-hidden"),"1D Seamless hidden for grain");
            Check(noise.dimensions==NoiseLayerBehaviour.NoiseDimensions.ThreeD&&noise.offset.z==3,"Grain does not reset 3D settings");
            noise.noiseType=NoiseLayerBehaviour.NoiseType.Perlin;Refresh();
            Check(dimensions.value=="3D"&&dimensions.choices.Contains("3D"),"Returning restores 3D");
            window.Close();window=null;
            noise.Scale=new Vector2(6.3f,10.7f);noise.offset=new Vector3(.3f,.7f,.2f);
            foreach(int kind in new[]{0,1,2,3,4,5})foreach(int warp in new[]{0,1,2,3})foreach(bool periodic in new[]{false,true})
            {
                noise.noiseType=(NoiseLayerBehaviour.NoiseType)kind;noise.warp=(NoiseLayerBehaviour.WarpType)warp;
                noise.periodic=periodic?NoiseLayerBehaviour.PeriodicAxes.XY:NoiseLayerBehaviour.PeriodicAxes.None;
                noise.offset.z=.2f;var a=Compose();noise.offset.z=.7f;var b=Compose();
                try
                {
                    double diff=0;var ca=a.GetPixels();var cb=b.GetPixels();
                    for(int p=0;p<ca.Length;p++)diff+=Math.Abs(ca[p].r-cb[p].r);
                    Check(diff/ca.Length>.001,"Z changes slice "+kind+"/"+warp+"/"+periodic);
                }
                finally {UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
            }
            var thumbnail=noise.GetPreviewTexture(24);noise.offset.z+=.1f;var next=noise.GetPreviewTexture(24);
            Check(thumbnail==null&&next!=null,"Z invalidates thumbnail");
            thumbnail=next;noise.Scale=new Vector2(8,10);next=noise.GetPreviewTexture(24);
            Check(thumbnail==null&&next!=null,"Scale axes invalidate thumbnail");
            thumbnail=next;noise.periodic=NoiseLayerBehaviour.PeriodicAxes.X;next=noise.GetPreviewTexture(24);
            Check(thumbnail==null&&next!=null,"Periodicity invalidates thumbnail");
            thumbnail=next;noise.periodic1D=!noise.periodic1D;next=noise.GetPreviewTexture(24);
            Check(thumbnail==null&&next!=null,"1D Seamless invalidates thumbnail");
            noise.dimensions=NoiseLayerBehaviour.NoiseDimensions.OneD;noise.periodic1D=true;
            string saved=JsonUtility.ToJson(noise);var copy=JsonUtility.FromJson<NoiseLayerBehaviour>(saved);
            Check(copy.Scale==noise.Scale&&copy.offset==noise.offset&&copy.periodic==noise.periodic&&copy.dimensions==noise.dimensions,"Serialized settings round trip");
            Check(copy.periodic1D==noise.periodic1D,"Serialized 1D Seamless round trip");
            string portable=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",flags).Invoke(null,new object[]{document,document.layers});
            using var pasted=(IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",flags).Invoke(null,new object[]{portable,32,32});
            var pastedDoc=(TextureCompositor)pasted.GetType().GetField("Document",flags).GetValue(pasted);
            var pastedNoise=(NoiseLayerBehaviour)pastedDoc.layers[0].Behaviour;
            Check(pastedNoise.periodic1D&&pastedNoise.dimensions==noise.dimensions,"Optimized clipboard retains active 1D Seamless settings");
            using var full = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions {Mode = WhimTexJsonWriteMode.Full}).Json);
            var fullNoise = (NoiseLayerBehaviour)full.Document.layers[0].Behaviour;
            Check(fullNoise.periodic1D&&fullNoise.periodic==noise.periodic&&fullNoise.dimensions==noise.dimensions,"Full JSON retains both active and inactive Seamless settings");
            return "PASS Noise controls, Z slices and cache: "+checks;
        }
        finally {if(window!=null)window.Close();bindingsType.GetMethod("Clear").Invoke(bindings,null);UnityEngine.Object.DestroyImmediate(document);}
    }
}
