using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphFractal { None, FBm, Ridged, PingPong, Progressive, Independent,
        Multifractal, HybridMultifractal, HeteroTerrain, RidgedMultifractal }

    internal static partial class ShaderGraphNodes
    {
        internal static readonly string[] WarpFractals = { "None", "Progressive", "Independent" };
        static readonly Dictionary<ShaderGraphNodeKind, string[]> proceduralModes = new Dictionary<ShaderGraphNodeKind, string[]>
        {
            [ShaderGraphNodeKind.Scatter] = new[] { "Max", "Add", "Alpha Over" },
            [ShaderGraphNodeKind.CausticsNoise] = new[] { "OpenSimplex2S", "Simplex" },
            [ShaderGraphNodeKind.Shape] = new[] { "Ellipse", "Rectangle", "Rounded Rectangle", "Polygon", "Star", "Capsule", "Line Segment" },
            [ShaderGraphNodeKind.Stripes] = new[] { "Square", "Triangle", "Sine" },
            [ShaderGraphNodeKind.GaborNoise] = new[] { "Sine", "Triangle", "Pulse" },
            [ShaderGraphNodeKind.RangeMask] = new[] { "Linear", "Smooth" },
            [ShaderGraphNodeKind.GradientFactor] = new[] { "Linear", "Radial", "Angular" },
            [ShaderGraphNodeKind.CombineDistance] = new[] { "Union", "Intersection", "Subtract" },
            [ShaderGraphNodeKind.PolarCoordinates] = new[] { "To Polar", "From Polar" },
            [ShaderGraphNodeKind.Mirror] = new[] { "X", "Y", "Both" },
            [ShaderGraphNodeKind.VectorDisplacement] = new[] { "Signed RG", "Packed RG" }
        };
        internal static string[] ModeNames(ShaderGraphNodeKind kind) => SurfaceModes(kind) ?? (UsesFilterKernel(kind) ? filterKernels : kind == ShaderGraphNodeKind.RadialBlur ? radialBlurModes : proceduralModes.TryGetValue(kind, out var modes) || colorModes.TryGetValue(kind, out modes) || extendedModes.TryGetValue(kind, out modes) ? modes : null);
        internal static string ModeLabel(ShaderGraphNodeKind kind) => UsesFilterKernel(kind) ? "Minimum Kernel" : kind switch
        {
            ShaderGraphNodeKind.Shape => "Shape",
            ShaderGraphNodeKind.CausticsNoise => "Algorithm",
            ShaderGraphNodeKind.RangeMask => "Falloff",
            ShaderGraphNodeKind.RotateVector => "Dimensions", ShaderGraphNodeKind.BilateralBlur => "Guide",
            ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.GaborNoise => "Profile", ShaderGraphNodeKind.CombineDistance => "Operation",
            ShaderGraphNodeKind.Mirror => "Axes", ShaderGraphNodeKind.MixColor => "Color Model",
            ShaderGraphNodeKind.ColorMask => "Distance", ShaderGraphNodeKind.GradientMap => "Alpha",
            ShaderGraphNodeKind.HeightToDistortion or ShaderGraphNodeKind.NormalToDistortion => "Output", _ => "Mode"
        };
        internal static string InputInactiveReason(ShaderGraphNode node, string input) => !InputVisible(node, input)
            ? "Not used by the current node settings." : NoiseInputInactiveReason(node, input) ?? node.kind switch
        {
            ShaderGraphNodeKind.Fractal when input == "PingPongStrength" && node.fractal != ShaderGraphFractal.PingPong =>
                "Used only by the Ping Pong fractal.",
            ShaderGraphNodeKind.TileUV when input == "RowOffset" && node.option == 0 => "Used only by Staggered rows.",
            ShaderGraphNodeKind.Shape when input == "Radius" && node.option != 2 =>
                "Used only by Rounded Rectangle.",
            ShaderGraphNodeKind.Stripes when input == "Width" && node.option != 0 =>
                "Used only by the Square profile.",
            ShaderGraphNodeKind.GradientFactor when input == "Angle" && node.option == 1 =>
                "The Radial gradient has no angle.",
            ShaderGraphNodeKind.RadialBlur when input == "Angle" && node.option == 1 => "Used only by Spin.",
            ShaderGraphNodeKind.RadialBlur when input == "Strength" && node.option == 0 => "Used only by Zoom.",
            _ => null
        };
        static ShaderGraphPort P2(string name, float x, float y) => new ShaderGraphPort(name, ShaderGraphValueType.Float2, new Vector4(x, y, 0, 0));
        static ShaderGraphPort N(string name, float value = 0) => P(name, ShaderGraphValueType.Float, value);
        static ShaderGraphPort Center() => P("Center", ShaderGraphValueType.Float2, .5f);
        static readonly ShaderGraphPort[] polygonPorts = { UV(), Center(), P("Size", ShaderGraphValueType.Float2, .5f), N("Sides", 6), N("Rotation"), N("Feather") };
        static readonly ShaderGraphPort[] starPorts = { UV(), Center(), P("Size", ShaderGraphValueType.Float2, .5f), N("Sides", 5), N("InnerRadius", .5f), N("Rotation"), N("Feather") };
        static readonly ShaderGraphPort[] capsulePorts = { UV(), Center(), P2("Size", .3f, .6f), N("Rotation"), N("Feather") };
        static readonly ShaderGraphPort[] segmentPorts = { UV(), P2("Start", .25f, .5f), P2("End", .75f, .5f), N("Width", .02f), N("Feather") };
        static IReadOnlyList<ShaderGraphPort> ShapeInputs(ShaderGraphNode node) => node.option switch
        {
            3 => polygonPorts, 4 => starPorts, 5 => capsulePorts, 6 => segmentPorts, _ => ports[ShaderGraphNodeKind.Shape]
        };
        static void BuildProceduralPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.PixelateUV] = new[] { UV(), P("CellSize", ShaderGraphValueType.Float2, 8), P("Offset", ShaderGraphValueType.Float2) };
            map[ShaderGraphNodeKind.Random] = new[] { new ShaderGraphPort("Coordinates", ShaderGraphValueType.Float2, uvDefault: true), N("Seed", 1337) };
            map[ShaderGraphNodeKind.Checkerboard] = new[] { UV(), P("Scale", ShaderGraphValueType.Float2, 8), P("Offset", ShaderGraphValueType.Float2) };
            map[ShaderGraphNodeKind.Stripes] = new[] { UV(), N("Frequency", 8), N("Width", .5f), N("Angle"), N("Phase") };
            map[ShaderGraphNodeKind.Grid] = new[] { UV(), P("Scale", ShaderGraphValueType.Float2, 8), P("Width", ShaderGraphValueType.Float2, .1f), P("Offset", ShaderGraphValueType.Float2) };
            map[ShaderGraphNodeKind.Rings] = new[] { UV(), Center(), N("Frequency", 8), N("Width", .5f), N("Phase") };
            map[ShaderGraphNodeKind.GradientFactor] = new[] { UV(), Center(), P("Scale", ShaderGraphValueType.Float2, 1), N("Angle") };
            map[ShaderGraphNodeKind.CombineDistance] = new[] { N("A"), N("B"), N("Smoothness"), N("Feather") };
            map[ShaderGraphNodeKind.PolarCoordinates] = new[] { UV(), Center(), N("Angle") };
            map[ShaderGraphNodeKind.Repeat] = new[] { UV(), P("Scale", ShaderGraphValueType.Float2, 4), P("Offset", ShaderGraphValueType.Float2) };
            map[ShaderGraphNodeKind.Mirror] = new[] { UV(), Center() };
            map[ShaderGraphNodeKind.Kaleidoscope] = new[] { UV(), Center(), N("Sectors", 6), N("Angle") };
            map[ShaderGraphNodeKind.VectorDisplacement] = new[] { UV(), P("Displacement", ShaderGraphValueType.Float2), P("Strength", ShaderGraphValueType.Float2, .1f) };
            map[ShaderGraphNodeKind.Twirl] = new[] { UV(), Center(), N("Strength", 90), N("Radius", .5f) };
            map[ShaderGraphNodeKind.BulgePinch] = new[] { UV(), Center(), N("Strength", .5f), N("Radius", .5f) };
            map[ShaderGraphNodeKind.Wave] = new[] { UV(), N("Strength", .03f), N("Frequency", 4), N("Direction"), N("Phase") };
            map[ShaderGraphNodeKind.Ripple] = new[] { UV(), Center(), N("Strength", .03f), N("Frequency", 8), N("Radius", .5f), N("Phase") };
        }
        internal static bool TryProceduralOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type)
        {
            type = ShaderGraphValueType.Float;
            switch (kind)
            {
                case ShaderGraphNodeKind.Shape:
                case ShaderGraphNodeKind.Random:
                case ShaderGraphNodeKind.Checkerboard: case ShaderGraphNodeKind.Stripes: case ShaderGraphNodeKind.Grid:
                case ShaderGraphNodeKind.Rings: case ShaderGraphNodeKind.GradientFactor: case ShaderGraphNodeKind.CombineDistance: return true;
                case ShaderGraphNodeKind.PolarCoordinates: case ShaderGraphNodeKind.Repeat: case ShaderGraphNodeKind.Mirror:
                case ShaderGraphNodeKind.Kaleidoscope: case ShaderGraphNodeKind.VectorDisplacement:
                case ShaderGraphNodeKind.Twirl: case ShaderGraphNodeKind.BulgePinch: case ShaderGraphNodeKind.Wave: case ShaderGraphNodeKind.Ripple: case ShaderGraphNodeKind.PixelateUV:
                    type = ShaderGraphValueType.Float2; return true;
                default: return false;
            }
        }
        internal static void ChangeMode(ShaderGraph graph, ShaderGraphNode node, int mode)
        {
            node.option = mode;
            var current = Inputs(node);
            if (node.kind == ShaderGraphNodeKind.RotateVector && node.FindInput("In") is ShaderGraphInput vector)
            {
                vector.type = mode == 0 ? ShaderGraphValueType.Float2 : ShaderGraphValueType.Float3;
            }
            foreach (var p in current)
                if (!p.uvDefault && p.type is not (ShaderGraphValueType.Texture or ShaderGraphValueType.Gradient or ShaderGraphValueType.Curve) && node.FindInput(p.name) == null)
                    node.inputs.Add(new ShaderGraphInput { port = p.name, type = p.type ?? ShaderGraphValueType.Float, value = p.value });
        }
    }
}
