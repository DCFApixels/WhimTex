using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private VisibleBoundsCache visibleBounds;
        internal int LastVisibleBoundsScannedTiles => visibleBounds?.ScannedTiles ?? 0;

        // Raw source pixels before FX, channel remapping and canvas clipping.
        // False means that the caller should retry. An empty drawing returns true and a zero rect.
        internal bool TryGetVisiblePixelBounds(out RectInt bounds)
        {
            bounds = default;
            EnsureDeferredTexture();
            if (HasBufferedStroke) return false;
            if (pixels == null) return true;
            visibleBounds ??= new VisibleBoundsCache();
            try { return visibleBounds.TryGet(this, out bounds); }
            catch (TiledPaint.TiledPaintResourceLostException)
            {
                visibleBounds = null; // Rendering restores the source before the next query.
                return true;
            }
        }

        [BurstCompile]
        private sealed class VisibleBoundsCache
        {
            const int Size = PaintTileSet.Size;
            struct Tile
            {
                public Vector2Int key;
                public Texture source;
                public RectInt region;
                public object version;
                public ulong generation;
                public RectInt bounds;
            }

            Dictionary<Vector2Int, Tile> previous = new(), current = new();
            readonly List<Tile> cpu = new(), gpu = new();
            Texture2D original;
            uint upload;
            ulong revision, denseGeneration;
            int tileCount, cursor, cpuScanned;
            bool initialized, failed, denseBlocksKnown;
            RectInt aggregate;
            BoundsBatch batch;
            internal int ScannedTiles => cpuScanned + gpu.Count;
            internal void Changed() => initialized = false;
            internal void DenseChanged(RectInt[] blocks)
            {
                foreach (var block in blocks) DenseChanged(block);
            }
            internal void DenseChanged(RectInt block)
            {
                for (int y = block.yMin / Size; y <= (block.yMax - 1) / Size; y++)
                for (int x = block.xMin / Size; x <= (block.xMax - 1) / Size; x++)
                    current.Remove(new Vector2Int(x, y));
                initialized = false;
                denseBlocksKnown = true;
            }

            internal bool TryGet(DrawingLayerBehaviour drawing, out RectInt result)
            {
                result = default;
                bool changed = !initialized || original != drawing.pixels || revision != drawing.paintSurfaceRevision ||
                    upload != drawing.pixels.updateCount || tileCount != drawing.TiledPixels.Count;
                if (batch != null)
                {
                    if (!batch.Poll()) return false;
                    if (!changed && batch.results != null)
                        for (int i = 0; i < gpu.Count; i++) Accept(gpu[i], batch.results[i]);
                    failed = batch.results == null;
                    batch = null;
                }
                if (changed)
                {
                    if ((drawing.tiledSurface == null || !SystemInfo.supportsComputeShaders) &&
                        PaintTileTransfer.HasPendingFor(drawing.pixels)) return false;
                    Prepare(drawing);
                    if (batch != null) return false;
                }
                if (failed) return true; // Use the ordinary frame when the graphics device cannot supply exact bounds.
                if (cursor < cpu.Count && !ScanPendingTiles()) return false;
                result = aggregate;
                return true;
            }

            bool ScanPendingTiles()
            {
                long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500;
                Texture2D source = null;
                NativeArray<byte> data = default;
                int width = 0, height = 0, stride = 0;
                do
                {
                    // Opposite ends of the dense tile grid usually establish the outer edges first.
                    int index = cursor / 2;
                    if ((cursor++ & 1) != 0) index = cpu.Count - 1 - index;
                    var tile = cpu[index];
                    var origin = tile.key * Size;
                    // No tile bounds are recorded for this case. If a later erase shrinks the
                    // known aggregate, a formerly interior tile can contribute its actual bounds.
                    if (origin.x >= aggregate.xMin && origin.x + tile.region.width <= aggregate.xMax &&
                        origin.y >= aggregate.yMin && origin.y + tile.region.height <= aggregate.yMax) continue;
                    if (!ReferenceEquals(source, tile.source))
                    {
                        source = (Texture2D)tile.source;
                        width = source.width;
                        height = source.height;
                        var format = source.format;
                        stride = format == TextureFormat.RGBA32 ? 4 : format == TextureFormat.RGBAHalf ? 8 : 16;
                        data = source.GetRawTextureData<byte>();
                    }
                    if (tile.region.xMax > width || tile.region.yMax > height ||
                        (long)width * height * stride > data.Length)
                    {
                        failed = true;
                        return false;
                    }
                    Scan(ref data, width, stride, tile.region, out var edges);
                    cpuScanned++;
                    Accept(tile, edges);
                } while (cursor < cpu.Count && Stopwatch.GetTimestamp() < deadline);
                return cursor == cpu.Count;
            }

            void Prepare(DrawingLayerBehaviour d)
            {
                (previous, current) = (current, previous);
                current.Clear(); cpu.Clear(); gpu.Clear(); aggregate = default; cursor = cpuScanned = 0; failed = false;
                if (!denseBlocksKnown) denseGeneration++;
                denseBlocksKnown = false;
                original = d.pixels; revision = d.paintSurfaceRevision; upload = original.updateCount;
                tileCount = d.TiledPixels.Count; initialized = true;
                bool tiled = d.tiledSurface != null;
                if (tiled)
                {
                    // Published versions are immutable. Submit reads before releasing the snapshot so
                    // later copy-on-write transfers may reuse the pages in GPU command order.
                    using var snapshot = d.tiledSurface.AcquireSnapshot();
                    bool pending = PaintTileTransfer.HasPendingFor(original) || d.ExactPixelTransferLost;
                    d.EnsureTiledPixelMap();
                    foreach (var pair in snapshot.State.Tiles)
                    {
                        var v = pair.Value;
                        Texture source = v.Page.Texture;
                        Vector2Int offset = v.Origin;
                        if (!pending && d.tiledPixelMap.TryGetValue(pair.Key, out var saved))
                        {
                            source = saved.texture;
                            offset = Vector2Int.zero;
                        }
                        Add(pair.Key, source, new RectInt(offset.x, offset.y, Size, Size), v, 0);
                    }
                    Submit();
                    return;
                }
                if (d.HasTiledSource) d.EnsureTiledPixelMap();
                if (!d.tiledBaseEmpty)
                {
                    if (!d.HasTiledSource) ValidatePaintTexture(d.paintSurface);
                    bool cpuHasWorkingPrecision = !SystemInfo.supportsComputeShaders && original.isReadable &&
                        original.format == TextureFormat.RGBAHalf && !d.paintSurfaceDirty && !d.ExactPixelTransferLost;
                    Texture source = d.paintSurface != null && !d.HasTiledSource && !cpuHasWorkingPrecision
                        ? d.paintSurface : original;
                    ulong version = source == original ? upload : denseGeneration;
                    for (int y = 0; y < original.height; y += Size)
                    for (int x = 0; x < original.width; x += Size)
                    {
                        var key = new Vector2Int(x / Size, y / Size);
                        if (d.HasTiledSource && d.tiledPixelMap.ContainsKey(key)) continue;
                        Add(key, source, new RectInt(x, y, Math.Min(Size, original.width-x), Math.Min(Size, original.height-y)), source, version);
                    }
                }
                foreach (var tile in d.TiledPixels)
                    if (tile?.texture != null)
                        Add(tile.key, tile.texture, new RectInt(0, 0, Size, Size), tile.texture, tile.texture.updateCount);
                Submit();
            }

            void Add(Vector2Int key, Texture source, RectInt region, object version, ulong generation)
            {
                if (previous.TryGetValue(key, out var entry) && ReferenceEquals(entry.version, version) && entry.generation == generation)
                {
                    Include(entry.bounds);
                    current.Add(key, entry);
                    return;
                }
                var tile = new Tile { key = key, source = source, region = region, version = version, generation = generation };
                if (source is Texture2D tex && tex.isReadable &&
                    (tex.format == TextureFormat.RGBAHalf || tex.format == TextureFormat.RGBA32 || tex.format == TextureFormat.RGBAFloat))
                    cpu.Add(tile);
                else gpu.Add(tile);
            }

            void Submit()
            {
                previous.Clear();
                if (gpu.Count == 0) return;
                batch = BoundsBatch.Begin(gpu);
                failed = batch == null;
            }

            void Accept(Tile tile, int4 edges)
            {
                if (edges.z > edges.x && edges.w > edges.y)
                {
                    Vector2Int origin = tile.key * Size;
                    tile.bounds = new RectInt(origin.x + edges.x, origin.y + edges.y, edges.z - edges.x, edges.w - edges.y);
                    Include(tile.bounds);
                }
                tile.source = null;
                current.Add(tile.key, tile);
            }

            void Include(RectInt r)
            {
                if (r.width <= 0) return;
                if (aggregate.width == 0) { aggregate = r; return; }
                var min = Vector2Int.Min(aggregate.min, r.min);
                aggregate.SetMinMax(min, Vector2Int.Max(aggregate.max, r.max));
            }

            // Half/float positive bit ranges include infinity and exclude negative, NaN and signed zero.
            [BurstCompile]
            static unsafe void Scan(ref NativeArray<byte> data, int width, int stride, in RectInt region, out int4 edges)
            {
                // Two opposite occupied corners prove all four bounds, even if the interior is transparent.
                byte* bottomLeft = (byte*)data.GetUnsafeReadOnlyPtr() + (region.y * width + region.x) * stride;
                int rightOffset = (region.width - 1) * stride;
                byte* topLeft = bottomLeft + (region.height - 1) * width * stride;
                if ((Positive(bottomLeft, stride) && Positive(topLeft + rightOffset, stride)) ||
                    (Positive(bottomLeft + rightOffset, stride) && Positive(topLeft, stride)))
                {
                    edges = new int4(0, 0, region.width, region.height);
                    return;
                }
                int left = region.width, bottom = region.height, right = 0, top = 0;
                for (int y = 0; y < region.height; y++)
                {
                    byte* row = bottomLeft + y * width * stride;
                    // Branch-free row reduction lets Burst skip a long transparent margin in bulk.
                    if (stride == 4 && row[3] == 0 && row[rightOffset + 3] == 0)
                    {
                        int alpha = 0;
                        for (int x = 0; x < region.width; x++) alpha |= row[x * 4 + 3];
                        if (alpha == 0) continue;
                    }
                    int first = 0;
                    while (first < region.width && !Positive(row + first * stride, stride)) first++;
                    if (first == region.width) continue;
                    int last = region.width - 1;
                    while (last > first && !Positive(row + last * stride, stride)) last--;
                    left = Math.Min(left, first);
                    right = Math.Max(right, last + 1);
                    bottom = Math.Min(bottom, y);
                    top = y + 1;
                }
                edges = new int4(left, bottom, right, top);
            }

            static unsafe bool Positive(byte* pixel, int stride)
            {
                if (stride == 4) return pixel[3] != 0;
                if (stride == 8)
                {
                    int bits = ((ushort*)pixel)[3];
                    return bits > 0 && bits <= 0x7c00;
                }
                return ((float*)pixel)[3] > 0f;
            }

            // One 16-byte result per tile, one readback for all atlas pages. Ordinary
            // synchronized sparse overrides are already available in CPU memory.
            sealed class BoundsBatch
            {
                static ComputeShader shader;
                ComputeBuffer regions, output;
                AsyncGPUReadbackRequest request;
                public int4[] results;
                bool finished;

                internal static BoundsBatch Begin(List<Tile> tiles)
                {
                    if (!SystemInfo.supportsComputeShaders) return null;
                    if (shader == null)
                        shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/DrawingTileBounds.compute");
                    if (shader == null || !shader.IsSupported(0)) return null;
                    var batch = new BoundsBatch();
                    try
                    {
                        var descriptors = new int4[tiles.Count];
                        for (int i = 0; i < tiles.Count; i++)
                        {
                            var r = tiles[i].region;
                            descriptors[i] = new int4(r.x, r.y, r.width, r.height);
                        }
                        batch.regions = new ComputeBuffer(tiles.Count, 16);
                        batch.output = new ComputeBuffer(tiles.Count, 16);
                        batch.regions.SetData(descriptors);
                        shader.SetBuffer(0, "_Regions", batch.regions);
                        shader.SetBuffer(0, "_Bounds", batch.output);
                        for (int first = 0; first < tiles.Count;)
                        {
                            int end = first + 1;
                            while (end < tiles.Count && tiles[end].source == tiles[first].source && end - first < 65535) end++;
                            shader.SetTexture(0, "_Source", tiles[first].source);
                            shader.SetInt("_First", first);
                            shader.Dispatch(0, end - first, 1, 1);
                            first = end;
                        }
                        if (SystemInfo.supportsAsyncGPUReadback)
                        {
                            AssemblyReloadEvents.beforeAssemblyReload += batch.Wait;
                            batch.request = AsyncGPUReadback.Request(batch.output, batch.Finish);
                            if (!batch.finished) batch.request.forcePlayerLoopUpdate = true;
                            GL.Flush();
                        }
                        else
                        {
                            batch.results = new int4[tiles.Count];
                            batch.output.GetData(batch.results);
                            batch.Release();
                        }
                        return batch;
                    }
                    catch (Exception e) when (e is OutOfMemoryException || e is UnityException)
                    {
                        batch.Release();
                        return null;
                    }
                    catch { batch.Release(); throw; }
                }
                internal bool Poll()
                {
                    if (!finished)
                    {
                        request.Update();
                        if (request.done) Finish(request);
                    }
                    return finished;
                }
                void Wait() { if (!finished) { request.WaitForCompletion(); Finish(request); } }
                void Finish(AsyncGPUReadbackRequest done)
                {
                    if (finished) return;
                    try { if (!done.hasError) results = done.GetData<int4>().ToArray(); }
                    finally { Release(); }
                }
                void Release()
                {
                    finished = true;
                    AssemblyReloadEvents.beforeAssemblyReload -= Wait;
                    regions?.Release(); output?.Release();
                }
            }
        }
    }
}
