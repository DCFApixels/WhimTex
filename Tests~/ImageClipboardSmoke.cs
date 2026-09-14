// Unity Pipeline eval_file. Tests synthetic data only: never reads or changes the OS clipboard.
var type = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.ImageClipboard", true);
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception(message); checks++; }
void Write(byte[] data, int offset, int value) { System.Array.Copy(System.BitConverter.GetBytes(value), 0, data, offset, 4); }
byte[] Dib(int bits, bool topDown, bool alpha)
{
    int header = alpha ? 124 : 40, stride = ((2 * bits + 31) / 32) * 4;
    var data = new byte[header + stride * 2];
    Write(data, 0, header); Write(data, 4, 2); Write(data, 8, topDown ? -2 : 2);
    data[12] = 1; data[14] = (byte)bits;
    if (alpha)
    {
        Write(data, 16, 3); Write(data, 40, 0xFF0000); Write(data, 44, 0xFF00); Write(data, 48, 0xFF);
        Write(data, 52, unchecked((int)0xFF000000));
    }
    // Logical bottom row red, top row blue. Each second pixel is half-gray.
    for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
    {
        int index = header + (topDown ? 1 - y : y) * stride + x * bits / 8;
        data[index + (y == 0 ? 2 : 0)] = 255;
        if (x == 1) data[index] = data[index + 1] = data[index + 2] = 128;
        if (bits == 32) data[index + 3] = alpha ? (byte)(x == 0 ? 0 : 128) : (byte)0;
    }
    return data;
}
UnityEngine.Texture2D Decode(string method, byte[] data) => (UnityEngine.Texture2D)type.GetMethod(method, flags).Invoke(null, new object[] { data });
void Reject(string method, byte[] data)
{
    try { var t = Decode(method, data); UnityEngine.Object.DestroyImmediate(t); }
    catch (System.Reflection.TargetInvocationException e) when (e.InnerException is System.InvalidOperationException) { checks++; return; }
    throw new System.Exception("Malformed clipboard image accepted");
}
foreach (int bits in new[] { 24, 32 }) foreach (bool topDown in new[] { false, true })
foreach (bool alpha in new[] { false, true })
{
    if (alpha && bits != 32) continue;
    var texture = Decode("DecodeDib", Dib(bits, topDown, alpha));
    try
    {
        var p = texture.GetPixels32();
        Check(texture.width == 2 && texture.height == 2, "dimensions");
        Check(p[0].r == 255 && p[0].b == 0 && p[2].r == 0 && p[2].b == 255, "row and channel orientation");
        Check(p[1].r == 128 && p[1].g == 128 && p[1].b == 128, "sRGB bytes unchanged");
        Check(p[0].a == (alpha ? 0 : 255) && p[1].a == (alpha ? 128 : 255), "explicit alpha vs reserved byte");
        Check(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), "sRGB texture");
        if (alpha)
        {
            var png = UnityEngine.ImageConversion.EncodeToPNG(texture);
            var roundTrip = Decode("DecodePng", png);
            try { var q = roundTrip.GetPixels32(); for (int i = 0; i < p.Length; i++) Check(p[i].Equals(q[i]), "PNG RGBA roundtrip"); }
            finally { UnityEngine.Object.DestroyImmediate(roundTrip); }
            Write(png, 16, int.MaxValue); Reject("DecodePng", png);
        }
    }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}
Reject("DecodeDib", new byte[10]); Reject("DecodePng", new byte[40]);
var truncated = Dib(32, false, true); System.Array.Resize(ref truncated, truncated.Length - 1); Reject("DecodeDib", truncated);
var huge = Dib(32, false, false); Write(huge, 4, int.MaxValue); Reject("DecodeDib", huge);
var masks = Dib(32, false, true); Write(masks, 44, 0xFF0000); Reject("DecodeDib", masks);
Write(masks, 44, 0); Reject("DecodeDib", masks);
var palette = Dib(24, false, false); Write(palette, 32, int.MaxValue); Reject("DecodeDib", palette);
var source = Decode("DecodeDib", Dib(24, false, false));
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
UnityEngine.Texture2D rendered = null;
try
{
    document.width = document.height = 8;
    var drawing = (DCFApixels.WhimTex.DrawingLayerBehaviour)typeof(DCFApixels.WhimTex.DrawingLayerBehaviour)
        .GetMethod("FromMergedTexture", flags).Invoke(null, new object[] { source });
    drawing.colorRange = DCFApixels.WhimTex.LayerColorRange.Standard;
    var placement = DCFApixels.WhimTex.TextureTransform.Default;
    placement.scale = new UnityEngine.Vector2(.25f, .25f);
    drawing.transform = placement;
    document.layers.Add(drawing);
    rendered = document.Compose();
    Check(source.width == 2 && source.height == 2, "source resolution retained");
    Check(rendered.GetPixel(0, 0).a < .001f, "centered image does not fill canvas");
    Check(rendered.GetPixel(3, 3).r > .99f, "image is visible at native pixel size");
    float expectedGray = UnityEngine.Mathf.GammaToLinearSpace(128f / 255f);
    Check(UnityEngine.Mathf.Abs(rendered.GetPixel(4, 3).r - expectedGray) < .004f, "sRGB clipboard decoded exactly once in drawing pipeline");
}
finally
{
    if (rendered != null) UnityEngine.Object.DestroyImmediate(rendered);
    UnityEngine.Object.DestroyImmediate(document);
    if (source != null) UnityEngine.Object.DestroyImmediate(source);
}
return "Image clipboard: " + checks + " decoder checks passed; system clipboard untouched.";
