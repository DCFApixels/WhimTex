using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DCFApixels.WhimTex
{
    internal static class ShaderPresetIO
    {
        internal const string Extension = "whimtexshader";
        internal const string FileExtension = "." + Extension;
        internal const string HlslExtension = "hlsl";
        internal const string Format = "whimtex.shader-preset";
        internal const int FormatVersion = 1;
        internal const int MaximumFileBytes = 64 * 1024 * 1024;
        private const string SourceReference = "$shaderSource";

        internal sealed class Loaded
        {
            internal ShaderProgram program;
            internal string path, menuPath, hash;
        }

        internal static bool IsProgramPath(string path) => path?.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) == true;
        internal static bool IsPresetPath(string path) => IsProgramPath(path) || path?.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) == true;
        internal static string ExtensionFor(ShaderProgram program) => program.mode == ShaderProgramMode.Graph ? Extension : HlslExtension;

        internal static string ReadText(string physicalPath)
        {
            long limit = IsProgramPath(physicalPath) ? MaximumFileBytes : 2 * 1024 * 1024;
            using var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > limit) throw new IOException("Shader preset exceeds its file size limit.");
            using var reader = new StreamReader(stream, ShaderGraphSources.Utf8, true);
            return reader.ReadToEnd();
        }

        private static JObject Envelope(string text)
        {
            var root = WhimTexDocumentJson.Parse(text);
            Keys(root, "format", "version", "menuPath", "group", "icon", "program", "shaderSources");
            if ((string)root["format"] != Format || root["version"]?.Type != JTokenType.Integer || (long)root["version"] != FormatVersion)
                throw new FormatException("Unsupported shader preset format or version.");
            ValidateMenuPath((string)root["menuPath"]);
            if (!(root["program"] is JObject program) || !(root["shaderSources"] is JObject))
                throw new FormatException("A shader preset requires a complete program and shaderSources table.");
            Keys(program, "mode", "draftGraph", "workingGraph", "draftCode", "workingCode", "draftCodeParameters", "workingCodeParameters", "presetId", "presetVersion");
            if (!(program["draftGraph"] is JObject graph) || !(program["draftCodeParameters"] is JArray) ||
                program.Property("draftCode") == null || program.Property("workingCode") == null)
                throw new FormatException("A shader preset must retain the complete Graph and Code data, including the inactive mode.");
            foreach (string field in new[] { "parameters", "nodes", "edges", "functions", "sources", "notes", "groups", "subGraphs" })
                if (!(graph[field] is JArray)) throw new FormatException("Shader preset graph is missing " + field + ".");
            if (program["mode"]?.Type != JTokenType.String ||
                !Enum.TryParse((string)program["mode"], out ShaderProgramMode mode) || !Enum.IsDefined(typeof(ShaderProgramMode), mode))
                throw new FormatException("Unknown shader preset program mode.");
            ReadProgramIdentity(program);
            if (root["group"] != null && root["group"].Type != JTokenType.Null && root["group"].Type != JTokenType.String)
                throw new FormatException("Shader preset group must be text.");
            return root;
        }

        internal static void ValidateMenuPath(string menuPath)
        {
            if (string.IsNullOrWhiteSpace(menuPath) || menuPath.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new FormatException("Choose a non-empty shader preset name.");
            foreach (string part in menuPath.Split('/'))
                if (string.IsNullOrWhiteSpace(part)) throw new FormatException("Shader category/name must not contain empty segments.");
        }

        private static void Keys(JObject node, params string[] allowed)
        {
            foreach (var property in node.Properties())
                if (Array.IndexOf(allowed, property.Name) < 0) throw new FormatException("Unknown shader preset field: " + property.Path);
        }

        internal static ShaderPresetIdentity ValidateIdentity(string id, int version, bool required = false)
        {
            if (string.IsNullOrEmpty(id) && version == 0 && !required) return default;
            if (!(Guid.TryParseExact(id, "N", out var guid) || Guid.TryParseExact(id, "D", out guid)) || guid == Guid.Empty || version < 1)
                throw new FormatException("A shader preset reference requires presetId (GUID) and presetVersion (positive integer).");
            return new ShaderPresetIdentity(guid.ToString("N"), version);
        }

        private static ShaderPresetIdentity ReadProgramIdentity(JObject program)
        {
            if (program["presetId"]?.Type != JTokenType.String || program["presetVersion"]?.Type != JTokenType.Integer)
                throw new FormatException("A portable shader preset requires presetId and presetVersion.");
            return ValidateIdentity((string)program["presetId"], (int)program["presetVersion"], true);
        }

        internal static ShaderPresetIdentity ReadIdentity(string path, string text)
            => IsProgramPath(path) ? ReadProgramIdentity((JObject)Envelope(text)["program"]) : ShaderPresetIdentity.Read(text);

        internal static string WriteIdentity(string path, string text, ShaderPresetIdentity identity)
        {
            if (!IsProgramPath(path)) return ShaderPresetIdentity.Write(text, identity);
            var root = Envelope(text);
            root["program"]["presetId"] = identity.id;
            root["program"]["presetVersion"] = identity.version;
            return root.ToString(Formatting.Indented);
        }

        // File-only: the catalog calls this from its background discovery worker.
        internal static ShaderPresetCatalog.Entry ReadHeader(string path, string physical, bool user)
        {
            var root = Envelope(ReadText(physical));
            var identity = ReadProgramIdentity((JObject)root["program"]);
            var entry = new ShaderPresetCatalog.Entry
            {
                id = identity.id, version = identity.version, path = path, user = user,
                menuPath = (string)root["menuPath"], group = (string)root["group"],
                mode = (ShaderProgramMode)Enum.Parse(typeof(ShaderProgramMode), (string)root["program"]["mode"])
            };
            if (root["icon"] is JObject icon)
            {
                Keys(icon, "reference", "svg");
                entry.icon = new ShaderIconMetadata.Descriptor { reference = (string)icon["reference"], svg = (string)icon["svg"] };
                if (entry.icon.reference != null && entry.icon.svg != null) throw new FormatException("Declare only one shader preset icon.");
                if (entry.icon.svg?.Length > ShaderIconMetadata.MaxSvg || entry.icon.reference?.Length > ShaderIconMetadata.MaxHeader)
                    throw new FormatException("Shader preset icon exceeds its size limit.");
            }
            else if (root["icon"] != null && root["icon"].Type != JTokenType.Null)
                throw new FormatException("Invalid shader preset icon.");
            return entry;
        }

        internal static Loaded Load(ShaderPresetCatalog.Entry entry)
        {
            if (entry == null || entry.HasError || !IsPresetPath(entry.path))
                throw new InvalidOperationException("Choose a valid .hlsl or " + FileExtension + " shader preset.");
            if (!string.IsNullOrEmpty(entry.id) && ShaderPresetCatalog.FindPreset(entry.id)?.path != entry.path)
                throw new IOException("The preset ID is ambiguous or its owner changed. Refresh the preset catalog.");
            string source = ShaderPresetCatalog.ReadSource(entry.path);
            var identity = ReadIdentity(entry.path, source);
            if (identity.id != entry.id || identity.version != entry.version)
                throw new IOException("The preset identity changed. Refresh the preset catalog.");
            ShaderProgram program;
            string menuPath, hash;
            if (IsProgramPath(entry.path))
            {
                var root = Envelope(source);
                var data = (JObject)root["program"];
                UnpackSources(data, (JObject)root["shaderSources"]);
                program = WhimTexDocumentJson.ReadShaderProgram(data);
                ValidatePortable(program);
                menuPath = (string)root["menuPath"];
                hash = ShaderGraphSources.Digest(ShaderGraphSources.Utf8.GetBytes(source));
            }
            else
            {
                string embedded = ShaderSourceBuilder.ExportIncludes(ShaderIconMetadata.MakePortable(source, entry.path), entry.path);
                // Controls retain source line numbers. Parse the same portable text that hosts
                // compile and documents store, so embedding an icon/include is not a revision.
                var parameters = ShaderMetadata.Parse(embedded, true, out menuPath);
                program = new ShaderProgram
                {
                    mode = ShaderProgramMode.Code, draftCode = embedded, workingCode = embedded, draftCodeParameters = parameters,
                    presetId = identity.id, presetVersion = identity.version
                };
                var dependencies = new List<string>(ShaderSourceBuilder.GetDependencies(source, entry.path));
                dependencies.Sort(StringComparer.Ordinal);
                var content = new StringBuilder(source);
                foreach (string dependency in dependencies)
                {
                    if (dependency == entry.path) continue;
                    content.Append('\n').Append(dependency).Append(':');
                    string physical = PresetLibraryPaths.PhysicalPath(dependency);
                    content.Append(File.Exists(physical) ? ShaderGraphSources.Digest(ShaderGraphSources.Utf8.GetBytes(ReadText(physical))) : "missing");
                }
                hash = ShaderGraphSources.Digest(ShaderGraphSources.Utf8.GetBytes(content.ToString()));
            }
            program.presetId = identity.id;
            program.presetVersion = identity.version;
            return new Loaded { program = program, path = entry.path, menuPath = menuPath, hash = hash };
        }

        internal static string Write(ShaderProgram program, WhimTexDocument document, string menuPath,
            ShaderPresetIdentity identity, string sourcePath = null, string group = null, ShaderIconMetadata.Descriptor icon = null)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            ValidateMenuPath(menuPath);
            ValidateIdentity(identity.id, identity.version, true);
            ValidatePortable(program);
            var data = WhimTexDocumentJson.ShaderProgramSnapshot(program, document, sourcePath);
            data["presetId"] = identity.id;
            data["presetVersion"] = identity.version;
            // The shared snapshot keeps broken Code drafts editable and embeds workingCode strictly.
            var sources = new JObject();
            PackSources(data, sources);
            var root = new JObject
            {
                ["format"] = Format, ["version"] = FormatVersion, ["menuPath"] = menuPath,
                ["group"] = group, ["program"] = data, ["shaderSources"] = sources
            };
            if (icon != null)
            {
                if (!string.IsNullOrEmpty(icon.error)) throw new FormatException(icon.error);
                byte[] image = ShaderIconMetadata.ReadImage(icon, sourcePath, out bool svg);
                root["icon"] = svg
                    ? new JObject { ["svg"] = ShaderGraphSources.Utf8.GetString(image) }
                    : new JObject { ["reference"] = "data:image/png;base64," + Convert.ToBase64String(image) };
            }
            string json = root.ToString(Formatting.Indented);
            if (ShaderGraphSources.Utf8.GetByteCount(json) > MaximumFileBytes) throw new IOException("Shader preset exceeds 64 MiB.");
            return json;
        }

        internal static void ValidatePortable(ShaderProgram program)
        {
            ValidatePortableParameters(program.draftGraph?.parameters);
            ValidatePortableParameters(program.draftCodeParameters);
            ValidatePortableParameters(program.workingGraph?.parameters);
            ValidatePortableParameters(program.workingCodeParameters);
        }

        private static void ValidatePortableParameters(IReadOnlyList<ShaderParameter> parameters)
        {
            if (parameters == null) return;
            foreach (var parameter in parameters)
            {
                if (parameter?.type != ShaderParameterType.Texture2D) continue;
                if (!string.IsNullOrEmpty(parameter.textureLayerId))
                    throw new FormatException("Texture " + parameter.name + " refers to a document layer. Clear its layer reference before saving a portable preset.");
                // The shared JSON writer validates project asset identities and preserves missing assets.
                if (parameter.textureValue != null && !UnityEditor.AssetDatabase.Contains(parameter.textureValue))
                    throw new FormatException("Texture " + parameter.name + " must be a saved project asset in a portable preset.");
            }
        }

        private static IEnumerable<JProperty> SourceFields(JObject program)
        {
            foreach (string name in new[] { "draftCode", "workingCode" })
                if (program.Property(name) is JProperty code) yield return code;
            foreach (string graphField in new[] { "draftGraph", "workingGraph" })
            {
                if (!(program[graphField] is JObject graph)) continue;
                if (graph["sources"] is JArray sources)
                    foreach (var source in sources)
                        if (source is JObject item && item.Property("code") is JProperty code) yield return code;
                if (graph["functions"] is JArray functions)
                    foreach (var function in functions)
                        if (function is JObject item)
                            foreach (string name in new[] { "body", "draft" })
                                if (item.Property(name) is JProperty code) yield return code;
            }
        }

        private static void PackSources(JObject program, JObject table)
        {
            int total = 0;
            foreach (var field in SourceFields(program))
            {
                if (field.Value.Type == JTokenType.Null) continue;
                string code = (string)field.Value;
                ShaderGraphSources.CodeByteCount(code);
                byte[] bytes = ShaderGraphSources.Utf8.GetBytes(code);
                string id = ShaderGraphSources.Digest(bytes);
                if (table[id] == null)
                {
                    if (table.Count >= ShaderGraphSources.MaximumSources || bytes.Length > ShaderGraphSources.MaximumArchiveBytes - total)
                        throw new FormatException("Shader preset source table exceeds its storage budget.");
                    total += bytes.Length;
                    using var buffer = new MemoryStream();
                    using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, true)) gzip.Write(bytes, 0, bytes.Length);
                    table[id] = new JObject { ["encoding"] = "gzip-base64", ["length"] = bytes.Length, ["data"] = Convert.ToBase64String(buffer.ToArray()) };
                }
                field.Value = new JObject { [SourceReference] = id };
            }
        }

        private static void UnpackSources(JObject program, JObject table)
        {
            if (table.Count > ShaderGraphSources.MaximumSources) throw new FormatException("Too many shader preset source entries.");
            var decoded = new Dictionary<string, string>(StringComparer.Ordinal);
            int total = 0;
            foreach (var field in SourceFields(program))
            {
                if (field.Value.Type == JTokenType.Null) continue;
                if (!(field.Value is JObject reference)) throw new FormatException("Shader preset code requires a compressed source reference.");
                Keys(reference, SourceReference);
                string id = (string)reference[SourceReference];
                if (id == null || id.Length != 64) throw new FormatException("Invalid shader source digest.");
                if (!decoded.TryGetValue(id, out string code))
                {
                    if (!(table[id] is JObject entry)) throw new FormatException("Missing shader source: " + id);
                    Keys(entry, "encoding", "length", "data");
                    if ((string)entry["encoding"] != "gzip-base64" || entry["length"]?.Type != JTokenType.Integer || entry["data"]?.Type != JTokenType.String)
                        throw new FormatException("Unsupported compressed shader source.");
                    long size = (long)entry["length"];
                    if (size < 0 || size > ShaderGraphSources.MaximumSourceBytes || size > ShaderGraphSources.MaximumArchiveBytes - total)
                        throw new FormatException("Shader preset source exceeds its storage budget.");
                    byte[] compressed = Convert.FromBase64String((string)entry["data"]);
                    using var buffer = new MemoryStream(compressed, false);
                    using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
                    var bytes = new byte[(int)size];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = gzip.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) throw new FormatException("Truncated compressed shader source.");
                        offset += read;
                    }
                    if (gzip.ReadByte() != -1 || ShaderGraphSources.Digest(bytes) != id)
                        throw new FormatException("Shader source length or digest does not match.");
                    total += bytes.Length;
                    code = ShaderGraphSources.Utf8.GetString(bytes);
                    decoded.Add(id, code);
                }
                field.Value = code;
            }
            if (decoded.Count != table.Count) throw new FormatException("Shader preset contains unreferenced source entries.");
        }
    }
}
