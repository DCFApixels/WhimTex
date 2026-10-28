using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class ShaderFXLocalityReason
    {
        public string code, message, file;
        public int line;
    }

    // Authoring contract validation, not a sandbox for arbitrary HLSL.
    internal readonly struct ShaderFXEntryPoint
    {
        internal const string WrapperMarker = "// WhimTex FX entry contract v3\n";
        internal const string PixelLocalMarker = "// WhimTex pixel-local v1\n";
        internal const string UnverifiedMarker = "// WhimTex pixel-local unverified v1\n";
        internal const string BypassWarning = "Warning: Pixel locality could not be verified because of direct texture access, derivatives or an unverified dependency. IsPixelLocal is false; the effect uses complete image inputs. This is expected for effects that inspect neighboring pixels and does not indicate a compilation failure.";
        internal enum Kind { Color, Pinned }
        internal readonly Kind kind;
        internal readonly bool verified;
        internal readonly ShaderFXLocalityReason[] reasons;
        internal readonly string preparedSource;
        internal readonly bool spatial;

        ShaderFXEntryPoint(Kind kind, ShaderFXLocalityReason[] reasons, ShaderFXPinnedUV analysis)
        {
            this.kind = kind;
            this.reasons = reasons;
            preparedSource = analysis.Lowered;
            spatial = analysis.spatialReads.Count > 0;
            verified = !spatial && reasons.Length == 0;
        }

        static readonly Regex Definition = new Regex(@"\b(\w+)\s+ApplyFX\s*\(([^()]*)\)\s*(?::[^;{}]+)?\{", RegexOptions.CultureInvariant);
        static readonly Regex Parameter = new Regex(@"^(?:(?:const|in)\s+)*(float2|float4|PinnedUV)\s+[A-Za-z_]\w*$", RegexOptions.CultureInvariant);
        static readonly Regex LineLocation = new Regex(@"^\s*#\s*line\s+(\d+)(?:\s+""([^""]+)"")?", RegexOptions.CultureInvariant);
        static readonly Regex Bypass = new Regex(
            @"\b(tex(?:1D|2D|3D|CUBE)\w*|ddx\w*|ddy\w*|fwidth\w*|_ScreenParams|_ProjectionParams|UNITY\w*|Unity\w*|\w*Buffer\w*)\b|\.\s*(?:Load|Sample\w*|Gather\w*)\s*\(|##|#\s*(?:include\w*|pragma)\b",
            RegexOptions.CultureInvariant);

        internal static ShaderFXEntryPoint Parse(string expanded) => ParseWithSource(expanded, null);

        internal static ShaderFXEntryPoint ParseWithSource(string expanded, string sourcePath)
        {
            string clean = Clean(expanded);
            var definitions = Definition.Matches(clean);
            if (definitions.Count != 1 || definitions[0].Groups[1].Value != "float4")
                throw new FormatException("ApplyFX requires exactly one float4 definition: ApplyFX(float4 color) or ApplyFX(PinnedUV uv, float4 color).");
            string[] arguments = definitions[0].Groups[2].Value.Split(',');
            string Type(string argument)
            {
                var match = Parameter.Match(argument.Trim());
                return match.Success ? match.Groups[1].Value : null;
            }
            Kind kind;
            if (arguments.Length == 1 && Type(arguments[0]) == "float4") kind = Kind.Color;
            else if (arguments.Length == 2 && Type(arguments[1]) == "float4" && Type(arguments[0]) == "PinnedUV") kind = Kind.Pinned;
            else throw new FormatException("Unsupported ApplyFX signature. Use float4 color alone, or PinnedUV uv followed by float4 color. The float2 entry point is no longer supported; use a float2 local for editable coordinates.");

            var analysis = new ShaderFXPinnedUV(expanded);

            var reasons = new List<ShaderFXLocalityReason>();
            List<int> lineOffsets = null;
            List<(string file, int line)> locations = null;
            void Add(string code, string message, int offset)
            {
                // Expanded includes carry #line directives. Keep locations tied to the
                // successfully built source, never to a later draft or changed include.
                if (lineOffsets == null)
                {
                    lineOffsets = new List<int>();
                    locations = new List<(string file, int line)>();
                    int start = 0, number = 1;
                    string file = sourcePath;
                    bool block = false;
                    foreach (string text in expanded.Split('\n'))
                    {
                        lineOffsets.Add(start);
                        locations.Add((file, number));
                        var directive = LineLocation.Match(ShaderFXSourceBuilder.MaskComments(text, ref block));
                        if (directive.Success && int.TryParse(directive.Groups[1].Value, out int mapped))
                        {
                            if (directive.Groups[2].Success) file = directive.Groups[2].Value;
                            number = mapped;
                        }
                        else number++;
                        start += text.Length + 1;
                    }
                }
                int index = lineOffsets.BinarySearch(offset);
                if (index < 0) index = ~index - 1;
                var location = locations[index];
                reasons.Add(new ShaderFXLocalityReason { code = code, message = message, file = location.file, line = location.line });
            }
            foreach (int offset in analysis.spatialReads)
                Add("spatial_sample", "Image sampling uses numeric coordinates, explicit addressing or explicit LOD and requires complete input.", offset);
            foreach (int offset in analysis.uncheckedReads)
                Add("unverified_sampling", "Sampling outside a typed function body or through a macro cannot be verified as pixel-local.", offset);

            // This reserved immutable hue LUT is not a document-image read. Mask it
            // without changing offsets or newlines used for diagnostic locations.
            string checkedSource = Regex.Replace(clean, @"\btex2Dlod\s*\(\s*_WhimTexHueGradient\s*,",
                match => Regex.Replace(match.Value, @"[^\r\n]", " "));
            foreach (Match match in Bypass.Matches(checkedSource))
            {
                string token = match.Value;
                if (token.StartsWith("tex", StringComparison.Ordinal) || token.StartsWith(".", StringComparison.Ordinal))
                    Add("direct_texture_read", "Direct texture access '" + token.Trim() + "' bypasses the PinnedUV sampling contract.", match.Index);
                else if (token.StartsWith("ddx", StringComparison.Ordinal) || token.StartsWith("ddy", StringComparison.Ordinal) || token.StartsWith("fwidth", StringComparison.Ordinal))
                    Add("derivative", "Derivative '" + token + "' depends on neighboring fragments.", match.Index);
                else
                    Add("unverified_dependency", "Dependency '" + token.Trim() + "' is outside the verified pixel-local contract.", match.Index);
            }
            foreach (Match match in Regex.Matches(checkedSource, @"\.\s*(?:pixels|uvMap)\b"))
                Add("texture_internals", "Access to texture-view internals bypasses the verified sampling contract.", match.Index);
            return new ShaderFXEntryPoint(kind, reasons.ToArray(), analysis);
        }

        internal string Call(string uv, string color) => kind == Kind.Color ? "ApplyFX(" + color + ")" :
            "ApplyFX(_WhimTex_PinUV(" + uv + "), " + color + ")";

        internal string Marker => WrapperMarker + (verified ? PixelLocalMarker : spatial ? "" : UnverifiedMarker);

        internal static string Clean(string source)
        {
            bool block = false;
            var result = new StringBuilder(source.Length);
            // Preserve source offsets for lowering and line numbers for compiler diagnostics.
            foreach (string line in source.Split('\n')) result.Append(ShaderFXSourceBuilder.MaskComments(line, ref block)).Append('\n');
            return Regex.Replace(result.ToString(), "\"(?:\\\\.|[^\"\\\\])*\"", match => new string(' ', match.Length));
        }

        internal const string PinnedSource = @"
struct PinnedUV { float x; float y; };
PinnedUV _WhimTex_PinUV(float2 uv)
{
    PinnedUV value; value.x = uv.x; value.y = uv.y; return value;
}
float2 _WhimTex_Unpin(PinnedUV uv) { return float2(uv.x, uv.y); }
float4 SampleTexture(WhimTexTexture source, PinnedUV uv) { return tex2D(source, float2(uv.x, uv.y)); }
float4 SampleInput(PinnedUV uv) { return SampleTexture(_MainTex, uv); }
";
        internal const string SpatialSamplingSource = @"
float4 SampleTexture(WhimTexTexture source, float2 uv) { return tex2D(source, uv); }
";
    }
}
