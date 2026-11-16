using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesAnisotropicKuwahara;

        Value EmitAnisotropicKuwahara(ShaderGraphNode node)
        {
            usesAnisotropicKuwahara = true;
            string key = node.id + ":$kuwaharaAnisotropic";
            if (values.TryGetValue(key, out var result)) return result;
            var source = SamplingFunction(node, "In");
            int components = ShaderGraphNodes.Components(source.type);
            string id = "kuwAniso" + NodeCodeName(node), tensor = "GraphKuwTensor" + sampleFunctionIndex++, uv = id + "UV";
            string Sample(string coordinate) => Cast(new Value(source.type, Call(source, coordinate)), ShaderGraphValueType.Float4, node).text;
            samplingCode.Append("float3 ").Append(tensor).Append("(float2 graphUV").Append(LoopParameters).AppendLine(") {")
                .Append("return GraphKuwTensorSamples(");
            var offsets = new[] { "-1,1", "0,1", "1,1", "-1,0", "1,0", "-1,-1", "0,-1", "1,-1" };
            foreach (string offset in offsets) samplingCode.Append(Sample("graphUV+float2(" + offset + ")*_CanvasSize.zw")).Append(',');
            samplingCode.Append(components).AppendLine(");\n}");
            string V(string port) => Input(node, port).text;
            string radius = id + "Radius", uniformity = id + "Uniformity", sharpness = V("Sharpness"), eccentricity = V("Eccentricity");
            string coordinate = V("UV"), radiusValue = V("Radius"), uniformityValue = V("Uniformity");
            body.Append("float2 ").Append(uv).Append('=').Append(coordinate).AppendLine(";")
                .Append("float ").Append(radius).Append("=max(0,").Append(radiusValue).AppendLine(");")
                .Append("int ").Append(uniformity).Append("=(int)max(0,floor(").Append(uniformityValue).AppendLine("));")
                .Append("float4 ").Append(id).Append('=').Append(Sample(uv)).AppendLine(";")
                .Append("if(").Append(radius).AppendLine(">0) {")
                .Append("float3 ").Append(id).Append("T=0;float ").Append(id).AppendLine("W=0;")
                .Append("[loop] for(int ").Append(id).Append("TY=-").Append(uniformity).Append(';').Append(id).Append("TY<=").Append(uniformity).Append(';').Append(id).AppendLine("TY++)")
                .Append("[loop] for(int ").Append(id).Append("TX=-").Append(uniformity).Append(';').Append(id).Append("TX<=").Append(uniformity).Append(';').Append(id).AppendLine("TX++) {")
                .Append("float ").Append(id).Append("TW=GraphKuwGaussian(").Append(id).Append("TX,").Append(uniformity).Append(")*GraphKuwGaussian(").Append(id).Append("TY,").Append(uniformity).AppendLine(");")
                .Append(id).Append("T+=").Append(id).Append("TW*").Append(tensor).Append('(').Append(uv).Append("+float2(").Append(id).Append("TX,").Append(id).Append("TY)*_CanvasSize.zw").Append(LoopArguments).AppendLine(");")
                .Append(id).Append("W+=").Append(id).AppendLine("TW;\n}")
                .Append("GraphKuwAniso ").Append(id).Append("State=GraphKuwAnisoSetup(").Append(id).Append("T/").Append(id).Append("W,").Append(id).Append(',').Append(radius).Append(',').Append(sharpness).Append(',').Append(eccentricity).Append(',').Append(components).AppendLine(");")
                .Append("[loop] for(int ").Append(id).Append("Y=-").Append(id).Append("State.bounds.y;").Append(id).Append("Y<=").Append(id).Append("State.bounds.y;").Append(id).AppendLine("Y++)")
                .Append("[loop] for(int ").Append(id).Append("X=-").Append(id).Append("State.bounds.x;").Append(id).Append("X<=").Append(id).Append("State.bounds.x;").Append(id).AppendLine("X++) {")
                .Append("if(").Append(id).Append("X==0&&").Append(id).AppendLine("Y==0) continue;")
                .Append("float2 ").Append(id).Append("P=GraphKuwAnisoPoint(").Append(id).Append("State,int2(").Append(id).Append("X,").Append(id).AppendLine("Y));")
                .Append("if(dot(").Append(id).Append("P,").Append(id).Append("P)<=1) GraphKuwAnisoAdd(").Append(id).Append("State,").Append(id).Append("P,")
                .Append(Sample(uv + "+float2(" + id + "X," + id + "Y)*_CanvasSize.zw")).AppendLine(");\n}")
                .Append(id).Append("=GraphKuwAnisoResult(").Append(id).AppendLine("State);\n}");
            result = new Value(source.type, id + Swizzle(source.type));
            values[key] = result;
            return result;
        }
    }
}
