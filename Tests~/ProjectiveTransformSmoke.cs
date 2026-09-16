using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class ProjectiveTransformSmoke
{
    [Serializable] public class Holder { public TextureTransform transform; }
    static int checks;
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); checks++; }
    public static string Main()
    {
        checks=0;
        var old=JsonUtility.FromJson<Holder>("{\"transform\":{\"pivot\":{\"x\":0.3,\"y\":0.6},\"position\":{\"x\":12.25,\"y\":-9.5},\"scale\":{\"x\":2,\"y\":0.7},\"rotation\":27,\"tiling\":2}}").transform;
        Check(old.position.x==12.25 && old.scale.x==2 && old.rotation==27 && Math.Abs(old.pivot.y-.6)<1e-6,"Old serialized TRS aliases: "+JsonUtility.ToJson(old));
        Check(old.storage==TransformStorage.TRS && old.tiling==TransformTilingMode.Mirror,"Legacy mode");
        var t=TextureTransform.Default;
        t.position=new Double2(12.123456789123,-3.987654321987); t.rotation=23.123456789123;
        var copy=JsonUtility.FromJson<Holder>(JsonUtility.ToJson(new Holder{transform=t})).transform;
        Check(copy.position.x==t.position.x && copy.rotation==t.rotation,"Double serialization");
        var rng=new System.Random(13);
        for(int i=0;i<1000;i++)
        {
            t.scale=new Double2(.5+rng.NextDouble()*2,.5+rng.NextDouble()*2);
            t.rotation=rng.NextDouble()*720;
            var m=t.ToMatrix(1024,768);
            Check(m.TryInverse(out var inv),"TRS inverse");
            var p=new Double2(rng.NextDouble(),rng.NextDouble());
            var q=inv.Point(m.Point(p));
            Check(Math.Abs(p.x-q.x)+Math.Abs(p.y-q.y)<1e-12,"TRS roundtrip");
        }
        Check(ProjectiveMatrix.TryQuad(new Double2(.15,.1),new Double2(.9,.2),new Double2(.72,.85),new Double2(.25,.7),out var quad),"Quad solve");
        Check(t.TrySetMatrix(quad),"Set projective");
        Check(quad.TryInverse(out var qi),"Projective inverse");
        for(int i=0;i<100;i++)
        {
            var p=new Double2(rng.NextDouble(),rng.NextDouble()); var q=qi.Point(quad.Point(p));
            Check(Math.Abs(p.x-q.x)+Math.Abs(p.y-q.y)<1e-12,"Projective roundtrip");
        }
        var invalid=ProjectiveMatrix.Identity; invalid.m20=-2;
        Check(!t.TrySetMatrix(invalid),"Reject horizon crossing");
        Check(!t.TrySetMatrix(default),"Reject singular");
        Gestures();
        Clipboard();
        Render(t);
        Render(t,true);
        Gradient(t);
        Paint(t);
        var affine=TextureTransform.Default;affine.scale=new Double2(.7,1.3);affine.rotation=37;Paint(affine);
        return checks+" projective checks passed.";
    }
    static void Gestures()
    {
        const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(TextureCompositorWindow).GetNestedType("PreviewTransformManipulator",BindingFlags.NonPublic);
        var manip=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{null},null);
        void Set(string name,object value)=>type.GetField(name,f).SetValue(manip,value);
        Set("original",TextureTransform.Default);Set("size",new Vector2(128,128));Set("pointerStart",Vector2.zero);
        for(int h=0;h<8;h++)for(int variant=0;variant<3;variant++)
        {
            Set("handle",h);Set("lastAlt",variant==2);
            var args=new object[]{new Vector2(8,4),new Vector2(8,4),variant>0,true,null};
            Check((bool)type.GetMethod("UpdateProjective",f).Invoke(manip,args),"Corner gesture "+h);
            var result=(TextureTransform)args[4];Check(result.storage==TransformStorage.Projective,"Switch to matrix");
            var first=result.matrix;
            type.GetMethod("UpdateProjective",f).Invoke(manip,args);
            Check(first.Equals(((TextureTransform)args[4]).matrix),"Gesture uses original snapshot");
        }
    }
    static void Clipboard()
    {
        const BindingFlags f=BindingFlags.Static|BindingFlags.NonPublic;
        var read=typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",f);
        string root="{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[{\"type\":\"color\",\"transform\":";
        string[] bodies={"{\"matrix\":[0.8,0.1,0.05,0,0.8,0.1,0,0.25,1]}","{\"position\":[12.123456789123,0]}","{\"matrix\":[1,0,0,0,1,0,0,0,1],\"scale\":[1,1]}"};
        for(int i=0;i<bodies.Length;i++)
        {
            object result=null;
            try
            {
                result=read.Invoke(null,new object[]{root+bodies[i]+"}]}",128,128});
                Check(i<2,"Reject ambiguous matrix and TRS");
                var doc=(TextureCompositor)result.GetType().GetField("Document",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(result);
                if(i==0)Check(doc.layers[0].transform.storage==TransformStorage.Projective,"Clipboard matrix");
                if(i==1)Check(doc.layers[0].transform.position.x==12.123456789123,"Clipboard double precision");
            }
            catch(TargetInvocationException) { if(i!=2)throw;checks++; }
            finally { (result as IDisposable)?.Dispose(); }
        }
    }

    static void Gradient(TextureTransform transform)
    {
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=64;
        var g=new GradientLayerBehaviour {gradientType=GradientLayerBehaviour.GradientType.Horizontal};
        g.gradient.Mode=WhimTexGradientMode.Linear;g.gradient.Smoothness=0;
        g.gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,1)});
        Layer layer=g;transform.tiling=TransformTilingMode.Unbounded;layer.transform=transform;doc.layers.Add(layer);
        RenderTexture output=null;Texture2D read=null;var previous=RenderTexture.active;
        try
        {
            output=(RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(doc,new object[]{64});
            RenderTexture.active=output;read=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            read.ReadPixels(new Rect(0,0,64,64),0,0);read.Apply();
            transform.ToMatrix(64,64).TryInverse(out var inv);
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                var p=new Double2((x+.5)/64,(y+.5)/64);
                if(inv.m20*p.x+inv.m21*p.y+inv.m22<=1e-6)continue;
                Check(Math.Abs(read.GetPixel(x,y).a-Mathf.Clamp01((float)inv.Point(p).x))<.025,"Unbounded gradient mapping");
            }
        }
        finally
        {
            RenderTexture.active=previous;if(output!=null)RenderTexture.ReleaseTemporary(output);
            if(read!=null)UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(doc);
        }
    }
    static void Paint(TextureTransform transform)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=128;
        var drawing=new DrawingLayerBehaviour { brushSize=24,brushHardness=1,brushColor=Color.white };
        Layer layer=drawing;doc.layers.Add(layer);layer.transform=transform;
        object Call(string name,params object[] args)=>typeof(DrawingLayerBehaviour).GetMethod(name,flags).Invoke(drawing,args);
        RenderTexture output=null;Texture2D read=null;var previous=RenderTexture.active;
        try
        {
            Call("InitializeCanvas",512,512);
            var center=transform.Map(new Vector2(.5f,.5f),new Vector2(128,128));
            var source=transform.Unmap(center,new Vector2(128,128));
            Call("BeginStroke",source);Call("PaintPoint",source,128,128,Call("GetStrokeParameters",false));Call("EndStroke");
            output=(RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview",flags).Invoke(doc,new object[]{128});
            RenderTexture.active=output;read=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();
            System.IO.Directory.CreateDirectory("Temp/WhimTex");
            System.IO.File.WriteAllBytes("Temp/WhimTex/projective-brush-test.png",read.EncodeToPNG());
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),center*128);
                if(distance<10)Check(read.GetPixel(x,y).a>.8f,"Round canvas-space brush interior "+transform.storage+" at "+x+","+y+" center="+center+" alpha="+read.GetPixel(x,y).a);
                if(distance>14)Check(read.GetPixel(x,y).a<.05f,"Round canvas-space brush exterior");
            }
            Check(((Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels",flags).GetValue(drawing)).width==512,"Source resolution preserved");
        }
        finally
        {
            RenderTexture.active=previous;if(output!=null)RenderTexture.ReleaseTemporary(output);
            if(read!=null)UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(doc);
        }
    }

    static void Render(TextureTransform transform,bool shape=false)
    {
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags=HideFlags.HideAndDontSave; doc.width=doc.height=128;
        var source=new Texture2D(128,128,TextureFormat.RGBA32,false,true);
        var pixels=new Color32[128*128]; for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(255,255,255,255);
        source.SetPixels32(pixels);source.Apply();
        Texture2D read=null;RenderTexture output=null; var previous=RenderTexture.active;
        try
        {
            var drawing=(DrawingLayerBehaviour)typeof(DrawingLayerBehaviour).GetMethod("FromMergedTexture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
            Layer layer=shape ? (Layer)new ShapeLayerBehaviour {kind=ShapeLayerBehaviour.ShapeKind.Rectangle,fill=true,stroke=false,roundness=0} : (Layer)drawing;
            layer.transform=transform; doc.layers.Add(layer);
            output=(RenderTexture)typeof(TextureCompositor).GetMethod("RenderPreview",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(doc,new object[]{128});
            Check(RenderTexture.active==previous,"Render state restored");
            RenderTexture.active=output;read=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
            read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();
            transform.ToMatrix(128,128).TryInverse(out var inv);
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                var uv=inv.Point(new Double2((x+.5)/128,(y+.5)/128));
                if(uv.x>.03 && uv.x<.97 && uv.y>.03 && uv.y<.97) Check(read.GetPixel(x,y).a>.95,"Quad interior");
                if(uv.x<-.03 || uv.x>1.03 || uv.y<-.03 || uv.y>1.03) Check(read.GetPixel(x,y).a<.01,"Quad exterior");
            }
        }
        finally
        {
            RenderTexture.active=previous;if(output!=null)RenderTexture.ReleaseTemporary(output);
            if(read!=null)UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(doc);UnityEngine.Object.DestroyImmediate(source);
        }
    }
}
