using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        // One deterministic reference for all image operations. Only the explicit baker owns it.
        static Texture2D reference;
        static bool IsImageExample(ShaderGraphNodeKind kind)
        {
            string category = ShaderGraphNodes.Category(kind);
            return kind == ShaderGraphNodeKind.SampleTexture || category == "Filter" ||
                category == "UV" || category.StartsWith("UV/", StringComparison.Ordinal);
        }

        static Texture2D CreateReference()
        {
            const int resolution = 256;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            { name = "Catalog reference", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var data = new Color[resolution * resolution];
            float Edge(float distance, float radius) => 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius - .006f, radius + .006f, distance));
            for (int y = 0; y < resolution; y++) for (int x = 0; x < resolution; x++)
            {
                var uv = new Vector2((x + .5f) / resolution, (y + .5f) / resolution);
                float gx = Mathf.Abs(Mathf.Repeat(uv.x * 4 + .5f, 1) - .5f);
                float gy = Mathf.Abs(Mathf.Repeat(uv.y * 4 + .5f, 1) - .5f);
                float grid = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.012f, .025f, Mathf.Min(gx, gy)));
                var color = Color.Lerp(new Color(.025f, .035f, .045f), new Color(.14f, .17f, .20f), grid);
                float circle = Vector2.Distance(uv, new Vector2(.32f, .66f));
                float height = Mathf.Clamp01(1 - circle / .25f);
                color = Color.Lerp(color, new Color(.26f, .62f, .70f) * (.55f + .45f * height), Edge(circle, .23f));
                // An asymmetric wedge, a ring, and a value ramp make direction and cropping readable.
                var p = uv - new Vector2(.69f, .3f);
                float wedge = Mathf.Max(Mathf.Abs(p.y) - .13f, Mathf.Max(-p.x - .19f, p.x + .65f * p.y - .18f));
                color = Color.Lerp(color, new Color(.80f, .50f, .17f), Edge(wedge, 0));
                float ring = Mathf.Abs(Vector2.Distance(uv, new Vector2(.77f, .76f)) - .105f);
                color = Color.Lerp(color, new Color(.8f, .8f, .8f), Edge(ring, .019f));
                if (uv.y < .105f) color = Color.Lerp(Color.black, Color.white, uv.x);
                if (uv.x > .08f && uv.x < .43f && uv.y > .18f && uv.y < .35f)
                {
                    float fine = ((Mathf.FloorToInt(uv.x * 43) + Mathf.FloorToInt(uv.y * 43)) & 1) == 0 ? .12f : .65f;
                    color = new Color(fine, fine, fine);
                }
                color.a = 1; data[y * resolution + x] = color;
            }
            texture.SetPixels(data); texture.Apply(); return texture;
        }

        static ShaderGraphNode ImageExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            reference ??= CreateReference();
            ShaderGraphNode Add(ShaderGraphNodeKind kind)
            { var added = ShaderGraphNodes.Create(kind, Vector2.zero); graph.nodes.Add(added); return added; }
            void Wire(ShaderGraphNode from, string fromPort, ShaderGraphNode to, string toPort) =>
                graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = fromPort, inputNode = to.id, inputPort = toPort });
            void Image(ShaderGraphNode targetNode, string port)
            {
                targetNode.wrap = ShaderGraphWrap.Repeat;
                var input = targetNode.FindInput(port);
                if (input == null) { input = new ShaderGraphInput { port = port, type = ShaderGraphValueType.Texture }; targetNode.inputs.Add(input); }
                input.resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = reference };
            }
            output = "Out";
            if (node.kind == ShaderGraphNodeKind.SampleTexture) { Image(node, "Texture"); return node; }

            if (ShaderGraphNodes.Category(node.kind) == "Filter")
            {
                var source = Add(ShaderGraphNodeKind.SampleTexture); Image(source, "Texture");
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
                    case ShaderGraphNodeKind.SlopeBlur: Wire(source, "R", node, "Slope"); Set(node, "Strength", 5); break;
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
                case ShaderGraphNodeKind.Twirl: Set(node, "Strength", 140); break;
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
