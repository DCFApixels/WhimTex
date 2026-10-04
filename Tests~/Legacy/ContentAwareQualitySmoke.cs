using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using DCFApixels.WhimTex;

// Pipeline run_script: Main is self-contained. RecordBaseline is an optional before-change benchmark.
// Only generated images in Temp; reflection targets WhimTex, never Unity internals.
public static class ContentAwareQualitySmoke
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int W = 129, H = 97;
    const string Folder = "Temp/WhimTex/FillQuality";
    static readonly Type Algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
    static readonly Type Input = Algorithm.GetNestedType("Input", Flags);
    static readonly string[] Cases = { "edge", "diagonal", "stripes", "texture-flat", "gradient", "alpha-hdr", "noisy-edge", "shading" };
    static Color[] Run(Color[] pixels, byte[] mask, int quality, int seed, Action<float> progress = null, CancellationToken token = default)
    {
        var input = Activator.CreateInstance(Input, true);
        void Set(string name, object value) => Input.GetField(name, Flags).SetValue(input, value);
        var donors = new byte[mask.Length]; Array.Fill(donors, (byte)255);
        Set("width", W); Set("height", H); Set("pixels", pixels); Set("target", mask); Set("donors", donors);
        Set("quality", quality); Set("seed", seed);
        var result = Algorithm.GetMethod("Run", Flags).Invoke(null, new object[] { input, token, progress });
        return (Color[])result.GetType().GetField("pixels", Flags).GetValue(result);
    }
    static Color[] Generate(string kind)
    {
        var pixels = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            float v = .5f;
            switch (kind)
            {
                case "edge": v = y < 48 ? .15f : .85f; break;
                case "diagonal": v = y < x * .5f + 16 ? .15f : .85f; break;
                case "stripes": v = (x / 4) % 2 == 0 ? .15f : .85f; break;
                case "texture-flat":
                    uint hash = unchecked((uint)((x % 8) * 173 + (y % 8) * 337 + 37));
                    hash ^= hash << 13; hash ^= hash >> 17; hash ^= hash << 5;
                    v = y < 65 ? .1f + .8f * (hash & 255) / 255f : .5f; break;
                case "gradient": v = .1f + .8f * (x + y) / (W + H - 2f); break;
                case "alpha-hdr":
                    pixels[y * W + x] = y < 48 ? new Color(3, -.2f, .4f, .4f) : new Color(.2f, .8f, 2, .8f); continue;
                case "noisy-edge":
                    uint noise = unchecked((uint)(x * 173 + y * 337 + 37));
                    noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                    v = (y < 48 ? .2f : .7f) + .15f * (noise & 255) / 255f; break;
                case "shading": v = .1f + .8f * Mathf.Exp(-((x - 64f) * (x - 64f) + (y - 48f) * (y - 48f)) / 4000f); break;
            }
            pixels[y * W + x] = new Color(v, v, v, 1);
        }
        return pixels;
    }
    static double Error(Color[] a, Color[] b, byte[] mask)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < a.Length; i++) if (mask[i] != 0)
        { sum += Math.Abs(a[i].r * a[i].a - b[i].r * b[i].a) + Math.Abs(a[i].g * a[i].a - b[i].g * b[i].a) + Math.Abs(a[i].b * a[i].a - b[i].b * b[i].a) + Math.Abs(a[i].a - b[i].a); n++; }
        return sum / (4 * n);
    }
    static double Contrast(Color[] pixels, byte[] mask)
    {
        double sum = 0; int n = 0;
        for (int y = 1; y < H; y++) for (int x = 1; x < W; x++)
        {
            int i = y * W + x;
            if (mask[i] == 0 || mask[i - 1] == 0 || mask[i - W] == 0) continue;
            sum += Math.Abs(pixels[i].r - pixels[i - 1].r) + Math.Abs(pixels[i].r - pixels[i - W].r); n++;
        }
        return sum / n;
    }
    static void SavePixels(string path, Color[] pixels)
    {
        using var stream = new BinaryWriter(File.Create(path));
        foreach (var c in pixels) { stream.Write(c.r); stream.Write(c.g); stream.Write(c.b); stream.Write(c.a); }
    }
    static Color[] LoadPixels(string path)
    {
        using var stream = new BinaryReader(File.OpenRead(path));
        var pixels = new Color[W * H];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle(), stream.ReadSingle());
        return pixels;
    }
    public static string RecordBaseline() => Execute(true);
    public static string Main() => Execute(false);
    static string Execute(bool baseline)
    {
        Directory.CreateDirectory(Folder);
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        var report = new StringBuilder("case,quality,median_ms,masked_mae,contrast_ratio,baseline_mae\n");
        var preview = new Color[W * 4 * H * Cases.Length];
        for (int c = 0; c < Cases.Length; c++)
        {
            string name = Cases[c]; var truth = Generate(name); var damaged = (Color[])truth.Clone();
            var mask = new byte[W * H];
            for (int y = 36; y < 61; y++) for (int x = 55; x < 74; x++) { mask[y * W + x] = 255; damaged[y * W + x] = Color.clear; }
            var donors = new HashSet<Color>(); for (int i = 0; i < truth.Length; i++) if (mask[i] == 0) donors.Add(truth[i]);
            for (int quality = 0; quality <= 2; quality++)
            {
                Color[] result = Run(damaged, mask, quality, 123); // warm-up outside timing
                var times = new double[3];
                for (int pass = 0; pass < times.Length; pass++)
                {
                    var timer = Stopwatch.StartNew(); var repeat = Run(damaged, mask, quality, 123); timer.Stop();
                    times[pass] = timer.Elapsed.TotalMilliseconds;
                    for (int i = 0; i < result.Length; i++) Check(result[i].Equals(repeat[i]), "Determinism " + name);
                }
                Array.Sort(times);
                for (int i = 0; i < result.Length; i++)
                {
                    Check(float.IsFinite(result[i].r) && float.IsFinite(result[i].g) && float.IsFinite(result[i].b) && float.IsFinite(result[i].a), "Finite " + name);
                    if (mask[i] == 0) Check(result[i].Equals(truth[i]), "Outside unchanged " + name);
                    else if (!baseline)
                    {
                        if (name == "edge" || name == "diagonal" || name == "stripes" || name == "alpha-hdr")
                            Check(donors.Contains(result[i]), "Sharp reconstruction retains an actual donor color " + name);
                        else
                            Check(result[i].r >= .09999f && result[i].r <= .90001f && result[i].a == 1,
                                "Smooth reconstruction stays in the source range " + name);
                    }
                    Check(mask[i] == 0 || damaged[i].a == 0, "Input immutable");
                }
                if (!baseline)
                {
                    foreach (int seed in new[] { 7, 123, 877 })
                    {
                        var variant = seed == 123 ? result : Run(damaged, mask, quality, seed);
                        double error = Error(variant, truth, mask);
                        double contrast = Contrast(variant, mask) / Contrast(truth, mask);
                        // Nonperiodic noise cannot be recovered exactly; bound error and texture loss instead.
                        // The missing shading peak has no matching donor (the old algorithm also misses it).
                        // This is a bounded limitation check, not a claim of exact shading reconstruction.
                        double tolerance = name == "noisy-edge" ? .07 : name == "shading" ? .065 : .025;
                        Check(error < tolerance, $"Quality {name}/{quality}/seed {seed}: MAE {error:F5}");
                        if (name == "texture-flat" || name == "stripes" || name == "noisy-edge")
                            Check(contrast > .7 && contrast < 1.4, $"Texture {name}/{quality}/seed {seed}: contrast {contrast:F3}");
                    }
                }
                string path = Folder + "/" + name + "-" + quality + ".baseline.bin";
                if (baseline) SavePixels(path, result);
                bool hasBaseline = File.Exists(path);
                var old = hasBaseline ? LoadPixels(path) : damaged;
                string baselineError = hasBaseline ? FormattableString.Invariant($"{Error(old, truth, mask):F5}") : "unavailable";
                report.AppendLine(FormattableString.Invariant($"{name},{quality},{times[1]:F2},{Error(result, truth, mask):F5},{Contrast(result, mask) / Contrast(truth, mask):F3},{baselineError}"));
                if (quality != 1) continue;
                var columns = new[] { truth, damaged, old, result };
                for (int col = 0; col < 4; col++) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    var p = columns[col][y * W + x];
                    p = p * p.a + new Color(.2f, .2f, .2f, 1) * (1 - p.a); p.a = 1;
                    preview[((Cases.Length - 1 - c) * H + y) * W * 4 + col * W + x] = p;
                }
            }
        }
        if (!baseline)
        {
            var source = Generate("edge"); var mask = new byte[W * H]; mask[48 * W + 64] = 255;
            float last = 0; int completed = 0;
            Run(source, mask, 0, 123, p => { Check(p > last && p <= 1, "Monotonic progress"); last = p; if (p == 1) completed++; });
            Check(last == 1 && completed == 1, "Completion is reported exactly once after final reconstruction");
            using var cancellation = new CancellationTokenSource();
            bool cancelled = false;
            try { Run(source, mask, 0, 123, p => { if (p < 1) cancellation.Cancel(); }, cancellation.Token); }
            catch (TargetInvocationException e) { cancelled = e.InnerException is OperationCanceledException; }
            Check(cancelled, "Cancellation during matching never returns a partial repair");
        }
        var image = new Texture2D(W * 4, H * Cases.Length, TextureFormat.RGBA32, false);
        try { image.SetPixels(preview); image.Apply(); File.WriteAllBytes(Folder + (baseline ? "/baseline.png" : "/comparison.png"), image.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
        File.WriteAllText(Folder + (baseline ? "/baseline.csv" : "/current.csv"), report.ToString());
        return $"PASS {checks} checks. Timings: managed fill only, 129x97, 19x25 hole, median of 3 warm runs.\n" + report;
    }
}
