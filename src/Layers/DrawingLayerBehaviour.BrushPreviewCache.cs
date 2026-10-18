using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        // Stroke-local derived straight image; never a new source of paint or Undo.
        // Retains the temporary target formerly allocated for each live render.
        [NonSerialized] private RenderTexture brushPreviewSurface;
        [NonSerialized] private PaintTileSet brushPreviewTiles;
        [NonSerialized] private bool brushPreviewValid;
        [NonSerialized] private ulong brushPreviewRevision;
        [NonSerialized] private BrushPreviewSettings brushPreviewSettings;

        private RenderTexture GetBrushPreviewSurface()
        {
            if (brushPreviewSurface == null || !brushPreviewSurface.IsCreated())
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
            brushPreviewValid = false;
            brushPreviewSettings = default;
            brushPreviewTiles?.Clear();
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
