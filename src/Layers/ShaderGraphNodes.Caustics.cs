using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        internal static bool IsCaustics(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.CausticsNoise;
        internal static bool IsBuffered(ShaderGraphNodeKind kind) => kind == ShaderGraphNodeKind.Buffer || IsCaustics(kind);

        static void BuildCausticsPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            foreach (var kind in new[] { ShaderGraphNodeKind.Caustics, ShaderGraphNodeKind.CausticsNoise })
            {
                var inputs = new List<ShaderGraphPort>();
                if (kind == ShaderGraphNodeKind.Caustics) inputs.Add(N("Height"));
                else inputs.AddRange(new[] { UV("Position"), P("Scale", ShaderGraphValueType.Float2, 4),
                    P("Offset", ShaderGraphValueType.Float2), P("Period", ShaderGraphValueType.Float2, 1), N("Seed", 1337), N("Phase") });
                inputs.AddRange(new[] { UV(), N("Strength", .02f), N("Depth", 1), N("IOR", 1.333f),
                    new ShaderGraphPort("LightDirection", ShaderGraphValueType.Float3, new Vector4(0, 0, -1, 0)),
                    N("Softness", 1), N("Intensity", .25f) });
                map[kind] = inputs.ToArray();
            }
        }
    }
}
