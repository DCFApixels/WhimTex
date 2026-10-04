using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientPipelineSmoke
{
    public static string Main()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags=HideFlags.HideAndDontSave;doc.width=doc.height=64;
        var behavior=new GradientLayerBehaviour {gradientType=GradientLayerBehaviour.GradientType.Radial};
        behavior.gradient.Mode=WhimTexGradientMode.Perceptual;
        behavior.gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(new Color(0,81/255f,125/255f),.5f),new GradientColorKey(Color.red,1)},
            new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        Layer source=behavior,group=new GroupLayerBehaviour();
        var effect=new BlurLayerBehaviour();
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        int count=0;
        Color[] Pixels(Texture2D texture)
        {
            try {return texture.GetPixels();}
            finally {UnityEngine.Object.DestroyImmediate(texture);}
        }
        void Same(Color[] a,Color[] b,string label,bool encoded=false)
        {
            if(a.Length!=b.Length)throw new Exception(label+" dimensions");
            for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)
            {
                count++;
                var expected=encoded?a[i].gamma:a[i];
                if(float.IsNaN(b[i][c])||Mathf.Abs(expected[c]-b[i][c])>.003f)
                    throw new Exception(label+" mismatch "+i+"/"+c+": "+a[i]+" / "+b[i]);
            }
        }
        Texture2D Export(Layer layer,bool input) => (Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdPixels",flags).Invoke(doc,new object[]{layer,input});
        try
        {
            doc.layers.Add(source);
            var baseline=Pixels(doc.ComposeCanvas());
            Same(baseline,Pixels(Export(source,false)),"Standalone export",true);
            group.children.Add(source);doc.layers.Clear();doc.layers.Add(group);
            group.compositing=GroupCompositing.Isolated;
            Same(baseline,Pixels(doc.ComposeCanvas()),"Isolated group composite");
            Same(baseline,Pixels((Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdGroupContent",flags).Invoke(doc,new object[]{group})),"Layered group export",true);
            group.compositing=GroupCompositing.PassThrough;
            Same(baseline,Pixels(doc.ComposeCanvas()),"Pass-through group composite");
            foreach(var target in new[] {source,group})
            {
                doc.layers.Clear();doc.layers.Add(effect);doc.layers.Add(target);
                typeof(TextureCompositor).GetMethod("NormalizeModel",flags).Invoke(doc,null);
                effect.inputMode=EffectInputMode.Specific;effect.TargetLayerId=target.Id;
                Same(baseline,Pixels(Export(effect,true)),"Specific Target input",true);
            }
            doc.layers.Clear();doc.layers.Add(source);
            source.clippingMask=true;
            doc.layers.Add(new ColorFillLayerBehaviour {color=new Color(0,0,0,.5f)});
            foreach(var pixel in Pixels(doc.ComposeCanvas()))
            {
                count++;if(Mathf.Abs(pixel.a-.5f)>.003f)throw new Exception("Clipping alpha");
            }
            source.clippingMask=false;
            doc.layers.RemoveAt(1);
            var thumb=behavior.GetPreviewTexture(32);
            behavior.gradient.Smoothness=.5f;
            if(behavior.GetPreviewTexture(32)==thumb)throw new Exception("Thumbnail smoothness invalidation");
            return "Rounded composition/Target/clipping/export: "+count+" channel checks passed.";
        }
        finally
        {
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            typeof(GradientLayerBehaviour).GetMethod("ReleaseTransientResources",flags).Invoke(behavior,null);
            doc.layers.Clear();group.children.Clear();UnityEngine.Object.DestroyImmediate(doc);
        }
    }
}
