namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        Value EmitParallax(ShaderGraphNode node)
        {
            usesColor = true;
            string uv = Input(node, "UV").text, view = Input(node, "ViewDirection").text;
            string amplitude = Input(node, "Amplitude").text;
            Value field = default;
            string texture = null;
            if (node.heightSource == ShaderGraphHeightSource.Value) field = SamplingFunction(node, "Height");
            else texture = Input(node, "Height").text;
            string Sample(string position) => texture == null ? Call(field, position) :
                $"GraphChannel(GraphSample({texture},{position},{(int)node.wrap},{(int)node.filter}),{(int)node.channel})";
            if (node.kind == ShaderGraphNodeKind.ParallaxMapping)
                return new Value(ShaderGraphValueType.Float2, $"GraphParallaxOffset({uv},{view},{amplitude},{Sample(uv)})");
            // Resolving an input can append its upstream statements to body.
            // Finish those statements before opening the trace initialization.
            string steps = Input(node, "Steps").text;
            string trace = "parallax" + NodeCodeName(node), index = trace + "Index";
            body.Append("GraphParallaxTrace ").Append(trace).Append("=GraphParallaxBegin(").Append(uv).Append(',')
                .Append(view).Append(',').Append(amplitude).Append(',').Append(steps).AppendLine(");")
                .Append("if(any(").Append(trace).AppendLine(".ray!=0)) {")
                .Append("[loop] for(int ").Append(index).Append("=0;").Append(index).Append("<68;").Append(index).AppendLine("++) {")
                .Append("if(!GraphParallaxAdvance(").Append(trace).Append(',').Append(Sample("GraphParallaxUV(" + trace + ")"))
                .AppendLine(")) break;\n}}");
            return new Value(ShaderGraphValueType.Float2, "GraphParallaxUV(" + trace + ")");
        }
    }
}
