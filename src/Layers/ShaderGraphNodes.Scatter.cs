using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static readonly string[] scatterInstanceOutputs = { "UV", "Index", "Random", "Center" };
        internal const int ScatterMaximumCount = 1024;
        internal const int ScatterMaximumSize = 16;

        static void BuildScatterPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.Scatter] = new[] {
                P("Source", null, 1), UV(), P2("Count", 8, 8), P2("Size", .8f, .8f), N("SizeRandom", .25f),
                N("Rotation"), N("RotationRandom", 180), N("Jitter", 1), N("Density", 1), N("Mask", 1),
                N("ScaleMap", 1), N("RotationMap"), N("Seed", 1337), P2("Period", 1, 1)
            };
        }

        internal static bool IsScatterSampledInput(ShaderGraphNode node, string port) => node.kind == ShaderGraphNodeKind.Scatter &&
            port is "Source" or "Density" or "Mask" or "ScaleMap" or "RotationMap";
    }
}
