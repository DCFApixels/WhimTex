using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static class ShaderPresetWriter
    {
        internal static string BuildSource(ShaderFX effect, string menuPath)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (effect.Mode == ShaderProgramMode.Graph)
                throw new InvalidOperationException("Save a Graph program as " + ShaderPresetIO.FileExtension + " to retain its editable graph.");
            return BuildParameterSource(effect.Code, effect.Parameters, menuPath, "whimtex-effect", effect.SourcePath);
        }

        internal static string BuildParameterSource(string code, System.Collections.Generic.IReadOnlyList<ShaderParameter> parameters,
            string menuPath, string marker, string sourcePath = null)
        {
            if (string.IsNullOrWhiteSpace(menuPath) || menuPath.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new FormatException("Choose a non-empty effect name.");
            if (marker == "whimtex-effect") code = ShaderIconMetadata.MakePortable(code, sourcePath);
            var values = ShaderMetadata.ParseWithPreviews(code, false, out _, out var previews);
            ShaderMetadata.PreserveValues(values, parameters);
            var result = new StringBuilder("// @" + marker + " " + menuPath + "\n");
            var identity = ShaderPresetIdentity.Read(code);
            if (identity.IsDefined) result.AppendLine(identity.Directive);
            string mainControl = ShaderMetadata.ReadControl(code, out _);
            if (mainControl != null) result.AppendLine("// @control(" + mainControl + ")");
            string presetGroup = ShaderMetadata.ReadPresetGroup(code, out _);
            if (presetGroup != null) result.AppendLine("// @group(\"" + presetGroup + "\")");
            var rows = new System.Collections.Generic.List<(int order, string text, ShaderParameterControl control)>();
            foreach (var p in values)
            {
                int defaultIndex = p.controls.FindIndex(c => c.type != ShaderParameterType.Toggle && CanDeclareDefault(c, p));
                if (defaultIndex < 0) defaultIndex = p.controls.FindIndex(c => CanDeclareDefault(c, p));
                if (defaultIndex < 0)
                    throw new FormatException("No control can represent the current preset default for " + p.name + ".");
                for (int i = 0; i < p.controls.Count; i++)
                {
                    var control = p.controls[i];
                    var row = p.Copy(); row.type = control.type;
                    row.hasMinimum = control.hasMinimum; row.hasMaximum = control.hasMaximum;
                    row.softMinimum = control.softMinimum; row.softMaximum = control.softMaximum;
                    row.cyclicRange = control.cyclicRange;
                    row.minimum = control.minimum; row.maximum = control.maximum;
                    string declaration;
                    if (control.type == ShaderParameterType.Enum || control.type == ShaderParameterType.Toggle ||
                        control.type == ShaderParameterType.Seed || control.type == ShaderParameterType.Color)
                        declaration = TypedDeclaration(p, control);
                    else declaration = Declaration(row);
                    if (control.hidden || control.rgba || !string.IsNullOrWhiteSpace(control.label) || !string.IsNullOrEmpty(control.previewFunction))
                    {
                        string modifiers = control.hidden ? "hidden " : string.Empty;
                        if (control.rgba && control.type != ShaderParameterType.Color) modifiers += "rgba ";
                        if (!string.IsNullOrEmpty(control.previewFunction)) modifiers += "preview(" + ShaderPreviewFunction.Declaration(control) + ") ";
                        if (!string.IsNullOrWhiteSpace(control.label))
                            modifiers += "label(\"" + control.label.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\") ";
                        declaration = declaration.Insert("// @param ".Length, modifiers);
                    }
                    if (i != defaultIndex) declaration = Regex.Replace(declaration, @"\s*=\s*[^\[\{~]+?(?=\s*[\[\{~]|$)", "");
                    if (!string.IsNullOrEmpty(control.tooltip)) declaration += " // " + control.tooltip;
                    string metadata = string.Empty;
                    if (control.headers != null && control.headers.Length > 0)
                        metadata += "// @header(" + string.Join(")\n// @header(", control.headers) + ")\n";
                    if (control.helpBoxes != null && control.helpBoxes.Length > 0)
                        metadata += "// @helpbox(" + string.Join(")\n// @helpbox(", control.helpBoxes) + ")\n";
                    declaration = metadata + declaration;
                    rows.Add((control.order, declaration, control));
                }
            }
            foreach (var preview in previews)
            {
                string text = "// @preview(" + ShaderPreviewFunction.Declaration(preview) + ")";
                if (preview.helpBoxes != null && preview.helpBoxes.Length > 0)
                    text = "// @helpbox(" + string.Join(")\n// @helpbox(", preview.helpBoxes) + ")\n" + text;
                if (preview.headers != null && preview.headers.Length > 0)
                    text = "// @header(" + string.Join(")\n// @header(", preview.headers) + ")\n" + text;
                rows.Add((preview.order, text, preview));
            }
            rows.Sort((a, b) => a.order.CompareTo(b.order));
            var activeContainers = Array.Empty<ShaderLayoutContainer>();
            foreach (var row in rows)
            {
                var path = row.control.containers ?? Array.Empty<ShaderLayoutContainer>();
                int shared = 0;
                while (shared < activeContainers.Length && shared < path.Length && activeContainers[shared].Equals(path[shared])) shared++;
                for (int i = activeContainers.Length - 1; i >= shared; i--)
                    result.AppendLine("// @end" + activeContainers[i].kind.ToString().ToLowerInvariant());
                for (int i = shared; i < path.Length; i++)
                {
                    var container = path[i];
                    string arguments = container.title ?? string.Empty;
                    if (!string.IsNullOrEmpty(container.headerParameter)) arguments += "; " + container.headerParameter;
                    string directive = "// @" + container.kind.ToString().ToLowerInvariant();
                    result.AppendLine(string.IsNullOrEmpty(arguments) ? directive : directive + "(" + arguments + ")");
                }
                activeContainers = path;
                if (!string.IsNullOrEmpty(row.control.visibleIfParameter))
                {
                    string op = row.control.visibleIfNotEqual ? "!=" : "==";
                    result.AppendLine("// @if " + row.control.visibleIfParameter + " " + op + " " + Number(row.control.visibleIfValue));
                }
                if (row.control.enableIf != null)
                    foreach (var condition in row.control.enableIf)
                        result.AppendLine("// @enableif " + condition.parameter + " " + (condition.notEqual ? "!=" : "==") + " " + Number(condition.value));
                result.AppendLine(row.text);
                if (row.control.enableIf != null)
                    for (int i = row.control.enableIf.Length - 1; i >= 0; i--) result.AppendLine("// @endenableif");
                if (!string.IsNullOrEmpty(row.control.visibleIfParameter)) result.AppendLine("// @endif");
            }
            for (int i = activeContainers.Length - 1; i >= 0; i--)
                result.AppendLine("// @end" + activeContainers[i].kind.ToString().ToLowerInvariant());
            result.AppendLine();
            using var reader = new StringReader(code ?? "");
            bool block = false;
            string line;
            int lineNumber = 0;
            var body = new StringBuilder();
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                bool metadata = !block && ((lineNumber == 1 && Regex.IsMatch(line.TrimStart('\uFEFF'), @"^//\s*@" + Regex.Escape(marker) + @"\s+")) || Regex.IsMatch(line.TrimStart('\uFEFF'), @"^\s*//\s*@(?:preset\b|control\b|param\b|preview\b|\s*(?:header|helpbox|group|endgroup|foldout|endfoldout|tabs|endtabs|tab|endtab)\b|if\b|endif\b|enableif\b|endenableif\b)"));
                ShaderSourceBuilder.MaskComments(line, ref block);
                if (!metadata) body.AppendLine(line);
            }
            result.Append(marker == "whimtex-effect" ? ShaderSourceBuilder.ExportIncludes(body.ToString(), sourcePath) : body.ToString());
            string source = result.ToString();
            if (Encoding.UTF8.GetByteCount(source) > 2 * 1024 * 1024)
                throw new IOException("Exported HLSL exceeds 2 MiB.");
            ShaderMetadata.Parse(source, marker == "whimtex-effect", out _);
            return source;
        }

        private static string TypedDeclaration(ShaderParameter p, ShaderParameterControl control)
        {
            string[] components = new string[p.Components];
            for (int i = 0; i < components.Length; i++) components[i] = p.IsBoolean ? p.GetNumber(i) == 0 ? "false" : "true" : Number(p.GetNumber(i));
            string result = "// @param " + control.type.ToString().ToLowerInvariant() + "(" + p.ValueType + ") " + p.name + " = " +
                (components.Length == 1 ? components[0] : "(" + string.Join(", ", components) + ")");
            if (control.type == ShaderParameterType.Seed) result += " [" + (control.softMinimum ? "~" : "") + Number(control.valueMinimum) + " .. " + (control.softMaximum ? "~" : "") + Number(control.valueMaximum) + "]";
            if (control.type == ShaderParameterType.Enum)
            {
                var options = new System.Collections.Generic.List<string>();
                for (int i = 0; i < control.optionNames.Length; i++) options.Add(control.optionNames[i] + ": " + Number(control.optionValues[i]));
                result += " { " + string.Join(", ", options) + " }";
            }
            return result;
        }

        private static bool CanDeclareDefault(ShaderParameterControl control, ShaderParameter parameter)
        {
            float value = parameter.floatValue;
            if (control.type == ShaderParameterType.Vector2 && control.hasMinimum && control.hasMaximum)
                return ShaderParameter.IsValidInterval(parameter.vectorValue, control.minimum, control.maximum, control.cyclicRange);
            if (control.type == ShaderParameterType.Toggle)
            {
                for (int i = 0; i < parameter.Components; i++) if (parameter.GetNumber(i) != 0 && parameter.GetNumber(i) != 1) return false;
                return true;
            }
            if (control.type != ShaderParameterType.Float) return true;
            return !(control.hasMinimum && !control.softMinimum && value < control.minimum ||
                control.hasMaximum && !control.softMaximum && value > control.maximum);
        }

        private static string Number(double value)
        {
            if (!ProjectiveMatrix.Finite(value)) throw new FormatException("Preset defaults must be finite numbers.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Preset defaults must be finite numbers.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ColorDefault(UnityEngine.Color color) => "(" + Number(color.r) + ", " + Number(color.g) + ", " +
            Number(color.b) + ", " + Number(color.a) + ")";

        internal static string Declaration(ShaderParameter p)
        {
            string prefix = "// @param " + (p.pixelUnits ? "px " : "");
            switch (p.type)
            {
                case ShaderParameterType.Curve:
                    return prefix + "curve " + p.name + " = " + WhimTexCurveTexture.Format(p.curveValue);
                case ShaderParameterType.Gradient:
                    if (p.gradientValue != null && p.gradientValue.HasCustomCurves)
                        throw new FormatException("Endpoint-only HLSL gradient defaults cannot store active or retained Custom curves. Keep this gradient in the document or save it as a gradient preset.");
                    if (!GradientUtility.IsTwoColorGradient(p.gradientValue, out var left, out var right))
                        throw new FormatException("Gradient preset defaults currently support two endpoint colors only. Simplify the gradient before saving this preset.");
                    return prefix + "gradient " + p.name + " = " + ColorDefault(left) + " -> " + ColorDefault(right);
                case ShaderParameterType.Toggle:
                    return prefix + "toggle(float) " + p.name + " = " + (p.BoolValue ? "true" : "false");
                case ShaderParameterType.Float:
                case ShaderParameterType.Enum:
                    return prefix + "float " + p.name + " = " + Number(p.floatValue) +
                        (p.hasMinimum || p.hasMaximum ? " [" + (p.softMinimum ? "~" : "") + (p.hasMinimum ? Number(p.minimum) : "") + (p.cyclicRange ? " % " : " .. ") + (p.softMaximum ? "~" : "") + (p.hasMaximum ? Number(p.maximum) : "") + "]" : "");
                case ShaderParameterType.Color:
                    var c = p.colorValue;
                    return prefix + "color " + p.name + " = (" + Number(c.r) + ", " + Number(c.g) + ", " + Number(c.b) + ", " + Number(c.a) + ")";
                case ShaderParameterType.Vector2:
                case ShaderParameterType.Point:
                case ShaderParameterType.Vector3:
                case ShaderParameterType.Normal:
                    var n = p.type == ShaderParameterType.Normal ? (UnityEngine.Vector4)ShaderParameter.NormalizeNormal(p.vectorValue) : p.vectorValue;
                    string kind = p.type == ShaderParameterType.Normal ? "normal" : p.type == ShaderParameterType.Point ? "point" : p.type == ShaderParameterType.Vector2 ? "float2" : "float3";
                    return prefix + kind + " " + p.name + " = (" + Number(n.x) + ", " + Number(n.y) + (p.type == ShaderParameterType.Vector2 || p.type == ShaderParameterType.Point ? "" : ", " + Number(n.z)) + ")" +
                        (p.IsInterval ? " [" + Number(p.minimum) + (p.cyclicRange ? " % " : " .. ") + Number(p.maximum) + "]" : "");
                case ShaderParameterType.Vector:
                    var v = p.vectorValue;
                    return prefix + "float4 " + p.name + " = (" + Number(v.x) + ", " + Number(v.y) + ", " + Number(v.z) + ", " + Number(v.w) + ")";
                case ShaderParameterType.Transform2D:
                    var t = p.transformValue;
                    if (t.storage == TransformStorage.Projective)
                    {
                        var m = t.matrix;
                        return prefix + "transform2D " + p.name + " = matrix(" +
                            Number(m.m00) + ", " + Number(m.m01) + ", " + Number(m.m02) + ", " +
                            Number(m.m10) + ", " + Number(m.m11) + ", " + Number(m.m12) + ", " +
                            Number(m.m20) + ", " + Number(m.m21) + ", " + Number(m.m22) + ")";
                    }
                    return prefix + "transform2D " + p.name + " = (" + Number(t.position.x) + ", " + Number(t.position.y) + ", " + Number(t.size.x) + ", " + Number(t.size.y) + ", " + Number(t.rotation) + ")";
                case ShaderParameterType.Texture2D:
                    string declaration = prefix + "texture2D " + p.name;
                    if (p.textureSource == ShaderTextureSource.None) return declaration + " = none";
                    if (p.textureSource == ShaderTextureSource.Self) return declaration + " = self";
                    if (p.textureSource == ShaderTextureSource.Layer || p.textureValue == null) return declaration;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.textureValue, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                        throw new IOException("Texture " + p.name + " must be a saved project asset to be used as a preset default.");
                    return declaration + " = \"guid:" + guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + "\"";
                default: throw new FormatException("Unsupported parameter type: " + p.type);
            }
        }

        internal static void Save(string path, ShaderFX effect, bool overwrite)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            Save(path, effect.Program, effect.EmbeddedOwner, overwrite, effect.SourcePath);
        }

        internal static void Save(string path, ShaderProgram program, WhimTexDocument document, bool overwrite, string sourcePath = null)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            bool portable = ShaderPresetIO.IsProgramPath(path);
            if (program.mode == ShaderProgramMode.Graph && !portable)
                throw new InvalidOperationException("Save a Graph program as " + ShaderPresetIO.FileExtension + " to retain its editable graph.");
            path = PresetLibraryPaths.ValidateDestination(path, ShaderPresetCatalog.Folder, portable ? ShaderPresetIO.Extension : ShaderPresetIO.HlslExtension);
            string previous = File.Exists(path) ? ShaderPresetIO.ReadText(path) : null;
            if (previous != null && !overwrite) throw new IOException("A preset with this name already exists.");
            var linked = ShaderPresetLink.IsLinked(program) ? ShaderPresetCatalog.FindPreset(program.presetId) : null;
            string menuPath = linked?.version == program.presetVersion ? linked.menuPath : null;
            if (menuPath == null)
            {
                using var reader = new StringReader(program.draftCode ?? "");
                ShaderMetadata.TryHeader(reader.ReadLine(), out menuPath);
            }
            int slash = menuPath?.LastIndexOf('/') ?? -1;
            menuPath = (slash >= 0 ? menuPath.Substring(0, slash + 1) : "") + Path.GetFileNameWithoutExtension(path);
            var identity = previous == null ? default : ShaderPresetIO.ReadIdentity(path, previous);
            if (!identity.IsDefined) identity = ShaderPresetIdentity.Create();
            string source;
            if (portable)
            {
                string metadata = ShaderIconMetadata.MakePortable(program.draftCode, sourcePath);
                string group = linked?.group ?? ShaderMetadata.ReadPresetGroup(metadata, out _);
                var icon = linked?.icon ?? ShaderIconMetadata.Read(metadata);
                if (icon != null)
                {
                    string iconSource = icon.svg != null ? "/* @icon-svg\n" + icon.svg + "\n*/\n" : "// @icon(\"" + icon.reference + "\")\n";
                    icon = ShaderIconMetadata.Read(ShaderIconMetadata.MakePortable(iconSource, linked?.path ?? sourcePath));
                }
                source = ShaderPresetIO.Write(program, document, menuPath, identity, sourcePath, group, icon);
            }
            else
            {
                source = BuildParameterSource(program.draftCode, program.draftCodeParameters, menuPath, "whimtex-effect", sourcePath);
                source = ShaderPresetIdentity.Write(source, identity);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(source);
                if (File.Exists(path))
                {
                    if (!overwrite || previous == null || File.ReadAllText(path) != previous)
                        throw new IOException("The preset changed while saving. Review it before overwriting.");
                    File.Replace(temporary, path, path + ".bak");
                }
                else
                {
                    if (previous != null) throw new IOException("The preset was removed while saving. Retry with a new destination.");
                    File.Move(temporary, path);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            PresetLibraryPaths.ImportSavedFile(path);
            ShaderPresetCatalog.UserPresetsChanged();
        }

        internal static bool ShowSaveDialog(ShaderProgram program, WhimTexDocument document, string name, string sourcePath = null)
        {
            Directory.CreateDirectory(ShaderPresetCatalog.Folder);
            string path = EditorUtility.SaveFilePanel("Save Shader Preset", ShaderPresetCatalog.Folder, name, ShaderPresetIO.ExtensionFor(program));
            if (string.IsNullOrEmpty(path)) return false;
            bool overwrite = File.Exists(path);
            if (overwrite && !EditorUtility.DisplayDialog("Overwrite Shader Preset", "Replace this preset? A .bak copy will be kept.", "Overwrite", "Cancel")) return false;
            Save(path, program, document, overwrite, sourcePath);
            return true;
        }
    }
}
