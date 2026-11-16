using System;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object=UnityEngine.Object;
public static class TiledDrawingTipPreviewTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,F)?.GetValue(o)??o.GetType().GetProperty(n,F)?.GetValue(o);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    public static string Run()=>TestContext.Run("Brush tip preview remains disposable and repeatable", t=>
    {
        var assembly=typeof(DrawingLayerBehaviour).Assembly;
        var layer=new DrawingLayerBehaviour();
        var settings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.PaintToolSettings"),true);
        settings.GetType().GetField("brushSize",F).SetValue(settings,40f);
        var display=new Material((Material)assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials").GetProperty("DisplayChannels",F).GetValue(null));
        display.SetVector("_Channels",Vector4.one);display.SetFloat("_Exposure",1);
        var target=RenderTexture.GetTemporary(384,192,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        byte[] Render(bool erase)
        {
            Call(layer,"RenderBrushPreview",target,Call(settings,"GetStrokeParameters",erase,(Color?)null),display,1f);
            var result=new Texture2D(384,192,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;result.ReadPixels(new Rect(0,0,384,192),0,0,false);return result.GetRawTextureData<byte>().ToArray();}
            finally {Object.DestroyImmediate(result);RenderTexture.active=previous;}
        }
        try
        {
            var first=Render(false);t.True(first.Any(b=>b!=0),"Visible tip stroke");
            var second=Render(false);t.True(first.SequenceEqual(second),"Repeated preview starts from clean background");
            var erased=Render(true);t.True(!first.SequenceEqual(erased),"Eraser preview has its own background");
            t.True(first.SequenceEqual(Render(false)),"Returning to Brush does not retain eraser pixels");
            t.True(!(bool)Get(layer,"HasTiledSource"),"Tip preview does not become a document source");
            t.True(Get(layer,"PixelUndoObject")==null,"Tip preview does not create pixel history");
        }
        finally {Call(layer,"ReleaseTransientResources");Object.DestroyImmediate(display);RenderTexture.active=previous;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);}
    });
}
