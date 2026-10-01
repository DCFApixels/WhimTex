// Pipeline run_script: NoiseIntegrationSmoke.Run. No persistent assets or user windows.
using System;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoiseIntegrationSmoke
{
    public static string Run()
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.width=doc.height=32;
        var noise=new NoiseLayerBehaviour
        {
            noiseType=NoiseLayerBehaviour.NoiseType.Perlin,
            dimensions=NoiseLayerBehaviour.NoiseDimensions.ThreeD,
            periodic=NoiseLayerBehaviour.PeriodicAxes.XY,
            offset=new Vector3(.3f,.7f,.2f)
        };
        noise.Scale=new Vector2(6.3f,10.7f);doc.layers.Add(noise);
        Color[] Read(){var texture=doc.Compose();try{return texture.GetPixels();}finally{UnityEngine.Object.DestroyImmediate(texture);}}
        double Difference(Color[] a,Color[] b){double sum=0;for(int p=0;p<a.Length;p++)sum+=Math.Abs(a[p].r-b[p].r);return sum/a.Length;}
        try
        {
            var plain=Read();
            var group=new GroupLayerBehaviour();group.layers.Add(noise);doc.layers.Clear();doc.layers.Add(group);
            foreach(var mode in new[]{GroupCompositing.PassThrough,GroupCompositing.Isolated})
            {
                group.compositing=mode;
                Check(Difference(plain,Read())<.0001,"Group preserves periodic 3D source: "+mode);
            }
            var basis=new ColorFillLayerBehaviour{color=new Color(1,1,1,.25f)};
            group.layers.Add(basis);noise.clippingMask=true;
            foreach(var pixel in Read())Check(Math.Abs(pixel.a-.25f)<.001,"Clipping preserves base coverage");
            noise.clippingMask=false;
            var effect=new NormalMapLayerBehaviour
            {
                inputMode=EffectInputMode.Specific,TargetLayerId=((Layer)noise).Id,
                inputSpace=NormalMapLayerBehaviour.InputSpace.Linear,
                output=NormalMapLayerBehaviour.OutputMode.Height,smoothing=0
            };
            doc.layers.Insert(0,effect);var a=Read();noise.offset.z+=.5f;var b=Read();
            Check(Difference(a,b)>.001,"Specific target tracks changed Z in nested group");
            foreach(var pixel in b)Check(!float.IsNaN(pixel.r)&&!float.IsInfinity(pixel.r),"Finite target output");
            return "PASS Noise group/clipping/target: "+checks;
        }
        finally{UnityEngine.Object.DestroyImmediate(doc);}
    }
}
