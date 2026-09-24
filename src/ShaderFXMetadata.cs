using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXMetadata
    {
        private static readonly Regex Header = new Regex(@"^//\s*@whimtex-effect\s+([^\r\n]+?)\s*$");
        private static readonly Regex Parameter = new Regex(@"^\s*//\s*@param\s+(float|bool|float2|float3|float4|normal|point|color|texture2D|transform2D|gradient|curve)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^\[\];~]+?))?\s*(?:\[\s*(.*?)\s*\.\.\s*(.*?)\s*\])?\s*$");
        private static readonly Regex If = new Regex(@"^\s*//\s*@if\s+([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=)\s*([^\s]+)\s*$");
        private static readonly Regex EndIf = new Regex(@"^\s*//\s*@endif\s*$");
        private static readonly Regex GroupStart = new Regex(@"^\s*//\s*@group(?:\s*\((.*)\))?\s*$");
        private static readonly Regex GroupEnd = new Regex(@"^\s*//\s*@endgroup\s*$");
        private static readonly Regex ParameterPrefix = new Regex(@"^\s*//\s*@param\b");
        private static readonly Regex FormerlySerializedAsStart = new Regex(@"^\s*//\s*@formerlyserializedas\b");
        private static readonly Regex FormerlySerializedAs = new Regex(@"^\s*//\s*@formerlyserializedas\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*$");

        private sealed class GroupDefinition
        {
            internal int id;
            internal int line;
            internal int parameterCount;
            internal int visibleParameterCount;
            internal string title;
            internal string headerParameter;
        }

        internal static bool TryHeader(string firstLine, out string menuPath)
        {
            Match match = Header.Match((firstLine ?? "").TrimStart('\uFEFF'));
            menuPath = match.Success ? match.Groups[1].Value.Trim() : null;
            return match.Success;
        }

        private static double TransformNumber(string value)
        {
            double result = double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!ProjectiveMatrix.Finite(result)) throw new FormatException("Transform components must be finite.");
            return result;
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
            string visibleIfParameter = null;
            bool visibleIfNotEqual = false;
            float visibleIfValue = 0f;
            int ifLine = 0;
            var pendingHeaders = new List<string>();
            var pendingHelpBoxes = new List<string>();
            var pendingFormerNames = new List<string>();
            var groups = new List<GroupDefinition>();
            GroupDefinition activeGroup = null;
            do
            {
                lineNumber++;
                bool declaration = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@param\b");
                bool sectionHeader = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@\s*header\b");
                bool helpBox = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@\s*helpbox\b");
                bool groupStart = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@group\b");
                bool groupEnd = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@endgroup\b");
                bool ifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@if\b");
                bool endifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@endif\b");
                bool formerlySerializedAs = !blockComment && line != null && FormerlySerializedAsStart.IsMatch(line);
                if (line != null) ShaderFXSourceBuilder.MaskComments(line, ref blockComment);
                if (formerlySerializedAs)
                {
                    Match formerName = FormerlySerializedAs.Match(line);
                    if (!formerName.Success)
                        throw new FormatException($"Line {lineNumber}: expected // @formerlyserializedas(_OldName) immediately before a parameter declaration.");
                    if (pendingFormerNames.Count >= 16)
                        throw new FormatException($"Line {lineNumber}: a parameter may have at most 16 former names.");
                    string former = formerName.Groups[1].Value;
                    if (pendingFormerNames.Contains(former))
                        throw new FormatException($"Line {lineNumber}: duplicate former parameter name {former}.");
                    pendingFormerNames.Add(former);
                    continue;
                }
                if (ifDirective)
                {
                    if (visibleIfParameter != null) throw new FormatException($"Line {lineNumber}: nested @if blocks are not supported.");
                    Match condition = If.Match(line);
                    if (!condition.Success || !float.TryParse(condition.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out visibleIfValue) ||
                        float.IsNaN(visibleIfValue) || float.IsInfinity(visibleIfValue))
                        throw new FormatException($"Line {lineNumber}: expected // @if _Parameter == number or // @if _Parameter != number.");
                    visibleIfParameter = condition.Groups[1].Value;
                    visibleIfNotEqual = condition.Groups[2].Value == "!=";
                    ifLine = lineNumber;
                    continue;
                }
                if (endifDirective)
                {
                    if (!EndIf.IsMatch(line)) throw new FormatException($"Line {lineNumber}: expected // @endif with no arguments.");
                    if (visibleIfParameter == null) throw new FormatException($"Line {lineNumber}: @endif has no matching @if.");
                    visibleIfParameter = null;
                    visibleIfNotEqual = false;
                    visibleIfValue = 0f;
                    ifLine = 0;
                    pendingHeaders.Clear();
                    pendingHelpBoxes.Clear();
                    pendingFormerNames.Clear();
                    continue;
                }
                if (groupStart)
                {
                    if (!GroupStart.IsMatch(line)) throw new FormatException($"Line {lineNumber}: expected // @group, // @group(Title), or // @group(Title; _Parameter).");
                    if (activeGroup != null) throw new FormatException($"Line {lineNumber}: nested @group blocks are not supported.");
                    if (visibleIfParameter != null) throw new FormatException($"Line {lineNumber}: @group must be declared outside conditional blocks.");
                    Match groupMatch = GroupStart.Match(line);
                    string arguments = groupMatch.Groups[1].Success ? groupMatch.Groups[1].Value.Trim() : null;
                    if (groupMatch.Groups[1].Success && string.IsNullOrEmpty(arguments))
                        throw new FormatException($"Line {lineNumber}: use // @group for an untitled group, or provide a non-empty title in parentheses.");
                    string title = null, headerParameter = null;
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        int separator = arguments.IndexOf(';');
                        title = (separator >= 0 ? arguments.Substring(0, separator) : arguments).Trim();
                        if (string.IsNullOrWhiteSpace(title) || title.IndexOf(';') >= 0)
                            throw new FormatException($"Line {lineNumber}: group title must be non-empty and cannot contain ';'.");
                        if (separator >= 0)
                        {
                            string option = arguments.Substring(separator + 1).Trim();
                            headerParameter = ParseGroupParameter(option, lineNumber);
                        }
                    }
                    activeGroup = new GroupDefinition { id = groups.Count, line = lineNumber, title = title, headerParameter = headerParameter };
                    groups.Add(activeGroup);
                    continue;
                }
                if (groupEnd)
                {
                    if (!GroupEnd.IsMatch(line)) throw new FormatException($"Line {lineNumber}: expected // @endgroup with no arguments.");
                    if (activeGroup == null) throw new FormatException($"Line {lineNumber}: @endgroup has no matching @group.");
                    if (visibleIfParameter != null) throw new FormatException($"Line {lineNumber}: conditional block must end before @endgroup.");
                    activeGroup = null;
                    pendingHeaders.Clear();
                    pendingHelpBoxes.Clear();
                    pendingFormerNames.Clear();
                    continue;
                }
                if (sectionHeader)
                {
                    var heading = Regex.Match(line, @"^\s*//\s*@\s*header\s*\((.+)\)\s*$");
                    if (!heading.Success || string.IsNullOrWhiteSpace(heading.Groups[1].Value))
                        throw new FormatException($"Line {lineNumber}: expected // @header(Name) with a non-empty title.");
                    if (pendingHeaders.Count >= 128) throw new FormatException($"Line {lineNumber}: too many consecutive headers.");
                    pendingHeaders.Add(heading.Groups[1].Value.Trim());
                    continue;
                }
                if (helpBox)
                {
                    var message = Regex.Match(line, @"^\s*//\s*@\s*helpbox\s*\((.*)\)\s*$");
                    if (!message.Success || string.IsNullOrWhiteSpace(message.Groups[1].Value))
                        throw new FormatException($"Line {lineNumber}: expected // @helpbox(Message) with a non-empty message.");
                    if (pendingHelpBoxes.Count >= 128) throw new FormatException($"Line {lineNumber}: too many consecutive help boxes.");
                    pendingHelpBoxes.Add(message.Groups[1].Value.Trim());
                    continue;
                }
                if (!declaration) continue;
                try
                {
                    string parameterLine = line;
                    ParseParameterModifiers(ref parameterLine, out bool hidden, out string displayLabel);
                    string tooltip = ExtractTooltip(ref parameterLine);
                    Match enumMatch = Regex.Match(parameterLine, @"^\s*//\s*@param\s+enum\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^{}]+?))?\s*\{([^{}]+)\}\s*$");
                    Match match = Parameter.Match(enumMatch.Success ? "// @param float " + enumMatch.Groups[1].Value : parameterLine);
                    if (!match.Success) throw new FormatException("Expected @param type name = value [min .. max], without a semicolon.");
                    string kind = match.Groups[1].Value;
                    string name = match.Groups[2].Value;
                    if (pendingFormerNames.Contains(name))
                        throw new FormatException("A parameter cannot list its current name as a former name: " + name);
                    var p = new ShaderFXParameter { name = name, declaredInCode = true, floatValue = 0f, colorValue = Color.clear };
                    string value = match.Groups[3].Value.Trim();
                    bool explicitDefault = match.Groups[3].Success;
                    bool bounded = match.Groups[4].Success;
                    switch (kind)
                    {
                        case "curve":
                            p.type = ShaderFXParameterType.Curve;
                            p.curveValue = explicitDefault ? WhimTexCurveTexture.Parse(value) : WhimTexCurveTexture.Default();
                            break;
                        case "gradient":
                            p.type = ShaderFXParameterType.Gradient;
                            p.gradientValue = explicitDefault ? ParseGradientDefault(value) : new WhimTexGradient();
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
                                string lower = match.Groups[4].Value.Trim(), upper = match.Groups[5].Value.Trim();
                                p.softMinimum = lower.StartsWith("~"); p.softMaximum = upper.StartsWith("~");
                                if (p.softMinimum) lower = lower.Substring(1).Trim();
                                if (p.softMaximum) upper = upper.Substring(1).Trim();
                                p.hasMinimum = lower.Length > 0;
                                p.hasMaximum = upper.Length > 0;
                                if (!p.hasMinimum && !p.hasMaximum) throw new FormatException("A range needs at least one boundary.");
                                if (p.hasMinimum) p.minimum = Number(lower);
                                if (p.hasMaximum) p.maximum = Number(upper);
                                if (p.hasMinimum && p.hasMaximum && p.minimum > p.maximum) throw new FormatException("Minimum exceeds maximum.");
                                if (p.HasSoftRange && (!p.hasMinimum || !p.hasMaximum || p.minimum >= p.maximum))
                                    throw new FormatException("A range with soft boundaries requires two finite values with min < max. Put ~ before each soft value: [min .. ~max].");
                            }
                            // A default outside a hard bound is an authoring mistake; a soft bound only shapes the slider.
                            if (explicitDefault && (p.hasMinimum && !p.softMinimum && p.floatValue < p.minimum ||
                                p.hasMaximum && !p.softMaximum && p.floatValue > p.maximum))
                                throw new FormatException("Default " + value + " is outside the declared range.");
                            break;
                        case "float2":
                        case "float3":
                        case "normal":
                        case "point":
                            int count = kind == "float2" || kind == "point" ? 2 : 3;
                            p.type = kind == "normal" ? ShaderFXParameterType.Normal : kind == "point" ? ShaderFXParameterType.Point : count == 2 ? ShaderFXParameterType.Vector2 : ShaderFXParameterType.Vector3;
                            p.vectorValue = kind == "normal" ? new Vector4(0,0,1,0) : kind == "point" ? new Vector4(.5f,.5f,0,0) : Vector4.zero;
                            if (explicitDefault)
                            {
                                if (!value.StartsWith("(") || !value.EndsWith(")")) throw new FormatException("Expected components in parentheses.");
                                var values = value.Substring(1,value.Length-2).Split(',');
                                if (values.Length != count) throw new FormatException("Expected " + count + " components.");
                                for (int i=0;i<count;i++) p.vectorValue[i] = Number(values[i]);
                            }
                            if (kind == "point" && (p.vectorValue.x < 0 || p.vectorValue.x > 1 || p.vectorValue.y < 0 || p.vectorValue.y > 1))
                                throw new FormatException("Point coordinates must be in the normalized canvas range 0..1.");
                            if (kind == "normal") p.vectorValue = ShaderFXParameter.NormalizeNormal(p.vectorValue);
                            break;
                        case "float4":
                        case "color":
                            if (!explicitDefault) value = "(0, 0, 0, 0)";
                            Color parsedColor = kind == "color" && value.StartsWith("#", StringComparison.Ordinal)
                                ? ParseHexColor(value)
                                : ParseTupleColor(value);
                            p.vectorValue = new Vector4(parsedColor.r, parsedColor.g, parsedColor.b, parsedColor.a);
                            p.type = kind == "color" ? ShaderFXParameterType.Color : ShaderFXParameterType.Vector;
                            p.colorValue = parsedColor;
                            break;
                        case "texture2D":
                            p.type = ShaderFXParameterType.Texture2D;
                            if (value == "none" || value == "self")
                            {
                                p.textureSource = value == "self" ? ShaderFXTextureSource.Self : ShaderFXTextureSource.None;
                                break;
                            }
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
                                if (value.StartsWith("matrix(") && value.EndsWith(")"))
                                {
                                    string[] entries = value.Substring(7, value.Length - 8).Split(',');
                                    if (entries.Length != 9) throw new FormatException("Expected nine row-major matrix components.");
                                    double D(int i) => TransformNumber(entries[i]);
                                    var matrix = new ProjectiveMatrix { m00=D(0),m01=D(1),m02=D(2),m10=D(3),m11=D(4),m12=D(5),m20=D(6),m21=D(7),m22=D(8) };
                                    if (!p.transformValue.TrySetMatrix(matrix)) throw new FormatException("Transform matrix must be invertible with no horizon crossing its rectangle.");
                                    break;
                                }
                                if (!value.StartsWith("(") || !value.EndsWith(")")) throw new FormatException("Expected (x, y, width, height, angle) or matrix(nine row-major values).");
                                string[] components = value.Substring(1, value.Length - 2).Split(',');
                                if (components.Length != 5) throw new FormatException("Expected five transform components.");
                                p.transformValue = new ShaderFXTransform
                                {
                                    position = new Double2(TransformNumber(components[0]), TransformNumber(components[1])),
                                    size = new Double2(TransformNumber(components[2]), TransformNumber(components[3])), rotation = TransformNumber(components[4])
                                };
                            }
                            break;
                    }
                    if (kind != "float" && bounded) throw new FormatException("Ranges apply only to float parameters.");
                    var control = new ShaderFXParameterControl { type = p.type, order = lineNumber, tooltip = tooltip, label = displayLabel,
                        headers = pendingHeaders.ToArray(),
                        helpBoxes = pendingHelpBoxes.ToArray(),
                        formerlySerializedAs = pendingFormerNames.ToArray(),
                        hidden = hidden,
                        inGroup = activeGroup != null,
                        groupId = activeGroup?.id ?? 0,
                        groupTitle = activeGroup?.title,
                        groupHeaderParameter = activeGroup?.headerParameter,
                        hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum, softMinimum = p.softMinimum, softMaximum = p.softMaximum, minimum = p.minimum, maximum = p.maximum,
                        visibleIfParameter = visibleIfParameter, visibleIfNotEqual = visibleIfNotEqual, visibleIfValue = visibleIfValue };
                    pendingHeaders.Clear();
                    pendingHelpBoxes.Clear();
                    pendingFormerNames.Clear();
                    if (activeGroup != null)
                    {
                        activeGroup.parameterCount++;
                        if (!hidden) activeGroup.visibleParameterCount++;
                    }
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
            if (visibleIfParameter != null) throw new FormatException($"Line {ifLine}: @if has no matching @endif.");
            if (activeGroup != null) throw new FormatException($"Line {activeGroup.line}: @group has no matching @endgroup.");
            if (pendingFormerNames.Count > 0) throw new FormatException($"Line {lineNumber}: @formerlyserializedas must be followed by a parameter declaration.");
            foreach (var group in groups)
            {
                if (group.parameterCount == 0) throw new FormatException($"Line {group.line}: @group must contain at least one parameter.");
                if (group.visibleParameterCount == 0) throw new FormatException($"Line {group.line}: @group must contain at least one visible parameter.");
                if (string.IsNullOrEmpty(group.headerParameter)) continue;
                if (!names.TryGetValue(group.headerParameter, out ShaderFXParameter headerParameter))
                    throw new FormatException($"Line {group.line}: group header parameter {group.headerParameter} is not declared in the group.");
                if (headerParameter.controls.Count != 1)
                    throw new FormatException($"Line {group.line}: group header parameter {group.headerParameter} must be declared once.");
                ShaderFXParameterControl headerControl = headerParameter.controls[0];
                if (!headerControl.inGroup || headerControl.groupId != group.id ||
                    !string.IsNullOrEmpty(headerControl.visibleIfParameter))
                    throw new FormatException($"Line {group.line}: group header parameter {group.headerParameter} must be unconditional and declared inside the group.");
            }
            var formerNameOwners = new Dictionary<string, ShaderFXParameter>(StringComparer.Ordinal);
            foreach (var parameter in result)
                foreach (var control in parameter.controls)
                    if (control.formerlySerializedAs != null)
                        foreach (string formerName in control.formerlySerializedAs)
                        {
                            if (names.TryGetValue(formerName, out ShaderFXParameter currentOwner) && !ReferenceEquals(currentOwner, parameter))
                                throw new FormatException($"Line {control.order}: former parameter name {formerName} is still used by another parameter.");
                            if (formerNameOwners.TryGetValue(formerName, out ShaderFXParameter formerOwner) && !ReferenceEquals(formerOwner, parameter))
                                throw new FormatException($"Line {control.order}: former parameter name {formerName} is claimed by more than one parameter.");
                            formerNameOwners[formerName] = parameter;
                        }
            foreach (var parameter in result)
                foreach (var control in parameter.controls)
                    if (!string.IsNullOrEmpty(control.visibleIfParameter))
                    {
                        ShaderFXParameter driver = result.Find(p => p.name == control.visibleIfParameter);
                        if (driver == null)
                            throw new FormatException($"Line {control.order}: @if references undeclared parameter {control.visibleIfParameter}.");
                        if (!IsScalar(driver.type))
                            throw new FormatException($"Line {control.order}: @if parameter {control.visibleIfParameter} must be float, bool or enum.");
                        if (driver.controls.Exists(c => !string.IsNullOrEmpty(c.visibleIfParameter)))
                            throw new FormatException($"Line {control.order}: @if parameter {control.visibleIfParameter} must be declared outside conditional blocks.");
                    }
            return result;
        }

        private static void ParseParameterModifiers(ref string line, out bool hidden, out string label)
        {
            hidden = false;
            label = null;
            Match prefix = ParameterPrefix.Match(line);
            if (!prefix.Success) return;

            int position = prefix.Length;
            while (position < line.Length)
            {
                while (position < line.Length && char.IsWhiteSpace(line[position])) position++;
                if (position >= line.Length) break;

                if (line.IndexOf("hidden", position, StringComparison.Ordinal) == position &&
                    (position + 6 == line.Length || char.IsWhiteSpace(line[position + 6])))
                {
                    if (hidden) throw new FormatException("The hidden modifier may be specified only once.");
                    hidden = true;
                    position += 6;
                    continue;
                }

                if (line.IndexOf("label", position, StringComparison.Ordinal) == position &&
                    (position + 5 == line.Length || char.IsWhiteSpace(line[position + 5]) || line[position + 5] == '('))
                {
                    if (label != null) throw new FormatException("The label modifier may be specified only once.");
                    position += 5;
                    while (position < line.Length && char.IsWhiteSpace(line[position])) position++;
                    if (position >= line.Length || line[position] != '(')
                        throw new FormatException("Expected label text in label(...).");

                    int valueStart = ++position;
                    int depth = 1;
                    bool quoted = false, escaped = false;
                    while (position < line.Length && depth > 0)
                    {
                        char c = line[position];
                        if (escaped) { escaped = false; position++; continue; }
                        if (quoted && c == '\\') { escaped = true; position++; continue; }
                        if (c == '"') { quoted = !quoted; position++; continue; }
                        if (!quoted && c == '(') depth++;
                        else if (!quoted && c == ')') depth--;
                        if (depth > 0) position++;
                    }
                    if (depth != 0 || quoted) throw new FormatException("Unterminated label(...).");
                    string parsedLabel = line.Substring(valueStart, position - valueStart).Trim();
                    position++; // closing parenthesis
                    if (parsedLabel.Length >= 2 && parsedLabel[0] == '"' && parsedLabel[parsedLabel.Length - 1] == '"')
                        parsedLabel = UnescapeLabel(parsedLabel.Substring(1, parsedLabel.Length - 2));
                    if (string.IsNullOrWhiteSpace(parsedLabel)) throw new FormatException("The label modifier must contain non-empty text.");
                    label = parsedLabel;
                    continue;
                }

                break;
            }

            line = line.Substring(0, prefix.Length) + " " + line.Substring(position).TrimStart();
        }

        private static string UnescapeLabel(string value)
        {
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length && (value[i + 1] == '\\' || value[i + 1] == '"'))
                    c = value[++i];
                result.Append(c);
            }
            return result.ToString();
        }

        private static string ExtractTooltip(ref string line)
        {
            bool quoted = false, escaped = false;
            int parenthesisDepth = 0;
            for (int i = line.IndexOf("@param", StringComparison.Ordinal) + 6; i < line.Length - 1; i++)
            {
                char c = line[i];
                if (escaped) { escaped = false; continue; }
                if (quoted && c == '\\') { escaped = true; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (!quoted && c == '(') { parenthesisDepth++; continue; }
                if (!quoted && c == ')' && parenthesisDepth > 0) { parenthesisDepth--; continue; }
                if (!quoted && parenthesisDepth == 0 && c == '/' && line[i + 1] == '/')
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

        private static string ParseGroupParameter(string value, int lineNumber)
        {
            string parameter = value.Trim();
            if (!Regex.IsMatch(parameter, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new FormatException($"Line {lineNumber}: expected a valid parameter name after ';'.");
            return parameter;
        }

        private static WhimTexGradient ParseGradientDefault(string value)
        {
            int separator = value.IndexOf("->", StringComparison.Ordinal);
            if (separator < 0 || value.IndexOf("->", separator + 2, StringComparison.Ordinal) >= 0)
                throw new FormatException("Expected two colors separated by ->, for example #FF0000 -> #0000FFFF.");
            Color start = ParseDefaultColor(value.Substring(0, separator));
            Color end = ParseDefaultColor(value.Substring(separator + 2));
            return GradientUtility.Create(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        }

        private static Color ParseDefaultColor(string value) => value.TrimStart().StartsWith("#", StringComparison.Ordinal)
            ? ParseHexColor(value.Trim())
            : ParseTupleColor(value.Trim());

        private static Color ParseTupleColor(string value)
        {
            if (!value.StartsWith("(", StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal))
                throw new FormatException("Expected #RRGGBB, #RRGGBBAA or four components in parentheses.");
            string[] parts = value.Substring(1, value.Length - 2).Split(',');
            if (parts.Length != 4) throw new FormatException("Expected four color components.");
            return new Color(Number(parts[0]), Number(parts[1]), Number(parts[2]), Number(parts[3]));
        }

        private static Color ParseHexColor(string value)
        {
            if ((value.Length != 7 && value.Length != 9) || value[0] != '#')
                throw new FormatException("Hex colors must use #RRGGBB or #RRGGBBAA format.");
            try
            {
                byte Component(int offset) => byte.Parse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                float alpha = value.Length == 9 ? Component(7) / 255f : 1f;
                return new Color(Component(1) / 255f, Component(3) / 255f, Component(5) / 255f, alpha);
            }
            catch (FormatException) { throw new FormatException("Hex colors must use #RRGGBB or #RRGGBBAA format."); }
        }

        internal static void PreserveValues(List<ShaderFXParameter> next, IReadOnlyList<ShaderFXParameter> previous)
        {
            for (int i = 0; i < next.Count; i++)
            {
                var p = next[i];
                ShaderFXParameter match = null;
                foreach (var old in previous)
                    if (old != null && old.name == p.name && (Compatible(old.type, p.type) || old.type == ShaderFXParameterType.Vector && p.type == ShaderFXParameterType.Vector3))
                    {
                        // If a legacy hand-authored value and a previous code
                        // declaration coexist, migrate the hand-authored value
                        // into the single declaration-driven parameter.
                        if (match == null || match.declaredInCode && !old.declaredInCode)
                            match = old;
                    }
                if (match == null)
                    foreach (var old in previous)
                        if (old != null && HasFormerName(p, old.name) &&
                            (Compatible(old.type, p.type) || old.type == ShaderFXParameterType.Vector && p.type == ShaderFXParameterType.Vector3))
                        {
                            if (match == null || match.declaredInCode && !old.declaredInCode)
                                match = old;
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
                p.textureSource = match.textureSource;
                p.textureLayerId = match.textureLayerId;
                p.gradientValue = match.gradientValue?.Clone();
                p.curveValue = match.curveValue == null ? null : WhimTexCurveTexture.Copy(match.curveValue);
                p.transformValue = match.transformValue;
            }
        }

        internal static bool HasFormerName(ShaderFXParameter parameter, string name)
        {
            if (parameter?.controls == null || string.IsNullOrEmpty(name)) return false;
            foreach (var control in parameter.controls)
                if (control?.formerlySerializedAs != null)
                    foreach (string formerName in control.formerlySerializedAs)
                        if (formerName == name) return true;
            return false;
        }

        internal static bool MatchesNameOrFormerName(ShaderFXParameter parameter, string name, ShaderFXParameterType type)
        {
            if (parameter == null || !(Compatible(parameter.type, type) || parameter.type == ShaderFXParameterType.Vector && type == ShaderFXParameterType.Vector3))
                return false;
            return parameter.name == name || HasFormerName(parameter, name);
        }

        internal static bool IsScalar(ShaderFXParameterType type) => type == ShaderFXParameterType.Float || type == ShaderFXParameterType.Bool || type == ShaderFXParameterType.Enum;
        internal static bool Compatible(ShaderFXParameterType a, ShaderFXParameterType b) => a == b || IsScalar(a) && IsScalar(b) ||
            a == ShaderFXParameterType.Vector2 && b == ShaderFXParameterType.Point || a == ShaderFXParameterType.Point && b == ShaderFXParameterType.Vector2;
        private static void CopyValue(ShaderFXParameter source, ShaderFXParameter target)
        {
            target.floatValue = source.floatValue; target.colorValue = source.colorValue;
            target.gradientValue = source.gradientValue?.Clone();
            target.curveValue = source.curveValue == null ? null : WhimTexCurveTexture.Copy(source.curveValue);
            target.vectorValue = source.vectorValue; target.textureValue = source.textureValue; target.transformValue = source.transformValue;
            target.textureSource = source.textureSource; target.textureLayerId = source.textureLayerId;
        }
    }
}
