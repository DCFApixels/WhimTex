using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Detached, frame-paced fixtures. The full-target renderer is the unchanged non-tiled
// path used by other painting consumers. Identical generated stamps feed both paths.
public static class BrushTileBufferTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const string Key = "WhimTex.Tests.BrushTiles";
    static Type TypeOf(string n) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + n, true);
    static object Get(object o, string n) => o.GetType().GetField(n, F)?.GetValue(o) ?? o.GetType().GetProperty(n, F)?.GetValue(o);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static void Set(object o, string n, object v) => o.GetType().GetField(n, F).SetValue(o, v);
    static object New(string n) => Activator.CreateInstance(TypeOf(n), true);
    static long Pool(string n) => (long)TypeOf("PaintStrokeBuffer").GetProperty(n, S).GetValue(null);
    static void ClearPool() => TypeOf("PaintStrokeBuffer").GetMethod("ClearPool", S).Invoke(null, null);
    static string Json(object o) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { o });
    static RenderTexture RT(int w, int h) => RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
    static byte[] Read(RenderTexture source, RectInt rect)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(rect.width, rect.height, TextureFormat.RGBAHalf, false, true);
        try { RenderTexture.active = source; image.ReadPixels(new Rect(rect.x,rect.y,rect.width,rect.height),0,0,false); return image.GetRawTextureData<byte>().ToArray(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
    }
    static Color[] Colors(RenderTexture source)
    {
        var previous = RenderTexture.active; var image = new Texture2D(source.width,source.height,TextureFormat.RGBAHalf,false,true);
        try { RenderTexture.active=source; image.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false); return image.GetPixels(); }
        finally { RenderTexture.active=previous; Object.DestroyImmediate(image); }
    }
    sealed class Fixture : IDisposable
    {
        public readonly int w,h;
        public readonly DrawingLayerBehaviour layer;
        public readonly object settings, dynamics, parameters;
        public readonly RenderTexture surface;
        public RenderTexture reference, referencePreview;
        public readonly Material conversion;
        public readonly Texture2D pixels,mask;
        readonly MethodInfo draw;
        readonly bool stampBlend, pressure, hdr;
        public Fixture(int w, int h, int diameter, string mode)
        {
            this.w=w;this.h=h;hdr=mode=="hdr";
            layer = new DrawingLayerBehaviour { colorRange=hdr?LayerColorRange.HDR:LayerColorRange.Standard, blendRange=hdr?LayerBlendRange.HDR:LayerBlendRange.Standard };
            pixels = new Texture2D(w,h,hdr?TextureFormat.RGBAHalf:TextureFormat.RGBA32,false,hdr);
            // Zero alpha with nonzero RGB must remain intact in storage. Avoid a full Color[] at 8K.
            if (hdr) { var data=pixels.GetRawTextureData<ushort>(); for(int i=0;i<data.Length;i+=4) { data[i]=0xbc00; data[i+1]=0x4400; data[i+2]=0x3800; data[i+3]=(ushort)((i/4)%7==0?0:0x3400); } }
            else { var data=pixels.GetRawTextureData<Color32>(); for(int i=0;i<data.Length;i++) data[i]=new Color32((byte)(i%251),70,120,(byte)(i%7==0?0:85)); }
            pixels.Apply(); Call(layer,"AdoptStoredTexture",pixels); Set(layer,"preserveStraightPaintRgb",true); surface=(RenderTexture)Call(layer,"EnsurePaintSurface",w,h);
            mask=new Texture2D(32,32,TextureFormat.R8,false,true) { wrapMode=TextureWrapMode.Clamp };
            var m=mask.GetRawTextureData<byte>();for(int i=0;i<m.Length;i++)m[i]=(byte)(20+(i*7)%220);mask.Apply();
            settings=New("PaintToolSettings"); dynamics=Get(settings,"dynamics");
            Set(settings,"brushSize",(float)diameter);Set(settings,"brushHardness",.25f);Set(settings,"brushSpacing",.2f);
            Set(settings,"brushColor",hdr?new Color(-.2f,2f,.6f,.023f):new Color(.7f,.3f,.4f,.023f));
            Set(dynamics,"opacity",.47f);Set(dynamics,"flow",.5f);
            if(mode=="stamp"||mode=="pressure") { Set(dynamics,"blend",BlendMode.Multiply);Set(dynamics,"blendApplication",Enum.Parse(Get(dynamics,"blendApplication").GetType(),"Stamp")); }
            if(mode=="pressure") { Set(dynamics,"pressure",true);Set(Get(dynamics,"pressureOpacity"),"enabled",true); }
            if(mode=="locked") Set(dynamics,"writeChannels",5);
            parameters=Call(settings,"GetStrokeParameters",mode=="erase",(Color?)null);
            if(mode!="normal") parameters=Call(parameters,"WithSelectionMask",mask);
            if(mode=="wrap") parameters=Call(parameters,"WithCanvasWrap");
            if(mode=="pressure") parameters=Call(parameters,"WithPressure",.35f);
            stampBlend=(bool)Call(dynamics,"BlendsEachStamp",mode=="erase");pressure=(bool)Get(dynamics,"PressureOpacity");
            conversion=new Material(Shader.Find("Hidden/WhimTex/AlphaConversion"));
            draw=typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer",BindingFlags.NonPublic).GetMethod("Draw",S);
            Call(layer,"BeginStroke",Point(0));
        }
        public Vector2 Point(int i)
        {
            float t=i/47f;
            return new Vector2(.5f+.45f*Mathf.Sin(t*2*Mathf.PI), .5f+.45f*Mathf.Sin(t*6*Mathf.PI+.2f));
        }
        public void Paint(int i)
        {
            if(i==0)Call(layer,"PaintPoint",Point(i),w,h,parameters);
            else Call(layer,"PaintSegment",Point(i-1),Point(i),w,h,false,parameters);
        }
        public void FullPaint()
        {
            if(reference==null)
            {
                reference=RT(w,h);
                if(stampBlend)
                {
                    if(pressure) { var blend=new Material(Shader.Find("Hidden/WhimTex/Blend")); try{Graphics.Blit(surface,reference,blend,2);}finally{Object.DestroyImmediate(blend);} }
                    else Graphics.Blit(surface,reference);
                }
                else {var old=RenderTexture.active;RenderTexture.active=reference;GL.Clear(false,true,Color.clear);RenderTexture.active=old;}
            }
            var color=(Color)Get(parameters,"Color");color.a*=(float)Get(dynamics,"flow");
            draw.Invoke(null,new object[]{reference,Get(layer,"segmentStamps"),Get(parameters,"Size"),Get(parameters,"Hardness"),Get(parameters,"PixelPerfect"),Get(parameters,"Shape"),color,false,w,h,Get(layer,"patternCenter"),Get(parameters,"WrapCanvas"),Get(layer.Owner,"PixelCanvasTransform"),!hdr,false,null,dynamics,Get(parameters,"StandardColorInputs"),stampBlend,hdr,null,surface,null});
        }
        public RenderTexture Preview() => (RenderTexture)Call(layer,"GetBrushPreviewSurface");
        public RenderTexture FullPreview()
        {
            bool full=referencePreview==null;
            if(full) referencePreview=RT(w,h);
            Call(layer,"ConfigureBrushComposite",conversion);conversion.SetFloat("_Mode",1);
            conversion.SetTexture("_BrushStroke",reference);conversion.SetVector("_BrushStrokeMap",new Vector4(1,1,0,0));
            conversion.SetFloat("_StraightFallback",(bool)Get(layer,"hasBakedPixelFrame")||(bool)Get(layer,"preserveStraightPaintRgb")?(hdr?1:2):0);conversion.SetTexture("_OriginalStraight",pixels);
            if(full) Graphics.Blit(surface,referencePreview,conversion);
            else
            {
                // Same dirty regions and retained full preview as the previous path.
                var tiles=Get(layer,"brushPreviewTiles");int n=(int)Get(tiles,"Count");
                conversion.SetTexture("_MainTex",surface);RenderTexture.active=referencePreview;
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();if(!conversion.SetPass(0))throw new Exception("Full preview shader unavailable");
                    GL.Begin(GL.QUADS);
                    for(int i=0;i<n;i++)
                    {
                        var r=(RectInt)tiles.GetType().GetProperty("Item",F).GetValue(tiles,new object[]{i});
                        float x0=r.xMin/(float)w,y0=r.yMin/(float)h,x1=r.xMax/(float)w,y1=r.yMax/(float)h;
                        GL.TexCoord2(x0,y0);GL.Vertex3(x0,y0,0);GL.TexCoord2(x0,y1);GL.Vertex3(x0,y1,0);
                        GL.TexCoord2(x1,y1);GL.Vertex3(x1,y1,0);GL.TexCoord2(x1,y0);GL.Vertex3(x1,y0,0);
                    }
                    GL.End();
                }
                finally {GL.PopMatrix();}
            }
            return referencePreview;
        }
        public void Dispose()
        {
            Call(layer,"CancelStroke");Call(layer,"ReleaseTransientResources");
            if(reference!=null)RenderTexture.ReleaseTemporary(reference);if(referencePreview!=null)RenderTexture.ReleaseTemporary(referencePreview);
            Object.DestroyImmediate(conversion);Object.DestroyImmediate(mask);Object.DestroyImmediate(pixels);
        }
    }
    public static string Run() => TestContext.Run("Tile accumulation versus full-target stamps",t=>
    {
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;long active=Pool("ActiveBytes");
        try
        {
            GL.sRGBWrite=false;
            foreach(var mode in new[]{"normal","hdr","stamp","pressure","locked","erase","wrap"})
            using(var f=new Fixture(513,517,210,mode))
            {
                for(int i=0;i<10;i++)
                {
                    f.Paint(i); f.FullPaint(); var expected=Colors(f.FullPreview());var actual=Colors(f.Preview());
                    double max=0;int worst=0;
                    for(int p=0;p<actual.Length;p++)for(int c=0;c<4;c++) {double d=Math.Abs(actual[p][c]-expected[p][c])/Math.Max(1,Math.Abs(expected[p][c]));if(d>max){max=d;worst=p;}}
                    t.Near(0,max,.002,mode+" all pixels, seams, partial blocks step="+i+" worst="+worst+" actual="+actual[worst]+" reference="+expected[worst]+" fallback="+Get(f.layer,"preserveStraightPaintRgb"));
                }
                var buffer=Get(f.layer,"advancedStroke");t.True((long)Get(buffer,"TouchedBytes")<=9*256L*256*8,"One allocation per logical block");
            }
            using (var first = new Fixture(513, 517, 210, "hdr"))
            {
                first.Paint(0); var before = Read(first.Preview(), new RectInt(240, 240, 32, 32));
                using (var second = new Fixture(513, 517, 210, "stamp")) { second.Paint(0); second.Preview(); }
                var after = Read(first.Preview(), new RectInt(240, 240, 32, 32));
                for (int i = 0; i < before.Length; i++) t.Equal(before[i], after[i], "Concurrent stroke owns distinct pool pages");
            }
            t.Equal(active,Pool("ActiveBytes"),"All fixture tiles returned");
            t.True(Pool("PooledBytes")<=64L*1024*1024,"Idle pool bounded globally");
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;}
    });
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];
    public static string Start(string runId,int side,int diameter,string mode,bool compare=true)
    {
        if(State!=null)return TestContext.Result("failed",0,"Diagnostic already active").ToJson();
        var state=new object[]{runId,TestContext.Result("running",0,"Preparing tile comparison").ToJson(),null};AppDomain.CurrentDomain.SetData(Key,state);
        var run=new RunState(state,side,diameter,mode,compare);state[2]=(Action)(()=>run.Finish(new Exception("Cancelled")));
        try{run.Prepare();}catch(Exception e){run.Finish(e);}return (string)state[1];
    }
    public static string Poll(string id)=>State!=null&&(string)State[0]==id?(string)State[1]:TestContext.Result("failed",0,"Missing diagnostic").ToJson();
    public static string Cancel(string id){if(State!=null&&(string)State[0]==id)((Action)State[2])();return TestContext.Result("cancelled",0,"Cancelled").ToJson();}
    public static string Cleanup(string id)=>TestContext.Run("Tile diagnostic cleanup",t=>{t.True(State==null||(string)State[0]==id,"Owned state");if(State!=null){((Action)State[2])();AppDomain.CurrentDomain.SetData(Key,null);}});
    sealed class RunState
    {
        readonly object[] state;readonly int side,diameter;readonly string mode;readonly bool compare;readonly TestContext test=new TestContext();
        readonly List<object> frames=new List<object>();Fixture fixture;Texture2D fenceRead;RenderTexture fence;int step;bool done;double started,next;long activeBefore,peak;string report;
        public RunState(object[] state,int side,int diameter,string mode,bool compare){this.state=state;this.side=side;this.diameter=diameter;this.mode=mode;this.compare=compare;}
        public void Prepare()
        {
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
            test.True(side<=8192 && diameter<=2048,"Bounded fixture");test.True(side<8192||SystemInfo.graphicsMemorySize>=7000,"8K VRAM guard");
            foreach(var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())test.True(Get(window,"paintingLayer")==null,"No active user stroke");
            test.True(!(bool)TypeOf("WhimTexProfiler").GetProperty("Recording",S).GetValue(null),"No user recording");
            activeBefore=Pool("ActiveBytes");ClearPool();
            fixture=new Fixture(side,side,diameter,mode);fence=RT(1,1);fenceRead=new Texture2D(1,1,TextureFormat.RGBAHalf,false,true);
            Fence(fixture.surface);started=EditorApplication.timeSinceStartup;next=started+.1;EditorApplication.update+=Tick;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        double Fence(RenderTexture source)
        {
            var clock=Stopwatch.StartNew();Graphics.Blit(source,fence);RenderTexture.active=fence;fenceRead.ReadPixels(new Rect(0,0,1,1),0,0,false);return clock.Elapsed.TotalMilliseconds;
        }
        void Tick()
        {
            if(done||EditorApplication.timeSinceStartup<next)return;
            var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                if(EditorApplication.timeSinceStartup-started>55)throw new Exception("Bounded diagnostic timed out");
                GL.sRGBWrite=false;var clock=Stopwatch.StartNew();fixture.Paint(step);double tiledCpu=clock.Elapsed.TotalMilliseconds;double tiledGpu=Fence(fixture.surface);
                double fullCpu=0,fullGpu=0,fullPreviewCpu=0,fullPreviewGpu=0;
                if(compare)
                {
                    clock.Restart();fixture.FullPaint();fullCpu=clock.Elapsed.TotalMilliseconds;fullGpu=Fence(fixture.reference);
                    clock.Restart();fixture.FullPreview();fullPreviewCpu=clock.Elapsed.TotalMilliseconds;fullPreviewGpu=Fence(fixture.referencePreview);
                }
                clock.Restart();var preview=fixture.Preview();double tiledPreviewCpu=clock.Elapsed.TotalMilliseconds;double tiledPreviewGpu=Fence(preview);
                long bytes=(long)Get(Get(fixture.layer,"advancedStroke"),"Bytes");peak=Math.Max(peak,bytes);
                frames.Add(new{step,tiledCpu,tiledGpu,fullCpu,fullGpu,fullPreviewCpu,fullPreviewGpu,tiledPreviewCpu,tiledPreviewGpu,bytes});
                // A bounded window straddling a tile corner checks pixel continuity, no full 8K readback.
                if(compare && (step%8==0||step==47))
                {
                    Vector2 p=fixture.Point(step);int x=Mathf.Clamp(Mathf.RoundToInt(p.x*side/256)*256-16,0,side-32),y=Mathf.Clamp(Mathf.RoundToInt(p.y*side/256)*256-16,0,side-32);
                    byte[] a=Read(preview,new RectInt(x,y,32,32)),b=Read(fixture.referencePreview,new RectInt(x,y,32,32));
                    // Half numeric comparison allows only the raster coordinate rounding seen at NPOT sizes.
                    var imageA=new Texture2D(32,32,TextureFormat.RGBAHalf,false,true);var imageB=new Texture2D(32,32,TextureFormat.RGBAHalf,false,true);
                    try{imageA.LoadRawTextureData(a);imageB.LoadRawTextureData(b);var ca=imageA.GetPixels();var cb=imageB.GetPixels();double max=0;
                        for(int i=0;i<ca.Length;i++)for(int c=0;c<4;c++)max=Math.Max(max,Math.Abs(ca[i][c]-cb[i][c])/Math.Max(1,Math.Abs(cb[i][c])));
                        test.Near(0,max,.002,"8K seam probe step "+step);
                    }finally{Object.DestroyImmediate(imageA);Object.DestroyImmediate(imageB);}
                }
                step++;next=EditorApplication.timeSinceStartup+.025;EditorApplication.QueuePlayerLoopUpdate();
                if(step==48)Finish(null);
            }
            catch(Exception e){Finish(e);}
            finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;}
        }
        public void Finish(Exception error)
        {
            if(done)return;done=true;EditorApplication.update-=Tick;
            try
            {
                fixture?.Dispose();if(fence!=null)RenderTexture.ReleaseTemporary(fence);if(fenceRead!=null)Object.DestroyImmediate(fenceRead);
                test.Equal(activeBefore,Pool("ActiveBytes"),"No leaked active tiles");test.True(Pool("PooledBytes")<=64L*1024*1024,"Bounded retained pool");
                string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/WhimTex/brush-tiles"));Directory.CreateDirectory(dir);
                report=Path.Combine(dir,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+side+"-"+diameter+"-"+mode+(compare?"-paired":"-isolated")+".json");
                File.WriteAllText(report,Json(new{side,diameter,mode,compare,peak,pooled=Pool("PooledBytes"),fullBytes=side*(long)side*8,frames,error=error?.ToString(),device=SystemInfo.graphicsDeviceName,
                    note="Frame-paced paired accumulation and retained-preview diagnostics with 1x1 GPU fences; generated stamps identical. Reflection/CPU generation is included only in tiledCpu; GPU comparison is fenced queue latency, not presented FPS. No commit/history in timed interval."}));
            }
            catch(Exception e){error=error??e;}
            state[1]=TestContext.Result(error==null?"passed":"failed",test.Checks,"Tile comparison "+report,error==null?Array.Empty<string>():new[]{error.ToString()}).ToJson();
        }
    }
}
