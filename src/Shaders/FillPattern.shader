Shader "Hidden/TextureCompositor/FillPattern"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "ProceduralUv.cginc"
            #include "HdrColor.cginc"
            float3 _PatternRow0, _PatternRow1;
            float2 _Cell;
            float4 _Vertices[6];
            int _Seamless, _PatternShape, _Staggered, _VertexCount, _Position, _Inverted, _GradientWrapMode;
            float _Rounding, _CircleRadius, _Inradius, _Bulge, _DistanceRange;
            sampler2D _GradientLut, _ProfileLut;
            float4 _GradientLut_TexelSize;
            int _CellColor, _ColorBlend, _Seed;
            float _Variation;
            float2 _CellPeriod;
            sampler2D _PaletteLut;
            float4 _PaletteLut_TexelSize;

            float Contour(float2 p)
            {
                float result = length(p) - _CircleRadius;
                if (_PatternShape != 3)
                {
                    float squared = 1e30;
                    bool inside = true;
                    [loop] for (int i = 0; i < _VertexCount; i++)
                    {
                        float2 a = _Vertices[i].xy;
                        float2 e = _Vertices[i + 1 < _VertexCount ? i + 1 : 0].xy - a;
                        float2 v = p - a;
                        float2 nearest = v - e * saturate(dot(v, e) / max(dot(e, e), 1e-16));
                        squared = min(squared, dot(nearest, nearest));
                        inside = inside && (e.x * v.y - e.y * v.x >= 0);
                    }
                    result = sqrt(squared) * (inside ? -1 : 1) - _Rounding;
                }
                return result;
            }

            void ConsiderCell(float distance, float3 candidate, inout float best, inout float3 id)
            {
                if (distance < best) { best = distance; id = candidate; }
            }

            float PatternDistance(float2 p, out float3 id)
            {
                float result = 1e30;
                id = float3(floor(p + .5), 0);
                if (_Staggered == 0) result = Contour((p - id.xy) * _Cell);
                else
                {
                    const float rowHeight = .866025403784;
                    float baseRow = floor(p.y / rowHeight);
                    // A fixed local neighborhood, independent of the number of cells on canvas.
                    // Neighbor contours also give correct distances in gaps after nonuniform fitting.
                    [unroll] for (int j = -1; j <= 1; j++)
                    {
                        float row = baseRow + j;
                        float shift = frac(row * .5);
                        float column = floor(p.x - shift);
                        [unroll] for (int i = -1; i <= 1; i++)
                        {
                            float x = column + i + shift;
                            if (_PatternShape == 0)
                            {
                                float2 up = (p - float2(x + .5, (row + 1.0/3.0) * rowHeight)) * _Cell;
                                float2 down = -(p - float2(x, (row + 2.0/3.0) * rowHeight)) * _Cell;
                                ConsiderCell(Contour(up), float3(column + i, row, 0), result, id);
                                ConsiderCell(Contour(down), float3(column + i, row, 1), result, id);
                            }
                            else ConsiderCell(Contour((p - float2(x, row * rowHeight)) * _Cell),
                                float3(column + i, row, 0), result, id);
                        }
                    }
                }
                return result;
            }

            uint HashCell(uint value)
            {
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            float3 CellTint(float3 id)
            {
                if (_Seamless != 0) id.xy -= floor(id.xy / _CellPeriod) * _CellPeriod;
                float t = 0;
                if (_CellColor == 1)
                {
                    uint hash = HashCell(asuint((int)id.x) ^ HashCell(asuint((int)id.y)) ^
                        HashCell(asuint(_Seed)) ^ ((uint)id.z * 0x9e3779b9u));
                    t = lerp(.5, (hash >> 8) * (1.0 / 16777216.0), _Variation);
                }
                else if (_PatternShape == 0) t = id.z;
                else if (_Staggered == 0) t = frac((id.x + id.y) * .5) * 2;
                else
                {
                    float index = id.x + frac(id.y * .5) * 4;
                    t = (index - floor(index / 3) * 3) * .5;
                }
                float u = lerp(.5 * _PaletteLut_TexelSize.x, 1 - .5 * _PaletteLut_TexelSize.x, t);
                return SpriteDecode(tex2Dlod(_PaletteLut, float4(u, .5, 0, 0)).rgb);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = _Seamless != 0 ? input.uv : ProceduralSourceUv(input.uv);
                float3 q = float3(uv, 1);
                float2 p = float2(dot(_PatternRow0, q), dot(_PatternRow1, q));
                float3 id;
                float d = PatternDistance(p, id);
                float range = max(_Inradius * _DistanceRange, 1e-6);
                // Bulge changes only the interior profile, not the zero contour or gaps.
                float interior = saturate(-d / max(_Inradius, 1e-6));
                float dome = sqrt(max(0, interior * (2 - interior)));
                if (d < 0) d = -lerp(interior, dome, _Bulge) * _Inradius;
                float t = .5 + .5 * d / range;
                if (_Position == 0) t = max(d, 0) / range;
                else if (_Position == 1) t = max(-d, 0) / range;
                else if (_Position == 2) t = abs(d) / range;
                t = saturate(t);
                if (_Inverted != 0) t = 1 - t;
                t = tex2Dlod(_ProfileLut, float4(lerp(.5/512, 1-.5/512, t), .5, 0, 0)).r;
                if (_GradientWrapMode == 1) t = frac(t);
                else if (_GradientWrapMode == 2) t = 1 - abs(frac(t * .5) * 2 - 1);
                else t = saturate(t);
                float u = lerp(.5 * _GradientLut_TexelSize.x, 1 - .5 * _GradientLut_TexelSize.x, t);
                float4 color = tex2Dlod(_GradientLut, float4(u, .5, 0, 0));
                color.rgb = SpriteDecode(color.rgb);
                if (_CellColor != 0)
                {
                    float3 tint = CellTint(id);
                    color.rgb = _ColorBlend == 0 ? color.rgb * tint : tint;
                }
                return color;
            }
            ENDCG
        }
    }
}
