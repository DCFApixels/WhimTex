// Unity Pipeline eval_file. Transient objects only; no scene/asset writes or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
document.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
document.width = document.height = 512;
var layer = new DCFApixels.WhimTex.GradientLayerBehaviour();
var layerType = layer.GetType();
var contextType = layerType.Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext");
var previous = UnityEngine.RenderTexture.active;
bool srgb = UnityEngine.GL.sRGBWrite;
var sentinel = UnityEngine.RenderTexture.GetTemporary(8, 8);
int checks = 0;
double maxError = 0;
void Check(bool value, string message) { if (!value) throw new System.Exception(message); checks++; }
UnityEngine.Texture2D Palette() => (UnityEngine.Texture2D)layerType.GetField("palette", flags).GetValue(layer);
UnityEngine.RenderTexture Render(int w, int h)
{
    var context = System.Activator.CreateInstance(contextType, new object[] { document, null, w, h, 1f, false, false });
    return (UnityEngine.RenderTexture)layerType.GetMethod("Render", flags).Invoke(layer, new object[] { context });
}
void Compare(string label, int w = 65, int h = 33, float tolerance = .002f)
{
    UnityEngine.Texture2D expected = null, actual = null;
    UnityEngine.RenderTexture output = null;
    try
    {
        expected = (UnityEngine.Texture2D)layerType.GetMethod("GenerateGradientTexture", flags).Invoke(layer, new object[] { w, h });
        UnityEngine.RenderTexture.active = sentinel; UnityEngine.GL.sRGBWrite = true;
        output = Render(w, h);
        Check(UnityEngine.RenderTexture.active == sentinel && UnityEngine.GL.sRGBWrite, "Preserve graphics state: " + label);
        actual = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBAFloat, false, true);
        UnityEngine.RenderTexture.active = output;
        actual.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0, false);
        var a = actual.GetPixels(); var e = expected.GetPixels();
        for (int i = 0; i < a.Length; i++) for (int c = 0; c < 4; c++)
        {
            float error = UnityEngine.Mathf.Abs(a[i][c] - e[i][c]);
            maxError = System.Math.Max(maxError, error);
            Check(error <= tolerance * UnityEngine.Mathf.Max(1, UnityEngine.Mathf.Abs(e[i][c])),
                label + " pixel " + i + " channel " + c + ": expected=" + e[i][c] + " actual=" + a[i][c]);
        }
    }
    finally
    {
        UnityEngine.RenderTexture.active = sentinel;
        if (output != null) UnityEngine.RenderTexture.ReleaseTemporary(output);
        if (expected != null) UnityEngine.Object.DestroyImmediate(expected);
        if (actual != null) UnityEngine.Object.DestroyImmediate(actual);
    }
}
try
{
    var shader = UnityEngine.Shader.Find("Hidden/TextureCompositor/Gradient");
    Check(shader != null && shader.isSupported, "Gradient shader supported");
    foreach (var message in UnityEditor.ShaderUtil.GetShaderMessages(shader))
        Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
    layer.gradient.SetKeys(new[] {
        new UnityEngine.GradientColorKey(new UnityEngine.Color(.8f, .05f, .12f), .1f),
        new UnityEngine.GradientColorKey(new UnityEngine.Color(.1f, .8f, .3f), .4998f),
        new UnityEngine.GradientColorKey(new UnityEngine.Color(.05f, .1f, .9f), .5002f),
        new UnityEngine.GradientColorKey(UnityEngine.Color.white, .9f)
    }, new[] { new UnityEngine.GradientAlphaKey(.1f, 0), new UnityEngine.GradientAlphaKey(.8f, .3f),
        new UnityEngine.GradientAlphaKey(.2f, .7f), new UnityEngine.GradientAlphaKey(1, 1) });
    foreach (var space in new[] { UnityEngine.ColorSpace.Gamma, UnityEngine.ColorSpace.Linear })
    foreach (DCFApixels.WhimTex.WhimTexGradientMode mode in System.Enum.GetValues(typeof(DCFApixels.WhimTex.WhimTexGradientMode)))
    foreach (DCFApixels.WhimTex.WhimTexGradientWrapMode wrap in System.Enum.GetValues(typeof(DCFApixels.WhimTex.WhimTexGradientWrapMode)))
    {
        layer.gradient.Mode = mode; layer.gradient.WrapMode = wrap; layer.gradient.ColorSpace = space;
        var copy = DCFApixels.WhimTex.GradientUtility.Create(layer.gradient);
        Check(!object.ReferenceEquals(copy, layer.gradient) && copy.Equals(layer.gradient),
            "Settings callback copies keys, mode and color space: " + mode + "/" + space);
        Check(copy.Mode == mode && copy.WrapMode == wrap && copy.ColorSpace == space, "Copied interpolation metadata");
        int originalHash = DCFApixels.WhimTex.GradientUtility.ComputeHash(copy);
        copy.Mode = mode == DCFApixels.WhimTex.WhimTexGradientMode.Classic ? DCFApixels.WhimTex.WhimTexGradientMode.Fixed : DCFApixels.WhimTex.WhimTexGradientMode.Classic;
        Check(originalHash != DCFApixels.WhimTex.GradientUtility.ComputeHash(copy), "Mode invalidates gradient hash");
        Check(layer.gradient.Mode == mode, "Independent copy does not modify original");
        copy.Mode = mode;
        copy.ColorSpace = space == UnityEngine.ColorSpace.Linear ? UnityEngine.ColorSpace.Gamma : UnityEngine.ColorSpace.Linear;
        Check(originalHash != DCFApixels.WhimTex.GradientUtility.ComputeHash(copy), "Color space invalidates gradient hash");
        foreach (DCFApixels.WhimTex.GradientLayerBehaviour.GradientType kind in
            System.Enum.GetValues(typeof(DCFApixels.WhimTex.GradientLayerBehaviour.GradientType)))
        {
            layer.gradientType = kind;
            layer.circularRepetitions = 3.2f;
            // Native PerceptualBlend quantizes RGB; interpolating the palette can differ
            // by one encoded 8-bit step (up to .009 in linear light near white).
            Compare(space + "/" + mode + "/" + wrap + "/" + kind, tolerance: mode == DCFApixels.WhimTex.WhimTexGradientMode.Perceptual ? .01f : .002f);
        }
    }
    layer.gradient.Mode = DCFApixels.WhimTex.WhimTexGradientMode.Classic;
    layer.gradientType = DCFApixels.WhimTex.GradientLayerBehaviour.GradientType.Circular;
    layer.circularWrapMode = DCFApixels.WhimTex.GradientLayerBehaviour.WrapMode.PingPong;
    Compare("Circular exact center and ping-pong");
    layer.circularRepetitions = 0; Compare("Zero repetitions");
    layer.gradientType = DCFApixels.WhimTex.GradientLayerBehaviour.GradientType.Radial;
    layer.gradient = null; Compare("Null fallback");
    layer.gradient = new DCFApixels.WhimTex.WhimTexGradient();
    layer.gradient.SetKeys(new[] {new UnityEngine.GradientColorKey(new UnityEngine.Color(-.5f, 2f, .2f), 0),
        new UnityEngine.GradientColorKey(new UnityEngine.Color(3f, -.2f, 1.2f), 1)},
        new[] {new UnityEngine.GradientAlphaKey(.2f, 0), new UnityEngine.GradientAlphaKey(.8f, 1)});
    Compare("Signed HDR");
    var palette = Palette(); uint revision = palette.updateCount;
    UnityEngine.Color paletteMiddle = palette.GetPixel(127, 0);
    for (int i = 0; i < 10; i++) { layer.circularRepetitions += .01f; var rt = Render(32, 16); UnityEngine.RenderTexture.ReleaseTemporary(rt); }
    Check(object.ReferenceEquals(palette, Palette()) && Palette().updateCount == revision, "Shape/size changes reuse the palette without uploads");
    var thumb = layer.GetPreviewTexture(18);
    Check(object.ReferenceEquals(thumb, layer.GetPreviewTexture(18)), "Stable thumbnail cache");
    Check(Palette().updateCount == revision, "Thumbnail does not invalidate GPU palette");
    layer.gradient.Mode = DCFApixels.WhimTex.WhimTexGradientMode.Fixed;
    Check(layer.GetPreviewTexture(18) != null && thumb == null, "Mode change invalidates thumbnail");
    var rtChanged = Render(32, 16); UnityEngine.RenderTexture.ReleaseTemporary(rtChanged);
    Check(Palette().GetPixel(127, 0) != paletteMiddle, "Mode change updates palette pixels");
    Compare("Updated fixed palette on GPU");
    palette = Palette();
    layerType.GetMethod("ReleaseTransientResources", flags).Invoke(layer, null);
    Check(palette == null && Palette() == null, "Release destroys palette");
    Compare("Recreate after release");
    var colors = new UnityEngine.GradientColorKey[8];
    var alphas = new UnityEngine.GradientAlphaKey[8];
    for (int i = 0; i < 8; i++)
    {
        colors[i] = new UnityEngine.GradientColorKey(new UnityEngine.Color(i / 7f, 1 - i / 7f, .4f), .01f + i * .13f);
        alphas[i] = new UnityEngine.GradientAlphaKey(i / 7f, .04f + i * .131f);
    }
    layer.gradient.SetKeys(colors, alphas);
    Compare("Maximum independent color/alpha keys, fixed");
    Check(Palette().height == 17, "Maximum palette interval count");
    layer.gradient.Mode = DCFApixels.WhimTex.WhimTexGradientMode.Classic;
    Compare("Maximum independent color/alpha keys, blend");
    var snapshot = layer.GetPreviewTexture(18);
    layer.gradient.ColorSpace = layer.gradient.ColorSpace == UnityEngine.ColorSpace.Linear ? UnityEngine.ColorSpace.Gamma : UnityEngine.ColorSpace.Linear;
    Check(layer.GetPreviewTexture(18) != null && snapshot == null, "Color-space-only change invalidates thumbnail");
    return "Gradient GPU checks passed: " + checks + "; max absolute difference=" + maxError;
}
catch (System.Exception exception) { throw new System.Exception("After " + checks + " checks: " + exception); }
finally
{
    layerType.GetMethod("ReleaseTransientResources", flags).Invoke(layer, null);
    UnityEngine.RenderTexture.active = previous; UnityEngine.GL.sRGBWrite = srgb;
    UnityEngine.RenderTexture.ReleaseTemporary(sentinel);
    UnityEngine.Object.DestroyImmediate(document);
}
