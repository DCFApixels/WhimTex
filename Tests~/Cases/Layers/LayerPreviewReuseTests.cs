using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LayerPreviewReuseTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, F).Invoke(obj, args);
    static object Get(object obj, string field) => obj.GetType().GetField(field, F).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, F).SetValue(obj, value);
    static Color[] Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); return cpu.GetPixels(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(cpu); }
    }
    static RenderTexture Reduce(RenderTexture rt)
    {
        float scale = Mathf.Min(1, 256f / Mathf.Max(rt.width, rt.height));
        var desc = rt.descriptor; desc.width = Mathf.Max(1, Mathf.RoundToInt(rt.width * scale)); desc.height = Mathf.Max(1, Mathf.RoundToInt(rt.height * scale));
        var copy = RenderTexture.GetTemporary(desc);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try { GL.sRGBWrite = copy.sRGB; Graphics.Blit(rt, copy); return copy; }
        catch { RenderTexture.ReleaseTemporary(copy); throw; }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    static string Begin(WhimTex.Tests.UnityC.AsyncFixture job)
    {
        int checks = 0;
        void Check(bool ok, string message) { job.Context.True(ok, message); checks++; }
        void Same(Color[] a, Color[] b, string message)
        {
            Check(a.Length == b.Length, "Dimensions: " + message);
            for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++) Check(Math.Abs(a[i][c] - b[i][c]) < .002f, message + " pixel " + i + " channel " + c);
        }
        var focus = EditorWindow.focusedWindow;
        var window = job.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
        var doc = job.Scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = 512; doc.height = 384;
        var asm = typeof(WhimTexDocument).Assembly;
        var stateType = asm.GetType("DCFApixels.WhimTex.LayerPreviewPanel+ViewState");
        var panelType = asm.GetType("DCFApixels.WhimTex.LayerPreviewPanel");
        var cache = job.Scope.OwnDisposable((IDisposable)Activator.CreateInstance(asm.GetType("DCFApixels.WhimTex.EffectRenderCache"), true));
        var state = Activator.CreateInstance(stateType, true); Set(state, "height", 256f);
        var mini = (VisualElement)Activator.CreateInstance(panelType, F, null, new[] { state }, null);
        job.Scope.OwnDisposable((IDisposable)mini);
        var noise = new NoiseLayerBehaviour { noiseType = NoiseLayerBehaviour.NoiseType.WhiteNoise, grainColor = NoiseLayerBehaviour.GrainColor.Color, seed = 7189 };
        Layer n = noise; doc.layers.Add(n); Call(doc, "NormalizeModel");
        RenderTexture sentinel = job.Scope.Temporary(RenderTexture.GetTemporary(2, 2));
        var original = RenderTexture.active; bool originalSrgb = GL.sRGBWrite;
        RenderTexture Source() => (RenderTexture)Get(mini, "source");
        void Bind(Layer layer) => Call(mini, "Bind", doc, layer);
        void Main()
        {
            var composite = (RenderTexture)Call(doc, "RenderCanvasWithCache", 512, cache, false, null);
            job.Scope.Release(composite);
        }
        void CompareLayer(Layer layer, int size)
        {
            var raw = (RenderTexture)Call(doc, "RenderLayerPreview", layer, size);
            var reduced = Reduce(raw);
            try { Check(Source() != null, "Main render supplied image"); Same(Read(Source()), Read(reduced), "Main render snapshot"); }
            finally { job.Scope.Release(reduced); job.Scope.Release(raw); }
        }
        void Cleanup() => job.DisposeOwned();
        job.OwnCleanup(() => {
            ((IDisposable)mini).Dispose(); job.Scope.CloseWindow(window); cache.Dispose();
            RenderTexture.active = original; GL.sRGBWrite = originalSrgb;
            job.Scope.Release(sentinel); job.Scope.Destroy(doc);
            if (focus != null) focus.Focus();
        });
        void Run()
        {
            try
            {
                Check(Math.Abs(mini.resolvedStyle.height - 256) < 1, "Block cap 256 including header");
                Main(); CompareLayer(n, 512);
                var baseline = (RenderTexture)Call(doc, "RenderCanvasAtSize", 512, 384);
                var observed = (RenderTexture)Call(doc, "RenderCanvasWithCache", 512, cache, false, null);
                try { Same(Read(baseline), Read(observed), "Layer Preview observation does not alter composite"); }
                finally { job.Scope.Release(baseline); job.Scope.Release(observed); }
                var captured = Read(Source());
                var fallback = (RenderTexture)Call(doc, "RenderLayerPreview", n, 256);
                var low = Read(fallback); job.Scope.Release(fallback);
                double difference = 0; for (int i = 0; i < low.Length; i++) difference += Math.Abs(low[i].r - captured[i].r);
                Check(difference / low.Length > .01, "Noise snapshot uses main-resolution sampling, not independent 256 render");
                var args = new object[] { n, null };
                Check(!(bool)Call(doc, "TryGetCachedLayerPreview", args), "Plain Noise has no effect-cache entry");
                noise.seed++; Main(); CompareLayer(n, 512);
                var held = Source();
                var export = (RenderTexture)Call(doc, "RenderCanvasAtSize", 512, 384); job.Scope.Release(export);
                Check(ReferenceEquals(held, Source()), "Export does not publish to Layer Preview");
                var thumbnail = (RenderTexture)Call(doc, "RenderLayerThumbnail", n, 64, cache); job.Scope.Release(thumbnail);
                Check(ReferenceEquals(held, Source()), "Thumbnail does not publish to Layer Preview");

                var blur = new BlurLayerBehaviour { radius = 6, inputMode = EffectInputMode.Specific, TargetLayerId = n.Id };
                Layer b = blur; doc.layers.Insert(0, b); n.enabled = false; Call(doc, "NormalizeModel");
                Bind(b); Main(); CompareLayer(b, 512);
                args = new object[] { b, null };
                Check((bool)Call(doc, "TryGetCachedLayerPreview", args), "Finished effect available from main cache");
                var expected = Reduce((RenderTexture)args[1]); var expectedPixels = Read(expected); job.Scope.Release(expected);
                Bind(null); Bind(b);
                int hits = (int)cache.GetType().GetProperty("Hits", F).GetValue(cache);
                RenderTexture.active = sentinel; GL.sRGBWrite = true;
                Call(mini, "Tick");
                Check(RenderTexture.active == sentinel && GL.sRGBWrite, "Cache display preserves render state");
                Check((int)cache.GetType().GetProperty("Hits", F).GetValue(cache) > hits, "Opening reuses existing cache without a new main render");
                Same(Read(Source()), expectedPixels, "Copied cache pixels");
                cache.Dispose();
                Check(Source().IsCreated(), "Layer Preview owns copy independent of cache eviction");
                Same(Read(Source()), expectedPixels, "Cache disposal preserves Layer Preview pixels");
                noise.seed++;
                args = new object[] { b, null };
                Check(!(bool)Call(doc, "TryGetCachedLayerPreview", args), "Missing/stale cache rejected");
                Main(); CompareLayer(b, 512);
                noise.seed++;
                Check(!(bool)Call(doc, "TryGetCachedLayerPreview", new object[] { b, null }), "Dependency change rejects cached result before main rerender");
                Main(); CompareLayer(b, 512);

                var fill = new ColorFillLayerBehaviour { color = new Color(.2f, .7f, .4f, .5f), opacity = .25f };
                Layer f = fill; doc.layers.Clear(); doc.layers.Add(f); Call(doc, "NormalizeModel"); Bind(f); Main(); CompareLayer(f, 512);
                Check(Math.Abs(Read(Source())[0].a - .5f) < .002f, "Snapshot precedes outer opacity/blending");
                var group = new GroupLayerBehaviour { compositing = GroupCompositing.Isolated };
                Layer g = group; g.opacity = .5f; g.children.Add(f); doc.layers.Clear(); doc.layers.Add(g); Call(doc, "NormalizeModel"); Bind(g); Main();
                Check(Source() != null && Read(Source())[0].g > Read(Source())[0].r, "Isolated group snapshot preserves rendered color");
                var groupPixels = Read(Source());
                var groupFallback = (RenderTexture)Call(doc, "RenderLayerPreviewFallback", g, 256);
                try { Same(groupPixels, Read(groupFallback), "Group fallback matches main color before outer opacity"); }
                finally { job.Scope.Release(groupFallback); }
                var processor = new ShaderProcessorLayerBehaviour(); Layer p = processor; doc.layers.Insert(0, p); Call(doc, "NormalizeModel"); Bind(p); Main();
                Check(Source() != null, "Stack processor publishes processed result");

                doc.layers.Clear(); f.clippingMask = true; doc.layers.Add(f);
                Layer basis = new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .4f) }; doc.layers.Add(basis); Call(doc, "NormalizeModel");
                Bind(f); Main();
                Check(Source() == null, "Unmasked clipping-chain source is not published as finished layer");
                Call(mini, "RequestLayerPreview", true); Set(mini, "awaitingCanvasRender", true); Call(mini, "Tick");
                CompareLayer(f, 256);
                Check(Math.Abs(Read(Source())[0].a - .2f) < .003f, "Fallback retains clipping coverage");
                Set(state, "collapsed", true); Call(mini, "UpdateLayout"); Main();
                Check(Source() == null, "Collapsed Layer Preview ignores main render");
                f.clippingMask = false; Bind(f); Set(state, "collapsed", false); Call(mini, "UpdateLayout");
                doc.width = 4; doc.height = 2; Main();
                Check(Source() != null && Source().width == 4 && Source().height == 2, "Small render retained for proportional display upscaling");
                Check(Math.Abs((float)Get(state, "height") - 256) < .1f, "Preferred height independent of render size");
                mini.RemoveFromHierarchy(); Main(); Check(Source() == null, "Detached Layer Preview ignores live renders");
                job.Pass();
            }
            catch (Exception ex) { job.Fail(ex); }
            finally { Cleanup(); }
        }
        
        try
        {
            window.ShowUtility(); window.position = new Rect(100, 100, 340, 550);
            asm.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { window.rootVisualElement });
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; window.rootVisualElement.Add(spacer);
            window.rootVisualElement.Add(mini); Bind(n);
            job.Schedule(window.rootVisualElement, Run, 700);
            return job.Read();
        }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }

    public static string Poll(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Poll(runId);
    public static string Result(string runId) => Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cancel(runId);
    public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cleanup(runId);

    public static string Start(string runId)
    {
        var job = WhimTex.Tests.UnityC.AsyncFixture.Create(runId);
        try { return Begin(job); }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }
}
