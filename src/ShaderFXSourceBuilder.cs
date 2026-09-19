using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXSourceBuilder
    {
        internal const string NoiseLibraryInclude = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl\"\n";
        private const int MaximumCharacters = 2 * 1024 * 1024;
        private static readonly Regex Include = new Regex("^\\s*#\\s*(include|include_with_pragmas)\\s+\"([^\"]+)\"\\s*$");
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");
        private readonly string projectRoot = Path.GetDirectoryName(Application.dataPath);

        internal static HashSet<string> GetDependencies(string source, string path)
        {
            var builder = new ShaderFXSourceBuilder();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
            void Visit(string text, string current, int depth)
            {
                if (depth > 32 || found.Count > 256) return;
                bool block = false;
                using var reader = new StringReader(text ?? "");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match include = Include.Match(MaskComments(line, ref block));
                    if (!include.Success) continue;
                    try
                    {
                        string dependency = builder.Resolve(include.Groups[2].Value, current);
                        if (!found.Add(dependency)) continue;
                        string physical = builder.PhysicalPath(dependency);
                        if (File.Exists(physical) && new FileInfo(physical).Length <= MaximumCharacters)
                            Visit(File.ReadAllText(physical), dependency, depth + 1);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (InvalidOperationException) { }
                }
            }
            Visit(source, path, 0);
            return found;
        }

        // Upgrade only generated helpers in the last applied source, never pending user code.
        internal static string UpgradeTransformHelpers(string source, List<ShaderFXParameter> parameters)
        {
            if (string.IsNullOrEmpty(source)) return source;
            foreach (var parameter in parameters)
            {
                if (parameter.type != ShaderFXParameterType.Transform2D) continue;
                foreach (string direction in new[] { "ToLocal", "ToInput" })
                {
                    string prefix = parameter.InternalPrefix + direction;
                    if (source.Contains("float4 " + prefix + "Row2;")) continue;
                    string old = $"float2 {parameter.name}_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); return float2(dot({prefix}Row0.xyz, p), dot({prefix}Row1.xyz, p)); }}";
                    string replacement = $"float4 {prefix}Row2;\nfloat2 {parameter.name}_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); float w = dot({prefix}Row2.xyz, p); w = w < 0.0 ? min(w, -1e-7) : max(w, 1e-7); return float2(dot({prefix}Row0.xyz, p), dot({prefix}Row1.xyz, p)) / w; }}";
                    source = source.Replace(old, replacement);
                }
            }
            return source;
        }

        internal static string Build(ShaderFX effect, string assetPath)
        {
            ShaderFXSourceBuilder builder = new ShaderFXSourceBuilder();
            StringBuilder properties = new StringBuilder();
            StringBuilder uniforms = new StringBuilder();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal)
            {
                "_MainTex", "_MainTex_TexelSize", "_InputSize", "_CanvasSize", "_PreviewScale",
                "ApplyFX", "SampleInput", "LayerToLocal", "SpriteFXFragment", "vert_img", "v2f_img"
            };
            foreach (ShaderFXParameter parameter in effect.Parameters)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name) || !Identifier.IsMatch(parameter.name) ||
                    parameter.name.StartsWith("_WhimTex_", StringComparison.Ordinal) || !names.Add(parameter.name))
                    throw new InvalidOperationException($"Invalid, duplicate or reserved parameter name: '{parameter?.name}'. Use an HLSL identifier such as _Amount.");
                string name = parameter.name;
                switch (parameter.type)
                {
                    case ShaderFXParameterType.Float:
                    case ShaderFXParameterType.Bool:
                    case ShaderFXParameterType.Enum:
                        properties.AppendLine($"{name} (\"{name}\", Float) = 0");
                        uniforms.AppendLine($"float {name};");
                        break;
                    case ShaderFXParameterType.Color:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (1,1,1,1)");
                        uniforms.AppendLine($"float4 {name};");
                        break;
                    case ShaderFXParameterType.Vector2:
                    case ShaderFXParameterType.Vector3:
                    case ShaderFXParameterType.Normal:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (0,0,0,0)");
                        uniforms.AppendLine($"float{(parameter.type == ShaderFXParameterType.Vector2 ? 2 : 3)} {name};");
                        break;
                    case ShaderFXParameterType.Vector:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (0,0,0,0)");
                        uniforms.AppendLine($"float4 {name};");
                        break;
                    case ShaderFXParameterType.Texture2D:
                        if (!names.Add(name + "_TexelSize"))
                            throw new InvalidOperationException($"Reserved texture parameter: {name}_TexelSize.");
                        properties.AppendLine($"{name} (\"{name}\", 2D) = \"white\" {{}}");
                        uniforms.AppendLine($"sampler2D {name};\nfloat4 {name}_TexelSize;");
                        break;
                    case ShaderFXParameterType.Transform2D:
                        if (!names.Add(name + "_ToLocal") || !names.Add(name + "_ToInput"))
                            throw new InvalidOperationException("Transform helper name conflicts with another parameter: " + name);
                        string prefix = parameter.InternalPrefix;
                        foreach (string direction in new[] { "ToLocal", "ToInput" })
                        {
                            uniforms.AppendLine($"float4 {prefix}{direction}Row0;\nfloat4 {prefix}{direction}Row1;\nfloat4 {prefix}{direction}Row2;");
                            uniforms.AppendLine($"float2 {name}_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); float w = dot({prefix}{direction}Row2.xyz, p); w = w < 0.0 ? min(w, -1e-7) : max(w, 1e-7); return float2(dot({prefix}{direction}Row0.xyz, p), dot({prefix}{direction}Row1.xyz, p)) / w; }}");
                        }
                        break;
                    case ShaderFXParameterType.Curve:
                        if (!names.Add(name + "_Sample"))
                            throw new InvalidOperationException("Curve helper name conflicts with another parameter: " + name);
                        string curveTexture = parameter.InternalPrefix + "Curve";
                        properties.AppendLine($"{curveTexture} (\"{name}\", 2D) = \"white\" {{}}");
                        uniforms.AppendLine($"sampler2D {curveTexture};");
                        uniforms.AppendLine($"float {name}_Sample(float t) {{ return tex2Dlod({curveTexture}, float4((saturate(t) * 511.0 + 0.5) / 512.0, 0.5, 0.0, 0.0)).r; }}");
                        break;
                    case ShaderFXParameterType.Gradient:
                        if (!names.Add(name + "_Sample"))
                            throw new InvalidOperationException("Gradient helper name conflicts with another parameter: " + name);
                        string gradientTexture = parameter.InternalPrefix + "Gradient";
                        properties.AppendLine($"{gradientTexture} (\"{name}\", 2D) = \"white\" {{}}");
                        uniforms.AppendLine($"sampler2D {gradientTexture};");
                        uniforms.AppendLine($"float4 {name}_Sample(float t) {{ return tex2Dlod({gradientTexture}, float4((saturate(t) * 511.0 + 0.5) / 512.0, 0.5, 0.0, 0.0)); }}");
                        break;
                    default: throw new InvalidOperationException($"Unsupported parameter type: {parameter.type}.");
                }
            }
            string expanded = builder.ResolveIncludes(effect.Code ?? string.Empty, assetPath);
            if (Regex.IsMatch(expanded, @"\b_WhimTex_[A-Za-z0-9_]*"))
                throw new InvalidOperationException("The _WhimTex_ prefix is reserved for generated shader data.");
            return "Shader \"Hidden/TextureCompositor/ShaderFX/" + effect.ShaderKey + "\"\n{\n" +
                "Properties {\n_MainTex (\"Input\", 2D) = \"white\" {}\n" + properties + "}\n" +
                "SubShader { Cull Off ZWrite Off ZTest Always Blend Off\nPass {\nCGPROGRAM\n" +
                "#pragma vertex vert_img\n#pragma fragment SpriteFXFragment\n#pragma target 3.5\n" +
                "#include \"UnityCG.cginc\"\n" + NoiseLibraryInclude + "sampler2D _MainTex;\nfloat4 _MainTex_TexelSize;\n" +
                "float4 _InputSize;\nfloat4 _CanvasSize;\nfloat _PreviewScale;\n" + uniforms +
                "float4 _WhimTex_LayerToLocalRow0, _WhimTex_LayerToLocalRow1, _WhimTex_LayerToLocalRow2;\n" +
                "float2 LayerToLocal(float2 uv) { float3 p = float3(uv, 1); float w = dot(_WhimTex_LayerToLocalRow2.xyz, p); w = abs(w) < 1e-8 ? (w < 0 ? -1e-8 : 1e-8) : w; return float2(dot(_WhimTex_LayerToLocalRow0.xyz, p), dot(_WhimTex_LayerToLocalRow1.xyz, p)) / w; }\n" +
                "float4 SampleInput(float2 uv) { return tex2D(_MainTex, uv); }\n" +
                LineDirective(1, assetPath) + expanded + "\n#line 1 \"SpriteFXWrapper\"\n" +
                "float4 SpriteFXFragment(v2f_img input) : SV_Target { return ApplyFX(input.uv, SampleInput(input.uv)); }\n" +
                "ENDCG\n}\n}\nFallback Off\n}\n";
        }

        // Preserve dependencies across Save As/migration without expanding their methods or
        // changing the editor's pending code. Only paths in the saved model are made project-relative.
        internal static string DocumentCode(string source, string sourcePath) =>
            string.IsNullOrEmpty(source) || !source.Contains("#") ? source : new ShaderFXSourceBuilder().ResolveIncludes(source, sourcePath);

        internal static bool HasRelativeIncludes(string source)
        {
            bool block = false;
            using var reader = new StringReader(source ?? "");
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var match = Include.Match(MaskComments(line, ref block));
                if (!match.Success) continue;
                string path = match.Groups[2].Value.Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private string ResolveIncludes(string source, string sourcePath)
        {
            if (source.Length > MaximumCharacters)
                throw new InvalidOperationException("Shader FX source exceeds the 2 MiB character limit.");
            StringBuilder result = new StringBuilder();
            bool blockComment = false;
            using (StringReader reader = new StringReader(source))
            {
                string original;
                int lineNumber = 0;
                while ((original = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string line = original;
                    Match include = Include.Match(MaskComments(original, ref blockComment));
                    if (include.Success)
                    {
                        try
                        {
                            string requested = include.Groups[2].Value.Replace('\\', '/');
                            string resolved = Resolve(requested, sourcePath);
                            if (requested.StartsWith("Assets/", StringComparison.Ordinal) ||
                                requested.StartsWith("Packages/", StringComparison.Ordinal) ||
                                requested.StartsWith(".", StringComparison.Ordinal) ||
                                File.Exists(PhysicalPath(resolved)))
                            {
                                Group pathGroup = include.Groups[2];
                                line = original.Substring(0, pathGroup.Index) + resolved +
                                    original.Substring(pathGroup.Index + pathGroup.Length);
                            }
                        }
                        catch (Exception exception)
                        {
                            throw new InvalidOperationException($"{sourcePath}:{lineNumber}: {exception.Message}", exception);
                        }
                    }
                    result.AppendLine(line);
                }
            }
            if (blockComment)
                throw new InvalidOperationException($"{sourcePath}: unterminated block comment.");
            return result.ToString();
        }

        internal static string ExportIncludes(string source, string sourcePath)
        {
            var builder = new ShaderFXSourceBuilder();
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int characters = 0;
            string Expand(string text, string path, int depth)
            {
                if (depth > 32 || !active.Add(path)) throw new IOException("Cyclic or excessively nested shader includes cannot be exported.");
                characters += text.Length;
                if (characters > MaximumCharacters) throw new IOException("Exported HLSL exceeds 2 MiB.");
                var result = new StringBuilder();
                using var reader = new StringReader(text);
                bool block = false;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var include = Include.Match(MaskComments(line, ref block));
                    if (include.Success)
                    {
                        string requested = include.Groups[2].Value;
                        string resolved = builder.Resolve(requested, path);
                        string physical = builder.PhysicalPath(resolved);
                        if (File.Exists(physical))
                        {
                            if (new FileInfo(physical).Length > MaximumCharacters) throw new IOException("Included HLSL exceeds 2 MiB.");
                            result.AppendLine(Expand(File.ReadAllText(physical), resolved, depth + 1));
                            continue;
                        }
                        if (requested.StartsWith(".", StringComparison.Ordinal) || requested.StartsWith("Assets/", StringComparison.Ordinal) || requested.StartsWith("Packages/", StringComparison.Ordinal))
                            throw new IOException("Missing shader include: " + resolved);
                        // Engine includes (UnityCG.cginc etc.) remain engine dependencies.
                    }
                    result.AppendLine(line);
                }
                active.Remove(path);
                return result.ToString();
            }
            return Expand(source, sourcePath, 0);
        }

        internal const int PortableMaximumBytes = 64 * 1024;
        private static readonly HashSet<string> PortableIncludes = new HashSet<string>(StringComparer.Ordinal)
        {
            "UnityCG.cginc",
            "Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl",
            "Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc"
        };
        private static readonly Regex PortableInclude = new Regex("^\\s*#\\s*include\\s+[\"<]([^\">]+)[\">]\\s*$");
        private static readonly Regex PortableDirective = new Regex(@"^\s*#\s*(\w+)\b");
        private static readonly HashSet<string> PortableDirectives = new HashSet<string>(StringComparer.Ordinal)
            { "define", "undef", "if", "ifdef", "ifndef", "elif", "else", "endif", "error" };

        internal static void ValidatePortableSource(string source)
        {
            if (string.IsNullOrEmpty(source) || Encoding.UTF8.GetByteCount(source) > PortableMaximumBytes)
                throw new FormatException("Portable FX exceeds 64 KiB of UTF-8 HLSL (including parameter declarations).");
            if (source.IndexOf("guid:", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new FormatException("Portable FX cannot reference texture asset GUIDs.");
            bool block = false;
            foreach (string line in PortableLogicalLines(source))
            {
                string clean = MaskComments(line, ref block);
                var directive = PortableDirective.Match(clean);
                if (!directive.Success)
                {
                    if (clean.IndexOf('#') >= 0) throw new FormatException("Invalid portable HLSL directive.");
                    continue;
                }
                string name = directive.Groups[1].Value;
                if (name == "include")
                {
                    var include = PortableInclude.Match(clean);
                    if (!include.Success || !PortableIncludes.Contains(include.Groups[1].Value))
                        throw new FormatException("Portable FX only accepts literal built-in includes: " + string.Join(", ", PortableIncludes) + ". Copy as Portable expands other files on the sender's machine.");
                }
                else if (!PortableDirectives.Contains(name))
                    throw new FormatException("Unsupported portable HLSL directive: #" + name);
            }
            if (block) throw new FormatException("Unterminated HLSL block comment.");
        }

        private static IEnumerable<string> PortableLogicalLines(string source)
        {
            // Match preprocessing order: line splicing happens before comments are removed.
            string joined = Regex.Replace(source, @"\\\r?\n", "");
            using var reader = new StringReader(joined);
            string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }

        internal static string ExportPortableIncludes(string source, string sourcePath)
        {
            var builder = new ShaderFXSourceBuilder();
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new StringBuilder();
            int bytes = 0;
            void Append(string text, string path)
            {
                bytes += Encoding.UTF8.GetByteCount(text);
                if (bytes > PortableMaximumBytes) throw new IOException("Portable FX exceeds 64 KiB while expanding: " + path);
                result.Append(text);
            }
            void Expand(string text, string path, int depth)
            {
                if (depth > 8) throw new IOException("Portable include nesting exceeds 8: " + path);
                if (!active.Add(path)) throw new IOException("Cyclic portable shader include: " + path);
                try
                {
                    bool block = false;
                    foreach (string line in PortableLogicalLines(text))
                    {
                        string clean = MaskComments(line, ref block);
                        var include = PortableInclude.Match(clean);
                        if (!include.Success) { Append(line + "\n", path); continue; }
                        string requested = include.Groups[1].Value;
                        string resolved = PortableIncludes.Contains(requested) ? requested : builder.Resolve(requested, path);
                        if (PortableIncludes.Contains(resolved))
                        {
                            int start = clean.IndexOf('#');
                            int end = include.Groups[1].Index + include.Groups[1].Length + 1;
                            Append(line.Substring(0, start) + "#include \"" + resolved + "\"" + line.Substring(end) + "\n", path);
                            continue;
                        }
                        string physical = builder.PhysicalPath(resolved);
                        if (!File.Exists(physical)) throw new IOException("Missing portable shader include: " + resolved);
                        if (new FileInfo(physical).Length > PortableMaximumBytes)
                            throw new IOException("Shader include exceeds 64 KiB: " + resolved);
                        int first = clean.IndexOf('#');
                        int last = include.Groups[1].Index + include.Groups[1].Length + 1;
                        Append(line.Substring(0, first) + "\n", path);
                        Expand(File.ReadAllText(physical), resolved, depth + 1);
                        Append(line.Substring(last) + "\n", path);
                    }
                    if (block) throw new IOException("Unterminated block comment in shader include: " + path);
                }
                finally { active.Remove(path); }
            }
            Expand(source, sourcePath, 0);
            string expanded = result.ToString();
            ValidatePortableSource(expanded);
            return expanded;
        }

        private string Resolve(string requested, string sourcePath)
        {
            requested = requested.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(requested) || Path.IsPathRooted(requested) ||
                requested.IndexOfAny(new[] { ':', '"', '\r', '\n' }) >= 0)
                throw new InvalidOperationException($"Use a project, package or relative library path: {requested}.");
            string relative = requested.StartsWith("Assets/", StringComparison.Ordinal) || requested.StartsWith("Packages/", StringComparison.Ordinal)
                ? requested : Path.GetDirectoryName(sourcePath).Replace('\\', '/') + "/" + requested;
            if (Path.IsPathRooted(sourcePath) && !requested.StartsWith("Assets/", StringComparison.Ordinal) &&
                !requested.StartsWith("Packages/", StringComparison.Ordinal))
            {
                string userPath = Path.GetFullPath(relative);
                if (!PresetLibraryPaths.IsInside(userPath, ShaderFXCatalog.Folder))
                    throw new InvalidOperationException($"Library path escapes the user ShaderFX folder: {requested}.");
                return userPath.Replace('\\', '/');
            }
            string absolute = Path.GetFullPath(Path.Combine(projectRoot, relative));
            string prefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Library path escapes the project: {requested}.");
            relative = absolute.Substring(prefix.Length).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal) && !relative.StartsWith("Packages/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Libraries must be inside Assets or Packages: {requested}.");
            return relative;
        }

        private string PhysicalPath(string assetPath)
        {
            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
                if (package != null && assetPath.StartsWith(package.assetPath + "/", StringComparison.Ordinal))
                    return Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length + 1));
            }
            return Path.Combine(projectRoot, assetPath);
        }

        private static string LineDirective(int number, string path) => $"#line {number} \"{path.Replace('\\', '/')}\"\n";

        internal static string MaskComments(string line, ref bool blockComment)
        {
            StringBuilder clean = new StringBuilder(line.Length);
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (blockComment)
                {
                    clean.Append(' ');
                    if (current == '*' && next == '/')
                    {
                        blockComment = false;
                        clean.Append(' ');
                        i++;
                    }
                    continue;
                }
                if (!quoted && current == '/' && next == '/')
                {
                    clean.Append(' ', line.Length - i);
                    break;
                }
                if (!quoted && current == '/' && next == '*')
                {
                    blockComment = true;
                    clean.Append("  ");
                    i++;
                    continue;
                }
                clean.Append(current);
                if (quoted && current == '\\' && next != '\0')
                {
                    clean.Append(next);
                    i++;
                }
                else if (current == '"')
                    quoted = !quoted;
            }
            return clean.ToString();
        }
    }
}
