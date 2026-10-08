using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ChannelMappingSource { R, G, B, A, OneMinusR, OneMinusG, OneMinusB, OneMinusA, Zero, One, RMultiplyA, GMultiplyA, BMultiplyA, Luminance, LuminanceMultiplyA }

    [Serializable]
    public struct LayerChannelMapping
    {
        // Zero is the identity mapping, so default values leave RGBA unchanged.
        [SerializeField] private int packed;
        internal static readonly string[] Labels = { "R", "G", "B", "A", "1-R", "1-G", "1-B", "1-A", "0", "1", "R * A", "G * A", "B * A", "Luminance", "Luminance * A" };

        public ChannelMappingSource this[int output]
        {
            get
            {
                if (output < 0 || output > 3) throw new ArgumentOutOfRangeException(nameof(output));
                int value = ((packed >> (output * 4)) & 15) ^ output;
                return value < Labels.Length ? (ChannelMappingSource)value : (ChannelMappingSource)output;
            }
            set
            {
                if (output < 0 || output > 3) throw new ArgumentOutOfRangeException(nameof(output));
                if ((int)value < 0 || (int)value >= Labels.Length) throw new ArgumentOutOfRangeException(nameof(value));
                packed = (packed & ~(15 << (output * 4))) | (((int)value ^ output) << (output * 4));
            }
        }

        public bool IsIdentity => this[0] == ChannelMappingSource.R && this[1] == ChannelMappingSource.G &&
                                  this[2] == ChannelMappingSource.B && this[3] == ChannelMappingSource.A;
        internal Vector4 ShaderValue => new Vector4((int)this[0], (int)this[1], (int)this[2], (int)this[3]);
    }
}
