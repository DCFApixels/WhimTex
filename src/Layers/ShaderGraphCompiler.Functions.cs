using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        readonly StringBuilder functionCode = new StringBuilder();
        readonly Dictionary<string, string> functionNames = new Dictionary<string, string>();
        readonly Dictionary<string, string> modules = new Dictionary<string, string>();
        int functionBytes;
        // Keep the graph's level-zero sampling inside each isolated module. Engine
        // includes must still be emitted at global scope by module preparation.
        const string FunctionPreamble = "float4 SampleTexture(WhimTexTexture tex, float2 uv) { return SampleTextureLod(tex, uv, 0); }\n";
        void AppendModule(ShaderGraphModuleSource.Prepared prepared, ShaderGraphNode node)
        {
            int bytes = ShaderGraphSources.Utf8.GetByteCount(prepared.Source) + ShaderGraphSources.Utf8.GetByteCount(prepared.Cleanup);
            if (bytes > ShaderGraphSources.MaximumArchiveBytes - functionBytes) Error(node, "Combined HLSL function source exceeds 32 MiB.");
            functionBytes += bytes;
            string directive = "\n#line 1 \"" + NodeSourceName(node) + "\"" + Environment.NewLine;
            // Library metadata describes node ports, not parameters of the generated FX.
            // Retain the comments and line positions without activating their directives.
            string moduleSource = Regex.Replace(prepared.Source, @"(?m)^([ \t]*//[ \t]*)@", "$1");
            CheckGeneratedSize(node, (long)directive.Length + moduleSource.Length + prepared.Cleanup.Length);
            functionCode.Append(directive).Append(moduleSource).Append(prepared.Cleanup);
        }

        void ValidateFunctions()
        {
            if (graph.functions == null || graph.sources == null || graph.functions.Count > 512)
                throw new FormatException("Invalid Shader function table.");
            var ids = new HashSet<string>();
            foreach (var f in graph.functions)
            {
                if (f == null || !Identifier(f.id) || !ids.Add(f.id)) throw new FormatException("Invalid or duplicate Shader function ID.");
                f.Validate();
                if (!string.IsNullOrEmpty(f.sourceId))
                {
                    var root = graph.sources.Find(s => s != null && s.id == f.sourceId);
                    if (root == null) throw new FormatException("Function source is missing: " + f.name);
                    if (f.inline && root.code != f.body) throw new FormatException("Inline function body differs from its source backup: " + f.name);
                }
                else if (f.inline && HasIncludes(f.body)) throw new FormatException("Inline includes require an embedded source archive: " + f.name);
            }
            ShaderGraphSources.Validate(graph.sources);
            graph.BindFunctions();
        }
        internal static bool HasIncludes(string code) => !string.IsNullOrEmpty(code) && Regex.IsMatch(ShaderEntryPoint.Clean(Regex.Replace(code, @"\\(?:\r\n|\r|\n)", "")), @"(?m)^\s*#\s*(?:include|include_with_pragmas)\b");

        string DeclareFunction(ShaderGraphNode node)
        {
            var f = node.function;
            if (functionNames.TryGetValue(f.id, out string name)) return name;
            if (f.inline)
            {
                string module = "_GraphInline_" + functionNames.Count;
                name = module + "::" + (f.fullSource ? f.entryPoint : "Function");
                string source = string.IsNullOrEmpty(f.sourceId) ? f.body : ShaderGraphSources.Expand(f.sourceId, graph.sources);
                if (!f.fullSource) source = f.Signature("Function") + "\n{\n" + source + "\n}";
                result.contextDependencies |= ShaderContextUsage.Analyze(source);
                var prepared = ShaderGraphModuleSource.Prepare(source, module, FunctionPreamble);
                AppendModule(prepared, node);
            }
            else
            {
                if (!modules.TryGetValue(f.sourceId, out string module))
                {
                    module = "_GraphModule_" + modules.Count;
                    string source = ShaderGraphSources.Expand(f.sourceId, graph.sources);
                    result.contextDependencies |= ShaderContextUsage.Analyze(source);
                    var prepared = ShaderGraphModuleSource.Prepare(source, module, FunctionPreamble);
                    AppendModule(prepared, node);
                    modules.Add(f.sourceId, module);
                }
                name = module + "::" + f.entryPoint;
            }
            functionCode.AppendLine("#line 1 \"ShaderGraph\"");
            functionNames.Add(f.id, name);
            return name;
        }

        Value EmitFunction(ShaderGraphNode node, string output)
        {
            var f = node.function;
            string function = DeclareFunction(node);
            var arguments = new StringBuilder();
            var outputs = new List<(string port, Value value)>();
            foreach (var p in f.ports)
            {
                if (arguments.Length != 0) arguments.Append(", ");
                if (p.direction == ShaderGraphFunctionDirection.Input) arguments.Append(Input(node, p.name).text);
                else
                {
                    string variable = "n" + NodeCodeName(node) + "_" + p.name;
                    string initial = p.direction == ShaderGraphFunctionDirection.InOut ? Input(node, p.name).text : "(" + Type(p.type) + ")0";
                    body.Append("#line 1 \"").Append(NodeSourceName(node)).AppendLine("\"");
                    body.Append(Type(p.type)).Append(' ').Append(variable).Append(" = ").Append(initial).AppendLine(";");
                    arguments.Append(variable); outputs.Add((p.name, new Value(p.type, variable)));
                }
            }
            body.Append("#line 1 \"").Append(NodeSourceName(node)).AppendLine("\"");
            if (f.returnType != "void")
            {
                string variable = "n" + NodeCodeName(node) + "_" + f.returnName;
                body.Append(Type(f.returnValueType)).Append(' ').Append(variable).Append(" = ");
                outputs.Add((f.returnName, new Value(f.returnValueType, variable)));
            }
            body.Append(function).Append('(').Append(arguments).AppendLine(");");
            foreach (var item in outputs) { string key = node.id + ":" + item.port; values.Add(key, item.value); result.outputTypes[key] = item.value.type; }
            if (!values.TryGetValue(node.id + ":" + output, out var value)) Error(node, "Unknown function output " + output);
            return value;
        }
    }
}
