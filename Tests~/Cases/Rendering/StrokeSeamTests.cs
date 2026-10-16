using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Only owned transient documents/textures; preview output is restricted to project Temp.
public static class StrokeSeamTests
{
    public static string Run() => TestContext.Run("Shared round dash seams", t =>
    {
        Preview();
        var assembly = typeof(VectorShape).Assembly;
        var styleType = assembly.GetType("DCFApixels.WhimTex.StrokeStyle", true);
        var from = styleType.GetMethod("From", BindingFlags.Static|BindingFlags.NonPublic, null, new[]{typeof(VectorShape)}, null);
        var build = assembly.GetType("DCFApixels.WhimTex.StrokeGeometry", true).GetMethod("Build", BindingFlags.Static|BindingFlags.NonPublic);
        int bad = 0; string first = null;
        foreach (float width in new[]{3.5f,8f,17f})
        for (int angle=0;angle<360;angle++)
        {
            float radians=angle*Mathf.Deg2Rad;
            Vector2 direction=new Vector2(Mathf.Cos(radians),Mathf.Sin(radians));
            Vector2 a=new Vector2(31.123f,67.234f),b=a+direction*(width*.01f);
            var shape=new VectorShape{strokeWidth=width,cap=VectorStrokeCap.Round,join=VectorStrokeJoin.Round};
            var polygons=(Paths64)build.Invoke(null,new object[]{new List<List<Vector2>>{new List<Vector2>{a,b}},
                new List<bool>{false},from.Invoke(null,new object[]{shape}),width*.5f,.025f});
            float nearest=BoundaryDistance(polygons,(a+b)*.5f);
            // The centre of a short capsule must be radius-away from EVERY boundary.
            // Tiny cracks can be invisible in area metrics but break signed-distance AA.
            if(nearest<width*.5f-.03f || polygons.Count!=1)
            {bad++;if(first==null)first=$"width={width}, angle={angle}, distance={nearest}, paths={polygons.Count}";}
        }
        t.Equal(0,bad,"Capsule interiors have no artificial boundaries; first: "+first);
        bad=0;first=null;
        foreach(float width in new[]{3.5f,8f,17f})
        foreach(float rotation in new[]{0f,13f,67f})
        for(int turn=-170;turn<=170;turn+=5)
        {
            Vector2 pivot=new Vector2(31.123f,67.234f);
            Vector2 Dir(float angle)=>new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
            var shape=new VectorShape{strokeWidth=width,cap=VectorStrokeCap.Round,join=VectorStrokeJoin.Round};
            var polygons=(Paths64)build.Invoke(null,new object[]{new List<List<Vector2>>{new List<Vector2>{pivot-Dir(rotation)*20,pivot,pivot+Dir(rotation+turn)*20}},
                new List<bool>{false},from.Invoke(null,new object[]{shape}),width*.5f,.025f});
            float nearest=BoundaryDistance(polygons,pivot);
            if(nearest<width*.5f-.03f || polygons.Count!=1)
            {bad++;if(first==null)first=$"width={width}, rotation={rotation}, turn={turn}, distance={nearest}, paths={polygons.Count}";}
        }
        t.Equal(0,bad,"Round joins have no artificial boundaries; first: "+first);
    });

    static float BoundaryDistance(Paths64 polygons,Vector2 point)
    {
        float nearest=float.MaxValue;
        foreach(var path in polygons) for(int i=0;i<path.Count;i++)
        {
            Vector2 p=new Vector2((float)(path[i].X/4096d),(float)(path[i].Y/4096d));
            var next=path[(i+1)%path.Count];Vector2 q=new Vector2((float)(next.X/4096d),(float)(next.Y/4096d));
            Vector2 edge=q-p;float along=Mathf.Clamp01(Vector2.Dot(point-p,edge)/Mathf.Max(edge.sqrMagnitude,1e-20f));
            nearest=Mathf.Min(nearest,Vector2.Distance(point,p+edge*along));
        }
        return nearest;
    }

    static void Preview()
    {
        var previous=RenderTexture.active; bool srgb=GL.sRGBWrite;
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;
        doc.width=doc.height=256;
        var shape=new ShapeLayerBehaviour{kind=ShapeLayerBehaviour.ShapeKind.Star,fill=true,fillColor=Color.white,
            stroke=true,strokeWidth=8,strokeColor=Color.black,strokePosition=ShapeLayerBehaviour.StrokePosition.Center,
            strokeJoin=VectorStrokeJoin.Round,strokeCap=VectorStrokeCap.Round,dashPattern=new List<float>{.01f,2}};
        var layer=new Layer(shape);layer.transform.scaleF=Vector2.one*.78f;layer.transform.positionF=new Vector2(.123f,.234f);
        doc.layers.Add(layer);doc.layers.Add(new Layer(new ColorFillLayerBehaviour{color=new Color(.65f,.8f,.7f)}));
        Texture2D a=null,b=null,output=null;
        try
        {
            a=doc.ComposeCanvas();var vector=ShapeToVector.Convert(shape,doc);layer.SetBehaviour(vector);
            layer.transform.tiling=TransformTilingMode.Unbounded;b=doc.ComposeCanvas();
            output=new Texture2D(512,256,TextureFormat.RGBA32,false,true);
            output.SetPixels(0,0,256,256,a.GetPixels());output.SetPixels(256,0,256,256,b.GetPixels());output.Apply();
            Directory.CreateDirectory("Temp/WhimTex");File.WriteAllBytes("Temp/WhimTex/stroke-seams.png",output.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            if(a!=null)Object.DestroyImmediate(a);if(b!=null)Object.DestroyImmediate(b);if(output!=null)Object.DestroyImmediate(output);
            Object.DestroyImmediate(doc);
        }
    }
}
