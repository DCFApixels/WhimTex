using System;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphAngleUnit { Radians, Degrees, Turns }
    public enum ShaderGraphRotationOrder { XYZ, XZY, YXZ, YZX, ZXY, ZYX }

    internal static partial class ShaderGraphNodes
    {
        internal static readonly string[] AngleUnitNames = { "Radians", "Degrees", "Turns" };
        internal static readonly string[] RotationOrderNames = { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" };

        internal static double AngleConversionFactor(ShaderGraphAngleUnit from, ShaderGraphAngleUnit to)
        {
            double RadiansPerUnit(ShaderGraphAngleUnit unit) => unit switch
            {
                ShaderGraphAngleUnit.Radians => 1,
                ShaderGraphAngleUnit.Degrees => Math.PI / 180,
                ShaderGraphAngleUnit.Turns => Math.PI * 2,
                _ => throw new FormatException("Unknown angle unit.")
            };
            return RadiansPerUnit(from) / RadiansPerUnit(to);
        }

        internal static string AnglePortUnit(ShaderGraphNode node, string port, bool output)
        {
            if (node.kind == ShaderGraphNodeKind.ConvertAngle)
                return AngleUnitSymbol(output ? node.outputAngleUnit : node.inputAngleUnit);
            if (node.kind == ShaderGraphNodeKind.EulerToQuaternion && !output && port == "Euler" ||
                node.kind == ShaderGraphNodeKind.QuaternionToEuler && output && port == "Out")
                return AngleUnitSymbol((ShaderGraphAngleUnit)node.option);
            if (output && node.kind == ShaderGraphNodeKind.GaborNoise && port == "Phase") return "turn";
            if (output)
                return node.kind is ShaderGraphNodeKind.Arcsine or ShaderGraphNodeKind.Arccosine or
                    ShaderGraphNodeKind.Arctangent or ShaderGraphNodeKind.Arctangent2 ? "rad" : null;
            return node.kind switch
            {
                ShaderGraphNodeKind.Sine or ShaderGraphNodeKind.Cosine or ShaderGraphNodeKind.Tangent
                    when port == "In" => "rad",
                ShaderGraphNodeKind.RotateAboutAxis when port == "Rotation" => AngleUnitSymbol((ShaderGraphAngleUnit)node.option),
                ShaderGraphNodeKind.PolarCoordinates when port == "Angle" => "turn",
                ShaderGraphNodeKind.Scatter when port is "Rotation" or "RotationRandom" or "RotationMap" => "turn",
                ShaderGraphNodeKind.TileUV when port == "RandomRotation" => "turn",
                ShaderGraphNodeKind.Twirl when port == "Strength" => "turn",
                ShaderGraphNodeKind.Hue when port == "Offset" => "turn",
                ShaderGraphNodeKind.HueRange when port is "Range" or "Feather" => "turn",
                ShaderGraphNodeKind.NoiseSineWave or ShaderGraphNodeKind.SawtoothWave or
                    ShaderGraphNodeKind.SquareWave or ShaderGraphNodeKind.TriangleWave when port == "In" => "turn",
                ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.Rings or ShaderGraphNodeKind.Wave or
                    ShaderGraphNodeKind.Ripple or ShaderGraphNodeKind.FlowNoise when port == "Phase" => "turn",
                ShaderGraphNodeKind.GaborNoise when port == "PhaseOffset" => "turn",
                ShaderGraphNodeKind.TransformUV or ShaderGraphNodeKind.Shape when port == "Rotation" => "°",
                ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.GradientFactor or ShaderGraphNodeKind.Kaleidoscope or
                    ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur or ShaderGraphNodeKind.RGBShift
                    when port == "Angle" => "°",
                ShaderGraphNodeKind.Wave or ShaderGraphNodeKind.WhiteNoise or ShaderGraphNodeKind.BlueNoise
                    when port == "Direction" => "°",
                _ => null
            };
        }

        static string AngleUnitSymbol(ShaderGraphAngleUnit unit) => unit switch
        {
            ShaderGraphAngleUnit.Radians => "rad", ShaderGraphAngleUnit.Degrees => "°",
            ShaderGraphAngleUnit.Turns => "turn", _ => null
        };
    }
}
