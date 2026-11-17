using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static Texture2D arrowReference;
        static bool IsPatternExample(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.Fractal or ShaderGraphNodeKind.Scatter or ShaderGraphNodeKind.RangeMask or
            ShaderGraphNodeKind.Step or ShaderGraphNodeKind.Smoothstep or ShaderGraphNodeKind.OneMinus or
            ShaderGraphNodeKind.Power or ShaderGraphNodeKind.Blackbody or
            ShaderGraphNodeKind.Absolute or ShaderGraphNodeKind.Saturate or
            ShaderGraphNodeKind.NoiseSineWave or ShaderGraphNodeKind.SawtoothWave or
            ShaderGraphNodeKind.SquareWave or ShaderGraphNodeKind.TriangleWave or
            ShaderGraphNodeKind.SphereMask or ShaderGraphNodeKind.CombineDistance or
            ShaderGraphNodeKind.SampleGradient or ShaderGraphNodeKind.SampleCurve;

        static Texture2D CreateArrowReference()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true) {
                name = "Catalog arrow stamp", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var uv = new Vector2((x + .5f) / size, (y + .5f) / size);
                float distance = ReferenceArrowDistance(new Vector2(.37f, .56f) + (uv - Vector2.one * .5f) * .72f);
                float coverage = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.004f, .004f, distance));
                pixels[y * size + x] = new Color(.12f, .62f, .8f, 1) * coverage;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        static ShaderGraphNode PatternExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            ShaderGraphNode Add(ShaderGraphNodeKind kind)
            { var added = ShaderGraphNodes.Create(kind, Vector2.zero); graph.nodes.Add(added); return added; }
            void Wire(ShaderGraphNode from, ShaderGraphNode to, string input, string port = "Out") =>
                graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = port, inputNode = to.id, inputPort = input });
            ShaderGraphNode Image(Texture2D texture)
            {
                var source = Add(ShaderGraphNodeKind.SampleTexture); source.wrap = ShaderGraphWrap.Clamp;
                source.inputs.Add(new ShaderGraphInput { port = "Texture", type = ShaderGraphValueType.Texture,
                    resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = texture } });
                return source;
            }
            output = "Out";
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Fractal:
                    var repeat = Add(ShaderGraphNodeKind.Repeat); Set(repeat, "Scale", 1);
                    var shape = Add(ShaderGraphNodeKind.Shape); Set(shape, "Size", .7f); Set(shape, "Feather", .015f);
                    Wire(repeat, shape, "UV"); Wire(shape, node, "In");
                    node.fractal = ShaderGraphFractal.FBm;
                    Set(node, "Octaves", 3); Set(node, "Lacunarity", 2); Set(node, "Gain", .65f); Set(node, "SeedStep", 0);
                    return node;
                case ShaderGraphNodeKind.Scatter:
                    Wire(Image(arrowReference ??= CreateArrowReference()), node, "Source");
                    Set(node, "Count", 3); Set(node, "Size", .95f); Set(node, "SizeRandom", .35f);
                    Set(node, "RotationRandom", 130f / 360); Set(node, "Jitter", .65f); Set(node, "Density", .8f);
                    return node;
                case ShaderGraphNodeKind.OneMinus:
                    Wire(Image(reference ??= CreateReference()), node, "In"); return node;
                case ShaderGraphNodeKind.SphereMask:
                    Wire(Add(ShaderGraphNodeKind.UV), node, "Coords");
                    Set(node, "Center", .5f); Set(node, "Radius", .22f); Set(node, "Hardness", .8f);
                    return node;
                case ShaderGraphNodeKind.CombineDistance:
                    var circle = Add(ShaderGraphNodeKind.Shape);
                    Set(circle, "Center", .36f, .61f); Set(circle, "Size", .48f);
                    var box = Add(ShaderGraphNodeKind.Shape); box.option = 1;
                    Set(box, "Center", .63f, .39f); Set(box, "Size", .46f, .42f);
                    Wire(circle, node, "A", "Distance"); Wire(box, node, "B", "Distance");
                    Set(node, "Feather", .01f); output = "Mask";
                    return node;
            }
            var ramp = Add(ShaderGraphNodeKind.GradientFactor);
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Absolute:
                case ShaderGraphNodeKind.Saturate:
                    var domain = Add(ShaderGraphNodeKind.Remap);
                    bool absolute = node.kind == ShaderGraphNodeKind.Absolute;
                    Set(domain, "OutMin", absolute ? -1 : -.5f);
                    Set(domain, "OutMax", absolute ? 1 : 1.5f);
                    Wire(ramp, domain, "In"); Wire(domain, node, "In"); return node;
                case ShaderGraphNodeKind.NoiseSineWave:
                case ShaderGraphNodeKind.SawtoothWave:
                case ShaderGraphNodeKind.SquareWave:
                case ShaderGraphNodeKind.TriangleWave:
                    bool noisy = node.kind == ShaderGraphNodeKind.NoiseSineWave;
                    var phase = Add(ShaderGraphNodeKind.Multiply);
                    Set(phase, "B", 3);
                    Wire(ramp, phase, "A"); Wire(phase, node, "In");
                    if (noisy) Set(node, "MinMax", -.3f, .3f);
                    // Display the whole signed signal instead of clipping its negative half.
                    var display = Add(ShaderGraphNodeKind.Remap);
                    Set(display, "InMin", noisy ? -1.3f : -1);
                    Set(display, "InMax", noisy ? 1.3f : 1);
                    Wire(node, display, "In"); return display;
                case ShaderGraphNodeKind.SampleGradient:
                    var gradient = new WhimTexGradient();
                    gradient.SetKeys(new[] {
                        new GradientColorKey(new Color(.02f, .03f, .16f), 0),
                        new GradientColorKey(new Color(.05f, .65f, .85f), .35f),
                        new GradientColorKey(new Color(.95f, .16f, .04f), .7f),
                        new GradientColorKey(new Color(1, .9f, .35f), 1) },
                        new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                    node.inputs.Add(new ShaderGraphInput { port = "Gradient", type = ShaderGraphValueType.Gradient,
                        resource = new ShaderParameter { type = ShaderParameterType.Gradient, gradientValue = gradient } });
                    Wire(ramp, node, "T"); return node;
                case ShaderGraphNodeKind.SampleCurve:
                    var curve = new AnimationCurve(new Keyframe(0, .05f), new Keyframe(.25f, .95f),
                        new Keyframe(.65f, .12f), new Keyframe(1, .8f));
                    for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
                    node.inputs.Add(new ShaderGraphInput { port = "Curve", type = ShaderGraphValueType.Curve,
                        resource = new ShaderParameter { type = ShaderParameterType.Curve, curveValue = curve } });
                    Wire(ramp, node, "T"); return node;
            }
            if (node.kind == ShaderGraphNodeKind.Blackbody)
            {
                var temperature = Add(ShaderGraphNodeKind.Remap);
                Set(temperature, "OutMin", 800); Set(temperature, "OutMax", 14000);
                Wire(ramp, temperature, "In"); Wire(temperature, node, "Temperature");
            }
            else
            {
                Wire(ramp, node, node.kind == ShaderGraphNodeKind.Power ? "A" : "In");
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.RangeMask:
                        node.option = 1; Set(node, "Center", .5f); Set(node, "Width", .5f); Set(node, "Feather", .12f); break;
                    case ShaderGraphNodeKind.Step: Set(node, "Edge", .5f); break;
                    case ShaderGraphNodeKind.Smoothstep:
                        float edge = 1f / ShaderGraphCatalogThumbnails.DisplaySize;
                        Set(node, "Min", edge); Set(node, "Max", 1 - edge); break;
                    case ShaderGraphNodeKind.Power: Set(node, "B", 3); break;
                }
            }
            return node;
        }
    }
}
