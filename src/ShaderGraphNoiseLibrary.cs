using System;
using System.IO;
using System.Text;

namespace DCFApixels.WhimTex
{
    // Only generated built-in code is eligible. Unknown code keeps the complete library.
    internal static class ShaderGraphNoiseLibrary
    {
        [Flags]
        enum Part
        {
            None = 0, Common = 1, Basis = 2, Flow = 4, Cellular = 8, Gabor = 16,
            Spots = 32, Shard = 64, Triangle = 128, Grain = 256, Warp = 512, Fields = 1024, GaborTypes = 2048, BasisKernels = 4096, CellularTypes = 8192
        }

        internal static string ForGeneratedSource(string source)
        {
            if (source == null) return null;
            Part parts = Part.None;
            bool block = false;
            using var reader = new StringReader(source);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // Preprocessor splicing/macros can change symbols and include ownership.
                if (line.EndsWith("\\", StringComparison.Ordinal)) return null;
                line = ShaderSourceBuilder.MaskComments(line, ref block).Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (line.StartsWith("#line ", StringComparison.Ordinal)) continue;
                    if (line == "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ProceduralUv.cginc\"" ||
                        line == "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/ColorBlend.cginc\"") continue;
                    return null;
                }
                for (int i = 0; i < line.Length;)
                {
                    char c = line[i];
                    if (c == '"' || c == '\'')
                    {
                        char quote = c; i++;
                        while (i < line.Length)
                        {
                            if (line[i++] == '\\') { i = Math.Min(i + 1, line.Length); continue; }
                            if (line[i - 1] == quote) break;
                        }
                        continue;
                    }
                    if (!(char.IsLetter(c) || c == '_')) { i++; continue; }
                    int start = i++;
                    while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                    var token = line.AsSpan(start, i - start);
                    if (token.SequenceEqual("namespace".AsSpan()) || token.StartsWith("GraphExpression".AsSpan(), StringComparison.Ordinal) ||
                        token.StartsWith("fnl".AsSpan(), StringComparison.Ordinal)) return null;
                    if (token.StartsWith("_fnl".AsSpan(), StringComparison.Ordinal) || token.StartsWith("RAND_VECS_".AsSpan(), StringComparison.Ordinal))
                    {
                        // Selected built-in kernels still share the original arithmetic/tables.
                        // An unknown helper preserves the full-library fallback.
                        if (!IsBasisKernel(token)) return null;
                        parts |= Part.BasisKernels;
                        continue;
                    }
                    if (!token.StartsWith("WT".AsSpan(), StringComparison.Ordinal)) continue;
                    if (!TryPart(token.ToString(), out var part)) return null;
                    parts |= part;
                }
            }
            if ((parts & Part.Fields) != 0 && (parts & (Part.Basis | Part.Flow | Part.Gabor)) == 0) return null;
            var result = new StringBuilder();
            void Include(Part part, string name)
            {
                if ((parts & part) != 0)
                    result.Append("#include \"Packages/com.dcfapixels.whimtex/src/Shaders/WTNoise").Append(name).AppendLine(".hlsl\"");
            }
            Include(Part.Common, "Common");
            Include(Part.BasisKernels, "BasisKernels");
            Include(Part.CellularTypes, "CellularTypes");
            Include(Part.Basis, "Basis");
            Include(Part.Flow, "Flow");
            Include(Part.Cellular, "Cellular");
            Include(Part.Gabor, "Gabor");
            Include(Part.GaborTypes, "GaborTypes");
            Include(Part.Spots, "Spots");
            Include(Part.Shard, "Shard");
            Include(Part.Triangle, "TriangleVoronoi");
            Include(Part.Grain, "Grain");
            Include(Part.Warp, "Warp");
            if ((parts & Part.Fields) != 0)
            {
                Include(Part.Basis, "BasisFields");
                Include(Part.Gabor, "GaborFields");
            }
            return result.ToString();
        }

        static bool IsBasisKernel(ReadOnlySpan<char> token)
        {
            // Names emitted by WT templates only. Generic dispatch/state entry
            // points are deliberately absent and keep the conservative fallback.
            return token.ToString() is
                "_fnlSingleSimplex2D" or "_fnlSingleOpenSimplex23D" or
                "_fnlSingleOpenSimplex2S2D" or "_fnlSingleOpenSimplex2S3D" or
                "_fnlSinglePerlin2D" or "_fnlSinglePerlin3D" or
                "_fnlSingleValue2D" or "_fnlSingleValue3D" or
                "_fnlSingleValueCubic2D" or "_fnlSingleValueCubic3D" or
                "_fnlHash2D" or "_fnlValCoord2D" or "_fnlValCoord3D" or
                "_fnlGradient2D" or "_fnlGradient3D" or "_fnlInterpQuintic" or
                "_fnlInterpHermite" or "_fnlCubicLerp" or "RAND_VECS_2D" or "RAND_VECS_3D";
        }

        static bool TryPart(string name, out Part part)
        {
            part = name switch
            {
                "WTNoiseSettings" or "WTNoiseDefaults" or "WTNoiseFractal" or "WTNoiseInternalOctaves" or
                "WTNoiseInternalSeed" or "WTNoiseInternalNextFrequency" or "WTFractalInternalVisibility" or
                "WTFractalInternalCascade" or "WTFractalInternalAccumulate" or "WTFractalInternalResolve" or
                "WT_NOISE_NONE" or "WT_NOISE_FBM" or "WTNoiseInternalHashCell" or "WTNoiseInternalHash" or "WTNoiseInternalUnit" or
                "WTNoiseInternalPosition" or "WTNoiseInternalAmplitude" or "WTNoiseInternalAccumulate" or
                "WTNoiseInternalFilterWeight" or "WTNoiseInternalAccumulateFiltered" or "WTNoiseInternalFractalValue" or
                "WTNoiseInternalPeriod" or "WTNoiseInternalOrigin" or "WTNoiseInternalLayout" or "WTNoiseInternalRotation" or "WTNoiseInternalWrap" or
                "WT_NOISE_RIDGED" or "WT_NOISE_PINGPONG" or
                "WT_NOISE_PERLIN" or "WT_NOISE_VALUE" or "WT_NOISE_VALUE_CUBIC" or "WT_NOISE_OPENSIMPLEX2" or "WT_NOISE_OPENSIMPLEX2S" => Part.Common,
                "WTNoisePerlin" or "WTNoiseValue" or "WTNoiseValueCubic" or "WTNoiseOpenSimplex2" or "WTNoiseOpenSimplex2S" or
                "WTNoiseInternalBasisSingle" or "WTNoiseInternalBasisSampleSingle" or "WTNoiseInternalBasisGradientSingle" or "WTNoiseInternalBasisCurlSingle" => Part.Basis,
                "WTNoiseGradient" or "WTNoiseCurl" => Part.Fields,
                "WTNoiseFlow" or "WTNoiseFlowSettings" or "WTNoiseFlowDefaults" or "WTNoiseInternalFlowRotation" => Part.Flow,
                "WTNoiseCellular" => Part.Cellular,
                "WTNoiseCellularSettings" or "WTNoiseCellularDefaults" or "WTNoiseInternalCell" or "WTNoiseInternalCellZero" or
                "WTNoiseCellularSample1D" or "WTNoiseCellularSample2D" or "WTNoiseCellularSample3D" or
                "WT_NOISE_CELL_UNIFORM" or "WT_NOISE_CELL_FIXED_RADIUS" or
                "WT_NOISE_DISTANCE_EUCLIDEAN" or "WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED" or
                "WT_NOISE_DISTANCE_MANHATTAN" or "WT_NOISE_DISTANCE_HYBRID" or
                "WT_NOISE_CELL_VALUE" or "WT_NOISE_CELL_F1" or "WT_NOISE_CELL_F2" or
                "WT_NOISE_CELL_ADD" or "WT_NOISE_CELL_SUBTRACT" or "WT_NOISE_CELL_MULTIPLY" or
                "WT_NOISE_CELL_DIVIDE" or "WT_NOISE_CELL_DISTANCE_TO_EDGE" => Part.CellularTypes,
                "WTNoiseGabor" or "WTNoiseInternalGaborProfile" => Part.Gabor,
                "WTNoiseGaborSettings" or "WTNoiseGaborDefaults" or "WTNoiseGaborSample" or
                "WT_NOISE_PHASOR_SINE" or "WT_NOISE_PHASOR_TRIANGLE" or "WT_NOISE_PHASOR_PULSE" => Part.GaborTypes,
                "WTNoiseSpots" or "WTNoiseSpotSettings" or "WTNoiseSpotDefaults" => Part.Spots,
                "WTNoiseShard" or "WTNoiseShardSettings" or "WTNoiseShardDefaults" => Part.Shard,
                "WTNoiseTriangleVoronoi" or "WTNoiseTriangleVoronoiSettings" or "WTNoiseTriangleVoronoiDefaults" or "WTNoiseTriangleVoronoiSample2D" => Part.Triangle,
                "WTNoiseWhite" or "WTNoiseBlue" or "WTNoiseGrainSettings" or "WTNoiseGrainDefaults" => Part.Grain,
                "WTNoiseDisplacement" or "WTNoiseWarpSettings" or "WTNoiseWarpDefaults" => Part.Warp,
                _ => Part.None
            };
            return part != Part.None;
        }
    }
}
