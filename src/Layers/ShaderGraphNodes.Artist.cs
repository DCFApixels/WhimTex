using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        static void BuildArtistPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.FadeTransition] = new[] { N("NoiseValue", .5f), N("FadeValue"), N("FadeContrast", 1) };
            map[ShaderGraphNodeKind.ReplaceColor] = new[] { ColorPort(),
                new ShaderGraphPort("From", ShaderGraphValueType.Color, new Vector4(0, 0, 0, 1)),
                ColorPort("To"), N("Range", .1f), N("Fuzziness", .05f) };
            map[ShaderGraphNodeKind.Contrast] = new[] { ColorPort(), N("Contrast", 1) };
            map[ShaderGraphNodeKind.NormalReconstructZ] = new[] { P("In", ShaderGraphValueType.Float2) };
            map[ShaderGraphNodeKind.NormalUnpack] = new[] {
                new ShaderGraphPort("In", ShaderGraphValueType.Float4, new Vector4(.5f, .5f, 1, 1)) };
            map[ShaderGraphNodeKind.RadialShear] = new[] { UV(), Center(),
                P("Strength", ShaderGraphValueType.Float2, 10), P("Offset", ShaderGraphValueType.Float2) };
        }

        internal static string ArtistCategory(ShaderGraphNodeKind kind) => kind switch
        {
            ShaderGraphNodeKind.FadeTransition => "Filter",
            ShaderGraphNodeKind.ReplaceColor or ShaderGraphNodeKind.Contrast => "Color/Adjustments",
            ShaderGraphNodeKind.NormalReconstructZ or ShaderGraphNodeKind.NormalUnpack => "Normal",
            ShaderGraphNodeKind.RadialShear => "UV/Distortion",
            _ => null
        };

        internal static bool TryArtistOutputType(ShaderGraphNodeKind kind, out ShaderGraphValueType type, string port = "Out")
        {
            type = ShaderGraphValueType.Float;
            switch (kind)
            {
                case ShaderGraphNodeKind.FadeTransition: return true;
                case ShaderGraphNodeKind.ReplaceColor:
                    type = port == "Mask" ? ShaderGraphValueType.Float : ShaderGraphValueType.Float4; return true;
                case ShaderGraphNodeKind.Contrast:
                    type = ShaderGraphValueType.Float4; return true;
                case ShaderGraphNodeKind.NormalReconstructZ: case ShaderGraphNodeKind.NormalUnpack:
                    type = ShaderGraphValueType.Float3; return true;
                case ShaderGraphNodeKind.RadialShear:
                    type = ShaderGraphValueType.Float2; return true;
                default: return false;
            }
        }
    }
}
