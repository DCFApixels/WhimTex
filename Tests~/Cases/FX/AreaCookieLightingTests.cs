using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class AreaCookieLightingTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, F).Invoke(obj, args);
    static Color[] Read(RenderTexture rt)
    {
        var prior = RenderTexture.active; var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
        finally { RenderTexture.active = prior; Object.DestroyImmediate(cpu); }
    }
    static Color[] Full(WhimTexDocument doc)
    { var cpu = doc.ComposeCanvas(); try { return cpu.GetPixels(); } finally { Object.DestroyImmediate(cpu); } }
    static void Near(TestContext t, Color expected, Color actual, float error, string label)
    { for (int i = 0; i < 4; i++) t.Near(expected[i], actual[i], error, label + " channel " + i); }

    // Dense Cartesian integration with disk rejection, independent of GPU Gaussian quadrature.
    static Color Reference(WhimTexLight light, Vector3 point, Vector3 normal, float specular = 0)
    {
        Quaternion frame = Quaternion.FromToRotation(Vector3.back, light.direction.normalized) * Quaternion.Euler(0, 0, light.rotation);
        Vector3 u = frame * Vector3.right, v = frame * Vector3.up;
        double sum = 0; int count = 0;
        for (int y = 0; y < 160; y++) for (int x = 0; x < 160; x++)
        {
            float a = (x + .5f) / 80 - 1, b = (y + .5f) / 80 - 1;
            if (light.areaShape == WhimTexAreaLightShape.Disk && a*a + b*b > 1) continue;
            Vector3 d = light.position + u * (a * light.size.x * .5f) + v * (b * (light.areaShape == WhimTexAreaLightShape.Disk ? light.size.x : light.size.y) * .5f) - point;
            float distance = d.magnitude; d /= distance;
            double response = Math.Max(Vector3.Dot(normal, d), 0);
            if (specular > 0)
            {
                Vector3 h = (d + Vector3.forward).normalized;
                response += specular * Math.Pow(Math.Max(Vector3.Dot(normal,h), 0), 2 / (.4*.4) - 2) * Math.Max(Vector3.Dot(normal,d),0);
            }
            sum += response * Math.Pow(Math.Max(1-distance/light.range,0),light.falloff) * Math.Max(Vector3.Dot(light.direction.normalized,-d),0);
            count++;
        }
        Color result = light.color * (float)(sum / count * light.intensity); result.a = 1; return result;
    }

    public static string Run() => TestContext.Run("Area quadrature, native 8K regions, optional Cookie atlas and independent shader variants", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = doc.height = 128;
        var effects = new List<ShaderFX>(); var owned = new List<Object>();
        var prior = RenderTexture.active; bool srgb = GL.sRGBWrite;
        using var cache = (IDisposable)Activator.CreateInstance(T("EffectRenderCache"), true);
        ShaderFX FX(string code)
        {
            var fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null).Invoke(null,new object[]{doc,code,new List<ShaderParameter>()});
            effects.Add(fx); Call(fx,"CompileAgentDraft");
            t.Equal("Compiled successfully.",(string)fx.GetType().GetProperty("Diagnostics",F).GetValue(fx),"No compile warnings"); return fx;
        }
        Color[] Region(RectInt r)
        {
            var region = Activator.CreateInstance(T("CanvasRenderRegion"), F, null,new object[]{r,1},null);
            var rt = (RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
            try { t.Equal(r.width,rt.width,"Native region width"); return Read(rt); }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }
        Texture2D Cookie(Color color)
        {
            var texture = new Texture2D(8,8,TextureFormat.RGBAHalf,false,true) { filterMode = FilterMode.Bilinear };
            owned.Add(texture); var p = new Color[64]; Array.Fill(p,color); texture.SetPixels(p); texture.Apply(); return texture;
        }
        try
        {
            var host = new Layer(new ColorFillLayerBehaviour { color = Color.white }) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            doc.layers.Add(host);
            var lighting = FX("float4 ApplyFX(PinnedUV uv,float4 c){return WTGlobalLighting(float3((float2)uv*_CanvasSize.xy,0),float4(1,1,1,1),float3(0,0,1),0,float3(1,1,1),.6,.4);}");
            host.fx.Add(lighting);
            var area = doc.AddLight(WhimTexLightType.Area);
            area.position = new Vector3(62,70,60); area.range = 512; area.size = new Vector2(90,38); area.color = new Color(3,1,.3f);
            t.Equal(Vector3.back,area.direction,"Area starts facing the canvas");
            t.True(lighting.IsPixelLocal,"Area remains pixel-local");
            foreach (var shape in new[]{WhimTexAreaLightShape.Rectangle,WhimTexAreaLightShape.Disk})
            foreach (var direction in new[]{Vector3.back,new Vector3(.3f,-.2f,-1),Vector3.forward})
            {
                area.areaShape=shape; area.direction=direction; area.rotation=27;
                var pixels=Full(doc);
                foreach (var xy in new[]{new Vector2Int(30,40),new Vector2Int(65,67),new Vector2Int(100,90)})
                    Near(t,Reference(area,new Vector3(xy.x+.5f,xy.y+.5f,0),Vector3.forward,.6f),pixels[xy.y*128+xy.x],.018f,"Area dense integral / shape / direction / HDR specular");
            }
            area.direction=Vector3.back; area.areaShape=WhimTexAreaLightShape.Rectangle;
            ShaderParameter Param(ShaderFX fx,string name)=>((List<ShaderParameter>)fx.GetType().GetProperty("parameters",F).GetValue(fx)).Find(p=>p.name==name);
            foreach(string name in new[]{"NormalLighting","BevelEmboss"})
            {
                var fx=FX(File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/"+name+".hlsl"));
                Param(fx,"_LightingMode").floatValue=1; Param(fx,"_BaseColor").colorValue=Color.white;
                Param(fx,"_ShadowColor").colorValue=Color.black; Param(fx,"_Ambient").floatValue=0;
                host.fx.Clear();
                if(name=="NormalLighting"){host.fx.Add(FX("float4 ApplyFX(float4 c){return float4(.5,.5,1,1);}"));Param(fx,"_InputEncoding").floatValue=0;}
                host.fx.Add(fx); Near(t,Reference(area,new Vector3(64.5f,64.5f,0),Vector3.forward),Full(doc)[64*128+64],.012f,name+" integrates Area");
            }
            var graph=new ShaderGraph(); var node=new ShaderGraphNode{kind=ShaderGraphNodeKind.NormalLighting,option=1};
            node.inputs.Add(new ShaderGraphInput{port="Ambient",type=ShaderGraphValueType.Float,value=Vector4.zero});
            var output=new ShaderGraphNode{kind=ShaderGraphNodeKind.Output}; graph.nodes.Add(node);graph.nodes.Add(output);
            graph.edges.Add(new ShaderGraphEdge{outputNode=node.id,inputNode=output.id,inputPort="RGBA"});
            var shader=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};
            doc.layers.Clear();doc.layers.Add(new Layer(shader){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR});
            t.True((bool)Call(shader,"CompileGraph",doc),"Area graph compiles");
            Near(t,Reference(area,new Vector3(64.5f,64.5f,0),Vector3.forward),Full(doc)[64*128+64],.012f,"Graph integrates Area");
            doc.layers.Clear();doc.layers.Add(host);host.fx.Clear();host.fx.Add(lighting);
            var full=Full(doc); var rect=new RectInt(30,40,64,64); var crop=Region(rect);
            for(int y=0;y<64;y+=7) for(int x=0;x<64;x+=9) Near(t,full[(40+y)*128+x+30],crop[y*64+x],.006f,"Area full/regional parity");
            area.size = new Vector2(150,90); crop=Region(rect);
            Near(t,Reference(area,new Vector3(62.5f,72.5f,0),Vector3.forward,.6f),crop[32*64+32],.02f,"Area size invalidates cached region");
            doc.width=doc.height=8192; area.position=new Vector3(4000,4200,900); area.size=new Vector2(1500,600); area.range=8192;
            crop=Region(new RectInt(3990,4190,64,64));
            Near(t,Reference(area,new Vector3(4000.5f,4200.5f,0),Vector3.forward,.6f),crop[10*64+10],.025f,"8K Area uses native canvas position");
            doc.width=doc.height=128; doc.lights.Clear();
            var spot=doc.AddLight(WhimTexLightType.Spot); spot.position=new Vector3(64,64,64); spot.range=512; spot.angle=90; spot.softness=0;
            var radiance=FX("float4 ApplyFX(PinnedUV uv,float4 c){WTLightSample l=WTSampleLight(0,float3((float2)uv*_CanvasSize.xy,0));return float4(l.color*l.attenuation,1);}");
            host.fx.Clear(); host.fx.Add(radiance);
            var plain=Full(doc); spot.cookie=Cookie(new Color(2,.5f,.25f,.5f)); var colored=Full(doc);
            Color expected=plain[64*128+64]*new Color(1,.25f,.125f,1);
            object Atlas()=>typeof(WhimTexDocument).GetField("lightCookieAtlas",F).GetValue(doc);
            var atlasTexture=(RenderTexture)Atlas().GetType().GetField("atlas",F).GetValue(Atlas());
            Near(t,new Color(2,.5f,.25f,.5f),Read(atlasTexture)[6*atlasTexture.width+6],.003f,"Cookie atlas stores unmodified linear HDR/RGBA");
            Near(t,expected,colored[64*128+64],.003f,"Cookie RGB/HDR and alpha multiply source radiance");
            t.True(Atlas()!=null,"Atlas exists only after Cookie use"); var existing=Atlas(); Full(doc); t.True(ReferenceEquals(existing,Atlas()),"Atlas reused across renders");
            spot.cookie.SetPixel(4,4,new Color(0,0,0,1)); spot.cookie.Apply();
            var edited=Region(new RectInt(60,60,16,16)); t.True(edited[4*16+4].r<colored[64*128+64].r,"Cookie pixel edit invalidates atlas and effect cache");
            var pattern=Cookie(Color.black); var patternPixels=new Color[64];
            for(int y=0;y<8;y++) for(int x=0;x<8;x++) patternPixels[y*8+x]=x<4?Color.red:Color.green;
            pattern.SetPixels(patternPixels); pattern.Apply(); spot.cookie=pattern;
            var split=Full(doc); t.True(split[64*128+32].r>split[64*128+32].g && split[64*128+96].g>split[64*128+96].r,"Cookie projection is not mirrored");
            spot.rotation=180; var rotated=Full(doc); t.True(rotated[64*128+32].g>rotated[64*128+32].r,"Cookie rotation changes projection");
            var second=doc.AddLight(WhimTexLightType.Spot); second.cookie=Cookie(Color.blue);
            var sum=FX("float4 ApplyFX(PinnedUV uv,float4 c){float3 sum=0;for(int i=0;i<WTLightCount();i++){WTLightSample l=WTSampleLight(i,float3((float2)uv*_CanvasSize.xy,0));sum+=l.color*l.attenuation;}return float4(sum,1);}");
            host.fx.Clear(); host.fx.Add(sum); var both=Full(doc); t.True(both[64*128+64].b>.01f,"Different Spot cookies coexist");
            doc.lights.Remove(second); host.fx.Clear(); host.fx.Add(radiance);
            spot.cookie=null; var none=Full(doc); t.True(Atlas()==null,"Removing final Cookie releases atlas");
            Near(t,plain[64*128+64],none[64*128+64],0,"No Cookie restores exact original pixels");
            spot.cookie=pattern; spot.enabled=false; Full(doc); t.True(Atlas()==null,"Disabled Cookie does not allocate atlas"); spot.enabled=true;
            doc.width=doc.height=8192; spot.position=new Vector3(4000,4200,900); spot.range=8192; spot.rotation=0;
            crop=Region(new RectInt(3550,4200,64,64)); t.True(crop[32*64+32].r>crop[32*64+32].g,"Cookie works on native 8K region");
            t.Equal(prior,RenderTexture.active,"Restores render target"); t.Equal(srgb,GL.sRGBWrite,"Restores color write state");
            // Missing asset identity must survive JSON and agent reads until the asset returns.
            doc.width=doc.height=128;
            var assets=AssetDatabase.FindAssets("t:Texture2D",new[]{"Packages/com.dcfapixels.whimtex"});
            t.True(assets.Length>0,"Package texture available for reference roundtrip");
            spot.cookie=AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(assets[0]));
            string json=WhimTexDocumentJson.Write(doc).Json;
            t.True(json.Contains("cookie"),"Cookie included in saved model");
            using(var restored=WhimTexDocumentJson.Read(json)) t.Equal(spot.cookie,restored.Document.lights[0].cookie,"Cookie asset identity roundtrip");
            doc.lights.Clear(); area=doc.AddLight(WhimTexLightType.Area); area.areaShape=WhimTexAreaLightShape.Disk; area.size=new Vector2(456,456); area.rotation=43;
            using(var restored=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc).Json))
                t.Equal(JsonUtility.ToJson(area),JsonUtility.ToJson(restored.Document.lights[0]),"Area settings roundtrip");
            var persistedCookie=doc.AddLight(WhimTexLightType.Spot);persistedCookie.cookie=spot.cookie;
            using(var container=new WhimTexDocumentContainer())
            {
                var serializer=T("WhimTexDocumentSerializer");
                byte[] payload=(byte[])serializer.GetMethod("Serialize",F).Invoke(null,new object[]{doc,container});
                container.Set(WhimTexDocumentContainer.DocumentBlock,payload);var composite=doc.ComposeCanvas();
                string path=Path.GetFullPath("Temp/WhimTex/area-cookie-"+Guid.NewGuid().ToString("N")+".tiff");
                try{File.WriteAllBytes(path,(byte[])T("WhimTexTiffCarrier").GetMethod("Write",F).Invoke(null,new object[]{container,composite,(bool?)false}));}
                finally{Object.DestroyImmediate(composite);}
                using var reopened=(WhimTexDocumentContainer)T("WhimTexTiffCarrier").GetMethod("OpenContainer",F).Invoke(null,new object[]{path});
                var value=serializer.GetMethod("Deserialize",F).Invoke(null,new object[]{reopened.Get(WhimTexDocumentContainer.DocumentBlock),reopened,typeof(WhimTexDocument),null,false});
                var restored=(WhimTexDocument)value.GetType().GetProperty("Model",F).GetValue(value);
                try{t.Equal(JsonUtility.ToJson(area),JsonUtility.ToJson(restored.lights[0]),"TIFF exact Area settings");t.Equal(persistedCookie.cookie,restored.lights[1].cookie,"TIFF Cookie asset reference");}
                finally{Object.DestroyImmediate(restored);}
            }
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc,"Area test"); area.size=new Vector2(700,700); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); t.Equal(456f,doc.lights[0].size.x,"Area size Undo"); Undo.PerformRedo(); t.Equal(700f,doc.lights[0].size.x,"Area size Redo");
        }
        finally
        {
            foreach(var fx in effects) if(fx!=null) Object.DestroyImmediate(fx);
            Undo.ClearUndo(doc); Object.DestroyImmediate(doc); foreach(var obj in owned) if(obj!=null) Object.DestroyImmediate(obj);
            RenderTexture.active=prior; GL.sRGBWrite=srgb;
        }
    });
}
