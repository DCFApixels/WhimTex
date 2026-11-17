using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex.TiledPaint
{
    // A pin protects a whole pending page. Committed versions own individual slots.
    // Releasing a pin after its last recorded read permits ordered reuse in that
    // command buffer; destruction is deferred to an explicit safe-point trim.
    internal sealed class TiledPageAllocation
    {
        internal readonly TiledPagePool Owner;
        internal RenderTexture Texture;
        internal ushort UsedBits;
        internal bool PendingPin;
        internal bool Registered;
        internal bool Cleared;
        internal readonly int PageSize, SlotsPerPage;
        internal bool Idle => !PendingPin && UsedBits == 0;
        internal TiledPageAllocation(TiledPagePool owner, RenderTexture texture)
        {
            Owner = owner; Texture = texture; PageSize = owner.PageSize; SlotsPerPage = owner.SlotsPerPage; Cleared = true;
        }
        internal void Validate()
        {
            Owner.CheckThread();
            if (Texture == null || !Texture.IsCreated()) throw new TiledPaintResourceLostException();
        }
    }

    internal sealed class TiledVersion
    {
        internal readonly TiledPageAllocation Page;
        internal readonly int Index;
        private int references = 1;
        internal Vector2Int Origin => new Vector2Int(Index % (Page.PageSize / 256) * 256, Index / (Page.PageSize / 256) * 256);
        internal TiledVersion(TiledPageAllocation page, int index) { Page = page; Index = index; }
        internal void Validate()
        {
            Page.Owner.CheckThread();
            if (references == 0) throw new ObjectDisposedException(nameof(TiledVersion));
            if ((Page.UsedBits & (1 << Index)) == 0) throw new InvalidOperationException("The tile no longer owns its storage slot.");
            Page.Validate();
        }
        internal void Retain() { Validate(); references = checked(references + 1); }
        internal void Release()
        {
            Page.Owner.CheckThread();
            if (references == 0) throw new ObjectDisposedException(nameof(TiledVersion));
            if (--references == 0) Page.Owner.ReleaseSlot(Page, Index);
        }
    }

    internal sealed class TiledPagePool : IDisposable
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly List<TiledPageAllocation> pages = new List<TiledPageAllocation>();
        private readonly long budget, idleBudget, pageBytes;
        private readonly bool randomWrite;
        private long externalBytes;
        private bool disposed;
        internal int PageSize { get; }
        internal int SlotsPerPage { get; }
        internal long AllocatedBytes { get { CheckThread(); return checked(pages.Count * pageBytes + externalBytes); } }
        internal long IdleBytes
        {
            get
            {
                CheckThread(); long bytes = 0;
                foreach (var page in pages) if (page.Idle) bytes += pageBytes;
                return bytes;
            }
        }

        internal TiledPagePool(int pageSize, TiledPaintOptions options)
        {
            if (pageSize < 256 || pageSize > 1024 || (pageSize & (pageSize - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            var settings = (options ?? throw new ArgumentNullException(nameof(options))).Copy();
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("Half-float render targets are required for tiled paint.");
            PageSize = pageSize; SlotsPerPage = (pageSize / 256) * (pageSize / 256);
            pageBytes = checked((long)pageSize * pageSize * 8);
            budget = settings.GpuBudgetBytes; idleBudget = settings.IdleBudgetBytes;
            randomWrite = settings.ComputeCommit && SystemInfo.supportsComputeShaders &&
                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        }

        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Tiled paint resources must be used on their owning Unity thread.");
        }
        private void CheckActive()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(TiledPagePool));
        }
        internal void Validate()
        {
            CheckActive();
            foreach (var page in pages) if (!page.Idle) page.Validate();
        }
        internal bool OwnsTexture(Texture texture) => FindAllocation(texture) != null;
        internal TiledPageAllocation FindAllocation(Texture texture)
        {
            CheckThread();
            if (ReferenceEquals(texture, null)) return null;
            // Views expose these exact managed resources, not rediscovered asset IDs.
            foreach (var page in pages) if (ReferenceEquals(page.Texture, texture)) return page;
            return null;
        }
        internal void ReserveExternal(long bytes)
        {
            CheckActive();
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            CheckCapacity(bytes);
            externalBytes += bytes;
        }
        internal void ReleaseExternal(long bytes)
        {
            CheckThread();
            if (bytes < 0 || bytes > externalBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
            externalBytes -= bytes;
        }
        private void CheckCapacity(long bytes)
        {
            if (bytes > budget - AllocatedBytes) throw new TiledPaintMemoryLimitException();
        }

        // Preparation is transactional: failed allocation discards only pages
        // created by this call, leaving retained pixels and existing capacity intact.
        internal void Prepare(int freePageCount)
        {
            CheckActive();
            if (freePageCount < 0) throw new ArgumentOutOfRangeException(nameof(freePageCount));
            int idle = 0;
            foreach (var page in pages)
            {
                if (!page.Idle) continue;
                EnsureIdleCreated(page);
                if (!page.Cleared) { ClearTexture(page.Texture); page.Cleared = true; }
                idle++;
            }
            int missing = Math.Max(0, freePageCount - idle);
            CheckCapacity(checked((long)missing * pageBytes));
            int originalCount = pages.Count;
            try { for (int i = 0; i < missing; i++) AddPage(); }
            catch
            {
                while (pages.Count > originalCount) DestroyPage(pages[pages.Count - 1]);
                throw;
            }
        }
        internal TiledPageAllocation RentPending()
        {
            CheckActive();
            foreach (var page in pages)
            {
                if (!page.Idle) continue;
                EnsureIdleCreated(page); page.PendingPin = true; return page;
            }
            var created = AddPage(); created.PendingPin = true; return created;
        }
        internal void ReleasePending(TiledPageAllocation page)
        {
            CheckThread(); CheckOwned(page);
            if (!page.PendingPin) throw new InvalidOperationException("The pending page has already been released.");
            page.PendingPin = false;
            if (disposed && page.Idle) DestroyPage(page);
        }
        internal TiledVersion RentVersion(TiledPageAllocation excludedPage)
        {
            CheckActive();
            if (excludedPage != null) CheckOwned(excludedPage);
            int full = (1 << SlotsPerPage) - 1;
            foreach (var page in pages)
            {
                if (page == excludedPage || page.PendingPin || page.UsedBits == full) continue;
                if (page.Idle) EnsureIdleCreated(page);
                // Lost historical versions stay invalid until released. They do not
                // block allocation for a replacement source or unrelated new paint.
                else if (page.Texture == null || !page.Texture.IsCreated()) continue;
                return AcquireSlot(page);
            }
            return AcquireSlot(AddPage());
        }
        private static TiledVersion AcquireSlot(TiledPageAllocation page)
        {
            int index = 0;
            while ((page.UsedBits & (1 << index)) != 0) index++;
            // Construct before publishing occupancy, including managed allocation failure.
            var version = new TiledVersion(page, index);
            page.Cleared = false;
            page.UsedBits |= (ushort)(1 << index);
            return version;
        }
        internal void ReleaseSlot(TiledPageAllocation page, int index)
        {
            CheckThread(); CheckOwned(page);
            if (index < 0 || index >= SlotsPerPage || (page.UsedBits & (1 << index)) == 0)
                throw new InvalidOperationException("The tile storage slot has already been released.");
            page.UsedBits = (ushort)(page.UsedBits & ~(1 << index));
            if (disposed && page.Idle) DestroyPage(page);
        }
        private void CheckOwned(TiledPageAllocation page)
        {
            if (page == null || page.Owner != this || !page.Registered)
                throw new ArgumentException("The page does not belong to this pool.");
        }

        // Call only after recorded commands have been submitted. Never trim while
        // a command buffer still references a consumed but not yet submitted page.
        internal void TrimIdle(bool all = false)
        {
            CheckActive();
            long remaining = IdleBytes, limit = all ? 0 : idleBudget;
            for (int i = pages.Count - 1; i >= 0 && remaining > limit; i--)
                if (pages[i].Idle) { DestroyPage(pages[i]); remaining -= pageBytes; }
        }
        private TiledPageAllocation AddPage()
        {
            CheckCapacity(pageBytes);
            var texture = CreateTexture();
            try
            {
                var page = new TiledPageAllocation(this, texture);
                pages.Add(page);
                page.Registered = true;
                return page;
            }
            catch { DestroyTexture(texture); throw; }
        }
        private void EnsureIdleCreated(TiledPageAllocation page)
        {
            if (page.Texture != null && page.Texture.IsCreated()) return;
            var replacement = CreateTexture();
            var previous = page.Texture; page.Texture = replacement;
            page.Cleared = true;
            DestroyTexture(previous);
        }
        private static void ClearTexture(RenderTexture texture)
        {
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try { RenderTexture.active = texture; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear); }
            finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; }
        }
        private RenderTexture CreateTexture()
        {
            RenderTexture texture = null;
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                texture = new RenderTexture(PageSize, PageSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "WhimTex tiled paint page", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                    enableRandomWrite = randomWrite, useMipMap = false, autoGenerateMips = false, antiAliasing = 1
                };
                if (!texture.Create()) throw new InvalidOperationException("Unable to allocate a tiled paint GPU page.");
                RenderTexture.active = texture; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear);
                return texture;
            }
            catch { DestroyTexture(texture); throw; }
            finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; }
        }
        private void DestroyPage(TiledPageAllocation page)
        {
            pages.Remove(page);
            page.Registered = false;
            var texture = page.Texture; page.Texture = null;
            DestroyTexture(texture);
        }
        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture != null) Object.DestroyImmediate(texture);
        }
        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            // Pending ownership belongs to the source. Version ownership can
            // outlive it through an externally retained snapshot or undo record.
            for (int i = pages.Count - 1; i >= 0; i--)
            {
                pages[i].PendingPin = false;
                if (pages[i].Idle) DestroyPage(pages[i]);
            }
        }
    }

    internal sealed class TiledSnapshotState
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private int references = 1;
        internal readonly TiledPaintFrame Frame;
        internal readonly object OwnerToken;
        internal readonly ReadOnlyDictionary<Vector2Int, TiledVersion> Tiles;
        // The caller retains its input versions. This state owns a separate
        // reference to every map entry and never exposes its mutable dictionary.
        internal TiledSnapshotState(TiledPaintFrame frame, IDictionary<Vector2Int, TiledVersion> tiles, object ownerToken = null)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            OwnerToken = ownerToken;
            var copy = new Dictionary<Vector2Int, TiledVersion>(tiles ?? throw new ArgumentNullException(nameof(tiles)));
            Tiles = new ReadOnlyDictionary<Vector2Int, TiledVersion>(copy);
            int retained = 0;
            try
            {
                foreach (var version in copy.Values)
                {
                    if (version == null) throw new ArgumentException("A snapshot contains a null tile.");
                    version.Retain(); retained++;
                }
            }
            catch
            {
                foreach (var version in copy.Values) { if (retained-- == 0) break; version.Release(); }
                throw;
            }
        }
        internal void CheckAlive()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Tiled paint snapshots must be used on their owning Unity thread.");
            if (references == 0) throw new ObjectDisposedException(nameof(TiledSnapshotState));
        }
        internal void Validate()
        {
            CheckAlive();
            foreach (var version in Tiles.Values) version.Validate();
        }
        internal void Retain() { CheckAlive(); references = checked(references + 1); }
        internal void Release()
        {
            CheckAlive();
            if (--references == 0) foreach (var version in Tiles.Values) version.Release();
        }
    }

    // GPU textures in tile views are borrowed, read-only resources. Keep this
    // lease alive until rendering finishes; never release or modify those textures.
    public sealed class TiledPaintSnapshot : IDisposable
    {
        private TiledSnapshotState state;
        internal TiledPaintSnapshot(TiledSnapshotState value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            value.Retain(); state = value;
        }
        internal TiledSnapshotState State { get { CheckAlive(); return state; } }
        public TiledPaintFrame Frame { get { CheckAlive(); return state.Frame; } }
        public int Count { get { CheckAlive(); return state.Tiles.Count; } }
        public IEnumerable<TiledPaintTileView> Tiles { get { CheckAlive(); return EnumerateTiles(); } }
        private void CheckAlive()
        {
            if (state == null) throw new ObjectDisposedException(nameof(TiledPaintSnapshot));
            state.CheckAlive();
        }
        private IEnumerable<TiledPaintTileView> EnumerateTiles()
        {
            CheckAlive();
            foreach (var pair in state.Tiles)
            {
                CheckAlive(); pair.Value.Validate();
                yield return new TiledPaintTileView(pair.Key, pair.Value);
            }
        }
        public bool TryGetTile(Vector2Int key, out TiledPaintTileView view)
        {
            CheckAlive();
            if (!state.Tiles.TryGetValue(key, out var version)) { view = default; return false; }
            version.Validate(); view = new TiledPaintTileView(key, version); return true;
        }
        public void Dispose()
        {
            if (state == null) return;
            state.CheckAlive(); var previous = state; state = null; previous.Release();
        }
    }
}
