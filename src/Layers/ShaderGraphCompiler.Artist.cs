namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value EmitArtist(ShaderGraphNode node, string port)
        {
            // ArtistHelpers shares the existing color-helper inclusion path.
            usesColor = true;
            string V(string name) => Input(node, name).text;
            if (node.kind == ShaderGraphNodeKind.ReplaceColor)
            {
                string key = node.id + ":$colorMask";
                if (!values.TryGetValue(key, out var mask))
                {
                    string id = "colorMask" + NodeCodeName(node);
                    string expression = $"GraphColorMask({V("Color")}.rgb,{V("From")}.rgb,{V("Range")},{V("Fuzziness")},{node.option})";
                    body.Append("float ").Append(id).Append('=').Append(expression).AppendLine(";");
                    values[key] = mask = new Value(ShaderGraphValueType.Float, id);
                }
                return port == "Mask" ? mask : new Value(ShaderGraphValueType.Color, $"GraphReplaceColor({V("Color")},{V("To")}.rgb,{mask.text})");
            }
            ShaderGraphNodes.TryArtistOutputType(node.kind, out var type);
            string text = node.kind switch
            {
                ShaderGraphNodeKind.FadeTransition => $"GraphFadeTransition({V("NoiseValue")},{V("FadeValue")},{V("FadeContrast")})",
                ShaderGraphNodeKind.Contrast => $"GraphContrast({V("Color")},{V("Contrast")})",
                ShaderGraphNodeKind.NormalReconstructZ => $"GraphNormalReconstructZ({V("In")})",
                ShaderGraphNodeKind.NormalUnpack => $"GraphNormalUnpack({V("In")},{node.option})",
                ShaderGraphNodeKind.RadialShear => $"GraphRadialShear({V("UV")},{V("Center")},{V("Strength")},{V("Offset")})",
                _ => throw new ShaderGraphException(node.id, "Unknown artist node.")
            };
            return new Value(type, text);
        }

        static string ArtistHelpers => ShaderGraphSharedSource.Read("Artist");
    }
}
