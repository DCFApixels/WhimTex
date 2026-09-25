using System;
using System.Reflection;
using System.Threading;
using UnityEngine;
using DCFApixels.WhimTex;

// Self-contained final-stage contracts; reflection only into WhimTex's managed worker.
// run_script ContentAwareReconstructionSmoke.Main. No scene, asset, window or file changes.
public static class ContentAwareReconstructionSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const int W = 33, H = 21, X = 16, Y = 10, A = 10 * W + 5, B = 10 * W + 26;
    static readonly Type Algorithm = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ContentAwareFill", true);
    static readonly Type Level = Algorithm.GetNestedType("Level", F);
    static Color Straight(Vector4 p) => new Color(p.x / p.w, p.y / p.w, p.z / p.w, p.w);
    static Vector4 Premult(Color c) => new Vector4(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
    public static string Main()
    {
        int checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
        var source = new Color[W * H]; var target = new byte[W * H]; var allowed = new byte[W * H];
        var matches = new int[W * H]; var errors = new float[W * H];
        Array.Fill(source, Color.gray); Array.Fill(allowed, (byte)255);
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            int i = (Y + dy) * W + X + dx; target[i] = 255; allowed[i] = 0;
            matches[i] = B + dy * W + dx;
        }
        matches[Y * W + X] = A;
        object level = Activator.CreateInstance(Level, true);
        void Set(string name, object value) => Level.GetField(name, F).SetValue(level, value);
        Set("width", W); Set("height", H); Set("radius", 1); Set("source", source);
        Set("target", target); Set("allowed", allowed); Set("matches", matches); Set("errors", errors);
        Color Reconstruct() => (Color)Algorithm.GetMethod("ReconstructCompatible", F).Invoke(null, new object[] { level, X, Y, A, null });
        void Plane(int center, Vector4 color, Vector4 slope)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                source[center + dy * W + dx] = Straight(color + slope * dx);
        }
        var a = new Vector4(.4f, .4f, .4f, 1); var b = new Vector4(.41f, .41f, .41f, 1);
        var slope = new Vector4(.02f, .02f, .02f, 0);
        Plane(A, a, slope); Plane(B, b, slope);
        Color blended = Reconstruct();
        Check(blended.r > .401f && blended.r < .41f && blended.a == 1, "Compatible smooth proposals blend");
        Check(blended.Equals(Reconstruct()), "Final reconstruction is deterministic");
        // A repaired output is intentionally poisoned: classification must only inspect source donors.
        var working = new Color[W * H]; Array.Fill(working, new Color(float.NaN, -100, 100, 0)); Set("working", working);
        Check(blended.Equals(Reconstruct()), "Reconstruction never classifies its own artifacts");
        Plane(B, b, -slope);
        Check(Reconstruct().Equals(source[A]), "Opposite slopes are not a compatible local pattern");
        Plane(B, new Vector4(.9f, .1f, .23f, 1), slope);
        Check(Reconstruct().Equals(source[A]), "Similar brightness alone does not justify mixing hues");
        Plane(B, b, slope); source[A + 1] = Color.white;
        Check(Reconstruct().Equals(source[A]), "Sharp source edge retains best donor exactly");
        Plane(A, a, slope); source[B + 1] = Color.white;
        Check(Reconstruct().Equals(source[A]), "Sharp candidate is not blended into a smooth anchor");
        Plane(B, b, slope); allowed[A - 1] = 0; source[A - 1] = new Color(float.NaN, 0, 0, 0);
        Check(Reconstruct().Equals(source[A]), "Missing source context keeps best donor without reading invalid data");
        allowed[A - 1] = 255; Plane(A, a, slope);
        Set("radius", 0); Check(Reconstruct().Equals(source[A]), "Zero-radius fallback is finite and unchanged"); Set("radius", 1);
        foreach (float scale in new[] { .01f, 10f, 1000f })
        {
            Vector4 Scale(Vector4 v) => new Vector4(v.x * scale, v.y * scale, v.z * scale, v.w);
            Plane(A, Scale(a), Scale(slope)); Plane(B, Scale(b), Scale(slope));
            Check(Math.Abs(Reconstruct().r / scale - blended.r) < 1e-5f, "HDR/dark scale invariance");
        }
        a = new Vector4(1.2f, -.1f, .8f, .4f); b = a + new Vector4(.01f, .005f, -.01f, .01f);
        slope = new Vector4(.02f, .01f, -.02f, .01f);
        Plane(A, a, slope); Plane(B, b, slope); Vector4 hdr = Premult(Reconstruct());
        float alphaFraction = (hdr.w - a.w) / (b.w - a.w);
        Check(alphaFraction > 0 && alphaFraction < 1, "Partial alpha is blended");
        Check((hdr - Vector4.Lerp(a, b, alphaFraction)).magnitude < 1e-5f,
            "All channels use the same premultiplied blend; negative and HDR RGB are not clamped");
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) { Plane(A, a * .8f, slope); Plane(B, b * .8f, slope); }
            Algorithm.GetMethod("ReconstructFinal", F).Invoke(null, new object[] { level, CancellationToken.None });
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int i = (Y + dy) * W + X + dx;
                Color uncached = (Color)Algorithm.GetMethod("ReconstructCompatible", F).Invoke(null,
                    new object[] { level, X + dx, Y + dy, matches[i], null });
                Check(working[i].Equals(uncached), "Bounded cache equals uncached output and never survives a run");
            }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
        try { Algorithm.GetMethod("ReconstructFinal", F).Invoke(null, new object[] { level, cancelled.Token }); }
        catch (TargetInvocationException e) { stopped = e.InnerException is OperationCanceledException; }
        Check(stopped, "Final stage remains cancellable");
        return $"PASS {checks} selective reconstruction checks";
    }
}
