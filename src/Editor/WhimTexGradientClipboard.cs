using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexGradientClipboard
    {
        private const string LegacyPrefix = "WhimTex.Gradient/1\n";

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
            if (text.StartsWith(LegacyPrefix, StringComparison.Ordinal))
                text = text.Substring(LegacyPrefix.Length);
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
            if (data is JObject serialized && serialized["colors"] is JArray keys &&
                keys.Count > 0 && keys[0]?["color"] is JObject)
            {
                // Accept the serialized value previously produced by Copy, as well as plain JSON of that value.
                Keys(serialized, "colors", "alphas", "mode", "wrapMode", "colorSpace", "smoothness");
                foreach (JToken token in keys)
                {
                    var stop = Obj(token, "color stop");
                    var value = Obj(stop["color"], "color"); Keys(value, "r", "g", "b", "a");
                    stop["color"] = new JArray(Number(value["r"], "r", -65504, 65504),
                        Number(value["g"], "g", -65504, 65504), Number(value["b"], "b", -65504, 65504),
                        Number(value["a"], "a", 0, 1));
                }
                ConvertEnum<WhimTexGradientMode>(serialized, "mode");
                ConvertEnum<WhimTexGradientWrapMode>(serialized, "wrapMode");
                ConvertEnum<ColorSpace>(serialized, "colorSpace");
            }
            return WhimTexApi.ReadGradient(data, WhimTexGradientMode.Classic, 65504f);
        }

        private static void ConvertEnum<T>(JObject data, string key) where T : struct
        {
            if (data[key]?.Type != JTokenType.Integer) return;
            int value = Int(data, key, 0, int.MinValue, int.MaxValue);
            Require(System.Enum.IsDefined(typeof(T), value), "Invalid " + key + ".");
            data[key] = System.Enum.GetName(typeof(T), value);
        }

        internal static bool TryRead(string text, out WhimTexGradient gradient)
        {
            try { gradient = Read(text); return true; }
            catch (Exception) { gradient = null; return false; }
        }
    }
}
