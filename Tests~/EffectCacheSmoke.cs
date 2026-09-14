// Opt-in after manual compilation. Transient documents/textures only; no asset writes or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var compositorType = typeof(DCFApixels.WhimTex.TextureCompositor);
var cacheType = compositorType.Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache");
var cache = System.Activator.CreateInstance(cacheType,true);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
document.width=document.height=64;
var texture=new UnityEngine.Texture2D(64,64,UnityEngine.TextureFormat.RGBAFloat,false,true);
texture.hideFlags=UnityEngine.HideFlags.HideAndDontSave;
var input=new UnityEngine.Color[4096];
for(int y=0;y<64;y++) for(int x=0;x<64;x++) input[y*64+x]=new UnityEngine.Color(2,.3f,.1f,x>16&&x<48&&y>16&&y<48?.9f:0);
texture.SetPixels(input);texture.Apply(false,false);
var file=new DCFApixels.WhimTex.FileLayerBehaviour { sourceTexture=texture,colorRange=DCFApixels.WhimTex.LayerColorRange.HDR };
var group=new DCFApixels.WhimTex.GroupLayerBehaviour { opacity=.7f,enabled=false,colorRange=DCFApixels.WhimTex.LayerColorRange.HDR };
group.layers.Add(file);
var outline=new DCFApixels.WhimTex.OutlineLayerBehaviour();
var gaussian=new DCFApixels.WhimTex.BlurLayerBehaviour { radius=40,colorRange=DCFApixels.WhimTex.LayerColorRange.HDR };
document.layers.Add(outline);document.layers.Add(group);
void Normalize()=>compositorType.GetMethod("NormalizeModel",flags).Invoke(document,null);
Normalize();
outline.inputMode=DCFApixels.WhimTex.EffectInputMode.Specific; outline.TargetLayerId=group.Id;
gaussian.inputMode=DCFApixels.WhimTex.EffectInputMode.Specific;gaussian.TargetLayerId=group.Id;
int checks=0;
void Check(bool value,string message){if(!value)throw new System.Exception(message);checks++;}
int Hits()=>(int)cacheType.GetProperty("Hits",flags).GetValue(cache);
long Bytes()=>(long)cacheType.GetProperty("Bytes",flags).GetValue(cache);
UnityEngine.Color[] Read(UnityEngine.RenderTexture rt)
{
    var previous=UnityEngine.RenderTexture.active;
    var read=new UnityEngine.Texture2D(rt.width,rt.height,UnityEngine.TextureFormat.RGBAFloat,false,true);
    try{UnityEngine.RenderTexture.active=rt;read.ReadPixels(new UnityEngine.Rect(0,0,rt.width,rt.height),0,0,false);return read.GetPixels();}
    finally{UnityEngine.RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);UnityEngine.RenderTexture.ReleaseTemporary(rt);}
}
UnityEngine.Color[] Render(bool fast=false)=>Read((UnityEngine.RenderTexture)compositorType.GetMethod("RenderCachedPreview",flags).Invoke(document,new object[]{64,cache,fast,null}));
UnityEngine.Color[] Fresh()
{
    var result=document.Compose();try{return result.GetPixels();}finally{UnityEngine.Object.DestroyImmediate(result);}
}
void Same(UnityEngine.Color[] a,UnityEngine.Color[] b,float tolerance,string message)
{ for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(System.Math.Abs(a[i][c]-b[i][c])<=tolerance,message); }
try
{
    var baseline=Render();int hits=Hits();Same(baseline,Render(),.001f,"Cache parity");Check(Hits()>hits,"Effect cache hit");
    var entries=(System.Collections.IDictionary)cacheType.GetField("entries",flags).GetValue(cache);
    object GroupEntry()
    {
        foreach(System.Collections.DictionaryEntry item in entries) if(((string)item.Key).Contains(group.Id+"/group/"))return item.Value;
        throw new System.Exception("Missing group source cache");
    }
    object entry=GroupEntry();Check((bool)entry.GetType().GetField("alphaOnly",flags).GetValue(entry),"Outline-only group stores alpha");
    document.layers.Insert(0,gaussian);Normalize();
    Render();entry=GroupEntry();Check(!(bool)entry.GetType().GetField("alphaOnly",flags).GetValue(entry),"Color consumer promotes group to RGBA");
    Same(Fresh(),Render(),.003f,"RGBA promotion matches uncached composition");
    // Keep Gaussian reachable without adding its pixels, then compare Outline after promotion.
    gaussian.opacity=0;Same(baseline,Render(),.003f,"Promotion preserves original Outline");gaussian.opacity=1;
    var normal=new DCFApixels.WhimTex.NormalMapLayerBehaviour {inputMode=DCFApixels.WhimTex.EffectInputMode.Specific,TargetLayerId=group.Id};
    document.layers.Insert(0,normal);Normalize();Same(Fresh(),Render(),.003f,"Normal Map shares color source");
    document.layers.Remove(normal);
    var motion=new DCFApixels.WhimTex.BlurLayerBehaviour {mode=DCFApixels.WhimTex.BlurType.Linear,distance=8,inputMode=DCFApixels.WhimTex.EffectInputMode.Specific,TargetLayerId=group.Id};
    document.layers.Insert(0,motion);Normalize();
    Same(Fresh(),Render(),.003f,"Motion Blur shares isolated RGBA source");
    hits=Hits();Render();Check(Hits()>hits,"Motion Blur cache hit");
    motion.mode=DCFApixels.WhimTex.BlurType.Circular;motion.arc=45;
    Same(Fresh(),Render(),.003f,"Motion Blur mode invalidation");
    motion.center=new UnityEngine.Vector2(.2f,.8f);
    Same(Fresh(),Render(),.003f,"Motion Blur center invalidation");
    motion.strength=2;
    Same(Fresh(),Render(),.003f,"Motion Blur strength invalidation");
    Render(true);Same(Fresh(),Render(),.003f,"Motion Blur refines after interactive rendering");
    document.layers.Remove(motion);
    file.opacity=.3f;Same(Fresh(),Render(),.003f,"Child opacity invalidation");
    group.opacity=.4f;Same(Fresh(),Render(),.003f,"Group opacity invalidation");
    input[32*64+32]=new UnityEngine.Color(0,3,0,1);texture.SetPixels(input);texture.Apply(false,false);
    Same(Fresh(),Render(),.003f,"External texture update invalidation");
    var fast=Render(true);var exact=Render();Same(Fresh(),exact,.003f,"Exact render never reuses fast result");
    double error=0;for(int i=0;i<fast.Length;i++) error+=System.Math.Abs(fast[i].a-exact[i].a);
    Check(error/fast.Length<.03,"Interactive blur has similar coverage");
    gaussian.radius=2;Same(Fresh(),Render(),.003f,"Radius invalidation");
    gaussian.strength=2;Same(Fresh(),Render(),.003f,"Gaussian strength invalidation");
    gaussian.strength=1;
    gaussian.TargetLayerId=gaussian.Id;Same(Fresh(),Render(),.003f,"Cycles are not masked by cache");gaussian.TargetLayerId=group.Id;
    // Cached sources must also reproduce accumulated diagnostics.
    input[32*64+32]=new UnityEngine.Color(float.NaN,float.PositiveInfinity,0,1);texture.SetPixels(input);texture.Apply(false,false);
    Render();var errorTexture=(UnityEngine.Texture)compositorType.GetProperty("NumericErrorMask",flags).GetValue(document);
    UnityEngine.Color[] ReadMask(UnityEngine.Texture source)
    {var copy=UnityEngine.RenderTexture.GetTemporary(64,64,0,UnityEngine.RenderTextureFormat.ARGBFloat,UnityEngine.RenderTextureReadWrite.Linear);UnityEngine.Graphics.Blit(source,copy);return Read(copy);}
    var beforeErrors=ReadMask(errorTexture);hits=Hits();Render();
    Same(beforeErrors,ReadMask((UnityEngine.Texture)compositorType.GetProperty("NumericErrorMask",flags).GetValue(document)),.001f,"Error-mask cache parity");
    Check(Hits()>hits,"Diagnostics retained on cache hit");
    bool marked=false;foreach(var p in beforeErrors)marked|=p.r>.5f;Check(marked,"Invalid source is marked");
    cacheType.GetProperty("BudgetBytes",flags).SetValue(cache,1024L);
    gaussian.radius=3;Render();Check(Bytes()<=1024,"Cache respects total budget");
    document.layers.Clear();document.layers.Add(group);group.enabled=true;Render();Check(Bytes()==0,"No effect consumers, no cache");
    string json=UnityEngine.JsonUtility.ToJson(document);Check(!json.Contains("effectCache")&&!json.Contains("numericErrors"),"Derived textures are not serialized");
    return "Effect cache GPU checks passed: "+checks;
}
finally{((System.IDisposable)cache).Dispose();UnityEngine.Object.DestroyImmediate(document);UnityEngine.Object.DestroyImmediate(texture);}
