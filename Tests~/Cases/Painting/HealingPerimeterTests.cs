using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

// run_script HealingPerimeterTests.Main. Reproduction with bounded regression gates.
// Own transient Noise/Drawing document, actual tiled brush mask/readback/worker/commit.
// Reflection targets WhimTex only. Saves generated comparison images in Temp, never Assets.
public static class HealingPerimeterTests
{
    const int N = 512;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object o, string name, params object[] args) => (o as Type ?? o.GetType()).GetMethod(name, F).Invoke(o is Type ? null : o, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static Texture2D Pixels(DrawingLayerBehaviour d) => (Texture2D)d.GetType().GetProperty("StoredTexture", F).GetValue(d);
    static void Preview(string name, params Color[][] columns)
    {
        var image = UnityBRun.Track(new Texture2D(N * columns.Length, N, TextureFormat.RGBA32, false));
        try
        {
            var display = new Color[N * N * columns.Length];
            for (int c = 0; c < columns.Length; c++) for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                display[y * N * columns.Length + c * N + x] = columns[c][((y + N / 2) % N) * N + (x + N / 2) % N].gamma;
            image.SetPixels(display); image.Apply();
            File.WriteAllBytes(UnityBRun.EvidencePath("NoiseSeams/" + name + ".png"), image.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    static async Task<string> ExecuteMain()
    {
        UnityBHealing.RequireIdle();
        var focused = EditorWindow.focusedWindow;
        const string pref = "DCFApixels.WhimTex.Canvas.Tool";
        bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        var report = new StringBuilder("512x512; scale 8; seed 1337; size 64; hardness .8; search 64; Balanced; Current Layer.\n");
        try
        {
            foreach (bool fractal in new[] { true })
            {
                TextureCompositorWindow window = null; TextureCompositor document = null; Texture2D source = null;
                bool shown = false;
                try
                {
                    window = UnityBRun.Create<TextureCompositorWindow>();
                    document = (TextureCompositor)Get(window, "compositor"); document.width = document.height = N;
                    var noise = new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.Perlin,
                        fractal = fractal ? NoiseLayerBehaviour.FractalType.FBm : NoiseLayerBehaviour.FractalType.None, scale = 8, seed = 1337 };
                    document.layers.Add(noise); Call(document, "NormalizeModel");
                    var previous = RenderTexture.active;
                    var rendered = (RenderTexture)Call(document, "RenderLayerPreview", noise.Owner, N);
                    try
                    {
                        source = UnityBRun.Track(new Texture2D(N, N, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave });
                        RenderTexture.active = rendered; source.ReadPixels(new Rect(0, 0, N, N), 0, 0); source.Apply();
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rendered); }
                    var original = source.GetPixels();
                    var drawing = (DrawingLayerBehaviour)Call(typeof(DrawingLayerBehaviour), "FromMergedTexture", source);
                    Call(noise, "ReleaseTransientResources");
                    document.layers.Clear(); document.layers.Add(drawing); Call(document, "NormalizeModel");
                    Call(window, "SelectOnlyLayer", drawing.Id);
                    var tool = typeof(TextureCompositorWindow).GetNestedType("CanvasTool", F);
                    Call(window, "ChangeCanvasTool", Enum.Parse(tool, "HealingBrush"));
                    window.ShowUtility(); shown = true; window.position = new Rect(80, 80, 1050, 720);
                    await UnityBRun.Delay(150); Call(window, "RefreshToolkitInterface", false);
                    Call(window, "SetTiledCanvas", true);
                    var settings = Get(window, "paintSettings");
                    Set(settings, "healingSize", 64f); Set(settings, "healingHardness", .8f); Set(settings, "healingSearch", 64);
                    Set(settings, "healingTransparentOnly", false);
                    Set(settings, "healingQuality", Enum.ToObject(settings.GetType().GetField("healingQuality").FieldType, 1));
                    Set(settings, "healingSample", Enum.ToObject(settings.GetType().GetField("healingSample").FieldType, 0));
                    async Task Stroke()
                    {
                        Call(window, "CancelHealing");
                        var selection = Call(window, "GetAreaSelection");
                        Set(window, "healingDocument", document); Set(window, "healingLayer", drawing);
                        Set(window, "healingCanvasSize", new Vector2Int(N, N));
                        Set(window, "healingSelectionRevision", (int)selection.GetType().GetProperty("Revision", F).GetValue(selection));
                        var transform = (TextureTransform)Call(document, "GetCanvasTransform", (Layer)drawing);
                        Set(window, "healingTransform", transform.ToMatrix(N, N));
                        Call(window, "BeginHealingStroke", Vector2.zero);
                        foreach (var point in new[] { new Vector2(N, 0), new Vector2(N, N), new Vector2(0, N), Vector2.zero })
                            Call(Get(window, "healingStroke"), "Add", point);
                        var timer = Stopwatch.StartNew(); Call(window, "StartHealing");
                        UnityBRun.Check(!(Get(window, "healingJob") == null), "Healing did not start");
                        while (Get(window, "healingJob") != null && timer.ElapsedMilliseconds < 25000)
                        { await UnityBRun.Delay(20); Call(window, "UpdateHealing"); }
                        UnityBRun.Check(!(Get(window, "healingJob") != null), "Healing timeout");
                        report.AppendLine("perimeter stroke " + timer.ElapsedMilliseconds + "ms (includes UI/readback/commit and polling)");
                    }
                    for (int attempt=0;attempt<4;attempt++)
                    {
                        var before=Pixels(drawing).GetPixels();
                        await Stroke();
                        var after=Pixels(drawing).GetPixels();
                        int changed=0;for(int i=0;i<before.Length;i++)if(!before[i].Equals(after[i]))changed++;
                        report.AppendLine("Attempt "+attempt+": changed pixels "+changed);
                        UnityBRun.Check(!(changed==0), "Perimeter result not applied: "+report);
                        double seamX = 0, seamY = 0, originalX = 0, originalY = 0;
                        for (int i = 0; i < N; i++)
                        {
                            seamX += Math.Abs(after[i*N].r-after[i*N+N-1].r);
                            seamY += Math.Abs(after[i].r-after[(N-1)*N+i].r);
                            originalX += Math.Abs(original[i*N].r-original[i*N+N-1].r);
                            originalY += Math.Abs(original[i].r-original[(N-1)*N+i].r);
                        }
                        report.AppendLine($"Seams X/Y {seamX/N:F6} / {seamY/N:F6}");
                        UnityBRun.Check(!(seamX > originalX*.4 || seamY > originalY*.4), "Perimeter seam regression");
                        for (int y=0;y<N;y++) for (int x=0;x<N;x++)
                        {
                            Color p=after[y*N+x];
                            UnityBRun.Check(!(!float.IsFinite(p.r) || !float.IsFinite(p.g) || !float.IsFinite(p.b) || p.a < .999f), "Invalid repaired pixel");
                            UnityBRun.Check(!(x>33 && y>33 && x<N-33 && y<N-33 && !p.Equals(before[y*N+x])), "Pixels outside stroke changed");
                        }
                        if(attempt==0)Preview("perimeter-512-recentered",original,after);
                    }
                }
                finally
                {
                    if (window != null) { await UnityBHealing.StopAndDrain(window); UnityBRun.CloseOwned(window); }
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
        File.WriteAllText(UnityBRun.EvidencePath("NoiseSeams/perimeter-report.txt"), report.ToString());
        return report.ToString();
    }
    public static string Main(string runId) => UnityBRun.Start(runId, "HealingPerimeterSmoke.Main", async () => { await ExecuteMain(); });
    public static string Poll(string runId) => UnityBRun.Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => UnityBRun.Cancel(runId);
    public static string Cleanup(string runId) => UnityBRun.Cleanup(runId);
}
