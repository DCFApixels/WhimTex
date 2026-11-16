using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex.TiledPaint
{
    public sealed partial class TiledPaintSurface
    {
        // Preview is a caller-owned source-pixel region. Pending paint is composed
        // directly into it; no committed tile, snapshot or history entry is created.
        public void CopyRegion(RectInt region, RenderTexture target, bool includePending)
        {
            CheckMutation();ValidateRegion(region);
            if (region.width < 1 || region.height < 1 || (long)region.x + region.width > int.MaxValue ||
                (long)region.y + region.height > int.MaxValue || target == null ||
                target.dimension != TextureDimension.Tex2D || target.antiAliasing != 1 ||
                target.width != region.width || target.height != region.height ||
                target.graphicsFormat != GraphicsFormat.R16G16B16A16_SFloat)
                throw new ArgumentException("CopyRegion requires a matching-size, non-MSAA linear RGBAHalf target and a valid pixel region.");

            // Reject aliases and lost resources before the first target clear,
            // including pages retained only by old snapshots and frozen inputs.
            Validate();
            if (pool.OwnsTexture(target) || selection != null && selection.Texture == target ||
                originalStraight != null && originalStraight.Texture == target)
                throw new ArgumentException("Cannot overwrite tiled paint storage or frozen stroke inputs.");
            if(!target.IsCreated()&&!target.Create())throw new InvalidOperationException("Cannot allocate tiled preview target.");
            if (!includePending || !Pending || dirty.Count == 0)
            {
                CopyRegion(region, target);
                return;
            }
            if (composite == null || composite.shader == null || !composite.shader.isSupported)
                throw new NotSupportedException("The pending paint composite shader is unavailable.");
            ConfigureComposite();

            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                CopyRegion(region, target);
                commands.Clear();
                commands.SetRenderTarget(target);
                // SV_Position addresses the full target, not a tile-sized viewport.
                commands.SetViewport(new Rect(0, 0, target.width, target.height));
                foreach (var page in pending) foreach (var tile in page.tiles)
                {
                    if (!dirty.Contains(tile.Key)) continue;
                    var logical = tile.Key * TileSize;
                    int x = Math.Max(region.xMin, logical.x), y = Math.Max(region.yMin, logical.y);
                    int right = Math.Min(region.xMax, logical.x + TileSize), top = Math.Min(region.yMax, logical.y + TileSize);
                    if (right <= x || top <= y) continue;
                    source.Tiles.TryGetValue(tile.Key, out var before);
                    var old = before?.Origin ?? Vector2Int.zero;
                    var stroke = tile.Origin;
                    properties.Clear();
                    properties.SetFloat("_HasBefore", before != null ? 1 : 0);
                    properties.SetTexture("_Before", (Texture)before?.Page.Texture ?? Texture2D.blackTexture);
                    properties.SetVector("_BeforeOffset", new Vector4(old.x + region.x - logical.x, old.y + region.y - logical.y, 0, 0));
                    properties.SetVector("_BrushSurfaceSize", new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));
                    properties.SetVector("_BrushStrokeMap", new Vector4(target.width / (float)PageSize, target.height / (float)PageSize,
                        (region.x - logical.x + stroke.x) / (float)PageSize, (region.y - logical.y + stroke.y) / (float)PageSize));
                    properties.SetTexture("_BrushStroke", page.Texture);
                    properties.SetVector("_LogicalOffset", new Vector4(region.x, region.y, 1f/Frame.SourceWidth, 1f/Frame.SourceHeight));
                    commands.EnableScissorRect(new Rect(x - region.x, y - region.y, right - x, top - y));
                    commands.DrawProcedural(Matrix4x4.identity, composite, 0, MeshTopology.Triangles, 3, 1, properties);
                }
                commands.DisableScissorRect();
                GL.sRGBWrite = false;
                GL.PushMatrix();
                try { GL.LoadOrtho(); Graphics.ExecuteCommandBuffer(commands); }
                finally { GL.PopMatrix(); }
            }
            finally
            {
                commands.Clear();
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }
    }
}
