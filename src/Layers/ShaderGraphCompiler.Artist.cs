namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value EmitArtist(ShaderGraphNode node)
        {
            // ArtistHelpers shares the existing color-helper inclusion path.
            usesColor = true;
            string V(string name) => Input(node, name).text;
            ShaderGraphNodes.TryArtistOutputType(node.kind, out var type);
            string text = node.kind switch
            {
                ShaderGraphNodeKind.FadeTransition => $"GraphFadeTransition({V("NoiseValue")},{V("FadeValue")},{V("FadeContrast")})",
                ShaderGraphNodeKind.ReplaceColor => $"GraphReplaceColor({V("Color")},{V("From")}.rgb,{V("To")}.rgb,{V("Range")},{V("Fuzziness")})",
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
