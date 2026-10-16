using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerAnalysisTests.Json;

public static class ProfilerDiagnosticsTests
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly A = typeof(WhimTexDocument).Assembly;
    static readonly Type P = A.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static object Call(string name, params object[] args) => P.GetMethod(name, S).Invoke(null, args);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, I).SetValue(o, value);
    static object Get(object o, string name) => o.GetType().GetField(name, I).GetValue(o);
    static object Invoke(object o, string name, params object[] args) => o.GetType().GetMethod(name, I).Invoke(o, args);
    static Json Api(string action, string fields = "") => Json.Parse(WhimTexApi.ProfileJson("{\"apiVersion\":1,\"action\":\"" + action + "\"" + fields + "}"));
    static Json[] Events() => Api("read", ",\"limit\":2000")["events"].ToArray();
    static IDisposable Span(string name, WhimTexDocument doc = null, Layer layer = null) => (IDisposable)Call("Measure", name, doc, layer, -1, null);

    public static string Run()
    {
        var status = Api("status")["capture"];
        if (status.Type != "Null" && ((bool)status["recording"] || (int)status["openSpans"] != 0 || (int)status["pendingGpuSamples"] != 0))
            return TestContext.Result("skipped", 0, "Needs idle profiler; user recording untouched.").ToJson();
        return TestContext.Run("Memory ownership, GC accounting and linked recalculation evidence", t =>
        {
            var field = P.GetField("capture", S); object saved = field.GetValue(null);
            var baselineField = P.GetField("<Baseline>k__BackingField", S); object savedBaseline = baselineField.GetValue(null);
            object cache = null; WhimTexDocument document = null; Texture2D texture = null;
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                object capture = Activator.CreateInstance(P.GetNestedType("Capture", BindingFlags.NonPublic), true);
                Set(capture, "id", Guid.NewGuid().ToString("N")); Set(capture, "start", Stopwatch.GetTimestamp());
                Set(capture, "deadline", long.MaxValue); Set(capture, "recording", true);
                Set(capture, "events", Array.CreateInstance(P.GetNestedType("Sample", BindingFlags.NonPublic), 16384));
                field.SetValue(null, capture); Call("ResetMemory"); Call("ResetCauses");
                Call("PrepareAllocations", capture);
                using (Span("test.root"))
                {
                    using (Span("test.child")) { var bytes = new byte[8192]; GC.KeepAlive(bytes); }
                    texture = new Texture2D(16, 8, TextureFormat.RGBAFloat, false, true);
                    Call("TrackTexture", texture, "owned texture", false);
                    Call("TrackTexture", texture, "owned texture", false);
                    t.Near(2048, (double)Api("status")["capture"]["memory"]["trackedTextureBytes"], 0, "Texture payload and deduplication");
                    Call("ReleaseTexture", texture); Call("ReleaseTexture", texture);
                    t.Near(0, (double)Api("status")["capture"]["memory"]["trackedTextureBytes"], 0, "Repeated release does not underflow");
                    Call("TrackTexture", texture, "existing texture", true); Call("ReleaseTexture", texture);
                    object native = new object(); Call("NativeCacheBytes", native, 1024L); Call("NativeCacheBytes", native, 512L); Call("NativeCacheBytes", native, 0L);
                }
                var memory = Api("status")["capture"]["memory"];
                t.Near(1, (double)memory["acquireCount"], 0, "Observation is not an allocation");
                t.Near(2, (double)memory["releaseCount"], 0, "Both ownership lifetimes released");
                t.Near(2048, (double)memory["peakTrackedTextureBytes"], 0, "Peak payload retained");
                t.Near(2048, (double)memory["observedBytes"], 0, "Preexisting payload separate");
                t.Near(0, (double)memory["nativeCacheBytes"], 0, "Native cache released");
                t.Near(1024, (double)memory["peakNativeCacheBytes"], 0, "Native peak separate from texture memory");
                var root = Events().Single(e => (string)e["phase"] == "test.root");
                var child = Events().Single(e => (string)e["phase"] == "test.child");
                t.True((bool)memory["editorManagedAllocationAvailable"], "Unity Editor byte counter available");
                t.True((double)memory["editorManagedAllocatedBytes"] >= 8192, "Editor-wide allocation bytes measured");
                t.Equal("all_editor_threads", (string)memory["editorManagedAllocationScope"], "Editor bytes explicitly scoped");
                t.True((double)child["gcAllocationCount"] >= 1, "Main-thread allocation count measured");
                t.Near((double)root["gcAllocationCount"] - (double)child["gcAllocationCount"], (double)root["gcSelfAllocationCount"], 0, "Child allocations not double-counted in self");
                t.True(root["gcAllocatedBytes"] == null, "No misleading per-stage byte attribution");
                int previous = Events().Length;
                using ((IDisposable)Call("SuppressInstrumentation"))
                using (Span("must.not.record")) { Call("TrackTexture", texture, "suppressed", false); }
                t.Equal(previous, Events().Length, "Benchmark suspension excludes events and resources");

                document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
                document.width = document.height = 24;
                var source = new ColorFillLayerBehaviour(); var sdf = new SDFLayerBehaviour { inputMode = EffectInputMode.Specific };
                document.layers.Add(sdf); document.layers.Add(source); Invoke(document, "NormalizeModel");
                sdf.TargetLayerId = source.Id;
                Call("TrackTexture", texture, "no layer owner", false);
                using (Span("test.release_elsewhere", document, document.layers[0])) Call("ReleaseTexture", texture);
                var release = Events().Last(e => (string)e["phase"] == "resource.release");
                t.Equal("Null", release["documentId"].Type, "Ownerless resource does not inherit releasing document");
                t.Equal("Null", release["layerId"].Type, "Ownerless resource does not inherit releasing layer");
                using (Span("test.acquire_owner", document, document.layers[1])) Call("TrackTexture", texture, "source owner", false);
                using (Span("test.release_elsewhere", document, document.layers[0])) Call("ReleaseTexture", texture);
                release = Events().Last(e => (string)e["phase"] == "resource.release");
                t.Equal(source.Id, (string)release["layerId"], "Release keeps original owner, not the currently executing layer");
                cache = Activator.CreateInstance(A.GetType("DCFApixels.WhimTex.EffectRenderCache", true), true);
                void Render()
                {
                    var rt = (RenderTexture)Invoke(document, "RenderCanvasWithCache", 24, cache, false, null);
                    A.GetType("DCFApixels.WhimTex.ProfiledRenderTexture").GetMethod("ReleaseTemporary", S).Invoke(null, new object[] { rt });
                }
                Render();
                t.True(!Events().Any(e => (string)e["phase"] == "edit.observed_change"), "First source observation has no invented prior state");
                source.color = new Color(.25f, .5f, .75f, .7f); Render();
                var changed = Events().Where(e => (string)e["phase"] == "edit.observed_change").ToArray();
                t.True(changed.Length > 0 && changed.All(e => (string)e["layerId"] == source.Id), "Property changes belong to the actual source layer");
                t.True(changed.Any(e => ((string)e["causeProperty"]).Contains("storedColor")), "Changed property path retained");
                var miss = Events().Last(e => (string)e["phase"] == "cache.miss" && (string)e["detail"] == "miss:content_or_settings");
                t.Equal(source.Id, (string)miss["causeLayerId"], "Dependent miss points to source, not last unrelated edit");
                t.Equal("dependency", (string)miss["causeKind"], "Dependency evidence labeled");
                string id = (string)Get(capture, "id");
                var explain = Api("explain", ",\"captureId\":\"" + id + "\",\"eventId\":" + miss["id"]);
                t.True((bool)explain["success"], "Explain current event");
                t.True(explain["nodes"].Any(e => (string)e["phase"] == "cache.dependency_changed"), "Explain includes dependency edge");
                t.True(explain["nodes"].Count(e => (string)e["phase"] == "edit.observed_change") > 1, "Explain retains multiple changed properties");
                var limited = Api("explain", ",\"captureId\":\"" + id + "\",\"eventId\":" + miss["id"] + ",\"limit\":1");
                t.True((bool)limited["truncated"], "Bounded explanation indicates omitted nodes");
                t.True(!(bool)Api("explain", ",\"captureId\":\"" + id + "\"")["success"], "Explain requires event ID");
                t.True(!(bool)Api("explain", ",\"captureId\":\"stale\",\"eventId\":1")["success"], "Explain rejects stale capture");
                t.Equal(0, Api("summary", ",\"groups\":[]")["brief"]["causes"].Count(), "Compact causes respect filters");
                int changesBefore = Events().Count(e => (string)e["phase"] == "edit.observed_change");
                Render();
                t.Equal(changesBefore, Events().Count(e => (string)e["phase"] == "edit.observed_change"), "Unchanged render does not invent new changes");
                ((IDisposable)cache).Dispose(); cache = null;
                var balances = Events().Where(e => (string)e["kind"] == "resource" && (string)e["resourceKey"] != null)
                    .GroupBy(e => (string)e["resourceKey"]).Select(g => new { key = g.Key, name = (string)g.First()["detail"],
                        bytes = g.Sum(e => ((string)e["phase"] == "resource.release" ? -1 : 1) * (double)e["value"]) })
                    .Where(v => v.bytes != 0).ToArray();
                // The document still owns its last diagnostic mask; one 1x1 readback
                // target per render can also remain alive until the asynchronous callback.
                var mask = (Texture)typeof(WhimTexDocument).GetProperty("NumericErrorMask", I).GetValue(document);
                string maskId = A.GetType("DCFApixels.WhimTex.UnityObjectID").GetMethod("FromObject", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { mask }).ToString();
                t.Near(24 * 24 * 4, balances.Single(v => v.key == maskId).bytes, 0, "Live document diagnostic mask remains owned");
                var pending = balances.Where(v => v.key != maskId).ToArray();
                t.True(pending.Length <= 3 && pending.All(v => v.bytes == 4 && v.name == "temporary RT checkout"),
                    "Only at most three pending 1x1 diagnostic readbacks remain after output/cache release");
                double pendingBytes = pending.Sum(v => v.bytes);
                UnityEngine.Object.DestroyImmediate(document); document = null;
                t.Near(pendingBytes, (double)Api("status")["capture"]["memory"]["trackedTextureBytes"], 0,
                    "Document destruction releases the mask; pending buffers stay owned until their callbacks");
                Call("Stop", "test_complete"); Call("ShutdownGpu");
                foreach (string tracker in new[] { "trackedTextures", "trackedNativeCaches", "settingsSnapshots", "layerCauses" })
                    t.Equal(0, ((IDictionary)P.GetField(tracker, S).GetValue(null)).Count, "Stop releases live tracker references: " + tracker);
                string savedMemory = Api("status")["capture"]["memory"].ToString();
                using (var stream = new MemoryStream())
                {
                    Call("WriteSnapshot", stream, capture); stream.Position = 0;
                    field.SetValue(null, Call("ReadSnapshot", stream));
                    t.Equal(savedMemory, Api("status")["capture"]["memory"].ToString(), "Memory survives reload codec");
                    t.Equal(explain["nodes"].ToString(), Api("explain", ",\"captureId\":\"" + id + "\",\"eventId\":" + miss["id"])["nodes"].ToString(), "Cause graph survives reload codec");
                }
            }
            finally
            {
                using ((IDisposable)Call("SuppressInstrumentation"))
                {
                    (cache as IDisposable)?.Dispose();
                    if (document != null) UnityEngine.Object.DestroyImmediate(document);
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                }
                Call("ShutdownGpu"); Call("ResetMemory"); Call("ResetCauses");
                field.SetValue(null, saved); baselineField.SetValue(null, savedBaseline);
                P.GetField("current", S).SetValue(null, -1); P.GetField("suppressed", S).SetValue(null, 0);
                RenderTexture.active = active; GL.sRGBWrite = srgb;
            }
            t.True(ReferenceEquals(saved, field.GetValue(null)) && ReferenceEquals(savedBaseline, baselineField.GetValue(null)), "User capture and baseline preserved");
        });
    }
}
