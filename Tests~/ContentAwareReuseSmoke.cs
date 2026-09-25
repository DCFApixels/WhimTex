using System;
using System.Reflection;
using System.Threading;
using UnityEngine;
using DCFApixels.WhimTex;

// Self-contained source-coverage and bounded-selection contracts. No Unity internal reflection.
public static class ContentAwareReuseSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
    static readonly Type Level = Algorithm.GetNestedType("Level", F);
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static object Call(string name, params object[] args) => Algorithm.GetMethod(name, F).Invoke(null, args);
    public static string Main()
    {
        checks = 0;
        const int w = 48, h = 40, n = w * h;
        foreach (int radius in new[] { 1, 2, 3 })
        foreach (int scenario in new[] { 0, 1, 2 })
        {
            bool coherent = scenario == 0, scarce = scenario == 2;
            var level = Activator.CreateInstance(Level, true);
            var target = new byte[n]; var allowed = new byte[n]; var matches = new int[n];
            var candidates = new System.Collections.Generic.List<int>();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (scarce ? x >= 14 && y >= 4 && y < 36 : x >= 26 && x < 42 && y >= 10 && y < 30)
                {
                    target[i] = 255;
                    matches[i] = coherent ? y * w + x - 22 : (18 + x % 2) * w + (scarce ? 5 : 10) + y % 2;
                }
                if (x < (scarce ? 12 : 24)) allowed[i] = 255;
                if (x >= radius && x < (scarce ? 12 : 24) - radius && y >= radius && y < h - radius) candidates.Add(i);
            }
            Set(level, "width", w); Set(level, "height", h); Set(level, "radius", radius);
            Set(level, "target", target); Set(level, "allowed", allowed); Set(level, "matches", matches);
            Set(level, "candidates", candidates.ToArray());
            bool active = (bool)Call("BuildReusePenalty", level, CancellationToken.None);
            Check(active != coherent, "Only concentrated repeated patches should activate the penalty.");
            var coverage = new int[n]; int targets = 0, donors = 0;
            for (int i = 0; i < n; i++)
            {
                if (allowed[i] != 0) donors++;
                if (target[i] == 0) continue;
                targets++;
                for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                    coverage[matches[i] + dy * w + dx]++;
            }
            int area = (2 * radius + 1) * (2 * radius + 1);
            double allowance = 2d * area * Math.Max(1d, targets / (double)donors);
            var penalties = (float[])Get(level, "reusePenalty");
            foreach (int c in candidates)
            {
                int total = 0;
                for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++) total += coverage[c + dy * w + dx];
                double average = total / (double)area;
                float expected = average > allowance ? (float)(1 - allowance / average) : 0;
                Check(penalties[c].Equals(expected), "Footprint coverage differs from brute force.");
            }
            if (!coherent)
            {
                int center = scarce ? 5 : 10;
                Check(penalties[18 * w + center] > .5f && penalties[19 * w + center + 1] > .5f,
                    "One-pixel donor shifts must not bypass the penalty.");
                // Rebuilding reuses buffers and clears stale coverage; no history across strokes.
                var scratch = Get(level, "usageScratch"); Array.Clear(target, 0, n);
                Check(!(bool)Call("BuildReusePenalty", level, CancellationToken.None), "Stale target coverage.");
                Check(ReferenceEquals(scratch, Get(level, "usageScratch")), "Per-pass scratch allocation.");
                foreach (int c in candidates) Check(penalties[c] == 0, "Stale penalty.");
            }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { Call("BuildReusePenalty", level, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (TargetInvocationException e) when (e.InnerException is OperationCanceledException) { checks++; }
        }
        var type = Algorithm.GetNestedType("MatchCandidate", F);
        var alternatives = Array.CreateInstance(type, 3);
        void Candidate(int i, float cost)
        {
            var candidate = Activator.CreateInstance(type); Set(candidate, "donor", i); Set(candidate, "cost", cost); alternatives.SetValue(candidate, i);
        }
        var usage = new[] { 1f, 0f, .5f };
        int Select() => (int)Call("SelectReusableCandidate", alternatives, 3, 0, usage);
        Candidate(0, 1); Candidate(1, 1.1f); Candidate(2, 1.02f);
        Check(Select() == 1, "Prefer an underused near-quality alternative.");
        Candidate(1, 1.16f); Candidate(2, 1.3f);
        Check(Select() == 0, "Never exceed the 15% appearance budget.");
        Candidate(0, 0); Candidate(1, .00001f); Candidate(2, .00002f);
        Check(Select() == 0, "Never sacrifice an exact match.");
        Candidate(0, 1e30f); Candidate(1, 1.1e30f); Candidate(2, 1.3e30f);
        Check(Select() == 1, "HDR-scale invariant selection.");
        Candidate(0, 1); Candidate(1, 1.1f); Candidate(2, .5f);
        Check((int)Call("SelectReusableCandidate", alternatives, 3, 2, usage) == 2,
            "Budget must use the final best appearance, not the initial incumbent.");
        var zero = Activator.CreateInstance(Level, true);
        Check(!(bool)Call("BuildReusePenalty", zero, CancellationToken.None), "Radius-zero fallback must not allocate or rebalance.");
        MatchContracts();
        return "PASS ContentAwareReuseSmoke: " + checks + " checks.";
    }

    static void MatchContracts()
    {
        const int w = 48, h = 40, n = w * h;
        var level = Activator.CreateInstance(Level, true);
        var source = new Color[n]; var allowed = new byte[n]; var target = new byte[n];
        var random = new System.Random(981);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x; float value = .25f + .5f * (float)random.NextDouble();
            source[i] = new Color(value, value, value, 1);
            if (x < 24) allowed[i] = 255;
            if (x >= 26 && y > 3 && y < 36) target[i] = 255;
        }
        Set(level, "width", w); Set(level, "height", h); Set(level, "source", source);
        Set(level, "allowed", allowed); Set(level, "target", target);
        Call("BuildTextureFeatures", level, CancellationToken.None);
        Call("Prepare", level, null, 2, CancellationToken.None);
        var matches = (int[])Get(level, "matches");
        int repeated = 20 * w + 12;
        for (int i = 0; i < n; i++) if (target[i] != 0) matches[i] = repeated;
        Call("BuildReusePenalty", level, CancellationToken.None);
        var frozen = (float[])((float[])Get(level, "reusePenalty")).Clone();
        var rng = Activator.CreateInstance(Algorithm.GetNestedType("Random", F)); Set(rng, "state", 19u);
        Call("Match", level, true, rng, CancellationToken.None);
        var penalties = (float[])Get(level, "reusePenalty");
        var errors = (float[])Get(level, "errors"); var scale = (float[])Get(level, "errorScale");
        var sampleType = Algorithm.GetNestedType("PatchSample", F);
        var samples = Array.CreateInstance(sampleType, 25);
        int changed = 0;
        for (int i = 0; i < n; i++)
        {
            Check(frozen[i].Equals(penalties[i]), "Usage must stay frozen during a match pass.");
            if (target[i] == 0) continue;
            if (matches[i] != repeated) changed++;
            int count = (int)Call("PrepareSamples", level, i % w, i / w, samples, null);
            float error = (float)Call("Cost", level, matches[i], samples, count, float.MaxValue);
            Check(errors[i].Equals(error * scale[i]), "Stored error includes penalty or an early-out estimate.");
        }
        Check(changed > 0, "Match integration did not exercise changed donors.");
    }
}
