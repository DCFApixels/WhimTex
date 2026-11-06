using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class ShaderLocalityReason
    {
        public string code, message, file;
        public int line;
    }

    // Authoring contract validation, not a sandbox for arbitrary HLSL.
    internal readonly struct ShaderEntryPoint
    {
        internal const string WrapperMarker = "// WhimTex FX entry contract v3\n";
        internal const string PixelLocalMarker = "// WhimTex pixel-local v1\n";
        internal const string UnverifiedMarker = "// WhimTex pixel-local unverified v1\n";
        internal const string UnverifiedInfo = "Pixel locality is unverified. The source uses operations or dependencies outside the verified sampling contract, including qualified function calls. IsPixelLocal remains false and complete image inputs are used. This is an informational status, not a compilation failure.";
        internal enum Kind { Color, Pinned }
        internal readonly Kind kind;
        internal readonly bool verified;
        internal readonly ShaderLocalityReason[] reasons;
        readonly ShaderPinnedUV analysis;
        internal string preparedSource => analysis.Lowered;
        internal readonly bool spatial;

        ShaderEntryPoint(Kind kind, ShaderLocalityReason[] reasons, ShaderPinnedUV analysis)
        {
            this.kind = kind;
            this.reasons = reasons;
            this.analysis = analysis;
            spatial = analysis.spatialReads.Count > 0;
            verified = !spatial && reasons.Length == 0;
        }

        static readonly Regex Definition = new Regex(@"\G(\w+)\s+ApplyFX\s*\(([^()]*)\)\s*(?::[^;{}]+)?\{", RegexOptions.CultureInvariant);
        static readonly Regex Parameter = new Regex(@"^(?:(?:const|in)\s+)*(float2|float4|PinnedUV)\s+[A-Za-z_]\w*$", RegexOptions.CultureInvariant);
        static readonly Regex LineLocation = new Regex(@"^\s*#\s*line\s+(\d+)(?:\s+""([^""]+)"")?", RegexOptions.CultureInvariant);

        internal static ShaderEntryPoint Parse(string expanded) => ParseWithSource(expanded, null);

        internal static ShaderEntryPoint ParseWithSource(string expanded, string sourcePath) => ParseDocument(new ShaderSourceDocument(expanded), sourcePath);

        internal static ShaderEntryPoint ParseDocument(ShaderSourceDocument document, string sourcePath)
        {
            string expanded = document.Source, clean = document.Clean;
            var definitions = new List<Match>();
            var tokens = document.AllTokens;
            for (int i = 1; i < tokens.Count; i++)
                if (tokens[i].text == "ApplyFX")
                {
                    var match = Definition.Match(clean, tokens[i - 1].start);
                    if (match.Success) definitions.Add(match);
                }
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

            var analysis = new ShaderPinnedUV(document);

            var reasons = new List<ShaderLocalityReason>();
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
                        var directive = LineLocation.Match(ShaderSourceBuilder.MaskComments(text, ref block));
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
                reasons.Add(new ShaderLocalityReason { code = code, message = message, file = location.file, line = location.line });
            }
            foreach (int offset in analysis.spatialReads)
                Add("spatial_sample", "Image sampling uses numeric coordinates, explicit addressing or explicit LOD and requires complete input.", offset);
            foreach (int offset in analysis.uncheckedReads)
                Add("unverified_sampling", "Sampling through a macro, outside an analyzed function body, or through a qualified function call cannot be verified as pixel-local.", offset);

            // This reserved immutable hue LUT is not a document-image read. Mask it
            // without changing offsets or newlines used for diagnostic locations.
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i].text;
                string Next(int n) => i + n < tokens.Count ? tokens[i + n].text : "";
                bool direct = token.StartsWith("tex1D", StringComparison.Ordinal) || token.StartsWith("tex2D", StringComparison.Ordinal) ||
                    token.StartsWith("tex3D", StringComparison.Ordinal) || token.StartsWith("texCUBE", StringComparison.Ordinal);
                if (token == "tex2Dlod" && Next(1) == "(" && Next(2) == "_WhimTexHueGradient" && Next(3) == ",") continue;
                bool derivative = token.StartsWith("ddx", StringComparison.Ordinal) || token.StartsWith("ddy", StringComparison.Ordinal) || token.StartsWith("fwidth", StringComparison.Ordinal);
                bool dependency = token == "_ScreenParams" || token == "_ProjectionParams" || token.StartsWith("UNITY", StringComparison.Ordinal) ||
                    token.StartsWith("Unity", StringComparison.Ordinal) || token.IndexOf("Buffer", StringComparison.Ordinal) >= 0;
                int last = i;
                if (token == "." && (Next(1) == "Load" || Next(1).StartsWith("Sample", StringComparison.Ordinal) || Next(1).StartsWith("Gather", StringComparison.Ordinal)) && Next(2) == "(")
                { direct = true; last = i + 2; }
                if (token == "#" && (Next(1) == "pragma" || Next(1).StartsWith("include", StringComparison.Ordinal) ||
                    Next(1) == "#" && tokens[i].end == tokens[i + 1].start)) { dependency = true; last = i + 1; }
                if (!direct && !derivative && !dependency) continue;
                int offset = tokens[i].start;
                token = clean.Substring(offset, tokens[last].end - offset);
                if (direct) Add("direct_texture_read", "Direct texture access '" + token.Trim() + "' bypasses the PinnedUV sampling contract.", offset);
                else if (derivative) Add("derivative", "Derivative '" + token + "' depends on neighboring fragments.", offset);
                else Add("unverified_dependency", "Dependency '" + token.Trim() + "' is outside the verified pixel-local contract.", offset);
                i = last;
            }
            for (int i = 0; i + 1 < tokens.Count; i++)
                if (tokens[i].text == "." && (tokens[i + 1].text == "pixels" || tokens[i + 1].text == "uvMap"))
                    Add("texture_internals", "Access to texture-view internals bypasses the verified sampling contract.", tokens[i].start);
            return new ShaderEntryPoint(kind, reasons.ToArray(), analysis);
        }

        internal string Call(string uv, string color) => kind == Kind.Color ? "ApplyFX(" + color + ")" :
            "ApplyFX(_WhimTex_PinUV(" + uv + "), " + color + ")";

        internal string Marker => WrapperMarker + (verified ? PixelLocalMarker : spatial ? "" : UnverifiedMarker);

        internal static string Clean(string source) => new ShaderSourceDocument(source).Clean;

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
