using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    // This scans directives and lexical scopes; the shader compiler still evaluates every
    // conditional and expands every macro. In particular, macro names and ## are untouched.
    internal static class ShaderGraphModuleSource
    {
        internal readonly struct Prepared
        {
            internal readonly string Source;
            internal readonly string Cleanup;
            internal Prepared(string source, string cleanup) { Source = source; Cleanup = cleanup; }
        }

        const string OwnershipPrefix = "_WhimTexModuleOwn_";
        const int MaximumMacros = 4096;
        const int MaximumNesting = 256;

        sealed class Conditional
        {
            internal int entryMin, entryMax, endMin = int.MaxValue, endMax;
            internal bool hasElse;
            internal void Merge(int minimum, int maximum)
            { endMin = Math.Min(endMin, minimum); endMax = Math.Max(endMax, maximum); }
        }

        internal static Prepared Prepare(string source, string moduleName)
        {
            if (source == null || source.Length > ShaderGraphSources.MaximumArchiveBytes ||
                ShaderGraphSources.Utf8.GetByteCount(source) > ShaderGraphSources.MaximumArchiveBytes)
                throw new FormatException("Module source is missing or exceeds 32 MiB.");
            if (!Identifier(moduleName) || moduleName.Length > 128)
                throw new FormatException("Invalid HLSL module namespace.");

            var macros = new HashSet<string>(StringComparer.Ordinal);
            var macroOrder = new List<string>();
            var conditions = new Stack<Conditional>();
            var body = new StringBuilder();
            int bodyBytes = 0, position = 0, copied = 0, braceMin = 0, braceMax = 0;
            bool blockComment = false, scopeMacro = false, engineInclude = false;
            var logical = new StringBuilder();
            string Marker(string name) => OwnershipPrefix + moduleName + "_" + name;

            void Append(string text, int start, int length)
            {
                int bytes = ShaderGraphSources.Utf8.GetByteCount(text.AsSpan(start, length));
                if (bytes > ShaderGraphSources.MaximumArchiveBytes - bodyBytes)
                    throw new FormatException("Prepared HLSL module exceeds 32 MiB.");
                bodyBytes += bytes; body.Append(text, start, length);
            }
            void Text(string text) => Append(text, 0, text.Length);
            void Replace(int start, int end, string replacement)
            {
                Append(source, copied, start - copied);
                Text(replacement); copied = end;
            }

            while (position < source.Length)
            {
                int lineStart = position;
                logical.Clear();
                while (position < source.Length)
                {
                    int splice = SpliceLength(source, position);
                    if (splice > 0) { position += splice; continue; }
                    char c = source[position++];
                    if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && position < source.Length && source[position] == '\n') position++;
                        break;
                    }
                    logical.Append(c);
                }
                string clean = ShaderFXSourceBuilder.MaskComments(logical.ToString(), ref blockComment);
                int cursor = 0;
                Spaces(clean, ref cursor);
                if (cursor == clean.Length) continue;
                int tokenStart = cursor;
                if (clean[cursor] != '#')
                {
                    int markerEnd = cursor;
                    string token = Word(clean, ref markerEnd);
                    if (token == "UNITY_EXPORT_REFLECTION")
                    {
                        int end = markerEnd;
                        Spaces(clean, ref end);
                        if (end < clean.Length && clean[end] == ';') { end++; Spaces(clean, ref end); }
                        if (end != clean.Length)
                            throw new FormatException("UNITY_EXPORT_REFLECTION must be a standalone marker line for portable HLSL.");
                        Replace(OriginalOffset(source, lineStart, tokenStart),
                            OriginalOffset(source, lineStart, clean.TrimEnd().Length), " ");
                        continue;
                    }
                    Braces(clean, ref braceMin, ref braceMax);
                    continue;
                }
                cursor++;
                Spaces(clean, ref cursor);
                string command = Word(clean, ref cursor);
                Spaces(clean, ref cursor);

                if (command == "if" || command == "ifdef" || command == "ifndef")
                {
                    if (conditions.Count >= MaximumNesting) throw new FormatException("HLSL preprocessor nesting exceeds 256 levels.");
                    conditions.Push(new Conditional { entryMin = braceMin, entryMax = braceMax });
                }
                else if (command == "else" || command == "elif")
                {
                    if (conditions.Count == 0 || conditions.Peek().hasElse) throw new FormatException("Unmatched HLSL #" + command + ".");
                    var condition = conditions.Peek();
                    condition.Merge(braceMin, braceMax);
                    braceMin = condition.entryMin; braceMax = condition.entryMax;
                    condition.hasElse = command == "else";
                }
                else if (command == "endif")
                {
                    if (conditions.Count == 0) throw new FormatException("Unmatched HLSL #endif.");
                    var condition = conditions.Pop();
                    condition.Merge(braceMin, braceMax);
                    if (!condition.hasElse) condition.Merge(condition.entryMin, condition.entryMax);
                    braceMin = condition.endMin; braceMax = condition.endMax;
                }
                else if (command == "define" || command == "undef")
                {
                    string name = Word(clean, ref cursor);
                    if (name.Length == 0 || name.Length > 4096) throw new FormatException("Invalid HLSL macro name.");
                    if (name.StartsWith(OwnershipPrefix, StringComparison.Ordinal)) throw new FormatException("HLSL ownership marker names are reserved.");
                    if (command == "undef")
                    {
                        Spaces(clean, ref cursor);
                        if (cursor != clean.Length) throw new FormatException("Invalid HLSL #undef directive.");
                    }
                    else
                    {
                        int minimum = 0, maximum = 0;
                        // Balanced macro bodies (including do/while blocks) are safe here;
                        // scope-opening/closing macros make include scope unknowable.
                        bool escapes = CountBraces(clean, cursor, ref minimum, ref maximum, false);
                        scopeMacro |= escapes || minimum != 0 || maximum != 0;
                    }
                    if (macros.Add(name))
                    {
                        if (macros.Count > MaximumMacros) throw new FormatException("An HLSL module exceeds 4096 mutated macro names.");
                        macroOrder.Add(name);
                    }
                    string marker = Marker(name);
                    int start = OriginalOffset(source, lineStart, tokenStart);
                    int end = OriginalOffset(source, lineStart, clean.TrimEnd().Length);
                    string original = source.Substring(start, end - start);
                    Replace(start, end, "\n#if defined(" + name + ") && !defined(" + marker + ")\n" +
                        "#error WhimTex module " + moduleName + " conflicts with global macro " + name + "\n#else\n" +
                        original + "\n" + (command == "define" ? "#define " + marker + " 1\n" : "#undef " + marker + "\n") + "#endif\n");
                }
                else if (command == "include" || command == "include_with_pragmas")
                {
                    if (cursor >= clean.Length || clean[cursor] != '"' && clean[cursor] != '<')
                        throw new FormatException("Module preparation requires literal engine include names.");
                    char closing = clean[cursor++] == '"' ? '"' : '>';
                    int nameStart = cursor;
                    while (cursor < clean.Length && clean[cursor] != closing) cursor++;
                    if (cursor == clean.Length) throw new FormatException("Unterminated HLSL engine include.");
                    string requested = clean.Substring(nameStart, cursor - nameStart);
                    int includeEnd = ++cursor;
                    Spaces(clean, ref cursor);
                    if (cursor != clean.Length || !ShaderGraphSources.IsEngineInclude(requested))
                        throw new FormatException("Unexpanded or unsupported HLSL include: " + requested);
                    if (braceMin != 0 || braceMax != 0)
                        throw new FormatException("Engine includes must be at module top level, outside functions, types and nested namespaces: " + requested);
                    engineInclude = true;
                    int start = OriginalOffset(source, lineStart, tokenStart);
                    int end = OriginalOffset(source, lineStart, includeEnd);
                    if (requested == "ShaderApiReflectionSupport.hlsl") Replace(start, end, " ");
                    else Replace(start, end, "\n}\n" + source.Substring(start, end - start) + "\nnamespace " + moduleName + " {\n");
                }
                else if (command.StartsWith("include", StringComparison.Ordinal) || command == "import")
                    throw new FormatException("Unsupported HLSL include directive: " + command);
            }
            if (blockComment || conditions.Count != 0 || braceMin != 0 || braceMax != 0)
                throw new FormatException("Unbalanced HLSL comment, conditional or lexical scope in module " + moduleName + ".");
            if (scopeMacro && engineInclude)
                throw new FormatException("Engine include scope cannot be verified when a module defines scope-opening or scope-closing macros.");
            Append(source, copied, source.Length - copied);

            var prefix = new StringBuilder();
            prefix.Append("\n#ifdef ").Append(moduleName).Append("\n#error WhimTex module namespace conflicts with a global macro\n#endif\n");
            var cleanup = new StringBuilder();
            foreach (string name in macroOrder)
            {
                string marker = Marker(name);
                prefix.Append("#ifdef ").Append(marker).Append("\n#error WhimTex ownership marker conflicts with a global macro\n#endif\n");
                cleanup.Append("#ifdef ").Append(marker).Append("\n#undef ").Append(name).Append("\n#undef ").Append(marker).Append("\n#endif\n");
            }
            prefix.Append("namespace ").Append(moduleName).Append(" {\n");
            string prefixText = prefix.ToString(), cleanupText = cleanup.ToString();
            long total = bodyBytes + ShaderGraphSources.Utf8.GetByteCount(prefixText) + ShaderGraphSources.Utf8.GetByteCount(cleanupText) + 3L;
            if (total > ShaderGraphSources.MaximumArchiveBytes) throw new FormatException("Prepared HLSL module and cleanup exceed 32 MiB.");
            return new Prepared(prefixText + body + "\n}\n", cleanupText);
        }

        static bool Identifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            int position = 0;
            return Word(value, ref position).Length == value.Length;
        }
        static string Word(string value, ref int position)
        {
            int start = position;
            if (position >= value.Length || !Letter(value[position])) return "";
            position++;
            while (position < value.Length && (Letter(value[position]) || value[position] >= '0' && value[position] <= '9')) position++;
            return value.Substring(start, position - start);
        }
        static bool Letter(char value) => value == '_' || value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z';
        static void Spaces(string value, ref int position) { while (position < value.Length && char.IsWhiteSpace(value[position])) position++; }
        static int SpliceLength(string value, int position)
        {
            if (value[position] != '\\' || position + 1 >= value.Length) return 0;
            if (value[position + 1] == '\n') return 2;
            return value[position + 1] == '\r' ? position + 2 < value.Length && value[position + 2] == '\n' ? 3 : 2 : 0;
        }
        static int OriginalOffset(string value, int start, int offset)
        {
            int position = start, logical = 0;
            while (logical < offset)
            {
                int splice = SpliceLength(value, position);
                if (splice > 0) position += splice;
                else { position++; logical++; }
            }
            return position;
        }
        static void Braces(string clean, ref int minimum, ref int maximum)
        {
            CountBraces(clean, 0, ref minimum, ref maximum, true);
        }
        static bool CountBraces(string clean, int start, ref int minimum, ref int maximum, bool enforceLimits)
        {
            char quote = '\0';
            bool escapes = false;
            for (int i = start; i < clean.Length; i++)
            {
                char c = clean[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'') { quote = c; continue; }
                if (c == '{') { minimum++; maximum++; }
                else if (c == '}') { minimum--; maximum--; }
                escapes |= minimum < 0;
                if (enforceLimits && (minimum < 0 || maximum > MaximumNesting))
                    throw new FormatException("HLSL scope escapes its module or exceeds 256 levels.");
            }
            return escapes;
        }
    }
}
