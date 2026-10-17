using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private PaintAlphaCoverage alphaCoverage;
        internal bool HasPaintSurface => paintSurface != null;

        internal bool CanUseAlphaCoverage
        {
            get
            {
                if (hasBakedPixelFrame || channelMapping[3] != ChannelMappingSource.A ||
                    paintSurface != null && !paintSurface.enableRandomWrite || !PaintAlphaCoverage.Supported) return false;
                if (fx != null) foreach (var effect in fx)
                    if (effect != null && (effect is not ShaderFX shaderFX || shaderFX.Active)) return false;
                return true;
            }
        }

        internal bool TryGetAlphaContentState(int width, int height, out LayerContentState state)
        {
            state = LayerContentState.Unknown;
            if (!CanUseAlphaCoverage) return false;
            // A pending stroke has not been assembled into the base yet.
            if (advancedStroke != null) return true;
            var surface = EnsurePaintSurface(width, height);
            if (!surface.enableRandomWrite) return false;
            alphaCoverage ??= new PaintAlphaCoverage(surface.width, surface.height);
            if (alphaCoverage.Failed) return false;
            state = alphaCoverage.Get(surface);
            return !alphaCoverage.Failed;
        }

        private void InvalidateAlphaCoverage() => alphaCoverage?.Invalidate();
    }

    // GPU metadata belongs to the paint surface, not its asynchronously saved CPU copy.
    internal sealed class PaintAlphaCoverage : IDisposable
    {
        internal static bool Supported => SystemInfo.supportsAsyncGPUReadback && BrushStrokeCompute.CoverageSupported;
        internal readonly ComputeBuffer buffer;
        internal readonly int width, height, columns;
        internal bool Valid { get; private set; }
        internal bool Failed { get; private set; }
        private ulong revision;
        private LayerContentState state;
        private bool pending, disposed;
        private AsyncGPUReadbackRequest request;

        internal PaintAlphaCoverage(int width, int height)
        {
            this.width = width; this.height = height; columns = (width + 255) / 256;
            buffer = new ComputeBuffer(columns * ((height + 255) / 256), sizeof(uint));
        }

        internal void Invalidate() { Valid = false; Changed(); }
        internal void Changed() { unchecked { revision++; } state = LayerContentState.Unknown; }

        internal LayerContentState Get(RenderTexture surface)
        {
            if (disposed || Failed) return LayerContentState.Unknown;
            if (!Valid)
            {
                using var profile = WhimTexProfiler.Measure("layer.content_coverage.initialize");
                using (profile.Gpu()) BrushStrokeCompute.ScanCoverage(surface, this, null);
                Valid = true;
            }
            if (state != LayerContentState.Unknown || pending) return state;
            ulong expected = revision;
            pending = true;
            try
            {
                request = AsyncGPUReadback.Request(buffer, readback =>
                {
                    pending = false;
                    if (disposed || expected != revision) return;
                    if (readback.hasError) { Failed = true; return; }
                    var flags = readback.GetData<uint>();
                    bool present = false;
                    for (int i = 0; i < flags.Length; i++) present |= flags[i] != 0;
                    state = present ? LayerContentState.Present : LayerContentState.Empty;
                });
                request.forcePlayerLoopUpdate = true;
                GL.Flush();
            }
            catch { pending = false; Failed = true; }
            return state;
        }

        internal void RestoreTile(RenderTexture surface, RectInt tile)
        {
            if (!Valid || Failed) return;
            BrushStrokeCompute.ScanCoverage(surface, this, new[] { tile });
            Changed();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Only teardown waits; ordinary polling, painting and Undo never wait for metadata.
            if (pending) request.WaitForCompletion();
            buffer.Release();
        }
    }
}
