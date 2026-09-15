using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXPresetWriter
    {
        internal static string BuildSource(ShaderFX effect, string menuPath)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (string.IsNullOrWhiteSpace(menuPath) || menuPath.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new FormatException("Choose a non-empty effect name.");
            var values = ShaderFXMetadata.Parse(effect.Code, false, out _);
            if (effect.UsesCodeParameters)
            {
                ShaderFXMetadata.PreserveValues(values, effect.Parameters);
                foreach (var old in effect.Parameters)
                    if (old != null && !old.declaredInCode && !values.Exists(p => p.name == old.name && ShaderFXMetadata.Compatible(p.type, old.type)))
                        throw new FormatException("Code declarations must include the existing parameter: " + old.name);
            }
            else foreach (var parameter in effect.Parameters) if (parameter != null) values.Add(parameter.Copy());
            var result = new StringBuilder("// @whimtex-effect " + menuPath + "\n");
            var rows = new System.Collections.Generic.List<(int order, string text)>();
            foreach (var p in values)
            {
                if (p.controls.Count == 0) { rows.Add((0, Declaration(p))); continue; }
                int defaultIndex = p.controls.FindIndex(c => c.type != ShaderFXParameterType.Bool);
                if (defaultIndex < 0) defaultIndex = 0;
                for (int i = 0; i < p.controls.Count; i++)
                {
                    var control = p.controls[i];
                    var row = p.Copy(); row.type = control.type;
                    row.hasMinimum = control.hasMinimum; row.hasMaximum = control.hasMaximum;
                    row.minimum = control.minimum; row.maximum = control.maximum;
                    string declaration;
                    if (control.type == ShaderFXParameterType.Enum)
                    {
                        var options = new System.Collections.Generic.List<string>();
                        for (int n = 0; n < control.optionNames.Length; n++) options.Add(control.optionNames[n] + ": " + Number(control.optionValues[n]));
                        declaration = "// @param enum " + p.name + " = " + Number(p.floatValue) + " { " + string.Join(", ", options) + " }";
                    }
                    else declaration = Declaration(row);
                    if (i != defaultIndex) declaration = Regex.Replace(declaration, @"\s*=\s*[^\[\{]+?(?=\s*[\[\{]|$)", "");
                    rows.Add((control.order, declaration));
                }
            }
            rows.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var row in rows) result.AppendLine(row.text);
            result.AppendLine();
            using var reader = new StringReader(effect.Code ?? "");
            bool block = false;
            string line;
            int lineNumber = 0;
            var body = new StringBuilder();
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                bool metadata = !block && ((lineNumber == 1 && ShaderFXMetadata.TryHeader(line, out _)) || Regex.IsMatch(line, @"^\s*//\s*@param\b"));
                ShaderFXSourceBuilder.MaskComments(line, ref block);
                if (!metadata) body.AppendLine(line);
            }
            result.Append(ShaderFXSourceBuilder.ExportIncludes(body.ToString(), effect.SourcePath));
            string source = result.ToString();
            if (Encoding.UTF8.GetByteCount(source) > 2 * 1024 * 1024)
                throw new IOException("Exported HLSL exceeds 2 MiB.");
            ShaderFXMetadata.Parse(source, true, out _);
            return source;
        }

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("Preset defaults must be finite numbers.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string Declaration(ShaderFXParameter p)
        {
            string prefix = "// @param ";
            switch (p.type)
            {
                case ShaderFXParameterType.Bool:
                    return prefix + "bool " + p.name + " = " + (p.BoolValue ? "true" : "false");
                case ShaderFXParameterType.Float:
                case ShaderFXParameterType.Enum:
                    return prefix + "float " + p.name + " = " + Number(p.floatValue) +
                        (p.hasMinimum || p.hasMaximum ? " [" + (p.hasMinimum ? Number(p.minimum) : "") + " .. " + (p.hasMaximum ? Number(p.maximum) : "") + "]" : "");
                case ShaderFXParameterType.Color:
                    var c = p.colorValue;
                    return prefix + "color " + p.name + " = (" + Number(c.r) + ", " + Number(c.g) + ", " + Number(c.b) + ", " + Number(c.a) + ")";
                case ShaderFXParameterType.Vector:
                    var v = p.vectorValue;
                    return prefix + "float4 " + p.name + " = (" + Number(v.x) + ", " + Number(v.y) + ", " + Number(v.z) + ", " + Number(v.w) + ")";
                case ShaderFXParameterType.Transform2D:
                    var t = p.transformValue;
                    return prefix + "transform2D " + p.name + " = (" + Number(t.position.x) + ", " + Number(t.position.y) + ", " + Number(t.size.x) + ", " + Number(t.size.y) + ", " + Number(t.rotation) + ")";
                case ShaderFXParameterType.Texture2D:
                    string declaration = prefix + "texture2D " + p.name;
                    if (p.textureValue == null) return declaration;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.textureValue, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                        throw new IOException("Texture " + p.name + " must be a saved project asset to be used as a preset default.");
                    return declaration + " = \"guid:" + guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + "\"";
                default: throw new FormatException("Unsupported parameter type: " + p.type);
            }
        }

        internal static void Save(string path, ShaderFX effect, bool overwrite)
        {
            path = PresetLibraryPaths.ValidateDestination(path, ShaderFXCatalog.Folder, "hlsl");
            ShaderFXMetadata.Parse(effect.Code, false, out string menuPath);
            int slash = menuPath?.LastIndexOf('/') ?? -1;
            menuPath = (slash >= 0 ? menuPath.Substring(0, slash + 1) : "") + Path.GetFileNameWithoutExtension(path);
            string source = BuildSource(effect, menuPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(source);
                if (File.Exists(path))
                {
                    if (!overwrite) throw new IOException("A preset with this name already exists.");
                    File.Replace(temporary, path, path + ".bak");
                }
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            PresetLibraryPaths.ImportSavedFile(path);
        }
    }
}
