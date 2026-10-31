using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Discovery parses declarations only. It never captures includes or changes a document.
    internal static class ShaderGraphFunctionParser
    {
        sealed class Token
        {
            internal string text;
            internal int start, end, line;
            internal bool comment, directive;
        }

        sealed class Header
        {
            internal string menuPath;
            internal int line;
            internal readonly StringBuilder metadata = new StringBuilder();
        }

        internal static List<ShaderGraphFunction> Parse(string source, string path, bool includeUnmarked = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ShaderGraphSources.CodeByteCount(source);
            var tokens = Lex(source);
            var result = new List<ShaderGraphFunction>();
            int position = 0;
            Scope(ref position, "", false, 0);
            return result;

            FormatException Error(int line, string message) => new FormatException((path ?? "HLSL") + ":" + line + ": " + message);

            void Scope(ref int index, string scope, bool nested, int depth)
            {
                if (depth > 64) throw Error(1, "HLSL namespace nesting exceeds 64 levels.");
                Header header = null;
                while (index < tokens.Count)
                {
                    Token token = tokens[index];
                    if (token.comment)
                    {
                        string text = token.text.Trim();
                        if (Regex.IsMatch(text, @"^@whimtex-node\b"))
                        {
                            if (header != null) throw Error(header.line, "The node marker must be followed by a function definition.");
                            string menu = text.Substring("@whimtex-node".Length).Trim();
                            if (menu.Length == 0) throw Error(token.line, "Expected @whimtex-node Category/Name.");
                            foreach (string segment in menu.Split('/'))
                                if (string.IsNullOrWhiteSpace(segment)) throw Error(token.line, "The node menu path contains an empty segment.");
                            header = new Header { menuPath = menu, line = token.line };
                        }
                        else if (header != null && text.StartsWith("@", StringComparison.Ordinal))
                        {
                            if (!Regex.IsMatch(text, @"^@(param|preset)\b"))
                                throw Error(token.line, "Only @param and @preset may follow a node marker.");
                            header.metadata.Append("// ").AppendLine(text);
                        }
                        index++;
                        continue;
                    }
                    if (token.directive)
                    {
                        if (header != null) throw Error(token.line, "Preprocessor directives cannot separate a node marker from its function.");
                        index++;
                        continue;
                    }
                    if (token.text == "}")
                    {
                        if (header != null) throw Error(header.line, "The node marker has no function.");
                        if (!nested) throw Error(token.line, "Unexpected closing brace.");
                        index++;
                        return;
                    }
                    // Namespace names may themselves be qualified.
                    if (token.text == "namespace" && index + 2 < tokens.Count && tokens[index + 2].text == "{")
                    {
                        if (header != null) throw Error(header.line, "A node marker must decorate a function, not a namespace.");
                        string name = tokens[index + 1].text;
                        index += 3;
                        Scope(ref index, scope + name + "::", true, depth + 1);
                        continue;
                    }

                    int start = index;
                    while (index < tokens.Count && IsQualifier(tokens[index].text)) index++;
                    if (index + 2 < tokens.Count && IsName(tokens[index].text) &&
                        ShaderGraphFunction.QualifiedIdentifier(tokens[index + 1].text) && tokens[index + 2].text == "(")
                    {
                        string returnType = tokens[index].text, name = scope + tokens[index + 1].text;
                        int open = index + 2, close = Closing(tokens, open, "(", ")");
                        int body = close + 1;
                        // Semantics are not graph ports, but ordinary file functions may declare one.
                        if (body + 1 < tokens.Count && tokens[body].text == ":") body += 2;
                        if (body < tokens.Count && tokens[body].text == "{")
                        {
                            int end = Closing(tokens, body, "{", "}");
                            if (header != null || includeUnmarked)
                            {
                                try
                                {
                                    var definition = ReadFunction(returnType, name, tokens, open + 1, close, header, path);
                                    definition.body = source.Substring(tokens[body].end, tokens[end].start - tokens[body].end);
                                    definition.draft = definition.body;
                                    // Validate the portable signature without inventing a captured source.
                                    definition.sourceId = "discovery";
                                    definition.Validate();
                                    definition.sourceId = null;
                                    result.Add(definition);
                                }
                                catch (FormatException error)
                                {
                                    if (header != null) throw Error(header.line, error.Message);
                                    // File picking lists supported functions, excluding unsupported helper signatures.
                                }
                            }
                            header = null;
                            index = end + 1;
                            continue;
                        }
                    }
                    if (header != null) throw Error(header.line, "Expected a supported HLSL function definition after @whimtex-node.");
                    // Skip structs, buffers, attributes, global initializers and prototypes as units.
                    index = start;
                    while (index < tokens.Count)
                    {
                        if (tokens[index].comment || tokens[index].directive || tokens[index].text == "}") break;
                        if (tokens[index].text == "{") { index = Closing(tokens, index, "{", "}") + 1; break; }
                        if (tokens[index++].text == ";") break;
                    }
                    if (index == start) index++;
                }
                if (header != null) throw Error(header.line, "The node marker has no function.");
                if (nested) throw Error(1, "Unclosed namespace.");
            }
        }

        static ShaderGraphFunction ReadFunction(string returnType, string name, List<Token> tokens,
            int start, int end, Header header, string path)
        {
            var arguments = new List<Token>();
            for (int i = start; i < end; i++) if (!tokens[i].comment) arguments.Add(tokens[i]);
            tokens = arguments; start = 0; end = arguments.Count;
            if (returnType == "float1") returnType = "float";
            if (returnType == "half1") returnType = "half";
            var definition = new ShaderGraphFunction
            {
                inline = false, entryPoint = name, returnType = returnType, sourcePath = path,
                name = name.Substring(name.LastIndexOf(':') + 1), body = "", draft = ""
            };
            if (returnType != "void")
            {
                definition.returnValueType = ShaderGraphFunction.ParseType(returnType);
                if (definition.returnValueType == ShaderGraphValueType.Texture)
                    throw new FormatException("Texture values can only be function inputs.");
            }
            definition.menuPath = header?.menuPath ?? "HLSL/" + Path.GetFileNameWithoutExtension(path ?? "Function") + "/" + definition.name;
            if (header != null) definition.name = definition.menuPath.Substring(definition.menuPath.LastIndexOf('/') + 1);
            var argumentTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (start + 1 == end && tokens[start].text == "void") start = end;
            while (start < end)
            {
                int stop = start;
                while (stop < end && tokens[stop].text != ",") stop++;
                int i = start;
                var direction = ShaderGraphFunctionDirection.Input;
                bool specifiedDirection = false;
                while (i < stop && (IsQualifier(tokens[i].text) || tokens[i].text == "in" || tokens[i].text == "out" || tokens[i].text == "inout"))
                {
                    string qualifier = tokens[i++].text;
                    if (qualifier == "in" || qualifier == "out" || qualifier == "inout")
                    {
                        if (specifiedDirection) throw new FormatException("Duplicate function argument direction.");
                        specifiedDirection = true;
                        direction = qualifier == "out" ? ShaderGraphFunctionDirection.Output : qualifier == "inout" ? ShaderGraphFunctionDirection.InOut : ShaderGraphFunctionDirection.Input;
                    }
                }
                if (i + 2 != stop || !ShaderGraphFunction.Identifier(tokens[i + 1].text))
                    throw new FormatException("Function arguments require a supported type and name; arrays and default expressions are unsupported.");
                string type = tokens[i].text, argument = tokens[i + 1].text;
                if (!argumentTypes.TryAdd(argument, type)) throw new FormatException("Duplicate function argument: " + argument);
                definition.ports.Add(new ShaderGraphFunctionPort
                {
                    name = argument, label = argument, type = PortType(type), direction = direction,
                    uvDefault = PortType(type) == ShaderGraphValueType.Float2 && direction == ShaderGraphFunctionDirection.Input && (argument == "UV" || argument == "uv")
                });
                start = stop + 1;
                if (start == end) throw new FormatException("Trailing comma in function arguments.");
            }
            if (header != null)
            {
                string metadata = header.metadata.ToString();
                var identity = ShaderFXPresetIdentity.Read(metadata);
                definition.presetId = identity.id;
                definition.presetVersion = identity.version;
                foreach (var parameter in ShaderFXMetadata.Parse(metadata, false, out _))
                {
                    bool returned = parameter.name == definition.returnName && returnType != "void";
                    if (returned && argumentTypes.ContainsKey(parameter.name))
                        throw new FormatException("@param Result is ambiguous with a function argument; rename the argument.");
                    string argumentType = returned ? returnType : argumentTypes.TryGetValue(parameter.name, out string declared) ? declared : null;
                    if (argumentType == null)
                        throw new FormatException("@param does not name a function argument: " + parameter.name);
                    var port = returned ? new ShaderGraphFunctionPort { type = definition.returnValueType } : definition.ports.Find(p => p.name == parameter.name);
                    string metadataType = parameter.type == ShaderFXParameterType.Texture2D ? "WhimTexTexture" : parameter.ValueType;
                    if (PortType(metadataType) != PortType(argumentType))
                        throw new FormatException("@param type for " + parameter.name + " must match " + argumentType + ".");
                    if (parameter.type == ShaderFXParameterType.Color)
                    {
                        if (port.type != ShaderGraphValueType.Float4) throw new FormatException("Color node metadata requires a float4 or half4 argument.");
                        port.type = ShaderGraphValueType.Color;
                        port.value = HdrUtility.Decode(parameter.GetNumericColor());
                    }
                    else if (port.type != ShaderGraphValueType.Texture)
                        for (int c = 0; c < parameter.Components; c++) port.value[c] = (float)parameter.GetNumber(c);
                    else if (parameter.textureSource != ShaderFXTextureSource.None || parameter.textureValue != null)
                        throw new FormatException("Texture node arguments require a graph connection; metadata cannot bind an image.");
                    if (parameter.controls.Count > 0 && !string.IsNullOrEmpty(parameter.controls[0].label)) port.label = parameter.controls[0].label;
                    port.hasRange = parameter.hasMinimum && parameter.hasMaximum;
                    port.minimum = parameter.minimum;
                    port.maximum = parameter.maximum;
                    if (returned) definition.returnValueType = port.type;
                }
            }
            return definition;
        }

        internal static bool SameVariant(ShaderGraphFunction a, ShaderGraphFunction b)
        {
            if (a == null || b == null || a.entryPoint != b.entryPoint || a.returnType != b.returnType ||
                a.returnValueType != b.returnValueType || a.ports == null || b.ports == null || a.ports.Count != b.ports.Count) return false;
            for (int i = 0; i < a.ports.Count; i++)
                if (a.ports[i].name != b.ports[i].name || a.ports[i].type != b.ports[i].type || a.ports[i].direction != b.ports[i].direction) return false;
            return true;
        }

        internal static ShaderGraphValueType PortType(string type) => ShaderGraphFunction.ParseType(type == "float1" ? "float" : type == "half1" ? "half" : type);
        static bool IsQualifier(string text) => text == "static" || text == "inline" || text == "const" || text == "precise" || text == "uniform" || text == "UNITY_EXPORT_REFLECTION";
        static bool IsName(string text) => ShaderGraphFunction.Identifier(text);

        static int Closing(List<Token> tokens, int open, string left, string right)
        {
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].comment || tokens[i].directive) continue;
                if (tokens[i].text == left) depth++;
                else if (tokens[i].text == right && --depth == 0) return i;
            }
            throw new FormatException("Line " + tokens[open].line + ": unclosed " + left + ".");
        }

        static List<Token> Lex(string source)
        {
            var result = new List<Token>();
            int i = 0, line = 1;
            bool lineStart = true;
            while (i < source.Length)
            {
                char c = source[i];
                if (char.IsWhiteSpace(c) || c == '\uFEFF')
                { if (c == '\n') { line++; lineStart = true; } i++; continue; }
                int start = i, startLine = line;
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i += 2;
                    int content = i;
                    while (i < source.Length && source[i] != '\n') i++;
                    if (lineStart) result.Add(new Token { text = source.Substring(content, i - content), start = start, end = i, line = startLine, comment = true });
                    continue;
                }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    { if (source[i++] == '\n') { line++; lineStart = true; } }
                    if (i + 1 >= source.Length) throw new FormatException("Line " + startLine + ": unclosed block comment.");
                    i += 2;
                    continue;
                }
                if (c == '#' && lineStart)
                {
                    do
                    {
                        while (i < source.Length && source[i] != '\n') i++;
                        int last = i - 1;
                        if (last >= start && source[last] == '\r') last--;
                        bool continued = last >= start && source[last] == '\\';
                        if (i < source.Length) { i++; line++; }
                        if (!continued) break;
                    } while (i < source.Length);
                    result.Add(new Token { text = source.Substring(start, i - start), start = start, end = i, line = startLine, directive = true });
                    lineStart = true;
                    continue;
                }
                lineStart = false;
                if (c == '"' || c == '\'')
                {
                    i++;
                    bool closed = false;
                    while (i < source.Length)
                    {
                        char value = source[i++];
                        if (value == '\\' && i < source.Length) { i++; continue; }
                        if (value == c) { closed = true; break; }
                        if (value == '\n') line++;
                    }
                    if (!closed) throw new FormatException("Line " + startLine + ": unclosed literal.");
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    i++;
                    while (i < source.Length)
                    {
                        if (char.IsLetterOrDigit(source[i]) || source[i] == '_') i++;
                        else if (i + 2 < source.Length && source[i] == ':' && source[i + 1] == ':' && (char.IsLetter(source[i + 2]) || source[i + 2] == '_')) i += 2;
                        else break;
                    }
                }
                else i++;
                result.Add(new Token { text = source.Substring(start, i - start), start = start, end = i, line = startLine });
            }
            return result;
        }
    }
}
