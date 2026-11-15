using System;
using System.Collections.Generic;
using System.Globalization;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        void ValidateUtility(ShaderGraphNode node)
        {
            if (node.kind != ShaderGraphNodeKind.Switch) return;
            var cases = ShaderGraphNodes.SwitchCases(node);
            if (cases.Count > 26) Error(node, "Switch supports at most 26 cases.");
            var ports = new HashSet<string> { "Predicate", "Fallback" };
            foreach (var item in cases)
                if (item == null || !Identifier(item.port) || !ports.Add(item.port) || !Enum.IsDefined(typeof(ShaderGraphComparison), item.comparison) || !ProjectiveMatrix.Finite(item.threshold))
                    Error(node, "Invalid Switch case.");
        }
        Value EmitUtility(ShaderGraphNode node, string port)
        {
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Append:
                {
                    var a = Input(node, "A"); var b = Input(node, "B"); Common(node, a, b);
                    int widthA = Math.Min(3, ShaderGraphNodes.Components(a.type));
                    int widthB = Math.Min(4 - widthA, ShaderGraphNodes.Components(b.type));
                    var type = ShaderGraphNodes.AppendType(a.type, b.type);
                    return new Value(type, $"{Type(type)}({Cast(a, (ShaderGraphValueType)(widthA - 1), node).text},{Cast(b, (ShaderGraphValueType)(widthB - 1), node).text})");
                }
                case ShaderGraphNodeKind.Switch:
                {
                    var cases = ShaderGraphNodes.SwitchCases(node);
                    var choices = new Value[cases.Count + 1]; choices[0] = Input(node, "Fallback");
                    for (int i = 0; i < cases.Count; i++) choices[i + 1] = Input(node, cases[i].port);
                    var type = Common(node, choices);
                    string predicate = Input(node, "Predicate").text;
                    if (node.floorPredicate) predicate = $"floor({predicate})";
                    string expression = Cast(choices[0], type, node).text;
                    for (int i = cases.Count - 1; i >= 0; i--)
                        expression = $"(({predicate} {ShaderGraphNodes.ComparisonOperator(cases[i].comparison)} {cases[i].threshold.ToString("R", CultureInfo.InvariantCulture)})?{Cast(choices[i + 1], type, node).text}:{expression})";
                    return new Value(type, expression);
                }
                case ShaderGraphNodeKind.All: case ShaderGraphNodeKind.Any:
                {
                    var input = Input(node, "In"); Common(node, input);
                    return new Value(ShaderGraphValueType.Boolean, $"{(node.kind == ShaderGraphNodeKind.All ? "all" : "any")}({input.text})");
                }
                case ShaderGraphNodeKind.IsNaN: case ShaderGraphNodeKind.IsInfinite:
                    return new Value(ShaderGraphValueType.Boolean, $"((asuint({Input(node, "In").text}) & 0x7fffffffU) {(node.kind == ShaderGraphNodeKind.IsNaN ? ">" : "==")} 0x7f800000U)");
                case ShaderGraphNodeKind.ParallaxMapping: case ShaderGraphNodeKind.ParallaxOcclusionMapping: return EmitParallax(node);
                default: return EmitArtist(node, port);
            }
        }
    }
}
