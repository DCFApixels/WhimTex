using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using Object = UnityEngine.Object;

public sealed class ShaderFXWorkflowWindow : EditorWindow { }

public static partial class ShaderFXRegionalTests
{
    public static string WorkflowStart(string runId, int side) =>
        AsyncD.Start(runId, (test, token) => Workflow(test, token, runId, side));
    public static string WorkflowPoll(string runId) => AsyncD.Poll(runId);
    public static Task<string> WorkflowCancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> WorkflowCleanup(string runId) => AsyncD.Cleanup(runId);
    static object Get(object obj, string name) =>
        obj.GetType().GetField(name, F)?.GetValue(obj) ?? obj.GetType().GetProperty(name, F)?.GetValue(obj);

    [Serializable] sealed class WorkflowFrame
    {
        public int cycle, phase, frame, step, regionWidth, regionHeight, inspectorCaptures, draws;
        public int brushBeforeWidth, brushAfterWidth, cacheCount;
        public long cacheBytes, brushBeforeBytes, brushAfterBytes, inspectorBrushBytes, graphicsDriverBytes;
        public double renderMs, inspectorMs;
        public bool brushReplaced;
    }
    [Serializable] sealed class WorkflowReport
    {
        public string device, unity, note;
        public int side, brush;
        public List<WorkflowFrame> frames = new List<WorkflowFrame>();
    }

    static async Task Workflow(TestContext t, CancellationToken token, string runId, int side)
    {
        using var f = new Fixture(); f.doc.width = f.doc.height = side;
        var drawing = new DrawingLayerBehaviour();
        var drawingLayer = new Layer(drawing) { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var processor = new Layer(new ShaderProcessorLayerBehaviour());
        var fx = f.Fx(@"// @preview(Show)
// @preview(Far)
float4 ApplyFX(PinnedUV uv,float4 color){return SampleInput(uv)*float4(.6,.7,.8,1);}
#ifdef WHIMTEX_PREVIEW
float4 Show(float2 uv){return SampleInput(uv);}
float4 Far(float2 uv){return SampleInput(float2(.03,.94));}
#endif");
        processor.fx.Add(fx);
        var background = new Layer(new ColorFillLayerBehaviour());
        background.fx.Add(f.Fx("float4 ApplyFX(PinnedUV uv,float4 color){return float4(uv.x*2-.2,uv.y*.7,.15,.7);}"));
        var group = new Layer(new GroupLayerBehaviour());
        group.children.Add(processor); group.children.Add(drawingLayer);
        f.doc.layers.Add(group); f.doc.layers.Add(background); Call(f.doc, "NormalizeModel");
        var mask = f.Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp });
        mask.SetPixels(new[] { Color.black, Color.white, Color.gray, Color.white }); mask.Apply();
        var settings = Activator.CreateInstance(T("PaintToolSettings"), true);
        settings.GetType().GetField("brushSize", F).SetValue(settings, side / 4f);
        settings.GetType().GetField("brushColor", F).SetValue(settings, new Color(2.3f, -.13f, .7f, .025f));
        var dynamics = Get(settings, "dynamics");
        dynamics.GetType().GetField("opacity", F).SetValue(dynamics, .47f);
        dynamics.GetType().GetField("flow", F).SetValue(dynamics, .35f);
        var parameters = Call(Call(settings, "GetStrokeParameters", false, (Color?)null), "WithSelectionMask", mask);
        var report = new WorkflowReport { side = side, brush = side / 4, unity = Application.unityVersion,
            device = SystemInfo.graphicsDeviceName,
            note = "Owned document and UI Toolkit previews; no OS input. Actual preview methods are ticked once per test frame with their production 100 ms gate. Native scheduling is paused only on these owned surfaces to include preview work in timings. Each stage includes a 1x1 GPU fence. Strokes are cancelled, not committed; Undo/finalization and subjective UI smoothness are outside scope. Driver bytes are Editor-wide, not this document's VRAM." };
        string directory = Path.GetFullPath("Temp/WhimTex/fx-workflow/" + runId);
        Directory.CreateDirectory(directory);
        ShaderFXWorkflowWindow window = null;
        var previews = new List<Image>();
        var fence = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = f.Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
        RenderTexture displayed = null;
        int captures = 0; string captureFailure = null;
        using var capture = f.Capture((effect, owner, ctx, mat, input) =>
        {
            if (effect != fx || !(bool)Get(f.doc, "CapturingInspectorFX")) return;
            captures++;
            if (input.width != Math.Min(512, side) || input.height != Math.Min(512, side))
                captureFailure = "Inspector received a crop or an oversized input: " + input.width + "x" + input.height;
        });
        void Fence(Texture texture)
        {
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; Graphics.Blit(texture, fence); RenderTexture.active = fence;
                cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        try
        {
            window = ScriptableObject.CreateInstance<ShaderFXWorkflowWindow>();
            window.titleContent = new GUIContent("WhimTex · automatic FX test");
            window.position = new Rect(100, 100, 680, 640);
            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.style.height = 360; window.rootVisualElement.Add(image);
            var inspector = new VisualElement(); window.rootVisualElement.Add(inspector);
            foreach (string function in new[] { "Show", "Far" })
            {
                var preview = (Image)Activator.CreateInstance(T("ShaderFXPreviewElement"), F, null,
                    new object[] { fx, null, new ShaderFXParameterControl { previewFunction = function }, false, f.doc, processor, 0 }, null);
                preview.style.height = 100; preview.style.flexShrink = 0;
                inspector.Add(preview); previews.Add(preview);
            }
            window.ShowUtility();
            for (int i = 0; i < 3; i++) await AsyncD.Tick(window.rootVisualElement, token);
            foreach (var preview in previews)
            {
                t.True(preview.panel != null && preview.contentRect.width > 0, "Real preview is attached and laid out");
                ((IVisualElementScheduledItem)Get(preview, "update")).Pause();
                Call(Get(preview, "cache"), "Detach", preview);
                t.True(preview.image != null, "Preview initially rendered: " + preview.tooltip);
            }
            for (int cycle = 0; cycle < 2; cycle++)
            for (int phase = 0; phase < 4; phase++)
            {
                bool paint = phase >= 2, visible = phase % 2 != 0;
                drawingLayer.enabled = paint;
                inspector.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                await AsyncD.Tick(window.rootVisualElement, token);
                if (paint) Call(drawing, "BeginStroke", new Vector2(.5f, .5f));
                int firstDraws = (int)Get(previews[0], "DrawCount");
                double began = EditorApplication.timeSinceStartup;
                try
                {
                    for (int i = 0; i < 24; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        t.True(window != null, "Owned window remains available");
                        int step = 1 << (i / 6 % 4);
                        int width = Math.Min(side, 1024 * step), height = Math.Min(side, 768 * step);
                        int x = (int)((side - width) * (.5 + .45 * Math.Sin(i * .37))) / step * step;
                        int y = (int)((side - height) * (.5 + .45 * Math.Cos(i * .29))) / step * step;
                        var rect = new RectInt(x, y, width, height);
                        var row = new WorkflowFrame { cycle = cycle, phase = phase, frame = i, step = step,
                            regionWidth = width / step, regionHeight = height / step };
                        var watch = Stopwatch.StartNew();
                        if (paint) Call(drawing, "PaintPoint", new Vector2(
                            .5f + .4f * Mathf.Sin(i * .37f), .5f + .4f * Mathf.Sin(i * .71f)), side, side, parameters);
                        var output = (RenderTexture)Call(f.doc, "RenderCanvasViewport", Region(rect, step), f.cache, paint, paint ? drawing : null);
                        image.image = output;
                        if (displayed != null) RenderTexture.ReleaseTemporary(displayed);
                        displayed = output;
                        Fence(output); row.renderMs = watch.Elapsed.TotalMilliseconds;
                        t.Equal(width / step, output.width, "Canvas uses planned region width");
                        var before = Get(drawing, "brushPreviewSurface") as RenderTexture;
                        row.brushBeforeWidth = before != null ? before.width : 0;
                        row.brushBeforeBytes = Payload(before);
                        int previousCaptures = captures;
                        watch.Restart();
                        foreach (var preview in previews) Call(preview, "UpdatePreview");
                        if (visible) Fence(previews[0].image);
                        row.inspectorMs = watch.Elapsed.TotalMilliseconds;
                        row.inspectorCaptures = captures - previousCaptures;
                        var after = Get(drawing, "brushPreviewSurface") as RenderTexture;
                        row.brushAfterWidth = after != null ? after.width : 0;
                        row.brushAfterBytes = Payload(after);
                        row.brushReplaced = before != after;
                        row.inspectorBrushBytes = Payload(Get(drawing, "brushInspectorSurface") as RenderTexture);
                        t.True(row.inspectorBrushBytes <= 512L * 512 * 16, "Inspector brush cache stays bounded");
                        t.True(!row.brushReplaced && row.brushBeforeBytes == row.brushAfterBytes,
                            "Inspector leaves the canvas brush buffer intact");
                        row.cacheBytes = (long)Get(f.cache, "Bytes"); row.cacheCount = (int)Get(f.cache, "Count");
                        row.draws = (int)Get(previews[0], "DrawCount");
                        row.graphicsDriverBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
                        report.frames.Add(row);
                        t.True(captureFailure == null, captureFailure ?? "Inspector has a complete bounded input");
                        t.True(row.cacheBytes <= (long)Get(f.cache, "BudgetBytes"), "Effect cache stays within its budget");
                        t.True(row.cacheCount <= 24, "Panning replaces entries rather than accumulating every view");
                        if (!visible) t.Equal(firstDraws, row.draws, "Hidden previews do not render");
                        if (i == 5 || i == 23) CheckWorkflowCache(t, f, rect, step, paint ? drawing : null, output);
                        window.Repaint();
                        await AsyncD.Tick(window.rootVisualElement, token);
                    }
                    int draws = (int)Get(previews[0], "DrawCount") - firstDraws;
                    if (visible) t.True(draws > 0 && draws <= Math.Ceiling((EditorApplication.timeSinceStartup - began) * 10) + 2,
                        "Visible preview refresh is bounded to 10 Hz: " + draws);
                }
                finally { if (paint) Call(drawing, "CancelStroke"); }
            }
            foreach (var preview in previews)
            {
                preview.RemoveFromHierarchy();
                t.True(preview.image == null && Get(preview, "result") == null, "Detached preview releases its texture");
            }
            f.cache.Dispose(); t.Equal(0L, (long)Get(f.cache, "Bytes"), "Disposed effect cache retains no texture bytes");
            Call(drawing, "ReleaseTransientResources");
            t.True(Get(drawing, "brushPreviewSurface") == null && Get(drawing, "brushInspectorSurface") == null && Get(drawing, "paintSurface") == null, "Drawing transient fields released");
        }
        finally
        {
            AsyncD.CleanupOwned(
                () =>
                {
                    string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
                        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
                    File.WriteAllText(Path.Combine(directory, "report.json"), json);
                    if (!json.Contains("\"frames\":[")) throw new InvalidOperationException("Per-update metrics were not serialized.");
                },
                () => { if (window != null) window.Close(); },
                () => { if (displayed != null) RenderTexture.ReleaseTemporary(displayed); },
                () => RenderTexture.ReleaseTemporary(fence),
                () => Call(drawing, "CancelStroke"),
                () => Call(drawing, "ReleaseTransientResources"),
                () => Call(settings, "ReleasePresetTip"));
        }
    }

    static long Payload(RenderTexture texture) => texture == null ? 0 :
        (long)texture.width * texture.height * (texture.format == RenderTextureFormat.ARGBFloat ? 16 : 8);

    static void CheckWorkflowCache(TestContext t, Fixture f, RectInt rect, int step, DrawingLayerBehaviour painting, RenderTexture cached)
    {
        using var cleanCache = (IDisposable)Activator.CreateInstance(T("EffectRenderCache"), true);
        var fresh = (RenderTexture)Call(f.doc, "RenderCanvasViewport", Region(rect, step), cleanCache, painting != null, painting);
        var sample = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(cached, sample); var a = f.Read(sample);
            Graphics.Blit(fresh, sample); var b = f.Read(sample);
            float error = 0;
            for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++)
                error = Mathf.Max(error, Mathf.Abs(a[i][c] - b[i][c]));
            t.True(error < .006f, "Retained cache agrees with fresh rendering after pan/zoom/strokes: " + error);
        }
        finally { RenderTexture.ReleaseTemporary(sample); RenderTexture.ReleaseTemporary(fresh); }
    }
}
