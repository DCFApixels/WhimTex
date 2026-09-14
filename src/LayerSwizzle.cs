using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum SwizzleChannel { R, G, B, A, OneMinusR, OneMinusG, OneMinusB, OneMinusA, Zero, One, RMultiplyA, GMultiplyA, BMultiplyA }

    [Serializable]
    public struct LayerSwizzle
    {
        // Identity is zero, including documents saved before this field existed.
        [SerializeField] private int packed;
        internal static readonly string[] Labels = { "R", "G", "B", "A", "1-R", "1-G", "1-B", "1-A", "0", "1", "R * A", "G * A", "B * A" };

        public SwizzleChannel this[int output]
        {
            get
            {
                if (output < 0 || output > 3) throw new ArgumentOutOfRangeException(nameof(output));
                int value = ((packed >> (output * 4)) & 15) ^ output;
                return value < Labels.Length ? (SwizzleChannel)value : (SwizzleChannel)output;
            }
            set
            {
                if (output < 0 || output > 3) throw new ArgumentOutOfRangeException(nameof(output));
                if ((int)value < 0 || (int)value >= Labels.Length) throw new ArgumentOutOfRangeException(nameof(value));
                packed = (packed & ~(15 << (output * 4))) | (((int)value ^ output) << (output * 4));
            }
        }

        public bool IsIdentity => this[0] == SwizzleChannel.R && this[1] == SwizzleChannel.G &&
                                  this[2] == SwizzleChannel.B && this[3] == SwizzleChannel.A;
        internal Vector4 ShaderValue => new Vector4((int)this[0], (int)this[1], (int)this[2], (int)this[3]);
    }
}
