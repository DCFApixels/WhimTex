using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.Rendering;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class BrushAlphaCoverageTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int W = 513, H = 257;
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, F)?.GetValue(target) ?? target.GetType().GetProperty(name, F)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    public static string Run() => TestContext.Run("Exact block alpha coverage against rendered output", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.width = W; doc.height = H;
        var layer = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
        var pixels = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
        var mask = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
        var readback = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
        var source = new Color[W * H];
        var settings = Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PaintToolSettings"), true);
        var dynamics = Get(settings, "dynamics");
        try
        {
            pixels.SetPixels(source); pixels.Apply(); Call(layer, "AdoptStoredTexture", pixels);
            doc.layers.Add(layer); Call(doc, "NormalizeModel");
            Set(settings, "brushSize", 4f); Set(settings, "brushHardness", 1f); Set(settings, "brushColor", new Color(-1, 2, .5f, 1));
            Set(dynamics, "flow", 1f); Set(dynamics, "opacity", 1f);
            void Check(string label)
            {
                var args = new object[] { W, H, null };
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    t.True((bool)Call(layer, "TryGetAlphaContentState", args), label + " uses block coverage");
                    var map = Get(layer, "alphaCoverage");
                    if ((bool)Get(map, "pending")) ((AsyncGPUReadbackRequest)Get(map, "request")).WaitForCompletion();
                    Call(layer, "TryGetAlphaContentState", args);
                    if (args[2].ToString() != "Unknown") break;
                }
                var coverage = Get(layer, "alphaCoverage");
                var buffer = (ComputeBuffer)Get(coverage, "buffer"); var actual = new uint[buffer.count]; buffer.GetData(actual);
                var rendered = (RenderTexture)Call(doc, "RenderLayerContent", layer.Owner, W, H, null);
                try
                {
                    RenderTexture.active = rendered; readback.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                    var values = readback.GetPixels(); var expected = new uint[actual.Length]; bool any = false;
                    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                        if (values[y * W + x].a != 0) { expected[(y / 256) * 3 + x / 256] = 1; any = true; }
                    for (int i = 0; i < actual.Length; i++) t.Equal(expected[i], actual[i], label + " block " + i);
                    t.Equal(any ? "Present" : "Empty", args[2].ToString(), label + " matches full rendered alpha");
                    t.Equal(6, buffer.count, "NPOT coverage uses six uints, no full-size metadata texture");
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rendered); }
            }
            void Upload()
            {
                pixels.SetPixels(source); pixels.Apply(); Call(layer, "InvalidatePaintSurface");
            }
            void Stroke(int x, int y, bool erase, Texture selection = null, bool cancel = false)
            {
                var uv = new Vector2((x + .5f) / W, (y + .5f) / H);
                Call(layer, "BeginStroke", uv);
                object p = Call(settings, "GetStrokeParameters", erase, (Color?)null);
                if (selection != null) p = Call(p, "WithSelectionMask", selection);
                for (int i = 0; i < 8; i++) Call(layer, "PaintPoint", uv, W, H, p);
                Call(layer, cancel ? "CancelStroke" : "EndStroke");
            }
            Check("Initially clear");
            source[32 * W + 32] = new Color(-1, 2, .5f, 1); source[source.Length - 1] = Color.white;
            Upload(); Check("Sparse separated source and last corner");
            Stroke(512, 256, true); Check("Erase corner retains untouched occupied block");
            Stroke(32, 32, true); Check("Erase final occupied block");
            mask.SetPixel(0, 0, Color.clear); mask.Apply();
            Stroke(255, 128, false, mask); Check("Zero selection adds no occupancy");
            mask.SetPixel(0, 0, new Color(.25f, 0, 0, 1)); mask.Apply();
            Set(settings, "brushSize", 160f);
            Stroke(255, 128, false, mask); Check("Soft selection across block boundary");
            var renderCache = Activator.CreateInstance(typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache"), true);
            try
            {
                Call(renderCache, "BeginFrame", doc, null); var before = Call(renderCache, "Stamp", layer.Owner);
                UnityEditor.EditorUtility.SetDirty(pixels);
                Call(renderCache, "BeginFrame", doc, null);
                t.Equal(before, Call(renderCache, "Stamp", layer.Owner), "CPU storage dirtiness does not invalidate the authoritative Half image");
                Call(layer, "SyncSurfaceToTexture"); Call(renderCache, "BeginFrame", doc, null);
                t.Equal(before, Call(renderCache, "Stamp", layer.Owner), "Storage synchronization does not invalidate rendered content");
            }
            finally { ((IDisposable)renderCache).Dispose(); }
            Stroke(480, 230, false, null, true); Check("Cancelled stroke preserves committed occupancy");
            Call(layer, "ClearSurface", W, H); Check("Clear invalidates all prior occupied blocks");
            foreach (float alpha in new[] { 0f, .000001f, .00000999f, .00001002f, .00002f, -.2f, float.PositiveInfinity, float.NaN })
            {
                Array.Clear(source, 0, source.Length); source[source.Length - 1] = new Color(2, -1, .5f, alpha);
                Upload(); Check("Stored Half alpha " + alpha);
            }
            Array.Clear(source, 0, source.Length); Upload(); Check("Clear after source replacement");
            Upload();
            Call(layer, "TryGetAlphaContentState", new object[] { W, H, null });
            Stroke(512, 256, false);
            Check("Older pending empty result cannot overwrite a newly committed occupied block");
            Upload(); Check("Reset before near-transparent strokes");
            Set(settings, "brushSize", 4f); Set(dynamics, "flow", .000001f);
            for (int i = 0; i < 5; i++) { Stroke(512, 256, false); Check("Near-transparent accumulated stroke " + i); }
            var mapping = layer.channelMapping; mapping[3] = ChannelMappingSource.One; layer.channelMapping = mapping;
            t.True(!(bool)Call(layer, "TryGetAlphaContentState", new object[] { W, H, null }), "Alpha remapping falls back to completed rendered output");
        }
        finally
        {
            Call(doc, "ReleaseLayerContent"); Call(layer, "ReleaseTransientResources"); doc.layers.Clear();
            Object.DestroyImmediate(doc); Object.DestroyImmediate(mask); Object.DestroyImmediate(readback);
            if (pixels != null) Object.DestroyImmediate(pixels);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });
}
