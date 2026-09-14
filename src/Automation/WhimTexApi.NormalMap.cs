using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetNormalMap(NormalMapLayerBehaviour layer, JObject value)
        {
            Keys(value, "mode", "sourceChannel", "inputSpace", "edges", "derivative", "alphaMode", "output", "encoding",
                "strength", "blackLevel", "whiteLevel", "gamma", "smoothing", "mediumRadius", "largeRadius",
                "fineDetail", "mediumDetail", "largeDetail", "lightRemoval", "inverted", "flipX", "flipY", "ignoreTransparent");
            layer.mode = Enum(value, "mode", layer.mode);
            layer.sourceChannel = Enum(value, "sourceChannel", layer.sourceChannel);
            layer.inputSpace = Enum(value, "inputSpace", layer.inputSpace);
            layer.edges = Enum(value, "edges", layer.edges);
            layer.derivative = Enum(value, "derivative", layer.derivative);
            layer.alphaMode = Enum(value, "alphaMode", layer.alphaMode);
            layer.output = Enum(value, "output", layer.output);
            layer.encoding = Enum(value, "encoding", layer.encoding);
            layer.strength = Number(value, "strength", layer.strength, 0f, 128f);
            layer.blackLevel = Number(value, "blackLevel", layer.blackLevel, 0f, 1f);
            layer.whiteLevel = Number(value, "whiteLevel", layer.whiteLevel, .0001f, 16f);
            layer.gamma = Number(value, "gamma", layer.gamma, .05f, 8f);
            layer.smoothing = Number(value, "smoothing", layer.smoothing, 0f, 64f);
            layer.mediumRadius = Number(value, "mediumRadius", layer.mediumRadius, .5f, 128f);
            layer.largeRadius = Number(value, "largeRadius", layer.largeRadius, .5f, 512f);
            layer.fineDetail = Number(value, "fineDetail", layer.fineDetail, 0f, 8f);
            layer.mediumDetail = Number(value, "mediumDetail", layer.mediumDetail, 0f, 8f);
            layer.largeDetail = Number(value, "largeDetail", layer.largeDetail, 0f, 8f);
            layer.lightRemoval = Number(value, "lightRemoval", layer.lightRemoval, 0f, 1f);
            layer.inverted = Bool(value, "inverted", layer.inverted);
            layer.flipX = Bool(value, "flipX", layer.flipX);
            layer.flipY = Bool(value, "flipY", layer.flipY);
            layer.ignoreTransparent = Bool(value, "ignoreTransparent", layer.ignoreTransparent);
            Require(layer.whiteLevel > layer.blackLevel, "Normal Map whiteLevel must exceed blackLevel.");
            Require(layer.largeRadius >= layer.mediumRadius, "Normal Map largeRadius must be at least mediumRadius.");
        }

        private static JObject NormalMapSnapshot(NormalMapLayerBehaviour layer) => new JObject
        {
            ["mode"] = layer.mode.ToString(), ["sourceChannel"] = layer.sourceChannel.ToString(),
            ["inputSpace"] = layer.inputSpace.ToString(), ["edges"] = layer.edges.ToString(),
            ["derivative"] = layer.derivative.ToString(), ["alphaMode"] = layer.alphaMode.ToString(),
            ["output"] = layer.output.ToString(), ["encoding"] = layer.encoding.ToString(),
            ["strength"] = layer.strength, ["blackLevel"] = layer.blackLevel, ["whiteLevel"] = layer.whiteLevel,
            ["gamma"] = layer.gamma, ["smoothing"] = layer.smoothing, ["mediumRadius"] = layer.mediumRadius,
            ["largeRadius"] = layer.largeRadius, ["fineDetail"] = layer.fineDetail,
            ["mediumDetail"] = layer.mediumDetail, ["largeDetail"] = layer.largeDetail,
            ["lightRemoval"] = layer.lightRemoval, ["inverted"] = layer.inverted,
            ["flipX"] = layer.flipX, ["flipY"] = layer.flipY, ["ignoreTransparent"] = layer.ignoreTransparent
        };
    }
}
