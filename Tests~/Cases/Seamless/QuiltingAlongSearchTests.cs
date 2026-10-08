using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;

public static class QuiltingAlongSearchTests
{
    const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Type Core=>typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless");
    static int checks;
    static void Check(bool ok,string message){ WhimTex.Tests.UnityC.FixtureContext.Context.True(ok, message); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=old;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static Color[] Render(Texture2D t,float along,int edges=0,int matching=0,float contrast=0,int seed=17,int quality=1,float band=.2f)
    {
        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var rt=(RenderTexture)Core.GetMethod("RenderShifted",F).Invoke(null,new object[]{t,t.width,t.height,
            (MakeSeamlessLayerBehaviour.PoissonEdges)edges,band,100f,(MakeSeamlessLayerBehaviour.QuiltingQuality)quality,seed,
            (MakeSeamlessLayerBehaviour.QuiltingChannels)matching,Vector4.one,contrast,along});
        Check(RenderTexture.active==old&&GL.sRGBWrite==srgb,"Caller state");
        try{return Read(rt);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
    }
    static string ExecuteMain()
    {
        try{return Run();}catch(Exception e){throw new Exception(e.ToString());}
    }
    static string Run()
    {
        checks=0;var shader=Shader.Find("Hidden/TextureCompositor/PatchQuilting");
        Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Shader compiles");
        Check(new MakeSeamlessLayerBehaviour().quiltingAlongSearch==0,"Off by default");
        var rowMap=Core.GetMethod("DonorRow",F);
        foreach(int rows in new[]{1,5,6,17,256})foreach(float shift in new[]{-.25f,-.1f,0,.1f,.25f})
        {
            float previous=-1;
            for(int i=0;i<=4*(rows-1);i++)
            {
                float row=i*.25f;float mapped=(float)rowMap.Invoke(null,new object[]{row,rows,shift});
                Check(mapped>=0&&mapped<=rows-1&&mapped>previous,"Monotonic bounded mapping");
                if(row<=2||row>=rows-3)Check(mapped==row,"Protected end samples");
                previous=mapped;
            }
        }
        var set=typeof(WhimTexApi).GetMethod("SetMakeSeamless",F);var jsonType=set.GetParameters()[1].ParameterType;
        object Parse(string json)=>jsonType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{json});
        var layer=new MakeSeamlessLayerBehaviour();
        set.Invoke(null,new object[]{layer,Parse("{\"quiltingAlongSearch\":0.25}")});
        Check(JsonUtility.FromJson<MakeSeamlessLayerBehaviour>(JsonUtility.ToJson(layer)).quiltingAlongSearch==.25f,"Persistence/API");
        foreach(string bad in new[]{"-0.01","0.251"})
        {
            bool rejected=false;try{set.Invoke(null,new object[]{layer,Parse("{\"quiltingAlongSearch\":"+bad+"}")});}catch(TargetInvocationException){rejected=true;}
            Check(rejected,"API range");
        }
        double changed=0;
        foreach(var size in new[]{new Vector2Int(1,7),new Vector2Int(17,13),new Vector2Int(64,48)})
        {
            var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size.x,size.y,TextureFormat.RGBAFloat,false,true));var p=new Color[size.x*size.y];
            for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++)p[y*size.x+x]=new Color(.4f+.35f*Mathf.Sin(x*.27f+y*.11f),.5f+.3f*Mathf.Cos(x*.12f-y*.31f),.2f+x*.02f,.3f+(y%7)*.1f);
            t.SetPixels(p);t.Apply();
            try{foreach(int edge in new[]{0,1,2})foreach(int matching in new[]{0,1})foreach(float contrast in new[]{0f,1f})
            {
                var zero=Render(t,0,edge,matching,contrast);
                foreach(float along in new[]{.1f,.25f})
                {
                    var result=Render(t,along,edge,matching,contrast);var repeat=Render(t,along,edge,matching,contrast);
                    int bx=Math.Max(1,Mathf.RoundToInt(size.x*.2f)),by=Math.Max(1,Mathf.RoundToInt(size.y*.2f));
                    for(int y=0;y<size.y;y++)for(int x=0;x<size.x;x++)for(int c=0;c<4;c++)
                    {
                        int i=y*size.x+x;float v=result[i][c];Check(float.IsFinite(v),"Finite HDR/alpha");
                        Check(v==repeat[i][c],"Deterministic result");changed+=Math.Abs(v-zero[i][c]);
                        if((edge==1||x>=bx&&x<size.x-bx)&&(edge==2||y>=by&&y<size.y-by))Check(Math.Abs(v-p[i][c])<3e-5,"Center preserved");
                    }
                }
            }}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
        }
        Check(changed>.01,"Expanded search changes output");
        var affine=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(64,48,TextureFormat.RGBAFloat,false,true));var pixels=new Color[64*48];
        for(int y=0;y<48;y++)for(int x=0;x<64;x++)pixels[y*64+x]=new Color(x/64f,y/48f,.5f,1);
        affine.SetPixels(pixels);affine.Apply();
        try{foreach(float band in new[]{.02f,.2f,.45f})foreach(int matching in new[]{0,1})foreach(int quality in new[]{0,1,2})
        {
            var result=Render(affine,.25f,0,matching,0,17,quality,band);
            for(int y=0;y<48;y++)Check(Math.Abs(result[y*64].r-result[y*64+63].r-1f/64)<1e-4,"Second axis preserves first-axis adjacency");
        }}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(affine);}
        return $"Along-seam search: {checks} checks passed; aggregate enabled/off difference {changed:F3}.";
    }
    static string ExecuteCapture(Texture2D original)
    {
        int w=original.width,h=original.height;Color[] pixels=original.GetPixels();
        for(int i=0;i<pixels.Length;i++){float g=pixels[i].g;pixels[i]=new Color(g,g,g,1);}
        var t=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));t.SetPixels(pixels);t.Apply();
        var sheet=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w*3,h,TextureFormat.RGBA32,false,true));var data=new Color[w*3*h];
        try
        {
            string folder=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temp+"/";string timing="";
            for(int col=0;col<3;col++)
            {
                float range=col*.125f;Render(t,range);var samples=new double[7];Color[] result=null;
                for(int i=0;i<7;i++){var watch=Stopwatch.StartNew();result=Render(t,range);watch.Stop();samples[i]=watch.Elapsed.TotalMilliseconds;}
                Array.Sort(samples);timing+=$"{range*100}%: {samples[3]:F3} ms; ";
                var single=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBA32,false,true));
                try
                {
                    var display=new Color[w*h];
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    {
                        var c=result[((y+h/2)%h)*w+(x+w/2)%w].gamma;c.a=1;
                        display[y*w+x]=c;data[y*w*3+col*w+x]=c;
                    }
                    single.SetPixels(display);single.Apply();File.WriteAllBytes(Path.Combine(folder,$"search-{col}.png"),single.EncodeToPNG());
                }
                finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(single);}
            }
            sheet.SetPixels(data);sheet.Apply();string path=Path.Combine(folder,"comparison.png");File.WriteAllBytes(path,sheet.EncodeToPNG());
            return path+"; columns 0/12.5/25%, Normal Linked, width20%, Feather100%, seed17, no correction/compensation. "+timing;
        }
        finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);}
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("QuiltingAlongSearchTests.Main", () => { ExecuteMain(); });

    public static string Capture()
    {
        const string label="QuiltingAlongSearchTests.Capture";
        try
        {
            string[] names={"search-0.png","search-1.png","search-2.png","comparison.png"};
            byte[][] outputs=null;
            string diagnostic=global::WhimTex.Tests.UnityC.ReviewedOracle.Live(label,original=>
            {
                var scope=global::WhimTex.Tests.UnityC.FixtureContext.Scope;
                string report=ExecuteCapture(original);
                if(!ReferenceEquals(scope,global::WhimTex.Tests.UnityC.FixtureContext.Scope))
                    throw new System.IO.InvalidDataException("Capture changed its owned scope");
                string root=System.IO.Path.GetFullPath(scope.Temp),leaf=System.IO.Path.GetFileName(root);
                if(leaf.Length!=39||!leaf.StartsWith("UnityC-",StringComparison.Ordinal)||
                    !Guid.TryParseExact(leaf.Substring(7),"N",out _))
                    throw new System.IO.InvalidDataException("Expected current GUID capture output scope");
                if((System.IO.File.GetAttributes(root)&System.IO.FileAttributes.ReparsePoint)!=0)
                    throw new System.IO.IOException("Refusing linked capture output scope");
                var captured=new byte[names.Length][];
                for(int i=0;i<names.Length;i++)
                {
                    string file=System.IO.Path.Combine(root,names[i]);
                    if((System.IO.File.GetAttributes(file)&System.IO.FileAttributes.ReparsePoint)!=0)
                        throw new System.IO.IOException("Refusing linked capture output");
                    captured[i]=System.IO.File.ReadAllBytes(file);
                }
                outputs=captured; // Exact original owned outputs, before Live disposes the scope.
                return report;
            },true);
            // Missing registration/authentication/body failure retains its exact original result.
            if(outputs==null)return diagnostic;
            var json=Assembly.Load("Newtonsoft.Json");
            var objectType=json.GetType("Newtonsoft.Json.Linq.JObject",true);
            var arrayType=json.GetType("Newtonsoft.Json.Linq.JArray",true);
            var valueType=json.GetType("Newtonsoft.Json.Linq.JValue",true);
            var item=objectType.GetProperty("Item",new[]{typeof(string)});
            object result=objectType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{diagnostic});
            var status=item.GetValue(result,new object[]{"status"});
            if(status==null||status.ToString()!="skipped")return diagnostic;
            object Text(string value)=>valueType.GetConstructor(new[]{typeof(string)}).Invoke(new object[]{value});
            void Put(object target,string key,object value)=>item.SetValue(target,value,new object[]{key});
            object artifacts=Activator.CreateInstance(arrayType);
            for(int i=0;i<names.Length;i++)
            {
                object artifact=Activator.CreateInstance(objectType);
                Put(artifact,"name",Text(names[i]));Put(artifact,"encoding",Text("base64"));
                Put(artifact,"content",Text(Convert.ToBase64String(outputs[i])));
                arrayType.GetMethod("Add",new[]{typeof(object)}).Invoke(artifacts,new object[]{artifact});
            }
            Put(result,"artifacts",artifacts);
            return result.ToString(); // Keep diagnostic/SKIP, checks and all actual failures.
        }
        catch(Exception error){return global::WhimTex.Tests.TestContext.Result("failed",0,label,error.ToString()).ToJson();}
    }
}
