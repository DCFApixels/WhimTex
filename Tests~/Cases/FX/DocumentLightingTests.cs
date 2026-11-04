using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class DocumentLightingTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type TypeOf(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, F).Invoke(owner, args);
    static ShaderParameter Parameter(ShaderFX effect, string name) => ((List<ShaderParameter>)effect.GetType().GetProperty("parameters", F).GetValue(effect)).Find(p => p.name == name);
    static Color[] Read(RenderTexture texture)
    {
        var before = RenderTexture.active;
        var cpu = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = texture; cpu.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
        finally { RenderTexture.active = before; Object.DestroyImmediate(cpu); }
    }
    static Color[] Full(WhimTexDocument doc)
    {
        var cpu = doc.ComposeCanvas();
        try { return cpu.GetPixels(); } finally { Object.DestroyImmediate(cpu); }
    }
    static Color[] Region(WhimTexDocument doc, IDisposable cache, RectInt rect, TestContext t)
    {
        object region = Activator.CreateInstance(TypeOf("CanvasRenderRegion"), F, null, new object[] { rect, 1 }, null);
        var rt = (RenderTexture)Call(doc, "RenderCanvasViewport", region, cache, false, null);
        try { t.Equal(rect.width, rt.width, "Native regional width"); t.Equal(rect.height, rt.height, "Native regional height"); return Read(rt); }
        finally { RenderTexture.ReleaseTemporary(rt); }
    }
    static ShaderFX FX(WhimTexDocument doc, string code, List<ShaderFX> owned)
    {
        var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
            new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null).Invoke(null, new object[] { doc, code, new List<ShaderParameter>() });
        owned.Add(effect); Call(effect, "CompileAgentDraft"); return effect;
    }
    static void Near(TestContext t, Color a, Color b, string label, float tolerance = .008f)
    { for (int c = 0; c < 4; c++) t.Near(a[c], b[c], tolerance, label + " channel " + c); }

    public static string Run() => TestContext.Run("Document lighting, HDR, graph, storage and native 8K regions", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = doc.height = 128;
        var other = ScriptableObject.CreateInstance<WhimTexDocument>(); other.width = other.height = 128;
        var effects = new List<ShaderFX>();
        using var cache = (IDisposable)Activator.CreateInstance(TypeOf("EffectRenderCache"), true);
        string directory = Path.GetFullPath("Temp/WhimTex/lighting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var before = RenderTexture.active; bool beforeSrgb = GL.sRGBWrite;
        try
        {
            var normal = FX(doc, File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/NormalLighting.hlsl"), effects);
            var bevel = FX(doc, File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/BevelEmboss.hlsl"), effects);
            t.True(normal.IsPixelLocal, "Normal Lighting remains pixel-local");
            t.True(!bevel.IsPixelLocal, "Bevel keeps its spatial height dependency");
            foreach (var fx in new[] { normal, bevel })
            {
                t.Equal("Compiled successfully.", (string)fx.GetType().GetProperty("Diagnostics", F).GetValue(fx), "Preset compiles without warning");
                Parameter(fx, "_BaseColor").colorValue = Color.white;
                Parameter(fx, "_ShadowColor").colorValue = Color.black;
                Parameter(fx, "_Ambient").floatValue = 0;
            }
            Parameter(normal, "_InputEncoding").floatValue = 0;
            Parameter(normal, "_LightDirection").vectorValue = new Vector4(0, 0, 1, 0);
            var packed = FX(doc, "float4 ApplyFX(PinnedUV p,float4 c){return float4(.5,.5,1,.6);}", effects);
            var host = new Layer(new ColorFillLayerBehaviour()) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            doc.layers.Add(host); host.fx.Add(packed); host.fx.Add(normal);
            Near(t, new Color(1, 1, 1, .6f), Full(doc)[64*128+64], "Unchanged Local flat-normal result");
            var sun = doc.AddLight(WhimTexLightType.Directional); sun.direction = Vector3.forward; sun.color = new Color(3, .5f, .25f); sun.intensity = 2;
            var point = doc.AddLight(WhimTexLightType.Point); point.position = new Vector3(64, 64, 32); point.range = 128; point.color = new Color(.25f, 1, 4); point.intensity = 3;
            Parameter(normal, "_LightingMode").floatValue = 1;
            var both = Full(doc);
            Color Expected(float x, float y)
            {
                var offset = point.position - new Vector3(x, y, 0);
                float attenuation = Mathf.Pow(Mathf.Clamp01(1-offset.magnitude/point.range), point.falloff);
                var rgb = sun.color * sun.intensity + point.color * (point.intensity * attenuation * Mathf.Max(0, offset.normalized.z));
                rgb.a = .6f; return rgb;
            }
            Near(t, Expected(64.5f, 64.5f), both[64*128+64], "Two HDR lights sum in linear space");
            Near(t, Expected(16.5f, 32.5f), both[32*128+16], "Point direction and falloff depend on canvas position");
            var rect = new RectInt(48, 40, 32, 24);
            var crop = Region(doc, cache, rect, t);
            for (int y=0;y<rect.height;y++) for(int x=0;x<rect.width;x++)
                Near(t, both[(y+rect.y)*128+x+rect.x],crop[y*rect.width+x],"Crop matches complete output");
            point.enabled = false;
            var changed = Region(doc, cache, rect, t);
            Near(t, new Color(6,1,.5f,.6f), changed[0], "Lighting fingerprint invalidates regional cache");
            point.enabled = true;

            // Reusing the same FX asset in another document cannot leak source constants.
            var otherHost = new Layer(new ColorFillLayerBehaviour()) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
            other.layers.Add(otherHost); otherHost.fx.Add(packed); otherHost.fx.Add(normal);
            Near(t, new Color(0,0,0,.6f),Full(other)[0],"An unlit document resets material light count");
            Near(t,Expected(64.5f,64.5f),Full(doc)[64*128+64],"Original document restores its own lights");

            // Shared lighting must survive group/Target, clipping and thumbnail paths.
            host.fx.Remove(normal);
            Layer group = new GroupLayerBehaviour(); group.colorRange = LayerColorRange.HDR;
            group.blendRange = LayerBlendRange.HDR; group.children.Add(host); group.fx.Add(normal);
            doc.layers.Clear(); doc.layers.Add(group);
            Near(t,both[64*128+64],Full(doc)[64*128+64],"Lighting on group input");
            var thumbnail=(RenderTexture)Call(doc,"RenderAgentLayerPreview",group,128);
            try { Near(t,both[64*128+64],Read(thumbnail)[64*128+64],"HDR lighting thumbnail"); }
            finally { RenderTexture.ReleaseTemporary(thumbnail); }
            doc.layers.Add(new ColorFillLayerBehaviour { color=Color.white });
            var overWhite=Full(doc); group.clippingMask=true;
            Near(t,overWhite[64*128+64],Full(doc)[64*128+64],"Lighting with clipped group");
            group.clippingMask=false;doc.layers.RemoveAt(1);group.enabled=false;
            Layer target=new BlurLayerBehaviour { inputMode=EffectInputMode.Specific, TargetLayerId=group.Id, radius=0 };
            target.colorRange=LayerColorRange.HDR;target.blendRange=LayerBlendRange.HDR;doc.layers.Insert(0,target);
            Near(t,both[64*128+64],Full(doc)[64*128+64],"Lighting through Target input");
            doc.layers.Clear();doc.layers.Add(host);group.children.Clear();group.fx.Clear();host.fx.Add(normal);

            // Both modes must also compile and render on the height-based preset.
            host.fx.Clear(); host.fx.Add(bevel); Parameter(bevel,"_LightingMode").floatValue=1;
            Near(t, new Color(Expected(64.5f,64.5f).r,Expected(64.5f,64.5f).g,Expected(64.5f,64.5f).b,1),Full(doc)[64*128+64],"Flat Bevel global result");
            host.fx.Clear(); host.fx.Add(packed); host.fx.Add(normal);
            string json = WhimTexDocumentJson.Write(doc).Json;
            using (var restored=WhimTexDocumentJson.Read(json))
            {
                t.Equal(2, restored.Document.lights.Count,"JSON light count");
                t.Equal(point.id,restored.Document.lights[1].id,"JSON stable light ID");
                Near(t,both[64*128+64],Full(restored.Document)[64*128+64],"JSON render roundtrip");
            }
            string path=Path.Combine(directory,"lighting.tiff");
            using (var container=new WhimTexDocumentContainer())
            {
                var serializer=TypeOf("WhimTexDocumentSerializer");
                byte[] payload=(byte[])serializer.GetMethod("Serialize",F).Invoke(null,new object[]{doc,container});
                container.Set(WhimTexDocumentContainer.DocumentBlock,payload);
                var composite=doc.ComposeCanvas();
                try { File.WriteAllBytes(path,(byte[])TypeOf("WhimTexTiffCarrier").GetMethod("Write",F).Invoke(null,new object[]{container,composite,(bool?)false})); }
                finally { Object.DestroyImmediate(composite); }
                using var reopened=(WhimTexDocumentContainer)TypeOf("WhimTexTiffCarrier").GetMethod("OpenContainer",F).Invoke(null,new object[]{path});
                var decoded=serializer.GetMethod("Deserialize",F).Invoke(null,new object[]{reopened.Get(WhimTexDocumentContainer.DocumentBlock),reopened,typeof(WhimTexDocument),null,false});
                var reloaded=(WhimTexDocument)decoded.GetType().GetProperty("Model",F).GetValue(decoded);
                try
                {
                    t.Equal(2,reloaded.lights.Count,"TIFF light count"); t.Equal(point.id,reloaded.lights[1].id,"TIFF stable light ID");
                    t.Equal(point.position,reloaded.lights[1].position,"TIFF point position");
                    Near(t,point.color,reloaded.lights[1].color,"TIFF preserves HDR light color",0);
                }
                finally { Object.DestroyImmediate(reloaded); }
            }

            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc,"Lighting Test");
            doc.lights[1].position = new Vector3(12,15,16); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); t.Equal(new Vector3(64,64,32),doc.lights[1].position,"Undo restores light position");
            Undo.PerformRedo(); t.Equal(new Vector3(12,15,16),doc.lights[1].position,"Redo restores light position");
            Undo.ClearUndo(doc);

            // A true 8K document, with only a small native-resolution visible region read back.
            doc.width=doc.height=8192; point=doc.lights[1]; point.position=new Vector3(4096,4096,1024);point.range=8192;
            sun=doc.lights[0];
            rect=new RectInt(4032,4032,128,128);
            var timer=Stopwatch.StartNew(); crop=Region(doc,cache,rect,t); timer.Stop();
            Near(t,Expected(4096.5f,4096.5f),crop[64*128+64],"8K uses full-canvas coordinates and HDR");
            var report=new List<string>{"first native 128x128 region in 8K document: "+timer.Elapsed.TotalMilliseconds+" ms"};
            for(int i=0;i<5;i++) { point.position.x+=13;timer.Restart();crop=Region(doc,cache,rect,t);timer.Stop();report.Add("light edit "+i+": "+timer.Elapsed.TotalMilliseconds+" ms"); }
            for(int i=0;i<6;i++)
            {
                var additional=doc.AddLight(WhimTexLightType.Point);
                additional.position=new Vector3(1024+i*1000,1024+i*700,1024);additional.range=8192;additional.intensity=.25f;
            }
            var fence=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try
            {
                foreach(int mode in new[]{0,1})
                {
                    Parameter(normal,"_LightingMode").floatValue=mode;
                    var area=new RectInt(3584,3712,1024,768);
                    var view=Activator.CreateInstance(TypeOf("CanvasRenderRegion"),F,null,new object[]{area,1},null);
                    for(int iteration=0;iteration<4;iteration++)
                    {
                        point.position.x+=1;timer.Restart();
                        var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",view,cache,false,null);
                        var active=RenderTexture.active;
                        try
                        {
                            t.Equal(1024,rt.width,"8K visible render stays regional");t.Equal(768,rt.height,"8K regional height");
                            RenderTexture.active=rt;fence.ReadPixels(new Rect(rt.width/2,rt.height/2,1,1),0,0,false);fence.Apply(false,false);
                        }
                        finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
                        timer.Stop();if(iteration>0)report.Add((mode==0?"Local":"Global / 8 lights")+" 1024x768, includes 1px GPU fence: "+timer.Elapsed.TotalMilliseconds+" ms");
                    }
                }
                var fullPreview=(RenderTexture)Call(doc,"RenderCanvas",512);
                try{t.Equal(512,fullPreview.width,"Full 8K canvas overview remains bounded");t.Equal(512,fullPreview.height,"Bounded overview height");}
                finally{RenderTexture.ReleaseTemporary(fullPreview);}
            }
            finally{Object.DestroyImmediate(fence);}
            doc.lights.RemoveRange(2,doc.lights.Count-2);
            File.WriteAllLines(Path.Combine(directory,"timings.txt"),report);

            // Graph light nodes and Global mode share the same document binding.
            doc.width=doc.height=128; doc.layers.Clear();
            point.position=new Vector3(64,64,32);point.range=128;
            var graph=new ShaderGraph();
            var lighting=new ShaderGraphNode{kind=ShaderGraphNodeKind.NormalLighting,option=1};
            lighting.inputs.Add(new ShaderGraphInput{port="Ambient",type=ShaderGraphValueType.Float,value=Vector4.zero});
            var output=new ShaderGraphNode{kind=ShaderGraphNodeKind.Output};
            graph.nodes.Add(lighting);graph.nodes.Add(output);
            graph.edges.Add(new ShaderGraphEdge{outputNode=lighting.id,inputNode=output.id,inputPort="RGBA"});
            var shader=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};
            doc.layers.Add(new Layer(shader){colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR});
            bool compiled=(bool)Call(shader,"CompileGraph",doc);
            t.True(compiled,"Global Normal Lighting graph compiles: "+shader.GetType().GetProperty("Diagnostics",F).GetValue(shader));
            t.True(shader.UsesLighting,"Graph records lighting dependency");
            var expected=Expected(64.5f,64.5f);expected.a=1;
            Near(t,expected,Full(doc)[64*128+64],"Global graph output");
            foreach(var kind in new[]{ShaderGraphNodeKind.LightCount,ShaderGraphNodeKind.LightDirection,ShaderGraphNodeKind.LightColor,ShaderGraphNodeKind.LightAttenuation})
            {
                lighting.kind=kind; lighting.option=0;lighting.inputs.Clear();
                t.True((bool)Call(shader,"CompileGraph",doc),kind+" compiles");
                var pixels=Full(doc);t.True(float.IsFinite(pixels[0].r),kind+" renders finite output");
            }
            t.Equal(before,RenderTexture.active,"Caller render target restored");t.Equal(beforeSrgb,GL.sRGBWrite,"Caller sRGB state restored");
        }
        finally
        {
            Undo.ClearUndo(doc);
            other.layers.Clear(); Object.DestroyImmediate(other);
            foreach(var effect in effects)if(effect!=null)Object.DestroyImmediate(effect);
            Object.DestroyImmediate(doc);
            RenderTexture.active=before;GL.sRGBWrite=beforeSrgb;
        }
    });
}
