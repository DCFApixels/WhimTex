using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests.UnityC;

public static class CropAlphaBoundsTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Package = typeof(WhimTexDocument).Assembly;
    static readonly Type DetectorType = Package.GetType("DCFApixels.WhimTex.WhimTexAlphaBounds", true);
    static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Flags).Invoke(value, args);

    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = FixtureContext.RunAsync(job, token => Execute(job, token));
        return AsyncFixture.Poll(id);
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);

    static RectInt Bounds(Color[] pixels, int width, int height)
    {
        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (pixels[y * width + x].a > 0)
            { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return x1 < 0 ? default : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static async Task<RectInt?> Detect(AsyncFixture job, object detector, RenderTexture source, bool synchronous, CancellationToken token)
    {
        var ready = new TaskCompletionSource<RectInt?>();
        Action<RectInt?> completed = value => ready.TrySetResult(value);
        job.Context.True((bool)Call(detector, "Begin", source, completed, synchronous), "Reducer accepts an idle operation");
        using (token.Register(() => ready.TrySetCanceled()))
        {
            Task timeout = Task.Delay(5000, token);
            if (await Task.WhenAny(ready.Task, timeout) != ready.Task)
                throw new TimeoutException("Alpha bounds callback did not finish within five seconds.");
            return await ready.Task;
        }
    }

    static async Task Execute(AsyncFixture job, CancellationToken token)
    {
        var t = job.Context; var scope = job.Scope;
        var detector = scope.OwnDisposable((IDisposable)Activator.CreateInstance(DetectorType, true));
        var sentinel = scope.Own(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat)); sentinel.Create();
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            var sizes = new[] { new Vector2Int(27, 13), new Vector2Int(1, 17), new Vector2Int(17, 1), Vector2Int.one };
            foreach (Vector2Int size in sizes)
            {
                var image = scope.Own(new Texture2D(size.x, size.y, TextureFormat.RGBAFloat, false, true));
                image.filterMode = FilterMode.Point;
                var source = scope.Own(new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
                source.filterMode = FilterMode.Point; source.Create();
                for (int variant = 0; variant < 4; variant++)
                {
                    var pixels = new Color[size.x * size.y];
                    Array.Fill(pixels, new Color(100, 12, -5, 0));
                    if (variant == 1) pixels[0] = Color.white;
                    if (variant == 2) pixels[(size.y - 1) * size.x + size.x - 1] = new Color(0, 0, 0, .000001f);
                    if (variant == 3)
                    {
                        int x0 = Math.Min(2, size.x - 1), y0 = Math.Min(1, size.y - 1);
                        int x1 = Math.Max(x0, size.x - 5), y1 = Math.Max(y0, size.y - 3);
                        pixels[y0 * size.x + x0] = new Color(1, 0, 0, .000001f);
                        pixels[y1 * size.x + x1] = new Color(0, 1, 0, .5f);
                    }
                    image.SetPixels(pixels); image.Apply(false, false);
                    GL.sRGBWrite = false; Graphics.Blit(image, source);
                    RenderTexture.active = sentinel; GL.sRGBWrite = true;
                    RectInt expected = Bounds(pixels, size.x, size.y);
                    foreach (bool synchronous in new[] { true, false })
                    {
                        RectInt? result = await Detect(job, detector, source, synchronous, token);
                        t.True(result.HasValue, "Supported alpha reduction returns known bounds");
                        t.Equal(expected, result.Value, "Exact pixel edges " + size + "/" + variant + "/" + synchronous);
                        t.True(RenderTexture.active == sentinel && GL.sRGBWrite, "Reducer preserves caller render state");
                    }
                }
                scope.Destroy(source); scope.Destroy(image);
            }
            t.True(!(await Detect(job, detector, null, true, token)).HasValue, "Missing source stays unknown rather than empty");
            foreach (Color invalid in new[] { new Color(float.NaN, 0, 4, 3), new Color(0, 0, float.PositiveInfinity, 3) })
                t.True(DetectorType.GetMethod("Decode", Flags).Invoke(null, new object[] { invalid, 27, 13 }) == null,
                    "Invalid readback edges stay unknown");

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                bool callback = false;
                t.True((bool)Call(detector, "Begin", sentinel, (Action<RectInt?>)(_ => callback = true), false), "Cancelable readback starts");
                bool pending = (bool)DetectorType.GetProperty("isPending", Flags).GetValue(detector);
                if (pending)
                {
                    Call(detector, "Cancel");
                    double deadline = EditorApplication.timeSinceStartup + 5;
                    while ((bool)DetectorType.GetProperty("isPending", Flags).GetValue(detector))
                    {
                        if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Cancelled reducer did not drain.");
                        await Task.Delay(20, token);
                    }
                    t.True(!callback, "Cancellation suppresses the stale callback");
                }
            }

            var document = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            document.hideFlags = HideFlags.HideAndDontSave; document.width = 27; document.height = 13;
            var fill = new ColorFillLayerBehaviour { color = Color.clear };
            Layer layer = fill; layer.opacity = 0; layer.enabled = false; layer.clippingMask = true;
            document.layers.Add(layer);
            Type cacheType = Package.GetType("DCFApixels.WhimTex.CanvasCropBoundsCache", true);
            var cache = scope.OwnDisposable((IDisposable)Activator.CreateInstance(cacheType, Flags, null, new object[] { document }, null));
            async Task<RectInt?> Cached()
            {
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (true)
                {
                    RectInt? value = (RectInt?)Call(cache, "Get", layer, false);
                    if (value.HasValue) return value;
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Layer content bounds remained unknown.");
                    await Task.Delay(20, token);
                }
            }
            t.Equal(default(RectInt), (await Cached()).Value, "Transparent source is a known empty layer");
            fill.color = Color.white; Call(document, "MarkChanged");
            t.Equal(new RectInt(0, 0, 27, 13), (await Cached()).Value, "Own opacity, visibility and clipping do not hide source bounds");
            fill.color = Color.clear; Call(document, "MarkChanged");
            t.Equal(default(RectInt), (await Cached()).Value, "Changed content invalidates cached positive bounds");
            fill.color = Color.white;
            var effect = scope.Own(ScriptableObject.CreateInstance<ShaderFX>());
            typeof(ShaderFX).GetField("code", Flags).SetValue(effect,
                "float4 ApplyFX(float2 uv, float4 color) { color.a = 0; return color; }");
            t.True((bool)Call(effect, "ApplyCore", false), "Owned alpha FX compiles without a document edit or global Undo");
            layer.fx.Add(effect); Call(document, "MarkChanged");
            t.Equal(default(RectInt), (await Cached()).Value, "FX that removes alpha makes the content bounds empty");
            typeof(ShaderFX).GetField("code", Flags).SetValue(effect,
                "float4 ApplyFX(float2 uv, float4 color) { color.a *= uv.x >= 4.0/27.0 && uv.x < 22.0/27.0 && uv.y >= 2.0/13.0 && uv.y < 11.0/13.0 ? 1 : 0; return color; }");
            t.True((bool)Call(effect, "ApplyCore", false), "Owned asymmetric alpha FX compiles");
            Call(document, "MarkChanged");
            t.Equal(new RectInt(4, 2, 18, 9), (await Cached()).Value, "Bounds follow the post-FX nontransparent pixel edges");
            effect.Active = false; Call(document, "MarkChanged");
            t.Equal(new RectInt(0, 0, 27, 13), (await Cached()).Value, "Disabling FX restores source-content bounds");
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }
}
