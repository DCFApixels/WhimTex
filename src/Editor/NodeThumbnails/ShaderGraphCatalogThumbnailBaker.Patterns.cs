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
            ShaderGraphNodeKind.Absolute or
            ShaderGraphNodeKind.SquareRoot or ShaderGraphNodeKind.Exponential or ShaderGraphNodeKind.Log or
            ShaderGraphNodeKind.SoftSign or ShaderGraphNodeKind.HyperbolicTangent or
            ShaderGraphNodeKind.FresnelEffect or ShaderGraphNodeKind.Distance or
            ShaderGraphNodeKind.DDX or ShaderGraphNodeKind.DDY or ShaderGraphNodeKind.FWidth or
            ShaderGraphNodeKind.Premultiply or ShaderGraphNodeKind.Unpremultiply or ShaderGraphNodeKind.SetAlpha or
            ShaderGraphNodeKind.Sine or ShaderGraphNodeKind.Cosine or ShaderGraphNodeKind.InverseSoftSign or
            ShaderGraphNodeKind.Reflection or ShaderGraphNodeKind.Refract or
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
            ShaderGraphNode Expression(ShaderGraphValueType type, string code)
            {
                var expression = Add(ShaderGraphNodeKind.Expression);
                expression.valueType = type;
                expression.expression = code;
                return expression;
            }
            ShaderGraphNode Remap(ShaderGraphNode source, float inMin, float inMax, float outMin, float outMax)
            {
                var remap = Add(ShaderGraphNodeKind.Remap);
                Set(remap, "InMin", inMin); Set(remap, "InMax", inMax);
                Set(remap, "OutMin", outMin); Set(remap, "OutMax", outMax);
                Wire(source, remap, "In");
                return remap;
            }
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
                case ShaderGraphNodeKind.Distance:
                    Wire(Add(ShaderGraphNodeKind.UV), node, "A");
                    Set(node, "B", .5f, .5f);
                    return Remap(node, 0, Mathf.Sqrt(.5f), 0, 1);
                case ShaderGraphNodeKind.FresnelEffect:
                {
                    const string radial = "((P.xy - 0.5) * 2.35)";
                    var uv = Add(ShaderGraphNodeKind.UV);
                    var normal = Expression(ShaderGraphValueType.Float3,
                        $"float3({radial}, sqrt(saturate(1 - dot({radial}, {radial}))))");
                    Wire(uv, normal, "P");
                    Wire(normal, node, "Normal");
                    var sphere = Expression(ShaderGraphValueType.Float3,
                        $"(float3)(0.004 + (A.x * 0.93 + 0.04) * (1 - smoothstep(0.98, 1.025, length({radial}))))");
                    Wire(uv, sphere, "P"); Wire(node, sphere, "A");
                    return sphere;
                }
                case ShaderGraphNodeKind.DDX:
                case ShaderGraphNodeKind.DDY:
                case ShaderGraphNodeKind.FWidth:
                {
                    var field = Expression(ShaderGraphValueType.Float2,
                        "float2(smoothstep(0, 0.035, min(min(P.x - 0.18, 0.82 - P.x), min(P.y - 0.20, 0.90 - P.y - P.x * 0.43))), 0)");
                    var scalar = Add(ShaderGraphNodeKind.Split);
                    Wire(Add(ShaderGraphNodeKind.UV), field, "P"); Wire(field, scalar, "In");
                    Wire(scalar, node, "In", "X");
                    var magnitude = Expression(ShaderGraphValueType.Float, "saturate(abs(A) * 3.5)");
                    Wire(node, magnitude, "A");
                    return magnitude;
                }
                case ShaderGraphNodeKind.Premultiply:
                case ShaderGraphNodeKind.Unpremultiply:
                case ShaderGraphNodeKind.SetAlpha:
                {
                    var uv = Add(ShaderGraphNodeKind.UV);
                    var straight = Expression(ShaderGraphValueType.Float4,
                        "float4(lerp(float3(0.025, 0.42, 0.72), float3(0.30, 0.88, 0.74), P.y), saturate((0.42 - length(P.xy - 0.5)) / 0.24))");
                    Wire(uv, straight, "P");
                    bool setAlpha = node.kind == ShaderGraphNodeKind.SetAlpha;
                    if (setAlpha)
                    {
                        var mask = Expression(ShaderGraphValueType.Float4, "(float4)(saturate((0.42 - length(P.xy - 0.5)) / 0.14) * smoothstep(0.18, 0.8, P.x))");
                        Wire(uv, mask, "P"); Wire(mask, node, "Alpha"); Wire(straight, node, "Color");
                    }
                    else
                    {
                        var premultiply = node.kind == ShaderGraphNodeKind.Premultiply ? node : Add(ShaderGraphNodeKind.Premultiply);
                        Wire(straight, premultiply, "Color");
                        if (node != premultiply) Wire(premultiply, node, "Color");
                    }
                    // Alpha operations show the same checker and color swatch.
                    string coverage = setAlpha ? "A.a" : "smoothstep(0, 0.25, A.a)";
                    var tile = Image(WhimTexCheckerboard.Mask);
                    tile.wrap = ShaderGraphWrap.Repeat;
                    tile.filter = ShaderGraphFilter.Point;
                    var tileUV = Expression(ShaderGraphValueType.Float2, "P.xy * 3");
                    Wire(uv, tileUV, "P"); Wire(tileUV, tile, "UV");
                    var swatch = Expression(ShaderGraphValueType.Float4,
                        $"float4(lerp(0.043 + 0.045 * B.a, A.rgb, {coverage}), 1)");
                    Wire(tile, swatch, "B"); Wire(node, swatch, "A");
                    return swatch;
                }
                case ShaderGraphNodeKind.Reflection:
                case ShaderGraphNodeKind.Refract:
                {
                    bool refract = node.kind == ShaderGraphNodeKind.Refract;
                    Set(node, "Incident", .8f, -.6f);
                    Set(node, "Normal", 0, 1);
                    if (!refract)
                    {
                        node.FindInput("Incident").type = ShaderGraphValueType.Float3;
                        node.FindInput("Normal").type = ShaderGraphValueType.Float3;
                    }
                    var ray = Expression(ShaderGraphValueType.Float4, RayDiagram());
                    Wire(Add(ShaderGraphNodeKind.UV), ray, "P");
                    Wire(node, ray, "D", refract ? "Refracted" : "Out");
                    return ray;
                }
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
                case ShaderGraphNodeKind.Sine:
                case ShaderGraphNodeKind.Cosine:
                    Wire(Remap(ramp, 0, 1, 0, 2 * Mathf.PI), node, "In");
                    return Remap(node, -1, 1, 0, 1);
                case ShaderGraphNodeKind.InverseSoftSign:
                    Wire(Remap(ramp, 0, 1, -.8f, .8f), node, "In");
                    return Remap(node, -4, 4, 0, 1);
                case ShaderGraphNodeKind.Exponential:
                    Wire(Remap(ramp, 0, 1, 0, 3), node, "In");
                    return Remap(node, 1, Mathf.Exp(3), 0, 1);
                case ShaderGraphNodeKind.Log:
                    node.option = 1;
                    Wire(Remap(ramp, 0, 1, 1f / 64, 1), node, "In");
                    return Remap(node, -6, 0, 0, 1);
                case ShaderGraphNodeKind.SoftSign:
                case ShaderGraphNodeKind.HyperbolicTangent:
                    float extent = node.kind == ShaderGraphNodeKind.SoftSign ? 4 : 2;
                    Wire(Remap(ramp, 0, 1, -extent, extent), node, "In");
                    return Remap(node, -1, 1, 0, 1);
                case ShaderGraphNodeKind.Absolute:
                    Wire(Remap(ramp, 0, 1, -1, 1), node, "In");
                    return node;
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

        static string RayDiagram()
        {
            const string origin = "float2(0.49, 0.48)", incoming = "float2(-0.8, 0.6)";
            const string outgoing = "D.xy", tip = "(float2(0.49, 0.48) + 0.46 * D.xy)";
            string Stroke(string start, string direction, string length, string width = "0.018")
            {
                string delta = $"(P.xy - {start})";
                string distance = $"length({delta} - {direction} * clamp(dot({delta}, {direction}), 0, {length}))";
                return $"(1 - smoothstep({width} - 0.008, {width} + 0.008, {distance}))";
            }
            string input = Stroke(origin, incoming, "0.52");
            string line = Stroke(origin, outgoing, "0.46");
            string ArrowWing(string sign) => Stroke(tip, $"(-0.8 * D.xy {sign} 0.6 * float2(-D.y, D.x))", "0.13", "0.016");
            string arrow = $"max({line}, max({ArrowWing("+")}, {ArrowWing("-")}))";
            string backdrop = "lerp(float3(0.018, 0.05, 0.09), float3(0.025, 0.029, 0.038), step(0.48, P.y))";
            const string edge = "(1 - smoothstep(0.009, 0.022, abs(P.y - 0.48)))";
            return $"float4(lerp(lerp(lerp({backdrop}, float3(0.16, 0.22, 0.28), {edge})," +
                $" float3(0.97, 0.48, 0.14), {input}), float3(0.15, 0.83, 0.90), {arrow}), 1)";
        }
    }
}
