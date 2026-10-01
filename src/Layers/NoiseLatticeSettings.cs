using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // CPU-prepared lattice bases. Each of eight octaves and the warp has its own
    // integer period; non-integral lacunarity therefore cannot break the tile.
    internal sealed class NoiseLatticeSettings
    {
        private const int Stride = 16;
        private readonly Vector4[] data = new Vector4[9 * Stride];
        private static readonly int DataId = Shader.PropertyToID("_NoiseLattice");
        private static readonly int InverseId = Shader.PropertyToID("_NoiseWarpInverse");

        internal void Apply(Material material, NoiseLayerBehaviour noise, double width, double height)
        {
            double frequency = 1;
            bool three = noise.EffectiveDimensions == NoiseLayerBehaviour.NoiseDimensions.ThreeD;
            for (int octave = 0; octave < 9; octave++)
            {
                bool warp = octave == 8;
                double f = warp ? 1 : frequency;
                frequency *= NoiseLayerBehaviour.Limit(noise.lacunarity, 1, 4, 2);
                int count = noise.fractal == NoiseLayerBehaviour.FractalType.None ? 1 : Mathf.Clamp(noise.octaves, 1, 8);
                if (!warp && octave >= count) continue;
                bool simplex = warp ? noise.warp != NoiseLayerBehaviour.WarpType.BasicGrid : (int)noise.noiseType < 2;
                int layout = simplex ? (three ? 3 : 2) : 1;
                double unitX = simplex ? (three ? 3 : Math.Sqrt(2.0 / 3.0)) : 1;
                double unitY = simplex ? (three ? 3 : Math.Sqrt(2)) : 1;
                bool repeatX = ((int)noise.EffectivePeriodic & 1) != 0;
                bool repeatY = ((int)noise.EffectivePeriodic & 2) != 0;
                // The triangular 2D lattice rounds the base cell count first.
                double cellsX = simplex && !three ? Math.Max(1, Math.Round(width / unitX)) * f : width * f / unitX;
                double cellsY = simplex && !three ? Math.Max(1, Math.Round(height / unitY)) * f : height * f / unitY;
                double countX = Math.Max(1, Math.Round(cellsX, MidpointRounding.AwayFromZero));
                double countY = Math.Max(1, Math.Round(cellsY, MidpointRounding.AwayFromZero));
                double sx = repeatX ? countX * unitX / width : f;
                double sy = repeatY ? countY * unitY / height : f;
                double px = repeatX ? countX * (simplex && three ? 3 : 1) : 0;
                double py = repeatY ? countY * (simplex && three ? 3 : 1) : 0;
                // Protect integer lattice arithmetic on extremely elongated canvases.
                if (Math.Max(width * sx, height * sy) > 100000000 || Math.Max(px, py) > 100000000)
                    throw new InvalidOperationException("Noise lattice is too large. Reduce Scale, Octaves, Lacunarity or canvas aspect ratio.");
                int start = octave * Stride;
                void Basis(double x, double y, double z, int slot)
                {
                    Transform(ref x, ref y, ref z, layout, sx, sy, f);
                    if (slot == 5)
                    {
                        var integers = new Vector4();
                        var tails = new Vector4();
                        for (int axis = 0; axis < 3; axis++)
                        {
                            double value = axis == 0 ? x : axis == 1 ? y : z;
                            if (Math.Abs(value) > 500000000)
                                throw new InvalidOperationException("Noise coordinates are too large. Reduce Offset, Scale or fractal detail.");
                            int whole = (int)Math.Floor(value);
                            integers[axis] = whole >> 12; tails[axis] = whole & 4095;
                            if (axis == 0) x -= whole; else if (axis == 1) y -= whole; else z -= whole;
                        }
                        data[start + 13] = integers; data[start + 14] = tails;
                    }
                    Split(start + slot * 2, x, y, z);
                }
                Basis(width, 0, 0, 0);
                Basis(0, height, 0, 1);
                Basis(1, 0, 0, 2);
                Basis(0, 1, 0, 3);
                Basis(0, 0, 1, 4);
                double ox = NoiseLayerBehaviour.Limit(noise.offset.x, -10000, 10000, 0);
                double oy = NoiseLayerBehaviour.Limit(noise.offset.y, -10000, 10000, 0);
                if (repeatX) ox %= width;
                if (repeatY) oy %= height;
                Basis(-width * .5 + ox, -height * .5 + oy,
                    three ? NoiseLayerBehaviour.Limit(noise.offset.z, -10000, 10000, 0) : 0, 5);
                // Normal floats only: subnormal bit-casts can flush to zero in dynamic GPU loads.
                int ix = (int)px, iy = (int)py;
                data[start + 12] = new Vector4(ix >> 12, iy >> 12, layout, (ix & 4095) | ((iy & 4095) << 12));
                if (warp) material.SetVector(InverseId, new Vector4((float)(1 / sx), (float)(1 / sy), 1, layout));
            }
            material.SetVectorArray(DataId, data);
        }

        private void Split(int index, double x, double y, double z)
        {
            var high = new Vector4((float)x, (float)y, (float)z, 0);
            data[index] = high;
            data[index + 1] = new Vector4((float)(x - high.x), (float)(y - high.y), (float)(z - high.z), 0);
        }

        private static void Transform(ref double x, ref double y, ref double z, int layout, double sx, double sy, double sz)
        {
            x *= sx; y *= sy; z *= sz;
            if (layout == 2)
            {
                double a = (x - y) / Math.Sqrt(2), b = (x + y) / Math.Sqrt(2);
                double skew = (a + b) * ((Math.Sqrt(3) - 1) * .5);
                x = a + skew; y = b + skew;
            }
            else if (layout == 3)
            {
                double r = (x + y + z) * (2.0 / 3.0);
                x = r - x; y = r - y; z = r - z;
            }
        }
    }
}
