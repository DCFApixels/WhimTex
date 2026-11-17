using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        // Shared color reference and smooth auxiliary height. Only the explicit baker owns them.
        static Texture2D reference, heightReference;
        static bool IsImageExample(ShaderGraphNodeKind kind)
        {
            string category = ShaderGraphNodes.Category(kind);
            return IsColorExample(kind) || IsReliefExample(kind) || IsPatternExample(kind) || IsAdditionalExample(kind) || category == "Filter" ||
                category == "UV" || category.StartsWith("UV/", StringComparison.Ordinal);
        }

        static readonly Vector2[] referenceArrow = {
            new Vector2(.0514f, .4335f), new Vector2(.3464f, .4335f), new Vector2(.3464f, .2932f),
            new Vector2(.6886f, .56f), new Vector2(.3464f, .8268f), new Vector2(.3464f, .6865f), new Vector2(.0514f, .6865f)
        };
        static float ReferenceArrowDistance(Vector2 p)
        {
            float distance = float.PositiveInfinity;
            bool inside = false;
            for (int i = 0, j = referenceArrow.Length - 1; i < referenceArrow.Length; j = i++)
            {
                var a = referenceArrow[j]; var b = referenceArrow[i]; var segment = b - a;
                float along = Mathf.Clamp01(Vector2.Dot(p - a, segment) / segment.sqrMagnitude);
                distance = Mathf.Min(distance, Vector2.Distance(p, a + segment * along));
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside ? -distance : distance;
        }
        static Texture2D CreateReference(bool height = false)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true) {
                name = height ? "Catalog height reference" : "Catalog reference", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            float Edge(float d) => 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.004f, .004f, d));
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + .5f) / size, (y + .5f) / size);
                if (height)
                {
                    float value = .5f + .22f * Mathf.Sin(6 * Mathf.PI * p.x) * Mathf.Sin(4 * Mathf.PI * p.y)
                        + .12f * Mathf.Cos(4 * Mathf.PI * p.x + 6 * Mathf.PI * p.y);
                    pixels[y * size + x] = new Color(value, value, value, 1); continue;
                }
                int parity = (Mathf.FloorToInt(p.x * 4) + Mathf.FloorToInt(p.y * 4)) & 1;
                Color color = parity == 0 ? new Color(.026f, .035f, .043f) : new Color(.060f, .076f, .087f);
                float gx = Mathf.Abs(Mathf.Repeat(p.x + .125f, .25f) - .125f);
                float gy = Mathf.Abs(Mathf.Repeat(p.y + .125f, .25f) - .125f);
                color = Color.Lerp(color, new Color(.19f, .23f, .25f), Edge(Mathf.Min(gx, gy) - .010f));
                // Straight tail and a thin outline entirely inside the arrow silhouette.
                float distance = ReferenceArrowDistance(p);
                float shape = Edge(distance);
                var bright = new Color(.34f, .70f, .76f);
                float ramp = Mathf.SmoothStep(.22f, 1f, Mathf.InverseLerp(.0514f, .6886f, p.x));
                color = Color.Lerp(color, bright * ramp, shape);
                const float outline = .6f / ShaderGraphCatalogThumbnails.DisplaySize;
                float inner = Mathf.Clamp01(shape - Edge(distance + outline));
                color = Color.Lerp(color, bright, inner);
                // A skewed rectangle has horizontal edges and one slanted side.
                var wedgePoint = p - new Vector2(.68f, .29f);
                wedgePoint.x = -wedgePoint.x;
                float wedge = Mathf.Max(Mathf.Abs(wedgePoint.y) - .13f,
                    Mathf.Max(-wedgePoint.x - .20f, wedgePoint.x + .65f * wedgePoint.y - .18f));
                color = Color.Lerp(color, new Color(.80f, .50f, .17f), Edge(wedge));
                float ring = Mathf.Abs(Vector2.Distance(p, new Vector2(.77f, .76f)) - .107f);
                color = Color.Lerp(color, new Color(.8f, .8f, .8f), Edge(ring - .023f));
                if (p.y < .105f) color = new Color(p.x, p.x, p.x);
                color.a = 1; pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }

        static ShaderGraphNode ImageExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            if (IsColorExample(node.kind)) return ColorExample(graph, node, out output);
            if (IsReliefExample(node.kind)) return ReliefExample(graph, node, out output);
            if (IsPatternExample(node.kind)) return PatternExample(graph, node, out output);
            if (IsAdditionalExample(node.kind)) return AdditionalExample(graph, node, out output);
            reference ??= CreateReference();
            ShaderGraphNode Add(ShaderGraphNodeKind kind)
            { var added = ShaderGraphNodes.Create(kind, Vector2.zero); graph.nodes.Add(added); return added; }
            void Wire(ShaderGraphNode from, string fromPort, ShaderGraphNode to, string toPort) =>
                graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = fromPort, inputNode = to.id, inputPort = toPort });
            void Image(ShaderGraphNode targetNode, string port, bool height = false)
            {
                targetNode.wrap = ShaderGraphWrap.Repeat;
                var input = targetNode.FindInput(port);
                if (input == null) { input = new ShaderGraphInput { port = port, type = ShaderGraphValueType.Texture }; targetNode.inputs.Add(input); }
                var texture = height || port == "Height" ? (heightReference ??= CreateReference(true)) : reference;
                input.resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = texture };
            }
            output = "Out";

            if (ShaderGraphNodes.Category(node.kind) == "Filter")
            {
                var source = Add(ShaderGraphNodeKind.SampleTexture); Image(source, "Texture", node.kind == ShaderGraphNodeKind.Caustics);
                bool scalar = node.kind is ShaderGraphNodeKind.Caustics or ShaderGraphNodeKind.FadeTransition;
                Wire(source, scalar ? "R" : "Out", node, node.kind switch {
                    ShaderGraphNodeKind.Caustics => "Height", ShaderGraphNodeKind.FadeTransition => "NoiseValue", _ => "In" });
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Blur: Set(node, "Radius", 3); break;
                    case ShaderGraphNodeKind.Sharpen: Set(node, "Radius", 2); Set(node, "Strength", 2); break;
                    case ShaderGraphNodeKind.Dilate: case ShaderGraphNodeKind.Erode: Set(node, "Radius", 2); break;
                    case ShaderGraphNodeKind.EdgeDetection: Set(node, "Radius", 1); break;
                    case ShaderGraphNodeKind.DirectionalBlur: Set(node, "Radius", 7); Set(node, "Angle", 35); break;
                    case ShaderGraphNodeKind.RadialBlur: Set(node, "Angle", 30); break;
                    case ShaderGraphNodeKind.RGBShift: node.option = 1; Set(node, "Amount", 3); Set(node, "Angle", 15); break;
                    case ShaderGraphNodeKind.SlopeBlur:
                        var slope = Add(ShaderGraphNodeKind.SampleTexture); Image(slope, "Texture", true);
                        Wire(slope, "R", node, "Slope"); Set(node, "Strength", 5); break;
                    case ShaderGraphNodeKind.BilateralBlur: Set(node, "Radius", 3); Set(node, "Tolerance", .2f); break;
                    case ShaderGraphNodeKind.Median: Set(node, "Radius", 2); break;
                    case ShaderGraphNodeKind.Kuwahara: Set(node, "Radius", 3); break;
                    case ShaderGraphNodeKind.Supersample: node.option = 2; break;
                    case ShaderGraphNodeKind.MakeTileable: Set(node, "BlendWidth", .45f); break;
                    case ShaderGraphNodeKind.FadeTransition: Set(node, "FadeValue", .6f); Set(node, "FadeContrast", 4); break;
                    case ShaderGraphNodeKind.Caustics:
                        node.bufferSize = new Vector2Int(128, 128);
                        Set(node, "Strength", .015f); Set(node, "Dispersion", 0); output = "Value"; break;
                    default: throw new NotSupportedException("Missing filter catalog recipe: " + node.kind);
                }
                return node;
            }

            string uvPort = ShaderGraphNodes.Outputs(node)[0];
            ShaderGraphNode uvNode = node;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.TransformUV: Set(node, "Rotation", 25); Set(node, "Scale", .85f); break;
                case ShaderGraphNodeKind.ApplyTransform2D:
                    node.inputs.Add(new ShaderGraphInput { port = "Transform", type = ShaderGraphValueType.Transform2D,
                        resource = new ShaderParameter { type = ShaderParameterType.Transform2D,
                            transformValue = new ShaderTransform { position = new Double2(.5, .5), size = new Double2(.8, .65), rotation = 20 } } }); break;
                case ShaderGraphNodeKind.PixelateUV: Set(node, "CellSize", 5); break;
                case ShaderGraphNodeKind.PolarCoordinates: break;
                case ShaderGraphNodeKind.Repeat: Set(node, "Scale", 2); break;
                case ShaderGraphNodeKind.Mirror: node.option = 2; break;
                case ShaderGraphNodeKind.Kaleidoscope: Set(node, "Sectors", 5); break;
                case ShaderGraphNodeKind.Twirl: Set(node, "Strength", 140f / 360); break;
                case ShaderGraphNodeKind.BulgePinch: Set(node, "Strength", .7f); break;
                case ShaderGraphNodeKind.Wave: Set(node, "Strength", .08f); Set(node, "Frequency", 3); break;
                case ShaderGraphNodeKind.Ripple: Set(node, "Strength", .07f); Set(node, "Frequency", 4); break;
                case ShaderGraphNodeKind.RadialShear: Set(node, "Strength", 3); break;
                case ShaderGraphNodeKind.TileUV: Set(node, "Count", 2); node.option = 1; break;
                case ShaderGraphNodeKind.PerspectiveUV:
                    Set(node, "TopLeft", .23f, .92f); Set(node, "TopRight", .77f, .8f); break;
                case ShaderGraphNodeKind.Flipbook: Set(node, "Columns", 2); Set(node, "Rows", 2); Set(node, "Frame", 2); break;
                case ShaderGraphNodeKind.ParallaxMapping: case ShaderGraphNodeKind.ParallaxOcclusionMapping:
                    Image(node, "Height"); Set(node, "Amplitude", .3f); break;
                case ShaderGraphNodeKind.HeightToDistortion: case ShaderGraphNodeKind.NormalToDistortion:
                case ShaderGraphNodeKind.VectorDisplacement: case ShaderGraphNodeKind.FlowWarp:
                    var gradient = node.kind == ShaderGraphNodeKind.HeightToDistortion ? node : Add(ShaderGraphNodeKind.HeightToDistortion);
                    Image(gradient, "Height"); Set(gradient, "Strength", 5);
                    if (node.kind == ShaderGraphNodeKind.NormalToDistortion)
                    {
                        graph.nodes.Remove(gradient);
                        var normal = Add(ShaderGraphNodeKind.HeightToNormal); Image(normal, "Height"); Set(normal, "Strength", 5);
                        Wire(normal, "Out", node, "Normal"); Set(node, "Strength", .12f);
                    }
                    if (node.kind == ShaderGraphNodeKind.VectorDisplacement)
                    { Wire(gradient, "Out", node, "Displacement"); Set(node, "Strength", 1); }
                    else if (node.kind == ShaderGraphNodeKind.FlowWarp)
                    { Wire(gradient, "Out", node, "Flow"); Set(node, "Strength", 20); }
                    else
                    { uvNode = Add(ShaderGraphNodeKind.VectorDisplacement); Set(uvNode, "Strength", 1); Wire(node, "Out", uvNode, "Displacement"); }
                    uvPort = "Out"; break;
                default: throw new NotSupportedException("Missing UV catalog recipe: " + node.kind);
            }
            // A separate lookup avoids a feedback edge when the same reference supplies a height/flow input.
            var result = Add(ShaderGraphNodeKind.SampleTexture); Image(result, "Texture");
            Wire(uvNode, uvPort, result, "UV"); return result;
        }
    }
}
