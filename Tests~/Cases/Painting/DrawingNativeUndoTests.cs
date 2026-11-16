using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    public static string RunNativeConversion() => TestContext.Run("Native storage Undo restores dense/sparse CPU and GPU, including hidden HDR and signed tiles", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        foreach (bool sparse in new[] { false, true })
        {
            using var f = new ReuseFixture();
            byte[] Render() => sparse ? f.Capture() : NativeDenseBytes(f.drawing);
            try
            {
                if (sparse) f.Stroke(new Color(4f, -.5f, .23f, .6f));
                Static("PaintTileTransfer", "Drain");
                Call(f.document, "MarkChanged");
                var before = NativeSnapshot(test, f.drawing, "Before conversion"); var beforeRender = Render();
                Convert(f.drawing, f.document); Call(f.document, "MarkChanged");
                var converted = NativeSnapshot(test, f.drawing, "Converted"); var convertedRender = Render();
                test.True(!ReuseEqual(beforeRender, convertedRender), "Conversion changes the actual rendered source");
                for (int i = 0; i < 3; i++)
                {
                    Undo.PerformUndo(); f.drawing = (DrawingLayerBehaviour)f.document.layers[0].Behaviour;
                    test.Equal(TextureFormat.RGBAHalf, f.original.format, "HDR format on Undo " + i);
                    AssertNativeSnapshot(test, before, f.drawing, "Undo " + i);
                    Exact(test, beforeRender, Render(), "Rendered Undo " + i);
                    Undo.PerformRedo(); f.drawing = (DrawingLayerBehaviour)f.document.layers[0].Behaviour;
                    test.Equal(TextureFormat.RGBA32, f.original.format, "8-bit format on Redo " + i);
                    AssertNativeSnapshot(test, converted, f.drawing, "Redo " + i);
                    Exact(test, convertedRender, Render(), "Rendered Redo " + i);
                }
                test.Equal(sparse, (bool)Get(f.drawing, "HasTiledSource"), "Fixture keeps its dense/sparse rendering path");
            }
            finally
            {
                foreach (var texture in NativeTextures(f.drawing).Values) if (texture != null) Undo.ClearUndo(texture);
            }
        }
    });

    public static string RunNativeTracking() => TestContext.Run("Native Undo tracking excludes completed and pending paint uploads across documents", test =>
    {
        ReusePreflight(test);
        using var disk = new PaintTileHistoryTests.DiskScope();
        using var a = new ReuseFixture(); using var b = new ReuseFixture();
        a.Stroke(new Color(3f, -.4f, .5f, .5f)); Call(a.document, "MarkChanged");
        b.Stroke(new Color(2f, -.5f, .2f, .4f)); Call(b.document, "MarkChanged");
        Static("PaintTileTransfer", "Drain");
        var first = b.Capture();
        var beforeCompleted = NativeUpdates(a.drawing, b.drawing);
        Rename(a.document);
        Undo.PerformUndo(); a.drawing = (DrawingLayerBehaviour)a.document.layers[0].Behaviour;
        AssertNativeUpdates(test, beforeCompleted, "Unrelated Undo after completed readback");
        Undo.PerformRedo(); a.drawing = (DrawingLayerBehaviour)a.document.layers[0].Behaviour;
        AssertNativeUpdates(test, beforeCompleted, "Unrelated Redo after completed readback");

        b.Stroke(new Color(-.25f, 4f, .8f, .7f)); Call(b.document, "MarkChanged");
        var second = b.Capture();
        test.True(ReuseStat<int>("PaintTileTransfer", "PendingCount") > 0, "Other document still has queued readback");
        var beforePending = NativeUpdates(a.drawing, b.drawing);
        Rename(a.document);
        Undo.PerformUndo(); a.drawing = (DrawingLayerBehaviour)a.document.layers[0].Behaviour;
        AssertNativeUpdates(test, beforePending, "Unrelated Undo while another document drains");
        Exact(test, second, b.Capture(), "Other document keeps its latest rendered stroke");
        uint originalVersion = b.original.updateCount;
        Undo.PerformUndo(); b.drawing = (DrawingLayerBehaviour)b.document.layers[0].Behaviour;
        test.Equal(originalVersion, b.original.updateCount, "Paint Undo does not upload the full original source");
        Exact(test, first, b.Capture(), "Paint Undo remains exact after unrelated Undo");
        Undo.PerformRedo(); b.drawing = (DrawingLayerBehaviour)b.document.layers[0].Behaviour;
        test.Equal(originalVersion, b.original.updateCount, "Paint Redo does not upload the full original source");
        Exact(test, second, b.Capture(), "Paint Redo remains exact after unrelated Undo");
    });

    static void Rename(WhimTexDocument document)
    {
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.RegisterCompleteObjectUndo(document, "Owned native tracking rename");
        document.name += "x"; Call(document, "MarkChanged");
        Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
    }
    static byte[] NativeDenseBytes(DrawingLayerBehaviour drawing)
    {
        var previous = RenderTexture.active;
        var surface = (RenderTexture)Call(drawing, "EnsurePaintSurface", 256, 256);
        var read = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true);
        try
        {
            RenderTexture.active = surface; read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false);
            return read.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(read); }
    }
    static Dictionary<string, Texture2D> NativeTextures(DrawingLayerBehaviour drawing)
    {
        var result = new Dictionary<string, Texture2D> { ["base"] = (Texture2D)Get(drawing, "StoredTexture") };
        foreach (var tile in (IEnumerable)Get(drawing, "TiledPixels"))
            result.Add(((Vector2Int)Get(tile, "key")).ToString(), (Texture2D)Get(tile, "texture"));
        return result;
    }
    static Dictionary<string, byte[]> NativeSnapshot(TestContext test, DrawingLayerBehaviour drawing, string label)
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var pair in NativeTextures(drawing))
        {
            var raw = pair.Value.GetRawTextureData<byte>().ToArray();
            Exact(test, raw, NativeGpuBytes(pair.Value), label + " CPU/GPU " + pair.Key);
            result.Add(pair.Key, raw);
        }
        return result;
    }
    static void AssertNativeSnapshot(TestContext test, Dictionary<string, byte[]> expected, DrawingLayerBehaviour drawing, string label)
    {
        var actual = NativeSnapshot(test, drawing, label);
        test.Equal(expected.Count, actual.Count, label + " texture count");
        foreach (var pair in expected) Exact(test, pair.Value, actual[pair.Key], label + " expected bytes " + pair.Key);
    }
    static List<(Texture2D texture, uint version)> NativeUpdates(params DrawingLayerBehaviour[] drawings)
    {
        var result = new List<(Texture2D texture, uint version)>();
        foreach (var drawing in drawings) foreach (var texture in NativeTextures(drawing).Values) result.Add((texture, texture.updateCount));
        return result;
    }
    static void AssertNativeUpdates(TestContext test, List<(Texture2D texture, uint version)> expected, string label)
    {
        foreach (var pair in expected) test.Equal(pair.version, pair.texture.updateCount, label + " texture " + pair.texture.name);
    }
    static byte[] NativeGpuBytes(Texture2D source)
    {
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(source.width, source.height)
            { graphicsFormat = source.graphicsFormat, depthBufferBits = 0, msaaSamples = 1 });
        var read = new Texture2D(source.width, source.height, source.format, false, !source.isDataSRGB);
        try
        {
            RenderTexture.active = null; Graphics.CopyTexture(source, 0, 0, target, 0, 0);
            RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            return read.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(target); }
    }
}
