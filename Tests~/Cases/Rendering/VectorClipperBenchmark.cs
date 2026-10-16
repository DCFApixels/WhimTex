using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

public static class VectorClipperBenchmark
{
    const string Folder = "Temp/WhimTex/vector-clipper-v1";
    const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Assembly Product = typeof(VectorLayerBehaviour).Assembly;
    static readonly MethodInfo Render = typeof(WhimTexDocument).GetMethod("RenderCanvas", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly Type Timing = Product.GetType("DCFApixels.WhimTex.VectorGeometryTiming", true);
    static readonly string[] Phases = { "vector.clip.resolve", "vector.clip.budget", "vector.clip.input", "vector.clip.execute", "vector.boolean.filled_region" };
    [Serializable] public sealed class Part { public string phase; public double medianMs; public long calls; }
    [Serializable] public sealed class Report
    {
        public string name, phase;
        public int coldSamples = 9, timingSamples = 3;
        public double submitMedianMs, completedMedianMs, maxPixelDelta;
        public Part[] parts;
    }
    [Serializable] public sealed class Result { public string status, message; public int checks; public string[] failures; }
    public static string Before() => Check("before");
    public static string BeforeTimings() => Check("before-timings");
    public static string After() => Check("after");
    public static string AfterFinal() => Check("after-final");
    public static string AfterBurst() => Check("after-burst");
    public static string UnionBefore() => Check("union-before");
    public static string UnionAfter() => Check("union-after");
    public static string PreparedBefore() => Check("prepared-before");
    public static string PreparedAfter() => Check("prepared-after");
    static string Check(string phase)
    {
        int done = 0;
        try
        {
            if (!Unity.Burst.BurstCompiler.IsEnabled) throw new InvalidOperationException("Burst must be enabled");
            for (; done < 6; done++) Run(phase, done);
            return JsonUtility.ToJson(new Result { status = "passed", checks = done, message = Folder + "/" + phase, failures = Array.Empty<string>() });
        }
        catch (Exception e) { return JsonUtility.ToJson(new Result { status = "failed", checks = done, failures = new[] { e.ToString() } }); }
    }
    public static string Run(string phase, int scenario)
    {
        if (phase != "before" && phase != "before-timings" && phase != "after" && phase != "after-final" && phase != "after-burst" && phase != "union-before" && phase != "union-after" && phase != "prepared-before" && phase != "prepared-after") throw new ArgumentException("phase");
        if (scenario < 0 || scenario > 5) throw new ArgumentOutOfRangeException(nameof(scenario));
        Directory.CreateDirectory(Folder);
        string reportFile = Folder + "/" + phase + "-" + scenario + ".json";
        string pixelsFile = Folder + "/before-" + scenario + ".rgba";
        if (File.Exists(reportFile) || phase == "before" && File.Exists(pixelsFile)) throw new InvalidOperationException("Preserve immutable measurements");
        if (phase != "before" && !File.Exists(pixelsFile)) throw new InvalidOperationException("Pre-change reference required");
        int size = scenario < 3 ? 512 : 1024, kind = scenario % 3;
        var report = new Report { phase = phase, name = (kind == 0 ? "vector" : kind == 1 ? "boolean-4-stroke" : "boolean-16-fill") + "-" + size };
        var submit = new List<double>(); var complete = new List<double>();
        var phaseTimes = new List<double>[Phases.Length]; var counts = new long[Phases.Length];
        for (int i = 0; i < phaseTimes.Length; i++) phaseTimes[i] = new List<double>();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        using var suppression = (IDisposable)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler", true)
            .GetMethod("SuppressInstrumentation", Hidden).Invoke(null, null);
        try
        {
            // Whole-render samples have the timing sink disabled. Three separate fresh renders
            // collect the decomposition, so diagnostic dictionary/clock costs do not skew totals.
            for (int sample = -2; sample < 12; sample++)
            {
                var doc = VectorColdBenchmark.Create(size, kind);
                var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
                RenderTexture output = null; object timing = null;
                try
                {
                    if (sample >= 9) timing = Timing.GetMethod("Begin", Hidden).Invoke(null, null);
                    var watch = Stopwatch.StartNew();
                    output = (RenderTexture)Render.Invoke(doc, new object[] { size });
                    double submitted = watch.Elapsed.TotalMilliseconds;
                    RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    double completed = watch.Elapsed.TotalMilliseconds;
                    if (sample >= 0 && sample < 9) { submit.Add(submitted); complete.Add(completed); }
                    if (timing != null)
                    {
                        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
                        var ticks = (Dictionary<string, long>)timing.GetType().GetField("ticks", fields).GetValue(timing);
                        var calls = (Dictionary<string, long>)timing.GetType().GetField("calls", fields).GetValue(timing);
                        for (int i = 0; i < Phases.Length; i++)
                        {
                            ticks.TryGetValue(Phases[i], out long value); calls.TryGetValue(Phases[i], out long count);
                            phaseTimes[i].Add(value * 1000d / Stopwatch.Frequency);
                            if (sample > 9 && count != counts[i]) throw new Exception("Non-deterministic resolver call count");
                            counts[i] = count;
                        }
                    }
                    if (sample == 8)
                    {
                        var data = read.GetRawTextureData<float>();
                        if (phase == "before")
                        {
                            using var writer = new BinaryWriter(new FileStream(pixelsFile, FileMode.CreateNew));
                            for (int p = 0; p < data.Length; p++) writer.Write(data[p]);
                        }
                        else
                        {
                            using var reader = new BinaryReader(File.OpenRead(pixelsFile));
                            if (reader.BaseStream.Length != data.Length * 4L) throw new Exception("Pixel dimensions differ");
                            for (int p = 0; p < data.Length; p++)
                            {
                                double delta = Math.Abs(data[p] - reader.ReadSingle());
                                if (!double.IsFinite(delta)) throw new Exception("Nonfinite pixels");
                                report.maxPixelDelta = Math.Max(report.maxPixelDelta, delta);
                            }
                        }
                        File.WriteAllBytes(Folder + "/" + phase + "-" + scenario + ".png", read.EncodeToPNG());
                    }
                }
                finally
                {
                    (timing as IDisposable)?.Dispose(); RenderTexture.active = previous;
                    if (output != null) RenderTexture.ReleaseTemporary(output);
                    Object.DestroyImmediate(read); Object.DestroyImmediate(doc);
                }
            }
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        submit.Sort(); complete.Sort(); report.submitMedianMs = submit[4]; report.completedMedianMs = complete[4];
        report.parts = new Part[Phases.Length];
        for (int i = 0; i < Phases.Length; i++)
        {
            phaseTimes[i].Sort(); report.parts[i] = new Part { phase = Phases[i], medianMs = phaseTimes[i][1], calls = counts[i] };
        }
        // The Editor also loads an embedded serializer copy; bind the public package API explicitly.
        string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
            .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
        File.WriteAllText(reportFile, json);
        if (report.maxPixelDelta != 0) throw new Exception("Pixel parity failed: " + json);
        return json;
    }
}
