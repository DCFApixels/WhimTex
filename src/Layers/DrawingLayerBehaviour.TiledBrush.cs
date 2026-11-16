using System;
using System.Collections.Generic;
using DCFApixels.WhimTex.TiledPaint;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        private static partial class PaintBrushRenderer
        {
            private static TiledPaintSurface rasterTarget;
            private static bool rasterDrawing => rasterTarget != null;
            private static readonly NativeRasterBatch nativeRasterBatch = new NativeRasterBatch();
            private static RenderTexture pressureBase;

            // The stamp builder and material remain the same for all brush variants.
            // This adapter only directs their native geometry to sparse GPU slots.
            private sealed class NativeRasterBatch : ITiledPaintBackdropBatch
            {
                private readonly List<Rect> bounds = new List<Rect>();
                private readonly List<int> alternate = new List<int>();
                private readonly List<int> indices = new List<int>();
                private readonly List<Range> ranges = new List<Range>();
                private readonly List<SubMeshDescriptor> subMeshes = new List<SubMeshDescriptor>();
                private Material material;
                private bool sequential, stampBlend, pressure;
                internal TiledPaintSnapshot Source;
                internal bool NeedsSource => stampBlend;
                public bool SupportsPressureOpacity => true;
                public int Count => bounds.Count;
                public Rect Bounds(int stamp) => bounds[stamp];

                internal void Prepare(Material value, bool readsBackdrop)
                {
                    if (readsBackdrop && (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                        throw new NotSupportedException("This device cannot copy the brush backdrop without conversion.");
                    material = value; sequential = readsBackdrop;
                    stampBlend = value.GetFloat("_StampBlendEnabled") > .5f;
                    pressure = value.GetFloat("_PressureOpacity") > .5f;
                    bounds.Clear(); alternate.Clear(); ranges.Clear(); indices.Clear(); subMeshes.Clear();
                    int quads = meshPositions.Count / 4;
                    for (int i = 0; i < quads; i++)
                    {
                        int first = i * 4;
                        var a = meshPositions[first]; var b = meshPositions[first + 2];
                        var r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                        bounds.Add(r); alternate.Add(0);
                        if (a.x >= 0 && a.y >= 0 && b.x <= 1 && b.y <= 1) continue;
                        // Rect stores size rather than max: reconstructing its far
                        // corner can round a projective quad by one float ULP.
                        float x = Mathf.Max(0, a.x), y = Mathf.Max(0, a.y);
                        float right = Mathf.Min(1, b.x), top = Mathf.Min(1, b.y);
                        if (right <= x || top <= y) { alternate[i] = -1; continue; }
                        // Native clipped quads keep exactly the former interpolation
                        // inside the original frame, independent of outside storage.
                        alternate[i] = meshPositions.Count + 1;
                        CopyVertex(first, new Vector3(x, y, 0));
                        CopyVertex(first + 1, new Vector3(x, top, 0));
                        CopyVertex(first + 2, new Vector3(right, top, 0));
                        CopyVertex(first + 3, new Vector3(right, y, 0));
                    }
                    if (sequential)
                    {
                        EnsureScratch(ref tileBackdrop, "WhimTex stroke tile backdrop");
                        if (stampBlend && pressure) EnsureScratch(ref pressureBase, "WhimTex pressure tile base");
                    }
                }

                private static void CopyVertex(int index, Vector3 position)
                {
                    meshPositions.Add(position); meshUv.Add(meshUv[index]);
                    meshClipMin.Add(meshClipMin[index]); meshClipMax.Add(meshClipMax[index]);
                    meshClips.Add(meshClips[index]); meshTiles.Add(meshTiles[index]);
                    meshColors.Add(meshColors[index]); meshStamps.Add(meshStamps[index]);
                    if (meshSequence) meshContexts.Add(meshContexts[index]);
                }

                public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
                {
                    foreach (var page in pages) foreach (var tile in page.Tiles)
                    {
                        if (tile.Indices.Count == 0) continue;
                        long x = (long)tile.Key.x * 256, y = (long)tile.Key.y * 256;
                        int width = (int)Math.Max(0L, Math.Min(256, frame.SourceWidth - x));
                        int height = (int)Math.Max(0L, Math.Min(256, frame.SourceHeight - y));
                        bool inside = x >= 0 && y >= 0 && width > 0 && height > 0;
                        var origin = tile.Origin;
                        if (inside)
                        {
                            int first = indices.Count;
                            AddIndices(tile, true);
                            if (indices.Count > first) ranges.Add(new Range(tile, first, indices.Count - first,
                                new Rect(origin.x, origin.y, width, height), width != 256 || height != 256));
                            if (width == 256 && height == 256) continue;
                        }
                        int rawFirst = indices.Count;
                        AddIndices(tile, false);
                        if (!inside) ranges.Add(new Range(tile, rawFirst, indices.Count - rawFirst, default, false));
                        else
                        {
                            if (width < 256) ranges.Add(new Range(tile, rawFirst, indices.Count - rawFirst,
                                new Rect(origin.x + width, origin.y, 256 - width, 256), true));
                            if (height < 256)
                            {
                                // Mesh subset index spans must not overlap. The two
                                // physical edge strips can share vertices, not indices.
                                if (width < 256) { rawFirst = indices.Count; AddIndices(tile, false); }
                                ranges.Add(new Range(tile, rawFirst, indices.Count - rawFirst,
                                    new Rect(origin.x, origin.y + height, width, 256 - height), true));
                            }
                        }
                    }
                    // Upload once: SetIndices per tile repeatedly rebuilds the shared
                    // index buffer. Keep native quads and their interpolation order.
                    const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
                    brushMesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
                    brushMesh.SetIndexBufferData(indices, 0, 0, indices.Count, flags);
                    foreach (var range in ranges)
                        subMeshes.Add(new SubMeshDescriptor(range.First, range.Count, MeshTopology.Quads)
                            { firstVertex = 0, vertexCount = meshPositions.Count, bounds = brushMesh.bounds });
                    brushMesh.SetSubMeshes(subMeshes, flags);
                    tileProperties ??= new MaterialPropertyBlock();
                    TiledPaintTile previousTile = null;
                    TiledPaintPage previousPage = null;
                    bool scissor = false;
                    commands.DisableScissorRect();
                    for (int i = 0; i < ranges.Count; i++)
                    {
                        var range = ranges[i]; var tile = range.Tile; var origin = tile.Origin;
                        if (tile != previousTile)
                        {
                            if (scissor) { commands.DisableScissorRect(); scissor = false; }
                            tileProperties.Clear();
                            if (sequential) ConfigureBackdrop(commands, tile, frame);
                            previousTile = tile;
                            // A pressure-base blit changes the active render target.
                            if (sequential || previousPage != tile.Page) commands.SetRenderTarget(tile.Page.Texture);
                            previousPage = tile.Page;
                            commands.SetViewport(new Rect(origin.x, origin.y, 256, 256));
                            float x = tile.Key.x * 256 / (float)frame.SourceWidth, y = tile.Key.y * 256 / (float)frame.SourceHeight;
                            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(x,
                                x + 256f / frame.SourceWidth, y, y + 256f / frame.SourceHeight, -1, 1));
                        }
                        if (range.Clipped) commands.EnableScissorRect(range.Scissor);
                        else if (scissor) commands.DisableScissorRect();
                        scissor = range.Clipped;
                        commands.DrawMesh(brushMesh, Matrix4x4.identity, material, i, 0, tileProperties);
                    }
                    if (scissor) commands.DisableScissorRect();
                }

                private void AddIndices(TiledPaintTile tile, bool native)
                {
                    foreach (int stamp in tile.Indices)
                    {
                        int first = stamp * 4;
                        if (native && alternate[stamp] < 0) continue;
                        if (native && alternate[stamp] > 0) first = alternate[stamp] - 1;
                        indices.Add(first); indices.Add(first + 1); indices.Add(first + 2); indices.Add(first + 3);
                    }
                }

                private void ConfigureBackdrop(CommandBuffer commands, TiledPaintTile tile, TiledPaintFrame frame)
                {
                    TiledPaintTileView original = default;
                    bool hasSource = Source != null && Source.TryGetTile(tile.Key, out original);
                    var origin = tile.Origin;
                    if (!tile.BackdropInitialized)
                    {
                        if (stampBlend && hasSource)
                        {
                            var rect = original.PhysicalRect;
                            if (pressure)
                            {
                                commands.CopyTexture(original.Texture, 0, 0, rect.x, rect.y, 256, 256, tileBackdrop, 0, 0, 0, 0);
                                commands.Blit(tileBackdrop, pressureBase, WhimTexMaterials.Blend, 2);
                                commands.CopyTexture(pressureBase, 0, 0, 0, 0, 256, 256, tile.Page.Texture, 0, 0, origin.x, origin.y);
                            }
                            else commands.CopyTexture(original.Texture, 0, 0, rect.x, rect.y, 256, 256,
                                tile.Page.Texture, 0, 0, origin.x, origin.y);
                        }
                        tile.BackdropInitialized = true;
                    }
                    commands.CopyTexture(tile.Page.Texture, 0, 0, origin.x, origin.y, 256, 256, tileBackdrop, 0, 0, 0, 0);
                    var logical = tile.Key * 256;
                    tileProperties.SetTexture("_Backdrop", tileBackdrop);
                    tileProperties.SetVector("_BackdropTransform", new Vector4(frame.SourceWidth / 256f,
                        frame.SourceHeight / 256f, -logical.x / 256f, -logical.y / 256f));
                    tileProperties.SetTexture("_StrokeBase", hasSource ? (Texture)original.Texture : Texture2D.blackTexture);
                    tileProperties.SetFloat("_StrokeBasePresent", hasSource ? 1f : 0f);
                    tileProperties.SetVector("_StrokeBaseTransform", hasSource
                        ? new Vector4(frame.SourceWidth / (float)original.Texture.width, frame.SourceHeight / (float)original.Texture.height,
                            (original.PhysicalRect.x - logical.x) / (float)original.Texture.width,
                            (original.PhysicalRect.y - logical.y) / (float)original.Texture.height)
                        : new Vector4(1, 1, 0, 0));
                }

                internal void Clear()
                {
                    Source = null; material = null;
                    bounds.Clear(); alternate.Clear(); indices.Clear(); ranges.Clear(); subMeshes.Clear();
                }

                private readonly struct Range
                {
                    internal readonly TiledPaintTile Tile;
                    internal readonly int First, Count;
                    internal readonly Rect Scissor;
                    internal readonly bool Clipped;
                    internal Range(TiledPaintTile tile, int first, int count, Rect scissor, bool clipped)
                    { Tile = tile; First = first; Count = count; Scissor = scissor; Clipped = clipped; }
                }
            }

            private static void EnsureScratch(ref RenderTexture texture, string name)
            {
                if (texture != null && texture.IsCreated()) return;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                texture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                if (!texture.Create())
                {
                    UnityEngine.Object.DestroyImmediate(texture); texture = null;
                    throw new InvalidOperationException("Cannot allocate the brush tile backdrop.");
                }
            }

            private static void ReleaseNativeRasterBatch()
            {
                nativeRasterBatch.Clear(); rasterTarget = null;
                if (pressureBase != null) UnityEngine.Object.DestroyImmediate(pressureBase);
                pressureBase = null;
            }
        }
    }
}
