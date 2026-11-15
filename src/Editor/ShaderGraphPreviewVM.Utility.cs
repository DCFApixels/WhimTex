using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value EmitUtility(ShaderGraphNode node, string port)
            {
                var op = new Instruction { op = 40, o = (int)node.kind };
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.Append:
                    {
                        var a = Input(node, "A"); var b = Input(node, "B"); Common(a, b);
                        op.a = a.register; op.b = b.register;
                        op.i = Math.Min(3, ShaderGraphNodes.Components(a.type));
                        op.j = Math.Min(4 - op.i, ShaderGraphNodes.Components(b.type));
                        return Add(op, ShaderGraphNodes.AppendType(a.type, b.type));
                    }
                    case ShaderGraphNodeKind.Switch:
                    {
                        var cases = ShaderGraphNodes.SwitchCases(node);
                        var choices = new Value[cases.Count + 1]; choices[0] = Input(node, "Fallback");
                        for (int i = 0; i < cases.Count; i++) choices[i + 1] = Input(node, cases[i].port);
                        var type = Common(choices);
                        var predicate = Input(node, "Predicate");
                        if (node.floorPredicate) predicate = Add(new Instruction { op = 11, a = predicate.register, c = (int)ShaderGraphNodeKind.Floor, d = 1 }, ShaderGraphValueType.Float);
                        var result = Cast(choices[0], type);
                        for (int i = cases.Count - 1; i >= 0; i--)
                        {
                            var condition = Add(new Instruction { op = 40, o = (int)ShaderGraphNodeKind.Compare, i = (int)cases[i].comparison,
                                a = predicate.register, b = Number(cases[i].threshold).register }, ShaderGraphValueType.Boolean);
                            result = Add(new Instruction { op = 40, o = (int)ShaderGraphNodeKind.Select,
                                a = Cast(choices[i + 1], type).register, b = result.register, c = condition.register }, type);
                        }
                        return result;
                    }
                    case ShaderGraphNodeKind.All: case ShaderGraphNodeKind.Any:
                    {
                        var input = Input(node, "In"); Common(input);
                        op.a = input.register; op.m = ShaderGraphNodes.Components(input.type);
                        return Add(op, ShaderGraphValueType.Boolean);
                    }
                    case ShaderGraphNodeKind.IsNaN: case ShaderGraphNodeKind.IsInfinite:
                        op.a = Input(node, "In").register;
                        return Add(op, ShaderGraphValueType.Boolean);
                    case ShaderGraphNodeKind.ParallaxMapping: case ShaderGraphNodeKind.ParallaxOcclusionMapping: return EmitParallax(node);
                    default: return EmitArtist(node, port);
                }
            }
        }
    }
}
