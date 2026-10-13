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
            if (control.type != ShaderFXParameterType.Float || !control.hasMinimum || !control.hasMaximum ||
                float.IsNaN(control.minimum) || float.IsInfinity(control.minimum) ||
                float.IsNaN(control.maximum) || float.IsInfinity(control.maximum) ||
                control.minimum < -Limit || control.maximum > Limit || Math.Ceiling(control.minimum) > Math.Floor(control.maximum))
                throw new FormatException("seed requires float with two finite bounds in [-16777216 .. 16777216] containing at least one integer.");
        }

        internal static float NextValue(ShaderFXParameterControl control)
        {
            Validate(control);
            return random.Next((int)Math.Ceiling(control.minimum), (int)Math.Floor(control.maximum) + 1);
        }
    }
}
