using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

// run_script HealingNoiseSeamDiagnostic.Main. Reproduction with bounded regression gates.
// Own transient Noise/Drawing document, actual tiled brush mask/readback/worker/commit.
// Reflection targets WhimTex only. Saves generated comparison images in Temp, never Assets.
public static class HealingNoiseSeamDiagnostic
{
    const int N = 256;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object o, string name, params object[] args) => (o as Type ?? o.GetType()).GetMethod(name, F).Invoke(o is Type ? null : o, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static Texture2D Pixels(DrawingLayerBehaviour d) => (Texture2D)d.GetType().GetProperty("StoredTexture", F).GetValue(d);
    static void Preview(string name, params Color[][] columns)
    {
        var image = new Texture2D(N * columns.Length, N, TextureFormat.RGBA32, false);
        try
        {
            var display = new Color[N * N * columns.Length];
            for (int c = 0; c < columns.Length; c++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                display[y * N * columns.Length + c * N + x] = columns[c][((y + N / 2) % N) * N + (x + N / 2) % N].gamma;
            image.SetPixels(display); image.Apply();
            File.WriteAllBytes("Temp/WhimTex/NoiseSeams/" + name + ".png", image.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    static (double seam, double curvature, double deviation) Metrics(Color[] p)
    {
        double seam = 0, curvature = 0, sum = 0, squares = 0; int count = 0;
        for (int y = 0; y < N; y++) seam += Math.Abs(p[y * N].r - p[y * N + N - 1].r);
        for (int y = 3; y < N - 3; y++) for (int x = 3; x < N - 3; x++)
        {
            // Inside the repair strip but away from the original seam and canvas boundaries.
            if (x >= 28 && x < N - 28) continue;
            int i = y * N + x;
            curvature += Math.Abs(p[i - 1].r - 2 * p[i].r + p[i + 1].r) + Math.Abs(p[i - N].r - 2 * p[i].r + p[i + N].r); count++;
            sum += p[i].r; squares += p[i].r * (double)p[i].r;
        }
        return (seam / N, curvature / count, Math.Sqrt(Math.Max(0, squares / count - sum * sum / (count * (double)count))));
    }
    static string Format((double seam, double curvature, double deviation) m) =>
        FormattableString.Invariant($"seam_jump={m.seam:F6}, strip_curvature={m.curvature:F6}, strip_deviation={m.deviation:F6}");
    public static async Task<string> Main()
    {
        var focused = EditorWindow.focusedWindow;
        const string pref = "DCFApixels.WhimTex.PreviewTool";
        bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        var report = new StringBuilder("256x256; scale 8; seed 1337; size 64; hardness .8; search 64; Balanced; Current Layer.\n");
        Directory.CreateDirectory("Temp/WhimTex/NoiseSeams");
        try
        {
            foreach (bool fractal in new[] { false, true })
            {
                TextureCompositorWindow window = null; TextureCompositor document = null; Texture2D source = null;
                bool shown = false;
                try
                {
                    window = ScriptableObject.CreateInstance<TextureCompositorWindow>();
                    document = (TextureCompositor)Get(window, "compositor"); document.width = document.height = N;
                    var noise = new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.Perlin,
                        fractal = fractal ? NoiseLayerBehaviour.FractalType.FBm : NoiseLayerBehaviour.FractalType.None, scale = 8, seed = 1337 };
                    document.layers.Add(noise); Call(document, "NormalizeModel");
                    var previous = RenderTexture.active;
                    var rendered = (RenderTexture)Call(document, "RenderLayerPreview", noise.Owner, N);
                    try
                    {
                        source = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                        RenderTexture.active = rendered; source.ReadPixels(new Rect(0, 0, N, N), 0, 0); source.Apply();
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rendered); }
                    var original = source.GetPixels();
                    var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", source);
                    Call(noise, "ReleaseTransientResources");
                    document.layers.Clear(); document.layers.Add(drawing); Call(document, "NormalizeModel");
                    Call(window, "SelectOnlyLayer", drawing.Id);
                    var tool = typeof(TextureCompositorWindow).GetNestedType("PreviewTool", F);
                    Call(window, "ChangePreviewTool", Enum.Parse(tool, "HealingBrush"));
                    window.ShowUtility(); shown = true; window.position = new Rect(80, 80, 1050, 720);
                    await Task.Delay(150); Call(window, "RefreshToolkitInterface", false);
                    Call(window, "SetTiledPreview", true);
                    var settings = Get(window, "paintSettings");
                    Set(settings, "healingSize", 64f); Set(settings, "healingHardness", .8f); Set(settings, "healingSearch", 64);
                    Set(settings, "healingTransparentOnly", false);
                    Set(settings, "healingQuality", Enum.ToObject(settings.GetType().GetField("healingQuality").FieldType, 1));
                    Set(settings, "healingSample", Enum.ToObject(settings.GetType().GetField("healingSample").FieldType, 0));
                    async Task Stroke(bool vertical)
                    {
                        Call(window, "CancelHealing");
                        var selection = Call(window, "GetAreaSelection");
                        Set(window, "healingDocument", document); Set(window, "healingLayer", drawing);
                        Set(window, "healingCanvasSize", new Vector2Int(N, N));
                        Set(window, "healingSelectionRevision", (int)selection.GetType().GetProperty("Revision", F).GetValue(selection));
                        var transform = (TextureTransform)Call(document, "GetCanvasTransform", (Layer)drawing);
                        Set(window, "healingTransform", transform.ToMatrix(N, N));
                        Call(window, "BeginHealingStroke", vertical ? new Vector2(0, -8) : new Vector2(-8, 0));
                        Call(Get(window, "healingStroke"), "Add", vertical ? new Vector2(0, N + 8) : new Vector2(N + 8, 0));
                        if (vertical)
                        {
                            // Capture the exact periodic crop for isolated worker A/B experiments.
                            var stroke = Get(window, "healingStroke");
                            var bounds = (RectInt)Call(stroke, "Bounds", 64);
                            var maskTexture = (RenderTexture)stroke.GetType().GetProperty("Texture", F).GetValue(stroke);
                            var read = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
                            var active = RenderTexture.active;
                            try
                            {
                                RenderTexture.active = maskTexture; read.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                                var maskPixels = read.GetPixels();
                                using var writer = new BinaryWriter(File.Create("Temp/WhimTex/NoiseSeams/" + (fractal ? "fbm" : "smooth") + ".bin"));
                                writer.Write(bounds.x); writer.Write(bounds.y); writer.Write(bounds.width); writer.Write(bounds.height);
                                foreach (var p in original) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
                                for (int y = 0; y < bounds.height; y++) for (int x = 0; x < bounds.width; x++)
                                    writer.Write((byte)Mathf.RoundToInt(maskPixels[((bounds.y + y) % N) * N + (bounds.x + x) % N].a * 255));
                            }
                            finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(read); }
                        }
                        var timer = Stopwatch.StartNew(); Call(window, "StartHealing");
                        if (Get(window, "healingJob") == null) throw new Exception("Healing did not start");
                        while (Get(window, "healingJob") != null && timer.ElapsedMilliseconds < 25000)
                        { await Task.Delay(20); Call(window, "UpdateHealing"); }
                        if (Get(window, "healingJob") != null) throw new Exception("Healing timeout");
                        report.AppendLine((vertical ? "vertical" : "horizontal") + " stroke " + timer.ElapsedMilliseconds + "ms (includes UI/readback/commit and polling)");
                    }
                    await Stroke(true); var verticalResult = Pixels(drawing).GetPixels();
                    await Stroke(false); var both = Pixels(drawing).GetPixels();
                    string name = fractal ? "perlin-fbm" : "perlin-smooth";
                    Preview(name, original, verticalResult, both);
                    var before = Metrics(original); var after = Metrics(verticalResult);
                    report.AppendLine(name + " before: " + Format(before));
                    report.AppendLine(name + " vertical: " + Format(after));
                    // These gates describe this fixed fixture, not a universal quality score.
                    // Curvature alone would reward a flat fill; also retain tonal variation and remove the seam.
                    if (after.curvature > before.curvature * (fractal ? 1.25 : 1.4)) throw new Exception("Patch-switch roughness regression: " + name);
                    if (after.seam > before.seam * .5) throw new Exception("Tiled seam remains: " + name);
                    // The pre-blend best-donor worker already reduces contrast on this seam.
                    // Guard against additional loss, not an unsupported claim of source-level contrast.
                    double bestDonorDeviation = fractal ? .060118 : .086895;
                    if (after.deviation < bestDonorDeviation * .9 || after.deviation > bestDonorDeviation * 1.1)
                        throw new Exception("Tonal variation lost or exaggerated: " + name);
                    // Compared with the immediate pre-transfer worker (selective final blend).
                    // Require a modest contrast gain as well as the existing roughness/seam bounds.
                    double coarseColorDeviation = fractal ? .059922 : .086862;
                    if (after.deviation < coarseColorDeviation * (fractal ? 1.04 : 1.02))
                        throw new Exception("Fine-level contrast transfer regression: " + name);
                    for (int i = 0; i < both.Length; i++)
                        if (!float.IsFinite(both[i].r) || both[i].a < .999f) throw new Exception("Invalid healed pixel");
                }
                finally
                {
                    if (window != null) { Call(window, "CancelHealing"); window.DiscardChanges(); if (shown) window.Close(); else UnityEngine.Object.DestroyImmediate(window); }
                    if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
                    if (source != null) { Undo.ClearUndo(source); UnityEngine.Object.DestroyImmediate(source); }
                }
            }
        }
        finally
        {
            if (hadPref) EditorPrefs.SetString(pref, oldPref); else EditorPrefs.DeleteKey(pref);
            if (focused != null) focused.Focus();
        }
        File.WriteAllText("Temp/WhimTex/NoiseSeams/report.txt", report.ToString());
        return report.ToString();
    }
}
