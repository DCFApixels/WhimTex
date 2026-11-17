using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Sequential model/viewport diagnostics; the renderer is never replaced by a test implementation.
public static class PencilStageBenchmark
{
    const string Key = "WhimTex.Tests.PencilStages";
    [Serializable] sealed class Status { public string status, message; public int checks; public string[] failures; }
    [Serializable] sealed class Manifest
    {
        public string runId, status, error, sourceHash, adapterHash, fixtureHash;
        public bool profile;
        public int checks;
        public List<string> runs = new();
    }
    static string Hash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    static string Json(object value) => (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    public static string Poll(string id)
    {
        var state = AppDomain.CurrentDomain.GetData(Key) as object[];
        return state != null && (string)state[0] == id ? (string)state[1] : Json(new Status { status = "failed", failures = new[] { "Unknown Pencil run" } });
    }
    public static string Cancel(string id)
    {
        var state = AppDomain.CurrentDomain.GetData(Key) as object[];
        if (state != null && (string)state[0] == id && state[2] is Action cancel) cancel();
        return Poll(id);
    }
    public static string Start(string id, bool profile) => StartCore(id, profile, false, false);
    public static string StartMotion(string id, bool correctness) => StartCore(id, false, true, correctness);
    public static string StartMotionProfile(string id) => StartCore(id, true, true, false);
    static string StartCore(string id, bool profile, bool motion, bool correctness)
    {
        var previous = AppDomain.CurrentDomain.GetData(Key) as object[];
        if (previous != null && previous[2] != null) throw new InvalidOperationException("Pencil diagnostic already active");
        var state = new object[] { id, "", null }; AppDomain.CurrentDomain.SetData(Key, state);
        var report = new Manifest { runId = id + (motion ? correctness ? "-motion-exact" : "-motion-timing" : "-stages") + (profile ? "-profile" : ""), profile = profile, status = "running",
            sourceHash = Hash("Packages/com.dcfapixels.whimtex/src/Layers/DrawingLayerBehaviour.cs"),
            adapterHash = Hash("Packages/com.dcfapixels.whimtex/src/Layers/DrawingLayerBehaviour.TiledBrush.cs"),
            fixtureHash = Hash("Packages/com.dcfapixels.whimtex/Tests~/Cases/Painting/TiledDrawingCycleBenchmark.cs") };
        var cases = new List<TiledDrawingCycleBenchmark.Config>();
        if (motion)
        {
            foreach (int size in new[] { 1, 9 }) foreach (int length in correctness ? new[] { 6500 } : new[] { 250, 1000, 6500 })
            foreach (bool outside in new[] { false, true }) foreach (bool mask in correctness ? new[] { false, true } : new[] { false })
                cases.Add(new TiledDrawingCycleBenchmark.Config
                { name = "pencil-motion-" + size + "-" + length + "-" + (outside ? "outside" : "inside") + (mask ? "-mask-gba" : ""),
                    pattern = "long", side = 8192, diameter = size, motionPixels = length, points = 2, strokes = correctness ? 3 : 12,
                    pencil = true, outside = outside, outsideDistance = 1.25f, pencilMetrics = true, interval = 0,
                    mask = mask, channels = mask ? 14 : 15, pencilStrip = correctness, profile = profile });
        }
        else
        {
            foreach (int size in new[] { 1, 9, 81, 256 }) foreach (bool outside in new[] { false, true }) Add(size, outside, 2, 10);
            foreach (bool outside in new[] { false, true }) Add(81, outside, 9, 5);
        }
        void Add(int size, bool outside, int points, int strokes) => cases.Add(new TiledDrawingCycleBenchmark.Config
        { name = "pencil-" + size + "-" + points + "p-" + (outside ? "outside" : "inside"), pattern = "long", side = 8192,
            diameter = size, pencil = true, outside = outside, outsideDistance = 1.25f, pencilMetrics = true,
            points = points, strokes = strokes, interval = 0, profile = profile });
        int index = 0; string active = null; bool done = false;
        PaintTileHistoryTests.DiskScope disk = null;
        double started = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction tick = null;
        AssemblyReloadEvents.AssemblyReloadCallback reload = null;
        void Finish(Exception error)
        {
            if (done) return; done = true;
            EditorApplication.update -= tick; AssemblyReloadEvents.beforeAssemblyReload -= reload;
            if (active != null) TiledDrawingCycleBenchmark.Cancel(active);
            try { disk?.Dispose(); } catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); }
            report.status = error == null ? "passed" : "failed"; report.error = error?.ToString();
            state[1] = Json(new Status { status = report.status, checks = report.checks,
                message = "Pencil stages: " + index + "/" + cases.Count, failures = error == null ? Array.Empty<string>() : new[] { error.ToString() } });
            state[2] = null;
            Directory.CreateDirectory("Temp/WhimTex/pencil-stages");
            File.WriteAllText("Temp/WhimTex/pencil-stages/" + report.runId + ".json", Json(report));
        }
        tick = () =>
        {
            try
            {
                if (EditorApplication.timeSinceStartup - started > 270) throw new TimeoutException("Pencil matrix timeout");
                if (active == null)
                {
                    if (index == cases.Count) { Finish(null); return; }
                    active = report.runId + "-" + index; report.runs.Add(active);
                    TiledDrawingCycleBenchmark.Start(active, JsonUtility.ToJson(cases[index]));
                }
                var result = JsonUtility.FromJson<Status>(TiledDrawingCycleBenchmark.Poll(active));
                state[1] = Json(new Status { status = "running", checks = report.checks, message = cases[index].name, failures = Array.Empty<string>() });
                if (result.status == "running") return;
                if (result.status != "passed") throw new InvalidOperationException(TiledDrawingCycleBenchmark.Poll(active));
                report.checks += result.checks; active = null; index++;
            }
            catch (Exception e) { Finish(e); }
        };
        reload = () => Finish(new InvalidOperationException("Reload interrupted Pencil diagnostic"));
        state[2] = (Action)(() => Finish(new OperationCanceledException()));
        state[1] = Json(new Status { status = "running", message = "Preparing Pencil matrix", failures = Array.Empty<string>() });
        try { disk = new PaintTileHistoryTests.DiskScope(); }
        catch (Exception e) { Finish(e); return Poll(id); }
        EditorApplication.update += tick; AssemblyReloadEvents.beforeAssemblyReload += reload;
        return Poll(id);
    }
}
