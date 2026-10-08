using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
public static class OffsetOptionsTests
{
    const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try {RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);return t.GetPixels();}
        finally {RenderTexture.active=previous;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static void Same(Color[] a,Color[] b,string label)
    {for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<2e-5,label);}
    static string ExecuteMain()
    {
        checks=0;
        var assembly=typeof(WhimTexDocument).Assembly;
        var old=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{\"mode\":3,\"histogramContrast\":0.37}");
        Check(old.mode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend,"Stable serialized mode");
        Check(old.offsetContrastCompensation&&old.offsetSeamCorrection&&old.offsetAutoRadius&&old.histogramContrast==.37f,"Old parameters/default behavior");
        Check(old.offsetTransitionStart==.325f,"Missing transition start preserves original fade");
        var set=typeof(WhimTexApi).GetMethod("SetMakeSeamless",Flags);
        var jsonType=set.GetParameters()[1].ParameterType;
        object Parse(string json)=>jsonType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{json});
        var snapshot=typeof(WhimTexApi).GetMethod("MakeSeamlessSnapshot",Flags).Invoke(null,new object[]{old});
        Check(snapshot.ToString().Contains("OffsetBlend"),"Canonical export");
        Check(snapshot.ToString().Contains("offsetTransitionStart"),"Transition start exported");
        set.Invoke(null,new object[]{old,Parse("{\"offsetTransitionStart\":-0.25}")});
        Check(old.offsetTransitionStart==-.25f,"Transition start API");
        Check(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(old)).offsetTransitionStart==-.25f,"Transition start serialized");
        bool rejected=false;
        try {set.Invoke(null,new object[]{old,Parse("{\"mode\":\"HistogramBlend\"}")});}
        catch(TargetInvocationException){rejected=true;}
        Check(rejected,"No legacy alias or migration");
        set.Invoke(null,new object[]{old,Parse("{\"mode\":\"OffsetBlend\",\"offsetContrastCompensation\":false,\"offsetSeamCorrection\":false,\"offsetAutoRadius\":false,\"offsetCorrectionRadius\":0.11}")});
        Check(!old.offsetContrastCompensation&&!old.offsetSeamCorrection&&!old.offsetAutoRadius&&old.offsetCorrectionRadius==.11f,"API options");
        Check(Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.SeamlessMode)).Length==4,"Four UI methods");
        foreach(var size in new[]{new Vector2Int(1,3),new Vector2Int(17,13),new Vector2Int(64,48)})
        {
            int w=size.x,h=size.y;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));
            var input=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)input[y*w+x]=new Color(.2f+.01f*x,.8f-.012f*y,.4f+.1f*Mathf.Sin(x*.3f+y*.4f),.2f+.6f*x/Math.Max(1,w-1));
            t.SetPixels(input);t.Apply();
            Color[] Render(Vector4 edges,float strength,bool correct,float radius,float start=.325f)
            {
                var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
                var rt=(RenderTexture)assembly.GetType("DCFApixels.WhimTex.HistogramSeamless").GetMethod("RenderOffset",Flags)
                    .Invoke(null,new object[]{t,w,h,edges,.4f,strength,correct,radius,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,start});
                Check(RenderTexture.active==previous&&GL.sRGBWrite==srgb,"Caller state");
                try {return Read(rt);}finally {WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
            }
            try
            {
                foreach(var edges in new[]{new Vector4(0,1,0,0),new Vector4(1,1,0,0),Vector4.one})
                foreach(float start in new[]{-1f,-.25f,0f,.325f,.75f,.95f})
                {
                    var raw=Render(edges,0,false,0,start);var reference=new Color[w*h];
                    float Fade(float distance,float n) {float q=Mathf.Clamp01((distance-start*.4f*n)/((1-start)*.4f*n));return 1-q*q*q*(10+q*(-15+6*q));}
                    Color P(Color c)=>new Color(c.r*c.a,c.g*c.a,c.b*c.a,c.a);
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    {
                        int xx=(x+w-w/2)%w,yy=(y+h-h/2)%h;
                        float fx=w==1?0:Math.Max(Fade(x,w)*edges.x,Fade(w-1-x,w)*edges.y);
                        float fy=h==1?0:Math.Max(Fade(y,h)*edges.z,Fade(h-1-y,h)*edges.w);
                        var c=Color.Lerp(Color.Lerp(P(input[y*w+x]),P(input[y*w+xx]),fx),Color.Lerp(P(input[yy*w+x]),P(input[yy*w+xx]),fx),fy);
                        if(c.a>1e-8) {c.r/=c.a;c.g/=c.a;c.b/=c.a;}reference[y*w+x]=c;
                    }
                    Same(raw,reference,"Plain offset = independent translated-copy fade");
                    Same(Render(edges,1,true,0,start),Render(edges,1,true,.1f,start),"Auto radius preserves width/4");
                }
                if(w>4)
                {
                    var edges=new Vector4(0,1,0,0);var a=Render(edges,1,false,0);var b=Render(edges,1,true,0);
                    double change=0;for(int i=0;i<a.Length;i++)change+=Math.Abs(a[i].r-b[i].r);
                    Check(change>.001,"Correction switch changes one-sided output");
                    a=Render(edges,1,true,.005f);b=Render(edges,1,true,.25f);change=0;
                    for(int i=0;i<a.Length;i++)change+=Math.Abs(a[i].r-b[i].r);
                    Check(change>.001,"Custom radius has an effect");
                }
            }
            finally {WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        var focused=EditorWindow.focusedWindow;
        var window=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
        try {
        window.Show();
        var model=MakeSeamlessLayerBehaviour.CreateDefault();var root=window.rootVisualElement;
        var bindings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
        void Refresh()=>bindings.GetType().GetMethod("Refresh",Flags).Invoke(bindings,new object[]{true});
        int changes=0;
        Action<string,Action> applyChange=(name,action)=>{changes++;action();Refresh();};
        typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",Flags).Invoke(null,new object[]{root,model,null,applyChange,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
        var correction=root.Q<Toggle>("offsetSeamCorrection");
        var transition=root.Q<Slider>("offsetTransitionStart");
        Check(transition!=null&&Math.Abs(transition.value+25f)<1e-4,"Transition start UI default");
        transition.value=-50;
        Check(Math.Abs(model.offsetTransitionStart+.5f)<1e-6,"Transition start UI writes model");
        model.offsetTransitionStart=.325f;Refresh();
        Check(Math.Abs(transition.value-32.5f)<1e-4,"Transition start binding refresh");
        Check(correction.enabledSelf&&correction.value,"Correction independent of copy-edge pairing");
        model.leftEdge=model.bottomEdge=model.topEdge=false;Refresh();
        Check(correction.enabledSelf,"Single-sided correction available");
        root.Q<Toggle>("offsetAutoRadius").value=false;
        var radiusField=root.Q<Slider>("offsetCorrectionRadius");Check(radiusField.enabledSelf,"Manual radius enabled");radiusField.value=11;
        Check(Math.Abs(model.offsetCorrectionRadius-.11f)<1e-6,"Manual radius persisted");
        root.Q<Toggle>("offsetContrastCompensation").value=false;
        Check(!model.offsetContrastCompensation&&root.Q<Slider>("offsetCompensation").ClassListContains("whimtex-hidden"),"Compensation toggle");
        correction.value=false;Check(!model.offsetSeamCorrection&&radiusField.ClassListContains("whimtex-hidden"),"Correction toggle");
        Check(root.Q<PopupField<MakeSeamlessLayerBehaviour.SeamlessMode>>("seamlessMethod").label=="Method"&&root.Q<Slider>("seamlessEdgeWidth").label=="Blend Width (%)","Consistent labels");
        foreach(string id in new[]{"poissonEdges","mirrorPoissonEdges","offsetPoissonEdges"})
        {
            Check(root.Q<EnumField>(id)==null,"Poisson dropdown replaced");
            var field=typeof(MakeSeamlessLayerBehaviour).GetField(id);
            var saved=field.GetValue(model);
            for(int state=0;state<3;state++)foreach(string edge in new[]{"top","bottom","left","right"})
            {
                field.SetValue(model,(MakeSeamlessLayerBehaviour.PoissonEdges)state);Refresh();
                int pair=edge=="top"||edge=="bottom"?1:2;
                var button=root.Q<Button>(id+"-"+edge);
                Check(button.enabledSelf,"Every pair can be cleared");
                int before=changes;
                WhimTex.Tests.UnityC.PublicInput.Click(button);
                int expected=state==pair?3:state==0?3-pair:0;
                Check((int)(MakeSeamlessLayerBehaviour.PoissonEdges)field.GetValue(model)==expected,"Paired click state");
                Check(changes==before+1,"One action per pair toggle");
                foreach(string side in new[]{"top","bottom","left","right"})
                {
                    int sidePair=side=="top"||side=="bottom"?1:2;
                    Check(root.Q<Button>(id+"-"+side).ClassListContains("whimtex-seamless-edge--selected")==(expected==0||expected==sidePair),"Both edges highlight together");
                }
            }
            field.SetValue(model,saved);Refresh();
        }
        var method=root.Q<PopupField<MakeSeamlessLayerBehaviour.SeamlessMode>>("seamlessMethod");
        model.mirrorContrast=.37f;model.mirrorCorrectionRadius=.08f;model.screeningRadius=.21f;
        method.value=MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
        method.value=MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson;
        method.value=MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend;
        Check(model.mirrorContrast==.37f&&model.mirrorCorrectionRadius==.08f&&model.screeningRadius==.21f&&Math.Abs(model.offsetCorrectionRadius-.11f)<1e-6&&!model.offsetSeamCorrection,"Switching methods never copies incompatible options");
        }
        finally {global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window);if(focused!=null)focused.Focus();}
        return $"Offset options: {checks} checks passed (no legacy alias, serialization, plain-blend reference, automatic/manual radius, optional correction, UI applicability, independent options).";
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("OffsetOptionsTests.Main", () => { ExecuteMain(); });
}
