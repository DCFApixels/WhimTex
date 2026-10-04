// Cold shared material: Blur Brush must not cap subsequent Gaussian/Sharpen kernels.
// Only owned transient objects; the previous shared material is restored in finally.
using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GaussianSharedKernelSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static string Run()
    {
        var materials = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
        var cache = materials.GetField("gaussianBlurMaterial", F);
        var previousMaterial = cache.GetValue(null);
        var fresh = new Material(Shader.Find("Hidden/TextureCompositor/GaussianBlur")) { hideFlags = HideFlags.HideAndDontSave };
        var brushDoc = ScriptableObject.CreateInstance<TextureCompositor>();
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var source = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
        Texture2D rendered = null;
        RenderTexture preview = null;
        var previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        try
        {
            cache.SetValue(null, fresh);
            brushDoc.width = brushDoc.height = 64;
            var drawing = new DrawingLayerBehaviour();
            brushDoc.layers.Add(drawing);
            typeof(TextureCompositor).GetMethod("NormalizeModel",F).Invoke(brushDoc,null);
            typeof(DrawingLayerBehaviour).GetMethod("InitializeCanvas",F).Invoke(drawing,new object[]{64,64});
            typeof(DrawingLayerBehaviour).GetMethod("BlurSegment",F).Invoke(drawing,
                new object[]{new Vector2(.5f,.5f),new Vector2(.5f,.5f),64,64,16f,1f,1f,null,false});
            Check(fresh.GetVectorArray("_Kernel").Length == 128, "Blur Brush capped the shared Gaussian array");
            var pixels = new Color[64*64];
            pixels[32*64+32] = Color.white;
            source.SetPixels(pixels); source.Apply();
            document.width = document.height = 64;
            var blur = new BlurLayerBehaviour {radius=24,colorRange=LayerColorRange.HDR};
            document.layers.Add(blur);
            document.layers.Add(new FileLayerBehaviour {sourceTexture=source,colorRange=LayerColorRange.HDR});
            typeof(TextureCompositor).GetMethod("NormalizeModel",F).Invoke(document,null);
            preview = (RenderTexture)typeof(TextureCompositor).GetMethod("RenderLayerPreview",F)
                .Invoke(document,new object[]{blur.Owner,64});
            rendered = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            RenderTexture.active=preview; rendered.ReadPixels(new Rect(0,0,64,64),0,0);
            var actual = rendered.GetPixels();
            double total=0;
            for(int i=-24;i<=24;i++) total+=Math.Exp(-i*i/128d);
            double worst=0;
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                int dx=x-32,dy=y-32;
                double expected=Math.Abs(dx)<=24&&Math.Abs(dy)<=24
                    ? Math.Exp(-(dx*dx+dy*dy)/128d)/(total*total) : 0;
                Check(float.IsFinite(actual[y*64+x].a), "Nonfinite Gaussian impulse");
                worst=Math.Max(worst,Math.Abs(actual[y*64+x].a-expected));
            }
            Check(worst < .00005, "Gaussian paired bilinear samples differ from CPU impulse: "+worst);
            return "PASS: "+checks+" cold Brush -> Gaussian array-capacity and CPU-reference checks; max delta="+worst;
        }
        finally
        {
            cache.SetValue(null, previousMaterial);
            RenderTexture.active=previousTarget; GL.sRGBWrite=previousSrgb;
            if(preview!=null) RenderTexture.ReleaseTemporary(preview);
            if(rendered!=null) UnityEngine.Object.DestroyImmediate(rendered);
            UnityEngine.Object.DestroyImmediate(brushDoc); UnityEngine.Object.DestroyImmediate(document);
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(fresh);
        }
    }
}
