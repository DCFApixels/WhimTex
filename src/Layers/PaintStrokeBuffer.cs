using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Only touched source blocks own storage. Absence means no stroke, not transparent source data.
    internal sealed class PaintStrokeBuffer : IDisposable
    {
        internal const int TileSize = PaintTileSet.Size;
        internal const long TileBytes = TileSize * TileSize * 8L;
        private readonly int PageSize, TilesPerRow, TilesPerPage;
        private long PageBytes => PageSize * PageSize * 8L;
        internal const long PoolLimitBytes = 64L * 1024 * 1024;
        private static readonly Stack<RenderTexture>[] idle = { new Stack<RenderTexture>(), new Stack<RenderTexture>(), new Stack<RenderTexture>() };
        internal static long PooledBytes { get; private set; }
        internal static long ActiveBytes { get; private set; }
        internal long Bytes => pages.Count * PageBytes;
        internal long TouchedBytes => touched.Count * TileBytes;
        internal readonly int width, height;
        internal readonly PaintTileSet touched = new PaintTileSet();
        private readonly int[] slots;
        private readonly List<RenderTexture> pages = new List<RenderTexture>();
        private readonly RenderTexture source;
        private bool backdrop, pressureOpacity;
        private int Columns => (width + TileSize - 1) / TileSize;
        internal static readonly int MapId = Shader.PropertyToID("_BrushStrokeMap");
        internal static readonly int StrokeId = Shader.PropertyToID("_BrushStroke");

        static PaintStrokeBuffer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearPool;
            EditorApplication.quitting += ClearPool;
        }

        internal PaintStrokeBuffer(RenderTexture source)
        {
            this.source = source; width = source.width; height = source.height;
            PageSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), TileSize, 1024);
            TilesPerRow = PageSize / TileSize; TilesPerPage = TilesPerRow * TilesPerRow;
            slots = new int[Columns * ((height + TileSize - 1) / TileSize)];
            Array.Fill(slots, -1);
            touched.Reset(width, height);
        }

        internal void Reset(bool withBackdrop, bool pressure)
        {
            Dispose(); backdrop = withBackdrop; pressureOpacity = pressure;
        }

        private int Index(RectInt tile) => tile.y / TileSize * Columns + tile.x / TileSize;
        internal RenderTexture Get(RectInt tile)
        {
            int slot = slots[Index(tile)];
            return slot < 0 ? null : pages[slot / TilesPerPage];
        }
        internal RectInt Slot(RectInt tile)
        {
            int slot = slots[Index(tile)] % TilesPerPage;
            return new RectInt(slot % TilesPerRow * TileSize, slot / TilesPerRow * TileSize, TileSize, TileSize);
        }
        internal Vector4 Map(RectInt tile)
        {
            var slot = Slot(tile);
            return new Vector4(width / (float)PageSize, height / (float)PageSize,
                (slot.x - tile.x) / (float)PageSize, (slot.y - tile.y) / (float)PageSize);
        }

        internal RenderTexture Ensure(RectInt tile)
        {
            int index = Index(tile);
            if (slots[index] >= 0) return Get(tile);
            using var profile = WhimTexProfiler.Measure("brush.tile_allocate");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                int slotIndex = touched.Count;
                if (slotIndex % TilesPerPage == 0)
                {
                    var page = Rent(PageSize);
                    pages.Add(page);
                    // Every page is private to this stroke until returned. Clear once,
                    // not once per stamp or logical tile, including on pool reuse.
                    RenderTexture.active = page; GL.Clear(false, true, Color.clear);
                }
                int slot = slotIndex % TilesPerPage;
                var texture = pages[slotIndex / TilesPerPage];
                if (backdrop)
                {
                    var copy = WhimTexMaterials.Blend;
                    try
                    {
                        DrawingLayerBehaviour.DrawBrushRegion(source, texture, copy, pressureOpacity ? 2 : 3,
                            new Rect(slot % TilesPerRow * TileSize / (float)PageSize, slot / TilesPerRow * TileSize / (float)PageSize,
                                TileSize / (float)PageSize, TileSize / (float)PageSize),
                            new Rect(tile.x / (float)width, tile.y / (float)height, TileSize / (float)width, TileSize / (float)height));
                    }
                    finally { copy.SetTexture("_MainTex", null); }
                }
                slots[index] = slotIndex; touched.Include(tile);
                return texture;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        internal void Bind(Material material, RectInt tile)
        {
            var texture = Get(tile);
            material.SetFloat("_PendingBrush", texture != null ? 1 : 0);
            material.SetTexture(StrokeId, texture);
            material.SetVector(MapId, Map(tile));
        }

        public void Dispose()
        {
            for (int i = 0; i < touched.Count; i++) slots[Index(touched[i])] = -1;
            foreach (var page in pages) Return(page);
            pages.Clear(); touched.Clear();
        }

        private static int Bucket(int size) => size <= 256 ? 0 : size <= 512 ? 1 : 2;

        private static RenderTexture Rent(int size)
        {
            RenderTexture result;
            var pool = idle[Bucket(size)];
            long bytes = size * (long)size * 8;
            if (pool.Count > 0) { result = pool.Pop(); PooledBytes -= bytes; }
            else
            {
                result = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "WhimTex stroke tile page", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false
                };
                if (!result.Create()) { UnityEngine.Object.DestroyImmediate(result); throw new InvalidOperationException("Cannot allocate stroke tile."); }
            }
            ActiveBytes += bytes;
            return result;
        }

        private static void Return(RenderTexture texture)
        {
            if (texture == null) return;
            long bytes = texture.width * (long)texture.height * 8;
            ActiveBytes -= bytes;
            if (PooledBytes + bytes <= PoolLimitBytes) { idle[Bucket(texture.width)].Push(texture); PooledBytes += bytes; }
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        internal static void ClearPool()
        {
            foreach (var pool in idle) while (pool.Count > 0) UnityEngine.Object.DestroyImmediate(pool.Pop());
            PooledBytes = 0;
        }
    }
}
