using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    // This service never starts, stops, reads, or replaces a profiler capture/baseline.
    [InitializeOnLoad]
    internal static class WhimTexBenchmark
    {
        internal const int DefaultRepetitions = 7, MaximumRepetitions = 30;
        internal const int DefaultWarmups = 1, MaximumWarmups = 5;
        internal const int DefaultMaxSize = 512, MaximumSize = 2048;
        internal const double DeadlineSeconds = 120;
        internal const long MaximumSnapshotBytes = 128L * 1024 * 1024;
        const string ReloadKey = "WhimTex.Profiler.BenchmarkResult";
        static readonly int MainThread = Thread.CurrentThread.ManagedThreadId;
        static Run current;
        static JObject retained;

        internal sealed class Run
        {
            internal string id, documentId, mode, state = "running", phase = "snapshot", stopReason, error;
            internal int repetitions, warmups, maxSize, completedWarmups;
            internal long start, end, deadline;
            internal readonly List<double> cold = new(), warm = new();
            internal JObject metadata;
            internal WhimTexDocument snapshot, document;
            internal EffectRenderCache cache;
            internal bool Running => state == "running";
        }

        static WhimTexBenchmark()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += OnQuit;
            string saved = SessionState.GetString(ReloadKey, "");
            if (saved.Length != 0 && saved.Length <= 65536)
                try { retained = JObject.Parse(saved); } catch (Exception) { SessionState.EraseString(ReloadKey); }
        }

        static void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThread)
                throw new WhimTexApiException("benchmark_thread", "Benchmarks require the Editor main thread.");
        }

        internal static void Validate(string documentId, string mode, int repetitions, int warmups, int maxSize)
        {
            AgentJson.Require(!string.IsNullOrWhiteSpace(documentId), "documentId is required.");
            AgentJson.Require(mode == "cold" || mode == "warm" || mode == "both", "mode must be cold, warm or both.");
            AgentJson.Require(repetitions >= 1 && repetitions <= MaximumRepetitions, "repetitions must be between 1 and 30.");
            AgentJson.Require(warmups >= 0 && warmups <= MaximumWarmups, "warmups must be between 0 and 5.");
            AgentJson.Require(maxSize >= 1 && maxSize <= MaximumSize, "maxSize must be between 1 and 2048.");
        }

        internal static JObject Start(string documentId, string mode, int repetitions, int warmups, int maxSize)
        {
            RequireMainThread();
            Validate(documentId, mode, repetitions, warmups, maxSize);
            AgentJson.Require(current?.Running != true, "A benchmark is already running.", "benchmark_active");
            AgentJson.Require(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode,
                "Wait for the Editor to finish importing/compiling and exit Play mode.", "benchmark_editor_busy");
            WhimTexDocument source = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
                if (candidate != current?.snapshot && candidate != current?.document &&
                    UnityObjectID.FromObject(candidate).ToString() == documentId) { source = candidate; break; }
            AgentJson.Require(source != null && !AssetDatabase.Contains(source),
                "No in-memory document matches this profiler documentId. Rediscover documents after reload.", "benchmark_document_missing");
            AgentJson.Require(string.IsNullOrEmpty(source.documentLoadWarning), "An incomplete document cannot be benchmarked.", "benchmark_snapshot_invalid");

            long now = Stopwatch.GetTimestamp();
            var run = new Run { id = Guid.NewGuid().ToString("N"), documentId = documentId, mode = mode,
                repetitions = repetitions, warmups = warmups, maxSize = maxSize, start = now,
                deadline = now + (long)(DeadlineSeconds * Stopwatch.Frequency) };
            // Freeze at acceptance. Reading the live document on later updates would race user edits.
            current = run; retained = null; SessionState.EraseString(ReloadKey);
            using var suppression = WhimTexProfiler.SuppressInstrumentation();
            using var renderState = new RenderState(true);
            string stage = "create_snapshot";
            try
            {
                run.snapshot = WhimTexDocument.CreateBenchmarkCopy(source);
                stage = "metadata";
                run.metadata = Metadata(run, source.name);
                stage = "validate_effects";
                CheckOwnedEffects(run.snapshot.layers);
                stage = "content_hash";
                run.metadata["contentHash"] = ContentHash(run.snapshot);
                run.phase = mode == "warm" ? "warmup" : "cold";
                if (Expired(run)) Finish(run, "timedOut", "deadline");
            }
            catch (Exception e) { Finish(run, "failed", "snapshot_failed", stage + ": " + e); }
            return Report(run);
        }

        internal static JToken Status(string id = null)
        {
            RequireMainThread();
            JToken report = current != null ? Report(current) : retained?.DeepClone() ?? JValue.CreateNull();
            if (id != null) AgentJson.Require(report is JObject && (string)report["benchmarkId"] == id,
                "The benchmark ID is missing or no longer current. Read benchmarkStatus first.", "benchmark_conflict");
            return report;
        }

        internal static JToken Cancel(string id)
        {
            RequireMainThread();
            AgentJson.Require(!string.IsNullOrWhiteSpace(id), "benchmarkId is required.");
            Status(id);
            if (current?.Running == true) Finish(current, "cancelled", "cancelled");
            return Status(id);
        }

        static bool Expired(Run run) => Stopwatch.GetTimestamp() >= run.deadline;

        static void Update()
        {
            Run run = current;
            if (run?.Running != true) return;
            if (Expired(run)) { Finish(run, "timedOut", "deadline"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { Finish(run, "interrupted", "editor_state_changed"); return; }
            using var suppression = WhimTexProfiler.SuppressInstrumentation();
            using var renderState = new RenderState(true);
            try
            {
                bool cold = run.phase == "cold";
                if (run.document == null)
                {
                    run.document = WhimTexDocument.CreateBenchmarkCopy(run.snapshot);
                    run.cache = new EffectRenderCache { BudgetBytes = MaximumSnapshotBytes };
                }
                if (Expired(run)) { Finish(run, "timedOut", "deadline"); return; }
                // Exactly one measured render OR preparatory warmup per Editor update.
                double elapsed = RenderSample(run);
                if (cold) run.cold.Add(elapsed);
                else if (run.completedWarmups < run.warmups)
                {
                    run.completedWarmups++;
                    if (run.completedWarmups == run.warmups) run.phase = "warm";
                }
                else { run.phase = "warm"; run.warm.Add(elapsed); }

                if (Expired(run)) { Finish(run, "timedOut", "deadline"); return; }

                if (cold)
                {
                    ReleaseRender(run);
                    if (run.cold.Count == run.repetitions)
                    {
                        if (run.mode == "cold") { Finish(run, "completed", "completed"); return; }
                        run.phase = "warmup";
                    }
                }
                else if (run.warm.Count == run.repetitions) { Finish(run, "completed", "completed"); return; }
            }
            catch (Exception e) { Finish(run, "failed", "render_failed", e.ToString()); }
        }

        static double RenderSample(Run run)
        {
            RenderTexture output = null;
            Texture2D readback = null;
            long start = Stopwatch.GetTimestamp();
            try
            {
                output = run.document.RenderCanvasWithCache(run.maxSize, run.cache, false, null);
                if (output == null) throw new InvalidOperationException("The renderer returned no output.");
                // ReadPixels is the explicit completion boundary for this output. This is
                // CPU wall elapsed including GPU/queue waits and transfer, never GPU duration.
                readback = HdrUtility.ReadLinear(output, upload: false);
                return (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            }
            finally
            {
                if (readback != null) Object.DestroyImmediate(readback);
                if (output != null) RenderTexture.ReleaseTemporary(output);
            }
        }

        static void ReleaseRender(Run run)
        {
            try { run.cache?.Dispose(); }
            finally
            {
                run.cache = null;
                var document = run.document; run.document = null;
                if (document != null) document.ReleaseBenchmarkCopy();
            }
        }

        static void Finish(Run run, string state, string reason, string error = null)
        {
            using var suppression = WhimTexProfiler.SuppressInstrumentation();
            using var renderState = new RenderState(true);
            run.state = state; run.stopReason = reason;
            run.error = error != null && error.Length > 16384 ? error.Substring(0, 16384) + "\n[diagnostic truncated]" : error;
            run.end = Stopwatch.GetTimestamp();
            try { ReleaseRender(run); }
            catch (Exception e) { run.error = (run.error ?? "") + " Cleanup: " + e.Message; run.state = "failed"; }
            finally
            {
                try { if (run.snapshot != null) run.snapshot.ReleaseBenchmarkCopy(); }
                catch (Exception e) { run.error = (run.error ?? "") + " Snapshot cleanup: " + e.Message; run.state = "failed"; }
                run.snapshot = null;
            }
        }

        static void BeforeReload()
        {
            if (current?.Running == true) Finish(current, "interrupted", "assembly_reload");
            var report = current != null ? Report(current) : retained;
            if (report != null) SessionState.SetString(ReloadKey, report.ToString(Newtonsoft.Json.Formatting.None));
        }

        static void OnQuit()
        {
            if (current?.Running == true) Finish(current, "interrupted", "editor_quit");
            SessionState.EraseString(ReloadKey);
        }

        internal static JObject Statistics(IReadOnlyList<double> values)
        {
            int count = values.Count;
            if (count == 0) return null;
            var sorted = new double[count];
            double mean = 0, m2 = 0;
            for (int i = 0; i < count; i++)
            {
                double value = values[i];
                if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(values));
                sorted[i] = value;
                double delta = value - mean;
                mean += delta / (i + 1); m2 += delta * (value - mean);
            }
            Array.Sort(sorted);
            double Percentile(double p)
            {
                double position = (count - 1) * p;
                int lower = (int)position;
                return sorted[lower] + (sorted[Math.Min(lower + 1, count - 1)] - sorted[lower]) * (position - lower);
            }
            return new JObject { ["count"] = count, ["medianMs"] = Percentile(.5), ["minMs"] = sorted[0],
                ["maxMs"] = sorted[count - 1], ["meanMs"] = mean, ["p95Ms"] = Percentile(.95),
                ["stddevMs"] = Math.Sqrt(Math.Max(0, m2 / count)) };
        }

        static JObject Series(List<double> samples) => new JObject {
            ["samplesMs"] = new JArray(samples), ["statistics"] = (JToken)Statistics(samples) ?? JValue.CreateNull() };

        static JObject Report(Run run) => new JObject {
            ["benchmarkId"] = run.id, ["documentId"] = run.documentId, ["state"] = run.state,
            ["phase"] = run.phase, ["running"] = run.Running, ["mode"] = run.mode,
            ["repetitions"] = run.repetitions, ["warmups"] = run.warmups, ["maxSize"] = run.maxSize,
            ["completedSamples"] = run.cold.Count + run.warm.Count,
            ["totalSamples"] = run.repetitions * (run.mode == "both" ? 2 : 1),
            ["completedWarmups"] = run.completedWarmups, ["stopReason"] = run.stopReason, ["error"] = run.error,
            ["elapsedMs"] = ((run.Running ? Stopwatch.GetTimestamp() : run.end) - run.start) * 1000d / Stopwatch.Frequency,
            ["partial"] = !run.Running && run.state != "completed", ["metadata"] = run.metadata?.DeepClone(),
            ["cold"] = Series(run.cold), ["warm"] = Series(run.warm) };

        static JObject Metadata(Run run, string name)
        {
            var doc = run.snapshot;
            float scale = Mathf.Min(1f, (float)run.maxSize / Mathf.Max(doc.width, doc.height));
            return new JObject {
                ["documentName"] = name, ["sourceWidth"] = doc.width, ["sourceHeight"] = doc.height,
                ["width"] = Mathf.Max(1, Mathf.RoundToInt(doc.width * scale)), ["height"] = Mathf.Max(1, Mathf.RoundToInt(doc.height * scale)),
                ["contentHash"] = null, ["contentHashScope"] = "SHA-256 of serialized owned model, Drawing and frozen texture dependencies; fonts/meshes remain asset references.",
                ["unityVersion"] = Application.unityVersion, ["operatingSystem"] = SystemInfo.operatingSystem,
                ["processor"] = SystemInfo.processorType, ["processorCount"] = SystemInfo.processorCount,
                ["graphicsDevice"] = SystemInfo.graphicsDeviceName, ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                ["graphicsVersion"] = SystemInfo.graphicsDeviceVersion, ["graphicsMemoryMb"] = SystemInfo.graphicsMemorySize,
                ["qualityLevel"] = QualitySettings.GetQualityLevel(), ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["quality"] = "settled (interactive=false), GL.sRGBWrite=false during benchmark operations", ["deadlineSeconds"] = DeadlineSeconds,
                ["timing"] = "Main-thread CPU wall elapsed from render entry through synchronous HDR ReadPixels. Includes GPU completion/queue wait, allocation and readback; excludes snapshot/clone setup and cleanup. Not GPU duration or CPU execution time.",
                ["coldDefinition"] = "Fresh owned document, per-layer derived resources and EffectRenderCache for each sample. Shared Unity/material/font caches, temporary RT pool, JIT and driver/shader caches are not reset.",
                ["warmDefinition"] = "One owned document/cache reused after the requested preparatory warmups; warmups excluded from samples and statistics.",
                ["statistics"] = "p95 uses linear interpolation at (n-1)*0.95; stddev is population standard deviation (divide by n).",
                ["caveats"] = new JArray(
                    "Drawing and texture dependencies are frozen at start. Non-readable dependencies are normalized to readable linear RGBAFloat with generated mips; readable sources retain raw pixels/mips. Fonts/meshes and shared Unity resources are not frozen.",
                    "Known Unity time-dependent Shader FX and arbitrary Material FX are rejected. Other shader globals, Editor activity, GC, GPU load and clock throttling are uncontrolled.",
                    "Deadline/cancellation are checked between operations. A synchronous render, snapshot or GPU readback cannot be preempted; the deadline can be exceeded by one operation.",
                    "One render per Editor update. Cold runs precede warm runs in both mode; global caches can become warmer during the cold series.",
                    run.warmups == 0 ? "warmups=0: the warm series includes first-use work." : "Warmup samples are not retained.",
                    "Only the latest run is retained in this Editor session. Reload interrupts active work and retains partial statistics; no archive or files are created.") };
        }

        static string ContentHash(WhimTexDocument snapshot)
        {
            using var container = new WhimTexDocumentContainer { ReusePixelCache = false };
            byte[] model = WhimTexDocumentSerializer.Serialize(snapshot, container);
            container.Set(WhimTexDocumentContainer.DocumentBlock, model);
            // outputSrgb is deliberately omitted by the document serializer, but affects the model contract.
            container.Set("benchmark:output", Encoding.UTF8.GetBytes(snapshot.outputSrgb ? "srgb" : "linear"));
            using var hash = SHA256.Create();
            long bytes = 0;
            var names = new List<string>(container.Names); names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                bytes += container.LengthOf(name);
                if (bytes > MaximumSnapshotBytes) throw new WhimTexDocumentException("Benchmark snapshot exceeds the 128 MiB budget.");
                byte[] data = container.Get(name);
                byte[] label = Encoding.UTF8.GetBytes(name + "\0" + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");
                hash.TransformBlock(label, 0, label.Length, null, 0);
                hash.TransformBlock(data, 0, data.Length, null, 0);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }

        static void CheckOwnedEffects(List<Layer> layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                if (layer.fx != null) foreach (var value in layer.fx)
                {
                    if (value == null) continue;
                    var fx = value;
                    if (fx.LastApplyFailed || fx.HasPendingChanges || !fx.HasAppliedShader)
                        throw new WhimTexDocumentException("Apply or fix Shader FX before benchmarking: " + fx.name);
                    if (fx.UsesUnityTimeInputs) throw new WhimTexDocumentException("Time-dependent Shader FX cannot be benchmarked repeatably: " + fx.name);
                }
                CheckOwnedEffects(layer.children);
            }
        }

        internal readonly struct RenderState : IDisposable
        {
            readonly RenderTexture active;
            readonly bool srgb;
            // A parameterless struct constructor is unavailable on older supported C# versions.
            internal RenderState(bool linear)
            {
                active = RenderTexture.active; srgb = GL.sRGBWrite;
                if (linear) GL.sRGBWrite = false;
            }
            public void Dispose() { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
    }

    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private List<Texture2D> benchmarkTextures;

        internal void ReleaseBenchmarkCopy()
        {
            var textures = benchmarkTextures; benchmarkTextures = null;
            try { Object.DestroyImmediate(this); }
            finally { if (textures != null) foreach (var texture in textures) if (texture != null) Object.DestroyImmediate(texture); }
        }

        // Keep snapshot access to owned private data here, without reflection into Unity.
        internal static WhimTexDocument CreateBenchmarkCopy(WhimTexDocument source)
        {
            int count = 0; long bytes = 0;
            Validate(source.layers, 0);
            var copy = ScriptableObject.CreateInstance<WhimTexDocument>();
            var effects = new Dictionary<UnityObjectID, ShaderFX>();
            bool borrowed = true;
            string stage = "serialize_model";
            try
            {
                string model = JsonUtility.ToJson(source);
                if (model.Length > 8 * 1024 * 1024)
                    throw new WhimTexDocumentException("Benchmark serialized model exceeds 8 Mi characters.");
                stage = "deserialize_model";
                JsonUtility.FromJsonOverwrite(model, copy);
                copy.hideFlags = HideFlags.HideAndDontSave; copy.name = source.name;
                copy.documentBinding = null;
                copy.embeddedShaderFX = new List<ShaderFX>();
                copy.benchmarkTextures = new List<Texture2D>();
                // Remove every borrowed owned-pixel reference before any fallible copy/cleanup.
                stage = "detach_borrowed_pixels";
                DetachPixels(copy.layers);
                borrowed = false;
                stage = "copy_layers_and_pixels";
                CopyLayers(source.layers, copy.layers);
                stage = "normalize_model";
                copy.NormalizeModel();
                stage = "freeze_dependencies";
                FreezeDependencies();
                stage = "reset_undo_tracking";
                copy.ResetUndoTrackingAfterLoad();
                return copy;
            }
            catch (Exception error)
            {
                if (borrowed) { copy.layers = new List<Layer>(); copy.embeddedShaderFX = new List<ShaderFX>(); copy.documentBinding = null; }
                try { copy.ReleaseBenchmarkCopy(); }
                catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
                throw new WhimTexDocumentException("Benchmark snapshot failed during " + stage + ".", error);
            }

            void Validate(List<Layer> layers, int depth)
            {
                if (layers == null) return;
                if (depth > 32) throw new WhimTexDocumentException("Benchmark layer nesting exceeds 32.");
                foreach (var layer in layers)
                {
                    if (++count > 256) throw new WhimTexDocumentException("Benchmark documents are limited to 256 layers.");
                    if (layer?.BehaviourWithoutBinding is DrawingLayerBehaviour drawing) bytes += drawing.BenchmarkPixelBytes;
                    if (bytes > WhimTexBenchmark.MaximumSnapshotBytes) throw new WhimTexDocumentException("Benchmark Drawing pixels exceed 128 MiB.");
                    if (layer != null) Validate(layer.children, depth + 1);
                }
            }
            void DetachPixels(List<Layer> layers)
            {
                if (layers == null) return;
                foreach (var layer in layers)
                {
                    if (layer?.BehaviourWithoutBinding is DrawingLayerBehaviour drawing) drawing.ForgetBenchmarkBorrowedPixels();
                    if (layer != null) DetachPixels(layer.children);
                }
            }
            void CopyLayers(List<Layer> original, List<Layer> cloned)
            {
                if (original == null) return;
                if (cloned == null || cloned.Count != original.Count) throw new WhimTexDocumentException("Benchmark serialization lost layers.");
                for (int i = 0; i < original.Count; i++)
                {
                    var a = original[i]; var b = cloned[i];
                    if (a == null) continue;
                    if (b == null || a.BehaviourWithoutBinding?.GetType() != b.BehaviourWithoutBinding?.GetType())
                        throw new WhimTexDocumentException("Benchmark serialization lost a layer behaviour.");
                    if (a.BehaviourWithoutBinding is DrawingLayerBehaviour pixels)
                        ((DrawingLayerBehaviour)b.Behaviour).CopyBenchmarkPixels(pixels);
                    if (a.fx != null) for (int f = 0; f < a.fx.Count; f++)
                    {
                        if (a.fx[f] == null) continue;
                        var effect = a.fx[f];
                        var key = UnityObjectID.FromObject(effect);
                        if (!effects.TryGetValue(key, out var owned))
                        {
                            owned = effect.CloneForBenchmark(copy);
                            copy.embeddedShaderFX.Add(owned); effects.Add(key, owned);
                        }
                        b.fx[f] = owned;
                    }
                    CopyLayers(a.children, b.children);
                }
            }

            void FreezeDependencies()
            {
                var frozen = new Dictionary<UnityObjectID, Texture2D>();
                var owned = new HashSet<UnityObjectID>();
                var visited = new HashSet<object>();
                var visitedObjects = new HashSet<UnityObjectID>();
                // Drawing pixels are already independent. Resolve File sources using only
                // the copy's original-file cache, never the user's document/window cache.
                Prepare(copy.layers);
                Freeze(copy);
                copy.ReleaseOriginalFileTextureCache();

                void Prepare(List<Layer> layers)
                {
                    if (layers == null) return;
                    foreach (var layer in layers)
                    {
                        if (layer?.BehaviourWithoutBinding is DrawingLayerBehaviour drawing && drawing.StoredTexture != null)
                            owned.Add(UnityObjectID.FromObject(drawing.StoredTexture));
                        if (layer?.BehaviourWithoutBinding is FileLayerBehaviour file && file.sourceTexture != null)
                        {
                            string path = AssetDatabase.GetAssetPath(file.sourceTexture);
                            if (IsOriginalFileDecodeSupported(System.IO.Path.GetExtension(path ?? "").ToLowerInvariant()))
                            {
                                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                                {
                                    importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                                    if ((long)w * h * 16 > WhimTexBenchmark.MaximumSnapshotBytes - bytes)
                                        throw new WhimTexDocumentException("Original File pixels exceed the benchmark snapshot budget.");
                                }
                                if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > WhimTexBenchmark.MaximumSnapshotBytes)
                                    throw new WhimTexDocumentException("Original File input exceeds the benchmark snapshot budget.");
                            }
                            file.sourceTexture = copy.ResolveOriginalFileTexture(file.sourceTexture);
                        }
                        if (layer != null) Prepare(layer.children);
                    }
                }
                object Freeze(object value)
                {
                    if (value == null) return null;
                    // Unity JSON may restore an empty native reference as a managed wrapper.
                    // Test Unity null semantics before accessing any Texture properties.
                    if (value is Object missing && missing == null) return null;
                    if (value is Texture2D texture)
                    {
                        var key = UnityObjectID.FromObject(texture);
                        if (owned.Contains(key)) return texture;
                        if (frozen.TryGetValue(key, out var existing)) return existing;
                        long length = texture.isReadable ? texture.GetRawTextureData<byte>().Length
                            : (long)texture.width * texture.height * 16 * (texture.mipmapCount > 1 ? 2 : 1);
                        bytes += length;
                        if (bytes > WhimTexBenchmark.MaximumSnapshotBytes)
                            throw new WhimTexDocumentException("Benchmark texture dependencies exceed the 128 MiB snapshot budget.");
                        var result = CopyTexture(texture);
                        copy.benchmarkTextures.Add(result); frozen.Add(key, result); owned.Add(UnityObjectID.FromObject(result));
                        return result;
                    }
                    var type = value.GetType();
                    if (type.IsPrimitive || type.IsEnum || value is string) return value;
                    if (!type.IsValueType && (value is Object obj ? !visitedObjects.Add(UnityObjectID.FromObject(obj)) : !visited.Add(value))) return value;
                    if (value is IList list)
                    {
                        for (int i = 0; i < list.Count; i++) list[i] = Freeze(list[i]);
                        return value;
                    }
                    // Reflect only this package's declared serializable model fields through
                    // its existing field enumerator; never inspect Unity's internals.
                    if (type.Assembly != typeof(WhimTexDocument).Assembly) return value;
                    if (value is Object unity && unity != copy && !(unity is ShaderFX fx && fx.EmbeddedOwner == copy)) return value;
                    foreach (var field in WhimTexDocumentJson.Fields(type))
                        try { field.SetValue(value, Freeze(field.GetValue(value))); }
                        catch (Exception error)
                        { throw new WhimTexDocumentException("Cannot freeze benchmark field " + type.Name + "." + field.Name + ".", error); }
                    return value;
                }
            }
        }

        static Texture2D CopyTexture(Texture2D source)
        {
            Texture2D result = null;
            using var state = new WhimTexBenchmark.RenderState(true);
            try
            {
                if (source.isReadable)
                {
                    result = new Texture2D(source.width, source.height, source.format, source.mipmapCount, !source.isDataSRGB);
                    result.LoadRawTextureData(source.GetRawTextureData<byte>());
                    result.Apply(false, false);
                }
                else
                {
                    var surface = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    try
                    {
                        GL.sRGBWrite = false;
                        Graphics.Blit(source, surface);
                        result = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, source.mipmapCount > 1, true);
                        RenderTexture.active = surface;
                        WhimTexProfiler.ReadPixels(result, new Rect(0, 0, source.width, source.height), 0, 0, false);
                        result.Apply(source.mipmapCount > 1, false);
                    }
                    finally { RenderTexture.ReleaseTemporary(surface); }
                }
                result.name = source.name; result.hideFlags = HideFlags.HideAndDontSave;
                result.filterMode = source.filterMode; result.wrapModeU = source.wrapModeU; result.wrapModeV = source.wrapModeV;
                result.wrapModeW = source.wrapModeW; result.anisoLevel = source.anisoLevel; result.mipMapBias = source.mipMapBias;
                return result;
            }
            catch { if (result != null) Object.DestroyImmediate(result); throw; }
        }
    }

    public sealed partial class DrawingLayerBehaviour
    {
        internal long BenchmarkPixelBytes => paintSurface != null && (paintSurfaceDirty || pixels == null)
            ? (long)paintSurface.width * paintSurface.height * 8
            : deferredTexture.HasValue && pixels == null ? (long)deferredTexture.Value.width * deferredTexture.Value.height * 16
            : pixels == null ? 0 : (long)pixels.width * pixels.height * 16;

        internal void ForgetBenchmarkBorrowedPixels() { pixels = null; deferredTexture = null; }

        internal void CopyBenchmarkPixels(DrawingLayerBehaviour source)
        {
            // Read pending pixels without changing the source's dirty flag, pixel object or caches.
            if (source.paintSurface != null && (source.paintSurfaceDirty || source.pixels == null))
            {
                var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
                var straight = RenderTexture.GetTemporary(source.paintSurface.width, source.paintSurface.height, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                try
                {
                    GL.sRGBWrite = false;
                    source.BlitStraightSurface(straight, source.colorRange == LayerColorRange.HDR ? 3f : 2f);
                    pixels = new Texture2D(straight.width, straight.height,
                        source.colorRange == LayerColorRange.HDR ? TextureFormat.RGBAHalf : TextureFormat.RGBA32,
                        false, source.colorRange == LayerColorRange.HDR) { hideFlags = HideFlags.HideAndDontSave };
                    RenderTexture.active = straight;
                    WhimTexProfiler.ReadPixels(pixels, new Rect(0, 0, straight.width, straight.height), 0, 0, false);
                    pixels.Apply(false, false);
                }
                finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(straight); }
            }
            else if (source.deferredTexture.HasValue && source.pixels == null)
                pixels = WhimTexDocumentSerializer.MaterializeDeferredTexture(source.deferredTexture.Value);
            else if (source.pixels != null) pixels = Object.Instantiate(source.pixels);
            if (pixels != null) pixels.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    public sealed partial class ShaderFX
    {
        internal ShaderFX CloneForBenchmark(WhimTexDocument owner)
        {
            var copy = CloneForClipboard();
            copy.RestoreDocumentOwner(owner);
            copy.SuspendDocumentCatalogReload();
            // Stable serialization/hash identity; all runtime shader/material objects are still owned copies.
            copy.shaderKey = shaderKey;
            return copy;
        }
    }
}
