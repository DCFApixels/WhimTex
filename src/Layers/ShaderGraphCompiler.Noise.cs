using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        void ValidateNoiseOptions(ShaderGraphNode n)
        {
            if (!ShaderGraphNodes.IsNoise(n.kind)) return;
            if (n.kind == ShaderGraphNodeKind.TriangleVoronoiNoise && n.noiseDimensions != ShaderGraphNoiseDimensions.TwoD)
                Error(n, "Triangle Voronoi Noise supports 2D positions only.");
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
            string name = "gn" + n.id;
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
            Set("scale", "Scale", true); Set("offset", "Offset", true); Set("period", "Period", true);
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
                    dx = $"({field.text}(graphUV+_GraphSampleDx*0.5)-{field.text}(graphUV-_GraphSampleDx*0.5))";
                    dy = $"({field.text}(graphUV+_GraphSampleDy*0.5)-{field.text}(graphUV-_GraphSampleDy*0.5))";
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
                case ShaderGraphNodeKind.FlowNoise: Family("Flow"); Field("phase", "Phase"); break;
                case ShaderGraphNodeKind.ShardNoise: Family("Shard"); Field("sharpness", "Sharpness"); break;
                case ShaderGraphNodeKind.WaveletNoise: Family("Wavelet"); Field("frequency", "Frequency"); Field("phase", "Phase"); break;
                case ShaderGraphNodeKind.TriangleVoronoiNoise:
                    Family("TriangleVoronoi"); Line($"{family}.distribution = {(int)n.noiseDistribution};"); Field("jitter", "Jitter"); Field("evolution", "Evolution"); Field("stretch", "Stretch"); break;
                case ShaderGraphNodeKind.CellularNoise:
                    Family("Cellular"); Line($"{family}.distribution = {(int)n.noiseDistribution}; {family}.metric = {(int)n.noiseMetric}; {family}.output = {(int)n.noiseOutput};");
                    Field("jitter", "Jitter"); Field("evolution", "Evolution"); Field("smoothness", "Smoothness"); break;
                case ShaderGraphNodeKind.VoronoiEdgeNoise:
                    Family("Voronoi"); Line($"{family}.distribution = {(int)n.noiseDistribution};"); Field("jitter", "Jitter"); Field("evolution", "Evolution"); break;
                case ShaderGraphNodeKind.VoronoiseNoise:
                    Family("Voronoise"); Line($"{family}.distribution = {(int)n.noiseDistribution};"); Field("jitter", "Jitter"); Field("evolution", "Evolution"); Field("smoothness", "Smoothness"); break;
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
            ShaderGraphNodes.TryNoiseOutputType(n, port, out var type);
            string position = Input(n, "Position").text;
            if (ShaderGraphNodes.IsGrainNoise(n.kind)) return EmitGrainNoise(n, port, position);
            string settings = NoiseSettings(n, position), family = settings + "Family";
            string arguments = position + "," + settings;
            string function = n.kind switch
            {
                ShaderGraphNodeKind.PerlinNoise => "Perlin", ShaderGraphNodeKind.ValueNoise => "Value",
                ShaderGraphNodeKind.ValueCubicNoise => "ValueCubic", ShaderGraphNodeKind.OpenSimplex2Noise => "OpenSimplex2",
                ShaderGraphNodeKind.OpenSimplex2SNoise => "OpenSimplex2S", ShaderGraphNodeKind.FlowNoise => "Flow",
                ShaderGraphNodeKind.CellularNoise => "Cellular", ShaderGraphNodeKind.VoronoiEdgeNoise => "VoronoiEdge",
                ShaderGraphNodeKind.VoronoiseNoise => "Voronoise", ShaderGraphNodeKind.GaborNoise => "Gabor",
                ShaderGraphNodeKind.SpotsNoise => "Spots", ShaderGraphNodeKind.ShardNoise => "Shard",
                ShaderGraphNodeKind.TriangleVoronoiNoise => "TriangleVoronoi", ShaderGraphNodeKind.WaveletNoise => "Wavelet", _ => "Displacement"
            };
            bool basis = n.kind is ShaderGraphNodeKind.PerlinNoise or ShaderGraphNodeKind.ValueNoise or ShaderGraphNodeKind.ValueCubicNoise or
                ShaderGraphNodeKind.OpenSimplex2Noise or ShaderGraphNodeKind.OpenSimplex2SNoise;
            if (!basis) arguments += "," + family;
            if (port is "Gradient" or "Curl")
            {
                if (basis) arguments += "," + (n.kind switch
                {
                    ShaderGraphNodeKind.PerlinNoise => "WT_NOISE_PERLIN", ShaderGraphNodeKind.ValueNoise => "WT_NOISE_VALUE",
                    ShaderGraphNodeKind.ValueCubicNoise => "WT_NOISE_VALUE_CUBIC", ShaderGraphNodeKind.OpenSimplex2Noise => "WT_NOISE_OPENSIMPLEX2", _ => "WT_NOISE_OPENSIMPLEX2S"
                });
                return new Value(type, "WTNoise" + port + "(" + arguments + ")");
            }
            if (n.kind is ShaderGraphNodeKind.CellularNoise or ShaderGraphNodeKind.GaborNoise or ShaderGraphNodeKind.TriangleVoronoiNoise || ShaderGraphNodes.IsWarpNoise(n.kind))
            {
                string key = n.id + ":$noiseSample";
                if (!values.TryGetValue(key, out var sample))
                {
                    string variable = settings + "Value", data = settings + "Sample";
                    bool warp = ShaderGraphNodes.IsWarpNoise(n.kind);
                    if (!warp)
                    {
                        string sampleType = n.kind == ShaderGraphNodeKind.GaborNoise ? "WTNoiseGaborSample" :
                            n.kind == ShaderGraphNodeKind.TriangleVoronoiNoise ? "WTNoiseTriangleVoronoiSample2D" : "WTNoiseCellularSample" + ShaderGraphNodes.NoiseDimensions(n) + "D";
                        body.AppendLine(sampleType + " " + data + ";"); arguments += "," + data;
                    }
                    var valueType = warp ? ShaderGraphNodes.NoisePositionType(n) : ShaderGraphValueType.Float;
                    body.AppendLine($"{Type(valueType)} {variable} = WTNoise{function}({arguments});");
                    sample = new Value(valueType, variable); values.Add(key, sample);
                }
                if (ShaderGraphNodes.IsWarpNoise(n.kind)) return new Value(type, port == "Position" ? "(" + position + "+" + sample.text + ")" : sample.text);
                string member = port switch { "F1" => "distance1", "F2" => "distance2", "Distance" => "distance", "BorderNormal" => "borderNormal", "CellValue" => "value", "ToPoint" => "toPoint", "Phase" => "phase", "Intensity" => "intensity", _ => null };
                return new Value(type, member == null ? sample.text : settings + "Sample." + member);
            }
            return new Value(type, $"WTNoise{function}({arguments})");
        }
        Value EmitGrainNoise(ShaderGraphNode n, string port, string position)
        {
            string V(string p) => Input(n, p).text;
            string name = "gg" + n.id + port;
            // Resolve connected expressions before appending a statement to the shared body.
            string init = $"WTNoiseGrainSettings {name} = WTNoiseGrainDefaults({V("ImageSize")},asint(asuint((int)({V("Seed")}))+asuint(_GraphNoiseSeed)));";
            body.AppendLine(init);
            string fields = $"{name}.offset = {V("Offset")}; {name}.grainSize = {V("GrainSize")};";
            body.AppendLine(fields);
            if (n.noiseDimensions == ShaderGraphNoiseDimensions.OneD)
            {
                string direction = V("Direction"); body.AppendLine($"{name}.oneD = true; {name}.direction = {direction};");
            }
            body.AppendLine($"{name}.inverted = {(n.noiseInverted ? "true" : "false")}; {name}.color = {(port == "Color" ? "true" : "false")};");
            string expression = $"WTNoise{(n.kind == ShaderGraphNodeKind.WhiteNoise ? "White" : "Blue")}({position},{name})";
            return port == "Color" ? new Value(ShaderGraphValueType.Color, "float4(" + expression + ",1)") : new Value(ShaderGraphValueType.Float, expression + ".x");
        }
    }
}
