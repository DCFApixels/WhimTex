using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexGradientClipboard
    {
        internal const int MaximumJsonLength = 262144;
        internal static string Write(WhimTexGradient gradient)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            return new JObject
            {
                ["format"] = "whimtex.gradient", ["version"] = 2,
                ["gradient"] = WhimTexApi.WriteGradient(gradient)
            }.ToString(Formatting.Indented);
        }
        internal static WhimTexGradient Read(string text)
        {
            Require(!string.IsNullOrWhiteSpace(text) && text.Length <= MaximumJsonLength, "Gradient JSON must contain 1..262144 characters.");
            text = text.Trim().TrimStart('\uFEFF');
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int newline = text.IndexOf('\n');
                Require(newline >= 0 && (text.Substring(0, newline).Trim() == "```json" ||
                    text.Substring(0, newline).Trim() == "```") && text.EndsWith("```", StringComparison.Ordinal),
                    "Copy one complete gradient JSON block.");
                text = text.Substring(newline + 1, text.Length - newline - 4).Trim();
            }
            JToken data;
            if (text.StartsWith("[", StringComparison.Ordinal))
                data = Parse("{\"gradient\":" + text + "}")["gradient"];
            else
            {
                var root = Parse(text);
                if (root["format"] != null)
                {
                    Keys(root, "format", "version", "gradient");
                    Require(Text(root, "format") == "whimtex.gradient", "format must be whimtex.gradient.");
                    Require(Int(root, "version", 0, 0, int.MaxValue) == 2, "Supported gradient version: 2. Older gradient presets are not supported.");
                    data = root["gradient"];
                }
                else data = root;
            }
            return WhimTexApi.ReadGradient(data, WhimTexGradientColorMode.Perceptual, 65504f);
        }

        internal static bool TryRead(string text, out WhimTexGradient gradient)
        {
            try { gradient = Read(text); return true; }
            catch (Exception) { gradient = null; return false; }
        }
    }
}
