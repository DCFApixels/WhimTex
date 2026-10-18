using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static partial class PaintTileHistoryTests
{
    public static string CheckHistoryWithoutBurst()
    {
        using var disk = new DiskScope();
        bool previous = Unity.Burst.BurstCompiler.Options.EnableBurstCompilation;
        try { Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = false; return RunRaw(); }
        finally
        {
            try { Static("PaintTileTransfer", "Drain"); Static("PaintHistoryStore", "FlushWrites"); }
            finally { Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = previous; }
        }
    }
    static long NativeBytes(string property) => (long)Type("PaintHistoryBuffers").GetProperty(property, S).GetValue(null);
    static void CheckPatchRows(TestContext t)
    {
        bool previous = Unity.Burst.BurstCompiler.Options.EnableBurstCompilation;
        try
        {
            foreach (bool accelerated in new[] { true, false })
            foreach (int bpp in new[] { 4, 8 })
            foreach (int width in new[] { 1, 63, 128, 256 })
            {
                Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = accelerated;
                using var cpu = new NativeArray<byte>(518 * 519 * bpp, Allocator.Persistent);
                using var after = new NativeArray<byte>(300 * 260 * bpp, Allocator.Persistent);
                var original = new byte[cpu.Length]; var source = new byte[after.Length];
                new System.Random(931 + width).NextBytes(original); new System.Random(413 + bpp).NextBytes(source);
                cpu.CopyFrom(original); after.CopyFrom(source);
                using var payload = (IDisposable)Activator.CreateInstance(Type("PaintHistoryPayload"), F, null,
                    new object[] { 300, 300 * 260, bpp, 1 }, null);
                var before = (NativeArray<byte>)Get(payload, "beforeStored"); before.AsSpan().Fill(0xA5);
                var expectedBefore = before.ToArray(); var expectedCpu = (byte[])original.Clone();
                var rect = new RectInt(518 - width, 263, width, 256);
                var slot = new RectInt(300 - width, 4, width, 256);
                for (int y = 0; y < rect.height; y++)
                {
                    int ci = ((rect.y + y) * 518 + rect.x) * bpp, pi = ((slot.y + y) * 300 + slot.x) * bpp;
                    Array.Copy(original, ci, expectedBefore, pi, width * bpp);
                    Array.Copy(source, pi, expectedCpu, ci, width * bpp);
                }
                Call(payload, "Add", cpu, 518, rect, slot, after);
                t.Equal(accelerated, (bool)Type("PaintHistoryPayload").GetProperty("LastPatchUsedBurst", S).GetValue(null), "Requested copy implementation used");
                Exact(t, expectedCpu, cpu.ToArray(), "Raw CPU patch including untouched rows and columns");
                Exact(t, expectedBefore, before.ToArray(), "Exact previous bytes and untouched atlas padding");
                Exact(t, source, after.ToArray(), "Readback plane stays immutable");
                bool refused = false;
                try { Call(payload, "Add", cpu, 518, new RectInt(517, 518, 2, 2), slot, after); }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException is ArgumentException) { refused = true; }
                t.True(refused, "Out-of-bounds copy rejected before writes");
                Exact(t, expectedCpu, cpu.ToArray(), "Rejected patch leaves CPU untouched");
            }
        }
        finally { Unity.Burst.BurstCompiler.Options.EnableBurstCompilation = previous; }
    }
    public static string CheckHistoryPages() => TestContext.Run("Native page ownership, reuse and failed-write Undo", t =>
    {
        using var disk = new DiskScope();
        t.Equal(0L, NativeBytes("ActiveBytes"), "No previous owned native consumers");
        CheckPatchRows(t);
        Static("PaintHistoryBuffers", "Clear");
        long allocated = NativeBytes("AllocatedBytes"), reused = NativeBytes("ReusedBytes");
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var mask = Mask();
        try
        {
            using var f = new Fixture(518, true);
            Set(f.settings, "brushSize", 256f);
            var rect = new RectInt(0, 0, 518, 518);
            var points = new[] { new Vector2(.25f, .25f), new Vector2(.8f, .8f) };
            f.Stroke(points, mask); Static("PaintHistoryStore", "FlushWrites");
            t.Equal(0L, NativeBytes("ActiveBytes"), "Completed pages return every native plane");
            t.True(NativeBytes("AllocatedBytes") > allocated, "First page allocates native storage");
            allocated = NativeBytes("AllocatedBytes");
            f.Stroke(points, mask); Static("PaintHistoryStore", "FlushWrites");
            t.Equal(allocated, NativeBytes("AllocatedBytes"), "Identical sequential page reuses storage without native allocation");
            t.True(NativeBytes("ReusedBytes") > reused, "Readback and old stored planes are reused");
            t.True(NativeBytes("IdleBytes") <= 64L * 1024 * 1024, "Shared idle native pool cap");

            var oldHalf = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect);
            var oldStored = f.pixels.GetRawTextureData<byte>().ToArray();
            f.Stroke(points, mask, drain: false);
            var cursor = Get(f.layer, "PixelUndoObject");
            var step = Static("PaintHistoryStore", "Get", (string)Get(cursor, "Revision"));
            // Make only this owned page's temporary output unwritable. It has not
            // entered CPU processing yet; no user paths or permissions are changed.
            string blocked = (string)Static("PaintHistoryStore", "PagePath", step, 0) + ".tmp";
            Directory.CreateDirectory(blocked);
            Static("PaintTileTransfer", "Drain");
            var page = ((Array)Get(step, "payload")).GetValue(0);
            var task = (Task)Get(page, "write");
            bool failed = false;
            try { task.GetAwaiter().GetResult(); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { failed = true; }
            t.True(failed && Get(page, "native") != null, "Failed publication retains immutable native payload");
            Set(page, "errorReported", true); // The expected injected failure needs no user-facing console error.
            Static("PaintHistoryStore", "FlushWrites");
            var newHalf = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect);
            var newStored = f.pixels.GetRawTextureData<byte>().ToArray();
            long retained = NativeBytes("ActiveBytes");
            t.True(retained > 0, "Failed page has not returned live memory to the pool");
            Undo.PerformUndo();
            Exact(t, oldHalf, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Failed write still supports exact Half Undo");
            Exact(t, oldStored, f.pixels.GetRawTextureData<byte>().ToArray(), "Failed write still supports exact stored Undo");
            Undo.PerformRedo();
            Exact(t, newHalf, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Failed write still supports exact Half Redo");
            Exact(t, newStored, f.pixels.GetRawTextureData<byte>().ToArray(), "Failed write still supports exact stored Redo");
            t.Equal(retained, NativeBytes("ActiveBytes"), "Replay never releases retained writer input");
            Static("PaintHistoryStore", "DeleteStep", step);
            t.Equal(0L, NativeBytes("ActiveBytes"), "Explicit removal releases the failed native payload exactly once");
            t.Equal(0L, (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null), "Writer reservation released after removal");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mask); RenderTexture.active = active; GL.sRGBWrite = srgb;
            Static("PaintHistoryBuffers", "Clear");
        }
        t.Equal(0L, NativeBytes("IdleBytes"), "Idle pool can be fully released");
    });
}
