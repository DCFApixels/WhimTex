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
    internal readonly struct ShaderSourceAnalysis
    {
        internal readonly bool IsPixelLocal, SupportsRegionalColorInput;
        internal readonly string LocalityStatus, UnityTimeWarning;
        internal readonly ShaderContextDependencies ContextDependencies;
        internal readonly ShaderLocalityReason[] LocalityReasons;
        internal ShaderSourceAnalysis(ShaderEntryPoint entry, bool regional, ShaderContextDependencies dependencies, string unityTimeWarning)
        {
            IsPixelLocal = entry.verified;
            LocalityStatus = entry.verified ? "pixelLocal" : entry.spatial ? "spatial" : "unverified";
            SupportsRegionalColorInput = regional; ContextDependencies = dependencies;
            LocalityReasons = entry.reasons; UnityTimeWarning = unityTimeWarning;
        }
    }

    internal sealed class ShaderSourceBuilder
    {
        internal const string NoiseLibraryInclude = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/WTNoise.hlsl\"\n";
        internal const string ColorLibraryInclude = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc\"\n";
        internal const int MaximumCharacters = 2 * 1024 * 1024;
        internal const string RegionalColorMarker = "// WhimTex regional color input v1\n";
        internal const string ViewportWrapperMarker = "// WhimTex viewport wrapper v5\n";
        // Conservative capability inference over expanded user code, including helpers.
        // Any explicit sampling, derivative or unresolved compiler input needs a full frame.
        private static readonly Regex SpatialInput = new Regex(
            @"\b(SampleInput(?:Lod)?|_MainTex\w*|_ScreenParams|_ProjectionParams|UNITY\w*|Unity\w*|ddx\w*|ddy\w*|fwidth\w*|tex\w*|\w*Buffer\w*|\w*Texture\w*)\b|\.\s*(Load|Sample\w*|Gather\w*)\s*\(|##|#\s*(include\w*|pragma|line)\b",
            RegexOptions.CultureInvariant);

        private static bool HasRegionalColorInput(string expanded, IReadOnlyList<ShaderParameter> parameters)
            => HasRegionalDocument(new ShaderSourceDocument(expanded), parameters);

        private static bool HasRegionalDocument(ShaderSourceDocument document, IReadOnlyList<ShaderParameter> parameters)
        {
            foreach (var parameter in parameters)
                if (parameter?.type == ShaderParameterType.Texture2D) return false;
            var tokens = document.Tokens;
            // The coordinate pass deliberately leaves namespace bodies opaque. Retain
            // the conservative scanner for those uncommon authoring constructs.
            foreach (var token in tokens)
                if (token.text == "namespace" || token.text == "::") return HasRegionalUnparsed(document.Source, parameters);
            string source = document.Comments;
            var spatial = RegionalSpatialOffsets(document);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var remaining = new StringBuilder();
            int position = 0;
            foreach (var f in document.Functions)
            {
                int start = tokens[f.type].start, end = tokens[f.end].end;
                names.Add(tokens[f.type + 1].text);
                remaining.Append(source, position, start - position);
                int index = spatial.BinarySearch(start);
                if (index < 0) index = ~index;
                if (index < spatial.Count && spatial[index] < end) return false;
                position = end;
            }
            if (!names.Contains("ApplyFX")) return false;
            remaining.Append(source, position, source.Length - position);
            foreach (string declaration in remaining.ToString().Split('\n'))
            {
                if (declaration.TrimStart().StartsWith("#line ", StringComparison.Ordinal)) continue;
                if ((declaration.Contains("#") || declaration.Contains("=")) && SpatialInput.IsMatch(declaration)) return false;
                if (declaration.TrimStart().StartsWith("#define", StringComparison.Ordinal))
                    foreach (Match identifier in Regex.Matches(declaration, @"\b[A-Za-z_]\w*\b"))
                        if (names.Contains(identifier.Value)) return false;
            }
            return true;
        }

        static List<int> RegionalSpatialOffsets(ShaderSourceDocument document)
        {
            var result = new List<int>();
            var tokens = document.AllTokens;
            for (int i = 0; i < tokens.Count; i++)
            {
                string name = tokens[i].text;
                string Next(int n) => i + n < tokens.Count ? tokens[i + n].text : "";
                if (name == "tex2Dlod" && Next(1) == "(" && Next(2) == "_WhimTexHueGradient" && Next(3) == ",") continue;
                bool spatial = name == "SampleInput" || name == "SampleInputLod" || name.StartsWith("_MainTex", StringComparison.Ordinal) ||
                    name == "_ScreenParams" || name == "_ProjectionParams" || name.StartsWith("UNITY", StringComparison.Ordinal) || name.StartsWith("Unity", StringComparison.Ordinal) ||
                    name.StartsWith("ddx", StringComparison.Ordinal) || name.StartsWith("ddy", StringComparison.Ordinal) || name.StartsWith("fwidth", StringComparison.Ordinal) ||
                    name.StartsWith("tex", StringComparison.Ordinal) || name.IndexOf("Buffer", StringComparison.Ordinal) >= 0 || name.IndexOf("Texture", StringComparison.Ordinal) >= 0;
                if (name == "." && (Next(1) == "Load" || Next(1).StartsWith("Sample", StringComparison.Ordinal) || Next(1).StartsWith("Gather", StringComparison.Ordinal)) && Next(2) == "(") spatial = true;
                if (name == "#" && (Next(1) == "pragma" || Next(1).StartsWith("include", StringComparison.Ordinal) ||
                    Next(1) == "#" && tokens[i].end == tokens[i + 1].start)) spatial = true;
                if (spatial) result.Add(tokens[i].start);
            }
            return result;
        }

        private static bool HasRegionalUnparsed(string expanded, IReadOnlyList<ShaderParameter> parameters)
        {
            foreach (var parameter in parameters)
                if (parameter?.type == ShaderParameterType.Texture2D) return false;
            bool block = false;
            var clean = new StringBuilder();
            using var reader = new StringReader(expanded);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // Include expansion emits source locations; they do not consume pixels.
                if (line.TrimStart().StartsWith("#line ", StringComparison.Ordinal)) continue;
                clean.AppendLine(MaskComments(line, ref block));
            }
            string source = clean.ToString();
            var functions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var remaining = new StringBuilder();
            var signature = new Regex(@"\b\w+\s+(\w+)\s*\([^;{}]*\)\s*(?::[^;{}]+)?\{", RegexOptions.CultureInvariant);
            int position = 0;
            while (position < source.Length)
            {
                var match = signature.Match(source, position);
                if (!match.Success) break;
                int end = match.Index + match.Length, depth = 1;
                while (end < source.Length && depth > 0)
                {
                    char c = source[end++];
                    if (c == '{') depth++; else if (c == '}') depth--;
                }
                if (depth != 0) return false;
                remaining.Append(source, position, match.Index - position);
                string name = match.Groups[1].Value;
                if (!functions.TryGetValue(name, out var overloads)) functions[name] = overloads = new List<string>();
                overloads.Add(source.Substring(match.Index, end - match.Index));
                position = end;
            }
            remaining.Append(source, position, source.Length - position);
            // Declarations do not read pixels. Macro expansions/global initializers can.
            foreach (string declaration in remaining.ToString().Split('\n'))
            {
                if ((declaration.Contains("#") || declaration.Contains("=")) && SpatialInput.IsMatch(declaration)) return false;
                if (declaration.TrimStart().StartsWith("#define", StringComparison.Ordinal))
                    foreach (Match identifier in Regex.Matches(declaration, @"\b[A-Za-z_]\w*\b"))
                        if (functions.ContainsKey(identifier.Value)) return false;
            }
            if (!functions.ContainsKey("ApplyFX")) return false;
            // Include preview callbacks and inactive helpers conservatively: an inspector
            // consuming SampleInput must not receive a cropped image as its whole input.
            foreach (var overloads in functions.Values)
            {
                foreach (var body in overloads)
                {
                    // Only this reserved immutable lookup is independent of document
                    // texture input. Other reads, even in an overload, remain spatial.
                    string checkedBody = Regex.Replace(body, @"\btex2Dlod\s*\(\s*_WhimTexHueGradient\s*,",
                        "WhimTexImmutableHueLookup(");
                    if (SpatialInput.IsMatch(checkedBody)) return false;
                }
            }
            return true;
        }
        private const string InputSamplingSource = @"
float _WhimTex_InputFilter;
float4 SampleInput(float2 uv) { return tex2D(_MainTex, uv); }
float4 SampleInputLod(float2 uv, float lod) { return SampleTextureLod(_MainTex, uv, lod); }
float4 SampleInput(float2 uv, float tiling)
{
    float4 result = 0.0;
    if (tiling < 0.5) result = SampleInput(uv);
    else if (tiling >= 2.5)
    {
        if (all(uv >= 0.0) && all(uv <= 1.0)) result = SampleInput(uv);
    }
    else if (tiling >= 1.5)
        result = SampleInput(1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0));
    else
    {
        uv = frac(uv);
        if (_WhimTex_InputFilter < 0.5) result = SampleInput(uv);
        else
        {
            float2 size = _MainTex_TexelSize.zw;
            float2 pixel = uv * size - 0.5;
            float2 origin = floor(pixel);
            float2 weight = pixel - origin;
            float2 first = frac((origin + 0.5) / size);
            float2 second = frac((origin + 1.5) / size);
            float4 a = tex2Dlod(_MainTex, float4(first, 0.0, 0.0));
            float4 b = tex2Dlod(_MainTex, float4(second.x, first.y, 0.0, 0.0));
            float4 c = tex2Dlod(_MainTex, float4(first.x, second.y, 0.0, 0.0));
            float4 d = tex2Dlod(_MainTex, float4(second, 0.0, 0.0));
            result = lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
        }
    }
    return result;
}
";
        private static readonly Regex Include = new Regex(@"^\s*#\s*(?:include|include_with_pragmas)\s*(?:""(?<path>[^""]+)""|<(?<path>[^>]+)>)\s*$");
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly Regex UnityTimeInput = new Regex(
            @"(?<![A-Za-z0-9_])(_Time|_SinTime|_CosTime|_TimeParameters|unity_DeltaTime|unity_Time|unity_SinTime|unity_CosTime)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        private readonly string projectRoot = ShaderPreparationEnvironment.Current?.projectRoot ?? Path.GetDirectoryName(Application.dataPath);

        internal static HashSet<string> GetDependencies(string source, string path)
        {
            var builder = new ShaderSourceBuilder();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
            void Visit(string text, string current, int depth)
            {
                if (depth > 32 || found.Count > 256) return;
                bool block = false;
                using var reader = new StringReader(text ?? "");
                while (ReadSourceLine(reader, out _, out string line, out _))
                {
                    Match include = Include.Match(MaskComments(line, ref block));
                    if (!include.Success) continue;
                    try
                    {
                        string dependency = builder.Resolve(include.Groups["path"].Value, current);
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

        internal static string GetDeterminismWarning(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            return GetDeterminismWarningDocument(new ShaderSourceDocument(source));
        }
        private static string GetDeterminismWarningDocument(ShaderSourceDocument document)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in UnityTimeInput.Matches(document.Comments)) found.Add(match.Value);
            if (found.Count == 0) return null;
            var names = new List<string>(found);
            names.Sort(StringComparer.Ordinal);
            return "time-dependent Unity inputs (" + string.Join(", ", names) +
                ") are allowed, but WhimTex does not control their updates. Results may differ between Canvas, thumbnails and export. Result caching is disabled; use _WhimTexTime or an explicit parameter for predictable behavior.";
        }

        internal static string Build(ShaderFX effect, string assetPath) => BuildCore(effect, assetPath, null, null, out _);

        internal static string BuildPreview(ShaderFX effect, ShaderParameterControl preview, ShaderParameter parameter)
            => BuildCore(effect, effect.SourcePath, preview, parameter, out _);

        internal static string BuildResolvedPreview(ShaderFX effect, ShaderParameterControl preview, string expanded, string call)
            => BuildCore(effect, effect.SourcePath, preview, null, out _, expanded, call);

        private static string BuildCore(ShaderFX effect, string assetPath, ShaderParameterControl preview, ShaderParameter previewParameter,
            out ShaderSourceAnalysis analysis, string resolvedSource = null, string previewCall = null)
            => BuildCore(effect.Code, effect.Parameters, "Hidden/WhimTex/ShaderFX/" + effect.ShaderKey,
                assetPath, preview, previewParameter, out analysis, resolvedSource, previewCall);

        internal static string BuildGenerated(string source, IReadOnlyList<ShaderParameter> parameters, string shaderName, string sourcePath, int maximumCharacters)
            => BuildCore(source, parameters, shaderName, sourcePath, null, null, out _, maximumCharacters: maximumCharacters);

        internal static string BuildLayer(string source, IReadOnlyList<ShaderParameter> parameters, string sourcePath)
            => BuildCore(source, parameters, "Hidden/WhimTex/ShaderLayer", sourcePath, null, null, out _, proceduralInput: true);

        internal static string BuildProgram(string source, IReadOnlyList<ShaderParameter> parameters, string sourcePath,
            bool proceduralInput, int maximumCharacters, out ShaderSourceAnalysis analysis, string noiseLibrary = null)
            => BuildCore(source, parameters, "Hidden/WhimTex/ShaderProgram", sourcePath, null, null, out analysis,
                maximumCharacters: maximumCharacters, proceduralInput: proceduralInput, noiseLibrary: noiseLibrary);

        internal static string BuildGraphProgram(ShaderGraphCompilation compilation, string sourcePath, out ShaderSourceAnalysis analysis)
            => BuildCore(compilation.ExecutableSource, compilation.shaderParameters, "Hidden/WhimTex/ShaderProgram", sourcePath, null, null, out analysis,
                maximumCharacters: ShaderGraphSources.MaximumArchiveBytes + 1024 * 1024,
                noiseLibrary: ShaderGraphNoiseLibrary.ForGeneratedSource(compilation.ExecutableSource), generatedSource: compilation.generatedSource,
                additionalDependencies: compilation.contextDependencies);

        private static string BuildCore(string code, IReadOnlyList<ShaderParameter> parameters, string shaderName,
            string assetPath, ShaderParameterControl preview, ShaderParameter previewParameter,
            out ShaderSourceAnalysis analysis, string resolvedSource = null, string previewCall = null, int maximumCharacters = MaximumCharacters, bool proceduralInput = false, string noiseLibrary = null,
            ShaderGraphCompiler.GeneratedSource generatedSource = null, ShaderContextDependencies additionalDependencies = ShaderContextDependencies.None)
        {
            if (ShaderPreparationEnvironment.Current == null)
            { WhimTexHueGradient.Ensure(); BlueNoiseTextures.EnsureShaderGlobals(); }
            ShaderSourceBuilder builder = new ShaderSourceBuilder();
            StringBuilder properties = new StringBuilder();
            StringBuilder uniforms = new StringBuilder();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal)
            {
                "_MainTex", "_MainTex_TexelSize", "_InputSize", "_CanvasSize", "_RenderScale", "_PixelScale",
                "_WhimTexTime", "_WhimTexInstanceIndex",
                "ApplyFX", "SampleInput", "SampleTexture", "PinnedUV", "WhimTexTexture", "LayerToLocal", "SpriteFXFragment", "vert_img", "v2f_img"
            };
            BuildParameterDeclarations(parameters, properties, uniforms, names);
            var textureViews = new StringBuilder();
            ShaderTextureViews.Declare("_MainTex", properties, textureViews);
            foreach (var parameter in parameters)
                if (parameter.type == ShaderParameterType.Texture2D)
                    ShaderTextureViews.Declare(parameter.name, properties, textureViews);
            string expanded = resolvedSource ?? builder.ResolveIncludes(code ?? string.Empty, assetPath, maximumCharacters);
            if (Regex.IsMatch(expanded, @"\b_WhimTex_[A-Za-z0-9_]*"))
                throw new InvalidOperationException("The _WhimTex_ prefix is reserved for generated shader data.");
            string contractSource = ExpandExportIncludes(expanded, assetPath, true, maximumCharacters);
            var document = generatedSource?.CreateDocument(code, contractSource) ?? new ShaderSourceDocument(contractSource);
            var contextDependencies = ShaderContextUsage.AnalyzeDocument(document) | additionalDependencies;
            var entry = ShaderEntryPoint.ParseDocument(document, assetPath);
            // Expand before lowering so helper functions in project/package includes receive
            // the same immutable-coordinate rules. Keep the ordinary include path untouched.
            ShaderTextureViews.PrepareDocument(document);
            string lowered = entry.preparedSource;
            string imageUv = "_WhimTex_Viewport.xy + input.uv * _WhimTex_Viewport.zw";
            string entryUv = proceduralInput ? "ProceduralSourceUv(" + imageUv + ")" : imageUv;
            string fragment = preview == null ? entry.Call(entryUv, "tex2D(_MainTex, " + entryUv + ")")
                : previewCall ?? ShaderPreviewFunction.Call(expanded, preview.previewFunction, previewParameter, preview.type, preview.previewParameters, parameters);
            bool regional = false;
            if (preview == null)
            {
                try { regional = HasRegionalDocument(document, parameters); }
                catch (IOException) { } // Unknown dependencies keep the complete-input path.
                catch (InvalidOperationException) { }
            }
            analysis = new ShaderSourceAnalysis(entry, regional, contextDependencies, GetDeterminismWarningDocument(document));
            return ViewportWrapperMarker + entry.Marker + ShaderContextUsage.Marker(contextDependencies) + "Shader \"" + shaderName + (preview == null ? "" : "/Preview/" + preview.previewFunction) + "\"\n{\n" +
                (regional ? RegionalColorMarker : "") +
                "Properties {\n_MainTex (\"Input\", 2D) = \"white\" {}\n[HideInInspector] _WhimTex_Viewport (\"Viewport\", Vector) = (0,0,1,1)\n" +
                "[HideInInspector] _WhimTex_InputFilter (\"Input Filter\", Float) = 1\n" + properties + "}\n" +
                "SubShader { Cull Off ZWrite Off ZTest Always Blend Off\nPass {\nCGPROGRAM\n" +
                "#pragma vertex vert_img\n#pragma fragment SpriteFXFragment\n#pragma target 3.5\n" +
                ((contextDependencies & ShaderContextDependencies.Lighting) != 0 ?
                    "#pragma multi_compile_local __ WHIMTEX_AREA_LIGHTS\n#pragma multi_compile_local __ WHIMTEX_LIGHT_COOKIES\n" : "") +
                (preview == null ? "" : "#define WHIMTEX_PREVIEW 1\nfloat4 _WhimTex_PreviewRange;\n") +
                "#include \"UnityCG.cginc\"\n" + (noiseLibrary ?? NoiseLibraryInclude) + ColorLibraryInclude + "sampler2D _MainTex;\nfloat4 _MainTex_TexelSize;\n" +
                "float4 _WhimTex_Viewport;\nfloat4 _InputSize;\nfloat4 _CanvasSize;\nfloat _RenderScale;\nfloat _PixelScale;\n" + ShaderEvaluationContext.Declarations + uniforms +
                "float4 _WhimTex_LayerToLocalRow0, _WhimTex_LayerToLocalRow1, _WhimTex_LayerToLocalRow2;\n" +
                "float2 LayerToLocal(float2 uv) { float3 p = float3(uv, 1); float w = dot(_WhimTex_LayerToLocalRow2.xyz, p); w = abs(w) < 1e-8 ? (w < 0 ? -1e-8 : 1e-8) : w; return float2(dot(_WhimTex_LayerToLocalRow0.xyz, p), dot(_WhimTex_LayerToLocalRow1.xyz, p)) / w; }\n" +
                ShaderTextureViews.Types + textureViews +
                ShaderEntryPoint.PinnedSource +
                InputSamplingSource + ShaderEntryPoint.SpatialSamplingSource +
                "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/DocumentLighting.cginc\"\n" +
                (proceduralInput ? "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"\n" : "") +
                LineDirective(1, assetPath) + lowered + "\n#line 1 \"SpriteFXWrapper\"\n" +
                "float4 SpriteFXFragment(v2f_img input) : SV_Target { return " + fragment + "; }\n" +
                "ENDCG\n}\n}\nFallback Off\n}\n";
        }

        internal static void BuildParameterDeclarations(IReadOnlyList<ShaderParameter> parameters,
            StringBuilder properties, StringBuilder uniforms, HashSet<string> names, bool generatedNames = false)
        {
            foreach (ShaderParameter parameter in parameters)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name) || !Identifier.IsMatch(parameter.name) ||
                    parameter.name == "_WhimTexHueGradient" || parameter.name == "_WhimTexHueGradient_TexelSize" ||
                    parameter.name == "WhimTexHue" || parameter.name == "WhimTexHueLinear" ||
                    !generatedNames && parameter.name.StartsWith("_WhimTex_", StringComparison.Ordinal) || !names.Add(parameter.name))
                    throw new InvalidOperationException($"Invalid, duplicate or reserved parameter name: '{parameter?.name}'. Use an HLSL identifier such as _Amount.");
                string name = parameter.name;
                if (ShaderNumericValue.NeedsTypedBinding(parameter)) { ShaderNumericValue.Declare(parameter, properties, uniforms); continue; }
                switch (parameter.type)
                {
                    case ShaderParameterType.Float:
                    case ShaderParameterType.Seed:
                    case ShaderParameterType.Toggle:
                    case ShaderParameterType.Enum:
                        properties.AppendLine($"{name} (\"{name}\", Float) = 0");
                        uniforms.AppendLine($"float {name};");
                        break;
                    case ShaderParameterType.Color:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (1,1,1,1)");
                        uniforms.AppendLine($"float4 {name};");
                        break;
                    case ShaderParameterType.Vector2:
                    case ShaderParameterType.Point:
                    case ShaderParameterType.Vector3:
                    case ShaderParameterType.Normal:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (0,0,0,0)");
                        uniforms.AppendLine($"float{(parameter.type == ShaderParameterType.Vector2 || parameter.type == ShaderParameterType.Point ? 2 : 3)} {name};");
                        break;
                    case ShaderParameterType.Vector:
                        properties.AppendLine($"{name} (\"{name}\", Vector) = (0,0,0,0)");
                        uniforms.AppendLine($"float4 {name};");
                        break;
                    case ShaderParameterType.Texture2D:
                        if (!names.Add(name + "_TexelSize"))
                            throw new InvalidOperationException($"Reserved texture parameter: {name}_TexelSize.");
                        properties.AppendLine($"{name} (\"{name}\", 2D) = \"white\" {{}}");
                        uniforms.AppendLine($"sampler2D {name};\nfloat4 {name}_TexelSize;");
                        break;
                    case ShaderParameterType.Transform2D:
                        if (!names.Add(name + "_ToLocal") || !names.Add(name + "_ToInput"))
                            throw new InvalidOperationException("Transform helper name conflicts with another parameter: " + name);
                        string prefix = parameter.InternalPrefix;
                        foreach (string direction in new[] { "ToLocal", "ToInput" })
                        {
                            uniforms.AppendLine($"float4 {prefix}{direction}Row0;\nfloat4 {prefix}{direction}Row1;\nfloat4 {prefix}{direction}Row2;");
                            uniforms.AppendLine($"float2 {name}_{direction}(float2 uv) {{ float3 p = float3(uv, 1.0); float w = dot({prefix}{direction}Row2.xyz, p); w = w < 0.0 ? min(w, -1e-7) : max(w, 1e-7); return float2(dot({prefix}{direction}Row0.xyz, p), dot({prefix}{direction}Row1.xyz, p)) / w; }}");
                        }
                        break;
                    case ShaderParameterType.Curve:
                        if (!names.Add(name + "_Sample"))
                            throw new InvalidOperationException("Curve helper name conflicts with another parameter: " + name);
                        string curveTexture = parameter.InternalPrefix + "Curve";
                        properties.AppendLine($"{curveTexture} (\"{name}\", 2D) = \"white\" {{}}");
                        uniforms.AppendLine($"sampler2D {curveTexture};");
                        uniforms.AppendLine($"float {name}_Sample(float t) {{ return tex2Dlod({curveTexture}, float4((saturate(t) * 511.0 + 0.5) / 512.0, 0.5, 0.0, 0.0)).r; }}");
                        break;
                    case ShaderParameterType.Gradient:
                        if (!names.Add(name + "_Sample"))
                            throw new InvalidOperationException("Gradient helper name conflicts with another parameter: " + name);
                        string gradientTexture = parameter.InternalPrefix + "Gradient";
                        string gradientWrap = parameter.InternalPrefix + "GradientWrap";
                        properties.AppendLine($"{gradientTexture} (\"{name}\", 2D) = \"white\" {{}}");
                        properties.AppendLine($"{gradientWrap} (\"{name} Wrap Mode\", Float) = 0");
                        uniforms.AppendLine($"sampler2D {gradientTexture};");
                        uniforms.AppendLine($"float {gradientWrap};");
                        uniforms.AppendLine($"float4 {name}_Sample(float t) {{ if ({gradientWrap} < 0.5) t = saturate(t); else if ({gradientWrap} < 1.5) t = frac(t); else {{ t -= floor(t * 0.5) * 2.0; t = t <= 1.0 ? t : 2.0 - t; }} return tex2Dlod({gradientTexture}, float4((saturate(t) * 511.0 + 0.5) / 512.0, 0.5, 0.0, 0.0)); }}");
                        break;
                    default: throw new InvalidOperationException($"Unsupported parameter type: {parameter.type}.");
                }
            }
        }

        // Preserve dependencies across Save As without expanding their methods or
        // changing the editor's pending code. Only paths in the saved model are made project-relative.
        internal static string DocumentCode(string source, string sourcePath) =>
            string.IsNullOrEmpty(source) || !source.Contains("#") ? source : new ShaderSourceBuilder().ResolveIncludes(source, sourcePath);

        internal static bool HasRelativeIncludes(string source)
        {
            bool block = false;
            using var reader = new StringReader(source ?? "");
            while (ReadSourceLine(reader, out _, out string line, out _))
            {
                var match = Include.Match(MaskComments(line, ref block));
                if (!match.Success) continue;
                string path = match.Groups["path"].Value.Replace('\\', '/');
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private string ResolveIncludes(string source, string sourcePath, int maximumCharacters = MaximumCharacters)
        {
            if (source.Length > maximumCharacters)
                throw new InvalidOperationException("Shader FX source exceeds its character limit (" + maximumCharacters + ").");
            StringBuilder result = new StringBuilder();
            bool blockComment = false;
            using (StringReader reader = new StringReader(source))
            {
                int lineNumber = 0;
                while (ReadSourceLine(reader, out string original, out string logical, out int lines))
                {
                    int startLine = lineNumber + 1;
                    lineNumber += lines;
                    string line = original;
                    Match include = Include.Match(MaskComments(logical, ref blockComment));
                    if (include.Success)
                    {
                        try
                        {
                            string requested = include.Groups["path"].Value.Replace('\\', '/');
                            string resolved = Resolve(requested, sourcePath);
                            // User-library includes are expanded next. Keep their relative form:
                            // Resolve intentionally rejects absolute paths supplied in source.
                            if (!Path.IsPathRooted(resolved) && (requested.StartsWith("Assets/", StringComparison.Ordinal) ||
                                requested.StartsWith("Packages/", StringComparison.Ordinal) ||
                                requested.StartsWith(".", StringComparison.Ordinal) ||
                                File.Exists(PhysicalPath(resolved))))
                            {
                                Group pathGroup = include.Groups["path"];
                                line = logical.Substring(0, pathGroup.Index) + resolved +
                                    logical.Substring(pathGroup.Index + pathGroup.Length) + new string('\n', lines - 1);
                            }
                        }
                        catch (Exception exception)
                        {
                            throw new InvalidOperationException($"{sourcePath}:{startLine}: {exception.Message}", exception);
                        }
                    }
                    result.AppendLine(line);
                }
            }
            if (blockComment)
                throw new InvalidOperationException($"{sourcePath}: unterminated block comment.");
            return result.ToString();
        }

        internal static string ExportIncludes(string source, string sourcePath) => ExpandExportIncludes(source, sourcePath, false);

        private static string ExpandExportIncludes(string source, string sourcePath, bool lineDirectives, int maximumCharacters = MaximumCharacters)
        {
            var builder = new ShaderSourceBuilder();
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int characters = 0;
            string Expand(string text, string path, int depth)
            {
                if (depth > 32 || !active.Add(path)) throw new IOException("Cyclic or excessively nested shader includes cannot be exported.");
                characters += text.Length;
                if (characters > maximumCharacters) throw new IOException("Expanded HLSL exceeds its character limit (" + maximumCharacters + ").");
                var result = new StringBuilder();
                using var reader = new StringReader(text);
                bool block = false;
                int lineNumber = 0;
                while (ReadSourceLine(reader, out string original, out string line, out int lines))
                {
                    lineNumber += lines;
                    var include = Include.Match(MaskComments(line, ref block));
                    if (include.Success)
                    {
                        string requested = include.Groups["path"].Value;
                        string resolved = builder.Resolve(requested, path);
                        // These headers are already supplied by the wrapper. Expanding a
                        // redundant include would parse FNL's macro implementation as user
                        // PinnedUV code and unnecessarily embed it into exported presets.
                        if (resolved == "Packages/com.dcfapixels.whimtex/src/Shaders/WTNoise.hlsl" ||
                            resolved == "Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl")
                        {
                            result.Append('\n', lines);
                            continue;
                        }
                        string physical = builder.PhysicalPath(resolved);
                        if (File.Exists(physical))
                        {
                            if (new FileInfo(physical).Length > maximumCharacters) throw new IOException("Included HLSL exceeds its size limit (" + maximumCharacters + ").");
                            if (lineDirectives) result.Append(LineDirective(1, resolved));
                            result.AppendLine(Expand(File.ReadAllText(physical), resolved, depth + 1));
                            if (lineDirectives) result.Append(LineDirective(lineNumber + 1, path));
                            continue;
                        }
                        if (requested.StartsWith(".", StringComparison.Ordinal) || requested.StartsWith("Assets/", StringComparison.Ordinal) || requested.StartsWith("Packages/", StringComparison.Ordinal))
                            throw new IOException("Missing shader include: " + resolved);
                        // Engine includes (UnityCG.cginc etc.) remain engine dependencies.
                    }
                    result.AppendLine(original);
                }
                active.Remove(path);
                return result.ToString();
            }
            return Expand(source, sourcePath, 0);
        }

        // Line splicing precedes comment parsing in HLSL. Keep physical lines for output
        // and diagnostics, while every include consumer inspects the same logical line.
        private static bool ReadSourceLine(StringReader reader, out string original, out string logical, out int lines)
        {
            original = logical = reader.ReadLine();
            lines = original == null ? 0 : 1;
            if (original == null) return false;
            if (!original.EndsWith("\\", StringComparison.Ordinal)) return true;
            var physical = new StringBuilder(original);
            var joined = new StringBuilder(original);
            string line = original;
            while (line.EndsWith("\\", StringComparison.Ordinal))
            {
                line = reader.ReadLine();
                if (line == null) break;
                physical.Append('\n').Append(line);
                joined.Length--;
                joined.Append(line);
                lines++;
            }
            original = physical.ToString();
            logical = joined.ToString();
            return true;
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
                if (!PresetLibraryPaths.IsInside(userPath, ShaderPreparationEnvironment.Current?.presetFolder ?? ShaderPresetCatalog.Folder))
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
            if (ShaderPreparationEnvironment.Current is { } environment) return environment.PhysicalPath(assetPath);
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
