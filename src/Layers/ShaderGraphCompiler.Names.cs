using System.Collections.Generic;
using System.Globalization;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        // Executable names follow evaluation order, never persistent document IDs.
        // Bindings and diagnostic locations remain private to each compilation.
        readonly Dictionary<string, string> codeNames = new();
        readonly Dictionary<string, string> parameterNames = new();

        string NodeCodeName(ShaderGraphNode node)
        {
            if (!codeNames.TryGetValue(node.id, out string name))
            {
                name = "N" + codeNames.Count.ToString(CultureInfo.InvariantCulture);
                codeNames.Add(node.id, name);
                result.diagnosticNodes.Add("GraphNode/" + name, node.id);
            }
            return name;
        }

        string NodeSourceName(ShaderGraphNode node) => "GraphNode/" + NodeCodeName(node);

        string InputSourceName(ShaderGraphNode node, string port)
        {
            string original = InputName(node, port);
            if (!result.inputNames.TryGetValue(original, out string name))
            {
                name = "_GraphInput" + result.inputNames.Count.ToString(CultureInfo.InvariantCulture);
                result.inputNames.Add(original, name);
            }
            return name;
        }

        string ParameterSourceName(ShaderParameter parameter)
        {
            if (!parameterNames.TryGetValue(parameter.id, out string name))
            {
                name = "_GraphParameter" + parameterNames.Count.ToString(CultureInfo.InvariantCulture);
                parameterNames.Add(parameter.id, name);
            }
            return name;
        }
    }
}
