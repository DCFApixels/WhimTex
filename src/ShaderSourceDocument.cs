using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    // One source snapshot for contract checks. Offsets always address the original
    // text, including comments and #line directives; edits are applied only at the end.
    internal sealed class ShaderSourceDocument
    {
        internal readonly string Source, Comments, Clean;
        internal readonly Dictionary<int, string> Before = new(), After = new();
        List<Token> tokens, allTokens;
        Dictionary<int, int> pairs;
        List<Function> functions;
        List<Match> directives;
        internal List<Token> Tokens { get { ReadSyntax(); return tokens; } }
        internal List<Token> AllTokens { get { ReadSyntax(); return allTokens; } }
        internal Dictionary<int, int> Pairs { get { ReadSyntax(); return pairs; } }
        internal List<Function> Functions { get { ReadSyntax(); return functions; } }
        internal IReadOnlyList<Match> Directives
        {
            get
            {
                if (directives == null) { directives = new(); foreach (Match m in Directive.Matches(Clean)) directives.Add(m); }
                return directives;
            }
        }

        internal readonly struct Token
        {
            internal readonly string text;
            internal readonly int start, end;
            internal Token(string text, int start, int end) { this.text = text; this.start = start; this.end = end; }
        }
        internal readonly struct Function
        {
            internal readonly int type, close, body, end;
            internal Function(int type, int close, int body, int end)
            { this.type = type; this.close = close; this.body = body; this.end = end; }
        }
        static readonly Regex Strings = new("\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.CultureInvariant);
        static readonly Regex Directive = new(@"(?m)^[\t ]*#[^\r\n]*(?:\r?\n(?<=\\\r?\n)[^\r\n]*)*", RegexOptions.CultureInvariant);

        internal ShaderSourceDocument(string source)
        {
            Source = source ?? "";
            bool block = false;
            var text = new StringBuilder(Source.Length + 1);
            foreach (string line in Source.Split('\n')) text.Append(ShaderSourceBuilder.MaskComments(line, ref block)).Append('\n');
            Comments = text.ToString();
            Clean = Strings.Replace(Comments, m => new string(' ', m.Length));
        }
        internal static bool Identifier(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_');
        internal FormatException Error(int offset, string message)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(offset, Source.Length); i++) if (Source[i] == '\n') line++;
            return new FormatException("Line " + line + ": " + message);
        }
        static bool Word(char c) => c == '_' || char.IsLetterOrDigit(c) || c >= 128 &&
            (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark ||
             char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.ConnectorPunctuation);
        static bool Hex(char c) => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F';
        static List<Token> Lex(string text)
        {
            var result = new List<Token>();
            for (int i = 0; i < text.Length;)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i++;
                if (c == '_' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
                    while (i < text.Length && Word(text[i])) i++;
                else if (c == '0' && i + 1 < text.Length && (text[i] == 'x' || text[i] == 'X') && Hex(text[i + 1]))
                {
                    i++;
                    while (i < text.Length && Hex(text[i])) i++;
                    while (i < text.Length && "uUlL".IndexOf(text[i]) >= 0) i++;
                }
                else if (char.IsDigit(c) || c == '.' && i < text.Length && char.IsDigit(text[i]))
                {
                    while (i < text.Length && char.IsDigit(text[i])) i++;
                    if (c != '.' && i < text.Length && text[i] == '.')
                    { i++; while (i < text.Length && char.IsDigit(text[i])) i++; }
                    if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                    {
                        int exponent = i + 1;
                        if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-')) exponent++;
                        int digits = exponent;
                        while (exponent < text.Length && char.IsDigit(text[exponent])) exponent++;
                        if (exponent > digits) i = exponent;
                    }
                    if (i < text.Length && "fFuU".IndexOf(text[i]) >= 0) i++;
                }
                else if (i < text.Length)
                {
                    char next = text[i];
                    if ((c == '<' || c == '>') && next == c)
                    { i++; if (i < text.Length && text[i] == '=') i++; }
                    else if (next == '=' && "+-*/%=!<>&|^".IndexOf(c) >= 0 || next == c && ":+-&|".IndexOf(c) >= 0) i++;
                }
                result.Add(new Token(text.Substring(start, i - start), start, i));
            }
            return result;
        }
        void ReadSyntax()
        {
            if (tokens != null) return;
            var readAll = Lex(Clean);
            var readTokens = new List<Token>(readAll.Count);
            int directiveIndex = 0;
            var lines = Directives;
            foreach (var token in readAll)
            {
                while (directiveIndex < lines.Count && token.start >= lines[directiveIndex].Index + lines[directiveIndex].Length) directiveIndex++;
                if (directiveIndex < lines.Count && token.start >= lines[directiveIndex].Index) continue;
                readTokens.Add(token);
            }
            var readPairs = new Dictionary<int, int>();
            var stack = new Stack<int>();
            for (int i = 0; i < readTokens.Count; i++)
            {
                string t = readTokens[i].text;
                if (t == "(" || t == "[" || t == "{") stack.Push(i);
                else if (t == ")" || t == "]" || t == "}")
                {
                    if (stack.Count == 0) throw Error(readTokens[i].start, "Unmatched HLSL delimiter.");
                    int open = stack.Pop();
                    string pair = readTokens[open].text + t;
                    if (pair != "()" && pair != "[]" && pair != "{}") throw Error(readTokens[i].start, "Mismatched HLSL delimiters.");
                    readPairs[open] = i; readPairs[i] = open;
                }
            }
            if (stack.Count != 0) throw Error(readTokens[stack.Peek()].start, "Unclosed HLSL delimiter.");
            var readFunctions = new List<Function>();
            for (int i = 0; i + 3 < readTokens.Count; i++)
            {
                if (Identifier(readTokens[i].text) && Identifier(readTokens[i + 1].text) && readTokens[i + 2].text == "(")
                {
                    int close = readPairs[i + 2], body = close + 1;
                    if (body < readTokens.Count && readTokens[body].text == ":")
                        while (body < readTokens.Count && readTokens[body].text != "{" && readTokens[body].text != ";") body++;
                    if (body < readTokens.Count && readTokens[body].text == "{")
                    { int end = readPairs[body]; readFunctions.Add(new Function(i, close, body, end)); i = end; continue; }
                }
                if (readTokens[i].text == "{") i = readPairs[i];
            }
            tokens = readTokens; allTokens = readAll; pairs = readPairs; functions = readFunctions;
        }

        internal string Lowered
        {
            get
            {
                if (Before.Count == 0 && After.Count == 0) return Source;
                var offsets = new List<int>(Before.Count + After.Count);
                foreach (int i in Before.Keys) offsets.Add(i);
                foreach (int i in After.Keys) if (!Before.ContainsKey(i)) offsets.Add(i);
                offsets.Sort();
                var result = new StringBuilder(Source.Length + offsets.Count * 24);
                int previous = 0;
                foreach (int offset in offsets)
                {
                    result.Append(Source, previous, offset - previous);
                    if (After.TryGetValue(offset, out string suffix)) result.Append(suffix);
                    if (Before.TryGetValue(offset, out string prefix)) result.Append(prefix);
                    previous = offset;
                }
                result.Append(Source, previous, Source.Length - previous);
                return result.ToString();
            }
        }
    }
}
