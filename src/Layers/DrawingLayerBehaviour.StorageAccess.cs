using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal bool MayHavePixelsOutsideSourceFrame
        {
            get
            {
                if (!HasTiledSource) return false;
                if (tiledSurface?.Pending == true) return true;
                foreach (var tile in TiledPixels)
                {
                    long x = (long)tile.key.x * 256, y = (long)tile.key.y * 256;
                    if (x < 0 || y < 0 || x + 256 > TiledSourceWidth || y + 256 > TiledSourceHeight) return true;
                }
                return false;
            }
        }

        // Dense editing consumers operate in the original source frame. Merge
        // only intersecting overlays on CPU; outside tiles remain in the source.
        internal NativeArray<Color> ReadSourcePixels(Allocator allocator)
        {
            SyncPendingSurfaceToTexture();
            var original = StoredTexture;
            var result = HdrUtility.ReadPixels(original, allocator);
            try
            {
                foreach (var tile in TiledPixels)
                {
                    int left = tile.key.x * 256, bottom = tile.key.y * 256;
                    int x0 = Mathf.Max(0, left), y0 = Mathf.Max(0, bottom);
                    int x1 = Mathf.Min(original.width, left + 256), y1 = Mathf.Min(original.height, bottom + 256);
                    if (x0 >= x1 || y0 >= y1) continue;
                    var values = tile.texture.GetRawTextureData<half4>();
                    for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int index = y * original.width + x;
                        float4 value = values[(y - bottom) * 256 + x - left];
                        Color color = result[index];
                        if (value.w > .00001f)
                        {
                            color.r = value.x / value.w;
                            color.g = value.y / value.w;
                            color.b = value.z / value.w;
                        }
                        color.a = value.w;
                        result[index] = HdrUtility.Safe(color);
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        // Ownership/byte-accounting consumers must include sparse overlays, not just
        // the original source frame. Synchronization remains an explicit caller step.
        internal IEnumerable<Texture2D> StoredPixelTextures
        {
            get
            {
                var original = StoredTexture;
                if (original != null) yield return original;
                if (!HasTiledPixels) yield break;
                foreach (var tile in TiledPixels)
                    if (tile?.texture != null) yield return tile.texture;
            }
        }

        internal long StoredPixelCount
        {
            get
            {
                long count = 0;
                foreach (var texture in StoredPixelTextures)
                    count = checked(count + (long)texture.width * texture.height);
                return count;
            }
        }

        internal long GetStoredPixelCount(int fallbackWidth, int fallbackHeight)
        {
            long count = StoredPixelCount;
            return count != 0 || HasTiledSource ? count : (long)fallbackWidth * fallbackHeight;
        }

        internal void ValidateTiledPixels()
        {
            if (HasTiledSource && (tiledSourceWidth < 0 || tiledSourceHeight < 0 ||
                TiledSourceWidth < 1 || TiledSourceHeight < 1 || TiledSourceWidth > 16384 || TiledSourceHeight > 16384))
                throw new WhimTexDocumentException("Drawing source frame dimensions must be between 1 and 16384.");
            if (!HasTiledPixels) return;
            if (!HasTiledSource)
                throw new WhimTexDocumentException("Drawing tile data requires its tiled source flag.");
            const int tileSize = 256, coordinateLimit = 1048576;
            if (TiledPixels.Count > WhimTexDocumentLimits.TotalTextureBytes / (tileSize * tileSize * 8L))
                throw new WhimTexDocumentException("Drawing tile data exceeds the document pixel budget.");
            var keys = new HashSet<Vector2Int>();
            var textures = new HashSet<UnityObjectID>();
            if (pixels != null) textures.Add(UnityObjectID.FromObject(pixels));
            foreach (var tile in TiledPixels)
            {
                if (tile == null || !keys.Add(tile.key))
                    throw new WhimTexDocumentException("Drawing tile data contains a missing or duplicate tile.");
                long x = (long)tile.key.x * tileSize, y = (long)tile.key.y * tileSize;
                if (x < -coordinateLimit || y < -coordinateLimit || x + tileSize > coordinateLimit || y + tileSize > coordinateLimit)
                    throw new WhimTexDocumentException("Drawing tile coordinates exceed the supported range.");
                var texture = tile.texture;
                if (texture == null || texture.width != tileSize || texture.height != tileSize ||
                    texture.format != TextureFormat.RGBAHalf || texture.isDataSRGB || texture.mipmapCount != 1 || !texture.isReadable)
                    throw new WhimTexDocumentException("Drawing tiles require readable 256 x 256 linear RGBAHalf textures without mipmaps.");
                if (!textures.Add(UnityObjectID.FromObject(texture)))
                    throw new WhimTexDocumentException("Each Drawing tile must own a separate texture, independent of the original source.");
            }
        }
    }
}
