using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Opt-in breadcrumbs, not a sampler of another thread's stack. The external
    // process dump is needed to inspect where a busy/native thread actually is.
    [InitializeOnLoad]
    internal static class WhimTexHangDiagnostics
    {
        const int MaxRecords = 20000;
        const long MaxBytes = 32L * 1024 * 1024;
        static readonly object gate = new();
        static readonly string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/HangDiagnostics"));
        static readonly string flag = Path.Combine(directory, "vector-brush.enabled");
        static StreamWriter writer;
        static volatile bool active;
        static bool used;
        static int generation, count;
        static long nextId, start;
        static double nextHeartbeat;
        static string failure;
        [ThreadStatic] static long current;
        [ThreadStatic] static int threadGeneration;

        static WhimTexHangDiagnostics()
        {
            EditorApplication.update += Update;
            EditorApplication.quitting += () => Stop("editor_quitting");
            AssemblyReloadEvents.beforeAssemblyReload += () => Stop("assembly_reload");
        }

        // Called only on the Editor thread, immediately before finishing a mask stroke.
        internal static void Start(string reason, string layerId)
        {
            if (active || used || !File.Exists(flag)) return;
            lock (gate)
            {
                try
                {
                    used = true;
                    Directory.CreateDirectory(directory);
                    using var process = Process.GetCurrentProcess();
                    string path = Path.Combine(directory, $"trace-{process.Id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                    writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                    generation++; count = 0; start = Stopwatch.GetTimestamp(); active = true;
                    Write(new { kind = "session", utc = DateTime.UtcNow.ToString("O"), processId = process.Id,
                        processStartedUtc = process.StartTime.ToUniversalTime().ToString("O"),
                        unity = Application.unityVersion, assembly = typeof(WhimTexWindow).Module.ModuleVersionId.ToString(),
                        mainManagedThreadId = Thread.CurrentThread.ManagedThreadId, reason, layerId,
                        note = "entryStack is a cooperative entry breadcrumb, NOT the current stack of a hung thread" });
                }
                catch (Exception error) { Fail(error); }
            }
        }

        internal static Scope Enter(string phase)
        {
            if (!active) return default;
            try
            {
                string stack = new StackTrace(1, true).ToString();
                lock (gate)
                {
                    if (!active) return default;
                    if (threadGeneration != generation) { threadGeneration = generation; current = 0; }
                    long id = ++nextId, parent = current, ticks = Stopwatch.GetTimestamp();
                    Write(new { kind = "enter", utc = DateTime.UtcNow.ToString("O"), elapsedMs = Milliseconds(ticks - start),
                        id, parentId = parent, managedThreadId = Thread.CurrentThread.ManagedThreadId, phase, entryStack = stack });
                    if (!active) return default;
                    current = id;
                    return new Scope(id, parent, ticks, generation);
                }
            }
            catch (Exception error) { lock (gate) Fail(error); return default; }
        }

        internal static void Note(string phase, object data)
        {
            if (!active) return;
            lock (gate)
            {
                if (!active) return;
                try { Write(new { kind = "note", utc = DateTime.UtcNow.ToString("O"), phase,
                    managedThreadId = Thread.CurrentThread.ManagedThreadId, data }); }
                catch (Exception error) { Fail(error); }
            }
        }

        internal readonly struct Scope : IDisposable
        {
            readonly long id, parent, ticks;
            readonly int session;
            internal Scope(long id, long parent, long ticks, int session)
            { this.id = id; this.parent = parent; this.ticks = ticks; this.session = session; }
            public void Dispose()
            {
                if (id == 0) return;
                lock (gate)
                {
                    if (session != generation) return;
                    current = parent;
                    if (!active) return;
                    try
                    {
                        Write(new { kind = "leave", utc = DateTime.UtcNow.ToString("O"), id,
                            managedThreadId = Thread.CurrentThread.ManagedThreadId,
                            durationMs = Milliseconds(Stopwatch.GetTimestamp() - ticks) });
                    }
                    catch (Exception error) { Fail(error); }
                }
            }
        }

        static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        // All writes are flushed before entering the next operation and remain readable
        // from outside Unity. No worker calls Unity APIs or waits for the main thread.
        static void Write(object record)
        {
            if (writer == null) return;
            writer.WriteLine(JsonConvert.SerializeObject(record));
            if (++count >= MaxRecords || writer.BaseStream.Position >= MaxBytes) Stop("size_limit");
        }

        static void Update()
        {
            if (failure != null)
            {
                string message = failure; failure = null;
                UnityEngine.Debug.LogWarning("WhimTex hang diagnostics stopped: " + message);
            }
            if (EditorApplication.timeSinceStartup < nextHeartbeat) return;
            nextHeartbeat = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(flag)) { Stop("disarmed"); used = false; return; }
            if (!active) return;
            lock (gate)
            {
                try
                {
                    if (Milliseconds(Stopwatch.GetTimestamp() - start) > 600000) { Stop("time_limit"); return; }
                    Write(new { kind = "heartbeat", utc = DateTime.UtcNow.ToString("O"),
                        managedThreadId = Thread.CurrentThread.ManagedThreadId });
                }
                catch (Exception error) { Fail(error); }
            }
        }

        static void Stop(string reason)
        {
            lock (gate)
            {
                active = false;
                try { writer?.WriteLine(JsonConvert.SerializeObject(new { kind = "stop", utc = DateTime.UtcNow.ToString("O"), reason })); }
                catch (Exception error) { failure = error.Message; }
                try { writer?.Dispose(); } catch (Exception error) { failure = error.Message; }
                writer = null;
            }
        }

        static void Fail(Exception error)
        {
            failure = error.Message;
            Stop("io_error");
        }
    }
}
