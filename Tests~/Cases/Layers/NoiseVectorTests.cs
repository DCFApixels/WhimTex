using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;

public static class NoiseVectorTests
{
    static Color[] Read(WhimTexDocument document)
    {
        Texture2D image = document.ComposeCanvas();
        try { return image.GetPixels(); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }

    static float Difference(Color[] a, Color[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Mathf.Abs(a[i].r - b[i].r)
            + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
        return sum / (a.Length * 3);
    }

    public static string Run(string suite = "render") => TestContext.Run("Noise vector " + suite, t =>
    {
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.width = document.height = 48;
        document.outputSrgb = false;
        document.outputPrecision = WhimTexOutputPrecision.Float32;
        var n = new NoiseLayerBehaviour { field = NoiseLayerBehaviour.Field.GradientVector,
            noiseType = NoiseLayerBehaviour.NoiseType.Perlin, vectorOutput = NoiseLayerBehaviour.VectorOutput.SignedVector,
            fractal = NoiseLayerBehaviour.FractalType.None, Scale3D = new Vector3(2, 3, 1), offset = new Vector3(.13f, .27f, .19f) };
        Layer layer = n;
        layer.colorRange = LayerColorRange.HDR;
        layer.blendRange = LayerBlendRange.HDR;
        document.layers.Add(layer);
        typeof(WhimTexDocument).GetMethod("NormalizeModel", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(document, null);
        RenderTexture active = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            if (suite == "render")
            {
                foreach (var dimension in new[] { NoiseLayerBehaviour.NoiseDimensions.TwoD, NoiseLayerBehaviour.NoiseDimensions.ThreeD })
                {
                    n.dimensions = dimension;
                    foreach (var field in new[] { NoiseLayerBehaviour.Field.GradientVector, NoiseLayerBehaviour.Field.Curl, NoiseLayerBehaviour.Field.CellDirection })
                    {
                        n.field = field;
                        Color[] signed = Read(document);
                        bool positive = false, negative = false;
                        foreach (Color c in signed)
                        {
                            t.True(float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b), "Finite " + dimension + "/" + field);
                            t.Near(1, c.a, .001, "Opaque vector");
                            if (dimension == NoiseLayerBehaviour.NoiseDimensions.TwoD) t.Near(0, c.b, 0, "2D zero Z");
                            positive |= c.r > .01f; negative |= c.r < -.01f;
                        }
                        t.True(positive && negative, "Signed field contains both directions: " + field);
                        n.vectorOutput = NoiseLayerBehaviour.VectorOutput.PackedVector;
                        var packed = Read(document);
                        n.vectorOutput = NoiseLayerBehaviour.VectorOutput.SignedVector;
                        n.inverted = true;
                        var inverted = Read(document);
                        n.inverted = false;
                        n.normalize = true; n.strength = 2;
                        var unit = Read(document);
                        for (int i = 0; i < signed.Length; i++)
                        {
                            for (int axis = 0; axis < 3; axis++)
                            {
                                t.Near(signed[i][axis] * .5 + .5, packed[i][axis], .004, "Packed linear identity");
                                t.Near(-signed[i][axis], inverted[i][axis], .004, "Inversion identity");
                            }
                            float length = new Vector3(unit[i].r, unit[i].g, unit[i].b).magnitude;
                            t.True(length < .0001 || Math.Abs(length - 2) < .005, "Normalize then Strength");
                        }
                        n.strength = 0;
                        foreach (Color c in Read(document)) t.Near(0, new Vector3(c.r, c.g, c.b).magnitude, 0, "Zero signed strength");
                        n.vectorOutput = NoiseLayerBehaviour.VectorOutput.PackedVector;
                        foreach (Color c in Read(document))
                            for (int axis = 0; axis < 3; axis++) t.Near(.5, c[axis], 0, "Zero packed strength");
                        n.vectorOutput = NoiseLayerBehaviour.VectorOutput.SignedVector;
                        n.normalize = false; n.strength = 1;
                    }
                }
            }
            else if (suite == "coverage2d" || suite == "coverage3d")
            {
                n.dimensions = suite == "coverage3d" ? NoiseLayerBehaviour.NoiseDimensions.ThreeD : NoiseLayerBehaviour.NoiseDimensions.TwoD;
                foreach (var kind in new[] { NoiseLayerBehaviour.NoiseType.OpenSimplex2, NoiseLayerBehaviour.NoiseType.OpenSimplex2S,
                    NoiseLayerBehaviour.NoiseType.Perlin, NoiseLayerBehaviour.NoiseType.ValueCubic, NoiseLayerBehaviour.NoiseType.Value })
                foreach (var field in new[] { NoiseLayerBehaviour.Field.GradientVector, NoiseLayerBehaviour.Field.Curl })
                {
                    if (!NoiseLayerBehaviour.SupportsNoiseType(field, kind)) continue;
                    n.noiseType = kind; n.field = field;
                    foreach (NoiseLayerBehaviour.FractalType fractal in Enum.GetValues(typeof(NoiseLayerBehaviour.FractalType)))
                    {
                        n.fractal = fractal; n.weightedStrength = 0;
                        var before = Read(document);
                        bool varying = false;
                        foreach (Color pixel in before)
                        {
                            t.True(float.IsFinite(pixel.r) && float.IsFinite(pixel.g) && float.IsFinite(pixel.b), "Finite " + field + "/" + kind + "/" + fractal);
                            varying |= Mathf.Abs(pixel.r) + Mathf.Abs(pixel.g) > .001f;
                        }
                        t.True(varying, "Nonconstant vector field " + kind);
                        if (fractal != NoiseLayerBehaviour.FractalType.None)
                        {
                            n.weightedStrength = .7f;
                            t.True(Difference(before, Read(document)) > .0001, "Weighted Strength affects " + field + "/" + kind + "/" + fractal);
                        }
                    }
                }
            }
            else if (suite == "grain")
            {
                n.field = NoiseLayerBehaviour.Field.Value;
                n.normalize = true; n.strength = 0; n.grainSize = 8;
                foreach (var kind in new[] { NoiseLayerBehaviour.NoiseType.WhiteNoise, NoiseLayerBehaviour.NoiseType.BlueNoise })
                foreach (var dimension in new[] { NoiseLayerBehaviour.NoiseDimensions.OneD, NoiseLayerBehaviour.NoiseDimensions.TwoD })
                foreach (NoiseLayerBehaviour.GrainColor color in Enum.GetValues(typeof(NoiseLayerBehaviour.GrainColor)))
                {
                    n.noiseType = kind; n.dimensions = dimension; n.grainColor = color;
                    bool differentChannels = false, varying = false;
                    var grain = Read(document);
                    foreach (Color pixel in grain)
                    {
                        for (int axis = 0; axis < 3; axis++) t.True(float.IsFinite(pixel[axis]) && pixel[axis] >= 0 && pixel[axis] <= 1, "Valid grain data " + kind);
                        t.Near(1, pixel.a, .001, "Grain opaque");
                        differentChannels |= Mathf.Abs(pixel.r - pixel.g) > .01f;
                        varying |= Mathf.Abs(pixel.r - grain[0].r) > .01f;
                        if (color == NoiseLayerBehaviour.GrainColor.Monochrome)
                        {
                            t.Near(pixel.r, pixel.g, 0, "Monochrome G");
                            t.Near(pixel.r, pixel.b, 0, "Monochrome B");
                        }
                    }
                    t.True(varying, "Grain ignores vector Strength and stays nonconstant");
                    t.Equal(color == NoiseLayerBehaviour.GrainColor.Color, differentChannels, "Shared Grain Color uniform " + kind + "/" + dimension);
                }
            }
            else if (suite == "seam")
            {
                var materials = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexMaterials");
                var shared = (Material)materials.GetProperty("Noise", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
                var surface = RenderTexture.GetTemporary(17, 17, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var readback = new Texture2D(17, 17, TextureFormat.RGBAFloat, false, true);
                Material probe = null;
                try
                {
                    n.periodic = NoiseLayerBehaviour.PeriodicAxes.XY;
                    n.warp = NoiseLayerBehaviour.WarpType.BasicGrid; n.warpStrength = .7f;
                    foreach (var dimension in new[] { NoiseLayerBehaviour.NoiseDimensions.TwoD, NoiseLayerBehaviour.NoiseDimensions.ThreeD })
                    foreach (var field in new[] { NoiseLayerBehaviour.Field.GradientVector, NoiseLayerBehaviour.Field.Curl, NoiseLayerBehaviour.Field.CellDirection })
                    {
                        n.dimensions = dimension; n.field = field;
                        n.fractal = NoiseLayerBehaviour.FractalType.FBm; n.weightedStrength = .7f;
                        Read(document);
                        probe = new Material(shared);
                        foreach (string key in new[] { "_NoiseOneD", "_NoisePeriodic", "_NoiseSeed", "_NoiseType", "_NoiseFractal", "_NoiseOctaves",
                            "_NoiseCellularDistance", "_NoiseCellularReturn", "_NoiseWarp", "_NoiseEncoding", "_NoiseInverted", "_UseGradient",
                            "_NoiseField", "_NoiseVectorOutput", "_NoiseNormalize", "_NoiseWarpSeed" }) probe.SetInteger(key, shared.GetInteger(key));
                        foreach (string key in new[] { "_NoiseDomain", "_NoiseScale", "_NoiseAxis", "_NoiseFractalSettings", "_NoiseWarpInverse", "_NoiseWarpScale" })
                            probe.SetVector(key, shared.GetVector(key));
                        foreach (string key in new[] { "_NoiseZ", "_NoiseCellularJitter", "_NoiseWarpStrength", "_NoiseStrength" }) probe.SetFloat(key, shared.GetFloat(key));
                        probe.SetVectorArray("_NoiseLattice", shared.GetVectorArray("_NoiseLattice"));
                        probe.SetInteger("_UnboundedUv", 1);
                        Color[] Sample(float x, float y)
                        {
                            probe.SetVector("_UvRow0", new Vector4(17f / 16, 0, x - .28125f, 0));
                            probe.SetVector("_UvRow1", new Vector4(0, 17f / 16, y - .15625f, 0));
                            probe.SetVector("_UvRow2", new Vector4(0, 0, 1, 0));
                            GL.sRGBWrite = false; Graphics.Blit(null, surface, probe); RenderTexture.active = surface;
                            readback.ReadPixels(new Rect(0, 0, 17, 17), 0, 0, false);
                            return readback.GetPixels();
                        }
                        var baseline = Sample(0, 0);
                        foreach (var shift in new[] { new Vector2(1, 0), new Vector2(0, -1), new Vector2(1, 1) })
                        {
                            var repeated = Sample(shift.x, shift.y);
                            for (int i = 0; i < repeated.Length; i++) for (int axis = 0; axis < 3; axis++)
                                t.Near(baseline[i][axis], repeated[i][axis], .005, "Seamless vector " + dimension + "/" + field);
                        }
                        UnityEngine.Object.DestroyImmediate(probe); probe = null;
                    }
                }
                finally
                {
                    if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
                    UnityEngine.Object.DestroyImmediate(readback);
                    RenderTexture.active = active; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(surface);
                }
            }
            else if (suite == "math")
            {
                n.dimensions = NoiseLayerBehaviour.NoiseDimensions.TwoD;
                n.field = NoiseLayerBehaviour.Field.GradientVector;
                var gradient = Read(document);
                n.field = NoiseLayerBehaviour.Field.Curl;
                var curl = Read(document);
                for (int i = 0; i < gradient.Length; i++)
                {
                    t.Near(gradient[i].g, curl[i].r, .001, "2D Curl dY");
                    t.Near(-gradient[i].r, curl[i].g, .001, "2D Curl -dX");
                }
                n.field = NoiseLayerBehaviour.Field.Value;
                var scalar = Read(document);
                float error = 0;
                for (int y = 1; y < document.height - 1; y++) for (int x = 1; x < document.width - 1; x++)
                {
                    int i = y * document.width + x;
                    error += Mathf.Abs(gradient[i].r - (scalar[i + 1].r - scalar[i - 1].r) * document.width / 3);
                    error += Mathf.Abs(gradient[i].g - (scalar[i + document.width].r - scalar[i - document.width].r) * document.height / 3);
                }
                t.True(error / ((document.width - 2) * (document.height - 2) * 2) < .025, "Gradient agrees with scalar derivative including Scale ratios");
                n.field = NoiseLayerBehaviour.Field.CellDirection;
                n.cellularJitter = 0; n.Scale3D = Vector3.one; n.offset = new Vector3(.13f, .27f, 0);
                foreach (NoiseLayerBehaviour.CellularDistance metric in Enum.GetValues(typeof(NoiseLayerBehaviour.CellularDistance)))
                {
                    n.cellularDistance = metric;
                    var cells = Read(document);
                    for (int y = 0; y < document.height; y++) for (int x = 0; x < document.width; x++)
                    {
                        float px = (x + .5f) / document.width - .5f + n.offset.x;
                        float py = (y + .5f) / document.height - .5f + n.offset.y;
                        Color c = cells[y * document.width + x];
                        t.Near(Mathf.Floor(px + .5f) - px, c.r, .001, "Cell X nearest center " + metric);
                        t.Near(Mathf.Floor(py + .5f) - py, c.g, .001, "Cell Y nearest center " + metric);
                    }
                }
                n.dimensions = NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                n.field = NoiseLayerBehaviour.Field.GradientVector; n.cellularDistance = NoiseLayerBehaviour.CellularDistance.EuclideanSquared;
                int originalSeed = n.seed;
                var a = Read(document);
                n.seed = originalSeed ^ 0x68bc21eb; var b = Read(document);
                n.seed = originalSeed ^ 0x63d83595; var c3 = Read(document);
                n.seed = originalSeed; n.field = NoiseLayerBehaviour.Field.Curl; var curl3 = Read(document);
                for (int i = 0; i < curl3.Length; i++)
                {
                    t.Near(c3[i].g - b[i].b, curl3[i].r, .004, "3D Curl X");
                    t.Near(a[i].b - c3[i].r, curl3[i].g, .004, "3D Curl Y");
                    t.Near(b[i].r - a[i].g, curl3[i].b, .004, "3D Curl Z");
                }
            }
            else if (suite == "warp")
            {
                n.dimensions = NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                n.offset.z = .4f;
                foreach (var field in new[] { NoiseLayerBehaviour.Field.Value, NoiseLayerBehaviour.Field.GradientVector,
                    NoiseLayerBehaviour.Field.Curl, NoiseLayerBehaviour.Field.CellDirection })
                foreach (var periodic in new[] { NoiseLayerBehaviour.PeriodicAxes.None, NoiseLayerBehaviour.PeriodicAxes.XY })
                foreach (var warp in new[] { NoiseLayerBehaviour.WarpType.BasicGrid, NoiseLayerBehaviour.WarpType.OpenSimplex2, NoiseLayerBehaviour.WarpType.OpenSimplex2Reduced })
                {
                    n.field = field; n.periodic = periodic; n.warp = warp; n.warpStrength = .7f;
                    n.WarpScale3D = new Vector3(2, 3, 1); n.warpSeed = 1337;
                    var initial = Read(document);
                    n.warpSeed = int.MinValue;
                    t.True(Difference(initial, Read(document)) > .001, "Independent Warp Seed " + field + "/" + periodic + "/" + warp);
                    n.warpSeed = 1337; n.warpScaleZ = 3;
                    t.True(Difference(initial, Read(document)) > .001, "Warp Scale Z " + field + "/" + periodic + "/" + warp);
                    n.warpStrength = 0; var disabled = Read(document);
                    n.warpSeed = int.MaxValue; n.warpScaleZ = 5;
                    t.Near(0, Difference(disabled, Read(document)), 0, "Zero Warp ignores seed and scale");
                }
            }
            else if (suite == "integration")
            {
                var plain = Read(document);
                var group = new GroupLayerBehaviour();
                Layer groupLayer = group;
                groupLayer.colorRange = LayerColorRange.HDR;
                groupLayer.blendRange = LayerBlendRange.HDR;
                group.layers.Add(layer); document.layers.Clear(); document.layers.Add(groupLayer);
                foreach (var mode in new[] { GroupCompositing.PassThrough, GroupCompositing.Isolated })
                {
                    group.compositing = mode;
                    t.Near(0, Difference(plain, Read(document)), .001, "Group keeps signed data " + mode);
                }
                var processor = new ShaderProcessorLayerBehaviour();
                document.layers.Insert(0, processor);
                t.Near(0, Difference(plain, Read(document)), .001, "Processor receives signed composite");
                document.layers.RemoveAt(0);
                Layer basis = new ColorFillLayerBehaviour { color = new Color(1, 1, 1, .25f) };
                group.layers.Add(basis); layer.clippingMask = true;
                foreach (Color pixel in Read(document)) t.Near(.25, pixel.a, .001, "Vector clipping keeps coverage");
                layer.clippingMask = false; group.layers.Remove(basis);
                var targeted = new NormalMapLayerBehaviour { inputMode = EffectInputMode.Specific, TargetLayerId = layer.Id,
                    inputSpace = NormalMapLayerBehaviour.InputSpace.Linear, sourceChannel = NormalMapLayerBehaviour.HeightChannel.Red,
                    output = NormalMapLayerBehaviour.OutputMode.Height, encoding = NormalMapLayerBehaviour.OutputEncoding.LinearData, smoothing = 0 };
                document.layers.Insert(0, targeted);
                var before = Read(document);
                n.field = NoiseLayerBehaviour.Field.Curl;
                t.True(Difference(before, Read(document)) > .01, "Specific target invalidates Field");
                n.warp = NoiseLayerBehaviour.WarpType.BasicGrid; n.warpStrength = .7f;
                before = Read(document); n.warpSeed++;
                t.True(Difference(before, Read(document)) > .001, "Specific target invalidates Warp Seed");
                document.layers.RemoveAt(0);
                var thumbnail = n.GetPreviewTexture(24);
                n.field = NoiseLayerBehaviour.Field.GradientVector;
                var next = n.GetPreviewTexture(24);
                t.True(thumbnail == null && next != null, "Field invalidates thumbnail");
                thumbnail = next; n.warpScaleZ = 3; next = n.GetPreviewTexture(24);
                t.True(thumbnail == null && next != null, "Warp Z invalidates thumbnail even while hidden");
                n.vectorOutput = NoiseLayerBehaviour.VectorOutput.PackedVector;
                n.normalize = true; n.strength = 1;
                Texture2D exported = document.ComposeCanvas(), decoded = new Texture2D(2, 2);
                try
                {
                    var assembly = typeof(WhimTexDocument).Assembly;
                    var encoder = assembly.GetType("DCFApixels.WhimTex.WhimTexRasterEncoder");
                    var png = Enum.Parse(assembly.GetType("DCFApixels.WhimTex.RasterImageFormat"), "Png");
                    var bytes = (byte[])encoder.GetMethod("Encode", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { exported, png, 95, Texture2D.EXRFlags.CompressZIP });
                    t.True(decoded.LoadImage(bytes), "Packed PNG decodes through product encoder");
                    t.Equal(document.width, decoded.width, "Export width");
                    t.Equal(document.height, decoded.height, "Export height");
                }
                finally { UnityEngine.Object.DestroyImmediate(exported); UnityEngine.Object.DestroyImmediate(decoded); }
            }
            else if (suite == "storage")
            {
                n.field = NoiseLayerBehaviour.Field.Curl; n.dimensions = NoiseLayerBehaviour.NoiseDimensions.ThreeD;
                n.WarpScale3D = new Vector3(2, 3, 4); n.warpSeed = -981; n.normalize = true; n.strength = 2.7f;
                foreach (WhimTexJsonWriteMode mode in Enum.GetValues(typeof(WhimTexJsonWriteMode)))
                {
                    n.warp = NoiseLayerBehaviour.WarpType.BasicGrid;
                    using var copy = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json);
                    var saved = (NoiseLayerBehaviour)copy.Document.layers[0].Behaviour;
                    t.Equal(n.field, saved.field, "JSON field " + mode);
                    t.Equal(n.vectorOutput, saved.vectorOutput, "JSON vector encoding " + mode);
                    t.Equal(n.normalize, saved.normalize, "JSON Normalize " + mode);
                    t.Equal(n.strength, saved.strength, "JSON Strength " + mode);
                    t.Equal(n.warpSeed, saved.warpSeed, "JSON Warp Seed " + mode);
                    t.Equal(n.WarpScale3D, saved.WarpScale3D, "JSON Warp XYZ " + mode);
                }
                var types = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json;
                t.True(types.Contains("\"Curl\""), "Compact retains field");
                n.strength = 1; n.normalize = false; n.warpSeed = 1337; n.WarpScale3D = Vector3.one;
                using (var compact = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Compact }).Json))
                {
                    var saved = (NoiseLayerBehaviour)compact.Document.layers[0].Behaviour;
                    t.Equal(1f, saved.strength, "Omitted strength defaults to 1");
                    t.Equal(1337, saved.warpSeed, "Omitted Warp Seed defaults to 1337");
                    t.Equal(Vector3.one, saved.WarpScale3D, "Omitted Warp Z defaults to 1");
                }
                var loaded = (WhimTexDocument)typeof(WhimTexDocumentFile).GetMethod("CreateEditableCopy", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { document });
                try
                {
                    var saved = (NoiseLayerBehaviour)loaded.layers[0].Behaviour;
                    t.Equal(n.field, saved.field, "Binary model field");
                    t.Equal(n.WarpScale3D, saved.WarpScale3D, "Binary model Warp XYZ");
                    t.Equal(n.vectorOutput, saved.vectorOutput, "Binary model signed output");
                }
                finally { UnityEngine.Object.DestroyImmediate(loaded); }
            }
            else throw new ArgumentException("Unknown Noise vector suite: " + suite);
            t.Equal(active, RenderTexture.active, "Render target restored");
            t.Equal(srgb, GL.sRGBWrite, "Color write state restored");
        }
        finally { UnityEngine.Object.DestroyImmediate(document); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });
}
