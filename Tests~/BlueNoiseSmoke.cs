// Connected Editor only. Transient document/textures; optional comparison PNG in project Temp.
var type = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 256;
var layer = new DCFApixels.SpriteEditor.NoiseLayerBehaviour
{
    noiseType = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.BlueNoise,
    encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.LinearData
};
document.layers.Add(layer);
type.GetMethod("NormalizeModel", flags).Invoke(document, null);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new System.Exception(message); checks++; }
UnityEngine.Color[] Render(int size = 256)
{
    var previous = UnityEngine.RenderTexture.active;
    var rt = (UnityEngine.RenderTexture)type.GetMethod("RenderLayerPreview", flags).Invoke(document, new object[] { layer.Owner, size });
    var read = new UnityEngine.Texture2D(rt.width, rt.height, UnityEngine.TextureFormat.RGBAFloat, false, true);
    try
    {
        UnityEngine.RenderTexture.active = rt;
        read.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0, false);
        return read.GetPixels();
    }
    finally { UnityEngine.RenderTexture.active = previous; UnityEngine.RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(read); }
}
float Difference(UnityEngine.Color[] a, UnityEngine.Color[] b)
{
    float sum = 0;
    for (int i = 0; i < a.Length; i++) sum += UnityEngine.Mathf.Abs(a[i].r - b[i].r);
    return sum / a.Length;
}
try
{
    var blue = Render();
    Check(Difference(blue, Render()) == 0, "Stable blue seed");
    layer.seed++;
    Check(Difference(blue, Render()) > .1f, "Seed changes blue pattern");
    layer.seed = 1337;
    layer.whiteNoiseColor = DCFApixels.SpriteEditor.NoiseLayerBehaviour.WhiteNoiseColor.Color;
    var rgb = Render();
    double rg = 0, rb = 0, gb = 0;
    for (int i = 0; i < rgb.Length; i++)
    {
        var p = rgb[i];
        Check(p.r == blue[i].r, "Monochrome uses red channel");
        Check(p.r >= 0 && p.r <= 1 && p.g >= 0 && p.g <= 1 && p.b >= 0 && p.b <= 1 && p.a > .999f, "Bounded opaque RGB");
        rg += (p.r - .5) * (p.g - .5); rb += (p.r - .5) * (p.b - .5); gb += (p.g - .5) * (p.b - .5);
    }
    foreach (double sum in new[] { rg, rb, gb }) Check(System.Math.Abs(sum / rgb.Length) < .01, "Separate RGB patterns");
    layer.inverted = true;
    var inverted = Render();
    for (int i = 0; i < rgb.Length; i++) for (int c = 0; c < 3; c++)
        Check(UnityEngine.Mathf.Abs(rgb[i][c] + inverted[i][c] - 1) < .002f, "Blue inversion");
    layer.inverted = false;
    layer.offset = new UnityEngine.Vector2(-128, 128);
    Check(Difference(rgb, Render()) == 0, "Periodic table and negative coordinates");
    layer.offset = UnityEngine.Vector2.zero;
    layer.whiteNoiseSize = 4;
    var grain = Render();
    var small = Render(64);
    for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        Check(grain[y * 256 + x] == small[(y / 4) * 64 + x / 4], "Grain and preview resolution agree");
    layer.fractal = DCFApixels.SpriteEditor.NoiseLayerBehaviour.FractalType.PingPong;
    layer.warp = DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType.BasicGrid;
    layer.scale = 1000;
    Check(Difference(grain, Render()) == 0, "Blue ignores fractal/warp/Scale");
    layer.dimensions = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseDimensions.OneD;
    layer.whiteNoiseSize = 1;
    foreach (float angle in new[] { 0f, 90f, -180f })
    {
        layer.direction = angle;
        var stripes = Render();
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            Check(stripes[y * 256 + x] == stripes[angle == 90 ? y * 256 : x], "Blue 1D bands");
    }
    layer.dimensions = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseDimensions.TwoD;
    var resources = type.Assembly.GetType("DCFApixels.SpriteEditor.BlueNoiseTextures");
    resources.GetMethod("Release", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
    Check(Difference(rgb, Render()) == 0, "Shared tables recreate deterministically after release");
    layer.noiseType = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.WhiteNoise;
    var white = Render();
    var preview = new UnityEngine.Texture2D(512, 512, UnityEngine.TextureFormat.RGBA32, false, true);
    try
    {
        var pixels = new UnityEngine.Color32[512 * 512];
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        {
            int i = y * 256 + x;
            pixels[(y + 256) * 512 + x] = white[i]; pixels[(y + 256) * 512 + x + 256] = rgb[i];
            pixels[y * 512 + x] = white[i].r < .1f ? UnityEngine.Color.white : UnityEngine.Color.black;
            pixels[y * 512 + x + 256] = rgb[i].r < .1f ? UnityEngine.Color.white : UnityEngine.Color.black;
        }
        preview.SetPixels32(pixels); preview.Apply();
        System.IO.File.WriteAllBytes("D:/DCFA/Projects/Test6.6/Temp/WhimTexBlueNoiseComparison.png", UnityEngine.ImageConversion.EncodeToPNG(preview));
    }
    finally { UnityEngine.Object.DestroyImmediate(preview); }
    return "Blue Noise GPU checks passed: " + checks + "; comparison: Temp/WhimTexBlueNoiseComparison.png (white left, blue right)";
}
finally { UnityEngine.Object.DestroyImmediate(document); }
