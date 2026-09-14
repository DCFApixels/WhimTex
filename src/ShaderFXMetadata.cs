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
        private static readonly Regex Parameter = new Regex(@"^\s*//\s*@param\s+(float|float4|color|texture2D|transform2D)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^\[\];]+?))?\s*(?:\[\s*(.*?)\s*\.\.\s*(.*?)\s*\])?\s*$");

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
            var names = new HashSet<string>(StringComparer.Ordinal);
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
            bool blockComment = false;
            do
            {
                lineNumber++;
                bool declaration = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@param\b");
                if (line != null) ShaderFXSourceBuilder.MaskComments(line, ref blockComment);
                if (!declaration) continue;
                try
                {
                    Match match = Parameter.Match(line);
                    if (!match.Success) throw new FormatException("Expected @param type name = value [min .. max], without a semicolon.");
                    string kind = match.Groups[1].Value;
                    string name = match.Groups[2].Value;
                    if (!names.Add(name)) throw new FormatException("Duplicate parameter: " + name);
                    var p = new ShaderFXParameter { name = name, declaredInCode = true };
                    string value = match.Groups[3].Value.Trim();
                    bool bounded = match.Groups[4].Success;
                    switch (kind)
                    {
                        case "float":
                            p.type = ShaderFXParameterType.Float;
                            p.floatValue = Number(value);
                            if (bounded)
                            {
                                p.hasMinimum = match.Groups[4].Value.Trim().Length > 0;
                                p.hasMaximum = match.Groups[5].Value.Trim().Length > 0;
                                if (!p.hasMinimum && !p.hasMaximum) throw new FormatException("A range needs at least one boundary.");
                                if (p.hasMinimum) p.minimum = Number(match.Groups[4].Value);
                                if (p.hasMaximum) p.maximum = Number(match.Groups[5].Value);
                                if (p.hasMinimum && p.hasMaximum && p.minimum > p.maximum) throw new FormatException("Minimum exceeds maximum.");
                                if (p.Clamp(p.floatValue) != p.floatValue) throw new FormatException("Default value is outside the range.");
                            }
                            break;
                        case "float4":
                        case "color":
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
                    result.Add(p);
                    if (result.Count > 128) throw new FormatException("At most 128 parameters are supported.");
                }
                catch (FormatException error) { throw new FormatException($"Line {lineNumber}: {error.Message}"); }
            } while ((line = reader.ReadLine()) != null);
            return result;
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
                    if (old != null && old.name == p.name && old.type == p.type)
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
                p.floatValue = p.Clamp(match.floatValue);
                p.colorValue = match.colorValue;
                p.vectorValue = match.vectorValue;
                p.textureValue = match.textureValue;
                p.transformValue = match.transformValue;
            }
        }
    }
}
