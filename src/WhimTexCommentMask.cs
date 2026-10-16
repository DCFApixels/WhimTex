using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>An immutable coverage snapshot, placed in bottom-left canvas UV coordinates.</summary>
    [Serializable]
    public sealed class WhimTexCommentMask
    {
        public int version = 1;
        public string id;
        public int width, height;
        public string coverageDeflate, sha256;
        public ProjectiveMatrix localToCanvas = ProjectiveMatrix.Identity;

        [NonSerialized] private string validatedData, validatedHash;
        [NonSerialized] private int validatedWidth, validatedHeight;
        internal const int MaximumPixels = 16777216;

        public static WhimTexCommentMask Capture(byte[] coverage, int canvasWidth, int canvasHeight)
        {
            CheckSize(canvasWidth, canvasHeight);
            if (coverage == null || coverage.Length != canvasWidth * canvasHeight) throw Error("Coverage dimensions do not match.");
            RectInt bounds = CoverageBounds(coverage, canvasWidth, canvasHeight);
            if (bounds.width == 0) throw Error("Select a nonempty area first.");
            var cropped = new byte[bounds.width * bounds.height];
            for (int y = 0; y < bounds.height; y++)
                Buffer.BlockCopy(coverage, (bounds.y + y) * canvasWidth + bounds.x, cropped, y * bounds.width, bounds.width);
            using var stream = new MemoryStream();
            using (var zip = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Optimal, true)) zip.Write(cropped, 0, cropped.Length);
            return new WhimTexCommentMask
            {
                id = Guid.NewGuid().ToString("N"), width = bounds.width, height = bounds.height,
                coverageDeflate = Convert.ToBase64String(stream.ToArray()), sha256 = Hash(cropped),
                localToCanvas = ProjectiveMatrix.Translate((double)bounds.x / canvasWidth, (double)bounds.y / canvasHeight) *
                    ProjectiveMatrix.Scale((double)bounds.width / canvasWidth, (double)bounds.height / canvasHeight)
            };
        }

        public WhimTexCommentMask Copy(bool newId = false) => new WhimTexCommentMask
        {
            version = version, id = newId ? Guid.NewGuid().ToString("N") : id, width = width, height = height,
            coverageDeflate = coverageDeflate, sha256 = sha256, localToCanvas = localToCanvas,
            validatedData = validatedData, validatedHash = validatedHash, validatedWidth = validatedWidth, validatedHeight = validatedHeight
        };

        internal void Validate()
        {
            CheckSize(width, height);
            if (version != 1) throw Error("Unsupported mask version: " + version);
            if (!Guid.TryParseExact(id, "N", out _)) throw Error("Mask ID must be a GUID.");
            ValidatePlacement(localToCanvas);
            if (string.IsNullOrEmpty(coverageDeflate) || coverageDeflate.Length > WhimTexDocument.MaximumCommentsBlockBytes ||
                sha256 == null || sha256.Length != 44) throw Error("Invalid compressed coverage or checksum.");
            if (validatedData == coverageDeflate && validatedHash == sha256 && validatedWidth == width && validatedHeight == height) return;
            Decode();
        }

        internal static void ValidatePlacement(ProjectiveMatrix matrix)
        {
            // Canvas tools use affine maps. Reject perspective and singular inputs at the storage boundary.
            if (!matrix.IsFinite || matrix.m20 != 0 || matrix.m21 != 0 || matrix.m22 != 1 || !matrix.TryInverse(out _))
                throw Error("Mask placement must be an invertible affine matrix.");
            for (int y = 0; y <= 1; y++) for (int x = 0; x <= 1; x++)
            {
                var p = matrix.Point(new Double2(x, y));
                if (Math.Abs(p.x) > 1e9 || Math.Abs(p.y) > 1e9) throw Error("Mask placement exceeds the supported range.");
            }
        }

        public byte[] Decode()
        {
            CheckSize(width, height);
            if (string.IsNullOrEmpty(coverageDeflate) || coverageDeflate.Length > WhimTexDocument.MaximumCommentsBlockBytes)
                throw Error("Invalid compressed coverage length.");
            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(coverageDeflate), false);
                using var zip = new DeflateStream(stream, CompressionMode.Decompress);
                var data = new byte[width * height];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = zip.Read(data, offset, data.Length - offset);
                    if (read == 0) throw Error("Truncated mask coverage.");
                    offset += read;
                }
                if (zip.ReadByte() != -1 || Hash(data) != sha256) throw Error("Mask coverage length or checksum does not match.");
                if (CoverageBounds(data, width, height) != new RectInt(0, 0, width, height))
                    throw Error("Mask must be nonempty and cropped to its nonzero bounds.");
                validatedData = coverageDeflate; validatedHash = sha256; validatedWidth = width; validatedHeight = height;
                return data;
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidDataException || ex is IOException)
            { throw Error("Invalid compressed coverage: " + ex.Message); }
        }

        /// <summary>Sample the original snapshot once, at current canvas pixel centres. No accumulated resampling.</summary>
        public byte[] Rasterize(int canvasWidth, int canvasHeight)
        {
            Validate(); CheckSize(canvasWidth, canvasHeight);
            byte[] source = Decode(), result = new byte[canvasWidth * canvasHeight];
            localToCanvas.TryInverse(out var inverse);
            RectInt bounds = CanvasBounds(canvasWidth, canvasHeight);
            for (int y = bounds.yMin; y < bounds.yMax; y++) for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                var p = inverse.Point(new Double2((x + .5) / canvasWidth, (y + .5) / canvasHeight));
                if (p.x >= 0 && p.x < 1 && p.y >= 0 && p.y < 1)
                    result[y * canvasWidth + x] = source[(int)(p.y * height) * width + (int)(p.x * width)];
            }
            return result;
        }

        public RectInt CanvasBounds(int canvasWidth, int canvasHeight)
        {
            double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
            for (int y = 0; y <= 1; y++) for (int x = 0; x <= 1; x++)
            {
                var p = localToCanvas.Point(new Double2(x, y));
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x); minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            int left = (int)Math.Floor(Math.Clamp(minX * canvasWidth, 0, canvasWidth)),
                bottom = (int)Math.Floor(Math.Clamp(minY * canvasHeight, 0, canvasHeight));
            return new RectInt(left, bottom, (int)Math.Ceiling(Math.Clamp(maxX * canvasWidth, 0, canvasWidth)) - left,
                (int)Math.Ceiling(Math.Clamp(maxY * canvasHeight, 0, canvasHeight)) - bottom);
        }

        internal static RectInt CoverageBounds(byte[] data, int width, int height)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0, i = 0; y < height; y++) for (int x = 0; x < width; x++, i++) if (data[i] != 0)
            { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            return maxX < 0 ? default : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static void CheckSize(int width, int height)
        {
            if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > MaximumPixels)
                throw Error("Mask dimensions must be 1..16384 and at most 16,777,216 pixels.");
        }
        private static string Hash(byte[] data) { using var hash = SHA256.Create(); return Convert.ToBase64String(hash.ComputeHash(data)); }
        private static WhimTexDocumentException Error(string message) => new WhimTexDocumentException("Comment mask: " + message);
    }
}
