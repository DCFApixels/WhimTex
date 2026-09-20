using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexApiException : Exception
    {
        public readonly string Code;
        public string Phase { get; set; }
        public WhimTexApiException(string code, string message) : base(message) => Code = code;
    }

    internal static class AgentJson
    {
        public static void Require(bool condition, string message, string code = "invalid_request")
        {
            if (!condition) throw new WhimTexApiException(code, message);
        }

        public static JObject Parse(string json)
        {
            Require(json != null && json.Length <= 4 * 1024 * 1024, "Request must be at most 4 MiB.");
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Require(!reader.Read(), "Only one JSON object is allowed.");
            return result;
        }

        public static void Keys(JObject obj, params string[] allowed)
        {
            foreach (var property in obj.Properties())
                Require(Array.IndexOf(allowed, property.Name) >= 0, "Unknown field: " + property.Path);
        }

        public static JObject Obj(JToken token, string name)
        {
            Require(token is JObject, name + " must be an object.");
            return (JObject)token;
        }

        public static string Text(JObject obj, string key, string fallback = null)
        {
            JToken token = obj[key];
            if (token == null) return fallback;
            Require(token.Type == JTokenType.String, key + " must be a string.");
            string value = (string)token;
            Require(value.Length <= 4096, key + " is too long.");
            return value;
        }

        public static bool Bool(JObject obj, string key, bool fallback = false)
        {
            JToken token = obj[key];
            if (token == null) return fallback;
            Require(token.Type == JTokenType.Boolean, key + " must be a boolean.");
            return (bool)token;
        }

        public static float Number(JToken token, string name, float min, float max)
        {
            Require(token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer), name + " must be a number.");
            double value = (double)token;
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max,
                name + " must be between " + min + " and " + max + ".");
            return (float)value;
        }

        public static float Number(JObject obj, string key, float fallback, float min, float max) =>
            obj[key] == null ? fallback : Number(obj[key], key, min, max);

        public static int Int(JObject obj, string key, int fallback, int min, int max)
        {
            JToken token = obj[key];
            if (token == null) return fallback;
            Require(token.Type == JTokenType.Integer, key + " must be an integer.");
            long value = (long)token;
            Require(value >= min && value <= max, key + " is out of range.");
            return (int)value;
        }

        public static Vector2 Vector(JToken token, string name)
        {
            Require(token is JArray array && array.Count == 2, name + " must be [x, y].");
            return new Vector2(Number(token[0], name, -1000000f, 1000000f), Number(token[1], name, -1000000f, 1000000f));
        }

        public static Color Color(JToken token)
        {
            Require(token is JArray array && array.Count == 4, "Color must be [r, g, b, a]; alpha is 0..1, RGB may be HDR.");
            return new Color(Number(token[0], "r", -107f, 107f), Number(token[1], "g", -107f, 107f),
                Number(token[2], "b", -107f, 107f), Number(token[3], "a", 0f, 1f));
        }

        public static T Enum<T>(JObject obj, string key, T fallback) where T : struct
        {
            if (obj[key] == null) return fallback;
            string value = Text(obj, key);
            Require(System.Enum.GetNames(typeof(T)).Contains(value), key + " must be one of: " + string.Join(", ", System.Enum.GetNames(typeof(T))));
            return (T)System.Enum.Parse(typeof(T), value);
        }

        public static JArray Json(Vector2 value) => new JArray(value.x, value.y);
        public static JArray Json(Color value) => new JArray(value.r, value.g, value.b, value.a);
    }
}
