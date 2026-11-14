using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static readonly string[] tileModes = { "Grid", "Staggered" }, rotationModes = { "2D", "3D" },
            slopeModes = { "Average", "Min", "Max" }, bilateralModes = { "Source", "Custom" };
        static readonly ShaderGraphPort[] rotate3D = { new ShaderGraphPort("In", ShaderGraphValueType.Float3, new Vector4(1, 0, 0, 0)), N("Angle"),
            new ShaderGraphPort("Axis", ShaderGraphValueType.Float3, new Vector4(0, 0, 1, 0)) };
        static readonly ShaderGraphPort[] bilateralCustom = { P("In"), UV(), N("Radius", 2), N("Tolerance", .1f), P("Guide") };
        static string[] SurfaceModes(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.TileUV => tileModes, ShaderGraphNodeKind.RotateAboutAxis => rotationModes,
            ShaderGraphNodeKind.SlopeBlur => slopeModes, ShaderGraphNodeKind.BilateralBlur => bilateralModes, _ => null
        };
        static IReadOnlyList<ShaderGraphPort> SurfaceInputs(ShaderGraphNode node) => node.option == 1 ? node.kind switch
        {
            ShaderGraphNodeKind.RotateAboutAxis => rotate3D,
            ShaderGraphNodeKind.BilateralBlur => bilateralCustom, _ => null
        } : null;
        static void BuildSurfacePorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.HeightBlend] = new[] { N("Bottom"), N("Top", 1), N("Offset"), N("Smoothness", .1f), N("Opacity", 1) };
            map[ShaderGraphNodeKind.TileUV] = new[] { UV(), P("Count", ShaderGraphValueType.Float2, 4), P("Offset", ShaderGraphValueType.Float2),
                N("RowOffset", .5f), N("RandomRotation"), N("RandomScale"), N("Seed", 1337) };
            map[ShaderGraphNodeKind.PerspectiveUV] = new[] { UV(), P2("BottomLeft", 0, 0), P2("BottomRight", 1, 0), P2("TopRight", 1, 1), P2("TopLeft", 0, 1) };
            map[ShaderGraphNodeKind.RotateAboutAxis] = new[] { P2("In", 1, 0), N("Angle") };
            map[ShaderGraphNodeKind.Blackbody] = new[] { N("Temperature", 6500) };
            map[ShaderGraphNodeKind.SlopeBlur] = new[] { P("In"), UV(), N("Slope"), N("Strength", 8), N("Samples", 16) };
            map[ShaderGraphNodeKind.BilateralBlur] = new[] { P("In"), UV(), N("Radius", 2), N("Tolerance", .1f) };
        }
        internal static bool TrySurfaceOutputType(ShaderGraphNode node, string port, out ShaderGraphValueType type)
        {
            type = ShaderGraphValueType.Float;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.HeightBlend: return true;
                case ShaderGraphNodeKind.TileUV: case ShaderGraphNodeKind.PerspectiveUV:
                    type = port is "Mask" or "Random" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float2; return true;
                case ShaderGraphNodeKind.RotateAboutAxis: type = node.option == 0 ? ShaderGraphValueType.Float2 : ShaderGraphValueType.Float3; return true;
                case ShaderGraphNodeKind.Blackbody: type = ShaderGraphValueType.Color; return true;
                default: return false;
            }
        }
        internal static string SurfaceCategory(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.HeightBlend => "Height",
            ShaderGraphNodeKind.TileUV or ShaderGraphNodeKind.PerspectiveUV => "UV",
            ShaderGraphNodeKind.RotateAboutAxis => "Math/Vector",
            ShaderGraphNodeKind.Blackbody => "Color/Generation",
            ShaderGraphNodeKind.SlopeBlur or ShaderGraphNodeKind.BilateralBlur => "Filter", _ => null
        };
    }
}
