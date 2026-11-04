using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class NoisePrecision
    {
        internal static void Split(double3 value, out Vector4 high, out Vector4 low)
        {
            high = new Vector4((float)value.x, (float)value.y, (float)value.z, 0);
            low = new Vector4((float)(value.x - high.x), (float)(value.y - high.y), (float)(value.z - high.z), 0);
        }

        internal static void WriteSplit(Vector4[] data, int index, double x, double y, double z)
        {
            Split(new double3(x, y, z), out var high, out var low);
            data[index] = high;
            data[index + 1] = low;
        }
    }
}
