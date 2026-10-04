using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
public static class SeamlessControlsSmoke
{
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try { RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);return t.GetPixels(); }
        finally {RenderTexture.active=old;Object.DestroyImmediate(t);}
    }
    static Color[] Render(Texture t,bool histogram,Vector4 edges,float radius,float width,float contrast)
    {
        var type=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex."+(histogram?"HistogramSeamless":"ScreenedSeamless"));
        var rt=(RenderTexture)type.GetMethod("RenderConfigured",Flags).Invoke(null,histogram?
            new object[]{t,t.width,t.height,edges,width,contrast}:new object[]{t,t.width,t.height,
                edges.x+edges.y==0?MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom:edges.z+edges.w==0?MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight:MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,radius});
        try {return Read(rt);}finally {RenderTexture.ReleaseTemporary(rt);}
    }
    static int Band(int n,float width,bool both) => Math.Max(2,Math.Min((int)Math.Ceiling(width*n),both?n/2:n-2));
    public static string Main()
    {
        checks=0;
        var old=JsonUtility.FromJson<MakeSeamlessLayerBehaviour>("{\"mode\":2}");
        Check(old.leftEdge&&old.rightEdge&&old.bottomEdge&&old.topEdge,"Old document edges default");
        Check(old.screeningRadius==.05f&&old.edgeWidth==.2f&&old.histogramContrast==1,"Old document parameter defaults");
        foreach(bool histogram in new[]{false,true})
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(2,3),new Vector2Int(17,13),new Vector2Int(64,48)})
        foreach(float width in new[]{.02f,.2f,.5f})
        {
            int w=size.x,h=size.y;var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);
            var input=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {float v=.4f+.17f*Mathf.Sin(x*.21f+y*.09f)+.12f*Mathf.Cos(y*.29f-x*.11f);input[y*w+x]=new Color(v,v*.8f+.1f,v*.6f,1);}
            t.SetPixels(input);t.Apply();
            try {for(int mask=0;mask<16;mask++)
            {
                if(!histogram&&mask!=3&&mask!=12&&mask!=15)continue;
                bool l=(mask&1)!=0,r=(mask&2)!=0,b=(mask&4)!=0,u=(mask&8)!=0;
                var edges=new Vector4(l?1:0,r?1:0,b?1:0,u?1:0);
                var result=Render(t,histogram,edges,mask%2==0?.005f:.25f,width,mask%3*.5f);
                int bx=Band(w,width,l&&r),by=Band(h,width,b&&u);
                if(histogram) {bx=Math.Max(bx,(int)Math.Ceiling(width*w));by=Math.Max(by,(int)Math.Ceiling(width*h));}
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int c=0;c<4;c++)
                {
                    float v=result[y*w+x][c];Check(!float.IsNaN(v)&&!float.IsInfinity(v),"Finite");
                    bool modified=l&&x<bx||r&&w-1-x<bx||b&&y<by||u&&h-1-y<by;
                    if(histogram&&!modified) Check(Math.Abs(v-input[y*w+x][c])<3e-5,$"Outside copy band {mask} {width} ({x},{y})");
                }
                if(w>=4&&(l||r)&&!histogram)for(int y=0;y<h;y++)
                    Slope(result[y*w+w-2],result[y*w+w-1],result[y*w],result[y*w+1],$"X {histogram} {mask}");
                if(h>=4&&(b||u)&&!histogram)for(int x=0;x<w;x++)
                    Slope(result[(h-2)*w+x],result[(h-1)*w+x],result[x],result[w+x],$"Y {histogram} {mask}");
                if(!histogram&&w>=4&&h>=4)
                {
                    float radius=mask%2==0?.005f:.25f,lambda=1/(radius*radius*Math.Min(w,h)*Math.Min(w,h));
                    float Delta(int x,int y,int channel)
                    {
                        x=l||r?(x+w)%w:Math.Max(0,Math.Min(w-1,x));
                        y=b||u?(y+h)%h:Math.Max(0,Math.Min(h-1,y));
                        return result[y*w+x][channel]-input[y*w+x][channel];
                    }
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    {
                        bool pinned=(l||r)&&(x<2||w-1-x<2)||(b||u)&&(y<2||h-1-y<2);
                        if(pinned)continue;
                        for(int c=0;c<3;c++)
                        {
                            double residual=(4+lambda)*Delta(x,y,c)-Delta(x-1,y,c)-Delta(x+1,y,c)-Delta(x,y-1,c)-Delta(x,y+1,c);
                            Check(Math.Abs(residual)<.00015,$"Local equation residual {residual} mask {mask} width {width}");
                        }
                    }
                }
            }}finally {Object.DestroyImmediate(t);}
        }
        return $"Seamless controls: {checks} checks passed; 16 copy masks, 3 Poisson directions, parameter extremes, tiny/odd sizes, finite pixels, copy exterior, global equation residuals and paired seam slopes.";
    }
    static void Slope(Color a,Color b,Color c,Color d,string message)
    {
        for(int k=0;k<3;k++) {Check(Math.Abs((b[k]-a[k])-(c[k]-b[k]))<5e-5,message);Check(Math.Abs((d[k]-c[k])-(c[k]-b[k]))<5e-5,message);}
    }
    public static string Preview()
    {
        string dir=Path.GetFullPath("output/seamless-controls-lab-2026-09-28");Directory.CreateDirectory(dir);
        var t=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
        try
        {
            t.LoadImage(File.ReadAllBytes("output/copy-blend-lab-2026-09-28/00_source.png"));
            foreach(bool histogram in new[]{false,true})for(int mask=0;mask<4;mask++)
            {
                var edges=mask==0?Vector4.one:mask==1?new Vector4(0,1,0,0):mask==2?new Vector4(0,0,0,1):new Vector4(0,1,0,1);
                var pixels=Render(t,histogram,edges,.05f,.2f,1);
                string name=(histogram?"histogram":"screened")+"_"+mask;
                using(var writer=new BinaryWriter(File.Create(Path.Combine(dir,name+".bin"))))
                {writer.Write(t.width);writer.Write(t.height);foreach(var p in pixels){writer.Write(p.r);writer.Write(p.g);writer.Write(p.b);writer.Write(p.a);}}
                var output=new Texture2D(t.width,t.height,TextureFormat.RGBA32,false,true);
                try {for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;output.SetPixels(pixels);output.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),output.EncodeToPNG());}
                finally {Object.DestroyImmediate(output);}
            }
        }
        finally {Object.DestroyImmediate(t);}
        return dir;
    }
}
