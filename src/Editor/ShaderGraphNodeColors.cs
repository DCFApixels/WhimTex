using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderGraphNodeColors
    {
        // Palette/ratings provenance and license are recorded in ThirdPartyNotices.md.
        private static readonly Color32[] HeatColors =
        {
            new(0, 255, 255, 255), new(0, 0, 4, 255), new(27, 12, 65, 255),
            new(74, 12, 107, 255), new(120, 28, 109, 255), new(165, 44, 96, 255),
            new(207, 68, 70, 255), new(237, 105, 37, 255), new(251, 155, 6, 255),
            new(247, 209, 61, 255), new(252, 255, 164, 255)
        };

        internal static bool TryGet(ShaderGraphNode node, ShaderGraphColorMode mode, out Color color)
        {
            color = default;
            if (node == null) return false;
            switch (mode)
            {
                case ShaderGraphColorMode.Category:
                    color = Category(ShaderGraphNodes.Category(node.kind));
                    return true;
                case ShaderGraphColorMode.Heatmap:
                    color = HeatColors[Heat(node.kind)]; return true;
                case ShaderGraphColorMode.Precision:
                    // Graph-generated arithmetic uses float; there is no per-node half precision.
                    color = new Color32(75, 146, 243, 255); return true;
                case ShaderGraphColorMode.UserDefined:
                    if (!ColorUtility.TryParseHtmlString(node.userColor, out color)) return false;
                    color.a = 1; return true;
                default: return false;
            }
        }

        internal static Color32 Category(string path)
        {
            int separator = path.IndexOf('/');
            return CategoryRoot(separator < 0 ? path : path.Substring(0, separator));
        }

        private static Color32 CategoryRoot(string root) => root switch
        {
            "Color" or "Filter" or "Normal" => new Color32(219, 119, 59, 255),
            "Channel" => new Color32(151, 209, 61, 255),
            "Input" or "Sampling" => new Color32(203, 48, 34, 255),
            "Math" => new Color32(75, 146, 243, 255),
            "Procedural" => new Color32(156, 79, 255, 255),
            "UV" => new Color32(8, 215, 139, 255),
            _ => new Color32(174, 174, 174, 255)
        };

        // Static, local estimates, not timings or the accumulated cost of an upstream branch.
        // Zero denotes unknown/data-dependent cost. WhimTex-specific generators and repeated
        // sampling have explicit estimates; user functions and subgraphs remain unknown.
        private static int Heat(ShaderGraphNodeKind kind) => kind switch
        {
                ShaderGraphNodeKind.Constant or
                ShaderGraphNodeKind.UV or
                ShaderGraphNodeKind.CanvasData or
                ShaderGraphNodeKind.Split or
                ShaderGraphNodeKind.Combine or
                ShaderGraphNodeKind.Absolute or
                ShaderGraphNodeKind.Swizzle or
                ShaderGraphNodeKind.Time or
                ShaderGraphNodeKind.ColorMask or
                ShaderGraphNodeKind.Saturate or
                ShaderGraphNodeKind.CanvasPosition or
                ShaderGraphNodeKind.MatrixConstruction or
                ShaderGraphNodeKind.MatrixSplit or
                ShaderGraphNodeKind.TextureSize or
                ShaderGraphNodeKind.Value or
                ShaderGraphNodeKind.InstanceIndex or
                ShaderGraphNodeKind.Output or
                ShaderGraphNodeKind.SubGraphInput or
                ShaderGraphNodeKind.SubGraphOutput or
                ShaderGraphNodeKind.ForBegin or
                ShaderGraphNodeKind.SetAlpha or
                ShaderGraphNodeKind.LightCount or
                ShaderGraphNodeKind.LightDirection or
                ShaderGraphNodeKind.LightColor or
                ShaderGraphNodeKind.ScatterInstance => 1,
                ShaderGraphNodeKind.Add or
                ShaderGraphNodeKind.Subtract or
                ShaderGraphNodeKind.Multiply or
                ShaderGraphNodeKind.Divide or
                ShaderGraphNodeKind.Minimum or
                ShaderGraphNodeKind.Maximum or
                ShaderGraphNodeKind.OneMinus or
                ShaderGraphNodeKind.Negate or
                ShaderGraphNodeKind.Floor or
                ShaderGraphNodeKind.Ceiling or
                ShaderGraphNodeKind.Round or
                ShaderGraphNodeKind.Fraction or
                ShaderGraphNodeKind.Lerp or
                ShaderGraphNodeKind.DotProduct or
                ShaderGraphNodeKind.Reflection or
                ShaderGraphNodeKind.InverseLerp or
                ShaderGraphNodeKind.DegreesToRadians or
                ShaderGraphNodeKind.RadiansToDegrees or
                ShaderGraphNodeKind.Truncate or
                ShaderGraphNodeKind.Premultiply or
                ShaderGraphNodeKind.Unpremultiply or
                ShaderGraphNodeKind.Lightness or
                ShaderGraphNodeKind.Luminance or
                ShaderGraphNodeKind.VectorDisplacement => 2,
                ShaderGraphNodeKind.Clamp or
                ShaderGraphNodeKind.Distance or
                ShaderGraphNodeKind.Sign or
                ShaderGraphNodeKind.Log or
                ShaderGraphNodeKind.And or
                ShaderGraphNodeKind.Not or
                ShaderGraphNodeKind.NormalStrength or
                ShaderGraphNodeKind.SawtoothWave or
                ShaderGraphNodeKind.SquareWave or
                ShaderGraphNodeKind.Compare or
                ShaderGraphNodeKind.Repeat or
                ShaderGraphNodeKind.NormalToDistortion or
                ShaderGraphNodeKind.NormalEncode or
                ShaderGraphNodeKind.NormalDecode => 3,
                ShaderGraphNodeKind.Power or
                ShaderGraphNodeKind.SquareRoot or
                ShaderGraphNodeKind.Sine or
                ShaderGraphNodeKind.Cosine or
                ShaderGraphNodeKind.Step or
                ShaderGraphNodeKind.Smoothstep or
                ShaderGraphNodeKind.Length or
                ShaderGraphNodeKind.TransformUV or
                ShaderGraphNodeKind.Modulo or
                ShaderGraphNodeKind.Exponential or
                ShaderGraphNodeKind.CrossProduct or
                ShaderGraphNodeKind.Or or
                ShaderGraphNodeKind.TriangleWave or
                ShaderGraphNodeKind.Reciprocal or
                ShaderGraphNodeKind.ReciprocalSquareRoot or
                ShaderGraphNodeKind.SoftSign or
                ShaderGraphNodeKind.InverseSoftSign or
                ShaderGraphNodeKind.Projection or
                ShaderGraphNodeKind.DDX or
                ShaderGraphNodeKind.DDY or
                ShaderGraphNodeKind.FWidth or
                ShaderGraphNodeKind.LightAttenuation or
                ShaderGraphNodeKind.Mirror or
                ShaderGraphNodeKind.PixelateUV or
                ShaderGraphNodeKind.TileUV => 4,
                ShaderGraphNodeKind.Remap or
                ShaderGraphNodeKind.Normalize or
                ShaderGraphNodeKind.Random or
                ShaderGraphNodeKind.Saturation or
                ShaderGraphNodeKind.Posterize or
                ShaderGraphNodeKind.NormalBlend or
                ShaderGraphNodeKind.ChannelMixer or
                ShaderGraphNodeKind.Tangent or
                ShaderGraphNodeKind.HyperbolicSine or
                ShaderGraphNodeKind.HyperbolicCosine or
                ShaderGraphNodeKind.RandomRange or
                ShaderGraphNodeKind.Rejection or
                ShaderGraphNodeKind.Levels or
                ShaderGraphNodeKind.ColorBalance or
                ShaderGraphNodeKind.RangeMask or
                ShaderGraphNodeKind.HeightBlend or
                ShaderGraphNodeKind.MixColor or
                ShaderGraphNodeKind.Stripes or
                ShaderGraphNodeKind.Grid or
                ShaderGraphNodeKind.GradientFactor or
                ShaderGraphNodeKind.CombineDistance or
                ShaderGraphNodeKind.WhiteNoise => 5,
                ShaderGraphNodeKind.Twirl or
                ShaderGraphNodeKind.ConvertColor or
                ShaderGraphNodeKind.Arctangent2 or
                ShaderGraphNodeKind.Arcsine or
                ShaderGraphNodeKind.Arccosine or
                ShaderGraphNodeKind.Flipbook or
                ShaderGraphNodeKind.Arctangent or
                ShaderGraphNodeKind.HyperbolicTangent or
                ShaderGraphNodeKind.NoiseSineWave or
                ShaderGraphNodeKind.SphereMask or
                ShaderGraphNodeKind.Rings or
                ShaderGraphNodeKind.BulgePinch or
                ShaderGraphNodeKind.Wave or
                ShaderGraphNodeKind.ValueNoise or
                ShaderGraphNodeKind.BasicGridWarpNoise => 6,
                ShaderGraphNodeKind.Checkerboard or
                ShaderGraphNodeKind.PolarCoordinates or
                ShaderGraphNodeKind.Hue or
                ShaderGraphNodeKind.Refract or
                ShaderGraphNodeKind.Dither or
                ShaderGraphNodeKind.WhiteBalance or
                ShaderGraphNodeKind.RotateAboutAxis or
                ShaderGraphNodeKind.Blackbody or
                ShaderGraphNodeKind.MatrixDeterminant or
                ShaderGraphNodeKind.MatrixTranspose or
                ShaderGraphNodeKind.Transform or
                ShaderGraphNodeKind.Shape or
                ShaderGraphNodeKind.Kaleidoscope or
                ShaderGraphNodeKind.Ripple or
                ShaderGraphNodeKind.HeightToNormal or
                ShaderGraphNodeKind.HeightToDistortion or
                ShaderGraphNodeKind.ApplyTransform2D or
                ShaderGraphNodeKind.PerlinNoise or
                ShaderGraphNodeKind.ValueCubicNoise or
                ShaderGraphNodeKind.PerspectiveUV or
                ShaderGraphNodeKind.FresnelEffect or
                ShaderGraphNodeKind.HueRange => 7,
                ShaderGraphNodeKind.OpenSimplex2Noise or
                ShaderGraphNodeKind.OpenSimplex2SNoise or
                ShaderGraphNodeKind.FlowNoise or
                ShaderGraphNodeKind.ShardNoise or
                ShaderGraphNodeKind.OpenSimplex2ReducedWarpNoise => 8,
                ShaderGraphNodeKind.CellularNoise or
                ShaderGraphNodeKind.SpotsNoise or
                ShaderGraphNodeKind.OpenSimplex2WarpNoise or
                ShaderGraphNodeKind.Blur or
                ShaderGraphNodeKind.Sharpen or
                ShaderGraphNodeKind.EdgeDetection or
                ShaderGraphNodeKind.Dilate or
                ShaderGraphNodeKind.Erode => 9,
                ShaderGraphNodeKind.GaborNoise or
                ShaderGraphNodeKind.TriangleVoronoiNoise or
                ShaderGraphNodeKind.CausticsNoise or
                ShaderGraphNodeKind.Fractal or
                ShaderGraphNodeKind.NormalLighting or
                ShaderGraphNodeKind.Caustics or
                ShaderGraphNodeKind.Scatter or
                ShaderGraphNodeKind.DirectionalBlur or
                ShaderGraphNodeKind.RadialBlur or
                ShaderGraphNodeKind.SlopeBlur or
                ShaderGraphNodeKind.BilateralBlur => 10,
            _ => 0
        };
    }
}
