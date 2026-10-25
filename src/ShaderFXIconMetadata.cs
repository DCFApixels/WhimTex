using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXIconMetadata
    {
        internal const int MaxHeader = 512 * 1024, MaxSvg = 64 * 1024, MaxPng = 256 * 1024;
        private const string PngPrefix = "data:image/png;base64,";
        private static readonly Regex Comments = new Regex(@"//[^\n]*|/\*[\s\S]*?\*/");
        private static readonly Regex Reference = new Regex("^//\\s*@icon\\(\"([^\"\\r\\n]+)\"\\)\\s*$");

        [Serializable]
        internal sealed class Descriptor
        {
            public string reference, svg, error;
            internal int start, length;
        }

        internal static string ReadHeader(TextReader reader)
        {
            var text = new StringBuilder();
            bool block = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string rest = ShaderFXSourceBuilder.MaskComments(line.TrimStart('\uFEFF'), ref block);
                if (!string.IsNullOrWhiteSpace(rest)) break;
                if (text.Length + line.Length + 1 > MaxHeader) break;
                text.AppendLine(line);
            }
            return text.ToString().Replace("\r\n", "\n");
        }

        internal static Descriptor Read(string source)
        {
            using var reader = new StringReader(source ?? "");
            string header = ReadHeader(reader);
            Descriptor result = null;
            foreach (Match comment in Comments.Matches(header))
            {
                string text = comment.Value;
                bool svg = Regex.IsMatch(text, @"^/\*\s*@icon-svg\b");
                bool reference = Regex.IsMatch(text, @"^//\s*@icon\b");
                if (!svg && !reference) continue;
                if (result != null) { result.error = "Declare only one FX icon."; return result; }
                result = new Descriptor { start = comment.Index, length = comment.Length };
                if (svg)
                {
                    result.svg = Regex.Replace(text.Substring(0, text.Length - 2), @"^/\*\s*@icon-svg\s*", "").Trim();
                    if (result.svg.Length > MaxSvg) result.error = "SVG icon exceeds 64 KiB.";
                }
                else
                {
                    var match = Reference.Match(text.TrimEnd('\r'));
                    if (match.Success) result.reference = match.Groups[1].Value;
                    else result.error = "Expected // @icon(\"relative/path.svg\") or // @icon(\"guid:...\").";
                }
            }
            return result;
        }

        internal static byte[] ReadImage(Descriptor icon, string sourcePath, out bool svg)
        {
            if (!string.IsNullOrEmpty(icon.error)) throw new FormatException(icon.error);
            if (icon.svg != null)
            {
                svg = true;
                ValidateSvg(icon.svg);
                return Encoding.UTF8.GetBytes(icon.svg);
            }
            string reference = icon.reference;
            if (reference.StartsWith(PngPrefix, StringComparison.Ordinal))
            {
                svg = false;
                byte[] png = Convert.FromBase64String(reference.Substring(PngPrefix.Length));
                ValidatePng(png);
                return png;
            }
            string path;
            if (reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            {
                string guid = reference.Substring(5);
                if (!Guid.TryParseExact(guid, "N", out _)) throw new FormatException("FX icon GUID must contain 32 hexadecimal characters.");
                path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) throw new IOException("FX icon GUID was not found in this project.");
                path = PresetLibraryPaths.PhysicalPath(path);
            }
            else
            {
                if (Path.IsPathRooted(reference) || reference.Contains(":")) throw new FormatException("Use a relative icon path or guid: reference.");
                if (string.IsNullOrEmpty(sourcePath)) throw new IOException("A relative FX icon needs an HLSL source path.");
                path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(PresetLibraryPaths.PhysicalPath(sourcePath)), reference.Replace('\\', '/')));
            }
            string extension = Path.GetExtension(path);
            svg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
            if (!svg && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new FormatException("FX icons support SVG and PNG files.");
            using var stream = File.OpenRead(path);
            if (stream.Length > (svg ? MaxSvg : MaxPng)) throw new IOException("FX icon exceeds its size limit (SVG 64 KiB; PNG 256 KiB).");
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0, read;
            while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
            if (offset != bytes.Length) throw new IOException("FX icon changed while it was being read.");
            if (svg) ValidateSvg(Encoding.UTF8.GetString(bytes)); else ValidatePng(bytes);
            return bytes;
        }

        private static void ValidatePng(byte[] bytes)
        {
            if (bytes.Length > MaxPng || bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                throw new FormatException("Invalid PNG icon or PNG larger than 256 KiB.");
            uint Dimension(int p) => ((uint)bytes[p] << 24) | ((uint)bytes[p + 1] << 16) | ((uint)bytes[p + 2] << 8) | bytes[p + 3];
            if (Dimension(16) == 0 || Dimension(20) == 0 || Dimension(16) > 1024 || Dimension(20) > 1024)
                throw new FormatException("PNG icons must be at most 1024 × 1024 pixels.");
        }

        private static void ValidateSvg(string svg)
        {
            if (svg.Length > MaxSvg || svg.Contains("*/")) throw new FormatException("SVG icon exceeds 64 KiB or contains a comment terminator.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxSvg };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            bool root = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.ProcessingInstruction) throw new FormatException("SVG icon processing instructions are unsupported.");
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!root) { if (reader.LocalName != "svg") throw new FormatException("An SVG icon must have an svg root."); root = true; }
                if (reader.LocalName == "image" || reader.LocalName == "script" || reader.LocalName == "foreignObject")
                    throw new FormatException("SVG icons support vector content only.");
                while (reader.MoveToNextAttribute())
                    if (reader.LocalName == "href" && !reader.Value.StartsWith("#", StringComparison.Ordinal))
                        throw new FormatException("External references inside SVG icons are unsupported.");
                reader.MoveToElement();
            }
            if (!root) throw new FormatException("SVG icon is empty.");
            foreach (Match url in Regex.Matches(svg, @"url\s*\(([^)]*)\)", RegexOptions.IgnoreCase))
                if (!url.Groups[1].Value.Trim().Trim('\'', '"').StartsWith("#", StringComparison.Ordinal))
                    throw new FormatException("External references inside SVG icons are unsupported.");
        }

        internal static string MakePortable(string source, string sourcePath)
        {
            string normalized = (source ?? "").Replace("\r\n", "\n");
            var icon = Read(normalized);
            if (icon == null || icon.svg != null || icon.reference?.StartsWith(PngPrefix, StringComparison.Ordinal) == true) return source;
            try
            {
                byte[] data = ReadImage(icon, sourcePath, out bool svg);
                string declaration = svg ? "/* @icon-svg\n" + Encoding.UTF8.GetString(data).TrimStart('\uFEFF').Trim() + "\n*/"
                    : "// @icon(\"" + PngPrefix + Convert.ToBase64String(data) + "\")";
                return normalized.Substring(0, icon.start) + declaration + normalized.Substring(icon.start + icon.length);
            }
            catch (Exception) { return source; }
        }
    }
}
