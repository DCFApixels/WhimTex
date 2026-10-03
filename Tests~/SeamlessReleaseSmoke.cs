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

public static class SeamlessReleaseSmoke
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
    static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,F).SetValue(o,value);
    static void Check(bool b,string text){checks++;if(!b)throw new Exception(text);}
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=previous;Object.DestroyImmediate(t);}
    }
    static Color[] Render(TextureCompositor doc)
    {var rt=(RenderTexture)Call(doc,"RenderAllLayers",doc.width,doc.height);try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}}
    static void Same(Color[] a,Color[] b,string label,float tolerance=.003f)
    {
        Check(a.Length==b.Length,label+" size");float max=0;
        for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)
        {Check(float.IsFinite(b[i][c]),label+" finite");max=Math.Max(max,Math.Abs(a[i][c]-b[i][c]));}
        Check(max<=tolerance,label+" max error "+max);
    }
    static Texture2D Source(int w,int h,int fixture)
    {
        var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);var p=new Color[w*h];
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
    static MakeSeamlessLayerBehaviour Populate(TextureCompositor doc,int w,int h,int fixture,Mode mode)
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
    static void Close(TextureCompositorWindow window)
    {if(window==null)return;typeof(EditorWindow).GetProperty("hasUnsavedChanges",F).SetValue(window,false);window.Close();}
    public static string Status()=>"Editor main thread responsive; stress status: "+SessionState.GetString("WhimTex.ReleaseStress","No stored result");
    public static string Workflow()
    {
        checks=0;var focus=EditorWindow.focusedWindow;var selection=Selection.objects;
        string folder="Assets/WhimTexSeamRelease_"+Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
        try
        {
            foreach(Mode mode in new[]{Mode.OffsetBlend,Mode.Mirror,Mode.ScreenedPoisson,Mode.PatchQuilting})
            {
                TextureCompositorWindow source=null,target=null;TextureCompositor doc=null,loaded=null,snapshot=null;
                try
                {
                    doc=ScriptableObject.CreateInstance<TextureCompositor>();var effect=Populate(doc,96,64,3,mode);
                    var expected=Render(doc);string settings=JsonUtility.ToJson(effect);
                    string path=WhimTexDocumentFile.Save(doc,folder+"/"+mode+".tiff");
                    Object.DestroyImmediate(doc);doc=null;
                    loaded=WhimTexDocumentFile.Load(path);
                    var restored=(MakeSeamlessLayerBehaviour)loaded.layers[0].Behaviour;
                    Settings(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(settings),restored);
                    Same(expected,Render(loaded),mode+" save/close/reopen");
                    Check(restored.TargetLayerId==loaded.layers[1].Id,"Saved target");
                    source=ScriptableObject.CreateInstance<TextureCompositorWindow>();
                    Object.DestroyImmediate((TextureCompositor)Get(source,"compositor"));Set(source,"compositor",loaded);loaded=null;
                    source.ShowUtility();doc=(TextureCompositor)Get(source,"compositor");
                    int editGroup;Undo.IncrementCurrentGroup();editGroup=Undo.GetCurrentGroup();
                    Call(source,"ExecuteModelChange","Release smoke seed",new Action(()=>((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed=71));
                    Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed==-184,"Undo");
                    Undo.PerformRedo();Check(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingSeed==71,"Redo");
                    Undo.RevertAllDownToGroup(editGroup);
                    target=ScriptableObject.CreateInstance<TextureCompositorWindow>();target.ShowUtility();
                    var dest=(TextureCompositor)Get(target,"compositor");dest.layers.Clear();dest.width=96;dest.height=64;
                    snapshot=(TextureCompositor)Call(doc,"CaptureLayerClipboard",new List<Layer>(doc.layers));
                    Call(target,"PasteCopiedLayersAt",snapshot,dest.layers,0,null,false,null);
                    Check(dest.layers.Count==2,"Window copy count");
                    var copied=(MakeSeamlessLayerBehaviour)dest.layers[0].Behaviour;
                    Settings((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour,copied);
                    Check(copied.Id!=doc.layers[0].Id&&copied.TargetLayerId==dest.layers[1].Id,"Copied target remapped");
                    Same(Render(doc),Render(dest),mode+" window copy");
                    var compose=doc.Compose();
                    try
                    {
                        var linear=compose.GetPixels();Same(Render(doc),linear,mode+" export compose");
                        var format=typeof(TextureCompositorWindow).GetNestedType("TextureExportFormat",F);
                        byte[] png=(byte[])typeof(TextureCompositorWindow).GetMethod("EncodeExportTexture",F).Invoke(null,new object[]{compose,Enum.Parse(format,"Png")});
                        var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                        try
                        {
                            Check(decoded.LoadImage(png),"Export PNG decode");
                            for(int i=0;i<linear.Length;i++)linear[i]=new Color(Mathf.Clamp01(linear[i].r),Mathf.Clamp01(linear[i].g),Mathf.Clamp01(linear[i].b),Mathf.Clamp01(linear[i].a)).gamma;
                            Same(linear,decoded.GetPixels(),mode+" PNG encoding",.005f);
                        }
                        finally{Object.DestroyImmediate(decoded);}
                    }
                    finally{Object.DestroyImmediate(compose);}
                    Undo.ClearUndo(doc);Undo.ClearUndo(dest);
                }
                finally{if(snapshot!=null)Object.DestroyImmediate(snapshot);Close(target);Close(source);if(doc!=null)Object.DestroyImmediate(doc);if(loaded!=null)Object.DestroyImmediate(loaded);}
            }
            return "PASS workflow: "+checks+" checks; four modes saved/reopened, settings, target IDs, Undo/Redo, cross-window copy path and export composition.";
        }
        finally{Undo.RevertAllDownToGroup(group);Selection.objects=selection;if(focus!=null)focus.Focus();AssetDatabase.DeleteAsset(folder);}
    }
    public static string Preview()
    {
        checks=0;var focus=EditorWindow.focusedWindow;var window=ScriptableObject.CreateInstance<TextureCompositorWindow>();
        try
        {
            var doc=(TextureCompositor)Get(window,"compositor");Populate(doc,768,512,2,Mode.PatchQuilting);window.ShowUtility();
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
    public static string Stress(int size=1024,int onlyMode=-1)
    {
        checks=0;var doc=ScriptableObject.CreateInstance<TextureCompositor>();var report=new StringBuilder();
        var cache=(IDisposable)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
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
                    var watch=Stopwatch.StartNew();var rt=(RenderTexture)Call(doc,"RenderCachedPreview",size,cache,false,null);
                    Color[] pixels;try{pixels=Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}watch.Stop();times[i]=watch.Elapsed.TotalMilliseconds;
                    if(first==null){first=pixels;Same(pixels,pixels,"Stress finite");}else Same(first,pixels,"Stress cache");
                }
                report.AppendLine($"{mode} {size}x{size*3/4}: first {times[0]:F2} ms; cached {times[1]:F2}/{times[2]:F2} ms (readback included)");
            }
            return report.ToString();
        }
        finally{cache.Dispose();Object.DestroyImmediate(doc);SessionState.SetString("WhimTex.ReleaseStress",report.ToString()+"Completed cleanup");}
    }
    public static string Visuals()
    {
        string folder=Path.GetFullPath("output/seamless-release");Directory.CreateDirectory(folder);
        const int n=192;var sheet=new Texture2D(n*5,n*5,TextureFormat.RGB24,false,false);var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        try
        {
            for(int fixture=0;fixture<5;fixture++)
            {
                if(fixture>0){Object.DestroyImmediate(doc);doc=ScriptableObject.CreateInstance<TextureCompositor>();}
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
            return "Saved "+folder+"/visual-matrix.png; columns source/Offset/Mirror/Poisson/Quilting; rows smooth/sharp/color/alpha/packed R; joins centered.";
        }
        finally{Object.DestroyImmediate(sheet);Object.DestroyImmediate(doc);}
    }
    public static string MultiWindow()
    {
        checks=0;var focus=EditorWindow.focusedWindow;
        var windows=new TextureCompositorWindow[2];var caches=new object[2];var report=new StringBuilder();
        long Bytes(object cache)=>(long)cache.GetType().GetProperty("Bytes",F).GetValue(cache);
        try
        {
            for(int i=0;i<2;i++)
            {
                var w=windows[i]=ScriptableObject.CreateInstance<TextureCompositorWindow>();
                var doc=(TextureCompositor)Get(w,"compositor");var layer=Populate(doc,1024,768,4,Mode.PatchQuilting);
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
}
