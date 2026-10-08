using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
using Mode=DCFApixels.WhimTex.MakeSeamlessLayerBehaviour.SeamlessMode;

public static class SeamlessReleaseTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
    static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
    static void Check(bool b,string text){ WhimTex.Tests.UnityC.FixtureContext.Context.True(b, text); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=previous;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static Color[] Render(WhimTexDocument doc)
    {var rt=(RenderTexture)Call(doc,"RenderCanvasAtSize",doc.width,doc.height);try{return Read(rt);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}}
    static void Same(Color[] a,Color[] b,string label,float tolerance=.003f)
    {
        Check(a.Length==b.Length,label+" size");float max=0;
        for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)
        {Check(float.IsFinite(b[i][c]),label+" finite");max=Math.Max(max,Math.Abs(a[i][c]-b[i][c]));}
        Check(max<=tolerance,label+" max error "+max);
    }
    static Texture2D Source(int w,int h,int fixture)
    {
        var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));var p=new Color[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            float u=x/(float)w,v=y/(float)h;
            float n=.5f+.22f*Mathf.Sin(u*23+v*9)+.19f*Mathf.Cos(v*31-u*7);
            p[y*w+x]=fixture==0?new Color(n,n,n,1):fixture==1?new Color(x%29<9?1:0,y%37<11?.9f:.1f,n,1):
                fixture==2?new Color(n,.5f+.4f*Mathf.Sin(u*17-v*3),1-n,1):
                fixture==3?new Color(n,1-n,.8f,Mathf.Clamp01((n-.3f)*2)):
                new Color(n*2-.3f,.5f+.6f*Mathf.Sin(u*11+v*27),1-n,v);
        }
        t.SetPixels(p);t.Apply(false,false);return t;
    }
    static MakeSeamlessLayerBehaviour Populate(WhimTexDocument doc,int w,int h,int fixture,Mode mode)
    {
        doc.width=w;doc.height=h;doc.outputSrgb=false;doc.outputPrecision=WhimTexOutputPrecision.Float32;doc.layers.Clear();
        var input=Source(w,h,fixture);
        var drawing=(DrawingLayerBehaviour)typeof(DrawingLayerBehaviour).GetMethod("FromMergedTexture",F).Invoke(null,new object[]{input});
        drawing.colorRange=LayerColorRange.HDR;
        var effect=MakeSeamlessLayerBehaviour.CreateDefault();effect.mode=mode;effect.colorRange=LayerColorRange.HDR;
        effect.quiltingAlongSearch=.175f;effect.quiltingWidth=.27f;effect.quiltingFeather=73;effect.quiltingSeed=-184;
        effect.quiltingContrastCompensation=true;effect.quiltingContrast=.65f;effect.quiltingSeamCorrection=true;
        effect.quiltingChannels=MakeSeamlessLayerBehaviour.QuiltingChannels.Independent;
        effect.mirrorContrastCompensation=true;effect.mirrorContrast=.73f;effect.mirrorTransitionStart=-.31f;
        effect.offsetTransitionStart=-.22f;effect.histogramContrast=.82f;effect.offsetAutoRadius=false;effect.offsetCorrectionRadius=.037f;
        doc.layers.Add(effect);doc.layers.Add(drawing);Call(doc,"NormalizeModel");effect.TargetLayerId=drawing.Id;return effect;
    }
    static void Settings(MakeSeamlessLayerBehaviour a,MakeSeamlessLayerBehaviour b)
    {
        foreach(var f in typeof(MakeSeamlessLayerBehaviour).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
            Check(Equals(f.GetValue(a),f.GetValue(b)),"Persisted setting "+f.Name);
    }
    static void Close(WhimTexWindow window)
    {if(window==null)return;global::WhimTex.Tests.UnityC.FixtureContext.Scope.CloseWindow(window);}
    static string ExecuteWorkflow()
    {
        checks=0;var focus=EditorWindow.focusedWindow;var selection=Selection.objects;
        string folder=WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder();
        // WhimTex.Tests.UnityC.FixtureContext.Scope.AssetFolder() created this unique owned folder.
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
        try
        {
            foreach(Mode mode in new[]{Mode.OffsetBlend,Mode.Mirror,Mode.ScreenedPoisson,Mode.PatchQuilting})
            {
                WhimTexWindow source=null,target=null;WhimTexDocument doc=null,loaded=null,snapshot=null;
                try
                {
                    doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());var effect=Populate(doc,96,64,3,mode);
                    var expected=Render(doc);string settings=JsonUtility.ToJson(effect);
                    string path=WhimTexDocumentFile.Save(doc,folder+"/"+mode+".tiff");
                    WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);doc=null;
                    loaded=WhimTexDocumentFile.Load(path);
                    var restored=(MakeSeamlessLayerBehaviour)loaded.layers[0].Behaviour;
                    Settings(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(settings),restored);
                    Same(expected,Render(loaded),mode+" save/close/reopen");
                    Check(restored.TargetLayerId==loaded.layers[1].Id,"Saved target");
                    source=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
                    WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy((WhimTexDocument)Get(source,"activeDocument"));Set(source,"activeDocument",loaded);loaded=null;
                    source.ShowUtility();doc=(WhimTexDocument)Get(source,"activeDocument");
                    int editGroup;Undo.IncrementCurrentGroup();editGroup=Undo.GetCurrentGroup();
                    Call(source,"ExecuteModelChange","Release smoke seed",new Action(()=>((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed=71));
                    Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed==-184,"Undo");
                    Undo.PerformRedo();Check(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed==71,"Redo");
                    Undo.RevertAllDownToGroup(editGroup);
                    target=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());target.ShowUtility();
                    var dest=(WhimTexDocument)Get(target,"activeDocument");dest.layers.Clear();dest.width=96;dest.height=64;
                    snapshot=(WhimTexDocument)Call(doc,"CaptureLayerClipboard",new List<Layer>(doc.layers));
                    Call(target,"PasteCopiedLayersAt",snapshot,dest.layers,0,null,false);
                    Check(dest.layers.Count==2,"Window copy count");
                    var copied=(MakeSeamlessLayerBehaviour)dest.layers[0].Behaviour;
                    Settings((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour,copied);
                    Check(copied.Id!=doc.layers[0].Id&&copied.TargetLayerId==dest.layers[1].Id,"Copied target remapped");
                    Same(Render(doc),Render(dest),mode+" window copy");
                    var compose=doc.ComposeCanvas();
                    try
                    {
                        var linear=compose.GetPixels();Same(Render(doc),linear,mode+" export compose");
                        var format=typeof(WhimTexWindow).GetNestedType("TextureExportFormat",F);
                        byte[] png=(byte[])typeof(WhimTexWindow).GetMethod("EncodeExportTexture",F).Invoke(null,new object[]{compose,Enum.Parse(format,"Png")});
                        var decoded=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(2,2,TextureFormat.RGBA32,false,true));
                        try
                        {
                            Check(decoded.LoadImage(png),"Export PNG decode");
                            for(int i=0;i<linear.Length;i++)linear[i]=new Color(Mathf.Clamp01(linear[i].r),Mathf.Clamp01(linear[i].g),Mathf.Clamp01(linear[i].b),Mathf.Clamp01(linear[i].a)).gamma;
                            Same(linear,decoded.GetPixels(),mode+" PNG encoding",.005f);
                        }
                        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(decoded);}
                    }
                    finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(compose);}
                    Undo.ClearUndo(doc);Undo.ClearUndo(dest);
                }
                finally{if(snapshot!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(snapshot);Close(target);Close(source);if(doc!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);if(loaded!=null)WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(loaded);}
            }
            return "PASS workflow: "+checks+" checks; four modes saved/reopened, settings, target IDs, Undo/Redo, cross-window copy path and export composition.";
        }
        finally{Undo.RevertAllDownToGroup(group);Selection.objects=selection;if(focus!=null)focus.Focus();WhimTex.Tests.UnityC.FixtureContext.Scope.DeleteAsset(folder);}
    }
    static string ExecutePreview()
    {
        checks=0;var focus=EditorWindow.focusedWindow;var window=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
        try
        {
            var doc=(WhimTexDocument)Get(window,"activeDocument");Populate(doc,768,512,2,Mode.PatchQuilting);window.ShowUtility();
            var tool=window.GetType().GetField("canvasTool",F);
            tool.SetValue(window,Enum.Parse(tool.FieldType,"Pencil"));Call(window,"UpdateCanvasRender");
            var rt=(RenderTexture)Get(window,"canvasTexture");Check(rt.width==768&&rt.height==512,"Actual Pencil preview dimensions");
            Same(Render(doc),Read(rt),"Full-resolution window preview/export");
            var cache=Get(window,"canvasEffectCache");Check((long)cache.GetType().GetProperty("Bytes",F).GetValue(cache)>0,"Window cache populated");
            var callbacks=new List<Action>();
            foreach(var update in (List<Action<bool>>)Get(Get(window,"toolkitLayerBindings"),"updates"))
                if(update.Target!=null)foreach(var field in update.Target.GetType().GetFields(F))
                    if(field.GetValue(update.Target) is Action action&&action.Method.Name.Contains("RefreshThumbnail"))callbacks.Add(action);
            Check(callbacks.Count>0,"Captured actual thumbnail callbacks");
            Close(window);window=null;
            foreach(var callback in callbacks)callback();
            Check((long)cache.GetType().GetProperty("Bytes",F).GetValue(cache)==0,"Window close releases result cache");
            return "PASS full-resolution preview: "+checks+" checks at 768x512.";
        }
        finally{Close(window);if(focus!=null)focus.Focus();}
    }
    static string ExecuteStress(int size=1024,int onlyMode=-1)
    {
        checks=0;var doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());var report=new StringBuilder();
        var cache=(IDisposable)Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        try
        {
            var layer=Populate(doc,size,size*3/4,4,Mode.OffsetBlend);layer.quiltingQuality=MakeSeamlessLayerBehaviour.QuiltingQuality.High;
            layer.quiltingWidth=.45f;layer.quiltingAlongSearch=.25f;
            foreach(Mode mode in new[]{Mode.OffsetBlend,Mode.Mirror,Mode.ScreenedPoisson,Mode.PatchQuilting})
            {
                if(onlyMode>=0&&(int)mode!=onlyMode)continue;
                layer.mode=mode;Color[] first=null;var times=new double[3];
                for(int i=0;i<3;i++)
                {
                    var watch=Stopwatch.StartNew();var rt=(RenderTexture)Call(doc,"RenderCanvasWithCache",size,cache,false,null);
                    Color[] pixels;try{pixels=Read(rt);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}watch.Stop();times[i]=watch.Elapsed.TotalMilliseconds;
                    if(first==null){first=pixels;Same(pixels,pixels,"Stress finite");}else Same(first,pixels,"Stress cache");
                }
                report.AppendLine($"{mode} {size}x{size*3/4}: first {times[0]:F2} ms; cached {times[1]:F2}/{times[2]:F2} ms (readback included)");
            }
            return report.ToString();
        }
        finally{cache.Dispose();WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);}
    }
    static string ExecuteVisuals()
    {
        string folder=WhimTex.Tests.UnityC.FixtureContext.Scope.Temp;
        const int n=192;var sheet=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(n*5,n*5,TextureFormat.RGB24,false,false));var doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
        try
        {
            for(int fixture=0;fixture<5;fixture++)
            {
                if(fixture>0){WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());}
                var effect=Populate(doc,n,n,fixture,Mode.OffsetBlend);
                effect.quiltingChannels=fixture==4?MakeSeamlessLayerBehaviour.QuiltingChannels.Independent:MakeSeamlessLayerBehaviour.QuiltingChannels.Linked;
                for(int column=0;column<5;column++)
                {
                    effect.enabled=column!=0;
                    if(column>0)effect.mode=new[]{Mode.OffsetBlend,Mode.Mirror,Mode.ScreenedPoisson,Mode.PatchQuilting}[column-1];
                    var pixels=Render(doc);
                    for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                    {
                        var c=pixels[((y+n/2)%n)*n+(x+n/2)%n];
                        if(fixture==4)c=new Color(c.r,c.r,c.r,1);
                        float bg=((x/12+y/12)%2==0)?.22f:.45f;
                        c=new Color(c.r*c.a+bg*(1-c.a),c.g*c.a+bg*(1-c.a),c.b*c.a+bg*(1-c.a),1).gamma;
                        sheet.SetPixel(column*n+x,(4-fixture)*n+y,c);
                    }
                }
            }
            sheet.Apply();File.WriteAllBytes(Path.Combine(folder,"visual-matrix.png"),sheet.EncodeToPNG());
            return "Generated original five-by-five visual matrix; actual PNG is retained in the diagnostic artifacts envelope before owned cleanup. Manual review remains pending.";
        }
        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);}
    }
    static string ExecuteMultiWindow()
    {
        checks=0;var focus=EditorWindow.focusedWindow;
        var windows=new WhimTexWindow[2];var caches=new object[2];var report=new StringBuilder();
        long Bytes(object cache)=>(long)cache.GetType().GetProperty("Bytes",F).GetValue(cache);
        try
        {
            for(int i=0;i<2;i++)
            {
                var w=windows[i]=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexWindow>());
                var doc=(WhimTexDocument)Get(w,"activeDocument");var layer=Populate(doc,1024,768,4,Mode.PatchQuilting);
                layer.quiltingQuality=MakeSeamlessLayerBehaviour.QuiltingQuality.High;layer.quiltingSeed+=i;
                w.ShowUtility();var tool=w.GetType().GetField("canvasTool",F);tool.SetValue(w,Enum.Parse(tool.FieldType,"Pencil"));
                Call(w,"UpdateCanvasRender");caches[i]=Get(w,"canvasEffectCache");
                long bytes=Bytes(caches[i]);Check(bytes>0&&bytes<=256L*1024*1024,"Per-window cache budget");
                report.AppendLine($"Window {i}: result cache {bytes/1048576.0:F1} MiB");
            }
            long retained=Bytes(caches[1]);var before=Read((RenderTexture)Get(windows[1],"canvasTexture"));
            Close(windows[0]);windows[0]=null;Check(Bytes(caches[0])==0,"First cache disposed");Check(Bytes(caches[1])==retained,"Second window cache isolated");
            Call(windows[1],"UpdateCanvasRender");Same(before,Read((RenderTexture)Get(windows[1],"canvasTexture")),"Other window survives close");
            Close(windows[1]);windows[1]=null;Check(Bytes(caches[1])==0,"Second cache disposed");
            return "PASS two full-resolution windows; "+report;
        }
        finally{foreach(var window in windows)Close(window);if(focus!=null)focus.Focus();}
    }

    // Stress is synchronous: RunReport emits its result only after owned cleanup.
    // Redirect consumers to that exact receipt; do not infer a latest run from shared state.
    // Literal JSON preserves the redirect in the native raw response without DTO interpretation.
    public static string Status() => @"{
        ""status"":""skipped"",
        ""checks"":0,
        ""message"":""Stress status and timings are returned by SeamlessReleaseTests.Stress(size, onlyMode) after owned cleanup. Read the selected Stress result in the uniform runner report under Temp/WhimTex/test-runs. This redirect does not run Stress, read or validate a report, or assert a current outcome."",
        ""failures"":[],
        ""redirect"":{
            ""entry"":""SeamlessReleaseTests.Stress"",
            ""reportDirectory"":""Temp/WhimTex/test-runs"",
            ""scenarioIdPrefix"":""seamless-release-stress"",
            ""resultPath"":""results[].testResult"",
            ""nativeResponsePath"":""results[].attempts[].reply.stdout"",
            ""resultFields"": [""status"",""checks"",""message"",""failures""],
            ""verification"":""Match the selected scenario ID, source input and typed native arguments; verify current source/production receipts and completed cleanup. Do not infer current proof from file age, timings alone or an older PASS.""
        }
    }";

    public static string Workflow() => WhimTex.Tests.UnityC.FixtureContext.Run("SeamlessReleaseTests.Workflow", () => { ExecuteWorkflow(); });

    public static string Preview() => WhimTex.Tests.UnityC.FixtureContext.Run("SeamlessReleaseTests.Preview", () => { ExecutePreview(); });

    public static string Stress(int size=1024,int onlyMode=-1) => WhimTex.Tests.UnityC.FixtureContext.RunReport("SeamlessReleaseTests.Stress", () =>
    {
        // Stored IDs are sparse (0, 2, 3, 4), not indices into the four-mode loop.
        WhimTex.Tests.UnityC.FixtureContext.Context.True(onlyMode < 0 || Enum.IsDefined(typeof(Mode), onlyMode),
            "Stress requires a stored SeamlessMode ID (0, 2, 3, 4), or a negative all-modes selector; got " + onlyMode);
        return ExecuteStress(size, onlyMode);
    });

    public static string Visuals()
    {
        const string label="SeamlessReleaseTests.Visuals";
        try
        {
            byte[] png=null;
            string diagnostic=WhimTex.Tests.UnityC.FixtureContext.Diagnostic(label,()=>
            {
                var scope=WhimTex.Tests.UnityC.FixtureContext.Scope;
                string report=ExecuteVisuals();
                if(!ReferenceEquals(scope,WhimTex.Tests.UnityC.FixtureContext.Scope))
                    throw new InvalidDataException("Visual diagnostic changed its owned scope");
                string root=Path.GetFullPath(scope.Temp),leaf=Path.GetFileName(root);
                if(leaf.Length!=39||!leaf.StartsWith("UnityC-",StringComparison.Ordinal)||
                    !Guid.TryParseExact(leaf.Substring(7),"N",out _))
                    throw new InvalidDataException("Expected current GUID visual output scope");
                string file=Path.Combine(root,"visual-matrix.png");
                if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0||
                    (File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Refusing linked visual diagnostic output");
                png=File.ReadAllBytes(file); // Exact owned output, before Diagnostic disposes the scope.
                return report;
            });
            var json=Assembly.Load("Newtonsoft.Json");
            var objectType=json.GetType("Newtonsoft.Json.Linq.JObject",true);
            var arrayType=json.GetType("Newtonsoft.Json.Linq.JArray",true);
            var valueType=json.GetType("Newtonsoft.Json.Linq.JValue",true);
            var item=objectType.GetProperty("Item",new[]{typeof(string)});
            object result=objectType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{diagnostic});
            var status=item.GetValue(result,new object[]{"status"});
            if(status==null||status.ToString()!="skipped")return diagnostic;
            if(png==null)throw new InvalidDataException("Visual output was not captured");
            object Text(string value)=>valueType.GetConstructor(new[]{typeof(string)}).Invoke(new object[]{value});
            void Put(object target,string key,object value)=>item.SetValue(target,value,new object[]{key});
            object artifact=Activator.CreateInstance(objectType),artifacts=Activator.CreateInstance(arrayType);
            Put(artifact,"name",Text("visual-matrix.png"));Put(artifact,"encoding",Text("base64"));
            Put(artifact,"content",Text(Convert.ToBase64String(png)));
            arrayType.GetMethod("Add",new[]{typeof(object)}).Invoke(artifacts,new object[]{artifact});
            Put(result,"artifacts",artifacts);
            return result.ToString(); // Preserve diagnostic/SKIP and all assertion/cleanup failures.
        }
        catch(Exception error){return WhimTex.Tests.TestContext.Result("failed",0,label,error.ToString()).ToJson();}
    }

    public static string MultiWindow() => WhimTex.Tests.UnityC.FixtureContext.Run("SeamlessReleaseTests.MultiWindow", () => { ExecuteMultiWindow(); });
}
