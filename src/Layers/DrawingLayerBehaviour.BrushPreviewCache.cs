using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        // Stroke-local derived straight image; output-sized for viewport rendering,
        // source-sized only for full-input consumers. Never a source of paint or Undo.
        [NonSerialized] private RenderTexture brushPreviewSurface;
        [NonSerialized] private PaintTileSet brushPreviewTiles;
        [NonSerialized] private bool brushPreviewValid;
        [NonSerialized] private ulong brushPreviewRevision;
        [NonSerialized] private BrushPreviewSettings brushPreviewSettings;
        [NonSerialized] private bool brushPreviewRegional;
        [NonSerialized] private ProjectiveMatrix brushPreviewToView;
        [NonSerialized] private FilterMode brushPreviewFilter;
        [NonSerialized] private PaintTileSet brushPreviewFootprints;
        [NonSerialized] private RenderTexture brushInspectorSurface;
        [NonSerialized] private PaintTileSet brushInspectorTiles;
        [NonSerialized] private bool brushInspectorValid;
        [NonSerialized] private ulong brushInspectorRevision;
        [NonSerialized] private BrushPreviewSettings brushInspectorSettings;
        [NonSerialized] private ProjectiveMatrix brushInspectorToView;
        [NonSerialized] private FilterMode brushInspectorFilter;

        private bool TryRenderBrushViewport(in LayerRenderContext context, out RenderTexture result)
        {
            result = null;
            var doc = context.activeDocument;
            var tiling = Owner.ResolveTiling(Owner.RenderTransform.tiling);
            var material = WhimTexMaterials.Transform;
            if (!doc.RenderingViewport || !context.applyTransform ||
                (tiling != TransformTilingMode.Clip && tiling != TransformTilingMode.Unbounded) ||
                material == null || material.passCount < 2) return false;
            var toView = doc.CanvasToViewport * Owner.RenderTransform.ToMatrix(doc.width, doc.height);
            if (!toView.ValidUnitQuad()) return false;

            if (!brushPreviewRegional || brushPreviewSurface == null || !brushPreviewSurface.IsCreated() ||
                brushPreviewSurface.width != context.width || brushPreviewSurface.height != context.height)
            {
                ReleaseBrushPreview();
                // Cache output pixels, not the 8K source. Float retains intermediate
                // diagnostics/HDR until the ordinary document finishing pass.
                brushPreviewSurface = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                brushPreviewRegional = true;
            }
            var filter = ResolveFilterMode();
            var settings = new BrushPreviewSettings(this);
            bool full = !brushPreviewValid || brushPreviewRevision != paintSurfaceRevision ||
                !settings.Equals(brushPreviewSettings) || !toView.Equals(brushPreviewToView) || filter != brushPreviewFilter;
            if (full || brushPreviewTiles.Count > 0)
            {
                brushPreviewValid = false;
                DrawBrushPreviewTarget(brushPreviewSurface, context, toView, filter, full,
                    full ? advancedStroke.touched : brushPreviewTiles);
                brushPreviewTiles.Clear();
                brushPreviewSettings = settings; brushPreviewRevision = paintSurfaceRevision;
                brushPreviewToView = toView; brushPreviewFilter = filter; brushPreviewValid = true;
            }
            else WhimTexProfiler.Event("layer.drawing_preview_reuse", "counter", value: 1);
            // FinishStage owns its input. Keep the cached raw image private so repeated
            // renders and diagnostics cannot mutate it or release it out from under us.
            var output = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            output.filterMode = filter;
            output.wrapMode = TextureWrapMode.Clamp;
            try
            {
                Graphics.Blit(brushPreviewSurface, output);
                result = doc.FinishStage(output);
                return true;
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(output); throw; }
        }

        // Inspector callbacks need the complete layer, but only at their bounded output
        // resolution. Do not replace or invalidate the independently retained canvas cache.
        private bool TryRenderBrushInspector(in LayerRenderContext context, out RenderTexture result)
        {
            result = null;
            var doc = context.activeDocument;
            var tiling = context.applyTransform ? Owner.ResolveTiling(Owner.RenderTransform.tiling) : TransformTilingMode.Clip;
            var material = WhimTexMaterials.Transform;
            if (!doc.CapturingInspectorFX ||
                (tiling != TransformTilingMode.Clip && tiling != TransformTilingMode.Unbounded) ||
                material == null || material.passCount < 2) return false;
            var toView = context.applyTransform ? Owner.RenderTransform.ToMatrix(doc.width, doc.height) : ProjectiveMatrix.Identity;
            if (!toView.ValidUnitQuad()) return false;
            if (brushInspectorSurface == null || !brushInspectorSurface.IsCreated() ||
                brushInspectorSurface.width != context.width || brushInspectorSurface.height != context.height)
            {
                ReleaseBrushInspector();
                brushInspectorSurface = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                brushInspectorTiles ??= new PaintTileSet();
                brushInspectorTiles.Reset(paintSurface.width, paintSurface.height);
            }
            var filter = ResolveFilterMode();
            var settings = new BrushPreviewSettings(this);
            bool full = !brushInspectorValid || brushInspectorRevision != paintSurfaceRevision ||
                !settings.Equals(brushInspectorSettings) || !toView.Equals(brushInspectorToView) || filter != brushInspectorFilter;
            if (full || brushInspectorTiles.Count > 0)
            {
                brushInspectorValid = false;
                DrawBrushPreviewTarget(brushInspectorSurface, context, toView, filter, full,
                    full ? advancedStroke.touched : brushInspectorTiles);
                brushInspectorTiles.Clear();
                brushInspectorRevision = paintSurfaceRevision; brushInspectorSettings = settings;
                brushInspectorToView = toView; brushInspectorFilter = filter; brushInspectorValid = true;
            }
            var output = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            output.filterMode = filter; output.wrapMode = TextureWrapMode.Clamp;
            try
            {
                Graphics.Blit(brushInspectorSurface, output);
                result = doc.FinishStage(output);
                return true;
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(output); throw; }
        }

        private void DrawBrushPreviewTarget(RenderTexture target, in LayerRenderContext context,
            ProjectiveMatrix toView, FilterMode filter, bool full, PaintTileSet dirty)
        {
            using var profile = WhimTexProfiler.Measure(context.activeDocument.CapturingInspectorFX
                ? "layer.drawing_preview_inspector" : "layer.drawing_preview_region", context.activeDocument, Owner);
            using var gpu = profile.Gpu();
            var material = WhimTexMaterials.Transform;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Owner.ConfigureSourceTransform(material, paintSurface, context, premultipliedSource: true,
                    straightFallback: hasBakedPixelFrame || preserveStraightPaintRgb ? pixels : null,
                    decodeFallback: pixels != null && !HdrUtility.IsHdr(pixels));
                ConfigureBrushComposite(material);
                if (full) Graphics.Blit(paintSurface, target, material, 0);
                brushPreviewFootprints ??= new PaintTileSet();
                brushPreviewFootprints.Reset(paintSurface.width, paintSurface.height);
                for (int i = 0; i < dirty.Count; i++)
                {
                    var tile = dirty[i];
                    // A bilinear footprint starting in the preceding tile can read
                    // this tile. Include those owners even if they contain no stroke.
                    if (filter != FilterMode.Point)
                        tile = new RectInt(tile.x - 1, tile.y - 1, tile.width + 1, tile.height + 1);
                    brushPreviewFootprints.Include(tile);
                }
                int submitted = 0;
                for (int i = 0; i < brushPreviewFootprints.Count; i++)
                {
                    var tile = brushPreviewFootprints[i];
                    if (!TryBrushPreviewRect(tile, toView, filter, target.width, target.height, out var rect)) continue;
                    var present = new Vector4(
                        BindBrushPreviewPage(material, tile, "_BrushStroke", "_BrushStrokeMap"),
                        BindBrushPreviewPage(material, new RectInt(tile.x + PaintTileSet.Size, tile.y, 1, 1), "_PreviewStroke10", "_PreviewMap10"),
                        BindBrushPreviewPage(material, new RectInt(tile.x, tile.y + PaintTileSet.Size, 1, 1), "_PreviewStroke01", "_PreviewMap01"),
                        BindBrushPreviewPage(material, new RectInt(tile.x + PaintTileSet.Size, tile.y + PaintTileSet.Size, 1, 1), "_PreviewStroke11", "_PreviewMap11"));
                    material.SetVector("_PreviewPresent", present);
                    material.SetVector("_PreviewTile", new Vector4(tile.xMin, tile.yMin, tile.xMax, tile.yMax));
                    DrawBrushRegion(paintSurface, target, material, 1, rect, rect);
                    submitted++;
                }
                WhimTexProfiler.Event("layer.drawing_preview_region_tiles", "counter", value: submitted);
            }
            finally
            {
                ClearBrushComposite(material);
                material.SetTexture("_PreviewStroke10", null); material.SetTexture("_PreviewStroke01", null);
                material.SetTexture("_PreviewStroke11", null); material.SetTexture("_OriginalStraight", null);
                material.SetTexture("_MainTex", null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        private float BindBrushPreviewPage(Material material, RectInt tile, string textureName, string mapName)
        {
            var page = tile.x < paintSurface.width && tile.y < paintSurface.height ? advancedStroke.Get(tile) : null;
            material.SetTexture(textureName, page != null ? page : Texture2D.blackTexture);
            if (page != null) material.SetVector(mapName, advancedStroke.Map(tile));
            return page != null ? 1 : 0;
        }

        private bool TryBrushPreviewRect(RectInt tile, ProjectiveMatrix toView, FilterMode filter, int width, int height, out Rect rect)
        {
            double offset = filter == FilterMode.Point ? 0 : .5;
            double x0 = tile.x == 0 ? 0 : (tile.xMin + offset) / paintSurface.width;
            double y0 = tile.y == 0 ? 0 : (tile.yMin + offset) / paintSurface.height;
            double x1 = Math.Min(1, (tile.xMax + offset) / paintSurface.width);
            double y1 = Math.Min(1, (tile.yMax + offset) / paintSurface.height);
            var a = toView.Point(new Double2(x0, y0)); var b = toView.Point(new Double2(x1, y0));
            var c = toView.Point(new Double2(x1, y1)); var d = toView.Point(new Double2(x0, y1));
            float w = width, h = height;
            float left = Mathf.Clamp(Mathf.Floor((float)Math.Min(Math.Min(a.x,b.x),Math.Min(c.x,d.x)) * w) - 1, 0, w);
            float bottom = Mathf.Clamp(Mathf.Floor((float)Math.Min(Math.Min(a.y,b.y),Math.Min(c.y,d.y)) * h) - 1, 0, h);
            float right = Mathf.Clamp(Mathf.Ceil((float)Math.Max(Math.Max(a.x,b.x),Math.Max(c.x,d.x)) * w) + 1, 0, w);
            float top = Mathf.Clamp(Mathf.Ceil((float)Math.Max(Math.Max(a.y,b.y),Math.Max(c.y,d.y)) * h) + 1, 0, h);
            rect = new Rect(left / w, bottom / h, (right - left) / w, (top - bottom) / h);
            return rect.width > 0 && rect.height > 0;
        }

        private RenderTexture GetBrushPreviewSurface()
        {
            if (brushPreviewRegional || brushPreviewSurface == null || !brushPreviewSurface.IsCreated())
            {
                ReleaseBrushPreview();
                brushPreviewSurface = HdrUtility.Temporary(paintSurface.width, paintSurface.height);
            }
            brushPreviewSurface.filterMode = ResolveFilterMode();
            var settings = new BrushPreviewSettings(this);
            bool full = !brushPreviewValid || brushPreviewRevision != paintSurfaceRevision ||
                !settings.Equals(brushPreviewSettings);
            if (full || brushPreviewTiles.Count > 0)
            {
                // Recompute from Half base + accumulated stroke, never from the old
                // straight cache: selection, blend and opacity must apply only once.
                brushPreviewValid = false;
                ConvertStraightSurface(brushPreviewSurface, 1f, full ? null : brushPreviewTiles);
                brushPreviewTiles.Clear();
                brushPreviewSettings = settings;
                brushPreviewRevision = paintSurfaceRevision;
                brushPreviewValid = true;
                WhimTexProfiler.Event(full ? "layer.drawing_preview_full" : "layer.drawing_preview_partial", "counter", value: 1);
            }
            else WhimTexProfiler.Event("layer.drawing_preview_reuse", "counter", value: 1);
            return brushPreviewSurface;
        }

        private void DrawBrushPreviewTiles(RenderTexture target, Material material, PaintTileSet tiles)
        {
            if (advancedStroke != null)
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    advancedStroke.Bind(material, tile);
                    var uv = new Rect(tile.x / (float)target.width, tile.y / (float)target.height,
                        tile.width / (float)target.width, tile.height / (float)target.height);
                    DrawBrushRegion(paintSurface, target, material, 0, uv, uv);
                }
                return;
            }
            material.SetTexture("_MainTex", paintSurface);
            RenderTexture.active = target;
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                if (!material.SetPass(0)) throw new InvalidOperationException("Cannot update brush preview.");
                // One submission for all disjoint tiles, preserving the untouched target.
                GL.Begin(GL.QUADS);
                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    float x0 = tile.xMin / (float)target.width, x1 = tile.xMax / (float)target.width;
                    float y0 = tile.yMin / (float)target.height, y1 = tile.yMax / (float)target.height;
                    GL.TexCoord2(x0, y0); GL.Vertex3(x0, y0, 0);
                    GL.TexCoord2(x0, y1); GL.Vertex3(x0, y1, 0);
                    GL.TexCoord2(x1, y1); GL.Vertex3(x1, y1, 0);
                    GL.TexCoord2(x1, y0); GL.Vertex3(x1, y0, 0);
                }
                GL.End();
            }
            finally { GL.PopMatrix(); }
        }

        private void ReleaseBrushPreview()
        {
            if (brushPreviewSurface != null) ProfiledRenderTexture.ReleaseTemporary(brushPreviewSurface);
            brushPreviewSurface = null;
            brushPreviewRegional = false;
            brushPreviewValid = false;
            brushPreviewSettings = default;
            brushPreviewTiles?.Clear();
        }

        private void ReleaseBrushInspector()
        {
            if (brushInspectorSurface != null) ProfiledRenderTexture.ReleaseTemporary(brushInspectorSurface);
            brushInspectorSurface = null;
            brushInspectorValid = false;
            brushInspectorSettings = default;
            brushInspectorTiles?.Clear();
        }

        // Capture values, not the mutable dynamics object. Changes that affect already
        // accumulated dabs require a full refresh, even without a new pointer event.
        private readonly struct BrushPreviewSettings : IEquatable<BrushPreviewSettings>
        {
            private readonly Vector4 modes, options;
            private readonly float opacity;
            private readonly Texture mask, original;
            private readonly uint maskVersion, originalVersion;
            private readonly ProjectiveMatrix transform;
            private readonly bool fallback;
            internal BrushPreviewSettings(DrawingLayerBehaviour layer)
            {
                var p = layer.brushCompositeParameters;
                var d = p.Dynamics;
                modes = new Vector4((int)(d?.blend ?? BlendMode.Normal), (int)layer.blendRange,
                    (int)layer.colorRange, p.WriteChannels);
                options = new Vector4(p.Erase ? 1 : 0, p.WrapCanvas ? 1 : 0,
                    d?.BlendsEachStamp(p.Erase) == true ? 1 : 0, d?.PressureOpacity == true ? 1 : 0);
                opacity = d?.opacity ?? 1;
                mask = p.SelectionMask; maskVersion = mask != null ? mask.updateCount : 0;
                original = layer.pixels; originalVersion = original != null ? original.updateCount : 0;
                transform = layer.brushCompositeTransform;
                fallback = layer.hasBakedPixelFrame || layer.preserveStraightPaintRgb;
            }
            public bool Equals(BrushPreviewSettings other) => modes.Equals(other.modes) && options.Equals(other.options) &&
                opacity == other.opacity && mask == other.mask && maskVersion == other.maskVersion &&
                original == other.original && originalVersion == other.originalVersion && transform.Equals(other.transform) && fallback == other.fallback;
        }
    }
}
