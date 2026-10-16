using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    internal static partial class WhimTexProfiler
    {
        [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
        internal sealed class MemoryTotals
        {
            internal long editorManagedAllocatedBytes, trackedTextureBytes, peakTrackedTextureBytes;
            internal long acquiredBytes, releasedBytes, observedBytes, acquireCount, releaseCount;
            internal long processUsedBytes, processReservedBytes, peakProcessUsedBytes;
            internal long nativeCacheBytes, peakNativeCacheBytes;
            internal bool trackingLimited;
            internal bool editorManagedAllocationAvailable, editorManagedAllocationLimited, allocationCountLimited;
            internal MemoryTotals Copy() => (MemoryTotals)MemberwiseClone();
        }

        struct TrackedTexture { internal long bytes; internal string documentId, layerId, layerName, kind; }
        struct TrackedNativeCache { internal long bytes; internal string documentId, layerId, layerName; }
        static readonly Dictionary<UnityObjectID, TrackedTexture> trackedTextures = new();
        static readonly Dictionary<object, TrackedNativeCache> trackedNativeCaches = new();
        const int AllocationSampleCapacity = 16384, AllocationFrameCapacity = 512;
        static readonly List<ProfilerRecorderSample> allocationFrames = new(AllocationFrameCapacity);
        static ProfilerRecorder allocationCounts, allocationBytes;
        static Capture allocationOwner;
        static long allocationSequence, allocationEpoch, previousFrameBytes;
        static void ResetMemory()
        {
            ShutdownAllocations(); trackedTextures.Clear(); trackedNativeCaches.Clear();
        }

        internal readonly struct InstrumentationSuspension : IDisposable
        {
            internal InstrumentationSuspension(bool suspend) { instrumentationSuspended++; }
            public void Dispose() { instrumentationSuspended--; }
        }
        internal static InstrumentationSuspension SuppressInstrumentation() => new(true);

        static void PrepareAllocations(Capture owner)
        {
            ShutdownAllocations();
            allocationOwner = owner;
            owner.memory ??= new MemoryTotals();
            allocationSequence = allocationEpoch = previousFrameBytes = 0;
            try
            {
                // Each GC.Alloc marker contributes one sample, not bytes. Do not aggregate
                // per frame: nested scopes need an immediate main-thread count.
                allocationCounts = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc",
                    AllocationSampleCapacity, ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            }
            catch (Exception) { allocationCounts.Dispose(); allocationCounts = default; }
            try
            {
                allocationBytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame",
                    AllocationFrameCapacity, ProfilerRecorderOptions.Default);
                owner.memory.editorManagedAllocationAvailable = allocationBytes.Valid;
                if (allocationBytes.Valid) previousFrameBytes = allocationBytes.CurrentValue;
            }
            catch (Exception) { allocationBytes.Dispose(); allocationBytes = default; }
        }

        static void ShutdownAllocations()
        {
            allocationCounts.Dispose(); allocationBytes.Dispose();
            allocationCounts = allocationBytes = default;
            allocationOwner = null; allocationFrames.Clear();
        }

        static bool ReadAllocationCount(Capture owner, out long count, out long epoch)
        {
            count = allocationSequence; epoch = allocationEpoch;
            if (!ReferenceEquals(owner, allocationOwner) || !allocationCounts.Valid) return false;
            int buffered = allocationCounts.Count;
            if (buffered >= AllocationSampleCapacity)
            {
                allocationEpoch++;
                if (owner.open > 0) owner.memory.allocationCountLimited = true;
            }
            allocationSequence += buffered;
            // Reset stops the recorder. No managed work may be inserted before Start.
            allocationCounts.Reset(); allocationCounts.Start();
            count = allocationSequence; epoch = allocationEpoch;
            return true;
        }

        internal static void SampleEditorAllocations(Capture owner)
        {
            if (!ReferenceEquals(owner, allocationOwner) || owner?.recording != true || !allocationBytes.Valid) return;
            var memory = owner.memory;
            if (allocationBytes.Count >= AllocationFrameCapacity || allocationBytes.WrappedAround)
                memory.editorManagedAllocationLimited = true;
            // CopyTo(reset:true) drains completed frames without stopping the counter.
            // Carry the unfinished frame separately; subtract the previous partial frame
            // so status polls and capture start/stop mid-frame cannot double-count it.
            allocationBytes.CopyTo(allocationFrames, true);
            long currentBytes = allocationBytes.CurrentValue;
            long delta = currentBytes - previousFrameBytes;
            for (int i = 0; i < allocationFrames.Count; i++) delta += allocationFrames[i].Value;
            if (delta < 0) memory.editorManagedAllocationLimited = true;
            else memory.editorManagedAllocatedBytes += delta;
            previousFrameBytes = currentBytes;
        }

        static void PollAllocations()
        {
            SampleEditorAllocations(capture);
            if (capture?.recording == true) ReadAllocationCount(capture, out _, out _);
        }

        internal static void SampleProcessMemory(Capture owner)
        {
            if (owner?.memory == null) return;
            var memory = owner.memory;
            memory.processUsedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            memory.processReservedBytes = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            memory.peakProcessUsedBytes = Math.Max(memory.peakProcessUsedBytes, memory.processUsedBytes);
        }

        internal static long TexturePayloadBytes(Texture texture)
        {
            if (texture == null) return 0;
            long bytes = 0;
            int w = texture.width, h = texture.height;
            int levels = texture is Texture2D t ? t.mipmapCount : texture is RenderTexture rt && rt.useMipMap ? rt.mipmapCount : 1;
            for (int i = 0; i < levels; i++)
            {
                if (texture.graphicsFormat != GraphicsFormat.None)
                    bytes += GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, w >> i), Math.Max(1, h >> i), texture.graphicsFormat);
            }
            if (texture is RenderTexture target)
            {
                bytes *= Math.Max(1, target.volumeDepth);
                int samples = Math.Max(1, target.antiAliasing);
                bytes *= samples > 1 && !target.bindTextureMS ? samples + 1 : samples;
                if (target.depthStencilFormat != GraphicsFormat.None)
                    bytes += (long)GraphicsFormatUtility.ComputeMipmapSize(w, h, target.depthStencilFormat) * Math.Max(1, target.volumeDepth) * samples;
            }
            return bytes;
        }

        internal static void TrackTexture(Texture texture, string kind, bool existing = false)
        {
            if (!Recording || suppressed != 0 || texture == null) return;
            capture.memory ??= new MemoryTotals();
            var parent = current < 0 ? default : capture.events[current];
            if (capture.documentId != null && capture.documentId != parent.documentId) return;
            var key = UnityObjectID.FromObject(texture);
            if (trackedTextures.ContainsKey(key)) return;
            if (trackedTextures.Count >= 8192) { capture.memory.trackingLimited = true; return; }
            long bytes = TexturePayloadBytes(texture);
            trackedTextures.Add(key, new TrackedTexture { bytes = bytes, documentId = parent.documentId,
                layerId = parent.layerId, layerName = parent.layerName, kind = kind });
            var m = capture.memory;
            m.trackedTextureBytes += bytes;
            m.peakTrackedTextureBytes = Math.Max(m.peakTrackedTextureBytes, m.trackedTextureBytes);
            if (existing) m.observedBytes += bytes;
            else { m.acquiredBytes += bytes; m.acquireCount++; }
            MemoryEvent(existing ? "resource.observe" : "resource.acquire", key.ToString(), kind, bytes, parent.documentId, parent.layerId, parent.layerName);
        }

        internal static void ReleaseTexture(Texture texture)
        {
            if (!Recording || texture == null || !trackedTextures.Remove(UnityObjectID.FromObject(texture), out var tracked)) return;
            var m = capture.memory;
            m.trackedTextureBytes -= tracked.bytes; m.releasedBytes += tracked.bytes; m.releaseCount++;
            MemoryEvent("resource.release", UnityObjectID.FromObject(texture).ToString(), tracked.kind, tracked.bytes,
                tracked.documentId, tracked.layerId, tracked.layerName);
        }

        internal static void NativeCacheBytes(object owner, long bytes)
        {
            if (!Recording || owner == null) return;
            capture.memory ??= new MemoryTotals();
            var parent = current < 0 ? default : capture.events[current];
            bool known = trackedNativeCaches.TryGetValue(owner, out var previous);
            // Releases may occur outside the originating document's current render scope.
            if (bytes != 0 && (suppressed != 0 || capture.documentId != null && capture.documentId != parent.documentId)) return;
            bytes = Math.Max(0, bytes);
            if (bytes == previous.bytes) return;
            if (bytes == 0) trackedNativeCaches.Remove(owner);
            else if (known || trackedNativeCaches.Count < 8192) trackedNativeCaches[owner] = new TrackedNativeCache {
                bytes = bytes, documentId = parent.documentId, layerId = parent.layerId, layerName = parent.layerName };
            else { capture.memory.trackingLimited = true; return; }
            capture.memory.nativeCacheBytes += bytes - previous.bytes;
            capture.memory.peakNativeCacheBytes = Math.Max(capture.memory.peakNativeCacheBytes, capture.memory.nativeCacheBytes);
            MemoryEvent("memory.native_cache", null, "retained payload", bytes - previous.bytes,
                known ? previous.documentId : parent.documentId, known ? previous.layerId : parent.layerId,
                known ? previous.layerName : parent.layerName);
        }

        static void MemoryEvent(string phase, string key, string kind, long bytes, string doc, string layer, string name)
        {
            int slot = Reserve(); if (slot < 0) return;
            var sample = CreateSample(slot, phase, "resource", kind, doc, layer, layerName: name);
            // Ownership can outlive its originating scope. In particular a null owner
            // must not inherit the unrelated layer/FX executing when the resource is released.
            sample.documentId = doc; sample.layerId = layer; sample.layerName = name;
            sample.fxIndex = -1; sample.fxName = null;
            if (doc == null)
            {
                sample.changeId = sample.changeEventId = 0;
                sample.changeReason = sample.changeOrigin = sample.changedLayerId = sample.changedLayerName = null;
            }
            sample.resourceKey = key; sample.value = bytes; sample.completed = true; sample.end = sample.start;
            capture.events[slot] = sample;
        }
    }

    internal static class ProfiledRenderTexture
    {
        internal static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format,
            RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default)
        {
            var result = RenderTexture.GetTemporary(width, height, depthBuffer, format, readWrite);
            WhimTexProfiler.TrackTexture(result, "temporary RT checkout");
            return result;
        }
        internal static RenderTexture GetTemporary(RenderTextureDescriptor descriptor)
        {
            var result = RenderTexture.GetTemporary(descriptor);
            WhimTexProfiler.TrackTexture(result, "temporary RT checkout");
            return result;
        }
        internal static void ReleaseTemporary(RenderTexture texture)
        {
            WhimTexProfiler.ReleaseTexture(texture);
            RenderTexture.ReleaseTemporary(texture);
        }
    }
}
