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
    internal static partial class ShaderFXMetadata
    {
        private static readonly Regex Header = new Regex(@"^//\s*@whimtex-effect\s+([^\r\n]+?)\s*$");
        private static readonly Regex Parameter = new Regex(@"^\s*//\s*@param\s+(float|float2|float3|float4|normal|point|color|texture2D|transform2D|gradient|curve)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*=\s*([^\[\];~]+?))?\s*(?:\[\s*(.*?)\s*(?<separator>\.\.|%)\s*(.*?)\s*\])?\s*$");
        private static readonly Regex If = new Regex(@"^\s*//\s*@(?:if|enableif)\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[xyzwrgba])?)\s*(==|!=)\s*([^\s]+)\s*$");
        private static readonly Regex EndIf = new Regex(@"^\s*//\s*@endif\s*$");
        private static readonly Regex EndEnableIf = new Regex(@"^\s*//\s*@endenableif\s*$");
        private static readonly Regex ContainerStart = new Regex(@"^\s*//\s*@(?<kind>group|foldout|tabs|tab)(?:\s*\((.*)\))?\s*$");
        private static readonly Regex ContainerEnd = new Regex(@"^\s*//\s*@end(?<kind>group|foldout|tabs|tab)\s*$");
        private static readonly Regex ParameterPrefix = new Regex(@"^\s*//\s*@param\b");

        private sealed class GroupDefinition
        {
            internal int id;
            internal int line;
            internal int parameterCount;
            internal int visibleParameterCount;
            internal string title;
            internal string headerParameter;
            internal ShaderFXContainerKind kind;
            internal int tabCount;
            internal readonly HashSet<string> tabNames = new HashSet<string>(StringComparer.Ordinal);
            internal ShaderFXLayoutContainer Layout => new ShaderFXLayoutContainer
                { id = id, kind = kind, title = title, headerParameter = headerParameter };
        }

        private static ShaderFXLayoutContainer[] ContainerPath(List<GroupDefinition> stack)
        {
            if (stack.Count == 0) return Array.Empty<ShaderFXLayoutContainer>();
            var path = new ShaderFXLayoutContainer[stack.Count];
            for (int i = 0; i < path.Length; i++) path[i] = stack[i].Layout;
            return path;
        }

        private static void CountContainerRow(List<GroupDefinition> stack, bool visible)
        {
            foreach (var container in stack)
            {
                container.parameterCount++;
                if (visible) container.visibleParameterCount++;
            }
        }

        internal static bool TryHeader(string firstLine, out string menuPath)
        {
            Match match = Header.Match((firstLine ?? "").TrimStart('\uFEFF'));
            menuPath = match.Success ? match.Groups[1].Value.Trim() : null;
            return match.Success;
        }

        // Quoted names are preset metadata; unquoted @group(...) remains a layout container.
        private static bool IsPresetGroup(string line) => line != null &&
            Regex.IsMatch(line.TrimStart('\uFEFF'), "^\\s*//\\s*@group\\s*\\(\\s*\"");

        internal static string ReadPresetGroup(string source, out string error)
        {
            error = null;
            string group = null;
            using var sourceReader = new StringReader(source ?? "");
            using var reader = new StringReader(ShaderFXIconMetadata.ReadHeader(sourceReader));
            bool block = false, declared = false;
            string line;
            int number = 0;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                bool directive = !block && IsPresetGroup(line);
                ShaderFXSourceBuilder.MaskComments(line, ref block);
                if (!directive) continue;
                var match = Regex.Match(line.TrimStart('\uFEFF'), "^\\s*//\\s*@group\\s*\\(\\s*\"([^\"\\r\\n]+)\"\\s*\\)\\s*$");
                if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    error = $"Line {number}: expected // @group(\"Name\") with a non-empty preset group.";
                else if (declared) error = $"Line {number}: declare only one preset @group.";
                else group = match.Groups[1].Value.Trim();
                declared = true;
            }
            return group;
        }

        private static double TransformNumber(string value)
        {
            double result = double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!ProjectiveMatrix.Finite(result)) throw new FormatException("Transform components must be finite.");
            return result;
        }


        internal static string ReadControl(string source, out string warnings)
        {
            var messages = new StringBuilder();
            string control = null;
            bool block = false;
            int count = 0, lineNumber = 0, expectedLine = 1;
            using var reader = new StringReader(source ?? "");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (lineNumber == 1 && (TryHeader(line, out _) ||
                    Regex.IsMatch(line.TrimStart('\uFEFF'), @"^//\s*@whimtex-brush\s+"))) expectedLine = 2;
                if (!block && lineNumber == expectedLine && ShaderFXPresetIdentity.IsDirective(line)) expectedLine++;
                bool directive = !block && Regex.IsMatch(line.TrimStart('\uFEFF'), @"^\s*//\s*@control\b");
                ShaderFXSourceBuilder.MaskComments(line, ref block);
                if (!directive) continue;
                count++;
                var match = Regex.Match(line.TrimStart('\uFEFF'), @"^\s*//\s*@control\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*$");
                control = match.Success ? match.Groups[1].Value : null;
                if (!match.Success) messages.AppendLine($"Warning: Line {lineNumber}: expected // @control(_Parameter).");
                if (count > 1) messages.AppendLine($"Warning: Line {lineNumber}: multiple @control declarations; the last declaration wins.");
                else if (lineNumber != expectedLine) messages.AppendLine($"Warning: Line {lineNumber}: place @control on line {expectedLine}, immediately after the effect marker or first when no marker exists.");
            }
            warnings = messages.ToString();
            return control;
        }

        internal static string ControlWarnings(string source, IReadOnlyList<ShaderFXParameter> parameters)
        {
            string name = ReadControl(source, out string warnings);
            if (name == null) return warnings;
            foreach (var parameter in parameters)
                if (parameter != null && parameter.name == name)
                    return parameter.controls.Count == 1 ? warnings : warnings +
                        $"Warning: @control parameter {name} must have one declaration to select an unambiguous field.\n";
            return warnings + $"Warning: @control refers to undeclared parameter {name}.\n";
        }

        internal static List<ShaderFXParameter> Parse(string source, bool requireHeader, out string menuPath)
            => ParseWithPreviews(source, requireHeader, out menuPath, out _);

        internal static List<ShaderFXParameter> ParseWithPreviews(string source, bool requireHeader, out string menuPath,
            out List<ShaderFXParameterControl> previews)
        {
            ShaderFXPresetIdentity.Read(source);
            previews = new List<ShaderFXParameterControl>();
            string presetGroup = ReadPresetGroup(source, out string presetGroupError);
            if (presetGroupError != null) throw new FormatException(presetGroupError);
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
            bool leadingComments = true;
            string visibleIfParameter = null;
            bool visibleIfNotEqual = false;
            double visibleIfValue = 0;
            var enableConditions = new List<ShaderFXEnableCondition>();
            var conditionScopes = new List<(bool enable, int line)>();
            var pendingHeaders = new List<string>();
            var pendingHelpBoxes = new List<string>();
            var groups = new List<GroupDefinition>();
            var containers = new List<GroupDefinition>();
            GroupDefinition activeGroup = null;
            do
            {
                lineNumber++;
                bool declaration = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@param\b");
                bool sectionHeader = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@\s*header\b");
                bool helpBox = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@\s*helpbox\b");
                bool groupStart = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@(?:group|foldout|tabs|tab)\b");
                bool groupEnd = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@end(?:group|foldout|tabs|tab)\b");
                bool ifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@if\b");
                bool endifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@endif\b");
                bool enableifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@enableif\b");
                bool endenableifDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@endenableif\b");
                bool previewDirective = !blockComment && line != null && Regex.IsMatch(line, @"^\s*//\s*@preview\b");
                bool presetGroupDirective = !blockComment && IsPresetGroup(line);
                if (line != null && !string.IsNullOrWhiteSpace(ShaderFXSourceBuilder.MaskComments(line.TrimStart('\uFEFF'), ref blockComment)))
                    leadingComments = false;
                if (presetGroupDirective)
                {
                    if (!leadingComments || presetGroup == null)
                        throw new FormatException($"Line {lineNumber}: preset @group must appear in the leading comments before HLSL code.");
                    continue;
                }
                if (ifDirective || enableifDirective)
                {
                    if (ifDirective && visibleIfParameter != null) throw new FormatException($"Line {lineNumber}: nested @if blocks are not supported.");
                    if (conditionScopes.Count >= 16) throw new FormatException($"Line {lineNumber}: at most 16 condition levels are supported.");
                    string directive = enableifDirective ? "enableif" : "if";
                    Match condition = If.Match(line);
                    if (!condition.Success || !double.TryParse(condition.Groups[3].Value == "true" ? "1" : condition.Groups[3].Value == "false" ? "0" : condition.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                        double.IsNaN(value) || double.IsInfinity(value))
                        throw new FormatException($"Line {lineNumber}: expected // @{directive} _Parameter == number or // @{directive} _Parameter != number.");
                    if (enableifDirective)
                        enableConditions.Add(new ShaderFXEnableCondition { parameter = condition.Groups[1].Value,
                            notEqual = condition.Groups[2].Value == "!=", value = value });
                    else
                    {
                        visibleIfParameter = condition.Groups[1].Value;
                        visibleIfNotEqual = condition.Groups[2].Value == "!=";
                        visibleIfValue = value;
                    }
                    conditionScopes.Add((enableifDirective, lineNumber));
                    continue;
                }
                if (endifDirective || endenableifDirective)
                {
                    string directive = endenableifDirective ? "endenableif" : "endif";
                    if (!(endenableifDirective ? EndEnableIf : EndIf).IsMatch(line))
                        throw new FormatException($"Line {lineNumber}: expected // @{directive} with no arguments.");
                    if (conditionScopes.Count == 0 || conditionScopes[conditionScopes.Count - 1].enable != endenableifDirective)
                        throw new FormatException($"Line {lineNumber}: @{directive} does not match the innermost open condition.");
                    conditionScopes.RemoveAt(conditionScopes.Count - 1);
                    if (endenableifDirective) enableConditions.RemoveAt(enableConditions.Count - 1);
                    else
                    {
                        visibleIfParameter = null;
                        visibleIfNotEqual = false;
                        visibleIfValue = 0f;
                    }
                    pendingHeaders.Clear();
                    pendingHelpBoxes.Clear();
                    continue;
                }
                if (groupStart)
                {
                    Match groupMatch = ContainerStart.Match(line);
                    if (!groupMatch.Success) throw new FormatException($"Line {lineNumber}: invalid layout container declaration.");
                    string directive = groupMatch.Groups["kind"].Value;
                    var kind = (ShaderFXContainerKind)Enum.Parse(typeof(ShaderFXContainerKind), directive, true);
                    if (conditionScopes.Count > 0) throw new FormatException($"Line {lineNumber}: @{directive} must be declared outside conditional blocks.");
                    if (containers.Count >= 16) throw new FormatException($"Line {lineNumber}: at most 16 layout levels are supported.");
                    var parent = containers.Count == 0 ? null : containers[containers.Count - 1];
                    if (kind == ShaderFXContainerKind.Tab)
                    {
                        if (parent?.kind != ShaderFXContainerKind.Tabs) throw new FormatException($"Line {lineNumber}: @tab must be directly inside @tabs.");
                    }
                    else if (parent?.kind == ShaderFXContainerKind.Tabs)
                        throw new FormatException($"Line {lineNumber}: place @{directive} inside a @tab page, not directly inside @tabs.");
                    if (kind == ShaderFXContainerKind.Tabs && containers.Exists(c => c.kind == kind))
                        throw new FormatException($"Line {lineNumber}: nested @tabs blocks are not supported.");
                    if (kind == ShaderFXContainerKind.Group && activeGroup?.kind == kind)
                        throw new FormatException($"Line {lineNumber}: nested @group blocks are not supported.");
                    string arguments = groupMatch.Groups[1].Success ? groupMatch.Groups[1].Value.Trim() : null;
                    if (groupMatch.Groups[1].Success && string.IsNullOrEmpty(arguments))
                        throw new FormatException($"Line {lineNumber}: omit parentheses for an untitled container, or provide a non-empty title.");
                    string title = null, headerParameter = null;
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        int separator = arguments.IndexOf(';');
                        title = (separator >= 0 ? arguments.Substring(0, separator) : arguments).Trim();
                        if (string.IsNullOrWhiteSpace(title) || title.IndexOf(';') >= 0)
                            throw new FormatException($"Line {lineNumber}: group title must be non-empty and cannot contain ';'.");
                        if (separator >= 0)
                        {
                            if (kind == ShaderFXContainerKind.Tabs)
                                throw new FormatException($"Line {lineNumber}: @{directive} accepts only a title, not a linked parameter.");
                            string option = arguments.Substring(separator + 1).Trim();
                            headerParameter = ParseGroupParameter(option, lineNumber);
                        }
                    }
                    if ((kind == ShaderFXContainerKind.Foldout || kind == ShaderFXContainerKind.Tab) && string.IsNullOrEmpty(title))
                        throw new FormatException($"Line {lineNumber}: @{directive} requires a title.");
                    if (kind == ShaderFXContainerKind.Tab)
                    {
                        if (!parent.tabNames.Add(title)) throw new FormatException($"Line {lineNumber}: duplicate tab title {title}.");
                        parent.tabCount++;
                    }
                    var definition = new GroupDefinition { id = groups.Count, line = lineNumber, title = title, headerParameter = headerParameter, kind = kind };
                    groups.Add(definition);
                    containers.Add(definition);
                    if (kind == ShaderFXContainerKind.Group || kind == ShaderFXContainerKind.Foldout) activeGroup = definition;
                    continue;
                }
                if (groupEnd)
                {
                    Match end = ContainerEnd.Match(line);
                    if (!end.Success) throw new FormatException($"Line {lineNumber}: expected a container end directive with no arguments.");
                    string directive = end.Groups["kind"].Value;
                    if (containers.Count == 0 || !string.Equals(containers[containers.Count - 1].kind.ToString(), directive, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException($"Line {lineNumber}: @end{directive} has no matching open @{directive} at this level.");
                    if (conditionScopes.Count > 0) throw new FormatException($"Line {lineNumber}: conditional block must end before @end{directive}.");
                    containers.RemoveAt(containers.Count - 1);
                    activeGroup = null;
                    for (int i = containers.Count - 1; i >= 0; i--)
                        if (containers[i].kind == ShaderFXContainerKind.Group || containers[i].kind == ShaderFXContainerKind.Foldout)
                        { activeGroup = containers[i]; break; }
                    pendingHeaders.Clear();
                    pendingHelpBoxes.Clear();
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
                if (previewDirective)
                {
                    if (containers.Count > 0 && containers[containers.Count - 1].kind == ShaderFXContainerKind.Tabs)
                        throw new FormatException($"Line {lineNumber}: declare previews inside a @tab page.");
                    Match preview = Regex.Match(line, @"^\s*//\s*@preview\s*\(([^()]*)\)\s*$");
                    if (!preview.Success) throw new FormatException($"Line {lineNumber}: expected // @preview(FunctionName, optional parameter names).");
                    ShaderFXPreviewFunction.Parse(preview.Groups[1].Value, out string previewFunction, out string[] previewParameters);
                    if (previews.Count >= 128) throw new FormatException($"Line {lineNumber}: at most 128 standalone previews are supported.");
                    previews.Add(new ShaderFXParameterControl { previewFunction = previewFunction, previewParameters = previewParameters, order = lineNumber,
                        headers = pendingHeaders.ToArray(), helpBoxes = pendingHelpBoxes.ToArray(),
                        inGroup = activeGroup != null, groupId = activeGroup?.id ?? 0,
                        groupTitle = activeGroup?.title, groupHeaderParameter = activeGroup?.headerParameter,
                        containers = ContainerPath(containers),
                        enableIf = enableConditions.ToArray(),
                        visibleIfParameter = visibleIfParameter, visibleIfNotEqual = visibleIfNotEqual, visibleIfValue = visibleIfValue });
                    pendingHeaders.Clear(); pendingHelpBoxes.Clear();
                    CountContainerRow(containers, true);
                    continue;
                }
                if (!declaration) continue;
                if (containers.Count > 0 && containers[containers.Count - 1].kind == ShaderFXContainerKind.Tabs)
                    throw new FormatException($"Line {lineNumber}: declare parameters inside a @tab page.");
                try
                {
                    string parameterLine = line;
                    ParseParameterModifiers(ref parameterLine, out bool hidden, out bool rgba, out bool pixelUnits, out string displayLabel, out string previewFunction, out string[] previewParameters);
                    string tooltip = ExtractTooltip(ref parameterLine);
                    ShaderFXParameter p = ParseTypedParameter(parameterLine, out bool explicitDefault, out ShaderFXParameterControl control)
                        ?? ParseOrdinaryParameter(parameterLine, pixelUnits, out explicitDefault, out control);
                    string name = p.name;
                    if (pixelUnits && control.type != ShaderFXParameterType.Float && control.type != ShaderFXParameterType.Vector2 &&
                        control.type != ShaderFXParameterType.Vector3 && control.type != ShaderFXParameterType.Vector)
                        throw new FormatException("px applies only to ordinary numeric length fields.");
                    p.pixelUnits = pixelUnits;
                    if (rgba && (p.Components < 2 || control.type != ShaderFXParameterType.Toggle && control.type != ShaderFXParameterType.Color &&
                        control.type != ShaderFXParameterType.Vector2 && control.type != ShaderFXParameterType.Vector3 && control.type != ShaderFXParameterType.Vector))
                        throw new FormatException("rgba requires a vector field or vector toggle/color.");
                    control.rgba = rgba || control.type == ShaderFXParameterType.Color;
                    control.order = lineNumber; control.tooltip = tooltip; control.label = displayLabel;
                    control.previewFunction = previewFunction; control.previewParameters = previewParameters;
                    control.headers = pendingHeaders.ToArray(); control.helpBoxes = pendingHelpBoxes.ToArray(); control.hidden = hidden;
                    control.inGroup = activeGroup != null; control.groupId = activeGroup?.id ?? 0;
                    control.groupTitle = activeGroup?.title; control.groupHeaderParameter = activeGroup?.headerParameter;
                    control.containers = ContainerPath(containers); control.enableIf = enableConditions.ToArray();
                    control.visibleIfParameter = visibleIfParameter; control.visibleIfNotEqual = visibleIfNotEqual; control.visibleIfValue = visibleIfValue;
                    pendingHeaders.Clear(); pendingHelpBoxes.Clear(); CountContainerRow(containers, !hidden);
                    p.controls.Add(control);
                    if (names.TryGetValue(name, out var existing))
                    {
                        if (!Compatible(existing.type, p.type) || existing.ValueType != p.ValueType) throw new FormatException("Conflicting storage types for " + name);
                        if (existing.pixelUnits != p.pixelUnits) throw new FormatException("Conflicting pixel units for " + name);
                        if (existing.cyclicRange != p.cyclicRange) throw new FormatException("Conflicting cyclic range declarations for " + name);
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
            if (conditionScopes.Count > 0)
            {
                var scope = conditionScopes[conditionScopes.Count - 1];
                string directive = scope.enable ? "enableif" : "if";
                throw new FormatException($"Line {scope.line}: @{directive} has no matching @end{directive}.");
            }
            if (containers.Count > 0) throw new FormatException($"Line {containers[containers.Count - 1].line}: unclosed @{containers[containers.Count - 1].kind.ToString().ToLowerInvariant()} container.");
            foreach (var group in groups)
            {
                string directive = group.kind.ToString().ToLowerInvariant();
                if (group.kind == ShaderFXContainerKind.Tabs && group.tabCount == 0)
                    throw new FormatException($"Line {group.line}: @tabs must contain at least one @tab page.");
                if (group.parameterCount == 0) throw new FormatException($"Line {group.line}: @{directive} must contain at least one parameter or preview.");
                if (group.visibleParameterCount == 0) throw new FormatException($"Line {group.line}: @{directive} must contain at least one visible parameter or preview.");
                if (string.IsNullOrEmpty(group.headerParameter)) continue;
                if (!names.TryGetValue(group.headerParameter, out ShaderFXParameter headerParameter))
                    throw new FormatException($"Line {group.line}: @{directive} header parameter {group.headerParameter} is not declared in the container.");
                if (headerParameter.controls.Count != 1)
                    throw new FormatException($"Line {group.line}: @{directive} header parameter {group.headerParameter} must be declared once.");
                ShaderFXParameterControl headerControl = headerParameter.controls[0];
                if (headerControl.containers.Length == 0 || headerControl.containers[headerControl.containers.Length - 1].id != group.id ||
                    !string.IsNullOrEmpty(headerControl.visibleIfParameter) || headerControl.enableIf.Length > 0)
                    throw new FormatException($"Line {group.line}: @{directive} header parameter {group.headerParameter} must be unconditional and directly inside its own container.");
                if (group.kind == ShaderFXContainerKind.Tab && (headerControl.type != ShaderFXParameterType.Toggle || headerParameter.Components != 1))
                    throw new FormatException($"Line {group.line}: @tab header parameter {group.headerParameter} must be a scalar toggle.");
            }
            var conditions = new List<ShaderFXParameterControl>(previews);
            foreach (var parameter in result) conditions.AddRange(parameter.controls);
            foreach (var control in conditions)
            {
                if (!string.IsNullOrEmpty(control.visibleIfParameter))
                    ValidateCondition(names, control.visibleIfParameter, "if", control.order);
                foreach (var condition in control.enableIf)
                    ValidateCondition(names, condition.parameter, "enableif", control.order);
            }
            return result;
        }

        private static void ValidateCondition(Dictionary<string, ShaderFXParameter> names, string name, string directive, int line)
        {
            if (!names.TryGetValue(name.Split('.')[0], out var driver))
                throw new FormatException($"Line {line}: @{directive} references undeclared parameter {name}.");
            if (!ShaderFXNumericValue.TryCondition(driver, name, out _))
                throw new FormatException($"Line {line}: @{directive} parameter {name} must be a numeric scalar or an explicit vector component.");
            if (driver.controls.Exists(c => !string.IsNullOrEmpty(c.visibleIfParameter)))
                throw new FormatException($"Line {line}: @{directive} parameter {name} must be declared outside visibility conditions.");
        }

        private static void ParseParameterModifiers(ref string line, out bool hidden, out bool rgba, out bool pixelUnits, out string label, out string previewFunction, out string[] previewParameters)
        {
            hidden = false;
            rgba = false;
            pixelUnits = false;
            label = null;
            previewFunction = null;
            previewParameters = Array.Empty<string>();
            Match prefix = ParameterPrefix.Match(line);
            if (!prefix.Success) return;

            int position = prefix.Length;
            while (position < line.Length)
            {
                while (position < line.Length && char.IsWhiteSpace(line[position])) position++;
                if (position >= line.Length) break;

                if (Regex.IsMatch(line.Substring(position), @"^preview\b"))
                {
                    if (previewFunction != null) throw new FormatException("The preview modifier may be specified only once.");
                    Match preview = Regex.Match(line.Substring(position), @"^preview\s*\(([^()]*)\)(?=\s|$)");
                    if (!preview.Success) throw new FormatException("Expected preview(FunctionName, optional parameter names).");
                    ShaderFXPreviewFunction.Parse(preview.Groups[1].Value, out previewFunction, out previewParameters);
                    position += preview.Length;
                    continue;
                }

                if (line.IndexOf("px", position, StringComparison.Ordinal) == position &&
                    (position + 2 == line.Length || char.IsWhiteSpace(line[position + 2])))
                {
                    if (pixelUnits) throw new FormatException("The px modifier may be specified only once.");
                    pixelUnits = true;
                    position += 2;
                    continue;
                }

                if (line.IndexOf("rgba", position, StringComparison.Ordinal) == position &&
                    (position + 4 == line.Length || char.IsWhiteSpace(line[position + 4])))
                {
                    if (rgba) throw new FormatException("The rgba modifier may be specified only once.");
                    rgba = true; position += 4; continue;
                }

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
            var gradient = GradientUtility.Create(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
            // Endpoint-only defaults describe a straight encoded-RGB blend.
            gradient.ColorMode = WhimTexGradientColorMode.Gamma;
            gradient.Interpolation = WhimTexGradientInterpolation.Blend;
            gradient.Smoothness = 0f;
            return gradient;
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
                    if (old != null && old.name == p.name &&
                        ((Compatible(old.type, p.type) || old.type == ShaderFXParameterType.Vector && p.type == ShaderFXParameterType.Vector3) && old.ValueType == p.ValueType ||
                         old.type == ShaderFXParameterType.Color && p.type == ShaderFXParameterType.Color && old.ValueType == "float4" && p.ValueType == "float3"))
                    {
                        if (match == null)
                            match = old;
                    }
                // A rename in place keeps identity. Do not guess across insertions/removals or reorders.
                if (match == null && next.Count == previous.Count && previous[i] is ShaderFXParameter candidate &&
                    candidate.type == p.type && candidate.ValueType == p.ValueType && !next.Exists(item => item.name == candidate.name))
                {
                    bool reusedName = false;
                    foreach (var old in previous) reusedName |= old != null && old.name == p.name;
                    if (!reusedName) match = candidate;
                }
                if (match == null) continue;
                p.id = match.id;
                p.integerValues = match.integerValues == null ? new long[4] : (long[])match.integerValues.Clone();
                p.floatValue = match.floatValue;
                p.colorValue = match.colorValue;
                p.vectorValue = p.ClampInterval(match.vectorValue);
                p.textureValue = match.textureValue;
                p.textureSource = match.textureSource;
                p.textureLayerId = match.textureLayerId;
                p.gradientValue = match.gradientValue?.Clone();
                p.curveValue = match.curveValue == null ? null : WhimTexCurveTexture.Copy(match.curveValue);
                p.transformValue = match.transformValue;
            }
        }

        private static ShaderFXParameter ParseOrdinaryParameter(string parameterLine, bool pixelUnits,
            out bool explicitDefault, out ShaderFXParameterControl control)
        {
                    Match match = Parameter.Match(parameterLine);
                    if (!match.Success) throw new FormatException("Expected @param type name = value [min .. max] or [min % max], without a semicolon.");
                    string kind = match.Groups[1].Value;
                    string name = match.Groups[2].Value;
                    var p = new ShaderFXParameter { name = name, floatValue = 0f, colorValue = Color.clear, pixelUnits = pixelUnits };
                    string value = match.Groups[3].Value.Trim();
                    explicitDefault = match.Groups[3].Success;
                    bool bounded = match.Groups[4].Success;
                    p.cyclicRange = bounded && match.Groups["separator"].Value == "%";
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
                                if (p.cyclicRange && (p.HasSoftRange || !p.hasMinimum || !p.hasMaximum || p.minimum >= p.maximum))
                                    throw new FormatException("A % range needs two hard finite limits with min < max.");
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
                            if (kind == "normal") p.vectorValue = ShaderFXParameter.NormalizeNormal(p.vectorValue);
                            if (kind == "float2" && bounded)
                            {
                                p.hasMinimum = p.hasMaximum = true;
                                p.minimum = Number(match.Groups[4].Value.Trim());
                                p.maximum = Number(match.Groups[5].Value.Trim());
                                if (p.minimum >= p.maximum) throw new FormatException("A float2 interval needs two hard finite limits with min < max.");
                                if (!explicitDefault) p.vectorValue = new Vector4(p.minimum, p.maximum, 0, 0);
                                if (!ShaderFXParameter.IsValidInterval(p.vectorValue, p.minimum, p.maximum, p.cyclicRange))
                                    throw new FormatException(p.cyclicRange ? "Cyclic endpoints must be within the limits, or one period apart with at least one endpoint within the limits." : "A float2 interval default must satisfy min <= x <= y <= max.");
                            }
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
                    if (kind != "float" && kind != "float2" && bounded) throw new FormatException("Ranges apply only to float and float2 parameters.");
                    if (pixelUnits && (kind != "float" && kind != "float2" && kind != "float3" && kind != "float4"))
                        throw new FormatException("The px modifier applies only to numeric float, float2, float3 or float4 lengths, without seed.");
                    control = new ShaderFXParameterControl { type = p.type, valueType = p.ValueType,
                        hasMinimum = p.hasMinimum, hasMaximum = p.hasMaximum, softMinimum = p.softMinimum, softMaximum = p.softMaximum,
                        cyclicRange = p.cyclicRange, minimum = p.minimum, maximum = p.maximum };
                    return p;
        }

        internal static bool IsScalar(ShaderFXParameterType type) => type == ShaderFXParameterType.Float || type == ShaderFXParameterType.Toggle || type == ShaderFXParameterType.Enum;
        internal static bool Compatible(ShaderFXParameterType a, ShaderFXParameterType b) => a == b || IsScalar(a) && IsScalar(b) ||
            a == ShaderFXParameterType.Vector2 && b == ShaderFXParameterType.Point || a == ShaderFXParameterType.Point && b == ShaderFXParameterType.Vector2;
        internal static void CopyValue(ShaderFXParameter source, ShaderFXParameter target)
        {
            target.integerValues = source.integerValues == null ? new long[4] : (long[])source.integerValues.Clone();
            target.floatValue = source.floatValue; target.colorValue = source.colorValue;
            target.gradientValue = source.gradientValue?.Clone();
            target.curveValue = source.curveValue == null ? null : WhimTexCurveTexture.Copy(source.curveValue);
            target.vectorValue = source.vectorValue; target.textureValue = source.textureValue; target.transformValue = source.transformValue;
            target.textureSource = source.textureSource; target.textureLayerId = source.textureLayerId;
        }
    }
}
