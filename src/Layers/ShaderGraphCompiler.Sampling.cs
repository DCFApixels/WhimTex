using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        readonly StringBuilder samplingCode = new StringBuilder();
        readonly Dictionary<string, Value> samplingFunctions = new Dictionary<string, Value>();
        int sampleFunctionIndex;

        // Each connected branch is emitted once as a function of UV. Nested filters call
        // these functions instead of duplicating their source for every sample.
        Value SamplingFunction(ShaderGraphNode node, string port)
        {
            string key = incoming.TryGetValue(node.id + ":" + port, out var edge)
                ? edge.outputNode + ":" + edge.outputPort : node.id + ":literal:" + port;
            foreach (var input in ShaderGraphNodes.Inputs(node)) if (input.name == port) key += ":" + input.type;
            if (samplingFunctions.TryGetValue(key, out var function)) return function;
            string name = "GraphBranch" + sampleFunctionIndex++;
            var oldBody = body; var oldValues = values;
            body = new StringBuilder(); values = new Dictionary<string, Value>();
            try
            {
                var value = Input(node, port);
                if (!ShaderGraphNodes.IsNumeric(value.type)) Error(node, "A sampled branch requires a numeric or Color value.");
                samplingCode.Append(Type(value.type)).Append(' ').Append(name).AppendLine("(float2 graphUV) {")
                    .Append(body).Append("return ").Append(value.text).AppendLine(";\n}");
                function = new Value(value.type, name);
                samplingFunctions.Add(key, function);
                return function;
            }
            finally { body = oldBody; values = oldValues; }
        }

        string EmitHeightFieldSlope(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "Height");
            string uv = Input(node, "UV").text, radius = Input(node, "Radius").text;
            string r = "hr" + node.id, center = "hu" + node.id, resultName = "hs" + node.id;
            body.Append("float ").Append(r).Append(" = max(abs(").Append(radius).AppendLine("), 0.001);");
            body.Append("float2 ").Append(center).Append(" = ").Append(uv).AppendLine(";");
            string Sample(int x, int y) => source.text + "(" + center + "+float2(" + x + "," + y + ")*" + r + "*_GraphImageSize.zw)";
            string dx, dy;
            if (node.derivative == ShaderGraphDerivative.CentralDifference)
            {
                dx = "(" + Sample(1, 0) + "-" + Sample(-1, 0) + ")/(2*" + r + ")";
                dy = "(" + Sample(0, 1) + "-" + Sample(0, -1) + ")/(2*" + r + ")";
            }
            else
            {
                int side = node.derivative == ShaderGraphDerivative.Scharr ? 3 : 1;
                int middle = node.derivative == ShaderGraphDerivative.Scharr ? 10 : 2;
                int divisor = 2 * (2 * side + middle);
                dx = $"({side}*({Sample(1, -1)}-{Sample(-1, -1)}+{Sample(1, 1)}-{Sample(-1, 1)})+{middle}*({Sample(1, 0)}-{Sample(-1, 0)}))/({divisor}*{r})";
                dy = $"({side}*({Sample(-1, 1)}-{Sample(-1, -1)}+{Sample(1, 1)}-{Sample(1, -1)})+{middle}*({Sample(0, 1)}-{Sample(0, -1)}))/({divisor}*{r})";
            }
            body.Append("float2 ").Append(resultName).Append(" = float2(").Append(dx).Append(',').Append(dy).AppendLine(");");
            return resultName;
        }

        Value EmitFilter(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "In");
            string uv = Input(node, "UV").text, radius = Input(node, "Radius").text;
            string name = "GraphFilter" + sampleFunctionIndex++;
            string type = Type(source.type);
            bool edge = node.kind == ShaderGraphNodeKind.EdgeDetection;
            bool morphology = node.kind is ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode;
            int half = node.option switch { 1 => 2, 2 => 4, _ => 1 };
            samplingCode.Append(type).Append(' ').Append(name).AppendLine("(float2 uv, float radius) {")
                .AppendLine("radius = abs(radius);")
                .Append(type).Append(" center = ").Append(source.text).AppendLine("(uv);")
                .AppendLine("if (radius <= 1e-7) return " + (edge ? "0;" : "center;"));
            if (edge)
            {
                samplingCode.AppendLine("float2 stepUV = radius * _GraphImageSize.zw;");
                string S(int x, int y) => source.text + "(uv+float2(" + x + "," + y + ")*stepUV)";
                samplingCode.Append(type).Append(" dx = (").Append(S(1,-1)).Append('+').Append(S(1,0)).Append("*2+").Append(S(1,1))
                    .Append('-').Append(S(-1,-1)).Append('-').Append(S(-1,0)).Append("*2-").Append(S(-1,1)).AppendLine(")/8;");
                samplingCode.Append(type).Append(" dy = (").Append(S(-1,1)).Append('+').Append(S(0,1)).Append("*2+").Append(S(1,1))
                    .Append('-').Append(S(-1,-1)).Append('-').Append(S(0,-1)).Append("*2-").Append(S(1,-1)).AppendLine(")/8;");
                samplingCode.AppendLine("return sqrt(dx*dx+dy*dy);\n}");
            }
            else
            {
                samplingCode.Append(type).Append(" sum = ").AppendLine(morphology ? "center;" : "0;")
                    .AppendLine("float weightSum = 0;")
                    .Append("[loop] for (int y=-").Append(half).Append(";y<=").Append(half).AppendLine(";y++)")
                    .Append("[loop] for (int x=-").Append(half).Append(";x<=").Append(half).AppendLine(";x++) {")
                    .Append("float2 offset = float2(x,y)/").Append(half).AppendLine(".0;")
                    .Append(type).Append(" value = ").Append(source.text).AppendLine("(uv + offset * radius * _GraphImageSize.zw);");
                if (morphology)
                    samplingCode.Append("sum = ").Append(node.kind == ShaderGraphNodeKind.Dilate ? "max" : "min").AppendLine("(sum,value);");
                else samplingCode.AppendLine("float weight = exp(-2*dot(offset,offset)); sum += value*weight; weightSum += weight;");
                samplingCode.AppendLine(morphology ? "}\nreturn sum;\n}" : "}\nreturn sum/weightSum;\n}");
            }
            string expression = name + "(" + uv + "," + radius + ")";
            if (node.kind == ShaderGraphNodeKind.Sharpen)
                expression = $"({source.text}({uv})+({source.text}({uv})-{expression})*{Input(node,"Strength").text})";
            else if (edge) expression = "(" + expression + "*" + Input(node, "Strength").text + ")";
            return new Value(source.type, expression);
        }
    }
}
