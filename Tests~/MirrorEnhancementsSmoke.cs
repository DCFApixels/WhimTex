using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;

public static class MirrorEnhancementsSmoke
{
    const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static readonly Assembly Assembly=typeof(TextureCompositor).Assembly;
    static void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;}
    static Color[] Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try {RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally {RenderTexture.active=previous;Object.DestroyImmediate(t);}
    }
    static RenderTexture Histogram(Texture t,int x,int y,float width,float falloff,float strength,float start=0)
    {
        var e=new Vector4(x==2?1:0,x==1?1:0,y==2?1:0,y==1?1:0);
        return (RenderTexture)Assembly.GetType("DCFApixels.WhimTex.HistogramSeamless").GetMethod("RenderMirrorTransition",Flags)
            .Invoke(null,new object[]{t,t.width,t.height,e,width,falloff,strength,start});
    }
    static float Weight(int p,int n,int direction,float width,float falloff,float start=0)
    {
        if(direction==0||n==1)return 0;
        float d=direction==1?1-p/(float)(n-1):p/(float)(n-1);
        float q=Mathf.Clamp01((1-d/width)/(1-start));return Mathf.Pow(q*q*(3-2*q),falloff);
    }
    static Color[] Reference(Color[] input,int w,int h,int dx,int dy,float width,float falloff,float start=0)
    {
        var output=new Color[input.Length];
        Color P(Color c) {c.a=Mathf.Clamp01(c.a);return new Color(c.r*c.a,c.g*c.a,c.b*c.a,c.a);}
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            float a=Weight(x,w,dx,width,falloff,start),b=Weight(y,h,dy,width,falloff,start);
            var c=Color.Lerp(Color.Lerp(P(input[y*w+x]),P(input[y*w+w-1-x]),a),
                Color.Lerp(P(input[(h-1-y)*w+x]),P(input[(h-1-y)*w+w-1-x]),a),b);
            if(c.a>1e-8){c.r/=c.a;c.g/=c.a;c.b/=c.a;}else c=Color.clear;
            output[y*w+x]=c;
        }
        return output;
    }
    static void Same(Color[] a,Color[] b,string message,float eps=3e-5f)
    {Check(a.Length==b.Length,message);for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<eps,message+" "+i+" "+c+" actual="+a[i][c]+" expected="+b[i][c]);}
    public static string Main()
    {
        checks=0;
        var defaults=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{}");
        Check(!defaults.mirrorContrastCompensation&&!defaults.mirrorSeamCorrection,"Legacy options off");
        Check(defaults.mirrorContrast==1&&defaults.mirrorCorrectionRadius==.05f,"Parameter defaults");
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(2,3),new Vector2Int(17,13),new Vector2Int(64,48),new Vector2Int(259,257)})
        {
            int w=size.x,h=size.y;var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);
            try
            {
                foreach(bool alpha in new[]{false,true})
                {
                    var pixels=new Color[w*h];var rng=new System.Random(941);
                    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color((float)rng.NextDouble()*1.5f-.2f,(float)rng.NextDouble(),(float)rng.NextDouble(),alpha?(float)rng.NextDouble():1);
                    t.SetPixels(pixels);t.Apply();
                    for(int x=0;x<3;x++)for(int y=0;y<3;y++)
                    {
                        var zero=Histogram(t,x,y,.37f,.7f,0);
                        var full=Histogram(t,x,y,.37f,.7f,1);
                        var half=Histogram(t,x,y,.37f,.7f,.5f);
                        try
                        {
                            var a=Read(zero);var b=Read(full);var c=Read(half);
                            Same(a,Reference(pixels,w,h,x,y,.37f,.7f),"Zero equals independent Mirror",.0001f);
                            for(int i=0;i<b.Length;i++)
                            {
                                float wx=Weight(i%w,w,x,.37f,.7f),wy=Weight(i/w,h,y,.37f,.7f);
                                for(int channel=0;channel<4;channel++)
                                {
                                    Check(!float.IsNaN(b[i][channel])&&!float.IsInfinity(b[i][channel]),"Finite HDR/alpha");
                                    float ap=channel<3?a[i][channel]*a[i].a:a[i].a;
                                    float bp=channel<3?b[i][channel]*b[i].a:b[i].a;
                                    float cp=channel<3?c[i][channel]*c[i].a:c[i].a;
                                    Check(Math.Abs(cp-(ap+bp)*.5f)<.0001f,"Strength interpolates premultiplied result");
                                    if(wx==0&&wy==0)Check(Math.Abs(a[i][channel]-b[i][channel])<.0001f,"Outside unchanged");
                                }
                                Check(b[i].a>=0&&b[i].a<=1,"Alpha bounds");
                            }
                        }
                        finally {RenderTexture.ReleaseTemporary(zero);RenderTexture.ReleaseTemporary(full);RenderTexture.ReleaseTemporary(half);}
                    }
                }
            }
            finally {Object.DestroyImmediate(t);}
        }
        return $"Mirror histogram: {checks} checks passed (independent zero-strength reference, reflection directions, premultiplied strength, HDR/alpha, exterior, tiny/odd/large sizes, old defaults).";
    }
    public static string Transition()
    {
        checks=0;
        Check(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{}").mirrorTransitionStart==0,"Missing field preserves fade");
        var model=new MakeSeamlessLayerBehaviour();
        var set=typeof(WhimTexApi).GetMethod("SetMakeSeamless",Flags);
        var jsonType=set.GetParameters()[1].ParameterType;
        object Parse(string json)=>jsonType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{json});
        set.Invoke(null,new object[]{model,Parse("{\"mirrorTransitionStart\":-0.25}")});
        Check(model.mirrorTransitionStart==-.25f&&model.offsetTransitionStart==.325f,"Independent API value");
        Check(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(model)).mirrorTransitionStart==-.25f,"Serialized value");
        foreach(float invalid in new[]{-1.01f,.96f})
        {
            bool rejected=false;
            try{set.Invoke(null,new object[]{model,Parse("{\"mirrorTransitionStart\":"+invalid.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}")});}
            catch(TargetInvocationException){rejected=true;}
            Check(rejected,"API rejects out of range");
        }
        foreach(var size in new[]{new Vector2Int(1,7),new Vector2Int(17,13),new Vector2Int(64,48)})
        {
            int w=size.x,h=size.y;var texture=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);
            var pixels=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=new Color(-.1f+x*.05f,.3f+y*.03f,.7f,.2f+(x%7)*.1f);
            texture.SetPixels(pixels);texture.Apply();
            try{foreach(float start in new[]{-1f,-.25f,0f,.75f,.95f})
            for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)foreach(float falloff in new[]{.25f,1f,4f})
            {
                var rt=Histogram(texture,dx,dy,.4f,falloff,0,start);
                try{Same(Read(rt),Reference(pixels,w,h,dx,dy,.4f,falloff,start),"Transition reference",.0001f);}
                finally{RenderTexture.ReleaseTemporary(rt);}
                rt=Histogram(texture,dx,dy,.4f,falloff,1,start);
                try{foreach(var p in Read(rt))for(int c=0;c<4;c++)Check(!float.IsNaN(p[c])&&!float.IsInfinity(p[c]),"Compensated transition finite");}
                finally{RenderTexture.ReleaseTemporary(rt);}
            }}finally{Object.DestroyImmediate(texture);}
        }
        return $"Mirror transition: {checks} checks passed (range, serialization, independent reference, directions, falloff, compensation and tiny sizes).";
    }
    public static string Integration()
    {
        checks=0;
        object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,Flags).Invoke(obj,args);
        var document=ScriptableObject.CreateInstance<TextureCompositor>();document.width=32;document.height=16;
        var texture=new Texture2D(32,16,TextureFormat.RGBAFloat,false,true);
        var renderedInput=new Texture2D(32,16,TextureFormat.RGBAFloat,false,true);
        var pixels=new Color[512];
        for(int y=0;y<16;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(.4f+.1f*Mathf.Sin(x*.7f+y*.2f),.2f+x*.01f,.3f+y*.02f,1);
        texture.SetPixels(pixels);texture.Apply();
        var source=new FileLayerBehaviour {sourceTexture=texture,colorRange=LayerColorRange.HDR};
        var effect=new MakeSeamlessLayerBehaviour {colorRange=LayerColorRange.HDR};
        document.layers.Add(effect);document.layers.Add(source);
        var cache=Activator.CreateInstance(Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        var focus=EditorWindow.focusedWindow;EditorWindow window=null;
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
        Color[] Render(string method,params object[] args)
        {
            var before=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var rt=(RenderTexture)Call(document,method,args);
            Check(RenderTexture.active==before&&GL.sRGBWrite==srgb,"Caller render state");
            try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}
        }
        try
        {
            Call(document,"NormalizeModel");
            renderedInput.SetPixels(Render("RenderLayerPreview",source.Owner,32));renderedInput.Apply();
            foreach(float start in new[]{-1f,-.25f,0f,.75f,.95f})
            for(int direction=0;direction<3;direction++)for(int option=0;option<4;option++)
            {
                effect.mirrorTransitionStart=start;
                effect.mirrorPoissonEdges=(MakeSeamlessLayerBehaviour.PoissonEdges)direction;
                effect.mirrorContrastCompensation=(option&1)!=0;effect.mirrorSeamCorrection=(option&2)!=0;
                effect.mirrorContrast=.65f;effect.mirrorCorrectionRadius=.09f;
                var direct=Histogram(renderedInput,1,1,effect.blendWidth,effect.falloff,effect.mirrorContrastCompensation?effect.mirrorContrast:0,start);
                if(effect.mirrorSeamCorrection)
                {
                    var corrected=(RenderTexture)Assembly.GetType("DCFApixels.WhimTex.ScreenedSeamless").GetMethod("RenderConfigured",Flags)
                        .Invoke(null,new object[]{direct,32,16,effect.mirrorPoissonEdges,effect.mirrorCorrectionRadius});
                    RenderTexture.ReleaseTemporary(direct);direct=corrected;
                }
                Color[] expected;
                try {expected=Read(direct);}finally {RenderTexture.ReleaseTemporary(direct);}
                Same(Render("RenderLayerPreview",effect.Owner,32),expected,"Layer "+option,.003f);
                Same(Render("RenderThumbnailLayer",effect.Owner,32,cache),expected,"Thumbnail",.003f);
                source.enabled=false;
                Same(Render("RenderAllLayers",32,16),expected,"Export",.003f);
                Same(Render("RenderCachedPreview",32,cache,false,null),expected,"Settings invalidate cache",.003f);
                Same(Render("RenderCachedPreview",32,cache,false,null),expected,"Cache hit",.003f);
                source.enabled=true;
            }
            var group=new GroupLayerBehaviour {compositing=GroupCompositing.Isolated};
            document.layers.Remove(source.Owner);group.layers.Add(source);document.layers.Add(group);effect.TargetLayerId=group.Id;
            Call(document,"NormalizeModel");
            var grouped=Render("RenderLayerPreview",effect.Owner,32);
            effect.clippingMask=true;Same(Render("RenderLayerPreview",effect.Owner,32),grouped,"Opaque clipping",.003f);effect.clippingMask=false;
            effect.horizontal=MakeSeamlessLayerBehaviour.HorizontalDirection.Off;effect.vertical=MakeSeamlessLayerBehaviour.VerticalDirection.Off;
            effect.mirrorSeamCorrection=false;
            Same(Render("RenderLayerPreview",effect.Owner,32),pixels,"Both axes off bypass",.003f);
            effect.mirrorSeamCorrection=true;
            var onlyCorrection=(RenderTexture)Assembly.GetType("DCFApixels.WhimTex.ScreenedSeamless").GetMethod("RenderConfigured",Flags)
                .Invoke(null,new object[]{renderedInput,32,16,effect.mirrorPoissonEdges,effect.mirrorCorrectionRadius});
            try{Same(Render("RenderLayerPreview",effect.Owner,32),Read(onlyCorrection),"Correction independent of mirror directions",.003f);}
            finally{RenderTexture.ReleaseTemporary(onlyCorrection);}
            var json=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",Flags).Invoke(null,new object[]{document,document.layers});
            var clipboard=typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",Flags).Invoke(null,new object[]{json,32,16});
            try
            {
                var copy=(TextureCompositor)clipboard.GetType().GetField("Document",Flags).GetValue(clipboard);
                var saved=(MakeSeamlessLayerBehaviour)copy.layers[0].Behaviour;
                Check(saved.mirrorTransitionStart==effect.mirrorTransitionStart,"Portable transition start");
                Check(saved.mirrorContrastCompensation&&saved.mirrorSeamCorrection&&saved.mirrorContrast==.65f&&saved.mirrorCorrectionRadius==.09f,"Portable controls");
            }
            finally {((IDisposable)clipboard).Dispose();}
            window=ScriptableObject.CreateInstance<EditorWindow>();window.titleContent=new GUIContent("Mirror verification");window.Show();
            var bindings=Activator.CreateInstance(Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            Action<string,Action> apply=(name,change)=>{Undo.RegisterCompleteObjectUndo(document,name);change();EditorUtility.SetDirty(document);Call(bindings,"Refresh",true);};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",Flags).Invoke(null,new object[]{window.rootVisualElement,effect,document,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var root=window.rootVisualElement;
            var transition=root.Q<Slider>("mirrorTransitionStart");
            Check(transition!=null&&transition.lowValue==-100&&transition.highValue==95,"Transition range");
            transition.value=-25;
            Check(effect.mirrorTransitionStart==-.25f,"Transition UI percent conversion");
            root.Q<Toggle>("mirrorContrastCompensation").value=false;
            root.Q<Toggle>("mirrorSeamCorrection").value=false;
            Check(!effect.mirrorContrastCompensation&&!effect.mirrorSeamCorrection,"Toggle callbacks");
            foreach(var slider in root.Query<Slider>().ToList())
            {
                if(slider.name=="mirrorCompensation") {Check(slider.ClassListContains("whimtex-hidden"),"Strength hidden");slider.value=25;}
                if(slider.name=="mirrorCorrectionRadius") {Check(slider.ClassListContains("whimtex-hidden"),"Radius hidden");slider.value=12;}
            }
            Check(effect.mirrorContrast==.25f&&Math.Abs(effect.mirrorCorrectionRadius-.12f)<1e-6,"Slider units");
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            var undone=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;Check(undone.mirrorContrastCompensation&&undone.mirrorSeamCorrection,"Undo");
            Undo.PerformRedo();var redone=(MakeSeamlessLayerBehaviour)document.layers[0].Behaviour;
            Check(!redone.mirrorContrastCompensation&&!redone.mirrorSeamCorrection&&redone.mirrorContrast==.25f&&redone.mirrorTransitionStart==-.25f,"Redo");
            return $"Mirror integration: {checks} checks passed (all option combinations, render paths, cache, groups, clipping, bypass, Portable, UI, Undo/Redo, render state).";
        }
        catch(TargetInvocationException ex){throw new Exception(ex.InnerException?.ToString()??ex.ToString());}
        finally
        {
            if(window!=null)window.Close();if(focus!=null)focus.Focus();
            ((IDisposable)cache).Dispose();Undo.RevertAllDownToGroup(undo);Object.DestroyImmediate(document);Object.DestroyImmediate(texture);Object.DestroyImmediate(renderedInput);
        }
    }
    public static string Preview()
    {
        string dir=Path.GetFullPath("output/mirror-enhancements-2026-09-28");Directory.CreateDirectory(dir);
        var t=new Texture2D(2,2,TextureFormat.RGBA32,false,false);var material=new Material(Shader.Find("Hidden/TextureCompositor/MakeSeamless"));
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            t.LoadImage(File.ReadAllBytes("output/copy-blend-lab-2026-09-28/00_source.png"));
            GL.sRGBWrite=false;
            for(int option=0;option<4;option++)
            {
                RenderTexture result=null;
                try
                {
                    if((option&1)!=0)result=Histogram(t,1,1,.2f,1,1);
                    else
                    {
                        result=RenderTexture.GetTemporary(t.width,t.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                        material.SetVector("_Directions",new Vector4(1,1,0,0));material.SetFloat("_BlendWidth",.2f);material.SetFloat("_Falloff",1);Graphics.Blit(t,result,material);
                    }
                    if((option&2)!=0)
                    {
                        var corrected=(RenderTexture)Assembly.GetType("DCFApixels.WhimTex.ScreenedSeamless").GetMethod("RenderConfigured",Flags)
                            .Invoke(null,new object[]{result,t.width,t.height,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,.05f});
                        RenderTexture.ReleaseTemporary(result);result=corrected;
                    }
                    var pixels=Read(result);var png=new Texture2D(t.width,t.height,TextureFormat.RGBA32,false,true);
                    try {for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;png.SetPixels(pixels);png.Apply();File.WriteAllBytes(Path.Combine(dir,option+".png"),png.EncodeToPNG());}
                    finally {Object.DestroyImmediate(png);}
                }
                finally {if(result!=null)RenderTexture.ReleaseTemporary(result);}
            }
        }
        finally {RenderTexture.active=previous;GL.sRGBWrite=srgb;Object.DestroyImmediate(material);Object.DestroyImmediate(t);}
        return dir;
    }
}
