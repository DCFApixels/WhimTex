using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Support: Framework/TestApi.cs. Reflection targets package-owned instrumentation only.
// Synchronous coverage/error tests deliberately keep native GPU queries unavailable. Actual
// backend timestamp delivery still needs the asynchronous profiler GPU fixture.
public static class ProfilerGpuCoverageTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static object State(string name) => Profiler.GetField(name, Static).GetValue(null);
    static void State(string name, object value) => Profiler.GetField(name, Static).SetValue(null, value);
    static object Call(string name, params object[] args) => Profiler.GetMethod(name, Static).Invoke(null, args);
    static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    static object New(string name) => Activator.CreateInstance(Profiler.GetNestedType(name, BindingFlags.NonPublic), true);
    static object[] Samples(object capture) => ((Array)Get(capture, "events")).Cast<object>().Take((int)Get(capture, "count")).ToArray();

    public static string Run()
    {
        object retained = State("capture");
        var slots = (IList)State("gpuSlots");
        if ((retained != null && ((bool)Get(retained, "recording") || (int)Get(retained, "open") != 0)) ||
            (int)State("current") != -1 || (bool)State("gpuEnabled") || slots.Cast<object>().Any(s => (bool)Get(s, "busy")))
            return TestContext.Result("skipped", 0, "Requires idle profiler without in-flight queries; retained capture is preserved.").ToJson();

        return TestContext.Run("GPU boundaries, pixel parity, unavailable results and bounded query bookkeeping", t =>
        {
            var scalars = new Dictionary<string, object>();
            foreach (string name in new[] { "capture", "current", "suppressed", "nextChange", "gpuCapture", "gpuReady", "gpuEnabled", "gpuAvailability" })
                scalars.Add(name, State(name));
            var dictionaries = new Dictionary<IDictionary, DictionaryEntry[]>();
            foreach (string name in new[] { "changes", "documentIds" })
            {
                var dictionary = (IDictionary)State(name);
                dictionaries.Add(dictionary, dictionary.Keys.Cast<object>().Select(key => new DictionaryEntry(key, dictionary[key])).ToArray());
            }
            object[] savedSlots = slots.Cast<object>().ToArray();
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var owned = new List<WhimTexDocument>();
            object cache = null;
            try
            {
                // Never call Start/Clear: those also mutate user reload backups and baselines.
                foreach (var dictionary in dictionaries.Keys) dictionary.Clear();
                Call("ResetMemory"); Call("ResetCauses");
                slots.Clear();
                State("capture", null); State("gpuCapture", null);
                State("gpuEnabled", false); State("gpuReady", false);
                State("current", -1); State("suppressed", 0);
                var baseline = MakeDocument(owned, out var baselineDrawing);
                Paint(baselineDrawing);
                Color[] expected = Render(baseline);
                var document = MakeDocument(owned, out var drawing);

                object capture = New("Capture");
                long now = Stopwatch.GetTimestamp();
                Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", now);
                Set(capture, "deadline", now + 60 * Stopwatch.Frequency); Set(capture, "recording", true);
                Set(capture, "events", Array.CreateInstance(Profiler.GetNestedType("Sample", BindingFlags.NonPublic), 8192));
                Set(capture, "memory", New("MemoryTotals"));
                State("capture", capture); State("gpuCapture", capture); State("gpuAvailability", "warming_up");

                using ((IDisposable)Call("Measure", "brush.input_batch", document, (Layer)drawing, -1, null)) Paint(drawing);
                t.True(expected.SequenceEqual(Render(document)), "GPU instrumentation unavailable does not change rendered pixels");
                var rows = Samples(capture);
                foreach (string phase in new[] { "render.blend", "render.clear", "render.backdrop_copy",
                    "layer.generate", "layer.transform", "layer.drawing_surface", "layer.drawing_convert", "brush.draw_batch" })
                {
                    var matches = rows.Where(s => (string)Get(s, "phase") == phase).ToArray();
                    t.True(matches.Length > 0, "Real work reaches " + phase);
                    t.True(matches.All(s => (string)Get(s, "gpuStatus") == (SystemInfo.supportsGpuRecorder ? "warming_up" : "unsupported")),
                        phase + " reports unavailable GPU timing explicitly");
                    t.True(matches.All(s => (long)Get(s, "gpuNanoseconds") == 0), "No fabricated GPU duration for " + phase);
                }
                foreach (string phase in new[] { "document.render", "document.compose_readback", "brush.segment", "brush.submit_batch",
                    "transfer.canvas_readback", "transfer.drawing_readback", "transfer.texture_readback", "transfer.texture_upload" })
                {
                    var matches = rows.Where(s => (string)Get(s, "phase") == phase).ToArray();
                    t.True(matches.Length > 0 && matches.All(s => Get(s, "gpuStatus") == null), phase + " retains wall timing only");
                }
                var batch = rows.Single(s => (string)Get(s, "phase") == "brush.draw_batch");
                var parent = rows.Single(s => (long)Get(s, "id") == (long)Get(batch, "parentId"));
                t.Equal("brush.submit_batch", (string)Get(parent, "phase"), "Drawing batch stays nested under brush submission");
                t.Equal(((Layer)drawing).Id, (string)Get(batch, "layerId"), "Batch inherits the painted layer identity");

                Invoke(baselineDrawing, "EnsurePaintSurface", 24, 24);
                Invoke(baselineDrawing, "ReleasePaintSurface");
                Invoke(drawing, "ReleasePaintSurface");
                rows = Samples(capture);
                var surfaces = rows.Where(s => (string)Get(s, "detail") == "drawing.surface").ToArray();
                t.True(surfaces.Any(s => (string)Get(s, "phase") == "resource.acquire"), "New Drawing surface payload is acquired");
                t.True(surfaces.Any(s => (string)Get(s, "phase") == "resource.observe"), "Preexisting Drawing surface is observed when touched");
                t.Equal(2, surfaces.Count(s => (string)Get(s, "phase") == "resource.release"), "Both observed and acquired Drawing surfaces are released");

                int count = (int)Get(capture, "count");
                Invoke(new DrawingLayerBehaviour(), "SyncSurfaceToTexture");
                t.Equal(count, (int)Get(capture, "count"), "Missing Drawing surface does not emit a transfer or GPU interval");

                // Exercise the encoded copy without an upload, preserving its existing ownership.
                var source = (RenderTexture)Invoke(document, "RenderCanvas", 24);
                try
                {
                    int start = (int)Get(capture, "count");
                    var encoded = (Texture2D)typeof(WhimTexDocument).GetMethod("CopyToTexture2D", Static).Invoke(null, new object[] { source, false });
                    UnityEngine.Object.DestroyImmediate(encoded);
                    var transfers = Samples(capture).Skip(start).ToArray();
                    t.True(transfers.Any(s => (string)Get(s, "phase") == "transfer.encode" && Get(s, "gpuStatus") != null), "Encoding blit has a GPU boundary");
                    t.True(!transfers.Any(s => (string)Get(s, "phase") == "transfer.texture_upload"), "uploadToGpu=false does not report an upload");
                }
                finally { Call("ReleaseTexture", source); RenderTexture.ReleaseTemporary(source); }

                // A retained targeted result should emit a copy interval, not pretend it reran its generator.
                var cachedDocument = MakeDocument(owned, out _);
                Layer cachedLayer = new BlurLayerBehaviour { radius = 1f };
                cachedDocument.layers.Insert(0, cachedLayer);
                Invoke(cachedDocument, "NormalizeModel");
                cache = Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache", true), true);
                for (int i = 0; i < 2; i++)
                {
                    int start = (int)Get(capture, "count");
                    var texture = (RenderTexture)Invoke(cachedDocument, "RenderCanvasWithCache", 24, cache, false, null);
                    Call("ReleaseTexture", texture); RenderTexture.ReleaseTemporary(texture);
                    if (i == 1)
                        t.True(!Samples(capture).Skip(start).Any(s => (string)Get(s, "phase") == "layer.generate" &&
                            (string)Get(s, "layerId") == cachedLayer.Id), "Cache hit does not report another layer generation");
                }
                t.True(Samples(capture).Any(s => (string)Get(s, "phase") == "cache.restore" && Get(s, "gpuStatus") != null), "Warm cache copy is measured");
                CheckLayerBoundaries(t, capture, owned);
                t.Equal(0, (int)Get(capture, "open"), "All CPU scopes are balanced");
                t.Equal(0, slots.Count, "Unavailable capture never allocates native GPU slots");
                CheckQueries(t, capture, slots);
                Set(capture, "recording", false);
            }
            finally
            {
                State("capture", null); State("current", -1);
                (cache as IDisposable)?.Dispose();
                foreach (var document in owned) if (document != null) UnityEngine.Object.DestroyImmediate(document);
                Call("ResetMemory"); Call("ResetCauses");
                slots.Clear(); foreach (object slot in savedSlots) slots.Add(slot);
                foreach (var pair in dictionaries) { pair.Key.Clear(); foreach (var entry in pair.Value) pair.Key.Add(entry.Key, entry.Value); }
                foreach (var pair in scalars) State(pair.Key, pair.Value);
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            }
            t.True(ReferenceEquals(retained, State("capture")), "Retained user capture restored unchanged");
        });
    }

    static WhimTexDocument MakeDocument(List<WhimTexDocument> owned, out DrawingLayerBehaviour drawing)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 24; owned.Add(document);
        drawing = new DrawingLayerBehaviour { brushSize = 5f, brushColor = new Color(.3f, .6f, .9f, .8f) };
        var group = new GroupLayerBehaviour { compositing = GroupCompositing.PassThrough, opacity = .6f };
        group.layers.Add(drawing);
        document.layers.Add(group);
        document.layers.Add(new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV });
        Invoke(document, "NormalizeModel");
        return document;
    }

    static void Paint(DrawingLayerBehaviour drawing)
    {
        Invoke(drawing, "BeginStroke", new Vector2(.2f, .3f));
        object parameters = Invoke(drawing, "GetStrokeParameters", false);
        Invoke(drawing, "PaintSegment", new Vector2(.2f, .3f), new Vector2(.8f, .7f), 24, 24, true, parameters);
        Invoke(drawing, "SyncSurfaceToTexture");
        Invoke(drawing, "EndStroke");
    }

    static Color[] Render(WhimTexDocument document)
    {
        Texture2D texture = null;
        try { texture = document.ComposeCanvas(); return texture.GetPixels(); }
        finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
    }

    static void CheckLayerBoundaries(TestContext t, object capture, List<WhimTexDocument> owned)
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 24; owned.Add(document);
        Layer layer = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.UV };
        document.layers.Add(layer); Invoke(document, "NormalizeModel");
        var shader = Shader.Find("Hidden/WhimTex/Hdr");
        t.True(shader != null && shader.isSupported, "Existing finishing shader is available for the owned material FX fixture");
        var effect = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        layer.fx.Add(effect);
        Type contextType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
        object Context(bool transform, bool fx) => Activator.CreateInstance(contextType, Instance, null,
            new object[] { document, null, 24, 24, 1f, transform, fx, null }, null);
        object oldFilter = Get(capture, "documentId");
        try
        {
            // Root calls have no enclosing document span, so their context must carry the filter identity.
            string documentId = (string)Call("DocumentId", document);
            Set(capture, "documentId", documentId);
            foreach (bool enabled in new[] { true, false })
            {
                int start = (int)Get(capture, "count");
                var rendered = (RenderTexture)Invoke(layer, "Render", Context(enabled, enabled));
                try { t.True(rendered != null, "Generic layer dispatch still produces its render target"); }
                finally { if (rendered != null) { Call("ReleaseTexture", rendered); RenderTexture.ReleaseTemporary(rendered); } }
                var rows = Samples(capture).Skip(start).ToArray();
                var generation = rows.Single(s => (string)Get(s, "phase") == "layer.generate");
                var transform = rows.Single(s => (string)Get(s, "phase") == "layer.transform");
                t.Equal(documentId, (string)Get(generation, "documentId"), "Root generation passes document filtering");
                t.Equal(layer.Id, (string)Get(transform, "layerId"), "Common transform retains layer identity");
                t.Equal((long)Get(generation, "id"), (long)Get(transform, "parentId"), "Transform nests under inclusive generation");
                t.True(((string)Get(generation, "detail")).Contains("Inclusive"), "Generation explicitly describes its inclusive boundary");
                t.True(Get(generation, "gpuStatus") != null && Get(transform, "gpuStatus") != null,
                    "Both intervals request timing, including identity resampling when transforms are disabled");
                if (enabled)
                {
                    var fx = rows.Single(s => (string)Get(s, "phase") == "fx.block");
                    t.Equal((long)Get(generation, "id"), (long)Get(fx, "parentId"), "FX is a sibling of transform, not part of its interval");
                    t.True((long)Get(transform, "end") <= (long)Get(fx, "start"), "Transform scope closes before FX begins");
                    t.True((long)Get(fx, "end") <= (long)Get(generation, "end"), "Inclusive generation encloses FX");
                }
                else t.True(!rows.Any(s => ((string)Get(s, "phase")).StartsWith("fx.", StringComparison.Ordinal)), "Disabled FX remains absent");
                t.Equal(0, (int)Get(capture, "open"), "Common layer scopes balance after each dispatch");
                t.Equal(-1, (int)State("current"), "Dispatch restores the parent scope");
            }

            object context = Context(true, true);
            int before = (int)Get(capture, "count");
            t.True(Invoke(layer, "ApplyTransformAndFx", null, context, null) == null, "Null source keeps its original early return");
            t.Equal(before, (int)Get(capture, "count"), "Null source emits no transform timing");
            Layer pending = (LayerBehaviour)Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PendingLayerBehaviour", true), true);
            foreach (Layer empty in new Layer[] { new Layer(), new GroupLayerBehaviour(), pending, new FileLayerBehaviour(),
                new BlurLayerBehaviour(), new SDFLayerBehaviour(), new ShaderProcessorLayerBehaviour() })
            {
                before = (int)Get(capture, "count");
                t.True(Invoke(empty, "Render", context) == null, "Empty layer dispatch remains empty");
                var rows = Samples(capture).Skip(before).ToArray();
                t.True(rows.All(s => Get(s, "gpuStatus") == null), "Known empty dispatch does not request GPU timing");
                t.True(!rows.Any(s => (string)Get(s, "phase") == "layer.transform"), "Empty dispatch has no transform pass");
            }
        }
        finally { Set(capture, "documentId", oldFilter); layer.fx.Remove(effect); UnityEngine.Object.DestroyImmediate(effect); }
    }

    static void CheckQueries(TestContext t, object capture, IList slots)
    {
        int max = (int)Profiler.GetField("MaxGpuSlots", Static).GetRawConstantValue();
        t.Equal(128, max, "GPU marker identities stay bounded to 128, including retired slots");
        for (int i = 0; i < max; i++) { object retired = New("GpuSlot"); Set(retired, "retired", true); slots.Add(retired); }
        State("gpuReady", true);
        var span = (IDisposable)Call("Measure", "render.capacity_probe", null, null, -1, null);
        int index = (int)State("current");
        try
        {
            ((IDisposable)Invoke(span, "Gpu")).Dispose();
            object sample = ((Array)Get(capture, "events")).GetValue(index);
            t.Equal(SystemInfo.supportsGpuRecorder ? "capacity" : "unsupported", (string)Get(sample, "gpuStatus"), "Exhaustion reports no fabricated GPU time");
            ((IDisposable)Invoke(span, "Gpu")).Dispose();
            t.Equal(max, slots.Count, "Duplicate and capacity calls do not allocate additional markers");

            object slot = New("GpuSlot"); Set(slot, "owner", capture); Set(slot, "sample", index); Set(slot, "busy", true);
            Call("GpuError", slot, new InvalidOperationException("owned marker failure"));
            sample = ((Array)Get(capture, "events")).GetValue(index);
            t.Equal("error", (string)Get(sample, "gpuStatus"), "Marker failure becomes a diagnostic");
            t.True(((string)Get(sample, "gpuDetail")).Contains("owned marker failure"), "Failure detail retained");
            t.True((bool)Get(slot, "retired") && !(bool)Get(slot, "busy") && Get(slot, "owner") == null, "Failed marker cannot be reused or retain the capture");
            t.Equal(0L, (long)Get(sample, "gpuNanoseconds"), "Error has no made-up timing");
            State("gpuReady", false);
            ((IDisposable)Invoke(span, "Gpu")).Dispose();
            sample = ((Array)Get(capture, "events")).GetValue(index);
            t.Equal("error", (string)Get(sample, "gpuStatus"), "A copied scope cannot replace its failure with a later interval");
        }
        finally { span.Dispose(); }
        // Invalid/default/stale handles must be harmless without touching any native recorder.
        ((IDisposable)Call("BeginGpu", null, -1)).Dispose();
        ((IDisposable)Call("BeginGpu", capture, int.MaxValue)).Dispose();
        Type scope = Profiler.GetNestedType("GpuScope", BindingFlags.NonPublic);
        ((IDisposable)Activator.CreateInstance(scope)).Dispose();
        Set(slots[0], "busy", true); Set(slots[0], "generation", 124);
        var stale = (IDisposable)Activator.CreateInstance(scope, Instance, null, new object[] { 0, 123 }, null);
        stale.Dispose(); stale.Dispose();
        t.True((bool)Get(slots[0], "busy") && !(bool)Get(slots[0], "ended"), "Stale generation cannot end another invocation");
        Set(slots[0], "busy", false);
        t.Equal(max, slots.Count, "Default, invalid and stale scope disposal leaves the bounded slots intact");
        State("gpuReady", false);
    }
}
