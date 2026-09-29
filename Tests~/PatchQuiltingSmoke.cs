using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using Unity.Collections;
using DCFApixels.WhimTex;
using Object=UnityEngine.Object;
public static class PatchQuiltingSmoke
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static int checks;
    static Type Core=>typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless");
    static void Check(bool v,string m){checks++;if(!v)throw new Exception(m);}
    static Color[] Read(RenderTexture rt)
    {
        var before=RenderTexture.active;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);return t.GetPixels();}
        finally{RenderTexture.active=before;Object.DestroyImmediate(t);}
    }
    static void Same(Color[] a,Color[] b,string message,float epsilon=3e-5f)
    {Check(a.Length==b.Length,message);for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)Check(Math.Abs(a[i][c]-b[i][c])<epsilon,message+" "+i+"/"+c);}
    public static string Main()=>Unwrap(MainCore);
    static string Unwrap(Func<string> run){try{return run();}catch(Exception e){throw new Exception(e.ToString());}}
    static string MainCore()
    {
        checks=0;
        AssetDatabase.ImportAsset("Packages/com.dcfapixels.whimtex/src/Shaders/PatchQuilting.shader",ImportAssetOptions.ForceUpdate);
        var shader=Shader.Find("Hidden/TextureCompositor/PatchQuilting");
        Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Shader compiles");
        var random=new System.Random(15);
        for(int rows=1;rows<7;rows++)for(int cols=1;cols<5;cols++)foreach(bool closed in new[]{false,true})
        {
            var cost=new float[rows*cols];for(int i=0;i<cost.Length;i++)cost[i]=(float)random.NextDouble();
            using var nativeCost=new NativeArray<float>(cost,Allocator.TempJob);
            using var work=new NativeArray<float>(cols*2,Allocator.TempJob);
            using var back=new NativeArray<sbyte>(rows*cols,Allocator.TempJob);
            using var path=new NativeArray<int>(rows,Allocator.TempJob);
            float score=(float)Core.GetMethod("MinimumCut",F).Invoke(null,new object[]{nativeCost,work,back,path,rows,cols,closed,0,0,0});
            int guard=Math.Min(3,Math.Max(1,rows/2));double best=double.PositiveInfinity;
            void Brute(int row,int last,double sum)
            {
                if(row==rows){best=Math.Min(best,sum);return;}
                for(int x=0;x<cols;x++)
                    if((row==0||Math.Abs(x-last)<=1)&&(!closed||!(row<guard||row>=rows-guard)||x==path[0]))
                        Brute(row+1,x,sum+cost[row*cols+x]);
            }
            Brute(0,0,0);Check(Math.Abs(best-score)<2e-6,"DP vs exhaustive constrained optimum");
            if(closed)Check(path[0]==path[rows-1],"Closed cut");
        }
        foreach(var size in new[]{new Vector2Int(1,1),new Vector2Int(2,7),new Vector2Int(17,13),new Vector2Int(64,48)})
        {
            int w=size.x,h=size.y;var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);
            var input=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)input[y*w+x]=new Color(.3f+.25f*Mathf.Sin(.27f*x+.31f*y),.4f+.2f*Mathf.Cos(.4f*x-.2f*y),.2f+.3f*Mathf.Sin(.5f*x+.7f*y),.6f+.2f*Mathf.Sin(.21f*x));
            t.SetPixels(input);t.Apply();
            var lower=new float[]{float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};
            var upper=new float[]{float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity};
            foreach(var pixel in input)for(int c=0;c<4;c++){lower[c]=Math.Min(lower[c],pixel[c]);upper[c]=Math.Max(upper[c],pixel[c]);}
            Color[] Render(int edge,int matching,float feather,int seed=15,Vector4? mask=null)
            {
                var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
                var rt=(RenderTexture)Core.GetMethod("Render",F).Invoke(null,new object[]{t,w,h,(MakeSeamlessLayerBehaviour.PoissonEdges)edge,.25f,feather,MakeSeamlessLayerBehaviour.QuiltingQuality.Draft,seed,(MakeSeamlessLayerBehaviour.QuiltingChannels)matching,mask??Vector4.one});
                Check(RenderTexture.active==active&&GL.sRGBWrite==srgb,"Caller render state");
                try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}
            }
            try
            {
                foreach(int edge in new[]{0,1,2})foreach(int match in new[]{0,1})foreach(float feather in new[]{0f,25f,50f,100f})
                {
                    var a=Render(edge,match,feather);Same(a,Render(edge,match,feather),"Deterministic output",1e-6f);
                    int bx=Mathf.Clamp(Mathf.RoundToInt(w*.25f),1,Math.Max(1,w/2)),by=Mathf.Clamp(Mathf.RoundToInt(h*.25f),1,Math.Max(1,h/2));
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int c=0;c<4;c++)
                    {
                        float value=a[y*w+x][c];Check(float.IsFinite(value),"Finite output");
                        Check(value>=lower[c]-3e-5&&value<=upper[c]+3e-5,"Convex source range");
                        if((edge==1||w==1||x>=bx&&x<w-bx)&&(edge==2||h==1||y>=by&&y<h-by))
                            Check(Math.Abs(value-input[y*w+x][c])<3e-5,"Protected center");
                    }
                }
                var masked=Render(0,1,3,15,new Vector4(0,1,0,0));
                for(int i=0;i<input.Length;i++)foreach(int c in new[]{0,2,3})Check(Math.Abs(masked[i][c]-input[i][c])<3e-5,"Independent disabled channels");
                if(w==64)
                {
                    var seeded=Render(0,1,3,73);float delta=0;
                    var first=Render(0,1,3,15);
                    for(int i=0;i<first.Length;i++)delta+=Math.Abs(first[i].r-seeded[i].r);
                    Check(delta>.01f,"Seed changes a nonconstant fixture");
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)input[y*w+x]=new Color(x/(float)w,y/(float)h,(x+y)/(float)(w+h),1);
                    t.SetPixels(input);t.Apply();
                    foreach(int edge in new[]{0,1,2})foreach(float feather in new[]{0f,25f,50f,100f})
                    {
                        var joined=Render(edge,1,feather);
                        if(edge!=1)for(int y=0;y<h;y++)for(int c=0;c<3;c++)
                            Check(Math.Abs(joined[y*w][c]-joined[y*w+w-1][c])<=1f/w+3e-5,"Horizontal seam, including corners, follows donor adjacency");
                        if(edge!=2)for(int x=0;x<w;x++)for(int c=0;c<3;c++)
                            Check(Math.Abs(joined[x][c]-joined[(h-1)*w+x][c])<=1f/h+3e-5,"Vertical seam, including corners, follows donor adjacency");
                    }
                }
                for(int i=0;i<input.Length;i++)input[i]=new Color(2,.37f,-.4f,.5f);
                t.SetPixels(input);t.Apply();Same(Render(0,0,3),input,"HDR constant linked");Same(Render(0,1,3),input,"HDR constant independent");
                for(int i=0;i<input.Length;i++)input[i]=i%3==0?new Color(0,100,0,0):new Color(2,0,0,.5f);
                t.SetPixels(input);t.Apply();
                foreach(var pixel in Render(0,0,32))if(pixel.a>1e-5f)
                    Check(Math.Abs(pixel.r-2)<3e-5&&Math.Abs(pixel.g)<3e-5,"Linked transparent hidden RGB does not bleed");
            }
            finally{Object.DestroyImmediate(t);}
        }
        return "Patch Quilting: "+checks+" numerical checks passed.";
    }

    public static string Integration()=>Unwrap(IntegrationCore);
    public static string IntegrationCompensated()=>Unwrap(()=>IntegrationCore(true));
    public static string IntegrationShifted()=>Unwrap(()=>IntegrationCore(true,.25f));
    public static string Cache()=>Unwrap(CacheCore);
    public static string Workspace()=>Unwrap(WorkspaceCore);
    public static string Candidates()=>Unwrap(CandidatesCore);
    static string CandidatesCore()
    {
        checks=0;
        var owner=Core.GetMethod("RentWorkspace",F).Invoke(null,null);
        try
        {
            owner.GetType().GetMethod("Prepare",F).Invoke(owner,new object[]{256,256,256,48,4});
            var aliases=(NativeArray<int>)owner.GetType().GetField("aliases",F).GetValue(owner);
            var tasks=owner.GetType().GetField("tasks",F).GetValue(owner);
            var item=tasks.GetType().GetProperty("Item");
            int Task(int i,string field)=>(int)item.GetValue(tasks,new object[]{i}).GetType().GetField(field,F).GetValue(item.GetValue(tasks,new object[]{i}));
            uint Hash(uint x){unchecked{x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16);}}
            foreach(int n in new[]{3,17,64,256})foreach(int band in new[]{1,Math.Max(1,n/5),n/2})
            foreach(int count in new[]{8,24,48})foreach(int seed in new[]{0,19,int.MinValue,int.MaxValue})
            foreach(bool transpose in new[]{false,true})foreach(bool independent in new[]{false,true})foreach(int mask in new[]{0,5,10,15})
            {
                var channels=new Vector4(mask&1,(mask>>1)&1,(mask>>2)&1,(mask>>3)&1);
                int length=(int)Core.GetMethod("BuildSearchTasks",F).Invoke(null,new object[]{owner,n,band,count,seed,transpose,independent,channels});
                var unique=new System.Collections.Generic.HashSet<(int,int)>();
                for(int channel=0;channel<(independent?4:1);channel++)
                {
                    if(independent&&channels[channel]==0)continue;
                    int searchSeed=unchecked(seed+channel*7919+(transpose?104729:0));
                    for(int k=0;k<count;k++)
                    {
                        float jitter=(Hash(unchecked((uint)searchSeed+(uint)k*747796405u))&65535)/65536f;
                        int donor=k==0?(n-2*band)/2:(int)Unity.Mathematics.math.round((k+jitter)/count*(n-2*band));
                        int index=aliases[channel*count+k];
                        Check(index>=0&&index+1<length&&index%2==0,"Valid disjoint pair alias");
                        Check(Task(index,"donor")==donor&&Task(index+1,"donor")==donor,"Original rounded donor preserved");
                        Check(Task(index,"channel")== (independent?channel:-1)&&Task(index+1,"channel")== (independent?channel:-1),"Channel isolated");
                        Check(Task(index,"side")==0&&Task(index+1,"side")==1,"Both sides scheduled once");
                        unique.Add((channel,donor));
                    }
                }
                Check(length==2*unique.Count,"Exactly one pair per distinct channel/donor");
            }
            return "Patch Quilting candidates: "+checks+" checks passed.";
        }
        finally{((IDisposable)owner).Dispose();}
    }
    static string WorkspaceCore()
    {
        checks=0;
        object Static(string name,params object[] args)=>Core.GetMethod(name,F).Invoke(null,args);
        object Spare()=>Core.GetField("spare",F).GetValue(null);
        object Field(object owner,string name)=>owner.GetType().GetField(name,F).GetValue(owner);
        void Released(object owner)
        {
            foreach(string name in new[]{"values","gradients","map","costs","work","scores","back","cuts","aliases","tasks","options"})
            {var array=Field(owner,name);Check(!(bool)array.GetType().GetProperty("IsCreated").GetValue(array),"Disposed "+name);}
            foreach(string name in new[]{"readback","paths","alternatePaths"})Check((Object)Field(owner,name)==null,"Destroyed "+name);
        }
        var t=new Texture2D(64,48,TextureFormat.RGBAFloat,false,true);var pixels=new Color[64*48];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(.3f+.2f*Mathf.Sin(i*.41f),.6f+.3f*Mathf.Cos(i*.19f),.4f,.8f);
        t.SetPixels(pixels);t.Apply();
        Color[] Render(int seed,Vector4 mask)
        {
            var rt=(RenderTexture)Static("Render",t,64,48,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,.45f,8f,
                MakeSeamlessLayerBehaviour.QuiltingQuality.High,seed,MakeSeamlessLayerBehaviour.QuiltingChannels.Independent,mask);
            try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}
        }
        Static("ClearWorkspace");
        try
        {
            var first=Render(0,Vector4.one);var owner=Spare();Check(owner!=null,"Workspace returned");
            var values=Field(owner,"values");var costs=Field(owner,"costs");
            var readback=Field(owner,"readback");var paths=Field(owner,"paths");var alternate=Field(owner,"alternatePaths");
            Same(first,Render(0,Vector4.one),"Warm output exact",1e-6f);
            Check(ReferenceEquals(owner,Spare()),"Same workspace reused");
            Check(values.Equals(Field(owner,"values"))&&costs.Equals(Field(owner,"costs")),"Native allocations reused");
            Check(ReferenceEquals(readback,Field(owner,"readback")),"Readback reused");
            Check(ReferenceEquals(paths,Field(owner,"paths"))&&ReferenceEquals(alternate,Field(owner,"alternatePaths")),"Both rectangular path textures reused");
            Same(pixels,Render(0,Vector4.zero),"All disabled after populated paths",1e-6f);
            Render(int.MinValue,new Vector4(0,1,0,0));
            var warm=Render(19,Vector4.one);Static("ClearWorkspace");Released(owner);
            Same(warm,Render(19,Vector4.one),"Pooled vs cold exact",1e-6f);
            owner=Spare();long bytes=(long)owner.GetType().GetProperty("Bytes",F).GetValue(owner);
            Check(bytes>0&&bytes<=64L*1024*1024,"Retained workspace budget");
            var rented=Static("RentWorkspace");var nested=Static("RentWorkspace");
            Check(ReferenceEquals(owner,rented)&&!ReferenceEquals(rented,nested)&&Spare()==null,"Nested rent is isolated");
            Static("ReturnWorkspace",nested);Static("ReturnWorkspace",rented);Released(nested);
            Check(ReferenceEquals(rented,Spare()),"One retained workspace only");
            Core.GetField("spareSince",F).SetValue(null,EditorApplication.timeSinceStartup-31);
            Static("TrimWorkspace");Check(Spare()==null,"Idle eviction");Released(rented);
            // Force an oversized scratch owner without allocating any user image/asset.
            var oversized=Static("RentWorkspace");
            oversized.GetType().GetField("costs",F).SetValue(oversized,new NativeArray<float>(17*1024*1024,Allocator.Persistent,NativeArrayOptions.UninitializedMemory));
            Static("ReturnWorkspace",oversized);Check(Spare()==null,"Over-budget workspace not retained");Released(oversized);
            return "Patch Quilting workspace: "+checks+" checks passed.";
        }
        finally{Static("ClearWorkspace");Object.DestroyImmediate(t);}
    }
    static string CacheCore()
    {
        checks=0;
        object Call(object o,string name,params object[] a)=>o.GetType().GetMethod(name,F).Invoke(o,a);
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.width=64;doc.height=48;
        var cache=(IDisposable)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        var effect=new MakeSeamlessLayerBehaviour{mode=MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting,quiltingQuality=MakeSeamlessLayerBehaviour.QuiltingQuality.Draft,colorRange=LayerColorRange.HDR};
        var tex=new Texture2D(64,48,TextureFormat.RGBAFloat,false,true);var pixels=new Color[64*48];
        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(.4f+.3f*Mathf.Sin(i*.231f),.5f+.4f*Mathf.Cos(i*.079f),.3f,1);
        tex.SetPixels(pixels);tex.Apply();
        var source=new FileLayerBehaviour{sourceTexture=tex,colorRange=LayerColorRange.HDR};
        doc.layers.Add(effect);doc.layers.Add(new PendingLayerBehaviour());doc.layers.Add(source);
        var entries=(System.Collections.IDictionary)cache.GetType().GetField("entries",F).GetValue(cache);
        object Entry()=>entries[effect.Id+"/quilting"];
        Color[] Render(bool cached)
        {
            var rt=(RenderTexture)(cached?Call(doc,"RenderCachedPreview",64,cache,false,null):Call(doc,"RenderAllLayers",64,48));
            try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}
        }
        try
        {
            Call(doc,"NormalizeModel");Render(true);var original=Entry();Check(original!=null,"Raw quilting stage stored through pending-layer input");
            var texture=(RenderTexture)original.GetType().GetField("pixels",F).GetValue(original);
            Check(texture.format==RenderTextureFormat.ARGBFloat,"Stage retains FP32");
            effect.quiltingSeamCorrection=true;Render(true);Check(ReferenceEquals(original,Entry()),"Correction toggle reuses quilting");
            effect.quiltingCorrectionRadius=.11f;Same(Render(true),Render(false),"Radius cached/fresh",.005f);Check(ReferenceEquals(original,Entry()),"Radius reuses quilting");
            effect.quiltingPoissonEdges=MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom;Render(true);Check(ReferenceEquals(original,Entry()),"Correction edges reuse quilting");
            void Invalidates(Action change,string name)
            {
                var before=Entry();change();Same(Render(true),Render(false),name+" cached/fresh",.005f);Check(!ReferenceEquals(before,Entry()),name+" invalidates quilting");
            }
            Invalidates(()=>effect.quiltingFeather=3,"Feather");
            Invalidates(()=>effect.quiltingContrastCompensation=true,"Contrast toggle");
            Invalidates(()=>effect.quiltingContrast=.37f,"Contrast amount");
            Invalidates(()=>effect.quiltingWidth=.35f,"Width");
            Invalidates(()=>effect.quiltingAlongSearch=.25f,"Along-seam search");
            Invalidates(()=>effect.quiltingSeed=715,"Seed");
            Invalidates(()=>effect.quiltingChannels=MakeSeamlessLayerBehaviour.QuiltingChannels.Independent,"Matching");
            Invalidates(()=>effect.processRed=false,"Channel mask");
            Invalidates(()=>effect.quiltingQuality=MakeSeamlessLayerBehaviour.QuiltingQuality.Normal,"Quality");
            Invalidates(()=>effect.quiltingEdges=MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight,"Edges");
            Invalidates(()=>{for(int i=0;i<pixels.Length;i++)pixels[i].g*=.6f;tex.SetPixels(pixels);tex.Apply();},"Source behind pending layer");
            effect.mode=MakeSeamlessLayerBehaviour.SeamlessMode.Mirror;Render(true);Check(Entry()==null,"Mode change evicts stage");
            effect.mode=MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting;Render(true);
            cache.GetType().GetProperty("BudgetBytes",F).SetValue(cache,1L);Render(true);
            Check((long)cache.GetType().GetProperty("Bytes",F).GetValue(cache)<=1,"Stage respects shared memory budget");
            cache.Dispose();Check(entries.Count==0,"Dispose clears stage");
            return "Patch Quilting stage cache: "+checks+" checks passed.";
        }
        finally{cache.Dispose();Object.DestroyImmediate(doc);Object.DestroyImmediate(tex);}
    }
    static string IntegrationCore()=>IntegrationCore(false);
    static string IntegrationCore(bool compensate,float alongSearch=0)
    {
        checks=0;
        void Same(Color[] a,Color[] b,string message)=>PatchQuiltingSmoke.Same(a,b,message,.005f);
        object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F).Invoke(o,args);
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.width=64;doc.height=48;
        var cache=(IDisposable)Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"),true);
        var t=new Texture2D(64,48,TextureFormat.RGBAFloat,false,true);
        var input=new Color[64*48];for(int i=0;i<input.Length;i++)input[i]=new Color(.4f+.3f*Mathf.Sin(i*.231f),.5f+.4f*Mathf.Cos(i*.079f),.3f,1);
        t.SetPixels(input);t.Apply();
        var effect=new MakeSeamlessLayerBehaviour{mode=MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting,colorRange=LayerColorRange.HDR,quiltingQuality=MakeSeamlessLayerBehaviour.QuiltingQuality.Draft};
        effect.quiltingContrastCompensation=compensate;effect.quiltingContrast=.37f;
        effect.quiltingAlongSearch=alongSearch;
        var source=new FileLayerBehaviour{sourceTexture=t,colorRange=LayerColorRange.HDR};doc.layers.Add(effect);doc.layers.Add(source);
        EditorWindow window=null;var focus=EditorWindow.focusedWindow;
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
        Color[] Render(string name,params object[] args){var rt=(RenderTexture)Call(doc,name,args);try{return Read(rt);}finally{RenderTexture.ReleaseTemporary(rt);}}
        try
        {
            Call(doc,"NormalizeModel");effect.TargetLayerId=source.Id;
            var expected=Render("RenderAllLayers",64,48);
            Same(Render("RenderPreview",64),expected,"Composite");
            Same(Render("RenderLayerPreview",effect.Owner,64),expected,"Target layer");
            Same(Render("RenderThumbnailLayer",effect.Owner,64,cache),expected,"Thumbnail");
            Same(Render("RenderCachedPreview",64,cache,false,null),expected,"Cache");
            int hits=(int)cache.GetType().GetProperty("Hits",F).GetValue(cache);
            Same(Render("RenderCachedPreview",64,cache,false,null),expected,"Cached reuse");
            Check((int)cache.GetType().GetProperty("Hits",F).GetValue(cache)>hits,"Cache hits");
            var changed=(Color[])input.Clone();for(int i=0;i<changed.Length;i++)changed[i].r*=.6f;
            t.SetPixels(changed);t.Apply();
            Same(Render("RenderCachedPreview",64,cache,false,null),Render("RenderAllLayers",64,48),"Source change invalidates cache");
            t.SetPixels(input);t.Apply();
            foreach(var edge in new[]{0,1,2,3})foreach(bool correction in new[]{false,true})
            {
                effect.quiltingEdges=(MakeSeamlessLayerBehaviour.PoissonEdges)edge;effect.quiltingSeamCorrection=correction;
                effect.quiltingSeed+=13;effect.quiltingChannels=MakeSeamlessLayerBehaviour.QuiltingChannels.Independent;
                effect.quiltingWidth=.35f;effect.quiltingFeather=7;effect.quiltingPoissonEdges=(MakeSeamlessLayerBehaviour.PoissonEdges)edge;
                Same(Render("RenderCachedPreview",64,cache,false,null),Render("RenderAllLayers",64,48),"Settings invalidate cache");
            }
            effect.quiltingSeamCorrection=false;
            effect.quiltingEdges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;
            expected=Render("RenderAllLayers",64,48);
            var group=new GroupLayerBehaviour{compositing=GroupCompositing.Isolated};doc.layers.Remove(source.Owner);group.layers.Add(source);doc.layers.Add(group);effect.TargetLayerId=group.Id;
            Call(doc,"NormalizeModel");Same(Render("RenderAllLayers",64,48),expected,"Group target");
            effect.clippingMask=true;Same(Render("RenderLayerPreview",effect.Owner,64),expected,"Clipped target");effect.clippingMask=false;
            for(int mask=0;mask<16;mask++)
            {
                effect.processRed=(mask&1)!=0;effect.processGreen=(mask&2)!=0;effect.processBlue=(mask&4)!=0;effect.processAlpha=(mask&8)!=0;
                var a=Render("RenderLayerPreview",effect.Owner,64);
                for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++)if((mask&(1<<c))==0)Check(Math.Abs(a[i][c]-input[i][c])<.005,"Channel bypass");
            }
            effect.processRed=effect.processGreen=effect.processBlue=effect.processAlpha=true;
            string json=(string)typeof(WhimTexApi).GetMethod("WritePortableClipboard",F).Invoke(null,new object[]{doc,doc.layers});
            var clipboard=(IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard",F).Invoke(null,new object[]{json,64,48});
            using(clipboard)
            {
                var decoded=(TextureCompositor)clipboard.GetType().GetField("Document",F).GetValue(clipboard);
                var saved=(MakeSeamlessLayerBehaviour)decoded.layers[0].Behaviour;
                Check(saved.mode==effect.mode&&saved.quiltingWidth==effect.quiltingWidth&&saved.quiltingSeed==effect.quiltingSeed&&saved.quiltingChannels==effect.quiltingChannels,"Portable roundtrip");
                Check(saved.quiltingContrastCompensation==compensate&&Math.Abs(saved.quiltingContrast-.37f)<1e-6f,"Contrast portable roundtrip");
                Check(saved.quiltingAlongSearch==alongSearch,"Along search portable roundtrip");
            }
            window=ScriptableObject.CreateInstance<EditorWindow>();window.Show();
            var bindings=Activator.CreateInstance(typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            Action<string,Action> apply=(label,change)=>{Undo.RegisterCompleteObjectUndo(doc,label);change();Call(bindings,"Refresh",true);};
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",F).Invoke(null,new object[]{window.rootVisualElement,effect,doc,apply,bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var root=window.rootVisualElement;
            var along=root.Q<Slider>("quiltingAlongSearch");
            Check(along.lowValue==0&&along.highValue==25,"Along search UI range");
            along.value=12.5f;Check(effect.quiltingAlongSearch==.125f,"Along search percent conversion");
            var compensation=root.Q<Slider>("quiltingContrast");
            Check(compensation.ClassListContains("whimtex-hidden")==!compensate,"Contrast visibility");
            root.Q<Toggle>("quiltingContrastCompensation").value=true;
            Check(!compensation.ClassListContains("whimtex-hidden"),"Contrast enabled visibility");
            compensation.value=63;Check(Math.Abs(effect.quiltingContrast-.63f)<1e-6,"Contrast percentage UI");
            Check(!root.Q("quiltingOptions").ClassListContains("whimtex-hidden")&&root.Q("seamlessProcessingEdges").parent.ClassListContains("whimtex-hidden"),"Mode panels");
            var slider=root.Q<Slider>("quiltingFeather");float initial=effect.quiltingFeather;
            Check(slider.label=="Feather (%)"&&slider.lowValue==0&&slider.highValue==100,"Percentage Feather UI contract");
            using(var evt=PointerCaptureEvent.GetPooled(slider,null,PointerId.mousePointerId)){slider.SendEvent(evt);}
            slider.value=17;slider.value=19;
            Check(effect.quiltingFeather==initial,"Slider defers model changes while captured");
            using(var evt=PointerCaptureOutEvent.GetPooled(slider,null,PointerId.mousePointerId)){slider.SendEvent(evt);}
            Check(effect.quiltingFeather==19,"Slider commits on release");
            slider.value=100;Check(effect.quiltingFeather==100,"Feather accepts maximum percent");
            root.Q<EnumField>("quiltingQuality").value=MakeSeamlessLayerBehaviour.QuiltingQuality.High;
            Check(effect.quiltingQuality==MakeSeamlessLayerBehaviour.QuiltingQuality.High,"Quality UI");
            root.Q<EnumField>("quiltingChannels").value=MakeSeamlessLayerBehaviour.QuiltingChannels.Linked;
            Check(effect.quiltingChannels==MakeSeamlessLayerBehaviour.QuiltingChannels.Linked,"Matching UI");
            root.Q<IntegerField>("quiltingSeed").value=123;
            Check(effect.quiltingSeed==123,"Seed UI");
            void Click(string id){var b=root.Q<Button>(id);b.clickable.GetType().GetMethod("Invoke",F).Invoke(b.clickable,new object[]{null});}
            Click("quiltingRandomSeed");Check(effect.quiltingSeed!=123,"Random seed changes model");
            effect.quiltingEdges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;Call(bindings,"Refresh",true);
            Click("quiltingEdges-top");
            Check(effect.quiltingEdges==MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight,"Paired quilting edges");
            Check(root.Q<Button>("quiltingEdges-left").enabledSelf&&root.Q<Button>("quiltingEdges-right").enabledSelf,"Last pair remains interactive");
            Click("quiltingEdges-bottom");Check(effect.quiltingEdges==MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,"Pair restored together");
            root.Q<Toggle>("quiltingSeamCorrection").value=true;
            Check(effect.quiltingSeamCorrection&&!root.Q("quiltingPoissonEdges").ClassListContains("whimtex-hidden"),"Optional correction controls");
            effect.quiltingPoissonEdges=MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges;Call(bindings,"Refresh",true);
            Click("quiltingPoissonEdges-left");
            Check(effect.quiltingPoissonEdges==MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom&&effect.quiltingEdges==MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,"Independent correction edges");
            Undo.IncrementCurrentGroup();
            float prior=effect.quiltingWidth;root.Q<Slider>("quiltingWidth").value=27;
            Check(Math.Abs(effect.quiltingWidth-.27f)<1e-6,"UI width percent");
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();Check(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingWidth==prior,"UI undo");
            Undo.PerformRedo();Check(Math.Abs(((MakeSeamlessLayerBehaviour)doc.layers[0].Behaviour).quiltingWidth-.27f)<1e-6,"UI redo");
            return "Patch Quilting integration: "+checks+" checks passed.";
        }
        finally{if(window!=null)window.Close();if(focus!=null)focus.Focus();cache.Dispose();Undo.RevertAllDownToGroup(undo);Object.DestroyImmediate(doc);Object.DestroyImmediate(t);}
    }
}
