using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using Unity.Collections;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Actual Drawing source/consumer paths against an independent expanded dense File
// image with the same world-space pixels. No assets, windows, Undo or user models.
public static class TiledDrawingConsumerTests
{
    const int Side = 256;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(object owner, string name, params object[] args)
    {
        var type = owner as Type ?? owner.GetType();
        var method = type.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
        try { return method.Invoke(owner is Type ? null : owner, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, F).SetValue(owner, value);
    static TextureTransform Transform(ProjectiveMatrix matrix) => new TextureTransform
    { storage = TransformStorage.Projective, matrix = matrix, tiling = TransformTilingMode.Unbounded, pivot = new Vector2(.5f, .5f), scale = Vector2.one };
    static Color Source(int x, int y)
    {
        if (y < 0 || y >= Side || x < -Side || x >= 2 * Side) return Color.clear;
        if (x < 0) return new Color(-.25f, 2.5f + y / 64 * .25f, .5f, .5f);
        if (x >= Side) return new Color(4, .125f, -.5f, .75f);
        return new Color(1 + x / 32 * .125f, .25f + y / 32 * .125f, 2, 1);
    }
    static Color[] Read(RenderTexture source)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = source; pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); return pixels.GetPixels(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
    }
    static void Equal(TestContext t, Color[] expected, Color[] actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " pixel count");
        double maximum = 0; int differences = 0, first = -1;
        for (int i = 0; i < actual.Length; i++) for (int channel = 0; channel < 4; channel++)
        {
            double difference = Math.Abs((double)expected[i][channel] - actual[i][channel]);
            maximum = Math.Max(maximum, difference);
            if (!float.IsFinite(actual[i][channel]) || difference > .004 * Math.Max(1, Math.Abs(expected[i][channel])))
            { differences++; if (first < 0) first = i; }
        }
        t.True(differences == 0, label + ": differences=" + differences + ", max=" + maximum + ", first=" + first +
            (first < 0 ? "" : ", expected=" + expected[first] + ", actual=" + actual[first]));
    }

    public static string Run() => TestContext.Run("Sparse Drawing rendering consumers", t =>
    {
        var owned = new List<Object>();
        var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        Texture2D Texture(int size, Func<int, int, Color> sample)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            owned.Add(texture); var data = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) data[y * size + x] = sample(x, y);
            texture.SetPixels(data); texture.Apply(false, false); return texture;
        }
        WhimTexDocument Document()
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); owned.Add(doc);
            doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = Side; return doc;
        }
        var actualDoc = Document(); var expectedDoc = Document();
        var sentinel = new RenderTexture(4, 4, 0) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(sentinel); sentinel.Create();
        try
        {
            var original = Texture(Side, Source);
            var drawing = new DrawingLayerBehaviour(); Set(drawing, "pixels", original); Set(drawing, "tiledSourceEnabled", true);
            Set(drawing, "tiledSourceWidth", Side); Set(drawing, "tiledSourceHeight", Side);
            var listType = drawing.GetType().GetField("tiledPixels", F).FieldType;
            var tiles = (IList)Activator.CreateInstance(listType); var tileType = listType.GetGenericArguments()[0];
            foreach (int key in new[] { -1, 1 })
            {
                var tile = Activator.CreateInstance(tileType, true); Set(tile, "key", new Vector2Int(key, 0));
                Set(tile, "texture", Texture(Side, (x, y) => { var c = Source(x + key * Side, y); return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); }));
                tiles.Add(tile);
            }
            Set(drawing, "tiledPixels", tiles); Call(drawing, "ValidateTiledPixels");
            var matrix = ProjectiveMatrix.Translate(.25, .25) * ProjectiveMatrix.Scale(.5, .5);
            var actualLayer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR,
                filterMode = LayerFilterMode.Point, transform = Transform(matrix) };
            var expanded = Texture(Side * 3, (x, y) => Source(x - Side, y - Side));
            var expectedLayer = new Layer(new FileLayerBehaviour { sourceTexture = expanded })
            { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR, filterMode = LayerFilterMode.Point,
                transform = Transform(matrix * ProjectiveMatrix.Translate(-1, -1) * ProjectiveMatrix.Scale(3, 3)) };
            actualDoc.layers.Add(actualLayer); expectedDoc.layers.Add(expectedLayer);
            void Normalize() { Call(actualDoc, "NormalizeModel"); Call(expectedDoc, "NormalizeModel"); }
            Normalize();
            Color[] Canvas(WhimTexDocument doc)
            {
                Texture2D pixels = null;
                try
                {
                    RenderTexture.active = sentinel; GL.sRGBWrite = true; pixels = doc.ComposeCanvas();
                    t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Canvas restores caller render state");
                    return pixels.GetPixels();
                }
                finally { if (pixels != null) Object.DestroyImmediate(pixels); }
            }
            void Compare(string label)
            {
                var expected = Canvas(expectedDoc);
                t.True(expected.Any(c => c.a > .4f), label + " exercises visible pixels");
                Equal(t, expected, Canvas(actualDoc), label);
            }
            var reference = Canvas(expectedDoc); var actual = Canvas(actualDoc);
            Equal(t, reference, actual, "Composite outside original Transform");
            t.True(actual.Any(c => c.r < -.1f) && actual.Any(c => c.r > 3), "Composite retains signed HDR on both outside-frame sides");
            t.True(actual[128 * Side + 16].a > .4f && actual[128 * Side + 240].a > .7f, "Off-frame tiles remain visible inside canvas");

            var thumbnailRender = (RenderTexture)Call(actualDoc, "RenderLayerThumbnail", actualLayer, Side, null);
            try { Equal(t, reference, Read(thumbnailRender), "Thumbnail render includes transformed sparse pixels"); }
            finally { RenderTexture.ReleaseTemporary(thumbnailRender); }
            var thumbnail = (Texture2D)Call(actualDoc, "GetLayerThumbnail", actualLayer, 128, false);
            t.True(thumbnail != null && thumbnail.width == 128 && thumbnail.height == 128, "Cached Drawing thumbnail exists");
            t.True(thumbnail.GetPixel(8, 64).a > .4f && thumbnail.GetPixel(120, 64).a > .7f, "Cached thumbnail retains both overflow regions");
            var raster = (Texture2D)Call(actualDoc, "RasterizeLayer", actualLayer, true);
            try { Equal(t, reference, raster.GetPixels(), "Raster export retains sparse HDR pixels"); }
            finally { Object.DestroyImmediate(raster); }
            var selection = (Texture2D)Call(actualDoc, "RenderAreaSelectionAlphaSource", actualLayer);
            try { Equal(t, reference, selection.GetPixels(), "Selection source includes sparse pixels"); }
            finally { Object.DestroyImmediate(selection); }

            Layer Group(WhimTexDocument doc, Layer child)
            {
                var group = new Layer(new GroupLayerBehaviour()) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                group.children.Add(child); doc.layers.Clear(); doc.layers.Add(group); return group;
            }
            var actualGroup = Group(actualDoc, actualLayer); var expectedGroup = Group(expectedDoc, expectedLayer); Normalize();
            foreach (var mode in new[] { GroupCompositing.PassThrough, GroupCompositing.Isolated })
            { actualGroup.compositing = expectedGroup.compositing = mode; Compare("Group " + mode); }

            Layer Target(WhimTexDocument doc, Layer group)
            {
                var blur = new BlurLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = group.Id, strength = 0, radius = 0 };
                var layer = new Layer(blur) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                group.enabled = false; doc.layers.Insert(0, layer); return layer;
            }
            var actualTarget = Target(actualDoc, actualGroup); var expectedTarget = Target(expectedDoc, expectedGroup); Normalize();
            Compare("Hidden group as targeted effect input");
            actualTarget.enabled = expectedTarget.enabled = false;
            Layer ShaderInput(WhimTexDocument doc, Layer group)
            {
                var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Code, draftCode = ShaderLayerBehaviour.PassthroughCode,
                    inputMode = ShaderLayerInputMode.Layer, inputLayerId = group.Id };
                var layer = new Layer(shader) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                doc.layers.Insert(0, layer); Call(doc, "NormalizeModel");
                t.True((bool)Call(shader, "CompileCode", doc), "Layer input passthrough compiles"); return layer;
            }
            var actualShader = ShaderInput(actualDoc, actualGroup); var expectedShader = ShaderInput(expectedDoc, expectedGroup);
            Compare("Hidden group as shader Layer input");
            actualShader.enabled = expectedShader.enabled = false;
            actualGroup.enabled = expectedGroup.enabled = true; actualGroup.clippingMask = expectedGroup.clippingMask = true;
            foreach (var doc in new[] { actualDoc, expectedDoc })
                doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = new Color(.125f, .25f, .5f, .5f) }));
            Normalize(); Compare("Clipping group against alpha base");

            // CPU consumers must see edited tiles, including hidden RGB at zero
            // alpha, rather than reading the immutable original source texture.
            Call(drawing, "ReleaseTiledRuntime");
            var overlay = Texture(Side, (x, y) => x == 0 && y == 0 ? Color.clear : new Color(-.5f, 2, .25f, .5f));
            var sourceTile = Activator.CreateInstance(tileType, true);
            Set(sourceTile, "key", Vector2Int.zero); Set(sourceTile, "texture", overlay); tiles.Add(sourceTile);
            using var sourcePixels = (NativeArray<Color>)Call(drawing, "ReadSourcePixels", Allocator.TempJob);
            t.Equal(new Color(-1, 4, .5f, .5f), sourcePixels[1], "CPU source unpremultiplies edited HDR values");
            var transparent = Source(0, 0); transparent.a = 0;
            t.Equal(transparent, sourcePixels[0], "CPU source retains original hidden RGB at zero alpha");
            var sourceRender = new RenderTexture(Side, Side, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(sourceRender); sourceRender.Create();
            Call(drawing, "BlitTiledStraight", sourceRender, 3f);
            var ordinarySource = sourcePixels.ToArray(); ordinarySource[0] = Color.clear;
            Equal(t, ordinarySource, Read(sourceRender), "Ordinary rendering retains the original zero-hidden-RGB contract");
            Set(drawing, "preserveStraightPaintRgb", true);
            try
            {
                Call(drawing, "BlitTiledStraight", sourceRender, 3f);
                Equal(t, sourcePixels.ToArray(), Read(sourceRender), "Protected source rendering retains stored hidden RGB");
            }
            finally { Set(drawing, "preserveStraightPaintRgb", false); }

            actualLayer.transform = TextureTransform.Default; Normalize();
            var job = Activator.CreateInstance(typeof(WhimTexApi).GetNestedType("LiveJob", BindingFlags.NonPublic), true);
            Set(job, "document", actualDoc); Set(job, "width", Side); Set(job, "height", Side);
            Set(job, "region", new RectInt(8, 8, 4, 4));
            var insertedColor = new Color(-.25f, 8, .125f, 1);
            var image = Texture(4, (_, _) => insertedColor);
            using var livePixels = (NativeArray<Color>)Call(typeof(WhimTexApi), "LiveImagePixels", job, image, "stretch", drawing);
            var expectedPixels = sourcePixels.ToArray();
            for (int y = 8; y < 12; y++) for (int x = 8; x < 12; x++) expectedPixels[y * Side + x] = insertedColor;
            Equal(t, expectedPixels, livePixels.ToArray(), "Live image replacement preserves all untouched Raster pixels");

            Call(drawing, "ReleaseTiledRuntime"); tiles.Remove(sourceTile);
            original.SetPixels(new Color[Side * Side]); original.Apply(false, false);
            actualLayer.transform = Transform(matrix); Normalize();
            t.True(!(bool)Call(actualDoc, "CanInspectLayerContent", actualLayer, new HashSet<Layer>()),
                "Outside-only Drawing does not queue a misleading original-frame alpha reduction");
            t.Equal("Unknown", Call(actualDoc, "GetLayerContentState", actualLayer, false).ToString(),
                "Outside-only pixels cannot receive a false Empty badge");
            t.True((bool)Call(actualDoc, "CanInspectCropLayerContent", actualLayer, new HashSet<Layer>()),
                "Canvas-space crop inspection remains available for outside-source pixels");
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            foreach (var doc in owned.OfType<WhimTexDocument>().ToArray()) if (doc != null) Object.DestroyImmediate(doc);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
        }
    });
}
