using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;

// Unity Pipeline run_script. Temporary GPU fixtures only; no user document edits.
public static class PatchQuiltingFeatherTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static void Check(bool value,string message){ WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
    static Color[] Read(RenderTexture rt)
    {
        var old=RenderTexture.active;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true));
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);return t.GetPixels();}
        finally{RenderTexture.active=old;WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static float Profile(float distance,float width)
    {
        if(width<1e-6f)return distance>=0?1:0;
        float t=Mathf.Clamp01(.5f+distance/width);return t*t*(3-2*t);
    }
    static float Weight(float strip,float left,float right,int band,float amount,bool tiny)
    {
        if(band<=1)return 1;
        if(tiny){left=(band-1)*.5f;right=band+(band-1)*.5f;}
        float a=2*amount*Math.Max(0,Math.Min(left,band-1-left));
        float b=2*amount*Math.Max(0,Math.Min(right-band,2*band-1-right));
        return Math.Min(Profile(strip-left,a),Profile(right-strip,b));
    }
    static string ExecuteMain()
    {
        try{return Run();}catch(Exception e){throw new Exception(e.ToString());}
    }
    static string ExecuteRendering()
    {
        checks=0;
        int size=512;var t=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
        var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)pixels[y*size+x]=new Color(x/(float)size,y/(float)size,(x+y)/(2f*size),1);
        t.SetPixels(pixels);t.Apply(false,false);
        var method=typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless").GetMethod("Render",F);
        Color[] Render(int quality,int edge,float width,float feather,int matching)
        {
            var result=(RenderTexture)method.Invoke(null,new object[]{t,size,size,(MakeSeamlessLayerBehaviour.PoissonEdges)edge,width,feather,
                (MakeSeamlessLayerBehaviour.QuiltingQuality)quality,0,(MakeSeamlessLayerBehaviour.QuiltingChannels)matching,Vector4.one});
            try{return Read(result);}finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Release(result);}
        }
        try
        {
            foreach(int q in new[]{0,1,2})foreach(int edge in new[]{0,1,2})foreach(float width in new[]{.02f,.45f})foreach(int matching in new[]{0,1})
            {
                int band=Mathf.RoundToInt(size*width);Color[] previous=null;
                foreach(float feather in new[]{0f,50f,100f})
                {
                    var p=Render(q,edge,width,feather,matching);
                    double delta=0;
                    for(int y=0;y<size;y++)for(int x=0;x<size;x++)for(int c=0;c<4;c++)
                    {
                        float value=p[y*size+x][c];Check(float.IsFinite(value)&&value>=-1e-6f&&value<=1+1e-6f,"Finite convex output");
                        if((edge==1||x>=band&&x<size-band)&&(edge==2||y>=band&&y<size-band))
                            Check(Math.Abs(value-pixels[y*size+x][c])<3e-5f,"Protected center");
                        if(previous!=null)delta+=Math.Abs(value-previous[y*size+x][c]);
                    }
                    if(previous!=null)Check(delta>.01,$"Feather changes actual output quality={q} edge={edge} width={width} matching={matching} feather={feather}");
                    if(edge!=1)for(int y=0;y<size;y++)for(int c=0;c<3;c++)Check(Math.Abs(p[y*size][c]-p[y*size+size-1][c])<=1f/size+3e-5f,"X donor adjacency preserved");
                    if(edge!=2)for(int x=0;x<size;x++)for(int c=0;c<3;c++)Check(Math.Abs(p[x][c]-p[(size-1)*size+x][c])<=1f/size+3e-5f,"Y donor adjacency preserved");
                    previous=p;
                }
            }
            return "Patch Quilting Feather full rendering: "+checks+" checks passed.";
        }
        finally{WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(t);}
    }
    static string ExecuteCapture(Texture2D original)
    {
        string folder=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Temp+"/";int w=original.width,h=original.height;Color[] pixels=original.GetPixels();
        for(int i=0;i<pixels.Length;i++){float g=pixels[i].g;pixels[i]=new Color(g,g,g,1);}
        var source=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));source.SetPixels(pixels);source.Apply(false,false);
        var sheet=global::WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w*3,h*2,TextureFormat.RGBA32,false,true));var output=new Color[w*3*h*2];
        var render=typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless").GetMethod("Render",F);
        try
        {
            for(int row=0;row<2;row++)for(int col=0;col<3;col++)
            {
                var rt=(RenderTexture)render.Invoke(null,new object[]{source,w,h,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,row==0?.02f:.45f,col*50f,
                    MakeSeamlessLayerBehaviour.QuiltingQuality.Normal,0,MakeSeamlessLayerBehaviour.QuiltingChannels.Linked,Vector4.one});
                Color[] result;try{result=Read(rt);}finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    var c=result[((y+h/2)%h)*w+(x+w/2)%w].gamma;c.a=1;
                    output[((1-row)*h+y)*(w*3)+col*w+x]=c;
                }
            }
            sheet.SetPixels(output);sheet.Apply(false,false);System.IO.Directory.CreateDirectory(folder);
            string path=folder+"feather-0-50-100.png";System.IO.File.WriteAllBytes(path,sheet.EncodeToPNG());
            return path+"; columns: Feather 0/50/100%; top: Patch Width 2%, bottom: 45%; Normal, Linked, no Poisson; tile joins centered; RGB encoded for display.";
        }
        finally{global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(sheet);global::WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source);}
    }
    static string Run()
    {
        checks=0;
        var shader=Shader.Find("Hidden/WhimTex/PatchQuilting");
        Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Shader compiles");
        int w=128,h=64;
        var source=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(w,h,TextureFormat.RGBAFloat,false,true));
        var pixels=new Color[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)pixels[y*w+x]=new Color(x/(float)w,y/(float)h,.4f+.3f*Mathf.Sin(x*.21f+y*.31f),.7f);
        source.SetPixels(pixels);source.Apply(false,false);
        var material=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Material(shader));var rt=WhimTex.Tests.UnityC.FixtureContext.Scope.Temporary(RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;
        try
        {
            GL.sRGBWrite=false;
            material.SetVector("_Size",new Vector4(w,h,1f/w,1f/h));
            material.SetInt("_Independent",1);material.SetVector("_Channels",Vector4.one);
            foreach(bool transpose in new[]{false,true})
            {
                int n=transpose?h:w,rows=transpose?w:h;
                var paths=WhimTex.Tests.UnityC.FixtureContext.Scope.Own(new Texture2D(rows,3,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp});
                var map=new Color[rows*3];
                try
                {
                    material.SetInt("_Transpose",transpose?1:0);material.SetTexture("_Paths",paths);
                    foreach(int band in new[]{1,2,3,10,n/2-2})foreach(bool tiny in new[]{false,true})
                    {
                        float left=(band-1)*.45f,right=band+(band-1)*.91f;
                        int donor=(n-2*band)/2;
                        for(int y=0;y<rows;y++){map[y]=Color.white*(left/(2*band-1));map[rows+y]=Color.white*(right/(2*band-1));map[2*rows+y]=Color.white*donor;}
                        paths.SetPixels(map);paths.Apply(false,false);
                        material.SetFloat("_Band",band);material.SetInt("_Tiny",tiny?1:0);
                        foreach(float amount in new[]{0f,.25f,.5f,1f})
                        {
                            material.SetFloat("_Feather",amount);Graphics.Blit(source,rt,material,1);var result=Read(rt);
                            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                            {
                                int axis=transpose?y:x;float weight=0;int sample=0;
                                if(axis<band||axis>=n-band)
                                {
                                    int strip=axis>=n-band?axis-(n-band):axis+band;
                                    weight=Weight(strip,left,right,band,amount,tiny);
                                    sample=transpose?(donor+strip)*w+x:y*w+donor+strip;
                                }
                                var expected=Color.LerpUnclamped(pixels[y*w+x],pixels[sample],weight);
                                for(int c=0;c<4;c++)Check(Math.Abs(result[y*w+x][c]-expected[c])<3e-5f,$"GPU profile axis={transpose} band={band} tiny={tiny} feather={amount} at {x},{y}/{c}");
                            }
                        }
                    }
                    // Moving the other cut must not shrink this cut's transition.
                    int testBand=n/3;float testLeft=(testBand-1)*.5f;Color[] first=null;
                    foreach(float rightMargin in new[]{1f,(testBand-1)*.5f})
                    {
                        for(int y=0;y<rows;y++){map[y]=Color.white*(testLeft/(2*testBand-1));map[rows+y]=Color.white*((2*testBand-1-rightMargin)/(2*testBand-1));map[2*rows+y]=Color.white*2;}
                        paths.SetPixels(map);paths.Apply(false,false);
                        material.SetFloat("_Band",testBand);material.SetInt("_Tiny",0);material.SetFloat("_Feather",1);
                        Graphics.Blit(source,rt,material,1);var result=Read(rt);
                        if(first!=null)for(int y=0;y<h;y++)for(int x=0;x<w;x++)if((transpose?y:x)>=n-testBand)
                            for(int c=0;c<4;c++)Check(Math.Abs(first[y*w+x][c]-result[y*w+x][c])<1e-6f,"Independent left-cut width");
                        first=result;
                    }
                }
                finally{material.SetTexture("_Paths",null);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(paths);}
            }
            var layer=new MakeSeamlessLayerBehaviour();Check(layer.quiltingFeather==50,"New default 50 percent");
            var set=typeof(WhimTexApi).GetMethod("SetMakeSeamless",F);
            var jsonType=set.GetParameters()[1].ParameterType;
            object Json(float value)=>jsonType.GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{"{\"quiltingFeather\":"+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}"});
            foreach(float percent in new[]{0f,16f,50f,100f})
            {
                set.Invoke(null,new object[]{layer,Json(percent)});
                Check(layer.quiltingFeather==percent,"API accepts percentage");
                var snapshot=typeof(WhimTexApi).GetMethod("MakeSeamlessSnapshot",F).Invoke(null,new object[]{layer});
                var token=jsonType.GetProperty("Item",new[]{typeof(string)}).GetValue(snapshot,new object[]{"quiltingFeather"});
                Check(float.Parse(token.ToString(),System.Globalization.CultureInfo.InvariantCulture)==percent,"API exports percentage");
            }
            foreach(float invalid in new[]{-1f,101f})
            {
                bool rejected=false;try{set.Invoke(null,new object[]{layer,Json(invalid)});}catch(TargetInvocationException){rejected=true;}
                Check(rejected,"API rejects out of range");
            }
            JsonUtility.FromJsonOverwrite("{\"quiltingFeather\":16}",layer);
            Check(layer.quiltingFeather==16,"Old numeric value retained without migration");
            return "Patch Quilting percentage Feather: "+checks+" checks passed.";
        }
        finally{RenderTexture.active=previous;GL.sRGBWrite=srgb;material.SetTexture("_Paths",null);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(material);WhimTex.Tests.UnityC.FixtureContext.Scope.Destroy(source);WhimTex.Tests.UnityC.FixtureContext.Scope.Release(rt);}
    }

    public static string Main() => WhimTex.Tests.UnityC.FixtureContext.Run("PatchQuiltingFeatherTests.Main", () => { ExecuteMain(); });

    public static string Rendering() => WhimTex.Tests.UnityC.FixtureContext.Run("PatchQuiltingFeatherTests.Rendering", () => { ExecuteRendering(); });

    public static string Capture()
    {
        const string label="PatchQuiltingFeatherTests.Capture";
        try
        {
            string[] names={"feather-0-50-100.png"};
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
