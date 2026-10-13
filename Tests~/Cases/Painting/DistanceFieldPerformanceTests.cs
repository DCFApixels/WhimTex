using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text;
using UnityEngine;
using Unity.Burst;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Diagnostic only: real production Render calls, no timing acceptance threshold.
public static class DistanceFieldPerformanceTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] public sealed class Phase { public string name; public double ms; }
    [Serializable] public sealed class Sample
    {
        public string mode;
        public double renderMs, completionReadbackMs, endToEndMs;
        public Phase[] phases;
    }
    [Serializable] public sealed class Report
    {
        public string utc, unity, cpu, gpu, fixture, kind;
        public int processors, size, warmups, repetitions;
        public bool burstEnabled, cacheBypassed;
        public string notes;
    }

    public static string Run(string fixture, int size, string kind, string mode, int repetitions)
    {
        if ((fixture != "star" && fixture != "rings") || (kind != "sdf" && kind != "outline")
            || size < 128 || size > 2048 || repetitions < 1 || repetitions > 7)
            return TestContext.Result("failed", 0, "Invalid bounded benchmark arguments", "Arguments out of range").ToJson();
        var previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        Texture2D source = null, read = null;
        WhimTexDocument doc = null;
        RenderTexture input = null, rendered = null;
        LayerBehaviour behaviour = null;
        var timingType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.DistanceFieldTiming", true);
        var receiver = timingType.GetField("receiver", BindingFlags.Static | BindingFlags.NonPublic);
        object oldReceiver = receiver.GetValue(null);
        var samples = new List<Sample>();
        var phases = new Dictionary<string, double>();
        bool recording = false;
        string artifact = null;
        try
        {
            doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            doc.hideFlags = HideFlags.HideAndDontSave;
            doc.width = doc.height = size;
            source = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true)
                { hideFlags = HideFlags.HideAndDontSave };
            source.SetPixels32(Input(fixture, size)); source.Apply(false, false);
            input = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            GL.sRGBWrite = false; Graphics.Blit(source, input);
            // Flush fixture upload before any measured production call.
            RenderTexture.active = input; read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            var contextType = typeof(SDFLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            object context = Activator.CreateInstance(contextType, doc, input, size, size, 1f, false, false, null);
            receiver.SetValue(null, (Action<string, double>)((name, ms) =>
            {
                if (!recording) return;
                phases.TryGetValue(name, out double accumulated);
                phases[name] = accumulated + ms;
            }));
            var modes = mode == "all" ? (DistanceMetric[])Enum.GetValues(typeof(DistanceMetric))
                : new[] { (DistanceMetric)Enum.Parse(typeof(DistanceMetric), mode) };
            foreach (var metric in modes)
            {
                behaviour = kind == "sdf"
                    ? (LayerBehaviour)new SDFLayerBehaviour { metric = metric, threshold = 128,
                        encoding = SDFLayerBehaviour.OutputEncoding.Gradient, maxDistanceNormalization = 32 }
                    : new OutlineLayerBehaviour { metric = metric, outlineWidth = 8, outlineSoftness = 1, fillCenter = true };
                doc.layers.Add(behaviour.Owner);
                var render = behaviour.GetType().GetMethod("Render", Hidden);
                var arguments = new[] { context };
                for (int iteration = -2; iteration < repetitions; iteration++)
                {
                    phases.Clear(); recording = iteration >= 0;
                    RenderTexture.active = input; GL.sRGBWrite = false;
                    var total = Stopwatch.StartNew();
                    rendered = (RenderTexture)render.Invoke(behaviour, arguments);
                    double renderMs = total.Elapsed.TotalMilliseconds;
                    if (rendered == null || RenderTexture.active != input || GL.sRGBWrite)
                        throw new InvalidOperationException("Render did not preserve caller state.");
                    RenderTexture.active = rendered;
                    var completion = Stopwatch.StartNew();
                    read.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    double completionMs = completion.Elapsed.TotalMilliseconds;
                    double endToEndMs = total.Elapsed.TotalMilliseconds;
                    if (recording)
                    {
                        var phaseArray = new List<Phase>();
                        foreach (var pair in phases) phaseArray.Add(new Phase { name = pair.Key, ms = pair.Value });
                        samples.Add(new Sample { mode = metric.ToString(), renderMs = renderMs,
                            completionReadbackMs = completionMs, endToEndMs = endToEndMs, phases = phaseArray.ToArray() });
                    }
                    // Numerical/resource sanity is outside the timed interval, not an image golden.
                    var pixels = read.GetRawTextureData<Color>();
                    for (int i = 0; i < pixels.Length; i++)
                        if (!float.IsFinite(pixels[i].r) || !float.IsFinite(pixels[i].a))
                            throw new InvalidOperationException("Non-finite benchmark output.");
                    RenderTexture.active = input; RenderTexture.ReleaseTemporary(rendered); rendered = null;
                }
                behaviour.GetType().GetMethod("ReleaseTransientResources", Hidden).Invoke(behaviour, null);
                doc.layers.Clear(); behaviour = null;
            }
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                cpu = SystemInfo.processorType, processors = SystemInfo.processorCount, gpu = SystemInfo.graphicsDeviceName,
                fixture = fixture, kind = kind, size = size, warmups = 2, repetitions = repetitions,
                burstEnabled = BurstCompiler.IsEnabled, cacheBypassed = true,
                notes = "Wall time in graphical Editor. Production Render bypasses outer document cache. Phase timers include allocation/job waits where scoped. Nested phase durations overlap; do not sum them. output.submit is CPU submission, not GPU duration. completionReadbackMs includes GPU completion AND RGBAFloat CPU readback, not GPU-only time. Fixture generation, result validation and final RT release are untimed. Two warmups do not guarantee asynchronous Burst compilation has finished; retain raw variance." };
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/distance-fields/performance"));
            Directory.CreateDirectory(directory);
            artifact = Path.Combine(directory, fixture + "-" + size + "-" + kind + "-" + mode + "-" + Guid.NewGuid().ToString("N") + ".json");
            // The Editor serializer does not reliably retain nested arrays from an
            // ephemeral diagnostic assembly. Serialize and verify the complete raw data.
            if (samples.Count != modes.Length * repetitions)
                throw new InvalidOperationException("Benchmark sample retention failed.");
            File.WriteAllText(artifact, Serialize(report, samples));
        }
        catch (Exception error)
        {
            return TestContext.Result("failed", 0, "Distance field benchmark failed", error.ToString()).ToJson();
        }
        finally
        {
            receiver.SetValue(null, oldReceiver);
            RenderTexture.active = previous; GL.sRGBWrite = previousSrgb;
            if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
            if (input != null) RenderTexture.ReleaseTemporary(input);
            if (behaviour != null) behaviour.GetType().GetMethod("ReleaseTransientResources", Hidden).Invoke(behaviour, null);
            if (doc != null) UnityEngine.Object.DestroyImmediate(doc);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (read != null) UnityEngine.Object.DestroyImmediate(read);
        }
        return TestContext.Result("skipped", samples.Count,
            "Diagnostic samples retained: " + artifact + "; no performance acceptance threshold or image-equivalence verdict.").ToJson();
    }

    static string Serialize(Report metadata, List<Sample> samples)
    {
        // Primitive metadata uses the supported Editor serializer; explicit arrays
        // avoid its ephemeral-type limitation without another assembly dependency.
        var json = new StringBuilder(JsonUtility.ToJson(metadata).TrimEnd('}'));
        json.Append(",\"samples\":[");
        string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new InvalidOperationException("Invalid timing sample.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        for (int i = 0; i < samples.Count; i++)
        {
            if (i > 0) json.Append(',');
            var s = samples[i];
            json.Append("{\"mode\":\"").Append(s.mode).Append("\",\"renderMs\":").Append(Number(s.renderMs))
                .Append(",\"completionReadbackMs\":").Append(Number(s.completionReadbackMs))
                .Append(",\"endToEndMs\":").Append(Number(s.endToEndMs)).Append(",\"phases\":[");
            for (int j = 0; j < s.phases.Length; j++)
            {
                if (j > 0) json.Append(',');
                json.Append(JsonUtility.ToJson(s.phases[j]));
            }
            json.Append("]}");
        }
        return json.Append("]}").ToString();
    }

    static Color32[] Input(string fixture, int size)
    {
        var output = new Color32[size * size];
        var points = new Vector2[10];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = Mathf.PI * .5f + i * Mathf.PI / 5;
            float radius = size * (i % 2 == 0 ? .36f : .16f);
            points[i] = new Vector2(size * .5f + Mathf.Cos(angle) * radius, size * .5f + Mathf.Sin(angle) * radius);
        }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float distance;
            if (fixture == "rings")
            {
                float tile = size / 6f;
                float dx = Mathf.Repeat(x + .5f, tile) - tile * .5f;
                float dy = Mathf.Repeat(y + .5f, tile) - tile * .5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                distance = Mathf.Max(r - tile * .34f, tile * .19f - r);
            }
            else
            {
                Vector2 p = new Vector2(x + .5f, y + .5f);
                float squared = float.PositiveInfinity;
                bool inside = false;
                for (int i = 0; i < 10; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % 10], delta = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude);
                    squared = Mathf.Min(squared, (p - a - delta * t).sqrMagnitude);
                    if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
                }
                distance = Mathf.Sqrt(squared) * (inside ? -1 : 1);
            }
            byte coverage = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(.5f - distance));
            output[y * size + x] = new Color32(255, 255, 255, coverage);
        }
        return output;
    }
}
