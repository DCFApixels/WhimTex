using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXMetadata
    {
        private static readonly Regex Header = new Regex(@"^//\s*@whimtex-effect\s+([^\r\n]+?)\s*$");
        private static readonly Regex Parameter = new Regex(@"^\s*//\s*@param\s+(float|bool|float4|color|texture2D|transform2D|gradient)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^\[\];]+?))?\s*(?:\[\s*(.*?)\s*\.\.\s*(.*?)\s*\])?\s*$");

        internal static bool TryHeader(string firstLine, out string menuPath)
        {
            Match match = Header.Match((firstLine ?? "").TrimStart('\uFEFF'));
            menuPath = match.Success ? match.Groups[1].Value.Trim() : null;
            return match.Success;
        }

        internal static bool HasDeclarations(string source) => Regex.IsMatch(source ?? "", @"(?m)^\s*//\s*@param\b");

        internal static List<ShaderFXParameter> Parse(string source, bool requireHeader, out string menuPath)
        {
            var result = new List<ShaderFXParameter>();
            var names = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
            using var reader = new StringReader(source ?? "");
            string line = reader.ReadLine();
            bool header = TryHeader(line, out menuPath);
            if (requireHeader && !header) throw new FormatException("Line 1: expected // @whimtex-effect Category/Name.");
            if (header)
            {
                foreach (string segment in menuPath.Split('/'))
                    if (string.IsNullOrWhiteSpace(segment)) throw new FormatException("Line 1: effect category/name must not contain empty segments.");
            }
            int lineNumber = 0;
            int declarationCount = 0;
            bool blockComment = false;
            do
            {
                lineNumber++;
                bool declaration = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@param\b");
                if (line != null) ShaderFXSourceBuilder.MaskComments(line, ref blockComment);
                if (!declaration) continue;
                try
                {
                    string tooltip = ExtractTooltip(ref line);
                    Match enumMatch = Regex.Match(line, @"^\s*//\s*@param\s+enum\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^{}]+?))?\s*\{([^{}]+)\}\s*$");
                    Match match = Parameter.Match(enumMatch.Success ? "// @param float " + enumMatch.Groups[1].Value : line);
                    if (!match.Success) throw new FormatException("Expected @param type name = value [min .. max], without a semicolon.");
                    string kind = match.Groups[1].Value;
                    string name = match.Groups[2].Value;
                    var p = new ShaderFXParameter { name = name, declaredInCode = true, floatValue = 0f, colorValue = Color.clear };
                    string value = match.Groups[3].Value.Trim();
                    bool explicitDefault = match.Groups[3].Success;
                    bool bounded = match.Groups[4].Success;
                    switch (kind)
                    {
                        case "gradient":
                            if (explicitDefault) throw new FormatException("Gradient declarations do not accept a default value. Use @param gradient " + name + ".");
                            p.type = ShaderFXParameterType.Gradient;
                            p.gradientValue = new WhimTexGradient();
                            break;
                        case "bool":
                            p.type = ShaderFXParameterType.Bool;
                            if (!explicitDefault) p.floatValue = 0f;
                            else if (value == "true" || value == "1") p.floatValue = 1f;
                            else if (value == "false" || value == "0") p.floatValue = 0f;
                            else throw new FormatException("Expected true, false, 0 or 1 for bool.");
                            break;
                        case "float":
                            p.type = ShaderFXParameterType.Float;
                            p.floatValue = explicitDefault ? Number(value) : 0f;
                            if (bounded)
                            {
                                p.hasMinimum = match.Groups[4].Value.Trim().Length > 0;
                                p.hasMaximum = match.Groups[5].Value.Trim().Length > 0;
                                if (!p.hasMinimum && !p.hasMaximum) throw new FormatException("A range needs at least one boundary.");
                                if (p.hasMinimum) p.minimum = Number(match.Groups[4].Value);
                                if (p.hasMaximum) p.maximum = Number(match.Groups[5].Value);
                                if (p.hasMinimum && p.hasMaximum && p.minimum > p.maximum) throw new FormatException("Minimum exceeds maximum.");
                            }
                            break;
                        case "float4":
                        case "color":
                            if (!explicitDefault) value = "(0, 0, 0, 0)";
                            if (!value.StartsWith("(") || !value.EndsWith(")")) throw new FormatException("Expected four components in parentheses.");
                            string[] parts = value.Substring(1, value.Length - 2).Split(',');
                            if (parts.Length != 4) throw new FormatException("Expected four components.");
                            p.vectorValue = new Vector4(Number(parts[0]), Number(parts[1]), Number(parts[2]), Number(parts[3]));
                            p.type = kind == "color" ? ShaderFXParameterType.Color : ShaderFXParameterType.Vector;
                            p.colorValue = new Color(p.vectorValue.x, p.vectorValue.y, p.vectorValue.z, p.vectorValue.w);
                            break;
                        case "texture2D":
                            p.type = ShaderFXParameterType.Texture2D;
                            if (value.Length != 0)
                            {
                                var reference = Regex.Match(value, "^\"guid:([0-9a-fA-F]{32}):(-?[0-9]+)\"$");
                                if (!reference.Success || !long.TryParse(reference.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long localId))
                                    throw new FormatException("Expected a texture reference: \"guid:<asset GUID>:<local file ID>\".");
                                string path = AssetDatabase.GUIDToAssetPath(reference.Groups[1].Value);
                                if (!string.IsNullOrEmpty(path))
                                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                                        if (asset is Texture2D texture && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string _, out long id) && id == localId)
                                        { p.textureValue = texture; break; }
                            }
                            break;
                        case "transform2D":
                            p.type = ShaderFXParameterType.Transform2D;
                            if (value.Length != 0)
                            {
                                if (!value.StartsWith("(") || !value.EndsWith(")")) throw new FormatException("Expected (x, y, width, height, angle).");
                                string[] components = value.Substring(1, value.Length - 2).Split(',');
                                if (components.Length != 5) throw new FormatException("Expected five transform components.");
                                p.transformValue = new ShaderFXTransform
                                {
                                    position = new Vector2(Number(components[0]), Number(components[1])),
                                    size = new Vector2(Number(components[2]), Number(components[3])), rotation = Number(components[4])
                                };
                            }
                            break;
                    }
                    if (kind != "float" && bounded) throw new FormatException("Ranges apply only to float parameters.");
                    var control = new ShaderFXParameterControl { type = p.type, order = lineNumber, tooltip = tooltip,
                        hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum, minimum = p.minimum, maximum = p.maximum };
                    if (enumMatch.Success)
                    {
                        control.type = ShaderFXParameterType.Enum;
                        p.type = ShaderFXParameterType.Float;
                        var labels = new List<string>();
                        var numbers = new List<float>();
                        foreach (string item in enumMatch.Groups[3].Value.Split(','))
                        {
                            Match option = Regex.Match(item.Trim(), @"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.+)$");
                            if (!option.Success) throw new FormatException("Enum items must be Name: number.");
                            string label = option.Groups[1].Value;
                            float number = Number(option.Groups[2].Value);
                            if (labels.Contains(label) || numbers.Contains(number)) throw new FormatException("Enum names and values must be unique.");
                            labels.Add(label); numbers.Add(number);
                        }
                        control.optionNames = labels.ToArray(); control.optionValues = numbers.ToArray();
                        explicitDefault = enumMatch.Groups[2].Success;
                        if (explicitDefault)
                        {
                            string initial = enumMatch.Groups[2].Value.Trim();
                            int index = labels.IndexOf(initial);
                            p.floatValue = index >= 0 ? numbers[index] : Number(initial);
                        }
                    }
                    p.controls.Add(control);
                    if (names.TryGetValue(name, out var existing))
                    {
                        if (!Compatible(existing.type, p.type)) throw new FormatException("Conflicting storage types for " + name);
                        existing.controls.Add(control);
                        if (IsScalar(p.type)) existing.type = ShaderFXParameterType.Float;
                        if (explicitDefault) CopyValue(p, existing);
                    }
                    else { names.Add(name, p); result.Add(p); }
                    if (++declarationCount > 128) throw new FormatException("At most 128 parameter controls are supported.");
                    if (result.Count > 128) throw new FormatException("At most 128 parameters are supported.");
                }
                catch (FormatException error) { throw new FormatException($"Line {lineNumber}: {error.Message}"); }
            } while ((line = reader.ReadLine()) != null);
            return result;
        }

        private static string ExtractTooltip(ref string line)
        {
            bool quoted = false, escaped = false;
            for (int i = line.IndexOf("@param", StringComparison.Ordinal) + 6; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (escaped) { escaped = false; continue; }
                if (quoted && c == '\\') { escaped = true; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (!quoted && c == '/' && line[i + 1] == '/')
                {
                    string tooltip = line.Substring(i + 2).Trim();
                    line = line.Substring(0, i).TrimEnd();
                    return tooltip;
                }
            }
            return null;
        }

        private static float Number(string value)
        {
            if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ||
                float.IsNaN(result) || float.IsInfinity(result)) throw new FormatException("Expected a finite number: " + value);
            return result;
        }

        internal static void PreserveValues(List<ShaderFXParameter> next, IReadOnlyList<ShaderFXParameter> previous)
        {
            for (int i = 0; i < next.Count; i++)
            {
                var p = next[i];
                ShaderFXParameter match = null;
                foreach (var old in previous)
                    if (old != null && old.name == p.name && Compatible(old.type, p.type))
                    {
                        match = old;
                        break;
                    }
                // A rename in place keeps identity. Do not guess across insertions/removals or reorders.
                if (match == null && next.Count == previous.Count && previous[i] is ShaderFXParameter candidate &&
                    candidate.type == p.type && !next.Exists(item => item.name == candidate.name))
                {
                    bool reusedName = false;
                    foreach (var old in previous) reusedName |= old != null && old.name == p.name;
                    if (!reusedName) match = candidate;
                }
                if (match == null) continue;
                p.id = match.id;
                p.floatValue = p.controls.Count > 0 ? match.floatValue : p.Clamp(match.floatValue);
                p.colorValue = match.colorValue;
                p.vectorValue = match.vectorValue;
                p.textureValue = match.textureValue;
                p.gradientValue = match.gradientValue?.Clone();
                p.transformValue = match.transformValue;
            }
        }

        internal static bool IsScalar(ShaderFXParameterType type) => type == ShaderFXParameterType.Float || type == ShaderFXParameterType.Bool || type == ShaderFXParameterType.Enum;
        internal static bool Compatible(ShaderFXParameterType a, ShaderFXParameterType b) => a == b || IsScalar(a) && IsScalar(b);
        private static void CopyValue(ShaderFXParameter source, ShaderFXParameter target)
        {
            target.floatValue = source.floatValue; target.colorValue = source.colorValue;
            target.gradientValue = source.gradientValue?.Clone();
            target.vectorValue = source.vectorValue; target.textureValue = source.textureValue; target.transformValue = source.transformValue;
        }
    }
}
