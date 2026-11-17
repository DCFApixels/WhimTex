using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex.TiledPaint
{
    public sealed partial class TiledPaintSurface
    {
        readonly HashSet<Vector2Int> renderKeys = new HashSet<Vector2Int>();
        readonly Texture[] renderTextures = new Texture[8];
        readonly Vector4[] renderMaps = new Vector4[8];
        static readonly int tileKeyId = Shader.PropertyToID("_TileKey");
        static readonly int[] baseIds = PropertyIds("_Base"), inkIds = PropertyIds("_Ink"), baseMapIds = PropertyIds("_BaseMap"), inkMapIds = PropertyIds("_InkMap");
        static int[] PropertyIds(string prefix)
        {
            var ids = new int[4];
            for (int i = 0; i < 4; i++)
                ids[i] = Shader.PropertyToID(prefix + i);
            return ids;
        }

        // Each output pixel is drawn once. Adjacent logical tiles are fetched explicitly
        // before filtering, including across GPU pages and missing transparent tiles.
        public void Render(RenderTexture target, Material material, ProjectiveMatrix sourceToView, FilterMode filter, bool clipFrame, Texture2D straightFallback, bool decodeFallback, RenderTexture denseOverride = null, float outputMode = 1)
        {
            CheckMutation();
            Validate();
            if (target == null || material == null || !sourceToView.TryInverse(out var inverse))
                throw new ArgumentException("Invalid tiled view target or transform.");
            var emptyTexture = Texture2D.blackTexture;
            ConfigureComposite(material);
            inverse.SetShader(material, "_ViewInverse");
            material.SetVector("_SourceSize", new Vector4(Frame.SourceWidth, Frame.SourceHeight, 0, 0));
            material.SetVector("_LogicalOffset", new Vector4(0, 0, 1f / Frame.SourceWidth, 1f / Frame.SourceHeight));
            material.SetFloat("_Point", filter == FilterMode.Point ? 1 : 0);
            material.SetFloat("_OutputMode", outputMode);
            material.SetFloat("_ClipFrame", clipFrame ? 1 : 0);
            material.SetTexture("_Fallback", (Texture)straightFallback ?? emptyTexture);
            material.SetFloat("_HasFallback", straightFallback != null ? 1 : 0);
            material.SetFloat("_FallbackGamma", decodeFallback ? 1 : 0);
            material.SetTexture("_DenseOverride", (Texture)denseOverride ?? emptyTexture);
            material.SetFloat("_UseDenseOverride", denseOverride != null ? 1 : 0);
            renderKeys.Clear();
            if (denseOverride != null)
                for (int y = 0; y < Frame.SourceHeight; y += 256)
                    for (int x = 0; x < Frame.SourceWidth; x += 256)
                        Include(new Vector2Int(x / 256, y / 256));
            foreach (var key in source.Tiles.Keys)
                Include(key);
            if (Pending)
                foreach (var key in dirty)
                    Include(key);
            bool firstTile = true;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                commands.Clear();
                commands.SetRenderTarget(target);
                commands.SetViewport(new Rect(0, 0, target.width, target.height));
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 1));
                // DrawProcedural copies the block: retain unchanged bindings only within this render.
                properties.Clear();
                foreach (var key in renderKeys)
                {
                    if (clipFrame && (key.x < 0 || key.y < 0 || key.x * 256 >= Frame.SourceWidth || key.y * 256 >= Frame.SourceHeight))
                        continue;
                    if (!ViewRect(key, sourceToView, target.width, target.height, filter != FilterMode.Point, out var rect))
                        continue;
                    properties.SetVector(tileKeyId, new Vector4(key.x, key.y, 0, 0));
                    for (int y = 0, i = 0; y <= 1; y++)
                        for (int x = 0; x <= 1; x++, i++)
                        {
                            var neighbor = key + new Vector2Int(x, y);
                            source.Tiles.TryGetValue(neighbor, out var version);
                            var origin = version?.Origin ?? Vector2Int.zero;
                            Bind(i, baseIds[i], baseMapIds[i], (Texture)version?.Page.Texture ?? emptyTexture,
                                new Vector4(origin.x, origin.y, version != null ? 1 : 0, 0));
                            TiledPaintTile tile = null;
                            if (Pending && dirty.Contains(neighbor))
                                slots.TryGetValue(neighbor, out tile);
                            origin = tile?.Origin ?? Vector2Int.zero;
                            Bind(i + 4, inkIds[i], inkMapIds[i], (Texture)tile?.Page.Texture ?? emptyTexture,
                                new Vector4(origin.x, origin.y, tile != null ? 1 : 0, 0));
                        }

                    firstTile = false;
                    commands.EnableScissorRect(rect);
                    commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, properties);
                }

                commands.DisableScissorRect();
                GL.sRGBWrite = false;
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    Graphics.ExecuteCommandBuffer(commands);
                }
                finally
                {
                    GL.PopMatrix();
                }
            }
            finally
            {
                commands.Clear();
                Array.Clear(renderTextures, 0, renderTextures.Length);
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }

            void Bind(int index, int textureId, int mapId, Texture texture, Vector4 map)
            {
                if (firstTile || !ReferenceEquals(renderTextures[index], texture))
                    properties.SetTexture(textureId, texture);
                if (firstTile || !renderMaps[index].Equals(map))
                    properties.SetVector(mapId, map);
                renderTextures[index] = texture;
                renderMaps[index] = map;
            }

            void Include(Vector2Int key)
            {
                renderKeys.Add(key);
                if (filter == FilterMode.Point)
                    return;
                renderKeys.Add(key + Vector2Int.left);
                renderKeys.Add(key + Vector2Int.down);
                renderKeys.Add(key + new Vector2Int(-1, -1));
            }
        }

        bool ViewRect(Vector2Int key, ProjectiveMatrix matrix, int width, int height, bool bilinear, out Rect rect)
        {
            double x = key.x * 256d / Frame.SourceWidth, y = key.y * 256d / Frame.SourceHeight;
            double right = x + 256d / Frame.SourceWidth, top = y + 256d / Frame.SourceHeight;
            if (bilinear)
            {
                x -= 1d / Frame.SourceWidth;
                y -= 1d / Frame.SourceHeight;
                right += 1d / Frame.SourceWidth;
                top += 1d / Frame.SourceHeight;
            }

            rect = new Rect(0, 0, width, height);
            if (!matrix.TryPoint(new Double2(x, y), out var a) || !matrix.TryPoint(new Double2(right, y), out var b) || !matrix.TryPoint(new Double2(x, top), out var c) || !matrix.TryPoint(new Double2(right, top), out var d))
                return true;
            // A projective pole inside the tile requires conservative full output coverage.
            double w0 = matrix.m20 * x + matrix.m21 * y + matrix.m22;
            if (w0 * (matrix.m20 * right + matrix.m21 * y + matrix.m22) <= 0 || w0 * (matrix.m20 * x + matrix.m21 * top + matrix.m22) <= 0 || w0 * (matrix.m20 * right + matrix.m21 * top + matrix.m22) <= 0)
                return true;
            float left = Mathf.Clamp(Mathf.FloorToInt((float)Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, d.x)) * width) - 1, 0, width);
            float bottom = Mathf.Clamp(Mathf.FloorToInt((float)Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, d.y)) * height) - 1, 0, height);
            float r = Mathf.Clamp(Mathf.CeilToInt((float)Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, d.x)) * width) + 1, 0, width);
            float t = Mathf.Clamp(Mathf.CeilToInt((float)Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, d.y)) * height) + 1, 0, height);
            rect = Rect.MinMaxRect(left, bottom, r, t);
            return r > left && t > bottom;
        }
    }
}
