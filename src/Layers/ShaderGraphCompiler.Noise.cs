using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        internal const string ErosionNoiseInclude = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/GraphErosionNoise.hlsl\"";
        bool usesErosionNoise;

        void ValidateNoiseOptions(ShaderGraphNode n)
        {
            if (n.kind == ShaderGraphNodeKind.CausticsNoise)
            {
                if (n.noiseDimensions != ShaderGraphNoiseDimensions.TwoD)
                    Error(n, "Caustics Noise supports 2D positions only.");
                if (n.fractal != ShaderGraphFractal.None)
                    Error(n, "Noise octaves are configured by a separate Fractal node.");
                return;
            }
            if (!ShaderGraphNodes.IsNoise(n.kind)) return;
            if (ShaderGraphNodes.IsFixed2DNoise(n.kind) && n.noiseDimensions != ShaderGraphNoiseDimensions.TwoD)
                Error(n, "Supports 2D positions only.");
            if (n.kind == ShaderGraphNodeKind.ErosionNoise && n.noiseFiltering)
                Error(n, "Filtering is unavailable for this noise.");
            if (!ShaderGraphNodes.IsWarpNoise(n.kind) && n.fractal != ShaderGraphFractal.None)
                Error(n, "Noise octaves are configured by a separate Fractal node.");
            if (!Enum.IsDefined(typeof(ShaderGraphNoiseDimensions), n.noiseDimensions) ||
                !Enum.IsDefined(typeof(ShaderGraphNoiseDistribution), n.noiseDistribution) ||
                !Enum.IsDefined(typeof(ShaderGraphNoiseMetric), n.noiseMetric) ||
                !Enum.IsDefined(typeof(ShaderGraphNoiseOutput), n.noiseOutput) ||
                ShaderGraphNodes.IsGrainNoise(n.kind) && (n.noiseDimensions == ShaderGraphNoiseDimensions.ThreeD || n.fractal != ShaderGraphFractal.None))
                Error(n, "Unknown noise option.");
        }
        static string NoiseVector3(string value, int dimensions) => dimensions switch
        { 1 => "float3(" + value + ",0,0)", 2 => "float3(" + value + ",0)", _ => value };

        string NoiseSettings(ShaderGraphNode n, string position)
        {
            string key = n.id + ":$noiseSettings";
            if (values.TryGetValue(key, out var cached)) return cached.text;
            int dimensions = ShaderGraphNodes.NoiseDimensions(n);
            string name = "gn" + NodeCodeName(n);
            string V(string port) => Input(n, port).text;
            void Line(string text) => body.AppendLine(text);
            void Set(string field, string port, bool vector = false, bool integer = false)
            {
                if (ShaderGraphNodes.NoiseInputInactiveReason(n, port) != null) return;
                string value = V(port);
                if (vector) value = NoiseVector3(value, dimensions);
                if (integer) value = "(int)(" + value + ")";
                Line(name + "." + field + " = " + value + ";");
            }
            Line($"WTNoiseSettings {name} = WTNoiseDefaults(asint(asuint((int)({V("Seed")}))+asuint(_GraphNoiseSeed)));");
            Set("scale", "Scale", true);
            // Disabled periodicity keeps both defaults at zero and omits their upstream branches.
            if (n.noisePeriodic) { Set("period", "Period", true); Set("offset", "Offset", true); }
            bool warp = ShaderGraphNodes.IsWarpNoise(n.kind);
            if (warp)
            {
                Set("fractal.octaves", "Octaves", integer: true); Set("fractal.lacunarity", "Lacunarity"); Set("fractal.gain", "Gain");
            }
            if (n.noiseFiltering)
            {
                string dx, dy;
                if (sampledEvaluation)
                {
                    // Evaluate footprints outside derivative intrinsics in divergent filter loops.
                    usesSampleFootprint = true;
                    var field = SamplingExpression(n.id + ":noisePosition", () => Input(n, "Position"), n);
                    dx = $"({Call(field, "graphUV+_GraphSampleDx*0.5")}-{Call(field, "graphUV-_GraphSampleDx*0.5")})";
                    dy = $"({Call(field, "graphUV+_GraphSampleDy*0.5")}-{Call(field, "graphUV-_GraphSampleDy*0.5")})";
                }
                else { dx = "ddx(" + position + ")"; dy = "ddy(" + position + ")"; }
                Line($"{name}.dpdx = {NoiseVector3(dx, dimensions)};");
                Line($"{name}.dpdy = {NoiseVector3(dy, dimensions)};");
            }
            string family = name + "Family";
            void Family(string type) => Line($"WTNoise{type}Settings {family} = WTNoise{type}Defaults();");
            void Field(string field, string port, bool vector = false)
            {
                if (ShaderGraphNodes.NoiseInputInactiveReason(n, port) != null) return;
                string value = V(port);
                Line($"{family}.{field} = {(vector ? NoiseVector3(value, dimensions) : value)};");
            }
            switch (n.kind)
            {
                case ShaderGraphNodeKind.CausticsNoise: Family("Caustics"); Field("evolution", "Evolution"); Field("strength", "Strength"); break;
                case ShaderGraphNodeKind.FlowNoise: Family("Flow"); Field("phase", "Phase"); break;
                case ShaderGraphNodeKind.ShardNoise: Family("Shard"); Field("sharpness", "Sharpness"); break;
                case ShaderGraphNodeKind.TriangleVoronoiNoise:
                    Family("TriangleVoronoi"); Line($"{family}.distribution = {(int)n.noiseDistribution};"); Field("jitter", "Jitter"); Field("evolution", "Evolution"); Field("stretch", "Stretch"); break;
                case ShaderGraphNodeKind.CellularNoise:
                    Family("Cellular"); Line($"{family}.distribution = {(int)n.noiseDistribution}; {family}.metric = {(int)n.noiseMetric}; {family}.output = {(int)n.noiseOutput};");
                    Field("jitter", "Jitter", true); Field("evolution", "Evolution"); Field("smoothness", "Smoothness"); break;
                case ShaderGraphNodeKind.GaborNoise:
                    Family("Gabor"); Field("frequency", "Frequency"); Field("bandwidth", "Bandwidth"); Field("anisotropy", "Anisotropy"); Field("direction", "Direction", true); break;
                case ShaderGraphNodeKind.SpotsNoise:
                    Family("Spot"); Field("density", "Density"); Field("radius", "Radius"); Field("softness", "Softness"); Field("elongation", "Elongation"); Field("direction", "Direction", true); break;
                case ShaderGraphNodeKind.OpenSimplex2WarpNoise: case ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise: case ShaderGraphNodeKind.BasicGridWarpNoise:
                    Family("Warp");
                    int algorithm = n.kind == ShaderGraphNodeKind.OpenSimplex2WarpNoise ? 0 : n.kind == ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise ? 1 : 2;
                    int mode = n.fractal == ShaderGraphFractal.Progressive ? 1 : n.fractal == ShaderGraphFractal.Independent ? 2 : 0;
                    Line($"{family}.algorithm = {algorithm}; {family}.mode = {mode};"); Field("strength", "Strength"); break;
            }
            values.Add(key, new Value(ShaderGraphValueType.Float, name));
            return name;
        }
        Value EmitNoise(ShaderGraphNode n, string port)
        {
            if (n.kind == ShaderGraphNodeKind.ErosionNoise) return EmitErosionNoise(n, port);
            ShaderGraphNodes.TryNoiseOutputType(n, port, out var type);
            string position = Input(n, "Position").text;
            if (ShaderGraphNodes.IsGrainNoise(n.kind)) return EmitGrainTemplateNoise(n, port, position);
            string settings = NoiseSettings(n, position);
            if (ShaderGraphNodes.IsWarpNoise(n.kind)) return EmitWarp(n, port, position, settings, type);
            if (n.kind == ShaderGraphNodeKind.GaborNoise) return EmitGabor(n, port, position, settings, type);
            if (n.kind == ShaderGraphNodeKind.CellularNoise) return EmitCellular(n, port, position, settings, type);
            if (n.kind is ShaderGraphNodeKind.PerlinNoise or ShaderGraphNodeKind.ValueNoise or ShaderGraphNodeKind.ValueCubicNoise or
                ShaderGraphNodeKind.OpenSimplex2Noise or ShaderGraphNodeKind.OpenSimplex2SNoise or ShaderGraphNodeKind.FlowNoise)
                return EmitBasis(n, port, position, settings, type);
            if (n.kind == ShaderGraphNodeKind.ShardNoise) return EmitShard(n, port, position, settings, type);
            if (n.kind == ShaderGraphNodeKind.SpotsNoise) return EmitSpots(n, position, settings, type);
            if (n.kind == ShaderGraphNodeKind.TriangleVoronoiNoise) return EmitTriangle(n, port, position, settings, type);
            throw new InvalidOperationException("Unsupported noise node: " + n.kind);
        }

        Value EmitErosionNoise(ShaderGraphNode node, string port)
        {
            usesErosionNoise = true;
            string key = node.id + ":$erosion";
            if (!values.TryGetValue(key, out var sample))
            {
                string V(string input) => Input(node, input).text;
                string position = V("Position"), period = "float2(0,0)";
                if (node.noisePeriodic)
                {
                    position = $"({position}+{V("Offset")})";
                    period = V("Period");
                }
                string name = "ge" + NodeCodeName(node);
                string seed = $"asint(asuint((int)({V("Seed")}))+asuint(_GraphNoiseSeed))";
                body.AppendLine($"float3 {name} = GraphErosionNoise({position},{V("Scale")},{period},{seed}," +
                    $"{V("Strength")},{V("Detail")},{V("SlopeStrength")},{V("Branching")}," +
                    $"{V("Octaves")},{V("Lacunarity")},{V("Gain")});");
                sample = new Value(ShaderGraphValueType.Float3, name);
                values.Add(key, sample);
            }
            string component = port switch { "Base" => ".y", "Erosion" => ".z", _ => ".x" };
            return new Value(ShaderGraphValueType.Float, sample.text + component);
        }
    }
}
