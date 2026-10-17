using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private PaintTileSet brushDirtyTiles;
        [NonSerialized] private string pixelUndoLabel;
        [NonSerialized] private bool partialStorageCurrent, restoredPixelUndo;
        internal int LastPaintTileCount { get; private set; }
        internal long LastPaintTilePixels { get; private set; }
        internal UnityEngine.Object PixelUndoObject => DrawingPixelHistory.Find(pixels);

        private bool CommitPixelHistory(Material composite)
        {
            if (pixelUndoLabel == null || !PaintTileTransfer.SupportsPixels(pixels) || brushDirtyTiles == null || brushDirtyTiles.Count == 0) return false;
            using var profile = WhimTexProfiler.Measure("brush.history.commit");
            RectInt[] blocks = brushDirtyTiles.ToArray();
            var history = DrawingPixelHistory.Find(pixels, true);
            var step = history.Begin(blocks, pixelUndoLabel);
            LastPaintTileCount = blocks.Length; LastPaintTilePixels = brushDirtyTiles.PixelCount;
            for (int start = 0, pageIndex = 0; start < blocks.Length; start += PaintTileTransfer.TilesPerPage, pageIndex++)
            {
                int length = Math.Min(PaintTileTransfer.TilesPerPage, blocks.Length - start);
                var pageBlocks = new RectInt[length]; Array.Copy(blocks, start, pageBlocks, 0, length);
                var page = new PaintTileTransfer.Page(pixels, step, pageIndex, pageBlocks);
                try
                {
                    page.CopyHalf(paintSurface, true);
                    using (WhimTexProfiler.Measure("brush.history.computeSubmit"))
                    if (!BrushStrokeCompute.TryCommitTiles(paintSurface, pageBlocks, composite,
                        advancedStroke, brushCompositeParameters.SelectionMask, pixels))
                        foreach (var tile in pageBlocks) CommitBrushTile(composite, tile);
                    page.CopyHalf(paintSurface, false);
                    ConvertStoredPage(page);
                    page.Queue();
                }
                catch { page.Abandon(); throw; }
            }
            partialStorageCurrent = true;
            paintSurfaceDirty = false;
            pixelUndoLabel = null;
            return true;
        }
        private void ConvertStoredPage(PaintTileTransfer.Page page)
        {
            using var profile = WhimTexProfiler.Measure("brush.history.convertStored");
            var conversion = WhimTexMaterials.AlphaConversion;
            bool hdr = HdrUtility.IsHdr(pixels);
            conversion.SetFloat("_Mode", hdr ? 3f : 2f);
            ClearBrushComposite(conversion);
            conversion.SetFloat("_StraightFallback", (hasBakedPixelFrame || preserveStraightPaintRgb) && pixels != null ? hdr ? 1f : 2f : 0f);
            conversion.SetTexture("_OriginalStraight", pixels);
            conversion.SetFloat("_SkipUnchangedPaint", 1f);
            conversion.SetTexture("_PaintPreviousHalf", page.before);
            var filter = paintSurface.filterMode;
            paintSurface.filterMode = FilterMode.Point;
            try
            {
                for (int i = 0; i < page.blocks.Length; i++)
                {
                    var r = page.blocks[i]; var s = PaintTileTransfer.Slot(i, r);
                    RenderTexture.active = null;
                    Graphics.CopyTexture(pixels, 0, 0, r.x, r.y, r.width, r.height, page.stored, 0, 0, s.x, s.y);
                    conversion.SetVector("_PaintPreviousMap", new Vector4(paintSurface.width / (float)page.stored.width,
                        paintSurface.height / (float)page.stored.height, (s.x - r.x) / (float)page.stored.width, (s.y - r.y) / (float)page.stored.height));
                    DrawBrushRegion(paintSurface, page.stored, conversion, 0,
                        new Rect(s.x / (float)page.stored.width, s.y / (float)page.stored.height,
                            s.width / (float)page.stored.width, s.height / (float)page.stored.height),
                        new Rect(r.x / (float)paintSurface.width, r.y / (float)paintSurface.height,
                            r.width / (float)paintSurface.width, r.height / (float)paintSurface.height));
                }
            }
            finally
            {
                paintSurface.filterMode = filter;
                conversion.SetTexture("_OriginalStraight", null); conversion.SetFloat("_StraightFallback", 0f); conversion.SetTexture("_MainTex", null);
                conversion.SetFloat("_SkipUnchangedPaint", 0f); conversion.SetTexture("_PaintPreviousHalf", null);
            }
        }
        private void CommitBrushTile(Material material, RectInt tile)
        {
            if (BrushStrokeCompute.TryCommit(paintSurface, tile, material, advancedStroke, brushCompositeParameters.SelectionMask, pixels)) return;
            var result = HdrUtility.Temporary(tile.width, tile.height);
            try
            {
                DrawBrushRegion(paintSurface, result, material, 1, new Rect(0, 0, 1, 1),
                    new Rect(tile.x / (float)paintSurface.width, tile.y / (float)paintSurface.height,
                        tile.width / (float)paintSurface.width, tile.height / (float)paintSurface.height));
                CopyBrushRegion(result, paintSurface, tile);
            }
            finally { ProfiledRenderTexture.ReleaseTemporary(result); }
        }
        // Interactive completion keeps the GPU result immediately available. CPU-only
        // consumers and persistence explicitly drain through SyncPendingSurfaceToTexture.
        internal void FinishStrokeStorage()
        {
            if (!partialStorageCurrent || paintSurfaceDirty) SyncSurfaceToTexture();
        }
        internal bool ReconcilePixelUndo()
        {
            var history = DrawingPixelHistory.Find(pixels);
            if (history == null || !history.NeedsRestore) return false;
            using var profile = WhimTexProfiler.Measure("brush.undo.reconcile");
            PaintTileTransfer.Drain();
            ReleaseAdvancedStroke();
            bool rebuilt = paintSurface == null;
            var surface = EnsurePaintSurface(pixels.width, pixels.height);
            history.Restore((block, forward) => RestorePixelBlock(surface, block, forward));
            if (rebuilt) RestoreHalfHistory();
            paintSurfaceDirty = false; partialStorageCurrent = true; restoredPixelUndo = true;
            unchecked { paintSurfaceRevision++; }
            return true;
        }
        private void RestoreHalfHistory()
        {
            using var profile = WhimTexProfiler.Measure("brush.undo.rebuildHalf");
            DrawingPixelHistory.Find(pixels)?.RestoreCurrentHalf((block, _) =>
            {
                var r = block.rect;
                var tile = new Texture2D(r.width, r.height, TextureFormat.RGBAHalf, false, true);
                try
                {
                    tile.LoadRawTextureData(block.afterHalf); tile.Apply(false, false);
                    Graphics.CopyTexture(tile, 0, 0, 0, 0, r.width, r.height, paintSurface, 0, 0, r.x, r.y);
                }
                finally { UnityEngine.Object.DestroyImmediate(tile); }
            });
        }
        private void RestorePixelBlock(RenderTexture surface, PaintHistoryStore.Block block, bool forward)
        {
            using var profile = WhimTexProfiler.Measure("brush.undo.block");
            var r = block.rect;
            byte[] stored = forward ? block.afterStored : block.beforeStored;
            byte[] half = forward ? block.afterHalf : block.beforeHalf;
            using (WhimTexProfiler.Measure("brush.undo.patchCpu"))
                PaintHistoryStore.WriteRows(pixels.GetRawTextureData<byte>(), pixels.width, r, HdrUtility.IsHdr(pixels) ? 8 : 4, stored);
            var storageTile = new Texture2D(r.width, r.height, pixels.format, false, !pixels.isDataSRGB);
            var halfTile = new Texture2D(r.width, r.height, TextureFormat.RGBAHalf, false, true);
            try
            {
                using (WhimTexProfiler.Measure("brush.undo.upload"))
                {
                    storageTile.LoadRawTextureData(stored); storageTile.Apply(false, false);
                    halfTile.LoadRawTextureData(half); halfTile.Apply(false, false);
                }
                using var copy = WhimTexProfiler.Measure("brush.undo.copyGpu");
                Graphics.CopyTexture(storageTile, 0, 0, 0, 0, r.width, r.height, pixels, 0, 0, r.x, r.y);
                Graphics.CopyTexture(halfTile, 0, 0, 0, 0, r.width, r.height, surface, 0, 0, r.x, r.y);
            }
            finally { UnityEngine.Object.DestroyImmediate(storageTile); UnityEngine.Object.DestroyImmediate(halfTile); }
        }
    }
}
