// Memory-only regression checks, run through the connected Unity Editor's run_script.
// No Assets, scenes, windows, worker sleeps or platform-specific APIs.
using System;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using DCFApixels.WhimTex;

public static class DocumentOperationWaitSmoke
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Type Operation = typeof(WhimTexDocumentContainer).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentOperation");
    static readonly ConstructorInfo Constructor = Operation.GetConstructor(Any, null,
        new[] { typeof(string), typeof(Func<string, float, bool>) }, null);
    static readonly Action<Action> RunWork = Bind<Action<Action>>("Run");
    static readonly Action<string, float> Report = Bind<Action<string, float>>("Report");
    static readonly Action Commit = Bind<Action>("Commit");
    static int checks;

    static T Bind<T>(string method) where T : Delegate => (T)Operation.GetMethod(method, Any).CreateDelegate(typeof(T));
    static IDisposable Scope(Func<string, float, bool> callback) =>
        (IDisposable)Constructor.Invoke(new object[] { "Operation wait regression", callback });
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    static Exception Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }
    static void Wait(ManualResetEventSlim gate)
    {
        if (!gate.Wait(3000)) throw new TimeoutException("Regression worker was not released.");
    }

    public static string Run()
    {
        checks = 0;
        int caller = Thread.CurrentThread.ManagedThreadId, worker = 0;
        RunWork(() => worker = Thread.CurrentThread.ManagedThreadId);
        Check(caller == worker, "no scope runs synchronously");
        var syncError = new InvalidOperationException("synchronous error");
        Check(ReferenceEquals(syncError, Capture(() => RunWork(() => throw syncError))), "synchronous exception identity");

        int workerPolls = 0, nestedThread = 0;
        using (Scope((stage, _) => { if (stage == "worker report") workerPolls++; return false; }))
            RunWork(() =>
            {
                worker = Thread.CurrentThread.ManagedThreadId;
                Report("worker report", .5f);
                RunWork(() => nestedThread = Thread.CurrentThread.ManagedThreadId);
            });
        Check(worker != caller, "scoped work uses worker thread");
        Check(nestedThread == worker && workerPolls == 0, "operation scope does not flow into worker");

        // Hold work until the third callback: a completion wait must still poll long operations.
        using (var release = new ManualResetEventSlim())
        {
            int polls = 0, result = 0;
            using (Scope((_, __) => { if (++polls == 3) release.Set(); return false; }))
                RunWork(() => { Wait(release); result = 42; });
            Check(polls >= 3, "long work continues progress/cancellation polling");
            Check(result == 42, "successful result visible before return");
        }

        CheckWorkerError(new InvalidOperationException("worker error"));
        CheckWorkerError(new OperationCanceledException("worker cancellation"));
        CheckWorkerError(new AggregateException("worker's own aggregate", new Exception("inner")));

        // The callback may cancel or throw while work still owns buffers. Both must drain it.
        CheckDrain(false, false);
        CheckDrain(false, true);
        CheckDrain(true, true);

        int outerPolls = 0, innerPolls = 0;
        using (Scope((_, __) => { outerPolls++; return false; }))
        {
            Report("outer", 0);
            using (Scope((_, __) => { innerPolls++; return false; })) Report("inner", 0);
            Report("outer restored", 1);
        }
        Report("no scope", 0);
        Check(outerPolls == 2 && innerPolls == 1, "nested scope restoration and disposal");

        int commitPolls = 0;
        using (Scope((_, __) => { commitPolls++; return false; }))
        {
            Commit();
            int before = commitPolls;
            Report("after commit", 1);
            RunWork(() => { });
            Check(commitPolls == before, "commit disables cancellation polling");
        }
        Check(commitPolls == 1, "commit polls before write boundary");

        // Exercise real compression/integrity preparation, then its already-prepared fast path.
        var pixels = new byte[128 * 1024];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
        using (var baseline = new WhimTexDocumentContainer())
        using (var scoped = new WhimTexDocumentContainer())
        {
            baseline.Set("texture:0", pixels, CompressionLevel.Fastest);
            scoped.Set("texture:0", pixels, CompressionLevel.Fastest);
            byte[] expected = baseline.Serialize();
            using (Scope((_, __) => false))
            {
                byte[] actual = scoped.Serialize();
                Check(expected.AsSpan().SequenceEqual(actual), "scoped container serialization is byte-identical");
                Check(actual.AsSpan().SequenceEqual(scoped.Serialize()), "repeated preparation is byte-identical");
                using var parsed = WhimTexDocumentContainer.Parse(actual);
                Check(pixels.AsSpan().SequenceEqual(parsed.Get("texture:0")), "compressed pixels and integrity round-trip");
            }
        }

        worker = 0;
        RunWork(() => worker = Thread.CurrentThread.ManagedThreadId);
        Check(worker == caller, "all operation scopes restored");
        return "PASS: " + checks + " operation wait checks; no Assets created.";
    }

    static void CheckWorkerError(Exception expected)
    {
        using var release = new ManualResetEventSlim();
        int polls = 0;
        using var scope = Scope((_, __) => { polls++; release.Set(); return false; });
        var actual = Capture(() => RunWork(() => { Wait(release); throw expected; }));
        Check(polls > 0, "faulting worker passes through polling: " + expected.GetType().Name);
        Check(ReferenceEquals(expected, actual), "worker exception preserved without extra AggregateException: " + expected.GetType().Name);
    }

    static void CheckDrain(bool callbackThrows, bool workerThrows)
    {
        using var release = new ManualResetEventSlim();
        var callbackError = new InvalidOperationException("callback error");
        var workerError = new InvalidOperationException("secondary worker error");
        var buffer = new byte[256 * 1024];
        int finished = 0, disposed = 0, prematureDisposal = 0;
        using var scope = Scope((_, __) =>
        {
            release.Set();
            if (callbackThrows) throw callbackError;
            return true;
        });
        Exception actual;
        try
        {
            actual = Capture(() => RunWork(() =>
            {
                Wait(release);
                try
                {
                    for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i % 251);
                    if (Volatile.Read(ref disposed) != 0) Interlocked.Exchange(ref prematureDisposal, 1);
                    if (workerThrows) throw workerError;
                }
                finally { Interlocked.Exchange(ref finished, 1); }
            }));
        }
        finally { Volatile.Write(ref disposed, 1); }
        Check(Volatile.Read(ref finished) == 1 && prematureDisposal == 0, "callback interruption drains worker before buffer disposal");
        Check(buffer[buffer.Length - 1] == (byte)((buffer.Length - 1) % 251), "worker finished writing buffer");
        Check(callbackThrows ? ReferenceEquals(callbackError, actual) : actual is OperationCanceledException,
            "callback cancellation/error takes precedence over secondary worker error");
    }
}
