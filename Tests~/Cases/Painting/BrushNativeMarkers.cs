using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

// Public portable CPU profiler recorders. No native plug-in or Editor-internal reflection.
// Two calls bracket an independently reviewed workload; an idle callback only cleans up.
public static class BrushNativeMarkers
{
    const string Key = "WhimTex.Tests.BrushNativeMarkers";
    static string DirectoryPath => Path.GetFullPath("Temp/WhimTex/brush-pipeline-benchmark");
    public static string Start() => TestContext.Run("Start bounded native marker capture", t =>
    {
        t.True(AppDomain.CurrentDomain.GetData(Key) == null, "No previous diagnostic recorder");
        t.True(!UnityEngine.Profiling.Profiler.enabled, "Do not interrupt an existing Unity CPU capture");
        var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
        var recorders = new Dictionary<string, ProfilerRecorder>();
        var names = new List<string>();
        try
        {
            foreach (var handle in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(handle);
                string name = d.Name;
                if (!(name.IndexOf("Gfx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Shader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Mesh", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("GC."))) continue;
                names.Add(d.Category.Name + " | " + d.UnitType + " | " + name);
                if (d.UnitType.ToString() != "TimeNanoseconds" || recorders.ContainsKey(name)) continue;
                var recorder = ProfilerRecorder.StartNew(d.Category, name, 256);
                if (recorder.Valid) recorders.Add(name, recorder); else recorder.Dispose();
                if (recorders.Count >= 192) break;
            }
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllLines(Path.Combine(DirectoryPath, "native-marker-candidates.txt"), names);
            t.True(recorders.Count > 0, "Supported CPU markers discovered");
            AppDomain.CurrentDomain.SetData(Key, recorders);
            double deadline = EditorApplication.timeSinceStartup + 90;
            void Cleanup()
            {
                if (AppDomain.CurrentDomain.GetData(Key) != recorders || EditorApplication.timeSinceStartup >= deadline)
                {
                    if (AppDomain.CurrentDomain.GetData(Key) == recorders) Finish(recorders, "timeout");
                    EditorApplication.update -= Cleanup;
                }
            }
            EditorApplication.update += Cleanup;
        }
        catch { foreach (var recorder in recorders.Values) recorder.Dispose(); AppDomain.CurrentDomain.SetData(Key, null); throw; }
    });
    public static string Stop() => TestContext.Run("Stop native marker capture", t =>
    {
        var recorders = AppDomain.CurrentDomain.GetData(Key) as Dictionary<string, ProfilerRecorder>;
        t.True(recorders != null, "Capture still owned and within deadline");
        Finish(recorders, "complete");
    });
    static void Finish(Dictionary<string, ProfilerRecorder> recorders, string reason)
    {
        AppDomain.CurrentDomain.SetData(Key, null);
        var rows = new List<string> { "reason\t" + reason, "name\tsamples\tmaxFrameMs\tsumMs\tcallCount" };
        bool any = false;
        try
        {
            foreach (var pair in recorders)
            {
                var recorder = pair.Value; recorder.Stop();
                long max = 0, sum = 0, calls = 0;
                for (int i = 0; i < recorder.Count; i++)
                { var s = recorder.GetSample(i); max = Math.Max(max, s.Value); sum += s.Value; calls += s.Count; }
                any |= calls > 0;
                rows.Add(pair.Key + "\t" + recorder.Count + "\t" + max / 1e6 + "\t" + sum / 1e6 + "\t" + calls);
            }
            string path = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-native-markers.tsv");
            File.WriteAllLines(path, rows); Debug.Log("Brush native marker capture: " + path);
            if (!any && reason != "timeout") throw new InvalidOperationException("No native CPU samples collected; capture is unavailable.");
        }
        finally { foreach (var recorder in recorders.Values) recorder.Dispose(); }
    }
}
