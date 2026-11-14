using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        const string ValueBindingPort = "Literal";

        static void ValidateValue(ShaderGraphNode node, bool effectContext)
        {
            if (node.kind != ShaderGraphNodeKind.Value) return;
            int size = ShaderGraphNodes.MatrixSize(node.valueType);
            if (size != 0)
            {
                if (node.literal != null || node.matrixRows?.Length > 0 &&
                    (node.matrixRows.Length != size || Array.Exists(node.matrixRows, row => !Finite(row))))
                    Error(node, "Invalid matrix rows.");
                return;
            }
            var value = node.literal;
            if (node.valueType != ShaderGraphValueType.Float || node.matrixRows?.Length > 0) Error(node, "Invalid Value type.");
            if (value == null) return; // Compact graph data may omit the default Float.
            if (!Enum.IsDefined(typeof(ShaderParameterType), value.type) || value.type == ShaderParameterType.Seed ||
                value.ValueType != ShaderNumericValue.NativeType(value.type) &&
                !(value.type == ShaderParameterType.Toggle && value.ValueType == "bool"))
                Error(node, "Invalid Value type.");
            if (value.type == ShaderParameterType.Texture2D &&
                (!Enum.IsDefined(typeof(ShaderTextureSource), value.textureSource) || !effectContext && value.textureSource == ShaderTextureSource.Self))
                Error(node, "Invalid texture source for this graph.");
            for (int i = 0; i < value.Components; i++) ShaderNumericValue.Check(value.ValueType, value.GetNumber(i));
        }

        static Value MatrixIdentity(ShaderGraphValueType type)
        {
            int size = ShaderGraphNodes.MatrixSize(type);
            var text = new StringBuilder(Type(type)).Append('(');
            for (int i = 0; i < size * size; i++)
            {
                if (i != 0) text.Append(", ");
                text.Append(i / size == i % size ? '1' : '0');
            }
            return new Value(type, text.Append(')').ToString());
        }

        Value EmitMatrixConstruction(ShaderGraphNode node, string port)
        {
            var type = ShaderGraphNodes.MatrixConstructionType(port);
            int size = ShaderGraphNodes.MatrixSize(type);
            var text = new StringBuilder(Type(type)).Append('(');
            for (int row = 0; row < size; row++)
            {
                if (row != 0) text.Append(", ");
                text.Append('(').Append(Input(node, "M" + row).text).Append(')').Append(VectorSwizzle(size));
            }
            text.Append(')');
            return new Value(type, node.option == 1 ? "transpose(" + text + ")" : text.ToString());
        }

        static Value EmitMatrixSplit(ShaderGraphNode node, string port, Value matrix)
        {
            int size = ShaderGraphNodes.MatrixSize(matrix.type), index = port[1] - '0';
            var type = ShaderGraphNodes.MatrixVectorType(matrix.type);
            if (index >= size) return new Value(type, "(" + Type(type) + ")0");
            return new Value(type, (node.option == 1 ? "transpose(" + matrix.text + ")" : "(" + matrix.text + ")") + "[" + index + "]");
        }

        Value EmitMultiply(ShaderGraphNode node, Value a, Value b)
        {
            bool matrixA = ShaderGraphNodes.MatrixSize(a.type) != 0, matrixB = ShaderGraphNodes.MatrixSize(b.type) != 0;
            if (!matrixA && !matrixB)
            {
                var type = Common(node, a, b);
                return new Value(type, "(" + Cast(a, type, node).text + "*" + Cast(b, type, node).text + ")");
            }
            var result = ShaderGraphNodes.MultiplyType(a.type, b.type);
            if (matrixA && matrixB)
            {
                string left = a.type == result ? a.text : "(" + Type(result) + ")(" + a.text + ")";
                string right = b.type == result ? b.text : "(" + Type(result) + ")(" + b.text + ")";
                return new Value(result, "mul(" + left + ", " + right + ")");
            }
            return new Value(result, matrixA ? "mul(" + a.text + ", " + Cast(b, result, node).text + ")" :
                "mul(" + Cast(a, result, node).text + ", " + b.text + ")");
        }

        static string VectorSwizzle(int count) => count == 2 ? ".xy" : count == 3 ? ".xyz" : "";

        Value EmitValue(ShaderGraphNode node)
        {
            var type = ShaderGraphNodes.ValueOutputType(node);
            int size = ShaderGraphNodes.MatrixSize(type);
            if (size != 0)
            {
                var text = new StringBuilder(Type(type)).Append('(');
                for (int row = 0; row < size; row++)
                {
                    if (row != 0) text.Append(", ");
                    string name = InputSourceName(node, ValueBindingPort + row.ToString(CultureInfo.InvariantCulture));
                    var value = ShaderGraphNodes.MatrixRow(node, row);
                    if (declarations.Add(name))
                    {
                        var declaration = new ShaderParameter { name = name, id = name, type = ShaderParameterType.Vector, vectorValue = value };
                        inputParameters.Add(declaration);
                        result.valueBindings.Add((node.uniformNodeId ?? node.id, declaration, row));
                        AppendParameter(inputDeclarations, declaration, hidden: true);
                    }
                    text.Append(name).Append(VectorSwizzle(size));
                }
                return new Value(type, text.Append(')').ToString());
            }

            string uniform = InputSourceName(node, ValueBindingPort);
            if (declarations.Add(uniform))
            {
                var declaration = node.literal?.CopyDeclaration() ?? new ShaderParameter { floatValue = 0 };
                declaration.name = uniform;
                declaration.id = uniform.TrimStart('_');
                declaration.controls.Clear();
                inputParameters.Add(declaration);
                result.valueBindings.Add((node.uniformNodeId ?? node.id, declaration, -1));
                AppendParameter(inputDeclarations, declaration, hidden: true);
            }
            return new Value(type, type == ShaderGraphValueType.Boolean && node.literal?.ValueType != "bool" ? "(" + uniform + " != 0)" : uniform);
        }

        // Only compiler-owned statements pass here; user functions/includes retain their formatting.
        // Keep every newline and #line directive intact for node diagnostics.
        void AppendIndentedGeneratedCode(StringBuilder target, string code, ShaderGraphNode node, int executableStart)
        {
            int depth = 0;
            for (int start = 0; start < code.Length;)
            {
                int end = code.IndexOf('\n', start);
                if (end < 0) end = code.Length;
                int first = start;
                while (first < end && (code[first] == ' ' || code[first] == '\t')) first++;
                bool directive = first < end && code[first] == '#';
                int indentation = 0;
                if (!directive && first < end && code[first] != '\r')
                {
                    int closing = first, indent = depth;
                    while (closing < end)
                    {
                        char c = code[closing++];
                        if (c == '}') indent--;
                        else if (c != ' ' && c != '\t') break;
                    }
                    indentation = Math.Max(0, indent) * ShaderFX.CodeIndent.Length;
                    for (int i = first; i < end; i++)
                    {
                        if (code[i] == '{') depth++;
                        else if (code[i] == '}') depth--;
                    }
                }
                generationBudget.CheckSource(node, (long)target.Length - executableStart + indentation +
                    end - first + (end < code.Length ? 1 : 0));
                target.Append(' ', indentation);
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
            static readonly object gate = new();
            static BuiltInLibrary instance;
            static int revision = -1;
            internal static BuiltInLibrary Instance
            {
                get
                {
                    lock (gate)
                    {
                        int current = ShaderProgramSourceRevision.Current;
                        if (instance == null || current != revision)
                        { instance = new BuiltInLibrary(); revision = current; }
                        return instance;
                    }
                }
            }
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
