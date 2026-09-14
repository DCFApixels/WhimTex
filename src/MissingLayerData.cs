using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    // Reads Unity's missing-reference diagnostic payload, never rewrites asset YAML.
    internal static class MissingLayerData
    {
        internal sealed class ScalarText
        {
            internal readonly string Text;
            internal ScalarText(string text) { Text = text; }
        }

        private static JToken Scalar(JToken token, string text)
        {
            token.AddAnnotation(new ScalarText(text));
            return token;
        }
        internal static JObject Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new JObject();
            if (text.Length > 4 * 1024 * 1024) throw new FormatException("Saved layer data exceeds the recovery limit.");
            if (text.TrimStart().StartsWith("{")) return JObject.Parse(text);
            var lines = new List<string>();
            using (var reader = new StringReader(text))
                for (string line; (line = reader.ReadLine()) != null;)
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.TrimEnd());
            int index = 0;
            // Unity versions also return a typed diagnostic tree, not YAML:
            // recoveryId "..." (string) / strength 2.75 (float).
            JToken result = lines.Count > 0 && TypedHeader(lines[0].TrimStart(), out _, out _, out _)
                ? TypedBlock(lines, ref index, 0) : Block(lines, ref index, 0);
            if (index != lines.Count || !(result is JObject obj)) throw new FormatException("Unsupported saved layer data.");
            return obj;
        }

        private static int Indent(string line) => line.Length - line.TrimStart(' ', '\t').Length;

        private static bool TypedHeader(string line, out string name, out string value, out string type)
        {
            name = value = type = null;
            int suffix = line.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffix <= 0 || !line.EndsWith(")", StringComparison.Ordinal)) return false;
            type = line.Substring(suffix + 2, line.Length - suffix - 3);
            string field = line.Substring(0, suffix);
            int space = field.IndexOf(' ');
            name = space < 0 ? field : field.Substring(0, space);
            value = space < 0 ? "" : field.Substring(space + 1).Trim();
            // Field names in the diagnostic tree are identifiers, never quoted YAML keys.
            if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_')) return false;
            foreach (char c in name) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return type.Length > 0;
        }

        private static JToken Unavailable() => new JObject { ["$unavailable"] = true };

        private static JToken TypedBlock(List<string> lines, ref int index, int depth, bool isArray = false)
        {
            if (depth > 64) throw new FormatException("Saved layer data is nested too deeply.");
            int indent = Indent(lines[index]);
            var map = new JObject();
            JArray array = null;
            int count = -1;
            while (index < lines.Count && Indent(lines[index]) == indent)
            {
                string line = lines[index++].Substring(indent);
                if (!TypedHeader(line, out string name, out string value, out string type))
                    throw new FormatException("Unsupported typed saved field.");
                JToken token;
                if (index < lines.Count && Indent(lines[index]) > indent)
                    token = TypedBlock(lines, ref index, depth + 1, type == "Array");
                else if (type == "string")
                {
                    // Do not interpret numeric strings or unescape them as field syntax.
                    try { token = value.StartsWith("\"") ? JToken.Parse(value) : Unavailable(); }
                    catch (Newtonsoft.Json.JsonException) { token = Unavailable(); }
                }
                else if (value.Length == 0) token = Unavailable();
                else token = Inline(value, depth + 1);

                // Unity vectors/lists wrap their elements in an Array with size/data fields.
                if (isArray && name == "size" && map.Count == 0 && array == null && type == "int" && token.Type == JTokenType.Integer)
                {
                    long size = (long)token;
                    if (size < 0 || size > 65536) throw new FormatException("Unsupported saved array size.");
                    count = (int)size;
                    array = new JArray();
                }
                else if (array != null)
                {
                    if (name != "data" || array.Count >= count) throw new FormatException("Unsupported saved array item.");
                    array.Add(token);
                }
                else
                {
                    if (map.ContainsKey(name)) throw new FormatException("Duplicate saved field: " + name);
                    map.Add(name, token);
                }
            }
            if (array != null)
            {
                if (array.Count != count) throw new FormatException("Incomplete saved array.");
                return array;
            }
            return map.Count == 1 && map["Array"] is JArray elements ? (JToken)elements.DeepClone() : map;
        }
        private static JToken Block(List<string> lines, ref int index, int depth)
        {
            if (index >= lines.Count) throw new FormatException("Incomplete saved layer data.");
            if (depth > 64) throw new FormatException("Saved layer data is nested too deeply.");
            int indent = Indent(lines[index]);
            bool array = lines[index].Substring(indent).StartsWith("- ");
            if (array)
            {
                var result = new JArray();
                while (index < lines.Count && Indent(lines[index]) == indent && lines[index].Substring(indent).StartsWith("- "))
                {
                    string value = lines[index++].Substring(indent + 2).Trim();
                    if (value.Length == 0) result.Add(Block(lines, ref index, depth + 1));
                    else if (value[0] != '{' && value[0] != '[' && value[0] != '"' && value[0] != '\'' &&
                        (value.Contains(": ") || value.EndsWith(":")))
                    {
                        var item = new JObject();
                        Pair(item, value, lines, ref index, indent + 2, depth + 1);
                        if (index < lines.Count && Indent(lines[index]) > indent)
                        {
                            var rest = Block(lines, ref index, depth + 1) as JObject;
                            if (rest == null) throw new FormatException("Unsupported sequence data.");
                            foreach (var property in rest.Properties()) item.Add(property.Name, property.Value);
                        }
                        result.Add(item);
                    }
                    else result.Add(Inline(value, depth + 1));
                }
                return result;
            }
            var map = new JObject();
            while (index < lines.Count && Indent(lines[index]) == indent && !lines[index].Substring(indent).StartsWith("- "))
            {
                string line = lines[index++].Substring(indent);
                Pair(map, line, lines, ref index, indent, depth);
            }
            return map;
        }

        private static void Pair(JObject map, string line, List<string> lines, ref int index, int indent, int depth)
        {
            int colon = line.IndexOf(':');
            if (colon < 1) throw new FormatException("Unsupported saved field: " + line);
            string key = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
            if (map.ContainsKey(key)) throw new FormatException("Duplicate saved field: " + key);
            if (value.Length == 0 && index < lines.Count && (Indent(lines[index]) > indent ||
                Indent(lines[index]) == indent && lines[index].TrimStart().StartsWith("- ")))
                map.Add(key, Block(lines, ref index, depth + 1));
            else map.Add(key, Inline(value, depth + 1));
        }

        private static JToken Inline(string value, int depth)
        {
            if (depth > 64) throw new FormatException("Saved layer data is nested too deeply.");
            if (value.StartsWith("{") && !value.EndsWith("}") || value.StartsWith("[") && !value.EndsWith("]"))
                throw new FormatException("Unbalanced saved data.");
            if (value.StartsWith("{") && value.EndsWith("}"))
            {
                var map = new JObject();
                foreach (string part in Split(value.Substring(1, value.Length - 2)))
                {
                    int colon = part.IndexOf(':');
                    if (colon < 1) throw new FormatException("Unsupported inline field.");
                    map.Add(part.Substring(0, colon).Trim(), Inline(part.Substring(colon + 1).Trim(), depth + 1));
                }
                return map;
            }
            if (value.StartsWith("[") && value.EndsWith("]"))
            {
                var array = new JArray();
                foreach (string part in Split(value.Substring(1, value.Length - 2))) array.Add(Inline(part, depth + 1));
                return array;
            }
            if (value.StartsWith("\"")) return JToken.Parse(value);
            if (value.StartsWith("'") && value.EndsWith("'")) return new JValue(value.Substring(1, value.Length - 2).Replace("''", "'"));
            if (value == "true" || value == "false") return Scalar(new JValue(value == "true"), value);
            if (value == "null" || value == "~") return Scalar(JValue.CreateNull(), value);
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) return Scalar(new JValue(integer), value);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsInfinity(number) && !double.IsNaN(number)) return Scalar(new JValue(number), value);
            if (value.StartsWith("|") || value.StartsWith(">") || value.StartsWith("&") || value.StartsWith("*"))
                throw new FormatException("This saved data uses an unsupported YAML construct.");
            return new JValue(value);
        }

        private static IEnumerable<string> Split(string value)
        {
            int start = 0, nesting = 0; char quote = '\0'; bool escape = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (quote != '\0')
                {
                    if (escape) escape = false;
                    else if (c == '\\' && quote == '"') escape = true;
                    else if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '{' || c == '[') nesting++;
                else if (c == '}' || c == ']') nesting--;
                else if (c == ',' && nesting == 0) { yield return value.Substring(start, i - start).Trim(); start = i + 1; }
            }
            if (quote != '\0' || nesting != 0) throw new FormatException("Unbalanced saved data.");
            if (start < value.Length) yield return value.Substring(start).Trim();
        }
    }
}
