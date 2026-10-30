using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal readonly struct ShaderFXPresetIdentity
    {
        private static readonly Regex Prefix = new Regex(@"^\s*//\s*@preset\b");
        private static readonly Regex Declaration = new Regex("^\\s*//\\s*@preset\\s*\\(\\s*\"([^\"]+)\"\\s*,\\s*([0-9]+)\\s*\\)\\s*$");
        internal readonly string id;
        internal readonly int version;
        internal bool IsDefined => !string.IsNullOrEmpty(id);

        internal ShaderFXPresetIdentity(string id, int version) { this.id = id; this.version = version; }
        internal string Directive => "// @preset(\"" + id + "\", " + version.ToString(CultureInfo.InvariantCulture) + ")";
        internal static ShaderFXPresetIdentity Create() => new ShaderFXPresetIdentity(Guid.NewGuid().ToString("N"), 1);
        internal static bool IsDirective(string line) => Prefix.IsMatch(line.TrimStart('\uFEFF'));

        internal static ShaderFXPresetIdentity Read(string source)
        {
            var result = default(ShaderFXPresetIdentity);
            bool block = false, header = true;
            int number = 0;
            using var reader = new StringReader(source ?? "");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                line = line.TrimStart('\uFEFF');
                bool directive = !block && IsDirective(line);
                if (!string.IsNullOrWhiteSpace(ShaderFXSourceBuilder.MaskComments(line, ref block))) header = false;
                if (!directive) continue;
                if (!header) throw new FormatException($"Line {number}: @preset must precede HLSL code.");
                if (result.IsDefined) throw new FormatException($"Line {number}: declare only one @preset.");
                var match = Declaration.Match(line);
                if (!match.Success ||
                    !(Guid.TryParseExact(match.Groups[1].Value, "N", out var guid) || Guid.TryParseExact(match.Groups[1].Value, "D", out guid)) ||
                    guid == Guid.Empty || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int version) || version < 1)
                    throw new FormatException($"Line {number}: expected // @preset(\"GUID\", version), with a non-empty GUID and positive integer version.");
                result = new ShaderFXPresetIdentity(guid.ToString("N"), version);
            }
            return result;
        }

        // Preserve the rest of the file, including its line endings and final newline.
        internal static string Write(string source, ShaderFXPresetIdentity identity)
        {
            Read(source);
            bool block = false;
            for (int start = 0; start < source.Length;)
            {
                int end = source.IndexOf('\n', start);
                if (end < 0) end = source.Length;
                int contentEnd = end > start && source[end - 1] == '\r' ? end - 1 : end;
                string line = source.Substring(start, contentEnd - start);
                if (!block && IsDirective(line))
                    return source.Substring(0, start) + identity.Directive + source.Substring(contentEnd);
                ShaderFXSourceBuilder.MaskComments(line, ref block);
                start = end + 1;
            }
            string newline = source.Contains("\r\n") ? "\r\n" : "\n";
            int firstEnd = source.IndexOf('\n');
            if (firstEnd < 0) return source + newline + identity.Directive + newline;
            return source.Insert(firstEnd + 1, identity.Directive + newline);
        }

        internal static void ReplaceFile(string path, string expected, string replacement)
        {
            if (!string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal))
                throw new IOException("The preset changed during identity resolution; retry after saving the file.");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("The preset is read-only.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, replacement, new UTF8Encoding(false));
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
