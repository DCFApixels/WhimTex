using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

// Run the existing paced cycle in an owned journal on the project volume. The
// original journal and its files are never retired or changed by this matrix.
public static class PaintUndoReuseBenchmark
{
    const string Key = "WhimTex.Tests.UndoReuseJournal";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] sealed class Result { public string status; }
    [Serializable] sealed class Config { public string name; }
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];

    public static string Start(string runId, string config)
    {
        if (State != null) throw new InvalidOperationException("An owned Undo benchmark is already active.");
        var assembly = typeof(DrawingLayerBehaviour).Assembly;
        var transfer = assembly.GetType("DCFApixels.WhimTex.PaintTileTransfer", true);
        var history = assembly.GetType("DCFApixels.WhimTex.DrawingPixelHistory", true);
        if ((int)transfer.GetProperty("PendingCount", Flags).GetValue(null) != 0)
            throw new InvalidOperationException("User paint transfers are pending.");
        foreach (var item in Resources.FindObjectsOfTypeAll(history))
            if ((bool)history.GetProperty("HasLiveHistory", Flags).GetValue(item))
                throw new InvalidOperationException("An existing user paint history is retained.");
        var scope = new PaintTileHistoryTests.DiskScope();
        AppDomain.CurrentDomain.SetData(Key, new object[] { runId, (Action)scope.Dispose });
        string cycleId = runId + "-" + JsonUtility.FromJson<Config>(config).name;
        AppDomain.CurrentDomain.SetData(Key + ".cycle", cycleId);
        try
        {
            string result = TiledDrawingCycleBenchmark.Start(cycleId, config);
            AssemblyReloadEvents.beforeAssemblyReload += Reload;
            return CompleteIfDone(result);
        }
        catch { Cleanup(); throw; }
    }

    static string Cycle(string runId)
    {
        var id = AppDomain.CurrentDomain.GetData(Key + ".cycle") as string;
        if (id == null || !id.StartsWith(runId + "-", StringComparison.Ordinal))
            throw new InvalidOperationException("Undo benchmark ownership does not match.");
        return id;
    }
    public static string Poll(string runId) => CompleteIfDone(TiledDrawingCycleBenchmark.Poll(Cycle(runId)));
    public static string Cancel(string runId) => CompleteIfDone(TiledDrawingCycleBenchmark.Cancel(Cycle(runId)));
    static string CompleteIfDone(string result)
    {
        if (JsonUtility.FromJson<Result>(result).status != "running") Cleanup();
        return result;
    }
    static void Reload()
    {
        if (State == null) return;
        try { TiledDrawingCycleBenchmark.Cancel((string)AppDomain.CurrentDomain.GetData(Key + ".cycle")); }
        finally { Cleanup(); }
    }
    static void Cleanup()
    {
        var state = State;
        if (state == null) return;
        // Keep ownership visible if restoration fails; never silently start another run.
        ((Action)state[1])();
        AssemblyReloadEvents.beforeAssemblyReload -= Reload;
        AppDomain.CurrentDomain.SetData(Key, null);
    }
}
