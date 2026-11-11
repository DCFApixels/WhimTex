using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class CausticsDispersionTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type WT(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string n) => o.GetType().GetField(n,F)?.GetValue(o) ?? o.GetType().GetProperty(n,F)?.GetValue(o);
    static object Call(object o,string n,params object[] a) => o.GetType().GetMethod(n,F).Invoke(o,a);
    static object Static(string type,string n,params object[] a) => WT(type).GetMethod(n,F).Invoke(null,a);
    static ShaderGraphNode Node(ShaderGraph g,ShaderGraphNodeKind kind)
    { var n=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,Vector2.zero); g.nodes.Add(n); return n; }
    static void Edge(ShaderGraph g,ShaderGraphNode a,string output,ShaderGraphNode b,string input)
        => g.edges.Add(new ShaderGraphEdge {outputNode=a.id,outputPort=output,inputNode=b.id,inputPort=input});
    static void Set(ShaderGraphNode n,string p,float x) => n.FindInput(p).value=Vector4.one*x;
    static double Error(Color[] a,Color[] b,int channel=-1)
    {
        double error=0;
        for(int i=0;i<a.Length;i++) for(int c=channel<0?0:channel;c<(channel<0?4:channel+1);c++)
            error=Math.Max(error,Math.Abs(a[i][c]-b[i][c]));
        return error;
    }
    public static string Start(string id,int testVariant)
    {
        var job=AsyncFixture.Create(id);
        job.Worker=FixtureContext.RunAsync(job,async token =>
        {
            var previousTarget=RenderTexture.active;
            try
            {
            var t=job.Context; const int size=48;
            var doc=job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width=doc.height=size;
            var image=job.Scope.Own(new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear));image.Create();
            var cpu=job.Scope.Own(new Texture2D(size,size,TextureFormat.RGBAFloat,false,true));
            var context=Activator.CreateInstance(WT("LayerRenderContext"),F,null,new object[]{doc,null,size,size,1f,false,false,false},null);
            Color[] Read(RenderTexture target=null)
            { var prev=RenderTexture.active; try{RenderTexture.active=target??image;cpu.ReadPixels(new Rect(0,0,size,size),0,0);return cpu.GetPixels();}finally{RenderTexture.active=prev;} }
            Color[] Native(object runtime,ShaderGraph g)
            {
                Call(runtime,"Bind",g,context,null,false,false); bool srgb=GL.sRGBWrite;
                try{GL.sRGBWrite=false;Call(runtime,"Render",Texture2D.blackTexture,image,context);return Read();}finally{GL.sRGBWrite=srgb;}
            }
            IDisposable Compile(ShaderGraph g,string node,string port)
            {
                Static("ShaderGraphRevision","Invalidate",g);
                var rt=(IDisposable)Static("ShaderProgramRuntime","Create",g,node,port,false);
                t.True(rt!=null,"Native compilation");
                t.Equal(0,((IEnumerable)Get(rt,"DiagnosticMessages")).Cast<object>().Count(),"Clean compiler diagnostics: "+string.Join("; ",((IEnumerable)Get(rt,"DiagnosticMessages")).Cast<object>()));
                return rt;
            }
            ShaderParameter Spectrum(ShaderGraphNode node, Color a, Color b)
            {
                var gradient=new WhimTexGradient { ColorSpace=ColorSpace.Linear, ColorMode=WhimTexGradientColorMode.Linear,
                    Interpolation=WhimTexGradientInterpolation.Blend };
                gradient.SetKeys(new[]{new GradientColorKey(a,0),new GradientColorKey(b,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                var input=node.FindInput("Spectrum");
                if(input==null)node.inputs.Add(input=new ShaderGraphInput{port="Spectrum",type=ShaderGraphValueType.Gradient});
                input.resource=new ShaderParameter{type=ShaderParameterType.Gradient,gradientValue=gradient};
                return input.resource;
            }
            async Task PaletteChecks(ShaderGraph g, ShaderGraphNode n, bool hybrid)
            {
                Set(n,"Dispersion",0);
                var palette=Spectrum(n,new Color(.2f,.5f,.8f),new Color(.2f,.5f,.8f));
                using var scalar=Compile(g,n.id,"Value");var mono=Native(scalar,g);
                using(var balanced=Compile(g,n.id,"Color"))
                    t.Near(0,Error(mono,Native(balanced,g)),.00005,"White balance removes uniform palette tint");
                n.causticsWhiteBalance=false;
                using(var tinted=Compile(g,n.id,"Color"))
                {
                    var actual=Native(tinted,g);var expected=mono.Select(c=>new Color(c.r*.2f,c.r*.5f,c.r*.8f,1)).ToArray();
                    t.Near(0,Error(expected,actual),.0007,"Disabled balance preserves constant tint, including cached buffers");
                }
                // Connect the same resource through a parameter and edit it without changing topology.
                var parameter=Node(g,ShaderGraphNodeKind.Parameter);palette.name="SpectrumTest";g.parameters.Add(palette);parameter.parameterId=palette.id;
                Edge(g,parameter,"Out",n,"Spectrum");
                using var connected=Compile(g,n.id,"Color");var old=Native(connected,g);
                palette.gradientValue.SetKeys(new[]{new GradientColorKey(new Color(.8f,.3f,.1f),0),new GradientColorKey(new Color(.1f,.6f,.9f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                Set(n,"Dispersion",.4f);var color=Native(connected,g);
                t.True(Error(old,color)>.001,"Connected palette edits invalidate cached color");
                object[] build=hybrid?new object[]{g,n.id,"Color",false,null}:new object[]{g,n.id,"Color",null};
                t.True((bool)Static("ShaderGraphPreviewVM",hybrid?"TryCreateHybrid":"TryCreate",build),"Custom spectrum supports SVM");
                using var vm=(IDisposable)build[hybrid?4:3];
                if(hybrid)
                {
                    var owner=job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());owner.width=owner.height=size;
                    Layer layer=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=g};owner.layers.Add(layer);Call(owner,"NormalizeModel");
                    Call(vm,"ConfigureHybrid",owner,layer,false,true);
                }
                await Ready(vm);
                t.True((bool)(hybrid?Call(vm,"RenderBoundaries",image,context):Call(vm,"Render",image,0f)),"Custom spectrum SVM renders");
                t.Near(0,Error(color,Read()),.0007,"Custom spectrum native/SVM parity");
                t.Near(0,Error(mono,Native(scalar,g)),.00003,"Spectrum and White Balance leave Value unchanged");
                palette.gradientValue.SetKeys(new[]{new GradientColorKey(Color.black,0),new GradientColorKey(Color.black,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
                n.causticsWhiteBalance=true;
                using(var black=Compile(g,n.id,"Color"))
                    t.True(Native(black,g).All(c=>c.r==0&&c.g==0&&c.b==0&&c.a==1),"Black spectrum stays finite and black");
                g.edges.RemoveAll(e=>e.outputNode==parameter.id);g.nodes.Remove(parameter);g.parameters.Remove(palette);n.inputs.Remove(n.FindInput("Spectrum"));
                Set(n,"Dispersion",.2f);
            }
            async Task Ready(object vm)
            {
                var start=DateTime.UtcNow;
                while(!(bool)Call(vm,"UpdatePreparation"))
                {
                    t.True(!(bool)Get(vm,"Failed"),"SVM preparation did not fail");
                    if((DateTime.UtcNow-start).TotalSeconds>90) throw new TimeoutException("SVM preparation");
                    await Task.Delay(25,token);
                }
            }
            for(int variant=testVariant;variant<Math.Min(testVariant+1,8);variant++)
            {
                var g=new ShaderGraph(); var n=Node(g,ShaderGraphNodeKind.CausticsNoise);
                n.option=variant%2; n.noisePeriodic=(variant&2)!=0;n.noiseFiltering=(variant&4)!=0;
                Set(n,"Evolution",.73f);Set(n,"Strength",1.4f);Set(n,"Dispersion",.18f);
                var output=Node(g,ShaderGraphNodeKind.Output);Edge(g,n,"Color",output,"RGBA");
                using var native=Compile(g,n.id,"Color"); using var scalar=Compile(g,n.id,"Value");
                var scalarReference=Native(scalar,g);
                object[] build={g,n.id,"Color",null};
                t.True((bool)Static("ShaderGraphPreviewVM","TryCreate",build),"Caustics Color uses SVM");
                using var vm=(IDisposable)build[3];await Ready(vm);
                foreach(float dispersion in new[]{0f,.18f,-1f,1f,2f,8f})
                {
                    Set(n,"Dispersion",dispersion);
                    var actual=Native(native,g);var mono=Native(scalar,g);
                    t.Near(0,Error(scalarReference,mono),.00002,"Dispersion does not change Value "+variant);
                    t.True(actual.All(p=>float.IsFinite(p.r)&&float.IsFinite(p.g)&&float.IsFinite(p.b)&&p.a==1),"Finite RGB, opaque alpha");
                    if(dispersion<=0) t.Near(0,Error(actual,mono),.00002,"Zero/negative dispersion is monochrome");
                    else t.True(actual.Any(p=>Math.Abs(p.r-p.b)>.002),"Dispersion separates the channels");
                    t.True((bool)Call(vm,"TryUpdateValues",g,null),"Update dispersion without rebuilding VM");
                    t.True((bool)Call(vm,"Render",image,0f),"SVM rendered");
                    t.Near(0,Error(actual,Read()),.0002,"Native/SVM parity "+variant+" d="+dispersion);
                }
                Set(n,"Dispersion",1);var unit=Native(native,g);Set(n,"Dispersion",2);
                t.True(Error(unit,Native(native,g))>.002,"Dispersion above one continues separating noise samples");
                if(n.noiseFiltering)
                {
                    Set(n,"Scale",100000);var filtered=Native(native,g);
                    t.True(filtered.All(p=>Math.Abs(p.r-.5f)<.00002&&Math.Abs(p.g-.5f)<.00002&&Math.Abs(p.b-.5f)<.00002),"Unresolved spectrum filters to neutral gray");
                    t.True((bool)Call(vm,"TryUpdateValues",g,null),"Filtering value update");Call(vm,"Render",image,0f);
                    t.Near(0,Error(filtered,Read()),.0002,"Filtered neutral native/SVM parity");Set(n,"Scale",8);
                }
                if(n.noisePeriodic)
                {
                    var input=n.FindInput("Position");
                    if(input==null){input=new ShaderGraphInput{port="Position",type=ShaderGraphValueType.Float2};n.inputs.Add(input);}
                    input.source=ShaderGraphInputSource.Value;input.value=new Vector4(.231f,.417f,0,0);
                    using var point=Compile(g,n.id,"Color");var first=Native(point,g);
                    input.value+=new Vector4(1,0,0,0);t.Near(0,Error(first,Native(point,g)),.0005,"X periodic RGB");
                    input.value+=new Vector4(-1,1,0,0);t.Near(0,Error(first,Native(point,g)),.0005,"Y periodic RGB");
                }
                if(variant<=1) await PaletteChecks(g,n,false);
                // A disconnected output must not drag its malformed input branch into Value.
                var bad=Node(g,ShaderGraphNodeKind.Expression);bad.expression="1 +";Edge(g,bad,"Out",n,"Dispersion");
                using var isolated=Compile(g,n.id,"Value");
                var compilation=Call(Static("ShaderGraphCompiler","Prepare",g,false),"Compile",n.id,"Color",false);
                t.True(((string)Get(compilation,"source")).Contains("1 +"),"Color includes its dispersion branch for HLSL validation");
            }
            if(testVariant==10)
            {
                foreach(bool projected in new[]{false,true})
                {
                    var owner=job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());owner.width=owner.height=size;
                    var g=new ShaderGraph();var noise=Node(g,ShaderGraphNodeKind.CausticsNoise);noise.noisePeriodic=true;
                    var source=noise;
                    if(projected){source=Node(g,ShaderGraphNodeKind.Caustics);source.bufferResolution=ShaderGraphBufferResolution.Custom;source.bufferSize=new Vector2Int(size,size);Edge(g,noise,"Value",source,"Height");}
                    Set(source,"Dispersion",2);var output=Node(g,ShaderGraphNodeKind.Output);Edge(g,source,"Color",output,"RGBA");
                    var behaviour=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=g};
                    var layer=new Layer(behaviour){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
                    owner.layers.Add(layer);Call(owner,"NormalizeModel");t.True((bool)Call(behaviour,"CompileGraph",owner),"Layer graph compiles");
                    var sentinel=job.Scope.Own(new RenderTexture(8,8,0));sentinel.Create();
                    Color[] Canvas()
                    {
                        var old=RenderTexture.active;bool srgb=GL.sRGBWrite;RenderTexture result=null;
                        try
                        {
                            RenderTexture.active=sentinel;GL.sRGBWrite=true;result=(RenderTexture)Call(owner,"RenderCanvas",size);
                            t.True(RenderTexture.active==sentinel&&GL.sRGBWrite,"Composite restores caller graphics state");
                            RenderTexture.active=result;cpu.ReadPixels(new Rect(0,0,size,size),0,0);return cpu.GetPixels();
                        }
                        finally{RenderTexture.active=old;GL.sRGBWrite=srgb;if(result!=null)RenderTexture.ReleaseTemporary(result);}
                    }
                    var reference=Canvas();t.True(reference.Any(p=>Math.Abs(p.r-p.b)>.001),"Composite contains spectral color");
                    var thumb=(Texture2D)Call(owner,"GetLayerThumbnail",layer,size,false);
                    t.True(thumb!=null&&thumb.GetPixels().Any(p=>Math.Abs(p.r-p.b)>.001),"Thumbnail contains spectral color");
                    var group=new Layer(new GroupLayerBehaviour()){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};group.children.Add(layer);
                    owner.layers.Clear();owner.layers.Add(group);Call(owner,"NormalizeModel");
                    foreach(var mode in new[]{GroupCompositing.PassThrough,GroupCompositing.Isolated})
                    {group.compositing=mode;t.Near(0,Error(reference,Canvas()),.003,"Group preserves caustics spectrum");}
                    var exported=job.Scope.Own((Texture2D)Call(owner,"RasterizeLayer",group,true));
                    t.Near(0,Error(reference,exported.GetPixels()),.003,"Rasterized export preserves spectrum");
                    var reader=new ShaderLayerBehaviour{mode=ShaderProgramMode.Code,draftCode=ShaderLayerBehaviour.PassthroughCode,inputMode=ShaderLayerInputMode.Layer,inputLayerId=group.Id};
                    var target=new Layer(reader){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
                    owner.layers.Insert(0,target);group.enabled=false;Call(owner,"NormalizeModel");t.True((bool)Call(reader,"CompileCode",owner),"Layer input reader compiles");
                    t.Near(0,Error(reference,Canvas()),.003,"Hidden group as Layer input preserves spectrum");
                    target.enabled=false;group.enabled=true;group.clippingMask=true;
                    owner.layers.Add(new Layer(new ColorFillLayerBehaviour{color=new Color(1,1,1,.4f)}));Call(owner,"NormalizeModel");
                    var clipped=Canvas();t.True(clipped.All(p=>Math.Abs(p.a-.4f)<.003),"Clipping retains base alpha");
                    for(int i=0;i<reference.Length;i++)reference[i].a=.4f;
                    t.Near(0,Error(reference,clipped),.003,"Clipping preserves spectral RGB");
                }
            }
            if(testVariant==9)
            {
                var g=new ShaderGraph();var n=Node(g,ShaderGraphNodeKind.CausticsNoise);
                n.noisePeriodic=n.noiseFiltering=true;Set(n,"Dispersion",2);
                var fractal=Node(g,ShaderGraphNodeKind.Fractal);Edge(g,n,"Color",fractal,"In");
                var output=Node(g,ShaderGraphNodeKind.Output);Edge(g,fractal,"Out",output,"RGBA");
                using var native=Compile(g,null,"Out");var expected=Native(native,g);
                object[] build={g,null,"Out",null};t.True((bool)Static("ShaderGraphPreviewVM","TryCreate",build),"RGB Fractal uses SVM");
                using var vm=(IDisposable)build[3];await Ready(vm);
                t.True((bool)Call(vm,"Render",image,0f),"RGB Fractal rendered");
                t.Near(0,Error(expected,Read()),.0003,"RGB Fractal HLSL/SVM parity");
            }
            if(testVariant==8)
            {
                var g=new ShaderGraph();var uv=Node(g,ShaderGraphNodeKind.UV);
                var height=Node(g,ShaderGraphNodeKind.Expression);height.valueType=ShaderGraphValueType.Float2;
                height.expression="sin(A.x*12.5663706)*cos(A.y*12.5663706)";height.inputs.Clear();Edge(g,uv,"Out",height,"A");
                var n=Node(g,ShaderGraphNodeKind.Caustics);n.bufferResolution=ShaderGraphBufferResolution.Custom;n.bufferSize=new Vector2Int(size,size);n.wrap=ShaderGraphWrap.Repeat;
                Set(n,"Dispersion",.2f);Edge(g,height,"Out",n,"Height");
                var sum=Node(g,ShaderGraphNodeKind.Add);Edge(g,n,"Color",sum,"A");Edge(g,n,"Value",sum,"B");
                var output=Node(g,ShaderGraphNodeKind.Output);Edge(g,sum,"Out",output,"RGBA");
                using var native=Compile(g,n.id,"Color");using var scalar=Compile(g,n.id,"Value");
                var scalarReference=Native(scalar,g);
                foreach(float d in new[]{-1f,0f,.1f,.2f,1f,2f,8f})
                {
                    Set(n,"Dispersion",d);var color=Native(native,g);var mono=Native(scalar,g);
                    t.Near(0,Error(scalarReference,mono),.00003,"Projected Value is independent of dispersion");
                    t.True(color.All(p=>float.IsFinite(p.r)&&float.IsFinite(p.g)&&float.IsFinite(p.b)&&p.r>=0&&p.g>=0&&p.b>=0&&p.a==1),"Finite, nonnegative projected RGB");
                    if(d<=0)t.Near(0,Error(color,mono),.00003,"Zero/negative dispersion projection is monochrome");
                    else t.True(color.Any(p=>Math.Abs(p.r-p.b)>.001),"Refraction disperses channels");
                }
                Set(n,"Dispersion",1);var unit=Native(native,g);Set(n,"Dispersion",2);
                t.True(Error(unit,Native(native,g))>.001,"Dispersion above one continues spectral refraction");
                Set(n,"Strength",.06f);
                var cie=Native(native,g);
                var buffers=(IEnumerable)Get(Get(native,"compilation"),"buffers");
                var pass=buffers.Cast<object>().Single(p=>(bool)Get(p,"causticsColor"));
                t.True(Get(pass,"causticsSpectrum")==null,"Default CIE/D65 does not bind a gradient");
                var signed=Read((RenderTexture)Call(native,"BufferTexture",(string)Get(pass,"textureName")));
                t.True(signed.Any(p=>Math.Min(p.r,Math.Min(p.g,p.b))<-.0001f),"CIE/D65 retains signed spectral contributions in the receiver");
                float Luminance(Color p)=>p.r*.212639f+p.g*.715169f+p.b*.072192f;
                t.Near(0,cie.Zip(signed,(a,b)=>Math.Abs(Luminance(a)-Luminance(b))).Max(),.0001,"Fitting signed RGB preserves luminance");
                Set(n,"Strength",.02f);
                Set(n,"IOR",1);var white=Native(native,g);
                t.True(white.All(p=>Math.Abs(p.r-p.g)<.00003&&Math.Abs(p.g-p.b)<.00003),"No refraction sums to neutral white");Set(n,"IOR",1.333f);
                Set(n,"Depth",0);white=Native(native,g);
                t.True(white.All(p=>Math.Abs(p.r-p.g)<.00003&&Math.Abs(p.g-p.b)<.00003),"Zero depth sums all spectral bands to neutral white");
                Set(n,"Depth",.0001f);var shallow=Native(native,g);
                t.True(shallow.Max(p=>Math.Max(p.r,Math.Max(p.g,p.b))-Math.Min(p.r,Math.Min(p.g,p.b)))<.001,"Vanishing depth has no persistent color cast");
                Set(n,"Depth",1);Set(n,"IOR",1.00001f);var weak=Native(native,g);
                t.True(weak.Max(p=>Math.Max(p.r,Math.Max(p.g,p.b))-Math.Min(p.r,Math.Min(p.g,p.b)))<.001,"Vanishing refraction has no persistent color cast");
                Set(n,"IOR",1.333f);Set(n,"Intensity",2);
                t.True(Native(native,g).Any(p=>p.maxColorComponent>1),"Spectral resolve preserves HDR highlights");Set(n,"Intensity",.25f);
                var uvInput=new ShaderGraphInput{port="UV",type=ShaderGraphValueType.Float2,source=ShaderGraphInputSource.Value,value=new Vector4(.217f,.389f,0,0)};n.inputs.Add(uvInput);
                using(var point=Compile(g,n.id,"Color"))
                {
                    var first=Native(point,g);uvInput.value+=new Vector4(1,0,0,0);
                    t.Near(0,Error(first,Native(point,g)),.0001,"Projected spectral X repetition");
                    uvInput.value+=new Vector4(-1,1,0,0);
                    t.Near(0,Error(first,Native(point,g)),.0001,"Projected spectral Y repetition");
                }
                n.inputs.Remove(uvInput);Set(n,"Dispersion",.2f);
                await PaletteChecks(g,n,true);
                Set(n,"Dispersion",2);
                using var combined=Compile(g,null,"Out");var expected=Native(combined,g);
                object[] build={g,null,"Out",false,null};t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",build),"Caustics resumes SVM");
                using var vm=(IDisposable)build[4];
                var behaviour=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=g};Layer layer=behaviour;doc.layers.Add(layer);Call(doc,"NormalizeModel");
                Call(vm,"ConfigureHybrid",doc,layer,false,true);await Ready(vm);
                t.True((bool)Call(vm,"RenderBoundaries",image,context),"Hybrid renders both boundaries");
                t.Near(0,Error(expected,Read()),.0001,"Simultaneous Value/Color hybrid parity");
                n.causticsWhiteBalance=false;Spectrum(n,Color.cyan,Color.magenta);
                using var parsed=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc).Json,false);
                var restored=((ShaderLayerBehaviour)parsed.Document.layers[0].Behaviour).draftGraph;
                t.Equal(n.FindInput("Dispersion").value,restored.FindNode(n.id).FindInput("Dispersion").value,"Dispersion JSON roundtrip");
                t.Equal(2,restored.edges.Count(e=>e.outputNode==n.id),"Both output edges survive JSON");
                t.True(!restored.FindNode(n.id).causticsWhiteBalance,"White Balance JSON roundtrip");
                t.True(n.FindInput("Spectrum").resource.gradientValue.Equals(restored.FindNode(n.id).FindInput("Spectrum").resource.gradientValue),"Spectrum JSON roundtrip");
                string code="float4 ApplyFX(PinnedUV uv,float4 color){ WTNoiseSettings n=WTNoiseDefaults(1337); n.scale=float3(8,8,1); WTNoiseCausticsSettings c=WTNoiseCausticsDefaults(); return float4((WTNoiseCausticsColor((float2)uv,n,c,0.12)+WTNoiseCausticsColor((float2)uv,n,0.12))*0.5,1); }";
                var factory=typeof(ShaderFX).GetMethod("CreateAgentDraft",F,null,new[]{typeof(WhimTexDocument),typeof(string),typeof(System.Collections.Generic.List<ShaderParameter>)},null);
                var fx=job.Scope.Own((ShaderFX)factory.Invoke(null,new object[]{doc,code,new System.Collections.Generic.List<ShaderParameter>()}));
                Call(fx,"CompileAgentDraft");t.True((bool)Get(fx,"HasWorkingShader"),"Public WT Color overloads and automatic includes compile in FX");
            }
            }
            finally { RenderTexture.active=previousTarget; }
        });
        return job.Read();
    }
    public static string Poll(string id)=>AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id)=>AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id)=>AsyncFixture.Cleanup(id);
}
