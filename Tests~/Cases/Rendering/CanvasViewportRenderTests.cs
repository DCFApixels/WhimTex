using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class CanvasViewportRenderTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type Type(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static object Region(RectInt rect, int step = 1) => Activator.CreateInstance(Type("CanvasRenderRegion"), F, null, new object[] { rect, step }, null);
    static Color[] Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); cpu.Apply(false, false); return cpu.GetPixels(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
    }
    static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a));
    static VectorLayerBehaviour Vector()
    {
        var vector = new VectorLayerBehaviour { referenceSize = new Vector2(512, 384) };
        var shape = new VectorShape(); var contour = new VectorContour();
        foreach (var p in new[] {new Vector2(.1f,.2f), new Vector2(.9f,.3f), new Vector2(.8f,.8f), new Vector2(.2f,.9f)})
            contour.points.Add(new VectorPoint(p));
        shape.contours.Add(contour); vector.shapes.Add(shape); return vector;
    }
    public static string Run() => TestContext.Run("Viewport composition, pixel grid and full-frame isolation", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 512; doc.height = 384;
        var cache = (IDisposable)Activator.CreateInstance(Type("EffectRenderCache"), true);
        var source = new Texture2D(512,384,TextureFormat.RGBAHalf,false,true) { hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color[512*384];
        for (int y=0;y<384;y++) for(int x=0;x<512;x++) pixels[y*512+x] = new Color((x%31)*.13f-.5f,(y%17)*.11f, .7f, (x%11)==0 ? 0 : (x%7)==0 ? .0001f : .73f);
        source.SetPixels(pixels); source.Apply(false,false);
        var before = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            foreach (var behaviour in new LayerBehaviour[] { new FileLayerBehaviour {sourceTexture=source},
                new ShapeLayerBehaviour(), new ShapeLayerBehaviour {edgeMode=ShapeLayerBehaviour.EdgeMode.Step}, new NoiseLayerBehaviour(),
                new ColorFillLayerBehaviour {mode=ColorFillLayerBehaviour.FillMode.UV}, Vector(),
                new TextLayerBehaviour {text="Viewport 64",fontSize=64,fontFamily="Arial"} })
            {
                var layer = new Layer(behaviour); doc.layers.Add(layer);
                if (behaviour is FileLayerBehaviour) {layer.colorRange=LayerColorRange.HDR;layer.blendRange=LayerBlendRange.HDR;}
                Call(doc,"NormalizeModel");
                t.True((bool)Call(doc,"SupportsViewportRendering"), behaviour.GetType().Name + " supported");
                foreach (bool transform in new[] {false,true})
                {
                    if(transform) {layer.transform.position = new Vector2(.03f,-.04f);layer.transform.scale=new Vector2(.83f,1.13f);layer.transform.rotation=17;}
                    // Hardware bilinear interpolation quantizes the fractional coordinate;
                    // the HDR test has ~4-unit discontinuities across individual texels.
                    Compare(t,doc,cache,behaviour.GetType().Name + "/" + transform,
                        transform && behaviour is FileLayerBehaviour ? .016f : transform && behaviour is TextLayerBehaviour ? 1f/255 : .001f);
                }
                Compare(t,doc,cache,behaviour.GetType().Name+" reduced",behaviour is FileLayerBehaviour ? .016f : behaviour is TextLayerBehaviour ? 1f/255 : .001f,2);
                doc.layers.Clear(); Call(behaviour,"ReleaseTransientResources");
            }
            foreach(GradientLayerBehaviour.GradientType kind in Enum.GetValues(typeof(GradientLayerBehaviour.GradientType)))
            {
                var gradient = new GradientLayerBehaviour {gradientType=kind};
                doc.layers.Add(new Layer(gradient));Call(doc,"NormalizeModel");
                Compare(t,doc,cache,"Gradient "+kind,.001f);
                doc.layers.Clear();Call(gradient,"ReleaseTransientResources");
            }
            var drawing = new DrawingLayerBehaviour();
            var drawingLayer = new Layer(drawing) {colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
            var owned = Object.Instantiate(source); Call(drawing,"AdoptStoredTexture",owned);
            doc.layers.Add(drawingLayer); Call(doc,"NormalizeModel");
            Compare(t,doc,cache,"Drawing Half + hidden RGB + low alpha",.004f);
            var settings=Activator.CreateInstance(Type("PaintToolSettings"),true);
            settings.GetType().GetField("brushSize",F).SetValue(settings,81f);
            var parameters=Call(settings,"GetStrokeParameters",false,(Color?)null);
            var mask=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            mask.SetPixels(new[]{new Color(.25f,0,0,1),new Color(.5f,0,0,1),new Color(.75f,0,0,1),Color.red});mask.Apply(false,false);
            parameters=Call(parameters,"WithSelectionMask",mask);
            try
            {
                Call(drawing,"BeginStroke",new Vector2(.5f,.5f));
                Call(drawing,"PaintPoint",new Vector2(.5f,.5f),512,384,parameters);
                Compare(t,doc,cache,"Drawing active masked stroke HDR",.004f);
            }
            finally {Call(drawing,"CancelStroke");Call(settings,"ReleasePresetTip");Object.DestroyImmediate(mask);}
            doc.layers.Clear(); Call(drawing,"ReleaseTransientResources"); Object.DestroyImmediate(owned);

            var boolean=new Layer(new BooleanGroupLayerBehaviour());
            boolean.children.Add(new Layer(new ShapeLayerBehaviour()));
            doc.layers.Add(boolean);Call(doc,"NormalizeModel");
            Compare(t,doc,cache,"Boolean region",.001f);
            doc.layers.Clear();Call(boolean.Behaviour,"ReleaseTransientResources");

            var group = new Layer(new GroupLayerBehaviour());
            group.children.Add(new Layer(new ShapeLayerBehaviour()));
            var clipped = new Layer(new GradientLayerBehaviour()) {clippingMask=true, opacity=.64f}; group.children.Insert(0,clipped);
            doc.layers.Add(group); Call(doc,"NormalizeModel");
            Compare(t,doc,cache,"Group/clipping",.001f);
            group.transform.rotation=23;group.transform.scale=new Vector2(.8f,1.1f);
            Compare(t,doc,cache,"Transformed group/clipping",.002f);
            group.children.Add(new Layer(new BlurLayerBehaviour()));
            t.True((bool)Call(doc,"SupportsViewportRendering"),"Input effects preserve regional composition");
            Compare(t,doc,cache,"Group with spatial input effect",.003f);
            group.children.RemoveAt(group.children.Count-1);
            var fx = ScriptableObject.CreateInstance<ShaderFX>();
            try {group.fx.Add(fx);t.True((bool)Call(doc,"SupportsViewportRendering"),"Unavailable FX do not disable regions");}
            finally {group.fx.Clear();Object.DestroyImmediate(fx);}

            var plan = Type("CanvasRenderRegion").GetMethod("Plan",F);
            foreach (float zoom in new[] {.05f,.125f,.25f,1f,2f,16f})
            {
                var value=plan.Invoke(null,new object[]{new Rect(0,0,1200,800),new Rect(-3500*zoom,-3500*zoom,8192*zoom,8192*zoom),8192,8192,1f,1f,16384});
                int step=(int)value.GetType().GetField("step",F).GetValue(value);
                var rect=(RectInt)value.GetType().GetField("pixels",F).GetValue(value);
                t.True(zoom<1 || step==1,"Native source grid when zoomed in");
                t.True(rect.width/step<=2464 && rect.height/step<=1664,"Output bounded by visible area");
                t.Equal(0,rect.x%step,"Pixel aligned x");t.Equal(0,rect.y%step,"Pixel aligned y");
            }
            t.True(!((bool)doc.GetType().GetProperty("RenderingViewport",F).GetValue(doc)),"Viewport scope restored");
            t.True(RenderTexture.active==before && GL.sRGBWrite==srgb,"Caller render state restored");
            FxRegions(t);
            NativeText(t);
            RegionalStroke(t);
        }
        finally {cache.Dispose();Object.DestroyImmediate(doc);Object.DestroyImmediate(source);RenderTexture.active=before;GL.sRGBWrite=srgb;}
    });
    static void RegionalStroke(TestContext t)
    {
        const int w=1031,h=773;
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=w;doc.height=h;
        var cache=(IDisposable)Activator.CreateInstance(Type("EffectRenderCache"),true);
        var fx=Fx(doc,"float4 ApplyFX(PinnedUV uv,float4 color){return SampleInput(uv)*float4(.5,.75,1,1);}");
        var mask=new Texture2D(4,4,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};
        var masks=new Color[16];for(int i=0;i<16;i++)masks[i]=new Color(i/15f,0,0,1);mask.SetPixels(masks);mask.Apply();
        try
        {
            foreach(string mode in new[]{"hdr","multiply","stamp","erase","locked","point"})
            {
                var drawing=new DrawingLayerBehaviour();
                var layer=new Layer(drawing){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR,
                    filterMode=mode=="point"?LayerFilterMode.Point:LayerFilterMode.Bilinear};
                layer.fx.Add(fx);
                var source=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);
                var colors=new Color[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)colors[y*w+x]=new Color(-.3f,2.4f,x/(float)w,x%9==0?0:x%11==0?.0001f:.3f);
                source.SetPixels(colors);source.Apply();Call(drawing,"AdoptStoredTexture",source);
                doc.layers.Add(layer);Call(doc,"NormalizeModel");
                var settings=Activator.CreateInstance(Type("PaintToolSettings"),true);
                var dynamics=settings.GetType().GetField("dynamics",F).GetValue(settings);
                settings.GetType().GetField("brushSize",F).SetValue(settings,420f);
                settings.GetType().GetField("brushColor",F).SetValue(settings,new Color(3.5f,-.2f,.7f,.013f));
                dynamics.GetType().GetField("opacity",F).SetValue(dynamics,.47f);
                dynamics.GetType().GetField("flow",F).SetValue(dynamics,.35f);
                if(mode=="multiply"||mode=="stamp")dynamics.GetType().GetField("blend",F).SetValue(dynamics,BlendMode.Multiply);
                if(mode=="stamp") {var field=dynamics.GetType().GetField("blendApplication",F);field.SetValue(dynamics,Enum.Parse(field.FieldType,"Stamp"));}
                var parameters=Call(settings,"GetStrokeParameters",mode=="erase",(Color?)null);
                parameters=Call(parameters,"WithSelectionMask",mask);
                if(mode=="locked")parameters=Call(parameters,"WithWriteProtection",5);
                try
                {
                    Call(drawing,"BeginStroke",new Vector2(.2f,.2f));
                    // Allocate across more than one atlas page, including partial edge tiles.
                    for(int y=0;y<3;y++)for(int x=0;x<4;x++)Call(drawing,"PaintPoint",new Vector2((x+.5f)/4,(y+.5f)/3),w,h,parameters);
                    foreach(bool transformed in new[]{false,true})
                    {
                        if(transformed){layer.transform.rotation=13;layer.transform.scale=new Vector2(.93f,1.07f);}
                        var full=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);Color[] expected;
                        try{Call(drawing,"BlitStraightSurface",full,1f);expected=Read(full);}finally{RenderTexture.ReleaseTemporary(full);}
                        object frame=typeof(Layer).GetProperty("RenderTransform",F).GetValue(layer);
                        var matrix=(ProjectiveMatrix)Call(frame,"ToMatrix",w,h);matrix.TryInverse(out var inverse);
                        var rect=new RectInt(192,160,512,384);
                        var region=Region(rect);
                        var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
                        try
                        {
                            var actual=Read(rt);float error=0;
                            for(int y=0;y<rt.height;y++)for(int x=0;x<rt.width;x++)
                            {
                                var uv=inverse.Point(new Double2((rect.x+x+.5)/w,(rect.y+y+.5)/h));
                                Color reference=Sample(expected,w,h,uv,mode=="point"); reference.r*=.5f; reference.g*=.75f;
                                var color=actual[y*rt.width+x];
                                // Compare visible values against independently filtered full native texels.
                                // Hardware filter weights in the old full path are quantized, so they
                                // are not an exact oracle for large HDR discontinuities.
                                for(int channel=0;channel<4;channel++)
                                {float a=channel==3?color.a:color[channel]*color.a;float b=channel==3?reference.a:reference[channel]*reference.a;
                                 error=Mathf.Max(error,Mathf.Abs(a-b)/Mathf.Max(1,Mathf.Abs(b)));}
                            }
                            t.True(error<.003f,mode+" regional taps against CPU-filtered native reference; transformed="+transformed+" error="+error);
                        }finally{RenderTexture.ReleaseTemporary(rt);}
                        var preview=(RenderTexture)drawing.GetType().GetField("brushPreviewSurface",F).GetValue(drawing);
                        t.True(preview.width==512&&preview.height==384,"Brush cache is output-sized");
                        for(int k=0;k<3;k++)
                        {
                            Call(drawing,"PaintPoint",new Vector2((256.2f+k*130)/w,256.4f/h),w,h,parameters);
                            rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);Color[] incremental;
                            try{incremental=Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}
                            t.True(ReferenceEquals(preview,drawing.GetType().GetField("brushPreviewSurface",F).GetValue(drawing)),"Incremental update keeps cache allocation");
                            drawing.GetType().GetField("brushPreviewValid",F).SetValue(drawing,false);
                            rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
                            try{var rebuilt=Read(rt);float error=0;for(int i=0;i<rebuilt.Length;i++)error=Mathf.Max(error,Difference(rebuilt[i],incremental[i]));t.True(error==0,mode+" incremental equals rebuilt exactly; error="+error);}
                            finally{RenderTexture.ReleaseTemporary(rt);}
                        }
                        // Reduced full-view output also stays regional rather than becoming a full source cache.
                        rt=(RenderTexture)Call(doc,"RenderCanvasViewport",Region(new RectInt(0,0,1024,768),2),cache,false,null);
                        RenderTexture.ReleaseTemporary(rt);
                        preview=(RenderTexture)drawing.GetType().GetField("brushPreviewSurface",F).GetValue(drawing);
                        t.True(preview.width==512&&preview.height==384,"Fit view keeps reduced brush cache");
                    }
                }
                finally{Call(drawing,"CancelStroke");Call(settings,"ReleasePresetTip");doc.layers.Clear();Call(drawing,"ReleaseTransientResources");Object.DestroyImmediate(source);}
            }
        }
        finally{cache.Dispose();Object.DestroyImmediate(mask);Object.DestroyImmediate(fx);Object.DestroyImmediate(doc);}
    }
    static Color Sample(Color[] image,int w,int h,Double2 uv,bool point)
    {
        if(uv.x<0||uv.x>1||uv.y<0||uv.y>1)return Color.clear;
        Color Fetch(int x,int y)=>image[Mathf.Clamp(y,0,h-1)*w+Mathf.Clamp(x,0,w-1)];
        if(point)return Fetch((int)(uv.x*w),(int)(uv.y*h));
        double px=uv.x*w-.5,py=uv.y*h-.5;int ix=(int)Math.Floor(px),iy=(int)Math.Floor(py);
        return Color.LerpUnclamped(Color.LerpUnclamped(Fetch(ix,iy),Fetch(ix+1,iy),(float)(px-ix)),
            Color.LerpUnclamped(Fetch(ix,iy+1),Fetch(ix+1,iy+1),(float)(px-ix)),(float)(py-iy));
    }
    static ShaderFX Fx(WhimTexDocument doc,string code)
    {
        var method=typeof(ShaderFX).GetMethod("CreateAgentDraft",F,null,new[]{typeof(WhimTexDocument),typeof(string),typeof(System.Collections.Generic.List<ShaderFXParameter>),typeof(string)},null);
        var fx=(ShaderFX)method.Invoke(null,new object[]{doc,code,new System.Collections.Generic.List<ShaderFXParameter>(),null});
        Call(fx,"ApplyAgentDraft");return fx;
    }
    static void FxRegions(TestContext t)
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=512;doc.height=384;
        var cache=(IDisposable)Activator.CreateInstance(Type("EffectRenderCache"),true);
        var point=Fx(doc,"float4 ApplyFX(float2 uv,float4 color){return float4(color.rgb * float3(uv,1) + _RenderScale/_InputSize.x, color.a);}");
        var spatial=Fx(doc,"float4 ReadNeighbour(float2 uv){return SampleInput(uv+float2(17.0/_InputSize.x,0));} float4 ApplyFX(float2 uv,float4 color){return ReadNeighbour(uv);}");
        var hsv=Fx(doc,File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl"));
        try
        {
            t.True((bool)typeof(ShaderFX).GetProperty("SupportsRegionalColorInput",F).GetValue(point),"Pointwise UV effect supports regional input");
            t.True((bool)typeof(ShaderFX).GetProperty("SupportsRegionalColorInput",F).GetValue(hsv),"HSV supports regional input despite unused preview texture helper");
            t.True(!(bool)typeof(ShaderFX).GetProperty("SupportsRegionalColorInput",F).GetValue(spatial),"Neighbour reads in helper require full input");
            var previewInput=Fx(doc,"float4 Preview(float2 uv){return SampleInput(uv);} float4 ApplyFX(float2 uv,float4 color){return color;}");
            try{t.True(!(bool)typeof(ShaderFX).GetProperty("SupportsRegionalColorInput",F).GetValue(previewInput),"Input-reading preview/helper keeps complete input");}
            finally{Object.DestroyImmediate(previewInput);}
            var layer=new Layer(new GradientLayerBehaviour());doc.layers.Add(layer);layer.fx.Add(point);Call(doc,"NormalizeModel");
            Compare(t,doc,cache,"Pointwise FX coordinates/cache pan",.002f);
            Compare(t,doc,cache,"Pointwise FX reduced",.002f,2);
            var view=Region(new RectInt(64,32,192,224));
            object Entry(){foreach(System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)cache.GetType().GetField("entries",F).GetValue(cache))
                if(((string)entry.Key).StartsWith(layer.Id+"/effect/viewport")) return entry.Value;return null;}
            var first=(RenderTexture)Call(doc,"RenderCanvasViewport",view,cache,false,null);RenderTexture.ReleaseTemporary(first);var held=Entry();
            var second=(RenderTexture)Call(doc,"RenderCanvasViewport",view,cache,false,null);RenderTexture.ReleaseTemporary(second);
            t.True(held!=null && ReferenceEquals(held,Entry()),"Unchanged region reuses retained FX entry across frames");
            var sourceField=typeof(ShaderFX).GetField("appliedSource",F);var codeField=typeof(ShaderFX).GetField("code",F);
            string marker=(string)Type("ShaderFXSourceBuilder").GetField("ViewportWrapperMarker",F).GetRawConstantValue();
            t.True(((string)sourceField.GetValue(point)).Contains(marker),"Applied source has the current wrapper marker");
            sourceField.SetValue(point,((string)sourceField.GetValue(point)).Replace(marker, "// stale generated wrapper\n"));
            string draft=(string)codeField.GetValue(point);codeField.SetValue(point,"Pending, deliberately uncompiled source");
            Call(point,"RestoreDocumentShader");
            t.Equal("Pending, deliberately uncompiled source",(string)codeField.GetValue(point),"Wrapper restoration preserves pending code");
            t.True((bool)typeof(ShaderFX).GetProperty("SupportsRegionalColorInput",F).GetValue(point),"Wrapper restoration retains regional capability");
            codeField.SetValue(point,draft);
            layer.fx.Insert(0,spatial);Compare(t,doc,cache,"Spatial prefix and regional suffix",.003f);
            layer.fx.Reverse();Compare(t,doc,cache,"Regional color preceding spatial FX",.003f);
            spatial.Active=false;Compare(t,doc,cache,"Disabled spatial FX",.002f);spatial.Active=true;
            var group=new Layer(new GroupLayerBehaviour());doc.layers.Clear();doc.layers.Add(group);group.children.Add(layer);
            group.fx.Add(spatial);group.fx.Add(point);Call(doc,"NormalizeModel");Compare(t,doc,cache,"Group spatial prefix",.004f);
            group.clippingMask=true;doc.layers.Add(new Layer(new ShapeLayerBehaviour()));Call(doc,"NormalizeModel");Compare(t,doc,cache,"Spatial group clipping",.004f);
            group.clippingMask=false;group.fx.Clear();layer.fx.Clear();
            var target=new Layer(new BlurLayerBehaviour {radius=3, inputMode=EffectInputMode.Specific, TargetLayerId=group.Id});
            target.fx.Add(point);doc.layers.Insert(0,target);Call(doc,"NormalizeModel");
            Compare(t,doc,cache,"Specific Target group input and regional color FX",.004f);
            doc.layers.Remove(target);Call(target.Behaviour,"ReleaseTransientResources");
            var processor=new Layer(new ShaderProcessorLayerBehaviour());processor.fx.Add(spatial);processor.fx.Add(point);doc.layers.Insert(0,processor);
            Call(doc,"NormalizeModel");Compare(t,doc,cache,"Processor full prefix retains surrounding detail",.004f);
        }
        finally {cache.Dispose();Object.DestroyImmediate(point);Object.DestroyImmediate(spatial);Object.DestroyImmediate(hsv);Object.DestroyImmediate(doc);}
    }
    static void NativeText(TestContext t)
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=8192;
        var text=new TextLayerBehaviour{text="Native 8K",fontSize=64,fontFamily="Arial"};doc.layers.Add(new Layer(text));
        var hsv=Fx(doc,File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/HueSaturation.hlsl"));
        var background=new Layer(new ColorFillLayerBehaviour());background.fx.Add(hsv);background.opacity=0.01f;doc.layers.Add(background);
        var cache=(IDisposable)Activator.CreateInstance(Type("EffectRenderCache"),true);
        RenderTexture rt=null; Texture2D png=null;var previous=RenderTexture.active;
        try
        {
            Call(doc,"NormalizeModel");rt=(RenderTexture)Call(doc,"RenderCanvasViewport",Region(new RectInt(3584,3712,1024,768)),cache,false,null);
            t.Equal(1024,rt.width,"8K canvas renders only 1024px region");
            var pixels=Read(rt);int rows=0;
            for(int y=0;y<rt.height;y++){bool ink=false;for(int x=0;x<rt.width;x++)ink|=pixels[y*rt.width+x].a>.1f;if(ink)rows++;}
            t.True(rows>=35 && rows<=80,"64px text retains native detail; ink rows="+rows);
            png=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,true);
            RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);png.Apply(false,false);
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/WhimTex/viewport"));Directory.CreateDirectory(folder);
            var display=png.GetPixels();
            for(int i=0;i<display.Length;i++){display[i]=Color.Lerp(new Color(.15f,.15f,.15f,1),display[i],display[i].a);display[i].a=1;}
            png.SetPixels(display);png.Apply(false,false);
            File.WriteAllBytes(Path.Combine(folder,"native-text.png"),png.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;if(png!=null)Object.DestroyImmediate(png);if(rt!=null)RenderTexture.ReleaseTemporary(rt);cache.Dispose();Object.DestroyImmediate(hsv);Object.DestroyImmediate(doc);}
    }
    static void Compare(TestContext t,WhimTexDocument doc,object cache,string label,float tolerance,int step=1)
    {
        var full=(RenderTexture)Call(doc,"RenderCanvas",512/step);
        Color[] expected;
        try {expected=Read(full);} finally {RenderTexture.ReleaseTemporary(full);}
        foreach(var rect in new[]{new RectInt(64,32,192,224),new RectInt(192,128,192,224),new RectInt(0,0,96,96)})
        {
            var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",Region(rect,step),cache,false,null);
            try
            {
                int w=rect.width/step,h=rect.height/step,fullWidth=doc.width/step;
                t.Equal(w,rt.width,label+" cropped width");
                var actual=Read(rt);float maximum=0; int worst=0; bool raw=label.StartsWith("File") || label.StartsWith("Drawing");
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                    { Color a=actual[y*w+x], b=expected[(rect.y/step+y)*fullWidth+rect.x/step+x]; if(!raw){a=new Color(a.r*a.a,a.g*a.a,a.b*a.a,a.a);b=new Color(b.r*b.a,b.g*b.a,b.b*b.a,b.a);} float error=Difference(a,b); if(error>maximum){maximum=error;worst=y*w+x;} }
                t.True(maximum<=tolerance,label+" matches full frame crop; max="+maximum+" rect="+rect+" at="+worst+" actual="+actual[worst]+" expected="+expected[(rect.y/step+worst/w)*fullWidth+rect.x/step+worst%w]);
                t.True(doc.GetType().GetField("lastLayerPreviewCache",F).GetValue(doc)==null,"Region does not become a thumbnail source");
            }
            finally {RenderTexture.ReleaseTemporary(rt);}
        }
        var again=(RenderTexture)Call(doc,"RenderCanvas",512/step);
        try {var actual=Read(again);float maximum=0;for(int i=0;i<actual.Length;i++)maximum=Mathf.Max(maximum,Difference(actual[i],expected[i]));t.True(maximum<=tolerance,label+" full frame unchanged after viewport; max="+maximum);}
        finally {RenderTexture.ReleaseTemporary(again);}
    }
}
