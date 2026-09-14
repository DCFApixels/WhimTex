// Opt-in after manual Unity compilation. Invoke through the same script runner as DrawingPatternSmoke.
// Exercises production jobs without creating assets or GPU resources.
var checks = 0;
var fillType = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.FloodFillUtility", true);
var fillMethod = fillType.GetMethod("Fill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var clear = new UnityEngine.Color32(0, 0, 0, 0);
var black = new UnityEngine.Color32(0, 0, 0, 255);
var white = new UnityEngine.Color32(255, 255, 255, 255);
var red = new UnityEngine.Color32(255, 0, 0, 255);
var blue = new UnityEngine.Color32(0, 0, 255, 255);

void Check(bool condition, string message)
{
    if (!condition) throw new System.Exception(message);
    checks++;
}
UnityEngine.Color32[] Fill(UnityEngine.Color32[] source, UnityEngine.Color32[] reference,
    int width, int seed, UnityEngine.Color32 color, int tolerance, int expand, bool aa,
    byte[] mask, out bool changed, bool contiguous = true)
{
    using var src = new Unity.Collections.NativeArray<UnityEngine.Color32>(source, Unity.Collections.Allocator.TempJob);
    using var sample = new Unity.Collections.NativeArray<UnityEngine.Color32>(reference, Unity.Collections.Allocator.TempJob);
    using var valid = new Unity.Collections.NativeArray<byte>(source.Length, Unity.Collections.Allocator.TempJob);
    using var output = new Unity.Collections.NativeArray<UnityEngine.Color32>(source.Length, Unity.Collections.Allocator.TempJob);
    if (mask == null)
    {
        mask = new byte[source.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = 1;
    }
    valid.CopyFrom(mask);
    changed = (bool)fillMethod.Invoke(null, new object[]
    {
        src, sample, valid, output, width, source.Length / width, seed, color, tolerance, expand, aa, contiguous
    });
    return output.ToArray();
}

// A separate reference can bound an empty destination; disconnected and diagonal matches stay untouched.
var result = Fill(new[] { clear, clear, clear }, new[] { black, white, black },
    3, 0, red, 0, 0, false, null, out bool changed);
Check(changed && result[0].Equals(red) && result[1].Equals(clear) && result[2].Equals(clear),
    "Reference sampling fills only the connected destination region");
result = Fill(new[] { black, white, white, black }, new[] { black, white, white, black },
    2, 0, red, 0, 0, false, null, out changed);
Check(result[3].Equals(black), "Diagonal contact is not four-connected");
var gradient = new[] { black, new UnityEngine.Color32(20, 20, 20, 255), new UnityEngine.Color32(40, 40, 40, 255) };
result = Fill(gradient, gradient, 3, 0, red, 25, 0, false, null, out changed);
Check(result[1].Equals(red) && result[2].Equals(gradient[2]), "Tolerance compares to seed, not neighbors");
result = Fill(gradient, gradient, 3, 0, red, 0, 0, false, null, out changed);
Check(result[1].Equals(gradient[1]), "Zero tolerance excludes nonidentical colors");
result = Fill(new[] { black, white, clear }, new[] { black, white, clear },
    3, 0, red, 255, 0, false, null, out changed);
Check(result[2].Equals(red), "Maximum tolerance includes opaque and transparent pixels");
var hidden = new[] { clear, new UnityEngine.Color32(255, 30, 90, 0), white };
result = Fill(hidden, hidden, 3, 0, red, 0, 0, false, null, out changed);
Check(result[1].Equals(red) && result[2].Equals(white), "Transparent hidden RGB is ignored, alpha is not");
result = Fill(new[] { blue }, new[] { blue }, 1, 0, new UnityEngine.Color32(255, 0, 0, 128),
    0, 0, false, null, out changed);
Check(result[0].r == 128 && result[0].b == 127 && result[0].a == 255, "Foreground uses straight-alpha source-over");
result = Fill(new[] { blue }, new[] { blue }, 1, 0, clear, 255, 32, true, null, out changed);
Check(!changed && result[0].Equals(blue), "Zero alpha is a no-op");
result = Fill(new[] { red }, new[] { red }, 1, 0, red, 0, 0, false, null, out changed);
Check(!changed, "Identical output does not create an edit");
var blank = new UnityEngine.Color32[9];
var center = new[] { white, white, white, white, black, white, white, white, white };
result = Fill(blank, center, 3, 4, red, 0, 0, true, null, out changed);
Check(result[4].a == 255 && result[1].a > 0 && result[1].a < 255 && result[0].a < result[1].a,
    "Antialias gives the outer edge partial coverage");
result = Fill(blank, center, 3, 4, red, 0, 0, false, null, out changed);
Check(result[4].a == 255 && result[1].a == 0, "Antialias off retains hard source-pixel edges");
result = Fill(blank, center, 3, 4, red, 0, 1, false, null, out changed);
Check(result[1].a == 255 && result[0].a == 0, "One-pixel expansion uses grid distance");
result = Fill(blank, center, 3, 4, red, 0, 2, false, null, out changed);
Check(result[0].a == 255, "Two-pixel expansion includes diagonal neighbors");
result = Fill(new[] { clear, clear, clear }, new[] { black, black, black },
    3, 0, red, 0, 0, false, new byte[] { 1, 0, 1 }, out changed);
Check(result[1].a == 0 && result[2].a == 0, "Invalid reference pixels block traversal");
result = Fill(new[] { clear, clear, clear }, new[] { black, black, black },
    3, 0, red, 0, 32, true, new byte[] { 0, 1, 1 }, out changed);
Check(!changed, "A seed outside the composite cannot expand into a fill");
result = Fill(new[] { clear, clear }, new[] { black, white },
    2, 0, red, 0, 32, true, new byte[] { 1, 0 }, out changed);
Check(result[1].a == 0, "Expansion and antialias never write invalid reference pixels");

result = Fill(new[] { clear, clear, clear }, new[] { black, white, black },
    3, 0, red, 0, 0, false, null, out changed, false);
Check(result[0].Equals(red) && result[1].Equals(clear) && result[2].Equals(red),
    "Contiguous off fills disconnected reference matches on the destination layer");
result = Fill(new[] { black, white, white, black }, new[] { black, white, white, black },
    2, 0, red, 0, 0, false, null, out changed, false);
Check(result[3].Equals(red) && result[1].Equals(white), "Contiguous off also includes diagonal matches");
result = Fill(new[] { clear, clear, clear, clear }, new[] { black, white, gradient[1], gradient[2] },
    4, 0, red, 25, 0, false, null, out changed, false);
Check(result[2].Equals(red) && result[3].Equals(clear), "Global matching uses the same seed tolerance");
result = Fill(new[] { clear, clear, clear }, new[] { black, black, black },
    3, 0, red, 0, 0, false, new byte[] { 1, 0, 1 }, out changed, false);
Check(result[1].a == 0 && result[2].Equals(red), "Invalid pixels are excluded but do not separate global matches");
result = Fill(new[] { clear, clear }, new[] { black, black },
    2, 0, red, 255, 32, true, new byte[] { 0, 1 }, out changed, false);
Check(!changed, "Contiguous off still needs a valid seed");
result = Fill(hidden, hidden, 3, 0, red, 0, 0, false, null, out changed, false);
Check(result[1].Equals(red) && result[2].Equals(white), "Global matching preserves transparent-color semantics");
var separated = new[] { black, white, white, white, black };
result = Fill(new UnityEngine.Color32[5], separated, 5, 0, red, 0, 0, true, null, out changed, false);
Check(result[0].a == 255 && result[4].a == 255 && result[1].a > 0 && result[3].a > 0 && result[2].a == 0,
    "Antialias softens each disconnected matched region");
result = Fill(new UnityEngine.Color32[5], separated, 5, 0, red, 0, 1, false, null, out changed, false);
Check(result[1].a == 255 && result[3].a == 255 && result[2].a == 0, "Expansion applies to every matched region");
Check(new DCFApixels.WhimTex.DrawingLayerBehaviour().fillContiguous, "New layers keep connected fill by default");
Check(new DCFApixels.WhimTex.DrawingLayerBehaviour().fillSampleMode == DCFApixels.WhimTex.FillSampleMode.CurrentLayer,
    "New layers keep All Layers off by default");

var layer = new DCFApixels.WhimTex.DrawingLayerBehaviour
{
    fillSampleMode = DCFApixels.WhimTex.FillSampleMode.AllLayers,
    fillContiguous = false,
    fillTolerance = 75, fillExpand = 3, fillAntialias = false
};
var copy = UnityEngine.JsonUtility.FromJson<DCFApixels.WhimTex.DrawingLayerBehaviour>(UnityEngine.JsonUtility.ToJson(layer));
Check(copy.fillSampleMode == layer.fillSampleMode && !copy.fillContiguous && copy.fillTolerance == 75 && copy.fillExpand == 3 && !copy.fillAntialias,
    "Per-layer fill settings survive serialization and cloning");
return "Flood fill checks passed: " + checks + ". No assets or GPU resources created.";
