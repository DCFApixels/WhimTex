namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPreviewVM
    {
        sealed partial class Builder
        {
            Value EmitArtist(ShaderGraphNode node, string port)
            {
                var op = new Instruction { op = 40, o = (int)node.kind, i = node.option };
                int R(string name) => Input(node, name).register;
                if (node.kind == ShaderGraphNodeKind.ReplaceColor)
                {
                    string key = node.id + ":$colorMask";
                    if (!values.TryGetValue(key, out var mask))
                        values[key] = mask = Add(new Instruction { op = 40, o = (int)ShaderGraphNodeKind.ColorMask,
                            a = R("Color"), b = R("From"), c = R("Range"), d = R("Fuzziness"), i = node.option }, ShaderGraphValueType.Float);
                    return port == "Mask" ? mask : Add(new Instruction { op = 40, o = (int)node.kind,
                        a = R("Color"), b = R("To"), c = mask.register }, ShaderGraphValueType.Color);
                }
                switch (node.kind)
                {
                    case ShaderGraphNodeKind.FadeTransition:
                        op.a=R("NoiseValue"); op.b=R("FadeValue"); op.c=R("FadeContrast"); break;
                    case ShaderGraphNodeKind.Contrast:
                        op.a=R("Color"); op.b=R("Contrast"); break;
                    case ShaderGraphNodeKind.NormalReconstructZ: case ShaderGraphNodeKind.NormalUnpack:
                        op.a=R("In"); break;
                    case ShaderGraphNodeKind.RadialShear:
                        op.a=R("UV"); op.b=R("Center"); op.c=R("Strength"); op.d=R("Offset"); break;
                    default: Unsupported("Artist node " + node.kind); return default;
                }
                ShaderGraphNodes.TryArtistOutputType(node.kind, out var type);
                return Add(op, type);
            }
        }
    }
}
