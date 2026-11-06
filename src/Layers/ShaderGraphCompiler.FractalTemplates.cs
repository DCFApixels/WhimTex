using System;
using System.Collections.Generic;
using System.Text;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        readonly Dictionary<(ShaderGraphFractal mode, ShaderGraphValueType type), string> fractalTemplates = new();

        string RegisterFractalTemplate(ShaderGraphFractal mode, ShaderGraphValueType type)
        {
            // Color and float4 share arithmetic and therefore the same helpers.
            if (type == ShaderGraphValueType.Color) type = ShaderGraphValueType.Float4;
            var key = (mode, type);
            if (!fractalTemplates.TryGetValue(key, out string name))
                fractalTemplates.Add(key, name = "GraphFractal" + mode + Type(type));
            return name;
        }

        static Dictionary<string, string> FractalReplacements(string prefix, string type) => new(StringComparer.Ordinal)
        {
            ["WTFractalValue"] = type,
            ["WTFractalInternalFold"] = prefix + "Fold",
            ["WTFractalInternalWeight"] = prefix + "Weight",
            ["WTFractalInternalAccumulate"] = prefix + "Accumulate",
            ["WTFractalInternalResolve"] = prefix + "Resolve",
            ["WTFractalInternalCascade"] = prefix + "Cascade"
        };

        string FractalTemplateSource()
        {
            if (fractalTemplates.Count == 0) return "";
            var template = ShaderSourceTemplate.Load("Packages/com.dcfapixels.whimtex/src/Shaders/WTNoiseFractalValues.hlsl");
            var result = new StringBuilder();
            foreach (var entry in fractalTemplates)
            {
                var mode = entry.Key.mode;
                bool cascade = ShaderGraphNodes.IsMusgraveFractal(mode);
                string feature = mode switch
                {
                    ShaderGraphFractal.Ridged => "Ridged",
                    ShaderGraphFractal.PingPong => "PingPong",
                    ShaderGraphFractal.Multifractal => "Product",
                    ShaderGraphFractal.HybridMultifractal => "Hybrid",
                    ShaderGraphFractal.HeteroTerrain => "Height",
                    ShaderGraphFractal.RidgedMultifractal => "RidgedCascade",
                    _ => null
                };
                result.Append(template.Generate(cascade ? new[] { "Cascade" } : new[] { "Accumulate", "Resolve" },
                    feature == null ? null : new[] { feature }, FractalReplacements(entry.Value, Type(entry.Key.type))));
            }
            return result.ToString();
        }
    }
}
