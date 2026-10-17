using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Opt-in, detached GPU benchmark. No windows, input, assets, preferences or user documents.
// Old orchestration follows e5486ca; both paths share the audited current stamp renderer.
public static class BrushPipelineBenchmark
{
    const string Package = "Packages/com.dcfapixels.whimtex/";
    const string Baseline = "Tests~/Framework/Painting/BrushBaseline/";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Updates = 16, Repetitions = 9;
    static readonly Assembly Product = typeof(DrawingLayerBehaviour).Assembly;
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    static object New(string name) => Activator.CreateInstance(Product.GetType("DCFApixels.WhimTex." + name, true), true);
    static string Read(string path) => File.ReadAllText(Package + path).Replace("\r\n", "\n");
    static string Hash(string text)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    // Ephemeral Pipeline DTOs are not registered with Unity's native serializer.
    // Use the public managed JSON API, resolved to avoid duplicate compiler references.
    static readonly Type Json = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
    static T Decode<T>(string text) => (T)Json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { text, typeof(T) });
    static string Encode(object value) => (string)Json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    [Serializable] public sealed class Source { public string path, sha256; public bool rendererOnly; }
    [Serializable] public sealed class Provenance { public string commit; public Source[] files; }
    [Serializable] public sealed class Stats { public double median, p95, minimum, maximum; }
    [Serializable] public sealed class TextureMemory
    {
        public string role, format;
        public int width, height, depth, samples, mipCount;
        public long bytes;
    }
    [Serializable] public sealed class Sample
    {
        public double strokeMs, setupMs, drawMs, previewMs, commitMs, barrierMs;
        public long managedBytes, activeTextureBytes, activeRtPayloadBytes, peakRtPayloadBytes, commitScratchBytes;
        public long graphicsDriverBytes;
        public int stamps;
        public TextureMemory[] textures;
    }
    [Serializable] public sealed class TraceEvent
    {
        public string stage;
        public int stroke, update, stamps, gc0, gc1, gc2;
        public long startTicks, endTicks;
        public double submitMs, waitMs, fenceBlitMs, fenceReadMs;
        public long[] nativeNanoseconds;
    }
    [Serializable] public sealed class TraceResult
    {
        public string utc, gpu, api, unity, scenario, timing, control;
        public int side, diameter;
        public bool paired, fenced;
        public long driverBefore, driverAfter;
        public long stopwatchFrequency = Stopwatch.Frequency;
        public TraceEvent[] events;
        public TraceEvent[] warmupEvents;
        public string[] nativeMarkers = NativeMarkers;
    }
    static readonly string[] NativeMarkers = { "Gfx.CreateTexture", "Gfx.UploadTextureData", "Gfx.Reserve",
        "GfxTask_Execute", "GfxTask_ReadValue", "GC.Collect" };
    [Serializable] public sealed class Result
    {
        public string utc, gpu, graphicsApi, unity, colorSpace, scenario, baselineCommit, comparison, timing, memory;
        public int canvas, diameter, updates, vramMB, ramMB;
        public double maxProbeDifference;
        public Stats oldStroke, newStroke, oldCommit, newCommit;
        public Sample[] oldSamples, newSamples, oldCommitSamples, newCommitSamples;
        public Sample oldPhases, newPhases;
        public Provenance provenance;
    }
    static Stats Summary(Sample[] values, bool commit = false)
    {
        var sorted = new double[values.Length]; for (int i = 0; i < values.Length; i++) sorted[i] = commit ? values[i].commitMs : values[i].strokeMs;
        Array.Sort(sorted); double at = .95 * (sorted.Length - 1); int lo = (int)at;
        return new Stats { median = sorted[sorted.Length / 2], p95 = sorted[lo] + (sorted[Math.Min(lo + 1, sorted.Length - 1)] - sorted[lo]) * (at - lo), minimum = sorted[0], maximum = sorted[sorted.Length - 1] };
    }
    // Compile adapters once, outside timings; avoid MethodInfo.Invoke allocations per update.
    static Action<object, object[]> Bind(Type type, string name, bool isStatic = false)
    {
        var method = type.GetMethod(name, F | (isStatic ? BindingFlags.Static : 0));
        var target = Expression.Parameter(typeof(object)); var arguments = Expression.Parameter(typeof(object[]));
        var parameters = method.GetParameters(); var converted = new Expression[parameters.Length];
        for (int i = 0; i < converted.Length; i++) converted[i] = Expression.Convert(Expression.ArrayIndex(arguments, Expression.Constant(i)), parameters[i].ParameterType);
        var call = Expression.Call(isStatic ? null : Expression.Convert(target, type), method, converted);
        return Expression.Lambda<Action<object, object[]>>(call, target, arguments).Compile();
    }
    sealed class Harness : IDisposable
    {
        public readonly DrawingLayerBehaviour layer = new DrawingLayerBehaviour();
        readonly object settings, dynamics, parameters;
        readonly object[][] batches;
        readonly Action<object, object[]> drawCurrent, drawOld, beginCurrent, previewCurrent, end, protect, configureProtection;
        readonly Action<object, object[]> configureComposite, releaseStroke;
        readonly bool fullAssembly;
        readonly object transform;
        readonly object[] beginArgs, previewArgs;
        readonly Material oldBlend, oldConversion;
        readonly Shader oldBlendShader, oldConversionShader;
        readonly int side, diameter;
        readonly bool buffered, stampBlend, pressureOpacity, hdr, locked;
        readonly float opacity;
        readonly BlendMode blend;
        RenderTexture surface;
        readonly RenderTexture preview, fenceTarget, rasterSurface, computeSurface;
        readonly bool computeComparison;
        readonly Texture2D selection, fence;
        readonly Texture2D stored;
        RenderTexture oldBase, oldStroke;
        readonly Color initial;
        public int stampCount;
        public Color[] oldProbes, newProbes;
        public bool ComputeEnabled => surface.enableRandomWrite;
        public Harness(int side, int diameter, string scenario, bool fullAssembly = false, bool computeComparison = false, bool diagnostic = false)
        {
            try
            {
            this.side = side; this.diameter = diameter;
            this.fullAssembly = fullAssembly;
            this.computeComparison = computeComparison;
            hdr = scenario == "hdr"; locked = scenario == "locked";
            stampBlend = scenario == "stamp"; pressureOpacity = scenario == "pressure";
            blend = scenario == "blend" || stampBlend ? BlendMode.Multiply : BlendMode.Normal;
            opacity = scenario == "opacity" || scenario == "blend" || stampBlend || pressureOpacity ? .4f : 1f;
            buffered = opacity < 1f || pressureOpacity || blend != BlendMode.Normal;
            layer.colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard;
            layer.blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard;
            initial = hdr ? new Color(.9f, -.06f, .18f, .6f) : new Color(.12f, .24f, .36f, .6f);
            settings = New("PaintToolSettings"); dynamics = Get(settings, "dynamics");
            Set(settings, "brushSize", (float)diameter); Set(settings, "brushHardness", .65f); Set(settings, "brushSpacing", .15f);
            Set(settings, "brushColor", hdr ? new Color(1.5f, -.2f, .7f, .8f) : new Color(.8f, .5f, .2f, .8f));
            Set(dynamics, "flow", .35f); Set(dynamics, "opacity", opacity); Set(dynamics, "blend", blend);
            Set(dynamics, "blendApplication", Enum.Parse(Get(dynamics, "blendApplication").GetType(), stampBlend ? "Stamp" : "Stroke"));
            Set(dynamics, "writeChannels", locked ? 7 : 15);
            Set(Get(dynamics, "pressureFlow"), "enabled", !pressureOpacity);
            Set(Get(dynamics, "pressureOpacity"), "enabled", pressureOpacity);
            if (scenario == "selection")
            {
                selection = new Texture2D(side, side, TextureFormat.R8, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var bytes = new byte[side * side]; Array.Fill(bytes, (byte)89);
                selection.LoadRawTextureData(bytes); selection.Apply(false, false);
            }
            parameters = Call(settings, "GetStrokeParameters", false, (Color?)null);
            if (selection != null) parameters = Call(parameters, "WithSelectionMask", selection);
            if (pressureOpacity) parameters = Call(parameters, "WithPressure", .5f);
            dynamics = Get(parameters, "Dynamics");
            transform = Get(layer.Owner, "PixelCanvasTransform");
            stored = new Texture2D(side, side, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr)
                { hideFlags = HideFlags.HideAndDontSave };
            Color straight = new Color(initial.r / initial.a, initial.g / initial.a, initial.b / initial.a, initial.a);
            if (hdr)
            {
                var data = stored.GetRawTextureData<ushort>();
                ushort r = Mathf.FloatToHalf(straight.r), g = Mathf.FloatToHalf(straight.g), b = Mathf.FloatToHalf(straight.b), a = Mathf.FloatToHalf(straight.a);
                for (int i = 0; i < data.Length; i += 4) { data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = a; }
            }
            else
            {
                var data = stored.GetRawTextureData<Color32>(); Color32 encoded = straight.gamma;
                for (int i = 0; i < data.Length; i++) data[i] = encoded;
            }
            stored.Apply(false, false); Call(layer, "AdoptStoredTexture", stored);
            surface = (RenderTexture)Call(layer, "EnsurePaintSurface", side, side); Clear();
            if (computeComparison)
            {
                if (!surface.enableRandomWrite) throw new InvalidOperationException("Compute surface is unavailable.");
                computeSurface = surface;
                var descriptor = surface.descriptor; descriptor.enableRandomWrite = false;
                rasterSurface = new RenderTexture(descriptor) { hideFlags = HideFlags.HideAndDontSave,
                    filterMode = surface.filterMode, wrapMode = surface.wrapMode };
                if (!rasterSurface.Create()) throw new InvalidOperationException("Raster control surface is unavailable.");
            }
            preview = Rt(); fence = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            fenceTarget = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            if (!computeComparison && !diagnostic)
            {
                oldBlendShader = fullAssembly ? BrushFullAssemblyReference.Compile("Blend.shader") : Compile("Blend.shader");
                oldConversionShader = fullAssembly ? BrushFullAssemblyReference.Compile("AlphaConversion.shader") : Compile("AlphaConversion.shader");
                oldBlend = new Material(oldBlendShader) { hideFlags = HideFlags.HideAndDontSave };
                oldConversion = new Material(oldConversionShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var type = typeof(DrawingLayerBehaviour);
            drawCurrent = Bind(type, "DrawBrushStamps"); beginCurrent = Bind(type, "EnsureAdvancedStroke");
            previewCurrent = Bind(type, "BlitStraightSurface"); end = Bind(type, "EndStroke");
            configureComposite = Bind(type, "ConfigureBrushComposite"); releaseStroke = Bind(type, "ReleaseAdvancedStroke");
            protect = Bind(type, "ApplyPaintWriteProtection"); configureProtection = Bind(type, "ConfigureStrokeWriteProtection");
            drawOld = Bind(type.GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic), "Draw", true);
            beginArgs = new object[] { surface, stampBlend, pressureOpacity }; previewArgs = new object[] { preview, 1f };
            batches = new object[Updates][];
            var listField = type.GetField("segmentStamps", F); var from = new Vector2(.15f, .4f);
            Call(layer, "BeginStroke", from);
            for (int i = 0; i < Updates; i++)
            {
                var to = new Vector2(.15f + .7f * i / (Updates - 1), .4f + .2f * i / (Updates - 1));
                var stamps = (IList)Activator.CreateInstance(listField.FieldType); listField.SetValue(layer, stamps);
                Call(layer, "BuildBrushSegment", from, to, side, side, i == 0, parameters, (to - from).magnitude * side, 8192);
                batches[i] = new object[] { surface, stamps, side, side, parameters };
                stampCount += stamps.Count; from = to;
            }
            Call(layer, "EndStroke");
            }
            catch { Dispose(); throw; }
        }
        Shader Compile(string file)
        {
            string source = Read(Baseline + file).Replace("Hidden/WhimTex/", "Hidden/WhimTex/BenchmarkBaseline/")
                .Replace("\"HdrColor.cginc\"", "\"" + Package + "src/Shaders/HdrColor.cginc\"")
                .Replace("\"ColorBlend.cginc\"", "\"" + Package + "src/Shaders/ColorBlend.cginc\"");
            var shader = ShaderUtil.CreateShaderAsset(source, true);
            if (shader == null) throw new InvalidOperationException("Cannot compile baseline shader.");
            shader.hideFlags = HideFlags.HideAndDontSave;
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { Object.DestroyImmediate(shader); throw new InvalidOperationException(message.message); }
            return shader;
        }
        RenderTexture Rt()
        { var rt = RenderTexture.GetTemporary(side, side, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear); rt.filterMode = FilterMode.Bilinear; rt.wrapMode = TextureWrapMode.Clamp; return rt; }
        void Clear() { RenderTexture.active = surface; GL.Clear(false, true, initial); }
        // ReadPixels can allocate a staging image matching the ENTIRE source RT.
        // Sample into a real 1x1 RT first, bounding every diagnostic readback.
        Color Probe(int x, int y)
        {
            long start = Stopwatch.GetTimestamp();
            Graphics.Blit(preview, fenceTarget, Vector2.zero, new Vector2((x + .5f) / side, (y + .5f) / side));
            RenderTexture.active = fenceTarget;
            long submitted = Stopwatch.GetTimestamp();
            fence.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            fenceBlitMs = (submitted - start) * 1000d / Stopwatch.Frequency;
            fenceReadMs = (Stopwatch.GetTimestamp() - submitted) * 1000d / Stopwatch.Frequency;
            return fence.GetPixel(0, 0);
        }
        bool asyncFence, graphicsFence;
        double fenceBlitMs, fenceReadMs;
        void Barrier()
        {
            if (graphicsFence)
            {
                long started = Stopwatch.GetTimestamp();
                var completion = Graphics.CreateGraphicsFence(UnityEngine.Rendering.GraphicsFenceType.AsyncQueueSynchronisation,
                    UnityEngine.Rendering.SynchronisationStageFlags.AllGPUOperations);
                GL.Flush();
                long queued = Stopwatch.GetTimestamp();
                while (!completion.passed)
                {
                    if ((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency > 2)
                        throw new TimeoutException("Graphics fence did not complete within two seconds; stop this diagnostic.");
                    System.Threading.Thread.Yield();
                }
                fenceBlitMs = (queued - started) * 1000d / Stopwatch.Frequency;
                fenceReadMs = (Stopwatch.GetTimestamp() - queued) * 1000d / Stopwatch.Frequency;
                return;
            }
            if (!asyncFence) { Probe(0, 0); return; }
            long start = Stopwatch.GetTimestamp();
            Graphics.Blit(preview, fenceTarget, Vector2.zero, new Vector2(.5f / side, .5f / side));
            long submitted = Stopwatch.GetTimestamp();
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(fenceTarget, 0);
            request.WaitForCompletion();
            fenceBlitMs = (submitted - start) * 1000d / Stopwatch.Frequency;
            fenceReadMs = (Stopwatch.GetTimestamp() - submitted) * 1000d / Stopwatch.Frequency;
            if (request.hasError) throw new InvalidOperationException("Bounded 1x1 readback failed.");
        }
        public void SelectCompute(bool enabled)
        {
            Call(layer, "CancelStroke");
            surface = enabled ? computeSurface : rasterSurface;
            Set(layer, "paintSurface", surface); beginArgs[0] = surface;
            foreach (var batch in batches) batch[0] = surface;
        }
        double Phase(Action action, bool measure)
        {
            if (!measure) { action(); return 0; }
            long start = Stopwatch.GetTimestamp(); action(); Barrier();
            return (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
        public TraceEvent[] Trace(bool fenced, string control)
        {
            const int count = 32, eventsPerStroke = 37;
            var events = new TraceEvent[count * eventsPerStroke];
            // Allocate the trace and delegates before measuring; no per-event logging or JSON.
            var nativeCapture = AppDomain.CurrentDomain.GetData("WhimTex.Tests.BrushNativeMarkers") as Dictionary<string, Unity.Profiling.ProfilerRecorder>;
            var nativeRecorders = new Unity.Profiling.ProfilerRecorder[NativeMarkers.Length];
            var nativeBefore = new long[NativeMarkers.Length];
            if (nativeCapture != null) for (int i = 0; i < nativeRecorders.Length; i++) nativeCapture.TryGetValue(NativeMarkers[i], out nativeRecorders[i]);
            for (int i = 0; i < events.Length; i++) events[i] = new TraceEvent { nativeNanoseconds = nativeCapture != null ? new long[NativeMarkers.Length] : null };
            Action prepare = () => { Call(layer, "CancelStroke"); Clear(); Graphics.Blit(surface, preview); Set(layer, "paintSurfaceDirty", false); };
            asyncFence = control == "async-fence";
            graphicsFence = control == "graphics-fence";
            Action begin = () => beginCurrent(layer, beginArgs);
            Action render = control == "no-preview" ? () => { } : () => previewCurrent(layer, previewArgs);
            Action finish = () => end(layer, Array.Empty<object>());
            var draw = new Action[Updates];
            using var replayCommands = new UnityEngine.Rendering.CommandBuffer();
            var rendererType = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic);
            Mesh replayMesh = (Mesh)rendererType.GetField("brushMesh", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Material replayMaterial = (Material)Product.GetType("DCFApixels.WhimTex.WhimTexMaterials")
                .GetProperty("PaintBrush", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            for (int i = 0; i < Updates; i++)
            {
                int at = i;
                draw[i] = control == "clear-only" ? () =>
                {
                    RenderTexture.active = (RenderTexture)Get(layer, "advancedStroke"); GL.Clear(false, true, Color.clear);
                } : control == "replay-mesh" || control == "replay-bound" ? () =>
                {
                    if (control == "replay-bound") replayMaterial.SetTexture("_StrokeBase", surface);
                    replayCommands.Clear();
                    replayCommands.SetRenderTarget((RenderTexture)Get(layer, "advancedStroke"));
                    replayCommands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 1));
                    replayCommands.DrawMesh(replayMesh, Matrix4x4.identity, replayMaterial, 0, 0);
                    Graphics.ExecuteCommandBuffer(replayCommands);
                    replayCommands.Clear();
                    if (control == "replay-bound") replayMaterial.SetTexture("_StrokeBase", null);
                } : () => drawCurrent(layer, batches[at]);
            }
            Action empty = () => { };
            int index = 0;
            for (int stroke = 0; stroke < count; stroke++)
            {
                Record("idleFence", -1, empty, true);
                Record("prepare", -1, prepare, true);
                Record("begin", -1, begin, fenced);
                for (int i = 0; i < Updates; i++)
                {
                    Record("draw", i, draw[i], fenced);
                    Record("preview", i, render, fenced);
                }
                Record("commit", -1, finish, fenced);
                Record("finishFence", -1, empty, true);

                void Record(string stage, int update, Action action, bool wait)
                {
                    var e = events[index++]; e.stage = stage; e.stroke = stroke; e.update = update;
                    e.stamps = update < 0 ? 0 : ((IList)batches[update][1]).Count;
                    int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                    if (nativeCapture != null) for (int n = 0; n < nativeRecorders.Length; n++) nativeBefore[n] = nativeRecorders[n].Valid ? nativeRecorders[n].CurrentValue : 0;
                    long start = Stopwatch.GetTimestamp(); action(); long submitted = Stopwatch.GetTimestamp();
                    if (wait) Barrier();
                    long finished = Stopwatch.GetTimestamp();
                    e.startTicks = start; e.endTicks = finished;
                    e.submitMs = (submitted - start) * 1000d / Stopwatch.Frequency;
                    e.waitMs = (finished - submitted) * 1000d / Stopwatch.Frequency;
                    e.fenceBlitMs = wait ? fenceBlitMs : 0; e.fenceReadMs = wait ? fenceReadMs : 0;
                    e.gc0 = GC.CollectionCount(0) - gc0; e.gc1 = GC.CollectionCount(1) - gc1; e.gc2 = GC.CollectionCount(2) - gc2;
                    if (nativeCapture != null) for (int n = 0; n < nativeRecorders.Length; n++) e.nativeNanoseconds[n] = nativeRecorders[n].Valid ? nativeRecorders[n].CurrentValue - nativeBefore[n] : 0;
                }
            }
            return events;
        }
        void OldBegin()
        {
            if (fullAssembly) { beginCurrent(layer, beginArgs); return; }
            if (locked) configureProtection(layer, new object[] { 7 });
            if (!buffered) return;
            oldBase = Rt(); oldStroke = Rt(); Graphics.Blit(surface, oldBase);
            if (stampBlend && pressureOpacity) Graphics.Blit(oldBase, oldStroke, oldBlend, 2);
            else if (stampBlend) Graphics.Blit(oldBase, oldStroke);
            else { RenderTexture.active = oldStroke; GL.Clear(false, true, Color.clear); }
        }
        object[] OldArguments(object stamps)
        {
            Color color = (Color)Get(parameters, "Color"); color.a *= .35f;
            return new object[] { null, stamps, (float)diameter, .65f, false, Get(parameters, "Shape"), color, false,
                side, side, new Vector2(.5f, .5f), false, transform, !hdr, !buffered && hdr,
                selection, dynamics, Get(parameters, "StandardColorInputs"), stampBlend, hdr, null, null };
        }
        void OldDraw(object[] args)
        {
            if (((IList)args[1]).Count == 0) return;
            args[0] = buffered ? oldStroke : surface; args[21] = oldBase;
            drawOld(null, args);
            if (buffered)
            {
                oldBlend.SetTexture("_Blend", oldStroke); oldBlend.SetFloat("_Opacity", opacity); oldBlend.SetFloat("_Mode", (int)blend);
                oldBlend.SetFloat("_HdrBlend", hdr ? 1 : 0); oldBlend.SetFloat("_BrushErase", 0); oldBlend.SetFloat("_BrushStandard", hdr ? 0 : 1);
                oldBlend.SetFloat("_BrushStampAccumulation", stampBlend ? 1 : 0); oldBlend.SetFloat("_BrushPressureOpacity", pressureOpacity ? 1 : 0);
                Graphics.Blit(oldBase, surface, oldBlend, 1);
                oldBlend.SetTexture("_Blend", null); oldBlend.SetFloat("_BrushStampAccumulation", 0); oldBlend.SetFloat("_BrushPressureOpacity", 0);
            }
            if (locked) protect(layer, Array.Empty<object>());
        }
        void OldPreview()
        {
            if (fullAssembly) configureComposite(layer, new object[] { oldConversion });
            oldConversion.SetFloat("_Mode", 1); Graphics.Blit(surface, preview, oldConversion);
            if (fullAssembly) ClearComposite(oldConversion);
        }
        static void ClearComposite(Material material)
        {
            material.SetFloat("_PendingBrush", 0); material.SetTexture("_BrushStroke", null);
            material.SetTexture("_BrushSelection", null); material.SetTexture("_BrushOriginalStraight", null);
        }
        void OldEnd()
        {
            if (fullAssembly && Get(layer, "advancedStroke") != null)
            {
                var result = Rt();
                try
                {
                    configureComposite(layer, new object[] { oldBlend });
                    Graphics.Blit(surface, result, oldBlend, 1); Graphics.Blit(result, surface);
                    releaseStroke(layer, Array.Empty<object>());
                }
                finally { ClearComposite(oldBlend); RenderTexture.ReleaseTemporary(result); }
            }
            if (oldStroke != null) RenderTexture.ReleaseTemporary(oldStroke);
            if (oldBase != null) RenderTexture.ReleaseTemporary(oldBase);
            oldStroke = oldBase = null; end?.Invoke(layer, Array.Empty<object>());
        }
        public Sample Stroke(bool current, bool phases)
        {
            Call(layer, "CancelStroke"); Clear(); Graphics.Blit(surface, preview); Barrier();
            Set(layer, "paintSurfaceDirty", false);
            var oldArgs = new object[Updates][]; for (int i = 0; i < Updates; i++) oldArgs[i] = OldArguments(batches[i][1]);
            Action begin = current ? () => beginCurrent(layer, beginArgs) : OldBegin;
            Action render = current ? () => previewCurrent(layer, previewArgs) : OldPreview;
            Action finish = current ? () => end(layer, Array.Empty<object>()) : OldEnd;
            var sample = new Sample { stamps = stampCount };
            sample.barrierMs = Phase(() => { }, true);
            long gc = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            sample.setupMs = Phase(begin, phases);
            for (int i = 0; i < Updates; i++)
            {
                int at = i;
                sample.drawMs += Phase(() => { if (current || fullAssembly) drawCurrent(layer, batches[at]); else OldDraw(oldArgs[at]); }, phases);
                sample.previewMs += Phase(render, phases);
            }
            long payload = (long)side * side * 8;
            var dirty = (RectInt)Get(layer, "brushDirtyBounds");
            int tileLimit = (int)typeof(DrawingLayerBehaviour).GetField("BrushCompositeTileSize", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            sample.commitScratchBytes = current ? surface.enableRandomWrite ? 0 : (long)Math.Min(tileLimit, dirty.width) * Math.Min(tileLimit, dirty.height) * 8 : fullAssembly ? payload : 0;
            sample.commitMs = Phase(finish, phases);
            Barrier(); sample.strokeMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            sample.managedBytes = GC.GetAllocatedBytesForCurrentThread() - gc;
            sample.graphicsDriverBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
            if (phases)
            {
                // Verify committed pixels, not just the preview retained from before mouse-up.
                if (computeComparison) Graphics.Blit(surface, preview);
                var probes = new Color[9];
                for (int i = 0; i < probes.Length; i++)
                {
                    probes[i] = Probe(side * (i % 3 + 1) / 4, side * (i / 3 + 1) / 4);
                }
                if (current) newProbes = probes; else oldProbes = probes;
                Clear(); begin(); Barrier();
                int persistent = current || fullAssembly ? 1 : (buffered ? 2 : 0) + (locked ? 2 : 0);
                // This workload has fixed-size, untransformed round dabs. Six pixels conservatively
                // cover AA, integer rounding and the backdrop guard; production buckets by 128.
                int stampCapacity = Math.Min(side, ((diameter + 6 + 127) / 128) * 128);
                long stampScratch = stampBlend || pressureOpacity ? (long)stampCapacity * stampCapacity * 8 : 0;
                sample.activeRtPayloadBytes = (2 + persistent) * payload;
                sample.peakRtPayloadBytes = sample.activeRtPayloadBytes + Math.Max(sample.commitScratchBytes, stampScratch);
                var textures = new List<TextureMemory>();
                Add(surface, "surface"); Add(preview, "preview"); Add(stored, "storage"); Add(selection, "selection");
                Add((Texture)Get(layer, "advancedStroke"), "stroke"); Add(oldBase, "oldBase"); Add(oldStroke, "oldStroke");
                Add((Texture)Get(layer, "paintWriteBefore"), "writeBefore"); Add((Texture)Get(layer, "paintWriteScratch"), "writeScratch");
                sample.textures = textures.ToArray();
                void Add(Texture texture, string role)
                {
                    if (texture == null) return;
                    var rt = texture as RenderTexture;
                    long bytes = Profiler.GetRuntimeMemorySizeLong(texture); sample.activeTextureBytes += bytes;
                    textures.Add(new TextureMemory { role = role, bytes = bytes, width = texture.width, height = texture.height,
                        format = texture.graphicsFormat.ToString(), depth = rt?.depth ?? 0, samples = rt?.antiAliasing ?? 1, mipCount = texture.mipmapCount });
                }
                if (current) Call(layer, "CancelStroke"); else OldEnd();
            }
            return sample;
        }
        public void Dispose()
        {
            RenderTexture.active = null;
            Call(layer, "CancelStroke"); OldEnd(); Call(layer, "ReleaseTransientResources");
            if (rasterSurface != null) Object.DestroyImmediate(rasterSurface);
            if (computeSurface != null) Object.DestroyImmediate(computeSurface);
            if (preview != null) RenderTexture.ReleaseTemporary(preview);
            if (fenceTarget != null) RenderTexture.ReleaseTemporary(fenceTarget);
            if (stored != null) Object.DestroyImmediate(stored);
            foreach (var item in new Object[] { selection, fence, oldBlend, oldConversion, oldBlendShader, oldConversionShader }) if (item != null) Object.DestroyImmediate(item);
        }
    }
    public static string Run(int side, int diameter, string scenario) => RunCore(side, diameter, scenario, false);
    public static string RunRegion(int side, int diameter, string scenario) => RunCore(side, diameter, scenario, true);
    public static string RunIsolated(int side, int diameter, string scenario, int path) => RunCore(side, diameter, scenario, true, path);
    public static string RunCompute(int side, int diameter, string scenario) => TestContext.Run("Raster vs compute commit " + side + "/" + diameter + "/" + scenario, t =>
    {
        t.True(side >= 256 && side <= 8192 && diameter >= 16 && diameter <= 4096, "Bounded dimensions");
        t.True(Array.IndexOf(new[] { "normal", "selection", "opacity", "blend", "stamp", "pressure", "locked", "hdr" }, scenario) >= 0, "Known workload");
        t.True(!(bool)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler").GetProperty("Recording", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "No user profiler recording");
        t.True(side <= SystemInfo.maxTextureSize && (side < 8192 || SystemInfo.graphicsMemorySize >= 7000), "Hardware working-set capacity");
        t.True((bool)Product.GetType("DCFApixels.WhimTex.BrushStrokeCompute").GetProperty("Supported", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Compute supported");
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            using var harness = new Harness(side, diameter, scenario, computeComparison: true);
            var result = new Result { utc = DateTime.UtcNow.ToString("O"), canvas = side, diameter = diameter, scenario = scenario, updates = Updates,
                gpu = SystemInfo.graphicsDeviceName, vramMB = SystemInfo.graphicsMemorySize, ramMB = SystemInfo.systemMemorySize,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, colorSpace = QualitySettings.activeColorSpace.ToString(),
                comparison = "A: current tiled raster commit on non-UAV surface; B: compute commit on UAV surface. Both use identical current accumulation, preview, stroke batches and inputs. No historical/full-canvas commit path.",
                timing = "Warm synchronous wall latency including GPU completion through a separate 1x1 RT/readback. Two warmups, nine alternating A/B throughput samples, then nine alternating separately fenced phase samples. Commit includes its fence cost (barrierMs); not GPU timestamps. Whole stroke includes 16 updates/previews and commit, excludes storage readback, UI and downstream FX.",
                memory = "Logical RT payload for the active production path plus preview. The harness retains one additional inactive control surface for allocation-free A/B switching; excluded from reported production payload. Unity/driver counters are not physical VRAM residency.",
                oldSamples = new Sample[Repetitions], newSamples = new Sample[Repetitions],
                oldCommitSamples = new Sample[Repetitions], newCommitSamples = new Sample[Repetitions] };
            for (int warm = 0; warm < 2; warm++) { harness.SelectCompute(false); harness.Stroke(true, false); harness.SelectCompute(true); harness.Stroke(true, false); }
            for (int phase = 0; phase < 2; phase++)
            for (int i = 0; i < Repetitions; i++)
            {
                Color[] raster = null, compute = null;
                for (int order = 0; order < 2; order++)
                {
                    bool useCompute = (i + order) % 2 != 0; harness.SelectCompute(useCompute);
                    var sample = harness.Stroke(true, phase == 1);
                    if (phase == 0) (useCompute ? result.newSamples : result.oldSamples)[i] = sample;
                    else
                    {
                        (useCompute ? result.newCommitSamples : result.oldCommitSamples)[i] = sample;
                        if (useCompute) compute = harness.newProbes; else raster = harness.newProbes;
                    }
                }
                if (phase == 1)
                    for (int p = 0; p < raster.Length; p++) for (int c = 0; c < 4; c++)
                    {
                        t.True(!float.IsNaN(compute[p][c]) && !float.IsInfinity(compute[p][c]), "Finite committed probe");
                        result.maxProbeDifference = Math.Max(result.maxProbeDifference, Math.Abs(raster[p][c] - compute[p][c]));
                    }
            }
            t.Near(0, result.maxProbeDifference, .003, "Equivalent committed output");
            result.oldStroke = Summary(result.oldSamples); result.newStroke = Summary(result.newSamples);
            result.oldCommit = Summary(result.oldCommitSamples, true); result.newCommit = Summary(result.newCommitSamples, true);
            result.oldPhases = result.oldCommitSamples[0]; result.newPhases = result.newCommitSamples[0];
            string directory = Path.GetFullPath("Temp/WhimTex/brush-pipeline-benchmark"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-compute-" + side + "-" + diameter + "-" + scenario + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, Encode(result)); t.True(File.Exists(path), "Raw samples retained");
            UnityEngine.Debug.Log("Brush compute benchmark: " + path);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    public static string RunSpikeTrace(int side, int diameter, string scenario, bool paired, bool fenced, string control = "production") => TestContext.Run("Brush stage trace", t =>
    {
        t.True(side >= 256 && side <= 8192 && diameter >= 16 && diameter <= 4096, "Bounded dimensions");
        t.True(scenario == "normal" || scenario == "selection", "Bounded scenarios");
        t.True(Array.IndexOf(new[] { "production", "async-fence", "graphics-fence", "no-preview", "clear-only", "replay-mesh", "replay-bound", "warm-repeat" }, control) >= 0, "Known control");
        t.True(control != "async-fence" || SystemInfo.supportsAsyncGPUReadback, "Readback control supported");
        t.True(control != "graphics-fence" || SystemInfo.supportsGraphicsFence, "Graphics fence control supported");
        t.True(!(bool)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler").GetProperty("Recording", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "No user profiler recording");
        t.True(side < 8192 || SystemInfo.graphicsMemorySize >= 7000, "Hardware capacity");
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            using var harness = new Harness(side, diameter, scenario, computeComparison: paired, diagnostic: true);
            if (paired) harness.SelectCompute(true);
            t.True(harness.ComputeEnabled, "Compute is retained");
            for (int i = 0; i < 2; i++) harness.Stroke(true, false);
            var result = new TraceResult { utc = DateTime.UtcNow.ToString("O"), gpu = SystemInfo.graphicsDeviceName,
                api = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, side = side, diameter = diameter,
                scenario = scenario, paired = paired, fenced = fenced, control = control,
                timing = "32 synchronous strokes, actual production code. submitMs is main-thread action wall time; waitMs is 1x1 GPU readback completion wall time, NOT a GPU timestamp. Fenced mode drains after each stage; streamed mode only at boundaries. Prepare/idle are retained separately. Paired mode holds an extra idle non-UAV control surface; it never executes the fallback.",
                driverBefore = Profiler.GetAllocatedMemoryForGraphicsDriver() };
            if (control == "warm-repeat") result.warmupEvents = harness.Trace(fenced, control);
            result.events = harness.Trace(fenced, control); result.driverAfter = Profiler.GetAllocatedMemoryForGraphicsDriver();
            string directory = Path.GetFullPath("Temp/WhimTex/brush-pipeline-benchmark"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-trace-" + side + "-" + diameter + "-" + scenario + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, Encode(result)); t.True(result.events.Length == 1184, "Every stage captured");
            UnityEngine.Debug.Log("Brush stage trace: " + path);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
    static string RunCore(int side, int diameter, string scenario, bool fullAssembly, int pathMode = 0) => TestContext.Run("Brush pipeline benchmark " + side + "/" + diameter + "/" + scenario, t =>
    {
        t.True(side >= 256 && side <= 8192 && diameter >= 16 && diameter <= 4096, "Bounded diagnostic dimensions");
        t.True(pathMode >= 0 && pathMode <= 2, "Known path: paired, full assembly or region");
        t.True(Array.IndexOf(new[] { "normal", "selection", "opacity", "blend", "stamp", "pressure", "locked", "hdr" }, scenario) >= 0, "Known scenario");
        t.True(!(bool)Product.GetType("DCFApixels.WhimTex.WhimTexProfiler").GetProperty("Recording", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "No user profiler recording");
        t.True(side <= SystemInfo.maxTextureSize && (side < 8192 || SystemInfo.graphicsMemorySize >= 7000), "Hardware capacity for bounded working set");
        var provenance = Decode<Provenance>(Read(Baseline + "provenance.json"));
        if (fullAssembly) BrushFullAssemblyReference.Validate(t);
        foreach (var source in provenance.files)
        {
            string actual = Read(source.path);
            if (source.rendererOnly) actual = actual.Substring(actual.IndexOf("        private static partial class PaintBrushRenderer", StringComparison.Ordinal));
            t.Equal(source.sha256, Hash(actual), "Authenticated baseline/shared implementation: " + source.path);
        }
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = false;
            using var harness = new Harness(side, diameter, scenario, fullAssembly);
            for (int warm = 0; warm < 2; warm++)
            {
                if (pathMode != 2) harness.Stroke(false, false);
                if (pathMode != 1) harness.Stroke(true, false);
            }
            var result = new Result { utc = DateTime.UtcNow.ToString("O"), canvas = side, diameter = diameter, scenario = scenario, updates = Updates,
                gpu = SystemInfo.graphicsDeviceName, vramMB = SystemInfo.graphicsMemorySize, ramMB = SystemInfo.systemMemorySize,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, colorSpace = QualitySettings.activeColorSpace.ToString(),
                baselineCommit = provenance.commit, provenance = provenance,
                comparison = "Reconstructed old orchestration with frozen old shaders and hash-authenticated current shared rasterizer, versus current production accumulation/preview/commit. Both paths use compact stamp backdrops; this does not reproduce historical full-size stamp scratch. Same stroke batches, source and preview. No UI or downstream FX.",
                timing = "Warm main-thread wall latency including GPU completion via blit into a 1x1 RT, then 1-pixel ReadPixels. No full-size readback. Not isolated GPU timestamps. Whole stroke includes buffer setup, 16 updates with full-source preview and commit; excludes storage readback. Alternating A/B order, 2 warmups per path, 9 samples. Phase diagnostic adds a completion fence after each stage; phase sums are not the throughput total.",
                memory = "Active texture memory is Unity GetRuntimeMemorySizeLong for owned textures; peak RT payload is lifetime accounting, excluding CPU copies, shared materials/meshes, other documents and pooled allocations retained by Unity/driver. No claim of process/physical-VRAM peak.",
                oldSamples = pathMode != 2 ? new Sample[Repetitions] : null, newSamples = pathMode != 1 ? new Sample[Repetitions] : null };
            if (fullAssembly)
            {
                result.baselineCommit = "2026-10-17 unified full assembly, captured before region optimization";
                result.comparison = "Unified paintSurface + stroke on both sides. A: frozen full-preview/full-commit shaders and full-size commit scratch. B: current dirty-region preview/commit. Both share bounds bookkeeping and compact stamp backdrops; no UI or downstream FX.";
            }
            if (pathMode != 0)
            {
                result.comparison += " Isolated path " + pathMode + " (1=full, 2=region); the other path is never executed in this invocation.";
                result.timing = result.timing.Replace("Alternating A/B order, 2 warmups per path, 9 samples.", "Synchronous single path, 2 warmups and 9 samples, then one phase diagnostic. No Editor-frame delays or asynchronous carrier.");
            }
            for (int i = 0; i < Repetitions; i++)
            {
                if (pathMode == 1) result.oldSamples[i] = harness.Stroke(false, false);
                else if (pathMode == 2) result.newSamples[i] = harness.Stroke(true, false);
                else if (i % 2 == 0) { result.oldSamples[i] = harness.Stroke(false, false); result.newSamples[i] = harness.Stroke(true, false); }
                else { result.newSamples[i] = harness.Stroke(true, false); result.oldSamples[i] = harness.Stroke(false, false); }
            }
            if (pathMode != 2) result.oldPhases = harness.Stroke(false, true);
            if (pathMode != 1) result.newPhases = harness.Stroke(true, true);
            if (pathMode == 0)
                for (int i = 0; i < harness.oldProbes.Length; i++) for (int c = 0; c < 4; c++)
                    result.maxProbeDifference = Math.Max(result.maxProbeDifference, Math.Abs(harness.oldProbes[i][c] - harness.newProbes[i][c]));
            // Old channel locks ran after each update; the new route protects the assembled stroke.
            // Selection and locks intentionally differ, so retain their differences without an equivalence claim.
            if (pathMode == 0 && (fullAssembly || (scenario != "selection" && scenario != "locked"))) t.True(result.maxProbeDifference < .025, "Equivalent output within half-precision accumulation tolerance: " + result.maxProbeDifference);
            result.oldStroke = pathMode != 2 ? Summary(result.oldSamples) : null; result.newStroke = pathMode != 1 ? Summary(result.newSamples) : null;
            string directory = Path.GetFullPath("Temp/WhimTex/brush-pipeline-benchmark"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + (pathMode != 0 ? "-sync" + pathMode + "-" : fullAssembly ? "-region-" : "-legacy-") + side + "-" + diameter + "-" + scenario + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, Encode(result));
            t.True(File.Exists(path), "Raw samples retained"); UnityEngine.Debug.Log("Brush pipeline benchmark: " + path);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    });
}
