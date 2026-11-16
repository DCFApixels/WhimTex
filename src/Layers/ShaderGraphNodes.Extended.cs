using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static readonly Dictionary<ShaderGraphNodeKind, string[]> extendedModes = new Dictionary<ShaderGraphNodeKind, string[]>
        {
            [ShaderGraphNodeKind.NormalLighting] = new[] { "Local", "Global" },
            [ShaderGraphNodeKind.NormalUnpack] = new[] { "Tangent", "Object" },
            [ShaderGraphNodeKind.Flipbook] = new[] { "Bottom to Top", "Top to Bottom" },
            [ShaderGraphNodeKind.Dither] = new[] { "Bayer 4x4", "Bayer 2x2", "Bayer 8x8" },
            [ShaderGraphNodeKind.Exponential] = new[] { "Base E", "Base 2" },
            [ShaderGraphNodeKind.Log] = new[] { "Base E", "Base 2", "Base 10" },
            [ShaderGraphNodeKind.Refract] = new[] { "Critical Angle", "Safe" },
            [ShaderGraphNodeKind.Reciprocal] = new[] { "Default", "Fast" }
        };

        static ShaderGraphPort[] LocalLightingPorts() => new[]
        {
            ColorPort(), NormalPort("Normal"),
            new ShaderGraphPort("LightDirection", ShaderGraphValueType.Float3, new Vector4(-.5f, .5f, 1, 0)),
            ColorPort("LightColor"), N("Intensity", 1), N("Ambient", .15f),
            ColorPort("SpecularColor"), N("SpecularIntensity"), N("Roughness", .25f), BoolPort("SpecularOnly")
        };
        static readonly ShaderGraphPort[] globalLighting =
        {
            ColorPort(), NormalPort("Normal"), UV(), N("Height"), N("Ambient", .15f),
            ColorPort("SpecularColor"), N("SpecularIntensity"), N("Roughness", .25f)
        };

        internal static IReadOnlyList<ShaderGraphPort> ExtendedInputs(ShaderGraphNode node) =>
            node.kind == ShaderGraphNodeKind.NormalLighting && node.option == 1 ? globalLighting : null;

        static void BuildExtendedPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.LightCount] = new ShaderGraphPort[0];
            map[ShaderGraphNodeKind.LightDirection] = map[ShaderGraphNodeKind.LightColor] = map[ShaderGraphNodeKind.LightAttenuation] =
                new[] { N("Index"), UV(), N("Height") };
            foreach (var kind in new[] { ShaderGraphNodeKind.Sign, ShaderGraphNodeKind.Saturate, ShaderGraphNodeKind.Exponential,
                ShaderGraphNodeKind.SoftSign, ShaderGraphNodeKind.InverseSoftSign })
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
            map[ShaderGraphNodeKind.Reflection] = new[] { P("Incident", null, -1), P("Normal", null, 1) };
            map[ShaderGraphNodeKind.Refract] = new[]
            {
                new ShaderGraphPort("Incident", ShaderGraphValueType.Float3, new Vector4(0, 0, 1, 0)),
                new ShaderGraphPort("Normal", ShaderGraphValueType.Float3, new Vector4(0, 0, -1, 0)),
                N("IORSource", 1), N("IORMedium", 1.5f)
            };
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
            map[ShaderGraphNodeKind.NormalEncode] = new[] { NormalPort("Normal") };
            map[ShaderGraphNodeKind.NormalDecode] = new[] { new ShaderGraphPort("Normal", ShaderGraphValueType.Float3, new Vector4(.5f, .5f, 1, 0)) };
            map[ShaderGraphNodeKind.NormalStrength] = new[] { NormalPort("Normal"), N("Strength", 1) };
            map[ShaderGraphNodeKind.NormalLighting] = LocalLightingPorts();
            map[ShaderGraphNodeKind.Premultiply] = map[ShaderGraphNodeKind.Unpremultiply] = new[] { ColorPort() };
            map[ShaderGraphNodeKind.Flipbook] = new[] { UV(), N("Columns", 4), N("Rows", 4), N("Frame") };
            map[ShaderGraphNodeKind.Dither] = new[] { ColorPort(), UV(), N("Levels", 8), N("PixelSize", 1) };
        }

        // Only fixed outputs belong here. Dynamic math is inferred from the widest
        // numeric input, including Color as Float4, by the graph and the compiler.
        internal static bool TryExtendedOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type, string port = "Out")
        {
            type = ShaderGraphValueType.Float;
            switch (kind)
            {
                case ShaderGraphNodeKind.Refract:
                    type = port == "Intensity" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float3; return true;
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
                    type = ShaderGraphValueType.Float4; return true;
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
            ShaderGraphNodeKind.Reciprocal or ShaderGraphNodeKind.ReciprocalSquareRoot or
            ShaderGraphNodeKind.SoftSign or ShaderGraphNodeKind.InverseSoftSign => "Math/Advanced",
            ShaderGraphNodeKind.CrossProduct or ShaderGraphNodeKind.Reflection or ShaderGraphNodeKind.Refract or
            ShaderGraphNodeKind.Projection or ShaderGraphNodeKind.Rejection or ShaderGraphNodeKind.FresnelEffect or
            ShaderGraphNodeKind.SphereMask or ShaderGraphNodeKind.Transform => "Math/Vector",
            ShaderGraphNodeKind.And or ShaderGraphNodeKind.Or or ShaderGraphNodeKind.Not or
            ShaderGraphNodeKind.Compare or ShaderGraphNodeKind.Select => "Logic",
            ShaderGraphNodeKind.NormalEncode or ShaderGraphNodeKind.NormalDecode or
            ShaderGraphNodeKind.NormalStrength or ShaderGraphNodeKind.NormalLighting => "Normal",
            ShaderGraphNodeKind.Premultiply or ShaderGraphNodeKind.Unpremultiply => "Color/Alpha",
            ShaderGraphNodeKind.Flipbook => "UV",
            ShaderGraphNodeKind.Dither => "Color/Adjustments",
            _ => null
        };
    }
}
