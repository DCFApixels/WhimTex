using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class GaussianKernel
    {
        internal const int Capacity = 128;
        private static readonly Vector4[] Buffer = new Vector4[Capacity];

        internal static void Set(Material material, float sigma, float support)
        {
            int extent = Mathf.Clamp(Mathf.CeilToInt(support), 1, Capacity * 2);
            double divisor = 2d * Math.Max(.0001d, sigma * sigma);
            double total = 1d;
            int count = 0;
            for (int i = 1; i <= extent; i += 2)
            {
                double a = Math.Exp(-(double)i * i / divisor);
                double b = i + 1 <= extent ? Math.Exp(-(double)(i + 1) * (i + 1) / divisor) : 0d;
                double weight = a + b;
                Buffer[count++] = new Vector4((float)(i + (weight > 0d ? b / weight : 0d)), (float)weight, 0f, 0f);
                total += 2d * weight;
            }
            for (int i = 0; i < count; i++) Buffer[i].y /= (float)total;
            Upload(material, (float)(1d / total), count, Buffer);
        }

        internal static void Upload(Material material, float centerWeight, int pairCount, Vector4[] buffer)
        {
            // Unity fixes material array capacity on its first assignment, even for a five-pair brush.
            if (buffer == null || buffer.Length != Capacity)
                throw new ArgumentException("The Gaussian kernel buffer must contain 128 elements.", nameof(buffer));
            if (pairCount < 0 || pairCount > Capacity) throw new ArgumentOutOfRangeException(nameof(pairCount));
            material.SetFloat("_CenterWeight", centerWeight);
            material.SetInt("_PairCount", pairCount);
            material.SetVectorArray("_Kernel", buffer);
        }
    }
}
