using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        [Flags]
        enum BasisResults { None = 0, Value = 1, Gradient = 2, Curl = 4 }
        sealed class BasisRequest
        {
            internal int dimensions, kind;
            internal bool filtering, flow, periodic;
            internal string algorithm, name;
            internal BasisResults results;
        }
        sealed class CellularRequest
        {
            internal int dimensions, distribution, metric, output;
            internal bool filtering, scalar;
            internal string name;
        }
        readonly Dictionary<string, BasisRequest> basisRequests = new(StringComparer.Ordinal);
        readonly Dictionary<string, CellularRequest> cellularRequests = new(StringComparer.Ordinal);

        // Keep template selection after all reachable ports, including sampled
        // branches, have registered their requirements.
        string NoiseTemplateSource() => BasisSource() + CellularSource() + GaborSource() + WarpSource() + RemainingNoiseTemplateSource() + FractalTemplateSource();

        Value EmitBasis(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            int dimensions = ShaderGraphNodes.NoiseDimensions(node);
            string key = node.id + ":$basisSample";
            // values belongs to the current evaluation body. A different sampled UV
            // or loop context must request its own result and its own output profile.
            if (!values.TryGetValue(key, out var cached))
            {
                (string algorithm, int kind) = node.kind switch
                {
                    ShaderGraphNodeKind.PerlinNoise => ("Perlin", 3),
                    ShaderGraphNodeKind.ValueNoise => ("Value", 5),
                    ShaderGraphNodeKind.ValueCubicNoise => ("ValueCubic", 4),
                    ShaderGraphNodeKind.OpenSimplex2Noise => ("OpenSimplex2", 0),
                    _ => ("OpenSimplex2S", 1)
                };
                string name = "GraphBasisRequest" + basisRequests.Count;
                var created = new BasisRequest { dimensions = dimensions, kind = kind, algorithm = algorithm,
                    filtering = node.noiseFiltering, flow = node.kind == ShaderGraphNodeKind.FlowNoise, periodic = node.noisePeriodic, name = name };
                basisRequests.Add(name, created);
                cached = new Value(ShaderGraphValueType.Float, name);
                values.Add(key, cached);
                body.Append("float3 ").Append(settings).Append("Gradient,").Append(settings).AppendLine("Curl;")
                    .Append("float ").Append(settings).Append("Value = ").Append(name).Append('(')
                    .Append(NoiseVector3(position, dimensions)).Append(',').Append(settings);
                if (created.flow) body.Append(',').Append(settings).Append("Family.phase");
                body.Append(',').Append(settings).Append("Gradient,").Append(settings).AppendLine("Curl);");
            }
            var request = basisRequests[cached.text];
            request.results |= port switch
            { "Gradient" => BasisResults.Gradient, "Curl" => BasisResults.Curl, _ => BasisResults.Value };
            string gradient = settings + "Gradient";
            string expression = port switch
            {
                "Gradient" => gradient + (dimensions == 1 ? ".x" : dimensions == 2 ? ".xy" : ""),
                "Curl" when dimensions == 3 => settings + "Curl",
                "Curl" => "float2(" + gradient + ".y,-" + gradient + ".x)",
                _ => settings + "Value"
            };
            return new Value(type, expression);
        }

        string BasisSource()
        {
            if (basisRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseBasis.hlsl");
            ShaderSourceTemplate derivatives = null;
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in basisRequests.Values)
            {
                bool derivative = (request.results & (BasisResults.Gradient | BasisResults.Curl)) != 0;
                bool curl3 = request.dimensions == 3 && (request.results & BasisResults.Curl) != 0;
                string prefix = "GraphBasis" + request.algorithm + "D" + request.dimensions +
                    (request.filtering ? "F" : "U") + (request.periodic ? "P" : "N") + (curl3 ? "C" : derivative ? "G" : "V");
                if (emitted.Add(prefix))
                {
                    var features = new List<string> { "Dim" + request.dimensions, request.algorithm };
                    if (request.kind >= 3) features.Add("Grid");
                    if (request.filtering) features.Add("Filtering");
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["WTNoiseInternalBasis1D"] = prefix + "Basis1D",
                        ["WTNoiseInternalBasisValue"] = prefix + "BasisValue",
                        ["WTNoiseInternalBasisSingle"] = prefix + "Single",
                        ["WTNoiseInternalBasisSampleSingle"] = prefix + "SampleSingle",
                        ["WTNoiseInternalBasisSamplePrepared"] = prefix + "SamplePrepared",
                        ["WTNoiseInternalBasisDerivative"] = prefix + "Derivative",
                        ["WTNoiseInternalGridDerivative"] = prefix + "GridDerivative",
                        ["WTNoiseInternalWeights"] = prefix + "Weights"
                    };
                    var blocks = new List<string>();
                    if (!request.periodic)
                    {
                        replacements["WTNoiseInternalPosition"] = prefix + "Position";
                        source.Append(ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCommon.hlsl")
                            .Generate(new[] { "Position" }, Array.Empty<string>(), replacements)).AppendLine();
                    }
                    if (derivative)
                    {
                        derivatives ??= ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseDerivatives.hlsl");
                        var derivativeBlocks = new List<string> { "BasisDerivative" };
                        if (request.kind >= 3) derivativeBlocks.Add("GridDerivative");
                        source.Append(derivatives.Generate(derivativeBlocks, features, replacements)).AppendLine();
                        blocks.Add("SampleSingle");
                    }
                    else
                    {
                        if (request.kind >= 3 && request.dimensions == 1) blocks.Add("Basis1D");
                        if (request.kind == 4 && request.dimensions == 2)
                        {
                            // Scalar-only: derivative ports retain their shared value/gradient kernel.
                            blocks.Add("ValueCubic2DRows");
                            replacements["WTNoiseInternalValueCubic2DRows"] = prefix + "ValueCubic2DRows";
                            replacements["_fnlSingleValueCubic2D"] = prefix + "ValueCubic2DRows";
                        }
                        blocks.Add("Single");
                    }
                    if (curl3) features.Add("Curl3");
                    source.Append(template.Generate(blocks, features, replacements)).AppendLine();
                }
                source.AppendLine("#line 1 \"WhimTex/BasisBindings\"")
                    .Append("float ").Append(request.name).Append("(float3 p,WTNoiseSettings n,");
                if (request.flow) source.Append("float phase,");
                source.AppendLine("out float3 gradient,out float3 curl) {");
                if (request.flow) source.AppendLine("float2 savedRotation=WTNoiseInternalRotation; WTNoiseInternalRotation=WTNoiseInternalFlowRotation(phase);");
                if (!derivative) source.AppendLine("gradient=0;");
                if (!derivative) source.AppendLine("curl=0;");
                source.Append("float value=").Append(prefix).Append(derivative ? "SampleSingle" : "Single")
                    .Append("(p,n,").Append(request.dimensions).Append(',').Append(request.kind);
                if (derivative) source.Append(curl3 ? ",true,gradient,curl" : ",false,gradient,curl");
                source.AppendLine(");");
                if (request.flow) source.AppendLine("WTNoiseInternalRotation=savedRotation;");
                source.AppendLine("return value; }");
            }
            return source.ToString();
        }

        Value EmitCellular(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            if (!cellularRequests.TryGetValue(node.id, out var request))
            {
                request = new CellularRequest { dimensions = ShaderGraphNodes.NoiseDimensions(node),
                    distribution = (int)node.noiseDistribution, metric = (int)node.noiseMetric, output = (int)node.noiseOutput,
                    filtering = node.noiseFiltering, name = "GraphCellRequest" + cellularRequests.Count };
                cellularRequests.Add(node.id, request);
            }
            request.scalar |= port == "Value";
            // Geometry always describes the raw first octave, independent of
            // smoothing/filtering. Share it only inside this evaluation context.
            string key = node.id + ":$noiseSample", data = settings + "Sample";
            if (!values.TryGetValue(key, out var sample))
            {
                sample = new Value(ShaderGraphValueType.Float, settings + "Value");
                body.Append("WTNoiseInternalCell ").Append(data).AppendLine(";")
                    .Append("float ").Append(sample.text).Append(" = ").Append(request.name).Append('(')
                    .Append(NoiseVector3(position, request.dimensions)).Append(',').Append(settings).Append(',')
                    .Append(settings).Append("Family,").Append(data).AppendLine(");");
                values.Add(key, sample);
            }
            string member = port switch
            { "F1" => "distance1", "F2" => "distance2", "CellValue" => "value", "ToPoint" => "toPoint", _ => null };
            string expression = member == null ? sample.text : data + "." + member;
            if (port == "ToPoint" && request.dimensions < 3) expression += request.dimensions == 1 ? ".x" : ".xy";
            return new Value(type, expression);
        }

        static readonly string[] CellularTemplateSymbols =
        {
            "Metric", "CellRadius", "CellBefore", "EvolutionSpeed", "EvolveFeature", "Feature", "CellCandidate",
            "CellBounds", "Cells", "CellMetricExtent", "CellCovering", "CellWindow", "CellBound", "CellBlend",
            "CellSmoothPair", "CellEdge", "CellValue", "CellTriangleIntegral", "CellUniformMean", "CellMeanPoint",
            "CellMeanScale", "CellEdgeMean", "CellularMean", "Cellular"
        };

        string CellularSource()
        {
            if (cellularRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCellular.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in cellularRequests.Values)
            {
                bool edge = request.output == (int)ShaderGraphNoiseOutput.DistanceToEdge;
                bool cellValue = request.output == (int)ShaderGraphNoiseOutput.CellValue;
                bool filtering = request.scalar && request.filtering;
                int metric = edge ? 0 : request.metric;
                string prefix = "GraphCellD" + request.dimensions + "R" + request.distribution + "M" + metric + "O" + request.output +
                    (request.scalar ? "S" : "G") + (filtering ? "F" : "U");
                if (emitted.Add(prefix))
                {
                    var features = new List<string> { "Dim" + request.dimensions,
                        request.distribution == 1 ? "FixedRadius" : "Uniform", edge ? "Edge" : cellValue ? "CellValue" : "Distance" };
                    var blocks = new List<string> { "Cells", "Cellular" };
                    if (request.scalar)
                    {
                        features.Add("Scalar");
                        if (edge) blocks.Add("CellEdge");
                        else
                        {
                            blocks.Add("CellValue");
                            blocks.Add(cellValue ? "CellBlend" : "CellSmoothPair");
                        }
                        if (filtering)
                        {
                            features.Add("Filtering");
                            blocks.Add("CellularMean");
                            if (edge) blocks.Add("CellEdgeMean");
                            else if (cellValue) blocks.Add("CellUniformMean");
                            else { blocks.Add("CellMeanScale"); blocks.Add("CellMeanPoint"); }
                        }
                    }
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string symbol in CellularTemplateSymbols)
                        replacements.Add("WTNoiseInternal" + symbol, prefix + symbol);
                    source.Append(template.Generate(blocks, features, replacements)).AppendLine()
                        .AppendLine("#line 1 \"WhimTex/CellularBindings\"");
                }
                source.Append("float ").Append(request.name)
                    .Append("(float3 p,WTNoiseSettings n,WTNoiseCellularSettings c,out WTNoiseInternalCell sample) { ")
                    .Append("c.distribution=").Append(request.distribution).Append("; c.metric=").Append(metric)
                    .Append("; c.output=").Append(request.output).Append("; return ").Append(prefix)
                    .Append("Cellular(p,n,c,").Append(request.dimensions).AppendLine(",sample); }");
            }
            return source.ToString();
        }


        [Flags]
        enum GaborResults { None = 0, Gradient = 1, Phase = 2, Intensity = 4, Phasor = 8 }
        sealed class GaborRequest
        {
            internal int dimensions;
            internal bool filtering;
            internal string name;
            internal GaborResults results;
        }
        readonly Dictionary<string, GaborRequest> gaborRequests = new(StringComparer.Ordinal);

        GaborRequest RequestGabor(ShaderGraphNode node, string port, bool curlField = false)
        {
            string key = node.id + (curlField ? ":curl" : "");
            if (!gaborRequests.TryGetValue(key, out var request))
            {
                request = new GaborRequest { dimensions = ShaderGraphNodes.NoiseDimensions(node), filtering = node.noiseFiltering,
                    name = "GraphGaborRequest" + gaborRequests.Count };
                gaborRequests.Add(key, request);
            }
            request.results |= port switch
            {
                "Gradient" or "Curl" => GaborResults.Gradient,
                "Phase" => GaborResults.Phase,
                "Intensity" => GaborResults.Intensity,
                "Phasor" => GaborResults.Phasor,
                _ => GaborResults.None
            };
            return request;
        }

        Value EmitGabor(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            var request = RequestGabor(node, port);
            string key = node.id + ":$gaborSample";
            string sample = settings + "Sample", gradient = settings + "Gradient";
            string p = NoiseVector3(position, request.dimensions);
            if (!values.TryGetValue(key, out var value))
            {
                value = new Value(ShaderGraphValueType.Float, settings + "Value");
                body.Append("WTNoiseGaborSample ").Append(sample).AppendLine(";")
                    .Append("float3 ").Append(gradient).AppendLine(";")
                    .Append("float ").Append(value.text).Append(" = ").Append(request.name).Append('(').Append(p).Append(',')
                    .Append(settings).Append(',').Append(settings).Append("Family,").Append(sample).Append(',').Append(gradient).AppendLine(");");
                values.Add(key, value);
            }
            string expression;
            if (port == "Curl" && request.dimensions == 3)
            {
                // The other two scalar fields use independent seeds. Their profile never
                // needs phase or intensity even when those outputs use the primary field.
                var other = RequestGabor(node, "Gradient", true);
                string curlKey = node.id + ":$gaborCurl", curl = settings + "Curl";
                if (!values.ContainsKey(curlKey))
                {
                    body.Append("WTNoiseSettings ").Append(curl).Append("Settings = ").Append(settings).AppendLine(";")
                        .Append(curl).AppendLine("Settings.seed ^= 0x68bc21eb;")
                        .Append("WTNoiseGaborSample ").Append(curl).AppendLine("Sample;")
                        .Append("float3 ").Append(curl).Append("Y,").Append(curl).AppendLine("Z;")
                        .Append(other.name).Append('(').Append(p).Append(',').Append(curl).Append("Settings,").Append(settings).Append("Family,")
                        .Append(curl).Append("Sample,").Append(curl).AppendLine("Y);")
                        .Append(curl).AppendLine("Settings.seed ^= 0x68bc21eb ^ 0x63d83595;")
                        .Append(other.name).Append('(').Append(p).Append(',').Append(curl).Append("Settings,").Append(settings).Append("Family,")
                        .Append(curl).Append("Sample,").Append(curl).AppendLine("Z);")
                        .Append("float3 ").Append(curl).Append(" = float3(").Append(curl).Append("Z.y-").Append(curl).Append("Y.z,")
                        .Append(gradient).Append(".z-").Append(curl).Append("Z.x,").Append(curl).Append("Y.x-").Append(gradient).AppendLine(".y);");
                    values.Add(curlKey, new Value(type, curl));
                }
                expression = curl;
            }
            else expression = port switch
            {
                "Gradient" => gradient + (request.dimensions == 1 ? ".x" : request.dimensions == 2 ? ".xy" : ""),
                "Curl" => "float2(" + gradient + ".y,-" + gradient + ".x)",
                "Phase" => sample + ".phase",
                "Intensity" => sample + ".intensity",
                "Phasor" => request.name + "Profile(" + sample + "," + node.option + "," + Input(node, "PhaseOffset").text + "," +
                    (node.option == 2 ? Input(node, "Width").text : "0.5") + ")",
                _ => value.text
            };
            return new Value(type, expression);
        }

        string GaborSource()
        {
            if (gaborRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseGabor.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in gaborRequests.Values)
            {
                string prefix = "GraphGabor" + request.dimensions + (request.filtering ? "F" : "U") + (int)request.results;
                if (emitted.Add(prefix))
                {
                    var features = new List<string> { "Dim" + request.dimensions };
                    if (request.filtering) features.Add("Filtering");
                    if ((request.results & GaborResults.Gradient) != 0) features.Add("RealGradient");
                    if ((request.results & GaborResults.Phase) != 0) features.Add("Phase");
                    if ((request.results & GaborResults.Intensity) != 0) features.Add("Intensity");
                    if ((request.results & GaborResults.Phasor) != 0) features.Add("PhaseFilter");
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string symbol in new[] { "WTNoiseInternalPhasorArea", "WTNoiseInternalPhasorProfile", "WTNoiseInternalPhasorFilter",
                        "WTNoiseInternalGaborProfile", "WTNoiseInternalGaborDirection", "WTNoiseInternalGaborComplex", "WTNoiseInternalGabor" })
                        replacements.Add(symbol, prefix + symbol.Substring("WTNoiseInternal".Length));
                    string generated = template.Generate((request.results & GaborResults.Phasor) != 0 ? new[] { "Sample", "Profile" } : new[] { "Sample" }, features, replacements);
                    source.Append(generated).AppendLine().AppendLine("#line 1 \"WhimTex/GaborBindings\"");
                }
                source.Append("float ").Append(request.name).Append("(float3 p,WTNoiseSettings n,WTNoiseGaborSettings g,out WTNoiseGaborSample sample,out float3 gradient) { return ")
                    .Append(prefix).Append("Gabor(p,n,g,").Append(request.dimensions).AppendLine(",sample,gradient); }");
                if ((request.results & GaborResults.Phasor) != 0)
                    source.Append("float ").Append(request.name).Append("Profile(WTNoiseGaborSample sample,int profile,float offset,float width) { return ")
                        .Append(prefix).AppendLine("GaborProfile(sample,profile,offset,width); }");
            }
            return source.ToString();
        }
    }
}
