using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        const string ReloadKey = "WhimTex.Profiler.ReloadSnapshot";
        const string BaselineReloadKey = "WhimTex.Profiler.BaselineSnapshot";
        const long MaxSnapshotBytes = 256L * 1024 * 1024;

        [JsonObject(MemberSerialization.Fields)]
        sealed class ReloadSnapshot
        {
            internal int version, capacity;
            internal long frequency;
            internal Capture capture;
        }

        static string SnapshotPath(string token)
        {
            if (!Guid.TryParseExact(token, "N", out _)) throw new InvalidDataException("Invalid profiler snapshot identity.");
            string path = Path.GetFullPath("Temp/WhimTex/profiler-reload-" + token + ".json");
            for (string item = path; item != null; item = Path.GetDirectoryName(item))
                if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Redirected profiler snapshot path is not supported.");
            return path;
        }

        static void DiscardReloadBackup()
        {
            string token = SessionState.GetString(ReloadKey, "");
            SessionState.EraseString(ReloadKey);
            if (token.Length == 0) return;
            try { File.Delete(SnapshotPath(token)); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("WhimTex Profiler could not remove its reload snapshot: " + error.Message); }
        }

        static void SaveForReload()
        {
            Stop("assembly_reload");
            ShutdownGpu();
            SaveBaselineForReload();
            DiscardReloadBackup();
            if (capture == null) return;
            string token = Guid.NewGuid().ToString("N");
            string path = null;
            try
            {
                path = SnapshotPath(token);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    WriteSnapshot(stream, capture);
                    stream.Flush(true);
                    if (stream.Length > MaxSnapshotBytes) throw new InvalidDataException("Profiler reload snapshot exceeds its size limit.");
                }
                SessionState.SetString(ReloadKey, token);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("WhimTex Profiler could not save its capture before reload: " + error.Message + "\nSnapshot: " + path);
            }
        }

        static void WriteSnapshot(Stream stream, Capture source)
        {
            var retained = new Sample[source.count];
            Array.Copy(source.events, retained, retained.Length);
            for (int i = 0; i < retained.Length; i++)
            {
                if (!retained[i].completed) retained[i].interrupted = true;
                if (retained[i].gpuStatus == "pending") retained[i].gpuStatus = "interrupted";
            }
            var snapshot = new ReloadSnapshot { version = 1, frequency = Stopwatch.Frequency, capacity = source.events.Length, capture = new Capture {
                id = source.id, stopReason = source.stopReason, events = retained,
                start = source.start, end = source.end, count = source.count, dropped = source.dropped,
                documentId = source.documentId, unityVersion = source.unityVersion, recording = false, open = 0,
                renderingThreadingMode = source.renderingThreadingMode, graphicsJobs = source.graphicsJobs,
                memory = source.memory?.Copy(), causeTrackingLimited = source.causeTrackingLimited,
                collectGpu = source.collectGpu, collectAllocations = source.collectAllocations
            } };
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 4096, true))
            using (var json = new JsonTextWriter(writer))
                new JsonSerializer { TypeNameHandling = TypeNameHandling.None }.Serialize(json, snapshot);
        }

        static Capture ReadSnapshot(Stream stream)
        {
            if (stream.Length > MaxSnapshotBytes) throw new InvalidDataException("Profiler reload snapshot exceeds its size limit.");
            ReloadSnapshot snapshot;
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, true))
            using (var json = new JsonTextReader(reader) { MaxDepth = 16 })
                snapshot = new JsonSerializer { TypeNameHandling = TypeNameHandling.None, CheckAdditionalContent = true }.Deserialize<ReloadSnapshot>(json);
            Capture restored = snapshot?.capture;
            if (snapshot == null || snapshot.version != 1 || snapshot.frequency != Stopwatch.Frequency ||
                snapshot.capacity < 256 || snapshot.capacity > 262144 || restored == null ||
                !Guid.TryParseExact(restored.id, "N", out _) || restored.events == null ||
                restored.count != restored.events.Length || restored.count > snapshot.capacity ||
                restored.recording || restored.open != 0 || restored.dropped < 0 ||
                restored.start < 0 || restored.end < restored.start || string.IsNullOrEmpty(restored.stopReason))
                throw new InvalidDataException("Invalid or unsupported profiler reload snapshot.");
            for (int i = 0; i < restored.count; i++)
            {
                Sample sample = restored.events[i];
                if (sample.id != i + 1L || sample.parentId < 0 || sample.parentId > i ||
                    sample.operationId < 1 || sample.operationId > i + 1L || sample.depth < 0 ||
                    string.IsNullOrEmpty(sample.phase) || string.IsNullOrEmpty(sample.kind) ||
                    sample.start < restored.start || sample.childTicks < 0 ||
                    sample.completed && (sample.end < sample.start || sample.interrupted) ||
                    !sample.completed && !sample.interrupted)
                    throw new InvalidDataException("Invalid profiler event in reload snapshot.");
                Sample parent = sample.parentId == 0 ? default : restored.events[(int)sample.parentId - 1];
                if (sample.depth != (sample.parentId == 0 ? 0 : parent.depth + 1) ||
                    sample.operationId != (sample.parentId == 0 ? sample.id : parent.operationId))
                    throw new InvalidDataException("Invalid profiler hierarchy in reload snapshot.");
            }
            Array.Resize(ref restored.events, snapshot.capacity);
            return restored;
        }

        static void RestoreAfterReload()
        {
            string token = SessionState.GetString(ReloadKey, "");
            if (token.Length == 0 || capture != null) return;
            SessionState.EraseString(ReloadKey);
            string path = null;
            try
            {
                path = SnapshotPath(token);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) capture = ReadSnapshot(stream);
                current = -1; suppressed = 0;
                File.Delete(path);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("WhimTex Profiler could not restore its reload snapshot: " + error.Message + "\nSnapshot retained for inspection: " + path);
            }
        }

        static void DiscardBaselineBackup()
        {
            string token = SessionState.GetString(BaselineReloadKey, "");
            SessionState.EraseString(BaselineReloadKey);
            if (token.Length == 0) return;
            try { File.Delete(SnapshotPath(token)); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("WhimTex Profiler could not remove its baseline snapshot: " + error.Message); }
        }

        static void SaveBaselineForReload()
        {
            DiscardBaselineBackup();
            if (Baseline == null) return;
            string token = Guid.NewGuid().ToString("N"), path = null;
            try
            {
                path = SnapshotPath(token);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    WriteSnapshot(stream, Baseline);
                    stream.Flush(true);
                    if (stream.Length > MaxSnapshotBytes) throw new InvalidDataException("Profiler baseline exceeds its size limit.");
                }
                SessionState.SetString(BaselineReloadKey, token);
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("WhimTex Profiler could not retain its baseline: " + error.Message + "\nSnapshot: " + path); }
        }

        static void RestoreBaselineAfterReload()
        {
            string token = SessionState.GetString(BaselineReloadKey, "");
            if (token.Length == 0 || Baseline != null) return;
            SessionState.EraseString(BaselineReloadKey);
            string path = null;
            try
            {
                path = SnapshotPath(token);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read)) Baseline = ReadSnapshot(stream);
                File.Delete(path);
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("WhimTex Profiler could not restore its baseline: " + error.Message + "\nSnapshot retained for inspection: " + path); }
        }
    }
}
