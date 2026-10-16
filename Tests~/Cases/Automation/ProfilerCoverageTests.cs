using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerTests.Json;

public static class ProfilerCoverageTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type Profiler = Package.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static object Get(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Instance).Invoke(target, args);
    static object Profile(string method, params object[] args) => Profiler.GetMethod(method, Static).Invoke(null, args);
    static IDisposable Span(string phase, WhimTexDocument document, Layer layer = null) =>
        (IDisposable)Profile("Measure", phase, document, layer, -1, null);
    static void Reason(string phase, string reason, WhimTexDocument document) => Profile("Reason", phase, reason, document, null, null, 1d);
    static Json Read() => Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"read\",\"limit\":2000}"));
    static Json[] Events() => Read()["events"].ToArray();
    static void SelectAll<T>(T selection) => Invoke(selection, "All");

    public static string Run()
    {
        var status = Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"status\"}"))["capture"];
        if (status.Type != "Null" && ((bool)status["recording"] || (int)status["openSpans"] != 0 || (int)status["pendingGpuSamples"] != 0))
            return TestContext.Result("skipped", 0, "Requires idle profiler; retained stopped capture is preserved.").ToJson();
        return TestContext.Run("Editor coverage, recomputation reasons, document isolation and pixel parity", t =>
        {
            var captureField = Profiler.GetField("capture", Static);
            object previousCapture = captureField.GetValue(null);
            string previousEvents = previousCapture == null ? null : Read()["events"].ToString();
            var dictionaries = new Dictionary<IDictionary, DictionaryEntry[]>();
            foreach (string name in new[] { "changes", "documentIds" })
            {
                var dictionary = (IDictionary)Profiler.GetField(name, Static).GetValue(null);
                dictionaries.Add(dictionary, dictionary.Keys.Cast<object>().Select(key => new DictionaryEntry(key, dictionary[key])).ToArray());
            }
            long previousChange = (long)Profiler.GetField("nextChange", Static).GetValue(null);
            var activeRt = RenderTexture.active; bool srgb = GL.sRGBWrite;
            WhimTexWindow window = null;
            var documents = new List<WhimTexDocument>();
            object cache = null;
            Texture2D texture = null;
            WhimTexDocument OwnDocument()
            {
                var document = ScriptableObject.CreateInstance<WhimTexDocument>();
                document.width = document.height = 32; document.hideFlags = HideFlags.HideAndDontSave;
                documents.Add(document); return document;
            }
            Color[] Render(WhimTexDocument document)
            {
                var rt = (RenderTexture)Invoke(document, "RenderCanvasWithCache", 32, cache, false, null);
                var read = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
                try { RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0, false); return read.GetPixels(); }
                finally { RenderTexture.active = activeRt; UnityEngine.Object.DestroyImmediate(read); RenderTexture.ReleaseTemporary(rt); }
            }
            Color[] Paint(WhimTexDocument document)
            {
                var drawing = new DrawingLayerBehaviour(); document.layers.Add(drawing); Invoke(document, "NormalizeModel");
                var type = Package.GetType("DCFApixels.WhimTex.PaintStrokeParameters", true);
                var constructor = type.GetConstructors(Instance).Single(c => c.GetParameters().Length == 10);
                var shape = Enum.ToObject(Package.GetType("DCFApixels.WhimTex.PencilShape", true), 0);
                var parameters = constructor.Invoke(new object[] { Color.white, 7f, .6f, .15f, false, false, shape, null, false, 1f });
                using (Span("brush.input_batch", document, drawing.Owner))
                {
                    Invoke(drawing, "PrepareStroke", 32, 32, "Owned profiler stroke");
                    Invoke(drawing, "BeginStroke", new Vector2(.2f, .3f));
                    Invoke(drawing, "PaintSegment", new Vector2(.2f, .3f), new Vector2(.8f, .7f), 32, 32, true, parameters);
                    Invoke(drawing, "SyncSurfaceToTexture");
                }
                var pixels = (Texture2D)Get(drawing, "pixels");
                Undo.ClearUndo(pixels); // Only the fixture's texture, never the global undo stack.
                return pixels.GetPixels();
            }
            try
            {
                var document = OwnDocument(); var other = OwnDocument();
                texture = new Texture2D(32, 32, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[1024];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)(x > 8 && x < 24 && y > 6 && y < 25 ? 255 : 0));
                texture.SetPixels32(pixels); texture.Apply();
                var file = new FileLayerBehaviour { sourceTexture = texture };
                var sdf = new SDFLayerBehaviour { inputMode = EffectInputMode.Specific };
                document.layers.Add(sdf); document.layers.Add(file); Invoke(document, "NormalizeModel");
                sdf.TargetLayerId = file.Id; file.layerName = "Owned source"; sdf.layerName = "Owned dependent";
                other.layers.Add(new ColorFillLayerBehaviour()); Invoke(other, "NormalizeModel");
                window = ScriptableObject.CreateInstance<WhimTexWindow>();
                Invoke(window, "SetDocument", document);
                cache = Activator.CreateInstance(Package.GetType("DCFApixels.WhimTex.EffectRenderCache", true), true);
                var baseline = Render(document); ((IDisposable)cache).Dispose();
                var brushBaseline = Paint(OwnDocument());

                // Isolate the synchronous probe without Start/Clear or touching the user's reload backup.
                object capture = Activator.CreateInstance(Profiler.GetNestedType("Capture", BindingFlags.NonPublic), true);
                long now = Stopwatch.GetTimestamp();
                Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", now);
                Set(capture, "deadline", now + 60 * Stopwatch.Frequency); Set(capture, "recording", true);
                Set(capture, "events", Array.CreateInstance(Profiler.GetNestedType("Sample", BindingFlags.NonPublic), 4096));
                foreach (var dictionary in dictionaries.Keys) dictionary.Clear();
                captureField.SetValue(null, capture);
                string docId = (string)Profile("DocumentId", document), otherId = (string)Profile("DocumentId", other);
                using (Span("edit.transform_update", document, file.Owner))
                {
                    using (Span("edit.commit", document)) Profile("Changed", document, "Move owned source");
                    Reason("render.trigger", "transform.live", document);
                    using (Span("edit.model", other)) Profile("Changed", other, "Change other document");
                    Reason("render.trigger", "document.changed", document);
                }
                var changed = Events().Single(e => (string)e["phase"] == "document.changed" && (string)e["documentId"] == docId);
                var trigger = Events().Last(e => (string)e["phase"] == "render.trigger");
                t.Equal((long)changed["id"], (long)trigger["changeEventId"], "Nested render sees latest change, not the parent's stale revision");
                t.Equal("Move owned source", (string)trigger["changeReason"], "Other document does not replace this reason");
                t.Equal("edit.transform_update", (string)trigger["changeOrigin"], "Commit wrapper does not hide originating edit");
                t.Equal(file.Id, (string)trigger["changedLayerId"], "Changed layer retained independently of rendered layer");
                file.layerName = "Renamed source";
                t.Equal("Owned source", (string)trigger["changedLayerName"], "Changed layer label is a snapshot");

                Set(capture, "documentId", docId);
                int count = Events().Length;
                using (Span("edit.model", other)) Reason("render.trigger", "filtered", other);
                t.Equal(count, Events().Length, "Document filter suppresses other document and its reasons");
                Reason("render.trigger", "root", document);
                t.Equal(0L, (long)Events().Last()["parentId"], "Root reasons do not need a timed scope");
                Set(capture, "documentId", null);

                t.True(baseline.SequenceEqual(Render(document)), "Recording does not change composite pixels");
                Render(document);
                sdf.threshold = 143; Invoke(document, "MarkChanged"); Render(document);
                var misses = Events().Where(e => (string)e["phase"] == "cache.miss").ToArray();
                t.True(misses.Any(e => (string)e["detail"] == "miss:not_retained"), "Cold cache has explicit reason");
                t.True(misses.Any(e => (string)e["detail"] == "miss:content_or_settings"), "Parameter edit records invalidation reason");
                t.True(misses.All(e => ((string)e["resourceKey"] == sdf.Id + "/effect" || (string)e["resourceKey"] == sdf.Id + "/effect/debug") && (string)e["layerId"] == sdf.Id),
                    "Cache reasons identify the dependent entry/layer: " + string.Join(",", misses.Select(e => e["resourceKey"] + ":" + e["layerId"])));
                t.True(Events().Any(e => (string)e["phase"] == "effect.cache" && (string)e["detail"] == "hit"), "Unchanged render reuses cache");

                Invoke(window, "PublishCanvasReasons"); // Drain requests from the real model-change callback.
                int requestStart = Events().Length;
                Invoke(window, "ProfileCanvasRequest", "brush.live"); Invoke(window, "ProfileCanvasRequest", "brush.live");
                Invoke(window, "ProfileCanvasRequest", "quality.settled_refinement");
                using (Span("ui.canvas_update", document)) Invoke(window, "PublishCanvasReasons");
                var requests = Events().Skip(requestStart).Where(e => (string)e["phase"] == "render.trigger").ToArray();
                t.Equal(2, requests.Length, "Repeated requests coalesce by reason");
                t.Equal(2d, (double)requests.Single(e => (string)e["detail"] == "brush.live")["value"], "Request count retained");
                Invoke(window, "PublishCanvasReasons");
                t.Equal(2, Events().Skip(requestStart).Count(e => (string)e["phase"] == "render.trigger"), "Publishing consumes pending requests");
                Invoke(window, "ProfileCanvasRequest", "stale"); Set(capture, "id", Guid.NewGuid().ToString("N"));
                Invoke(window, "PublishCanvasReasons");
                t.True(!Events().Any(e => (string)e["detail"] == "stale"), "Requests cannot leak across captures");

                Type selection = Package.GetType("DCFApixels.WhimTex.CanvasSelection", true);
                var all = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(selection),
                    typeof(ProfilerCoverageTests).GetMethod("SelectAll", Static).MakeGenericMethod(selection));
                Invoke(window, "ChangeAreaSelection", all);
                var firstMask = Invoke(window, "GetAreaSelectionTexture");
                t.True(firstMask != null && ReferenceEquals(firstMask, Invoke(window, "GetAreaSelectionTexture")), "Unchanged selection reuses uploaded mask");
                t.Equal(1, Events().Count(e => (string)e["phase"] == "selection.upload"), "Only actual mask uploads are measured");
                t.True(Events().Any(e => (string)e["phase"] == "selection.change"), "Selection operation is covered");
                int readbacks = Events().Count(e => (string)e["phase"] == "transfer.drawing_readback");
                Invoke(new DrawingLayerBehaviour(), "SyncSurfaceToTexture");
                t.Equal(readbacks, Events().Count(e => (string)e["phase"] == "transfer.drawing_readback"), "Missing paint surface does not report a GPU readback");
                t.True(brushBaseline.SequenceEqual(Paint(OwnDocument())), "Brush pixels unchanged by profiling");
                foreach (string phase in new[] { "brush.prepare", "brush.segment", "brush.submit_batch", "brush.stamp_count", "transfer.drawing_readback" })
                    t.True(Events().Any(e => (string)e["phase"] == phase), "Real stroke emits " + phase);
                t.True(Events().Where(e => (string)e["phase"] == "brush.stamp_count").All(e => (double)e["value"] > 0), "Stamp counts are per real batch");

                string json = WhimTexDocumentJson.Write(other).Json;
                using (var restored = WhimTexDocumentJson.Read(json, false))
                    t.Equal(json, WhimTexDocumentJson.Write(restored.Document).Json, "JSON remains unchanged by instrumentation");
                t.True(Events().Any(e => (string)e["phase"] == "storage.serialize_json"), "Serialization covered");
                t.True(Events().Any(e => (string)e["phase"] == "storage.deserialize_json"), "Deserialization covered");
                ((IDisposable)cache).Dispose();
                t.True(Events().Any(e => (string)e["phase"] == "cache.invalidate" && (string)e["detail"] == "cache.disposed"), "Cache disposal reason retained");
                Profile("Stop", "test_complete");
                string events = Read()["events"].ToString();
                t.Equal(0, (int)Read()["capture"]["openSpans"], "All measured operations complete");
                t.True(Events().Where(e => (string)e["kind"] == "reason").All(e => (double)e["durationMs"] == 0 && e["selfMs"].Type == "Null"), "Reasons are instantaneous, without self timing");
                var profilerWindow = (EditorWindow)ScriptableObject.CreateInstance(Package.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true));
                try
                {
                    Invoke(profilerWindow, "CreateGUI");
                    Invoke(profilerWindow, "SelectEvent", new object[] { new object[] { misses.Last().Raw } });
                    string detail = ((Label)Get(profilerWindow, "detailInfo")).text;
                    t.True(detail.Contains("Latest change:") && detail.Contains("Cache entry: " + sdf.Id) && detail.Contains("Occurrences: 1"),
                        "Event details expose change context, entry and reason count");
                    Invoke(profilerWindow, "SelectEvent", new object[] { new object[] { requests.Single(e => (string)e["detail"] == "brush.live").Raw } });
                    t.True(((Label)Get(profilerWindow, "detailInfo")).text.Contains("Occurrences: 2"), "Request aggregation is readable without raw JSON");
                }
                finally { UnityEngine.Object.DestroyImmediate(profilerWindow); }
                using (var stream = new MemoryStream())
                {
                    Profile("WriteSnapshot", stream, capture); stream.Position = 0;
                    captureField.SetValue(null, Profile("ReadSnapshot", stream));
                    t.Equal(events, Read()["events"].ToString(), "New provenance survives the reload serialization codec exactly");
                }
            }
            finally
            {
                captureField.SetValue(null, previousCapture);
                Profiler.GetField("current", Static).SetValue(null, -1);
                Profiler.GetField("nextChange", Static).SetValue(null, previousChange);
                foreach (var pair in dictionaries) { pair.Key.Clear(); foreach (var entry in pair.Value) pair.Key.Add(entry.Key, entry.Value); }
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                (cache as IDisposable)?.Dispose();
                foreach (var document in documents) if (document != null)
                {
                    foreach (var layer in document.layers)
                        if (layer.Behaviour is DrawingLayerBehaviour drawing && Get(drawing, "pixels") is Texture2D pixels)
                            Undo.ClearUndo(pixels);
                    UnityEngine.Object.DestroyImmediate(document);
                }
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                RenderTexture.active = activeRt; GL.sRGBWrite = srgb;
            }
            t.True(ReferenceEquals(previousCapture, captureField.GetValue(null)), "Retained user capture restored");
            if (previousCapture != null) t.Equal(previousEvents, Read()["events"].ToString(), "User capture events untouched");
        });
    }
}
