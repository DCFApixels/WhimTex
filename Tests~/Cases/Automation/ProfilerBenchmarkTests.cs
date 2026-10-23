using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = WhimTex.Tests.ApiJson;
using Object = UnityEngine.Object;

// Support: Framework/TestApi.cs, Framework/Unity/ApiJson.cs. Main agent runs via the
// existing reviewed Pipeline runner. No windows, files, assets, Undo, or actual reload.
public static class ProfilerBenchmarkTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    const string ReloadKey = "WhimTex.Profiler.BenchmarkResult";
    static readonly Type Benchmark = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexBenchmark", true);
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static readonly FieldInfo Current = Benchmark.GetField("current", Static);
    static object Invoke(string method, params object[] args) => Benchmark.GetMethod(method, Static).Invoke(null, args);
    static object Get(object value, string field) => value.GetType().GetField(field, Instance).GetValue(value);
    static void Set(object value, string field, object content) => value.GetType().GetField(field, Instance).SetValue(value, content);
    static double Number(Json value) => double.Parse(value.ToString(), CultureInfo.InvariantCulture);
    static Json Call(string action, params object[] fields)
    {
        var request = new Json { ["apiVersion"] = 1, ["action"] = action };
        for (int i = 0; i < fields.Length; i += 2) request[(string)fields[i]] = Json.FromObject(fields[i + 1]);
        return Json.Parse(WhimTexApi.ProfileJson(request.ToString()));
    }
    static Json Read(string id) => Call("benchmarkRead", "benchmarkId", id)["benchmark"];
    static string Id(WhimTexDocument document) => typeof(WhimTexDocument).Assembly
        .GetType("DCFApixels.WhimTex.UnityObjectID", true).GetMethod("FromObject", BindingFlags.Public | BindingFlags.Static)
        .Invoke(null, new object[] { document }).ToString();
    static IDisposable Suppress() => (IDisposable)Profiler.GetMethod("SuppressInstrumentation", Static).Invoke(null, null);
    static WhimTexDocument Document()
    {
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = 8; doc.height = 4;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
        return doc;
    }
    static Texture2D Pixels(Color color)
    {
        var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
        pixels.SetPixels(new[] { color, color, color, color }); pixels.Apply(false, false); return pixels;
    }

    public static string Statistics()
    {
        return TestContext.Run("Benchmark descriptive statistics and percentile convention", t =>
        {
            t.True(Invoke("Statistics", new List<double>()) == null, "No samples have no statistics");
            var values = new List<double> { 4, 1, 3, 2 };
            var result = Json.Parse(Invoke("Statistics", values).ToString());
            t.Equal(4, (int)result["count"], "Count");
            t.Near(2.5, Number(result["medianMs"]), 1e-12, "Even median");
            t.Near(1, Number(result["minMs"]), 0, "Minimum");
            t.Near(4, Number(result["maxMs"]), 0, "Maximum");
            t.Near(2.5, Number(result["meanMs"]), 1e-12, "Mean");
            t.Near(3.85, Number(result["p95Ms"]), 1e-12, "Interpolated p95");
            t.Near(Math.Sqrt(1.25), Number(result["stddevMs"]), 1e-12, "Population standard deviation");
            t.Equal(4d, values[0], "Statistics preserve sample order");
            var one = Json.Parse(Invoke("Statistics", new List<double> { 7 }).ToString());
            t.Near(7, Number(one["medianMs"]), 0, "Singleton median");
            t.Near(7, Number(one["p95Ms"]), 0, "Singleton p95");
            t.Near(0, Number(one["stddevMs"]), 0, "Singleton standard deviation");
            foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
            {
                bool rejected = false;
                try { Invoke("Statistics", new List<double> { invalid }); }
                catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentOutOfRangeException; }
                t.True(rejected, "Invalid sample rejected: " + invalid);
            }
            Invoke("Validate", "opaque-doc-id", "both", 30, 5, 2048);
            t.True(true, "Upper bounds are accepted without scheduling work");
        });
    }

    public static string Run()
    {
        var existing = Current.GetValue(null);
        if (existing != null && (string)Get(existing, "state") == "running")
            return TestContext.Result("skipped", 0, "A user benchmark is running; it was left untouched.").ToJson();
        var captureField = Profiler.GetField("capture", Static);
        var baselineField = Profiler.GetField("<Baseline>k__BackingField", Static);
        object savedCapture = captureField.GetValue(null), savedBaseline = baselineField.GetValue(null);
        if (savedCapture != null && ((bool)Get(savedCapture, "recording") || (int)Get(savedCapture, "open") != 0))
            return TestContext.Result("skipped", 0, "An active user capture was left untouched.").ToJson();
        return TestContext.Run("Benchmark validation, isolated Drawing/dependencies, real render and cleanup", t =>
        {
            var retainedField = Benchmark.GetField("retained", Static);
            object savedRetained = retainedField.GetValue(null);
            string savedReload = SessionState.GetString(ReloadKey, "");
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var guard = RenderTexture.GetTemporary(2, 2, 0);
            WhimTexDocument doc = null;
            Texture2D dependency = null, drawingPixels = null;
            try
            {
                doc = Document();
                var fill = (ColorFillLayerBehaviour)doc.layers[0].Behaviour;
                var originalPreview = fill.GetPreviewTexture(8);
                dependency = Pixels(Color.green);
                doc.layers.Add(new Layer(new FileLayerBehaviour { sourceTexture = dependency }));
                var drawing = new DrawingLayerBehaviour();
                var dynamicsType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.BrushDynamics", true);
                Set(drawing, "brushDynamics", Activator.CreateInstance(dynamicsType, true));
                drawingPixels = Pixels(Color.blue); Set(drawing, "pixels", drawingPixels);
                doc.layers.Add(new Layer(drawing));
                typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance).Invoke(doc, null);

                // A private fixture capture proves suppression works even during recording.
                var captureType = Profiler.GetNestedType("Capture", BindingFlags.NonPublic);
                var sampleType = Profiler.GetNestedType("Sample", BindingFlags.NonPublic);
                object capture = Activator.CreateInstance(captureType, true);
                Set(capture, "id", "benchmark-test-capture"); Set(capture, "recording", true);
                Set(capture, "deadline", long.MaxValue);
                Set(capture, "events", Array.CreateInstance(sampleType, 256));
                captureField.SetValue(null, capture);
                baselineField.SetValue(null, savedBaseline);

                string original = JsonUtility.ToJson(doc);
                RenderTexture.active = guard; GL.sRGBWrite = true;
                foreach (object[] fields in new[] {
                    new object[] { }, new object[] { "documentId", "" },
                    new object[] { "documentId", Id(doc), "mode", "unknown" },
                    new object[] { "documentId", Id(doc), "repetitions", 0 },
                    new object[] { "documentId", Id(doc), "repetitions", 31 },
                    new object[] { "documentId", Id(doc), "repetitions", 1.5 },
                    new object[] { "documentId", Id(doc), "warmups", -1 },
                    new object[] { "documentId", Id(doc), "warmups", 6 },
                    new object[] { "documentId", Id(doc), "maxSize", 2049 },
                    new object[] { "documentId", Id(doc), "maxSize", 0 },
                    new object[] { "documentId", Id(doc), "captureId", "unrelated" } })
                    t.True(!(bool)Call("benchmarkStart", fields)["success"], "Invalid benchmark request rejected");
                t.True(!(bool)Call("benchmarkRead")["success"], "Read requires an ID");
                t.True(!(bool)Call("benchmarkCancel")["success"], "Cancel requires an ID");
                t.True(!(bool)Call("benchmarkStart", "documentId", "no-such-document")["success"], "Unknown document rejected");

                var started = Call("benchmarkStart", "documentId", Id(doc), "repetitions", 2, "maxSize", 4);
                t.True((bool)started["success"], "Start request accepted: " + started);
                string id = (string)started["benchmark"]["benchmarkId"];
                object run = Current.GetValue(null);
                t.Equal("running", (string)Read(id)["state"], "Snapshot successful: " + Read(id));
                t.Equal(0, (int)Read(id)["completedSamples"], "Start does not render a sample");
                t.Equal(4, (int)Read(id)["metadata"]["width"], "Bounded width");
                t.Equal(2, (int)Read(id)["metadata"]["height"], "Aspect ratio preserved");
                string hash = (string)Read(id)["metadata"]["contentHash"];
                t.Equal(64, hash.Length, "SHA-256 content hash");
                t.Equal(original, JsonUtility.ToJson(doc), "Source serialized model unchanged by snapshot");
                t.True(ReferenceEquals(originalPreview, Get(fill, "cachedPreview")), "Source layer cache preserved");
                t.True(RenderTexture.active == guard && GL.sRGBWrite, "Start restores caller render state");
                t.True(!(bool)Call("benchmarkStart", "documentId", Id(doc))["success"], "Concurrent run rejected");
                t.True(!(bool)Call("benchmarkCancel", "benchmarkId", "stale")["success"], "Stale cancel rejected");

                var snapshot = (WhimTexDocument)Get(run, "snapshot");
                var frozenFile = ((FileLayerBehaviour)snapshot.layers[1].Behaviour).sourceTexture;
                var frozenDrawing = (Texture2D)Get((DrawingLayerBehaviour)snapshot.layers[2].Behaviour, "pixels");
                t.True(frozenFile != dependency && frozenDrawing != drawingPixels, "Both kinds of texture are independently owned");
                object frozenDynamics = Get((DrawingLayerBehaviour)snapshot.layers[2].Behaviour, "brushDynamics");
                t.True(frozenDynamics != null && ReferenceEquals(null, dynamicsType.GetField("tip").GetValue(frozenDynamics)),
                    "Default null brush tip is normalized to CLR null before texture snapshot access");
                dependency.SetPixel(0, 0, Color.red); dependency.Apply(false, false);
                drawingPixels.SetPixel(0, 0, Color.red); drawingPixels.Apply(false, false);
                fill.color = Color.yellow;
                t.Near(1, frozenFile.GetPixel(0, 0).g, .001, "File dependency snapshot is frozen");
                t.Near(1, frozenDrawing.GetPixel(0, 0).b, .001, "Drawing snapshot is frozen");
                t.Equal(hash, (string)Read(id)["metadata"]["contentHash"], "Source edits cannot change accepted snapshot hash");

                Invoke("Update");
                t.Equal(1, (int)Read(id)["completedSamples"], "One update adds exactly one cold sample");
                t.True(Get(run, "document") == null && Get(run, "cache") == null, "Cold sample destroys owned render/cache");
                Invoke("Update");
                t.Equal(2, Read(id)["cold"]["samplesMs"].Count, "Second cold sample");
                Invoke("Update");
                t.Equal(1, (int)Read(id)["completedWarmups"], "One excluded preparatory warmup");
                t.Equal(2, (int)Read(id)["completedSamples"], "Warmup excluded from samples");
                object warmDocument = Get(run, "document"), warmCache = Get(run, "cache");
                Invoke("Update");
                t.True(ReferenceEquals(warmDocument, Get(run, "document")) && ReferenceEquals(warmCache, Get(run, "cache")), "Warm samples reuse owned document/cache");
                Invoke("Update");
                t.Equal("completed", (string)Read(id)["state"], "Both modes finish");
                t.Equal(2, Read(id)["warm"]["samplesMs"].Count, "Two measured warm samples");
                t.True(Get(run, "snapshot") == null && Get(run, "document") == null && Get(run, "cache") == null, "Completion releases all owned state");
                t.True(frozenFile == null && frozenDrawing == null, "Completion destroys frozen dependency pixels");
                t.True(RenderTexture.active == guard && GL.sRGBWrite, "All samples restore caller render state");
                t.True(ReferenceEquals(capture, captureField.GetValue(null)) && (int)Get(capture, "count") == 0 && (bool)Get(capture, "recording"), "Capture remains recording, unreplaced and unpolluted");
                t.True(ReferenceEquals(savedBaseline, baselineField.GetValue(null)), "Baseline identity unchanged");
                t.True(dependency != null && drawingPixels != null && originalPreview != null, "Source textures/cache remain alive");

                CheckStops(t, doc);
                CheckPendingDrawing(t);
                CheckUnreadableDependency(t);
                CheckTimeValidation(t);
            }
            finally
            {
                object ownedRun = Current.GetValue(null);
                if (ownedRun != null && (string)Get(ownedRun, "state") == "running")
                    Invoke("Cancel", (string)Get(ownedRun, "id"));
                Current.SetValue(null, existing); retainedField.SetValue(null, savedRetained);
                if (savedReload.Length == 0) SessionState.EraseString(ReloadKey); else SessionState.SetString(ReloadKey, savedReload);
                captureField.SetValue(null, savedCapture); baselineField.SetValue(null, savedBaseline);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(guard);
                using (Suppress()) { if (doc != null) Object.DestroyImmediate(doc); }
                if (dependency != null) Object.DestroyImmediate(dependency);
                if (drawingPixels != null) Object.DestroyImmediate(drawingPixels);
            }
        });
    }

    static void CheckStops(TestContext t, WhimTexDocument doc)
    {
        string Start(string mode = "warm")
        {
            var response = Call("benchmarkStart", "documentId", Id(doc), "mode", mode, "warmups", 0, "maxSize", 4);
            t.True((bool)response["success"] && (string)response["benchmark"]["state"] == "running", "Stop fixture starts: " + response);
            t.Equal(7, (int)response["benchmark"]["repetitions"], "Default repetitions");
            return (string)response["benchmark"]["benchmarkId"];
        }
        string id = Start();
        string firstHash = (string)Read(id)["metadata"]["contentHash"];
        object run = Current.GetValue(null);
        Invoke("Update");
        t.Equal("cancelled", (string)Call("benchmarkCancel", "benchmarkId", id)["benchmark"]["state"], "Cancellation stops the run");
        t.Equal(1, Read(id)["warm"]["samplesMs"].Count, "Cancellation retains partial measurements");
        t.True((bool)Read(id)["partial"] && Get(run, "document") == null && Get(run, "snapshot") == null, "Cancellation cleans owned state");
        t.True((bool)Call("benchmarkCancel", "benchmarkId", id)["success"], "Cancel terminal run is idempotent");
        id = Start("cold"); run = Current.GetValue(null); Set(run, "deadline", 0L); Invoke("Update");
        t.Equal(firstHash, (string)Read(id)["metadata"]["contentHash"], "Unchanged document has a stable snapshot hash across starts");
        t.Equal("timedOut", (string)Read(id)["state"], "Deadline checked before any render");
        t.Equal(0, (int)Read(id)["completedSamples"], "Timed-out run makes no sample");
        t.True(Get(run, "snapshot") == null, "Deadline cleans snapshot");
        id = Start(); run = Current.GetValue(null);
        int beforeFailure = Resources.FindObjectsOfTypeAll<WhimTexDocument>().Length;
        ((WhimTexDocument)Get(run, "snapshot")).pixelScale = 0;
        Invoke("Update");
        t.Equal("failed", (string)Read(id)["state"], "Owned rendering failure is reported");
        t.True(!string.IsNullOrEmpty((string)Read(id)["error"]) && Get(run, "snapshot") == null, "Failure includes error and cleans up");
        t.True(((string)Read(id)["error"]).Contains("normalize_model") &&
            ((string)Read(id)["error"]).Contains("CreateBenchmarkCopy"), "Failure retains the snapshot stage and original stack trace");
        t.Equal(beforeFailure - 1, Resources.FindObjectsOfTypeAll<WhimTexDocument>().Length, "Failed clone and frozen snapshot are both released");
        id = Start(); run = Current.GetValue(null); Invoke("Update"); Invoke("BeforeReload");
        t.Equal("interrupted", (string)Read(id)["state"], "Reload callback interrupts");
        t.Equal("assembly_reload", (string)Read(id)["stopReason"], "Reload reason");
        var saved = Json.Parse(SessionState.GetString(ReloadKey, ""));
        t.Equal(id, (string)saved["benchmarkId"], "Reload retains report identity");
        t.Equal(1, saved["warm"]["samplesMs"].Count, "Reload retains partial measurements");
        t.True(Get(run, "snapshot") == null && Get(run, "cache") == null, "Reload releases native state");
    }

    static void CheckPendingDrawing(TestContext t)
    {
        var doc = Document();
        var drawing = new DrawingLayerBehaviour();
        var layer = new Layer(drawing) { colorRange = LayerColorRange.HDR };
        var surface = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        Texture2D stored = Pixels(Color.blue);
        try
        {
            surface.Create(); var previous = RenderTexture.active;
            try { RenderTexture.active = surface; GL.Clear(false, true, Color.green); }
            finally { RenderTexture.active = previous; }
            Set(drawing, "pixels", stored); Set(drawing, "paintSurface", surface); Set(drawing, "paintSurfaceDirty", true);
            doc.layers.Add(layer);
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance).Invoke(doc, null);
            string id = (string)Call("benchmarkStart", "documentId", Id(doc), "mode", "cold")["benchmark"]["benchmarkId"];
            t.Equal("running", (string)Read(id)["state"], "Pending Drawing snapshot succeeds");
            var frozen = (WhimTexDocument)Get(Current.GetValue(null), "snapshot");
            var texture = (Texture2D)Get((DrawingLayerBehaviour)frozen.layers[1].Behaviour, "pixels");
            t.Near(1, texture.GetPixel(0, 0).g, .002, "Pending GPU pixels captured");
            t.Near(1, stored.GetPixel(0, 0).b, .002, "Original stored pixels unchanged");
            t.True((bool)Get(drawing, "paintSurfaceDirty") && ReferenceEquals(surface, Get(drawing, "paintSurface")), "Source pending state/cache untouched");
            Call("benchmarkCancel", "benchmarkId", id);
        }
        finally
        {
            using (Suppress()) Object.DestroyImmediate(doc);
            if (surface != null) { surface.Release(); Object.DestroyImmediate(surface); }
            if (stored != null) Object.DestroyImmediate(stored);
        }
    }

    static void CheckUnreadableDependency(TestContext t)
    {
        var doc = Document(); var texture = Pixels(Color.green);
        try
        {
            texture.Apply(false, true);
            doc.layers.Add(new Layer(new FileLayerBehaviour { sourceTexture = texture }));
            typeof(WhimTexDocument).GetMethod("NormalizeModel", Instance).Invoke(doc, null);
            string id = (string)Call("benchmarkStart", "documentId", Id(doc), "mode", "cold", "repetitions", 1)["benchmark"]["benchmarkId"];
            t.Equal("running", (string)Read(id)["state"], "Unreadable texture snapshot succeeds");
            var frozen = (WhimTexDocument)Get(Current.GetValue(null), "snapshot");
            var pixels = ((FileLayerBehaviour)frozen.layers[1].Behaviour).sourceTexture;
            t.True(pixels != texture && pixels.isReadable, "Non-readable dependency becomes owned/readable");
            t.Near(1, pixels.GetPixel(0, 0).g, .002, "Dependency readback preserves color");
            Invoke("Update");
            t.Equal("completed", (string)Read(id)["state"], "Minimal actual render completes");
        }
        finally { using (Suppress()) Object.DestroyImmediate(doc); Object.DestroyImmediate(texture); }
    }

    static void CheckTimeValidation(TestContext t)
    {
        var effect = ScriptableObject.CreateInstance<ShaderFX>();
        effect.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            string code = "float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * _Time.y; }";
            Set(effect, "code", code); Set(effect, "appliedCode", code); Set(effect, "appliedSource", code);
            Set(effect, "compiledShader", Shader.Find("Hidden/InternalErrorShader"));
            var layer = new Layer(new ColorFillLayerBehaviour()); layer.fx.Add(effect);
            bool rejected = false;
            try { Invoke("CheckOwnedEffects", new List<Layer> { layer }); }
            catch (TargetInvocationException e) { rejected = e.InnerException.Message.Contains("Time-dependent"); }
            t.True(rejected, "Known Unity time inputs are rejected without compiling shaders");
        }
        finally
        {
            // The validation fixture borrowed a built-in shader and must never destroy it.
            Set(effect, "compiledShader", null); Object.DestroyImmediate(effect);
        }
    }
}
