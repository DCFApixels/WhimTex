using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        internal static bool IsParallax(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.ParallaxMapping or ShaderGraphNodeKind.ParallaxOcclusionMapping;

        static readonly ShaderGraphPort[][] parallaxPorts = BuildParallaxPorts();
        static ShaderGraphPort[][] BuildParallaxPorts()
        {
            var result = new ShaderGraphPort[4][];
            for (int i = 0; i < result.Length; i++)
            {
                var ports = new ShaderGraphPort[(i & 2) != 0 ? 5 : 4];
                ports[0] = (i & 1) != 0 ? N("Height", .5f) : P("Height", ShaderGraphValueType.Texture);
                ports[1] = UV();
                ports[2] = new ShaderGraphPort("ViewDirection", ShaderGraphValueType.Float3, new Vector4(.5f, .5f, 1, 0));
                ports[3] = N("Amplitude", .05f);
                if (ports.Length == 5) ports[4] = N("Steps", 16);
                result[i] = ports;
            }
            return result;
        }
        static ShaderGraphPort[] ParallaxInputs(ShaderGraphNode node) => !IsParallax(node.kind) ? null :
            parallaxPorts[(node.heightSource == ShaderGraphHeightSource.Value ? 1 : 0) |
                (node.kind == ShaderGraphNodeKind.ParallaxOcclusionMapping ? 2 : 0)];

        internal static string SearchAliases(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.FWidth => "DDXY",
            ShaderGraphNodeKind.ParallaxMapping => "Bump Offset Parallax Offset",
            ShaderGraphNodeKind.ParallaxOcclusionMapping => "POM",
            _ => ""
        };
    }
}
