// Opt-in Pipeline eval body; transient objects only, no saves, prefs or Undo.
var type = typeof(DCFApixels.WhimTex.TextureCompositor);
var hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var all = hidden | System.Reflection.BindingFlags.Public;
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new System.Exception(message);
    checks++;
}
var cpu = type.GetMethod("ComposeCanvas", hidden, null, new[] { typeof(int) }, null);
var gpu = type.GetMethod("RenderCanvas", hidden);
var exact = type.GetMethod("RenderCanvasAtSize", hidden);
var size = type.GetMethod("GetCanvasRenderSize", hidden);
Check(type.GetMethod("ComposeCanvas", all, null, System.Type.EmptyTypes, null).IsPublic, "Native ComposeCanvas must remain public");
Check(cpu != null && cpu.IsAssembly && cpu.ReturnType == typeof(UnityEngine.Texture2D), "Limited ComposeCanvas remains internal");
Check(gpu != null && gpu.IsAssembly && gpu.ReturnType == typeof(UnityEngine.RenderTexture), "RenderCanvas signature");
foreach (string oldName in new[] { "Compose", "ComposePreview", "RenderPreview", "RenderCachedPreview", "GetPreviewDimensions", "ComposeAtSize", "RenderComposite", "RenderAllLayers", "RenderThumbnailLayer" })
    Check(type.GetMethod(oldName, all) == null, "Unexpected old compositor alias: " + oldName);
Check(type.GetMethod("RenderLayerPreview", hidden) != null, "Layer preview is not canvas rendering");
Check(type.GetMethod("RenderAgentLayerPreview", hidden) != null, "Agent layer preview remains distinct");
var cacheType = type.Assembly.GetType("DCFApixels.WhimTex.EffectRenderCache");
object cache = null;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = 64; document.height = 32;
UnityEngine.Texture2D source = null;
UnityEngine.RenderTexture sentinel = null;
var previousTarget = UnityEngine.RenderTexture.active;
bool previousSrgb = UnityEngine.GL.sRGBWrite;
UnityEngine.Color[] Read(UnityEngine.RenderTexture rt)
{
    var copy = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGBAFloat, false, true);
    var previous = UnityEngine.RenderTexture.active;
    try
    {
        UnityEngine.RenderTexture.active = rt;
        copy.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0, false);
        return copy.GetPixels();
    }
    finally { UnityEngine.RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
}
void Same(UnityEngine.Color[] a, UnityEngine.Color[] b)
{
    Check(a.Length == b.Length, "Pixel counts differ");
    for (int i = 0; i < a.Length; i++)
        for (int c = 0; c < 4; c++)
            Check(UnityEngine.Mathf.Abs(a[i][c] - b[i][c]) < .002f, "CPU/GPU/cache pixel mismatch");
}
try
{
    cache = System.Activator.CreateInstance(cacheType, true);
    source = new UnityEngine.Texture2D(64, 32, UnityEngine.TextureFormat.RGBAFloat, false, true);
    source.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    var pixels = new UnityEngine.Color[64 * 32];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = new UnityEngine.Color(2f, .25f, .5f, .75f);
    source.SetPixels(pixels); source.Apply(false, false);
    document.layers.Add(new DCFApixels.WhimTex.FileLayerBehaviour
        { sourceTexture = source, colorRange = DCFApixels.WhimTex.LayerColorRange.HDR });
    sentinel = UnityEngine.RenderTexture.GetTemporary(2, 2);
    UnityEngine.RenderTexture.active = sentinel;
    var native = document.ComposeCanvas();
    try { Check(native.width == 64 && native.height == 32 && native.isReadable && native.format == UnityEngine.TextureFormat.RGBAHalf, "Native readable HDR composition"); }
    finally { UnityEngine.Object.DestroyImmediate(native); }
    var limits = new[] { 0, 1, 7, 16, 64, 128 };
    var widths = new[] { 1, 1, 7, 16, 64, 64 };
    var heights = new[] { 1, 1, 4, 8, 32, 32 };
    for (int i = 0; i < limits.Length; i++)
    {
        object[] sizing = { limits[i], 0, 0, 0f };
        size.Invoke(document, sizing);
        Check((int)sizing[1] == widths[i] && (int)sizing[2] == heights[i], "Clamped aspect-preserving render size");
        Check(UnityEngine.Mathf.Abs((float)sizing[3] - UnityEngine.Mathf.Max(64f / widths[i], 32f / heights[i])) < .0001f, "Unchanged render scale");
        UnityEngine.Texture2D texture = null;
        UnityEngine.RenderTexture target = null;
        try
        {
            texture = (UnityEngine.Texture2D)cpu.Invoke(document, new object[] { limits[i] });
            target = (UnityEngine.RenderTexture)gpu.Invoke(document, new object[] { limits[i] });
            Check(texture.width == widths[i] && texture.height == heights[i] && texture.format == UnityEngine.TextureFormat.RGBAHalf && texture.isReadable, "Limited readable HDR composition");
            Check(target.width == widths[i] && target.height == heights[i] && target.IsCreated(), "GPU render size");
            Check(UnityEngine.RenderTexture.active == sentinel && UnityEngine.GL.sRGBWrite == previousSrgb, "Caller render state restored");
            Same(texture.GetPixels(), Read(target));
        }
        finally
        {
            UnityEngine.RenderTexture.active = sentinel;
            if (target != null) UnityEngine.RenderTexture.ReleaseTemporary(target);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    UnityEngine.RenderTexture cached = null, direct = null, sized = null, thumb = null;
    try
    {
        cached = (UnityEngine.RenderTexture)type.GetMethod("RenderCanvasWithCache", hidden).Invoke(document, new object[] { 16, cache, false, null });
        direct = (UnityEngine.RenderTexture)gpu.Invoke(document, new object[] { 16 });
        Same(Read(cached), Read(direct));
        sized = (UnityEngine.RenderTexture)exact.Invoke(document, new object[] { 11, 9 });
        Check(sized.width == 11 && sized.height == 9, "Exact size does not use aspect-constrained maxSize");
        thumb = (UnityEngine.RenderTexture)type.GetMethod("RenderLayerThumbnail", hidden).Invoke(document, new object[] { document.layers[0], 16, cache });
        Check(thumb.width == 16 && thumb.height == 8, "Layer thumbnail renamed without resizing changes");
        Check(UnityEngine.RenderTexture.active == sentinel && UnityEngine.GL.sRGBWrite == previousSrgb, "Cached/exact/thumbnail render state restored");
    }
    finally
    {
        UnityEngine.RenderTexture.active = sentinel;
        foreach (var target in new[] { cached, direct, sized, thumb }) if (target != null) UnityEngine.RenderTexture.ReleaseTemporary(target);
    }
    return "Canvas render naming checks passed: " + checks;
}
finally
{
    UnityEngine.RenderTexture.active = previousTarget; UnityEngine.GL.sRGBWrite = previousSrgb;
    if (cache != null) ((System.IDisposable)cache).Dispose();
    if (sentinel != null) UnityEngine.RenderTexture.ReleaseTemporary(sentinel);
    UnityEngine.Object.DestroyImmediate(document);
    if (source != null) UnityEngine.Object.DestroyImmediate(source);
}
