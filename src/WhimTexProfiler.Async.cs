using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        internal readonly struct WorkContext
        {
            internal readonly string captureId;
            internal readonly Sample sample;
            internal WorkContext(Capture owner, Sample sample) { captureId = owner?.id; this.sample = sample; }
        }
        internal static WorkContext CaptureWorkContext() =>
            Recording && current >= 0 ? new WorkContext(capture, capture.events[current]) : default;

        // Worker threads only write their ticket. Capture arrays and hierarchy remain
        // main-thread owned; publishing never subtracts worker time from a main span.
        internal sealed class WorkerTicket
        {
            internal readonly Capture owner;
            internal readonly int slot;
            internal long started, ended, allocated, encode, write, hash;
            internal int thread, done;
            internal bool stagesMeasured;
            long allocationStart;
            internal WorkerTicket(Capture owner, int slot) { this.owner = owner; this.slot = slot; }
            internal WorkerScope Start()
            {
                started = Stopwatch.GetTimestamp(); thread = Thread.CurrentThread.ManagedThreadId;
                allocationStart = owner.collectAllocations ? GC.GetAllocatedBytesForCurrentThread() : -1;
                return new WorkerScope(this);
            }
            internal void Stages(long encodeTicks, long writeTicks, long hashTicks)
            { encode = encodeTicks; write = writeTicks; hash = hashTicks; stagesMeasured = true; }
            internal void Cancel() { started = ended = Stopwatch.GetTimestamp(); Volatile.Write(ref done, 2); }
            internal void Finish()
            {
                long count = allocationStart >= 0 ? GC.GetAllocatedBytesForCurrentThread() : -1;
                // Some Editor runtimes return a constant zero for this .NET API.
                // Do not report that as measured zero allocation traffic.
                allocated = allocationStart >= 0 && count > 0 && count >= allocationStart ? count - allocationStart : -1;
                ended = Stopwatch.GetTimestamp(); Volatile.Write(ref done, 1);
            }
        }
        internal readonly struct WorkerScope : IDisposable
        {
            readonly WorkerTicket ticket;
            internal WorkerScope(WorkerTicket ticket) { this.ticket = ticket; }
            public void Dispose() => ticket?.Finish();
        }
        static readonly List<WorkerTicket> workers = new();
        internal static WorkerTicket QueueWorker(string phase, WorkContext context = default)
        {
            if (!Recording || suppressed != 0) return null;
            Sample origin = context.captureId == capture.id ? context.sample : current < 0 ? default : capture.events[current];
            if (capture.documentId != null && capture.documentId != origin.documentId) return null;
            if (workers.Count >= 256) { Stop("worker_limit"); return null; }
            int slot = Reserve(); if (slot < 0) return null;
            var sample = origin;
            sample.id = sample.operationId = slot + 1L; sample.parentId = 0; sample.depth = 0;
            sample.originId = origin.id; sample.phase = phase; sample.kind = "worker";
            sample.start = Stopwatch.GetTimestamp(); sample.end = sample.childTicks = 0;
            sample.completed = sample.interrupted = sample.fxTiming = sample.allocationCountMeasured = false;
            sample.gpuStatus = sample.gpuDetail = null; sample.gpuNanoseconds = 0;
            sample.asyncStatus = "queued"; sample.threadId = 0;
            capture.events[slot] = sample; capture.open++;
            var ticket = new WorkerTicket(capture, slot); workers.Add(ticket); return ticket;
        }

        sealed class InputFlight
        {
            internal Capture owner;
            internal int slot;
            internal GraphicsFence fence;
            internal long submitted, polled;
        }
        static readonly Dictionary<string, InputFlight> inputs = new();
        static readonly List<InputFlight> completions = new();
        static long ActiveInputId(string doc) => doc != null && inputs.TryGetValue(doc, out var flight)
            ? flight.owner.events[flight.slot].id : 0;
        internal static void ActionInput(WhimTexDocument document, string reason) => BeginInput(document, reason, true);
        internal static void CanvasRequested(WhimTexDocument document, string reason) => BeginInput(document, reason, false);
        static void BeginInput(WhimTexDocument document, string reason, bool additionalInput)
        {
            if (!Recording || document == null) return;
            string doc = DocumentId(document);
            if (capture.documentId != null && capture.documentId != doc) return;
            if (inputs.TryGetValue(doc, out var flight))
            {
                if (!additionalInput) return;
                ref var pending = ref flight.owner.events[flight.slot];
                pending.latestInputTicks = Stopwatch.GetTimestamp(); pending.coalescedInputs++;
                return;
            }
            if (inputs.Count >= 64) { Stop("input_limit"); return; }
            int slot = Reserve(); if (slot < 0) return;
            var sample = CreateSample(slot, "input.canvas_ready", "latency", reason, doc);
            sample.inputId = sample.id;
            sample.originId = sample.parentId; sample.parentId = 0; sample.operationId = sample.id; sample.depth = 0;
            sample.latestInputTicks = sample.start; sample.coalescedInputs = 1; sample.threadId = mainThread;
            sample.asyncStatus = "waiting_for_canvas";
            capture.events[slot] = sample; capture.open++;
            inputs.Add(doc, new InputFlight { owner = capture, slot = slot });
        }
        internal static void CanvasReady(WhimTexDocument document)
        {
            if (!Recording || document == null || !inputs.Remove(DocumentId(document), out var flight)) return;
            ref var sample = ref flight.owner.events[flight.slot];
            sample.end = Stopwatch.GetTimestamp(); sample.completed = true; sample.asyncStatus = "canvas_ready";
            flight.owner.open--;
            if (!capture.collectGpu) return;
            int slot = Reserve(); if (slot < 0) return;
            var completion = sample;
            completion.id = completion.operationId = slot + 1L; completion.originId = sample.id;
            completion.phase = "input.gpu_observed"; completion.end = 0; completion.completed = false;
            completion.asyncStatus = "pending";
            capture.events[slot] = completion;
            try
            {
                if (!SystemInfo.supportsGraphicsFence) throw new NotSupportedException();
                if (completions.Count >= 64) throw new InvalidOperationException();
                var fence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.PixelProcessing);
                completions.Add(new InputFlight { owner = capture, slot = slot, fence = fence,
                    submitted = sample.end, polled = sample.end });
                capture.open++;
            }
            catch
            {
                capture.events[slot].interrupted = true; capture.events[slot].asyncStatus = "unavailable";
            }
        }
        static void StopPendingInputs(Capture owner)
        {
            foreach (var flight in inputs.Values)
            {
                ref var sample = ref flight.owner.events[flight.slot];
                sample.interrupted = true; sample.asyncStatus = "no_canvas_before_stop";
                flight.owner.open--;
            }
            inputs.Clear();
        }
        static void PollAsync()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) return;
            for (int i = workers.Count - 1; i >= 0; i--)
            {
                var ticket = workers[i]; int done = Volatile.Read(ref ticket.done);
                if (done == 0) continue;
                ref var sample = ref ticket.owner.events[ticket.slot];
                sample.queuedTicks = Math.Max(0, ticket.started - sample.start);
                sample.start = ticket.started; sample.end = ticket.ended; sample.threadId = ticket.thread;
                sample.workerAllocatedBytes = ticket.allocated;
                sample.workerStagesMeasured = ticket.stagesMeasured;
                sample.workerEncodeTicks = ticket.encode; sample.workerWriteTicks = ticket.write; sample.workerHashTicks = ticket.hash;
                sample.completed = done == 1; sample.interrupted = done != 1; sample.asyncStatus = done == 1 ? "finished" : "cancelled";
                ticket.owner.open--; workers.RemoveAt(i);
            }
            long now = Stopwatch.GetTimestamp();
            for (int i = completions.Count - 1; i >= 0; i--)
            {
                var flight = completions[i];
                string status = null;
                try { if (flight.fence.passed) status = "ready_observed"; }
                catch { status = "unavailable"; }
                if (status == null && now - flight.submitted >= 10 * Stopwatch.Frequency) status = "timeout";
                if (status != null)
                {
                    ref var sample = ref flight.owner.events[flight.slot];
                    sample.end = now; sample.completed = status == "ready_observed"; sample.interrupted = !sample.completed;
                    sample.asyncStatus = status; sample.completionPollIntervalMs = Milliseconds(now - flight.polled);
                    flight.owner.open--; completions.RemoveAt(i);
                }
                flight.polled = now;
            }
        }
    }
}
