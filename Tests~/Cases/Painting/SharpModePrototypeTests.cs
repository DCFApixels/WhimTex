using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class SharpModePrototypeTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly DistanceMetric[] Metrics = { DistanceMetric.Sharp, DistanceMetric.MTSDF,
        DistanceMetric.CornerFit, DistanceMetric.Spring, DistanceMetric.Rbf };

    public static string Run() => TestContext.Run("Two sharp modes: bounded prototype smoke, not quality certification", t =>
    {
        var assembly = typeof(SDFLayerBehaviour).Assembly;
        var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility");
        var compute = utility.GetMethod("ComputeArtisticDistance", Static);
        var multi = utility.GetMethod("ComputeMtsdfDistance", Static);
        var cacheType = assembly.GetType("DCFApixels.WhimTex.ReconstructedDistanceCache");
        var cached = cacheType.GetMethod("Compute", Instance);
        const int w = 40, h = 40;
        var atlas = new Color32[w * 10 * h * 2];
        int differentModes = 0;
        for (int shape = 0; shape < 2; shape++)
        {
            var polygon = new Vector2[shape == 0 ? 10 : 26];
            for (int k = 0; k < polygon.Length; k++)
            {
                float angle = Mathf.PI * .5f + k * Mathf.PI * 2 / polygon.Length;
                polygon[k] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (k % 2 == 0 ? 12 : shape == 0 ? 5 : 3);
            }
            var mask = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                mask[y*w+x] = new Color32(255,255,255,(byte)Mathf.RoundToInt(255*Mathf.Clamp01(.5f - Distance(new Vector2(x-19.2f,y-19.3f),polygon))));
            using var input = new NativeArray<Color32>(mask, Allocator.TempJob);
            using var raw = new NativeArray<float>(mask.Length, Allocator.TempJob);
            using var guarded = new NativeArray<float>(mask.Length, Allocator.TempJob);
            using var again = new NativeArray<float>(mask.Length, Allocator.TempJob);
            byte threshold = (byte)(shape == 0 ? 128 : 77);
            for (int m = 0; m < Metrics.Length; m++)
            {
                var metric = Metrics[m];
                compute.Invoke(null,new object[]{input,raw,w,h,threshold,0,metric,64f,0f,SharpDistanceMode.Unrestricted});
                compute.Invoke(null,new object[]{input,guarded,w,h,threshold,0,metric,64f,0f,SharpDistanceMode.GuardedSharp});
                float delta = 0;
                bool finite = true;
                for (int i = 0; i < mask.Length; i++)
                {
                    finite &= float.IsFinite(raw[i]) && float.IsFinite(guarded[i]);
                    delta = Mathf.Max(delta,Mathf.Abs(raw[i]-guarded[i]));
                    for (int mode = 0; mode < 2; mode++)
                    {
                        byte v = (byte)Mathf.RoundToInt(255*Mathf.Clamp01(.5f+(mode==0?raw[i]:guarded[i])/12));
                        int target = ((1-shape)*h+i/w)*w*10+(m*2+mode)*w+i%w;
                        atlas[target] = new Color32(v,v,v,255);
                    }
                }
                t.True(finite,metric+" finite in both modes");
                if (delta > .0001f) differentModes++;
                compute.Invoke(null,new object[]{input,again,w,h,threshold,0,metric,1f,0f,SharpDistanceMode.Unrestricted});
                t.True(Same(raw,again),metric+" unrestricted ignores miter");
                if (metric == DistanceMetric.MTSDF)
                {
                    using var rgba = new NativeArray<float4>(mask.Length,Allocator.TempJob);
                    foreach (var mode in new[]{SharpDistanceMode.Unrestricted,SharpDistanceMode.GuardedSharp})
                    {
                        multi.Invoke(null,new object[]{input,rgba,w,h,threshold,0,0f,mode});
                        float maxError=0;
                        for(int i=0;i<mask.Length;i++)
                        {
                            float3 c=rgba[i].xyz;
                            float median=math.max(math.min(c.x,c.y),math.min(math.max(c.x,c.y),c.z));
                            maxError=math.max(maxError,math.abs(median-(mode==SharpDistanceMode.Unrestricted?raw[i]:guarded[i])));
                        }
                        t.Near(0,maxError,1e-6,"MTSDF scalar/data agree: "+mode);
                    }
                }
                if (metric == DistanceMetric.CornerFit || metric == DistanceMetric.Spring || metric == DistanceMetric.Rbf)
                {
                    using var cache = (IDisposable)Activator.CreateInstance(cacheType,true);
                    foreach(var mode in new[]{SharpDistanceMode.Unrestricted,SharpDistanceMode.GuardedSharp,SharpDistanceMode.Unrestricted})
                    {
                        cached.Invoke(cache,new object[]{input,again,w,h,threshold,0,metric,64f,mode});
                        t.True(Same(mode==SharpDistanceMode.Unrestricted?raw:guarded,again),metric+" cache switches "+mode);
                    }
                }
            }
        }
        t.True(differentModes>=5,"Modes produce different fields across the sample set");
        foreach(var mode in new[]{SharpDistanceMode.Unrestricted,SharpDistanceMode.GuardedSharp})
        {
            string json="{\"format\":\"whimtex.document\",\"version\":3,\"layers\":[{\"id\":\"s\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"sharpMode\":\""+mode+"\"}},{\"id\":\"o\",\"behaviour\":{\"$type\":\"OutlineLayerBehaviour\",\"sharpMode\":\""+mode+"\"}}]}";
            using var source=Read(json);
            var doc=Document(source);
            string exported=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",Static).Invoke(null,new object[]{doc,doc.layers});
            using var restored=Read(exported);
            var result=Document(restored);
            t.Equal(mode,((SDFLayerBehaviour)result.layers[0].Behaviour).sharpMode,"SDF JSON mode");
            t.Equal(mode,((OutlineLayerBehaviour)result.layers[1].Behaviour).sharpMode,"Outline JSON mode");
        }
        CheckUi(t,new SDFLayerBehaviour {metric=DistanceMetric.Sharp},typeof(SDFLayerEditorWindow));
        CheckUi(t,new OutlineLayerBehaviour {metric=DistanceMetric.Sharp},typeof(OutlineLayerEditorWindow));
        var texture=new Texture2D(w*10,h*2,TextureFormat.RGBA32,false,true);
        try
        {
            texture.SetPixels32(atlas);texture.Apply();
            string dir=Path.GetFullPath("Temp/WhimTex/sharp-modes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir,"comparison.png"),texture.EncodeToPNG());
            Debug.Log("Sharp modes preview: "+dir+"; pairs: Sharp, MTSDF, CornerFit, Spring, RBF; each pair Unrestricted / GuardedSharp.");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    });

    static bool Same(NativeArray<float> a,NativeArray<float> b)
    {for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static IDisposable Read(string json)=>(IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",Static).Invoke(null,new object[]{json,40,40});
    static WhimTexDocument Document(IDisposable value)=>(WhimTexDocument)value.GetType().GetField("Document",Instance).GetValue(value);
    static void CheckUi(TestContext t,object layer,Type editor)
    {
        var previous=UnityEditor.EditorWindow.focusedWindow;
        var window=ScriptableObject.CreateInstance<SharpModeTestWindow>();
        try
        {
        window.ShowUtility();
        var root=window.rootVisualElement;
        var bindingsType=typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings");
        object bindings=Activator.CreateInstance(bindingsType,true);
        Action<string,Action> apply=(_,action)=>action();
        Action<VisualElement,TargetedLayerBehaviour> target=(_,__)=>{};
        editor.GetMethod("BuildFields",Static).Invoke(null,new object[]{root,layer,null,apply,bindings,target});
        EnumField mode=null; FloatField miter=null;
        root.Query<EnumField>().ForEach(f=>{if(f.label=="Corner Mode")mode=f;});
        root.Query<FloatField>().ForEach(f=>{if(f.label=="Miter Limit")miter=f;});
        t.True(mode!=null && !mode.ClassListContains("whimtex-hidden"),editor.Name+" shows mode");
        t.True(miter.ClassListContains("whimtex-hidden"),"Unrestricted hides miter");
        mode.value=SharpDistanceMode.GuardedSharp;
        t.Equal(SharpDistanceMode.GuardedSharp,(SharpDistanceMode)layer.GetType().GetField("sharpMode").GetValue(layer),"UI changes model");
        t.True(!miter.ClassListContains("whimtex-hidden"),"Guarded shows miter");
        root.Clear();
        if(bindings is IDisposable disposable)disposable.Dispose();
        }
        finally { window.Close(); if(previous!=null)previous.Focus(); }
    }
    static float Distance(Vector2 p,Vector2[] polygon)
    {
        float best=float.PositiveInfinity;bool inside=false;
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        {
            Vector2 a=polygon[j],b=polygon[i],d=b-a;
            best=Mathf.Min(best,(p-a-Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude)*d).magnitude);
            if((a.y>p.y)!=(b.y>p.y)&&p.x<a.x+(p.y-a.y)*d.x/d.y)inside=!inside;
        }
        return inside?-best:best;
    }
}
public sealed class SharpModeTestWindow : UnityEditor.EditorWindow { }
