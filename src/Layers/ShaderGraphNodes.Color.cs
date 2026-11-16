using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphColorSpace { LinearRGB, SRGB, HSV, HSL, Oklab }
    public enum ShaderGraphChannel { Luminance, Red, Green, Blue, Alpha }
    public enum ShaderGraphDerivative { Scharr, Sobel, CentralDifference }

    internal static partial class ShaderGraphNodes
    {
        internal static readonly string[] ColorSpaceNames = { "Linear", "Gamma", "HSV", "HSL", "Perceptual" };
        static readonly string[] colorDistanceModes = { "Linear", "Perceptual", "Chroma" };
        internal static readonly string[] ChannelNames = { "Luminance", "Red", "Green", "Blue", "Alpha" };
        internal static readonly string[] DerivativeNames = { "Scharr", "Sobel", "Central Difference" };
        static readonly Dictionary<ShaderGraphNodeKind, string[]> colorModes = new Dictionary<ShaderGraphNodeKind, string[]>
        {
            [ShaderGraphNodeKind.Levels] = new[] { "Luminance", "RGB" },
            [ShaderGraphNodeKind.ColorCurves] = new[] { "RGB", "Luminance" },
            [ShaderGraphNodeKind.ColorMask] = colorDistanceModes,
            [ShaderGraphNodeKind.ReplaceColor] = colorDistanceModes,
            [ShaderGraphNodeKind.MixColor] = new[] { "Perceptual", "Linear", "Gamma" },
            [ShaderGraphNodeKind.GradientMap] = new[] { "Preserve", "Multiply", "Replace" },
            [ShaderGraphNodeKind.SetAlpha] = new[] { "Replace", "Multiply" },
            [ShaderGraphNodeKind.NormalBlend] = new[] { "Reoriented", "Whiteout" },
            [ShaderGraphNodeKind.HeightToDistortion] = new[] { "Signed RG", "Packed RG" },
            [ShaderGraphNodeKind.NormalToDistortion] = new[] { "Signed RG", "Packed RG" }
        };
        static ShaderGraphPort ColorPort(string name = "Color") => P(name, ShaderGraphValueType.Color, 1);
        static ShaderGraphPort BoolPort(string name, bool value = false) => P(name, ShaderGraphValueType.Boolean, value ? 1 : 0);
        static ShaderGraphPort NormalPort(string name) => new ShaderGraphPort(name, ShaderGraphValueType.Float3, new Vector4(0, 0, 1, 0));

        internal static bool UsesHeightTexture(ShaderGraphNodeKind kind) => UsesHeightDerivative(kind) || IsParallax(kind);
        internal static bool UsesHeightDerivative(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.HeightToDistortion;
        static readonly string[] heightChannelInputs = { "Height" }, slopeChannelInputs = { "Slope" },
            heightBlendChannelInputs = { "Bottom", "Top", "Opacity" }, scatterChannelInputs = { "Density", "Mask" },
            fadeChannelInputs = { "NoiseValue" }, alphaChannelInputs = { "Alpha" },
            mixChannelInputs = { "T" }, blendChannelInputs = { "Opacity" };
        internal static string[] ChannelInputs(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.HeightToDistortion or
            ShaderGraphNodeKind.ParallaxMapping or ShaderGraphNodeKind.ParallaxOcclusionMapping or
            ShaderGraphNodeKind.HeightCurvature or ShaderGraphNodeKind.HeightToAO or ShaderGraphNodeKind.HeightShadow or
            ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.NormalLighting or ShaderGraphNodeKind.LightDirection or
            ShaderGraphNodeKind.LightColor or ShaderGraphNodeKind.LightAttenuation => heightChannelInputs,
            ShaderGraphNodeKind.SlopeBlur => slopeChannelInputs,
            ShaderGraphNodeKind.HeightBlend => heightBlendChannelInputs,
            ShaderGraphNodeKind.Scatter => scatterChannelInputs,
            ShaderGraphNodeKind.FadeTransition => fadeChannelInputs,
            ShaderGraphNodeKind.SetAlpha => alphaChannelInputs,
            ShaderGraphNodeKind.MixColor => mixChannelInputs,
            ShaderGraphNodeKind.Blend => blendChannelInputs,
            _ => System.Array.Empty<string>()
        };
        internal static bool UsesChannel(ShaderGraphNodeKind kind) => ChannelInputs(kind).Length != 0 || kind == ShaderGraphNodeKind.GradientMap;
        internal static bool InputChannel(ShaderGraphNode node, string port, out ShaderGraphChannel channel)
        {
            channel = node.channel;
            if (node.kind == ShaderGraphNodeKind.RangeMask)
            { channel = ShaderGraphChannel.Luminance; return node.rangeLuminance && port == "In"; }
            foreach (var input in ChannelInputs(node.kind)) if (input == port) return true;
            return false;
        }
        internal static bool IsOptionalCurve(ShaderGraphNode node, string port) => node.kind == ShaderGraphNodeKind.ColorCurves && port != "Color";

        static void BuildColorPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.Hue] = new[] { ColorPort(), N("Degrees") };
            map[ShaderGraphNodeKind.Saturation] = new[] { ColorPort(), N("Amount", 1) };
            map[ShaderGraphNodeKind.Exposure] = new[] { ColorPort(), N("Stops") };
            map[ShaderGraphNodeKind.Lightness] = new[] { ColorPort(), N("Amount") };
            map[ShaderGraphNodeKind.Levels] = new[] { ColorPort(), P2("InputRange", 0, 1), N("Gamma", 1), P2("OutputRange", 0, 1), BoolPort("Clamp", true) };
            map[ShaderGraphNodeKind.ColorBalance] = new[] { ColorPort(), P("Shadows", ShaderGraphValueType.Float3), P("Midtones", ShaderGraphValueType.Float3),
                P("Highlights", ShaderGraphValueType.Float3), N("ShadowRange", .5f), N("HighlightRange", .5f), BoolPort("PreserveLuminance", true) };
            map[ShaderGraphNodeKind.ChannelMixer] = new[] { ColorPort(),
                new ShaderGraphPort("Red", ShaderGraphValueType.Float3, new Vector4(1, 0, 0, 0)),
                new ShaderGraphPort("Green", ShaderGraphValueType.Float3, new Vector4(0, 1, 0, 0)),
                new ShaderGraphPort("Blue", ShaderGraphValueType.Float3, new Vector4(0, 0, 1, 0)) };
            map[ShaderGraphNodeKind.WhiteBalance] = new[] { ColorPort(), N("Temperature"), N("Tint") };
            map[ShaderGraphNodeKind.Posterize] = new[] { ColorPort(), N("Levels", 8) };
            map[ShaderGraphNodeKind.ConvertColor] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.Luminance] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.GradientMap] = new[] { ColorPort(), P("Gradient", ShaderGraphValueType.Gradient) };
            map[ShaderGraphNodeKind.ColorCurves] = new[] { ColorPort(), P("Master", ShaderGraphValueType.Curve), P("Red", ShaderGraphValueType.Curve),
                P("Green", ShaderGraphValueType.Curve), P("Blue", ShaderGraphValueType.Curve), P("Alpha", ShaderGraphValueType.Curve) };
            map[ShaderGraphNodeKind.ColorMask] = new[] { ColorPort(), new ShaderGraphPort("KeyColor", ShaderGraphValueType.Color, new Vector4(0, 1, 0, 1)), N("Range", .1f), N("Fuzziness", .05f) };
            map[ShaderGraphNodeKind.HueRange] = new[] { ColorPort(), P2("Range", 330, 30), N("Feather", 15), P2("SaturationRange", .05f, .1f) };
            map[ShaderGraphNodeKind.MixColor] = new[] { new ShaderGraphPort("A", ShaderGraphValueType.Color, new Vector4(0, 0, 0, 1)), ColorPort("B"), N("T", .5f) };
            map[ShaderGraphNodeKind.SetAlpha] = new[] { ColorPort(), N("Alpha", 1) };
            map[ShaderGraphNodeKind.HeightToNormal] = map[ShaderGraphNodeKind.HeightToDistortion] = new[] {
                P("Height", ShaderGraphValueType.Texture), UV(), N("Strength", 8), N("Radius", 1), BoolPort("FlipX"), BoolPort("FlipY") };
            map[ShaderGraphNodeKind.NormalBlend] = new[] { NormalPort("Base"), NormalPort("Detail"), N("Strength", 1) };
            map[ShaderGraphNodeKind.NormalToDistortion] = new[] { NormalPort("Normal"), P("Strength", ShaderGraphValueType.Float2, 1), BoolPort("FlipX"), BoolPort("FlipY") };
        }

        internal static bool TryColorOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type)
        {
            type = ShaderGraphValueType.Float4;
            switch (kind)
            {
                case ShaderGraphNodeKind.Hue: case ShaderGraphNodeKind.Saturation: case ShaderGraphNodeKind.Exposure:
                case ShaderGraphNodeKind.Lightness: case ShaderGraphNodeKind.Levels: case ShaderGraphNodeKind.ColorBalance:
                case ShaderGraphNodeKind.ChannelMixer: case ShaderGraphNodeKind.WhiteBalance: case ShaderGraphNodeKind.RGBShift:
                case ShaderGraphNodeKind.Posterize: case ShaderGraphNodeKind.ConvertColor: case ShaderGraphNodeKind.GradientMap:
                case ShaderGraphNodeKind.ColorCurves: case ShaderGraphNodeKind.MixColor: case ShaderGraphNodeKind.SetAlpha: return true;
                case ShaderGraphNodeKind.Luminance: case ShaderGraphNodeKind.ColorMask: case ShaderGraphNodeKind.HueRange:
                    type = ShaderGraphValueType.Float; return true;
                case ShaderGraphNodeKind.HeightToNormal: case ShaderGraphNodeKind.NormalBlend:
                    type = ShaderGraphValueType.Float3; return true;
                case ShaderGraphNodeKind.HeightToDistortion: case ShaderGraphNodeKind.NormalToDistortion:
                    type = ShaderGraphValueType.Float2; return true;
                default: return false;
            }
        }

    }
}
