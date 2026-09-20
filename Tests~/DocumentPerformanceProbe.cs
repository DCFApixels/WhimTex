// run_script entry DocumentPerformanceProbe.Run, args [size, layers, hdr, randomPixels].
// Samples whole-Editor CPU memory (not GPU memory, not an isolated process). Unique assets cleaned in finally.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DocumentPerformanceProbe
{
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    [StructLayout(LayoutKind.Sequential)] struct MemoryCounters
    {
        public uint cb, pageFaults;
        public UIntPtr peakWorking, working, peakPaged, paged, peakNonPaged, nonPaged, pageFile, peakPageFile, privateBytes;
    }
    [DllImport("psapi.dll", SetLastError = true)] static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    static void ReadMemory(Process process, out long privateBytes, out long working)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var counters = new MemoryCounters { cb = (uint)Marshal.SizeOf<MemoryCounters>() };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.cb)) throw new System.ComponentModel.Win32Exception();
            privateBytes = (long)counters.privateBytes.ToUInt64(); working = (long)counters.working.ToUInt64();
        }
        else { process.Refresh(); privateBytes = process.PrivateMemorySize64; working = process.WorkingSet64; }
    }
    [Serializable] public sealed class Measurement
    {
        public string operation;
        public double milliseconds, baselinePrivateMiB, peakPrivateMiB, peakWorkingSetMiB, peakManagedMiB;
    }
    [Serializable] public sealed class Report
    {
        public string unity, graphics;
        public int size, layers, loadedLayers;
        public int importedWidth, importedHeight;
        public string importedFormat;
        public bool hdr, randomPixels, unchangedKeptTimestamp;
        public long fileBytes;
        public List<Measurement> measurements = new List<Measurement>();
        public List<string> saveStages = new List<string>();
    }
    static Measurement Measure(string operation, Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        ReadMemory(process, out long baseline, out long peakWorking);
        long peakPrivate = baseline, peakManaged = GC.GetTotalMemory(false);
        using var stop = new ManualResetEvent(false);
        Exception samplingError = null;
        var thread = new Thread(() => {
            try {
                do {
                    ReadMemory(process, out long privateBytes, out long working);
                    peakPrivate = Math.Max(peakPrivate, privateBytes);
                    peakWorking = Math.Max(peakWorking, working); peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                } while (!stop.WaitOne(10));
            } catch (Exception error) { samplingError = error; }
        }) { IsBackground = true, Name = "WhimTex memory probe" };
        thread.Start(); var clock = Stopwatch.StartNew();
        try { action(); }
        finally { clock.Stop(); stop.Set(); thread.Join(); }
        if (samplingError != null) throw new InvalidOperationException("Memory sampling failed.", samplingError);
        return new Measurement { operation = operation, milliseconds = clock.Elapsed.TotalMilliseconds,
            baselinePrivateMiB = baseline / 1048576.0, peakPrivateMiB = peakPrivate / 1048576.0,
            peakWorkingSetMiB = peakWorking / 1048576.0, peakManagedMiB = peakManaged / 1048576.0 };
    }
    public static Report Run(int size = 2048, int layers = 3, bool hdr = false, bool randomPixels = true)
    {
        if (size < 32 || size > 8192 || layers < 1 || layers > 4 || (long)size * size * (hdr ? 8 : 4) > 256L * 1048576 ||
            (long)size * size * layers * (hdr ? 8 : 4) > 512L * 1048576)
            throw new ArgumentException("Probe budget exceeded.");
        string folder = "Assets/WhimTexPerformance_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = size; doc.height = size;
        TextureCompositor loaded = null;
        var ownedPixels = new List<Texture2D>();
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(), size = size, layers = layers, hdr = hdr, randomPixels = randomPixels };
        Application.LogCallback log = (message, _, type) => {
            if (message.StartsWith("WhimTex: saved " + folder, StringComparison.Ordinal)) report.saveStages.Add(message);
        };
        Application.logMessageReceived += log;
        try
        {
            for (int index = 0; index < layers; index++)
            {
                var texture = new Texture2D(size, size, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                ownedPixels.Add(texture);
                uint state = 19U + (uint)index;
                if (hdr)
                {
                    var data = texture.GetRawTextureData<ushort>();
                    for (int i = 0; i < data.Length; i += 4)
                    {
                        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                        ushort v = randomPixels ? (ushort)(0x3800 + (state & 2047)) : (ushort)0x4000;
                        data[i] = v; data[i + 1] = v; data[i + 2] = 0x4000; data[i + 3] = 0x3c00;
                    }
                }
                else
                {
                    var data = texture.GetRawTextureData<Color32>();
                    for (int i = 0; i < data.Length; i++)
                    {
                        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                        data[i] = randomPixels ? new Color32((byte)state, (byte)(state >> 8), (byte)(state >> 16), 255) : new Color32(32, 128, 64, 255);
                    }
                }
                texture.Apply(false, false);
                var drawing = new DrawingLayerBehaviour();
                typeof(DrawingLayerBehaviour).GetField("pixels", Any).SetValue(drawing, texture);
                doc.layers.Add(new Layer(drawing) { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                    blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard });
            }
            string path = folder + "/Benchmark.whimtex.tiff";
            report.measurements.Add(Measure("first_save_including_import", () => WhimTexDocumentFile.Save(doc, path)));
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            report.importedWidth = imported.width; report.importedHeight = imported.height; report.importedFormat = imported.format.ToString();
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            report.measurements.Add(Measure("unchanged_save", () => WhimTexDocumentFile.Save(doc, path)));
            report.unchangedKeptTimestamp = File.GetLastWriteTimeUtc(path) == timestamp;
            var pixels = (Texture2D)typeof(DrawingLayerBehaviour).GetField("pixels", Any).GetValue(doc.layers[0].Behaviour);
            pixels.SetPixel(size / 2, size / 2, hdr ? new Color(3, 0, 0, 1) : Color.red); pixels.Apply(false, false);
            typeof(DrawingLayerBehaviour).GetMethod("InvalidatePaintSurface", Any).Invoke(doc.layers[0].Behaviour, null);
            report.measurements.Add(Measure("one_changed_layer_save", () => WhimTexDocumentFile.Save(doc, path)));
            report.fileBytes = new FileInfo(path).Length;
            Object.DestroyImmediate(doc); doc = null;
            report.measurements.Add(Measure("open_all_drawing_pixels", () => loaded = WhimTexDocumentFile.Load(path)));
            report.loadedLayers = loaded.layers.Count;
            string output = "Temp/WhimTex/TiffValidationResults"; Directory.CreateDirectory(output);
            // run_script sees Localization's embedded Json.NET as well; select the public assembly explicitly.
            string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")
                .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            File.WriteAllText(output + "/perf-" + size + "-" + layers + "-" + (hdr ? "hdr" : "ldr") + "-" + randomPixels + "-streaming.json", json);
            if (loaded.layers.Count != layers || !report.unchangedKeptTimestamp)
                throw new Exception("Performance probe correctness check failed; inspect the saved report.");
            return report;
        }
        finally
        {
            Application.logMessageReceived -= log;
            if (doc != null) Object.DestroyImmediate(doc);
            if (loaded != null) Object.DestroyImmediate(loaded);
            foreach (var texture in ownedPixels) if (texture != null) Object.DestroyImmediate(texture);
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
