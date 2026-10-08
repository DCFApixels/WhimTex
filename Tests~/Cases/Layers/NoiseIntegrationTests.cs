// Pipeline run_script: NoiseIntegrationTests.Run. No persistent assets or user windows.
using System;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoiseIntegrationTests
{
    static string ExecuteRun(bool oneD = false)
    {
        int checks=0;
        void Check(bool ok,string message){ WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
        var doc=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());doc.width=doc.height=32;
        var noise=new NoiseLayerBehaviour
        {
            noiseType=NoiseLayerBehaviour.NoiseType.Perlin,
            dimensions=NoiseLayerBehaviour.NoiseDimensions.ThreeD,
            periodic=NoiseLayerBehaviour.PeriodicAxes.XY,
            offset=new Vector3(.3f,.7f,.2f),warp=NoiseLayerBehaviour.WarpType.BasicGrid,
            scaleZ=2,
            WarpScale=new Vector2(2.3f,.7f)
        };
        noise.Scale=new Vector2(6.3f,10.7f);doc.layers.Add(noise);
        if(oneD){noise.dimensions=NoiseLayerBehaviour.NoiseDimensions.OneD;noise.periodic1D=true;noise.direction=37;}
        Color[] Read(){var texture=doc.ComposeCanvas();try{return texture.GetPixels();}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(texture);}}
        double Difference(Color[] a,Color[] b){double sum=0;for(int p=0;p<a.Length;p++)sum+=Math.Abs(a[p].r-b[p].r);return sum/a.Length;}
        try
        {
            var plain=Read();
            var group=new GroupLayerBehaviour();group.layers.Add(noise);doc.layers.Clear();doc.layers.Add(group);
            foreach(var mode in new[]{GroupCompositing.PassThrough,GroupCompositing.Isolated})
            {
                group.compositing=mode;
                Check(Difference(plain,Read())<.0001,"Group preserves periodic source: "+mode);
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
            doc.layers.Insert(0,effect);var a=Read();
            if(oneD)noise.offset.x+=.5f;else noise.offset.z+=.5f;
            var b=Read();
            Check(Difference(a,b)>.001,"Specific target tracks changed slice in nested group");
            if(!oneD)
            {
                noise.scaleZ=3;var changedScaleZ=Read();
                Check(Difference(b,changedScaleZ)>.001,"Specific target tracks Scale Z");
                noise.scaleZ=2;Check(Difference(b,Read())<.0001,"Restoring Scale Z restores target output");
            }
            if(oneD)
            {
                noise.periodic1D=false;var unwrapped=Read();
                Check(Difference(b,unwrapped)>.001,"Specific target invalidates 1D Seamless toggle");
                noise.periodic1D=true;Check(Difference(b,Read())<.0001,"Re-enabling restores exact periodic output");
            }
            noise.warpScaleY=3;var changedWarp=Read();
            Check(Difference(b,changedWarp)>.001,"Specific target tracks Warp Scale Y");
            var export=doc.ComposeCanvas();var decoded=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(2,2));
            try
            {
                Check(decoded.LoadImage(export.EncodeToPNG()),"PNG export decodes");
                Check(decoded.width==doc.width && decoded.height==doc.height,"PNG export dimensions");
            }
            finally {WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(export);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(decoded);}
            foreach(var pixel in b)Check(!float.IsNaN(pixel.r)&&!float.IsInfinity(pixel.r),"Finite target output");
            return "PASS Noise group/clipping/target: "+checks;
        }
        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(doc);}
    }

    public static string Run(bool oneD = false) => WhimTex.Tests.UnityC.FixtureContext.Run("NoiseIntegrationTests.Run", () => { ExecuteRun(oneD); });
}
