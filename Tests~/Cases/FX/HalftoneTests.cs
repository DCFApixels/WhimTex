using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class HalftoneTests
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int W = 80, H = 64;
    const float CellSize = 13.7f, Spread = 7.3f, LayoutAngle = 31f;
    static readonly float[] Angles = { 37f, -28f, 90f, -17f };
    static readonly Vector2[] ManualOffsets = { new Vector2(3.7f, -2.1f), new Vector2(-4.2f, 5.3f), new Vector2(1.13f, 2.73f), new Vector2(-2.4f, -3.6f) };

    public static string Run() => TestContext.Run("Halftone cell sampling", t =>
    {
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        RenderTexture previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        RenderTexture output = null;
        try
        {
            var doc = Own(ScriptableObject.CreateInstance<WhimTexDocument>());
            doc.width = W; doc.height = H;
            string source = File.ReadAllText("Packages/com.dcfapixels.whimtex/src/FXPresets/Halftone.hlsl");
            var create = typeof(ShaderFX).GetMethod("CreateAgentDraft", Hidden, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null);
            var fx = Own((ShaderFX)create.Invoke(null, new object[] { doc, source, new List<ShaderFXParameter>() }));
            typeof(ShaderFX).GetMethod("ApplyAgentDraft", Hidden).Invoke(fx, null);
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden).GetValue(fx);
            ShaderFXParameter P(string name) => parameters.Find(p => p.name == name);
            var shader = (Shader)typeof(ShaderFX).GetField("compiledShader", Hidden).GetValue(fx);
            t.True(shader != null && !ShaderUtil.ShaderHasError(shader), "Halftone compiles");
            t.Equal(ShaderFXParameterType.Bool, P("_FixedShape").controls[0].type, "Fixed Shape uses a standard checkbox");
            t.Near(0, P("_FixedShape").floatValue, 0, "Continuous sampling remains the default");
            t.True(P("_FixedShape").controls[0].inGroup && !P("_FixedShape").controls[0].hidden &&
                string.IsNullOrEmpty(P("_FixedShape").controls[0].visibleIfParameter), "Checkbox is available in every screen mode");
            P("_DotSize").floatValue = CellSize;
            P("_PlateSpread").floatValue = Spread; P("_PlateRotation").floatValue = LayoutAngle;
            P("_Angle").floatValue = Angles[0];
            string[] cmyk = { "Cyan", "Magenta", "Yellow", "Black" }, rgb = { "Red", "Green", "Blue" };
            for (int i = 0; i < cmyk.Length; i++)
            {
                P("_" + cmyk[i] + "Angle").floatValue = Angles[i];
                P("_" + cmyk[i] + "Offset").vectorValue = ManualOffsets[i];
            }
            for (int i = 0; i < rgb.Length; i++)
            {
                P("_" + rgb[i] + "Angle").floatValue = Angles[i];
                P("_" + rgb[i] + "Offset").vectorValue = ManualOffsets[i];
            }
            var input = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var read = Own(new Texture2D(W, H, TextureFormat.RGBAFloat, false, true));
            var pixels = new Color[W * H];
            // Keep blue dominant so zero-ink early exits do not make AA derivatives divergent in CMYK.
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                pixels[y * W + x] = new Color(.05f + .3f * ((7 * x + 3 * y) % 17) / 16f,
                    .05f + .35f * ((2 * x + 5 * y) % 13) / 12f, .7f + .2f * ((3 * x + 2 * y) % 11) / 10f,
                    .2f + .6f * x / (W - 1f));
            input.SetPixels(pixels); input.Apply();
            output = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var contextType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.LayerRenderContext", true);
            var getMaterial = typeof(ShaderFX).GetMethod("GetMaterial", Hidden);
            float biggestToggleChange = 0;
            for (int scale = 1; scale <= 2; scale++)
            {
                doc.width = W * scale; doc.height = H * scale;
                object renderContext = Activator.CreateInstance(contextType, doc, null, W, H, 1f / scale, true, true, null);
                for (int mode = 0; mode < 6; mode++) for (int shape = 0; shape < 3; shape++)
                {
                    P("_Mode").floatValue = mode; P("_DotShape").floatValue = shape;
                    Color[] continuous = null;
                    for (int fixedShape = 0; fixedShape <= 1; fixedShape++)
                    {
                        P("_FixedShape").floatValue = fixedShape;
                        var material = (Material)getMaterial.Invoke(fx, new[] { renderContext });
                        t.True(material != null, "Applied material is available");
                        GL.sRGBWrite = false; Graphics.Blit(input, output, material);
                        RenderTexture.active = output; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); read.Apply();
                        Color[] actual = read.GetPixels();
                        double worst = 0;
                        string worstPixel = "";
                        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                        {
                            Color lower = Reference(pixels, x, y, scale, mode, shape, fixedShape != 0, out Color upper);
                            for (int channel = 0; channel < 4; channel++)
                            {
                                float value = actual[y * W + x][channel];
                                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Nonfinite output");
                                double difference = Math.Max(0, Math.Max(lower[channel] - value, value - upper[channel]));
                                if (difference > worst) { worst = difference; worstPixel = $" at {x},{y} channel {channel}: expected {lower[channel]}..{upper[channel]}, got {value}"; }
                            }
                            if (continuous != null) biggestToggleChange = Mathf.Max(biggestToggleChange,
                                Mathf.Abs(actual[y * W + x].r - continuous[y * W + x].r));
                        }
                        t.Near(0, worst, .006f, $"All-pixel reference: scale {scale}, mode {mode}, shape {shape}, fixed {fixedShape}" + worstPixel);
                        if (fixedShape == 0) continuous = actual;
                    }
                }
            }
            t.True(biggestToggleChange > .5f, "Fixed Shape visibly changes cells with internal color transitions");
            t.True(!ShaderUtil.ShaderHasError(shader), "All screen modes render without shader errors");
            Layer layer = new ColorFillLayerBehaviour(); layer.fx.Add(fx); doc.layers.Add(layer);
            typeof(Layer).GetMethod("AssignNewId", Hidden).Invoke(layer, null);
            P("_FixedShape").floatValue = 1;
            foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
            {
                using var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc, new WhimTexJsonWriteOptions { Mode = mode }).Json, false);
                var restored = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden).GetValue(loaded.Document.layers[0].fx[0]);
                t.Near(1, restored.Find(p => p.name == "_FixedShape").floatValue, 0, "Checkbox JSON roundtrip: " + mode);
            }
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (output != null) RenderTexture.ReleaseTemporary(output);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
        }
    });

    static Vector2 Rotate(Vector2 point, float degrees)
    {
        float angle = degrees * Mathf.Deg2Rad, sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
        return new Vector2(cosine * point.x - sine * point.y, sine * point.x + cosine * point.y);
    }

    static Vector2 Offset(int mode, int plate)
    {
        if (mode == 0) return Vector2.zero;
        if (mode == 1 || mode == 5) return ManualOffsets[plate];
        Vector2 position;
        if (mode == 3) position = new Vector2((plate & 1) == 0 ? -.5f : .5f, plate < 2 ? -.5f : .5f);
        else position = plate == 0 ? new Vector2(0, -.57735027f) : plate == 1 ? new Vector2(-.5f, .28867513f) :
            plate == 2 ? new Vector2(.5f, .28867513f) : Vector2.zero;
        return Rotate(position * Spread, LayoutAngle);
    }

    static Color Sample(Color[] pixels, Vector2 point, int scale)
        => pixels[Mathf.Clamp(Mathf.FloorToInt(point.y / scale), 0, H - 1) * W + Mathf.Clamp(Mathf.FloorToInt(point.x / scale), 0, W - 1)];

    static float Threshold(Vector2 pixel, float angle, int shape)
    {
        Vector2 screen = Rotate(pixel, angle) / CellSize;
        Vector2 cell = new Vector2(screen.x - Mathf.Floor(screen.x) - .5f, screen.y - Mathf.Floor(screen.y) - .5f);
        if (shape == 2) return 2 * Mathf.Abs(cell.y);
        if (shape == 1) { float radius = Mathf.Max(Mathf.Abs(cell.x), Mathf.Abs(cell.y)); return 4 * radius * radius; }
        float squared = cell.sqrMagnitude, reach = Mathf.Sqrt(Mathf.Max(squared - .25f, 0));
        return squared * (Mathf.PI - 4 * Mathf.Atan2(reach, .5f)) + 2 * reach;
    }

    static Vector2 Plate(Color[] pixels, int x, int y, int scale, int mode, int shape, bool fixedShape, int plate)
    {
        Vector2 pixel = new Vector2(x + .5f, y + .5f) * scale, offset = Offset(mode, plate);
        Vector2 inputPoint = pixel - offset, patternPoint = fixedShape ? inputPoint : pixel;
        if (fixedShape)
        {
            Vector2 grid = Rotate(inputPoint, Angles[plate]) / CellSize;
            inputPoint = Rotate(new Vector2(Mathf.Floor(grid.x) + .5f, Mathf.Floor(grid.y) + .5f) * CellSize, -Angles[plate]);
        }
        Color source = Sample(pixels, inputPoint, scale);
        float ink;
        if (mode == 0) ink = 1 - (.2126f * source.r + .7152f * source.g + .0722f * source.b);
        else if (mode >= 4) ink = source[plate];
        else
        {
            float black = 1 - Mathf.Max(source.r, Mathf.Max(source.g, source.b));
            ink = plate == 3 ? black : Mathf.Clamp01((1 - source[plate] - black) / Mathf.Max(1 - black, .00001f));
        }
        if (ink <= 0) return Vector2.zero;
        if (ink >= 1) return Vector2.one;
        float threshold = Threshold(patternPoint, Angles[plate], shape);
        Vector2 quadOrigin = new Vector2((x & ~1) + .5f, (y & ~1) + .5f) * scale - (fixedShape ? offset : Vector2.zero);
        float t00 = Threshold(quadOrigin, Angles[plate], shape);
        float t10 = Threshold(quadOrigin + new Vector2(scale, 0), Angles[plate], shape);
        float t01 = Threshold(quadOrigin + new Vector2(0, scale), Angles[plate], shape);
        float t11 = Threshold(quadOrigin + new Vector2(scale, scale), Angles[plate], shape);
        // Accept fine/coarse derivatives within the quad, without assuming one graphics backend's AA choice.
        float dx0 = Mathf.Abs(t10 - t00), dx1 = Mathf.Abs(t11 - t01);
        float dy0 = Mathf.Abs(t01 - t00), dy1 = Mathf.Abs(t11 - t10);
        float narrow = Mathf.Max(Mathf.Min(dx0, dx1) + Mathf.Min(dy0, dy1), .0001f);
        float wide = Mathf.Max(Mathf.Max(dx0, dx1) + Mathf.Max(dy0, dy1), .0001f);
        float Coverage(float edge)
        {
            float weight = Mathf.Clamp01((threshold - ink + edge) / (2 * edge));
            return 1 - weight * weight * (3 - 2 * weight);
        }
        float first = Coverage(narrow), second = Coverage(wide);
        return new Vector2(Mathf.Min(first, second), Mathf.Max(first, second));
    }

    static Color Reference(Color[] pixels, int x, int y, int scale, int mode, int shape, bool fixedShape, out Color upper)
    {
        Vector2 first = Plate(pixels, x, y, scale, mode, shape, fixedShape, 0);
        float alpha = pixels[y * W + x].a;
        if (mode == 0)
        {
            upper = new Color(1 - first.x, 1 - first.x, 1 - first.x, alpha);
            return new Color(1 - first.y, 1 - first.y, 1 - first.y, alpha);
        }
        Vector2 second = Plate(pixels, x, y, scale, mode, shape, fixedShape, 1);
        Vector2 third = Plate(pixels, x, y, scale, mode, shape, fixedShape, 2);
        if (mode >= 4)
        {
            upper = new Color(first.y, second.y, third.y, alpha);
            return new Color(first.x, second.x, third.x, alpha);
        }
        Vector2 black = Plate(pixels, x, y, scale, mode, shape, fixedShape, 3);
        upper = new Color((1 - first.x) * (1 - black.x), (1 - second.x) * (1 - black.x), (1 - third.x) * (1 - black.x), alpha);
        return new Color((1 - first.y) * (1 - black.y), (1 - second.y) * (1 - black.y), (1 - third.y) * (1 - black.y), alpha);
    }
}
