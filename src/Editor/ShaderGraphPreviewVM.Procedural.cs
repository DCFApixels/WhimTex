using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value Procedural(ShaderGraphNode node, string port)
            {
                bool periodic = node.kind is ShaderGraphNodeKind.Checkerboard or ShaderGraphNodeKind.Grid or
                    ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.Rings;
                Value Field()
                {
                    string key = node.id + ":$field";
                    if (values.TryGetValue(key, out var cached)) return cached;
                    var op = new Instruction { op = 40, o = (int)node.kind, i = node.option };
                    int R(string name) => Input(node, name).register;
                    switch (node.kind)
                    {
                        case ShaderGraphNodeKind.Checkerboard: case ShaderGraphNodeKind.Grid:
                            op.a=R("UV"); op.b=R("Scale"); op.c=R("Offset");
                            if (node.kind == ShaderGraphNodeKind.Grid) op.d=R("Width");
                            break;
                        case ShaderGraphNodeKind.Stripes:
                            op.a=R("UV"); op.b=R("Angle"); op.c=R("Frequency"); op.d=R("Phase"); break;
                        case ShaderGraphNodeKind.Rings:
                            op.a=R("UV"); op.b=R("Center"); op.c=R("Frequency"); op.d=R("Phase"); break;
                        case ShaderGraphNodeKind.CombineDistance:
                            op.a=R("A"); op.b=R("B"); op.c=R("Smoothness"); break;
                        case ShaderGraphNodeKind.Shape:
                            op.a=R("UV");
                            if (node.option == 6) { op.b=R("Start"); op.c=R("End"); op.d=R("Width"); }
                            else
                            {
                                op.b=R("Center"); op.c=R("Size");
                                if (node.option is 3 or 4)
                                { op.d=R("Sides"); op.e=node.option==4?R("InnerRadius"):Number(1).register; op.f=R("Rotation"); }
                                else if (node.option == 5) op.d=R("Rotation");
                                else op.d=R("Radius");
                            }
                            break;
                    }
                    var type = periodic ? (node.kind is ShaderGraphNodeKind.Checkerboard or ShaderGraphNodeKind.Grid
                        ? ShaderGraphValueType.Float4 : ShaderGraphValueType.Float2) : ShaderGraphValueType.Float;
                    var result = Add(op, type); values.Add(key, result); return result;
                }
                var field = Field();
                if (!periodic && (port == "Distance" || node.kind == ShaderGraphNodeKind.CombineDistance && port == "Out")) return field;
                var width = new Instruction { op = 44, a = field.register, i = periodic ? ShaderGraphNodes.Components(field.type) : 0 };
                if (depth + sampledEvaluationDepth == 0)
                {
                    if (varyingIterations) Unsupported("Root derivatives after varying iteration counts");
                    rootDerivatives = true;
                }
                else
                {
                    var sample = SampleSpan(Field, new Instruction { op = 32, i = -1 }, 4);
                    width.j=1; width.b=code[sample.register].b; width.c=sample.register;
                }
                int fw = Add(width, ShaderGraphValueType.Float4).register;
                int edge = node.kind == ShaderGraphNodeKind.Checkerboard ? Number(0).register : Input(node, periodic ? "Width" : "Feather").register;
                return Add(new Instruction { op=45, a=field.register, b=fw, c=edge, i=(int)node.kind, j=node.option }, ShaderGraphValueType.Float);
            }
        }
    }
}
