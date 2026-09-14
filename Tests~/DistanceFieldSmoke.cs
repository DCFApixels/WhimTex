// Opt-in after manual compilation. Runs CPU/Burst jobs; no assets, windows, rendering or compilation triggered.
var assembly = typeof(DCFApixels.WhimTex.SDFLayerBehaviour).Assembly;
var utility = assembly.GetType("DCFApixels.WhimTex.DistanceFieldUtility", true);
var compute = utility.GetMethod("ComputeSignedDistance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
using var pixels = new Unity.Collections.NativeArray<UnityEngine.Color32>(5, Unity.Collections.Allocator.TempJob);
using var distances = new Unity.Collections.NativeArray<float>(5, Unity.Collections.Allocator.TempJob);
var writable = pixels;
for (int i = 0; i < 5; i++) writable[i] = new UnityEngine.Color32(0, 0, 0, (byte)(i == 4 ? 255 : i * 64));
foreach (var metric in new[] { DCFApixels.WhimTex.DistanceMetric.EuclideanExact, DCFApixels.WhimTex.DistanceMetric.EuclideanAntialiased })
{
    compute.Invoke(null, new object[] { pixels, distances, 5, 1, (byte)128, 0, metric });
    for (int i = 0; i < distances.Length; i++) Check(!float.IsNaN(distances[i]) && !float.IsInfinity(distances[i]), "Distance remains finite");
    if (metric == DCFApixels.WhimTex.DistanceMetric.EuclideanAntialiased)
    {
        Check(distances[1] > 0 && distances[3] < 0, "Partial coverage preserves sign");
        Check(UnityEngine.Mathf.Abs(distances[2]) < .0001f, "Threshold coverage lies on the boundary");
    }
    else Check(distances[2] > 0, "Binary Exact keeps its original strict threshold");
}
foreach (byte alpha in new byte[] { 0, 255 })
{
    for (int i = 0; i < 5; i++) writable[i] = new UnityEngine.Color32(0, 0, 0, alpha);
    compute.Invoke(null, new object[] { pixels, distances, 5, 1, (byte)128, 0, DCFApixels.WhimTex.DistanceMetric.EuclideanAntialiased });
    for (int i = 0; i < 5; i++) Check(alpha == 0 ? distances[i] > 0 : distances[i] < 0, "Uniform mask has the expected sign");
}
var colorInput = typeof(DCFApixels.WhimTex.SDFLayerBehaviour).GetProperty("RequiresColorInput",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var layer = new DCFApixels.WhimTex.SDFLayerBehaviour();
foreach (DCFApixels.WhimTex.SDFLayerBehaviour.SourceChannel channel in System.Enum.GetValues(typeof(DCFApixels.WhimTex.SDFLayerBehaviour.SourceChannel)))
{
    layer.sourceChannel = channel;
    Check((bool)colorInput.GetValue(layer) == (channel != DCFApixels.WhimTex.SDFLayerBehaviour.SourceChannel.Alpha), "Group source requests the selected data channels");
}
return "Distance field CPU/Burst smoke checks passed: " + checks + "; actual group rendering and Outline appearance still need visual verification.";
