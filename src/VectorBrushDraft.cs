using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>The figure's editable source mask, owned and serialized by its vector layer.</summary>
    [Serializable]
    public sealed class VectorBrushDraft
    {
        public const int MaxDimension = 1024;
        public const int MaxPixels = 1048576;
        public const float MinTolerance = .25f, MaxTolerance = 64f, MaxExpansion = 256f;

        public int width, height;
        // Pixel centers are (x + .5, y + .5), row zero is the bottom row.
        // Treat this as immutable after Finish; changing controls never rewrites it.
        public byte[] coverage;
        public float tolerance = 1f;
        public float expansion;
        public Rect frame = new Rect(0, 0, 1, 1);
        public string shapeId = Guid.NewGuid().ToString("N");

        public void Validate()
        {
            CheckSize(width, height);
            if (!VectorGeometry.Finite(frame.min) || !VectorGeometry.Finite(frame.size) || frame.width <= 0 || frame.height <= 0)
                throw new ArgumentException("Vector brush frame must be finite and positive.");
            if (coverage == null || coverage.Length != width * height)
                throw new ArgumentException("Vector brush coverage must match the mask dimensions.");
            if (string.IsNullOrWhiteSpace(shapeId) || shapeId.Length > 128)
                throw new ArgumentException("Vector brush draft requires a shape ID of at most 128 characters.");
            if (!Finite(tolerance) || tolerance < MinTolerance || tolerance > MaxTolerance)
                throw new ArgumentOutOfRangeException(nameof(tolerance), "Vector brush tolerance must be 0.25 to 64 reference pixels.");
            if (!Finite(expansion) || Mathf.Abs(expansion) > MaxExpansion)
                throw new ArgumentOutOfRangeException(nameof(expansion), "Vector brush expansion must be -256 to 256 reference pixels.");
        }

        public VectorBrushDraft DeepClone()
        {
            return new VectorBrushDraft { width = width, height = height,
                coverage = coverage == null ? null : (byte[])coverage.Clone(),
                tolerance = tolerance, expansion = expansion, shapeId = shapeId, frame = frame };
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static void CheckSize(int width, int height)
        {
            if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension
                || (long)width * height > MaxPixels)
                throw new ArgumentOutOfRangeException(nameof(width), "Vector brush mask must fit within 1024 by 1024 pixels.");
        }
    }
}
