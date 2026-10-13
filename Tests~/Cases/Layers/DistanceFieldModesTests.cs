// Support: Tests~/Framework/TestApi.cs and Cases/Painting/DistanceFieldTests.cs.
// Native buffers and detached clipboard documents only; no assets, windows or files.
using System;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class DistanceFieldModesTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly DistanceMetric[] Modes = {
        DistanceMetric.EuclideanAntialiased, DistanceMetric.Sharp, DistanceMetric.Bevel,
        DistanceMetric.Diamond, DistanceMetric.Square, DistanceMetric.MTSDF
    };

    public static string Run() => TestContext.Run("Distance modes: source fields, channels and current-format roundtrip", t =>
    {
        t.Equal(DistanceMetric.EuclideanAntialiased, new SDFLayerBehaviour().metric, "SDF defaults to Round AA");
        t.Equal(DistanceMetric.EuclideanAntialiased, new OutlineLayerBehaviour().metric, "Outline defaults to Round AA");
        CheckColorInputs(t);
        CheckSourceFields(t);
        CheckImageBorders(t);
        CheckRoundtrip(t);
        CheckBinaryModelRoundtrip(t);
    });

    static void CheckColorInputs(TestContext t)
    {
        var sdf = new SDFLayerBehaviour();
        var outline = new OutlineLayerBehaviour();
        var sdfProperty = typeof(SDFLayerBehaviour).GetProperty("RequiresColorInput", Instance);
        var outlineProperty = typeof(OutlineLayerBehaviour).GetProperty("RequiresColorInput", Instance);
        t.True(sdfProperty != null && outlineProperty != null, "Both effects declare source data requirements");
        for (int channel = 0; channel < 5; channel++)
        {
            sdf.sourceChannel = (SDFLayerBehaviour.SourceChannel)channel;
            outline.sourceChannel = (OutlineLayerBehaviour.SourceChannel)channel;
            t.Equal(channel != 0, (bool)sdfProperty.GetValue(sdf), "SDF color input channel " + channel);
            t.Equal(channel != 0, (bool)outlineProperty.GetValue(outline), "Outline color input channel " + channel);
        }
    }

    static MethodInfo SourceMethod() => typeof(SDFLayerBehaviour).GetMethod("ComputeSourceDistance", Static, null,
        new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int), typeof(byte),
            typeof(int), typeof(DistanceMetric), typeof(Vector2), typeof(SDFLayerBehaviour.SourceEdges) }, null);

    static MethodInfo FieldMethod() => typeof(SDFLayerBehaviour).GetMethod("ComputeSourceField", Static, null,
        new[] { typeof(NativeArray<Color32>), typeof(NativeArray<float>), typeof(int), typeof(int), typeof(byte),
            typeof(int), typeof(DistanceMetric), typeof(Vector2), typeof(SDFLayerBehaviour.SourceEdges),
            typeof(float), typeof(NativeArray<float4>) }, null);

    static void CheckSourceFields(TestContext t)
    {
        const int w = 49, h = 45;
        var rings = new[] { DistanceFieldAnalyticFixture.Rotate(new[] {
            new Vector2(-13, -9), new Vector2(12, -7), new Vector2(8, 12), new Vector2(-10, 10)
        }, 19, new Vector2(24.2f, 22.3f)) };
        var mask = DistanceFieldAnalyticFixture.Raster(w, h, (x, y) => DistanceFieldAnalyticFixture.Distance(rings, x, y, 0));
        using var pixels = new NativeArray<Color32>(mask, Allocator.TempJob);
        using var output = new NativeArray<float>(w * h, Allocator.TempJob);
        using var channels = new NativeArray<float4>(w * h, Allocator.TempJob);
        var wrapper = SourceMethod(); var field = FieldMethod();
        t.True(wrapper != null && field != null, "Source wrapper and eleven-argument field entry are present");
        foreach (var mode in Modes) foreach (var offset in new[] { Vector2.zero, new Vector2(2.25f, -1.5f) })
        {
            field.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0, mode, offset,
                SDFLayerBehaviour.SourceEdges.Transparent, 4f,
                mode == DistanceMetric.MTSDF ? channels : default(NativeArray<float4>) });
            int distinct = 0;
            for (int y = 5; y < h - 5; y++) for (int x = 5; x < w - 5; x++)
            {
                int index = y * w + x;
                double expected = DistanceFieldAnalyticFixture.Distance(rings, x - offset.x, y - offset.y, 0);
                t.True(float.IsFinite(output[index]), "Finite translated " + mode + " source at " + index);
                if (Math.Abs(expected) > 1)
                    t.True((output[index] < 0) == (expected < 0), "Translated contour sign " + mode + " at " + index);
                if (mode == DistanceMetric.EuclideanAntialiased)
                    t.Near(expected, output[index], .7, "Translated Round continuous contour at " + index);
                if (mode == DistanceMetric.MTSDF)
                {
                    var value = channels[index];
                    for (int c = 0; c < 4; c++) t.True(float.IsFinite(value[c]), "Translated MTSDF channel " + c);
                    t.Near(expected, value.w, .7, "Source field preserves alpha true distance at " + index);
                    float median = Math.Max(Math.Min(value.x, value.y), Math.Min(Math.Max(value.x, value.y), value.z));
                    t.Near(median, output[index], .0001, "Source scalar uses sampled RGB median at " + index);
                    if (Math.Abs(expected) < 5 && Math.Abs(value.x - value.y) + Math.Abs(value.y - value.z) > .2) distinct++;
                }
                t.Equal(mask[index], pixels[index], "Source field does not mutate its mask");
            }
            if (mode == DistanceMetric.MTSDF) t.True(distinct > 10, "Source sampling retains edge-colored RGB");
        }
        wrapper.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0,
            DistanceMetric.EuclideanAntialiased, Vector2.zero, SDFLayerBehaviour.SourceEdges.Transparent });
        t.True(output[22 * w + 24] < -5 && output[5 * w + 5] > 5, "Nine-argument wrapper still computes the selected contour");
    }

    static void CheckImageBorders(TestContext t)
    {
        const int w = 11, h = 9;
        using var pixels = new NativeArray<Color32>(w * h, Allocator.TempJob);
        using var output = new NativeArray<float>(w * h, Allocator.TempJob);
        var writable = pixels;
        for (int i = 0; i < pixels.Length; i++) writable[i] = new Color32(255, 255, 255, 255);
        var field = FieldMethod();
        foreach (var mode in Modes)
        {
            field.Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0, mode, Vector2.zero,
                SDFLayerBehaviour.SourceEdges.Transparent, 4f, default(NativeArray<float4>) });
            t.True(output[4 * w + 5] < -3, "Full image has a negative interior with transparent source edges: " + mode);
            t.Near(-127.0 / 255, output[4 * w], .04, "Image border is a real subpixel contour: " + mode);
            for (int i = 0; i < output.Length; i++) t.True(float.IsFinite(output[i]) && output[i] < 0, "Finite full-image source distance " + mode);
        }
        foreach (var edges in new[] { SDFLayerBehaviour.SourceEdges.Clamp, SDFLayerBehaviour.SourceEdges.Repeat, SDFLayerBehaviour.SourceEdges.Mirror })
        {
            SourceMethod().Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0,
                DistanceMetric.EuclideanAntialiased, new Vector2(3, -2), edges });
            foreach (float d in output) t.True(float.IsFinite(d) && d < -1000, "Full " + edges + " source has no invented contour");
        }
        for (int i = 0; i < pixels.Length; i++) writable[i] = new Color32(0, 0, 0, 0);
        SourceMethod().Invoke(null, new object[] { pixels, output, w, h, (byte)128, 0,
            DistanceMetric.EuclideanAntialiased, Vector2.zero, SDFLayerBehaviour.SourceEdges.Transparent });
        foreach (float d in output) t.True(float.IsFinite(d) && d > 1000, "Empty source has no invented image-border contour");
    }

    static IDisposable Read(string json) => (IDisposable)typeof(WhimTexApi).GetMethod("ReadProceduralClipboard", Static)
        .Invoke(null, new object[] { json, 64, 64 });

    static WhimTexDocument Document(IDisposable clipboard) => (WhimTexDocument)clipboard.GetType()
        .GetField("Document", Instance).GetValue(clipboard);

    static void CheckRoundtrip(TestContext t)
    {
        using (var defaults = Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":["
            + "{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\"}},{\"id\":\"outline\",\"behaviour\":{\"$type\":\"OutlineLayerBehaviour\"}}]}"))
        {
            var document = Document(defaults);
            var sdf = (SDFLayerBehaviour)document.layers[0].Behaviour;
            var outline = (OutlineLayerBehaviour)document.layers[1].Behaviour;
            t.Equal(DistanceMetric.EuclideanAntialiased, sdf.metric, "JSON without metric defaults SDF to Round AA");
            t.Equal(DistanceMetric.EuclideanAntialiased, outline.metric, "JSON without metric defaults Outline to Round AA");
            t.Near(4, sdf.miterLimit, .0001, "JSON without miter limit defaults SDF to 4");
            t.Near(4, outline.miterLimit, .0001, "JSON without miter limit defaults Outline to 4");
        }
        foreach (var mode in Modes)
        {
            string json = "{\"format\":\"whimtex.document\",\"version\":2,\"layers\":["
                + "{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"metric\":\"" + mode
                + "\",\"miterLimit\":2.75,\"encoding\":\"LinearData\",\"sourceEdges\":\"Repeat\",\"sourceOffset\":[2.25,-1.5]}},"
                + "{\"id\":\"outline\",\"behaviour\":{\"$type\":\"OutlineLayerBehaviour\",\"metric\":\"" + mode
                + "\",\"miterLimit\":3.25,\"outlineWidth\":2.5,\"outlineOffset\":-1.25,\"fillCenter\":true}}]}";
            using var original = Read(json);
            var doc = Document(original);
            string exported = (string)typeof(WhimTexApi).GetMethod("WritePortableClipboard", Static)
                .Invoke(null, new object[] { doc, doc.layers });
            using var copied = Read(exported);
            var roundtrip = Document(copied);
            t.Equal(2, roundtrip.layers.Count, "Current-format two-effect roundtrip");
            var sdf = (SDFLayerBehaviour)roundtrip.layers[0].Behaviour;
            var outline = (OutlineLayerBehaviour)roundtrip.layers[1].Behaviour;
            t.Equal(mode, sdf.metric, "SDF selected artistic metric roundtrip");
            t.Equal(mode, outline.metric, "Outline selected artistic metric roundtrip");
            t.Near(2.75, sdf.miterLimit, .0001, "SDF miter limit roundtrip");
            t.Near(3.25, outline.miterLimit, .0001, "Outline miter limit roundtrip");
            t.Equal(new Vector2(2.25f, -1.5f), sdf.sourceOffset, "Fractional source offset roundtrip");
            t.Equal(SDFLayerBehaviour.SourceEdges.Repeat, sdf.sourceEdges, "Source edges roundtrip");
            t.Near(-1.25, outline.outlineOffset, .0001, "Contour offset roundtrip");
            t.True(outline.fillCenter, "Filled Outline roundtrip");
            t.Equal(SDFLayerBehaviour.OutputEncoding.LinearData, sdf.encoding, "Scalar encoding roundtrip");
        }
        using (var multi = Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\",\"metric\":\"MTSDF\",\"encoding\":\"MultiChannelData\"}}]}"))
            t.Equal(SDFLayerBehaviour.OutputEncoding.MultiChannelData, ((SDFLayerBehaviour)Document(multi).layers[0].Behaviour).encoding,
                "Explicit MTSDF data output accepted");
        foreach (string properties in new[] {
            "\"metric\":\"EuclideanAntialiased\",\"encoding\":\"MultiChannelData\"",
            "\"metric\":123", "\"miterLimit\":0", "\"miterLimit\":65"
        })
        {
            bool rejected = false;
            try
            {
                using var invalid = Read("{\"format\":\"whimtex.document\",\"version\":2,\"layers\":[{\"id\":\"sdf\",\"behaviour\":{\"$type\":\"SDFLayerBehaviour\","
                    + properties + "}}]}");
            }
            catch (TargetInvocationException error) { rejected = error.GetBaseException() is WhimTexDocumentException; }
            t.True(rejected, "Invalid current distance settings receive a diagnostic: " + properties);
        }
    }

    static void CheckBinaryModelRoundtrip(TestContext t)
    {
        // This exercises the binary model payload used by documents, not TIFF headers or files.
        var serializer = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        var serialize = serializer.GetMethod("Serialize", Static, null,
            new[] { typeof(object), typeof(WhimTexDocumentContainer) }, null);
        var deserialize = serializer.GetMethod("Deserialize", Static, null,
            new[] { typeof(byte[]), typeof(WhimTexDocumentContainer), typeof(Type), typeof(string), typeof(bool) }, null);
        t.True(serialize != null && deserialize != null, "Package binary model serializer entries are available");
        foreach (var mode in Modes)
        {
            var encoding = mode == DistanceMetric.MTSDF ? SDFLayerBehaviour.OutputEncoding.MultiChannelData
                : SDFLayerBehaviour.OutputEncoding.LinearData;
            var sourceSdf = new SDFLayerBehaviour { metric = mode, miterLimit = 2.75f, encoding = encoding,
                sourceOffset = new Vector2(2.25f, -1.5f), contourOffset = -.375f,
                insideDistance = 3.5f, outsideDistance = 7.25f };
            var sdf = (SDFLayerBehaviour)BinaryModelCopy(t, serialize, deserialize, sourceSdf);
            t.Equal(mode, sdf.metric, "Binary model SDF metric roundtrip");
            t.Near(2.75, sdf.miterLimit, .0001, "Binary model SDF miter limit roundtrip");
            t.Equal(encoding, sdf.encoding, "Binary model SDF encoding roundtrip, including MTSDF MultiChannelData");
            t.Equal(new Vector2(2.25f, -1.5f), sdf.sourceOffset, "Binary model SDF source offset roundtrip");
            t.Near(-.375, sdf.contourOffset, .0001, "Binary model SDF contour offset roundtrip");
            t.Near(3.5, sdf.insideDistance, .0001, "Binary model SDF inside distance roundtrip");
            t.Near(7.25, sdf.outsideDistance, .0001, "Binary model SDF outside distance roundtrip");

            var sourceOutline = new OutlineLayerBehaviour { metric = mode, miterLimit = 3.25f,
                outlineWidth = 2.5f, outlineOffset = -1.25f, fillCenter = true };
            var outline = (OutlineLayerBehaviour)BinaryModelCopy(t, serialize, deserialize, sourceOutline);
            t.Equal(mode, outline.metric, "Binary model Outline metric roundtrip");
            t.Near(3.25, outline.miterLimit, .0001, "Binary model Outline miter limit roundtrip");
            t.Near(2.5, outline.outlineWidth, .0001, "Binary model Outline width roundtrip");
            t.Near(-1.25, outline.outlineOffset, .0001, "Binary model Outline offset roundtrip");
            t.True(outline.fillCenter, "Binary model Outline fill roundtrip");
        }
    }

    static object BinaryModelCopy(TestContext t, MethodInfo serialize, MethodInfo deserialize, object source)
    {
        using var container = new WhimTexDocumentContainer();
        var bytes = (byte[])serialize.Invoke(null, new object[] { source, container });
        t.True(bytes != null && bytes.Length > 0, "Binary model payload is nonempty");
        object read = deserialize.Invoke(null, new object[] { bytes, container, source.GetType(), null, false });
        var model = read.GetType().GetProperty("Model", Instance);
        t.True(model != null, "Binary model read result exposes its package-owned model");
        object copy = model.GetValue(read);
        t.True(copy != null && copy.GetType() == source.GetType() && !ReferenceEquals(source, copy),
            "Binary model roundtrip reconstructs a separate root of the expected type");
        return copy;
    }
}
