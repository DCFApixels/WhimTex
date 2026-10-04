using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;

public static class NoiseWarpScaleSmoke
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    public static string Run()
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = doc.height = 64;
        var n = new NoiseLayerBehaviour { Scale=new Vector2(.68f,.43f), offset=new Vector3(.371f,-.619f,.371f),
            octaves=3, lacunarity=2.84f, warpStrength=.5f };
        doc.layers.Add(n);
        int checks=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception(message);checks++; }
        Color[] Render()
        {
            var image=doc.ComposeCanvas();
            try { var pixels=image.GetPixels();foreach(var c in pixels)Check(!float.IsNaN(c.r)&&!float.IsInfinity(c.r),"Finite render");return pixels; }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        float Difference(Color[] a,Color[] b) { float sum=0;for(int i=0;i<a.Length;i++)sum+=Mathf.Abs(a[i].r-b[i].r);return sum/a.Length; }
        var latticeType=typeof(NoiseLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.NoiseLatticeSettings");
        var lattice=Activator.CreateInstance(latticeType,true);
        var apply=latticeType.GetMethod("Apply",F);
        var material=new Material(Shader.Find("Hidden/TextureCompositor/Noise"));
        try
        {
            foreach(var dimension in new[]{NoiseLayerBehaviour.NoiseDimensions.TwoD,NoiseLayerBehaviour.NoiseDimensions.ThreeD})
            foreach(var axes in new[]{NoiseLayerBehaviour.PeriodicAxes.None,NoiseLayerBehaviour.PeriodicAxes.X,NoiseLayerBehaviour.PeriodicAxes.Y,NoiseLayerBehaviour.PeriodicAxes.XY})
            {
                n.dimensions=dimension;n.periodic=axes;n.Scale=new Vector2(.68f,.43f);
                foreach(var warp in new[]{NoiseLayerBehaviour.WarpType.BasicGrid,NoiseLayerBehaviour.WarpType.OpenSimplex2,NoiseLayerBehaviour.WarpType.OpenSimplex2Reduced})
                {
                    n.warp=warp;n.WarpScale=new Vector2(3,8);var a=Render();n.WarpScale=new Vector2(8,3);var b=Render();
                    Check(Difference(a,b)>.0001,"Warp Scale changes output: "+dimension+"/"+axes+"/"+warp);
                    Check(n.Scale==new Vector2(.68f,.43f)&&n.warpStrength==.5f,"Other controls unchanged");
                }
                n.warp=NoiseLayerBehaviour.WarpType.None;n.warpScale=1;var off=Render();n.warpScale=32;
                Check(Difference(off,Render())==0,"Disabled Warp ignores scale");
                n.warp=NoiseLayerBehaviour.WarpType.BasicGrid;n.warpStrength=0;n.warpScale=1;off=Render();n.warpScale=32;
                Check(Difference(off,Render())==0,"Zero-strength Warp ignores scale");n.warpStrength=.5f;
            }
            n.periodic=NoiseLayerBehaviour.PeriodicAxes.XY;n.dimensions=NoiseLayerBehaviour.NoiseDimensions.TwoD;n.WarpScale=new Vector2(6,4);
            n.offset=Vector3.zero;
            n.Scale=new Vector2(.5f,.75f);apply.Invoke(lattice,new object[]{material,n,.5,.75});
            var first=material.GetVectorArray("_NoiseLattice");
            n.Scale=new Vector2(8,12);n.WarpScale=new Vector2(.375f,.25f);apply.Invoke(lattice,new object[]{material,n,8.0,12.0});
            var second=material.GetVectorArray("_NoiseLattice");
            for(int i=128;i<132;i++)Check(first[i]==second[i],"Warp frequency depends on Scale times Warp Scale");
            var metadata=second[140];int packed=(int)metadata.w;
            Check(((int)metadata.x*4096+(packed&4095))==3 && ((int)metadata.y*4096+((packed>>12)&4095))==3,"Own BasicGrid period 3x3");
            string json=JsonUtility.ToJson(n);var copy=JsonUtility.FromJson<NoiseLayerBehaviour>(json);
            Check(copy.WarpScale==new Vector2(.375f,.25f),"Serialized Warp Scale axes");
            var thumb=n.GetPreviewTexture(32);var old=thumb.GetPixels();n.warpScaleY=8;var fresh=n.GetPreviewTexture(32).GetPixels();
            Check(Difference(old,fresh)>.0001,"Thumbnail invalidated");
            return "PASS axis Warp Scale multipliers GPU/cache/serialization: "+checks;
        }
        finally { UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(doc); }
    }
}
