using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DCFApixels.WhimTex
{
    /// <summary>Raised when a WhimTex document file cannot be read or written safely.</summary>
    public sealed class WhimTexDocumentException : Exception
    {
        public WhimTexDocumentException(string message) : base(message) { }
        public WhimTexDocumentException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Byte-level container of a WhimTex document. The document itself is a set of named blocks:
    /// the serialized model plus one block per drawing layer.
    ///
    /// The writer appends this container to a native TIFF image; regular image export is separate.
    /// </summary>
    public sealed class WhimTexDocumentContainer : IDisposable
    {
        public const int CurrentVersion = 1;
        public const string DocumentBlock = "document";

        private const string PayloadMagic = "WHIMTEXD";
        private const int MaximumBlockCount = 65536;
        private const int MaximumNameLength = 512;
        private const long MaximumTotalBytes = 8L * 1024 * 1024 * 1024;

        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, byte[]> _blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, CompressionLevel> _levels = new Dictionary<string, CompressionLevel>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _cacheKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Compressed> _reused = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Compressed> _prepared = new(StringComparer.Ordinal);
        private readonly HashSet<string> _exposed = new(StringComparer.Ordinal);
        private byte[] _payload;
        private Stream _source;
        private bool _leaveSourceOpen;
        private long _sourceStart;
        private const string IntegrityBlock = "integrity:sha256";
        private Dictionary<string, byte[]> _integrity;

        // Texture pixels are the bulk of a document, so they are kept in native memory: copying hundreds of
        // megabytes into the managed heap on every save is what the garbage collector would have to collect.
        // A container owns what it was given and frees it in Dispose.
        private readonly Dictionary<string, Unity.Collections.NativeArray<byte>> _native = new Dictionary<string, Unity.Collections.NativeArray<byte>>(StringComparer.Ordinal);
        private readonly List<Unity.Collections.NativeArray<byte>> _owned = new List<Unity.Collections.NativeArray<byte>>();

        // Pixel blocks are the dominant cost of a save, and every save builds a fresh container, so the
        // stored result is kept outside the container. Keys select candidates; exact raw comparison
        // proves equality before reuse. Immutable results include their already calculated SHA-256.
        private static readonly Dictionary<string, Compressed> CompressedCache = new Dictionary<string, Compressed>(StringComparer.Ordinal);
        private static readonly LinkedList<string> CompressedCacheOrder = new();
        private static readonly Dictionary<string, LinkedListNode<string>> CompressedCacheNodes = new(StringComparer.Ordinal);
        private static long _compressedCacheBytes;
        // Fits four 4K RGBA32 blocks without an unbounded per-document/history cache.
        private const long MaximumCompressedCacheBytes = 256L * 1024 * 1024;
        private const long ParallelDeflateBytes = 4L * 1024 * 1024;

        /// <summary>A deflated block. Incompressible blocks are stored as they are, so the flag travels with the bytes.</summary>
        private sealed class Compressed
        {
            public readonly byte[] bytes, digest;
            public readonly bool compressed;
            public readonly long rawLength;

            public Compressed(byte[] ownedBytes, bool compressed, long rawLength)
            {
                bytes = ownedBytes;
                this.compressed = compressed;
                this.rawLength = rawLength;
                digest = Digest(bytes);
            }
        }

        public int Count => _order.Count;
        public IReadOnlyList<string> Names => _order;
        public bool HasDocument => _order.Contains(DocumentBlock);
        internal bool HasExternalInputs { get; set; }
        internal bool ReusePixelCache { get; set; } = true;

        internal long LengthOf(string name)
        {
            if (_blocks.TryGetValue(name, out byte[] bytes)) return bytes.LongLength;
            if (_native.TryGetValue(name, out var native)) return native.Length;
            if (_reused.TryGetValue(name, out var reused)) return reused.rawLength;
            if (_entries.TryGetValue(name, out Entry entry)) return entry.rawLength;
            throw new WhimTexDocumentException("Missing block '" + name + "'.");
        }

        internal string ContentSignature()
        {
            PrepareStoredBlocks();
            using var hash = System.Security.Cryptography.SHA256.Create();
            foreach (string name in _order)
            {
                byte[] label = Encoding.UTF8.GetBytes(name + "\0");
                hash.TransformBlock(label, 0, label.Length, null, 0);
                // Fast cache fingerprints are only lookup hints, never proof of equality.
                // The save signature uses SHA-256 of the exact immutable stored blocks.
                Compressed block = _prepared[name];
                byte[] metadata = BitConverter.GetBytes(block.rawLength);
                hash.TransformBlock(metadata, 0, metadata.Length, null, 0);
                byte[] encoding = { (byte)(block.compressed ? 1 : 0) };
                hash.TransformBlock(encoding, 0, encoding.Length, null, 0);
                hash.TransformBlock(block.digest, 0, block.digest.Length, null, 0);
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToBase64String(hash.Hash);
        }

        public void Set(string name, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (data == null) throw new WhimTexDocumentException("Block '" + name + "' has no data.");
            ValidateName(name);
            PrepareMutation();
            if (!Contains(name)) _order.Add(name);
            _entries.Remove(name);
            _reused.Remove(name);
            _prepared.Remove(name);
            _exposed.Remove(name);
            _blocks[name] = (byte[])data.Clone();
            _native.Remove(name);
            _levels[name] = level;
            _cacheKeys.Remove(name);
        }

        /// <summary>Stores block data whose compression is expensive, reusing the previous result while the key is unchanged.</summary>
        public void SetCompressed(string name, string cacheKey, byte[] data, CompressionLevel level = CompressionLevel.Fastest)
        {
            Set(name, data, level);
            if (!string.IsNullOrEmpty(cacheKey)) _cacheKeys[name] = cacheKey;
        }

        /// <summary>Stores a block that already lives in native memory, taking ownership of it.</summary>
        public void SetNative(string name, string cacheKey, Unity.Collections.NativeArray<byte> data,
            CompressionLevel level = CompressionLevel.Fastest)
        {
            if (!data.IsCreated) throw new WhimTexDocumentException("Block '" + name + "' has no data.");
            ValidateName(name);
            PrepareMutation();
            if (!Contains(name)) _order.Add(name);
            _entries.Remove(name);
            _reused.Remove(name);
            _prepared.Remove(name);
            _exposed.Remove(name);
            _blocks.Remove(name);
            _native[name] = data;
            _owned.Add(data);
            _levels[name] = level;
            _cacheKeys.Remove(name);
            if (!string.IsNullOrEmpty(cacheKey)) _cacheKeys[name] = cacheKey;
        }

        // A native Hash128 is a cheap candidate lookup. Exact comparison also detects raw CPU
        // edits without Apply, Undo, and even a hash collision; no reliance on UI dirty flags.
        internal bool TryReusePixels(string name, string cacheKey, Unity.Collections.NativeArray<byte> pixels)
        {
            Compressed cached = FindCached(cacheKey);
            if (cached == null || !Matches(cached, pixels.AsSpan())) return false;
            ValidateName(name);
            PrepareMutation();
            if (!Contains(name)) _order.Add(name);
            _blocks.Remove(name); _native.Remove(name); _entries.Remove(name);
            _prepared.Remove(name);
            _exposed.Remove(name);
            _reused[name] = cached;
            _cacheKeys[name] = cacheKey;
            _levels[name] = CompressionLevel.Fastest;
            return true;
        }

        /// <summary>Frees the native blocks the container owns.</summary>
        public void Dispose()
        {
            foreach (Unity.Collections.NativeArray<byte> block in _owned)
                if (block.IsCreated) block.Dispose();
            _owned.Clear();
            _native.Clear();
            _levels.Clear();
            _cacheKeys.Clear();
            _reused.Clear();
            _prepared.Clear();
            _exposed.Clear();
            _entries.Clear();
            _blocks.Clear();
            _order.Clear();
            _payload = null;
            if (!_leaveSourceOpen) _source?.Dispose();
            _source = null;
            _integrity = null;
        }

        public bool Remove(string name)
        {
            PrepareMutation();
            _native.Remove(name);
            _reused.Remove(name);
            _prepared.Remove(name);
            _exposed.Remove(name);
            bool existed = _order.Remove(name);
            _levels.Remove(name);
            _entries.Remove(name);
            _blocks.Remove(name);
            _cacheKeys.Remove(name);
            return existed;
        }

        private void PrepareMutation()
        {
            // Pin the original manifest before changing its directory. Otherwise a valid
            // add/remove on a parsed container would look like an incomplete manifest.
            if (_integrity != null || !Contains(IntegrityBlock)) return;
            foreach (string name in _order)
                if (name != IntegrityBlock && _entries.ContainsKey(name)) { Get(name); break; }
        }

        public bool Contains(string name) => _blocks.ContainsKey(name) || _native.ContainsKey(name) || _entries.ContainsKey(name) || _reused.ContainsKey(name);

        /// <summary>Returns a block, inflating it on first use: a document never holds every layer at once.</summary>
        public byte[] Get(string name)
        {
            // Get exposes mutable raw bytes, never the immutable global cache entry.
            _prepared.Remove(name);
            _exposed.Add(name);
            if (_blocks.TryGetValue(name, out byte[] cached)) return cached;
            if (_reused.TryGetValue(name, out Compressed reused))
            {
                var data = reused.compressed ? Decompress(reused.bytes, 0, reused.bytes.Length, reused.rawLength, name) : (byte[])reused.bytes.Clone();
                _blocks[name] = data;
                _reused.Remove(name);
                return data;
            }
            // A native block becomes managed bytes only when a caller asks for them: the save path deflates
            // it where it already lies.
            if (_native.TryGetValue(name, out Unity.Collections.NativeArray<byte> native) && native.IsCreated)
            {
                byte[] copy = native.ToArray();
                _blocks[name] = copy;
                _native.Remove(name);
                return copy;
            }
            if (_entries.TryGetValue(name, out Entry entry))
            {
                byte[] payload = _payload;
                int offset = entry.dataOffset;
                if (_source != null)
                {
                    _source.Position = _sourceStart + entry.dataOffset;
                    payload = new byte[(int)entry.storedLength];
                    ReadExactly(_source, payload);
                    offset = 0;
                }
                Require(payload, offset, entry.storedLength);
                VerifyStored(name, payload.AsSpan(offset, (int)entry.storedLength));
                byte[] data = entry.compression == 0
                    ? (_source != null ? payload : Slice(payload, offset, (int)entry.storedLength))
                    : Decompress(payload, offset, (int)entry.storedLength, entry.rawLength, name);
                _blocks[name] = data;
                return data;
            }
            throw new WhimTexDocumentException("Block '" + name + "' is missing.");
        }

        public bool TryGet(string name, out byte[] data)
        {
            if (!Contains(name)) { data = null; return false; }
            data = Get(name);
            return true;
        }


        /// <summary>Serializes the container: a header with per-block sizes followed by the block payloads.</summary>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            WriteTo(stream);
            return stream.ToArray();
        }

        internal void WriteTo(Stream stream)
        {
            // Verify a parsed input before removing/rebuilding its integrity manifest.
            if (Contains(IntegrityBlock))
                foreach (string name in _order) Get(name);
            Remove(IntegrityBlock);
            if (_order.Count >= MaximumBlockCount) throw new WhimTexDocumentException("Too many container blocks.");
            PrepareStoredBlocks();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes(PayloadMagic));
                writer.Write(CurrentVersion);
                writer.Write(_order.Count + 1);
                using var integrity = new MemoryStream();
                using var checksums = new BinaryWriter(integrity, Encoding.UTF8, true);
                checksums.Write(_order.Count);
                for (int i = 0; i < _order.Count; i++)
                {
                    Compressed block = _prepared[_order[i]];
                    byte[] nameBytes = Encoding.UTF8.GetBytes(_order[i]);
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);
                    writer.Write(block.compressed ? 1 : 0);
                    writer.Write(block.rawLength);
                    writer.Write((long)block.bytes.Length);
                    checksums.Write(_order[i]);
                    checksums.Write(block.digest);
                }
                byte[] manifest = integrity.ToArray();
                byte[] manifestName = Encoding.UTF8.GetBytes(IntegrityBlock);
                writer.Write(manifestName.Length); writer.Write(manifestName);
                writer.Write(0); writer.Write((long)manifest.Length); writer.Write((long)manifest.Length);
                for (int i = 0; i < _order.Count; i++)
                    writer.Write(_prepared[_order[i]].bytes);
                writer.Write(manifest);
            }
        }

        internal void PrepareStoredBlocks()
        {
            var names = new List<string>();
            var raws = new List<byte[]>();
            var natives = new List<Unity.Collections.NativeArray<byte>>();
            long total = 0, largest = 1, pending = 0;
            foreach (string name in _order)
            {
                long length = LengthOf(name);
                total += length;
                if (total > MaximumTotalBytes) throw new WhimTexDocumentException("Document is too large.");
                if (_prepared.ContainsKey(name) && !_exposed.Contains(name)) continue;
                if (_reused.TryGetValue(name, out Compressed reused)) { _prepared[name] = reused; continue; }
                names.Add(name);
                if (_native.TryGetValue(name, out var native)) { natives.Add(native); raws.Add(null); }
                else { natives.Add(default); raws.Add(_blocks.TryGetValue(name, out byte[] raw) ? raw : Get(name)); }
                largest = Math.Max(largest, length);
                pending += length;
            }
            var results = new Compressed[names.Count];
            void Prepare(int i) => results[i] = Stored(names[i], raws[i], natives[i]);
            // Compression AND SHA-256 run together, not a parallel deflate followed by serial hashing.
            // Bound simultaneous input to ~256 MiB and at most four jobs; buffers/results cost extra.
            int workers = (int)Math.Max(1, Math.Min(4, Math.Min(Environment.ProcessorCount, 256L * 1024 * 1024 / largest)));
            if (names.Count > 1 && pending >= ParallelDeflateBytes && workers > 1)
                System.Threading.Tasks.Parallel.For(0, names.Count,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = workers }, Prepare);
            else for (int i = 0; i < names.Count; i++) Prepare(i);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                _prepared[name] = results[i];
                // The immutable stored block now owns everything needed by Save. Do not keep
                // hundreds of MiB of raw snapshots alive during Compose/TIFF encoding as well.
                if (_native.TryGetValue(name, out var native))
                {
                    _native.Remove(name);
                    _reused[name] = results[i];
                    _owned.Remove(native);
                    native.Dispose();
                }
            }
        }

        private static byte[] Digest(ReadOnlySpan<byte> data)
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            hash.AppendData(data);
            return hash.GetHashAndReset();
        }

        private void VerifyStored(string name, ReadOnlySpan<byte> stored)
        {
            if (name == IntegrityBlock || !Contains(IntegrityBlock)) return;
            if (_integrity == null)
            {
                if (LengthOf(IntegrityBlock) > 8 * 1024 * 1024) throw new WhimTexDocumentException("Invalid integrity manifest.");
                using var stream = new MemoryStream(Get(IntegrityBlock), false);
                using var reader = new BinaryReader(stream, Encoding.UTF8);
                int count = reader.ReadInt32();
                if (count != _order.Count - 1) throw new WhimTexDocumentException("Incomplete integrity manifest.");
                var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string block = reader.ReadString();
                    if (block == IntegrityBlock || !Contains(block) || block.Length > MaximumNameLength || hashes.ContainsKey(block))
                        throw new WhimTexDocumentException("Invalid integrity entry.");
                    byte[] digest = reader.ReadBytes(32);
                    if (digest.Length != 32) throw new WhimTexDocumentException("Truncated integrity entry.");
                    hashes.Add(block, digest);
                }
                if (stream.Position != stream.Length) throw new WhimTexDocumentException("Unexpected integrity data.");
                _integrity = hashes;
            }
            if (!_integrity.TryGetValue(name, out byte[] expected) || !Digest(stored).AsSpan().SequenceEqual(expected))
                throw new WhimTexDocumentException("Checksum failed for document block '" + name + "'.");
        }

        internal static WhimTexDocumentContainer Open(Stream stream, long start, long length)
        {
            if (start < 0 || length < 16 || length > int.MaxValue || start > stream.Length - length)
                throw new WhimTexDocumentException("Invalid document extent or document exceeds the 2 GiB container limit.");
            stream.Position = start;
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != PayloadMagic || reader.ReadInt32() != CurrentVersion)
                throw new WhimTexDocumentException("Unsupported document container.");
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumBlockCount) throw new WhimTexDocumentException("Invalid block count.");
            var result = new WhimTexDocumentContainer();
            long total = 0, storedTotal = 0;
            for (int i = 0; i < count; i++)
            {
                int nameLength = reader.ReadInt32();
                if (nameLength <= 0 || nameLength > MaximumNameLength || stream.Position > start + length - nameLength - 20)
                    throw new WhimTexDocumentException("Invalid block directory.");
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                int compression = reader.ReadInt32();
                long raw = reader.ReadInt64(), stored = reader.ReadInt64();
                if (result._entries.ContainsKey(name) || compression < 0 || compression > 1 || raw < 0 || raw > int.MaxValue ||
                    stored < 0 || stored > int.MaxValue || compression == 0 && raw != stored)
                    throw new WhimTexDocumentException("Invalid block metadata.");
                total += raw; storedTotal += stored;
                if (total > MaximumTotalBytes || storedTotal > length) throw new WhimTexDocumentException("Document exceeds safety limits.");
                result._order.Add(name);
                result._entries.Add(name, new Entry { name = name, compression = compression, rawLength = raw, storedLength = stored });
                result._levels[name] = CompressionLevel.Optimal;
            }
            if (stream.Position + storedTotal != start + length) throw new WhimTexDocumentException("Invalid document payload size.");
            int position = checked((int)(stream.Position - start));
            foreach (string name in result._order)
            {
                Entry entry = result._entries[name];
                entry.dataOffset = position;
                result._entries[name] = entry;
                position = checked(position + (int)entry.storedLength);
            }
            result._source = stream;
            result._sourceStart = start;
            return result;
        }

        private static void ReadExactly(Stream stream, byte[] data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read == 0) throw new WhimTexDocumentException("Truncated document block.");
                offset += read;
            }
        }

        private static Compressed FindCached(string key)
        {
            if (key == null) return null;
            lock (CompressedCache)
            {
                if (!CompressedCache.TryGetValue(key, out Compressed cached)) return null;
                LinkedListNode<string> node = CompressedCacheNodes[key];
                CompressedCacheOrder.Remove(node);
                CompressedCacheOrder.AddLast(node);
                return cached;
            }
        }

        private static bool Matches(Compressed candidate, ReadOnlySpan<byte> raw)
        {
            if (candidate.rawLength != raw.Length) return false;
            if (!candidate.compressed) return candidate.bytes.AsSpan().SequenceEqual(raw);
            // A lookup hash collision must not replace pixels with another layer's data.
            // Verify equality without allocating a full decoded layer or trusting updateCount/Undo.
            using var source = new MemoryStream(candidate.bytes, false);
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            byte[] scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                int offset = 0;
                while (offset < raw.Length)
                {
                    int count = inflate.Read(scratch, 0, Math.Min(65536, raw.Length - offset));
                    if (count == 0 || !scratch.AsSpan(0, count).SequenceEqual(raw.Slice(offset, count))) return false;
                    offset += count;
                }
                return inflate.ReadByte() == -1;
            }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(scratch); }
        }

        private Compressed Stored(string name, byte[] raw, Unity.Collections.NativeArray<byte> native)
        {
            ReadOnlySpan<byte> pixels = raw != null ? raw.AsSpan() : native.AsSpan();
            _cacheKeys.TryGetValue(name, out string cacheKey);
            Compressed cached = FindCached(cacheKey);
            if (cached != null && Matches(cached, pixels)) return cached;
            byte[] stored = Deflate(pixels, _levels[name], out bool compressed);
            var entry = new Compressed(stored, compressed, pixels.Length);
            if (cacheKey == null || stored.Length > MaximumCompressedCacheBytes) return entry;
            lock (CompressedCache)
            {
                if (CompressedCache.TryGetValue(cacheKey, out Compressed old))
                {
                    _compressedCacheBytes -= old.bytes.Length;
                    CompressedCacheOrder.Remove(CompressedCacheNodes[cacheKey]);
                }
                CompressedCache[cacheKey] = entry;
                CompressedCacheNodes[cacheKey] = CompressedCacheOrder.AddLast(cacheKey);
                _compressedCacheBytes += stored.Length;
                while ((_compressedCacheBytes > MaximumCompressedCacheBytes || CompressedCache.Count > 1024) && CompressedCacheOrder.Count > 0)
                {
                    string oldest = CompressedCacheOrder.First.Value;
                    CompressedCacheOrder.RemoveFirst();
                    _compressedCacheBytes -= CompressedCache[oldest].bytes.Length;
                    CompressedCache.Remove(oldest);
                    CompressedCacheNodes.Remove(oldest);
                }
            }
            return entry;
        }

        // Always return owned bytes, including incompressible native blocks. This lets the cache
        // retain the decision not to compress and never exposes a caller's mutable array as cached data.
        private static byte[] Deflate(ReadOnlySpan<byte> raw, CompressionLevel level, out bool compressed)
        {
            using var stream = new MemoryStream();
            using (var deflate = new DeflateStream(stream, level, true))
                deflate.Write(raw);
            compressed = stream.Length < raw.Length;
            return compressed ? stream.ToArray() : raw.ToArray();
        }

        /// <summary>Reads a container. Block payloads are inflated on demand, so opening a document does not touch every layer.</summary>
        public static WhimTexDocumentContainer Parse(byte[] payload)
        {
            if (payload == null) throw new WhimTexDocumentException("The document payload is missing.");
            if (payload.Length < 16) throw new WhimTexDocumentException("The document payload is truncated.");
            if (Encoding.ASCII.GetString(payload, 0, 8) != PayloadMagic)
                throw new WhimTexDocumentException("The file does not contain a WhimTex document.");
            int version = BitConverter.ToInt32(payload, 8);
            if (version <= 0 || version > CurrentVersion)
                throw new WhimTexDocumentException("Unsupported document version: " + version + ".");
            int count = BitConverter.ToInt32(payload, 12);
            if (count < 0 || count > MaximumBlockCount) throw new WhimTexDocumentException("Invalid block count: " + count + ".");

            var entries = new List<Entry>(count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            int offset = 16;
            for (int i = 0; i < count; i++)
            {
                Require(payload, offset, 4);
                int nameLength = BitConverter.ToInt32(payload, offset); offset += 4;
                if (nameLength <= 0 || nameLength > MaximumNameLength)
                    throw new WhimTexDocumentException("Invalid block name length: " + nameLength + ".");
                Require(payload, offset, nameLength);
                string name = Encoding.UTF8.GetString(payload, offset, nameLength); offset += nameLength;
                if (!names.Add(name)) throw new WhimTexDocumentException("Duplicate block '" + name + "'.");
                Require(payload, offset, 20);
                int compression = BitConverter.ToInt32(payload, offset); offset += 4;
                long rawLength = BitConverter.ToInt64(payload, offset); offset += 8;
                long storedLength = BitConverter.ToInt64(payload, offset); offset += 8;
                if (compression != 0 && compression != 1) throw new WhimTexDocumentException("Unknown compression " + compression + " for block '" + name + "'.");
                if (rawLength < 0 || rawLength > int.MaxValue || storedLength < 0 || storedLength > int.MaxValue ||
                    compression == 0 && rawLength != storedLength)
                    throw new WhimTexDocumentException("Invalid block size for '" + name + "'.");
                total += rawLength;
                if (total > MaximumTotalBytes) throw new WhimTexDocumentException("The document declares more than " + MaximumTotalBytes + " bytes of data.");
                entries.Add(new Entry { name = name, compression = compression, rawLength = rawLength, storedLength = storedLength });
            }
            var result = new WhimTexDocumentContainer();
            result._payload = payload;
            foreach (Entry entry in entries)
            {
                Require(payload, offset, entry.storedLength);
                Entry stored = entry;
                stored.dataOffset = offset;
                result._order.Add(stored.name);
                result._entries[stored.name] = stored;
                result._levels[stored.name] = CompressionLevel.Optimal;
                offset += (int)stored.storedLength;
                // Blocks stay compressed until someone asks for them, so opening a document does not
                // inflate every drawing layer and does not hold pixels and textures in memory together.
            }
            if (offset != payload.Length) throw new WhimTexDocumentException("Unexpected trailing container data.");
            return result;
        }



        internal static bool WriteStaged(string path, Action<Stream> write)
        {
            string full = Path.GetFullPath(path);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".whimtex-tmp";
            var baseline = new FileInfo(full);
            bool existed = baseline.Exists;
            long previousLength = existed ? baseline.Length : 0;
            DateTime previousWrite = existed ? baseline.LastWriteTimeUtc : default;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    write(stream);
                    if (stream.Length > 4L * 1024 * 1024 * 1024) throw new WhimTexDocumentException("Document exceeds the 4 GiB file limit.");
                    stream.Flush(true);
                }
                baseline.Refresh();
                if (baseline.Exists != existed || existed && (baseline.Length != previousLength || baseline.LastWriteTimeUtc != previousWrite))
                    throw new WhimTexDocumentException("The destination changed during save; it was not overwritten.");
                if (File.Exists(full) && FilesEqual(full, temporary)) { File.Delete(temporary); return false; }
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
                return true;
            }
            catch (Exception error)
            {
                TryDelete(temporary);
                throw new WhimTexDocumentException("Could not write the document: " + error.Message, error);
            }
        }

        private static bool FilesEqual(string first, string second)
        {
            using var a = File.OpenRead(first);
            using var b = File.OpenRead(second);
            if (a.Length != b.Length) return false;
            var left = new byte[65536]; var right = new byte[left.Length];
            long remaining = a.Length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(left.Length, remaining);
                int offset = 0;
                while (offset < count) { int n = a.Read(left, offset, count - offset); if (n == 0) return false; offset += n; }
                offset = 0;
                while (offset < count) { int n = b.Read(right, offset, count - offset); if (n == 0) return false; offset += n; }
                if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
                remaining -= count;
            }
            return true;
        }

        public static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Reads one block without inflating the rest of the container. The import pipeline uses this to
        /// fetch a few bytes of carrier flags from a document whose model and pixels it must not touch.
        /// </summary>
        public static bool TryReadBlock(byte[] payload, string name, out byte[] data, out string error)
        {
            data = null;
            error = null;
            try
            {
                using var container = Parse(payload);
                if (container.TryGet(name, out data)) return true;
                error = "Block '" + name + "' is missing.";
            }
            catch (Exception exception) when (exception is IOException || exception is WhimTexDocumentException)
            { error = exception.Message; }
            return false;
        }

        // The directory precedes ALL payloads. Scan it once, then seek directly to the small
        // requested block; imports must not allocate/read the image or Drawing pixels.
        internal static byte[] ReadSmallBlock(Stream stream, long start, long length, string name)
        {
            using var container = Open(stream, start, length);
            container._leaveSourceOpen = true;
            if (container.LengthOf(name) > 4096 || container._entries[name].storedLength > 4096)
                throw new WhimTexDocumentException("Small block exceeds the 4 KiB budget.");
            return container.Get(name);
        }

        // --- internals ---

        private struct Entry
        {
            public string name;
            public int compression;
            public long rawLength;
            public long storedLength;
            public int dataOffset;
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new WhimTexDocumentException("A block name cannot be empty.");
            if (Encoding.UTF8.GetByteCount(name) > MaximumNameLength) throw new WhimTexDocumentException("The block name is too long.");
        }

        private static void Require(byte[] buffer, int offset, long length)
        {
            if (offset < 0 || offset > buffer.Length || length < 0 || length > buffer.Length - offset)
                throw new WhimTexDocumentException("The document payload is truncated.");
        }

        private static byte[] Slice(byte[] buffer, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(buffer, offset, result, 0, length);
            return result;
        }

        private static byte[] Decompress(byte[] buffer, int offset, int storedLength, long rawLength, string name)
        {
            var result = new byte[rawLength];
            using var stream = new MemoryStream(buffer, offset, storedLength, false);
            using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
            int read = 0;
            while (read < result.Length)
            {
                int step = deflate.Read(result, read, result.Length - read);
                if (step <= 0) throw new WhimTexDocumentException("Block '" + name + "' ended before its declared size.");
                read += step;
            }
            if (deflate.ReadByte() != -1) throw new WhimTexDocumentException("Block '" + name + "' exceeds its declared size.");
            return result;
        }

    }
}
