using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        sealed class ShardRequest
        {
            internal int dimensions;
            internal string name;
        }
        sealed class SpotsRequest
        {
            internal int dimensions;
            internal bool filtering;
            internal string name;
        }
        [Flags]
        enum TriangleResults { None = 0, Value = 1, Distance = 2, CellValue = 4, ToPoint = 8, BorderNormal = 16 }
        sealed class TriangleRequest
        {
            internal int distribution;
            internal bool filtering;
            internal string name;
            internal TriangleResults results;
        }
        sealed class GrainRequest
        {
            internal int dimensions;
            internal bool blue, inverted, color;
            internal string name;
        }
        readonly Dictionary<string, ShardRequest> shardRequests = new(StringComparer.Ordinal);
        readonly Dictionary<string, SpotsRequest> spotsRequests = new(StringComparer.Ordinal);
        readonly Dictionary<string, TriangleRequest> triangleRequests = new(StringComparer.Ordinal);
        readonly Dictionary<string, GrainRequest> grainRequests = new(StringComparer.Ordinal);

        string RemainingNoiseTemplateSource() => ShardSource() + SpotsSource() + TriangleSource() + GrainSource();

        // Request IDs live in the current values scope. Sampling functions and loop
        // bodies must discover their own output profiles before source generation.
        Value EmitShard(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            string key = node.id + ":$shardTemplate";
            if (!values.TryGetValue(key, out var cached))
            {
                int dimensions = ShaderGraphNodes.NoiseDimensions(node);
                string name = "GraphShardRequest" + shardRequests.Count;
                shardRequests.Add(name, new ShardRequest { name = name, dimensions = dimensions });
                cached = new Value(type, settings + "Value");
                values.Add(key, cached);
                body.Append("float ").Append(cached.text).Append(" = ").Append(name).Append('(')
                    .Append(NoiseVector3(position, dimensions)).Append(',').Append(settings).Append(',')
                    .Append(settings).AppendLine("Family);");
            }
            return cached;
        }

        string ShardSource()
        {
            if (shardRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseShard.hlsl");
            var source = new StringBuilder(template.Generate(new[] { "Sample" }, null,
                RemainingNoiseSymbols("GraphShard", "ShardValue", "Shard")));
            // Keep the shared kernel's original control flow. Shortening its constant
            // loops made 2D execution slower, despite a smaller generated source.
            foreach (var request in shardRequests.Values)
                source.AppendLine("#line 1 \"WhimTex/ShardBindings\"")
                    .Append("float ").Append(request.name).Append("(float3 p,WTNoiseSettings n,WTNoiseShardSettings s) { return ")
                    .Append("GraphShardShard(p,n,s,").Append(request.dimensions).AppendLine("); }");
            return source.ToString();
        }

        Value EmitSpots(ShaderGraphNode node, string position, string settings, ShaderGraphValueType type)
        {
            string key = node.id + ":$spotsTemplate";
            if (!values.TryGetValue(key, out var cached))
            {
                int dimensions = ShaderGraphNodes.NoiseDimensions(node);
                string name = "GraphSpotsRequest" + spotsRequests.Count;
                spotsRequests.Add(name, new SpotsRequest { name = name, dimensions = dimensions, filtering = node.noiseFiltering });
                cached = new Value(type, settings + "Value");
                values.Add(key, cached);
                body.Append("float ").Append(cached.text).Append(" = ").Append(name).Append('(')
                    .Append(NoiseVector3(position, dimensions)).Append(',').Append(settings).Append(',')
                    .Append(settings).AppendLine("Family);");
            }
            return cached;
        }

        string SpotsSource()
        {
            if (spotsRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseSpots.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in spotsRequests.Values)
            {
                string prefix = "GraphSpotsD" + request.dimensions + (request.filtering ? "F" : "U");
                if (emitted.Add(prefix))
                {
                    var features = new List<string> { "Dim" + request.dimensions };
                    var blocks = new List<string> { "Sample" };
                    if (request.filtering) { features.Add("Filtering"); blocks.Add("Mean"); }
                    source.Append(template.Generate(blocks, features,
                        RemainingNoiseSymbols(prefix, "SpotDirection", "SpotKernel", "SpotsValue", "SpotMean", "Spots"))).AppendLine();
                }
                source.AppendLine("#line 1 \"WhimTex/SpotsBindings\"")
                    .Append("float ").Append(request.name).Append("(float3 p,WTNoiseSettings n,WTNoiseSpotSettings s) { return ")
                    .Append(prefix).Append("Spots(p,n,s,").Append(request.dimensions).AppendLine("); }");
            }
            return source.ToString();
        }

        Value EmitTriangle(ShaderGraphNode node, string port, string position, string settings, ShaderGraphValueType type)
        {
            string key = node.id + ":$triangleTemplate";
            if (!values.TryGetValue(key, out var cached))
            {
                string name = "GraphTriangleRequest" + triangleRequests.Count;
                triangleRequests.Add(name, new TriangleRequest { name = name, distribution = (int)node.noiseDistribution,
                    filtering = node.noiseFiltering });
                cached = new Value(ShaderGraphValueType.Float, name);
                values.Add(key, cached);
                body.Append("WTNoiseTriangleVoronoiSample2D ").Append(settings).AppendLine("Sample;")
                    .Append("float ").Append(settings).Append("Value = ").Append(name).Append('(').Append(position)
                    .Append(',').Append(settings).Append(',').Append(settings).Append("Family,").Append(settings).AppendLine("Sample);");
            }
            triangleRequests[cached.text].results |= port switch
            {
                "Distance" => TriangleResults.Distance, "CellValue" => TriangleResults.CellValue,
                "ToPoint" => TriangleResults.ToPoint, "BorderNormal" => TriangleResults.BorderNormal, _ => TriangleResults.Value
            };
            string member = port switch
            { "Distance" => "distance", "CellValue" => "value", "ToPoint" => "toPoint", "BorderNormal" => "borderNormal", _ => null };
            return new Value(type, member == null ? settings + "Value" : settings + "Sample." + member);
        }

        string TriangleSource()
        {
            if (triangleRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseTriangleVoronoi.hlsl");
            var cellular = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCellular.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in triangleRequests.Values)
            {
                bool scalar = (request.results & TriangleResults.Value) != 0;
                bool edge = (request.results & (TriangleResults.Value | TriangleResults.BorderNormal)) != 0;
                bool filtering = scalar && request.filtering;
                string prefix = "GraphTriangleR" + request.distribution + "O" + (int)request.results + (filtering ? "F" : "U");
                if (emitted.Add(prefix))
                {
                    string distribution = request.distribution == 1 ? "FixedRadius" : "Uniform";
                    // Keep the original octave preparation/control flow. The graph
                    // supplies None (one octave); removing the loop slowed GPU execution.
                    var features = new List<string> { distribution, "Fractal" };
                    if (scalar) features.Add("Scalar");
                    if (filtering) features.Add("Filtering");
                    if ((request.results & TriangleResults.Distance) != 0) features.Add("Distance");
                    if ((request.results & TriangleResults.CellValue) != 0) features.Add("CellValue");
                    if ((request.results & TriangleResults.ToPoint) != 0) features.Add("ToPoint");
                    if ((request.results & TriangleResults.BorderNormal) != 0) features.Add("BorderNormal");
                    var blocks = new List<string> { "Triangle" };
                    if (edge) blocks.Add("Border");
                    var cellBlocks = new List<string> { "Feature", "CellBefore" };
                    if (filtering) cellBlocks.Add("CellEdgeMean");
                    var replacements = RemainingNoiseSymbols(prefix, "TriangleAxis", "TriangleMetric", "TriangleBox", "TriangleBounds",
                        "TriangleConsider", "TriangleBorder", "TriangleSample", "Triangle", "CellRadius", "CellBefore",
                        "EvolutionSpeed", "EvolveFeature", "Feature", "CellEdgeMean", "CellMeanPoint");
                    source.Append(cellular.Generate(cellBlocks, new[] { "Dim2", distribution }, replacements)).AppendLine()
                        .Append(template.Generate(blocks, features, replacements)).AppendLine();
                }
                // Match the public sample overload: evaluate the first octave even
                // for Value-only requests. A visibility branch slowed the kernel.
                source.AppendLine("#line 1 \"WhimTex/TriangleBindings\"")
                    .Append("float ").Append(request.name)
                    .Append("(float2 p,WTNoiseSettings n,WTNoiseTriangleVoronoiSettings t,out WTNoiseTriangleVoronoiSample2D sample) { ")
                    .Append("t.distribution=").Append(request.distribution).Append("; return ").Append(prefix)
                    .AppendLine("Triangle(p,n,t,true,sample); }");
            }
            return source.ToString();
        }

        Value EmitGrainTemplateNoise(ShaderGraphNode node, string port, string position)
        {
            string key = node.id + ":$grainTemplate", settings = "gg" + NodeCodeName(node);
            if (!values.TryGetValue(key, out var cached))
            {
                int dimensions = ShaderGraphNodes.NoiseDimensions(node);
                string name = "GraphGrainRequest" + grainRequests.Count;
                grainRequests.Add(name, new GrainRequest { name = name, dimensions = dimensions,
                    blue = node.kind == ShaderGraphNodeKind.BlueNoise, inverted = node.noiseInverted });
                cached = new Value(ShaderGraphValueType.Float, name);
                values.Add(key, cached);
                // Resolve connected inputs before appending statements to the shared body.
                string size = Input(node, "ImageSize").text, seed = Input(node, "Seed").text;
                string grainSize = Input(node, "GrainSize").text;
                string direction = dimensions == 1 ? Input(node, "Direction").text : "0";
                body.Append("WTNoiseGrainSettings ").Append(settings).Append(" = WTNoiseGrainDefaults(").Append(size)
                    .Append(",asint(asuint((int)(").Append(seed).AppendLine("))+asuint(_GraphNoiseSeed)));")
                    .Append(settings).Append(".grainSize = ").Append(grainSize).AppendLine(";")
                    .Append(settings).Append(".direction = ").Append(direction).AppendLine(";")
                    .Append("float3 ").Append(settings).Append("Value = ").Append(name).Append('(').Append(position)
                    .Append(',').Append(settings).AppendLine(");");
            }
            grainRequests[cached.text].color |= port == "Color";
            return port == "Color" ? new Value(ShaderGraphValueType.Color, "float4(" + settings + "Value,1)") :
                new Value(ShaderGraphValueType.Float, settings + "Value.x");
        }

        string GrainSource()
        {
            if (grainRequests.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseGrain.hlsl");
            var source = new StringBuilder();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in grainRequests.Values)
            {
                string family = request.blue ? "Blue" : "White";
                string prefix = "GraphGrain" + family + "D" + request.dimensions + (request.color ? "C" : "V") + (request.inverted ? "I" : "U");
                if (emitted.Add(prefix))
                {
                    var features = new List<string> { "Dim" + request.dimensions };
                    if (request.color) features.Add("Color");
                    if (request.inverted) features.Add("Inverted");
                    var replacements = RemainingNoiseSymbols(prefix, "GrainCell", "Blue");
                    replacements.Add("WTNoiseWhite", prefix + "White");
                    replacements.Add("WTNoiseBlue", prefix + "BlueSample");
                    source.Append(template.Generate(new[] { family }, features, replacements)).AppendLine();
                }
                source.AppendLine("#line 1 \"WhimTex/GrainBindings\"")
                    .Append("float3 ").Append(request.name).Append("(float2 uv,WTNoiseGrainSettings n) { n.oneD=")
                    .Append(request.dimensions == 1 ? "true" : "false").Append("; n.color=").Append(request.color ? "true" : "false")
                    .Append("; n.inverted=").Append(request.inverted ? "true" : "false").Append("; return ")
                    .Append(prefix).Append(request.blue ? "BlueSample" : "White").AppendLine("(uv,n); }");
            }
            return source.ToString();
        }

        static Dictionary<string, string> RemainingNoiseSymbols(string prefix, params string[] symbols)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string symbol in symbols) result.Add("WTNoiseInternal" + symbol, prefix + symbol);
            return result;
        }
    }
}
