Shader "Hidden/TextureCompositor/Sharpen"
{
        Properties { [HideInInspector] _MainTex ("Source", 2D) = "black" {} }
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
        sampler2D _MainTex, _BlurTex;
        float4 _MainTex_TexelSize;
        float _Strength, _Radius, _Threshold, _NoiseReduction, _HaloSuppression;
        int _Edges, _Fast, _Algorithm, _ChannelMode, _UseBlur;

        float2 address(float2 pixel)
        {
            float2 size = _MainTex_TexelSize.zw;
            if (_Edges == 2) return (int2(pixel) % int2(size) + int2(size)) % int2(size);
            if (_Edges == 3)
            {
                float2 p = (int2(pixel) % int2(2 * size) + int2(2 * size)) % int2(2 * size);
                return min(p, 2 * size - 1 - p);
            }
            return clamp(pixel, 0, size - 1);
        }
        float4 at(float2 pixel)
        {
            if (_Edges == 0 && (any(pixel < 0) || any(pixel >= _MainTex_TexelSize.zw))) return 0;
            return tex2Dlod(_MainTex, float4((address(pixel) + .5) * _MainTex_TexelSize.xy, 0, 0));
        }
        float4 sampleEdge(float2 pixel)
        {
            // Keep fractional radii smooth, while respecting the selected edge mode.
            float2 low = floor(pixel);
            float2 f = frac(pixel);
            if (all(low >= 0) && all(low + 1 < _MainTex_TexelSize.zw))
                return tex2Dlod(_MainTex, float4((pixel + .5) * _MainTex_TexelSize.xy, 0, 0));
            return lerp(lerp(at(low), at(low + float2(1, 0)), f.x),
                lerp(at(low + float2(0, 1)), at(low + 1), f.x), f.y);
        }
        float lumaPremultiplied(float4 color)
        {
            return dot(color.rgb * color.a, float3(.2126, .7152, .0722));
        }
        float4 frag(v2f_img i) : SV_Target
        {
            float2 p = i.uv * _MainTex_TexelSize.zw - .5;
            float2 d = float2(_Radius, 0);
            float2 v = float2(0, _Radius);
            float4 c = at(p);
            float4 n;
            if (_UseBlur != 0)
            {
                // Final path: _BlurTex was produced by the same separable
                // Gaussian pipeline used by the Blur layer.
                n = tex2D(_BlurTex, i.uv);
            }
            else if (_Fast != 0)
            {
                // Cheap interactive approximation: one bilinear 3x3 binomial
                // kernel. It is still a single pass, but avoids the cross-shaped
                // response of the former four-cardinal average.
                n = c * .25;
                n += (sampleEdge(p - d) + sampleEdge(p + d) +
                    sampleEdge(p - v) + sampleEdge(p + v)) * .125;
                n += (sampleEdge(p - d - v) + sampleEdge(p - d + v) +
                    sampleEdge(p + d - v) + sampleEdge(p + d + v)) * .0625;
            }
            else
            {
                // Settled render: a compact Gaussian-like footprint. The
                // weights sum to one and keep the comparison in premultiplied
                // space, avoiding bright/dark halos around transparent edges.
                float2 h = d * .5;
                float2 q = d * .70710678;
                float2 qv = float2(q.y, q.x);
                float4 weighted = c * .20;
                float2 hv = float2(0, h.x);
                weighted += (at(p - h) + at(p + h) + at(p - hv) + at(p + hv)) * .10;
                weighted += (at(p - d) + at(p + d) + at(p - v) + at(p + v)) * .05;
                weighted += (at(p - q - qv) + at(p + q + qv) +
                    at(p - q + qv) + at(p + q - qv)) * .05;
                n = weighted;
            }
            float alpha = c.a;
            float3 center = c.rgb * alpha;
            float3 blurred = n.rgb * n.a;
            float3 detail = center - blurred;
            if (_ChannelMode == 1)
            {
                float luminance = dot(detail, float3(.2126, .7152, .0722));
                detail = luminance.xxx;
            }
            float contrast = max(max(abs(detail.r), abs(detail.g)), abs(detail.b));
            float thresholdWeight = _Threshold <= 0.00001
                ? 1.0 : saturate((contrast - _Threshold) / max(.0001, 1.0 - _Threshold));
            float adaptiveWeight = 1.0;
            float noiseWeight = 1.0;
            float3 localMin = c.rgb;
            float3 localMax = c.rgb;
            if (_Algorithm == 1 || _HaloSuppression > 0.00001)
            {
                // Share one 3x3 neighborhood between Adaptive's structure
                // analysis and halo limiting. Adaptive measures whether local
                // contrast has a coherent direction rather than only its size.
                float4 nw = at(p + float2(-1, -1));
                float4 north = at(p + float2(0, -1));
                float4 ne = at(p + float2(1, -1));
                float4 west = at(p + float2(-1, 0));
                float4 east = at(p + float2(1, 0));
                float4 sw = at(p + float2(-1, 1));
                float4 south = at(p + float2(0, 1));
                float4 se = at(p + float2(1, 1));

                if (_Algorithm == 1)
                {
                    float lNW = lumaPremultiplied(nw);
                    float lN = lumaPremultiplied(north);
                    float lNE = lumaPremultiplied(ne);
                    float lW = lumaPremultiplied(west);
                    float lE = lumaPremultiplied(east);
                    float lSW = lumaPremultiplied(sw);
                    float lS = lumaPremultiplied(south);
                    float lSE = lumaPremultiplied(se);
                    float lC = lumaPremultiplied(c);

                    // Average gradients from four overlapping 2x2 cells to
                    // estimate local orientation without privileging an axis.
                    float gx0 = ((lN + lC) - (lNW + lW)) * .5;
                    float gy0 = ((lW + lC) - (lNW + lN)) * .5;
                    float gx1 = ((lNE + lE) - (lN + lC)) * .5;
                    float gy1 = ((lC + lE) - (lN + lNE)) * .5;
                    float gx2 = ((lC + lS) - (lW + lSW)) * .5;
                    float gy2 = ((lSW + lS) - (lW + lC)) * .5;
                    float gx3 = ((lE + lSE) - (lC + lS)) * .5;
                    float gy3 = ((lS + lSE) - (lC + lE)) * .5;
                    float xx = gx0 * gx0 + gx1 * gx1 + gx2 * gx2 + gx3 * gx3;
                    float yy = gy0 * gy0 + gy1 * gy1 + gy2 * gy2 + gy3 * gy3;
                    float xy = gx0 * gy0 + gx1 * gy1 + gx2 * gy2 + gx3 * gy3;
                    float energy = xx + yy;
                    float orientation = sqrt(max(0.0, (xx - yy) * (xx - yy) + 4.0 * xy * xy))
                        / max(energy, .000001);
                    float edgeConfidence = saturate(orientation) * saturate(sqrt(energy) * 4.0);

                    // Adaptive has a gentle baseline selectivity; Noise
                    // Reduction increases rejection of weak, directionless
                    // high-frequency structure while keeping coherent edges.
                    adaptiveWeight = lerp(.5, 1.0, edgeConfidence);
                    noiseWeight = lerp(1.0, edgeConfidence, saturate(_NoiseReduction));
                }

                if (_HaloSuppression > 0.00001)
                {
                    if (nw.a > 0.00001) { localMin = min(localMin, nw.rgb); localMax = max(localMax, nw.rgb); }
                    if (north.a > 0.00001) { localMin = min(localMin, north.rgb); localMax = max(localMax, north.rgb); }
                    if (ne.a > 0.00001) { localMin = min(localMin, ne.rgb); localMax = max(localMax, ne.rgb); }
                    if (west.a > 0.00001) { localMin = min(localMin, west.rgb); localMax = max(localMax, west.rgb); }
                    if (east.a > 0.00001) { localMin = min(localMin, east.rgb); localMax = max(localMax, east.rgb); }
                    if (sw.a > 0.00001) { localMin = min(localMin, sw.rgb); localMax = max(localMax, sw.rgb); }
                    if (south.a > 0.00001) { localMin = min(localMin, south.rgb); localMax = max(localMax, south.rgb); }
                    if (se.a > 0.00001) { localMin = min(localMin, se.rgb); localMax = max(localMax, se.rgb); }
                }
            }
            detail *= thresholdWeight * adaptiveWeight * noiseWeight;
            float3 sharpened = center + detail * _Strength;
            float3 limited = sharpened;
            if (_HaloSuppression > 0.00001)
            {
                // Limit straight RGB against the immediate source neighborhood.
                // Keep the neighborhood fixed in texels so its extrema do not
                // jump with the sharpening radius, and ignore transparent RGB.
                float3 sharpenedColor = alpha > 0.00001 ? sharpened / alpha : 0;
                float3 rangeLimited = clamp(sharpenedColor, localMin, localMax);
                float3 limitedColor = lerp(sharpenedColor, rangeLimited, saturate(_HaloSuppression));
                limited = limitedColor * alpha;
            }
            // Keep HDR values intact. The compositor applies the layer's color
            // range and output policy later; sharpening must not clip here.
            float3 color = 0;
            if (alpha > 0) color = limited / alpha;
            return float4(color, alpha);
        }
        ENDCG
        }
    }
}
