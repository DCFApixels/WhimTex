using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetMakeSeamless(MakeSeamlessLayerBehaviour layer, JObject value)
        {
            Keys(value, "horizontal", "vertical", "blendWidth", "falloff");
            layer.horizontal = Enum(value, "horizontal", layer.horizontal);
            layer.vertical = Enum(value, "vertical", layer.vertical);
            layer.blendWidth = Number(value, "blendWidth", layer.blendWidth, .001f, .5f);
            layer.falloff = Number(value, "falloff", layer.falloff, .25f, 4f);
        }

        private static JObject MakeSeamlessSnapshot(MakeSeamlessLayerBehaviour layer) => new JObject
        {
            ["horizontal"] = layer.horizontal.ToString(),
            ["vertical"] = layer.vertical.ToString(),
            ["blendWidth"] = layer.blendWidth,
            ["falloff"] = layer.falloff
        };
    }
}
