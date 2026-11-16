using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class TiledDrawingHistoryTests
{
    public static string RunInitializationEdges() => TestContext.Run("Drawing initialization preserves source frame, exact empty pixels and existing data", test =>
    {
        ReusePreflight(test);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        foreach (bool hdr in new[] { false, true })
        {
            var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
            var drawing = new DrawingLayerBehaviour();
            var layer = new Layer(drawing) { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard };
            Set(layer, "sourceCanvasSize", new Vector2Int(17, 9));
            try
            {
                doc.layers.Add(layer); Call(doc, "NormalizeModel");
                Call(drawing, "InitializeCanvas", 512, 256);
                var source = (Texture2D)Get(drawing, "StoredTexture");
                test.True(Get(drawing, "paintSurface") == null, "Blank creation allocates no working GPU surface");
                test.True((bool)Get(drawing, "tiledBaseEmpty"), "Blank base is identified without importing dense pixels");
                test.Equal(17, source.width, "Frozen source frame width"); test.Equal(9, source.height, "Frozen source frame height");
                test.Equal(hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, source.format, "Storage format");
                test.Equal(!hdr, source.isDataSRGB, "Storage encoding");
                test.Equal(FilterMode.Bilinear, source.filterMode, "Filtering"); test.Equal(TextureWrapMode.Clamp, source.wrapMode, "Addressing");
                test.True(source.isReadable && source.mipmapCount == 1, "Readable single mip storage");
                byte[] empty = new byte[source.GetRawTextureData<byte>().Length];
                Exact(test, empty, source.GetRawTextureData<byte>().ToArray(), "Blank CPU image");
                Exact(test, empty, InitGpuBytes(source), "Blank GPU image");
                var colors = new Color[17 * 9];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(hdr ? 3 : .7f, hdr ? -.4f : .2f, .5f, i % 3 == 0 ? 0 : .6f);
                source.SetPixels(colors); source.Apply(false, false);
                byte[] content = source.GetRawTextureData<byte>().ToArray();
                Call(drawing, "InitializeCanvas", 1024, 1024);
                test.True(ReferenceEquals(source, Get(drawing, "StoredTexture")), "Existing source identity survives initialization");
                Exact(test, content, source.GetRawTextureData<byte>().ToArray(), "Existing CPU data including hidden RGB");
                Exact(test, content, InitGpuBytes(source), "Existing GPU data");
            }
            finally
            {
                Call(drawing, "ReleaseTransientResources"); doc.layers.Clear(); Object.DestroyImmediate(doc);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }
        var pendingDoc = ScriptableObject.CreateInstance<WhimTexDocument>(); pendingDoc.hideFlags = HideFlags.HideAndDontSave;
        var pending = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR };
        try
        {
            pendingDoc.layers.Add(pending); Call(pendingDoc, "NormalizeModel");
            var surface = (RenderTexture)Call(pending, "EnsurePaintSurface", 16, 16);
            RenderTexture.active = surface; GL.Clear(false, true, new Color(1.5f, -.25f, .5f, .5f));
            RenderTexture.active = previous; Set(pending, "paintSurfaceDirty", true);
            Call(pending, "InitializeCanvas", 16, 16);
            var source = (Texture2D)Get(pending, "StoredTexture"); var color = source.GetPixel(8, 8);
            test.Near(3, color.r, .002, "Existing premultiplied HDR surface is synchronized, not discarded");
            test.Near(-.5, color.g, .002, "Existing signed surface value");
            test.Near(.5, color.a, .002, "Existing surface alpha");
            Exact(test, source.GetRawTextureData<byte>().ToArray(), InitGpuBytes(source), "Synchronized surface CPU/GPU");
        }
        finally
        {
            Call(pending, "ReleaseTransientResources"); pendingDoc.layers.Clear(); Object.DestroyImmediate(pendingDoc);
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
    });

    static byte[] InitGpuBytes(Texture2D source)
    {
        var request = AsyncGPUReadback.Request(source, 0); request.WaitForCompletion();
        if (request.hasError) throw new InvalidOperationException("Owned source readback failed");
        return request.GetData<byte>().ToArray();
    }
}
