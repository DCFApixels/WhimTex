using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphNodes
    {
        internal static bool IsCaustics(ShaderGraphNodeKind kind) => kind == ShaderGraphNodeKind.Caustics;
        internal static bool HasCausticsSpectrum(ShaderGraphNodeKind kind) => IsCaustics(kind) || kind == ShaderGraphNodeKind.CausticsNoise;
        internal static bool IsBuffered(ShaderGraphNodeKind kind) => kind == ShaderGraphNodeKind.Buffer || IsCaustics(kind);

        internal static ShaderParameter DefaultResource(ShaderGraphNode node, string port, ShaderGraphValueType type)
        {
            var resource = ShaderGraphInput.DefaultResource(type);
            if (type != ShaderGraphValueType.Gradient || port != "Spectrum" ||
                !HasCausticsSpectrum(node.kind)) return resource;
            var colors = new[] { Color.red, new Color(1,.5f,0), new Color(.5f,1,0),
                new Color(0,1,.2f), new Color(0,.5f,1), new Color(.2f,0,1) };
            var keys = new GradientColorKey[6];
            for (int i=0;i<6;i++) keys[i] = new GradientColorKey(colors[i],i/5f);
            resource.gradientValue.ColorSpace = ColorSpace.Linear;
            resource.gradientValue.ColorMode = WhimTexGradientColorMode.Linear;
            resource.gradientValue.Interpolation = WhimTexGradientInterpolation.Blend;
            resource.gradientValue.SetKeys(keys, new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(1,1) });
            return resource;
        }

        static void BuildCausticsPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            map[ShaderGraphNodeKind.Caustics] = new[] { N("Height"), UV(), N("Strength", .02f), N("Depth", 1), N("IOR", 1.333f),
                new ShaderGraphPort("LightDirection", ShaderGraphValueType.Float3, new Vector4(0, 0, -1, 0)),
                N("Softness", 1), N("Intensity", .25f), N("Dispersion", 1), P("Spectrum", ShaderGraphValueType.Gradient) };
            map[ShaderGraphNodeKind.CausticsNoise] = new[] { UV("Position"), P("Scale", ShaderGraphValueType.Float2, 8),
                P("Offset", ShaderGraphValueType.Float2), P("Period", ShaderGraphValueType.Float2, 1),
                N("Seed", 1337), N("Evolution"), N("Strength", 1), N("Dispersion", 1), P("Spectrum", ShaderGraphValueType.Gradient) };
        }
    }
}
