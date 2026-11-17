using System;
using UnityEngine;
using Kind = DCFApixels.WhimTex.ShaderGraphNodeKind;
using Type = DCFApixels.WhimTex.ShaderGraphValueType;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static bool IsAdditionalCurve(Kind kind) => kind is Kind.Clamp or Kind.Saturate or Kind.Minimum or Kind.Maximum or Kind.Negate or Kind.Sign or
            Kind.Floor or Kind.Ceiling or Kind.Round or Kind.Truncate or Kind.Fraction or Kind.Modulo or
            Kind.Tangent or Kind.Arcsine or Kind.Arccosine or Kind.Arctangent or Kind.HyperbolicSine or Kind.HyperbolicCosine or
            Kind.Reciprocal or Kind.ReciprocalSquareRoot;

        static bool IsRoundingCurve(Kind kind) => kind is Kind.Floor or Kind.Ceiling or Kind.Round or Kind.Truncate;
        static bool HasAdditionalZeroGuide(Kind kind) => IsRoundingCurve(kind) || kind is Kind.Negate or Kind.Sign or Kind.Modulo or
            Kind.Tangent or Kind.Arcsine or Kind.Arctangent or Kind.HyperbolicSine or Kind.HyperbolicCosine;
        static bool HasIdentityGuide(Kind kind) => IsRoundingCurve(kind) || kind is Kind.Minimum or Kind.Maximum;

        static ShaderGraphNode AdditionalMathExample(CatalogGraph g, ShaderGraphNode node)
        {
            var ramp = g.Add(Kind.GradientFactor);
            switch (node.kind)
            {
                case Kind.Minimum:
                case Kind.Maximum:
                    var descending = g.Add(Kind.OneMinus);
                    g.Wire(ramp, descending, "In");
                    g.Wire(ramp, node, "A");
                    g.Wire(descending, node, "B");
                    return node;
                case Kind.Clamp:
                case Kind.Saturate:
                    if (node.kind == Kind.Clamp) { Set(node, "Min", .22f); Set(node, "Max", .78f); }
                    g.Wire(g.Field(Type.Float, "0.5 + 0.75 * sin(P.x * 6.28318530718)"), node, "In");
                    return node;
            }

            const float halfPi = Mathf.PI * .5f;
            var (min, max, low, high) = node.kind switch
            {
                Kind.Negate or Kind.Sign => (-1f, 1f, -1f, 1f),
                Kind.Floor or Kind.Ceiling or Kind.Round or Kind.Truncate => (-2.5f, 2.5f, -3f, 3f),
                Kind.Fraction => (-1.5f, 1.5f, 0f, 1f),
                Kind.Modulo => (-1.5f, 1.5f, -1f, 1f),
                Kind.Tangent => (0f, Mathf.PI, -3f, 3f),
                Kind.Arcsine => (-1f, 1f, -halfPi, halfPi),
                Kind.Arccosine => (-1f, 1f, 0f, Mathf.PI),
                Kind.Arctangent => (-4f, 4f, -halfPi, halfPi),
                Kind.HyperbolicSine or Kind.HyperbolicCosine => (-2f, 2f, -4f, 4f),
                Kind.Reciprocal or Kind.ReciprocalSquareRoot => (.4f, 3f, 0f, 2.5f),
                _ => throw new InvalidOperationException("No catalog curve for " + node.kind)
            };
            g.Wire(g.Remap(ramp, 0, 1, min, max), node, node.kind == Kind.Modulo ? "A" : "In");
            if (node.kind == Kind.Modulo) Set(node, "B", 1);
            return g.Remap(node, low, high, 0, 1);
        }
    }
}
