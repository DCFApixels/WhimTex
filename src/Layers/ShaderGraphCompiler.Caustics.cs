using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool causticsColorPass;
        bool usesCausticsGamut;
        bool HasCustomCausticsSpectrum(ShaderGraphNode node)
            => node.FindInput("Spectrum")?.resource != null || incoming.ContainsKey(node.id + ":Spectrum");

        Value EmitCaustics(ShaderGraphNode node, string port)
        {
            bool color = port == "Color";
            var pass = RequireBuffer(node, causticsColor: color);
            string sample = $"GraphSample({pass.textureName},{Input(node, "UV").text},{(int)node.wrap},1)";
            if (!color) return new Value(ShaderGraphValueType.Float, sample + ".r");
            string rgb = sample + ".rgb";
            if (pass.causticsSpectrum == null)
            {
                usesCausticsGamut = true;
                rgb = $"WTCausticsFitSpectrumToRGB({rgb})";
            }
            return new Value(ShaderGraphValueType.Color, $"float4({rgb},1)");
        }

        Value CausticsRays(ShaderGraphNode node, bool color)
        {
            usesSampleFootprint = true;
            var heightPass = RequireBuffer(node, height: true);
            string Sample(string uv) => $"GraphSample({heightPass.textureName},{uv},{(int)node.wrap},1).r";
            string V(string name) => Input(node, name).text;
            string depth = V("Depth"), ior = V("IOR"), light = V("LightDirection"),
                softness = V("Softness"), intensity = V("Intensity");
            if (color)
            {
                causticsColorPass = true;
                if (HasCustomCausticsSpectrum(node))
                    result.causticsSpectrum = Input(node, "Spectrum").text;
                ior = $"WTCausticsRefractiveIndex({ior},{V("Dispersion")},_GraphCausticsBand)";
            }
            body.AppendLine("float2 delta=_GraphBakeSize.zw;")
                .Append("float hx=(").Append(Sample("graphUV+float2(delta.x,0)")).Append('-').Append(Sample("graphUV-float2(delta.x,0)")).AppendLine(")/(2*delta.x);")
                .Append("float hy=(").Append(Sample("graphUV+float2(0,delta.y)")).Append('-').Append(Sample("graphUV-float2(0,delta.y)")).AppendLine(")/(2*delta.y);")
                .AppendLine("float3 normal=normalize(float3(-float2(hx,hy),1));")
                .Append("float3 light=").Append(light).AppendLine("; light=dot(light,light)>1e-12 ? normalize(light) : float3(0,0,-1);")
                .Append("float eta=1/max(").Append(ior).AppendLine(",1);")
                .AppendLine("float3 ray=refract(light,normal,eta);")
                .AppendLine("float3 flatRay=refract(light,float3(0,0,1),eta);")
                .AppendLine("bool valid=light.z<0 && dot(light,normal)<0 && ray.z<-1e-4;")
                .Append("float2 displacement=max(").Append(depth).AppendLine(",0)*(ray.xy/max(-ray.z,1e-4)-flatRay.xy/max(-flatRay.z,1e-4));")
                .Append("float4 causticRays=float4(valid ? displacement : 0,valid ? max(").Append(intensity).Append(",0) : 0,max(").Append(softness).AppendLine(",0));");
            return new Value(ShaderGraphValueType.Float4, "causticRays");
        }

        Value CausticsHeight(ShaderGraphNode node)
        {
            return new Value(ShaderGraphValueType.Float, $"({Input(node, "Height").text}*{Input(node, "Strength").text})");
        }

        readonly HashSet<(bool periodic, int algorithm)> causticsNoiseVariants = new();
        static string CausticsNoisePrefix(bool periodic, int algorithm)
            => "GraphCaustics" + (periodic ? "P" : "N") + (algorithm == 0 ? "Open" : "Simplex");
        Value EmitCausticsNoise(ShaderGraphNode node, string port)
        {
            string position = Input(node, "Position").text;
            string settings = NoiseSettings(node, position);
            causticsNoiseVariants.Add((node.noisePeriodic, node.option));
            string prefix = CausticsNoisePrefix(node.noisePeriodic, node.option);
            if (port == "Color")
            {
                bool custom = HasCustomCausticsSpectrum(node);
                string palette = "WTCausticsCieD65Palette()";
                if (custom)
                {
                    string spectrum = Input(node, "Spectrum").text;
                    palette = $"WTCausticsMakePalette({spectrum}_Sample(0).rgb,{spectrum}_Sample(0.2).rgb,{spectrum}_Sample(0.4).rgb,{spectrum}_Sample(0.6).rgb,{spectrum}_Sample(0.8).rgb,{spectrum}_Sample(1).rgb,{(node.causticsWhiteBalance ? "true" : "false")})";
                }
                return new Value(ShaderGraphValueType.Color,
                    $"float4({prefix}CausticsSamplePalette({position},{settings},{settings}Family,{node.option},true,{Input(node, "Dispersion").text},{palette},{(custom ? "false" : "true")}),1)");
            }
            return new Value(ShaderGraphValueType.Float, $"{prefix}CausticsSample({position},{settings},{settings}Family,{node.option},true)");
        }

        string CausticsNoiseSource()
        {
            if (causticsNoiseVariants.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCaustics.hlsl");
            var source = new StringBuilder();
            source.Append(ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCausticsSpectrum.hlsl")
                .Generate(new[] { "Palette" }, System.Array.Empty<string>(), new Dictionary<string, string>()));
            foreach (var variant in causticsNoiseVariants)
            {
                string prefix = CausticsNoisePrefix(variant.periodic, variant.algorithm);
                var positionFeatures = variant.periodic ? new[] { "Periodic" } : System.Array.Empty<string>();
                string algorithm = variant.algorithm == 0 ? "OpenSimplex" : "Simplex";
                var features = variant.periodic ? new[] { "Periodic", algorithm } : new[] { algorithm };
                var symbols = RemainingNoiseSymbols(prefix, "CausticsMod", "CausticsPermute", "CausticsGradient",
                    "CausticsPart", "CausticsBase", "CausticsSimplexMod", "CausticsSimplexPermute", "CausticsSimplex",
                    "CausticsField", "CausticsSpectrum", "CausticsTrace", "CausticsSamplePalette", "CausticsSampleRGB", "CausticsSample", "Caustics");
                symbols["WTNoiseInternalPosition"] = prefix + "Position";
                source.Append(ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCommon.hlsl")
                    .Generate(new[] { "Position" }, positionFeatures, symbols));
                source.Append(ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseCausticsSpectrum.hlsl")
                    .Generate(new[] { "Spectrum" }, System.Array.Empty<string>(), symbols));
                source.Append(template.Generate(new[] { "Sample" }, features, symbols));
            }
            return source.ToString();
        }
    }
}
