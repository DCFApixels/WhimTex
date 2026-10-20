using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    // Authoring contract validation, not a sandbox for arbitrary HLSL.
    internal readonly struct ShaderFXEntryPoint
    {
        internal const string WrapperMarker = "// WhimTex FX entry contract v2\n";
        internal const string PixelLocalMarker = "// WhimTex pixel-local v1\n";
        internal const string UnverifiedMarker = "// WhimTex pixel-local unverified v1\n";
        internal const string BypassWarning = "Warning: Pixel-local declaration uses a bypass texture read, derivatives or an unverified compiler dependency. IsPixelLocal is false. Use SampleInput/SampleTexture with PinnedUV for pixel-local image reads.";
        internal enum Kind { Color, Pinned, Spatial }
        internal readonly Kind kind;
        internal readonly bool verified;
        internal bool Restricted => kind != Kind.Spatial;

        ShaderFXEntryPoint(Kind kind, bool verified) { this.kind = kind; this.verified = verified; }

        static readonly Regex Definition = new Regex(@"\b(\w+)\s+ApplyFX\s*\(([^()]*)\)\s*(?::[^;{}]+)?\{", RegexOptions.CultureInvariant);
        static readonly Regex Parameter = new Regex(@"^(?:(?:const|in)\s+)*(float2|float4|PinnedUV)\s+[A-Za-z_]\w*$", RegexOptions.CultureInvariant);
        static readonly Regex Bypass = new Regex(
            @"\b(tex(?:1D|2D|3D|CUBE)\w*|ddx\w*|ddy\w*|fwidth\w*|_ScreenParams|_ProjectionParams|UNITY\w*|Unity\w*|\w*Buffer\w*)\b|\.\s*(?:Load|Sample\w*|Gather\w*)\s*\(|##|#\s*(?:include\w*|pragma)\b",
            RegexOptions.CultureInvariant);

        internal static ShaderFXEntryPoint Parse(string expanded)
        {
            string clean = Clean(expanded);
            var definitions = Definition.Matches(clean);
            if (definitions.Count != 1 || definitions[0].Groups[1].Value != "float4")
                throw new FormatException("ApplyFX requires exactly one float4 definition: ApplyFX(float4 color), ApplyFX(PinnedUV uv, float4 color), or ApplyFX(float2 uv, float4 color).");
            string[] arguments = definitions[0].Groups[2].Value.Split(',');
            string Type(string argument)
            {
                var match = Parameter.Match(argument.Trim());
                return match.Success ? match.Groups[1].Value : null;
            }
            Kind kind;
            if (arguments.Length == 1 && Type(arguments[0]) == "float4") kind = Kind.Color;
            else if (arguments.Length == 2 && Type(arguments[1]) == "float4" && Type(arguments[0]) == "PinnedUV") kind = Kind.Pinned;
            else if (arguments.Length == 2 && Type(arguments[1]) == "float4" && Type(arguments[0]) == "float2") kind = Kind.Spatial;
            else throw new FormatException("Unsupported ApplyFX signature. Use float4 color alone, or PinnedUV/float2 uv followed by float4 color; out/inout and extra arguments are not supported.");

            // This reserved immutable hue LUT is not a document-image read.
            string checkedSource = Regex.Replace(clean, @"\btex2Dlod\s*\(\s*_WhimTexHueGradient\s*,", "WhimTexImmutableHueLookup(");
            bool textureInternals = Regex.IsMatch(checkedSource, @"\.\s*(?:pixels|uvMap)\b");
            return new ShaderFXEntryPoint(kind, kind != Kind.Spatial && !Bypass.IsMatch(checkedSource) && !textureInternals);
        }

        internal string Call(string uv, string color) => kind == Kind.Color ? "ApplyFX(" + color + ")" :
            "ApplyFX(" + (kind == Kind.Pinned ? "_WhimTex_PinUV(" + uv + ")" : uv) + ", " + color + ")";

        internal string Marker => WrapperMarker + (verified ? PixelLocalMarker : Restricted ? UnverifiedMarker : "");

        internal static string PreparePinnedUV(string source)
        {
            // Lower every author-declared PinnedUV input/local to a const struct.
            // HLSL rejects component writes and reassignment; validation below rejects numeric uses.
            source = source.Replace("\r\n", "\n");
            string clean = Clean(source);
            if (Regex.IsMatch(clean, @"\b(?:struct|class|typedef)\b[^;{}\n]*\bPinnedUV\b|^\s*#\s*define[^\n]*\bPinnedUV\b", RegexOptions.Multiline))
                throw new FormatException("PinnedUV is a reserved type and cannot be redefined or introduced through a macro.");
            if (Regex.IsMatch(clean, @"\bPinnedUV\s*\(|\(\s*(?:const\s+)?PinnedUV\s*\)"))
                throw new FormatException("PinnedUV cannot be constructed or cast from other values. Use the coordinate supplied to ApplyFX, or copy/pass an existing PinnedUV.");
            ValidatePinnedUses(clean);
            var declarations = Regex.Matches(clean, @"\bPinnedUV\s+([A-Za-z_]\w*)");
            var result = new StringBuilder(source);
            for (int i = declarations.Count - 1; i >= 0; i--)
            {
                var match = declarations[i];
                int next = match.Index + match.Length;
                while (next < clean.Length && char.IsWhiteSpace(clean[next])) next++;
                if (next < clean.Length && clean[next] == '(') continue; // Return type, not a writable value.
                string before = clean.Substring(0, match.Index);
                if (Regex.IsMatch(before, @"\b(?:out|inout)\s+(?:const\s+)?$"))
                    throw new FormatException("PinnedUV cannot be an out/inout parameter.");
                if (next < clean.Length && clean[next] == '=')
                {
                    next++;
                    while (next < clean.Length && char.IsWhiteSpace(clean[next])) next++;
                    if (next < clean.Length && clean[next] == '{')
                        throw new FormatException("PinnedUV cannot be created with a component initializer.");
                }
                if (!Regex.IsMatch(before, @"\bconst\s+(?:in\s+)?$")) result.Insert(match.Index, "const ");
            }
            return result.ToString();
        }

        sealed class Function
        {
            internal string type, name, arguments, body;
            internal int bodyStart;
        }

        static void ValidatePinnedUses(string source)
        {
            // HLSL permits some explicit struct/vector conversions. Check direct value
            // uses as well as const lowering, without treating reconstructed float2 fields as pinned.
            var functions = new List<Function>();
            var signature = new Regex(@"\b(\w+)\s+(\w+)\s*\(([^()]*)\)\s*(?::[^;{}]+)?\{", RegexOptions.CultureInvariant);
            int offset = 0;
            while (offset < source.Length)
            {
                var match = signature.Match(source, offset);
                if (!match.Success) break;
                int start = match.Index + match.Length, end = start, depth = 1;
                while (end < source.Length && depth > 0)
                { char c = source[end++]; if (c == '{') depth++; else if (c == '}') depth--; }
                if (depth != 0) throw new FormatException("Unclosed HLSL function body.");
                functions.Add(new Function { type = match.Groups[1].Value, name = match.Groups[2].Value,
                    arguments = match.Groups[3].Value, body = source.Substring(start, end - start - 1), bodyStart = start });
                offset = end;
            }
            foreach (var function in functions)
            {
                var pinned = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match p in Regex.Matches(function.arguments, @"\bPinnedUV\s+([A-Za-z_]\w*)")) pinned.Add(p.Groups[1].Value);
                var tokens = Regex.Matches(function.body, @"[A-Za-z_]\w*|[^\s]");
                var declarations = new HashSet<int>();
                var pairs = new Dictionary<int, int>();
                var stack = new Stack<int>();
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i].Value == "(") stack.Push(i);
                    else if (tokens[i].Value == ")" && stack.Count > 0)
                    { int open = stack.Pop(); pairs[open] = i; pairs[i] = open; }
                }
                for (int i = 0; i + 1 < tokens.Count; i++)
                {
                    if (tokens[i].Value != "PinnedUV" || !Regex.IsMatch(tokens[i + 1].Value, @"^[A-Za-z_]\w*$")) continue;
                    int nesting = 0;
                    pinned.Add(tokens[i + 1].Value); declarations.Add(i + 1);
                    for (int j = i + 2; j < tokens.Count; j++)
                    {
                        string token = tokens[j].Value;
                        if (token == ";" && nesting == 0) break;
                        if (token == "(" || token == "[" || token == "{") nesting++;
                        else if (token == ")" || token == "]" || token == "}") nesting--;
                        else if (token == "," && nesting == 0 && j + 1 < tokens.Count)
                        { pinned.Add(tokens[j + 1].Value); declarations.Add(j + 1); }
                    }
                }
                bool AcceptsPinned(string name, int argument, int count)
                {
                    if (name == "SampleInput") return argument == 0 && count == 1;
                    if (name == "SampleTexture") return argument == 1 && count == 2;
                    foreach (var candidate in functions)
                    {
                        if (candidate.name != name) continue;
                        string[] parameters = candidate.arguments.Split(',');
                        if (parameters.Length == count && argument < count &&
                            Regex.IsMatch(parameters[argument], @"\bPinnedUV\s+[A-Za-z_]\w*")) return true;
                    }
                    return false;
                }
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (!pinned.Contains(tokens[i].Value) || declarations.Contains(i) || (i > 0 && tokens[i - 1].Value == ".")) continue;
                    if (i + 2 < tokens.Count && tokens[i + 1].Value == "." && (tokens[i + 2].Value == "x" || tokens[i + 2].Value == "y")) continue;
                    int first = i, last = i;
                    while (first > 0 && last + 1 < tokens.Count && tokens[first - 1].Value == "(" &&
                        pairs.TryGetValue(first - 1, out int close) && close == last + 1 &&
                        (first < 2 || !Regex.IsMatch(tokens[first - 2].Value, @"^[A-Za-z_]\w*$") || tokens[first - 2].Value == "return"))
                    { first--; last++; }
                    string before = first > 0 ? tokens[first - 1].Value : "";
                    string after = last + 1 < tokens.Count ? tokens[last + 1].Value : "";
                    if (after == ";" && before == "return" && function.type == "PinnedUV") continue;
                    if ((after == ";" || after == ",") && before == "=" && first > 1 && pinned.Contains(tokens[first - 2].Value)) continue;
                    // A const assignment on the left still reaches HLSL's normal readonly diagnostic.
                    if (after == "=") continue;
                    if ((before == "(" || before == ",") && (after == ")" || after == ","))
                    {
                        int open = first - 1;
                        while (open >= 0 && tokens[open].Value != "(")
                        {
                            if (tokens[open].Value == ")" && pairs.TryGetValue(open, out int partner)) open = partner;
                            open--;
                        }
                        if (open > 0 && pairs.TryGetValue(open, out int end))
                        {
                            int argument = 0, count = 1;
                            for (int j = open + 1; j < end; j++)
                            {
                                if (tokens[j].Value == "(" && pairs.TryGetValue(j, out int nested)) { j = nested; continue; }
                                if (tokens[j].Value == ",") { count++; if (j < first) argument++; }
                            }
                            if (AcceptsPinned(tokens[open - 1].Value, argument, count)) continue;
                        }
                    }
                    int line = 1;
                    for (int c = 0; c < function.bodyStart + tokens[i].Index; c++) if (source[c] == '\n') line++;
                    throw new FormatException("Line " + line + ": PinnedUV does not support arithmetic or numeric conversions. Copy it, return it from a PinnedUV helper, or pass it to a PinnedUV parameter; read .x/.y for numeric calculations.");
                }
            }
        }

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
float4 SampleTexture(WhimTexTexture source, PinnedUV uv) { return tex2D(source, float2(uv.x, uv.y)); }
float4 SampleInput(PinnedUV uv) { return SampleTexture(_MainTex, uv); }
";
        internal const string SpatialSamplingSource = @"
float4 SampleTexture(WhimTexTexture source, float2 uv) { return tex2D(source, uv); }
";
    }
}
