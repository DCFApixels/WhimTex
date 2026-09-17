using System;
using System.Reflection;
using System.IO;
using Unity.Collections;
using UnityEngine;
using DCFApixels.WhimTex;

public static class SdfControlsSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static string Main()
    {
        int checks=0;
        void Check(bool ok,string text) { if(!ok)throw new Exception(text);checks++; }
        const int w=8,h=7;
        using var pixelOwner=new NativeArray<Color32>(w*h,Allocator.TempJob);
        var pixels=pixelOwner;
        using var output=new NativeArray<float>(w*h,Allocator.TempJob);
        bool Original(int x,int y)=> (x<3 && y>=2 && y<=5) || (x==6 && y==1);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=new Color32(255,255,255,(byte)(Original(x,y)?255:0));
        int Wrap(int p,int n)=>((p%n)+n)%n;
        int Mirror(int p,int n) {p=Wrap(p,2*n);return p<n?p:2*n-1-p;}
        bool Sample(int x,int y,int mode)
        {
            if(mode==0 && (x<0||y<0||x>=w||y>=h))return false;
            if(mode==2)return Original(Wrap(x,w),Wrap(y,h));
            if(mode==3)return Original(Mirror(x,w),Mirror(y,h));
            return Original(Mathf.Clamp(x,0,w-1),Mathf.Clamp(y,0,h-1));
        }
        var compute=typeof(SDFLayerBehaviour).GetMethod("ComputeSourceDistance",F);
        for(int mode=0;mode<4;mode++)foreach(var shift in new[]{Vector2.zero,new Vector2(3,-2),new Vector2(-11,9)})
        {
            compute.Invoke(null,new object[]{pixels,output,w,h,(byte)128,0,DistanceMetric.EuclideanExact,shift,(SDFLayerBehaviour.SourceEdges)mode});
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int qx=x-(int)shift.x,qy=y-(int)shift.y;bool inside=Sample(qx,qy,mode);float best=1e9f;
                for(int yy=-32;yy<=32;yy++)for(int xx=-32;xx<=32;xx++)
                    if(Sample(xx,yy,mode)!=inside)best=Mathf.Min(best,(xx-qx)*(xx-qx)+(yy-qy)*(yy-qy));
                float expected=Mathf.Sqrt(best)*(inside?-1:1);
                Check(Mathf.Abs(output[y*w+x]-expected)<.001,$"boundary {mode} {shift} at {x},{y}: {output[y*w+x]} != {expected}");
            }
        }
        object Read(string json)=>typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",F).Invoke(null,new object[]{json,128,128});
        const string json="{\"format\":\"whimtex.layers\",\"version\":1,\"layers\":[{\"type\":\"sdf\",\"target\":\"shape\",\"properties\":{\"sourceOffset\":[4,-3],\"sourceEdges\":\"Repeat\",\"contourOffset\":3,\"insideDistance\":8,\"outsideDistance\":24,\"profile\":\"linear\"}},{\"type\":\"shape\",\"id\":\"shape\",\"properties\":{\"enabled\":false,\"shape\":{\"kind\":\"Ellipse\"}},\"transform\":{\"scale\":[0.5,0.5]}}]}";
        using var data=(IDisposable)Read(json);
        var doc=(TextureCompositor)data.GetType().GetField("Document",F).GetValue(data);
        string exported=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",F).Invoke(null,new object[]{doc,doc.layers});
        using var copy=(IDisposable)Read(exported);
        var copied=(TextureCompositor)copy.GetType().GetField("Document",F).GetValue(copy);
        var sdf=(SDFLayerBehaviour)copied.layers[0].Behaviour;
        Check(sdf.sourceOffset==new Vector2(4,-3)&&sdf.sourceEdges==SDFLayerBehaviour.SourceEdges.Repeat&&sdf.contourOffset==3&&sdf.insideDistance==8&&sdf.outsideDistance==24,"clipboard controls roundtrip");
        var texture=copied.Compose();
        try
        {
            foreach(var p in texture.GetPixels())Check(!float.IsNaN(p.r)&&!float.IsInfinity(p.r),"finite render");
            Directory.CreateDirectory("Temp/WhimTex");File.WriteAllBytes("Temp/WhimTex/sdf-controls.png",texture.EncodeToPNG());
        }
        finally{UnityEngine.Object.DestroyImmediate(texture);}
        sdf.profile=AnimationCurve.Linear(0,1,1,1);
        texture=copied.Compose();
        try {var colors=texture.GetPixels();foreach(var c in colors)Check(Mathf.Abs(c.r-colors[0].r)<.002,"constant profile");}
        finally{UnityEngine.Object.DestroyImmediate(texture);}
        return "PASS: "+checks+" SDF boundaries, offsets, clipboard, rendering and profile checks.";
    }
}
