using UnityEngine;
using Kind = DCFApixels.WhimTex.ShaderGraphNodeKind;
using Type = DCFApixels.WhimTex.ShaderGraphValueType;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static bool IsAdditionalExample(Kind kind) => IsAdditionalCurve(kind) || kind is Kind.UV or Kind.CanvasPosition or
            Kind.Transform or Kind.ScatterInstance or Kind.Random or Kind.RandomRange or Kind.LightAttenuation or
            Kind.Arctangent2 or Kind.Projection or Kind.Rejection or Kind.RotateAboutAxis or Kind.Normalize or Kind.DotProduct or
            Kind.CrossProduct or Kind.Compare or Kind.Select or Kind.Switch or Kind.And or Kind.Or or Kind.Not or Kind.All or Kind.Any;

        sealed class CatalogGraph
        {
            readonly ShaderGraph graph;
            ShaderGraphNode uv;
            internal CatalogGraph(ShaderGraph graph) => this.graph = graph;
            internal ShaderGraphNode UV => uv ??= Add(Kind.UV);
            internal ShaderGraphNode Add(Kind kind)
            {
                var node = ShaderGraphNodes.Create(kind, Vector2.zero);
                graph.nodes.Add(node);
                return node;
            }
            internal void Wire(ShaderGraphNode from, ShaderGraphNode to, string input, string output = "Out") =>
                graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = output, inputNode = to.id, inputPort = input });
            internal ShaderGraphNode Expression(Type type, string code)
            {
                var node = Add(Kind.Expression);
                node.valueType = type;
                node.expression = code;
                return node;
            }
            internal ShaderGraphNode Field(Type type, string code)
            {
                var node = Expression(type, code);
                Wire(UV, node, "P");
                return node;
            }
            internal ShaderGraphNode Field(ShaderGraphNode source, string code, string output = "Out")
            {
                var field = Field(Type.Float4, code);
                Wire(source, field, "D", output);
                return field;
            }
            internal ShaderGraphNode Remap(ShaderGraphNode source, float low, float high, float min, float max)
            {
                var remap = Add(Kind.Remap);
                Set(remap, "InMin", low); Set(remap, "InMax", high);
                Set(remap, "OutMin", min); Set(remap, "OutMax", max);
                Wire(source, remap, "In");
                return remap;
            }
            internal static void SetValue(ShaderGraphNode node, string port, Type type, float x, float y, float z = 0, float w = 1)
            {
                var input = node.FindInput(port);
                if (input == null)
                {
                    input = new ShaderGraphInput { port = port };
                    node.inputs.Add(input);
                }
                input.type = type;
                input.value = new Vector4(x, y, z, w);
            }
        }

        static ShaderGraphNode AdditionalExample(ShaderGraph graph, ShaderGraphNode node, out string output)
        {
            output = "Out";
            var g = new CatalogGraph(graph);
            switch (node.kind)
            {
                case Kind.UV: case Kind.CanvasPosition: case Kind.Transform:
                    return CoordinateExample(g, node);
                case Kind.LightAttenuation:
                    var light = g.Expression(Type.Float4, "float4(0.004 + D.x * float3(1, 0.72, 0.35), 1)");
                    g.Wire(node, light, "D");
                    return light;
                case Kind.ScatterInstance:
                    var scatter = g.Add(Kind.Scatter);
                    var arrow = g.Add(Kind.SampleTexture);
                    arrow.wrap = ShaderGraphWrap.Clamp;
                    arrow.inputs.Add(new ShaderGraphInput { port = "Texture", type = Type.Texture,
                        resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = arrowReference ??= CreateArrowReference() } });
                    var tint = g.Expression(Type.Float4, "float4(lerp(float3(1.25, 0.3, 0.12), float3(0.06, 0.94, 1), D.x) * A.a, 1)");
                    g.Wire(arrow, tint, "A"); g.Wire(node, tint, "D", "Random"); g.Wire(tint, scatter, "Source");
                    Set(scatter, "Count", 3); Set(scatter, "Size", .94f); Set(scatter, "Jitter", .31f);
                    Set(scatter, "RotationRandom", .19f);
                    return scatter;
                case Kind.Random: case Kind.RandomRange:
                    var cell = g.Field(Type.Float2, "floor(P.xy * 6)");
                    if (node.kind == Kind.Random)
                    {
                        Set(node, "Seed", 0); g.Wire(cell, node, "Coordinates");
                        return node;
                    }
                    Set(node, "Min", .28f); Set(node, "Max", .59f); g.Wire(cell, node, "Seed");
                    return node;
                case Kind.Arctangent2:
                    var xy = g.Field(Type.Float2, "P.xy - 0.5");
                    var split = g.Add(Kind.Split);
                    g.Wire(xy, split, "In"); g.Wire(split, node, "Y", "Y"); g.Wire(split, node, "X", "X");
                    return g.Field(node, "float4(lerp(float3(0.09, 0.25, 0.83), float3(0.96, 0.53, 0.07), saturate(0.5 + D.x * 0.1591549431))" +
                        " * (0.52 + 0.48 * saturate(length(P.xy - 0.5) * 3)), 1)");
                case Kind.Compare: case Kind.Select: case Kind.Switch:
                    return LogicExample(g, node);
                case Kind.And: case Kind.Or: case Kind.Not: case Kind.All: case Kind.Any:
                    return BooleanExample(g, node);
                case Kind.Projection: case Kind.Rejection: case Kind.RotateAboutAxis: case Kind.Normalize:
                case Kind.DotProduct: case Kind.CrossProduct:
                    return VectorExample(g, node);
                default:
                    return AdditionalMathExample(g, node);
            }
        }

        const string DiagramBase = "float3(0.016, 0.023, 0.034)", Guide = "float3(0.16, 0.22, 0.29)";
        const string Amber = "float3(1, 0.52, 0.15)", Cyan = "float3(0.15, 0.80, 0.88)";
        static string Stroke(string start, string end, string width = "0.020")
        {
            string p = $"(P.xy - {start})", d = $"({end} - {start})";
            return $"(1 - smoothstep({width} - 0.007, {width} + 0.007, length({p} - {d} * saturate(dot({p}, {d}) / max(dot({d}, {d}), 0.00001)))))";
        }
        static string Paint(string backdrop, string rgb, string mask) => $"lerp({backdrop}, {rgb}, {mask})";
        static string Rgba(string rgb) => $"float4({rgb}, 1)";

        static ShaderGraphNode CoordinateExample(CatalogGraph g, ShaderGraphNode node)
        {
            var canvas = node.kind == Kind.CanvasPosition ? node : g.Add(Kind.CanvasPosition);
            if (node.kind == Kind.CanvasPosition)
            {
                var color = g.Expression(Type.Float4, "float4(saturate(C.xy), 0, 1)");
                g.Wire(canvas, color, "C", "Normalized");
                return color;
            }
            var local = node.kind == Kind.UV ? node : g.UV;
            const string tile = "max(abs(L.x - 0.5), abs(L.y - 0.5))";
            const string border = "(1 - smoothstep(0.006, 0.022, abs(" + tile + " - 0.5)))";
            string Grid(string uv) => $"(1 - smoothstep(0.036, 0.083, min(abs(frac({uv}.x * 5 + 0.5) - 0.5), abs(frac({uv}.y * 5 + 0.5) - 0.5))))";
            string backdrop = Paint(DiagramBase, Guide, Grid("C") + " * 0.40");
            string inside = node.kind == Kind.UV ? "float3(saturate(L.xy), 0)"
                : Paint("float3(0.028, 0.085, 0.120)", Cyan, Grid("C") + " * 0.70");
            string rgb = Paint(Paint(backdrop, inside, $"step({tile}, 0.5)"), "float3(0.40, 0.56, 0.67)", border);
            bool transform = node.kind == Kind.Transform;
            if (transform)
            {
                const string point = "float2(0.72, 0.70)";
                g.Wire(g.Expression(Type.Float2, point), node, "In");
                rgb = Paint(rgb, Amber, Stroke("D.xy", point, "0.010"));
                rgb = Paint(rgb, Amber, $"(1 - smoothstep(0.025, 0.044, length(C.xy - {point})))");
                rgb = Paint(rgb, Cyan, "(1 - smoothstep(0.029, 0.049, length(C.xy - D.xy)))");
            }
            // The marker segment uses canvas coordinates so both endpoints share a frame.
            var diagram = g.Expression(Type.Float4, Rgba(transform ? rgb.Replace("P.xy", "C.xy") : rgb));
            g.Wire(local, diagram, "L"); g.Wire(canvas, diagram, "C", "Normalized");
            if (transform) g.Wire(node, diagram, "D");
            return diagram;
        }

        static ShaderGraphNode LogicExample(CatalogGraph g, ShaderGraphNode node)
        {
            var ramp = g.Add(Kind.GradientFactor);
            if (node.kind == Kind.Switch)
            {
                node.floorPredicate = false;
                node.switchCases.Clear();
                node.inputs.RemoveAll(input => input.port is "A" or "B");
                var colors = new[] { new Vector3(.91f, .18f, .16f), new Vector3(.06f, .68f, .72f), new Vector3(.43f, .20f, .83f) };
                for (int i = 0; i < colors.Length; i++)
                {
                    string port = "Case" + i;
                    node.switchCases.Add(new ShaderGraphSwitchCase { port = port, comparison = ShaderGraphComparison.Less, threshold = .25f * (i + 1) });
                    var color = colors[i];
                    CatalogGraph.SetValue(node, port, Type.Float3, color.x, color.y, color.z);
                }
                CatalogGraph.SetValue(node, "Fallback", Type.Float3, .84f, .54f, .09f);
                g.Wire(ramp, node, "Predicate");
                return node;
            }
            var compare = node.kind == Kind.Compare ? node : g.Add(Kind.Compare);
            compare.option = 0;
            var threshold = g.Field(Type.Float4, "(float4)(0.5 + 0.17 * sin(P.y * 6.2831853))");
            g.Wire(ramp, compare, "A"); g.Wire(threshold, compare, "B");
            if (node.kind == Kind.Compare)
            {
                var mask = g.Add(Kind.Select);
                g.Wire(compare, mask, "Condition");
                return g.Field(mask, "float4(lerp(float3(0.013, 0.034, 0.056), float3(0.18, 0.78, 0.86), D.x)" +
                    " + 0.055 * (1 - smoothstep(0.04, 0.09, abs(frac(P.y * 5 + 0.5) - 0.5))), 1)");
            }
            var bars = g.Field(Type.Float4, "float4(lerp(float3(0.018, 0.21, 0.31), float3(0.12, 0.83, 0.84), step(frac(P.x * 7), 0.45)), 1)");
            var bands = g.Field(Type.Float4, "float4(lerp(float3(0.34, 0.07, 0.018), float3(0.99, 0.58, 0.08), step(frac(length(P.xy - 0.5) * 13), 0.45)), 1)");
            g.Wire(compare, node, "Condition"); g.Wire(bars, node, "True"); g.Wire(bands, node, "False");
            return node;
        }

        static ShaderGraphNode BooleanExample(CatalogGraph g, ShaderGraphNode node)
        {
            bool threeCircles = node.kind is Kind.All or Kind.Any;
            bool singleCircle = node.kind == Kind.Not;
            string top = threeCircles ? "0.62" : "0.56";
            string left = $"(length(P.xy - float2(0.36, {top})) - 0.27)";
            string right = $"(length(P.xy - float2(0.64, {top})) - 0.27)";
            const string lower = "(length(P.xy - float2(0.5, 0.3775)) - 0.27)";
            if (threeCircles)
            {
                // The third component is negative inside its circle. All and Any test nonzero values.
                var masks = g.Field(Type.Float3, $"float3(step({left}, 0), step({right}, 0), -step({lower}, 0))");
                g.Wire(masks, node, "In");
            }
            else
            {
                ShaderGraphNode Inside(string field)
                {
                    var comparison = g.Add(Kind.Compare);
                    g.Wire(g.Field(Type.Float4, $"(float4){field}"), comparison, "A");
                    return comparison;
                }
                g.Wire(Inside(left), node, singleCircle ? "In" : "A");
                if (!singleCircle) g.Wire(Inside(right), node, "B");
            }
            var result = g.Add(Kind.Select);
            g.Wire(node, result, "Condition");
            string Outline(string field) => $"(1 - smoothstep(0.015, 0.028, abs({field})))";
            string outlines = $"float3(0.37, 0.17, 0.07) * {Outline(left)}";
            if (!singleCircle) outlines += $" + float3(0.16, 0.10, 0.34) * {Outline(right)}";
            if (threeCircles) outlines += $" + float3(0.13, 0.34, 0.13) * {Outline(lower)}";
            return g.Field(result, $"float4(0.015 + {outlines} + float3(0.06, 0.56, 0.67) * D.x, 1)");
        }

        static ShaderGraphNode VectorExample(CatalogGraph g, ShaderGraphNode node)
        {
            void Vector(string port, float x, float y, float z = 0) => CatalogGraph.SetValue(node, port, Type.Float3, x, y, z, 0);
            switch (node.kind)
            {
                case Kind.DotProduct:
                    g.Wire(g.Field(Type.Float3, "normalize(float3(P.xy - 0.5, 0))"), node, "A");
                    Vector("B", 1, 0);
                    const string radius = "length(P.xy - 0.5)";
                    string ring = $"(smoothstep(0.22, 0.25, {radius}) * (1 - smoothstep(0.41, 0.44, {radius})))";
                    string rgb = Paint(DiagramBase, "lerp(float3(0.07, 0.22, 0.52), float3(0.98, 0.63, 0.14), 0.5 + 0.5 * D.x)", ring);
                    return g.Field(node, Rgba(Paint(rgb, Amber, Stroke("float2(0.50, 0.50)", "float2(0.68, 0.50)"))));
                case Kind.Normalize:
                    g.Wire(g.Field(Type.Float3, "P.y > 0.5 ? float3(1.45, 0.45, 0) : float3(0.62, -0.3, 0)"), node, "In");
                    const string left = "float2(0.075, (P.y > 0.5 ? 0.70 : 0.32))";
                    const string right = "float2(0.57, (P.y > 0.5 ? 0.70 : 0.32))";
                    string input = $"({left} + (P.y > 0.5 ? float2(0.35, 0.109) : float2(0.15, -0.073)))";
                    string normalized = $"({right} + 0.34 * D.xy)";
                    rgb = Paint(DiagramBase, Guide, "(1 - smoothstep(0.005, 0.016, abs(P.x - 0.5)))");
                    rgb = Paint(Paint(rgb, Amber, Stroke(left, input)), Cyan, Stroke(right, normalized));
                    return g.Field(node, Rgba(rgb));
                case Kind.CrossProduct:
                    Vector("A", .60f, 0); Vector("B", 0, .55f);
                    const string center = "float2(0.48, 0.35)";
                    rgb = Paint(DiagramBase, Amber, Stroke(center, "float2(0.90, 0.22)"));
                    rgb = Paint(rgb, "float3(0.50, 0.29, 0.91)", Stroke(center, "float2(0.14, 0.20)"));
                    return g.Field(node, Rgba(Paint(rgb, Cyan, Stroke(center, "(float2(0.48, 0.35) + float2(0, 1.48 * D.z))"))));
            }
            const string origin = "float2(0.22, 0.22)";
            bool rotate = node.kind == Kind.RotateAboutAxis;
            if (rotate)
            {
                Vector("In", .62f, 0); Vector("Axis", 0, 0, 1); Set(node, "Rotation", .94f);
            }
            else { Vector("A", .56f, .53f); Vector("B", 1, 0); }
            string tip = rotate ? "float2(0.84, 0.22)" : "float2(0.78, 0.75)";
            string backdrop = Paint(DiagramBase, Guide, Stroke("float2(0.08, 0.22)", "float2(0.94, 0.22)", "0.012"));
            if (!rotate)
            {
                backdrop = Paint(backdrop, Guide, Stroke(tip, "float2(0.78, 0.22)", "0.009"));
                backdrop = Paint(backdrop, Guide, Stroke(tip, "float2(0.22, 0.75)", "0.009"));
            }
            backdrop = Paint(backdrop, Amber, Stroke(origin, tip));
            return g.Field(node, Rgba(Paint(backdrop, Cyan, Stroke(origin, $"({origin} + D.xy)"))));
        }
    }
}
