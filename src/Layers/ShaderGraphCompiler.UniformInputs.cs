using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        // Conservative proof, not algebraic simplification: opaque code, sampled values
        // and iteration-dependent sources retain their original per-pixel evaluation.
        bool PixelUniformInput(ShaderGraphNode node, string port)
        {
            if (!incoming.TryGetValue(node.id + ":" + port, out var edge)) return true;
            var pending = new Stack<string>(); var visited = new HashSet<string>();
            pending.Push(edge.outputNode);
            while (pending.Count != 0)
            {
                generationBudget.CheckCancellation();
                string id = pending.Pop(); if (!visited.Add(id)) continue;
                var current = nodes[id];
                if (current.kind == ShaderGraphNodeKind.UV ||
                    !IsOptimizableBuiltin(current.kind) && current.kind is not (ShaderGraphNodeKind.Time or ShaderGraphNodeKind.InstanceIndex)) return false;
                foreach (var input in ShaderGraphNodes.Inputs(current))
                {
                    if (!ShaderGraphParameters.EvaluatesInput(current, input.name)) continue;
                    if (incoming.TryGetValue(id + ":" + input.name, out var source)) pending.Push(source.outputNode);
                    else if (input.uvDefault && (current.FindInput(input.name) == null || current.FindInput(input.name).source != ShaderGraphInputSource.Value)) return false;
                }
            }
            return true;
        }
    }
}
