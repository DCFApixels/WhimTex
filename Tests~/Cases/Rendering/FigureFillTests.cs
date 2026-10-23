using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class FigureFillTests
{
    // Use the product PNG path: composed/readback textures contain linear float pixels.
    static byte[] EncodePng(UnityEngine.Texture2D texture)
    {
        var assembly = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly;
        var encoder = assembly.GetType("DCFApixels.WhimTex.WhimTexRasterEncoder", true);
        var format = System.Enum.Parse(assembly.GetType("DCFApixels.WhimTex.RasterImageFormat", true), "Png");
        return (byte[])encoder.GetMethod("Encode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { texture, format, 95, UnityEngine.Texture2D.EXRFlags.CompressZIP });
    }

    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly VectorFillMode[] Modes = { VectorFillMode.LinearGradient, VectorFillMode.RadialGradient,
        VectorFillMode.SquareGradient, VectorFillMode.AngularGradient };
    public static string Run() => TestContext.Run("Shared Shape/Vector gradient fills", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 80; doc.height = 60; doc.outputPrecision = WhimTexOutputPrecision.Float32;
        var shape = new ShapeLayerBehaviour { fillMode = VectorFillMode.LinearGradient, edgeMode = ShapeLayerBehaviour.EdgeMode.Step,
            gradientStart = new Vector2(.413f,.457f), gradientEnd = new Vector2(.783f,.621f) };
        var layer = new Layer(shape) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        layer.transform.scaleF = Vector2.one; doc.layers.Add(layer);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D a = null, b = null;
        try
        {
            foreach (var mode in Modes)
            foreach (WhimTexGradientMode interpolation in Enum.GetValues(typeof(WhimTexGradientMode)))
            foreach (WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
            foreach (ColorSpace space in new[] { ColorSpace.Linear, ColorSpace.Gamma })
            {
                shape.fillMode = mode; shape.gradient = Ramp(); shape.gradient.Mode = interpolation;
                shape.gradient.WrapMode = wrap; shape.gradient.ColorSpace = space;
                layer.SetBehaviour(shape); a = doc.ComposeCanvas();
                var vector = ShapeToVector.Convert(shape, doc);
                t.True(!ReferenceEquals(shape.gradient, vector.shapes[0].gradient) && shape.gradient.Equals(vector.shapes[0].gradient), "Conversion clones whole gradient");
                layer.SetBehaviour(vector); b = doc.ComposeCanvas();
                string label = mode + "/" + interpolation + "/" + wrap + "/" + space;
                for (int y = 2; y < doc.height - 2; y += 7) for (int x = 2; x < doc.width - 2; x += 7)
                {
                    double coordinate = Coordinate(mode, new Vector2((x+.5f)/doc.width,(y+.5f)/doc.height), shape.gradientStart, shape.gradientEnd, new Vector2(doc.width,doc.height));
                    Color expected = shape.gradient.Evaluate((float)coordinate);
                    if (space == ColorSpace.Gamma) expected = Decode(expected);
                    Near(t,expected,a.GetPixel(x,y),.012f,label+" GPU oracle");
                    Near(t,expected,b.GetPixel(x,y),.003f,label+" CPU oracle");
                }
                Object.DestroyImmediate(a); a = null; Object.DestroyImmediate(b); b = null;
            }
            shape.gradient = new WhimTexGradient { Mode=WhimTexGradientMode.Classic, ColorSpace=ColorSpace.Linear };
            shape.gradient.SetKeys(new[]{new GradientColorKey(new Color(-.5f,2f,.2f),0),new GradientColorKey(new Color(3f,-.2f,1.2f),1)},
                new[]{new GradientAlphaKey(.2f,0),new GradientAlphaKey(.8f,1)});
            foreach(var mode in Modes)
            {
                shape.fillMode=mode; layer.SetBehaviour(shape); a=doc.ComposeCanvas();
                layer.SetBehaviour(ShapeToVector.Convert(shape,doc)); b=doc.ComposeCanvas();
                for(int y=4;y<60;y+=11)for(int x=4;x<80;x+=11)
                {
                    var expected=shape.gradient.Evaluate((float)Coordinate(mode,new Vector2((x+.5f)/80,(y+.5f)/60),shape.gradientStart,shape.gradientEnd,new Vector2(80,60)));
                    Near(t,expected,a.GetPixel(x,y),.004f,"Signed HDR Shape "+mode);
                    Near(t,expected,b.GetPixel(x,y),.004f,"Signed HDR Vector "+mode);
                }
                Object.DestroyImmediate(a);a=null;Object.DestroyImmediate(b);b=null;
            }
            layer.SetBehaviour(shape); shape.gradient = Ramp(); shape.gradient.ColorSpace = ColorSpace.Linear;
            shape.fillMode=VectorFillMode.AngularGradient;
            foreach(float repetitions in new[]{.5f,2f,3.25f})
            foreach(WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
            {
                shape.gradientRepetitions=repetitions;shape.gradient.WrapMode=wrap;layer.SetBehaviour(shape);a=doc.ComposeCanvas();
                layer.SetBehaviour(ShapeToVector.Convert(shape,doc));b=doc.ComposeCanvas();
                for(int y=3;y<doc.height-2;y+=7)for(int x=3;x<doc.width-2;x+=7)
                {
                    double coordinate=Coordinate(VectorFillMode.AngularGradient,new Vector2((x+.5f)/doc.width,(y+.5f)/doc.height),
                        shape.gradientStart,shape.gradientEnd,new Vector2(doc.width,doc.height))*repetitions;
                    Color expected=shape.gradient.Evaluate((float)coordinate);
                    Near(t,expected,a.GetPixel(x,y),.012f,"Angular repetitions GPU "+repetitions+"/"+wrap);
                    Near(t,expected,b.GetPixel(x,y),.003f,"Angular repetitions CPU "+repetitions+"/"+wrap);
                }
                Object.DestroyImmediate(a);a=null;Object.DestroyImmediate(b);b=null;
            }
            shape.gradientRepetitions=1;layer.SetBehaviour(shape);
            shape.gradientStart = shape.gradientEnd = new Vector2(.5f,.5f);
            foreach (var mode in Modes)
            {
                shape.fillMode = mode; a = doc.ComposeCanvas();
                Near(t,shape.gradient.Evaluate(0),a.GetPixel(30,20),.003f,"Degenerate anchors " + mode);
                Object.DestroyImmediate(a); a = null;
            }
            shape.gradientStart = new Vector2(.5f,.5f); shape.gradientEnd = new Vector2(.8f,.65f);
            foreach (var mode in Modes)
            {
                shape.fillMode = mode; shape.edgeMode = ShapeLayerBehaviour.EdgeMode.Antialiased;
                layer.transform.scaleF = new Vector2(-.71f,.64f); layer.transform.rotationF = 17;
                layer.SetBehaviour(shape); a = doc.ComposeCanvas();
                var vector = ShapeToVector.Convert(shape,doc); layer.SetBehaviour(vector); layer.transform.tiling = TransformTilingMode.Unbounded;
                b = doc.ComposeCanvas(); int sampled = 0;
                for (int y=0;y<60;y+=3) for (int x=0;x<80;x+=3)
                {
                    Vector2 uv=layer.transform.Unmap(new Vector2((x+.5f)/80,(y+.5f)/60),new Vector2(80,60));
                    if(uv.x>.05f && uv.x<.95f && uv.y>.05f && uv.y<.95f)
                    { Near(t,a.GetPixel(x,y),b.GetPixel(x,y),.025f,"Transformed fill "+mode); sampled++; }
                }
                t.True(sampled>40,"Enough transformed samples");
                Object.DestroyImmediate(a); a=null; Object.DestroyImmediate(b); b=null;
            }
            layer.SetBehaviour(shape); layer.transform = TextureTransform.Default; layer.transform.scaleF=Vector2.one*.7f;
            shape.kind = ShapeLayerBehaviour.ShapeKind.Ellipse; shape.fillMode=VectorFillMode.SquareGradient; shape.feather=6;
            a=doc.ComposeCanvas(); var baseline=a.GetPixels();
            var thumb=shape.GetPreviewTexture(40); t.True(thumb != null,"Gradient thumbnail exists");
            b=doc.ComposeCanvas(); Equal(t,baseline,b.GetPixels(),"Thumbnail preserves full render"); Object.DestroyImmediate(b); b=null;
            var exported=(Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels",Hidden).Invoke(doc,new object[]{layer,false});
            try
            {
                var pixels=exported.GetPixels();
                // PSD readback is encoded RGBA8, whereas ComposeCanvas returns linear float pixels.
                for(int i=0;i<baseline.Length;i++) Near(t,Encode(baseline[i]),pixels[i],.003f,"Encoded layer export preserves gradient/Feather");
            }
            finally { Object.DestroyImmediate(exported); }
            var group=new Layer(new GroupLayerBehaviour()) { colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR };
            group.children.Add(layer); doc.layers.Clear(); doc.layers.Add(group);
            b=doc.ComposeCanvas(); Equal(t,baseline,b.GetPixels(),"Group gradient input"); Object.DestroyImmediate(b); b=null;
            var target=new Layer(new BlurLayerBehaviour { radius=0,strength=0,inputMode=EffectInputMode.Specific,TargetLayerId=group.Id })
                { colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR };
            doc.layers.Insert(0,target); group.enabled=false;
            b=doc.ComposeCanvas(); Equal(t,baseline,b.GetPixels(),"Hidden group Target gradient"); Object.DestroyImmediate(b); b=null;
            typeof(Layer).GetMethod("ReleaseTransientResources", Hidden).Invoke(target, null);
            doc.layers.Remove(target); group.enabled=true;
            var clip = new Layer(new ColorFillLayerBehaviour { color=Color.white }) { clippingMask=true }; group.children.Insert(0,clip);
            b=doc.ComposeCanvas(); var clipPixels=b.GetPixels(); for(int i=0;i<baseline.Length;i++) t.Near(baseline[i].a,clipPixels[i].a,.003,"Gradient alpha is clipping coverage");
            Object.DestroyImmediate(b); b=null; group.children.Remove(clip);
            shape.gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            b=doc.ComposeCanvas(); t.True((b.GetPixel(40,30)-a.GetPixel(40,30)).maxColorComponent>.1f,"Direct gradient edits invalidate render");
            Object.DestroyImmediate(b); b=null;
            t.True(!ReferenceEquals(thumb,shape.GetPreviewTexture(40)),"Gradient edits invalidate thumbnail");
            var palette=typeof(ShapeLayerBehaviour).GetField("fillPalette",Hidden).GetValue(shape);
            var texture=(Texture2D)palette.GetType().GetField("texture",Hidden).GetValue(palette);
            layer.SetBehaviour(new ShapeLayerBehaviour()); t.True(texture==null,"Replacing Shape releases fill palette");
            t.True(RenderTexture.active==previous && GL.sRGBWrite==srgb,"Render state restored");
            foreach(var shaderName in new[]{"Hidden/WhimTex/Shape","Hidden/WhimTex/Gradient"})
                t.True(!ShaderUtil.ShaderHasError(Shader.Find(shaderName)),shaderName+" compiled");
            Preview();
            StrokeGradients(t);
        }
        finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; if(a!=null)Object.DestroyImmediate(a); if(b!=null)Object.DestroyImmediate(b); Object.DestroyImmediate(doc); }
    });
    static WhimTexGradient Ramp()
    {
        var gradient=new WhimTexGradient(); gradient.SetKeys(new[]{
            new GradientColorKey(new Color(.8f,.05f,.12f),0),new GradientColorKey(new Color(.1f,.8f,.3f),.4998f),
            new GradientColorKey(new Color(.05f,.1f,.9f),.5002f),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.9f,.3f),new GradientAlphaKey(.55f,1)});
        gradient.SetMidpoint(false,0,.3f); gradient.SetMidpoint(true,0,.65f); return gradient;
    }
    static void StrokeGradients(TestContext t)
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;
        doc.width=80;doc.height=60;doc.outputPrecision=WhimTexOutputPrecision.Float32;
        var shape=new ShapeLayerBehaviour {fill=false,stroke=true,strokeWidth=12,strokePosition=ShapeLayerBehaviour.StrokePosition.Inside,
            edgeMode=ShapeLayerBehaviour.EdgeMode.Step,strokeGradientStart=new Vector2(.413f,.457f),strokeGradientEnd=new Vector2(.783f,.621f)};
        var layer=shape.Owner;layer.colorRange=LayerColorRange.HDR;layer.blendRange=LayerBlendRange.HDR;layer.transform.scaleF=Vector2.one;doc.layers.Add(layer);
        Texture2D a=null,b=null;
        try
        {
            foreach(var mode in Modes)
            foreach(var wrap in new[]{WhimTexGradientWrapMode.Clamp,WhimTexGradientWrapMode.Repeat,WhimTexGradientWrapMode.Mirror})
            foreach(var interpolation in new[]{WhimTexGradientMode.Classic,WhimTexGradientMode.Fixed,WhimTexGradientMode.Perceptual})
            {
                shape.strokeMode=mode;shape.strokeGradient=Ramp();shape.strokeGradient.WrapMode=wrap;shape.strokeGradient.Mode=interpolation;
                shape.strokeGradient.ColorSpace=ColorSpace.Linear;shape.strokeGradientRepetitions=2.5f;
                layer.SetBehaviour(shape);a=doc.ComposeCanvas();
                var vector=ShapeToVector.Convert(shape,doc);layer.SetBehaviour(vector);b=doc.ComposeCanvas();
                if(mode==VectorFillMode.LinearGradient && wrap==WhimTexGradientWrapMode.Clamp && interpolation==WhimTexGradientMode.Classic)
                { File.WriteAllBytes("Temp/WhimTex/stroke-gradient-shape-" + System.Guid.NewGuid().ToString("N") + ".png",EncodePng(a));File.WriteAllBytes("Temp/WhimTex/stroke-gradient-vector-" + System.Guid.NewGuid().ToString("N") + ".png",EncodePng(b)); }
                for(int y=2;y<60;y+=7)for(int x=2;x<80;x+=7)
                {
                    if(!((x<10 || x>70) && y>14 && y<46 || (y<10 || y>50) && x>14 && x<66))continue;
                    double q=Coordinate(mode,new Vector2((x+.5f)/80,(y+.5f)/60),shape.strokeGradientStart,shape.strokeGradientEnd,new Vector2(80,60));
                    if(mode==VectorFillMode.AngularGradient)q*=shape.strokeGradientRepetitions;
                    var expected=shape.strokeGradient.Evaluate((float)q);
                    Near(t,expected,a.GetPixel(x,y),.015f,"Stroke GPU oracle "+mode+"/"+wrap+"/"+interpolation);
                    Near(t,expected,b.GetPixel(x,y),.003f,"Stroke CPU oracle "+mode+"/"+wrap+"/"+interpolation+" at "+x+","+y+" width="+vector.shapes[0].strokeWidth+" contours="+vector.shapes[0].contours.Count);
                }
                Object.DestroyImmediate(a);a=null;Object.DestroyImmediate(b);b=null;
            }
            shape.fill=true;shape.fillMode=VectorFillMode.LinearGradient;shape.gradientStart=Vector2.zero;shape.gradientEnd=Vector2.right;
            shape.gradient=new WhimTexGradient {Mode=WhimTexGradientMode.Classic,ColorSpace=ColorSpace.Linear};shape.gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            shape.strokeGradient.SetKeys(new[]{new GradientColorKey(Color.green,0),new GradientColorKey(Color.yellow,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            shape.strokeGradient.WrapMode=WhimTexGradientWrapMode.Clamp;shape.strokeGradient.Mode=WhimTexGradientMode.Classic;
            layer.SetBehaviour(shape);a=doc.ComposeCanvas();
            t.True(shape.GetPreviewTexture(40)!=null,"Shape stroke gradient thumbnail");
            layer.SetBehaviour(ShapeToVector.Convert(shape,doc));b=doc.ComposeCanvas();
            t.True(layer.GetPreviewTexture(40)!=null,"Vector stroke gradient thumbnail");
            Near(t,a.GetPixel(40,30),b.GetPixel(40,30),.003f,"Independent fill palette CPU/GPU");
            Near(t,a.GetPixel(4,30),b.GetPixel(4,30),.012f,"Independent stroke palette CPU/GPU");
            t.True(a.GetPixel(40,30).g<.01f && a.GetPixel(4,30).g>.9f,"Fill and stroke palettes do not overwrite each other");
            Object.DestroyImmediate(a);a=null;Object.DestroyImmediate(b);b=null;
            var vectorModel=(VectorLayerBehaviour)layer.Behaviour;
            var style=vectorModel.shapes[0].Clone(false);style.contours.Clear();
            var operand=ShapeToVector.Convert(shape,doc).Owner;operand.transform=TextureTransform.Default;
            var boolean=new BooleanGroupLayerBehaviour {referenceSize=new Vector2(80,60),edgeMode=VectorEdgeMode.Step,style=style};
            layer.SetBehaviour(boolean);layer.children.Add(operand);a=doc.ComposeCanvas();
            var baseline=a.GetPixels();
            var export=(Texture2D)typeof(WhimTexDocument).GetMethod("RenderPsdPixels",Hidden).Invoke(doc,new object[]{layer,false});
            try{var pixels=export.GetPixels();for(int i=0;i<pixels.Length;i++)Near(t,Encode(baseline[i]),pixels[i],.003f,"Boolean stroke export");}finally{Object.DestroyImmediate(export);}
            var group=new Layer(new GroupLayerBehaviour());group.children.Add(layer);doc.layers.Clear();doc.layers.Add(group);
            b=doc.ComposeCanvas();Equal(t,baseline,b.GetPixels(),"Grouped stroke gradients");Object.DestroyImmediate(b);b=null;
            var target=new BlurLayerBehaviour {radius=0,strength=0,inputMode=EffectInputMode.Specific,TargetLayerId=group.Id}.Owner;doc.layers.Insert(0,target);group.enabled=false;
            b=doc.ComposeCanvas();Equal(t,baseline,b.GetPixels(),"Hidden Target stroke gradients");Object.DestroyImmediate(b);b=null;
            typeof(Layer).GetMethod("ReleaseTransientResources", Hidden).Invoke(target, null);
            doc.layers.Remove(target);group.enabled=true;
            var clip=new Layer(new ColorFillLayerBehaviour {color=Color.white}) {clippingMask=true};group.children.Insert(0,clip);
            b=doc.ComposeCanvas();var clipped=b.GetPixels();for(int i=0;i<baseline.Length;i++)t.Near(baseline[i].a,clipped[i].a,.003,"Stroke gradient clipping");
            Object.DestroyImmediate(b);b=null;group.children.Remove(clip);
            style.strokeGradient.SetKeys(new[]{new GradientColorKey(Color.magenta,0),new GradientColorKey(Color.cyan,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            b=doc.ComposeCanvas();t.True((b.GetPixel(4,30)-a.GetPixel(4,30)).maxColorComponent>.1f,"Direct stroke palette edits invalidate Boolean raster");
        }
        finally{if(a!=null)Object.DestroyImmediate(a);if(b!=null)Object.DestroyImmediate(b);Object.DestroyImmediate(doc);}
    }
    static double Coordinate(VectorFillMode mode,Vector2 uv,Vector2 start,Vector2 end,Vector2 size)
    {
        double x=(uv.x-start.x)*size.x,y=(uv.y-start.y)*size.y,dx=(end.x-start.x)*size.x,dy=(end.y-start.y)*size.y;
        double r2=dx*dx+dy*dy;if(r2<=1e-20)return 0;
        double u=(x*dx+y*dy)/r2,v=(y*dx-x*dy)/r2;
        switch(mode)
        {
            case VectorFillMode.LinearGradient:return u;
            case VectorFillMode.RadialGradient:return Math.Sqrt((x*x+y*y)/r2);
            case VectorFillMode.SquareGradient:return Math.Max(Math.Abs(u),Math.Abs(v));
            default:double a=Math.Atan2(v,u)/(2*Math.PI);return a-Math.Floor(a);
        }
    }
    static Color Decode(Color c)
    {
        float D(float v){float a=Math.Abs(v);return Mathf.Sign(v)*(a<=.04045f?a/12.92f:Mathf.Pow((a+.055f)/1.055f,2.4f));}
        return new Color(D(c.r),D(c.g),D(c.b),c.a);
    }
    static Color Encode(Color c)
    {
        float E(float v){v=Mathf.Clamp01(v);return v<=.0031308f?v*12.92f:1.055f*Mathf.Pow(v,1f/2.4f)-.055f;}
        return new Color(E(c.r),E(c.g),E(c.b),Mathf.Clamp01(c.a));
    }
    static void Near(TestContext t,Color a,Color b,float tolerance,string label)
    {for(int c=0;c<4;c++)t.Near(a[c],b[c],tolerance*Mathf.Max(1,Mathf.Abs(a[c])),label+" channel "+c);}
    static void Equal(TestContext t,Color[] a,Color[] b,string label)
    {t.Equal(a.Length,b.Length,label);for(int i=0;i<a.Length;i++)Near(t,a[i],b[i],.003f,label);}
    static void Preview()
    {
        var output=new Texture2D(Modes.Length*128,256,TextureFormat.RGBA32,false,true);
        try
        {
            for(int col=0;col<Modes.Length;col++)
            {
                var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=128;
                Texture2D a=null,b=null;
                try
                {
                    var shape=new ShapeLayerBehaviour {kind=ShapeLayerBehaviour.ShapeKind.Star,fillMode=Modes[col],
                        gradientStart=new Vector2(.5f,.5f),gradientEnd=new Vector2(.95f,.5f),stroke=true,strokeWidth=2,
                        strokePosition=ShapeLayerBehaviour.StrokePosition.Outside,gradient=Ramp()};
                    shape.gradient.SetKeys(new[]{new GradientColorKey(new Color(.08f,.5f,.8f),0),new GradientColorKey(new Color(1,.55f,.12f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                    var layer=new Layer(shape);layer.transform.scaleF=Vector2.one*.85f;doc.layers.Add(layer);
                    doc.layers.Add(new Layer(new ColorFillLayerBehaviour {color=new Color(.15f,.15f,.15f)}));
                    a=doc.ComposeCanvas();var vector=ShapeToVector.Convert(shape,doc);layer.SetBehaviour(vector);layer.transform.tiling=TransformTilingMode.Unbounded;b=doc.ComposeCanvas();
                    output.SetPixels(col*128,128,128,128,a.GetPixels());output.SetPixels(col*128,0,128,128,b.GetPixels());
                }
                finally{if(a!=null)Object.DestroyImmediate(a);if(b!=null)Object.DestroyImmediate(b);Object.DestroyImmediate(doc);}
            }
            output.Apply();Directory.CreateDirectory("Temp/WhimTex");File.WriteAllBytes("Temp/WhimTex/figure-fills-" + System.Guid.NewGuid().ToString("N") + ".png",output.EncodeToPNG());
        }
        finally{Object.DestroyImmediate(output);}
    }
}
