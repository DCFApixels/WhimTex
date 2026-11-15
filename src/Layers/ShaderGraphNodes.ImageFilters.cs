using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static readonly string[] supersampleModes = { "2x2", "3x3", "4x4" };
        static readonly string[] paletteDistanceModes = { "Linear", "Perceptual" };
        static readonly string[] truchetModes = { "Arcs", "Lines" };
        static readonly ShaderGraphPort[] signedCurvature = { NormalPort("Normal", false), UV(), N("Radius", 1), N("Strength", 1) };
        static readonly string[] curvatureOutputs = { "Convex", "Concave", "Curvature" };
        internal static int ImageFilterMode(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.HeightCurvature => 0, ShaderGraphNodeKind.HeightToAO => 1,
            ShaderGraphNodeKind.HeightShadow => 2, ShaderGraphNodeKind.Supersample => 3,
            ShaderGraphNodeKind.MakeTileable => 4, ShaderGraphNodeKind.NormalCurvature => 5,
            ShaderGraphNodeKind.Sharpen => 6, ShaderGraphNodeKind.Kuwahara => 7,
            ShaderGraphNodeKind.FlowWarp => 8, ShaderGraphNodeKind.Median => 9, _ => -1
        };
        internal static bool IsImageFilter(ShaderGraphNode node) => node.kind == ShaderGraphNodeKind.Sharpen ? node.adaptiveSharpen :
            ImageFilterMode(node.kind) >= 0 || node.kind is ShaderGraphNodeKind.PaletteQuantize or ShaderGraphNodeKind.Truchet;
        internal static string ImageFilterSource(int mode) => mode < 3 ? "Height" : mode == 5 ? "Normal" : mode == 8 ? "Flow" : "In";
        static string ImageFilterCategory(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.HeightCurvature or ShaderGraphNodeKind.HeightToAO or ShaderGraphNodeKind.HeightShadow => "Height",
            ShaderGraphNodeKind.PaletteQuantize => "Color/Adjustment",
            ShaderGraphNodeKind.NormalCurvature => "Normal", ShaderGraphNodeKind.FlowWarp => "UV/Distortion",
            ShaderGraphNodeKind.Truchet => "Procedural/Pattern", ShaderGraphNodeKind.Median or ShaderGraphNodeKind.Kuwahara => "Filter",
            ShaderGraphNodeKind.Supersample or ShaderGraphNodeKind.MakeTileable => "Filter", _ => null
        };
        static void BuildImageFilterPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.FlowWarp] = new[] { UV(), P("Flow", ShaderGraphValueType.Float2), N("Strength", 16), N("Steps", 8) };
            map[ShaderGraphNodeKind.Median] = new[] { P("In"), UV(), N("Radius", 1) };
            map[ShaderGraphNodeKind.Kuwahara] = new[] { P("In"), UV(), N("Radius", 2) };
            map[ShaderGraphNodeKind.NormalCurvature] = new[] { NormalPort("Normal", true), UV(), N("Radius", 1), N("Strength", 1) };
            map[ShaderGraphNodeKind.Truchet] = new[] { UV(), P("Scale", ShaderGraphValueType.Float2, 8), N("Width", .1f), N("Feather", 0), N("Seed", 1337) };
            map[ShaderGraphNodeKind.HeightCurvature] = new[] { N("Height"), UV(), N("Radius", 2), N("Strength", 8) };
            map[ShaderGraphNodeKind.HeightToAO] = new[] { N("Height"), UV(), N("Radius", 16), N("HeightScale", 16),
                N("Strength", 1), N("Bias", .05f), N("Directions", 8), N("Steps", 4) };
            map[ShaderGraphNodeKind.HeightShadow] = new[] { N("Height"), UV(),
                new ShaderGraphPort("LightDirection", ShaderGraphValueType.Float3, new Vector4(.5f, .5f, 1, 0)),
                N("HeightScale", 16), N("Distance", 64), N("Steps", 32), N("Bias", .05f), N("Softness", .1f) };
            map[ShaderGraphNodeKind.PaletteQuantize] = new[] { P("Color", ShaderGraphValueType.Color, 1), P("Palette", ShaderGraphValueType.Gradient), N("Colors", 8) };
            map[ShaderGraphNodeKind.Supersample] = new[] { P("In"), UV() };
            map[ShaderGraphNodeKind.MakeTileable] = new[] { P("In"), UV(), N("BlendWidth", .5f) };
        }
        internal static bool TryImageFilterOutputType(ShaderGraphNode node, out ShaderGraphValueType type)
        {
            if (node.kind == ShaderGraphNodeKind.FlowWarp) { type = ShaderGraphValueType.Float2; return true; }
            type = node.kind == ShaderGraphNodeKind.PaletteQuantize ? ShaderGraphValueType.Color : ShaderGraphValueType.Float;
            return node.kind is ShaderGraphNodeKind.HeightCurvature or ShaderGraphNodeKind.HeightToAO or ShaderGraphNodeKind.HeightShadow or ShaderGraphNodeKind.PaletteQuantize or ShaderGraphNodeKind.NormalCurvature or ShaderGraphNodeKind.Truchet;
        }
    }
}
