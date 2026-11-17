using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphHeightSource { Texture, Value }
    public enum ShaderGraphFilterQuality { Standard, High }

    internal static partial class ShaderGraphNodes
    {
        internal static ShaderGraphPort InactiveHeightInput(ShaderGraphNode node) =>
            node.heightSource == ShaderGraphHeightSource.Value ? P("HeightTexture", ShaderGraphValueType.Texture) :
            N("HeightValue", IsParallax(node.kind) ? .5f : 0);

        internal static void ChangeHeightSource(ShaderGraph graph, ShaderGraphNode node, ShaderGraphHeightSource source)
        {
            if (!UsesHeightTexture(node.kind) || node.heightSource == source) return;
            string restore = InactiveHeightInput(node).name;
            string retain = node.heightSource == ShaderGraphHeightSource.Texture ? "HeightTexture" : "HeightValue";
            foreach (var input in node.inputs)
                input.port = input.port == "Height" ? retain : input.port == restore ? "Height" : input.port;
            foreach (var edge in graph.edges)
                if (edge.inputNode == node.id)
                    edge.inputPort = edge.inputPort == "Height" ? retain : edge.inputPort == restore ? "Height" : edge.inputPort;
            node.heightSource = source;
        }

        internal static bool IsFilter(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen
            or ShaderGraphNodeKind.EdgeDetection or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode or ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur
            or ShaderGraphNodeKind.SlopeBlur or ShaderGraphNodeKind.BilateralBlur or ShaderGraphNodeKind.Buffer or ShaderGraphNodeKind.Fractal
            or ShaderGraphNodeKind.Median or ShaderGraphNodeKind.Kuwahara or ShaderGraphNodeKind.RGBShift or ShaderGraphNodeKind.Supersample or ShaderGraphNodeKind.MakeTileable;
        static readonly ShaderGraphPort[] heightValuePorts = {
            N("Height"), UV(), N("Strength", 8), N("Radius", 1), BoolPort("FlipX"), BoolPort("FlipY") };
        static bool UsesFilterQuality(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode;
        internal static readonly string[] filterQuality = { "Standard", "High" };
        internal static bool IsPathBlur(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur;
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
            map[ShaderGraphNodeKind.DirectionalBlur] = new[] { P("In"), UV(), N("Radius", 8), N("Angle") };
            map[ShaderGraphNodeKind.RadialBlur] = new[] { P("In"), UV(), Center(), N("Angle", 10), N("Strength", .1f) };
            map[ShaderGraphNodeKind.RGBShift] = new[] { ColorPort("In"), UV(), N("Amount", 2), N("Blend", 1), Center(), N("Falloff", 1), N("Angle") };
            map[ShaderGraphNodeKind.Buffer] = new[] { P("In"), UV() };
            map[ShaderGraphNodeKind.Fractal] = new[] { P("In"), N("Octaves", 3), N("Lacunarity", 2), N("Gain", .5f),
                N("WeightedStrength"), N("PingPongStrength", 2), N("Bias", 1), N("Feedback", 2), N("SeedStep", 1) };
        }
    }
}
