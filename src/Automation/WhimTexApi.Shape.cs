using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetShape(ShapeLayerBehaviour layer, JObject value)
        {
            Keys(value, "kind", "fill", "fillColor", "stroke", "strokeColor", "strokeWidth", "roundness", "cornerRoundness", "linkCorners", "sides", "innerRadius");
            layer.kind = Enum(value, "kind", layer.kind);
            layer.fill = Bool(value, "fill", layer.fill);
            layer.stroke = Bool(value, "stroke", layer.stroke);
            if (value["fillColor"] != null) layer.fillColor = Color(value["fillColor"]);
            if (value["strokeColor"] != null) layer.strokeColor = Color(value["strokeColor"]);
            layer.strokeWidth = Number(value, "strokeWidth", layer.strokeWidth, 0f, 8192f);
            if (value["roundness"] != null)
            {
                layer.roundness = Number(value, "roundness", layer.roundness, 0f, 1f);
                layer.cornerRoundness = UnityEngine.Vector4.one * layer.roundness;
            }
            if (value["cornerRoundness"] != null)
            {
                Require(value["cornerRoundness"] is JArray array && array.Count == 4,
                    "cornerRoundness must have four values: top-left, top-right, bottom-right, bottom-left.");
                var corners = (JArray)value["cornerRoundness"];
                var result = layer.GetCornerRoundness();
                for (int i = 0; i < 4; i++) result[i] = Number(corners[i], "cornerRoundness", 0f, 1f);
                layer.cornerRoundness = result;
            }
            layer.linkCorners = Bool(value, "linkCorners", layer.linkCorners);
            layer.sides = Int(value, "sides", layer.sides, 3, 32);
            layer.innerRadius = Number(value, "innerRadius", layer.innerRadius, .01f, 1f);
        }

        private static JObject ShapeSnapshot(ShapeLayerBehaviour layer) => new JObject
        {
            ["kind"] = layer.kind.ToString(), ["fill"] = layer.fill, ["fillColor"] = Json(layer.fillColor),
            ["stroke"] = layer.stroke, ["strokeColor"] = Json(layer.strokeColor), ["strokeWidth"] = layer.strokeWidth,
            ["roundness"] = layer.GetCornerRoundness().x,
            ["cornerRoundness"] = new JArray(layer.GetCornerRoundness().x, layer.GetCornerRoundness().y,
                layer.GetCornerRoundness().z, layer.GetCornerRoundness().w),
            ["linkCorners"] = layer.linkCorners, ["sides"] = layer.sides, ["innerRadius"] = layer.innerRadius
        };
    }
}
