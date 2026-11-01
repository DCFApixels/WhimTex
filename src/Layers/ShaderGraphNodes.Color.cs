using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphColorSpace { LinearRGB, SRGB, HSV, HSL, Oklab }
    public enum ShaderGraphChannel { Luminance, Red, Green, Blue, Alpha }
    public enum ShaderGraphDerivative { Scharr, Sobel, CentralDifference }
    public enum ShaderGraphNormalEncoding { PackedRGB, SignedXYZ }

    internal static partial class ShaderGraphNodes
    {
        internal static readonly string[] ColorSpaceNames = { "Linear RGB", "sRGB", "HSV", "HSL", "Oklab" };
        internal static readonly string[] ChannelNames = { "Luminance", "Red", "Green", "Blue", "Alpha" };
        internal static readonly string[] DerivativeNames = { "Scharr", "Sobel", "Central Difference" };
        internal static readonly string[] NormalEncodingNames = { "Packed RGB", "Signed XYZ" };
        static readonly Dictionary<ShaderGraphNodeKind, string[]> colorModes = new Dictionary<ShaderGraphNodeKind, string[]>
        {
            [ShaderGraphNodeKind.Levels] = new[] { "Luminance", "RGB" },
            [ShaderGraphNodeKind.ColorCurves] = new[] { "RGB", "Luminance" },
            [ShaderGraphNodeKind.ColorMask] = new[] { "Chroma", "RGB", "Perceptual" },
            [ShaderGraphNodeKind.MixColor] = new[] { "Perceptual", "Linear RGB", "sRGB" },
            [ShaderGraphNodeKind.GradientMap] = new[] { "Preserve", "Multiply", "Replace" },
            [ShaderGraphNodeKind.SetAlpha] = new[] { "Replace", "Multiply" },
            [ShaderGraphNodeKind.NormalBlend] = new[] { "Reoriented", "Whiteout" },
            [ShaderGraphNodeKind.HeightToDistortion] = new[] { "Signed RG", "Packed RG" },
            [ShaderGraphNodeKind.NormalToDistortion] = new[] { "Signed RG", "Packed RG" }
        };
        static ShaderGraphPort ColorPort(string name = "Color") => P(name, ShaderGraphValueType.Color, 1);
        static ShaderGraphPort BoolPort(string name, bool value = false) => P(name, ShaderGraphValueType.Boolean, value ? 1 : 0);
        static ShaderGraphPort NormalPort(string name, bool packed) => new ShaderGraphPort(name, ShaderGraphValueType.Float3,
            packed ? new Vector4(.5f, .5f, 1, 0) : new Vector4(0, 0, 1, 0));
        static readonly ShaderGraphPort[] signedNormalBlend = { NormalPort("Base", false), NormalPort("Detail", false), N("Strength", 1) };
        static readonly ShaderGraphPort[] signedNormalDistortion = { NormalPort("Normal", false), P("Strength", ShaderGraphValueType.Float2, 1), BoolPort("FlipX"), BoolPort("FlipY") };
        static IReadOnlyList<ShaderGraphPort> NormalInputs(ShaderGraphNode node) => node.normalEncoding == ShaderGraphNormalEncoding.SignedXYZ
            ? node.kind switch { ShaderGraphNodeKind.NormalBlend => signedNormalBlend, ShaderGraphNodeKind.NormalToDistortion => signedNormalDistortion, _ => null } : null;

        internal static bool UsesHeightTexture(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.HeightToDistortion;
        internal static bool UsesNormalEncoding(ShaderGraphNodeKind kind) => ExtendedUsesNormalEncoding(kind) || kind is ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.NormalBlend or ShaderGraphNodeKind.NormalToDistortion;
        internal static bool UsesChannel(ShaderGraphNodeKind kind) => UsesHeightTexture(kind) || kind == ShaderGraphNodeKind.GradientMap;
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
            map[ShaderGraphNodeKind.Posterize] = new[] { ColorPort(), N("Levels", 8) };
            map[ShaderGraphNodeKind.ConvertColor] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.Luminance] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.GradientMap] = new[] { ColorPort(), P("Gradient", ShaderGraphValueType.Gradient) };
            map[ShaderGraphNodeKind.ColorCurves] = new[] { ColorPort(), P("Master", ShaderGraphValueType.Curve), P("Red", ShaderGraphValueType.Curve),
                P("Green", ShaderGraphValueType.Curve), P("Blue", ShaderGraphValueType.Curve), P("Alpha", ShaderGraphValueType.Curve) };
            map[ShaderGraphNodeKind.ColorMask] = new[] { ColorPort(), new ShaderGraphPort("KeyColor", ShaderGraphValueType.Color, new Vector4(0, 1, 0, 1)), N("Tolerance", .1f), N("Softness", .05f) };
            map[ShaderGraphNodeKind.HueRange] = new[] { ColorPort(), P2("Range", 330, 30), N("Feather", 15), P2("SaturationRange", .05f, .1f) };
            map[ShaderGraphNodeKind.MixColor] = new[] { new ShaderGraphPort("A", ShaderGraphValueType.Color, new Vector4(0, 0, 0, 1)), ColorPort("B"), N("T", .5f) };
            map[ShaderGraphNodeKind.SetAlpha] = new[] { ColorPort(), N("Alpha", 1) };
            map[ShaderGraphNodeKind.HeightToNormal] = map[ShaderGraphNodeKind.HeightToDistortion] = new[] {
                P("Height", ShaderGraphValueType.Texture), UV(), N("Strength", 8), N("Radius", 1), BoolPort("FlipX"), BoolPort("FlipY") };
            map[ShaderGraphNodeKind.NormalBlend] = new[] { NormalPort("Base", true), NormalPort("Detail", true), N("Strength", 1) };
            map[ShaderGraphNodeKind.NormalToDistortion] = new[] { NormalPort("Normal", true), P("Strength", ShaderGraphValueType.Float2, 1), BoolPort("FlipX"), BoolPort("FlipY") };
        }

        internal static bool TryColorOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type)
        {
            type = ShaderGraphValueType.Color;
            switch (kind)
            {
                case ShaderGraphNodeKind.Hue: case ShaderGraphNodeKind.Saturation: case ShaderGraphNodeKind.Exposure:
                case ShaderGraphNodeKind.Lightness: case ShaderGraphNodeKind.Levels: case ShaderGraphNodeKind.ColorBalance:
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

        internal static void ChangeNormalEncoding(ShaderGraphNode node, ShaderGraphNormalEncoding encoding)
        {
            if (ExtendedUsesNormalEncoding(node.kind)) { ChangeExtendedNormalEncoding(node, encoding); return; }
            if (node.normalEncoding == encoding) return;
            if (node.kind is ShaderGraphNodeKind.NormalBlend or ShaderGraphNodeKind.NormalToDistortion)
                foreach (var input in node.inputs)
                    if (input.port is "Base" or "Detail" or "Normal")
                        for (int c = 0; c < 3; c++) input.value[c] = encoding == ShaderGraphNormalEncoding.PackedRGB ? input.value[c] * .5f + .5f : input.value[c] * 2 - 1;
            node.normalEncoding = encoding;
        }
    }
}
