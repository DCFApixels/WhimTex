using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// All disk/settings changes are confined to a fresh Temp directory. No user history is cleared.
public static partial class PaintTileHistoryTests
{
    sealed class DiskScope : IDisposable
    {
        internal readonly string home;
        internal readonly object disk;
        readonly object previous;
        readonly bool loaded;
        readonly IDictionary steps;
        readonly List<DictionaryEntry> saved = new();
        internal DiskScope()
        {
            Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites");
            Static("PaintHistoryStore", "FinishCheckpointWork", (object)null);
            home = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-undo-lifecycle", Guid.NewGuid().ToString("N")));
            previous = Type("PaintUndoDisk").GetField("current", S).GetValue(null);
            disk = NewDisk(home);
            steps = (IDictionary)Type("PaintHistoryStore").GetField("steps", S).GetValue(null);
            foreach (DictionaryEntry entry in steps) saved.Add(entry);
            steps.Clear();
            loaded = (bool)Type("PaintHistoryStore").GetField("loaded", S).GetValue(null);
            Type("PaintHistoryStore").GetField("loaded", S).SetValue(null, false);
            Type("PaintUndoDisk").GetField("current", S).SetValue(null, disk);
        }
        public void Dispose()
        {
            try { Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites"); Static("PaintHistoryStore", "FinishCheckpointWork", (object)null); Call(disk, "Close"); }
            finally
            {
                steps.Clear(); foreach (var entry in saved) steps.Add(entry.Key, entry.Value);
                Type("PaintHistoryStore").GetField("loaded", S).SetValue(null, loaded);
                Type("PaintUndoDisk").GetField("current", S).SetValue(null, previous);
            }
        }
    }
    static object NewDisk(string home, string session = null) => Activator.CreateInstance(Type("PaintUndoDisk"), F, null,
        new object[] { home, session ?? Guid.NewGuid().ToString("N") }, null);
    static bool Reserve(object disk, string path, long bytes)
    { var args = new object[] { path, bytes, null, false }; return (bool)Call(disk, "Reserve", args); }
    static string Child(object disk, string name) => Path.Combine((string)Get(disk, "Root"), name);
    [Serializable] sealed class OwnerData { public string format, machine; public int process; public long started, charge; }
    public static string RunDiskLifecycle() => TestContext.Run("Shared Undo quota, sessions, cleanup and settings", t =>
    {
        using var scope = new DiskScope(); var a = scope.disk; var b = NewDisk(scope.home);
        try
        {
            Call(a, "SetLimits", (int?)1, (int?)3);
            t.Equal(1, (int)Get(Call(b, "ReadLimits"), "diskGiB"), "Other session sees global disk limit");
            Call(b, "SetLimits", (int?)null, (int?)7);
            t.Equal(7, (int)Get(Call(a, "ReadLimits"), "steps"), "Other session sees operation limit without overwriting disk limit");
            string ap = Child(a, "a"), bp = Child(b, "b");
            bool ar = false, br = false;
            Task.WaitAll(Task.Run(() => ar = Reserve(a, ap, 600L * 1024 * 1024)),
                Task.Run(() => br = Reserve(b, bp, 600L * 1024 * 1024)));
            t.True(ar != br, "Concurrent reservations cannot overbook the common 1 GiB quota");
            Call(a, "CleanupAbandoned");
            t.True(Directory.Exists((string)Get(b, "Root")), "Cleanup protects another live session");
            var reopened = NewDisk(scope.home, (string)Get(a, "Session"));
            t.Equal((string)Get(a, "Root"), (string)Get(reopened, "Root"), "Assembly reload reopens the same process session");
            t.True((long)Call(reopened, "Usage") >= 600L * 1024 * 1024, "Reservations remain charged after reload");
            if (ar) Call(a, "DeleteOwned", ap); else Call(b, "DeleteOwned", bp);
            bool outsideRejected = false;
            try { Call(a, "DeleteOwned", (string)Get(b, "Root")); }
            catch (TargetInvocationException e) when (e.InnerException is IOException) { outsideRejected = true; }
            t.True(outsideRejected, "A session cannot delete another session");
            string orphan = Path.Combine(scope.home, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
            var data = JsonUtility.FromJson<OwnerData>(File.ReadAllText(Child(b, "session.json")));
            data.started--; // A reused process ID is not the old owner.
            File.WriteAllText(Path.Combine(orphan, "session.json"), JsonUtility.ToJson(data));
            File.WriteAllText(Path.Combine(orphan, "payload"), "owned fixture");
            string unknown = Path.Combine(scope.home, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unknown);
            File.WriteAllText(Path.Combine(unknown, "unrecognized.txt"), "preserve");
            t.Equal(1, (int)Call(a, "CleanupAbandoned"), "Abandoned session is reclaimed, including PID reuse");
            t.True(!Directory.Exists(orphan) && Directory.Exists(unknown), "Unknown directories are preserved");
            using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = "node", Arguments = "-e \"setInterval(()=>{},1000)\"", UseShellExecute = false, CreateNoWindow = true }))
            {
                try
                {
                    string crashed = Path.Combine(scope.home, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(crashed);
                    data.process = child.Id; data.started = child.StartTime.ToUniversalTime().Ticks;
                    File.WriteAllText(Path.Combine(crashed, "session.json"), JsonUtility.ToJson(data));
                    File.WriteAllText(Path.Combine(crashed, "payload"), "owned child-process fixture");
                    Call(a, "CleanupAbandoned");
                    t.True(Directory.Exists(crashed), "Real live child-process owner is protected");
                    child.Kill(); t.True(child.WaitForExit(3000), "Owned child terminates without normal cleanup");
                    t.Equal(1, (int)Call(a, "CleanupAbandoned"), "Files of a genuinely terminated process are reclaimed");
                    t.True(!Directory.Exists(crashed), "Abnormal exit leaves no owned payload after cleanup");
                }
                finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } }
            }
            Call(b, "Close");
            t.True(!Directory.Exists((string)Get(b, "Root")) && Directory.Exists((string)Get(a, "Root")), "Normal exit deletes only its own session");
            t.True(File.Exists((string)Get(a, "SettingsPath")), "Cleanup keeps shared settings");
        }
        finally { Call(b, "Close"); }
    });
    public static string RunReservationRelease() => TestContext.Run("Completed writes release raw reservations during sustained input", t =>
    {
        using var scope = new DiskScope();
        Call(scope.disk, "SetLimits", (int?)1, (int?)50);
        using var fixture = new Fixture(1536, true);
        Set(fixture.settings, "brushSize", 1536f);
        var rect = new RectInt(736, 736, 32, 32);
        byte[] initial = Probe(fixture.surface, rect);
        for (int i = 0; i < 20; i++)
        {
            fixture.Stroke(new[] { new Vector2(.5f, .5f) }, null);
            Static("PaintHistoryStore", "FlushWrites"); // No maintenance or Editor update between strokes.
            var cursor = Get(fixture.layer, "PixelUndoObject");
            string revision = (string)Get(cursor, "Revision");
            var step = Static("PaintHistoryStore", "Get", revision);
            t.True((bool)Get(step, "reservationReleased"), "Worker released reservation " + i);
            t.True(!File.Exists(Path.Combine((string)Static("PaintHistoryStore", "StepDirectory", revision), "reservation")), "Actual size charged " + i);
            t.Equal("", (string)Get(cursor, "Boundary"), "No false quota retirement " + i);
        }
        byte[] final = Probe(fixture.surface, rect);
        for (int i = 0; i < 20; i++) Undo.PerformUndo();
        Exact(t, initial, Probe((RenderTexture)Get(fixture.layer, "paintSurface"), rect), "All twenty Undo steps remain");
        for (int i = 0; i < 20; i++) Undo.PerformRedo();
        Exact(t, final, Probe((RenderTexture)Get(fixture.layer, "paintSurface"), rect), "All twenty Redo steps remain exact");
    });
    public static string RunRetention() => TestContext.Run("Bounded paint history preserves exact Half, masks, HDR and native Undo order", t =>
    {
        using var scope = new DiskScope(); var mask = Mask();
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            Call(scope.disk, "SetLimits", (int?)1, (int?)2);
            // A live foreign reservation fills the quota; refusal must precede any Half mutation.
            var occupied = NewDisk(scope.home);
            try
            {
                t.True(Reserve(occupied, Child(occupied, "occupied"), 990L * 1024 * 1024), "Foreign session reserves most of the shared quota");
                using var refused = new Fixture(513, false);
                var initial = Probe(refused.surface, new RectInt(0, 0, 513, 513));
                bool rejected = false;
                try { refused.Stroke(new[] { new Vector2(.5f, .5f) }, mask); }
                catch (TargetInvocationException e) when (e.InnerException is IOException) { rejected = true; }
                t.True(rejected, "Quota exhaustion refuses the stroke");
                Exact(t, initial, Probe(refused.surface, new RectInt(0, 0, 513, 513)), "Refused stroke leaves every Half pixel unchanged");
                t.Equal(0, Transfer<int>("PendingCount"), "Refused stroke queues no partial history");
                t.True(!(bool)Get(refused.layer, "HasBufferedStroke"), "Refusal releases the stroke preview");
            }
            finally { Call(occupied, "Close"); }
            foreach (bool hdr in new[] { false, true })
            {
                using var f = new Fixture(513, hdr);
                var rect = new RectInt(0, 0, 513, 513);
                var halves = new List<byte[]>(); var stored = new List<byte[]>(); var revisions = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    Set(f.dynamics, "flow", i == 0 ? .13f : .0001f);
                    f.Stroke(new[] { new Vector2(.1f, .1f), new Vector2(.1f, .9f), new Vector2(.9f, .9f), new Vector2(.9f, .1f) }, mask);
                    halves.Add(Probe((RenderTexture)Get(f.layer, "paintSurface"), rect));
                    stored.Add(f.pixels.GetRawTextureData<byte>().ToArray());
                    revisions.Add((string)Get(Get(f.layer, "PixelUndoObject"), "Revision"));
                }
                Static("PaintHistoryStore", "FlushWrites"); SettleRetention(Get(f.layer, "PixelUndoObject"), 2, out _, out _);
                t.True(Type("PaintHistoryStore").GetProperty("LastMaintenanceError", S).GetValue(null) == null, "Retention completed without an error");
                var cursor = Get(f.layer, "PixelUndoObject");
                var remaining = (IList)Static("PaintHistoryStore", "Snapshot");
                int count = 0; foreach (var s in remaining) if ((string)Get(s, "owner") == (string)Get(cursor, "Identity")) count++;
                t.Equal(2, count, "Operation count enforced for this document");
                t.Equal(revisions[1], (string)Get(cursor, "Boundary"), "Oldest two operations compacted into checkpoint");
                Call(f.layer, "InvalidatePaintSurface");
                Exact(t, halves[3], Probe((RenderTexture)Call(f.layer, "EnsurePaintSurface", 513, 513), rect), "Current precise Half rebuilt after pruning");
                Undo.PerformUndo(); Undo.PerformUndo();
                Exact(t, halves[1], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Undo reaches exact retained boundary");
                Exact(t, stored[1], f.pixels.GetRawTextureData<byte>().ToArray(), "Boundary straight storage is exact");
                Undo.PerformRedo(); Undo.PerformRedo();
                Exact(t, halves[3], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Remaining Redo order is exact");
                Call(cursor, "ClearHistory");
                Call(f.layer, "InvalidatePaintSurface");
                Exact(t, halves[3], Probe((RenderTexture)Call(f.layer, "EnsurePaintSurface", 513, 513), rect), "Forced clear keeps current Half precision");
                // An obsolete cursor restored by a larger native group must be ignored atomically.
                Set(cursor, "revision", revisions[0]); Call(f.layer, "ReconcilePixelUndo");
                Exact(t, stored[3], f.pixels.GetRawTextureData<byte>().ToArray(), "Cleared cursor cannot partially replay pixels");
                f.Stroke(new[] { new Vector2(.5f, .5f) }, mask);
                Undo.PerformUndo(); Exact(t, halves[3], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "New stroke after clear has an exact Undo baseline");
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });
    static string retentionPeakStage;
    static double SettleRetention(object cursor, int limit, out double maximumSlice, out int slices)
    {
        var total = System.Diagnostics.Stopwatch.StartNew(); maximumSlice = 0; slices = 0;
        while (true)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew(); Static("PaintHistoryStore", "Maintenance", true);
            double slice = watch.Elapsed.TotalMilliseconds;
            if (slice > maximumSlice) { maximumSlice = slice; retentionPeakStage =
                Type("PaintHistoryStore").GetProperty("MaintenanceSlowestStage", S)?.GetValue(null) + ": " +
                Type("PaintHistoryStore").GetProperty("MaintenanceSlowestStageMs", S)?.GetValue(null); }
            slices++;
            int count = 0; foreach (var step in (IList)Static("PaintHistoryStore", "Snapshot"))
                if ((string)Get(step, "owner") == (string)Get(cursor, "Identity")) count++;
            if (count <= limit && !(bool)Get(cursor, "HasPendingCheckpoint") &&
                !(bool)Type("PaintHistoryStore").GetProperty("MaintenanceBusy", S).GetValue(null)) return total.Elapsed.TotalMilliseconds;
            if (total.Elapsed.TotalSeconds > 30) throw new TimeoutException("Retention did not settle; count=" + count +
                ", pending=" + Get(cursor, "HasPendingCheckpoint") + ", worker=" + Type("PaintHistoryStore").GetProperty("MaintenanceBusy", S).GetValue(null) + ": " +
                Type("PaintHistoryStore").GetProperty("LastMaintenanceError", S).GetValue(null));
            System.Threading.Thread.Sleep(1);
        }
    }
}
