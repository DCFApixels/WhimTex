using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesProcedural;
        Value ProceduralShared(ShaderGraphNode n, ShaderGraphValueType type, Func<string> expression)
        {
            string key = n.id + ":$procedural";
            if (values.TryGetValue(key, out var cached)) return cached;
            string text = expression();
            string variable = "gp" + values.Count;
            body.Append("#line 1 \"").Append(NodeSourceName(n)).AppendLine("\"");
            body.Append(Type(type)).Append(' ').Append(variable).Append(" = ").Append(text).AppendLine(";");
            var result = new Value(type, variable); values.Add(key, result); return result;
        }
        Value EmitProcedural(ShaderGraphNode n, string port)
        {
            usesProcedural = true;
            string V(string name) => Input(n, name).text;
            var type = ShaderGraphValueType.Float;
            string text;
            switch (n.kind)
            {
                case ShaderGraphNodeKind.PixelateUV:
                    type = ShaderGraphValueType.Float2; text = $"GraphPixelateUV({V("UV")},{V("CellSize")},{V("Offset")})"; break;
                case ShaderGraphNodeKind.Random: text = $"GraphRandom({V("Coordinates")},{V("Seed")})"; break;
                case ShaderGraphNodeKind.Checkerboard:
                {
                    string Field() => $"float4({V("UV")}*{V("Scale")}*0.5,{V("Offset")}*0.5)";
                    string position = PeriodicField(n, ShaderGraphValueType.Float4, Field, out string width);
                    text = $"GraphCheckerboard({position},{width})"; break;
                }
                case ShaderGraphNodeKind.Stripes:
                {
                    string Field() => $"float2(dot({V("UV")},GraphDirection({V("Angle")}))*{V("Frequency")},{V("Phase")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float2, Field, out string width);
                    text = $"GraphStripeProfile({position},{V("Width")},{width},{n.option})"; break;
                }
                case ShaderGraphNodeKind.Grid:
                {
                    string Field() => $"float4({V("UV")}*{V("Scale")}+{V("Width")}*0.5,{V("Offset")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float4, Field, out string width);
                    text = $"GraphGridPhase({position},{V("Width")},{width})"; break;
                }
                case ShaderGraphNodeKind.Rings:
                {
                    string Field() => $"float2(length({V("UV")}-{V("Center")})*{V("Frequency")},{V("Phase")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float2, Field, out string width);
                    text = $"GraphPulse({position},{V("Width")},{width})"; break;
                }
                case ShaderGraphNodeKind.GradientFactor: text = $"GraphGradientFactor(({V("UV")}-{V("Center")})*{V("Scale")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Shape:
                    var shape = ProceduralShared(n, ShaderGraphValueType.Float, () => ShapeDistanceExpression(n));
                    text = port == "Distance" ? shape.text : $"GraphDistanceMask({shape.text},{V("Feather")}" +
                        (sampledEvaluation ? "," + SampledWidth(n, ShaderGraphValueType.Float, () => ShapeDistanceExpression(n)) : "") + ")"; break;
                case ShaderGraphNodeKind.CombineDistance:
                    var distance = ProceduralShared(n, ShaderGraphValueType.Float, () => $"GraphCombineDistance({V("A")},{V("B")},{V("Smoothness")},{n.option})");
                    text = port == "Mask" ? $"GraphDistanceMask({distance.text},{V("Feather")}" +
                        (sampledEvaluation ? "," + SampledWidth(n, ShaderGraphValueType.Float, () => $"GraphCombineDistance({V("A")},{V("B")},{V("Smoothness")},{n.option})") : "") + ")" : distance.text; break;
                case ShaderGraphNodeKind.PolarCoordinates: type = ShaderGraphValueType.Float2; text = $"GraphPolar({V("UV")},{V("Center")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Repeat:
                    type = ShaderGraphValueType.Float2;
                    var repeat = ProceduralShared(n, ShaderGraphValueType.Float4, () => $"GraphRepeat({V("UV")}*{V("Scale")}+{V("Offset")})");
                    text = repeat.text + (port == "Cell" ? ".zw" : ".xy"); break;
                case ShaderGraphNodeKind.Mirror: type = ShaderGraphValueType.Float2; text = $"GraphMirror({V("UV")},{V("Center")},{n.option})"; break;
                case ShaderGraphNodeKind.Kaleidoscope: type = ShaderGraphValueType.Float2; text = $"GraphKaleidoscope({V("UV")},{V("Center")},{V("Sectors")},{V("Angle")})"; break;
                case ShaderGraphNodeKind.VectorDisplacement:
                    type = ShaderGraphValueType.Float2; text = $"({V("UV")}+({V("Displacement")}{(n.option == 1 ? "*2.0-1.0" : "")})*{V("Strength")})"; break;
                case ShaderGraphNodeKind.Twirl: case ShaderGraphNodeKind.BulgePinch:
                    type = ShaderGraphValueType.Float2; text = $"GraphRadialWarp({V("UV")},{V("Center")},{V("Strength")},{V("Radius")},{(n.kind == ShaderGraphNodeKind.Twirl ? 0 : 1)})"; break;
                case ShaderGraphNodeKind.Wave:
                    type = ShaderGraphValueType.Float2; text = $"GraphWaveWarp({V("UV")},{V("Strength")},{V("Frequency")},{V("Direction")},{V("Phase")})"; break;
                case ShaderGraphNodeKind.Ripple:
                    type = ShaderGraphValueType.Float2; text = $"GraphRipple({V("UV")},{V("Center")},{V("Strength")},{V("Frequency")},{V("Radius")},{V("Phase")})"; break;
                default: throw new ShaderGraphException(n.id, "Unknown procedural node.");
            }
            return new Value(type, text);
        }
        // Keep the spatial coordinate and offset separate until both have been reduced
        // to a period. Differentiate their unwrapped parts so seams do not widen AA.
        string PeriodicField(ShaderGraphNode node, ShaderGraphValueType type, Func<string> expression, out string width)
        {
            var parts = ProceduralShared(node, type, expression);
            width = sampledEvaluation ? SampledWidth(node, type, expression, true) : $"GraphPeriodicWidth({parts.text})";
            return $"GraphPeriodicPosition({parts.text})";
        }
        string ShapeDistanceExpression(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            return node.option switch
            {
                3 or 4 => $"GraphPolygonDistance({V("UV")},{V("Center")},{V("Size")},{V("Sides")},{(node.option == 4 ? V("InnerRadius") : "1.0")},{V("Rotation")},{node.option})",
                5 => $"GraphCapsuleDistance({V("UV")},{V("Center")},{V("Size")},{V("Rotation")})",
                6 => $"GraphSegmentDistance({V("UV")},{V("Start")},{V("End")})-max({V("Width")},0)*0.5",
                _ => $"GraphShapeDistance({V("UV")},{V("Center")},{V("Size")},{V("Radius")},{node.option})"
            };
        }
        static string ProceduralHelpers => ShaderGraphSharedSource.Read("Procedural");
    }
}
