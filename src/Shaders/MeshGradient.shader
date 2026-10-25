Shader "Hidden/WhimTex/MeshGradient"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            #include "GradientColorModel.cginc"
            float4 _MeshPositions[16], _MeshColors[16];
            float _MeshHueChroma[16];
            int _MeshColorMode;
            float3 _MeshPriority;
            float2 _MeshUvStep;
            float4 _MeshTargetSize, _MeshOuterEdges;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { sample float4 vertex : SV_POSITION; sample float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex = float4(v.vertex.xy * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y = -o.vertex.y;
                #endif
                o.uv = v.uv; return o;
            }
            float4 basis(float t) { float s = 1 - t; return float4(s*s*s, 3*s*s*t, 3*s*t*t, t*t*t); }
            struct FragmentOutput { float4 color : SV_Target0; float2 weight : SV_Target1; };
            float cross2(float2 a, float2 b) { return a.x*b.y-a.y*b.x; }
            FragmentOutput frag(v2f input)
            {
                float2 uv = input.uv;
                // Each covered sample contributes its own surface color and geometric weight.
                // The vertex Y flip makes raster coordinates match local UV on each backend.
                float2 target = input.vertex.xy * _MeshTargetSize.zw;
                float2 minimumUv = input.uv - _MeshUvStep, maximumUv = input.uv + _MeshUvStep;
                [loop] for (int n = 0; n < 4; n++)
                {
                    float4 bu = basis(uv.x), bv = basis(uv.y);
                    float2 su = 1 - uv;
                    float3 qu = float3(su.x*su.x, 2*su.x*uv.x, uv.x*uv.x);
                    float3 qv = float3(su.y*su.y, 2*su.y*uv.y, uv.y*uv.y);
                    // Center the residual before summing; differences keep derivatives stable at folds.
                    float2 error = 0, u = 0, v = 0;
                    [unroll] for (int y = 0; y < 4; y++) [unroll] for (int x = 0; x < 4; x++)
                    {
                        float2 control = _MeshPositions[y*4+x].xy;
                        error += (control - target) * (bu[x]*bv[y]);
                        if (x < 3) u += 3 * (_MeshPositions[y*4+x+1].xy-control) * (qu[x]*bv[y]);
                        if (y < 3) v += 3 * (_MeshPositions[(y+1)*4+x].xy-control) * (bu[x]*qv[y]);
                    }
                    float det = u.x*v.y-u.y*v.x;
                    if (!(abs(det) > max(1e-15, 1e-7 * length(u) * length(v)))) break;
                    float2 correction = float2(error.x*v.y-error.y*v.x, u.x*error.y-u.y*error.x)/det;
                    uv = clamp(uv - correction, minimumUv, maximumUv);
                    if (all(abs(correction) < _MeshUvStep * 0.0001)) break;
                }
                float4 bu = basis(uv.x), bv = basis(uv.y), color = 0;
                float chroma = 0;
                float2 residual = 0, magnitude = 0;
                [unroll] for (int j = 0; j < 4; j++) [unroll] for (int i = 0; i < 4; i++)
                {
                    float weight = bu[i] * bv[j];
                    color += _MeshColors[j*4+i] * weight;
                    if (_MeshColorMode == 3) chroma += _MeshHueChroma[j*4+i] * weight;
                    float2 term = (_MeshPositions[j*4+i].xy - target) * weight;
                    residual += term; magnitude += abs(term);
                }
                // A triangle crossing a fold can cover a sample with no corresponding surface point.
                // Reject that fragment without hiding the valid surface underneath. Include rounding slack.
                if (any(abs(residual) > max(_MeshTargetSize.zw * 0.001, 2e-6 * magnitude))) discard;
                color.rgb = GradientModelToLinear(color.rgb, _MeshColorMode, chroma);
                // Distance to the local fold in screen space. Expressing derivatives in pixels
                // makes the fade independent of tessellation and parameter subdivision.
                float2 su = 1 - uv;
                float3 qu = float3(su.x*su.x, 2*su.x*uv.x, uv.x*uv.x);
                float3 qv = float3(su.y*su.y, 2*su.y*uv.y, uv.y*uv.y);
                float2 lu = float2(su.x, uv.x), lv = float2(su.y, uv.y);
                float2 pu = 0, pv = 0, puu = 0, puv = 0, pvv = 0;
                [unroll] for (int y = 0; y < 4; y++) [unroll] for (int x = 0; x < 4; x++)
                {
                    float2 p = _MeshPositions[y*4+x].xy;
                    if (x < 3) pu += 3 * (_MeshPositions[y*4+x+1].xy-p) * (qu[x]*bv[y]);
                    if (y < 3) pv += 3 * (_MeshPositions[(y+1)*4+x].xy-p) * (bu[x]*qv[y]);
                    if (x < 2) puu += 6 * (_MeshPositions[y*4+x+2].xy-2*_MeshPositions[y*4+x+1].xy+p) * (lu[x]*bv[y]);
                    if (y < 2) pvv += 6 * (_MeshPositions[(y+2)*4+x].xy-2*_MeshPositions[(y+1)*4+x].xy+p) * (bu[x]*lv[y]);
                    if (x < 3 && y < 3) puv += 9 * (_MeshPositions[(y+1)*4+x+1].xy-_MeshPositions[y*4+x+1].xy-_MeshPositions[(y+1)*4+x].xy+p) * (qu[x]*qv[y]);
                }
                pu *= _MeshTargetSize.xy; pv *= _MeshTargetSize.xy;
                puu *= _MeshTargetSize.xy; puv *= _MeshTargetSize.xy; pvv *= _MeshTargetSize.xy;
                float det = cross2(pu, pv);
                float2 dd = float2(cross2(puu, pv)+cross2(pu, puv), cross2(puv, pv)+cross2(pu, pvv));
                float2 normal = float2(pv.y*dd.x-pu.y*dd.y, pu.x*dd.y-pv.x*dd.x);
                float distance = .5 * det * det / max(length(normal), 1e-20);
                float2 edgeScale = abs(det) / max(float2(length(pv), length(pu)), 1e-20);
                if (_MeshOuterEdges.x > 0) distance = min(distance, max(0, uv.x) * edgeScale.x);
                if (_MeshOuterEdges.y > 0) distance = min(distance, max(0, 1-uv.x) * edgeScale.x);
                if (_MeshOuterEdges.z > 0) distance = min(distance, max(0, uv.y) * edgeScale.y);
                if (_MeshOuterEdges.w > 0) distance = min(distance, max(0, 1-uv.y) * edgeScale.y);
                float fadeWidth = max(1, min(_MeshTargetSize.x, _MeshTargetSize.y) / 64);
                float fade = smoothstep(0, fadeWidth, distance);
                float priority = _MeshPriority.x + dot(_MeshPriority.yz, uv);
                float weight = max(fade, 1e-8) * exp2(6 * (priority - 1));
                color.a = saturate(color.a);
                FragmentOutput o;
                o.color = float4(color.rgb * color.a, color.a) * weight;
                o.weight = float2(weight, 1);
                return o;
            }
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment resolve
            #pragma multi_compile_local _ MESH_MULTISAMPLE
            #include "UnityCG.cginc"
            #if defined(MESH_MULTISAMPLE)
                Texture2DMS<float4> _MeshAccumulatedColor;
                Texture2DMS<float2> _MeshAccumulatedWeight;
            #else
                Texture2D<float4> _MeshAccumulatedColor;
                Texture2D<float2> _MeshAccumulatedWeight;
            #endif
            int _MeshSampleCount;
            float4 resolve(v2f_img input) : SV_Target
            {
                uint2 pixel = uint2(input.pos.xy);
                float4 result = 0;
                // Normalize each MSAA sample before averaging, preserving outer-edge coverage.
                [loop] for (int i = 0; i < _MeshSampleCount; i++)
                {
                    #if defined(MESH_MULTISAMPLE)
                        float4 color = _MeshAccumulatedColor.Load(pixel, i);
                        float weight = _MeshAccumulatedWeight.Load(pixel, i).x;
                    #else
                        float4 color = _MeshAccumulatedColor.Load(int3(pixel, 0));
                        float weight = _MeshAccumulatedWeight.Load(int3(pixel, 0)).x;
                    #endif
                    if (weight > 0) result += color / weight;
                }
                return result / _MeshSampleCount;
            }
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment region
            #pragma multi_compile_local _ MESH_MULTISAMPLE
            #include "UnityCG.cginc"
            #if defined(MESH_MULTISAMPLE)
                Texture2DMS<float2> _MeshAccumulatedWeight;
            #else
                Texture2D<float2> _MeshAccumulatedWeight;
            #endif
            int _MeshSampleCount;
            float2 region(v2f_img input) : SV_Target
            {
                uint2 pixel = uint2(input.pos.xy);
                float covered = 0, overlap = 0;
                [loop] for (int i = 0; i < _MeshSampleCount; i++)
                {
                    #if defined(MESH_MULTISAMPLE)
                        float count = _MeshAccumulatedWeight.Load(pixel, i).y;
                    #else
                        float count = _MeshAccumulatedWeight.Load(int3(pixel, 0)).y;
                    #endif
                    covered += count > .5;
                    overlap += count > 1.5;
                }
                return float2(overlap / max(covered, 1), covered / _MeshSampleCount);
            }
            ENDCG
        }
        CGINCLUDE
        #include "UnityCG.cginc"
        Texture2D<float4> _MainTex, _MeshFixedColor, _MeshCoarseColor, _MeshPreviousColor;
        Texture2D<float2> _MeshRegion;
        float4 _MeshLevelSize, _MeshCoarseSize;
        float _MeshMomentum, _MeshBandRadius;
        int _MeshNormalizeCoverage, _MeshHasCoarse;
        int2 levelPixel(int2 p) { return clamp(p, int2(0, 0), int2(_MeshLevelSize.xy)-1); }
        float2 regionAt(int2 p) { return _MeshRegion.Load(int3(levelPixel(p), 0)); }
        float4 fixedAt(int2 p)
        {
            p = levelPixel(p);
            float4 c = _MeshFixedColor.Load(int3(p, 0));
            return _MeshNormalizeCoverage != 0 ? c / max(regionAt(p).y, 1e-8) : c;
        }
        float4 downColor(v2f_img input) : SV_Target
        {
            int2 basePixel = int2(input.pos.xy) * 2;
            float4 sum = 0; float coverage = 0;
            [unroll] for (int y = 0; y < 2; y++) [unroll] for (int x = 0; x < 2; x++)
            {
                int2 p = levelPixel(basePixel + int2(x, y));
                float w = regionAt(p).y;
                float4 c = _MainTex.Load(int3(p, 0));
                sum += _MeshNormalizeCoverage != 0 ? c : c * w;
                coverage += w;
            }
            return sum / max(coverage, 1e-8);
        }
        float2 downRegion(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy) * 2;
            float overlap = 0, coverage = 0;
            [unroll] for (int y = 0; y < 2; y++) [unroll] for (int x = 0; x < 2; x++)
            {
                float2 r = regionAt(p + int2(x, y));
                overlap = max(overlap, r.x); coverage += r.y;
            }
            return float2(overlap, coverage * .25);
        }
        float4 coarseAt(int2 p)
        {
            return _MeshCoarseColor.Load(int3(clamp(p, int2(0, 0), int2(_MeshCoarseSize.xy)-1), 0));
        }
        float4 prolongate(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy);
            if (_MeshHasCoarse == 0 || regionAt(p).x == 0) return fixedAt(p);
            float2 q = (float2(p) + .5) * .5 - .5;
            int2 b = int2(floor(q)); float2 f = frac(q);
            return lerp(lerp(coarseAt(b), coarseAt(b+int2(1,0)), f.x),
                lerp(coarseAt(b+int2(0,1)), coarseAt(b+int2(1,1)), f.x), f.y);
        }
        float4 extrapolateAt(int2 p)
        {
            p = levelPixel(p);
            float4 current = _MainTex.Load(int3(p, 0));
            return current + _MeshMomentum * (current - _MeshPreviousColor.Load(int3(p, 0)));
        }
        float4 differenceAt(int2 p, float4 center)
        {
            p = levelPixel(p);
            // Empty space outside the mesh is not a black/transparent color constraint.
            return regionAt(p).y > 0 ? extrapolateAt(p) - center : 0;
        }
        float4 laplacianAt(int2 p)
        {
            p = levelPixel(p);
            float4 c = extrapolateAt(p);
            return differenceAt(p+int2(1,0),c) + differenceAt(p-int2(1,0),c)
                + differenceAt(p+int2(0,1),c) + differenceAt(p-int2(0,1),c);
        }
        float4 laplacianDifferenceAt(int2 p, float4 center)
        {
            p = levelPixel(p);
            return regionAt(p).y > 0 ? laplacianAt(p) - center : 0;
        }
        float4 relax(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy);
            if (regionAt(p).x == 0) return fixedAt(p);
            float4 center = extrapolateAt(p);
            float4 laplacian = laplacianAt(p);
            float4 curvature = laplacianDifferenceAt(p+int2(1,0),laplacian) + laplacianDifferenceAt(p-int2(1,0),laplacian)
                + laplacianDifferenceAt(p+int2(0,1),laplacian) + laplacianDifferenceAt(p-int2(0,1),laplacian);
            // Accelerated gradient step: the squared grid Laplacian has spectral bound 64.
            // Differences preserve a constant HDR color/alpha without cancellation drift.
            // Squaring the covered-pixel Laplacian also stays stable at an open silhouette.
            return center - (1.0/64) * curvature;
        }
        float distanceHorizontal(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy); int radius = (int)ceil(_MeshBandRadius);
            float distance = (_MeshBandRadius+1)*(_MeshBandRadius+1);
            [loop] for (int x = -radius; x <= radius; x++)
                if (regionAt(p+int2(x,0)).x > 0) distance = min(distance, (float)(x*x));
            return distance;
        }
        float2 transitionRegion(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy); int radius = (int)ceil(_MeshBandRadius);
            float distance = (_MeshBandRadius+1)*(_MeshBandRadius+1);
            [loop] for (int y = -radius; y <= radius; y++)
                distance = min(distance, _MainTex.Load(int3(levelPixel(p+int2(0,y)),0)).x+y*y);
            float t = saturate(sqrt(distance)/_MeshBandRadius);
            // Zero first/second derivatives where the narrow band meets the original image.
            float weight = 1 - t*t*t*(t*(6*t-15)+10);
            float2 original = regionAt(p);
            return float2(original.y > 0 ? max(original.x, weight) : 0, original.y);
        }
        float4 finish(v2f_img input) : SV_Target
        {
            int2 p = int2(input.pos.xy); float2 r = regionAt(p);
            float4 original = _MeshFixedColor.Load(int3(p, 0));
            if (r.x == 0) return original;
            float4 color = _MainTex.Load(int3(p, 0));
            if (color.a <= 0) color = 0;
            else if (color.a > 1) color /= color.a;
            return lerp(original, color * r.y, r.x);
        }
        ENDCG
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment downColor
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment downRegion
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment prolongate
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment relax
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment finish
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment distanceHorizontal
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment transitionRegion
            ENDCG
        }
    }
}
