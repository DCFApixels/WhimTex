using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Owned transient documents only. Never clears the global Undo history.
public static class PixelToolHistoryTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static Type T(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object o, string name, params object[] a) => o.GetType().GetMethod(name, F).Invoke(o, a);
    static object Static(string name, string method, params object[] a) => T(name).GetMethod(method, S).Invoke(null, a);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    sealed class Fixture : IDisposable
    {
        internal WhimTexDocument doc;
        internal DrawingLayerBehaviour layer;
        internal Texture2D pixels;
        internal readonly int size;
        internal Fixture(int size, bool hdr)
        {
            this.size = size;
            doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
            doc.width = doc.height = size;
            layer = new DrawingLayerBehaviour { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
            pixels = new Texture2D(size, size, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr)
                { hideFlags = HideFlags.HideAndDontSave };
            if (size <= 1024)
            {
                var colors = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    colors[y * size + x] = new Color((x % 37) / 36f * (hdr ? 3 : 1), hdr ? -.25f : .2f,
                        (y % 23) / 22f, x % 19 == 0 ? 0 : .7f);
                pixels.SetPixels(colors);
            }
            else
            {
                var raw = pixels.GetRawTextureData<uint>();
                for (int i = 0; i < raw.Length; i++)
                    raw[i] = hdr ? ((i & 1) == 0 ? 0x38003e00u : 0x3c003000u) : ((i & 1) == 0 ? 0xff883311u : 0xff2255aau);
            }
            pixels.Apply(false, false);
            Call(layer, "AdoptStoredTexture", pixels); doc.layers.Add(layer); Call(doc, "NormalizeModel");
            Call(layer, "EnsurePaintSurface", size, size);
        }
        internal void Edit(Action action, bool prepare = true)
        {
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            if (prepare) Call(layer, "PrepareStroke", size, size, "Owned retouch test");
            action(); Call(layer, "EndStroke"); Call(layer, "FinishStrokeStorage");
            Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
        }
        internal void Smudge(float mixing, Texture mask)
        {
            var start = new Vector2(.25f, .4f);
            Call(layer, "BeginStroke", start);
            Call(layer, "ConfigureStrokeWriteProtection", 13);
            Call(layer, "BeginSmudgeStroke", start, size, size, size <= 1024 ? 90f : 128f, null, false, mixing);
            Call(layer, "SmudgeSegment", start, new Vector2(.35f, .43f), size, size, .3f, .6f, .5f, mask);
        }
        internal void Brush(Texture mask)
        {
            var settings = Activator.CreateInstance(T("PaintToolSettings"), true);
            Set(settings, "brushSize", 90f); Set(settings, "brushColor", new Color(1.8f, .3f, .7f, 1));
            Set(Get(settings, "dynamics"), "flow", .0001f);
            Call(layer, "PrepareBufferedStroke", size, size, "Owned brush test");
            Call(layer, "BeginStroke", new Vector2(.4f, .4f));
            var p = Call(settings, "GetStrokeParameters", false, (Color?)null);
            p = Call(p, "WithSelectionMask", mask);
            Call(layer, "PaintPoint", new Vector2(.4f, .4f), size, size, p);
        }
        internal void Restore(bool redo)
        {
            if (redo) Undo.PerformRedo(); else Undo.PerformUndo();
            layer = (DrawingLayerBehaviour)doc.layers[0].Behaviour;
            Call(layer, "ReconcilePixelUndo");
            Call(layer, "EnsurePaintSurface", size, size);
        }
        public void Dispose()
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            var cursor = (Object)Get(layer, "PixelUndoObject");
            if (cursor != null) { Undo.ClearUndo(cursor); Object.DestroyImmediate(cursor); }
            Undo.ClearUndo(pixels); Undo.ClearUndo(doc);
            Call(layer, "ReleaseTransientResources"); doc.layers.Clear(); Object.DestroyImmediate(doc);
        }
    }
    sealed class Snapshot
    {
        internal byte[] stored, half;
        internal Snapshot(Fixture f)
        {
            Static("PaintTileTransfer", "Drain");
            stored = f.pixels.GetRawTextureData<byte>().ToArray();
            half = Probe((RenderTexture)Get(f.layer, "paintSurface"), new RectInt(0, 0, f.size, f.size));
        }
        internal void Check(TestContext t, Fixture f, string label)
        {
            var result = new Snapshot(f);
            Exact(t, stored, result.stored, label + " stored/hidden RGB");
            Exact(t, half, result.half, label + " Half");
        }
    }
    static byte[] Probe(RenderTexture surface, RectInt r)
    {
        if (surface == null) throw new Exception("Missing owned paint surface");
        var previous = RenderTexture.active;
        var tex = new Texture2D(r.width, r.height, TextureFormat.RGBAHalf, false, true);
        try { RenderTexture.active = surface; tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0, false); return tex.GetRawTextureData<byte>().ToArray(); }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(tex); }
    }
    static void Exact(TestContext t, byte[] a, byte[] b, string label)
    {
        t.Equal(a.Length, b.Length, label + " length");
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) throw new Exception(label + " differs at " + i + ": " + a[i] + "/" + b[i]);
        t.True(true, label);
    }
    public static string Run() => TestContext.Run("Shared retouch, fill and brush pixel history", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var mask = new Texture2D(1, 1); mask.SetPixel(0, 0, new Color(.35f, 0, 0, 1)); mask.Apply();
        try
        {
            foreach (bool hdr in new[] { false, true })
            {
                using var f = new Fixture(513, hdr);
                var states = new List<Snapshot> { new Snapshot(f) };
                f.Edit(() => f.Brush(mask), false); states.Add(new Snapshot(f));
                foreach (float mixing in new[] { 0f, .4f, 1f })
                { f.Edit(() => f.Smudge(mixing, mask)); states.Add(new Snapshot(f)); }
                // Parameter Undo must remain an ordinary, visible step between textures.
                f.Edit(() => { Undo.RecordObject(f.doc, "Owned parameter"); f.doc.name = "Parameter changed"; }, false);
                states.Add(new Snapshot(f));
                f.Edit(() =>
                {
                    var source = (RenderTexture)Call(f.layer, "CaptureBlurSource", f.size, f.size);
                    try { Call(f.layer, "BlurSegment", new Vector2(.4f, .4f), new Vector2(.5f, .4f), f.size, f.size, 60f, .5f, .6f, source, false); }
                    finally { Static("ProfiledRenderTexture", "ReleaseTemporary", source); }
                }); states.Add(new Snapshot(f));
                f.Edit(() =>
                {
                    var patch = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
                    var coverage = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    try
                    {
                        patch.SetPixel(0, 0, new Color(hdr ? 2 : .8f, .1f, .4f, 1)); patch.Apply();
                        coverage.SetPixel(0, 0, Color.white); coverage.Apply();
                        var identity = T("ProjectiveMatrix").GetProperty("Identity", S).GetValue(null);
                        Call(f.layer, "ApplyHealingPatch", patch, coverage, new RectInt(210, 200, 20, 30), f.size, f.size, identity, false);
                    }
                    finally { Object.DestroyImmediate(patch); Object.DestroyImmediate(coverage); }
                }); states.Add(new Snapshot(f));
                f.Edit(() =>
                {
                    using var colors = new NativeArray<Color>(f.size * f.size, Allocator.Temp);
                    var values = colors;
                    for (int i = 0; i < colors.Length; i++) values[i] = new Color(hdr ? 2 : .5f, .2f, .8f, i % 11 == 0 ? 0 : .3f);
                    f.layer.GetType().GetMethod("ApplyFillPixels", F, null,
                        new[] { typeof(NativeArray<Color>), typeof(int), typeof(int), typeof(string) }, null)
                        .Invoke(f.layer, new object[] { colors, f.size, f.size, "Owned fill" });
                }, false); states.Add(new Snapshot(f));
                f.Edit(() => Call(f.layer, "ClearSurface", f.size, f.size)); states.Add(new Snapshot(f));
                for (int i = states.Count - 2; i >= 0; i--)
                {
                    f.Restore(false); states[i].Check(t, f, "Undo " + i + " HDR=" + hdr);
                    if (i == 4) t.True(f.doc.name != "Parameter changed", "Parameter Undo is not skipped");
                }
                for (int i = 1; i < states.Count; i++)
                {
                    f.Restore(true); states[i].Check(t, f, "Redo " + i + " HDR=" + hdr);
                    if (i == 5) t.Equal("Parameter changed", f.doc.name, "Parameter Redo order");
                }
                var beforeCancel = new Snapshot(f);
                Call(f.layer, "PrepareStroke", f.size, f.size, "Cancelled retouch");
                f.Smudge(.5f, mask); Call(f.layer, "CancelStroke");
                beforeCancel.Check(t, f, "Cancelled direct edit");
                t.True(Get(f.layer, "pixelEditBefore") == null, "No retained snapshot after cancellation");
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    public static string RunLarge(bool hdr) => TestContext.Run("8K retouch tile capture and exact history", t =>
    {
        using var f = new Fixture(8192, hdr);
        using var recording = new OwnedCapture();
        var region = new RectInt(1792, 3072, 1280, 768);
        var far = new RectInt(0, 0, 32, 32);
        var before = Probe((RenderTexture)Get(f.layer, "paintSurface"), region);
        var untouched = Probe((RenderTexture)Get(f.layer, "paintSurface"), far);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        f.Edit(() => f.Smudge(1, null));
        double submit = watch.Elapsed.TotalMilliseconds;
        int tiles = (int)Get(f.layer, "LastPaintTileCount");
        t.True(tiles > 0 && tiles < 32, "Native 8K retouch captures a narrow set of tiles");
        t.True(Get(f.layer, "pixelEditBefore") == null, "Before-atlas released at stroke end");
        var after = Probe((RenderTexture)Get(f.layer, "paintSurface"), region);
        Exact(t, untouched, Probe((RenderTexture)Get(f.layer, "paintSurface"), far), "Outside captured tiles stays exact");
        f.Restore(false); Exact(t, before, Probe((RenderTexture)Get(f.layer, "paintSurface"), region), "8K Undo Half");
        f.Restore(true); Exact(t, after, Probe((RenderTexture)Get(f.layer, "paintSurface"), region), "8K Redo Half");
        string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Temp/WhimTex/pixel-tool-history"));
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, hdr ? "8k-hdr.txt" : "8k-sdr.txt"),
            "submissionMs=" + submit + "\ntiles=" + tiles + "\nsnapshotPayloadBytes=" + tiles * 256L * 256 * 8);
        Static("PaintHistoryStore", "FlushWrites");
        Static("WhimTexProfiler", "Stop", "owned_8k_test");
        string report = WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"read\",\"limit\":2000}");
        t.True(report.Contains("\"kind\":\"worker\"") && report.Contains("storage.pixel_history.write"), "Real 8K disk writer is recorded off the main thread");
        t.True(report.Contains("\"paintBuffers\""), "Real pool/staging metrics exported");
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, hdr ? "profile-8k-hdr.json" : "profile-8k-sdr.json"), report);
    });
    sealed class OwnedCapture : IDisposable
    {
        readonly object previous;
        internal OwnedCapture()
        {
            previous = T("WhimTexProfiler").GetProperty("Current", S).GetValue(null);
            if (previous != null && ((bool)Get(previous, "recording") || (int)Get(previous, "open") != 0))
                throw new Exception("Stop profiling before the owned test");
            Static("WhimTexProfiler", "Start", 8192, 120d, null);
        }
        public void Dispose()
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            Static("WhimTexProfiler", "Stop", "owned_test"); Static("WhimTexProfiler", "PollAsync");
            Static("WhimTexProfiler", "Clear");
            T("WhimTexProfiler").GetField("capture", S).SetValue(null, previous);
        }
    }
}
