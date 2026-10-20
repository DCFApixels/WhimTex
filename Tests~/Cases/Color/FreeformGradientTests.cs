using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class FreeformGradientTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string n) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + n, true);
    static object Call(object o, string n, params object[] args) => o.GetType().GetMethods(F).Single(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(o, args);
    static object Static(string type, string n, params object[] args) => T(type).GetMethods(F).Single(m => m.Name == n && m.GetParameters().Length == args.Length).Invoke(null, args);
    static Color[] Read(RenderTexture rt)
    {
        var active = RenderTexture.active; var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false); return cpu.GetPixels(); }
        finally { RenderTexture.active = active; Object.DestroyImmediate(cpu); }
    }
    static float Error(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b),Mathf.Abs(a.a-b.a));
    static Color[] Render(WhimTexDocument doc)
    { var rt = (RenderTexture)Call(doc,"RenderCanvas",doc.width); try { return Read(rt); } finally { RenderTexture.ReleaseTemporary(rt); } }
    public static string Run() => TestContext.Run("Freeform GPU, HDR, persistence and render consumers", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 128; doc.height = 96;
        var gradient = new GradientLayerBehaviour { gradientType = GradientLayerBehaviour.GradientType.Freeform };
        var layer = new Layer(gradient) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        doc.layers.Add(layer); Call(doc,"NormalizeModel");
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/WhimTex/freeform",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        try
        {
            var data = gradient.freeform;
            data.points.Clear();
            data.points.Add(new FreeformGradientPoint(new Vector2(.25f,.5f),new Color(1,0,0,.2f)));
            data.points.Add(new FreeformGradientPoint(new Vector2(.75f,.5f),new Color(0,0,1,.8f)));
            t.True(Error(data.Evaluate(new Vector2(.5f,.5f)),new Color(.5f,0,.5f,.5f)) < .000001f,"Symmetric equal-weight linear RGBA midpoint");
            var stronger = data.points[0]; stronger.influence *= 2; data.points[0] = stronger;
            t.True(data.Evaluate(new Vector2(.5f,.5f)).r > .9f,"Influence expands a point's share");
            t.True(Error(data.Evaluate(stronger.position),new Color(1,0,0,.2f)) < .00001f,"Point color anchors the field");
            data.points.Add(new FreeformGradientPoint(new Vector2(.5f,.8f),new Color(-.5f,2,4,.0001f),.6f));
            foreach (int count in new[] { 3,64,1,3 })
            {
                while(data.points.Count < count) { int i = data.points.Count; data.points.Add(new FreeformGradientPoint(new Vector2((i%8+.5f)/8,(i/8+.5f)/8),new Color(i*.05f-1,.2f,2,.03f*(i%20)),.01f+i*.04f)); }
                while(data.points.Count > count) data.points.RemoveAt(data.points.Count-1);
                var context = Activator.CreateInstance(T("LayerRenderContext"), new object[] { doc,null,67,43,1f,false,false,null });
                var rt = (RenderTexture)Call(gradient,"Render",context);
                try
                {
                    var actual = Read(rt); float maximum = 0;
                    for(int y=0;y<43;y++) for(int x=0;x<67;x++)
                    {
                        var expected = data.Evaluate(new Vector2((x+.5f)/67,(y+.5f)/43));
                        maximum = Mathf.Max(maximum, Error(actual[y*67+x],expected) / Mathf.Max(1,Mathf.Abs(expected.r),Mathf.Abs(expected.g),Mathf.Abs(expected.b)));
                    }
                    t.True(maximum < .0011f,"Finished Half layer: 1/3/64 points, signed HDR and alpha: " + count + " error=" + maximum);
                }
                finally { RenderTexture.ReleaseTemporary(rt); }
                var material=new Material(Shader.Find("Hidden/WhimTex/Gradient"));
                var beforeRaw=RenderTexture.active;
                rt=RenderTexture.GetTemporary(67,43,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                try
                {
                    Call(data,"Bind",material,false);material.SetInt("_GradientType",6);material.SetInt("_UnboundedUv",0);
                    GL.sRGBWrite=false;Graphics.Blit(null,rt,material);
                    var actual=Read(rt);float maximum=0;
                    for(int y=0;y<43;y++)for(int x=0;x<67;x++)
                    {
                        var expected=data.Evaluate(new Vector2((x+.5f)/67,(y+.5f)/43));
                        maximum=Mathf.Max(maximum,Error(actual[y*67+x],expected)/Mathf.Max(1,Mathf.Abs(expected.r),Mathf.Abs(expected.g),Mathf.Abs(expected.b)));
                    }
                    t.True(maximum<.00005f,"Raw float shader precision, count="+count+" error="+maximum);
                }
                finally {RenderTexture.active=beforeRaw;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(material);GL.sRGBWrite=srgb;}
            }
            gradient.freeform = new FreeformGradient(); data = gradient.freeform;
            var preview = gradient.GetPreviewTexture(48);
            Color old = preview.GetPixel(12,12);
            var changed = data.points[0]; changed.color = Color.green; data.points[0] = changed;
            t.True(Error(old,gradient.GetPreviewTexture(48).GetPixel(12,12)) > .1f,"Point changes invalidate thumbnail");
            changed.color = new Color(2,-.2f,.6f,.37f); data.points[0] = changed;
            string expectedData = JsonUtility.ToJson(data);
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                string json = WhimTexDocumentJson.Write(doc,new WhimTexJsonWriteOptions { Mode = mode }).Json;
                using var copy = WhimTexDocumentJson.Read(json,false);
                t.Equal(expectedData,JsonUtility.ToJson(((GradientLayerBehaviour)copy.Document.layers[0].Behaviour).freeform),"JSON " + mode + " points");
            }
            gradient.gradientType = GradientLayerBehaviour.GradientType.Linear;
            using(var copy = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc,new WhimTexJsonWriteOptions {Mode=WhimTexJsonWriteMode.Compact}).Json,false))
                t.Equal(expectedData,JsonUtility.ToJson(((GradientLayerBehaviour)copy.Document.layers[0].Behaviour).freeform),"Inactive Freeform points retained");
            gradient.gradientType = GradientLayerBehaviour.GradientType.Freeform;
            var serialized = WhimTexDocumentJson.Write(doc).Json;
            var malformed = new System.Text.RegularExpressions.Regex("(\"influence\"\\s*:\\s*)[0-9.Ee+\\-]+").Replace(serialized,"${1}0",1);
            bool rejected = false;
            try { using var unused = WhimTexDocumentJson.Read(malformed,false); } catch(WhimTexDocumentException) { rejected = true; }
            t.True(rejected,"Invalid influence rejected before rendering");
            var set=typeof(WhimTexApi).GetMethod("SetLayer",F);
            var parse=set.GetParameters()[2].ParameterType.GetMethod("Parse",new[]{typeof(string)});
            var settings=parse.Invoke(null,new object[]{"{\"gradientType\":\"Freeform\",\"freeform\":{\"points\":[{\"position\":[0.3,0.7],\"color\":[3,-0.2,1,0.4],\"influence\":0.6}]}}"});
            set.Invoke(null,new object[]{doc,layer,settings});
            t.True(gradient.freeform.points.Count==1&&gradient.freeform.points[0].color.r==3&&gradient.freeform.points[0].color.g==-.2f,"Agent settings retain HDR points");
            gradient.freeform=data;
            string path = Path.Combine(folder,"freeform.tiff");
            using(var container=new WhimTexDocumentContainer())
            {
                container.Set(WhimTexDocumentContainer.DocumentBlock,(byte[])Static("WhimTexDocumentSerializer","Serialize",doc,container));
                var composite=doc.ComposeCanvas();
                try {File.WriteAllBytes(path,(byte[])Static("WhimTexTiffCarrier","Write",container,composite,(bool?)doc.outputSrgb));}
                finally {Object.DestroyImmediate(composite);}
                var loadArgs=new object[]{File.ReadAllBytes(path),null,null};
                var read=T("WhimTexTiffCarrier").GetMethods(F).Single(m=>m.Name=="TryRead"&&m.GetParameters()[0].ParameterType==typeof(byte[]));
                t.True((bool)read.Invoke(null,loadArgs),"TIFF carrier reopened: "+loadArgs[2]);
                using var reopened=WhimTexDocumentContainer.Parse((byte[])loadArgs[1]);
                var info=Static("WhimTexDocumentSerializer","Deserialize",reopened.Get(WhimTexDocumentContainer.DocumentBlock),reopened,typeof(WhimTexDocument),null,false);
                var loaded=(WhimTexDocument)info.GetType().GetProperty("Model",F).GetValue(info);
                try {t.Equal(expectedData,JsonUtility.ToJson(((GradientLayerBehaviour)loaded.layers[0].Behaviour).freeform),"TIFF retains points/HDR/alpha");}
                finally {Object.DestroyImmediate(loaded);}
            }
            var before = Render(doc);
            string id = layer.Id;
            Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc,"Freeform test");
            gradient.freeform.points.RemoveAt(1); Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Undo.PerformUndo(); gradient = (GradientLayerBehaviour)doc.layers[0].Behaviour; layer = gradient.Owner;
            t.Equal(expectedData,JsonUtility.ToJson(gradient.freeform),"Undo restores points exactly");
            Undo.PerformRedo(); t.Equal(2,((GradientLayerBehaviour)doc.layers[0].Behaviour).freeform.points.Count,"Redo retains order");
            Undo.PerformUndo(); gradient = (GradientLayerBehaviour)doc.layers[0].Behaviour; layer = gradient.Owner;
            var after = Render(doc); t.True(before.Zip(after,Error).Max() < .00001f,"Undo restores rendered HDR pixels");
            var group = new Layer(new GroupLayerBehaviour()) {compositing = GroupCompositing.Isolated,colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
            doc.layers.Clear(); group.children.Add(layer); doc.layers.Add(group); Call(doc,"NormalizeModel");
            t.True(before.Zip(Render(doc),Error).Max() < .001f,"Group input preserves Freeform");
            var target = new Layer(new BlurLayerBehaviour { radius=0,strength=0,inputMode=EffectInputMode.Specific,TargetLayerId=group.Id });
            doc.layers.Clear(); doc.layers.Add(target); doc.layers.Add(group); target.blendMode=BlendMode.Overwrite; Call(doc,"NormalizeModel");
            target.colorRange=LayerColorRange.HDR; target.blendRange=LayerBlendRange.HDR;
            t.True(before.Zip(Render(doc),Error).Max() < .002f,"Specific Target consumes Freeform group");
            doc.layers.Clear(); doc.layers.Add(layer); Call(doc,"NormalizeModel");
            var exported = doc.ComposeCanvas();
            try { t.Equal(128,exported.width,"Export uses document resolution"); t.True(exported.GetPixels().Any(c=>c.r>1),"HDR export retains highlights"); }
            finally {Object.DestroyImmediate(exported);}
            doc.width=doc.height=8192;
            var cache=(IDisposable)Activator.CreateInstance(T("EffectRenderCache"),true);
            try
            {
                var region=Activator.CreateInstance(T("CanvasRenderRegion"),F,null,new object[]{new RectInt(3500,3700,256,192),1},null);
                var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
                try {t.Equal(256,rt.width,"8K preview renders only the requested region");t.Equal(192,rt.height,"8K native region height");t.True(Read(rt).All(c=>float.IsFinite(c.r)&&float.IsFinite(c.a)),"8K finite HDR output");}
                finally{RenderTexture.ReleaseTemporary(rt);}
            }
            finally {cache.Dispose();}
            doc.width=512;doc.height=384;
            gradient.freeform=new FreeformGradient(); layer.colorRange=LayerColorRange.Standard;
            var image=doc.ComposeCanvas();
            try
            {
                var display=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false,true);
                try {display.SetPixels(image.GetPixels().Select(c=>c.gamma).ToArray());display.Apply();File.WriteAllBytes(Path.Combine(folder,"freeform.png"),display.EncodeToPNG());}
                finally {Object.DestroyImmediate(display);}
            }
            finally {Object.DestroyImmediate(image);}
            t.True(RenderTexture.active==active && GL.sRGBWrite==srgb,"Render callers preserve graphics state");
            Debug.Log("Freeform render artifacts: " + folder);
        }
        finally {Undo.ClearUndo(doc); Object.DestroyImmediate(doc);RenderTexture.active=active;GL.sRGBWrite=srgb;}
    });
}
