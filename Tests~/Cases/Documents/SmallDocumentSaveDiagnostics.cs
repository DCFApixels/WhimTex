using WhimTex.Tests;
using WhimTex.Tests.UnityD;
// Diagnostic only. Scheduling(): memory-only. Run(): requires permission for temporary Assets.
// Run(size, layers, repeats): full synchronous Save incl. import; no scene/window/user-document edits.
// Stopwatch only, no platform-specific memory APIs. No production algorithm changes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SmallDocumentSaveDiagnostics
{
    static TestContext context;
    static MigrationD fixture;
    public static string Scheduling(int repeats = 20, string reportSuffix = "scheduling")
        => Diagnostic("Diagnostic: scheduling timings; no performance acceptance threshold",
            () => MeasureScheduling(repeats, reportSuffix), report => Validate(report, repeats * 5));
    public static string Carrier(int size = 512, int repeats = 5)
        => Diagnostic("Diagnostic: native half carrier timings; no performance acceptance threshold",
            () => MeasureCarrier(size, repeats), report => Validate(report, repeats * 6));
    public static string Run(int size = 512, int layers = 3, int repeats = 5)
        => Diagnostic("Diagnostic: synchronous Save timings and no-op timestamp guard",
            () => MeasureRun(size, layers, repeats), report =>
        {
            Validate(report, 1 + repeats * 6);
            context.True(report.noOpTimestampPreserved, "Original no-op file timestamp guard");
            context.True(report.cleanupSucceeded, "Owned imported fixture removed");
        });
    static string SerializeReport(Report report)
        => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
            .GetMethod("SerializeObject", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(object) }, null).Invoke(null, new object[] { report });
    static string Diagnostic(string label, Func<Report> measure, Action<Report> validate)
    {
        Report report = null;
        var result = JsonUtility.FromJson<TestResult>(TestContext.Run(label, c =>
        {
            context = c;
            using (fixture = new MigrationD())
            {
                report = measure();
                validate(report);
            }
        }));
        // Append only after TestContext.Run has observed the complete owned finally/dispose verdict.
        // The transient file may be gone, but the actual samples/stages remain in this result.
        if (report != null)
        {
            try { result.message += "\n" + SerializeReport(report); }
            catch (Exception error)
            {
                result.status = "failed";
                var failures = new List<string>(result.failures ?? Array.Empty<string>());
                failures.Add(error.ToString()); // Preserve any original body/cleanup failure.
                result.failures = failures.ToArray();
                result.message += "\nDiagnostic serialization failed after owned cleanup";
            }
        }
        return result.ToJson();
    }
    static void Validate(Report report, int expectedSamples)
    {
        context.Equal(expectedSamples, report.samples.Count, "All original timed branches were sampled");
        foreach (var sample in report.samples)
            context.True(!double.IsNaN(sample.totalMs) && !double.IsInfinity(sample.totalMs) && sample.totalMs >= 0,
                "Diagnostic timer is finite: " + sample.operation);
        // Numeric timing results remain diagnostic and are reported without performance claims.
        context.True(report.repeats > 0, UnityEngine.JsonUtility.ToJson(report));
    }

    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Operation = typeof(WhimTexDocumentContainer).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentOperation");
    static readonly ConstructorInfo OperationCtor = Operation.GetConstructor(Any, null, new[] { typeof(string), typeof(Func<string, float, bool>) }, null);
    static readonly Action<Action> RunWork = (Action<Action>)Operation.GetMethod("Run", Any).CreateDelegate(typeof(Action<Action>));
    static readonly MethodInfo Prepare = typeof(WhimTexDocumentContainer).GetMethod("PrepareStoredBlocks", Any);
    [Serializable] public sealed class Stage { public string name; public double elapsedMs; }
    [Serializable] public sealed class Sample
    {
        public string operation, pipelineLog;
        public bool progressScope;
        public double totalMs;
        public List<Stage> stages = new List<Stage>();
    }
    [Serializable] public sealed class Report
    {
        public string unity, timestampUtc, input;
        public int size, layers, repeats;
        public long fileBytes;
        public int importedWidth, importedHeight;
        public string importedFormat;
        public bool noOpTimestampPreserved = true, cleanupSucceeded;
        public List<Sample> samples = new List<Sample>();
    }
    static IDisposable Scope(Func<string, float, bool> callback) =>
        (IDisposable)OperationCtor.Invoke(new object[] { "WhimTex save measurement", callback });

    private static Report MeasureScheduling(int repeats = 20, string reportSuffix = "scheduling")
    {
        if (repeats < 1 || repeats > 100) throw new ArgumentOutOfRangeException();
        if (string.IsNullOrEmpty(reportSuffix) || reportSuffix.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Report suffix must contain only letters, digits and hyphens.");
        var report = NewReport(0, 0, repeats);
        // Warm reflection, Task pool and code before measuring. Callback replaces only the progress UI.
        using (Scope((_, __) => false)) RunWork(() => { });
        for (int i = 0; i < repeats; i++)
            foreach (bool scope in new[] { false, true })
            {
                using var container = new WhimTexDocumentContainer();
                using var operation = scope ? Scope((_, __) => false) : null;
                var clock = Stopwatch.StartNew();
                RunWork(() => { });
                report.samples.Add(new Sample { operation = "empty-work", progressScope = scope, totalMs = clock.Elapsed.TotalMilliseconds });
                clock.Restart(); Prepare.Invoke(container, null);
                report.samples.Add(new Sample { operation = "empty-PrepareStoredBlocks", progressScope = scope, totalMs = clock.Elapsed.TotalMilliseconds });
            }
        for (int i = 0; i < repeats; i++)
        {
            // Isolated wait baseline without the application's progress/error-handling wrapper.
            var clock = Stopwatch.StartNew();
            var task = Task.Run(() => { });
            while (!task.Wait(20)) { }
            task.GetAwaiter().GetResult();
            report.samples.Add(new Sample { operation = "proposal-completion-wait", totalMs = clock.Elapsed.TotalMilliseconds });
        }
        WriteReport(report, reportSuffix);
        return report;
    }

    static Report NewReport(int size, int layers, int repeats) => new Report
    {
        unity = Application.unityVersion, timestampUtc = DateTime.UtcNow.ToString("O"),
        size = size, layers = layers, repeats = repeats,
        input = "RGBA32 paint-like synthetic: opaque base, translucent gradient patches above; unique per test"
    };
    private static Report MeasureCarrier(int size = 512, int repeats = 5)
    {
        if ((size != 512 && size != 1024) || repeats < 1 || repeats > 7) throw new ArgumentOutOfRangeException();
        var report = NewReport(size, 0, repeats);
        report.input = "Synthetic RGBAHalf within 0..1, native bytes only; NO render/import/disk";
        using var raw = new NativeArray<byte>(size * size * 8, Allocator.Persistent);
        var bytes = raw;
        for (int p = 0; p < bytes.Length; p += 2)
        {
            ushort value = (p % 8) == 6 ? (ushort)0x3c00 : (ushort)(0x3400 + ((p / 128) % 1024));
            bytes[p] = (byte)value; bytes[p + 1] = (byte)(value >> 8);
        }
        var precision = typeof(WhimTexTiffImage).GetMethod("HasValuesOutsideUnitRange", Any, null,
            new[] { typeof(NativeArray<byte>), typeof(int) }, null);
        var write = typeof(WhimTexTiffImage).GetMethod("WriteRawTo", Any);
        var validate = typeof(WhimTexTiffImage).GetMethod("ValidateStream", Any);
        // Warm JIT/static initialization, then measure separately from full Save.
        precision.Invoke(null, new object[] { raw, 16 });
        for (int i = 0; i < repeats; i++)
            foreach (bool scope in (i & 1) == 0 ? new[] { true, false } : new[] { false, true })
            {
                using var operation = scope ? Scope((_, __) => false) : null;
                var clock = Stopwatch.StartNew();
                RunWork(() => precision.Invoke(null, new object[] { raw, 16 }));
                report.samples.Add(new Sample { operation = "half-Auto-range-scan", progressScope = scope, totalMs = clock.Elapsed.TotalMilliseconds });
                using var stream = new MemoryStream();
                clock.Restart(); write.Invoke(null, new object[] { stream, size, size, raw, 16, 8, true, false });
                report.samples.Add(new Sample { operation = "half-to-LDR-tiff-with-LUT-and-compression", progressScope = scope, totalMs = clock.Elapsed.TotalMilliseconds });
                clock.Restart(); validate.Invoke(null, new object[] { stream });
                report.samples.Add(new Sample { operation = "TIFF-validate", progressScope = scope, totalMs = clock.Elapsed.TotalMilliseconds });
                report.fileBytes = stream.Length;
            }
        WriteReport(report, "carrier-" + size);
        return report;
    }
    static void WriteReport(Report report, string suffix)
    {
        string dir = fixture.TempFolder();
        Directory.CreateDirectory(dir);
        // JsonUtility omits generic lists of types compiled by run_script; use the public Json.NET assembly.
        string json = SerializeReport(report);
        File.WriteAllText(dir + "/small-save-" + suffix + ".json", json);
    }
    private static Report MeasureRun(int size = 512, int layers = 3, int repeats = 5)
    {
        if ((size != 512 && size != 1024) || layers < 1 || layers > 5 || repeats < 1 || repeats > 7)
            throw new ArgumentException("Probe bounds: 512/1024, 1-5 layers, 1-7 repeats.");
        string folder = fixture.AssetFolder();
        // AssetFolder already checked ownership and existence.
        // Fixture creates its owned GUID folder.
        var report = NewReport(size, layers, repeats);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = size;
        var ownedPixels = new List<Texture2D>();
        var activeSelection = Selection.objects;
        Sample active = null;
        string path = folder + "/Probe.tiff";
        Application.LogCallback log = (message, _, __) =>
        {
            if (active != null && message.StartsWith("WhimTex: saved " + path, StringComparison.Ordinal)) active.pipelineLog = message;
        };
        Application.logMessageReceived += log;
        try
        {
            int salt = (Guid.NewGuid().GetHashCode() & 127) + 1;
            for (int index = 0; index < layers; index++)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                ownedPixels.Add(texture);
                var raw = texture.GetRawTextureData<Color32>();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, v = y / (float)size;
                        float dx = u - (.25f + index * .08f), dy = v - (.4f + index * .05f);
                        float alpha = index == 0 ? 1 : Mathf.Clamp01((.32f - Mathf.Sqrt(dx * dx + dy * dy)) * 16f) * .7f;
                        raw[y * size + x] = new Color32((byte)((x / 8 + salt + index * 37) % 256),
                            (byte)((y / 8 + salt + index * 29) % 256), (byte)(40 + index * 21), (byte)(alpha * 255));
                    }
                texture.Apply(false, false);
                var drawing = new DrawingLayerBehaviour { brushSize = size / 24f, brushHardness = .8f, brushColor = Color.red };
                typeof(DrawingLayerBehaviour).GetField("pixels", Any).SetValue(drawing, texture);
                doc.layers.Add(new Layer(drawing));
            }
            void Save(string label, bool scope)
            {
                active = new Sample { operation = label, progressScope = scope };
                var clock = Stopwatch.StartNew();
                Func<string, float, bool> callback = (stage, _) =>
                {
                    if (active.stages.Count == 0 || active.stages[active.stages.Count - 1].name != stage)
                        active.stages.Add(new Stage { name = stage, elapsedMs = clock.Elapsed.TotalMilliseconds });
                    return false;
                };
                using (scope ? Scope(callback) : null) WhimTexDocumentFile.Save(doc, path, deferImport: false);
                clock.Stop(); active.totalMs = clock.Elapsed.TotalMilliseconds;
                report.samples.Add(active); active = null;
            }
            Save("first-save", true);
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            report.importedWidth = imported.width; report.importedHeight = imported.height; report.importedFormat = imported.format.ToString();
            var drawingLast = (DrawingLayerBehaviour)doc.layers[doc.layers.Count - 1].Behaviour;
            var parameters = typeof(DrawingLayerBehaviour).GetMethod("GetStrokeParameters", Any);
            var point = typeof(DrawingLayerBehaviour).GetMethod("PaintPoint", Any);
            int mutation = 0;
            for (int i = 0; i < repeats; i++)
                foreach (bool scope in (i & 1) == 0 ? new[] { true, false } : new[] { false, true })
                {
                    DateTime before = File.GetLastWriteTimeUtc(path);
                    Save("unchanged", scope);
                    report.noOpTimestampPreserved &= before == File.GetLastWriteTimeUtc(path);
                    doc.layers[0].layerName = "Renamed " + ++mutation;
                    Save("name-only", scope);
                    // Queue a real GPU brush stamp OUTSIDE the Save timer. Sync/readback stays INSIDE Save.
                    drawingLast.brushColor = (mutation & 1) == 0 ? Color.cyan : Color.magenta;
                    var stamp = parameters.Invoke(drawingLast, new object[] { false });
                    point.Invoke(drawingLast, new object[] { new Vector2(.1f + mutation * .03f, .6f), size, size, stamp });
                    Save("pending-brush-stamp", scope);
                }
            report.fileBytes = new FileInfo(path).Length;
            context.True(report.noOpTimestampPreserved, "Unexpected no-op file rewrite.");
            return report;
        }
        finally
        {
            Application.logMessageReceived -= log;
            if (doc != null) Object.DestroyImmediate(doc);
            foreach (var texture in ownedPixels) if (texture != null) Object.DestroyImmediate(texture);
            Selection.objects = activeSelection;
            string full = Path.GetFullPath(folder), assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetDirectoryName(full), Path.GetFullPath("Assets/WhimTexTestMigration"), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Test cleanup path escaped expected folder.");
            MigrationD.DeleteAsset(folder);
            report.cleanupSucceeded = !Directory.Exists(folder);
            WriteReport(report, size + "-" + layers);
            if (!report.cleanupSucceeded) throw new IOException("Test asset cleanup failed: " + folder);
        }
    }
}
