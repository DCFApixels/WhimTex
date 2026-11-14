using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static readonly Dictionary<ShaderGraphNodeKind, string[]> extendedModes = new Dictionary<ShaderGraphNodeKind, string[]>
        {
            [ShaderGraphNodeKind.NormalLighting] = new[] { "Local", "Global" },
            [ShaderGraphNodeKind.Flipbook] = new[] { "Bottom to Top", "Top to Bottom" },
            [ShaderGraphNodeKind.Dither] = new[] { "Bayer 4x4", "Bayer 2x2", "Bayer 8x8" },
            [ShaderGraphNodeKind.Exponential] = new[] { "Base E", "Base 2" },
            [ShaderGraphNodeKind.Reciprocal] = new[] { "Default", "Fast" }
        };

        static ShaderGraphPort[] ExtendedLightingPorts(bool packed) => new[]
        {
            ColorPort(), NormalPort("Normal", packed),
            new ShaderGraphPort("LightDirection", ShaderGraphValueType.Float3, new Vector4(-.5f, .5f, 1, 0)),
            ColorPort("LightColor"), N("Intensity", 1), N("Ambient", .15f),
            ColorPort("SpecularColor"), N("SpecularIntensity"), N("Roughness", .25f), BoolPort("SpecularOnly")
        };
        static readonly ShaderGraphPort[] signedExtendedStrength = { NormalPort("Normal", false), N("Strength", 1) };
        static readonly ShaderGraphPort[] signedExtendedLighting = ExtendedLightingPorts(false);
        static ShaderGraphPort[] GlobalLightingPorts(bool packed) => new[]
        {
            ColorPort(), NormalPort("Normal", packed), UV(), N("Height"), N("Ambient", .15f),
            ColorPort("SpecularColor"), N("SpecularIntensity"), N("Roughness", .25f)
        };
        static readonly ShaderGraphPort[] globalPackedLighting = GlobalLightingPorts(true);
        static readonly ShaderGraphPort[] globalSignedLighting = GlobalLightingPorts(false);

        // Return null when the ordinary port table supplies the defaults. Encode and
        // Decode have fixed directions and do not expose a normalEncoding setting.
        internal static IReadOnlyList<ShaderGraphPort> ExtendedInputs(ShaderGraphNode node) =>
            node.kind == ShaderGraphNodeKind.NormalLighting && node.option == 1
                ? (node.normalEncoding == ShaderGraphNormalEncoding.SignedXYZ ? globalSignedLighting : globalPackedLighting) :
            node.normalEncoding == ShaderGraphNormalEncoding.SignedXYZ ? node.kind switch
            {
                ShaderGraphNodeKind.NormalStrength => signedExtendedStrength,
                ShaderGraphNodeKind.NormalLighting => signedExtendedLighting,
                _ => null
            } : null;

        internal static bool ExtendedUsesNormalEncoding(ShaderGraphNodeKind kind) =>
            kind is ShaderGraphNodeKind.NormalStrength or ShaderGraphNodeKind.NormalLighting;

        // Same contract as ChangeNormalEncoding: convert stored input defaults before
        // assigning the new setting. Connections keep their values and must match it.
        internal static void ChangeExtendedNormalEncoding(ShaderGraphNode node, ShaderGraphNormalEncoding encoding)
        {
            if (!ExtendedUsesNormalEncoding(node.kind) || node.normalEncoding == encoding) return;
            if (node.inputs != null)
                foreach (var input in node.inputs)
                    if (input != null && input.port == "Normal")
                        for (int c = 0; c < 3; c++)
                            input.value[c] = encoding == ShaderGraphNormalEncoding.PackedRGB ? input.value[c] * .5f + .5f : input.value[c] * 2 - 1;
            node.normalEncoding = encoding;
        }

        static void BuildExtendedPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.LightCount] = new ShaderGraphPort[0];
            map[ShaderGraphNodeKind.LightDirection] = map[ShaderGraphNodeKind.LightColor] = map[ShaderGraphNodeKind.LightAttenuation] =
                new[] { N("Index"), UV(), N("Height") };
            foreach (var kind in new[] { ShaderGraphNodeKind.Sign, ShaderGraphNodeKind.Saturate, ShaderGraphNodeKind.Exponential })
                map[kind] = new[] { P("In") };
            foreach (var kind in Kinds)
                if (UnaryTrigonometryFunction(kind) != null)
                    map[kind] = new[] { P("In") };
            map[ShaderGraphNodeKind.Log] = map[ShaderGraphNodeKind.Reciprocal] =
                map[ShaderGraphNodeKind.ReciprocalSquareRoot] = new[] { P("In", null, 1) };
            map[ShaderGraphNodeKind.Modulo] = new[] { P("A"), P("B", null, 1) };
            map[ShaderGraphNodeKind.Arctangent2] = new[] { P("Y"), P("X", null, 1) };
            map[ShaderGraphNodeKind.CrossProduct] = new[]
            {
                new ShaderGraphPort("A", ShaderGraphValueType.Float3, new Vector4(1, 0, 0, 0)),
                new ShaderGraphPort("B", ShaderGraphValueType.Float3, new Vector4(0, 1, 0, 0))
            };
            // Incident and Normal remain dynamic, as do the corresponding intrinsics.
            // Unit-length inputs are the caller's responsibility; Eta is scalar.
            map[ShaderGraphNodeKind.Reflection] = new[] { P("Incident", null, -1), P("Normal", null, 1) };
            map[ShaderGraphNodeKind.Refract] = new[] { P("Incident", null, -1), P("Normal", null, 1), N("Eta", 1) };
            map[ShaderGraphNodeKind.Projection] = map[ShaderGraphNodeKind.Rejection] = new[] { P("A"), P("B", null, 1) };
            var forward = new Vector4(0, 0, 1, 0);
            map[ShaderGraphNodeKind.FresnelEffect] = new[]
            {
                new ShaderGraphPort("Normal", ShaderGraphValueType.Float3, forward),
                new ShaderGraphPort("ViewDir", ShaderGraphValueType.Float3, forward), N("Power", 1)
            };
            map[ShaderGraphNodeKind.SphereMask] = new[] { P("Coords"), P("Center", null, .5f), N("Radius", .1f), N("Hardness", .8f) };
            map[ShaderGraphNodeKind.Transform] = new[] { UV("In") };
            map[ShaderGraphNodeKind.And] = map[ShaderGraphNodeKind.Or] = new[] { BoolPort("A"), BoolPort("B") };
            map[ShaderGraphNodeKind.Not] = new[] { BoolPort("In") };
            map[ShaderGraphNodeKind.NormalEncode] = new[] { NormalPort("Normal", false) };
            map[ShaderGraphNodeKind.NormalDecode] = new[] { NormalPort("Normal", true) };
            map[ShaderGraphNodeKind.NormalStrength] = new[] { NormalPort("Normal", true), N("Strength", 1) };
            map[ShaderGraphNodeKind.NormalLighting] = ExtendedLightingPorts(true);
            map[ShaderGraphNodeKind.Premultiply] = map[ShaderGraphNodeKind.Unpremultiply] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.Flipbook] = new[] { UV(), N("Columns", 4), N("Rows", 4), N("Frame") };
            map[ShaderGraphNodeKind.Dither] = new[] { ColorPort(), UV(), N("Levels", 8), N("PixelSize", 1) };
        }

        // Only fixed outputs belong here. Dynamic math is inferred from the widest
        // numeric input, including Color as Float4, by the graph and the compiler.
        internal static bool TryExtendedOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type)
        {
            type = ShaderGraphValueType.Float;
            switch (kind)
            {
                case ShaderGraphNodeKind.LightCount: case ShaderGraphNodeKind.LightAttenuation: return true;
                case ShaderGraphNodeKind.FresnelEffect: case ShaderGraphNodeKind.SphereMask: return true;
                case ShaderGraphNodeKind.LightDirection: case ShaderGraphNodeKind.LightColor:
                    type = ShaderGraphValueType.Float3; return true;
                case ShaderGraphNodeKind.Flipbook: case ShaderGraphNodeKind.Transform: type = ShaderGraphValueType.Float2; return true;
                case ShaderGraphNodeKind.CrossProduct: case ShaderGraphNodeKind.NormalEncode:
                case ShaderGraphNodeKind.NormalDecode: case ShaderGraphNodeKind.NormalStrength:
                    type = ShaderGraphValueType.Float3; return true;
                case ShaderGraphNodeKind.NormalLighting: case ShaderGraphNodeKind.Premultiply:
                case ShaderGraphNodeKind.Unpremultiply: case ShaderGraphNodeKind.Dither:
                    type = ShaderGraphValueType.Color; return true;
                case ShaderGraphNodeKind.And: case ShaderGraphNodeKind.Or: case ShaderGraphNodeKind.Not:
                    type = ShaderGraphValueType.Boolean; return true;
                default: return false;
            }
        }

        internal static bool IsExtended(ShaderGraphNodeKind kind) => ExtendedCategory(kind) != null;

        internal static string UnaryTrigonometryFunction(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.Sine => "sin",
            ShaderGraphNodeKind.Cosine => "cos",
            ShaderGraphNodeKind.Tangent => "tan",
            ShaderGraphNodeKind.Arcsine => "asin",
            ShaderGraphNodeKind.Arccosine => "acos",
            ShaderGraphNodeKind.Arctangent => "atan",
            ShaderGraphNodeKind.HyperbolicSine => "sinh",
            ShaderGraphNodeKind.HyperbolicCosine => "cosh",
            ShaderGraphNodeKind.HyperbolicTangent => "tanh",
            ShaderGraphNodeKind.DegreesToRadians => "radians",
            ShaderGraphNodeKind.RadiansToDegrees => "degrees",
            _ => null
        };

        internal static string ExtendedCategory(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.Arctangent2 => "Math/Trigonometry",
            _ when UnaryTrigonometryFunction(kind) != null => "Math/Trigonometry",
            ShaderGraphNodeKind.LightCount or ShaderGraphNodeKind.LightDirection or
            ShaderGraphNodeKind.LightColor or ShaderGraphNodeKind.LightAttenuation => "Lighting",
            ShaderGraphNodeKind.Saturate => "Math/Range",
            ShaderGraphNodeKind.Sign => "Math/Round",
            ShaderGraphNodeKind.Modulo or ShaderGraphNodeKind.Exponential or ShaderGraphNodeKind.Log or
            ShaderGraphNodeKind.Reciprocal or ShaderGraphNodeKind.ReciprocalSquareRoot => "Math/Advanced",
            ShaderGraphNodeKind.CrossProduct or ShaderGraphNodeKind.Reflection or ShaderGraphNodeKind.Refract or
            ShaderGraphNodeKind.Projection or ShaderGraphNodeKind.Rejection or ShaderGraphNodeKind.FresnelEffect or
            ShaderGraphNodeKind.SphereMask or ShaderGraphNodeKind.Transform => "Math/Vector",
            ShaderGraphNodeKind.And or ShaderGraphNodeKind.Or or ShaderGraphNodeKind.Not => "Logic",
            ShaderGraphNodeKind.NormalEncode or ShaderGraphNodeKind.NormalDecode or
            ShaderGraphNodeKind.NormalStrength or ShaderGraphNodeKind.NormalLighting => "Normal",
            ShaderGraphNodeKind.Premultiply or ShaderGraphNodeKind.Unpremultiply => "Color/Alpha",
            ShaderGraphNodeKind.Flipbook => "UV",
            ShaderGraphNodeKind.Dither => "Color/Adjustments",
            _ => null
        };
    }
}
