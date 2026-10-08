using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;
using UnityEngine.UIElements;
public static class MakeSeamlessIntegrationTests
{
    static int checks;
    static void Check(bool value,string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old = RenderTexture.active;
        var texture = WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true));
        try { RenderTexture.active = rt; texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false); return texture.GetPixels(); }
        finally { RenderTexture.active = old; WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture); }
    }
    static string ExecuteMain() => IntegrationMode(MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson);
    static string ExecuteHistogram() => IntegrationMode(MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend);
    static string IntegrationMode(MakeSeamlessLayerBehaviour.SeamlessMode testedMode)
    {
        checks=0;
        const BindingFlags all = BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,all).Invoke(o,args);
        var assembly=typeof(TextureCompositor).Assembly;
        var cache=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        EditorWindow testWindow=null;
        var previousFocus=EditorWindow.focusedWindow;
        var document=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
        document.width=32;document.height=16;
        var input=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(32,16,TextureFormat.RGBAFloat,false,true));
        var pixels=new Color[512];
        for(int y=0;y<16;y++) for(int x=0;x<32;x++) pixels[y*32+x]=new Color(.2f+x/50f,.1f+y/25f,.4f,1);
        input.SetPixels(pixels);input.Apply(false,false);
        var source=new FileLayerBehaviour { sourceTexture=input,colorRange=LayerColorRange.HDR };
        var effect=MakeSeamlessLayerBehaviour.CreateDefault();effect.mode=testedMode;effect.colorRange=LayerColorRange.HDR;effect.offsetTransitionStart=-.25f;
        document.layers.Add(effect);document.layers.Add(source);
        Color[] RenderCall(string method,params object[] args)
        {
            var rt=(RenderTexture)Call(document,method,args);
            try { return Read(rt); } finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt); }
        }
        void Same(Color[] a,Color[] b,string message)
        {
            Check(a.Length==b.Length,message+" size");
            for(int i=0;i<a.Length;i++) for(int c=0;c<4;c++) Check(Math.Abs(a[i][c]-b[i][c])<.005,message);
        }
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
        Color[] Expected(Color[] values)
        {
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(32,16,TextureFormat.RGBAFloat,false,true));RenderTexture rt=null;
            try
            {
                t.SetPixels(values);t.Apply(false,false);
                var selected=new Vector4(effect.leftEdge?1:0,effect.rightEdge?1:0,effect.bottomEdge?1:0,effect.topEdge?1:0);
                rt=(RenderTexture)assembly.GetType(testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend ? "DCFApixels.WhimTex.HistogramSeamless" : "DCFApixels.WhimTex.ScreenedSeamless").GetMethod(testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend ? "RenderOffset" : "RenderConfigured",all)
                    .Invoke(null,testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend?
                        new object[]{t,32,16,selected,effect.edgeWidth,effect.offsetContrastCompensation?effect.histogramContrast:0,effect.offsetSeamCorrection,effect.offsetAutoRadius?0:effect.offsetCorrectionRadius,effect.offsetPoissonEdges,effect.offsetTransitionStart}:
                        new object[]{t,32,16,effect.poissonEdges,effect.screeningRadius});return Read(rt);
            }
            finally {if(rt!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        try
        {
            Call(document,"NormalizeModel");
            var expected=Expected(pixels);
            Check(Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.SeamlessMode)).Length==4,"Four modes");
            Check(MakeSeamlessLayerBehaviour.CreateDefault().mode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend,"Default mode");
            foreach(int stored in new[]{0,1,2,3})
            {
                var saved=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{\"mode\":"+stored+"}");
                Check((int)saved.mode==stored,"No migration on load");
                Check((int)JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(saved)).mode==stored,"No migration on save");
            }
            effect.mode=(MakeSeamlessLayerBehaviour.SeamlessMode)1;
            if(testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson)
                Same(RenderCall("RenderLayerPreview",effect.Owner,32),expected,"Removed value uses Screened");
            Check((int)effect.mode==1,"Render does not migrate");
            effect.mode=testedMode;
            Same(RenderCall("RenderLayerPreview",effect.Owner,32),expected,"Layer preview");
            Same(RenderCall("RenderLayerThumbnail",effect.Owner,32,cache),expected,"Thumbnail render");
            source.enabled=false;
            Same(RenderCall("RenderCanvas",32),expected,"Composite with hidden input");
            Same(RenderCall("RenderCanvasAtSize",32,16),expected,"Full resolution export render");
            Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),expected,"Cached render");
            int before=(int)cache.GetType().GetProperty("Hits",all).GetValue(cache);
            Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),expected,"Cache reuse");
            Check((int)cache.GetType().GetProperty("Hits",all).GetValue(cache)>before,"Cache did not hit");
            var modified=(Color[])pixels.Clone();
            for(int i=0;i<modified.Length;i++) modified[i].b+=.1f;
            input.SetPixels(modified);input.Apply(false,false);
            Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),Expected(modified),"Source edit invalidates cache");
            input.SetPixels(pixels);input.Apply(false,false);
            effect.mode=MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
            var mirrored=RenderCall("RenderCanvasWithCache",32,cache,false,null);
            Check(Math.Abs(mirrored[0].r-expected[0].r)>.02,"Mode did not invalidate cache");
            effect.mode=testedMode;
            effect.horizontal=MakeSeamlessLayerBehaviour.HorizontalDirection.Off;
            effect.vertical=MakeSeamlessLayerBehaviour.VerticalDirection.Off;
            Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),expected,"Screened Poisson ignores mirror settings");
            effect.leftEdge=false;effect.topEdge=false;effect.bottomEdge=false;
            effect.screeningRadius=.12f;effect.edgeWidth=.4f;effect.histogramContrast=.3f;
            Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),Expected(pixels),"Configured settings invalidate cache");
            if(testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend)
            {
                for(int options=0;options<8;options++)
                {
                    effect.offsetContrastCompensation=(options&1)!=0;effect.offsetSeamCorrection=(options&2)!=0;
                    effect.offsetAutoRadius=(options&4)!=0;effect.offsetCorrectionRadius=.12f;
                    var configured=Expected(pixels);
                    Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),configured,"Offset options invalidate cache");
                    Same(RenderCall("RenderCanvasAtSize",32,16),configured,"Offset options export");
                }
                effect.offsetContrastCompensation=effect.offsetSeamCorrection=effect.offsetAutoRadius=true;effect.offsetCorrectionRadius=.05f;
            }
            effect.rightEdge=false;
            Same(RenderCall("RenderLayerPreview",effect.Owner,32),Expected(pixels),"Poisson ignores blend edge mask");
            if(testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend)
            {
                effect.offsetSeamCorrection=false;
                Same(RenderCall("RenderLayerPreview",effect.Owner,32),pixels,"Copy and correction off bypasses");
                effect.offsetSeamCorrection=true;
            }
            foreach(MakeSeamlessLayerBehaviour.PoissonEdges direction in Enum.GetValues(typeof(MakeSeamlessLayerBehaviour.PoissonEdges)))
            {
                effect.poissonEdges=direction;effect.offsetPoissonEdges=direction;
                Same(RenderCall("RenderCanvasWithCache",32,cache,false,null),Expected(pixels),"Independent direction invalidates cache");
                Same(RenderCall("RenderCanvasAtSize",32,16),Expected(pixels),"Direction export");
            }
            effect.poissonEdges=effect.offsetPoissonEdges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;
            effect.leftEdge=effect.rightEdge=effect.topEdge=effect.bottomEdge=true;
            effect.screeningRadius=.05f;effect.edgeWidth=.2f;effect.histogramContrast=1;
            var group=new GroupLayerBehaviour { compositing=GroupCompositing.Isolated };
            source.enabled=true;document.layers.Remove(source.Owner);group.layers.Add(source);group.enabled=false;
            document.layers.Add(group);effect.TargetLayerId=group.Id;
            Call(document,"NormalizeModel");
            Same(RenderCall("RenderCanvas",32),expected,"Group target");
            group.enabled=true;
            effect.clippingMask=true;
            Same(RenderCall("RenderLayerPreview",effect.Owner,32),expected,"Clipped effect on opaque group");
            effect.clippingMask=false;
            var copy=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<TextureCompositor>());
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(document),copy);
                Check(((MakeSeamlessLayerBehaviour)copy.layers[0].Behaviour).mode==effect.mode,"Document roundtrip");
            }
            finally { WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(copy); }
            testWindow=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
            testWindow.titleContent=new GUIContent("Seamless verification");
            testWindow.Show();
            var root=testWindow.rootVisualElement;
            var bindings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            Action<string,Action> apply=(name,change)=>{ Undo.RegisterCompleteObjectUndo(document,name);change();EditorUtility.SetDirty(document);Call(bindings,"Refresh",true); };
            effect.mode=(MakeSeamlessLayerBehaviour.SeamlessMode)1;
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",all).Invoke(null,new object[]{root,effect,document,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var dropdown=root.Q<PopupField<MakeSeamlessLayerBehaviour.SeamlessMode>>("seamlessMethod");var edges=root.Q("seamlessEdges").parent;
            Check(dropdown.label=="Method","Method label");
            bool Hidden(VisualElement element) { for (; element != null; element = element.parent) if (element.ClassListContains("whimtex-hidden")) return true; return false; }
            Check(Hidden(edges),"Mirror controls visible in Screened Poisson");
            Check((MakeSeamlessLayerBehaviour.SeamlessMode)dropdown.value==MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson,"UI resolves removed value");
            Call(bindings,"Refresh",true);
            Check((int)effect.mode==1,"UI does not migrate");
            effect.mode=testedMode;Call(bindings,"Refresh",true);
            var processing=root.Q("seamlessProcessingEdges");
            Check(processing!=null&&!Hidden(processing.parent.parent),"Processing controls visible");
            Check(Hidden(processing)== (testedMode==MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson),"Copy edge selector only for Offset");
            var left=processing.Q<Button>("seamlessProcessing-left");
            WhimTex.Tests.UnityC.PublicInput.Click(left);
            Check(!effect.leftEdge,"Edge button toggles model");
            Check(!left.ClassListContains("whimtex-seamless-edge--selected"),"Edge binding refresh");
            var sliders=root.Query<Slider>().ToList();
            foreach(var slider in sliders)
            {
                if(slider.name=="screenedSolveRadius")slider.value=12;
                if(slider.name=="seamlessEdgeWidth")slider.value=40;
                if(slider.name=="offsetCompensation")slider.value=30;
            }
            Check(Math.Abs(effect.screeningRadius-.12f)<1e-6&&Math.Abs(effect.edgeWidth-.4f)<1e-6&&Math.Abs(effect.histogramContrast-.3f)<1e-6,"Slider units");
            Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
            void ClickPair(string id) {var button=root.Q<Button>(id);WhimTex.Tests.UnityC.PublicInput.Click(button);}
            ClickPair("poissonEdges-left");
            ClickPair("mirrorPoissonEdges-top");
            ClickPair("offsetPoissonEdges-right");
            root.Q<Toggle>("offsetContrastCompensation").value=false;
            root.Q<Toggle>("offsetAutoRadius").value=false;
            root.Q<Slider>("offsetCorrectionRadius").value=11;
            root.Q<Toggle>("offsetSeamCorrection").value=false;
            Check(!effect.offsetContrastCompensation&&!effect.offsetAutoRadius&&!effect.offsetSeamCorrection&&Math.Abs(effect.offsetCorrectionRadius-.11f)<1e-6,"Offset UI callbacks");
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            var restored=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(restored.offsetContrastCompensation&&restored.offsetAutoRadius&&restored.offsetSeamCorrection&&restored.offsetCorrectionRadius==.05f,"Undo offset options");
            Check(restored.poissonEdges==0&&restored.mirrorPoissonEdges==0&&restored.offsetPoissonEdges==0,"Undo directions");
            Undo.PerformRedo();
            restored=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(!restored.offsetContrastCompensation&&!restored.offsetAutoRadius&&!restored.offsetSeamCorrection&&Math.Abs(restored.offsetCorrectionRadius-.11f)<1e-6,"Redo offset options");
            Check(restored.poissonEdges==MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom&&restored.mirrorPoissonEdges==MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight&&restored.offsetPoissonEdges==MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom,"Redo independent directions");
            Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
            dropdown.value=MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;
            Check(effect.mode==MakeSeamlessLayerBehaviour.SeamlessMode.Mirror,"UI mode event");
            Check(!edges.ClassListContains("whimtex-hidden"),"Mirror controls hidden in Mirror");
            Check(EditorUtility.IsDirty(document),"UI edit did not dirty document");
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            effect=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(effect.mode==testedMode,"Undo mode");
            Undo.PerformRedo();effect=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(effect.mode==MakeSeamlessLayerBehaviour.SeamlessMode.Mirror,"Redo mode");
            // Remaining checks concern settings, not pixels. Preserve the referenced layer ID.
            source.Owner.SetBehaviour(new ColorFillLayerBehaviour());
            foreach(var mode in new[]{MakeSeamlessLayerBehaviour.SeamlessMode.Mirror,MakeSeamlessLayerBehaviour.SeamlessMode.ScreenedPoisson,MakeSeamlessLayerBehaviour.SeamlessMode.OffsetBlend})
            {
                effect.mode=mode;
                var clipboard=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document,new WhimTexJsonWriteOptions {Mode=WhimTexJsonWriteMode.Full}).Json);
                try
                {
                    var decoded=clipboard.Document;
                    Check(((MakeSeamlessLayerBehaviour)decoded.layers[0].Behaviour).mode==mode,"Portable mode roundtrip");
                    var saved=(MakeSeamlessLayerBehaviour)decoded.layers[0].Behaviour;
                    Check(saved.leftEdge==effect.leftEdge&&saved.rightEdge==effect.rightEdge&&saved.bottomEdge==effect.bottomEdge&&saved.topEdge==effect.topEdge,"Portable edges");
                    Check(saved.screeningRadius==effect.screeningRadius&&saved.edgeWidth==effect.edgeWidth&&saved.histogramContrast==effect.histogramContrast,"Portable parameters");
                    Check(saved.offsetContrastCompensation==effect.offsetContrastCompensation&&saved.offsetSeamCorrection==effect.offsetSeamCorrection&&saved.offsetAutoRadius==effect.offsetAutoRadius&&saved.offsetCorrectionRadius==effect.offsetCorrectionRadius,"Portable offset options");
                    Check(saved.poissonEdges==effect.poissonEdges&&saved.mirrorPoissonEdges==effect.mirrorPoissonEdges&&saved.offsetPoissonEdges==effect.offsetPoissonEdges,"Portable independent directions");
                }
                finally { ((IDisposable)clipboard).Dispose(); }
            }
            return $"{testedMode} integration: {checks} checks passed (preview, thumbnail, composite, export render, cache, group, clipping, UI, Undo/Redo, document and portable serialization).";
        }
        finally
        {
            if(testWindow!=null)global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(testWindow);
            if(previousFocus!=null)previousFocus.Focus();
            ((IDisposable)cache).Dispose();Undo.RevertAllDownToGroup(undo);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(document);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(input);
        }
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("MakeSeamlessIntegrationTests.Main", () => { ExecuteMain(); });

    public static string Histogram() => WhimTex.Tests.UnityC.FixtureContext.Run("MakeSeamlessIntegrationTests.Histogram", () => { ExecuteHistogram(); });
}
