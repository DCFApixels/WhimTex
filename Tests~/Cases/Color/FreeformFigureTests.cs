using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class FreeformFigureTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type T(string name) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(value, args);
    static object Static(Type type, string name, params object[] args) => type.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
    static object Json(string text) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true).GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    static Color[] Read(RenderTexture rt)
    {
        var previous = RenderTexture.active; var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); return cpu.GetPixels(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
    }
    static Color[] Render(WhimTexDocument doc)
    { var rt = (RenderTexture)Call(doc, "RenderCanvas", doc.width); try { return Read(rt); } finally { RenderTexture.ReleaseTemporary(rt); } }
    static float Error(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b), Mathf.Abs(a.a-b.a));
    static FreeformGradient Data(bool stroke)
    {
        var data = new FreeformGradient();
        for (int i = 0; i < data.points.Count; i++)
        {
            var p = data.points[i]; p.color = stroke ? new Color(.5f, 2, -.1f, .25f + i*.3f) : new Color(2-i*.7f, -.1f+i*.2f, .4f, .3f+i*.2f);
            p.influence = .2f+i*.15f; data.points[i] = p;
        }
        return data;
    }
    public static string Run() => TestContext.Run("Freeform figure fill/stroke, caches, HDR, storage and viewport", t =>
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 128; doc.height = 96; doc.outputPrecision = WhimTexOutputPrecision.Float32;
        var shape = new ShapeLayerBehaviour { fillMode = VectorFillMode.Freeform, strokeMode = VectorFillMode.Freeform,
            freeform = Data(false), strokeFreeform = Data(true), stroke = true, strokeWidth = 8, strokePosition = ShapeLayerBehaviour.StrokePosition.Inside,
            edgeMode = ShapeLayerBehaviour.EdgeMode.Step };
        var layer = new Layer(shape) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        layer.transform.scaleF = Vector2.one;
        doc.layers.Add(layer); Call(doc, "NormalizeModel");
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            foreach (bool vectorMode in new[] { false, true })
            {
                shape.stroke = false;
                var vector = ShapeToVector.Convert(shape, doc);
                t.True(!ReferenceEquals(vector.shapes[0].freeform, shape.freeform), "Conversion clones Freeform data");
                if (vectorMode) { vector.shapes[0].stroke = true; layer.SetBehaviour(vector); } else { shape.stroke = true; layer.SetBehaviour(shape); }
                var baseline = Render(doc);
                // Interior avoids fill/stroke overlap; near left edge samples the independent stroke only.
                foreach (var pixel in new[] { new Vector2Int(30,35), new Vector2Int(70,60), new Vector2Int(96,40) })
                {
                    Color expected = shape.freeform.Evaluate(new Vector2((pixel.x+.5f)/128,(pixel.y+.5f)/96));
                    t.True(Error(expected, baseline[pixel.y*128+pixel.x]) < .006f, "HDR fill oracle " + vectorMode + " expected=" + expected + " actual=" + baseline[pixel.y*128+pixel.x]);
                }
                if (!vectorMode)
                {
                    var expected = shape.strokeFreeform.Evaluate(new Vector2(2.5f/128,48.5f/96));
                    t.True(Error(expected, baseline[48*128+2]) < .006f, "Shape independent stroke oracle");
                }
                else
                {
                    var source = new Texture2D(128,96,TextureFormat.RGBAFloat,false,true);
                    try
                    {
                        var matrix = T("ProjectiveMatrix").GetProperty("Identity", F)?.GetValue(null) ?? T("ProjectiveMatrix").GetField("Identity",F).GetValue(null);
                        Static(T("VectorRasterizer"), "Render", vector, source, matrix, false);
                        t.True(source.GetPixels().Zip(baseline, Error).Max() < .008f, "CPU fallback matches GPU fill and overlapping translucent stroke");
                    }
                    finally { Object.DestroyImmediate(source); }
                    var geometry = Call(vector,"ComputeGeometryHash",false);
                    var data = vector.shapes[0].freeform; var point = data.points[0]; point.color = Color.blue; data.points[0] = point;
                    t.Equal(geometry,Call(vector,"ComputeGeometryHash",false),"Point paint changes retain coverage geometry");
                    t.True(Render(doc).Zip(baseline,Error).Max() > .1f,"Point edits invalidate Vector paint cache");
                    vector.shapes[0].freeform = shape.freeform.Clone(); baseline = Render(doc);
                    var style = vector.shapes[0].Clone(false); style.contours.Clear();
                    var boolean = new BooleanGroupLayerBehaviour { style=style, referenceSize=vector.referenceSize, edgeMode=VectorEdgeMode.Step };
                    layer.SetBehaviour(boolean); layer.children.Add(ShapeToVector.Convert(shape,doc).Owner);
                    t.True(Render(doc).Zip(baseline,Error).Max()<.008f,"Boolean Group retains independent Freeform style");
                    layer.children.Clear(); layer.SetBehaviour(vector);
                }
                var thumbnail = layer.Behaviour.GetPreviewTexture(48);
                t.True(thumbnail != null && baseline.Zip(Render(doc),Error).Max() < .001f,"Thumbnail preserves full image " + vectorMode);
                string fill = JsonUtility.ToJson(vectorMode ? vector.shapes[0].freeform : shape.freeform);
                string stroke = JsonUtility.ToJson(vectorMode ? vector.shapes[0].strokeFreeform : shape.strokeFreeform);
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    using var copy = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc,new WhimTexJsonWriteOptions {Mode=mode}).Json,false);
                    var restored = copy.Document.layers[0].Behaviour;
                    t.Equal(fill,JsonUtility.ToJson(restored is ShapeLayerBehaviour s ? s.freeform : ((VectorLayerBehaviour)restored).shapes[0].freeform),"JSON fill " + mode + "/" + vectorMode);
                    t.Equal(stroke,JsonUtility.ToJson(restored is ShapeLayerBehaviour ss ? ss.strokeFreeform : ((VectorLayerBehaviour)restored).shapes[0].strokeFreeform),"JSON stroke " + mode + "/" + vectorMode);
                }
                using (var container = new WhimTexDocumentContainer())
                {
                    var bytes = (byte[])Static(T("WhimTexDocumentSerializer"),"Serialize",doc,container);
                    var info = Static(T("WhimTexDocumentSerializer"),"Deserialize",bytes,container,typeof(WhimTexDocument),null,false);
                    var loaded = (WhimTexDocument)info.GetType().GetProperty("Model",F).GetValue(info);
                    try { t.True(Render(loaded).Zip(baseline,Error).Max() < .001f,"TIFF document block preserves rendered points " + vectorMode); }
                    finally { Object.DestroyImmediate(loaded); }
                }
                Undo.IncrementCurrentGroup(); Undo.RegisterCompleteObjectUndo(doc,"Freeform figure");
                if (vectorMode) vector.shapes[0].freeform.points.RemoveAt(0); else shape.freeform.points.RemoveAt(0);
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup(); Undo.PerformUndo(); layer = doc.layers[0];
                t.True(Render(doc).Zip(baseline,Error).Max() < .001f,"Undo restores exact rendered field " + vectorMode);
                Undo.PerformRedo(); Undo.PerformUndo(); layer = doc.layers[0]; Undo.ClearUndo(doc);
                shape = layer.Behaviour as ShapeLayerBehaviour ?? new ShapeLayerBehaviour {fillMode=VectorFillMode.Freeform,freeform=Data(false),strokeFreeform=Data(true),strokeMode=VectorFillMode.Freeform};
                var parent = new Layer(new GroupLayerBehaviour()) {colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR};
                doc.layers.Clear(); parent.children.Add(layer); doc.layers.Add(parent); Call(doc,"NormalizeModel");
                t.True(Render(doc).Zip(baseline,Error).Max() < .002f,"Group input preserves field " + vectorMode);
                var target = new Layer(new BlurLayerBehaviour {radius=0,strength=0,inputMode=EffectInputMode.Specific,TargetLayerId=parent.Id})
                    {colorRange=LayerColorRange.HDR,blendRange=LayerBlendRange.HDR,blendMode=BlendMode.Overwrite};
                doc.layers.Insert(0,target); Call(doc,"NormalizeModel");
                t.True(Render(doc).Zip(baseline,Error).Max() < .003f,"Target consumes group field " + vectorMode);
                doc.layers.Remove(target); var clip=new Layer(new ColorFillLayerBehaviour {color=Color.white}) {clippingMask=true};parent.children.Insert(0,clip);
                t.True(Render(doc).Zip(baseline,(a,b)=>Mathf.Abs(a.a-b.a)).Max()<.002f,"Clipping follows Freeform alpha " + vectorMode);
                parent.children.Remove(clip);doc.layers.Clear();doc.layers.Add(layer);Call(doc,"NormalizeModel");
                layer.transform.rotationF = 17; layer.transform.scaleF = new Vector2(-.8f,.7f);
                var full = Render(doc); var region = Activator.CreateInstance(T("CanvasRenderRegion"),F,null,new object[]{new RectInt(25,20,64,48),1},null);
                using(var cache=(IDisposable)Activator.CreateInstance(T("EffectRenderCache"),true))
                {
                    var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
                    try {var cropped=Read(rt);float error=0;for(int y=0;y<48;y++)for(int x=0;x<64;x++)error=Mathf.Max(error,Error(full[(y+20)*128+x+25],cropped[y*64+x]));t.True(error<.01f,"Native viewport matches transformed full render "+vectorMode+" error="+error);}
                    finally {RenderTexture.ReleaseTemporary(rt);}
                }
                layer.transform=TextureTransform.Default;
                var image=doc.ComposeCanvas();
                try
                {
                    t.True(image.GetPixels().Any(c=>c.r>1||c.g>1),"HDR export remains above one "+vectorMode);
                    var preview=new Texture2D(128,96,TextureFormat.RGBA32,false,true);
                    try {preview.SetPixels(image.GetPixels().Select(c=>c.gamma).ToArray());preview.Apply();string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/WhimTex/freeform-figure-"+vectorMode+".png"));File.WriteAllBytes(path,preview.EncodeToPNG());}
                    finally {Object.DestroyImmediate(preview);}
                }
                finally {Object.DestroyImmediate(image);}
                doc.width=doc.height=8192;
                using(var cache=(IDisposable)Activator.CreateInstance(T("EffectRenderCache"),true))
                {
                    region=Activator.CreateInstance(T("CanvasRenderRegion"),F,null,new object[]{new RectInt(3500,3700,256,192),1},null);
                    var rt=(RenderTexture)Call(doc,"RenderCanvasViewport",region,cache,false,null);
                    try {t.True(rt.width==256&&rt.height==192&&Read(rt).All(c=>float.IsFinite(c.r)),"8K native region bounded and finite "+vectorMode);}
                    finally {RenderTexture.ReleaseTemporary(rt);}
                }
                doc.width=128;doc.height=96;
            }
            layer.SetBehaviour(shape);
            Static(typeof(WhimTexApi),"SetLayer",doc,layer,Json("{\"shape\":{\"fillMode\":\"Freeform\",\"freeform\":{\"points\":[{\"position\":[0.2,0.3],\"color\":[2,-0.1,1,0.2],\"influence\":0.6}]}}}"));
            t.True(shape.freeform.points.Count==1 && shape.freeform.points[0].color.r==2,"Agent Shape point setting");
            bool rejected=false;
            try {Static(typeof(WhimTexApi),"SetText",new TextLayerBehaviour(),Json("{\"fillMode\":\"Freeform\"}"));} catch(TargetInvocationException) {rejected=true;}
            t.True(rejected,"Text still rejects Freeform");
            foreach(string name in new[]{"Shape","VectorPaint","Gradient"}) t.True(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/WhimTex/"+name)),"Shader compiled: "+name);
            t.True(RenderTexture.active==active&&GL.sRGBWrite==srgb,"Render state restored");
        }
        finally {Undo.ClearUndo(doc);Object.DestroyImmediate(doc);RenderTexture.active=active;GL.sRGBWrite=srgb;}
    });
}
