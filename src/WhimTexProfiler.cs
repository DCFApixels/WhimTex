using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Profiling;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    // Main-thread wall-time instrumentation. GPU submissions and Job.Complete waits
    // are deliberately not labelled as GPU time or CPU execution time.
    [InitializeOnLoad]
    internal static partial class WhimTexProfiler
    {
        [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
        internal struct Sample
        {
            internal long id, parentId, operationId, changeId;
            internal int fxIndex, depth;
            internal string documentId, layerId, layerName, phase, kind, detail;
            internal string fxName, gpuStatus, gpuDetail;
            internal string changeReason, changeOrigin, changedLayerId, changedLayerName, resourceKey;
            internal long changeEventId;
            internal long cpuPrepareTicks, cpuSubmitTicks, gpuNanoseconds;
            internal long start, end, childTicks;
            internal long gcCountStart, gcCountEpoch, gcAllocationCount, gcChildAllocationCount;
            internal bool allocationCountMeasured;
            internal long causeEventId;
            internal long[] causeEventIds;
            internal string causeLayerId, causeLayerName, causeProperty, causeKind;
            internal double value;
            internal bool completed, interrupted, fxTiming;
        }

        [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
        internal sealed class Capture
        {
            internal string id, stopReason, unityVersion;
            internal string renderingThreadingMode;
            internal bool? graphicsJobs;
            internal Sample[] events;
            internal long start, end, deadline;
            internal int count, dropped, open, gpuRevision;
            internal string documentId;
            internal bool recording;
            internal bool causeTrackingLimited;
            internal MemoryTotals memory;
        }

        struct Change
        {
            internal long id, eventId;
            internal string reason, origin, layerId, layerName;
        }
        static readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        static readonly Dictionary<string, ProfilerMarker> markers = new Dictionary<string, ProfilerMarker>(StringComparer.Ordinal);
        static readonly Dictionary<string, Change> changes = new Dictionary<string, Change>();
        static readonly Dictionary<UnityObjectID, string> documentIds = new Dictionary<UnityObjectID, string>();
        static readonly ProfilerMarker fallbackMarker = new ProfilerMarker("WhimTex.Other");
        static Capture capture;
        internal static Capture Baseline { get; private set; }
        static int current = -1, suppressed, instrumentationSuspended;
        static long nextChange;
        internal static bool Recording => capture?.recording == true && instrumentationSuspended == 0 && Thread.CurrentThread.ManagedThreadId == mainThread;
        internal static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        internal static Capture Current { get { CheckDeadline(); return capture; } }

        static WhimTexProfiler()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += SaveForReload;
            EditorApplication.quitting += () => { Stop("editor_quit"); ShutdownGpu(); DiscardReloadBackup(); DiscardBaselineBackup(); };
            RestoreAfterReload();
            RestoreBaselineAfterReload();
        }

        internal static string DocumentId(WhimTexDocument document)
        {
            if (document == null) return null;
            var key = UnityObjectID.FromObject(document);
            if (documentIds.TryGetValue(key, out string id)) return id;
            id = key.ToString();
            if (Recording && documentIds.Count < 256) documentIds.Add(key, id);
            return id;
        }

        internal static Capture Start(int capacity = 32768, double seconds = 120, string documentId = null)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Profiling control requires the main thread.");
            if (capture?.recording == true || capture?.open > 0) throw new InvalidOperationException("A capture is already active or has unfinished scopes.");
            if (capacity < 256 || capacity > 262144 || !double.IsFinite(seconds) || seconds < 1 || seconds > 600)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            DiscardReloadBackup();
            AbandonGpuCapture();
            long now = Stopwatch.GetTimestamp();
            capture = new Capture { id = Guid.NewGuid().ToString("N"), events = new Sample[capacity],
                start = now, deadline = now + (long)(seconds * Stopwatch.Frequency), recording = true, documentId = documentId,
                unityVersion = UnityEngine.Application.unityVersion,
                renderingThreadingMode = UnityEngine.SystemInfo.renderingThreadingMode.ToString(),
                graphicsJobs = GraphicsJobsActive(UnityEngine.SystemInfo.renderingThreadingMode) };
            current = -1; suppressed = 0; nextChange = 0; changes.Clear(); documentIds.Clear();
            ResetMemory(); ResetCauses();
            capture.memory = new MemoryTotals();
            SampleProcessMemory(capture);
            PrepareGpuCapture(capture);
            PrepareAllocations(capture);
            return capture;
        }

        internal static void Stop(string reason = "manual")
        {
            if (capture?.recording != true) return;
            SampleEditorAllocations(capture);
            capture.recording = false; capture.end = Stopwatch.GetTimestamp(); capture.stopReason = reason;
            SampleProcessMemory(capture);
            // The capture keeps scalar totals and recorded links, not live cache owners
            // or before-state snapshots after recording has ended.
            ResetMemory(); ResetCauses();
        }

        internal static void Clear()
        {
            if (capture?.recording == true || capture?.open > 0) throw new InvalidOperationException("Stop the capture and finish active scopes before clearing.");
            DiscardReloadBackup();
            AbandonGpuCapture();
            capture = null; current = -1; changes.Clear(); documentIds.Clear();
            ResetMemory(); ResetCauses();
        }

        internal static void PinBaseline()
        {
            if (capture == null || capture.recording || capture.open != 0 || PendingGpu(capture) != 0)
                throw new InvalidOperationException("Stop recording and wait for scopes and GPU results before setting a baseline.");
            var copy = new Sample[Math.Max(256, capture.count)];
            Array.Copy(capture.events, copy, capture.count);
            var frozen = new Capture { id = capture.id, events = copy, count = capture.count, start = capture.start,
                end = capture.end, stopReason = capture.stopReason, documentId = capture.documentId,
                dropped = capture.dropped, unityVersion = capture.unityVersion ?? UnityEngine.Application.unityVersion,
                renderingThreadingMode = capture.renderingThreadingMode, graphicsJobs = capture.graphicsJobs,
                memory = capture.memory?.Copy() };
            frozen.causeTrackingLimited = capture.causeTrackingLimited;
            DiscardBaselineBackup();
            Baseline = frozen;
        }

        internal static void ClearBaseline()
        {
            DiscardBaselineBackup();
            Baseline = null;
        }

        static void Update() { CheckDeadline(); PollGpu(); PollAllocations(); }

        static void CheckDeadline()
        {
            if (capture?.recording == true && Stopwatch.GetTimestamp() >= capture.deadline) Stop("time_limit");
        }

        static int Reserve()
        {
            if (!Recording || suppressed != 0) return -1;
            CheckDeadline();
            if (!capture.recording) return -1;
            if (capture.count == capture.events.Length)
            {
                capture.dropped++; Stop("event_limit"); return -1;
            }
            return capture.count++;
        }

        static Sample CreateSample(int slot, string phase, string kind, string detail,
            string documentId = null, string layerId = null, int fxIndex = -1, string layerName = null)
        {
            Sample parent = current < 0 ? default : capture.events[current];
            string doc = documentId ?? parent.documentId;
            Change change = default;
            if (doc != null) changes.TryGetValue(doc, out change);
            return new Sample { id = slot + 1L, parentId = parent.id,
                operationId = current < 0 ? slot + 1L : parent.operationId,
                changeId = change.id, changeEventId = change.eventId,
                changeReason = change.reason, changeOrigin = change.origin,
                changedLayerId = change.layerId, changedLayerName = change.layerName,
                documentId = doc, layerId = layerId ?? (doc == parent.documentId ? parent.layerId : null),
                layerName = layerId != null ? layerName : doc == parent.documentId ? parent.layerName : null,
                depth = current < 0 ? 0 : parent.depth + 1,
                fxIndex = fxIndex >= 0 ? fxIndex : current < 0 || layerId != null || doc != parent.documentId ? -1 : parent.fxIndex,
                fxName = phase == "fx.render" ? detail : layerId == null && doc == parent.documentId ? parent.fxName : null,
                phase = phase, kind = kind, detail = detail ?? (current < 0 ? change.reason : null), start = Stopwatch.GetTimestamp() };
        }

        internal static Scope Measure(string phase, WhimTexDocument document = null, Layer layer = null,
            int fxIndex = -1, string detail = null) => new Scope(phase, document, layer, fxIndex, detail);

        internal readonly struct Scope : IDisposable
        {
            readonly Capture owner;
            readonly int slot, parent;
            readonly bool filtered, native;
            readonly ProfilerMarker marker;
            readonly WhimTexHangDiagnostics.Scope diagnostic;

            internal GpuScope Gpu() => BeginGpu(owner, slot);

            internal Scope(string phase, WhimTexDocument document, Layer layer, int fxIndex, string detail)
            {
                owner = null; slot = parent = -1; filtered = native = false; marker = default; diagnostic = default;
                if (Thread.CurrentThread.ManagedThreadId != mainThread || instrumentationSuspended != 0) return;
                diagnostic = WhimTexHangDiagnostics.Enter(phase);
                if (UnityEngine.Profiling.Profiler.enabled)
                {
                    if (!markers.TryGetValue(phase, out marker))
                    {
                        marker = markers.Count < 256 ? new ProfilerMarker("WhimTex." + phase) : fallbackMarker;
                        if (markers.Count < 256) markers.Add(phase, marker);
                    }
                    marker.Begin(); native = true;
                }
                if (!Recording) return;
                string doc = DocumentId(document);
                if (suppressed != 0 || capture.documentId != null && doc != null && capture.documentId != doc)
                { suppressed++; filtered = true; return; }
                if (capture.documentId != null && doc == null && current < 0) return;
                slot = Reserve();
                if (slot < 0) return;
                owner = capture; parent = current;
                owner.memory ??= new MemoryTotals();
                owner.events[slot] = CreateSample(slot, phase, "span", detail, doc, layer?.Id, fxIndex, layer?.layerName);
                owner.events[slot].fxTiming = phase == "fx.block" || phase == "fx.render";
                owner.events[slot].allocationCountMeasured = ReadAllocationCount(owner,
                    out owner.events[slot].gcCountStart, out owner.events[slot].gcCountEpoch);
                current = slot; owner.open++;
            }

            public void Dispose()
            {
                try { Complete(); }
                finally { diagnostic.Dispose(); }
            }

            void Complete()
            {
                if (native) marker.End();
                if (filtered) { suppressed--; return; }
                if (owner == null || owner.events[slot].completed) return;
                long end = Stopwatch.GetTimestamp();
                ref Sample sample = ref owner.events[slot];
                sample.end = end; sample.completed = true;
                sample.allocationCountMeasured &= ReadAllocationCount(owner, out long gcCount, out long gcEpoch) && gcEpoch == sample.gcCountEpoch;
                sample.gcAllocationCount = sample.allocationCountMeasured ? Math.Max(0, gcCount - sample.gcCountStart) : 0;
                owner.open--;
                if (parent < 0 && owner.memory != null)
                {
                    SampleProcessMemory(owner);
                }
                if (parent >= 0)
                {
                    ref Sample enclosing = ref owner.events[parent];
                    enclosing.childTicks += end - sample.start;
                    enclosing.gcChildAllocationCount += sample.gcAllocationCount;
                    enclosing.allocationCountMeasured &= sample.allocationCountMeasured;
                    if (sample.phase == "fx.prepare") enclosing.cpuPrepareTicks += end - sample.start;
                    if (sample.phase == "fx.submit") enclosing.cpuSubmitTicks += end - sample.start;
                    if (sample.phase == "fx.render" && enclosing.phase == "fx.block")
                    {
                        enclosing.cpuPrepareTicks += sample.cpuPrepareTicks;
                        enclosing.cpuSubmitTicks += sample.cpuSubmitTicks;
                    }
                }
                if (ReferenceEquals(capture, owner)) current = parent;
            }
        }

        internal static void Event(string phase, string kind, string detail = null, double value = 0)
        {
            if (!Recording || current < 0) return;
            int slot = Reserve(); if (slot < 0) return;
            Sample sample = CreateSample(slot, phase, kind, detail);
            sample.completed = true; sample.end = sample.start; sample.value = value;
            capture.events[slot] = sample;
        }

        internal static void Changed(WhimTexDocument document, string reason)
        {
            if (!Recording || document == null) return;
            string doc = DocumentId(document);
            if (capture.documentId != null && capture.documentId != doc) return;
            if (!changes.ContainsKey(doc) && changes.Count >= 256) { Stop("document_limit"); return; }
            Sample origin = current < 0 ? default : capture.events[current];
            while (origin.phase == "edit.commit" && origin.parentId > 0)
                origin = capture.events[(int)origin.parentId - 1];
            bool sameDocument = origin.documentId == doc;
            var change = new Change { id = ++nextChange, reason = reason,
                origin = sameDocument ? origin.phase : null,
                layerId = sameDocument ? origin.layerId : null,
                layerName = sameDocument ? origin.layerName : null };
            changes[doc] = change;
            int slot = Reserve(); if (slot < 0) return;
            change.eventId = slot + 1L;
            changes[doc] = change;
            var sample = CreateSample(slot, "document.changed", "change", reason, doc);
            sample.changeId = change.id; sample.completed = true; sample.end = sample.start;
            capture.events[slot] = sample;
        }

        internal static void Reason(string phase, string reason, WhimTexDocument document = null,
            Layer layer = null, string resourceKey = null, double count = 1)
        {
            if (!Recording || suppressed != 0) return;
            string doc = document == null ? current < 0 ? null : capture.events[current].documentId : DocumentId(document);
            if (capture.documentId != null && capture.documentId != doc) return;
            int slot = Reserve(); if (slot < 0) return;
            var sample = CreateSample(slot, phase, "reason", reason, doc, layer?.Id, layerName: layer?.layerName);
            sample.completed = true; sample.end = sample.start;
            sample.resourceKey = resourceKey; sample.value = count;
            AttachCause(ref sample);
            capture.events[slot] = sample;
        }
    }
}
