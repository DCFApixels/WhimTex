using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class ShaderGraphInclude
    {
        public string requested;
        public string sourceId;
    }

    [Serializable]
    public sealed class ShaderGraphSource
    {
        public string id;
        public string code;
        public List<ShaderGraphInclude> includes = new List<ShaderGraphInclude>();
        [NonSerialized] private RenderFingerprint.TextStamp codeStamp;
        internal ulong CodeFingerprint => codeStamp.Get(code);
    }

    // Archive operations never rewrite the stored code or consult files during expansion.
    internal static class ShaderGraphSources
    {
        internal const int MaximumSourceBytes = 8 * 1024 * 1024;
        internal const int MaximumArchiveBytes = 32 * 1024 * 1024;
        internal const int MaximumSources = 4096;
        internal const int MaximumIncludes = 32768;
        internal const int MaximumDepth = 64;
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        // Exact compiler-provided names only. Package includes are captured like any file.
        static readonly HashSet<string> EngineIncludes = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityCG.cginc", "HLSLSupport.cginc", "UnityShaderVariables.cginc", "UnityInstancing.cginc",
            "Lighting.cginc", "AutoLight.cginc", "UnityPBSLighting.cginc", "UnityLightingCommon.cginc",
            "UnityStandardUtils.cginc", "UnityStandardBRDF.cginc", "UnityMetaPass.cginc", "UnityGBuffer.cginc",
            "ShaderApiReflectionSupport.hlsl"
        };
        static readonly Regex DirectiveHead = new Regex(@"^\s*#\s*([A-Za-z_][A-Za-z_0-9]*)\b", RegexOptions.CultureInvariant);
        static readonly Regex LiteralInclude = new Regex("^\\s*#\\s*(?:include|include_with_pragmas)\\s*(?:\"([^\"\\r\\n]+)\"|<([^>\\r\\n]+)>)\\s*$", RegexOptions.CultureInvariant);
        static readonly Regex Once = new Regex(@"^\s*#\s*pragma\s+once\s*$", RegexOptions.CultureInvariant);

        sealed class Directive
        {
            internal int start, end;
            internal string requested; // Null denotes a top-level #pragma once.
        }
        sealed class Module
        {
            internal ShaderGraphSource source;
            internal readonly List<Directive> directives = new List<Directive>();
            internal readonly Dictionary<string, string> bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            internal bool once;
            internal int depth;
        }

        internal static bool IsEngineInclude(string requested) => EngineIncludes.Contains(requested ?? "");

        internal static int CodeByteCount(string code)
        {
            if (code == null) throw new FormatException("Shader source code is missing.");
            if (code.Length > MaximumSourceBytes) throw new FormatException("A shader source exceeds 8 MiB of UTF-8 text.");
            int length = Utf8.GetByteCount(code);
            if (length > MaximumSourceBytes) throw new FormatException("A shader source exceeds 8 MiB of UTF-8 text.");
            return length;
        }

        internal static string Digest(byte[] bytes)
        {
            using var hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(bytes);
            var result = new StringBuilder(64);
            foreach (byte b in digest) result.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return result.ToString();
        }

        static void CheckId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) throw new FormatException("Shader source IDs must contain 1..128 identifier characters.");
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-'))
                    throw new FormatException("Invalid shader source ID: " + id);
        }

        // Match directives after line splicing and comment masking, but retain spans into
        // the original text. Expansion replaces only the directive, preserving comments.
        static Module ReadModule(ShaderGraphSource source)
        {
            if (source == null) throw new FormatException("A shader source entry is null.");
            CheckId(source.id);
            CodeByteCount(source.code);
            if (source.includes == null || source.includes.Count > MaximumIncludes)
                throw new FormatException("Invalid shader include bindings: " + source.id);
            var module = new Module { source = source };
            foreach (var binding in source.includes)
            {
                if (binding == null || string.IsNullOrEmpty(binding.requested) || binding.requested.Length > 4096 ||
                    binding.requested.IndexOfAny(new[] { '\0', '\r', '\n', '"', '<', '>' }) >= 0)
                    throw new FormatException("Invalid shader include name: " + source.id);
                CheckId(binding.sourceId);
                if (!module.bindings.TryAdd(binding.requested, binding.sourceId))
                    throw new FormatException("Duplicate shader include binding: " + binding.requested);
            }
            bool block = false;
            int position = 0, conditionalDepth = 0;
            var logical = new StringBuilder();
            while (position < source.code.Length)
            {
                int start = position;
                logical.Clear();
                while (position < source.code.Length)
                {
                    int splice = SpliceLength(source.code, position);
                    if (splice > 0) { position += splice; continue; }
                    char c = source.code[position++];
                    if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && position < source.code.Length && source.code[position] == '\n') position++;
                        break;
                    }
                    logical.Append(c);
                }
                string clean = ShaderSourceBuilder.MaskComments(logical.ToString(), ref block);
                var head = DirectiveHead.Match(clean);
                if (!head.Success) continue;
                string command = head.Groups[1].Value;
                if (command == "if" || command == "ifdef" || command == "ifndef") conditionalDepth++;
                else if (command == "endif") conditionalDepth = Math.Max(0, conditionalDepth - 1);
                if (command == "include" || command == "include_with_pragmas")
                {
                    var match = LiteralInclude.Match(clean);
                    if (!match.Success) throw new FormatException("Shader backups require literal quoted or angle-bracket includes: " + source.id);
                    var name = match.Groups[1].Success ? match.Groups[1] : match.Groups[2];
                    if (name.Length > 4096) throw new FormatException("Shader include name exceeds 4096 characters.");
                    module.directives.Add(new Directive { start = OriginalOffset(source.code, start, clean.IndexOf('#')),
                        end = OriginalOffset(source.code, start, name.Index + name.Length + 1), requested = name.Value });
                }
                else if (command == "pragma" && Once.IsMatch(clean))
                {
                    if (conditionalDepth != 0) throw new FormatException("Conditional #pragma once cannot be archived; use an explicit include guard: " + source.id);
                    module.once = true;
                    module.directives.Add(new Directive { start = OriginalOffset(source.code, start, clean.IndexOf('#')),
                        end = OriginalOffset(source.code, start, clean.TrimEnd().Length) });
                }
                else if (command.StartsWith("include", StringComparison.Ordinal) || command == "import")
                    throw new FormatException("Unsupported shader include directive: " + command);
                if (module.directives.Count > MaximumIncludes) throw new FormatException("Too many shader include directives.");
            }
            return module;
        }

        static int SpliceLength(string text, int index)
        {
            if (text[index] != '\\' || index + 1 >= text.Length) return 0;
            if (text[index + 1] == '\n') return 2;
            if (text[index + 1] == '\r') return index + 2 < text.Length && text[index + 2] == '\n' ? 3 : 2;
            return 0;
        }
        static int OriginalOffset(string text, int start, int logicalOffset)
        {
            int index = start, count = 0;
            while (count < logicalOffset)
            {
                int splice = SpliceLength(text, index);
                if (splice > 0) index += splice;
                else { index++; count++; }
            }
            return index;
        }

        internal static void ValidateSource(ShaderGraphSource source) => ReadModule(source);

        static Dictionary<string, Module> ReadArchive(IReadOnlyList<ShaderGraphSource> sources)
        {
            if (sources == null || sources.Count > MaximumSources) throw new FormatException("Shader source archive is missing or exceeds 4096 modules.");
            var modules = new Dictionary<string, Module>(StringComparer.Ordinal);
            long bytes = 0;
            int directives = 0;
            foreach (var source in sources)
            {
                if (source == null) throw new FormatException("A shader source entry is null.");
                bytes += CodeByteCount(source.code);
                if (bytes > MaximumArchiveBytes) throw new FormatException("Shader source archive exceeds 32 MiB of UTF-8 text.");
                var module = ReadModule(source);
                if (!modules.TryAdd(source.id, module)) throw new FormatException("Duplicate shader source ID: " + source.id);
                directives += module.directives.Count;
                if (directives > MaximumIncludes) throw new FormatException("Shader source archive has too many include directives.");
            }
            foreach (var module in modules.Values)
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var directive in module.directives)
                {
                    if (directive.requested == null) continue;
                    if (module.bindings.TryGetValue(directive.requested, out string target))
                    {
                        used.Add(directive.requested);
                        if (!modules.ContainsKey(target)) throw new FormatException("Missing embedded shader source: " + target);
                    }
                    else if (!IsEngineInclude(directive.requested))
                        throw new FormatException("Unresolved shader include in backup: " + directive.requested);
                }
                if (used.Count != module.bindings.Count) throw new FormatException("Shader include bindings do not match the original directives: " + module.source.id);
            }
            var active = new HashSet<string>(StringComparer.Ordinal);
            int Depth(Module module, int nesting)
            {
                if (nesting > MaximumDepth) throw new FormatException("Shader includes exceed 64 nested modules.");
                if (module.depth > 0) return module.depth;
                if (!active.Add(module.source.id)) throw new FormatException("Cyclic shader includes cannot be expanded, including guarded cycles: " + module.source.id);
                int depth = 1;
                foreach (string target in module.bindings.Values) depth = Math.Max(depth, 1 + Depth(modules[target], nesting + 1));
                active.Remove(module.source.id);
                if (depth > MaximumDepth) throw new FormatException("Shader includes exceed 64 nested modules.");
                return module.depth = depth;
            }
            foreach (var module in modules.Values) Depth(module, 1);
            return modules;
        }

        internal static void Validate(IReadOnlyList<ShaderGraphSource> sources) => ReadArchive(sources);

        internal static string Expand(string sourceId, IReadOnlyList<ShaderGraphSource> sources)
        {
            var modules = ReadArchive(sources);
            if (sourceId == null || !modules.TryGetValue(sourceId, out var root)) throw new FormatException("Shader source is missing: " + sourceId);
            var output = new StringBuilder();
            int bytes = 0, visits = 0;
            void Append(string text, int start, int length)
            {
                int added = Utf8.GetByteCount(text.AsSpan(start, length));
                if (added > MaximumArchiveBytes - bytes) throw new FormatException("Expanded shader source exceeds 32 MiB.");
                bytes += added; output.Append(text, start, length);
            }
            void Text(string text) => Append(text, 0, text.Length);
            void Visit(Module module)
            {
                if (++visits > 65536) throw new FormatException("Expanded shader source exceeds 65536 include visits.");
                string guard = module.once ? "WHIMTEX_SOURCE_ONCE_" + Digest(Utf8.GetBytes(module.source.id)) : null;
                if (guard != null) Text("\n#ifndef " + guard + "\n#define " + guard + "\n");
                int position = 0;
                foreach (var directive in module.directives)
                {
                    if (directive.requested != null && !module.bindings.ContainsKey(directive.requested)) continue; // Whitelisted engine include.
                    Append(module.source.code, position, directive.start - position);
                    Text("\n");
                    if (directive.requested != null) Visit(modules[module.bindings[directive.requested]]);
                    Text("\n");
                    position = directive.end;
                }
                Append(module.source.code, position, module.source.code.Length - position);
                if (guard != null) Text("\n#endif\n");
            }
            Visit(root);
            return output.ToString();
        }

        static string ModuleKey(ShaderGraphSource source)
        {
            var bindings = new List<ShaderGraphInclude>(source.includes);
            bindings.Sort((a, b) => StringComparer.Ordinal.Compare(a.requested, b.requested));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(source.code);
                writer.Write(bindings.Count);
                foreach (var binding in bindings) { writer.Write(binding.requested); writer.Write(binding.sourceId); }
            }
            return Digest(stream.ToArray());
        }

        internal static string Capture(string source, string path, List<ShaderGraphSource> destination)
            => CaptureRetainingIncludes(source, path, destination, null);

        internal static string CaptureRetainingIncludes(string source, string path, List<ShaderGraphSource> destination, string retainedRoot)
        {
            var known = ReadArchive(destination);
            // Local edits retain existing include snapshots; only newly requested includes read files.
            Module retained = null;
            if (retainedRoot != null && !known.TryGetValue(retainedRoot, out retained))
                throw new FormatException("Retained shader source is missing: " + retainedRoot);
            CodeByteCount(source);
            var byContent = new Dictionary<string, string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (var item in destination) { byContent.TryAdd(ModuleKey(item), item.id); bytes += CodeByteCount(item.code); }
            var additions = new List<ShaderGraphSource>();
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            long inspectedBytes = 0;
            int visits = 0;
            string rootPath = string.IsNullOrEmpty(path) ? null : PhysicalPath(path);
            string Visit(string code, string current, int depth)
            {
                if (depth > MaximumDepth) throw new IOException("Shader includes exceed 64 nested modules.");
                inspectedBytes += CodeByteCount(code);
                if (++visits > 65536 || inspectedBytes > 256L * 1024 * 1024)
                    throw new IOException("Shader capture exceeds 65536 file visits or 256 MiB of inspected text.");
                if (current != null && !active.Add(current)) throw new IOException("Cyclic shader includes cannot be captured, including guarded cycles: " + current);
                var item = new ShaderGraphSource { id = "capture", code = code };
                var parsed = ReadModule(item);
                var requested = new HashSet<string>(StringComparer.Ordinal);
                foreach (var directive in parsed.directives)
                {
                    string include = directive.requested;
                    if (include == null || !requested.Add(include)) continue;
                    if (depth == 1 && retained != null && retained.bindings.TryGetValue(include, out string saved))
                    {
                        item.includes.Add(new ShaderGraphInclude { requested = include, sourceId = saved });
                        continue;
                    }
                    string file = Resolve(include, current);
                    if (file == null || !File.Exists(file))
                    {
                        if (IsEngineInclude(include)) continue;
                        throw new FileNotFoundException("Cannot capture missing shader include '" + include + "' from " + (current ?? "inline source") + ".", file);
                    }
                    if (active.Contains(file)) throw new IOException("Cyclic shader includes cannot be captured, including guarded cycles: " + file);
                    if (!paths.TryGetValue(file, out string target)) target = Visit(ReadFile(file), file, depth + 1);
                    item.includes.Add(new ShaderGraphInclude { requested = include, sourceId = target });
                }
                string key = ModuleKey(item);
                if (!byContent.TryGetValue(key, out string id))
                {
                    id = "src_" + key;
                    if (known.ContainsKey(id)) throw new FormatException("Captured shader source ID conflicts with edited archive data: " + id);
                    bytes += CodeByteCount(code);
                    if (bytes > MaximumArchiveBytes || destination.Count + additions.Count >= MaximumSources)
                        throw new FormatException("Shader source archive exceeds its 32 MiB or 4096-module limit.");
                    item.id = id; additions.Add(item); byContent.Add(key, id);
                }
                if (current != null) { active.Remove(current); paths[current] = id; }
                return id;
            }
            string result = Visit(source, rootPath, 1);
            // Publish only after all files, mappings and aggregate bounds have succeeded.
            var combined = new List<ShaderGraphSource>(destination.Count + additions.Count);
            combined.AddRange(destination); combined.AddRange(additions);
            Validate(combined);
            destination.AddRange(additions);
            return result;
        }

        static string Resolve(string requested, string current)
        {
            if (requested.IndexOf('\0') >= 0) throw new IOException("Invalid shader include path.");
            string path = requested.Replace('\\', '/');
            if (Path.IsPathRooted(path) || path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                return PhysicalPath(path);
            return current == null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current), path));
        }

        internal static string PhysicalPath(string path)
        {
            path = path.Replace('\\', '/');
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
                throw new IOException("Shader source paths must be absolute or start with Assets/ or Packages/.");
            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package = PackageInfo.FindForAssetPath(path);
                if (package != null && path.StartsWith(package.assetPath + "/", StringComparison.Ordinal))
                    return Path.GetFullPath(Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1)));
            }
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        }

        static string ReadFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumSourceBytes) throw new IOException("Included shader file exceeds 8 MiB: " + path);
            var bytes = new byte[(int)stream.Length + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0) count += read;
            if (count == bytes.Length) throw new IOException("Shader file grew while being captured; retry: " + path);
            using var reader = new StreamReader(new MemoryStream(bytes, 0, count, false), Utf8, true);
            string result = reader.ReadToEnd();
            CodeByteCount(result);
            return result;
        }
    }
}
