using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetShape(ShapeLayerBehaviour layer, JObject value)
        {
            Keys(value, "kind", "fill", "fillColor", "fillMode", "gradient", "gradientStart", "gradientEnd", "gradientRepetitions",
                "stroke", "strokeMode", "strokeGradient", "strokeGradientStart", "strokeGradientEnd", "strokeGradientRepetitions",
                "strokeColor", "strokeWidth", "arcThickness", "strokePosition",
                "strokeJoin", "strokeCap", "miterLimit", "dashPattern", "dashOffset", "lineCap", "edgeMode",
                "feather", "featherPosition", "rectangleCorners", "polygonCorners", "outerCorner", "innerCorner", "linkCorners", "sides", "innerRadius", "startAngle", "sweepAngle");
            layer.kind = Enum(value, "kind", layer.kind);
            layer.fill = Bool(value, "fill", layer.fill);
            layer.fillMode = Enum(value, "fillMode", layer.fillMode);
            if (value["gradient"] != null) layer.gradient = ReadGradient(value["gradient"]);
            if (value["gradientStart"] != null) layer.gradientStart = Vector(value["gradientStart"], "gradientStart");
            if (value["gradientEnd"] != null) layer.gradientEnd = Vector(value["gradientEnd"], "gradientEnd");
            layer.gradientRepetitions = Number(value, "gradientRepetitions", layer.gradientRepetitions, .0001f, float.MaxValue);
            layer.stroke = Bool(value, "stroke", layer.stroke);
            layer.strokeMode = Enum(value, "strokeMode", layer.strokeMode);
            if (value["strokeGradient"] != null) layer.strokeGradient = ReadGradient(value["strokeGradient"]);
            if (value["strokeGradientStart"] != null) layer.strokeGradientStart = StrokeGradientAnchor(value["strokeGradientStart"], "strokeGradientStart");
            if (value["strokeGradientEnd"] != null) layer.strokeGradientEnd = StrokeGradientAnchor(value["strokeGradientEnd"], "strokeGradientEnd");
            layer.strokeGradientRepetitions = Number(value, "strokeGradientRepetitions", layer.strokeGradientRepetitions, .0001f, float.MaxValue);
            if (value["fillColor"] != null) layer.fillColor = Color(value["fillColor"]);
            if (value["strokeColor"] != null) layer.strokeColor = Color(value["strokeColor"]);
            layer.strokeWidth = Number(value, "strokeWidth", layer.strokeWidth, 0f, 8192f);
            layer.arcThickness = Number(value, "arcThickness", layer.arcThickness, 0f, 8192f);
            layer.strokePosition = Enum(value, "strokePosition", layer.strokePosition);
            layer.strokeJoin = Enum(value, "strokeJoin", layer.strokeJoin);
            layer.strokeCap = Enum(value, "strokeCap", layer.strokeCap);
            layer.miterLimit = Number(value, "miterLimit", layer.miterLimit, 1f, 10000f);
            layer.dashOffset = Number(value, "dashOffset", layer.dashOffset, -10000000f, 10000000f);
            if (value["dashPattern"] != null) layer.dashPattern = ReadShapeDashPattern(value["dashPattern"]);
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

        private static UnityEngine.Vector2 StrokeGradientAnchor(JToken token, string name)
        {
            Require(token is JArray array && array.Count == 2, name + " must be [x, y].");
            return new UnityEngine.Vector2(Number(token[0], name, -float.MaxValue, float.MaxValue),
                Number(token[1], name, -float.MaxValue, float.MaxValue));
        }

        private static List<float> ReadShapeDashPattern(JToken token)
        {
            if (token.Type == JTokenType.Null) return new List<float>();
            Require(token is JArray, "dashPattern must be an array of positive stroke-width multiples, or null for solid.");
            var values = (JArray)token;
            Require(values.Count <= 256, "dashPattern must contain at most 256 values.");
            var result = new List<float>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                string name = "dashPattern[" + i + "]";
                float length = Number(values[i], name, 0f, 10000000f);
                Require(length > 0f, name + " must be a positive stroke-width multiple.");
                result.Add(length);
            }
            return result;
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
            ["fillMode"] = layer.fillMode.ToString(), ["gradient"] = layer.gradient == null ? null : GradientSnapshot(layer.gradient),
            ["gradientStart"] = Json(layer.gradientStart), ["gradientEnd"] = Json(layer.gradientEnd),
            ["gradientRepetitions"] = layer.gradientRepetitions,
            ["stroke"] = layer.stroke, ["strokeColor"] = Json(layer.strokeColor), ["strokeWidth"] = layer.strokeWidth,
            ["strokeMode"] = layer.strokeMode.ToString(), ["strokeGradient"] = layer.strokeGradient == null ? null : GradientSnapshot(layer.strokeGradient),
            ["strokeGradientStart"] = Json(layer.strokeGradientStart), ["strokeGradientEnd"] = Json(layer.strokeGradientEnd),
            ["strokeGradientRepetitions"] = layer.strokeGradientRepetitions,
            ["arcThickness"] = layer.arcThickness,
            ["strokeJoin"] = layer.strokeJoin.ToString(), ["strokeCap"] = layer.strokeCap.ToString(), ["miterLimit"] = layer.miterLimit,
            ["dashPattern"] = layer.dashPattern == null ? new JArray() : new JArray(layer.dashPattern), ["dashOffset"] = layer.dashOffset,
            ["strokePosition"] = layer.strokePosition.ToString(), ["lineCap"] = layer.lineCap.ToString(), ["edgeMode"] = layer.edgeMode.ToString(),
            ["feather"] = layer.feather, ["featherPosition"] = layer.featherPosition.ToString(),
            ["rectangleCorners"] = CornersSnapshot(layer.rectangleCorners), ["polygonCorners"] = CornersSnapshot(layer.polygonCorners),
            ["outerCorner"] = CornerSnapshot(layer.outerCorner), ["innerCorner"] = CornerSnapshot(layer.innerCorner),
            ["linkCorners"] = layer.linkCorners, ["sides"] = layer.sides, ["innerRadius"] = layer.innerRadius,
            ["startAngle"] = layer.startAngle, ["sweepAngle"] = layer.sweepAngle
        };
    }
}
