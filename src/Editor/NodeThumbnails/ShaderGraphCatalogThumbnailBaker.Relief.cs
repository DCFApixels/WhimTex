using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static Texture2D reliefReference, reliefDetailReference;
        static bool IsReliefExample(ShaderGraphNodeKind kind) => kind is
            ShaderGraphNodeKind.HeightBlend or ShaderGraphNodeKind.HeightCurvature or
            ShaderGraphNodeKind.HeightToAO or ShaderGraphNodeKind.HeightShadow or
            ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.NormalBlend or
            ShaderGraphNodeKind.NormalStrength or ShaderGraphNodeKind.NormalCurvature or
            ShaderGraphNodeKind.NormalEncode or ShaderGraphNodeKind.NormalDecode or
            ShaderGraphNodeKind.NormalReconstructZ or ShaderGraphNodeKind.NormalUnpack or ShaderGraphNodeKind.NormalLighting;

        static float ReliefHeight(Vector2 p)
        {
            float radius = Vector2.Distance(p, new Vector2(.29f, .71f)) / .245f;
            float dome = Mathf.Pow(Mathf.Max(0, 1 - radius * radius), 2) * .67f;
            var q = p - new Vector2(.70f, .28f);
            float distance = Mathf.Max(Mathf.Abs(q.x) - .235f, Mathf.Abs(q.y) - .175f);
            float block = Mathf.SmoothStep(0, .62f, Mathf.Clamp01(-distance / .075f));
            var a = new Vector2(.12f, .13f); var ab = new Vector2(.76f, .74f);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = Vector2.Distance(p, a + t * ab);
            float groove = .23f * Mathf.Exp(-d * d / (.034f * .034f));
            return .28f + dome + block - groove;
        }

        static Texture2D CreateReliefReference(bool rotated = false)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true) {
                name = rotated ? "Catalog relief detail" : "Catalog relief", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + .5f) / size, (y + .5f) / size);
                float height = ReliefHeight(rotated ? new Vector2(1 - p.y, p.x) : p);
                colors[y * size + x] = new Color(height, height, height, 1);
            }
            texture.SetPixels(colors); texture.Apply(); return texture;
        }

        static ShaderGraphNode ReliefExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            reliefReference ??= CreateReliefReference();
            ShaderGraphNode Add(ShaderGraphNodeKind kind)
            { var added = ShaderGraphNodes.Create(kind, Vector2.zero); graph.nodes.Add(added); return added; }
            void Wire(ShaderGraphNode source, string from, ShaderGraphNode destination, string to) =>
                graph.edges.Add(new ShaderGraphEdge { outputNode = source.id, outputPort = from, inputNode = destination.id, inputPort = to });
            void Image(ShaderGraphNode targetNode, string port, bool detail = false)
            {
                var input = targetNode.FindInput(port);
                if (input == null) { input = new ShaderGraphInput { port = port, type = ShaderGraphValueType.Texture }; targetNode.inputs.Add(input); }
                input.resource = new ShaderParameter { type = ShaderParameterType.Texture2D,
                    textureValue = detail ? (reliefDetailReference ??= CreateReliefReference(true)) : reliefReference };
                targetNode.wrap = ShaderGraphWrap.Clamp;
            }
            ShaderGraphNode Sample(bool detail = false)
            { var sample = Add(ShaderGraphNodeKind.SampleTexture); Image(sample, "Texture", detail); return sample; }
            ShaderGraphNode Normal(bool detail = false)
            { var normal = Add(ShaderGraphNodeKind.HeightToNormal); Image(normal, "Height", detail); if (detail) Set(normal, "Strength", 5); return normal; }

            output = "Out";
            if (ShaderGraphNodes.Category(node.kind) == "Height")
            {
                var source = Sample();
                Wire(source, "R", node, node.kind == ShaderGraphNodeKind.HeightBlend ? "Bottom" : "Height");
                node.wrap = ShaderGraphWrap.Clamp;
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.HeightBlend: Wire(Sample(true), "R", node, "Top"); output = "Height"; break;
                    case ShaderGraphNodeKind.HeightCurvature: Set(node, "Radius", 2); Set(node, "Strength", 12); output = "Convex"; break;
                    case ShaderGraphNodeKind.HeightToAO:
                        Set(node, "Radius", 12); Set(node, "HeightScale", 20); Set(node, "Directions", 16); Set(node, "Steps", 6); break;
                    case ShaderGraphNodeKind.HeightShadow: Set(node, "HeightScale", 24); Set(node, "Distance", 32); break;
                    default: throw new NotSupportedException("Missing height catalog recipe: " + node.kind);
                }
                return node;
            }
            if (node.kind == ShaderGraphNodeKind.HeightToNormal) { Image(node, "Height"); return node; }
            var normalSource = Normal();
            switch (node.kind)
            {
                case ShaderGraphNodeKind.NormalBlend:
                    Wire(normalSource, "Out", node, "Base"); Wire(Normal(true), "Out", node, "Detail"); break;
                case ShaderGraphNodeKind.NormalReconstructZ: Wire(normalSource, "Out", node, "In"); break;
                case ShaderGraphNodeKind.NormalDecode:
                case ShaderGraphNodeKind.NormalUnpack:
                    var encoded = Add(ShaderGraphNodeKind.NormalEncode); Wire(normalSource, "Out", encoded, "Normal");
                    Wire(encoded, "Out", node, node.kind == ShaderGraphNodeKind.NormalDecode ? "Normal" : "In");
                    if (node.kind == ShaderGraphNodeKind.NormalUnpack) node.option = 1;
                    break;
                default:
                    Wire(normalSource, "Out", node, "Normal");
                    if (node.kind == ShaderGraphNodeKind.NormalStrength) Set(node, "Strength", 2);
                    if (node.kind == ShaderGraphNodeKind.NormalCurvature)
                    { Set(node, "Radius", 2); Set(node, "Strength", 3); output = "Convex"; }
                    if (node.kind == ShaderGraphNodeKind.NormalLighting)
                    {
                        Set(normalSource, "Strength", 24);
                        node.FindInput("Color").value = new Vector4(.45f, .45f, .45f, 1);
                    }
                    break;
            }
            return node;
        }
    }
}
