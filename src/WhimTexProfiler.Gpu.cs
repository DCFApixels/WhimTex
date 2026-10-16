using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        // One in-flight invocation per marker, never a frame total attributed to several operations.
        // Retire timed-out slots: a late GPU result must not be assigned to a newer invocation.
        // These are elapsed GPU timeline intervals, including submission gaps/idle time. Nested
        // intervals overlap and must not be summed to claim total GPU work or active execution.
        const int MaxGpuSlots = 128;
        const double GpuTimeoutSeconds = 10;
        static readonly List<GpuSlot> gpuSlots = new List<GpuSlot>();
        static readonly string gpuSession = Guid.NewGuid().ToString("N");
        static CommandBuffer gpuCommands;
        static ProfilerMarker gpuActivationMarker;
        static ProfilerRecorder gpuActivation;
        static Capture gpuCapture;
        static bool gpuEnabled, gpuReady;
        static long gpuStartedAt;
        static string gpuAvailability = "idle";

        internal static string GpuAvailability => gpuAvailability;

        internal static void PrepareGpuCapture(Capture owner)
        {
            gpuCapture = owner;
            try
            {
                if (!SystemInfo.supportsGpuRecorder) { gpuAvailability = "unsupported"; return; }
                if (gpuEnabled) return;
                if (!gpuActivation.Valid)
                {
                    gpuActivationMarker = new ProfilerMarker(ProfilerCategory.Render,
                        "WhimTex.GPU.Activation." + gpuSession, MarkerFlags.SampleGPU);
                    gpuActivation = new ProfilerRecorder(gpuActivationMarker, 8,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.GpuRecorder);
                }
                gpuActivation.Reset(); gpuActivation.Start();
                foreach (GpuSlot slot in gpuSlots)
                    if (!slot.busy && !slot.retired && slot.recorder.Valid) { slot.recorder.Reset(); slot.recorder.Start(); }
                gpuEnabled = true; gpuReady = false; gpuAvailability = "warming_up";
                gpuStartedAt = Stopwatch.GetTimestamp();
            }
            catch
            {
                StopGpuRecorders();
                gpuEnabled = gpuReady = false;
                gpuAvailability = "error";
            }
        }

        sealed class GpuSlot
        {
            internal ProfilerMarker marker;
            internal ProfilerRecorder recorder;
            internal Capture owner;
            internal int sample, generation;
            internal readonly List<ProfilerRecorderSample> samples = new List<ProfilerRecorderSample>(8);
            internal long endedAt;
            internal bool busy, ended, retired;
        }

        internal readonly struct GpuScope : IDisposable
        {
            readonly int index, generation;
            internal GpuScope(int index, int generation) { this.index = index; this.generation = generation; }
            public void Dispose()
            {
                if (generation == 0 || (uint)index >= (uint)gpuSlots.Count) return;
                GpuSlot slot = gpuSlots[index];
                if (!slot.busy || slot.generation != generation || slot.ended) return;
                try { SubmitGpuMarker(slot.marker, false); }
                catch (Exception error) { GpuError(slot, error); }
                slot.ended = true;
                slot.endedAt = Stopwatch.GetTimestamp();
            }
        }

        static GpuScope BeginGpu(Capture owner, int sample)
        {
            if (owner?.events == null || (uint)sample >= (uint)owner.events.Length) return default;
            // A copied CPU Scope must not start another query for the same invocation or replace
            // an unavailable result with a later, unrelated interval.
            if (owner.events[sample].gpuStatus != null) return default;
            GpuSlot slot = null;
            try
            {
                if (!SystemInfo.supportsGpuRecorder)
                { owner.events[sample].gpuStatus = "unsupported"; return default; }
                if (!ReferenceEquals(gpuCapture, owner)) PrepareGpuCapture(owner);
                if (!gpuReady) { owner.events[sample].gpuStatus = gpuAvailability; return default; }
                int index = gpuSlots.FindIndex(s => !s.busy && !s.retired);
                if (index < 0)
                {
                    if (gpuSlots.Count >= MaxGpuSlots)
                    { owner.events[sample].gpuStatus = "capacity"; return default; }
                    index = gpuSlots.Count;
                    slot = new GpuSlot();
                    gpuSlots.Add(slot);
                    // Marker creation can fail before a recorder exists. Keep it inside the same
                    // failure boundary, and retire even that identity rather than retrying forever.
                    slot.marker = new ProfilerMarker(ProfilerCategory.Render,
                        "WhimTex.GPU." + gpuSession + "." + index, MarkerFlags.SampleGPU);
                }
                else slot = gpuSlots[index];
                slot.owner = owner; slot.sample = sample; slot.busy = true; slot.ended = false;
                unchecked { slot.generation++; }
                if (slot.generation == 0) slot.generation = 1;
                owner.events[sample].gpuStatus = "pending";
                // Unity requires frame aggregation for GPU markers. This marker is submitted only
                // once until its result is consumed, so Count must be one (never several operations).
                if (!slot.recorder.Valid)
                    slot.recorder = new ProfilerRecorder(slot.marker, 8,
                        ProfilerRecorderOptions.GpuRecorder | ProfilerRecorderOptions.Default);
                if (!slot.recorder.Valid)
                { CompleteGpu(slot, "unsupported", retire: true); return default; }
                slot.recorder.CopyTo(slot.samples, true);
                if (!slot.recorder.IsRunning) slot.recorder.Start();
                SubmitGpuMarker(slot.marker, true);
                return new GpuScope(index, slot.generation);
            }
            catch (Exception error)
            {
                if (slot != null)
                {
                    slot.owner = owner; slot.sample = sample;
                    GpuError(slot, error);
                }
                else
                {
                    owner.events[sample].gpuStatus = "error";
                    owner.events[sample].gpuDetail = error.GetType().Name + ": " + error.Message;
                    owner.gpuRevision++;
                }
                return default;
            }
        }

        static void SubmitGpuMarker(ProfilerMarker marker, bool begin)
        {
            // Marker-only buffers bracket the existing immediate blits. In particular, do not
            // replace Graphics.Blit with CommandBuffer.Blit (different render-state semantics).
            // A named buffer adds its own implicit sample and would unbalance a scope spanning buffers.
            gpuCommands ??= new CommandBuffer();
            gpuCommands.Clear();
            if (begin) gpuCommands.BeginSample(marker); else gpuCommands.EndSample(marker);
            Graphics.ExecuteCommandBuffer(gpuCommands);
        }

        static void PollGpu()
        {
            long now = Stopwatch.GetTimestamp();
            if (gpuEnabled && gpuReady && (gpuCapture?.recording == true || gpuSlots.Exists(s => s.busy)))
            {
                // Sparse Editor rendering still needs frames carrying a GPU marker for delayed
                // results to arrive. This empty heartbeat does not replay a document or draw pixels.
                try { SubmitGpuMarker(gpuActivationMarker, true); SubmitGpuMarker(gpuActivationMarker, false); }
                catch { gpuReady = false; gpuAvailability = "error"; }
            }
            if (gpuEnabled && !gpuReady && gpuAvailability == "warming_up")
            {
                try
                {
                    for (int i = 0; i < gpuActivation.Count; i++)
                        if (gpuActivation.GetSample(i).Count > 0) { gpuReady = true; gpuAvailability = "ready"; break; }
                    if (!gpuReady)
                    {
                        if (Milliseconds(now - gpuStartedAt) >= GpuTimeoutSeconds * 1000) gpuAvailability = "timeout";
                        else
                        {
                            // No draw or replay: an empty marker checks whether this Editor is actually
                            // returning GPU timestamps before attributing any to document renders.
                            SubmitGpuMarker(gpuActivationMarker, true); SubmitGpuMarker(gpuActivationMarker, false);
                        }
                    }
                }
                catch { gpuAvailability = "error"; }
            }
            foreach (GpuSlot slot in gpuSlots)
            {
                if (!slot.busy || !slot.ended) continue;
                try
                {
                    for (int i = 0; i < slot.recorder.Count; i++)
                    {
                        ProfilerRecorderSample result = slot.recorder.GetSample(i);
                        if (result.Count == 0) continue;
                        if (result.Count != 1 || result.Value < 0)
                            CompleteGpu(slot, "ambiguous", retire: true);
                        else
                        {
                            if (slot.owner != null) slot.owner.events[slot.sample].gpuNanoseconds = result.Value;
                            CompleteGpu(slot, "ready");
                        }
                        break;
                    }
                    if (slot.busy && Milliseconds(now - slot.endedAt) >= GpuTimeoutSeconds * 1000)
                        CompleteGpu(slot, "timeout", retire: true);
                }
                catch (Exception error) { GpuError(slot, error); }
            }
            if (gpuEnabled && gpuCapture?.recording != true && gpuCapture?.open is not > 0 && !gpuSlots.Exists(s => s.busy))
            {
                StopGpuRecorders();
                gpuCapture = null; gpuEnabled = gpuReady = false; gpuAvailability = "idle";
            }
        }

        static void GpuError(GpuSlot slot, Exception error)
        {
            if (slot.owner != null) slot.owner.events[slot.sample].gpuDetail = error.GetType().Name + ": " + error.Message;
            CompleteGpu(slot, "error", retire: true);
        }

        static void CompleteGpu(GpuSlot slot, string status, bool retire = false)
        {
            if (slot.owner != null)
            {
                slot.owner.events[slot.sample].gpuStatus = status;
                slot.owner.gpuRevision++;
            }
            slot.owner = null;
            slot.busy = false;
            slot.retired |= retire;
            try
            {
                // Keep the native recorder attached during capture and drain before reusing it.
                // If cleanup fails, retire its identity so stale results cannot reach another operation.
                if (slot.recorder.Valid)
                {
                    slot.recorder.CopyTo(slot.samples, true);
                    if (retire) slot.recorder.Stop();
                }
            }
            catch
            {
                slot.retired = true;
                try { slot.recorder.Dispose(); } catch { }
                slot.recorder = default;
            }
        }

        static void StopGpuRecorders()
        {
            // Instrumentation failure must not interrupt rendering or retention of the CPU capture.
            try { if (gpuActivation.Valid) gpuActivation.Stop(); } catch { }
            foreach (GpuSlot slot in gpuSlots)
                try { if (slot.recorder.Valid) slot.recorder.Stop(); } catch { }
        }

        static void AbandonGpuCapture()
        {
            // Keep draining orphaned queries before recycling their marker identities.
            foreach (GpuSlot slot in gpuSlots)
                if (slot.owner != null)
                { slot.owner.events[slot.sample].gpuStatus = "interrupted"; slot.owner = null; }
            gpuCapture = null;
        }

        static void ShutdownGpu()
        {
            PollGpu();
            foreach (GpuSlot slot in gpuSlots)
            {
                if (slot.busy) CompleteGpu(slot, "interrupted", retire: true);
                try { slot.recorder.Dispose(); } catch { }
                slot.recorder = default;
            }
            try { gpuActivation.Dispose(); } catch { }
            gpuActivation = default;
            gpuCapture = null; gpuEnabled = gpuReady = false; gpuAvailability = "idle";
            try { gpuCommands?.Release(); } catch { }
            gpuCommands = null;
        }

        internal static int PendingGpu(Capture owner)
        {
            int count = 0;
            foreach (GpuSlot slot in gpuSlots) if (slot.owner == owner && slot.busy) count++;
            return count;
        }
    }
}
