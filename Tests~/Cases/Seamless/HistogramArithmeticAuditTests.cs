using System;
using System.Reflection;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
public static class HistogramArithmeticAuditTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static string ExecuteMemory()
    {
        var type=typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace");
        object Invoke(string name,params object[] args)=>type.GetMethod(name,F).Invoke(null,args);
        var spareField=type.GetField("spare",F);var idleField=type.GetField("idleSince",F);
        object borrowed=spareField.GetValue(null);object idle=idleField.GetValue(null);
        spareField.SetValue(null,null);
        object a=null,b=null;Material material=null;RenderTexture rt=null;
        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try {
        Invoke("Clear");a=Invoke("Rent");b=Invoke("Rent");
        UnityBRun.Check(!(ReferenceEquals(a,b)), "Nested rent aliases storage");
        material=UnityBRun.Track(new Material(Shader.Find("Hidden/WhimTex/HistogramSeamless")));
        rt=RenderTexture.GetTemporary(17,13,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        
            GL.sRGBWrite=false;Graphics.Blit(Texture2D.whiteTexture,rt);
            type.GetMethod("Configure",F).Invoke(a,new object[]{rt,material,false});
            var table=(Texture2D)type.GetField("table",F).GetValue(a);
            Invoke("Return",a);var reused=Invoke("Rent");UnityBRun.Check(!(!ReferenceEquals(a,reused)), "Workspace not reused");
            Invoke("Return",a);type.GetField("idleSince",F).SetValue(null,UnityEditor.EditorApplication.timeSinceStartup-31);Invoke("Trim");
            UnityBRun.Check(!(table!=null||((NativeArray<float>)type.GetField("planes",F).GetValue(a)).IsCreated), "Idle workspace leaked storage");
            return "Histogram workspace reuse, nested isolation, idle cleanup and NativeArray/texture disposal passed.";
        }
        finally{try { Invoke("Clear");(a as IDisposable)?.Dispose();(b as IDisposable)?.Dispose();if(material!=null)Object.DestroyImmediate(material);if(rt!=null)RenderTexture.ReleaseTemporary(rt); } finally { spareField.SetValue(null,borrowed);idleField.SetValue(null,idle);RenderTexture.active=old;GL.sRGBWrite=srgb; }}
    }
    static string ExecuteMain()
    {
        var type=typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace");
        IDisposable owner=null;Material material=null;Texture2D source=null;RenderTexture rt=null;
        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            owner=(IDisposable)Activator.CreateInstance(type,true);
            material=UnityBRun.Track(new Material(Shader.Find("Hidden/WhimTex/HistogramSeamless")));
            source=UnityBRun.Track(new Texture2D(17,13,TextureFormat.RGBAFloat,false,true));
            var data=new Color[17*13];var rng=new System.Random(55);
            for(int i=0;i<data.Length;i++)data[i]=new Color((float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble(),(float)rng.NextDouble());
            source.SetPixels(data);source.Apply(false,false);
            rt=RenderTexture.GetTemporary(17,13,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            GL.sRGBWrite=false;Graphics.Blit(source,rt);
            type.GetMethod("Configure",F).Invoke(owner,new object[]{rt,material,false});
            var native=(NativeArray<float>)type.GetField("planes",F).GetValue(owner);var expected=native.ToArray();
            object stats=type.GetField("statistics",F).GetValue(owner);var index=stats.GetType().GetProperty("Item");
            var oldStats=new object[4];for(int c=0;c<4;c++)oldStats[c]=index.GetValue(stats,new object[]{c});
            var jobType=type.GetNestedType("Build",F);object job=Activator.CreateInstance(jobType);
            foreach(string name in new[]{"values","knots","ranks","z","planes","statistics"})jobType.GetField(name,F).SetValue(job,type.GetField(name,F).GetValue(owner));
            var readback=(Texture2D)type.GetField("readback",F).GetValue(owner);jobType.GetField("pixels",F).SetValue(job,readback.GetPixelData<float4>(0));
            var quantiles=type.GetField("quantiles",F).GetValue(owner);jobType.GetField("quantiles",F).SetValue(job,quantiles.GetType().GetField("values",F).GetValue(quantiles));
            jobType.GetField("width",F).SetValue(job,17);jobType.GetField("height",F).SetValue(job,13);
            for(int c=0;c<4;c++)jobType.GetMethod("Execute",F).Invoke(job,new object[]{c});
            int changed=0;double delta=0;string first="";
            UnityBRun.Check(native.Length==expected.Length&&native.Length>0,"Scheduled and managed tables have the same storage size");
            for(int i=0;i<native.Length;i++)
            {
                UnityBRun.Check(float.IsFinite(expected[i])&&float.IsFinite(native[i]),"Finite scheduled/managed table value at "+i);
                if(native[i]!=expected[i]){changed++;delta=Math.Max(delta,Math.Abs(native[i]-expected[i]));if(first=="")first=$"{i}: {expected[i]:R} -> {native[i]:R}";}
            }
            string report=$"Tables changed {changed}, max {delta:R}, first {first}\n";
            for(int c=0;c<4;c++)
            {
                var actual=index.GetValue(stats,new object[]{c});
                foreach(var field in actual.GetType().GetFields(F))
                {
                    object before=field.GetValue(oldStats[c]),after=field.GetValue(actual);
                    if(before is float a&&after is float b)UnityBRun.Check(float.IsFinite(a)&&float.IsFinite(b),"Finite scheduled/managed statistic "+c+"/"+field.Name);
                    if(!Equals(before,after))report+=$"{c}/{field.Name}: {before} -> {after}\n";
                }
            }
            // Preserve the diagnostic differences; Legacy imposed no zero-difference threshold.
            return report;
        }
        finally
        {
            RenderTexture.active=old;GL.sRGBWrite=srgb;
            try{owner?.Dispose();}
            finally{if(material!=null)Object.DestroyImmediate(material);if(source!=null)Object.DestroyImmediate(source);if(rt!=null)RenderTexture.ReleaseTemporary(rt);}
        }
    }
    public static string Main()
    {
        string report=null;
        var result=JsonUtility.FromJson<WhimTex.Tests.TestResult>(UnityBRun.Run("HistogramArithmeticAudit.Main",()=>report=ExecuteMain()));
        if(result.status=="passed")result.message+="\n"+report;
        return result.ToJson();
    }
    public static string Memory() => UnityBRun.Run("HistogramArithmeticAudit.Memory", () => ExecuteMemory());
}
