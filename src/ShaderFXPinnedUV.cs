using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    // A narrow authoring-language pass. HLSL still checks numeric types and generates
    // GPU code; this pass owns only immutable coordinate provenance and conversions.
    internal sealed class ShaderFXPinnedUV
    {
        const string Pinned = "PinnedUV";
        readonly string source;
        readonly List<Token> tokens = new List<Token>();
        readonly Dictionary<int, int> pairs = new Dictionary<int, int>();
        readonly List<Function> functions = new List<Function>();
        readonly Dictionary<int, string> before = new Dictionary<int, string>();
        readonly Dictionary<int, string> after = new Dictionary<int, string>();
        readonly HashSet<(int, int)> conversions = new HashSet<(int, int)>();
        readonly HashSet<int> consumedPinned = new HashSet<int>();
        readonly HashSet<string> macros = new HashSet<string>(StringComparer.Ordinal);
        readonly Stack<Dictionary<string, string>> scopes = new Stack<Dictionary<string, string>>();
        Function current;
        int cursor, limit, depth;
        internal readonly List<int> spatialReads = new List<int>();
        internal readonly List<int> uncheckedReads = new List<int>();
        internal string Lowered { get; private set; }

        readonly struct Token
        {
            internal readonly string text;
            internal readonly int start, end;
            internal Token(Match match) { text = match.Value; start = match.Index; end = match.Index + match.Length; }
        }
        sealed class Function
        {
            internal string name, type;
            internal int start, end;
            internal readonly List<Parameter> parameters = new List<Parameter>();
            internal readonly List<int> reads = new List<int>();
            internal readonly List<int> unverifiedReads = new List<int>();
            internal readonly HashSet<Function> calls = new HashSet<Function>();
        }
        sealed class Parameter { internal string name, type; internal bool output; }
        readonly struct Value
        {
            internal readonly string type;
            internal readonly int start, end;
            internal readonly bool writable;
            internal Value(string type, int start, int end, bool writable = false)
            { this.type = type; this.start = start; this.end = end; this.writable = writable; }
        }
        static readonly Regex Lex = new Regex(@"[A-Za-z_]\w*|0[xX][0-9a-fA-F]+[uUlL]*|(?:\d*\.\d+|\d+\.?\d*)(?:[eE][+-]?\d+)?[fFuU]?|<<=|>>=|\+\+|--|\+=|-=|\*=|/=|%=|==|!=|<=|>=|&&|\|\||<<|>>|&=|\|=|\^=|[^\s]", RegexOptions.CultureInvariant);
        static bool Identifier(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_');
        static bool Numeric(string type) => Regex.IsMatch(type, @"^(?:float|half|double|int|uint|bool)(?:[1-4](?:x[1-4])?)?$");
        static bool Qualifier(string text) => text == "const" || text == "in" || text == "out" || text == "inout" || text == "static" || text == "uniform" || text == "inline" || text == "precise";

        internal ShaderFXPinnedUV(string source)
        {
            this.source = source;
            string clean = ShaderFXEntryPoint.Clean(source);
            // Macro expansion is left to HLSL. Never allow a macro to create a pinned
            // declaration, replace the sampling API, or hide a writable pinned alias.
            const string directivePattern = @"(?m)^[\t ]*#[^\r\n]*(?:\r?\n(?<=\\\r?\n)[^\r\n]*)*";
            // Join physical continuation lines before inspecting a macro. Preserve
            // original offsets in the text used by the lexer and diagnostics.
            foreach (Match directive in Regex.Matches(clean, directivePattern))
            {
                var macro = Regex.Match(directive.Value, @"^[\t ]*#\s*define\s+(\w+)");
                if (!macro.Success) continue;
                string text = directive.Value, name = macro.Groups[1].Value;
                macros.Add(name);
                if (Regex.IsMatch(text, @"\bPinnedUV\b") || name == "SampleInput" || name == "SampleTexture")
                    throw Error(directive.Index, "PinnedUV and the sampling API cannot be defined or aliased through macros.");
                if (Regex.IsMatch(text, @"\b(?:SampleInput|SampleTexture)\b")) uncheckedReads.Add(directive.Index);
            }
            clean = Regex.Replace(clean, directivePattern, m => Regex.Replace(m.Value, @"[^\r\n]", " "));
            foreach (Match match in Lex.Matches(clean)) tokens.Add(new Token(match));
            var stack = new Stack<int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i].text;
                if (t == "(" || t == "[" || t == "{") stack.Push(i);
                else if (t == ")" || t == "]" || t == "}")
                {
                    if (stack.Count == 0) throw Error(tokens[i].start, "Unmatched HLSL delimiter.");
                    int open = stack.Pop();
                    if (tokens[open].text + t != "()" && tokens[open].text + t != "[]" && tokens[open].text + t != "{}")
                        throw Error(tokens[i].start, "Mismatched HLSL delimiters.");
                    pairs[open] = i; pairs[i] = open;
                }
            }
            if (stack.Count != 0) throw Error(tokens[stack.Peek()].start, "Unclosed HLSL delimiter.");
            DiscoverFunctions();
            foreach (var function in functions) Analyze(function);
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].text == Pinned && !consumedPinned.Contains(i))
                    throw Error(tokens[i].start, "PinnedUV is allowed only as an immutable function input, return type or initialized local. It cannot be constructed, cast, aliased, stored in arrays or struct fields.");
            var visited = new HashSet<Function>();
            void Visit(Function f)
            {
                if (!visited.Add(f)) return;
                spatialReads.AddRange(f.reads);
                uncheckedReads.AddRange(f.unverifiedReads);
                foreach (var called in f.calls) Visit(called);
            }
            foreach (var f in functions) if (f.name == "ApplyFX") Visit(f);
            spatialReads.Sort();
            var result = new StringBuilder(source.Length + before.Count * 24);
            for (int i = 0; i <= source.Length; i++)
            {
                if (after.TryGetValue(i, out string suffix)) result.Append(suffix);
                if (before.TryGetValue(i, out string prefix)) result.Append(prefix);
                if (i < source.Length) result.Append(source[i]);
            }
            Lowered = result.ToString();
        }

        void DiscoverFunctions()
        {
            for (int i = 0; i + 3 < tokens.Count; i++)
            {
                if (Identifier(tokens[i].text) && Identifier(tokens[i + 1].text) && tokens[i + 2].text == "(")
                {
                    int close = pairs[i + 2], body = close + 1;
                    if (body < tokens.Count && tokens[body].text == ":")
                        while (body < tokens.Count && tokens[body].text != "{" && tokens[body].text != ";") body++;
                    if (body < tokens.Count && tokens[body].text == "{")
                    {
                        var f = new Function { type = tokens[i].text, name = tokens[i + 1].text, start = body, end = pairs[body] };
                        if (f.name == "SampleInput" || f.name == "SampleTexture") throw Error(tokens[i].start, "Sampling functions are reserved.");
                        if (f.type == Pinned) consumedPinned.Add(i);
                        int p = i + 3;
                        while (p < close)
                        {
                            bool output = false;
                            while (p < close && Qualifier(tokens[p].text)) { output |= tokens[p].text == "out" || tokens[p].text == "inout"; p++; }
                            int typeIndex = p;
                            string type = tokens[p++].text;
                            if (type == "void" && p == close) break;
                            if (p >= close || !Identifier(tokens[p].text)) throw Error(tokens[typeIndex].start, "Expected a named function parameter.");
                            string name = tokens[p++].text;
                            if (type == Pinned)
                            {
                                if (output) throw Error(tokens[typeIndex].start, "PinnedUV cannot be an out/inout parameter.");
                                consumedPinned.Add(typeIndex); Const(typeIndex);
                            }
                            f.parameters.Add(new Parameter { name = name, type = type, output = output });
                            while (p < close && tokens[p].text != ",")
                            {
                                if (type == Pinned) throw Error(tokens[p].start, "PinnedUV parameters cannot have arrays, defaults or semantics.");
                                if (pairs.TryGetValue(p, out int end) && end > p) p = end;
                                p++;
                            }
                            if (p < close) p++;
                        }
                        functions.Add(f); i = f.end; continue;
                    }
                }
                if (tokens[i].text == "{") i = pairs[i]; // Global structs/initializers are checked by HLSL.
                else if (tokens[i].text == "SampleInput" || tokens[i].text == "SampleTexture") uncheckedReads.Add(tokens[i].start);
            }
        }

        void Const(int index)
        {
            for (int i = index - 1; i >= 0 && Qualifier(tokens[i].text); i--) if (tokens[i].text == "const") return;
            before[tokens[index].start] = "const ";
        }
        void Analyze(Function function)
        {
            current = function; cursor = function.start; limit = function.end + 1;
            scopes.Clear(); scopes.Push(new Dictionary<string, string>(StringComparer.Ordinal));
            foreach (var p in function.parameters) scopes.Peek()[p.name] = p.type;
            Statement();
        }
        string Peek(int next = 0) => cursor + next < limit ? tokens[cursor + next].text : "";
        bool Take(string text) { if (Peek() != text) return false; cursor++; return true; }
        void Need(string text) { if (!Take(text)) throw Error(Offset(), "Expected '" + text + "' while reading HLSL coordinates."); }
        int Offset() => cursor < tokens.Count ? tokens[cursor].start : source.Length;
        string Lookup(string name) { foreach (var scope in scopes) if (scope.TryGetValue(name, out string type)) return type; return "?"; }
        void Scope(Action body) { scopes.Push(new Dictionary<string, string>(StringComparer.Ordinal)); try { body(); } finally { scopes.Pop(); } }

        void Statement()
        {
            if (++depth > 256) throw Error(Offset(), "HLSL coordinate analysis nesting exceeds 256.");
            try
            {
                if (Take(";")) return;
                if (Take("{")) { Scope(() => { while (cursor < limit && Peek() != "}") Statement(); Need("}"); }); return; }
                if (Take("[")) { cursor = pairs[cursor - 1] + 1; Statement(); return; }
                if (Take("if")) { Need("("); Expression(0); Need(")"); Scope(Statement); if (Take("else")) Scope(Statement); return; }
                if (Take("for"))
                {
                    Scope(() => { Need("("); Clause(";"); Clause(";"); if (Peek() != ")") Expression(0); Need(")"); Statement(); }); return;
                }
                if (Take("while") || Take("switch")) { Need("("); Expression(0); Need(")"); Scope(Statement); return; }
                if (Take("do")) { Scope(Statement); Need("while"); Need("("); Expression(0); Need(")"); Need(";"); return; }
                if (Take("case")) { Expression(1); Need(":"); return; }
                if (Take("default")) { Need(":"); return; }
                if (Take("break") || Take("continue") || Take("discard")) { Need(";"); return; }
                if (Take("return"))
                {
                    if (Peek() != ";") Assign(current.type, Expression(0));
                    else if (current.type == Pinned) throw Error(Offset(), "PinnedUV helpers must return an existing pinned coordinate.");
                    Need(";"); return;
                }
                Clause(";");
            }
            finally { depth--; }
        }
        void Clause(string end)
        {
            if (Take(end)) return;
            int typeIndex = cursor;
            while (Qualifier(tokens[typeIndex].text)) typeIndex++;
            bool declaration = typeIndex + 1 < limit && Identifier(tokens[typeIndex].text) && Identifier(tokens[typeIndex + 1].text);
            if (!declaration) { Expression(0); Need(end); return; }
            string type = tokens[typeIndex].text;
            if (type == Pinned) { consumedPinned.Add(typeIndex); Const(typeIndex); }
            cursor = typeIndex + 1;
            do
            {
                string name = Peek();
                if (!Identifier(name)) throw Error(Offset(), "Expected a local variable name.");
                cursor++;
                if (type == Pinned && Peek() == "[") throw Error(Offset(), "PinnedUV arrays are not supported; keep immutable local coordinates.");
                while (Take("[")) { if (Peek() != "]") Expression(0); Need("]"); }
                // Declare after its initializer: an uninitialized pinned name cannot certify itself.
                if (Take("=")) Assign(type, Expression(1));
                else if (type == Pinned) throw Error(Offset(), "PinnedUV locals require an existing pinned initializer.");
                if (scopes.Peek().TryGetValue(name, out string previous) && (previous == Pinned) != (type == Pinned))
                    current.unverifiedReads.Add(tokens[typeIndex].start); // Potential preprocessor alternatives.
                scopes.Peek()[name] = type;
            } while (Take(","));
            Need(end);
        }
        void Assign(string type, Value value)
        {
            if (type == Pinned && value.type != Pinned) throw Error(value.start, "Cannot convert a numeric or unknown value to PinnedUV. Copy the supplied coordinate instead.");
            if (type != Pinned) Unpin(value);
        }
        void Unpin(Value value)
        {
            if (value.type != Pinned || !conversions.Add((value.start, value.end))) return;
            before.TryGetValue(value.start, out string prefix); before[value.start] = prefix + "_WhimTex_Unpin(";
            after.TryGetValue(value.end, out string suffix); after[value.end] = ")" + suffix;
        }
        static int Precedence(string op)
        {
            switch (op)
            {
                case ",": return 0;
                case "=": case "+=": case "-=": case "*=": case "/=": case "%=": case "&=": case "|=": case "^=": case "<<=": case ">>=": return 1;
                case "?": return 2;
                case "||": return 3; case "&&": return 4; case "|": return 5; case "^": return 6; case "&": return 7;
                case "==": case "!=": return 8; case "<": case ">": case "<=": case ">=": return 9;
                case "<<": case ">>": return 10; case "+": case "-": return 11; case "*": case "/": case "%": return 12;
                default: return -1;
            }
        }
        Value Expression(int minimum)
        {
            if (++depth > 256) throw Error(Offset(), "HLSL coordinate analysis nesting exceeds 256.");
            try
            {
                Value left = Primary();
                while (cursor < limit)
                {
                    string op = Peek(); int priority = Precedence(op);
                    if (priority < minimum) break;
                    cursor++;
                    if (op == "?")
                    {
                        Unpin(left);
                        Value yes = Expression(0); Need(":"); Value no = Expression(priority);
                        bool pinned = yes.type == Pinned && no.type == Pinned;
                        if (!pinned) { Unpin(yes); Unpin(no); }
                        left = new Value(pinned ? Pinned : NumericResult(yes, no), left.start, no.end); continue;
                    }
                    Value right = Expression(priority == 1 ? priority : priority + 1);
                    if (priority == 1)
                    {
                        if (!left.writable || left.type == Pinned) throw Error(left.start, "PinnedUV and its components are immutable; use a float2 variable for edits.");
                        Unpin(right); left = new Value(left.type, left.start, right.end); continue;
                    }
                    if (op == ",") { Unpin(left); left = new Value(right.type, left.start, right.end); continue; }
                    Unpin(left); Unpin(right);
                    left = new Value(NumericResult(left, right), left.start, right.end);
                }
                return left;
            }
            finally { depth--; }
        }
        static string NumericResult(Value a, Value b) => a.type == Pinned || b.type == Pinned ? "float2" : a.type == b.type ? a.type : a.type == "float" ? b.type : b.type == "float" ? a.type : "?";

        Value Primary()
        {
            int start = Offset();
            string token = Peek(); cursor++;
            Value value;
            if (token == "+" || token == "-" || token == "!" || token == "~" || token == "++" || token == "--")
            {
                value = Primary();
                if ((token == "++" || token == "--") && (!value.writable || value.type == Pinned)) throw Error(start, "PinnedUV cannot be modified.");
                Unpin(value); return new Value(value.type == Pinned ? "float2" : value.type, start, value.end);
            }
            if (token == "(")
            {
                if ((Numeric(Peek()) || Peek() == Pinned) && Peek(1) == ")")
                {
                    string type = Peek();
                    if (type == Pinned) throw Error(start, "Numeric values cannot be cast to PinnedUV.");
                    cursor += 2; var operand = Primary(); Unpin(operand); return new Value(type, start, operand.end);
                }
                value = Expression(0); Need(")"); value = new Value(value.type, start, tokens[cursor - 1].end, value.writable);
            }
            else if (token == "{")
            {
                while (Peek() != "}" && cursor < limit) { var item = Expression(1); Unpin(item); if (!Take(",")) break; }
                Need("}"); value = new Value("?", start, tokens[cursor - 1].end);
            }
            else if (Identifier(token))
            {
                if (token == Pinned) throw Error(start, "PinnedUV cannot be constructed or cast from components.");
                if (Take("(")) value = Call(token, start);
                else value = new Value(Lookup(token), start, tokens[cursor - 1].end, Lookup(token) != Pinned);
            }
            else value = new Value("float", start, tokens[cursor - 1].end);
            while (cursor < limit)
            {
                if (Take("."))
                {
                    Unpin(value); bool writable = value.writable && value.type != Pinned;
                    string member = Peek(); cursor++;
                    if (Take("("))
                    {
                        var call = Call(member, start); value = new Value(call.type, start, call.end); continue;
                    }
                    string type = Regex.IsMatch(member, @"^[xyzwrgba]{1,4}$") ? "float" + (member.Length == 1 ? "" : member.Length.ToString()) : "?";
                    value = new Value(type, start, tokens[cursor - 1].end, writable); continue;
                }
                if (Take("["))
                {
                    Unpin(value); var index = Expression(0); Unpin(index); Need("]");
                    value = new Value("?", start, tokens[cursor - 1].end, value.writable && value.type != Pinned); continue;
                }
                if (Take("++") || Take("--"))
                {
                    if (!value.writable || value.type == Pinned) throw Error(start, "PinnedUV cannot be modified.");
                    value = new Value(value.type, start, tokens[cursor - 1].end); continue;
                }
                break;
            }
            return value;
        }
        Value Call(string name, int start)
        {
            var arguments = new List<Value>();
            if (Peek() != ")") do { arguments.Add(Expression(1)); } while (Take(","));
            Need(")"); int end = tokens[cursor - 1].end;
            if (macros.Contains(name)) current.unverifiedReads.Add(start);
            if (name == "SampleInput" || name == "SampleTexture")
            {
                int uv = name == "SampleInput" ? 0 : 1;
                bool pinned = arguments.Count == uv + 1 && arguments[uv].type == Pinned;
                if (!pinned) current.reads.Add(start);
                for (int i = 0; i < arguments.Count; i++) if (!pinned || i != uv) Unpin(arguments[i]);
                return new Value("float4", start, end);
            }
            var candidates = new List<Function>(); int best = int.MaxValue; bool known = false;
            foreach (var f in functions)
            {
                if (f.name != name) continue;
                known = true;
                if (f.parameters.Count != arguments.Count) continue;
                int score = 0; bool matches = true;
                for (int i = 0; i < arguments.Count; i++)
                {
                    string expected = f.parameters[i].type, actual = arguments[i].type;
                    if (expected == Pinned && actual != Pinned) { matches = false; break; }
                    // We own only pinned provenance, not HLSL numeric overload ranking.
                    // Retain every numeric candidate: a bool/vector conversion must not
                    // hide a spatial overload from the reachable-call graph.
                    if (actual == Pinned && expected != Pinned) score++;
                }
                if (!matches || score > best) continue;
                if (score < best) { best = score; candidates.Clear(); }
                candidates.Add(f);
            }
            if (known && candidates.Count == 0) throw Error(start, "No compatible overload for '" + name + "'. Numeric coordinates cannot be passed as PinnedUV.");
            string result = Numeric(name) ? name : "?";
            if (candidates.Count > 0)
            {
                result = candidates[0].type;
                foreach (var f in candidates)
                {
                    if ((f.type == Pinned) != (result == Pinned)) throw Error(start, "Ambiguous pinned/numeric return type. Make the overload arguments explicit.");
                    if (f.type != result) result = "?";
                    current.calls.Add(f);
                }
                for (int i = 0; i < arguments.Count; i++)
                {
                    bool pinned = candidates[0].parameters[i].type == Pinned;
                    foreach (var f in candidates)
                    {
                        if ((f.parameters[i].type == Pinned) != pinned) throw Error(start, "Ambiguous pinned/numeric overload. Convert the numeric argument explicitly to float2.");
                        if (f.parameters[i].output && (!arguments[i].writable || arguments[i].type == Pinned)) throw Error(arguments[i].start, "PinnedUV cannot be passed to out/inout parameters.");
                    }
                    if (!pinned) Unpin(arguments[i]);
                }
            }
            else foreach (var argument in arguments) Unpin(argument);
            return new Value(result, start, end);
        }

        FormatException Error(int offset, string message)
        {
            int line = 1; for (int i = 0; i < Math.Min(offset, source.Length); i++) if (source[i] == '\n') line++;
            return new FormatException("Line " + line + ": " + message);
        }
    }
}
