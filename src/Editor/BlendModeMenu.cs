namespace DCFApixels.WhimTex
{
    internal sealed class BlendModeMenu : IEnumMenuFactory
    {
        internal static readonly BlendMode[][] Groups =
        {
            new[] { BlendMode.Normal, BlendMode.Overwrite, BlendMode.Crossfade },
            new[] { BlendMode.Lighten, BlendMode.Screen, BlendMode.Dodge, BlendMode.LinearDodge, BlendMode.Add },
            new[] { BlendMode.Darken, BlendMode.Multiply, BlendMode.Burn, BlendMode.LinearBurn },
            new[] { BlendMode.Overlay, BlendMode.SoftLight, BlendMode.HardLight, BlendMode.VividLight,
                BlendMode.LinearLight, BlendMode.LinearLightAddSub, BlendMode.PinLight, BlendMode.HardMix },
            new[] { BlendMode.Difference, BlendMode.Exclusion, BlendMode.Negation, BlendMode.Subtract, BlendMode.Divide },
            new[] { BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity }
        };

        public void Build(EnumMenuBuilder menu)
        {
            foreach (var group in Groups)
            {
                menu.Separator();
                foreach (var mode in group) menu.Add(mode);
            }
        }
    }
}
