using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphHeightSource { Texture, Value }

    internal static partial class ShaderGraphNodes
    {
        internal static bool IsFilter(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen
            or ShaderGraphNodeKind.EdgeDetection or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode or ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur
            or ShaderGraphNodeKind.SlopeBlur or ShaderGraphNodeKind.BilateralBlur or ShaderGraphNodeKind.Buffer or ShaderGraphNodeKind.Fractal
            or ShaderGraphNodeKind.RGBShift;
        static bool UsesFilterKernel(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode;
        static readonly ShaderGraphPort[] heightValuePorts = {
            N("Height"), UV(), N("Strength", 8), N("Radius", 1), BoolPort("FlipX"), BoolPort("FlipY") };
        static readonly string[] filterKernels = { "3 x 3", "5 x 5", "9 x 9" };
        static readonly string[] radialBlurModes = { "Spin", "Zoom" };
        internal static readonly string[] FractalModes = { "FBm", "Ridged", "Ping Pong", "Multifractal",
            "Hybrid Multifractal", "Hetero Terrain", "Ridged Multifractal" };
        internal static readonly ShaderGraphFractal[] FractalModeValues = { ShaderGraphFractal.FBm,
            ShaderGraphFractal.Ridged, ShaderGraphFractal.PingPong, ShaderGraphFractal.Multifractal,
            ShaderGraphFractal.HybridMultifractal, ShaderGraphFractal.HeteroTerrain, ShaderGraphFractal.RidgedMultifractal };
        internal static bool IsMusgraveFractal(ShaderGraphFractal mode) => mode is ShaderGraphFractal.Multifractal
            or ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.HeteroTerrain or ShaderGraphFractal.RidgedMultifractal;
        internal static bool FractalInputVisible(ShaderGraphFractal mode, string name) => name switch
        {
            "WeightedStrength" => !IsMusgraveFractal(mode),
            "PingPongStrength" => mode == ShaderGraphFractal.PingPong,
            "Bias" => mode is ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.HeteroTerrain or ShaderGraphFractal.RidgedMultifractal,
            "Feedback" => mode is ShaderGraphFractal.HybridMultifractal or ShaderGraphFractal.RidgedMultifractal,
            _ => true
        };
        static void BuildSamplingPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            foreach (var kind in new[] { ShaderGraphNodeKind.Blur, ShaderGraphNodeKind.Dilate, ShaderGraphNodeKind.Erode })
                map[kind] = new[] { P("In"), UV(), N("Radius", 1) };
            map[ShaderGraphNodeKind.Sharpen] = new[] { P("In"), UV(), N("Radius", 1), N("Strength", 1) };
            map[ShaderGraphNodeKind.EdgeDetection] = new[] { P("In"), UV(), N("Radius", 1), N("Strength", 1) };
            map[ShaderGraphNodeKind.DirectionalBlur] = new[] { P("In"), UV(), N("Radius", 8), N("Angle"), N("Samples", 17) };
            map[ShaderGraphNodeKind.RadialBlur] = new[] { P("In"), UV(), Center(), N("Angle", 10), N("Strength", .1f), N("Samples", 17) };
            map[ShaderGraphNodeKind.RGBShift] = new[] { ColorPort("In"), UV(), N("Amount", 2), N("Blend", 1), Center(), N("Falloff", 1), N("Angle") };
            map[ShaderGraphNodeKind.Buffer] = new[] { P("In"), UV() };
            map[ShaderGraphNodeKind.Fractal] = new[] { P("In"), N("Octaves", 3), N("Lacunarity", 2), N("Gain", .5f),
                N("WeightedStrength"), N("PingPongStrength", 2), N("Bias", 1), N("Feedback", 2), N("SeedStep", 1) };
        }
    }
}
