// Run through the connected Unity Editor after compilation. Transient textures only; no saves or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var type = typeof(DCFApixels.SpriteEditor.TextureCompositor);
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.SpriteEditor.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = 99; document.height = 63;
var layer = new DCFApixels.SpriteEditor.NoiseLayerBehaviour { encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.LinearData };
document.layers.Add(layer);
type.GetMethod("NormalizeModel", flags).Invoke(document, null);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
UnityEngine.Color[] Render(int size = 33)
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
    var shader = UnityEngine.Shader.Find("Hidden/TextureCompositor/Noise");
    Check(shader != null && shader.isSupported, "Noise shader supported");
    foreach (var message in UnityEditor.ShaderUtil.GetShaderMessages(shader))
        Check(message.severity.ToString() != "Error", message.message);
    var a = Render();
    Check(Difference(a, Render()) == 0f, "Stable seed");
    var full = Render(99);
    for (int y = 0; y < 21; y++) for (int x = 0; x < 33; x++)
        Check(UnityEngine.Mathf.Abs(a[y * 33 + x].r - full[(y * 3 + 1) * 99 + x * 3 + 1].r) < .003f, "Resolution-independent coordinates");
    layer.seed++;
    Check(Difference(a, Render()) > .01f, "Seed changes pattern");
    layer.seed = 16777216;
    var wideSeed = Render(); layer.seed++;
    Check(Difference(wideSeed, Render()) > .01f, "Adjacent seeds above float integer precision remain distinct");
    layer.seed = 1337;
    layer.inverted = true;
    var inverse = Render();
    for (int i = 0; i < a.Length; i++) Check(UnityEngine.Mathf.Abs(a[i].r + inverse[i].r - 1) < .002f, "Invert");
    layer.inverted = false;
    layer.encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.ColorValues;
    var color = Render();
    for (int i = 0; i < a.Length; i++)
        Check(UnityEngine.Mathf.Abs(color[i].r - UnityEngine.Mathf.GammaToLinearSpace(a[i].r)) < .003f, "Color encoding");
    layer.encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.LinearData;
    foreach (DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType algorithm in System.Enum.GetValues(typeof(DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType)))
    foreach (DCFApixels.SpriteEditor.NoiseLayerBehaviour.FractalType fractal in System.Enum.GetValues(typeof(DCFApixels.SpriteEditor.NoiseLayerBehaviour.FractalType)))
    {
        layer.noiseType = algorithm; layer.fractal = fractal;
        foreach (var pixel in Render())
            Check(!float.IsNaN(pixel.r) && !float.IsInfinity(pixel.r) && pixel.r >= 0 && pixel.r <= 1 &&
                UnityEngine.Mathf.Abs(pixel.r - pixel.g) < .001f && UnityEngine.Mathf.Abs(pixel.r - pixel.b) < .001f && pixel.a > .999f,
                "Finite opaque grayscale: " + algorithm + "/" + fractal);
    }
    layer.noiseType = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.OpenSimplex2;
    layer.fractal = DCFApixels.SpriteEditor.NoiseLayerBehaviour.FractalType.FBm;
    foreach (DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType warp in System.Enum.GetValues(typeof(DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType)))
    {
        layer.warp = warp;
        if (warp != DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType.None)
            Check(Difference(a, Render()) > .005f, "Warp changes pattern: " + warp);
    }
    layer.noiseType = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseType.WhiteNoise;
    layer.warp = DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType.None;
    var white = Render(99);
    Check(Difference(white, Render(99)) == 0, "White Noise deterministic");
    var whiteSmall = Render();
    for (int y = 0; y < 21; y++) for (int x = 0; x < 33; x++)
        Check(whiteSmall[y * 33 + x] == white[(y * 3 + 1) * 99 + x * 3 + 1], "White grid is resolution-independent");
    layer.fractal = DCFApixels.SpriteEditor.NoiseLayerBehaviour.FractalType.PingPong;
    layer.warp = DCFApixels.SpriteEditor.NoiseLayerBehaviour.WarpType.BasicGrid;
    layer.scale = 1000;
    Check(Difference(white, Render(99)) == 0, "White Noise ignores fractal, warp and Scale");
    layer.seed = int.MinValue;
    Check(Difference(white, Render(99)) > .1f, "White Noise signed seed");
    var extremeSeed = Render(99);
    layer.seed++;
    Check(Difference(extremeSeed, Render(99)) > .1f, "White Noise adjacent extreme seeds");
    layer.seed = 1337;
    layer.whiteNoiseColor = DCFApixels.SpriteEditor.NoiseLayerBehaviour.WhiteNoiseColor.Color;
    var rgbNoise = Render(99);
    double meanR = 0, meanG = 0, meanB = 0, crossRG = 0, crossRB = 0, crossGB = 0, spatial = 0;
    for (int i = 0; i < rgbNoise.Length; i++)
    {
        var c = rgbNoise[i];
        Check(c.r == white[i].r, "Color keeps the monochrome red sequence");
        Check(c.r >= 0 && c.r <= 1 && c.g >= 0 && c.g <= 1 && c.b >= 0 && c.b <= 1 && c.a > .999f,
            "White color bounded and opaque");
        meanR += c.r; meanG += c.g; meanB += c.b;
        crossRG += (c.r - .5) * (c.g - .5);
        crossRB += (c.r - .5) * (c.b - .5);
        crossGB += (c.g - .5) * (c.b - .5);
        if (i > 0) spatial += (c.r - .5) * (rgbNoise[i - 1].r - .5);
    }
    foreach (double sum in new[] { meanR, meanG, meanB })
        Check(System.Math.Abs(sum / rgbNoise.Length - .5) < .025, "Uniform channel mean");
    foreach (double sum in new[] { crossRG, crossRB, crossGB, spatial })
        Check(System.Math.Abs(sum / rgbNoise.Length) < .004, "No strong channel/neighbor correlation");
    layer.inverted = true;
    var whiteInverse = Render(99);
    for (int i = 0; i < rgbNoise.Length; i++) for (int channel = 0; channel < 3; channel++)
        Check(UnityEngine.Mathf.Abs(rgbNoise[i][channel] + whiteInverse[i][channel] - 1) < .002f, "White RGB inversion");
    layer.inverted = false;
    layer.encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.ColorValues;
    var whiteDisplay = Render(99);
    for (int i = 0; i < rgbNoise.Length; i++) for (int channel = 0; channel < 3; channel++)
        Check(UnityEngine.Mathf.Abs(whiteDisplay[i][channel] - UnityEngine.Mathf.GammaToLinearSpace(rgbNoise[i][channel])) < .003f,
            "White RGB color encoding");
    layer.encoding = DCFApixels.SpriteEditor.NoiseLayerBehaviour.OutputEncoding.LinearData;
    layer.offset = new UnityEngine.Vector2(1, 0);
    var shifted = Render(99);
    for (int y = 0; y < 63; y++) for (int x = 0; x < 98; x++)
        Check(shifted[y * 99 + x] == rgbNoise[y * 99 + x + 1], "White offset uses canvas pixels");
    layer.offset = UnityEngine.Vector2.zero;
    layer.whiteNoiseSize = 4;
    var coarse = Render(99);
    for (int y = 0; y < 63; y++) for (int x = 0; x < 99; x++)
        Check(coarse[y * 99 + x] == coarse[(y / 4 * 4) * 99 + x / 4 * 4], "Four-pixel grain cells");
    layer.dimensions = DCFApixels.SpriteEditor.NoiseLayerBehaviour.NoiseDimensions.OneD;
    layer.direction = 0;
    var stripes = Render(99);
    for (int y = 0; y < 63; y++) for (int x = 0; x < 99; x++)
        Check(stripes[y * 99 + x] == stripes[x], "White 1D vertical bands");
    layer.direction = 90;
    stripes = Render(99);
    for (int y = 0; y < 63; y++) for (int x = 0; x < 99; x++)
        Check(stripes[y * 99 + x] == stripes[y * 99], "White 1D horizontal bands");
    return "Noise GPU checks passed: " + checks;
}
finally { UnityEngine.Object.DestroyImmediate(document); }
