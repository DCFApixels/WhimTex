using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        // Only compiler-owned statements pass here; user functions/includes retain their formatting.
        // Keep every newline and #line directive intact for node diagnostics.
        static void AppendIndentedGeneratedCode(StringBuilder target, string code)
        {
            int depth = 0;
            for (int start = 0; start < code.Length;)
            {
                int end = code.IndexOf('\n', start);
                if (end < 0) end = code.Length;
                int first = start;
                while (first < end && (code[first] == ' ' || code[first] == '\t')) first++;
                bool directive = first < end && code[first] == '#';
                if (!directive && first < end && code[first] != '\r')
                {
                    int closing = first, indent = depth;
                    while (closing < end)
                    {
                        char c = code[closing++];
                        if (c == '}') indent--;
                        else if (c != ' ' && c != '\t') break;
                    }
                    target.Append(' ', Math.Max(0, indent) * ShaderFX.CodeIndent.Length);
                    for (int i = first; i < end; i++)
                    {
                        if (code[i] == '{') depth++;
                        else if (code[i] == '}') depth--;
                    }
                }
                target.Append(code, first, end - first);
                if (end < code.Length) target.Append('\n');
                start = end + 1;
            }
        }

        string BuiltInSource(string statements, out bool blend)
        {
            // User macros/includes may construct helper names. Preserve their existing environment.
            if (functionCode.Length != 0)
            {
                blend = true;
                return Helpers + (usesProcedural ? ProceduralHelpers : "") + (usesColor ? ColorHelpers : "") +
                    (usesExtended ? ExtendedHelpers : "") + (usesSurface ? SurfaceHelpers : "") + (usesBlackbody ? BlackbodyHelpers : "");
            }
            return BuiltInLibrary.Instance.Emit(statements, samplingCode.ToString(), out blend);
        }

        // Index only our fixed helper blocks, once. Function bodies are emitted verbatim.
        private sealed class BuiltInLibrary
        {
            internal static readonly BuiltInLibrary Instance = new BuiltInLibrary();
            readonly string[] modules = { Helpers, ProceduralHelpers, ColorHelpers, ExtendedHelpers, SurfaceHelpers, BlackbodyHelpers };
            readonly List<Function> functions = new List<Function>();
            readonly Dictionary<string, List<int>> overloads = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            readonly string[] preambles;
            sealed class Function
            {
                internal int module, start, length;
                internal int[] dependencies;
            }

            BuiltInLibrary()
            {
                preambles = new string[modules.Length];
                var definition = new Regex(@"(?m)^[ \t]*(?:(?:float|half|uint|int|bool)[1-4]?|void|Graph\w+)[ \t]+(Graph\w+)[ \t]*\(", RegexOptions.CultureInvariant);
                for (int module = 0; module < modules.Length; module++)
                {
                    string source = modules[module], clean = ShaderEntryPoint.Clean(source);
                    var preamble = new StringBuilder();
                    int previous = 0;
                    foreach (Match match in definition.Matches(clean))
                    {
                        int body = clean.IndexOf('{', match.Index + match.Length), end = body + 1, depth = 1;
                        if (body < 0) throw new InvalidOperationException("Missing built-in graph helper body.");
                        while (end < clean.Length && depth > 0)
                        { char c = clean[end++]; if (c == '{') depth++; else if (c == '}') depth--; }
                        if (depth != 0) throw new InvalidOperationException("Unclosed built-in graph helper body.");
                        preamble.Append(source, previous, match.Index - previous); previous = end;
                        string name = match.Groups[1].Value;
                        if (!overloads.TryGetValue(name, out var group)) overloads.Add(name, group = new List<int>());
                        group.Add(functions.Count);
                        functions.Add(new Function { module = module, start = match.Index, length = end - match.Index });
                    }
                    preambles[module] = preamble.Append(source, previous, source.Length - previous).ToString();
                }
                var dependencies = new List<int>();
                foreach (var function in functions)
                {
                    dependencies.Clear();
                    References(modules[function.module], function.start, function.length, dependencies);
                    function.dependencies = dependencies.ToArray();
                }
            }

            void References(string source, int start, int length, List<int> found)
            {
                int end = start + length;
                for (int i = start; i < end;)
                {
                    if (!Identifier(source[i])) { i++; continue; }
                    int begin = i++;
                    while (i < end && Identifier(source[i])) i++;
                    if (i - begin < 6 || !source.AsSpan(begin, 5).SequenceEqual("Graph".AsSpan())) continue;
                    if (overloads.TryGetValue(source.Substring(begin, i - begin), out var group))
                        foreach (int index in group) if (!found.Contains(index)) found.Add(index);
                }
            }
            static bool Identifier(char c) => c == '_' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9';

            internal string Emit(string body, string sampling, out bool blend)
            {
                var selected = new bool[functions.Count];
                var pending = new List<int>();
                References(body, 0, body.Length, pending);
                References(sampling, 0, sampling.Length, pending);
                for (int i = 0; i < pending.Count; i++)
                {
                    int index = pending[i];
                    if (selected[index]) continue;
                    selected[index] = true;
                    foreach (int dependency in functions[index].dependencies)
                        if (!selected[dependency]) pending.Add(dependency);
                }
                blend = overloads.TryGetValue("GraphBlend", out var blends) && selected[blends[0]];
                var result = new StringBuilder();
                int lastModule = -1;
                for (int i = 0; i < functions.Count; i++)
                {
                    if (!selected[i]) continue;
                    var function = functions[i];
                    if (lastModule != function.module) { result.AppendLine(preambles[function.module]); lastModule = function.module; }
                    result.Append(modules[function.module], function.start, function.length).AppendLine();
                }
                return result.ToString();
            }
        }
    }
}
