using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
                name = bufferPrograms == null ? "_GraphInput" + result.inputNames.Count.ToString(CultureInfo.InvariantCulture) : BufferBindingName("_GraphInput", original);
                result.inputNames.Add(original, name);
            }
            return name;
        }

        // Independent programs share physical bindings. Encoding the key is collision-free,
        // and an edit in one producer cannot rename another producer's uniforms.
        static string BufferBindingName(string prefix, string key)
        {
            var name = new StringBuilder(prefix);
            foreach (byte value in Encoding.UTF8.GetBytes(key)) name.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return name.ToString();
        }

        string ParameterSourceName(ShaderParameter parameter)
        {
            if (!parameterNames.TryGetValue(parameter.id, out string name))
            {
                name = bufferPrograms == null ? "_GraphParameter" + parameterNames.Count.ToString(CultureInfo.InvariantCulture) : BufferBindingName("_GraphParameter", parameter.id);
                parameterNames.Add(parameter.id, name);
            }
            return name;
        }
    }
}
