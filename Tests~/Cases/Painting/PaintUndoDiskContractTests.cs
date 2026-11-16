using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

// Local disk instances only: no replacement of the user's journal, Unity Undo or settings.
public static class PaintUndoDiskContractTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const long MiB = 1024L * 1024;
    static readonly Type DiskType = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintUndoDisk", true);

    static object Call(object disk, string name, params object[] args)
    {
        MethodInfo method = null;
        foreach (var candidate in DiskType.GetMethods(Flags))
            if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
            {
                if (method != null) throw new AmbiguousMatchException(name);
                method = candidate;
            }
        if (method == null) throw new MissingMethodException(DiskType.FullName, name);
        try { return method.Invoke(disk, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }

    static string Field(object disk, string name) => (string)DiskType.GetField(name, Flags).GetValue(disk);
    static long Usage(object disk) => (long)Call(disk, "Usage");
    static string Child(object disk, string name) => Path.Combine(Field(disk, "Root"), name);
    static bool Reserve(object disk, string path, long bytes)
    {
        var args = new object[] { path, bytes, null, false };
        return (bool)Call(disk, "Reserve", args);
    }

    static void IoFailure(TestContext test, Action action, string label)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { rejected = true; }
        test.True(rejected, label);
    }

    static void SameFile(TestContext test, string path, byte[] expected, string label)
    {
        byte[] actual = File.ReadAllBytes(path);
        bool equal = actual.Length == expected.Length;
        for (int i = 0; equal && i < actual.Length; i++) equal = actual[i] == expected[i];
        test.True(equal, label);
    }

    sealed class OwnedStore : IDisposable
    {
        internal readonly string home = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Temp/WhimTex/paint-undo-contract", Guid.NewGuid().ToString("N")));
        readonly List<object> sessions = new();
        internal object Open()
        {
            var disk = Create(Guid.NewGuid().ToString("N"));
            sessions.Add(disk);
            return disk;
        }
        internal object Reopen(object disk) => Create(Field(disk, "Session"));
        object Create(string session) => Activator.CreateInstance(DiskType, Flags, null, new object[] { home, session }, null);
        public void Dispose()
        {
            var errors = new List<Exception>();
            foreach (var disk in sessions)
                try { Call(disk, "Close"); }
                catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Owned disk contract cleanup failed.", errors);
        }
    }

    public static string Run() => TestContext.Run("Atomic paint Undo reservations, retries and owned paths", test =>
    {
        using var store = new OwnedStore();
        var disk = store.Open();
        var peer = store.Open();
        Call(disk, "SetLimits", (int?)1, (int?)50);

        string step = Child(disk, "first");
        test.True(Reserve(disk, step, 4 * MiB), "Initial reservation succeeds");
        var payload = new byte[4099];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 37 + 11);
        string pixels = Path.Combine(step, "pixels.bin");
        File.WriteAllBytes(pixels, payload);
        long reservedUsage = Usage(disk);
        byte[] originalMarker = File.ReadAllBytes(Path.Combine(step, "reservation"));
        bool duplicateAccepted = false;
        try { duplicateAccepted = Reserve(disk, step, 8 * MiB); }
        catch (IOException) { }
        test.True(!duplicateAccepted, "An existing reservation cannot be replaced or charged twice");
        test.Equal(reservedUsage, Usage(peer), "Rejected duplicate keeps shared quota unchanged");
        SameFile(test, Path.Combine(step, "reservation"), originalMarker, "Rejected duplicate keeps the original reservation");
        SameFile(test, pixels, payload, "Rejected duplicate keeps payload bytes");

        Call(disk, "Complete", step);
        long completedUsage = Usage(peer);
        test.True(completedUsage < reservedUsage - 3 * MiB, "Complete releases unused reservation capacity");
        test.True(completedUsage >= payload.Length, "Completed data remains charged");
        test.True(!File.Exists(Path.Combine(step, "reservation")), "Successful completion removes the reservation marker");
        Call(disk, "Complete", step);
        test.Equal(completedUsage, Usage(peer), "Repeated Complete does not decrement quota twice");
        SameFile(test, pixels, payload, "Repeated completion preserves payload");

        var reopened = store.Reopen(disk);
        test.Equal(Field(disk, "Root"), Field(reopened, "Root"), "Reopen keeps the same owned session");
        long reopenedUsage = Usage(peer);
        test.True(reopenedUsage >= payload.Length, "Reopen retains the completed payload charge");
        Call(reopened, "Complete", step);
        test.Equal(reopenedUsage, Usage(disk), "Complete after reopen remains idempotent");

        CheckOwnedPaths(test, disk, peer, payload);
        CheckCompleteWritten(test, store, disk, peer, payload);
        CheckReserveMarkerFailure(test, disk, peer);
        CheckReserveLedgerFailure(test, store, disk, peer);
        CheckCompleteLedgerFailure(test, store, disk, peer, payload, false);
        CheckCompleteLedgerFailure(test, store, disk, peer, payload, true);

        Call(disk, "DeleteOwned", step);
        long removedUsage = Usage(peer);
        Call(reopened, "DeleteOwned", step);
        test.Equal(removedUsage, Usage(peer), "Repeated DeleteOwned after reopen does not decrement quota twice");
        test.True(!Directory.Exists(step), "Owned completed payload is deleted");
    });

    static void CheckOwnedPaths(TestContext test, object disk, object peer, byte[] payload)
    {
        string peerStep = Child(peer, "protected");
        test.True(Reserve(peer, peerStep, 2 * MiB), "Foreign live session reserves its own data");
        string peerPixels = Path.Combine(peerStep, "pixels.bin");
        File.WriteAllBytes(peerPixels, payload);
        byte[] marker = File.ReadAllBytes(Path.Combine(peerStep, "reservation"));
        long before = Usage(peer);
        string sibling = Path.Combine(Field(disk, "Root"), "..", Field(peer, "Session"), "protected");
        foreach (string path in new[] { peerStep, sibling, Field(disk, "Root") })
        {
            IoFailure(test, () => Reserve(disk, path, MiB), "Reserve rejects an unowned/root path");
            IoFailure(test, () => Call(disk, "Complete", path), "Complete rejects an unowned/root path");
            IoFailure(test, () => Call(disk, "CompleteWritten", path, (long)payload.Length),
                "CompleteWritten rejects an unowned/root path");
            IoFailure(test, () => Call(disk, "DeleteOwned", path), "Delete rejects an unowned/root path");
        }
        test.Equal(before, Usage(peer), "Rejected path operations leave shared charge unchanged");
        SameFile(test, peerPixels, payload, "Rejected path operations preserve foreign payload");
        SameFile(test, Path.Combine(peerStep, "reservation"), marker, "Rejected path operations preserve foreign reservation");
        Call(peer, "DeleteOwned", peerStep);
    }

    static void CheckCompleteWritten(TestContext test, OwnedStore store, object disk, object peer, byte[] payload)
    {
        string step = Child(disk, "complete-written");
        test.True(Reserve(disk, step, 4 * MiB), "Known-size completion reserves capacity");
        string pages = Path.Combine(step, "pages");
        Directory.CreateDirectory(pages);
        string pixels = Path.Combine(pages, "0000.bin");
        string manifest = Path.Combine(step, "step.json");
        var metadata = new byte[] { 123, 125 };
        File.WriteAllBytes(pixels, payload);
        File.WriteAllBytes(manifest, metadata);
        long writtenBytes = payload.LongLength + metadata.LongLength;
        long before = Usage(peer);
        Call(disk, "CompleteWritten", step, writtenBytes);
        long after = Usage(peer);
        test.Equal(before - 4 * MiB + writtenBytes, after,
            "CompleteWritten replaces the reservation with the exact published byte count");
        test.True(!File.Exists(Path.Combine(step, "reservation")), "CompleteWritten removes the reservation marker");
        Call(disk, "CompleteWritten", step, writtenBytes);
        Call(disk, "Complete", step);
        test.Equal(after, Usage(peer), "Both completion paths are idempotent after known-size publication");
        SameFile(test, pixels, payload, "CompleteWritten preserves every nested payload byte");
        SameFile(test, manifest, metadata, "CompleteWritten preserves published metadata");

        var reopened = store.Reopen(disk);
        long reopenedUsage = Usage(peer);
        Call(reopened, "CompleteWritten", step, writtenBytes);
        test.Equal(reopenedUsage, Usage(peer), "Known-size completion after reopen cannot release quota twice");
        Call(reopened, "DeleteOwned", step);
        test.Equal(reopenedUsage - writtenBytes, Usage(peer), "Deleting completed data releases its exact known-size charge");

        string empty = Child(disk, "complete-written-empty");
        long emptyBefore = Usage(peer);
        test.True(Reserve(disk, empty, MiB), "Empty known-size completion reserves capacity");
        Call(disk, "CompleteWritten", empty, 0L);
        test.Equal(emptyBefore, Usage(peer), "Zero-byte completion releases the whole reservation");
        Call(disk, "CompleteWritten", empty, 0L);
        Call(disk, "DeleteOwned", empty);
        test.Equal(emptyBefore, Usage(peer), "Repeated zero-byte completion and deletion do not change quota");
    }

    static void CheckReserveMarkerFailure(TestContext test, object disk, object peer)
    {
        string step = Child(disk, "marker-failure");
        string fault = Path.Combine(step, "reservation.tmp");
        Directory.CreateDirectory(fault);
        long before = Usage(peer);
        try
        {
            IoFailure(test, () => Reserve(disk, step, 2 * MiB), "Failed marker publication is reported");
            test.Equal(before, Usage(peer), "Failed marker publication does not leak quota");
            test.True(!File.Exists(Path.Combine(step, "reservation")), "Failed marker publication creates no live reservation");
        }
        finally { if (Directory.Exists(fault)) Directory.Delete(fault); }
        test.True(Reserve(disk, step, 2 * MiB), "Reservation can be retried after removing the owned marker fault");
        Call(disk, "DeleteOwned", step);
    }

    static void CheckReserveLedgerFailure(TestContext test, OwnedStore store, object disk, object peer)
    {
        string step = Child(disk, "reserve-ledger-failure");
        string fault = Child(disk, "session.json.tmp");
        long before = Usage(peer);
        Directory.CreateDirectory(fault);
        try
        {
            IoFailure(test, () => Reserve(disk, step, 2 * MiB), "Failed reservation charge publication is reported");
            bool marker = File.Exists(Path.Combine(step, "reservation"));
            long after = Usage(peer);
            test.True(marker ? after >= before + 2 * MiB : after == before,
                "Failed reservation is fully rolled back or remains conservatively charged");
        }
        finally { if (Directory.Exists(fault)) Directory.Delete(fault); }
        var reopened = store.Reopen(disk);
        if (Directory.Exists(step)) Call(reopened, "DeleteOwned", step);
        test.True(Reserve(reopened, step, 2 * MiB), "Reservation succeeds after fault recovery and reopen");
        Call(reopened, "DeleteOwned", step);
    }

    static void CheckCompleteLedgerFailure(TestContext test, OwnedStore store, object disk, object peer,
        byte[] payload, bool knownSize)
    {
        string method = knownSize ? "CompleteWritten" : "Complete";
        string step = Child(disk, knownSize ? "complete-written-ledger-failure" : "complete-ledger-failure");
        object[] args = knownSize ? new object[] { step, payload.LongLength } : new object[] { step };
        test.True(Reserve(disk, step, 4 * MiB), method + " fault fixture reserves capacity");
        string pixels = Path.Combine(step, "pixels.bin");
        File.WriteAllBytes(pixels, payload);
        byte[] marker = File.ReadAllBytes(Path.Combine(step, "reservation"));
        string fault = Child(disk, "session.json.tmp");
        long before = Usage(peer);
        Directory.CreateDirectory(fault);
        try
        {
            IoFailure(test, () => Call(disk, method, args), method + " reports failed charge publication");
            test.True(Usage(peer) >= before, method + " retains conservative quota after failure");
            SameFile(test, Path.Combine(step, "reservation"), marker, method + " retains its retryable reservation marker");
            SameFile(test, pixels, payload, method + " retains every payload byte after failure");
        }
        finally { if (Directory.Exists(fault)) Directory.Delete(fault); }
        var reopened = store.Reopen(disk);
        long retryBefore = Usage(peer);
        Call(reopened, method, args);
        long retryAfter = Usage(peer);
        test.Equal(retryBefore - 4 * MiB + payload.LongLength, retryAfter,
            method + " retry after reopen releases exactly the unused capacity");
        Call(disk, method, args);
        test.Equal(retryAfter, Usage(peer), method + " cannot double-release across old and reopened instances");
        SameFile(test, pixels, payload, method + " recovered completion preserves exact payload");
        Call(disk, "DeleteOwned", step);
    }
}
