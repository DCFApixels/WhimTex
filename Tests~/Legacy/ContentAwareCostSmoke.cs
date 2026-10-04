using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

// run_script ContentAwareCostSmoke.Main. Self-contained scalar reference, no assets.
public static class ContentAwareCostSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Main()
    {
        var algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
        var levelType = algorithm.GetNestedType("Level", F);
        var sampleType = algorithm.GetNestedType("PatchSample", F);
        int checks = 0;
        foreach (var size in new[] { new Vector2Int(9, 11), new Vector2Int(16, 12), new Vector2Int(1, 9) })
        foreach (int radius in new[] { 0, 1, 2, 3 })
        {
            int w = size.x, h = size.y, n = w * h;
            if (2 * radius >= Math.Min(w, h)) continue;
            var random = new System.Random(811 + radius);
            var source = new Color[n]; var working = new Color[n]; var texture = new Vector2[n];
            var workingTexture = new Vector2[n]; var target = new byte[n]; var known = new bool[n];
            for (int i = 0; i < n; i++)
            {
                float Next() => (float)random.NextDouble();
                source[i] = new Color(Next() * 8 - 2, Next(), Next() * 300, Next());
                working[i] = new Color(Next(), Next() * 2, Next() * 300, Next());
                target[i] = (byte)(i % 3 == 0 ? 128 : 0); known[i] = i % 4 != 0;
                if (target[i] == 0 && i % 7 == 0) working[i] = new Color(float.NaN, 0, 0, 0);
                texture[i] = new Vector2(Next(), Next()); workingTexture[i] = new Vector2(Next(), Next());
            }
            var level = Activator.CreateInstance(levelType, true);
            void Set(string name, object value) => levelType.GetField(name, F).SetValue(level, value);
            Set("width", w); Set("height", h); Set("radius", radius); Set("source", source);
            Set("working", working); Set("texture", texture); Set("workingTexture", workingTexture); Set("target", target);
            var samples = Array.CreateInstance(sampleType, (2 * radius + 1) * (2 * radius + 1));
            foreach (bool restricted in new[] { false, true })
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int count = (int)algorithm.GetMethod("PrepareSamples", F).Invoke(null,
                    new object[] { level, x, y, samples, restricted ? known : null });
                for (int sy = radius; sy < h - radius; sy += 3)
                for (int sx = radius; sx < w - radius; sx += 3)
                foreach (float limit in new[] { 0f, .01f, 100f, float.MaxValue })
                {
                    double sum = 0; int used = 0;
                    for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                    {
                        int tx = x + dx, ty = y + dy;
                        if ((uint)tx >= w || (uint)ty >= h) continue;
                        int i = ty * w + tx, donor = (sy + dy) * w + sx + dx;
                        if (restricted && !known[i]) continue;
                        Color a = working[i], b = source[donor];
                        if (target[i] == 0 && !(bool)algorithm.GetMethod("IsDonor", F).Invoke(null, new object[] { a })) continue;
                        double weight = target[i] == 0 ? 4 : 1;
                        double dr = a.r * (double)a.a - b.r * (double)b.a, dg = a.g * (double)a.a - b.g * (double)b.a;
                        double db = a.b * (double)a.a - b.b * (double)b.a, da = a.a - (double)b.a;
                        double fx = workingTexture[i].x - (double)texture[donor].x, fy = workingTexture[i].y - (double)texture[donor].y;
                        sum += weight * (dr * dr + dg * dg + db * db + da * da + 4f * (fx * fx + fy * fy));
                        used++;
                        if (sum >= limit) goto Finished;
                    }
                    Finished:
                    float expected = used == 0 ? 0 : sum >= limit ? limit : (float)Math.Min(float.MaxValue, sum);
                    float actual = (float)algorithm.GetMethod("Cost", F).Invoke(null,
                        new object[] { level, sy * w + sx, samples, count, limit });
                    if (!actual.Equals(expected)) throw new Exception($"Cost differs at {w}x{h}, radius {radius}, {x},{y}: {actual} != {expected}");
                    checks++;
                }
            }
        }
        return "PASS ContentAwareCostSmoke: " + checks + " exact scalar comparisons.";
    }
}
