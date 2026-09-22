using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetSharpen(SharpenLayerBehaviour layer, JObject value)
        {
            Keys(value, "algorithm", "strength", "radius", "threshold", "noiseReduction", "haloSuppression", "channelMode", "edges");
            layer.algorithm = Enum(value, "algorithm", layer.algorithm);
            layer.strength = Number(value, "strength", layer.strength, 0f, SharpenLayerBehaviour.MaximumStrength);
            layer.radius = Number(value, "radius", layer.radius, 0f, SharpenLayerBehaviour.MaximumRadius);
            layer.threshold = Number(value, "threshold", layer.threshold, 0f, 1f);
            layer.noiseReduction = Number(value, "noiseReduction", layer.noiseReduction, 0f, 1f);
            layer.haloSuppression = Number(value, "haloSuppression", layer.haloSuppression, 0f, 1f);
            layer.channelMode = Enum(value, "channelMode", layer.channelMode);
            layer.edges = Enum(value, "edges", layer.edges);
        }

        private static JObject SharpenSnapshot(SharpenLayerBehaviour layer) => new JObject
        {
            ["algorithm"] = layer.algorithm.ToString(), ["strength"] = layer.strength,
            ["radius"] = layer.radius,
            ["threshold"] = layer.threshold, ["noiseReduction"] = layer.noiseReduction,
            ["haloSuppression"] = layer.haloSuppression, ["channelMode"] = layer.channelMode.ToString(),
            ["edges"] = layer.edges.ToString()
        };
    }
}
