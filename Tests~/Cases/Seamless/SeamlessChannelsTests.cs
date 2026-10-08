using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;

public static class SeamlessChannelsTests
{
    const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static int checks;
    static void Check(bool ok,string label) { WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, label); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try {RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);return t.GetPixels();}
        finally {RenderTexture.active=previous;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static void Same(Color[] a,Color[] b,string label)
    {Check(a.Length==b.Length,label);for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<.002f,label+" pixel="+i+" channel="+c+" actual="+a[i][c]+" expected="+b[i][c]);}
    static string ExecuteMain()
    {
        checks=0;
        object Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,Flags).Invoke(obj,args);
        var document=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());document.width=32;document.height=16;
        var input=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(32,16,TextureFormat.RGBAFloat,false,true));
        var pixels=new Color[512];
        for(int y=0;y<16;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(.2f+x*.02f,.8f-y*.03f,.45f+.13f*Mathf.Sin(x*.8f+y),.25f+x*.02f);
        input.SetPixels(pixels);input.Apply();
        var source=new FileLayerBehaviour {sourceTexture=input,colorRange=LayerColorRange.HDR};
        var effect=MakeSeamlessLayerBehaviour.CreateDefault();effect.colorRange=LayerColorRange.HDR;
        document.layers.Add(effect);document.layers.Add(source);
        var cache=Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        var focus=EditorWindow.focusedWindow;EditorWindow window=null;
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
        Color[] Render(string method,params object[] args)
        {
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var rt=(RenderTexture)Call(document,method,args);
            Check(previous==RenderTexture.active&&srgb==GL.sRGBWrite,"Render state");
            try{return Read(rt);}finally {WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
        }
        try
        {
            var old=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{}");
            Check(old.processRed&&old.processGreen&&old.processBlue&&old.processAlpha,"Legacy defaults");
            Check(MakeSeamlessLayerBehaviour.CreateDefault().mode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend,"New layer defaults to Offset Blend");
            Call(document,"NormalizeModel");
            var original=Render("RenderLayerPreview",source.Owner,32);
            source.enabled=false;
            for(int variant=0;variant<6;variant++)for(int direction=0;direction<3;direction++)
            {
                effect.poissonEdges=effect.mirrorPoissonEdges=effect.offsetPoissonEdges=(MakeSeamlessLayerBehaviour.PoissonEdges)direction;
                effect.mode=variant<4?MakeSeamlessLayerBehaviour.SeamlessMode.Mirror:variant==4?MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson:MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend;
                effect.mirrorContrastCompensation=(variant&1)!=0;effect.mirrorSeamCorrection=(variant&2)!=0;
                effect.processRed=effect.processGreen=effect.processBlue=effect.processAlpha=true;
                var full=Render("RenderLayerPreview",effect.Owner,32);
                for(int mask=0;mask<16;mask++)
                {
                    effect.processRed=(mask&1)!=0;effect.processGreen=(mask&2)!=0;effect.processBlue=(mask&4)!=0;
                    effect.processAlpha=(mask&8)!=0;
                    var expected=(Color[])full.Clone();
                    for(int i=0;i<expected.Length;i++)for(int c=0;c<4;c++)if((mask&(1<<c))==0)expected[i][c]=original[i][c];
                    Same(Render("RenderLayerPreview",effect.Owner,32),expected,"Layer "+variant+"/"+mask);
                    Same(Render("RenderCanvasWithCache",32,cache,false,null),expected,"Cache mask change");
                    Same(Render("RenderLayerThumbnail",effect.Owner,32,cache),expected,"Thumbnail");
                    Same(Render("RenderCanvasAtSize",32,16),expected,"Export/composite");
                }
            }
            effect.processRed=false;effect.processGreen=true;effect.processBlue=false;effect.processAlpha=false;
            source.enabled=true;
            var group=new GroupLayerBehaviour {compositing=GroupCompositing.Isolated};
            document.layers.Remove(source.Owner);group.layers.Add(source);document.layers.Add(group);effect.TargetLayerId=group.Id;
            Call(document,"NormalizeModel");
            var grouped=Render("RenderLayerPreview",effect.Owner,32);
            for(int i=0;i<grouped.Length;i++)
            {Check(Math.Abs(grouped[i].r-original[i].r)<.002f,"Group source R");Check(Math.Abs(grouped[i].b-original[i].b)<.002f,"Group source B");}
            // GPU checks are complete; keep identity but remove the unsaved texture reference.
            source.Owner.SetBehaviour(new ColorFillLayerBehaviour());
            var json=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",Flags).Invoke(null,new object[]{document,document.layers});
            var clipboard=typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",Flags).Invoke(null,new object[]{json,32,16});
            try
            {
                var copy=(WhimTexDocument)clipboard.GetType().GetField("Document",Flags).GetValue(clipboard);
                var saved=(MakeSeamlessLayerBehaviour)copy.layers[0].Behaviour;
                Check(!saved.processRed&&saved.processGreen&&!saved.processBlue&&!saved.processAlpha,"Portable roundtrip");
            }
            finally {((IDisposable)clipboard).Dispose();}
            window=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());window.titleContent=new GUIContent("Channel verification");window.Show();
            var bindings=Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            Action<string,Action> apply=(name,change)=>{Undo.RegisterCompleteObjectUndo(document,name);change();EditorUtility.SetDirty(document);Call(bindings,"Refresh",true);};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",Flags).Invoke(null,new object[]{window.rootVisualElement,effect,document,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var root=window.rootVisualElement;
            root.Q<Toggle>("seamlessChannelR").value=true;root.Q<Toggle>("seamlessChannelG").value=false;root.Q<Toggle>("seamlessChannelB").value=true;
            root.Q<Toggle>("seamlessChannelA").value=true;
            Check(effect.processRed&&!effect.processGreen&&effect.processBlue&&effect.processAlpha,"RGBA UI callbacks");
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();var restored=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(!restored.processRed&&restored.processGreen&&!restored.processBlue&&!restored.processAlpha,"Undo");
            Undo.PerformRedo();restored=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(restored.processRed&&!restored.processGreen&&restored.processBlue&&restored.processAlpha,"Redo");
            return $"RGBA mask: {checks} checks passed (16 masks x 6 mode/option combinations x 3 Poisson directions, independent RGBA selection, all-off bypass, previews/export/cache, group, Portable, UI, Undo/Redo, defaults).";
        }
        catch (TargetInvocationException e) { throw new Exception(e.GetBaseException().ToString()); }
        finally
        {
            if(window!=null)global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window);if(focus!=null)focus.Focus();
            ((IDisposable)cache).Dispose();Undo.RevertAllDownToGroup(undo);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(input);
        }
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("SeamlessChannelsTests.Main", () => { ExecuteMain(); });
}
