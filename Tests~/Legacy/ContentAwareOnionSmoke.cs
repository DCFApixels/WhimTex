using System;
using System.Reflection;
using System.Threading;
using System.Text;
using DCFApixels.WhimTex;
using UnityEngine;

// Pipeline run_script ContentAwareOnionSmoke.Main. Generated data and Temp preview, no assets or windows.
// Reflection is confined to WhimTex's managed algorithm, never Unity internals.
public static class ContentAwareOnionSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
    static object New(string name) => Activator.CreateInstance(Algorithm.GetNestedType(name, F), true);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, F).GetValue(o);
    static object Call(string name, params object[] args) => Algorithm.GetMethod(name, F).Invoke(null, args);
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        foreach (string shape in new[] { "rectangle", "concave", "disconnected", "edge", "isolated", "single", "thin" })
        {
            int w = shape == "thin" ? 1 : 35, h = 29, n = w * h;
            var pixels = new Color[n]; var mask = new byte[n]; var allowed = new byte[n];
            Color donorColor = new Color(2, -.2f, .7f, .6f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool target = shape == "rectangle" ? x > 10 && x < 24 && y > 7 && y < 22 :
                    shape == "concave" ? (x > 8 && x < 14 && y > 4 && y < 23) || (x > 8 && x < 27 && y > 17 && y < 23) :
                    shape == "disconnected" ? (x > 7 && x < 15 || x > 22 && x < 29) && y > 8 && y < 20 :
                    shape == "edge" ? x < 12 && y < 12 :
                    shape == "single" ? x == 17 && y == 14 :
                    shape == "thin" ? y > 8 && y < 18 : x > 14 && x < 21 && y > 10 && y < 19;
                int i = y * w + x;
                mask[i] = target ? (byte)(i % 2 == 0 ? 128 : 255) : (byte)0;
                pixels[i] = target ? Color.clear : donorColor;
                if (shape == "isolated" && !target && x > 10 && x < 25 && y > 6 && y < 23) pixels[i] = Color.clear;
                allowed[i] = !target && pixels[i].a > 0 ? (byte)255 : (byte)0;
            }
            object Prepare(float poison)
            {
                var level = New("Level"); Set(level, "width", w); Set(level, "height", h);
                Set(level, "source", pixels); Set(level, "target", mask); Set(level, "allowed", allowed);
                Call("BuildTextureFeatures", level, CancellationToken.None);
                Call("Prepare", level, null, 3, CancellationToken.None);
                // Poison uninitialized colors/features: no current/future ring may leak into matching.
                if (shape != "isolated" && shape != "thin")
                    for (int i = 0; i < n; i++) if (mask[i] != 0)
                    {
                        Get<Color[]>(level, "working")[i] = new Color(poison, poison, poison, 1);
                        Get<Vector2[]>(level, "workingTexture")[i] = new Vector2(poison, poison);
                    }
                return level;
            }
            var first = Prepare(100); var second = Prepare(-100);
            foreach (var level in new[] { first, second })
            {
                var random = New("Random"); Set(random, "state", 123u);
                Call("InitializeFromBoundary", level, random, CancellationToken.None);
            }
            for (int i = 0; i < n; i++)
            {
                var a = Get<Color[]>(first, "working")[i]; var b = Get<Color[]>(second, "working")[i];
                Check(a.Equals(b), "Uninitialized values must not affect rings: " + shape);
                Check(a.Equals(mask[i] == 0 ? pixels[i] : donorColor), "All reachable rings filled, fallback safe: " + shape);
                if (mask[i] != 0)
                    Check(allowed[Get<int[]>(first, "matches")[i]] != 0, "Only original permitted donors: " + shape);
            }
            if (shape == "rectangle")
            {
                using var cts = new CancellationTokenSource(); cts.Cancel();
                bool cancelled = false;
                try { Call("InitializeFromBoundary", first, New("Random"), cts.Token); }
                catch (TargetInvocationException e) { cancelled = e.InnerException is OperationCanceledException; }
                Check(cancelled, "Onion initialization honors cancellation");
            }
        }
        var report = new StringBuilder();
        // Thin diagonal structure crossing increasingly large holes, all quality modes and three seeds.
        foreach (int size in new[] { 17, 33, 49 }) foreach (int quality in new[] { 0, 1, 2 }) foreach (int seed in new[] { 7, 123, 877 })
        {
            const int w = 97, h = 73;
            var pixels = new Color[w * h]; var truth = new Color[w * h];
            var mask = new byte[w * h]; var donors = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x; float v = Math.Abs(y - x * .4f - 16) < 3 ? .9f : .1f;
                pixels[i] = truth[i] = new Color(v, v, v, 1); donors[i] = 255;
                if (Math.Abs(x - 48) <= size / 2 && Math.Abs(y - 36) <= size / 2) { mask[i] = 255; pixels[i] = Color.clear; }
            }
            var input = New("Input"); Set(input, "width", w); Set(input, "height", h); Set(input, "pixels", pixels);
            Set(input, "target", mask); Set(input, "donors", donors); Set(input, "quality", quality); Set(input, "seed", seed);
            var output = Get<Color[]>(Call("Run", input, CancellationToken.None, null), "pixels");
            double error = 0; int count = 0;
            for (int i = 0; i < output.Length; i++)
                if (mask[i] != 0) { error += Math.Abs(output[i].r - truth[i].r); count++; }
                else Check(output[i].Equals(truth[i]), "Structure test leaves known pixels untouched");
            error /= count;
            Check(error < .04, $"Thin structure {size}, quality {quality}, seed {seed}: {error:F4}");
            if (size == 33 && quality == 1 && seed == 123)
            {
                var preview = new Color[w * 3 * h];
                var columns = new[] { truth, pixels, output };
                for (int c = 0; c < 3; c++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    preview[y * w * 3 + c * w + x] = columns[c][y * w + x];
                var image = new Texture2D(w * 3, h, TextureFormat.RGBA32, false);
                try
                {
                    image.SetPixels(preview); image.Apply();
                    System.IO.Directory.CreateDirectory("Temp/WhimTex");
                    System.IO.File.WriteAllBytes("Temp/WhimTex/OnionStructure.png", image.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            report.AppendLine($"bar {size}, q{quality}, seed {seed}: {error:F4}");
        }
        return "PASS " + checks + " onion initialization checks\n" + report;
    }
}
