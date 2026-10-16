using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Diagnostic only: counterbalanced cold renders, no product toggle or user-document mutation.
// The fresh-engine control uses the existing reentrant-isolation branch in our own wrapper.
// It is not a historical binary: current validation and final Clear remain in both modes.
public static class VectorClipperPairedBenchmark
{
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags InstanceHidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(VectorLayerBehaviour).Assembly;
    static readonly Type Geometry = Product.GetType("DCFApixels.WhimTex.StrokeGeometry", true);
    static readonly Type Timing = Product.GetType("DCFApixels.WhimTex.VectorGeometryTiming", true);
    static readonly FieldInfo Current = Geometry.GetField("currentBuild", Hidden);
    static readonly MethodInfo Begin = Geometry.GetMethod("BeginBuild", Hidden);
    static readonly MethodInfo Render = typeof(WhimTexDocument).GetMethod("RenderCanvas", InstanceHidden);
    static readonly string[] Phases = { "vector.clip.resolve", "vector.clip.budget", "vector.clip.input", "vector.clip.execute" };

    [Serializable] public sealed class Sample
    {
        public int size, pair, order, gc0, gc1, gc2;
        public bool reuse, instrumented;
        public double submitMs, completedMs, readbackMs, maxPixelDelta;
        public double[] phaseMs;
        public long[] phaseCalls;
    }
    [Serializable] public sealed class Report
    {
        public string utc, unityVersion, methodology;
        public int block;
        public List<Sample> samples = new List<Sample>();
    }
    [Serializable] public sealed class Result
    {
        public string status, message;
        public int checks;
        public string[] failures;
    }
    public static string Block1() => Run(1);
    public static string Block2() => Run(2);
    public static string Block3() => Run(3);
    public static string Block4() => Run(4);

    static string Run(int block)
    {
        int checks = 0;
        string folder = "Temp/WhimTex/diagnostics/vector-clipper-paired-" + Guid.NewGuid().ToString("N");
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            if (!Unity.Burst.BurstCompiler.IsEnabled) throw new Exception("Burst must be enabled");
            if (Current.GetValue(null) != null) throw new Exception("An existing geometry scope must not be replaced");
            using var suppression = (IDisposable)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler", true)
                .GetMethod("SuppressInstrumentation", Hidden).Invoke(null, null);
            var report = new Report { block = block, utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                methodology = "Fresh Vector document for every render; two warmups per mode/size excluded. Alternating AB/BA pairs; current scoped reuse versus isolated fresh Clipper per operation. CPU submission and synchronous GPU readback separate. GC collection deltas include all Editor threads. No production edits, no forced GC, no engine retained across renders. Phase samples separate from whole-render totals." };
            // Alternate resolution order between blocks as well as mode order within each pair.
            foreach (int size in block % 2 == 1 ? new[] { 1024, 512 } : new[] { 512, 1024 })
            {
                int scenario = size == 512 ? 0 : 3;
                byte[] bytes = File.ReadAllBytes("Temp/WhimTex/vector-clipper-v1/before-" + scenario + ".rgba");
                if (bytes.Length != size * size * 16) throw new Exception("Frozen reference dimensions differ");
                var reference = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, reference, 0, bytes.Length);
                int pairs = size == 1024 ? 16 : 8;
                for (int pair = -2; pair < pairs + 3; pair++)
                {
                    bool instrumented = pair >= pairs;
                    for (int order = 0; order < 2; order++)
                    {
                        bool reuse = ((pair + block + order) & 1) == 0;
                        bool verify = pair == 0 || pair == pairs - 1;
                        Sample sample = Measure(size, pair, order, reuse, instrumented, verify ? reference : null);
                        if (pair >= 0) report.samples.Add(sample);
                        checks++;
                    }
                }
            }
            if (Current.GetValue(null) != null) throw new Exception("Geometry scope leaked");
            if (report.samples.Count != 60) throw new Exception("Incomplete paired sample set");
            // Ephemeral test assemblies are not fully covered by Unity's native serialization
            // metadata. Use the public managed serializer for the nested sample collection.
            string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
                .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            if (!json.Contains("\"samples\"")) throw new Exception("Sample collection was not serialized");
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/samples.json", json);
            return JsonUtility.ToJson(new Result { status = "passed", checks = checks, message = folder + "/samples.json", failures = Array.Empty<string>() });
        }
        catch (Exception e)
        {
            return JsonUtility.ToJson(new Result { status = "failed", checks = checks, message = folder, failures = new[] { e.ToString() } });
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    static Sample Measure(int size, int pair, int order, bool reuse, bool instrumented, float[] reference)
    {
        var sample = new Sample { size = size, pair = pair, order = order, reuse = reuse, instrumented = instrumented };
        WhimTexDocument doc = null;
        Texture2D read = null;
        RenderTexture output = null;
        IDisposable isolation = null, timing = null;
        FieldInfo busy = null;
        var previous = RenderTexture.active;
        try
        {
            doc = VectorColdBenchmark.Create(size, 0);
            read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            if (!reuse)
            {
                // Reflection only into WhimTex, never Unity internals. The existing busy guard
                // forces a fresh engine for every Resolve; synchronous execution cannot expose
                // this temporary state to another Editor callback. Always restored below.
                isolation = (IDisposable)Begin.Invoke(null, null);
                busy = isolation.GetType().GetField("inUse", InstanceHidden);
                busy.SetValue(isolation, true);
            }
            if (instrumented) timing = (IDisposable)Timing.GetMethod("Begin", Hidden).Invoke(null, null);
            var args = new object[] { size };
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var watch = Stopwatch.StartNew();
            output = (RenderTexture)Render.Invoke(doc, args);
            sample.submitMs = watch.Elapsed.TotalMilliseconds;
            RenderTexture.active = output;
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            sample.completedMs = watch.Elapsed.TotalMilliseconds;
            watch.Stop();
            sample.readbackMs = sample.completedMs - sample.submitMs;
            sample.gc0 = GC.CollectionCount(0) - g0;
            sample.gc1 = GC.CollectionCount(1) - g1;
            sample.gc2 = GC.CollectionCount(2) - g2;
            if (isolation != null && isolation.GetType().GetField("engine", InstanceHidden).GetValue(isolation) != null)
                throw new Exception("Fresh control unexpectedly retained an engine");
            if (reuse && Current.GetValue(null) != null) throw new Exception("Normal render did not release its engine");
            if (timing != null)
            {
                var ticks = (Dictionary<string, long>)timing.GetType().GetField("ticks", InstanceHidden).GetValue(timing);
                var calls = (Dictionary<string, long>)timing.GetType().GetField("calls", InstanceHidden).GetValue(timing);
                sample.phaseMs = new double[Phases.Length]; sample.phaseCalls = new long[Phases.Length];
                for (int i = 0; i < Phases.Length; i++)
                {
                    ticks.TryGetValue(Phases[i], out long value); calls.TryGetValue(Phases[i], out long count);
                    if (count != 4) throw new Exception("Expected four resolver operations: " + Phases[i] + " got " + count);
                    sample.phaseMs[i] = value * 1000d / Stopwatch.Frequency; sample.phaseCalls[i] = count;
                }
            }
            if (reference != null)
            {
                var pixels = read.GetRawTextureData<float>();
                if (pixels.Length != reference.Length) throw new Exception("Reference pixel dimensions differ");
                for (int i = 0; i < pixels.Length; i++)
                {
                    double delta = Math.Abs(pixels[i] - reference[i]);
                    if (!double.IsFinite(delta)) throw new Exception("Nonfinite output");
                    sample.maxPixelDelta = Math.Max(sample.maxPixelDelta, delta);
                }
                if (sample.maxPixelDelta != 0) throw new Exception("Frozen pixel parity failed: " + sample.maxPixelDelta);
            }
            return sample;
        }
        finally
        {
            try { timing?.Dispose(); }
            finally
            {
                try
                {
                    if (isolation != null) { busy.SetValue(isolation, false); isolation.Dispose(); }
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (output != null) RenderTexture.ReleaseTemporary(output);
                    if (read != null) Object.DestroyImmediate(read);
                    if (doc != null) Object.DestroyImmediate(doc);
                }
            }
        }
    }
}
