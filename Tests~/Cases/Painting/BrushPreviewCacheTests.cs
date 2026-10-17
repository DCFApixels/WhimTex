using System;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class BrushPreviewCacheTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int W = 1031, H = 773;
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object New(string type) => Activator.CreateInstance(typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + type), true);
    static Color[] Read(RenderTexture rt)
    {
        var old = RenderTexture.active;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); return image.GetPixels(); }
        finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
    }
    static void Compare(TestContext t, DrawingLayerBehaviour layer, RenderTexture reference, string label)
    {
        var before = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var cache = (RenderTexture)Call(layer, "GetBrushPreviewSurface");
        t.True(RenderTexture.active == before && GL.sRGBWrite == srgb, label + " restores GPU state");
        Call(layer, "BlitStraightSurface", reference, 1f);
        var a = Read(reference); var b = Read(cache);
        double error = 0; int changed = 0;
        for (int i = 0; i < a.Length; i++)
            for (int c = 0; c < 4; c++)
            {
                double delta = Math.Abs(a[i][c] - b[i][c]);
                if (delta > 0) changed++;
                error = Math.Max(error, delta / Math.Max(1, Math.Abs(a[i][c])));
            }
        t.Near(0, error, .0011, label + " full-source reference, differing components=" + changed);
        t.Equal(0, (int)Get(Get(layer, "brushPreviewTiles"), "Count"), "Consumed only preview damage");
        t.True(ReferenceEquals(cache, Call(layer, "GetBrushPreviewSurface")), "Unchanged render reuses target");
    }
    public static string Run() => TestContext.Run("Incremental live brush preview", t =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var reference = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        try
        {
            foreach (string mode in new[] { "normal", "hdr", "erase", "stamp", "wrap", "locked" })
            {
                bool hdr = mode == "hdr";
                var layer = new DrawingLayerBehaviour { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                    blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
                Texture2D original = null, mask = null;
                try
                {
                    original = new Texture2D(W, H, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr);
                    mask = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp };
                    var colors = new Color[W * H]; var masks = new Color32[colors.Length];
                    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        colors[y * W + x] = new Color(hdr ? -1.25f : .12f, hdr ? 3.5f : .7f, x / (float)W, (x % 7) * .12f);
                        masks[y * W + x] = new Color32((byte)(x * 255 / W), 0, 0, 255);
                    }
                    original.SetPixels(colors); original.Apply(); mask.SetPixels32(masks); mask.Apply();
                    Call(layer, "AdoptStoredTexture", original); Call(layer, "EnsurePaintSurface", W, H);
                    var settings = New("PaintToolSettings"); var dynamics = Get(settings, "dynamics");
                    Set(settings, "brushSize", 81f); Set(settings, "brushHardness", .1f); Set(settings, "brushSpacing", .18f);
                    Set(settings, "brushColor", hdr ? new Color(4, -.3f, 1.5f, .013f) : new Color(.8f, .4f, .2f, .013f));
                    Set(dynamics, "opacity", .47f); Set(dynamics, "flow", .35f);
                    if (mode == "stamp")
                    {
                        Set(dynamics, "blend", BlendMode.Multiply);
                        Set(dynamics, "blendApplication", Enum.Parse(Get(dynamics, "blendApplication").GetType(), "Stamp"));
                    }
                    var p = Call(settings, "GetStrokeParameters", mode == "erase", (Color?)null);
                    p = Call(p, "WithSelectionMask", mask);
                    if (mode == "wrap") p = Call(p, "WithCanvasWrap");
                    if (mode == "locked") p = Call(p, "WithWriteProtection", 5);
                    var points = new[] { new Vector2(.1f,.1f), new Vector2(.501f,.333f), new Vector2(.9f,.8f), new Vector2(.99f,.99f) };
                    Call(layer, "BeginStroke", points[0]);
                    for (int j = 0; j < points.Length; j++)
                    {
                        // Disjoint new dabs exercise retained pixels, tile edges and wrapping.
                        for (int k = 0; k < 4; k++) Call(layer, "PaintPoint", points[j], W, H, p);
                        int dirty = (int)Get(Get(layer, "brushPreviewTiles"), "Count");
                        // Wrapped quads currently use a conservative union across opposite edges.
                        t.True(dirty > 0 && (dirty < 12 || mode == "wrap"), mode + " small dab marks source tiles: " + dirty);
                        Compare(t, layer, reference, mode + " step " + j);
                    }
                    t.True((int)Get(Get(layer, "brushDirtyTiles"), "Count") > 0, "History retains accumulated coverage");
                    Set(dynamics, "opacity", .83f); Compare(t, layer, reference, mode + " opacity invalidation");
                    Array.Fill(masks, new Color32(80, 0, 0, 255)); mask.SetPixels32(masks); mask.Apply();
                    Compare(t, layer, reference, mode + " mutable mask invalidation");
                    if (mode == "normal")
                    {
                        Call(layer, "ResetAdvancedStroke", false, false);
                        Call(layer, "PaintPoint", points[0], W, H, p);
                        Compare(t, layer, reference, "provisional stroke reset");
                    }
                    Call(layer, mode == "erase" ? "EndStroke" : "CancelStroke");
                    t.True(Get(layer, "brushPreviewSurface") == null, "Commit/cancel releases preview ownership");
                }
                finally
                {
                    Call(layer, "ReleaseTransientResources");
                    if (original != null) Object.DestroyImmediate(original);
                    if (mask != null) Object.DestroyImmediate(mask);
                }
            }
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(reference); }
    });
}
