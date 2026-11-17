using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static Texture2D colorReference;
        static bool IsColorExample(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.Hue or ShaderGraphNodeKind.Saturation or ShaderGraphNodeKind.Exposure or
            ShaderGraphNodeKind.Lightness or ShaderGraphNodeKind.Levels or ShaderGraphNodeKind.ColorBalance or
            ShaderGraphNodeKind.ChannelMixer or ShaderGraphNodeKind.WhiteBalance or ShaderGraphNodeKind.Posterize or
            ShaderGraphNodeKind.Luminance or ShaderGraphNodeKind.GradientMap or ShaderGraphNodeKind.ColorCurves or
            ShaderGraphNodeKind.ColorMask or ShaderGraphNodeKind.HueRange or ShaderGraphNodeKind.ReplaceColor or
            ShaderGraphNodeKind.Contrast or ShaderGraphNodeKind.Dither or ShaderGraphNodeKind.PaletteQuantize or
            ShaderGraphNodeKind.MixColor or ShaderGraphNodeKind.Blend or ShaderGraphNodeKind.ConvertColor;

        static Texture2D CreateColorReference()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) {
                name = "Catalog color table", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size, v = (y + .5f) / size;
                Color color;
                if (v < 5f / 32) color = new Color(u, u, u);
                else if (v < 6f / 32) color = new Color(.12f, .12f, .12f);
                else
                {
                    float lightness = Mathf.Lerp(.06f, .94f, (v - 6f / 32) / (26f / 32));
                    float chroma = (1 - Mathf.Abs(2 * lightness - 1)) * .92f;
                    float neutral = lightness - chroma / 2;
                    color = Color.HSVToRGB(u, 1, 1) * chroma + new Color(neutral, neutral, neutral);
                }
                color.a = 1; pixels[y * size + x] = color.linear;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        static ShaderGraphNode ColorExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            colorReference ??= CreateColorReference();
            var source = ShaderGraphNodes.Create(ShaderGraphNodeKind.SampleTexture, Vector2.zero);
            source.wrap = ShaderGraphWrap.Clamp;
            source.inputs.Add(new ShaderGraphInput { port = "Texture", type = ShaderGraphValueType.Texture,
                resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = colorReference } });
            graph.nodes.Add(source);
            string input = node.kind == ShaderGraphNodeKind.MixColor ? "A" : node.kind == ShaderGraphNodeKind.Blend ? "Background" : "Color";
            graph.edges.Add(new ShaderGraphEdge { outputNode = source.id, outputPort = "Out", inputNode = node.id, inputPort = input });
            output = "Out";
            void Vector(string port, float x, float y, float z, float w = 0) => node.FindInput(port).value = new Vector4(x, y, z, w);
            void Resource(string port, ShaderGraphValueType type, ShaderParameter value) =>
                node.inputs.Add(new ShaderGraphInput { port = port, type = type, resource = value });
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Hue: Set(node, "Offset", 1f / 6); break;
                case ShaderGraphNodeKind.Saturation: Set(node, "Amount", .25f); break;
                case ShaderGraphNodeKind.Exposure: Set(node, "Stops", 1); break;
                case ShaderGraphNodeKind.Lightness: Set(node, "Amount", 25); break;
                case ShaderGraphNodeKind.Contrast: Set(node, "Contrast", 1.8f); break;
                case ShaderGraphNodeKind.Levels: Set(node, "InputRange", .1f, .85f); Set(node, "Gamma", 1.4f); break;
                case ShaderGraphNodeKind.Posterize: Set(node, "Levels", 4); break;
                case ShaderGraphNodeKind.WhiteBalance: Set(node, "Temperature", .4f); Set(node, "Tint", -.15f); break;
                case ShaderGraphNodeKind.ColorBalance: Vector("Shadows", -.06f, 0, .1f); Vector("Highlights", .15f, .04f, -.06f); break;
                case ShaderGraphNodeKind.ChannelMixer: Vector("Red", 0, 1, 0); Vector("Green", 0, 0, 1); Vector("Blue", 1, 0, 0); break;
                case ShaderGraphNodeKind.HueRange: Set(node, "Range", 11f / 12, 1f / 12); Set(node, "Feather", 1f / 24); break;
                case ShaderGraphNodeKind.ColorMask: Vector("KeyColor", 0, 1, 0, 1); Set(node, "Range", .3f); Set(node, "Fuzziness", .25f); break;
                case ShaderGraphNodeKind.ReplaceColor:
                    Vector("From", 0, 1, 0, 1); Vector("To", 1, .1f, .5f, 1); Set(node, "Range", .3f); Set(node, "Fuzziness", .25f); break;
                case ShaderGraphNodeKind.Dither: Set(node, "Levels", 3); Set(node, "PixelSize", 2); break;
                case ShaderGraphNodeKind.MixColor: Vector("B", .05f, .15f, .8f, 1); Set(node, "T", .5f); break;
                case ShaderGraphNodeKind.Blend: Vector("Foreground", .8f, .2f, .04f, 1); Set(node, "Opacity", .5f); break;
                case ShaderGraphNodeKind.ConvertColor:
                {
                    node.inputColorSpace = ShaderGraphColorSpace.LinearRGB;
                    node.outputColorSpace = ShaderGraphColorSpace.HSV;
                    ShaderGraphNode Add(ShaderGraphNodeKind kind)
                    {
                        var created = ShaderGraphNodes.Create(kind, Vector2.zero);
                        graph.nodes.Add(created);
                        return created;
                    }
                    void Wire(ShaderGraphNode from, ShaderGraphNode to, string port) =>
                        graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = "Out", inputNode = to.id, inputPort = port });
                    var uv = Add(ShaderGraphNodeKind.UV);
                    var rows = Add(ShaderGraphNodeKind.Expression);
                    rows.valueType = ShaderGraphValueType.Float2;
                    rows.expression = "float2(P.x, frac(P.y * 2))";
                    Wire(uv, rows, "P"); Wire(rows, source, "UV");
                    var swatch = Add(ShaderGraphNodeKind.Expression);
                    swatch.valueType = ShaderGraphValueType.Float4;
                    swatch.expression = "abs(P.y - 0.5) < 0.015625 ? float4(0.02, 0.02, 0.02, 1) : (P.y > 0.5 ? RGB : HSV)";
                    Wire(uv, swatch, "P"); Wire(source, swatch, "RGB"); Wire(node, swatch, "HSV");
                    return swatch;
                }
                case ShaderGraphNodeKind.GradientMap:
                case ShaderGraphNodeKind.PaletteQuantize:
                    var gradient = new WhimTexGradient();
                    gradient.SetKeys(new[] { new GradientColorKey(new Color(.04f, .06f, .2f), 0),
                        new GradientColorKey(new Color(.9f, .2f, .06f), .5f), new GradientColorKey(new Color(1, .9f, .4f), 1) },
                        new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                    Resource(node.kind == ShaderGraphNodeKind.GradientMap ? "Gradient" : "Palette", ShaderGraphValueType.Gradient,
                        new ShaderParameter { type = ShaderParameterType.Gradient, gradientValue = gradient });
                    if (node.kind == ShaderGraphNodeKind.PaletteQuantize) Set(node, "Colors", 5);
                    break;
                case ShaderGraphNodeKind.ColorCurves:
                    var curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.25f, .1f), new Keyframe(.75f, .9f), new Keyframe(1, 1));
                    for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
                    Resource("Master", ShaderGraphValueType.Curve, new ShaderParameter { type = ShaderParameterType.Curve, curveValue = curve });
                    break;
            }
            return node;
        }
    }
}
