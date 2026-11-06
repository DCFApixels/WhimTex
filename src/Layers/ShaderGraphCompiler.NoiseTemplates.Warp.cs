using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        sealed class WarpRequest
        {
            internal int dimensions, algorithm, mode;
            internal bool filtering;
            internal string name;
        }
        readonly Dictionary<string, WarpRequest> warpRequests = new(StringComparer.Ordinal);

        Value EmitWarp(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            if (!warpRequests.TryGetValue(node.id, out var request))
            {
                request = new WarpRequest
                {
                    dimensions = ShaderGraphNodes.NoiseDimensions(node),
                    algorithm = node.kind == ShaderGraphNodeKind.OpenSimplex2WarpNoise ? 0 :
                        node.kind == ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise ? 1 : 2,
                    mode = node.fractal == ShaderGraphFractal.Progressive ? 1 :
                        node.fractal == ShaderGraphFractal.Independent ? 2 : 0,
                    filtering = node.noiseFiltering,
                    name = "GraphWarpRequest" + warpRequests.Count
                };
                warpRequests.Add(node.id, request);
            }
            // Values are local to an evaluation body: Position and Displacement
            // share one sample, while sampled/loop evaluations retain their domain.
            string key = node.id + ":$warpSample";
            if (!values.TryGetValue(key, out var sample))
            {
                var positionType = ShaderGraphNodes.NoisePositionType(node);
                string suffix = request.dimensions == 1 ? ".x" : request.dimensions == 2 ? ".xy" : "";
                sample = new Value(positionType, settings + "Displacement");
                body.Append(Type(positionType)).Append(' ').Append(sample.text).Append(" = ")
                    .Append(request.name).Append('(').Append(NoiseVector3(position, request.dimensions)).Append(',')
                    .Append(settings).Append(',').Append(settings).Append("Family)").Append(suffix).AppendLine(";");
                values.Add(key, sample);
            }
            return new Value(type, port == "Position" ? "(" + position + "+" + sample.text + ")" : sample.text);
        }

        string WarpSource()
        {
            if (warpRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseWarp.hlsl");
            var fnl = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in warpRequests.Values)
            {
                string algorithm = request.algorithm == 0 ? "OpenSimplex2" : request.algorithm == 1 ? "Reduced" : "BasicGrid";
                int kernelDimensions = request.dimensions == 3 ? 3 : 2;
                string kernelPrefix = "GraphWarp" + algorithm + "D" + kernelDimensions;
                string dispatch = "_fnlDoSingleDomainWarp" + kernelDimensions + "D";
                string kernel = request.algorithm == 2 ? "_fnlSingleDomainWarpBasicGrid" + kernelDimensions + "D" :
                    kernelDimensions == 3 ? "_fnlSingleDomainWarpOpenSimplex2Gradient" : "_fnlSingleDomainWarpSimplexGradient";
                if (emitted.Add(kernelPrefix))
                {
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [kernel] = kernelPrefix + "Kernel",
                        [dispatch] = kernelPrefix + "Dispatch",
                        ["fnl_state"] = "WTNoiseWarpSettings"
                    };
                    var features = new[] { "Warp" + algorithm };
                    // Resolve the selected body before asking the library selector
                    // for helpers/tables. Reduced never references the dual-gradient
                    // helpers, and therefore never requests their gradient tables.
                    string block = "Warp" + (request.algorithm == 2 ? "BasicGrid" : "Simplex") + kernelDimensions + "D";
                    source.Append(fnl.Generate(new[] { block }, features, replacements)).AppendLine();
                    // FNL's ordinary dispatcher precedes the kernel definitions and
                    // uses forward declarations. Emit the selected kernel first.
                    source.Append(fnl.Generate(new[] { "WarpDispatch" + kernelDimensions + "D" }, features, replacements)).AppendLine();
                }
                string prefix = kernelPrefix + "N" + request.dimensions + "M" + request.mode + (request.filtering ? "F" : "U");
                if (emitted.Add(prefix))
                {
                    var features = new List<string>
                    {
                        "Dim" + request.dimensions, algorithm,
                        request.mode == 1 ? "Progressive" : request.mode == 2 ? "Independent" : "None"
                    };
                    if (request.filtering) features.Add("Filtering");
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["WTNoiseInternalDisplacement"] = prefix + "Displacement",
                        [dispatch] = kernelPrefix + "Dispatch",
                        ["state"] = "w"
                    };
                    source.Append(template.Generate(new[] { "Displacement" }, features, replacements)).AppendLine();
                }
                source.AppendLine("#line 1 \"WhimTex/WarpBindings\"")
                    .Append("float3 ").Append(request.name).Append("(float3 p,WTNoiseSettings n,WTNoiseWarpSettings w) { ")
                    .Append("w.algorithm=").Append(request.algorithm).Append("; w.mode=").Append(request.mode)
                    .Append("; return ").Append(prefix).Append("Displacement(p,n,w,").Append(request.dimensions).AppendLine("); }");
            }
            return source.ToString();
        }
    }
}
