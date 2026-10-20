using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using Unity.Profiling;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Diagnostic only. Freeze the previous implementation of the EMPTY input path,
// rather than editing/recompiling production between the A/B samples.
// Source: a785b954dab1e01a93f4dff6b8edc6f042c26fb3, src/ShaderFX.TextureLayers.cs.
// Nonempty-only dependencies below deliberately throw; no nonempty input is timed.
public static class ShaderFXEmptyInputTiming
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    const int Pairs = 32, Batch = 2048;
    [Serializable] sealed class Report
    {
        public string id, unity, sourceCommit;
        public int pairs, batch;
        public bool activeTarget;
        public int[] parameterCounts;
        public double[] oldReflectionUs, currentReflectionUs, oldDirectUs, currentDirectUs;
        public double[] oldReflectionAllocations, currentReflectionAllocations, oldDirectAllocations, currentDirectAllocations;
    }
    delegate IDisposable CurrentBinder<T>(ShaderFX fx, Layer consumer, in T context, RenderTexture self);
    static Action Direct<T>(WhimTexDocument doc, ShaderFX fx, Layer consumer, object boxed, RenderTexture self)
    {
        var method = typeof(WhimTexDocument).GetMethod("BindShaderTextureLayers", F);
        var bind = (CurrentBinder<T>)method.CreateDelegate(typeof(CurrentBinder<T>), doc);
        T context = (T)boxed;
        return () => { var result = bind(fx, consumer, in context, self); result?.Dispose(); };
    }
    public static string Run(string id)
    {
        string path = Path.GetFullPath("Temp/WhimTex/fx-preparation/empty-input-" + id + ".json");
        if (!Guid.TryParse(id, out _)) return TestContext.Result("failed", 0, "Expected runner GUID").ToJson();
        return TestContext.Run("Paired empty FX input timing; " + path, t =>
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            var fx = ScriptableObject.CreateInstance<ShaderFX>();
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var self = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var profiler = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
            var suppression = (IDisposable)profiler.GetMethod("SuppressInstrumentation", F).Invoke(null, null);
            try
            {
                doc.hideFlags = fx.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = 32;
                self.hideFlags = HideFlags.HideAndDontSave; self.Create(); RenderTexture.active = self;
                Layer consumer = new ColorFillLayerBehaviour(); doc.layers.Add(consumer); consumer.fx.Add(fx);
                var counts = new[] { 0, 8, 64 }; int length = counts.Length * Pairs;
                var report = new Report { id = id, unity = Application.unityVersion,
                    sourceCommit = "a785b954dab1e01a93f4dff6b8edc6f042c26fb3", pairs = Pairs, batch = Batch, parameterCounts = counts, activeTarget = true,
                    oldReflectionUs = new double[length], currentReflectionUs = new double[length],
                    oldDirectUs = new double[length], currentDirectUs = new double[length],
                    oldReflectionAllocations = new double[counts.Length], currentReflectionAllocations = new double[counts.Length],
                    oldDirectAllocations = new double[counts.Length], currentDirectAllocations = new double[counts.Length] };
                var type = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
                object context = Activator.CreateInstance(type, F, null, new object[] { doc, self, 32, 32, 1f, true, true, null }, null);
                var method = typeof(WhimTexDocument).GetMethod("BindShaderTextureLayers", F);
                object[] args = { fx, consumer, context, self };
                var frozenDoc = new FrozenDocument();
                var frozenMethod = typeof(FrozenDocument).GetMethod("Bind", F);
                var oldContext = new FrozenContext(doc, self, 32, 32);
                for (int c = 0; c < counts.Length; c++)
                {
                    var drafts = new List<ShaderFXParameter>(); var applied = new List<ShaderFXParameter>();
                    for (int i = 0; i < counts[c]; i++)
                    {
                        var value = new ShaderFXParameter { name = "_P" + i, type = ShaderFXParameterType.Float };
                        drafts.Add(value); applied.Add(new ShaderFXParameter { name = value.name, type = value.type, id = value.id });
                    }
                    typeof(ShaderFX).GetField("parameters", F).SetValue(fx, drafts);
                    typeof(ShaderFX).GetField("appliedParameters", F).SetValue(fx, applied);
                    var frozenFx = new FrozenEffect { parameters = drafts, appliedParameters = applied };
                    object[] oldArgs = { frozenFx, consumer, oldContext, self };
                    Action oldReflect = () => { var result = (IDisposable)frozenMethod.Invoke(frozenDoc, oldArgs); result?.Dispose(); };
                    Action currentReflect = () => { var result = (IDisposable)method.Invoke(doc, args); result?.Dispose(); };
                    Action oldDirect = () => { var result = frozenDoc.Bind(frozenFx, consumer, in oldContext, self); result?.Dispose(); };
                    var currentDirect = (Action)typeof(ShaderFXEmptyInputTiming).GetMethod("Direct", F).MakeGenericMethod(type)
                        .Invoke(null, new object[] { doc, fx, consumer, context, self });
                    t.True(method.Invoke(doc, args) == null && frozenMethod.Invoke(frozenDoc, oldArgs) == null, "Both input paths are empty: " + counts[c]);
                    for (int i = 0; i < 2048; i++) { oldDirect(); currentDirect(); oldReflect(); currentReflect(); }
                    for (int r = 0; r < Pairs; r++)
                    {
                        int slot = c * Pairs + r;
                        // Alternate both implementation order and direct/reflection order.
                        if ((r & 1) == 0)
                        {
                            report.oldReflectionUs[slot] = Measure(oldReflect); report.currentReflectionUs[slot] = Measure(currentReflect);
                            report.currentDirectUs[slot] = Measure(currentDirect); report.oldDirectUs[slot] = Measure(oldDirect);
                        }
                        else
                        {
                            report.oldDirectUs[slot] = Measure(oldDirect); report.currentDirectUs[slot] = Measure(currentDirect);
                            report.currentReflectionUs[slot] = Measure(currentReflect); report.oldReflectionUs[slot] = Measure(oldReflect);
                        }
                    }
                    report.oldReflectionAllocations[c] = Allocations(oldReflect);
                    report.currentReflectionAllocations[c] = Allocations(currentReflect);
                    report.oldDirectAllocations[c] = Allocations(oldDirect);
                    report.currentDirectAllocations[c] = Allocations(currentDirect);
                    t.Equal(self, RenderTexture.active, "Caller RT preserved"); t.Equal(srgb, GL.sRGBWrite, "Caller write state preserved");
                    t.Equal(counts[c], drafts.Count, "Timing does not mutate declarations");
                }
                if (File.Exists(path)) throw new IOException("Refusing to replace measurement: " + path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
                var saved = JsonUtility.FromJson<Report>(File.ReadAllText(path));
                t.Equal(length, saved.currentDirectUs.Length, "Primitive timing arrays were retained");
                t.Equal(length, saved.oldReflectionUs.Length, "Baseline timing arrays were retained");
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                Object.DestroyImmediate(fx); Object.DestroyImmediate(doc); self.Release(); Object.DestroyImmediate(self); suppression.Dispose();
            }
        });
    }
    static double Measure(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Batch; i++) action();
        return (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency / Batch;
    }
    static double Allocations(Action action)
    {
        using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 8192, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        recorder.Reset(); recorder.Start();
        for (int i = 0; i < 512; i++) action();
        return recorder.Valid && recorder.Count < 8192 ? recorder.Count / 512.0 : -1;
    }

    // Field layout matches LayerRenderContext; it is internal to the production assembly.
    readonly struct FrozenContext
    {
        public readonly WhimTexDocument activeDocument;
        public readonly RenderTexture input;
        public readonly int width, height;
        public readonly float scaleMultiplier, pixelScale;
        public readonly bool applyTransform, applyFx, transformFxCoordinates;
        public FrozenContext(WhimTexDocument doc, RenderTexture self, int w, int h)
        { activeDocument = doc; input = self; width = w; height = h; scaleMultiplier = pixelScale = 1; applyTransform = applyFx = transformFxCoordinates = true; }
    }
    sealed class FrozenEffect
    {
        internal List<ShaderFXParameter> parameters, appliedParameters;
        internal IEnumerable<ShaderFXParameter> TextureParameters()
        {
            foreach (var applied in appliedParameters)
            {
                if (applied == null || applied.type != ShaderFXParameterType.Texture2D) continue;
                var value = applied;
                foreach (var draft in parameters)
                    if (draft != null && draft.name == applied.name && draft.type == applied.type) { value = draft; break; }
                yield return value;
            }
        }
    }
    sealed class FrozenDocument
    {
        internal FrozenBindings Bind(FrozenEffect effect, Layer consumer, in FrozenContext context, RenderTexture self)
        {
            FrozenBindings bindings = null;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                foreach (var parameter in effect.TextureParameters())
                {
                    if (parameter.textureSource == ShaderFXTextureSource.Texture) continue;
                    bindings ??= new FrozenBindings();
                    if (parameter.textureSource == ShaderFXTextureSource.Self && self != null)
                    {
                        bindings.inputs.Add((parameter.name, self));
                        continue;
                    }
                    string key = parameter.textureSource == ShaderFXTextureSource.Layer && IsUsableShaderTexture(consumer, parameter.textureLayerId) ? parameter.textureLayerId : "";
                    if (!bindings.sources.TryGetValue(key, out var pixels))
                    {
                        var source = key.Length == 0 ? null : FindLayer(key);
                        int w = context.width, h = context.height;
                        float scale = context.scaleMultiplier;
                        if (source != null && TryFindLayer(source, out var list, out int index))
                        {
                            pixels = CachedEffectRender(source, "fx-input", w, h, scale, false, () =>
                            {
                                var stack = new HashSet<Layer> { consumer };
                                return IsGroup(source)
                                    ? RenderGroupEffectInput(source, w, h, scale, stack, true, includeDisabled: true)
                                    : RenderStandalone(list, index, w, h, scale, stack, includeDisabled: true);
                            });
                        }
                        pixels ??= GetClearRenderTexture(w, h);
                        bindings.sources.Add(key, pixels);
                    }
                    bindings.inputs.Add((parameter.name, pixels));
                }
                return bindings;
            }
            catch { bindings?.Dispose(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        static InvalidOperationException Nonempty() => new InvalidOperationException("Only empty inputs are allowed in this frozen diagnostic.");
        bool IsUsableShaderTexture(Layer consumer, string id) => throw Nonempty();
        Layer FindLayer(string id) => throw Nonempty();
        bool IsGroup(Layer source) => throw Nonempty();
        bool TryFindLayer(Layer source, out List<Layer> list, out int index) => throw Nonempty();
        RenderTexture CachedEffectRender(Layer source, string stage, int w, int h, float scale, bool flag, Func<RenderTexture> render) => throw Nonempty();
        RenderTexture RenderGroupEffectInput(Layer source, int w, int h, float scale, HashSet<Layer> stack, bool flag, bool includeDisabled) => throw Nonempty();
        RenderTexture RenderStandalone(List<Layer> list, int index, int w, int h, float scale, HashSet<Layer> stack, bool includeDisabled) => throw Nonempty();
        RenderTexture GetClearRenderTexture(int w, int h) => throw Nonempty();
    }
    sealed class FrozenBindings : IDisposable
    {
        internal readonly Dictionary<string, RenderTexture> sources = new Dictionary<string, RenderTexture>();
        internal readonly List<(string name, RenderTexture texture)> inputs = new List<(string, RenderTexture)>();
        public void Dispose() => throw new InvalidOperationException("No bindings should be allocated for an empty input.");
    }
}
