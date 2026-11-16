using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex.TiledPaint;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [SerializeField, HideInInspector]
        bool tiledSourceEnabled, tiledBaseEmpty;
        [SerializeField, HideInInspector]
        int tiledSourceWidth, tiledSourceHeight;
        [NonSerialized]
        TiledPaintSurface tiledSurface;
        [NonSerialized]
        Material tiledViewMaterial;
        [NonSerialized]
        bool tiledDenseEdit;
        internal bool HasTiledSource => tiledSourceEnabled;
        internal int TiledSourceWidth => tiledSourceWidth > 0 ? tiledSourceWidth : pixels != null ? pixels.width : 1;
        internal int TiledSourceHeight => tiledSourceHeight > 0 ? tiledSourceHeight : pixels != null ? pixels.height : 1;
        public override TransformTilingMode SourceTiling => HasTiledSource ? TransformTilingMode.Unbounded : TransformTilingMode.Clip;

        void EnsureTiledSource(int canvasWidth, int canvasHeight)
        {
            if (tiledSurface != null)
                return;
            EnsureDeferredTexture();
            // The original texture remains the durable base and the native history identity.
            // Sparse tile records overlay it; occupied bounds never resize its coordinate frame.
            if (pixels == null)
            {
                bool enabled = tiledSourceEnabled;
                tiledSourceEnabled = false;
                try
                {
                    EnsurePaintSurface(canvasWidth, canvasHeight);
                    SyncSurfaceToTexture();
                    tiledBaseEmpty = true;
                }
                finally
                {
                    tiledSourceEnabled = enabled;
                }
            }

            if (tiledSourceWidth <= 0)
                tiledSourceWidth = pixels.width;
            if (tiledSourceHeight <= 0)
                tiledSourceHeight = pixels.height;
            var transform = Owner.PixelCanvasTransform.ToMatrix(canvasWidth, canvasHeight);
            var frame = new TiledPaintFrame(TiledSourceWidth, TiledSourceHeight, canvasWidth, canvasHeight, ProjectiveMatrix.Scale(canvasWidth, canvasHeight) * transform * ProjectiveMatrix.Scale(1d / TiledSourceWidth, 1d / TiledSourceHeight));
            var shaders = new TiledPaintShaders(WhimTexMaterials.PaintBrush.shader, AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.shader"), AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.compute"));
            var created = new TiledPaintSurface(frame, shaders, new TiledPaintOptions { GpuBudgetBytes = 2L * 1024 * 1024 * 1024 });
            try
            {
                if (!tiledBaseEmpty)
                {
                    var dense = EnsurePaintSurface(canvasWidth, canvasHeight);
                    ImportDenseSource(created, dense, new RectInt(0, 0, dense.width, dense.height)).Dispose();
                }

                if (tiledPixels != null && tiledPixels.Count > 0)
                {
                    var imports = new List<TiledPaintImportTile>(tiledPixels.Count);
                    foreach (var tile in tiledPixels)
                        imports.Add(new TiledPaintImportTile(tile.key, tile.texture, new RectInt(0, 0, 256, 256)));
                    using var delta = created.ImportTiles(imports);
                }

                tiledSurface = created;
                tiledSourceEnabled = true;
                if (paintSurface != null)
                {
                    UnityEngine.Object.DestroyImmediate(paintSurface);
                    paintSurface = null;
                }

                paintSurfaceDirty = false;
                InvalidateAlphaCoverage();
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }

        static TiledPaintDelta ImportDenseSource(TiledPaintSurface target, RenderTexture dense, RectInt bounds)
        {
            var imports = new List<TiledPaintImportTile>();
            var edges = new List<RenderTexture>();
            var previous = RenderTexture.active;
            try
            {
                for (int y = bounds.yMin / 256 * 256; y < bounds.yMax; y += 256)
                    for (int x = bounds.xMin / 256 * 256; x < bounds.xMax; x += 256)
                    {
                        int w = Math.Min(256, dense.width - x), h = Math.Min(256, dense.height - y);
                        if (w <= 0 || h <= 0)
                            continue;
                        Texture source = dense;
                        var region = new RectInt(x, y, 256, 256);
                        if (w < 256 || h < 256)
                        {
                            var edge = HdrUtility.Temporary(256, 256);
                            edges.Add(edge);
                            target.CopyRegion(new RectInt(x, y, 256, 256), edge);
                            RenderTexture.active = null;
                            Graphics.CopyTexture(dense, 0, 0, x, y, w, h, edge, 0, 0, 0, 0);
                            source = edge;
                            region = new RectInt(0, 0, 256, 256);
                        }

                        imports.Add(new TiledPaintImportTile(new Vector2Int(x / 256, y / 256), source, region));
                    }

                return target.ImportTiles(imports);
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var edge in edges)
                    ProfiledRenderTexture.ReleaseTemporary(edge);
            }
        }

        void EnsureTiledStroke(PaintStrokeParameters parameters, int width, int height)
        {
            if (tiledSurface.Pending)
                return;
            var dynamics = parameters.Dynamics;
            brushCompositeParameters = parameters;
            tiledSurface.SetCanvasMapping(Owner.PixelCanvasTransform.ToMatrix(width, height) * ProjectiveMatrix.Scale(1d / TiledSourceWidth, 1d / TiledSourceHeight));
            tiledSurface.Begin(new TiledStrokeSettings(dynamics?.opacity ?? 1f, dynamics?.blend ?? BlendMode.Normal, colorRange == LayerColorRange.Standard, blendRange == LayerBlendRange.HDR, parameters.Erase, parameters.WriteChannels, parameters.SelectionMask, parameters.WrapCanvas, dynamics?.BlendsEachStamp(parameters.Erase) == true, dynamics?.PressureOpacity == true, parameters.WriteChannels != 15 ? pixels : null, pixels != null && !HdrUtility.IsHdr(pixels) && QualitySettings.activeColorSpace == ColorSpace.Gamma));
        }

        void IncludeTiledBrushBounds(Rect bounds)
        {
            if (bounds.width <= 0 || bounds.height <= 0)
                return;
            int x = Mathf.FloorToInt(bounds.xMin * TiledSourceWidth), y = Mathf.FloorToInt(bounds.yMin * TiledSourceHeight);
            int right = Mathf.CeilToInt(bounds.xMax * TiledSourceWidth), top = Mathf.CeilToInt(bounds.yMax * TiledSourceHeight);
            if (brushDirtyBounds.width > 0 && brushDirtyBounds.height > 0)
            {
                x = Math.Min(x, brushDirtyBounds.xMin);
                y = Math.Min(y, brushDirtyBounds.yMin);
                right = Math.Max(right, brushDirtyBounds.xMax);
                top = Math.Max(top, brushDirtyBounds.yMax);
            }

            brushDirtyBounds = new RectInt(x, y, right - x, top - y);
        }

        void CommitTiledStroke()
        {
            if (tiledSurface?.Pending != true)
                return;
            using var profile = WhimTexProfiler.Measure("brush.history.commit", layer: Owner);
            var delta = tiledSurface.Commit();
            CommitTiledHistory(delta, pixelUndoLabel);
            pixelUndoLabel = null;
            paintSurfaceDirty = false;
            brushDirtyBounds = default;
            ClearFirstBrush();
            unchecked
            {
                paintSurfaceRevision++;
            }
        }

        void PrepareTiledDenseEdit(int width, int height, string label)
        {
            CommitTiledStroke();
            FlushTiledStorage();
            EnsureTiledSource(width, height);
            EnsurePaintSurface(width, height);
            tiledSurface.CopyRegion(new RectInt(0, 0, TiledSourceWidth, TiledSourceHeight), paintSurface, false);
            tiledDenseEdit = true;
            pixelUndoLabel = label;
        }

        bool CommitTiledDenseEdit()
        {
            // BeginStroke closes the previous operation after preparation. An
            // untouched dense bridge is still needed by the incoming gesture.
            if (!tiledDenseEdit || pixelEditBefore == null)
                return false;
            try
            {
                if (pixelEditBefore == null || pixelEditBefore.touched.Count == 0)
                    return false;
                // Import only touched tiles; preserving outside parts of partial edge tiles.
                var imports = new List<TiledPaintImportTile>();
                var edges = new List<RenderTexture>();
                var previous = RenderTexture.active;
                try
                {
                    for (int tileIndex = 0; tileIndex < pixelEditBefore.touched.Count; tileIndex++)
                    {
                        var r = pixelEditBefore.touched[tileIndex];
                        Texture source = paintSurface;
                        var region = new RectInt(r.x, r.y, 256, 256);
                        if (r.width < 256 || r.height < 256)
                        {
                            var edge = HdrUtility.Temporary(256, 256);
                            edges.Add(edge);
                            tiledSurface.CopyRegion(new RectInt(r.x, r.y, 256, 256), edge);
                            RenderTexture.active = null;
                            Graphics.CopyTexture(paintSurface, 0, 0, r.x, r.y, r.width, r.height, edge, 0, 0, 0, 0);
                            source = edge;
                            region = new RectInt(0, 0, 256, 256);
                        }

                        imports.Add(new TiledPaintImportTile(new Vector2Int(r.x / 256, r.y / 256), source, region));
                    }

                    CommitTiledHistory(tiledSurface.ImportTiles(imports), pixelUndoLabel);
                }
                finally
                {
                    RenderTexture.active = previous;
                    foreach (var edge in edges)
                        ProfiledRenderTexture.ReleaseTemporary(edge);
                }

                paintSurfaceDirty = false;
                return true;
            }
            finally
            {
                tiledDenseEdit = false;
                ReleasePixelEdit();
                ReleasePaintSurface();
            }
        }

        void ClearTiledSource(int width, int height)
        {
            CommitTiledStroke();
            EnsureTiledSource(width, height);
            var imports = new List<TiledPaintImportTile>();
            using var snapshot = tiledSurface.AcquireSnapshot();
            var empty = HdrUtility.Temporary(256, 256);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = empty;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = null;
                foreach (var tile in snapshot.Tiles)
                    imports.Add(new TiledPaintImportTile(tile.Key, empty, new RectInt(0, 0, 256, 256)));
                CommitTiledHistory(tiledSurface.ImportTiles(imports), pixelUndoLabel ?? "Clear Drawing");
                paintSurfaceDirty = false;
                tiledDenseEdit = false;
                ReleasePixelEdit();
                ReleasePaintSurface();
                unchecked
                {
                    paintSurfaceRevision++;
                }
            }
            finally
            {
                RenderTexture.active = previous;
                ProfiledRenderTexture.ReleaseTemporary(empty);
            }
        }

        void BlitTiledStraight(RenderTexture target, float mode)
        {
            EnsureTiledSource(target.width, target.height);
            if (tiledViewMaterial == null)
                tiledViewMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintView.shader"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            tiledSurface.Render(target, tiledViewMaterial, ProjectiveMatrix.Identity, FilterMode.Point,
                false, hasBakedPixelFrame || preserveStraightPaintRgb ? pixels : null,
                !HdrUtility.IsHdr(pixels) && QualitySettings.activeColorSpace == ColorSpace.Gamma,
                outputMode: mode);
        }

        RenderTexture RenderTiledSource(in LayerRenderContext context)
        {
            EnsureTiledSource(context.activeDocument.width, context.activeDocument.height);
            var tiling = context.applyTransform ? Owner.ResolveTiling(Owner.RenderTransform.tiling) : TransformTilingMode.Unbounded;
            if (tiling == TransformTilingMode.Repeat || tiling == TransformTilingMode.Mirror || tiling == TransformTilingMode.Clamp)
            {
                var dense = HdrUtility.Temporary(TiledSourceWidth, TiledSourceHeight);
                try
                {
                    tiledSurface.CopyRegion(new RectInt(0, 0, dense.width, dense.height), dense, true);
                    return Owner.ApplyTransformAndFx(dense, context, premultipliedSource: true,
                        straightFallback: hasBakedPixelFrame || preserveStraightPaintRgb ? pixels : null,
                        decodeFallback: !HdrUtility.IsHdr(pixels));
                }
                finally
                {
                    ProfiledRenderTexture.ReleaseTemporary(dense);
                }
            }

            if (tiledViewMaterial == null)
                tiledViewMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintView.shader"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            var matrix = context.applyTransform ? Owner.PixelCanvasTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height) : ProjectiveMatrix.Identity;
            if (context.applyTransform && context.activeDocument.RenderingViewport)
                matrix = context.activeDocument.CanvasToViewport * matrix;
            var output = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            output.filterMode = ResolveFilterMode();
            output.wrapMode = TextureWrapMode.Clamp;
            try
            {
                tiledSurface.Render(output, tiledViewMaterial, matrix, output.filterMode, tiling == TransformTilingMode.Clip,
                    hasBakedPixelFrame || preserveStraightPaintRgb ? pixels : null,
                    pixels != null && !HdrUtility.IsHdr(pixels) && QualitySettings.activeColorSpace == ColorSpace.Gamma,
                    tiledDenseEdit ? paintSurface : null);
                output = context.activeDocument.FinishStage(output);
                Owner.ApplyFx(ref output, context, output.filterMode);
                return output;
            }
            catch
            {
                if (output != null)
                    ProfiledRenderTexture.ReleaseTemporary(output);
                throw;
            }
        }

        void ReleaseTiledRuntime()
        {
            tiledSurface?.Dispose();
            tiledSurface = null;
            if (tiledViewMaterial != null)
                UnityEngine.Object.DestroyImmediate(tiledViewMaterial);
            tiledViewMaterial = null;
        }
    }
}
