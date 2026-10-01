using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetNoise(NoiseLayerBehaviour layer, JObject value)
        {
            Keys(value, "noiseType", "seed", "scale", "offset", "fractal", "octaves", "lacunarity", "gain",
                "weightedStrength", "pingPongStrength", "cellularDistance", "cellularReturn", "cellularJitter",
                "warp", "warpStrength", "encoding", "inverted", "dimensions", "direction", "whiteNoiseColor", "whiteNoiseSize", "gradient", "periodic", "linkScale");
            layer.noiseType = Enum(value, "noiseType", layer.noiseType);
            layer.whiteNoiseColor = Enum(value, "whiteNoiseColor", layer.whiteNoiseColor);
            layer.whiteNoiseSize = Number(value, "whiteNoiseSize", layer.whiteNoiseSize, 1f, 1024f);
            layer.dimensions = Enum(value, "dimensions", layer.dimensions);
            layer.direction = Number(value, "direction", layer.direction, -180f, 180f);
            layer.seed = Int(value, "seed", layer.seed, int.MinValue, int.MaxValue);
            layer.periodic = Enum(value, "periodic", layer.periodic);
            layer.linkScale = Bool(value, "linkScale", layer.linkScale);
            if (value["scale"] is JArray axes)
            {
                Require(axes.Count == 2, "Noise scale must be a number or [x,y].");
                layer.Scale = new UnityEngine.Vector2(Number(axes[0], "scale.x", .01f, 1000f), Number(axes[1], "scale.y", .01f, 1000f));
            }
            else if (value["scale"] != null)
            {
                float scale = Number(value["scale"], "scale", .01f, 1000f);
                layer.Scale = new UnityEngine.Vector2(scale, scale);
            }
            if (value["offset"] != null)
            {
                var offset = value["offset"] as JArray;
                Require(offset != null && (offset.Count == 2 || offset.Count == 3), "Noise offset must be [x,y] or [x,y,z].");
                layer.offset = new UnityEngine.Vector3(Number(offset[0], "offset.x", -10000, 10000),
                    Number(offset[1], "offset.y", -10000, 10000), offset.Count == 3
                        ? Number(offset[2], "offset.z", -10000, 10000) : layer.offset.z);
            }
            layer.fractal = Enum(value, "fractal", layer.fractal);
            layer.octaves = Int(value, "octaves", layer.octaves, 1, 8);
            layer.lacunarity = Number(value, "lacunarity", layer.lacunarity, 1f, 4f);
            layer.gain = Number(value, "gain", layer.gain, 0f, 1f);
            layer.weightedStrength = Number(value, "weightedStrength", layer.weightedStrength, 0f, 1f);
            layer.pingPongStrength = Number(value, "pingPongStrength", layer.pingPongStrength, .01f, 8f);
            layer.cellularDistance = Enum(value, "cellularDistance", layer.cellularDistance);
            layer.cellularReturn = Enum(value, "cellularReturn", layer.cellularReturn);
            layer.cellularJitter = Number(value, "cellularJitter", layer.cellularJitter, 0f, 1f);
            layer.warp = Enum(value, "warp", layer.warp);
            layer.warpStrength = Number(value, "warpStrength", layer.warpStrength, 0f, 100f);
            layer.encoding = Enum(value, "encoding", layer.encoding);
            layer.inverted = Bool(value, "inverted", layer.inverted);
            if (value["gradient"] != null) layer.gradient = ReadGradient(value["gradient"]);
        }

        private static JObject NoiseSnapshot(NoiseLayerBehaviour layer) => new JObject
        {
            ["noiseType"] = layer.noiseType.ToString(), ["seed"] = layer.seed,
            ["whiteNoiseColor"] = layer.whiteNoiseColor.ToString(), ["whiteNoiseSize"] = layer.whiteNoiseSize,
            ["dimensions"] = layer.dimensions.ToString(), ["direction"] = layer.direction,
            ["scale"] = Json(layer.Scale), ["linkScale"] = layer.linkScale, ["periodic"] = layer.periodic.ToString(),
            ["offset"] = new JArray(layer.offset.x, layer.offset.y, layer.offset.z),
            ["fractal"] = layer.fractal.ToString(), ["octaves"] = layer.octaves,
            ["lacunarity"] = layer.lacunarity, ["gain"] = layer.gain,
            ["weightedStrength"] = layer.weightedStrength, ["pingPongStrength"] = layer.pingPongStrength,
            ["cellularDistance"] = layer.cellularDistance.ToString(), ["cellularReturn"] = layer.cellularReturn.ToString(),
            ["cellularJitter"] = layer.cellularJitter, ["warp"] = layer.warp.ToString(),
            ["warpStrength"] = layer.warpStrength, ["encoding"] = layer.encoding.ToString(), ["inverted"] = layer.inverted,
            ["gradient"] = GradientSnapshot(layer.gradient)
        };
    }
}
