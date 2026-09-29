using System;
using System.Reflection;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
public static class HistogramArithmeticAudit
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public static string Memory()
    {
        var type=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace");
        object Invoke(string name,params object[] args)=>type.GetMethod(name,F).Invoke(null,args);
        Invoke("Clear");var a=Invoke("Rent");var b=Invoke("Rent");
        if(ReferenceEquals(a,b))throw new Exception("Nested rent aliases storage");
        var material=new Material(Shader.Find("Hidden/TextureCompositor/HistogramSeamless"));
        var rt=RenderTexture.GetTemporary(17,13,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;Graphics.Blit(Texture2D.whiteTexture,rt);
            type.GetMethod("Configure",F).Invoke(a,new object[]{rt,material,false});
            var table=(Texture2D)type.GetField("table",F).GetValue(a);
            Invoke("Return",a);var reused=Invoke("Rent");if(!ReferenceEquals(a,reused))throw new Exception("Workspace not reused");
            Invoke("Return",a);type.GetField("idleSince",F).SetValue(null,UnityEditor.EditorApplication.timeSinceStartup-31);Invoke("Trim");
            if(table!=null||((NativeArray<float>)type.GetField("planes",F).GetValue(a)).IsCreated)throw new Exception("Idle workspace leaked storage");
            return "Histogram workspace reuse, nested isolation, idle cleanup and NativeArray/texture disposal passed.";
        }
        finally{Invoke("Clear");((IDisposable)a).Dispose();((IDisposable)b).Dispose();Object.DestroyImmediate(material);RenderTexture.ReleaseTemporary(rt);RenderTexture.active=old;GL.sRGBWrite=srgb;}
    }
    public static string Main()
    {
        var type=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace");
        var owner=(IDisposable)Activator.CreateInstance(type,true);var shader=Shader.Find("Hidden/TextureCompositor/HistogramSeamless");
        var material=new Material(shader);var source=new Texture2D(17,13,TextureFormat.RGBAFloat,false,true);
        var data=new Color[17*13];var rng=new System.Random(55);
        for(int i=0;i<data.Length;i++)data[i]=new Color((float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble());
        source.SetPixels(data);source.Apply(false,false);var rt=RenderTexture.GetTemporary(17,13,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;Graphics.Blit(source,rt);
            type.GetMethod("Configure",F).Invoke(owner,new object[]{rt,material,false});
            var native=(NativeArray<float>)type.GetField("planes",F).GetValue(owner);var expected=native.ToArray();
            object stats=type.GetField("statistics",F).GetValue(owner);var index=stats.GetType().GetProperty("Item");var oldStats=new object[4];for(int c=0;c<4;c++)oldStats[c]=index.GetValue(stats,new object[]{c});
            var jobType=type.GetNestedType("Build",F);object job=Activator.CreateInstance(jobType);
            foreach(string name in new[]{"values","knots","ranks","z","planes","statistics"})jobType.GetField(name,F).SetValue(job,type.GetField(name,F).GetValue(owner));
            var readback=(Texture2D)type.GetField("readback",F).GetValue(owner);jobType.GetField("pixels",F).SetValue(job,readback.GetPixelData<float4>(0));
            var quantiles=type.GetField("quantiles",F).GetValue(owner);jobType.GetField("quantiles",F).SetValue(job,quantiles.GetType().GetField("values",F).GetValue(quantiles));
            jobType.GetField("width",F).SetValue(job,17);jobType.GetField("height",F).SetValue(job,13);
            for(int c=0;c<4;c++)jobType.GetMethod("Execute",F).Invoke(job,new object[]{c});
            int changed=0;double delta=0;string first="";
            for(int i=0;i<native.Length;i++)if(native[i]!=expected[i]){changed++;delta=Math.Max(delta,Math.Abs(native[i]-expected[i]));if(first=="")first=$"{i}: {expected[i]:R} -> {native[i]:R}";}
            string report=$"Tables changed {changed}, max {delta:R}, first {first}\n";
            for(int c=0;c<4;c++){var actual=index.GetValue(stats,new object[]{c});foreach(var field in actual.GetType().GetFields(F))if(!Equals(field.GetValue(oldStats[c]),field.GetValue(actual)))report+=$"{c}/{field.Name}: {field.GetValue(oldStats[c])} -> {field.GetValue(actual)}\n";}
            return report;
        }
        finally{RenderTexture.active=old;GL.sRGBWrite=srgb;owner.Dispose();Object.DestroyImmediate(material);Object.DestroyImmediate(source);RenderTexture.ReleaseTemporary(rt);}
    }
}
