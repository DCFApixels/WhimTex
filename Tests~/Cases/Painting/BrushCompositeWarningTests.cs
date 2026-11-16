using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class BrushCompositeWarningTests
{
    const string Package = "Packages/com.dcfapixels.whimtex/";
    const string Folder = "Temp/WhimTex/brush-composite-warning/";
    const int Width = 19, Height = 13;
    [Serializable] public sealed class Manifest
    {
        public string sourceHash, fixtureHash, blendHash, hdrHash, outputHash, unity, device;
        public int cases, bytes;
        public string[] diagnostics;
    }
    struct Case
    {
        public int mode, channels, selection, fallback;
        public bool hdr, stamp, pressure, erase, emptyRect;
        public string Name => $"mode={mode} hdr={hdr} stamp={stamp} pressure={pressure} channels={channels} selection={selection} fallback={fallback} erase={erase} emptyRect={emptyRect}";
    }
    static IEnumerable<Case> Cases()
    {
        foreach (bool hdr in new[] { false, true })
            foreach (BlendMode mode in Enum.GetValues(typeof(BlendMode)))
                foreach (bool stamp in new[] { false, true })
                    yield return new Case { mode = (int)mode, hdr = hdr, stamp = stamp,
                        pressure = (int)mode % 2 == 0, channels = 15, selection = 1 };
        for (int channels = 0; channels < 16; channels++)
            for (int fallback = 0; fallback < 3; fallback++)
                yield return new Case { mode = (int)BlendMode.Multiply, hdr = true,
                    stamp = (channels & 1) != 0, pressure = (channels & 2) != 0,
                    channels = channels, fallback = fallback, selection = channels % 5 };
        for (int selection = 0; selection < 5; selection++)
            yield return new Case { hdr = true, erase = true, channels = 15, selection = selection };
        yield return new Case { hdr = true, channels = 15, emptyRect = true };
    }
    static string Hash(byte[] data)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static string FileHash(string file) => Hash(File.ReadAllBytes(Package + file));
    static Texture2D Image(Func<int, int, Color> pixel)
    {
        var image = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var values = new Color[Width * Height];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) values[y * Width + x] = pixel(x, y);
        image.SetPixels(values); image.Apply(false, false); return image;
    }
    static float Alpha(int x) => x % 5 == 0 ? 0 : x % 5 == 1 ? .000001f : x % 5 == 2 ? .125f : x % 5 == 3 ? .5f : .9f;
    static Color Base(int x, int y) => new Color(-.2f - .01f * x, 1.5f + .03f * y, .17f + .005f * (x + y), 1);
    static void Configure(ComputeShader compute, Material raster, Case c)
    {
        void F(string n, float v) { compute.SetFloat(n, v); raster.SetFloat(n, v); }
        void V(string n, Vector4 v) { compute.SetVector(n, v); raster.SetVector(n, v); }
        F("_PendingBrush", 1); F("_StrokeOpacity", .63f); F("_StrokeMode", c.mode);
        F("_HdrBlend", c.hdr ? 1 : 0); F("_BrushStandard", c.hdr ? 0 : 1);
        F("_BrushErase", c.erase ? 1 : 0); F("_BrushStampAccumulation", c.stamp ? 1 : 0);
        F("_BrushPressureOpacity", c.pressure ? 1 : 0); F("_BrushUseSelection", c.selection == 0 ? 0 : 1);
        F("_BrushSelectionWrap", c.selection == 2 ? 1 : 0); F("_BrushStraightFallback", c.fallback);
        F("_BrushGamma", c.fallback == 2 ? 1 : 0);
        V("_BrushWriteChannels", new Vector4(c.channels & 1, (c.channels >> 1) & 1, (c.channels >> 2) & 1, (c.channels >> 3) & 1));
        V("_BrushCanvasRow0", new Vector4(1, .15f, c.selection == 2 ? -.35f : c.selection == 3 ? -2 : 0, 0));
        V("_BrushCanvasRow1", new Vector4(.1f, 1, c.selection == 2 ? .2f : 0, 0));
        V("_BrushCanvasRow2", new Vector4(0, 0, c.selection == 4 ? 0 : 1, 0));
        V("_BrushDirtyRect", c.emptyRect ? new Vector4(2, 2, 3, 3) : new Vector4(.2f, .2f, .8f, .8f));
        V("_BrushStrokeMap", new Vector4(1, 1, 0, 0));
        V("_BrushCommitRect", new Vector4(0, 0, Width, Height));
        V("_BrushSurfaceSize", new Vector4(Width, Height, 1f / Width, 1f / Height));
    }

    public static string Run(bool compare) => TestContext.Run("BrushComposite exact baseline and compiler diagnostics", test =>
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var owned = new List<Object>(); ComputeBuffer coverage = null;
        try
        {
            string compositePath = "src/Shaders/BrushComposite.cginc";
            var manifest = new Manifest { sourceHash = FileHash(compositePath),
                fixtureHash = FileHash("Tests~/Cases/Painting/BrushCompositeWarningTests.cs"),
                blendHash = FileHash("src/Shaders/ColorBlend.cginc"), hdrHash = FileHash("src/Shaders/HdrColor.cginc"),
                unity = Application.unityVersion, device = SystemInfo.graphicsDeviceType.ToString() };
            Manifest baseline = null; byte[] expected = null;
            if (compare)
            {
                baseline = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "baseline.json"));
                expected = File.ReadAllBytes(Folder + "baseline.bin");
                test.Equal(baseline.outputHash, Hash(expected), "Authenticated pre-change pixels");
                test.Equal(baseline.fixtureHash, manifest.fixtureHash, "Same fixture before and after");
                test.Equal(baseline.blendHash, manifest.blendHash, "Blend arithmetic unchanged");
                test.Equal(baseline.hdrHash, manifest.hdrHash, "HDR arithmetic unchanged");
                test.Equal(baseline.device, manifest.device, "Same active graphics backend");
                test.True(baseline.sourceHash != manifest.sourceHash, "Candidate differs from recorded source");
            }
            else test.True(!File.Exists(Folder + "baseline.bin"), "Never overwrite a captured baseline");
            var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(Package + "src/Shaders/BrushCommit.compute");
            test.True(asset != null && SystemInfo.supportsComputeShaders, "Production compute available");
            var compute = Object.Instantiate(asset); owned.Add(compute);
            var shader = Shader.Find("Hidden/WhimTex/Blend"); test.True(shader != null, "Production raster available");
            var raster = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(raster);
            var before = Image((x, y) => { var b = Base(x, y); float a = Alpha(x); return new Color(b.r * a, b.g * a, b.b * a, a); }); owned.Add(before);
            var original = Image((x, y) => { var b = Base(x, y); b.a = Alpha(x); return b; }); owned.Add(original);
            var stroke = Image((x, y) => { float a = Alpha(y); return new Color((-.35f - .011f * x) * a, (1.35f + .013f * y) * a, (.28f + .007f * (x + y)) * a, a); }); owned.Add(stroke);
            var selection = Image((x, y) => new Color(x % 4 / 3f, 0, 0, 1)); owned.Add(selection);
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true, filterMode = FilterMode.Point }; owned.Add(target);
            test.True(target.Create(), "Owned Half target allocated");
            var read = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(read);
            coverage = new ComputeBuffer(1, 4); var zero = new uint[1];
            foreach (string kernelName in new[] { "Commit", "CommitTracked", "ScanCoverage", "ClearCoverage" })
                test.True(compute.IsSupported(compute.FindKernel(kernelName)), kernelName + " supported");
            raster.SetTexture("_BrushStroke", stroke); raster.SetTexture("_BrushSelection", selection); raster.SetTexture("_BrushOriginalStraight", original);
            using var output = new MemoryStream();
            foreach (string backend in new[] { "Commit", "CommitTracked", "Raster" })
            foreach (var c in Cases())
            {
                Configure(compute, raster, c); GL.sRGBWrite = false;
                if (backend == "Raster") Graphics.Blit(before, target, raster, 1);
                else
                {
                    int kernel = compute.FindKernel(backend); Graphics.CopyTexture(before, target);
                    compute.SetTexture(kernel, "_BrushSurface", target); compute.SetTexture(kernel, "_BrushStroke", stroke);
                    compute.SetTexture(kernel, "_BrushSelection", selection); compute.SetTexture(kernel, "_BrushOriginalStraight", original);
                    if (backend == "CommitTracked")
                    { coverage.SetData(zero); compute.SetBuffer(kernel, "_AlphaCoverage", coverage); compute.SetInt("_CoverageColumns", 1); }
                    compute.Dispatch(kernel, (Width + 7) / 8, (Height + 7) / 8, 1);
                }
                RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                byte[] actual = read.GetRawTextureData<byte>().ToArray();
                if (compare)
                {
                    test.True(output.Position + actual.Length <= expected.Length, "Baseline contains this case");
                    int offset = (int)output.Position, differences = 0;
                    for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[offset + i]) differences++;
                    test.Equal(0, differences, backend + " " + c.Name + " exact Half bytes");
                }
                output.Write(actual, 0, actual.Length); manifest.cases++;
            }
            var diagnostics = new List<string>();
            foreach (var message in ShaderUtil.GetComputeShaderMessages(asset))
                diagnostics.Add(message.severity + ": " + message.message);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                diagnostics.Add(message.severity + ": " + message.message);
            manifest.diagnostics = diagnostics.ToArray(); byte[] pixels = output.ToArray();
            manifest.bytes = pixels.Length; manifest.outputHash = Hash(pixels);
            test.True(manifest.cases > 400, "Blend/channel/selection/pressure/erase matrix exercised");
            if (compare)
            {
                test.Equal(baseline.cases, manifest.cases, "Same case count");
                test.Equal(baseline.outputHash, manifest.outputHash, "All pre-change output bytes preserved");
                test.Equal(0, diagnostics.Count, "No compute/raster warnings or errors: " + string.Join("; ", diagnostics));
            }
            Directory.CreateDirectory(Folder);
            if (!compare)
            {
                File.WriteAllBytes(Folder + "baseline.bin", pixels);
                File.WriteAllText(Folder + "BrushComposite.before.cginc", File.ReadAllText(Package + compositePath));
            }
            File.WriteAllText(Folder + (compare ? "after.json" : "baseline.json"), JsonUtility.ToJson(manifest, true));
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb; coverage?.Dispose();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
    });
}
