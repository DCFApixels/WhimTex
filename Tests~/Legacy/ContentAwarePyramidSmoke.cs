using System;
using System.Reflection;
using System.Threading;
using UnityEngine;
using DCFApixels.WhimTex;

// run_script ContentAwarePyramidSmoke.Main. Self-contained, no assets or windows.
// Reflection targets WhimTex only. A coarse color buffer must never seed fine detail.
public static class ContentAwarePyramidSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
    static object New() => Activator.CreateInstance(Algorithm.GetNestedType("Level", F), true);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, F).GetValue(o);
    static object Call(string name, params object[] args) => Algorithm.GetMethod(name, F).Invoke(null, args);
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
        foreach (var size in new[] { new Vector2Int(65, 49), new Vector2Int(64, 48), new Vector2Int(1, 49) })
        foreach (bool invalidGuess in new[] { false, true })
        {
            int w = size.x, h = size.y, n = w * h;
            var pixels = new Color[n]; var target = new byte[n]; var allowed = new byte[n];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                // Real high-resolution variation, including negative RGB and partial alpha.
                pixels[i] = new Color((i % 7) * .4f, -.3f + (i % 5) * .02f, (i % 3) * .7f, .4f + (i % 2) * .3f);
                if (y >= 18 && y < 30 && (w == 1 || x >= 25 && x < 38)) target[i] = (byte)(i % 2 == 0 ? 128 : 255);
                else allowed[i] = 255;
            }
            object Fine()
            {
                var fine = New(); Set(fine, "width", w); Set(fine, "height", h);
                Set(fine, "source", pixels); Set(fine, "target", target); Set(fine, "allowed", allowed);
                Call("BuildTextureFeatures", fine, CancellationToken.None); return fine;
            }
            var fine = Fine(); var coarse = Call("Downsample", fine, CancellationToken.None);
            Call("Prepare", coarse, null, 3, CancellationToken.None);
            int cw = Get<int>(coarse, "width"); int[] cm = Get<int[]>(coarse, "matches");
            // Force an invalid upsampled patch so the nearest valid fine donor fallback is exercised.
            if (invalidGuess) Array.Fill(cm, (24 / 2) * cw + (w == 1 ? 0 : 30 / 2));
            Set(coarse, "working", null); Set(coarse, "workingTexture", null);
            Call("Prepare", fine, coarse, 3, CancellationToken.None);
            int[] matches = Get<int[]>(fine, "matches"); bool[] valid = Get<bool[]>(fine, "valid");
            var output = Get<Color[]>(fine, "working"); var texture = Get<Vector2[]>(fine, "texture");
            var features = Get<Vector2[]>(fine, "workingTexture"); int validGuesses = 0, fallbacks = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (target[i] == 0) { Check(output[i].Equals(pixels[i]), "Known colors unchanged"); continue; }
                Check(valid[matches[i]], "Fine seed always uses an allowed complete patch");
                Check(output[i].Equals(pixels[matches[i]]), "Color is an original fine-resolution donor sample");
                Check(features[i].Equals(texture[matches[i]]), "Texture features come from the same fine donor");
                int c = cm[(y / 2) * cw + x / 2];
                int sx = Math.Min(w - 1, c % cw * 2 + x % 2), sy = Math.Min(h - 1, c / cw * 2 + y % 2);
                if (valid[sy * w + sx]) { validGuesses++; Check(matches[i] == sy * w + sx, "Coordinate parity preserved"); }
                else fallbacks++;
            }
            Check(invalidGuess ? fallbacks > 0 : validGuesses > 0, "Expected transfer/fallback path exercised");
            Check(ReferenceEquals(target, Get<byte[]>(fine, "target")), "Soft coverage is not rewritten");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); bool stopped = false;
            try { Call("Prepare", Fine(), coarse, 3, cancellation.Token); }
            catch (TargetInvocationException e) { stopped = e.InnerException is OperationCanceledException; }
            Check(stopped, "Preparation honors cancellation");
        }
        return $"PASS {checks} pyramid transfer checks";
    }
}
