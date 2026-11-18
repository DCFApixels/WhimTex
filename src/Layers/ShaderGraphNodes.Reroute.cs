using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        // Most definitions put their data path first. Override control-first
        // layouts here, or declare a source with no incoming data path.
        static readonly Dictionary<ShaderGraphNodeKind, (string input, string output)> reroutePorts = new()
        {
            [ShaderGraphNodeKind.Step] = ("In", "Out"),
            [ShaderGraphNodeKind.Smoothstep] = ("In", "Out"),
            [ShaderGraphNodeKind.Select] = ("False", "Out"),
            [ShaderGraphNodeKind.Switch] = ("Fallback", "Out"),
            [ShaderGraphNodeKind.SampleTexture] = ("UV", "Out"),
            [ShaderGraphNodeKind.SampleGradient] = ("T", "Out"),
            [ShaderGraphNodeKind.SampleCurve] = ("T", "Out"),
            [ShaderGraphNodeKind.ApplyTransform2D] = ("UV", "Local"),
            [ShaderGraphNodeKind.ParallaxMapping] = ("UV", "Out"),
            [ShaderGraphNodeKind.ParallaxOcclusionMapping] = ("UV", "Out"),
            [ShaderGraphNodeKind.Time] = (null, "Out"),
            [ShaderGraphNodeKind.InstanceIndex] = (null, "Out"),
            [ShaderGraphNodeKind.LightCount] = (null, "Out"),
            [ShaderGraphNodeKind.LightDirection] = (null, "Out"),
            [ShaderGraphNodeKind.LightColor] = (null, "Out"),
            [ShaderGraphNodeKind.LightAttenuation] = (null, "Out"),
            [ShaderGraphNodeKind.TextureSize] = (null, "Size"),
            [ShaderGraphNodeKind.ScatterInstance] = (null, "UV")
        };

        internal static (string input, string output) ReroutePorts(ShaderGraphNode node)
        {
            var inputs = Inputs(node);
            var outputs = Outputs(node);
            if (reroutePorts.TryGetValue(node.kind, out var declared))
            {
                string input = null, output = null;
                foreach (var port in inputs)
                    if (port.name == declared.input && InputVisible(node, port.name)) input = port.name;
                foreach (string port in outputs)
                    if (port == declared.output) output = port;
                return (input, output);
            }

            string mainInput = null;
            foreach (var port in inputs)
                if (InputVisible(node, port.name) && node.function?.ports.Find(p => p.name == port.name)?.option != true)
                { mainInput = port.name; break; }
            return (mainInput, outputs.Length == 0 ? null : outputs[0]);
        }
    }
}
