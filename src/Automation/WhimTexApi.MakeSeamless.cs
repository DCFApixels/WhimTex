using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        private static void SetMakeSeamless(MakeSeamlessLayerBehaviour layer, JObject value)
        {
            Keys(value, "mode", "horizontal", "vertical", "blendWidth", "falloff", "mirrorTransitionStart", "leftEdge", "rightEdge", "bottomEdge", "topEdge", "screeningRadius", "edgeWidth", "offsetTransitionStart", "histogramContrast", "mirrorContrastCompensation", "mirrorContrast", "mirrorSeamCorrection", "mirrorAutoRadius", "mirrorCorrectionRadius", "processRed", "processGreen", "processBlue", "processAlpha", "offsetContrastCompensation", "offsetSeamCorrection", "offsetAutoRadius", "offsetCorrectionRadius", "poissonEdges", "mirrorPoissonEdges", "offsetPoissonEdges", "quiltingEdges", "quiltingWidth", "quiltingAlongSearch", "quiltingFeather", "quiltingContrastCompensation", "quiltingContrast", "quiltingQuality", "quiltingSeed", "quiltingChannels", "quiltingSeamCorrection", "quiltingPoissonEdges", "quiltingCorrectionRadius");
            layer.poissonEdges = Enum(value, "poissonEdges", layer.poissonEdges);
            layer.quiltingEdges = Enum(value,"quiltingEdges",layer.quiltingEdges);
            layer.quiltingWidth = SeamlessNumber(value,"quiltingWidth",layer.quiltingWidth,.02,.45);
            layer.quiltingAlongSearch = SeamlessNumber(value,"quiltingAlongSearch",layer.quiltingAlongSearch,0,.25);
            layer.quiltingFeather = SeamlessNumber(value,"quiltingFeather",layer.quiltingFeather,0,100);
            layer.quiltingContrastCompensation = Bool(value,"quiltingContrastCompensation",layer.quiltingContrastCompensation);
            layer.quiltingContrast = SeamlessNumber(value,"quiltingContrast",layer.quiltingContrast,0,1);
            layer.quiltingQuality = Enum(value,"quiltingQuality",layer.quiltingQuality);
            layer.quiltingSeed = Int(value,"quiltingSeed",layer.quiltingSeed,int.MinValue,int.MaxValue);
            layer.quiltingChannels = Enum(value,"quiltingChannels",layer.quiltingChannels);
            layer.quiltingSeamCorrection = Bool(value,"quiltingSeamCorrection",layer.quiltingSeamCorrection);
            layer.quiltingPoissonEdges = Enum(value,"quiltingPoissonEdges",layer.quiltingPoissonEdges);
            layer.quiltingCorrectionRadius = SeamlessNumber(value,"quiltingCorrectionRadius",layer.quiltingCorrectionRadius,.005,.25);
            layer.mirrorPoissonEdges = Enum(value, "mirrorPoissonEdges", layer.mirrorPoissonEdges);
            layer.offsetPoissonEdges = Enum(value, "offsetPoissonEdges", layer.offsetPoissonEdges);
            layer.processRed = Bool(value, "processRed", layer.processRed);
            layer.processGreen = Bool(value, "processGreen", layer.processGreen);
            layer.processBlue = Bool(value, "processBlue", layer.processBlue);
            layer.processAlpha = Bool(value, "processAlpha", layer.processAlpha);
            if (value["mode"] != null) layer.mode = Enum(value, "mode", layer.mode);
            layer.offsetContrastCompensation = Bool(value, "offsetContrastCompensation", layer.offsetContrastCompensation);
            layer.offsetSeamCorrection = Bool(value, "offsetSeamCorrection", layer.offsetSeamCorrection);
            layer.offsetAutoRadius = Bool(value, "offsetAutoRadius", layer.offsetAutoRadius);
            layer.offsetCorrectionRadius = SeamlessNumber(value, "offsetCorrectionRadius", layer.offsetCorrectionRadius, .005, .25);
            layer.horizontal = Enum(value, "horizontal", layer.horizontal);
            layer.vertical = Enum(value, "vertical", layer.vertical);
            layer.blendWidth = SeamlessNumber(value, "blendWidth", layer.blendWidth, .001, .5);
            layer.falloff = SeamlessNumber(value, "falloff", layer.falloff, .25, 4);
            layer.mirrorTransitionStart = SeamlessNumber(value, "mirrorTransitionStart", layer.mirrorTransitionStart, -1, .95);
            layer.mirrorContrastCompensation = Bool(value, "mirrorContrastCompensation", layer.mirrorContrastCompensation);
            layer.mirrorContrast = SeamlessNumber(value, "mirrorContrast", layer.mirrorContrast, 0, 1);
            layer.mirrorSeamCorrection = Bool(value, "mirrorSeamCorrection", layer.mirrorSeamCorrection);
            layer.mirrorAutoRadius = Bool(value, "mirrorAutoRadius", layer.mirrorAutoRadius);
            layer.mirrorCorrectionRadius = SeamlessNumber(value, "mirrorCorrectionRadius", layer.mirrorCorrectionRadius, .005, .25);
            layer.leftEdge = Bool(value, "leftEdge", layer.leftEdge);
            layer.rightEdge = Bool(value, "rightEdge", layer.rightEdge);
            layer.bottomEdge = Bool(value, "bottomEdge", layer.bottomEdge);
            layer.topEdge = Bool(value, "topEdge", layer.topEdge);
            layer.screeningRadius = SeamlessNumber(value, "screeningRadius", layer.screeningRadius, .005, .25);
            layer.edgeWidth = SeamlessNumber(value, "edgeWidth", layer.edgeWidth, .02, .5);
            layer.offsetTransitionStart = SeamlessNumber(value, "offsetTransitionStart", layer.offsetTransitionStart, -1, .95);
            layer.histogramContrast = SeamlessNumber(value, "histogramContrast", layer.histogramContrast, 0, 1);
        }

        private static float SeamlessNumber(JObject obj, string key, float fallback, double min, double max)
        {
            JToken token = obj[key];
            if (token == null) return fallback;
            Require(token.Type == JTokenType.Float || token.Type == JTokenType.Integer, key + " must be a number.");
            double value = (double)token;
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max,
                key + " must be between " + min + " and " + max + ".");
            return (float)value;
        }

        private static JObject MakeSeamlessSnapshot(MakeSeamlessLayerBehaviour layer) => new JObject
        {
            ["mode"] = layer.EffectiveMode.ToString(),
            ["quiltingEdges"] = layer.quiltingEdges.ToString(),
            ["quiltingWidth"] = layer.quiltingWidth,
            ["quiltingAlongSearch"] = layer.quiltingAlongSearch,
            ["quiltingFeather"] = layer.quiltingFeather,
            ["quiltingContrastCompensation"] = layer.quiltingContrastCompensation,
            ["quiltingContrast"] = layer.quiltingContrast,
            ["quiltingQuality"] = layer.quiltingQuality.ToString(),
            ["quiltingSeed"] = layer.quiltingSeed,
            ["quiltingChannels"] = layer.quiltingChannels.ToString(),
            ["quiltingSeamCorrection"] = layer.quiltingSeamCorrection,
            ["quiltingPoissonEdges"] = layer.quiltingPoissonEdges.ToString(),
            ["quiltingCorrectionRadius"] = layer.quiltingCorrectionRadius,
            ["poissonEdges"] = layer.poissonEdges.ToString(),
            ["mirrorPoissonEdges"] = layer.mirrorPoissonEdges.ToString(),
            ["offsetPoissonEdges"] = layer.offsetPoissonEdges.ToString(),
            ["offsetContrastCompensation"] = layer.offsetContrastCompensation,
            ["offsetSeamCorrection"] = layer.offsetSeamCorrection,
            ["offsetAutoRadius"] = layer.offsetAutoRadius,
            ["offsetCorrectionRadius"] = layer.offsetCorrectionRadius,
            ["processRed"] = layer.processRed,
            ["processGreen"] = layer.processGreen,
            ["processBlue"] = layer.processBlue,
            ["processAlpha"] = layer.processAlpha,
            ["horizontal"] = layer.horizontal.ToString(),
            ["vertical"] = layer.vertical.ToString(),
            ["blendWidth"] = layer.blendWidth,
            ["falloff"] = layer.falloff,
            ["mirrorTransitionStart"] = layer.mirrorTransitionStart,
            ["mirrorContrastCompensation"] = layer.mirrorContrastCompensation,
            ["mirrorContrast"] = layer.mirrorContrast,
            ["mirrorSeamCorrection"] = layer.mirrorSeamCorrection,
            ["mirrorAutoRadius"] = layer.mirrorAutoRadius,
            ["mirrorCorrectionRadius"] = layer.mirrorCorrectionRadius,
            ["leftEdge"] = layer.leftEdge,
            ["rightEdge"] = layer.rightEdge,
            ["bottomEdge"] = layer.bottomEdge,
            ["topEdge"] = layer.topEdge,
            ["screeningRadius"] = layer.screeningRadius,
            ["edgeWidth"] = layer.edgeWidth,
            ["offsetTransitionStart"] = layer.offsetTransitionStart,
            ["histogramContrast"] = layer.histogramContrast
        };
    }
}
