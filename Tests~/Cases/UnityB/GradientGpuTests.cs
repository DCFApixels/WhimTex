using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using DCFApixels.WhimTex;

public static class GradientGpuTests
{
    static string Execute()
    {
// Unity Pipeline eval_file. Transient objects only; no scene/asset writes or Undo.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var document = UnityBRun.Create<DCFApixels.WhimTex.TextureCompositor>();
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
void Check(bool value, string message) { UnityBRun.Check(!(!value), message); checks++; }
UnityEngine.Texture2D Palette() => (UnityEngine.Texture2D)layerType.GetField("palette", flags).GetValue(layer);
UnityEngine.RenderTexture Render(int w, int h)
{
    var context = System.Activator.CreateInstance(contextType, new object[] { document, null, w, h, 1f, false, false, null });
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
        actual = UnityBRun.Track(new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBAFloat, false, true));
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
void CompareFixedBoundaries()
{
    // Feed an exact uniform coordinate: no raster interpolation, radius or pixel-center rounding.
    // Test every boundary and both sides with the original color tolerance.
    var temporary = Render(1, 1); UnityEngine.RenderTexture.ReleaseTemporary(temporary);
    var material = UnityBRun.Track(new UnityEngine.Material(UnityEngine.Shader.Find("Hidden/TextureCompositor/Gradient")));
    var output = UnityBRun.Track(new UnityEngine.RenderTexture(1, 1, 0, UnityEngine.RenderTextureFormat.ARGBFloat, UnityEngine.RenderTextureReadWrite.Linear));
    var readback = UnityBRun.Track(new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBAFloat, false, true));
    var encode = typeof(DCFApixels.WhimTex.WhimTexGradient).GetMethod("EvaluateEncoded", flags);
    var decode = layerType.Assembly.GetType("DCFApixels.WhimTex.HdrUtility").GetMethod("Decode",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    try
    {
        output.Create();
        material.SetTexture("_GradientPalette", Palette());
        material.SetVectorArray("_GradientIntervals", (UnityEngine.Vector4[])layerType.GetField("paletteIntervals", flags).GetValue(layer));
        material.SetInt("_GradientIntervalCount", (int)layerType.GetField("paletteIntervalCount", flags).GetValue(layer));
        material.SetVector("_GradientStart", (UnityEngine.Color)layerType.GetField("paletteStart", flags).GetValue(layer));
        material.SetInt("_GradientType", 1);
        material.SetInt("_GradientWrapMode", (int)layer.gradient.WrapMode);
        material.SetVector("_GradientOutputSize", UnityEngine.Vector4.one);
        material.SetInt("_UnboundedUv", 1);
        material.SetVector("_UvRow1", new UnityEngine.Vector4(0, 0, .5f, 0));
        material.SetVector("_UvRow2", new UnityEngine.Vector4(0, 0, 1, 0));
        var times = new System.Collections.Generic.List<float> { 0, 1 };
        foreach (var key in layer.gradient.ColorKeys) times.Add(key.time);
        foreach (var key in layer.gradient.AlphaKeys) times.Add(key.time);
        foreach (float key in times)
        foreach (float offset in new[] { -2f, -1f, 0f, 1f, 2f })
        foreach (float side in new[] { -0.000001f, 0f, 0.000001f })
        {
            float time = key + offset + side;
            material.SetVector("_UvRow0", new UnityEngine.Vector4(0, 0, time, 0));
            UnityEngine.GL.sRGBWrite = false;
            UnityEngine.Graphics.Blit(null, output, material);
            UnityEngine.RenderTexture.active = output;
            readback.ReadPixels(new UnityEngine.Rect(0, 0, 1, 1), 0, 0, false);
            var encoded = (UnityEngine.Color)encode.Invoke(layer.gradient, new object[] { time });
            var expected = (UnityEngine.Color)decode.Invoke(null, new object[] { encoded });
            var actual = readback.GetPixel(0, 0);
            for (int c = 0; c < 4; c++)
                Check(UnityEngine.Mathf.Abs(actual[c] - expected[c]) <= .002f * UnityEngine.Mathf.Max(1, UnityEngine.Mathf.Abs(expected[c])),
                    "Exact Fixed boundary " + layer.gradient.ColorSpace + "/" + layer.gradient.WrapMode + " t=" + time.ToString("R") +
                    " channel " + c + ": expected=" + expected[c] + " actual=" + actual[c]);
        }
    }
    finally
    {
        UnityEngine.RenderTexture.active = sentinel;
        output.Release(); UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(material);
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
        if (mode == DCFApixels.WhimTex.WhimTexGradientMode.Fixed) CompareFixedBoundaries();
    }
    layer.gradient.Mode = DCFApixels.WhimTex.WhimTexGradientMode.Classic;
    layer.gradientType = DCFApixels.WhimTex.GradientLayerBehaviour.GradientType.Circular;
    layer.circularWrapMode = DCFApixels.WhimTex.GradientLayerBehaviour.WrapMode.PingPong;
    Compare("Circular exact center and ping-pong");
    layer.circularRepetitions = 0; Compare("Zero repetitions");
    layer.gradientType = DCFApixels.WhimTex.GradientLayerBehaviour.GradientType.Radial;
    layer.gradient = null; Compare("Null fallback");
    // The strict signed-HDR case tests Classic; all modes are covered above.
    layer.gradient = new DCFApixels.WhimTex.WhimTexGradient { Mode = DCFApixels.WhimTex.WhimTexGradientMode.Classic };
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
    // Avoid a radial/square sample exactly on t=.4 where CPU/GPU shape rounding differs.
    // Exact jump semantics are tested separately with uniform coordinates, not a larger color tolerance.
    Compare("Maximum independent color/alpha keys, fixed", 64, 34);
    CompareFixedBoundaries();
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

return "";
    }
    public static string Run() => UnityBRun.Run("GradientGpuSmoke", () => Execute());
}

