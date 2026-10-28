using System;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXSeed
    {
        // Integers in this interval have an exact float representation.
        internal const int Limit = 16777216;
        private static readonly Random random = new Random();

        internal static ShaderFXParameterControl Control(ShaderFXParameter parameter) =>
            parameter.controls.Find(c => c.seed);

        internal static void Validate(ShaderFXParameterControl control)
        {
            if (control.type != ShaderFXParameterType.Seed || !control.hasMinimum || !control.hasMaximum ||
                control.valueMinimum > control.valueMaximum || control.valueMinimum != Math.Truncate(control.valueMinimum) ||
                control.valueMaximum != Math.Truncate(control.valueMaximum))
                throw new FormatException("seed requires a valid integer range.");
        }

        internal static double NextValue(ShaderFXParameterControl control)
        {
            Validate(control);
            ulong width = (ulong)(control.valueMaximum - control.valueMinimum) + 1;
            ulong limit = (1UL << 32) / width * width;
            ulong sample;
            do { sample = ((ulong)random.Next(65536) << 16) | (uint)random.Next(65536); } while (sample >= limit);
            return control.valueMinimum + (long)(sample % width);
        }
    }
}
