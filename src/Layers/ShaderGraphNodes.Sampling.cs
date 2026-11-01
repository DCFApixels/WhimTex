using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    public enum ShaderGraphHeightSource { Texture, Value }

    internal static partial class ShaderGraphNodes
    {
        internal static bool IsFilter(ShaderGraphNodeKind kind) => kind is ShaderGraphNodeKind.Blur or ShaderGraphNodeKind.Sharpen
            or ShaderGraphNodeKind.EdgeDetection or ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode;
        static readonly ShaderGraphPort[] heightValuePorts = {
            N("Height"), UV(), N("Strength", 8), N("Radius", 1), BoolPort("FlipX"), BoolPort("FlipY") };
        static readonly string[] filterKernels = { "3 x 3", "5 x 5", "9 x 9" };
        static void BuildSamplingPorts(Dictionary<ShaderGraphNodeKind, ShaderGraphPort[]> map)
        {
            foreach (var kind in new[] { ShaderGraphNodeKind.Blur, ShaderGraphNodeKind.Dilate, ShaderGraphNodeKind.Erode })
                map[kind] = new[] { P("In"), UV(), N("Radius", 1) };
            map[ShaderGraphNodeKind.Sharpen] = new[] { P("In"), UV(), N("Radius", 1), N("Strength", 1) };
            map[ShaderGraphNodeKind.EdgeDetection] = new[] { P("In"), UV(), N("Radius", 1), N("Strength", 1) };
        }
    }
}
