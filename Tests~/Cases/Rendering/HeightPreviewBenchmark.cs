using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// An opt-in playground, deliberately independent of the production preview pipeline.
public static class HeightPreviewBenchmark
{
    const string Cases = "Packages/com.dcfapixels.whimtex/Tests~/Cases/Rendering/";
    static readonly string[] Names = { "flat", "hills", "steps", "thin-ridge", "rough", "signed-hdr" };
    [Serializable] public sealed class Row
    {
        public string pattern, input, method;
        public int sourceSize, previewSize, steps, repeats;
        public double medianMs, p95Ms, submissionMedianMs, firstMs, geometrySetupMs, coverageIoU, heightMae, lightingMae;
        public double[] completedMs, submittedMs;
        public long outputLogicalBytes, outputUnityBytes, geometryBytes;
    }
    [Serializable] public sealed class Preparation
    {
        public string pattern;
        public double generateMs, reduceMs, reduceBatchMs, referenceConvergenceIoU, referenceConvergenceHeightMae, denseGridIoU, denseGridHeightMae, oracleHeightMax;
        public int oracleChecks;
        public long sourceUnityBytes, sourceLogicalBytes, reducedLogicalBytes;
    }
    [Serializable] public sealed class Report
    {
        public string unity, gpu, api, threading, directory;
        public string timing = "Warmed command-buffer batch submission plus completion via a separate 1x1 readback, divided by batch count. Not isolated GPU timestamps or editor FPS. Alternating method order. No full-resolution readback.";
        public string quality = "Against a per-screen-column piecewise-linear heightfield intersection at 2*sourceSize depth intervals: includes every bilinear source texel-center breakpoint for this axis-aligned orthographic view. Independent 1024/2048-cell raster grids check convergence. No subpixel coverage supersampling. The reference shares the parallel-horizon implementation, so grid convergence and serial/parallel agreement are reported separately.";
        public List<Row> rows = new();
        public List<Preparation> preparation = new();
    }

    public static string Run(int sourceSize, int previewSize, bool smoke) => TestContext.Run("GPU grid vs Floating Horizon height preview", t =>
    {
        t.True(SystemInfo.supportsComputeShaders, "Compute is supported");
        t.True(sourceSize <= 8192 && previewSize <= 512, "Bounded experiment");
        string directory = "Temp/WhimTex/height-preview/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        Directory.CreateDirectory(directory);
        var report = new Report { unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), threading = SystemInfo.renderingThreadingMode.ToString(), directory = directory };
        string assetFolder = "Assets/WhimTexHeightBenchmark_" + Guid.NewGuid().ToString("N");
        var objects = new List<Object>();
        var previous = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        var commands = new CommandBuffer { name = "WhimTex isolated height preview" };
        RenderTexture source = null, reduced = null, raster = null, horizon = null, fence = null;
        Texture2D cpuFence = null;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GL.sRGBWrite = false;
            string common = File.ReadAllText(Cases + "HeightPreviewCommon.hlsl");
            Directory.CreateDirectory(assetFolder);
            var shaders = new List<Shader>();
            Material MaterialFrom(string text, string name)
            {
                string path = assetFolder + "/" + name + ".shader";
                File.WriteAllText(path, text); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path); shaders.Add(shader);
                t.True(!ShaderUtil.ShaderHasError(shader), "Shader compiles");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; objects.Add(material); return material;
            }
            var grid = MaterialFrom(File.ReadAllText(Cases + "HeightPreviewRaster.shader").Replace("__COMMON__", common), "Grid");
            var meshGrid = new Material(grid) { hideFlags = HideFlags.HideAndDontSave }; meshGrid.EnableKeyword("GRID_MESH"); objects.Add(meshGrid);
            var meshes = new Dictionary<int, Mesh>(); var setupTimes = new Dictionary<int, double>();
            Mesh GetMesh(int steps)
            {
                if (meshes.TryGetValue(steps, out var found)) return found;
                var watch = Stopwatch.StartNew(); int side = steps + 1;
                var vertices = new Vector3[side * side]; var indices = new int[steps * steps * 6];
                for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) vertices[y * side + x] = new Vector3((float)x / steps, (float)y / steps);
                for (int y = 0; y < steps; y++) for (int x = 0; x < steps; x++)
                {
                    int i = (y * steps + x) * 6, v = y * side + x;
                    indices[i] = v; indices[i + 1] = v + side; indices[i + 2] = v + side + 1;
                    indices[i + 3] = v; indices[i + 4] = v + side + 1; indices[i + 5] = v + 1;
                }
                var mesh = new Mesh { name = "Owned height preview grid", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                objects.Add(mesh); mesh.vertices = vertices; mesh.triangles = indices; mesh.UploadMeshData(true);
                setupTimes[steps] = watch.Elapsed.TotalMilliseconds; meshes[steps] = mesh; return mesh;
            }
            var generator = MaterialFrom("Shader \"Hidden/WhimTex/Tests/HeightGenerate\"\n{\nSubShader\n{\nPass\n{\nCull Off\nZWrite Off\nZTest Always\nHLSLPROGRAM\n#pragma target 4.5\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n" + common + "\nfloat4 frag(v2f_img i):SV_Target{return Fixture(i.uv).xxxx;}\nENDHLSL\n}\n}\n}\n", "Generate");
            var columns = MaterialFrom("Shader \"Hidden/WhimTex/Tests/HeightColumns\"\n{\nSubShader\n{\nPass\n{\nCull Off\nZWrite Off\nZTest Always\nHLSLPROGRAM\n#pragma target 4.5\n#pragma vertex vert_img\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n" + common + "\nfloat4 frag(v2f_img i):SV_Target{int c=min(7,(int)(i.uv.x*8));uint x=((uint)_OutputSize*(24u+21u*(uint)c))/200u;float u=((x+.5)/_OutputSize-.5)/.84+.5;float v=floor(i.uv.y*_Steps)/(float)_Steps;return HeightAt(float2(u,v)).xxxx;}\nENDHLSL\n}\n}\n}\n", "Columns");
            // Only this owned compute asset is imported. No persistent material, scene or assembly.
            Directory.CreateDirectory(assetFolder);
            string computePath = assetFolder + "/Horizon.compute";
            File.WriteAllText(computePath, File.ReadAllText(Cases + "HeightPreviewHorizon.compute").Replace("__COMMON__", common) + "\n" + File.ReadAllText(Cases + "HeightPreviewHorizonParallel.hlsl"));
            AssetDatabase.ImportAsset(computePath, ImportAssetOptions.ForceSynchronousImport);
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(computePath);
            t.True(compute != null, "Owned compute asset imported");
            foreach (var message in ShaderUtil.GetComputeShaderMessages(compute)) t.True(false, "Compute diagnostic: " + message.message);
            int kernel = compute.FindKernel("RenderHorizon");
            t.True(compute.IsSupported(kernel), "Compute kernel supported");
            int parallelKernel = compute.FindKernel("RenderHorizonParallel");
            t.True(compute.IsSupported(parallelKernel), "Parallel horizon supported");

            RenderTexture Make(int size, RenderTextureFormat format, int depth = 0, bool random = false)
            {
                var rt = new RenderTexture(size, size, depth, format, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = random, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
                rt.Create(); objects.Add(rt); return rt;
            }
            source = Make(sourceSize, RenderTextureFormat.RHalf);
            reduced = Make(previewSize, RenderTextureFormat.RHalf);
            raster = Make(previewSize, RenderTextureFormat.ARGBFloat, 24);
            horizon = Make(previewSize, RenderTextureFormat.ARGBFloat, 0, true);
            fence = Make(1, RenderTextureFormat.ARGBFloat);
            cpuFence = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true); objects.Add(cpuFence);
            var packedColumns = new RenderTexture(8, sourceSize * 2, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point }; packedColumns.Create(); objects.Add(packedColumns);
            var cpuColumns = new Texture2D(8, sourceSize * 2, TextureFormat.RFloat, false, true); objects.Add(cpuColumns);
            void Complete(RenderTexture target)
            {
                Graphics.Blit(target, fence);
                RenderTexture.active = fence;
                cpuFence.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            }
            void Bind(RenderTexture input)
            {
                grid.SetTexture("_Height", input); grid.SetInt("_SourceSize", input.width); grid.SetFloat("_HeightScale", .5f);
                meshGrid.SetTexture("_Height", input); meshGrid.SetInt("_SourceSize", input.width); meshGrid.SetFloat("_HeightScale", .5f);
                compute.SetTexture(kernel, "_Height", input); compute.SetTexture(kernel, "_Output", horizon);
                compute.SetTexture(parallelKernel, "_Height", input); compute.SetTexture(parallelKernel, "_Output", horizon);
                compute.SetInt("_SourceSize", input.width); compute.SetInt("_OutputSize", previewSize); compute.SetFloat("_HeightScale", .5f);
            }
            void Build(bool useHorizon, int steps, int repeats, bool indexed = false, bool parallel = false)
            {
                commands.Clear(); grid.SetInt("_Steps", steps); compute.SetInt("_Steps", steps);
                for (int r = 0; r < repeats; r++)
                {
                    if (useHorizon) commands.DispatchCompute(compute, parallel ? parallelKernel : kernel, parallel ? previewSize : (previewSize + 63) / 64, 1, 1);
                    else
                    {
                        commands.SetRenderTarget(raster); commands.ClearRenderTarget(true, true, Color.clear);
                        if (indexed) commands.DrawMesh(GetMesh(steps), Matrix4x4.identity, meshGrid);
                        else commands.DrawProcedural(Matrix4x4.identity, grid, 0, MeshTopology.Triangles, steps * steps * 6);
                    }
                }
            }
            Color[] Read(RenderTexture target, string name)
            {
                var cpu = new Texture2D(previewSize, previewSize, TextureFormat.RGBAFloat, false, true);
                try
                {
                    RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, previewSize, previewSize), 0, 0, false);
                    Color[] values = cpu.GetPixels(); var visible = new Color[values.Length];
                    for (int p = 0; p < values.Length; p++)
                    {
                        t.True(float.IsFinite(values[p].r) && float.IsFinite(values[p].g), "Finite preview pixel");
                        visible[p] = values[p].a > .5f ? new Color(.72f * values[p].g, .83f * values[p].g, values[p].g, 1) : new Color(.08f, .08f, .08f, 1);
                    }
                    var png = new Texture2D(previewSize, previewSize, TextureFormat.RGBA32, false, true);
                    try { png.SetPixels(visible); png.Apply(); File.WriteAllBytes(directory + "/" + name + ".png", png.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(png); }
                    return values;
                }
                finally { Object.DestroyImmediate(cpu); }
            }
            Color[] DrawRead(bool fh, int steps, string name, bool indexed = false, bool parallel = false)
            { Build(fh, steps, 1, indexed, parallel); Graphics.ExecuteCommandBuffer(commands); return Read(fh ? horizon : raster, name); }
            void CheckOracle(Color[] reference, Preparation preparation)
            {
                columns.SetTexture("_Height", source); columns.SetInt("_OutputSize", previewSize); columns.SetInt("_Steps", sourceSize * 2);
                Graphics.Blit(null, packedColumns, columns); RenderTexture.active = packedColumns;
                cpuColumns.ReadPixels(new Rect(0, 0, 8, sourceSize * 2), 0, 0, false);
                var heights = cpuColumns.GetRawTextureData<float>();
                // Independent double-precision ray/segment intersection; no horizon or prefix scan.
                for (int column = 0; column < 8; column++)
                {
                    int x = (previewSize * (24 + 21 * column)) / 200;
                    for (int y = 0; y < previewSize; y++)
                    {
                        bool hit = false; double expectedHeight = 0;
                        double target = (y + .5) / previewSize;
                        for (int segment = 0; segment < sourceSize * 2; segment++)
                        {
                            double v0 = (double)segment / (sourceSize * 2);
                            double v1 = (double)(segment + 1) / (sourceSize * 2);
                            double h0 = heights[segment * 8 + column];
                            double h1 = heights[Math.Min(sourceSize * 2 - 1, segment + 1) * 8 + column];
                            double a = .5 + ((v0 - .5) * Math.Sqrt(2) + h0 * .5 / Math.Sqrt(2)) * .52;
                            double b = .5 + ((v1 - .5) * Math.Sqrt(2) + h1 * .5 / Math.Sqrt(2)) * .52;
                            if (Math.Abs(b - a) < 1e-12) continue;
                            double fraction = (target - a) / (b - a);
                            if (fraction < 0 || fraction >= 1) continue;
                            hit = true; expectedHeight = h0 + fraction * (h1 - h0); break;
                        }
                        Color value = reference[y * previewSize + x];
                        t.True(hit == (value.a > .5f), "CPU ray coverage: " + preparation.pattern + " " + x + "," + y);
                        if (hit)
                        {
                            double delta = Math.Abs(expectedHeight - value.r); preparation.oracleHeightMax = Math.Max(delta, preparation.oracleHeightMax);
                            t.True(delta < .002, "CPU ray height: " + preparation.pattern + " " + x + "," + y + " delta=" + delta);
                        }
                        preparation.oracleChecks++;
                    }
                }
            }

            int count = smoke ? 2 : Names.Length;
            Bind(source); compute.SetInt("_Steps", 17);
            int probe = compute.FindKernel("Probe"); compute.SetTexture(probe, "_Output", horizon);
            compute.Dispatch(probe, (previewSize + 7) / 8, (previewSize + 7) / 8, 1);
            Complete(horizon);
            Color probeValue = cpuFence.GetPixel(0, 0);
            t.Near(previewSize, probeValue.r, 0, "Compute output size binding");
            t.Near(17, probeValue.g, 0, "Compute step binding");
            t.Near(sourceSize, probeValue.b, 0, "Compute source size binding");
            for (int pattern = 0; pattern < count; pattern++)
            {
                generator.SetInt("_Pattern", pattern); generator.SetInt("_SourceSize", sourceSize);
                Complete(source); var sw = Stopwatch.StartNew(); Graphics.Blit(null, source, generator); Complete(source); double generation = sw.Elapsed.TotalMilliseconds;
                sw.Restart(); Graphics.Blit(source, reduced); Complete(reduced); double reduction = sw.Elapsed.TotalMilliseconds;
                var reductions = new double[9];
                commands.Clear(); for (int repeat = 0; repeat < 32; repeat++) commands.Blit(source, reduced);
                for (int sample = -2; sample < 9; sample++)
                { sw.Restart(); Graphics.ExecuteCommandBuffer(commands); Complete(reduced); if (sample >= 0) reductions[sample] = sw.Elapsed.TotalMilliseconds / 32; }
                var prep = new Preparation { pattern = Names[pattern], generateMs = generation, reduceMs = reduction,
                    reduceBatchMs = Percentile(reductions, .5),
                    sourceLogicalBytes = sourceSize * (long)sourceSize * 2, sourceUnityBytes = Profiler.GetRuntimeMemorySizeLong(source), reducedLogicalBytes = previewSize * (long)previewSize * 2 };
                Bind(source);
                var lowRef = DrawRead(false, smoke ? 256 : 1024, Names[pattern] + "-reference-low");
                var denseGrid = DrawRead(false, smoke ? 512 : 2048, Names[pattern] + "-dense-grid");
                Compare(lowRef, denseGrid, out prep.referenceConvergenceIoU, out prep.referenceConvergenceHeightMae, out _);
                var reference = DrawRead(true, sourceSize * 2, Names[pattern] + "-reference", false, true);
                Compare(denseGrid, reference, out prep.denseGridIoU, out prep.denseGridHeightMae, out _);
                CheckOracle(reference, prep);
                report.preparation.Add(prep);
                foreach (bool small in new[] { false, true })
                {
                    Bind(small ? reduced : source);
                    int[] gridSteps = smoke ? new[] { 64, 128 } : new[] { 64, 128, 256, 512 };
                    foreach (int steps in gridSteps)
                    {
                        var pair = new Row[4]; Color[] serialPixels = null;
                        for (int method = 0; method < 4; method++)
                        {
                            bool fh = method == 1 || method == 3; int actualSteps = fh ? steps * 4 : steps;
                            var row = new Row { pattern = Names[pattern], input = small ? "reduced" : "direct", sourceSize = sourceSize, previewSize = previewSize,
                                method = method == 3 ? "parallel-horizon" : fh ? "floating-horizon" : method == 2 ? "indexed-grid" : "gpu-grid", steps = actualSteps, repeats = smoke ? 8 : 32,
                                completedMs = new double[9], submittedMs = new double[9], geometryBytes = 0,
                                outputLogicalBytes = previewSize * (long)previewSize * (fh ? 16 : 20), outputUnityBytes = Profiler.GetRuntimeMemorySizeLong(fh ? horizon : raster) };
                            Build(fh, actualSteps, 1, method == 2, method == 3); sw.Restart(); Graphics.ExecuteCommandBuffer(commands); Complete(fh ? horizon : raster); row.firstMs = sw.Elapsed.TotalMilliseconds;
                            if (method == 2) { row.geometryBytes = Profiler.GetRuntimeMemorySizeLong(GetMesh(steps)); row.geometrySetupMs = setupTimes[steps]; }
                            var pixels = DrawRead(fh, actualSteps, Names[pattern] + "-" + row.input + "-" + row.method + "-" + actualSteps, method == 2, method == 3);
                            if (method == 1) serialPixels = pixels;
                            if (method == 3)
                            {
                                Compare(pixels, serialPixels, out double parallelIoU, out double parallelHeight, out double parallelLight);
                                t.True(parallelIoU > .9999 && parallelHeight < .0001 && parallelLight < .0002, "Parallel/serial horizon agree: " + Names[pattern] + " " + parallelIoU + "/" + parallelHeight + "/" + parallelLight);
                            }
                            Compare(pixels, reference, out row.coverageIoU, out row.heightMae, out row.lightingMae);
                            if (pattern == 0) t.True(row.coverageIoU > .99 && row.heightMae < 1e-6, "Flat-plane projection agrees: " + row.method + " iou=" + row.coverageIoU + " height=" + row.heightMae);
                            pair[method] = row;
                        }
                        // Alternate A/B and B/A, warm each exact command-buffer workload first.
                        for (int sample = -2; sample < 9; sample++) for (int order = 0; order < 4; order++)
                        {
                            int method = (sample & 1) == 0 ? order : 3 - order; Row row = pair[method]; bool fh = method == 1 || method == 3;
                            Build(fh, row.steps, row.repeats, method == 2, method == 3); Complete(fh ? horizon : raster);
                            sw.Restart(); Graphics.ExecuteCommandBuffer(commands); double submit = sw.Elapsed.TotalMilliseconds;
                            Complete(fh ? horizon : raster); double finish = sw.Elapsed.TotalMilliseconds;
                            if (sample >= 0) { row.submittedMs[sample] = submit / row.repeats; row.completedMs[sample] = finish / row.repeats; }
                        }
                        foreach (Row row in pair)
                        {
                            row.medianMs = Percentile(row.completedMs, .5); row.p95Ms = Percentile(row.completedMs, .95); row.submissionMedianMs = Percentile(row.submittedMs, .5);
                            t.True(row.medianMs > 0 && row.medianMs < 1000, "Finite bounded timing"); report.rows.Add(row);
                        }
                        WriteReport(directory, report);
                    }
                }
                // Highest depth fidelity retains source-pixel ridges; timed independently of reference readback.
                Bind(source);
                var exactRow = new Row { pattern = Names[pattern], input = "direct", method = "parallel-horizon", steps = sourceSize * 2,
                    sourceSize = sourceSize, previewSize = previewSize, repeats = 8, completedMs = new double[9], submittedMs = new double[9],
                    coverageIoU = 1, outputLogicalBytes = previewSize * (long)previewSize * 16, outputUnityBytes = Profiler.GetRuntimeMemorySizeLong(horizon) };
                Build(true, exactRow.steps, exactRow.repeats, false, true);
                for (int sample = -2; sample < 9; sample++)
                {
                    sw.Restart(); Graphics.ExecuteCommandBuffer(commands); double submit = sw.Elapsed.TotalMilliseconds; Complete(horizon);
                    if (sample >= 0) { exactRow.completedMs[sample] = sw.Elapsed.TotalMilliseconds / exactRow.repeats; exactRow.submittedMs[sample] = submit / exactRow.repeats; }
                }
                exactRow.medianMs = Percentile(exactRow.completedMs, .5); exactRow.p95Ms = Percentile(exactRow.completedMs, .95); exactRow.submissionMedianMs = Percentile(exactRow.submittedMs, .5);
                report.rows.Add(exactRow);
            }
            foreach (Shader shader in shaders)
                foreach (var warning in ShaderUtil.GetShaderMessages(shader)) t.True(false, "Shader diagnostic: " + warning.message);
            WriteReport(directory, report);
        }
        finally
        {
            commands.Release(); RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; ShaderUtil.allowAsyncCompilation = previousAsync;
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            if (AssetDatabase.IsValidFolder(assetFolder)) AssetDatabase.DeleteAsset(assetFolder);
        }
        t.True(!Directory.Exists(assetFolder), "Owned compute asset cleaned");
    });
    static void WriteReport(string directory, Report report)
    {
        // JsonUtility omits nested collections from ephemeral Pipeline assemblies.
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        string value = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
        File.WriteAllText(directory + "/report.json", value);
    }
    static double Percentile(double[] values, double p) { var copy = (double[])values.Clone(); Array.Sort(copy); return copy[(int)Math.Ceiling((copy.Length - 1) * p)]; }
    static void Compare(Color[] actual, Color[] expected, out double iou, out double height, out double lighting)
    {
        int intersection = 0, union = 0; height = lighting = 0;
        for (int i = 0; i < actual.Length; i++)
        {
            bool a = actual[i].a > .5f, b = expected[i].a > .5f; if (a || b) union++;
            if (a && b) { intersection++; height += Math.Abs(actual[i].r - expected[i].r); lighting += Math.Abs(actual[i].g - expected[i].g); }
        }
        iou = union == 0 ? 1 : (double)intersection / union; height /= Math.Max(1, intersection); lighting /= Math.Max(1, intersection);
    }
}
