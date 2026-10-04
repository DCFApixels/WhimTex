using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;

public static class PatchQuiltingContrastTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Type Core=>typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless");
    static int checks;
    static void Check(bool b,string text){ WhimTex.Tests.UnityC.FixtureContext.Context.True(b, text); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=old;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static Color[] Render(Texture2D t,float width,float feather,int matching,Vector4 mask,float amount,int edge=2)
    {
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var rt=(RenderTexture)Core.GetMethod("RenderCompensated",F).Invoke(null,new object[]{t,t.width,t.height,
            (MakeSeamlessLayerBehaviour.PoissonEdges)edge,width,feather,MakeSeamlessLayerBehaviour.QuiltingQuality.Normal,0,
            (MakeSeamlessLayerBehaviour.QuiltingChannels)matching,mask,amount});
        Check(RenderTexture.active==previous&&GL.sRGBWrite==srgb,"Caller state");
        try{return Read(rt);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
    }
    static void Same(Color[] a,Color[] b,string text,float tolerance=2e-5f)
    {for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<tolerance,text);}
    static string ExecuteMain(){try{return Run();}catch(Exception e){throw new Exception(e.ToString());}}
    static string Run()
    {
        checks=0;var rng=new System.Random(22);
        Check(!new MakeSeamlessLayerBehaviour().quiltingContrastCompensation,"Off by default");
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(2,7),new Vector2Int(17,13),new Vector2Int(64,48)})
        {
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true));var p=new Color[size.x*size.y];
            try
            {
                for(int fixture=0;fixture<4;fixture++)
                {
                    for(int i=0;i<p.Length;i++)p[i]=fixture==0?new Color(.3f,.6f,-.5f,.7f):fixture==1?new Color(10,30,80,0):
                        fixture==2?new Color((float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble(),1):
                        new Color((float)rng.NextDouble()*3-1,(float)rng.NextDouble()*2,(float)rng.NextDouble(),(float)rng.NextDouble());
                    t.SetPixels(p);t.Apply(false,false);
                    foreach(int matching in new[]{0,1})foreach(float width in new[]{.02f,.2f,.45f})
                    {
                        var before=Render(t,width,100,matching,Vector4.one,0);
                        var after=Render(t,width,100,matching,Vector4.one,1);
                        int band=Math.Max(1,Math.Min(size.x/2,Mathf.RoundToInt(size.x*width)));
                        for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++)for(int c=0;c<4;c++)
                        {
                            float value=after[y*size.x+x][c];Check(float.IsFinite(value),"Finite HDR/alpha");
                            if(c==3)Check(value>=-1e-6f&&value<=1+1e-6f,"Alpha range");
                            if(x==0||x==size.x-1||x>=band&&x<size.x-band||fixture<2)
                                Check(Math.Abs(value-before[y*size.x+x][c])<3e-5f,"Pure samples/center/constants unchanged");
                        }
                        Same(Render(t,width,0,matching,Vector4.one,0),Render(t,width,0,matching,Vector4.one,1),"Feather zero bypass exact",1e-6f);
                        Same(before,Render(t,width,100,matching,Vector4.one,0),"Enabled then disabled has no stale state",1e-6f);
                        if(matching==1)
                        {
                            var masked=Render(t,width,100,matching,new Vector4(0,1,0,0),1);
                            for(int i=0;i<p.Length;i++)foreach(int c in new[]{0,2,3})Check(Math.Abs(masked[i][c]-p[i][c])<1e-6f,"Independent mask");
                        }
                    }
                }
            }
            finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        Core.GetMethod("ClearWorkspace",F).Invoke(null,null);
        return "Quilting contrast: "+checks+" checks passed.";
    }
    static string ExecuteVariance()
    {
        int n=256;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(n,n,TextureFormat.RGBAFloat,false,true));var p=new Color[n*n];var rng=new System.Random(55);
        for(int i=0;i<p.Length;i++){float v=(float)rng.NextDouble();p[i]=new Color(v,v,v,1);}t.SetPixels(p);t.Apply(false,false);
        try
        {
            var off=Render(t,.3f,100,1,Vector4.one,0);var on=Render(t,.3f,100,1,Vector4.one,1);
            int band=Mathf.RoundToInt(n*.3f),count=0;double a=0,b=0,aa=0,bb=0;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                if(x>=band&&x<n-band)continue;
                float av=off[y*n+x].r,bv=on[y*n+x].r;a+=av;b+=bv;aa+=av*av;bb+=bv*bv;count++;
            }
            double va=aa/count-a*a/(count*count),vb=bb/count-b*b/(count*count);
            Check(count>100,"Enough band pixels");Check(vb>va*1.03,$"Compensation restores band contrast: off={va}, on={vb}, count={count}");
            return $"Band samples={count}, variance off={va:F6}, on={vb:F6}, ratio={vb/va:F3}.";
        }
        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static string ExecuteCapture(Texture2D original)
    {
        string folder=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temp+"/";int w=original.width,h=original.height;Color[] pixels=original.GetPixels();
        for(int i=0;i<pixels.Length;i++){float g=pixels[i].g;pixels[i]=new Color(g,g,g,1);}
        var source=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));source.SetPixels(pixels);source.Apply(false,false);
        var sheet=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w*3,h*2,TextureFormat.RGBA32,false,true));var output=new Color[w*3*h*2];
        try
        {
            for(int row=0;row<2;row++)for(int col=0;col<3;col++)
            {
                var result=Render(source,row==0?.2f:.45f,100,0,Vector4.one,col*.5f,0);
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    var c=result[((y+h/2)%h)*w+(x+w/2)%w].gamma;c.a=1;
                    output[((1-row)*h+y)*(w*3)+col*w+x]=c;
                }
            }
            sheet.SetPixels(output);sheet.Apply(false,false);System.IO.Directory.CreateDirectory(folder);
            string path=folder+"compensation-0-50-100.png";System.IO.File.WriteAllBytes(path,sheet.EncodeToPNG());
            var timings=new double[2];
            for(int amount=0;amount<2;amount++)
            {
                for(int i=0;i<3;i++)Render(source,.2f,100,0,Vector4.one,amount,0);
                var samples=new double[15];for(int i=0;i<samples.Length;i++)
                {var timer=System.Diagnostics.Stopwatch.StartNew();Render(source,.2f,100,0,Vector4.one,amount,0);samples[i]=timer.Elapsed.TotalMilliseconds;}
                Array.Sort(samples);timings[amount]=samples[samples.Length/2];
            }
            return $"{path}; {w}x{h}, Normal/Linked/All, Feather 100%, Patch Width 20%/45%; columns compensation 0/50/100%; warm median incl. final synchronous test readback: off {timings[0]:F2}ms, on {timings[1]:F2}ms.";
        }
        finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source);Core.GetMethod("ClearWorkspace",F).Invoke(null,null);}
    }
    static string ExecuteMemory()
    {
        void Clear()=>Core.GetMethod("ClearWorkspace",F).Invoke(null,null);
        object Workspace()=>Core.GetField("spare",F).GetValue(null);
        object Contrast(object w)=>w.GetType().GetField("contrast",F).GetValue(w);
        var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(32,32,TextureFormat.RGBAFloat,false,true));var pixels=new Color[1024];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(i%31/31f,i%19/19f,.5f,1);
        t.SetPixels(pixels);t.Apply(false,false);
        try
        {
            Clear();Render(t,.3f,100,0,Vector4.one,0);Check(Contrast(Workspace())==null,"Zero amount allocates no contrast workspace");
            Clear();Render(t,.3f,0,0,Vector4.one,1);Check(Contrast(Workspace())==null,"Zero Feather allocates no contrast workspace");
            long before=(long)Workspace().GetType().GetProperty("Bytes",F).GetValue(Workspace());
            Render(t,.3f,100,0,Vector4.one,1);var w=Workspace();var c=Contrast(w);Check(c!=null,"Enabled workspace allocated");
            long extra=(long)c.GetType().GetProperty("Bytes",F).GetValue(c);
            Check(extra>0&&(long)w.GetType().GetProperty("Bytes",F).GetValue(w)>=before+extra,"Memory budget includes contrast");
            var table=(Texture2D)c.GetType().GetField("table",F).GetValue(c);Clear();
            Check(table==null&&(long)c.GetType().GetProperty("Bytes",F).GetValue(c)==0,"Native and texture storage released");
            return $"Contrast workspace lazy allocation/disposal passed; retained bytes {extra}.";
        }
        finally{Clear();WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("PatchQuiltingContrastTests.Main", () => { ExecuteMain(); });

    public static string Variance() => WhimTex.Tests.UnityC.FixtureContext.Run("PatchQuiltingContrastTests.Variance", () => { ExecuteVariance(); });

    public static string Capture() => global::WhimTex.Tests.UnityC.ReviewedOracle.Live("PatchQuiltingContrastTests.Capture", ExecuteCapture, true);

    public static string Memory() => WhimTex.Tests.UnityC.FixtureContext.Run("PatchQuiltingContrastTests.Memory", () => { ExecuteMemory(); });
}
