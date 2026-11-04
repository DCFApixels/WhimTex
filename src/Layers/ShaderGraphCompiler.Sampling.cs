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
        bool sampledEvaluation, usesSampleFootprint;

        // Each connected branch is emitted once as a function of UV. Nested filters call
        // these functions instead of duplicating their source for every sample.
        Value SamplingFunction(ShaderGraphNode node, string port, bool sampled = true)
        {
            var previousTrace = activeTrace;
            if (!sampled) activeTrace = null;
            try
            {
                string key = incoming.TryGetValue(node.id + ":" + port, out var edge)
                    ? edge.outputNode + ":" + edge.outputPort : node.id + ":literal:" + port;
                foreach (var input in ShaderGraphNodes.Inputs(node)) if (input.name == port) key += ":" + input.type;
                return SamplingExpression(key, () => Input(node, port), node, sampled);
            }
            finally { activeTrace = previousTrace; }
        }

        Value SamplingExpression(string key, Func<Value> expression, ShaderGraphNode node, bool sampled = true)
        {
            key += sampled ? ":sampled" : ":direct";
            bool traceNeeded = activeTrace?.functions.Add(key) == true;
            if (samplingFunctions.TryGetValue(key, out var function) && !traceNeeded) return function;
            string name = "GraphBranch" + sampleFunctionIndex++;
            var oldBody = body; var oldValues = values; bool oldSampled = sampledEvaluation;
            body = new StringBuilder(); values = new Dictionary<string, Value>(); sampledEvaluation = sampled;
            try
            {
                var value = expression();
                if (!ShaderGraphNodes.IsNumeric(value.type)) Error(node, "A sampled branch requires a numeric or Color value.");
                samplingCode.Append(Type(value.type)).Append(' ').Append(name).AppendLine("(float2 graphUV) {")
                    .Append(body).Append("return ").Append(value.text).AppendLine(";\n}");
                function = new Value(value.type, name);
                samplingFunctions[key] = function;
                return function;
            }
            finally { body = oldBody; values = oldValues; sampledEvaluation = oldSampled; }
        }

        // The entry point captures the pixel footprint before any divergent loops.
        // Reevaluate the field itself at neighboring UVs: this includes connected
        // transforms/parameters, without differentiating the varying filter radius.
        string SampledWidth(ShaderGraphNode node, ShaderGraphValueType type, Func<string> expression, bool periodic = false)
        {
            usesSampleFootprint = true;
            var field = SamplingExpression(node.id + ":footprint", () => new Value(type, expression()), node);
            string name = "gw" + node.id;
            string Delta(string axis)
            {
                string delta = $"{field.text}(graphUV + {axis} * 0.5) - {field.text}(graphUV - {axis} * 0.5)";
                return periodic ? "GraphPeriodSum(" + delta + ")" : delta;
            }
            var widthType = periodic ? (type == ShaderGraphValueType.Float2 ? ShaderGraphValueType.Float : ShaderGraphValueType.Float2) : type;
            body.Append(Type(widthType)).Append(' ').Append(name).Append(" = abs(").Append(Delta("_GraphSampleDx"))
                .Append(") + abs(").Append(Delta("_GraphSampleDy")).AppendLine(");");
            return name;
        }

        string EmitHeightFieldSlope(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "Height");
            string uv = Input(node, "UV").text, radius = Input(node, "Radius").text;
            string r = "hr" + node.id, center = "hu" + node.id, resultName = "hs" + node.id;
            body.Append("float ").Append(r).Append(" = max(abs(").Append(radius).AppendLine("), 0.001);");
            body.Append("float2 ").Append(center).Append(" = ").Append(uv).AppendLine(";");
            string dx = resultName + "X", dy = resultName + "Y";
            AppendDerivativeSamples(body, source, center, r + "*_CanvasSize.zw", dx, dy, node.derivative);
            body.Append("float2 ").Append(resultName).Append(" = float2(").Append(dx).Append(',').Append(dy).Append(")/").Append(r).AppendLine(";");
            return resultName;
        }

        // One branch call site keeps complex procedural sources compact in the shader compiler.
        // Opposite samples are subtracted first, preserving flat fields even for large HDR values.
        static void AppendDerivativeSamples(StringBuilder target, Value source, string center, string step,
            string dx, string dy, ShaderGraphDerivative derivative)
        {
            bool central = derivative == ShaderGraphDerivative.CentralDifference;
            int side = derivative == ShaderGraphDerivative.Scharr ? 3 : 1;
            int middle = derivative == ShaderGraphDerivative.Scharr ? 10 : 2;
            int divisor = central ? 2 : 2 * (2 * side + middle);
            string type = Type(source.type), previous = dx + "Previous";
            target.Append(type).Append(' ').Append(dx).Append("=0, ").Append(dy).Append("=0, ").Append(previous).AppendLine("=0;")
                .Append("[loop] for(int sampleIndex=0;sampleIndex<").Append(central ? 4 : 8).AppendLine(";sampleIndex++) {")
                .AppendLine("int pair=sampleIndex>>1;")
                .AppendLine(central ? "float2 offset=pair==0 ? float2(1,0) : float2(0,1);" :
                    "float2 offset=pair<3 ? float2(1,pair-1) : float2(0,1);")
                .Append(type).Append(" value=").Append(source.text).Append('(').Append(center)
                .Append("+offset*((sampleIndex&1)==0 ? 1 : -1)*(").Append(step).AppendLine("));")
                .Append("if((sampleIndex&1)==0) ").Append(previous).AppendLine("=value; else {")
                .Append(type).Append(" delta=").Append(previous).AppendLine("-value;");
            if (central) target.AppendLine("float2 weight=offset;");
            else target.Append("float2 weight=offset*float2(offset.y==0 ? ").Append(middle).Append(" : ").Append(side)
                .Append(",offset.x==0 ? ").Append(middle).Append(" : ").Append(side).AppendLine(");");
            target.Append(dx).Append("+=weight.x*delta; ").Append(dy).AppendLine("+=weight.y*delta;\n}}")
                .Append(dx).Append("/=").Append(divisor).Append("; ").Append(dy).Append("/=").Append(divisor).AppendLine(";");
        }

        Value EmitFilter(ShaderGraphNode node)
        {
            if (node.kind is ShaderGraphNodeKind.DirectionalBlur or ShaderGraphNodeKind.RadialBlur) return EmitPathBlur(node);
            var source = SamplingFunction(node, "In");
            // Keep the unfiltered center exact (including Radius = 0). A filter
            // nested in another sampled branch still uses the explicit footprint.
            var centerSource = sampledEvaluation ? source : SamplingFunction(node, "In", false);
            string uv = Input(node, "UV").text, radius = Input(node, "Radius").text;
            string name = "GraphFilter" + sampleFunctionIndex++;
            string type = Type(source.type);
            bool edge = node.kind == ShaderGraphNodeKind.EdgeDetection;
            bool color = source.type == ShaderGraphValueType.Color;
            bool morphology = node.kind is ShaderGraphNodeKind.Dilate or ShaderGraphNodeKind.Erode;
            int half = node.option switch { 1 => 2, 2 => 4, _ => 1 };
            samplingCode.Append(type).Append(' ').Append(name).AppendLine("(float2 uv, float radius) {")
                .AppendLine("radius = abs(radius);")
                .Append(type).Append(" center = ").Append(centerSource.text).AppendLine("(uv);");
            // Do not return early for individual pixels: that would deactivate
            // neighboring lanes before the direct center's derivatives execute.
            if (edge)
            {
                samplingCode.AppendLine("float2 stepUV = radius * _CanvasSize.zw;");
                AppendDerivativeSamples(samplingCode, source, "uv", "stepUV", "dx", "dy", ShaderGraphDerivative.Sobel);
                samplingCode.AppendLine(color ? "return float4(radius <= 1e-7 ? 0 : sqrt(dx.rgb*dx.rgb+dy.rgb*dy.rgb),center.a);\n}" : "return radius <= 1e-7 ? 0 : sqrt(dx*dx+dy*dy);\n}");
            }
            else
            {
                samplingCode.Append(type).Append(" sum = ").AppendLine(morphology ? "center;" : "0;")
                    .AppendLine("float weightSum = 0;")
                    // The chosen kernel is a minimum density, not a fixed sparse stencil.
                    // Never separate adjacent samples by more than one document pixel.
                    .Append("int extent = (int)max(").Append(half).AppendLine(",ceil(radius));")
                    .AppendLine("[loop] for (int y=-extent;y<=extent;y++)")
                    .AppendLine("[loop] for (int x=-extent;x<=extent;x++) {")
                    .AppendLine("float2 offset = float2(x,y)/extent;")
                    .Append(type).Append(" value = ").Append(source.text).AppendLine("(uv + offset * radius * _CanvasSize.zw);");
                if (morphology)
                    samplingCode.Append("sum = ").Append(node.kind == ShaderGraphNodeKind.Dilate ? "max" : "min").AppendLine("(sum,value);");
                else
                {
                    if (color) samplingCode.AppendLine("value.rgb *= value.a;");
                    samplingCode.AppendLine("float weight = exp(-2*dot(offset,offset)); sum += value*weight; weightSum += weight;");
                }
                if (morphology) samplingCode.AppendLine("}\nreturn radius <= 1e-7 ? center : sum;\n}");
                else
                {
                    samplingCode.AppendLine("}\nsum /= weightSum;");
                    if (color) samplingCode.AppendLine("sum.rgb = sum.a==0 ? 0 : sum.rgb/(sum.a==0 ? 1 : sum.a);");
                    samplingCode.AppendLine("return radius <= 1e-7 ? center : sum;\n}");
                }
            }
            string expression = name + "(" + uv + "," + radius + ")";
            if (node.kind == ShaderGraphNodeKind.Sharpen)
                expression = color ? $"float4({centerSource.text}({uv}).rgb+({centerSource.text}({uv}).rgb-({expression}).rgb)*{Input(node,"Strength").text},{centerSource.text}({uv}).a)" :
                    $"({centerSource.text}({uv})+({centerSource.text}({uv})-{expression})*{Input(node,"Strength").text})";
            else if (edge) expression = "(" + expression + "*" + (color ? "float4((" + Input(node, "Strength").text + ").xxx,1)" : Input(node, "Strength").text) + ")";
            return new Value(source.type, expression);
        }

        Value EmitPathBlur(ShaderGraphNode node)
        {
            var source = SamplingFunction(node, "In");
            var direct = sampledEvaluation ? source : SamplingFunction(node, "In", false);
            bool directional = node.kind == ShaderGraphNodeKind.DirectionalBlur;
            bool color = source.type == ShaderGraphValueType.Color;
            string type = Type(source.type), name = "GraphPathBlur" + sampleFunctionIndex++;
            string V(string port) => Input(node, port).text;
            string amount = V(directional ? "Radius" : node.option == 0 ? "Angle" : "Strength");
            samplingCode.Append(type).Append(' ').Append(name).AppendLine("(float2 uv,float2 center,float amount,float angle,float samples) {")
                .Append(type).Append(" original = ").Append(direct.text).AppendLine("(uv);")
                .AppendLine("int extent=(int)clamp(ceil((samples-1)*0.5),1,64);")
                .Append(type).AppendLine(" sum=0; float weightSum=0;");
            if (directional)
                samplingCode.AppendLine("float s,c; sincos(radians(angle),s,c); float2 delta=float2(c,s)*abs(amount)*_CanvasSize.zw;");
            else samplingCode.AppendLine("float2 p=(uv-center)*_CanvasSize.xy;");
            samplingCode.AppendLine("[loop] for(int i=-extent;i<=extent;i++) {")
                .AppendLine("float t=(float)i/extent;");
            if (directional) samplingCode.AppendLine("float2 sampleUV=uv+delta*t;");
            else if (node.option == 0)
                samplingCode.AppendLine("float s,c; sincos(radians(amount)*t,s,c); float2 sampleUV=center+float2(c*p.x-s*p.y,s*p.x+c*p.y)*_CanvasSize.zw;");
            else samplingCode.AppendLine("float2 sampleUV=center+p*(1+abs(amount)*t)*_CanvasSize.zw;");
            samplingCode.Append(type).Append(" value = ").Append(source.text).AppendLine("(sampleUV);");
            if (color) samplingCode.AppendLine("value.rgb*=value.a;");
            samplingCode.AppendLine("float weight=exp(-2*t*t); sum+=value*weight; weightSum+=weight;\n}\nsum/=weightSum;");
            if (color) samplingCode.AppendLine("sum.rgb=sum.a==0 ? 0 : sum.rgb/(sum.a==0 ? 1 : sum.a);");
            // Keep direct derivatives active even where the blur has no displacement.
            samplingCode.Append("return abs(amount)<=1e-7").Append(directional ? "" : " || dot(p,p)<=1e-20")
                .AppendLine(" ? original : sum;\n}");
            return new Value(source.type, $"{name}({V("UV")},{(directional ? "float2(0,0)" : V("Center"))},{amount},{(directional ? V("Angle") : "0")},{V("Samples")})");
        }
    }
}
