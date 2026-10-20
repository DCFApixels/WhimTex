using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class PaintHistoryStore
    {
        private sealed class CheckpointWork
        {
            internal DrawingPixelHistory owner;
            internal Step step;
            internal int page;
            internal string temporary, destination;
            internal string[] files;
            internal PaintUndoDisk disk;
            internal Task prepare;
        }
        private static CheckpointWork checkpointWork;
        // One worker owns these four tile arrays, at most 2 MiB for HDR. Reuse across
        // pages avoids repeatedly allocating large arrays during a limit reduction.
        private static Block checkpointScratch;
        internal static string MaintenanceSlowestStage { get; private set; }
        internal static double MaintenanceSlowestStageMs { get; private set; }
        internal readonly struct MaintenanceScope : IDisposable
        {
            private readonly string name;
            private readonly long start;
            internal MaintenanceScope(string name) { this.name = name; start = Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (ms > MaintenanceSlowestStageMs) { MaintenanceSlowestStageMs = ms; MaintenanceSlowestStage = name; }
            }
        }
        internal static bool MaintenanceBusy => checkpointWork != null;
        internal static void StartCheckpointWork(DrawingPixelHistory owner, Step step, int page, string destination)
        {
            if (checkpointWork != null) throw new InvalidOperationException("Checkpoint work is already active.");
            var work = new CheckpointWork { owner = owner, step = step, page = page, destination = destination,
                disk = PaintUndoDisk.Current, temporary = Path.Combine(Root, "checkpoint-" + Guid.NewGuid().ToString("N")) };
            checkpointWork = work;
            // Only immutable page bytes and paths cross this boundary. Unity objects are used
            // only on the main thread; original pages stay readable throughout preparation.
            var ticket = WhimTexProfiler.QueueWorker("storage.pixel_history.checkpoint", step.profiling);
            try
            {
                work.prepare = Task.Run(() =>
                {
                    using var worker = ticket == null ? default : ticket.Start();
                    PrepareCheckpoint(work);
                });
            }
            catch { ticket?.Cancel(); checkpointWork = null; throw; }
        }
        private static void PrepareCheckpoint(CheckpointWork work)
        {
            long bytes = 65536 + PaintTileTransfer.TilesPerPage * (long)PaintTileSet.Size * PaintTileSet.Size * (8 + work.step.bytesPerPixel);
            if (!work.disk.Reserve(work.temporary, bytes, out var reason, true)) throw new IOException(reason);
            ValidatePage(work.step, work.page);
            var files = new List<string>();
            VisitPageForRetention(work.step, work.page, block =>
            {
                string path = Path.Combine(work.temporary, "base-" + block.rect.x + "-" + block.rect.y + ".bin");
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using var writer = new BinaryWriter(file);
                writer.Write(0x57544231); writer.Write(work.step.width); writer.Write(work.step.height); writer.Write(work.step.bytesPerPixel);
                writer.Write(block.rect.x); writer.Write(block.rect.y); writer.Write(block.rect.width); writer.Write(block.rect.height);
                var hash = new Hash128(); hash.Append(block.afterHalf); hash.Append(block.afterStored); writer.Write(hash.ToString());
                using var zip = new DeflateStream(file, System.IO.Compression.CompressionLevel.Fastest, true);
                zip.Write(block.afterHalf, 0, block.afterHalf.Length); zip.Write(block.afterStored, 0, block.afterStored.Length);
                files.Add(path);
            }, checkpointScratch ??= new Block());
            work.files = files.ToArray();
            work.disk.PublishCheckpointPage(work.temporary, work.destination, work.files);
        }
        private static void PublishCheckpointWork()
        {
            var work = checkpointWork;
            if (work == null || !work.prepare.IsCompleted) return;
            try
            {
                work.prepare.GetAwaiter().GetResult();
                // Pending source pages cover every replaced baseline tile. Only source retirement
                // runs on the main thread, so a Half rebuild cannot race deletion of its input.
                work.disk.DeleteOwnedFile(PagePath(work.step, work.page));
                if (work.owner != null) work.owner.CompleteCheckpointPage(work.step.id, work.page);
                checkpointWork = null;
            }
            catch
            {
                work.disk.DeleteOwned(work.temporary);
                checkpointWork = null;
                throw; // Original page remains available if preparation/publication failed.
            }
        }
        internal static void FinishCheckpointWork(DrawingPixelHistory owner = null)
        {
            if (checkpointWork == null || (owner != null && checkpointWork.owner != owner)) return;
            // Explicit clear, disk-pressure reclamation and assembly shutdown may synchronize.
            // Ordinary Editor updates never wait for the worker.
            try { checkpointWork.prepare.GetAwaiter().GetResult(); }
            catch { /* Publisher reports and cleans up the failed temporary reservation. */ }
            PublishCheckpointWork();
        }
    }
}
