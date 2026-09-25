using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetFillPattern(FillPatternSettings p, JObject value)
        {
            Keys(value, "shape", "circleLayout", "size", "linkSize", "rotation", "offset", "seamless", "gap",
                "roundness", "bulge", "distanceRange", "position", "inverted", "profile", "gradient",
                "cellColor", "colorBlend", "seed", "variation", "palette");
            p.shape = Enum(value, "shape", p.shape);
            p.circleLayout = Enum(value, "circleLayout", p.circleLayout);
            if (value["size"] != null)
            {
                var size = value["size"] is JArray ? Vector(value["size"], "size")
                    : UnityEngine.Vector2.one * Number(value, "size", p.size, 1, 16384);
                Require(size.x >= 1 && size.x <= 16384 && size.y >= 1 && size.y <= 16384, "Pattern size is out of range.");
                p.Size = size;
            }
            p.linkSize = Bool(value, "linkSize", p.linkSize);
            p.rotation = Number(value, "rotation", p.rotation, -360000, 360000);
            if (value["offset"] != null)
            {
                var v = Vector(value["offset"], "offset");
                Require(v.x >= -1000000 && v.x <= 1000000 && v.y >= -1000000 && v.y <= 1000000, "Pattern offset is out of range.");
                p.offset = v;
            }
            p.seamless = Bool(value, "seamless", p.seamless);
            p.gap = Number(value, "gap", p.gap, 0, .99f);
            p.roundness = Number(value, "roundness", p.roundness, 0, 1);
            p.bulge = Number(value, "bulge", p.bulge, 0, 1);
            p.distanceRange = Number(value, "distanceRange", p.distanceRange, .001f, 16);
            p.position = Enum(value, "position", p.position);
            p.inverted = Bool(value, "inverted", p.inverted);
            if (value["profile"] != null) p.profile = WhimTexCurveTexture.Parse(Text(value, "profile"));
            if (value["gradient"] != null) p.gradient = ReadGradient(value["gradient"], WhimTexGradientMode.Linear);
            p.cellColor = Enum(value, "cellColor", p.cellColor);
            p.colorBlend = Enum(value, "colorBlend", p.colorBlend);
            p.seed = Int(value, "seed", p.seed, int.MinValue, int.MaxValue);
            p.variation = Number(value, "variation", p.variation, 0, 1);
            if (value["palette"] != null) p.palette = ReadGradient(value["palette"], WhimTexGradientMode.Linear);
        }

        private static JObject FillPatternSnapshot(FillPatternSettings p) => new JObject
        {
            ["shape"] = p.shape.ToString(), ["circleLayout"] = p.circleLayout.ToString(),
            ["size"] = Json(p.Size), ["linkSize"] = p.linkSize, ["rotation"] = p.rotation, ["offset"] = Json(p.offset),
            ["seamless"] = p.seamless, ["gap"] = p.gap, ["roundness"] = p.roundness,
            ["bulge"] = p.bulge, ["distanceRange"] = p.distanceRange,
            ["position"] = p.position.ToString(), ["inverted"] = p.inverted,
            ["profile"] = WhimTexCurveTexture.Format(p.profile), ["gradient"] = GradientSnapshot(p.gradient),
            ["cellColor"] = p.cellColor.ToString(), ["colorBlend"] = p.colorBlend.ToString(),
            ["seed"] = p.seed, ["variation"] = p.variation, ["palette"] = GradientSnapshot(p.palette)
        };
    }
}
