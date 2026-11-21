using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using Object = UnityEngine.Object;

// The full-frame reference preserves the pre-region Blur equations and pass order.
// Owned transient documents only; native Undo touches only these fixtures.
public static partial class BlurRegionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static object Call(object o, string name, params object[] args)
    {
        var method = o.GetType().GetMethod(name, F); var parameters = method.GetParameters(); int count = args.Length;
        Array.Resize(ref args, parameters.Length); for (int i = count; i < args.Length; i++) args[i] = parameters[i].DefaultValue;
        return method.Invoke(o, args);
    }
    static void Static(string type, string name) => T(type).GetMethod(name,F).Invoke(null,null);
    static Material Material(string name) => (Material)T("WhimTexMaterials").GetProperty(name,F).GetValue(null);
    static RenderTexture RT(int width, int height, bool full = false)
    {
        var r = new RenderTexture(width,height,0,full?RenderTextureFormat.ARGBFloat:RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
        {hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        if(!r.Create())throw new Exception("Allocation failed");return r;
    }
    static byte[] Read(RenderTexture r, RectInt? area = null)
    {
        var rect=area??new RectInt(0,0,r.width,r.height); var previous=RenderTexture.active; var t=new Texture2D(rect.width,rect.height,TextureFormat.RGBAHalf,false,true);
        try {RenderTexture.active=r;t.ReadPixels(new Rect(rect.x,rect.y,rect.width,rect.height),0,0,false);return t.GetRawTextureData<byte>().ToArray();}
        finally {RenderTexture.active=previous;Object.DestroyImmediate(t);}
    }
    static void Exact(byte[] a,byte[] b,string label)
    {if(a.Length!=b.Length)throw new Exception(label+" size");for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new Exception(label+" byte "+i+": "+a[i]+" != "+b[i]);}
    [Serializable] public class Report {public string status;public string message="Blur regions / full-frame oracle";public string[] failures=Array.Empty<string>();public int side,diameter,mismatchedHalf,channels,segments;public bool outside,masked;public double maxError,beforeMs,afterMs,commitMs;public long gpuBytes;public int checks;public string stage;public bool cleanupComplete;public List<string> differences=new List<string>();}
    public static string Run(int side=512,int diameter=81,bool outside=false,int channels=15,bool masked=false,int segments=1,bool transformed=false,int canvasSide=0,bool external=false,bool tiled=false)
    {
        var report=new Report {status="passed",side=side,diameter=diameter,channels=channels,masked=masked,segments=segments,outside=outside};
        string journal=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Temp/WhimTex/test-runs/blur-"+side+"-"+diameter+"-"+(outside?"outside":"inside")+"-progress.json"));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(journal));
        void Stage(string stage) {report.stage=stage;System.IO.File.WriteAllText(journal,JsonUtility.ToJson(report,true));}
        Stage("setup");
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.name="BlurRegion owned fixture";doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=canvasSide>0?canvasSide:side;
        var layer=new DrawingLayerBehaviour {colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
        var seed=new Texture2D(side,side,TextureFormat.RGBAHalf,false,true){hideFlags=HideFlags.HideAndDontSave};
        RenderTexture frozen=null,reference=null,actual=null;Object cursor=null;Texture2D selection=null;Material oldBlur=null,oldComposite=null;
        try
        {
            Stage("seed");var raw=seed.GetRawTextureData<ushort>();
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)
            {int i=(y*side+x)*4;raw[i]=(ushort)(0x3800+(x%29)*79);raw[i+1]=(ushort)(0xb400+(y%23)*53);raw[i+2]=(ushort)(0x3000+(x%19)*37);raw[i+3]=(ushort)(x%37==0?0:0x3a00);}
            seed.Apply(false,false);Call(layer,"AdoptStoredTexture",seed);doc.layers.Add(layer);Call(doc,"NormalizeModel");
            if(side==8192)
            {
                var repair=typeof(WhimTexApi).GetMethod("PaintRepair",F);
                var operation=repair.GetParameters()[2].ParameterType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{"{\"op\":\"blurStroke\",\"points\":[[100,100],[200,100]],\"size\":512}"});
                repair.Invoke(null,new object[]{doc,layer.Owner,operation,false});report.checks++;
            }
            if(transformed) layer.Owner.transform=new TextureTransform{scale=new Vector2(.8f,1.1f),rotation=23,pivot=new Vector2(.5f,.5f)};
            Call(layer,"PrepareBufferedStroke",doc.width,doc.height,"Blur test");
            var surface=(TiledPaintSurface)Get(layer,"tiledSurface");
            Stage("source ready");
            if(outside)
            {
                var tile=RT(256,256);surface.CopyRegion(new RectInt(0,0,256,256),tile);
                try
                {
                    var imports=new List<TiledPaintImportTile>();for(int y=0;y<side;y+=256)for(int x=-side;x<0;x+=256)
                        imports.Add(new TiledPaintImportTile(new Vector2Int(x/256,y/256),tile,new RectInt(0,0,256,256)));
                    Call(layer,"CommitTiledHistory",surface.ImportTiles(imports),"Owned overflow seed");Static("PaintTileTransfer","Drain");Static("PaintHistoryStore","FlushWrites");
                }
                finally {Object.DestroyImmediate(tile);}
            }
            int origin=outside?-side:0;
            var view=new RectInt(origin,0,side,side);
            float extent=diameter*.5f*side/doc.width*(transformed?1.5f:1);
            int p0=Math.Max(0,(int)(side*.48f-extent)-8),p1=Math.Min(side,(int)(side*.53f+extent)+8);
            var probe=tiled?new RectInt(0,0,side,side):new RectInt(p0,p0,p1-p0,p1-p0);
            var logicalProbe=new RectInt(probe.x+origin,probe.y,probe.width,probe.height);
            frozen=RT(side,side);reference=RT(side,side);actual=RT(probe.width,probe.height);
            surface.CopyRegion(view,frozen);Graphics.CopyTexture(frozen,reference);Read(frozen,new RectInt(0,0,1,1));
            var start=tiled?new Vector2(.01f,.51f):new Vector2(.48f,.51f);var end=tiled?new Vector2(-.02f,.48f):new Vector2(.53f,.48f);
            if(masked)
            {
                selection=new Texture2D(side,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var values=new Color[side];for(int x=0;x<side;x++)values[x]=Color.white*Selection(x,side);selection.SetPixels(values);selection.Apply();
            }
            oldBlur=Frozen("GaussianBlur");oldComposite=Frozen("BlurBrush");
            ShaderUtil.CompilePass(oldBlur,2,true);ShaderUtil.CompilePass(oldComposite,0,true);
            Stage("full-frame reference");var watch=Stopwatch.StartNew();for(int n=0;n<segments;n++) Reference(layer,reference,frozen,start,end,side,diameter,.5f,.63f,origin,channels,selection,oldBlur,oldComposite,doc.width,tiled);Read(reference,new RectInt(0,0,1,1));report.beforeMs=watch.Elapsed.TotalMilliseconds;var expected=Read(reference,probe);var initial=Read(frozen,probe);
            Object.DestroyImmediate(reference);reference=null;if(!external){Object.DestroyImmediate(frozen);frozen=null;}
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            start.x+=origin/(float)side;end.x+=origin/(float)side;
            Call(layer,"ConfigureStrokeWriteProtection",channels);
            var beforeTarget=RenderTexture.active;bool beforeSrgb=GL.sRGBWrite;
            Stage("regional blur");watch.Restart();for(int n=0;n<segments;n++) Call(layer,"BlurSegment",start,end,doc.width,doc.height,(float)diameter,.5f,.63f,external?frozen:null,tiled,selection);
            if(RenderTexture.active!=beforeTarget||GL.sRGBWrite!=beforeSrgb)throw new Exception("Blur leaked caller render state");report.checks++;
            surface.CopyRegion(logicalProbe,actual);Read(actual,new RectInt(0,0,1,1));report.afterMs=watch.Elapsed.TotalMilliseconds;var result=Read(actual);
            if(tiled)
            {
                var guard=RT(256,side);
                try {surface.CopyRegion(new RectInt(side,0,256,side),guard);foreach(byte value in Read(guard))if(value!=0)throw new Exception("Wrapped stroke wrote outside its frame");}
                finally {RenderTexture.active=beforeTarget;Object.DestroyImmediate(guard);}
                report.checks++;
            }
            for(int i=0;i<result.Length;i+=2)
            {
                ushort a=BitConverter.ToUInt16(expected,i),b=BitConverter.ToUInt16(result,i);
                if(a!=b){if(report.differences.Count<12)report.differences.Add(((i/8)%probe.width)+","+((i/8)/probe.width)+","+((i/2)%4)+": "+a+" -> "+b);report.mismatchedHalf++;report.maxError=Math.Max(report.maxError,Math.Abs(Mathf.HalfToFloat(a)-Mathf.HalfToFloat(b)));}
            }
            Stage("commit");watch.Restart();Call(layer,"EndStroke");Call(layer,"FinishStrokeStorage");Static("PaintHistoryStore","FlushWrites");report.commitMs=watch.Elapsed.TotalMilliseconds;
            cursor=(Object)Get(layer,"PixelUndoObject");Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);Undo.IncrementCurrentGroup();
            var final=Read(actual);
            bool changed=false;for(int i=0;i<final.Length;i++)if(final[i]!=initial[i]){changed=true;break;}
            if(!changed)throw new Exception("Blur changed no pixels");report.checks++;
            Stage("undo");Undo.PerformUndo();Call(layer,"ReconcilePixelUndo");surface=(TiledPaintSurface)Get(layer,"tiledSurface");
            if(surface==null){Call(layer,"EnsureTiledSource",side,side);surface=(TiledPaintSurface)Get(layer,"tiledSurface");}
            surface.CopyRegion(logicalProbe,actual);Exact(initial,Read(actual),"Undo");report.checks++;
            Stage("redo");Undo.PerformRedo();Call(layer,"ReconcilePixelUndo");Call(layer,"EnsureTiledSource",side,side);surface=(TiledPaintSurface)Get(layer,"tiledSurface");
            surface.CopyRegion(logicalProbe,actual);Exact(final,Read(actual),"Redo");report.checks++;
            Stage("cancel");Call(layer,"PrepareBufferedStroke",doc.width,doc.height,"Cancel blur");Call(layer,"BlurSegment",end,start,doc.width,doc.height,(float)diameter,.8f,1f,null,false,null);
            Call(layer,"CancelStroke");surface.CopyRegion(logicalProbe,actual);Exact(final,Read(actual),"Cancel");report.checks++;
            if(side<=1024)
            {
                Call(layer,"PrepareBufferedStroke",doc.width,doc.height,"Failed blur");Call(layer,"BlurSegment",end,start,doc.width,doc.height,(float)diameter,.8f,1f,null,false,null);
                bool rejected=false;try {Call(layer,"BlurSegment",new Vector2(float.NaN,0),start,doc.width,doc.height,(float)diameter,.8f,1f,null,false,null);} catch(TargetInvocationException e) when(e.InnerException is ArgumentException){rejected=true;}
                if(!rejected||Get(layer,"regionBefore")!=null)throw new Exception("Failed segment did not cancel the entire stroke");
                surface.CopyRegion(logicalProbe,actual);Exact(final,Read(actual),"Failed stroke rollback");report.checks++;
            }
            if(report.mismatchedHalf!=0)throw new Exception(JsonUtility.ToJson(report));
            foreach(string name in new[]{"GaussianBlur","BlurBrush","PaintBrush"})
                foreach(var issue in ShaderUtil.GetShaderMessages(Material(name).shader))
                    if(issue.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error ||
                        issue.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning && !ExistingWarning(oldBlur,issue.message))throw new Exception(name+": "+issue.message);
            report.checks++;
            report.gpuBytes=surface.AllocatedBytes;
            Stage("verified");
        }
        catch(Exception error) {report.status="failed";report.failures=new[]{error.ToString()};throw;}
        finally
        {
            Stage("cleanup");Static("PaintTileTransfer","Drain");Static("PaintHistoryStore","FlushWrites");
            if(cursor==null)cursor=(Object)Get(layer,"PixelUndoObject");if(cursor!=null)Undo.ClearUndo(cursor);
            Undo.ClearUndo(doc);Undo.ClearUndo(seed);Call(layer,"ReleaseTransientResources");if(seed!=null)Object.DestroyImmediate(seed);doc.layers.Clear();Object.DestroyImmediate(doc);
            foreach(var m in new[]{oldBlur,oldComposite})if(m!=null){Object.DestroyImmediate(m.shader);Object.DestroyImmediate(m);}
            if(selection!=null)Object.DestroyImmediate(selection);
            RenderTexture.active=previous;
            foreach(var r in new[]{frozen,reference,actual})if(r!=null)Object.DestroyImmediate(r);
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            report.cleanupComplete=true;Stage("finished");
        }
        return JsonUtility.ToJson(report,true);
    }
    static bool ExistingWarning(Material reference,string message)
    {
        foreach(var issue in ShaderUtil.GetShaderMessages(reference.shader))if(issue.message==message)return true;
        return false;
    }
    static float Selection(int x,int side) => x<0||x>=side?0:x<side*.48f?0:x<side*.52f?.5f:1;
    [Serializable] public class FixtureFile { public string path,sha256; }
    [Serializable] public class FixtureManifest { public FixtureFile[] files; }
    static Material Frozen(string name)
    {
        string path=System.IO.Path.Combine(Application.dataPath,"../Packages/com.dcfapixels.whimtex/Tests~/Fixtures/BlurRegion/",name+".shader.txt");
        var manifest=(FixtureManifest)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json").GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"manifest.json")),typeof(FixtureManifest)});
        var bytes=System.IO.File.ReadAllBytes(path);using(var hash=System.Security.Cryptography.SHA256.Create())
        {
            string actual=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            bool authenticated=false;foreach(var entry in manifest.files)if(entry.path==name+".shader.txt")authenticated=actual==entry.sha256;
            if(!authenticated)throw new Exception("Unauthenticated frozen reference: "+name);
        }
        var text=System.Text.Encoding.UTF8.GetString(bytes).Replace("Hidden/WhimTex/"+name,"Hidden/WhimTex/TestReference"+name);
        return new Material(ShaderUtil.CreateShaderAsset(text,true)){hideFlags=HideFlags.HideAndDontSave};
    }
    static void CheckRepeatKernel(RenderTexture frozen,RenderTexture expected,Vector4[] kernel)
    {
        int side=frozen.width,h=Mathf.CeilToInt(kernel[4].x)+2;
        var input=RT(side,side);var horizontal=RT(1024,side);var output=RT(1024,1024);
        var previous=RenderTexture.active;var blur=Material("GaussianBlur");var wrap=frozen.wrapMode;
        try
        {
            Graphics.CopyTexture(frozen,0,0,0,side-h,side,h,input,0,0,0,0);
            Graphics.CopyTexture(frozen,0,0,0,0,side,side-h,input,0,0,0,h);
            blur.SetVectorArray("_Kernel",kernel);blur.SetInt("_PairCount",5);blur.SetFloat("_CenterWeight",.2f);blur.SetInt("_Edges",1);
            blur.SetVector("_RegionFrame",new Vector4(side,side,1,0));blur.SetVector("_RegionInput",new Vector4(0,-h,0,0));
            blur.SetVector("_RegionOutput",new Vector4(-1,0,1024,side));blur.SetVector("_Direction",new Vector4(1f/side,0,0,0));Graphics.Blit(input,horizontal,blur,2);
            blur.SetVector("_RegionInput",new Vector4(-1,0,0,0));blur.SetVector("_RegionOutput",new Vector4(-1,-1,1024,1024));
            blur.SetVector("_Direction",new Vector4(0,1f/side,0,0));Graphics.Blit(horizontal,output,blur,2);
            Exact(Read(expected),Read(output,new RectInt(1,1,side,side)),"Repeat Gaussian");
        }
        finally {blur.SetVector("_RegionFrame",Vector4.zero);frozen.wrapMode=wrap;RenderTexture.active=previous;Object.DestroyImmediate(input);Object.DestroyImmediate(horizontal);Object.DestroyImmediate(output);}
    }
    static void Reference(DrawingLayerBehaviour layer,RenderTexture current,RenderTexture frozen,Vector2 from,Vector2 to,int side,float size,float hardness,float strength,int origin,int channels,Texture selection,Material oldBlur,Material oldComposite,int canvasSide,bool tiled)
    {
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        RenderTexture mask=RT(canvasSide,canvasSide,true),scratch=RT(side,side),blurred=RT(side,side),output=RT(side,side);
        var renderer=typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer",BindingFlags.NonPublic);
        var stampType=typeof(DrawingLayerBehaviour).GetNestedType("PaintStamp",BindingFlags.NonPublic);
        try
        {
            RenderTexture.active=mask;GL.Clear(false,true,Color.clear);
            var transform=(TextureTransform)Get(Get(layer,"Owner"),"PixelCanvasTransform");
            var stamps=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(stampType));
            var offset = new Vector2(origin / (float)side, 0);
            var canvasSize = new Vector2(canvasSide, canvasSide);
            var start = transform.Map(from + offset, canvasSize);
            var end = transform.Map(to + offset, canvasSize);
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start * canvasSide, end * canvasSide) / Mathf.Max(1, size * .2f)));
            for(int i=0;i<=count;i++){object stamp=Activator.CreateInstance(stampType);stampType.GetField("center").SetValue(stamp,Vector2.Lerp(start,end,i/(float)count));stampType.GetField("size").SetValue(stamp,size);stampType.GetField("color").SetValue(stamp,Color.white);stamps.Add(stamp);}
            var draw=renderer.GetMethod("Draw",F);object[] args={mask,stamps,size,hardness,false,Enum.ToObject(T("PencilShape"),0),Color.white,false,canvasSide,canvasSide,Vector2.zero,tiled,transform,false,false};
            int supplied=args.Length;Array.Resize(ref args,draw.GetParameters().Length);for(int i=supplied;i<args.Length;i++)args[i]=draw.GetParameters()[i].DefaultValue;args[args.Length-1]=new Rect(origin/(float)side,0,1,1);draw.Invoke(null,args);
            var blur=origin==0?oldBlur:Material("GaussianBlur");var kernel=new Vector4[128];float pixelScale=(float)Call(Get(layer,"Owner"),"GetPixelScale"),radius=Mathf.Max(pixelScale,size*.5f);
            blur.SetVector("_RegionFrame",origin==0?Vector4.zero:new Vector4(side,side,0,0));blur.SetVector("_RegionInput",new Vector4(origin,0,0,0));blur.SetVector("_RegionOutput",new Vector4(origin,0,side,side));
            kernel[0]=new Vector4(Mathf.Max(pixelScale,radius*.15f),.12f,0,0);kernel[1]=new Vector4(Mathf.Max(2*pixelScale,radius*.30f),.10f,0,0);kernel[2]=new Vector4(Mathf.Max(3*pixelScale,radius*.45f),.08f,0,0);kernel[3]=new Vector4(Mathf.Max(4*pixelScale,radius*.60f),.06f,0,0);kernel[4]=new Vector4(Mathf.Max(5*pixelScale,radius*.80f),.04f,0,0);
            blur.SetVectorArray("_Kernel",kernel);blur.SetInt("_PairCount",5);blur.SetFloat("_CenterWeight",.2f);blur.SetInt("_Edges",tiled?2:1);blur.SetVector("_Direction",new Vector4(1f/canvasSide,0,0,0));Graphics.Blit(frozen,scratch,blur,2);
            blur.SetVector("_Direction",new Vector4(0,1f/canvasSide,0,0));Graphics.Blit(scratch,blurred,blur,2);
            if(tiled&&side==512)CheckRepeatKernel(frozen,blurred,kernel);
            // Selection is a canvas-space multiplier of coverage, independently on the CPU.
            if(selection!=null)
            {
                var pixels=new Texture2D(side,side,TextureFormat.RGBAFloat,false,true);RenderTexture.active=mask;
                pixels.ReadPixels(new Rect(0,0,side,side),0,0,false);var values=pixels.GetPixels();
                for(int y=0;y<side;y++)for(int x=0;x<side;x++) {values[y*side+x]*=Selection(origin+x,side);}
                pixels.SetPixels(values);pixels.Apply();Graphics.Blit(pixels,mask);Object.DestroyImmediate(pixels);
            }
            oldComposite.SetTexture("_BlurTex",blurred);oldComposite.SetTexture("_MaskTex",mask);oldComposite.SetFloat("_Strength",strength);Graphics.Blit(current,output,oldComposite);
            if(channels!=15)
            {
                var protection=Material("PaintWriteProtection");protection.SetTexture("_PaintBefore",frozen);protection.SetTexture("_OriginalStraight",Get(layer,"StoredTexture") as Texture);protection.SetFloat("_StraightFallback",1);
                protection.SetVector("_WriteChannels",new Vector4((channels&1)!=0?1:0,(channels&2)!=0?1:0,(channels&4)!=0?1:0,(channels&8)!=0?1:0));Graphics.Blit(output,current,protection);
                protection.SetTexture("_PaintBefore",null);protection.SetTexture("_OriginalStraight",null);
            }
            else Graphics.CopyTexture(output,current);
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;Material("GaussianBlur").SetVector("_RegionFrame",Vector4.zero);foreach(var r in new[]{mask,scratch,blurred,output})Object.DestroyImmediate(r);}
    }
}
