using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetBlur(BlurLayerBehaviour layer, JObject value)
        {
            Keys(value, "mode", "strength", "radius", "distance", "angle", "arc", "center", "direction", "edges");
            layer.mode = Enum(value, "mode", layer.mode);
            layer.strength = Number(value, "strength", layer.strength, 0f, BlurLayerBehaviour.MaximumStrength);
            layer.radius = Number(value, "radius", layer.radius, 0f, BlurLayerBehaviour.MaximumRadius);
            layer.distance = Number(value, "distance", layer.distance, 0f, BlurLayerBehaviour.MaximumDistance);
            layer.angle = Number(value, "angle", layer.angle, -180f, 180f);
            layer.arc = Number(value, "arc", layer.arc, 0f, 360f);
            if (value["center"] != null)
            {
                Require(value["center"] is JArray array && array.Count == 2, "center must be [x, y].");
                layer.center = new Vector2(Number(value["center"][0], "center.x", 0f, 1f),
                    Number(value["center"][1], "center.y", 0f, 1f));
            }
            layer.direction = Enum(value, "direction", layer.direction);
            layer.edges = Enum(value, "edges", layer.edges);
        }

        private static JObject BlurSnapshot(BlurLayerBehaviour layer) => new JObject
        {
            ["mode"] = layer.mode.ToString(), ["strength"] = layer.strength,
            ["radius"] = layer.radius, ["distance"] = layer.distance, ["angle"] = layer.angle,
            ["arc"] = layer.arc, ["center"] = Json(layer.center),
            ["direction"] = layer.direction.ToString(), ["edges"] = layer.edges.ToString()
        };
    }
}
