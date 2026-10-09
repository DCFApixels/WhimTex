using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetShape(ShapeLayerBehaviour layer, JObject value)
        {
            Keys(value, "kind", "fill", "fillColor", "stroke", "strokeColor", "strokeWidth", "arcThickness", "strokePosition", "lineCap", "edgeMode",
                "feather", "featherPosition", "rectangleCorners", "polygonCorners", "outerCorner", "innerCorner", "linkCorners", "sides", "innerRadius", "startAngle", "sweepAngle");
            layer.kind = Enum(value, "kind", layer.kind);
            layer.fill = Bool(value, "fill", layer.fill);
            layer.stroke = Bool(value, "stroke", layer.stroke);
            if (value["fillColor"] != null) layer.fillColor = Color(value["fillColor"]);
            if (value["strokeColor"] != null) layer.strokeColor = Color(value["strokeColor"]);
            layer.strokeWidth = Number(value, "strokeWidth", layer.strokeWidth, 0f, 8192f);
            layer.arcThickness = Number(value, "arcThickness", layer.arcThickness, 0f, 8192f);
            layer.strokePosition = Enum(value, "strokePosition", layer.strokePosition);
            layer.lineCap = Enum(value, "lineCap", layer.lineCap);
            layer.edgeMode = Enum(value, "edgeMode", layer.edgeMode);
            layer.feather = Number(value, "feather", layer.feather, 0f, 8192f);
            layer.featherPosition = Enum(value, "featherPosition", layer.featherPosition);
            layer.linkCorners = Bool(value, "linkCorners", layer.linkCorners);
            layer.sides = Int(value, "sides", layer.sides, 3, 32);
            layer.innerRadius = Number(value, "innerRadius", layer.innerRadius, .01f, 1f);
            layer.startAngle = Number(value, "startAngle", layer.startAngle, -360000f, 360000f);
            layer.sweepAngle = Number(value, "sweepAngle", layer.sweepAngle, 0f, 360f);
            layer.EnsureCorners();
            ReadCorners(value, "rectangleCorners", layer.rectangleCorners);
            ReadCorners(value, "polygonCorners", layer.polygonCorners);
            layer.outerCorner = ReadCorner(value, "outerCorner", layer.outerCorner);
            layer.innerCorner = ReadCorner(value, "innerCorner", layer.innerCorner);
        }

        private static ShapeLayerBehaviour.Corner ReadCorner(JObject value, string key, ShapeLayerBehaviour.Corner current)
        {
            if (value[key] == null) return current;
            var node = value[key] as JObject;
            Require(node != null, key + " must be an object with style and amount.");
            Keys(node, "style", "amount");
            return new ShapeLayerBehaviour.Corner(Number(node, "amount", current.amount, 0f, 1f), Enum(node, "style", current.style));
        }
        private static void ReadCorners(JObject value, string key, ShapeLayerBehaviour.Corner[] target)
        {
            if (value[key] == null) return;
            Require(value[key] is JArray array && array.Count == target.Length, key + " must contain " + target.Length + " corner objects.");
            var items = (JArray)value[key];
            for (int i = 0; i < target.Length; i++)
                target[i] = ReadCorner(new JObject { ["corner"] = items[i].DeepClone() }, "corner", target[i]);
        }
        private static JObject CornerSnapshot(ShapeLayerBehaviour.Corner corner) => new JObject { ["style"] = corner.style.ToString(), ["amount"] = corner.amount };
        private static JArray CornersSnapshot(ShapeLayerBehaviour.Corner[] corners)
        {
            var result = new JArray();
            if (corners != null) foreach (var corner in corners) result.Add(CornerSnapshot(corner));
            return result;
        }

        private static JObject ShapeSnapshot(ShapeLayerBehaviour layer) => new JObject
        {
            ["kind"] = layer.kind.ToString(), ["fill"] = layer.fill, ["fillColor"] = Json(layer.fillColor),
            ["stroke"] = layer.stroke, ["strokeColor"] = Json(layer.strokeColor), ["strokeWidth"] = layer.strokeWidth,
            ["arcThickness"] = layer.arcThickness,
            ["strokePosition"] = layer.strokePosition.ToString(), ["lineCap"] = layer.lineCap.ToString(), ["edgeMode"] = layer.edgeMode.ToString(),
            ["feather"] = layer.feather, ["featherPosition"] = layer.featherPosition.ToString(),
            ["rectangleCorners"] = CornersSnapshot(layer.rectangleCorners), ["polygonCorners"] = CornersSnapshot(layer.polygonCorners),
            ["outerCorner"] = CornerSnapshot(layer.outerCorner), ["innerCorner"] = CornerSnapshot(layer.innerCorner),
            ["linkCorners"] = layer.linkCorners, ["sides"] = layer.sides, ["innerRadius"] = layer.innerRadius,
            ["startAngle"] = layer.startAngle, ["sweepAngle"] = layer.sweepAngle
        };
    }
}
