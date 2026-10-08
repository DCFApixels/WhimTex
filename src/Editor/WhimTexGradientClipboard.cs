using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexGradientClipboard
    {
        internal static string Write(WhimTexGradient gradient)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            gradient.Evaluate(0);
            var colors = new JArray();
            var alphas = new JArray();
            var c = gradient.ColorKeys;
            var a = gradient.AlphaKeys;
            for (int i = 0; i < c.Length; i++)
                colors.Add(new JObject { ["time"] = c[i].time,
                    ["color"] = new JArray(c[i].color.r, c[i].color.g, c[i].color.b, c[i].color.a),
                    ["midpoint"] = gradient.GetMidpoint(false, i) });
            for (int i = 0; i < a.Length; i++)
                alphas.Add(new JObject { ["time"] = a[i].time, ["alpha"] = a[i].alpha,
                    ["midpoint"] = gradient.GetMidpoint(true, i) });
            return new JObject
            {
                ["format"] = "whimtex.gradient", ["version"] = 1,
                ["gradient"] = new JObject { ["mode"] = gradient.Mode.ToString(),
                    ["wrapMode"] = gradient.WrapMode.ToString(),
                    ["colorSpace"] = gradient.ColorSpace.ToString(), ["smoothness"] = gradient.Smoothness,
                    ["colors"] = colors, ["alphas"] = alphas }
            }.ToString(Formatting.Indented);
        }

        internal static WhimTexGradient Read(string text)
        {
            Require(!string.IsNullOrWhiteSpace(text) && text.Length <= 65536, "Gradient JSON must contain 1..65536 characters.");
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
                    Require(Int(root, "version", 0, 1, 1) == 1, "Supported gradient version: 1.");
                    data = root["gradient"];
                }
                else data = root;
            }
            return WhimTexApi.ReadGradient(data, WhimTexGradientMode.Perceptual, 65504f);
        }

        internal static bool TryRead(string text, out WhimTexGradient gradient)
        {
            try { gradient = Read(text); return true; }
            catch (Exception) { gradient = null; return false; }
        }
    }
}
