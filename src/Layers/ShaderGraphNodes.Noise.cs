using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphNoiseDimensions { OneD, TwoD, ThreeD }
    public enum ShaderGraphNoiseDistribution { Uniform, FixedRadius }
    public enum ShaderGraphNoiseMetric { Euclidean, EuclideanSquared, Manhattan, Hybrid }
    public enum ShaderGraphNoiseOutput { CellValue, F1, F2, Add, Subtract, Multiply, Divide, SmoothF1 }

    internal static partial class ShaderGraphNodes
    {
        internal static bool IsNoise(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.PerlinNoise or ShaderGraphNodeKind.ValueNoise or ShaderGraphNodeKind.ValueCubicNoise or
            ShaderGraphNodeKind.OpenSimplex2Noise or ShaderGraphNodeKind.OpenSimplex2SNoise or ShaderGraphNodeKind.FlowNoise or
            ShaderGraphNodeKind.CellularNoise or ShaderGraphNodeKind.VoronoiEdgeNoise or ShaderGraphNodeKind.VoronoiseNoise or
            ShaderGraphNodeKind.GaborNoise or ShaderGraphNodeKind.SpotsNoise || IsGrainNoise(kind) || IsWarpNoise(kind);
        internal static bool IsGrainNoise(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.WhiteNoise or ShaderGraphNodeKind.BlueNoise;
        internal static bool IsWarpNoise(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.OpenSimplex2WarpNoise or
            ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise or ShaderGraphNodeKind.BasicGridWarpNoise;
        internal static bool HasNoiseDerivatives(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.PerlinNoise or ShaderGraphNodeKind.ValueNoise or ShaderGraphNodeKind.ValueCubicNoise or
            ShaderGraphNodeKind.OpenSimplex2Noise or ShaderGraphNodeKind.OpenSimplex2SNoise or ShaderGraphNodeKind.FlowNoise or ShaderGraphNodeKind.GaborNoise;
        internal static int NoiseDimensions(ShaderGraphNode node) => (int)node.noiseDimensions + 1;
        internal static ShaderGraphValueType NoisePositionType(ShaderGraphNode node) => IsGrainNoise(node.kind)
            ? ShaderGraphValueType.Float2 : (ShaderGraphValueType)(NoiseDimensions(node) - 1);
        static string NoiseCategory(ShaderGraphNodeKind kind) => !IsNoise(kind) ? null :
            IsGrainNoise(kind) ? "Procedural/Noise/Grain" : IsWarpNoise(kind) ? "Procedural/Noise/Warp" : "Procedural/Noise";

        static readonly string[] noiseValue = { "Value" }, noiseDerivative = { "Value", "Gradient" },
            noiseField = { "Value", "Gradient", "Curl" }, noiseGrain = { "Value", "Color" },
            noiseCellular = { "Value", "F1", "F2", "CellValue", "ToPoint" }, noiseWarp = { "Displacement", "Position" },
            noiseGabor1D = { "Value", "Gradient", "Phase", "Intensity" }, noiseGabor = { "Value", "Gradient", "Curl", "Phase", "Intensity" };
        internal static string[] NoiseOutputs(ShaderGraphNode node)
        {
            if (!IsNoise(node.kind)) return null;
            if (IsGrainNoise(node.kind)) return noiseGrain;
            if (IsWarpNoise(node.kind)) return noiseWarp;
            if (node.kind == ShaderGraphNodeKind.CellularNoise) return noiseCellular;
            if (node.kind == ShaderGraphNodeKind.GaborNoise) return NoiseDimensions(node) == 1 ? noiseGabor1D : noiseGabor;
            return HasNoiseDerivatives(node.kind) ? (NoiseDimensions(node) == 1 ? noiseDerivative : noiseField) : noiseValue;
        }
        internal static bool TryNoiseOutputType(ShaderGraphNode node, string port, out ShaderGraphValueType type)
        {
            type = port == "Color" ? ShaderGraphValueType.Color :
                port is "Gradient" or "Curl" or "ToPoint" or "Displacement" or "Position" ? NoisePositionType(node) : ShaderGraphValueType.Float;
            return IsNoise(node.kind) && Array.IndexOf(NoiseOutputs(node), port) >= 0;
        }

        // Fixed port layouts are shared by all nodes, including inspector/catalog queries.
        static readonly Dictionary<(ShaderGraphNodeKind, ShaderGraphNoiseDimensions), ShaderGraphPort[]> noisePorts = BuildNoisePorts();
        internal static IReadOnlyList<ShaderGraphPort> NoiseInputs(ShaderGraphNode node) =>
            noisePorts.TryGetValue((node.kind, node.noiseDimensions), out var result) ? result : null;
        static Dictionary<(ShaderGraphNodeKind, ShaderGraphNoiseDimensions), ShaderGraphPort[]> BuildNoisePorts()
        {
            var result = new Dictionary<(ShaderGraphNodeKind, ShaderGraphNoiseDimensions), ShaderGraphPort[]>();
            // Do not depend on partial-class static initialization order.
            foreach (ShaderGraphNodeKind kind in Enum.GetValues(typeof(ShaderGraphNodeKind)))
            {
                if (!IsNoise(kind)) continue;
                for (int dimension = 0; dimension < (IsGrainNoise(kind) ? 2 : 3); dimension++)
                {
                    var type = IsGrainNoise(kind) ? ShaderGraphValueType.Float2 : (ShaderGraphValueType)dimension;
                    var list = new List<ShaderGraphPort> { new ShaderGraphPort("Position", type, uvDefault: true) };
                    if (IsGrainNoise(kind))
                    {
                        list.Add(P("Offset", type)); list.Add(N("GrainSize", 1)); list.Add(N("Direction")); list.Add(N("Seed", 1337));
                        list.Add(new ShaderGraphPort("ImageSize", ShaderGraphValueType.Float2, canvasSizeDefault: true));
                    }
                    else
                    {
                        list.Add(P("Scale", type, 8)); list.Add(P("Offset", type)); list.Add(P("Period", type)); list.Add(N("Seed", 1337));
                        switch (kind)
                        {
                            case ShaderGraphNodeKind.FlowNoise: list.Add(N("Phase")); break;
                            case ShaderGraphNodeKind.CellularNoise: case ShaderGraphNodeKind.VoronoiseNoise:
                                list.Add(N("Jitter", 1)); list.Add(N("Smoothness", .5f)); break;
                            case ShaderGraphNodeKind.VoronoiEdgeNoise: list.Add(N("Jitter", 1)); break;
                            case ShaderGraphNodeKind.GaborNoise:
                                list.Add(N("Frequency", 2)); list.Add(N("Bandwidth", 1)); list.Add(N("Anisotropy", .8f));
                                list.Add(new ShaderGraphPort("Direction", type, new Vector4(1, 0, 0, 0))); break;
                            case ShaderGraphNodeKind.SpotsNoise:
                                list.Add(N("Density", 1)); list.Add(N("Radius", .35f)); list.Add(N("Softness", .5f)); list.Add(N("Elongation", 1));
                                list.Add(new ShaderGraphPort("Direction", type, new Vector4(1, 0, 0, 0))); break;
                        }
                        if (IsWarpNoise(kind))
                        {
                            list.Add(N("Strength", .1f)); list.Add(N("Octaves", 3)); list.Add(N("Lacunarity", 2)); list.Add(N("Gain", .5f));
                        }
                    }
                    result.Add((kind, (ShaderGraphNoiseDimensions)dimension), list.ToArray());
                }
            }
            return result;
        }
        internal static string NoiseInputInactiveReason(ShaderGraphNode node, string input)
        {
            if (!IsNoise(node.kind)) return null;
            if (IsGrainNoise(node.kind)) return input == "Direction" && node.noiseDimensions != ShaderGraphNoiseDimensions.OneD ? "Used only by 1D grain." : null;
            if (IsWarpNoise(node.kind) && input is "Octaves" or "Lacunarity" or "Gain")
                return node.fractal == ShaderGraphFractal.None ? "Used when Fractal is enabled." : null;
            if (input == "Smoothness" && node.kind == ShaderGraphNodeKind.CellularNoise && node.noiseOutput != ShaderGraphNoiseOutput.SmoothF1) return "Used only by Smooth F1.";
            if (NoiseDimensions(node) == 1 && input is "Direction" or "Anisotropy" or "Elongation") return "Used only in 2D and 3D.";
            return null;
        }
        internal static void ChangeNoiseDimensions(ShaderGraph graph, ShaderGraphNode node, ShaderGraphNoiseDimensions dimensions)
        {
            if (!IsNoise(node.kind) || !Enum.IsDefined(typeof(ShaderGraphNoiseDimensions), dimensions) ||
                IsGrainNoise(node.kind) && dimensions == ShaderGraphNoiseDimensions.ThreeD) throw new ArgumentOutOfRangeException(nameof(dimensions));
            node.noiseDimensions = dimensions;
            foreach (var p in Inputs(node))
            {
                var input = node.FindInput(p.name);
                if (input == null) continue;
                // Keep hidden components so a temporary 3D -> 2D -> 3D switch is lossless.
                input.type = p.type.Value;
            }
            var outputs = Outputs(node);
            graph.edges.RemoveAll(e => e.outputNode == node.id && Array.IndexOf(outputs, e.outputPort) < 0);
        }
    }
}
