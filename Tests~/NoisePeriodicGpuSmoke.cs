// Unity Pipeline run_script: NoisePeriodicGpuSmoke.Run, args [noiseTypeIndex, dimensions].
// The fixture is from the independent double-coordinate CPU prototype, not this shader.
using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class NoisePeriodicGpuSmoke
{
    public static string Run(int kind = 3, int dimensions = 3, bool stress = false)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 16;
        var noise = new NoiseLayerBehaviour { noiseType=(NoiseLayerBehaviour.NoiseType)kind,
            dimensions=dimensions==3?NoiseLayerBehaviour.NoiseDimensions.ThreeD:NoiseLayerBehaviour.NoiseDimensions.TwoD,
            Scale=new Vector2(6.3f,10.7f), offset=new Vector3(.371f,-.619f,.371f),
            octaves=4,lacunarity=1.73f,gain=.7f,weightedStrength=.65f,warpStrength=1.4f,pingPongStrength=2.3f };
        doc.layers.Add(noise);
        var flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
        typeof(TextureCompositor).GetMethod("NormalizeModel",flags).Invoke(doc,null);
        Texture2D image=null;
        double max=0, sum=0; int checks=0; string config="";
        try
        {
            foreach(var line in File.ReadLines("Packages/com.dcfapixels.whimtex/Tests~/NoisePeriodicReference"+(stress?".stress":"")+".csv"))
            {
                var c=line.Split(',');
                if(c[0]!=kind.ToString() || c[1]!=dimensions.ToString())continue;
                string key=string.Join(",",c,0,5)+(stress?c[7]:"");
                if(key!=config)
                {
                    if(image!=null)UnityEngine.Object.DestroyImmediate(image);
                    noise.periodic=(NoiseLayerBehaviour.PeriodicAxes)int.Parse(c[2]);
                    noise.fractal=(NoiseLayerBehaviour.FractalType)int.Parse(c[3]);
                    noise.warp=(NoiseLayerBehaviour.WarpType)int.Parse(c[4]);
                    if(stress)
                    {
                        int profile=int.Parse(c[7]);
                        noise.Scale=profile==0?new Vector2(.01f,.1f):profile==1?new Vector2(1000,333):profile==2?new Vector2(6.3f,10.7f):new Vector2(37.3f,91.7f);
                        noise.offset=profile==3?new Vector3(.371f,-.619f,.371f):new Vector3(10000,-10000,10000);
                        noise.octaves=profile==3?4:8;noise.lacunarity=profile==3?1.73f:4;noise.gain=profile==3?.7f:1;
                        noise.weightedStrength=0;noise.warpStrength=100;
                    }
                    image=doc.Compose(); config=key;
                    foreach(var error in ShaderUtil.GetShaderMessages(Shader.Find("Hidden/TextureCompositor/Noise")))
                        if(error.severity.ToString()=="Error")throw new Exception(error.message);
                }
                int sample=int.Parse(c[5]);
                double actual=image.GetPixel((sample%3)*5+2,(sample/3)*5+1).r;
                double expected=double.Parse(c[6],CultureInfo.InvariantCulture);
                double diff=Math.Abs(actual-expected);
                if(double.IsNaN(actual)||diff>(stress?.006:.001))throw new Exception("GPU/reference mismatch "+line+" actual="+actual+" diff="+diff);
                max=Math.Max(max,diff);sum+=diff;checks++;
            }
            if(checks!=(stress?108:432))throw new Exception("Missing fixture rows: "+checks);
            if(stress)return "PASS stress type="+kind+" dimensions="+dimensions+" samples="+checks+" max="+max+" mean="+(sum/checks);
            // Read the exact generated surface, without changing the shared material.
            var mt=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
            var shared=(Material)mt.GetProperty("Noise",flags).GetValue(null);
            var material=new Material(shared){hideFlags=HideFlags.HideAndDontSave};
            material.SetVectorArray("_NoiseLattice",shared.GetVectorArray("_NoiseLattice"));
            var rt=RenderTexture.GetTemporary(17,17,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var read=new Texture2D(17,17,TextureFormat.RGBAFloat,false,true);
            var active=RenderTexture.active; bool srgb=GL.sRGBWrite;
            try
            {
                material.SetInt("_UnboundedUv",1);
                material.SetVector("_UvRow0",new Vector4(17f/16,0,-1f/32,0));
                material.SetVector("_UvRow1",new Vector4(0,17f/16,-1f/32,0));
                material.SetVector("_UvRow2",new Vector4(0,0,1,0));
                GL.sRGBWrite=false; Graphics.Blit(null,rt,material); RenderTexture.active=rt;
                read.ReadPixels(new Rect(0,0,17,17),0,0,false);
                float low=1,high=0;
                foreach(var c in read.GetPixels()) {low=Mathf.Min(low,c.r);high=Mathf.Max(high,c.r);}
                if(high-low<.03f)throw new Exception("Endpoint test surface unexpectedly constant");
                for(int p=0;p<17;p++)
                    if(Mathf.Abs(read.GetPixel(0,p).r-read.GetPixel(16,p).r)>1e-5 ||
                        Mathf.Abs(read.GetPixel(p,0).r-read.GetPixel(p,16).r)>1e-5)
                        throw new Exception("Periodic endpoints differ");
                // Compare one-sided slopes at the join. A wrap alone would pass endpoints
                // but fail this check if the lattice really had a discontinuity.
                const float epsilon=1f/65536;
                foreach(bool vertical in new[]{false,true})
                {
                    material.SetVector("_UvRow0",vertical ? new Vector4(17f/16,0,-1f/32,0) : new Vector4(17*epsilon,0,-8.5f*epsilon,0));
                    material.SetVector("_UvRow1",vertical ? new Vector4(0,17*epsilon,-8.5f*epsilon,0) : new Vector4(0,17f/16,-1f/32,0));
                    Graphics.Blit(null,rt,material);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,17,17),0,0,false);
                    for(int p=0;p<17;p++)
                    {
                        float left=vertical?read.GetPixel(p,7).r:read.GetPixel(7,p).r;
                        float center=vertical?read.GetPixel(p,8).r:read.GetPixel(8,p).r;
                        float right=vertical?read.GetPixel(p,9).r:read.GetPixel(9,p).r;
                        if(Mathf.Abs(left+right-2*center)>.002f)throw new Exception("Seam slope discontinuity");
                    }
                }
            }
            finally { RenderTexture.active=active; GL.sRGBWrite=srgb; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(material); }
            return "PASS type="+kind+" dimensions="+dimensions+" samples="+checks+" max="+max+" mean="+(sum/checks);
        }
        finally { if(image!=null)UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(doc); }
    }
}
