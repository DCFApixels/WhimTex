using Newtonsoft.Json.Linq;
using static DCFApixels.SpriteEditor.AgentJson;

namespace DCFApixels.SpriteEditor
{
    public static partial class SpriteEditorApi
    {
        private static void SetNoise(NoiseLayerBehaviour layer, JObject value)
        {
            Keys(value, "noiseType", "seed", "scale", "offset", "fractal", "octaves", "lacunarity", "gain",
                "weightedStrength", "pingPongStrength", "cellularDistance", "cellularReturn", "cellularJitter",
                "warp", "warpStrength", "encoding", "inverted", "dimensions", "direction", "whiteNoiseColor", "whiteNoiseSize");
            layer.noiseType = Enum(value, "noiseType", layer.noiseType);
            layer.whiteNoiseColor = Enum(value, "whiteNoiseColor", layer.whiteNoiseColor);
            layer.whiteNoiseSize = Number(value, "whiteNoiseSize", layer.whiteNoiseSize, 1f, 1024f);
            layer.dimensions = Enum(value, "dimensions", layer.dimensions);
            layer.direction = Number(value, "direction", layer.direction, -180f, 180f);
            layer.seed = Int(value, "seed", layer.seed, int.MinValue, int.MaxValue);
            layer.scale = Number(value, "scale", layer.scale, .01f, 1000f);
            if (value["offset"] != null)
            {
                var offset = Vector(value["offset"], "offset");
                Require(offset.x >= -10000f && offset.x <= 10000f && offset.y >= -10000f && offset.y <= 10000f,
                    "Noise offset components must be between -10000 and 10000.");
                layer.offset = offset;
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
        }

        private static JObject NoiseSnapshot(NoiseLayerBehaviour layer) => new JObject
        {
            ["noiseType"] = layer.noiseType.ToString(), ["seed"] = layer.seed,
            ["whiteNoiseColor"] = layer.whiteNoiseColor.ToString(), ["whiteNoiseSize"] = layer.whiteNoiseSize,
            ["dimensions"] = layer.dimensions.ToString(), ["direction"] = layer.direction,
            ["scale"] = layer.scale, ["offset"] = Json(layer.offset),
            ["fractal"] = layer.fractal.ToString(), ["octaves"] = layer.octaves,
            ["lacunarity"] = layer.lacunarity, ["gain"] = layer.gain,
            ["weightedStrength"] = layer.weightedStrength, ["pingPongStrength"] = layer.pingPongStrength,
            ["cellularDistance"] = layer.cellularDistance.ToString(), ["cellularReturn"] = layer.cellularReturn.ToString(),
            ["cellularJitter"] = layer.cellularJitter, ["warp"] = layer.warp.ToString(),
            ["warpStrength"] = layer.warpStrength, ["encoding"] = layer.encoding.ToString(), ["inverted"] = layer.inverted
        };
    }
}
