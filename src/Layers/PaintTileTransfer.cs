using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    // Immutable packed pages keep asynchronous readback independent of later strokes.
    [InitializeOnLoad]
    internal static class PaintTileTransfer
    {
        internal const int TilesPerPage = 16;
        internal const int PageSize = 1024;
        internal const long StagingBudget = 256L * 1024 * 1024;
        private static readonly Queue<Page> pending = new();
        private static readonly Queue<Page> awaitingReadback = new();
        private static int reading;
        private static bool draining;
        internal static int PendingCount => pending.Count;
        internal static long StagingBytes { get; private set; }
        internal static long CapacityWaits { get; private set; }
        internal static long TransferredBytes { get; private set; }
        internal static long ReadbackBytes { get; private set; }
        private const CopyTextureSupport RequiredCopy = CopyTextureSupport.Basic | CopyTextureSupport.RTToTexture | CopyTextureSupport.TextureToRT;
        internal static bool Supported => (SystemInfo.copyTextureSupport & RequiredCopy) == RequiredCopy;
        internal static bool SupportsPixels(Texture2D texture) => Supported && texture != null && texture.isReadable && texture.mipmapCount == 1 &&
            (texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.RGBAHalf) &&
            SystemInfo.IsFormatSupported(texture.graphicsFormat, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Render);
        static PaintTileTransfer()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += DrainAll;
            EditorApplication.quitting += Quit;
        }
        private static void Update()
        {
            try { Process(false); PaintHistoryStore.Maintenance(); }
            catch (Exception error) { Debug.LogException(error); }
        }
        // SessionState keeps the cursor across reload; its journal must be durable first.
        private static void DrainAll() { Drain(); PaintHistoryStore.FlushWrites(); PaintHistoryStore.FinishCheckpointWork(); }
        private static void Quit() { DrainAll(); PaintHistoryStore.ClearSession(); }
        internal static void Drain() => Process(true);
        private static void MakeRoom(long bytes)
        {
            using var profile = WhimTexProfiler.Measure("brush.readback.makeRoom");
            while (pending.Count > 0 && StagingBytes + bytes > StagingBudget)
            { CapacityWaits++; Process(true, 1); }
        }
        private static void StartReadbacks()
        {
            while (reading < 2 && awaitingReadback.Count > 0)
            {
                var page = awaitingReadback.Peek();
                page.StartReadback(); awaitingReadback.Dequeue(); reading++;
            }
        }
        private static void Process(bool wait, int maximum = int.MaxValue)
        {
            if (draining) return;
            draining = true;
            long deadline = wait ? long.MaxValue : System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 250;
            try
            {
                StartReadbacks();
                while (pending.Count > 0 && maximum-- > 0)
                {
                    var page = pending.Peek();
                    if (!page.Ready(wait)) break;
                    if (!page.Apply(wait, deadline)) break;
                    pending.Dequeue(); reading--; StagingBytes -= page.StagingSize; page.Dispose();
                    StartReadbacks();
                    if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) break;
                }
            }
            finally { draining = false; }
        }
        internal static RectInt Slot(int index, RectInt source) => new RectInt(index % 4 * PaintTileSet.Size,
            index / 4 * PaintTileSet.Size, source.width, source.height);
        internal sealed class Page : IDisposable
        {
            internal readonly RectInt[] blocks;
            internal readonly RenderTexture before, after, stored;
            private readonly Texture2D texture;
            private readonly PaintHistoryStore.Step step;
            private readonly int pageIndex, bpp;
            private readonly AsyncGPUReadbackRequest[] requests = new AsyncGPUReadbackRequest[3];
            private readonly NativeArray<byte>[] data = new NativeArray<byte>[3];
            private readonly bool[] complete = new bool[3], failed = new bool[3];
            private bool queued;
            private PaintHistoryStore.Block[] payload;
            private int captured;
            internal long StagingSize { get; }
            private readonly long payloadSize;
            internal Page(Texture2D texture, PaintHistoryStore.Step step, int pageIndex, RectInt[] blocks)
            {
                this.texture = texture; this.step = step; this.pageIndex = pageIndex; this.blocks = blocks;
                bpp = HdrUtility.IsHdr(texture) ? 8 : 4;
                int width = Math.Min(4, blocks.Length) * PaintTileSet.Size;
                int height = ((blocks.Length + 3) / 4) * PaintTileSet.Size;
                StagingSize = (long)width * height * (16 + bpp);
                foreach (var block in blocks) payloadSize += (long)block.width * block.height * (16 + 2 * bpp);
                MakeRoom(StagingSize);
                try
                {
                    using var profile = WhimTexProfiler.Measure("brush.history.allocatePages");
                    before = Allocate(width, height, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
                    after = Allocate(width, height, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);
                    stored = Allocate(width, height, texture.graphicsFormat);
                }
                catch { Dispose(); throw; }
            }
            private static RenderTexture Allocate(int width, int height, UnityEngine.Experimental.Rendering.GraphicsFormat format)
            {
                var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = format, depthBufferBits = 0,
                    msaaSamples = 1, mipCount = 1, useMipMap = false, autoGenerateMips = false };
                var result = ProfiledRenderTexture.GetTemporary(descriptor);
                result.filterMode = FilterMode.Point;
                var previous = RenderTexture.active;
                try { RenderTexture.active = result; GL.Clear(false, true, Color.clear); }
                finally { RenderTexture.active = previous; }
                return result;
            }
            internal void CopyHalf(RenderTexture surface, bool old)
            {
                using var profile = WhimTexProfiler.Measure(old ? "brush.history.copyBefore" : "brush.history.copyAfter");
                RenderTexture.active = null;
                var destination = old ? before : after;
                for (int i = 0; i < blocks.Length; i++)
                { var r = blocks[i]; var s = Slot(i, r); Graphics.CopyTexture(surface, 0, 0, r.x, r.y, r.width, r.height, destination, 0, 0, s.x, s.y); }
            }
            internal void Queue()
            {
                using var profile = WhimTexProfiler.Measure("brush.readback.queue");
                ReadbackBytes += (long)stored.width * stored.height * (16 + bpp);
                RenderTexture.active = null;
                for (int i = 0; i < blocks.Length; i++)
                { var r = blocks[i]; var s = Slot(i, r); Graphics.CopyTexture(stored, 0, 0, s.x, s.y, r.width, r.height, texture, 0, 0, r.x, r.y); }
                // Later strokes may immediately reuse their brush buffer and mutate
                // paintSurface. These three immutable pages own this revision's pixels.
                queued = true; pending.Enqueue(this); awaitingReadback.Enqueue(this);
                StagingBytes += StagingSize;
                if (SystemInfo.supportsAsyncGPUReadback) StartReadbacks();
            }
            internal void StartReadback()
            {
                using var profile = WhimTexProfiler.Measure("brush.readback.submit");
                int submitted = 0;
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int index = i;
                        var source = i == 0 ? before : i == 1 ? after : stored;
                        data[i] = new NativeArray<byte>(source.width * source.height * (i == 2 ? bpp : 8), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                        if (SystemInfo.supportsAsyncGPUReadback)
                        {
                            requests[i] = AsyncGPUReadback.RequestIntoNativeArray(ref data[i], source, 0,
                                request => { failed[index] = request.hasError; complete[index] = true; });
                            submitted++;
                        }
                        else { data[i].CopyFrom(Read(source, i == 2 && bpp == 4)); complete[i] = true; }
                    }
                }
                catch
                {
                    for (int i = 0; i < submitted; i++) if (!complete[i]) requests[i].WaitForCompletion();
                    for (int i = 0; i < data.Length; i++)
                    { if (data[i].IsCreated) data[i].Dispose(); complete[i] = failed[i] = false; }
                    throw; // Retain queued GPU pages so a later update can retry safely.
                }
            }
            internal bool Ready(bool wait)
            {
                using var profile = WhimTexProfiler.Measure(wait ? "brush.readback.wait" : "brush.readback.poll");
                for (int i = 0; i < 3; i++)
                {
                    if (!complete[i])
                    {
                        if (wait) requests[i].WaitForCompletion();
                        if (!requests[i].done) return false;
                        failed[i] = requests[i].hasError; complete[i] = true;
                    }
                    if (failed[i])
                    {
                        data[i].CopyFrom(Read(i == 0 ? before : i == 1 ? after : stored, i == 2 && bpp == 4));
                        failed[i] = false;
                    }
                }
                return true;
            }
            private static byte[] Read(RenderTexture source, bool standard)
            {
                var previous = RenderTexture.active;
                var copy = new Texture2D(source.width, source.height, standard ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, false, true);
                try { RenderTexture.active = source; copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); return copy.GetRawTextureData<byte>().ToArray(); }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
            }
            internal bool Apply(bool wait, long deadline)
            {
                using var profile = WhimTexProfiler.Measure("brush.history.capture");
                if (texture == null) throw new InvalidOperationException("Drawing pixels were released before their transfer completed.");
                if (payload == null)
                {
                    // Reserve room for a whole page before patching any CPU pixels.
                    // Normal Editor updates never wait for the journal writer.
                    if (!PaintHistoryStore.MakeRoom(payloadSize, wait)) return false;
                    payload = new PaintHistoryStore.Block[blocks.Length];
                }
                var cpu = texture.GetRawTextureData<byte>();
                using (WhimTexProfiler.Measure("brush.history.extractAndPatchCpu"))
                for (; captured < blocks.Length;)
                {
                    int i = captured;
                    var r = blocks[i]; var s = Slot(i, r);
                    var block = new PaintHistoryStore.Block { rect = r,
                        beforeHalf = Extract(data[0], s, 8), afterHalf = Extract(data[1], s, 8),
                        beforeStored = PaintHistoryStore.ReadRows(cpu, texture.width, r, bpp), afterStored = Extract(data[2], s, bpp) };
                    PaintHistoryStore.WriteRows(cpu, texture.width, r, bpp, block.afterStored);
                    payload[i] = block;
                    TransferredBytes += (long)r.width * r.height * (16 + bpp);
                    captured++;
                    if (captured < blocks.Length && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) return false;
                }
                PaintHistoryStore.Store(step, pageIndex, payload);
                EditorUtility.SetDirty(texture);
                return true;
            }
            private byte[] Extract(NativeArray<byte> source, RectInt rect, int bpp)
            {
                var result = PaintHistoryStore.RentBuffer(rect.width * rect.height * bpp);
                for (int y = 0; y < rect.height; y++) NativeArray<byte>.Copy(source, ((rect.y + y) * before.width + rect.x) * bpp,
                    result, y * rect.width * bpp, rect.width * bpp);
                return result;
            }
            internal void Abandon() { if (!queued) Dispose(); }
            public void Dispose()
            {
                for (int i = 0; i < data.Length; i++) if (data[i].IsCreated) data[i].Dispose();
                if (before != null) ProfiledRenderTexture.ReleaseTemporary(before);
                if (after != null) ProfiledRenderTexture.ReleaseTemporary(after);
                if (stored != null) ProfiledRenderTexture.ReleaseTemporary(stored);
            }
        }
    }
}
