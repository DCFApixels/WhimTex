using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphExpression
    {
        internal const int MaximumLength = 4096;
        const int MaximumInputs = 64;
        static readonly Regex Tokens = new Regex(@"0[xX][0-9a-fA-F]+[uUlL]*|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?[fFhHuUlL]*|(?<name>[A-Za-z_][A-Za-z_0-9]*)|\S", RegexOptions.CultureInvariant);
        static readonly Regex NumericType = new Regex(@"^(?:bool|double|fixed|float|half|int|uint|dword|min10float|min12int|min16float|min16int|min16uint)(?:[1-4](?:x[1-4])?)?$", RegexOptions.CultureInvariant);
        static readonly HashSet<string> Reserved = new HashSet<string>(
            "break case cbuffer centroid class column_major const continue default discard do else extern false for groupshared if in inline inout interface namespace nointerpolation out precise register return row_major shared static struct switch true typedef uniform volatile while void vector matrix sampler sampler1D sampler2D sampler3D samplerCUBE Texture2D SamplerState PinnedUV WhimTexTexture".Split(' '), StringComparer.Ordinal);
        readonly string source;
        readonly ShaderGraphValueType type;
        internal readonly string code, error;
        internal readonly ShaderGraphPort[] ports;

        internal static ShaderGraphExpression Get(ShaderGraphNode node)
        {
            var cached = node.expressionCache;
            if (cached == null || cached.source != node.expression || cached.type != node.valueType)
                node.expressionCache = cached = new ShaderGraphExpression(node.expression, node.valueType);
            return cached;
        }

        ShaderGraphExpression(string source, ShaderGraphValueType type)
        {
            this.source = source; this.type = type;
            if (source?.Length > MaximumLength)
            {
                code = "0"; ports = Array.Empty<ShaderGraphPort>();
                error = "Expression exceeds 4096 characters."; return;
            }
            bool block = false;
            var cleaned = new StringBuilder();
            foreach (string line in (source ?? "").Split('\n'))
                cleaned.AppendLine(ShaderSourceBuilder.MaskComments(line, ref block));
            code = cleaned.ToString().Trim();
            if (block) error = "Unterminated Expression comment.";
            var tokens = Tokens.Matches(code);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var inputs = new List<ShaderGraphPort>();
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i].Value;
                if (token is ";" or "{" or "}" or "#" or "\"" or "'")
                    error = "Enter a single HLSL expression, without statements or directives.";
                if (!tokens[i].Groups["name"].Success || i > 0 && tokens[i - 1].Value == "." ||
                    i + 1 < tokens.Count && tokens[i + 1].Value == "(" ||
                    Reserved.Contains(token) || NumericType.IsMatch(token) || !names.Add(token)) continue;
                if (inputs.Count == MaximumInputs) { error = "Expression supports at most 64 inputs."; break; }
                inputs.Add(new ShaderGraphPort(token, type));
            }
            ports = inputs.ToArray();
        }

        internal static bool IsInputName(string name)
        {
            if (string.IsNullOrEmpty(name) || Reserved.Contains(name) || NumericType.IsMatch(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c != '_' && !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') && !(i > 0 && c >= '0' && c <= '9')) return false;
            }
            return true;
        }

        internal static void SetExpression(ShaderGraphNode node, string expression)
        {
            foreach (var port in Get(node).ports)
                if (node.FindInput(port.name) == null)
                    node.inputs.Add(new ShaderGraphInput { port = port.name, type = node.valueType });
            node.expression = expression ?? "";
        }
    }

    internal sealed partial class ShaderGraphCompiler
    {
        readonly HashSet<string> expressionFunctions = new HashSet<string>();
        Value EmitExpression(ShaderGraphNode node)
        {
            var expression = ShaderGraphExpression.Get(node);
            if (expression.error != null) Error(node, expression.error);
            string name = "GraphExpression" + NodeCodeName(node);
            string type = Type(node.valueType);
            if (expressionFunctions.Add(node.id))
            {
                result.contextDependencies |= ShaderContextUsage.Analyze(expression.code);
                samplingCode.Append("#line 1 \"").Append(NodeSourceName(node)).AppendLine("\"");
                samplingCode.Append(type).Append(' ').Append(name).Append('(');
                for (int i = 0; i < expression.ports.Length; i++)
                {
                    if (i > 0) samplingCode.Append(", ");
                    samplingCode.Append(type).Append(' ').Append(expression.ports[i].name);
                }
                samplingCode.AppendLine(") {").Append("return (")
                    .Append(expression.code.Length == 0 ? "(" + type + ")0" : expression.code).AppendLine(");\n}");
            }
            var call = new StringBuilder(name).Append('(');
            for (int i = 0; i < expression.ports.Length; i++)
            {
                if (i > 0) call.Append(", ");
                call.Append(Input(node, expression.ports[i].name).text);
            }
            return new Value(node.valueType, call.Append(')').ToString());
        }
    }
}
