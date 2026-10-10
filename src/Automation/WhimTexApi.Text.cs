using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetText(TextLayerBehaviour layer, JObject value)
        {
            Keys(value, "text", "fontFamily", "fontStyle", "casing", "fontSize", "maxFontSize", "spacing", "characterHorizontalScale", "alignment", "layoutMode", "frameSize", "wrapping", "overflow", "justify", "autoSize", "color");
            if (value["text"] != null)
            {
                Require(value["text"].Type == JTokenType.String, "text must be a string.");
                layer.text = (string)value["text"];
            }
            Require((layer.text?.Length ?? 0) <= TextLayerBehaviour.MaxCharacters, "Text exceeds the 8192-character limit.");
            layer.fontFamily = Text(value, "fontFamily", layer.fontFamily);
            layer.fontStyle = Enum(value, "fontStyle", layer.fontStyle);
            layer.casing = Enum(value, "casing", layer.casing);
            layer.fontSize = Number(value, "fontSize", layer.fontSize, 1, 2048);
            layer.maxFontSize = Number(value, "maxFontSize", layer.maxFontSize, 1, 2048);
            layer.characterHorizontalScale = Number(value, "characterHorizontalScale", layer.characterHorizontalScale, .01f, 10);
            if (value["spacing"] != null)
            {
                var spacing = Obj(value["spacing"], "spacing");
                Keys(spacing, "character", "word", "line", "paragraph");
                layer.spacing.character = Number(spacing, "character", layer.spacing.character, TextSpacing.Minimum, TextSpacing.Maximum);
                layer.spacing.word = Number(spacing, "word", layer.spacing.word, TextSpacing.Minimum, TextSpacing.Maximum);
                layer.spacing.line = Number(spacing, "line", layer.spacing.line, TextSpacing.Minimum, TextSpacing.Maximum);
                layer.spacing.paragraph = Number(spacing, "paragraph", layer.spacing.paragraph, TextSpacing.Minimum, TextSpacing.Maximum);
            }
            layer.alignment = Enum(value, "alignment", layer.alignment);
            layer.layoutMode = Enum(value, "layoutMode", layer.layoutMode);
            layer.wrapping = Enum(value, "wrapping", layer.wrapping);
            layer.overflow = Enum(value, "overflow", layer.overflow);
            layer.justify = Bool(value, "justify", layer.justify);
            layer.autoSize = Bool(value, "autoSize", layer.autoSize);
            if (value["frameSize"] != null) layer.frameSize = Vector(value["frameSize"], "frameSize");
            layer.Validate();
            if (value["color"] != null) layer.color = Color(value["color"]);
            layer.SetAlignment(layer.alignment, layer.justify);
        }
        private static JObject TextSnapshot(TextLayerBehaviour layer) => new JObject
        {
            ["text"] = layer.text, ["fontFamily"] = layer.fontFamily, ["fontStyle"] = layer.fontStyle.ToString(), ["casing"] = layer.casing.ToString(),
            ["fontSize"] = layer.fontSize, ["maxFontSize"] = layer.maxFontSize, ["alignment"] = layer.alignment.ToString(),
            ["characterHorizontalScale"] = layer.characterHorizontalScale,
            ["spacing"] = new JObject { ["character"] = layer.spacing.character, ["word"] = layer.spacing.word,
                ["line"] = layer.spacing.line, ["paragraph"] = layer.spacing.paragraph },
            ["layoutMode"] = layer.layoutMode.ToString(), ["frameSize"] = new JArray(layer.frameSize.x, layer.frameSize.y),
            ["wrapping"] = layer.wrapping.ToString(), ["overflow"] = layer.overflow.ToString(), ["justify"] = layer.justify, ["autoSize"] = layer.autoSize,
            ["color"] = new JArray(layer.color.r, layer.color.g, layer.color.b, layer.color.a)
        };
    }
}
