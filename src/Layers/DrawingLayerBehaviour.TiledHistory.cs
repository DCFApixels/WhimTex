using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex.TiledPaint;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class TiledDrawingTile
    {
        public Vector2Int key;
        public Texture2D texture;
        [NonSerialized] internal PaintHistoryStore.TileVersion historyVersion;
    }

    public sealed partial class DrawingLayerBehaviour
    {
        // Persist only modified source tiles; the original texture remains the base
        // and retains straight RGB that cannot be represented by premultiplication.
        [SerializeField] private List<TiledDrawingTile> tiledPixels;
        [NonSerialized] private Dictionary<Vector2Int, TiledDrawingTile> tiledPixelMap;
        internal IReadOnlyList<TiledDrawingTile> TiledPixels => tiledPixels ?? (IReadOnlyList<TiledDrawingTile>)Array.Empty<TiledDrawingTile>();
        internal bool HasTiledPixels => tiledPixels != null && tiledPixels.Count != 0;

        internal void EnsureTiledStorage(int width, int height)
        {
            if (pixels == null) throw new InvalidOperationException("Initialize the Drawing source before tiled painting.");
            if (pixels.width != width || pixels.height != height) throw new ArgumentException("The original Drawing frame must remain unchanged.");
            DrawingPixelHistory.Find(pixels, true).BindDocument(Owner);
            EnsureTiledPixelMap();
        }

        private void EnsureTiledPixelMap()
        {
            if (tiledPixelMap != null) return;
            tiledPixels ??= new List<TiledDrawingTile>();
            var map = new Dictionary<Vector2Int, TiledDrawingTile>(tiledPixels.Count);
            for (int i = tiledPixels.Count - 1; i >= 0; i--)
            {
                var tile = tiledPixels[i];
                // Undo may deserialize a reference to an override removed by an
                // earlier Undo. Its bytes are reconstructed from the journal.
                if (tile == null || tile.texture == null) { tiledPixels.RemoveAt(i); continue; }
                if (!map.TryAdd(tile.key, tile))
                    throw new InvalidOperationException("Invalid sparse Drawing storage.");
            }
            tiledPixelMap = map;
        }

        private TiledDrawingTile GetOrCreateTiledPixel(Vector2Int key)
        {
            EnsureTiledPixelMap();
            if (tiledPixelMap.TryGetValue(key, out var tile)) return tile;
            // Every GPU texel is copied before publication and every CPU byte is
            // supplied by readback before persistence: avoid a redundant clear/upload.
            tile = new TiledDrawingTile { key = key, texture = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true, true)
            {
                name = GetTextureName() + " Tile " + key.x + "," + key.y,
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            } };
            try { tiledPixelMap.Add(key, tile); tiledPixels.Add(tile); return tile; }
            catch { tiledPixelMap.Remove(key); UnityEngine.Object.DestroyImmediate(tile.texture); throw; }
        }

        // Consumes the delta. The source has already published its exact GPU
        // revision; failures restore Before before releasing either snapshot.
        internal void CommitTiledHistory(TiledPaintDelta delta, string label = null)
        {
            if (delta == null) throw new ArgumentNullException(nameof(delta));
            PaintHistoryStore.Step step = null;
            List<TiledDrawingTile> created = null;
            var previous = RenderTexture.active;
            try
            {
                if (delta.ChangedTiles.Count == 0) return;
                created = new List<TiledDrawingTile>(delta.ChangedTiles.Count);
                EnsureTiledStorage(TiledSourceWidth, TiledSourceHeight);
                var blocks = new RectInt[delta.ChangedTiles.Count];
                var textures = new Texture2D[blocks.Length];
                var beforePresent = new bool[blocks.Length];
                var beforeVersions = new PaintHistoryStore.TileVersion[blocks.Length];
                var history = DrawingPixelHistory.Find(pixels, true);
                for (int i = 0; i < blocks.Length; i++) blocks[i] = new RectInt(delta.ChangedTiles[i] * 256, new Vector2Int(256, 256));
                using (WhimTexProfiler.Measure("brush.history.reserveSparse")) step = history.ReserveSparse(blocks);
                var afterVersions = new PaintHistoryStore.TileVersion[step.pages];
                for (int i = 0; i < afterVersions.Length; i++) afterVersions[i] = new PaintHistoryStore.TileVersion(step, i);
                using (WhimTexProfiler.Measure("brush.history.materializeTiles"))
                for (int i = 0; i < blocks.Length; i++)
                {
                    var key = delta.ChangedTiles[i]; bool existed = tiledPixelMap.ContainsKey(key);
                    beforePresent[i] = existed;
                    var tile = GetOrCreateTiledPixel(key); textures[i] = tile.texture;
                    beforeVersions[i] = tile.historyVersion;
                    if (!existed) created.Add(tile);
                }
                for (int start = 0, pageIndex = 0; start < blocks.Length; start += PaintTileTransfer.TilesPerPage, pageIndex++)
                {
                    int count = Math.Min(PaintTileTransfer.TilesPerPage, blocks.Length - start);
                    var pageBlocks = new RectInt[count]; var pageTextures = new Texture2D[count];
                    var pagePresent = new bool[count]; Array.Copy(beforePresent, start, pagePresent, 0, count);
                    var pageVersions = new PaintHistoryStore.TileVersion[count];
                    Array.Copy(beforeVersions, start, pageVersions, 0, count);
                    for (int i = 0; i < count; i++)
                        if (pagePresent[i] && !PaintHistoryStore.CanReuse(pageVersions[i], history))
                        { pageVersions = null; break; }
                    Array.Copy(blocks, start, pageBlocks, 0, count); Array.Copy(textures, start, pageTextures, 0, count);
                    var page = new PaintTileTransfer.Page(pixels, step, pageIndex, pageBlocks, sparseTextures: pageTextures, sparseBeforePresent: pagePresent, sparseBeforeVersions: pageVersions);
                    try
                    {
                        RenderTexture.active = null;
                        using (WhimTexProfiler.Measure("brush.history.snapshotCopies"))
                        for (int i = 0; i < count; i++)
                        {
                            var key = delta.ChangedTiles[start + i]; var slot = PaintTileTransfer.Slot(i, pageBlocks[i]);
                            if (page.before != null) CopyTiledSnapshot(delta.Before, key, page.before, slot.position);
                            CopyTiledSnapshot(delta.After, key, page.after, slot.position);
                        }
                        page.Queue();
                    }
                    catch { page.Abandon(); throw; }
                }
                using (WhimTexProfiler.Measure("brush.history.publishSparse")) history.PublishSparse(step, label ?? pixelUndoLabel ?? "Paint Stroke");
                visibleBounds?.Changed();
                for (int start = 0; start < blocks.Length; start += PaintTileTransfer.TilesPerPage)
                {
                    var version = afterVersions[start / PaintTileTransfer.TilesPerPage];
                    for (int i = start; i < Math.Min(start + PaintTileTransfer.TilesPerPage, blocks.Length); i++)
                        tiledPixelMap[delta.ChangedTiles[i]].historyVersion = version;
                }
                LastPaintTileCount = blocks.Length; LastPaintTilePixels = (long)blocks.Length * 256 * 256;
                pixelUndoLabel = null;
            }
            catch (Exception error)
            {
                // Prevent every remaining page from patching CPU pixels or writing
                // this abandoned revision, even if waiting for GPU completion fails.
                if (step != null) step.cancelled = true;
                var cleanupErrors = new List<Exception>();
                void Cleanup(Action action) { try { action(); } catch (Exception cleanup) { cleanupErrors.Add(cleanup); } }
                Cleanup(PaintTileTransfer.Drain);
                Cleanup(() => tiledSurface.Restore(delta.Before));
                foreach (var key in delta.ChangedTiles)
                    if (tiledPixelMap != null && tiledPixelMap.TryGetValue(key, out var tile) && created?.Contains(tile) != true)
                        Cleanup(() => RestoreTiledBacking(delta.Before, key, tile.texture));
                if (created != null) foreach (var tile in created)
                    Cleanup(() => { tiledPixelMap.Remove(tile.key); tiledPixels.Remove(tile); UnityEngine.Object.DestroyImmediate(tile.texture); });
                if (step != null) Cleanup(() => PaintHistoryStore.DeleteStep(step));
                if (cleanupErrors.Count != 0)
                { cleanupErrors.Insert(0, error); throw new AggregateException("Paint commit failed; one or more rollback operations also failed.", cleanupErrors); }
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                delta.Dispose();
                // Submitted copies no longer need historical GPU versions. Keep
                // only the bounded idle capacity available for the next stroke.
                tiledSurface?.TrimIdle();
            }
        }

        private static void CopyTiledSnapshot(TiledPaintSnapshot snapshot, Vector2Int key, RenderTexture target, Vector2Int destination)
        {
            // Transfer pages start transparent, including keys absent in Before.
            if (!snapshot.TryGetTile(key, out var tile)) return;
            var r = tile.PhysicalRect;
            Graphics.CopyTexture(tile.Texture, 0, 0, r.x, r.y, 256, 256, target, 0, 0, destination.x, destination.y);
        }

        private static void RestoreTiledBacking(TiledPaintSnapshot snapshot, Vector2Int key, Texture2D target)
        {
            var scratch = HdrUtility.Temporary(256, 256); var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = scratch; GL.Clear(false, true, Color.clear); RenderTexture.active = null;
                CopyTiledSnapshot(snapshot, key, scratch, Vector2Int.zero);
                RenderTexture.active = scratch; target.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false); target.Apply(false, false);
            }
            finally { RenderTexture.active = previous; ProfiledRenderTexture.ReleaseTemporary(scratch); }
        }

        internal bool RestoreTiledHistory()
        {
            var history = DrawingPixelHistory.Find(pixels);
            if (history == null || !history.NeedsRestore) return false;
            PaintTileTransfer.Drain();
            tiledSurface?.Cancel();
            tiledPixelMap = null; EnsureTiledPixelMap();
            var patches = tiledSurface != null && !tiledDenseEdit ? new Dictionary<Vector2Int, Texture2D>() : null;
            bool patched = false, dense = false;
            bool rebuildDenseHalf = paintSurface == null || tiledSurface != null;
            try
            {
                bool changed = history.Restore((block, forward) =>
                {
                    if (!block.sparse)
                    {
                        // A dense history block belongs to the base, even when this Undo group also edits tile overrides.
                        if (tiledSurface != null)
                        {
                            ReleaseTiledRuntime();
                            if (paintSurface != null) ReleasePaintSurface();
                            patches = null;
                        }
                        dense = true;
                        RestorePixelBlock(EnsurePaintSurface(pixels.width, pixels.height), block, forward);
                        return;
                    }
                    var key = new Vector2Int(block.rect.x / 256, block.rect.y / 256);
                    if (!forward && !block.beforePresent)
                    {
                        // Absent overrides omit Before bytes. Exposing a nonempty
                        // dense base must use its existing exact reconstruction path.
                        if (!tiledBaseEmpty && block.rect.Overlaps(new RectInt(0, 0, TiledSourceWidth, TiledSourceHeight)))
                            patches = null;
                        if (tiledPixelMap.Remove(key, out var removed))
                        {
                            tiledPixels.Remove(removed);
                            UnityEngine.Object.DestroyImmediate(removed.texture);
                        }
                        if (patches != null) patches[key] = null;
                        return;
                    }
                    var tile = GetOrCreateTiledPixel(key);
                    tile.historyVersion = null;
                    tile.texture.LoadRawTextureData(forward ? block.afterHalf : block.beforeHalf);
                    tile.texture.Apply(false, false); EditorUtility.SetDirty(tile.texture);
                    if (patches != null) patches[key] = tile.texture;
                });
                if (changed && patches != null && !dense)
                {
                    var imports = new List<TiledPaintImportTile>(patches.Count);
                    var removed = new List<Vector2Int>();
                    foreach (var pair in patches)
                        if (pair.Value == null) removed.Add(pair.Key);
                        else imports.Add(new TiledPaintImportTile(pair.Key, pair.Value, new RectInt(0, 0, 256, 256)));
                    try
                    {
                        // Import publishes atomically with copy-on-write for leased snapshots.
                        using var profile = WhimTexProfiler.Measure("brush.undo.patchGpu", layer: Owner);
                        using (tiledSurface.ImportTiles(imports, removed: removed)) { }
                        tiledSurface.TrimIdle(); patched = true;
                    }
                    catch (TiledPaintMemoryLimitException) { } // Rebuild from restored CPU storage within the same limit.
                    catch (TiledPaintResourceLostException) { }
                }
                if (!dense) ReleasePaintSurface();
                else if (rebuildDenseHalf) RestoreHalfHistory();
                restoredPixelUndo = true; unchecked { paintSurfaceRevision++; }
                return changed;
            }
            finally
            {
                // Never expose stale GPU pixels after a failed patch or a dense/native edit.
                if (!patched) ReleaseTiledRuntime();
            }
        }

        internal void FlushTiledStorage() => PaintTileTransfer.Drain();

        internal void CloneTiledPixels()
        {
            FlushTiledStorage();
            if (tiledPixels == null) return;
            var copy = new List<TiledDrawingTile>(tiledPixels.Count);
            try
            {
                foreach (var tile in tiledPixels) copy.Add(new TiledDrawingTile { key = tile.key, texture = UnityEngine.Object.Instantiate(tile.texture) });
            }
            catch { foreach (var tile in copy) UnityEngine.Object.DestroyImmediate(tile.texture); throw; }
            tiledPixels = copy; tiledPixelMap = null;
        }

        internal void DestroyTiledPixels(bool undo)
        {
            FlushTiledStorage();
            if (tiledPixels != null)
                foreach (var tile in tiledPixels)
                    if (tile?.texture != null && !AssetDatabase.Contains(tile.texture))
                    { if (undo) Undo.DestroyObjectImmediate(tile.texture); else UnityEngine.Object.DestroyImmediate(tile.texture); }
            tiledPixels = null; tiledPixelMap = null;
        }
    }
}
