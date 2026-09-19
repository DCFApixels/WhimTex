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
    /// The container is carried inside a regular image file, because Unity only applies
    /// TextureImporter settings (compression, mipmaps, platform overrides, sprite borders) to
    /// assets imported from an image file by extension:
    ///   * PNG — an ancillary chunk placed right before IEND;
    ///   * EXR — the payload appended after the last pixel chunk, located by a footer.
    /// Both carriers were verified to survive import, reimport and platform switching, and Unity
    /// never rewrites the source bytes.
    /// </summary>
    public sealed class WhimTexDocumentContainer
    {
        public const int CurrentVersion = 1;
        public const string DocumentBlock = "document";
        public const string PixelBlockPrefix = "pixels:";

        private const string PayloadMagic = "WHIMTEXD";
        private const string PngChunkType = "whTX";
        private const int MaximumBlockCount = 65536;
        private const int MaximumNameLength = 512;
        private const long MaximumTotalBytes = 8L * 1024 * 1024 * 1024;
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, byte[]> _blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, CompressionLevel> _levels = new Dictionary<string, CompressionLevel>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _cacheKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private byte[] _payload;

        // Pixel blocks are the dominant cost of a save, and every save builds a fresh container, so the
        // deflated result is kept outside the container. A block joins the cache through SetCompressed with
        // a key that describes its content: the same key means the same bytes and nothing is deflated again.
        private static readonly Dictionary<string, Compressed> CompressedCache = new Dictionary<string, Compressed>(StringComparer.Ordinal);
        private static readonly Queue<string> CompressedCacheOrder = new Queue<string>();
        private static long _compressedCacheBytes;
        private const long MaximumCompressedCacheBytes = 192L * 1024 * 1024;
        private const long ParallelDeflateBytes = 4L * 1024 * 1024;

        /// <summary>A deflated block. Incompressible blocks are stored as they are, so the flag travels with the bytes.</summary>
        private sealed class Compressed
        {
            public byte[] bytes;
            public bool compressed;
        }

        public int Count => _order.Count;
        public IReadOnlyList<string> Names => _order;
        public bool HasDocument => _order.Contains(DocumentBlock);

        public void Set(string name, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
        {
            if (data == null) throw new WhimTexDocumentException("Block '" + name + "' has no data.");
            ValidateName(name);
            if (!_blocks.ContainsKey(name) && !_entries.ContainsKey(name)) _order.Add(name);
            _entries.Remove(name);
            _blocks[name] = data;
            _levels[name] = level;
            _cacheKeys.Remove(name);
        }

        /// <summary>Stores block data whose compression is expensive, reusing the previous result while the key is unchanged.</summary>
        public void SetCompressed(string name, string cacheKey, byte[] data, CompressionLevel level = CompressionLevel.Fastest)
        {
            Set(name, data, level);
            if (!string.IsNullOrEmpty(cacheKey)) _cacheKeys[name] = cacheKey;
        }

        public bool Remove(string name)
        {
            bool existed = _order.Remove(name);
            _levels.Remove(name);
            _entries.Remove(name);
            _blocks.Remove(name);
            _cacheKeys.Remove(name);
            return existed;
        }

        public bool Contains(string name) => _blocks.ContainsKey(name) || _entries.ContainsKey(name);

        /// <summary>Returns a block, inflating it on first use: a document never holds every layer at once.</summary>
        public byte[] Get(string name)
        {
            if (_blocks.TryGetValue(name, out byte[] cached)) return cached;
            if (_entries.TryGetValue(name, out Entry entry))
            {
                Require(_payload, entry.dataOffset, entry.storedLength);
                byte[] data = entry.compression == 0
                    ? Slice(_payload, entry.dataOffset, (int)entry.storedLength)
                    : Decompress(_payload, entry.dataOffset, (int)entry.storedLength, entry.rawLength, name);
                _blocks[name] = data;
                return data;
            }
            throw new WhimTexDocumentException("Block '" + name + "' is missing.");
        }

        public bool TryGet(string name, out byte[] data)
        {
            if (_blocks.TryGetValue(name, out data)) return true;
            if (!_entries.ContainsKey(name)) { data = null; return false; }
            data = Get(name);
            return true;
        }

        public string PixelBlockName(string layerId) => PixelBlockPrefix + layerId;

        /// <summary>Serializes the container: a header with per-block sizes followed by the block payloads.</summary>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes(PayloadMagic));
                writer.Write(CurrentVersion);
                writer.Write(_order.Count);
                var raws = new byte[_order.Count][];
                var storedBlocks = new byte[_order.Count][];
                var compressedFlags = new bool[_order.Count];
                long totalBytes = 0;
                for (int i = 0; i < _order.Count; i++)
                {
                    raws[i] = Get(_order[i]);
                    totalBytes += raws[i].Length;
                }
                // Blocks are independent, so a layered document is deflated on all cores. Small documents
                // keep the plain loop: there the thread pool costs more than the work it takes over.
                if (_order.Count > 1 && totalBytes >= ParallelDeflateBytes)
                    System.Threading.Tasks.Parallel.For(0, _order.Count,
                        i => storedBlocks[i] = Stored(_order[i], raws[i], out compressedFlags[i]));
                else
                    for (int i = 0; i < _order.Count; i++)
                        storedBlocks[i] = Stored(_order[i], raws[i], out compressedFlags[i]);
                for (int i = 0; i < _order.Count; i++)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(_order[i]);
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);
                    writer.Write((int)(compressedFlags[i] ? 1 : 0));
                    writer.Write((long)raws[i].Length);
                    writer.Write((long)storedBlocks[i].Length);
                }
                foreach (byte[] stored in storedBlocks) writer.Write(stored);
            }
            return stream.ToArray();
        }

        /// <summary>Deflates a block, reusing a cached result for an unchanged cache key.</summary>
        private byte[] Stored(string name, byte[] raw, out bool compressed)
        {
            if (!_cacheKeys.TryGetValue(name, out string cacheKey) || cacheKey == null)
            {
                byte[] fresh = Compress(raw, _levels[name]);
                compressed = !ReferenceEquals(fresh, raw);
                return fresh;
            }
            lock (CompressedCache)
            {
                if (CompressedCache.TryGetValue(cacheKey, out Compressed cached))
                {
                    compressed = cached.compressed;
                    return cached.bytes;
                }
            }
            byte[] stored = Compress(raw, _levels[name]);
            var entry = new Compressed { bytes = stored, compressed = !ReferenceEquals(stored, raw) };
            compressed = entry.compressed;
            lock (CompressedCache)
            {
                if (!CompressedCache.ContainsKey(cacheKey))
                {
                    CompressedCache[cacheKey] = entry;
                    CompressedCacheOrder.Enqueue(cacheKey);
                    _compressedCacheBytes += entry.bytes.Length;
                    while (_compressedCacheBytes > MaximumCompressedCacheBytes && CompressedCacheOrder.Count > 0)
                    {
                        string oldest = CompressedCacheOrder.Dequeue();
                        if (CompressedCache.TryGetValue(oldest, out Compressed evicted))
                        {
                            _compressedCacheBytes -= evicted.bytes.Length;
                            CompressedCache.Remove(oldest);
                        }
                    }
                }
            }
            return stored;
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
                Require(payload, offset, 20);
                int compression = BitConverter.ToInt32(payload, offset); offset += 4;
                long rawLength = BitConverter.ToInt64(payload, offset); offset += 8;
                long storedLength = BitConverter.ToInt64(payload, offset); offset += 8;
                if (compression != 0 && compression != 1) throw new WhimTexDocumentException("Unknown compression " + compression + " for block '" + name + "'.");
                if (rawLength < 0 || storedLength < 0) throw new WhimTexDocumentException("Negative block size for '" + name + "'.");
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
            return result;
        }

        // --- carriers ---

        /// <summary>Wraps a PNG produced by Unity by inserting the document chunk before IEND.</summary>
        public byte[] WritePng(byte[] pngBytes)
        {
            if (!IsPng(pngBytes)) throw new WhimTexDocumentException("The image is not a PNG.");
            int iend = FindPngChunk(pngBytes, "IEND");
            if (iend < 0) throw new WhimTexDocumentException("The PNG has no IEND chunk.");
            byte[] payload = Serialize();
            byte[] chunk = BuildPngChunk(payload);
            var result = new byte[pngBytes.Length + chunk.Length];
            Buffer.BlockCopy(pngBytes, 0, result, 0, iend);
            Buffer.BlockCopy(chunk, 0, result, iend, chunk.Length);
            Buffer.BlockCopy(pngBytes, iend, result, iend + chunk.Length, pngBytes.Length - iend);
            return result;
        }

        /// <summary>Wraps an EXR produced by Unity. The footer keeps the document out of the pixel data Unity reads.</summary>
        public byte[] WriteExr(byte[] exrBytes)
        {
            if (!IsExr(exrBytes)) throw new WhimTexDocumentException("The image is not an EXR.");
            byte[] payload = Serialize();
            var result = new byte[exrBytes.Length + payload.Length + 16];
            Buffer.BlockCopy(exrBytes, 0, result, 0, exrBytes.Length);
            Buffer.BlockCopy(payload, 0, result, exrBytes.Length, payload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes((long)payload.Length), 0, result, exrBytes.Length + payload.Length, 8);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(PayloadMagic), 0, result, exrBytes.Length + payload.Length + 8, 8);
            return result;
        }

        public static bool IsPng(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length < PngSignature.Length) return false;
            for (int i = 0; i < PngSignature.Length; i++) if (fileBytes[i] != PngSignature[i]) return false;
            return true;
        }

        public static bool IsExr(byte[] fileBytes) =>
            fileBytes != null && fileBytes.Length > 8 && fileBytes[0] == 0x76 && fileBytes[1] == 0x2f && fileBytes[2] == 0x31 && fileBytes[3] == 0x01;

        /// <summary>Extracts the raw payload from a PNG or EXR carrier, or returns false when the file carries no document.</summary>
        public static bool TryReadPayload(byte[] fileBytes, out byte[] payload, out string error)
        {
            payload = null;
            error = null;
            if (IsPng(fileBytes))
            {
                int offset = FindPngChunk(fileBytes, PngChunkType);
                if (offset < 0) { error = "The PNG carries no WhimTex document."; return false; }
                int length = ReadInt32(fileBytes, offset);
                if (length < 0 || offset + 12 + length > fileBytes.Length) { error = "The document chunk is truncated."; return false; }
                uint stored = ReadUInt32(fileBytes, offset + 8 + length);
                uint actual = Crc32(fileBytes, offset + 4, 4 + length);
                if (stored != actual) { error = "The document chunk is corrupted."; return false; }
                payload = Slice(fileBytes, offset + 8, length);
                return true;
            }
            if (IsExr(fileBytes))
            {
                if (fileBytes.Length < 16 || Encoding.ASCII.GetString(fileBytes, fileBytes.Length - 8, 8) != PayloadMagic)
                { error = "The EXR carries no WhimTex document."; return false; }
                long length = BitConverter.ToInt64(fileBytes, fileBytes.Length - 16);
                if (length <= 0 || length > fileBytes.Length - 16) { error = "The document footer is invalid."; return false; }
                payload = Slice(fileBytes, (int)(fileBytes.Length - 16 - length), (int)length);
                return true;
            }
            error = "The file is neither a PNG nor an EXR.";
            return false;
        }

        /// <summary>Replaces the file contents without ever touching the sidecar .meta, which holds the import settings.</summary>
        public static void WriteFileAtomic(string path, byte[] bytes)
        {
            if (string.IsNullOrEmpty(path)) throw new WhimTexDocumentException("The document path is empty.");
            string full = Path.GetFullPath(path);
            string temporary = full + ".whimtex-tmp";
            try
            {
                string directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
            }
            catch (Exception error)
            {
                TryDelete(temporary);
                throw new WhimTexDocumentException("Could not write the document: " + error.Message, error);
            }
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
            if (payload == null) { error = "The document payload is missing."; return false; }
            if (payload.Length < 16) { error = "The document payload is truncated."; return false; }
            if (Encoding.ASCII.GetString(payload, 0, 8) != PayloadMagic)
            {
                error = "The file does not contain a WhimTex document.";
                return false;
            }
            int count = BitConverter.ToInt32(payload, 12);
            if (count < 0 || count > MaximumBlockCount) { error = "Invalid block count: " + count + "."; return false; }
            int offset = 16;
            for (int i = 0; i < count; i++)
            {
                Require(payload, offset, 4);
                int nameLength = BitConverter.ToInt32(payload, offset); offset += 4;
                if (nameLength <= 0 || nameLength > MaximumNameLength) { error = "Invalid block name length."; return false; }
                Require(payload, offset, nameLength);
                string blockName = Encoding.UTF8.GetString(payload, offset, nameLength); offset += nameLength;
                Require(payload, offset, 20);
                int compression = BitConverter.ToInt32(payload, offset); offset += 4;
                long rawLength = BitConverter.ToInt64(payload, offset); offset += 8;
                long storedLength = BitConverter.ToInt64(payload, offset); offset += 8;
                if (name != blockName)
                {
                    offset += (int)storedLength;
                    continue;
                }
                if (compression != 0 && compression != 1) { error = "Unknown compression for block '" + name + "'."; return false; }
                if (rawLength < 0 || storedLength < 0 || offset + storedLength > payload.Length)
                {
                    error = "Block '" + name + "' is out of bounds.";
                    return false;
                }
                data = compression == 0
                    ? Slice(payload, offset, (int)storedLength)
                    : Decompress(payload, offset, (int)storedLength, rawLength, name);
                return true;
            }
            error = "Block '" + name + "' is missing.";
            return false;
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
            if (name.Length > MaximumNameLength) throw new WhimTexDocumentException("The block name is too long.");
        }

        private static void Require(byte[] buffer, int offset, long length)
        {
            if (offset < 0 || length < 0 || offset + length > buffer.Length)
                throw new WhimTexDocumentException("The document payload is truncated.");
        }

        private static byte[] Slice(byte[] buffer, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(buffer, offset, result, 0, length);
            return result;
        }

        private static byte[] Compress(byte[] raw, CompressionLevel level)
        {
            using var stream = new MemoryStream();
            using (var deflate = new DeflateStream(stream, level, true)) deflate.Write(raw, 0, raw.Length);
            byte[] stored = stream.ToArray();
            return stored.Length < raw.Length ? stored : raw;
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
            return result;
        }

        private static int FindPngChunk(byte[] fileBytes, string type)
        {
            if (!IsPng(fileBytes)) return -1;
            int offset = 8;
            while (offset + 8 <= fileBytes.Length)
            {
                int length = ReadInt32(fileBytes, offset);
                if (length < 0 || offset + 12 + length > fileBytes.Length) return -1;
                if (ReadChunkType(fileBytes, offset) == type) return offset;
                offset += 12 + length;
            }
            return -1;
        }

        private static string ReadChunkType(byte[] fileBytes, int offset) =>
            new string(new[] { (char)fileBytes[offset + 4], (char)fileBytes[offset + 5], (char)fileBytes[offset + 6], (char)fileBytes[offset + 7] });

        private static byte[] BuildPngChunk(byte[] payload)
        {
            // PNG length and CRC fields are big-endian.
            var chunk = new byte[12 + payload.Length];
            WriteInt32BigEndian(chunk, 0, payload.Length);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(PngChunkType), 0, chunk, 4, 4);
            Buffer.BlockCopy(payload, 0, chunk, 8, payload.Length);
            WriteUInt32BigEndian(chunk, 8 + payload.Length, Crc32(chunk, 4, 4 + payload.Length));
            return chunk;
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static int ReadInt32(byte[] buffer, int offset) =>
            buffer[offset] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3];

        private static uint ReadUInt32(byte[] buffer, int offset) =>
            (uint)(buffer[offset] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3]);

        private static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[offset + i];
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
