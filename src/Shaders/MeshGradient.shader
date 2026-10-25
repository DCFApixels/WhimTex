Shader "Hidden/WhimTex/MeshGradient"
{
    SubShader
    {
        Cull Off ZWrite On ZTest LEqual Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            float4 _MeshPositions[16], _MeshColors[16];
            int _MeshColorMode;
            float3 _MeshPriority;
            float2 _MeshUvStep;
            float4 _MeshTargetSize;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { sample float4 vertex : SV_POSITION; sample float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.vertex = float4(v.vertex.xy * 2 - 1, 0, 1);
                // Resolve overlaps by patch, then local v and u. Only the winning surface writes color and alpha.
                float priority = _MeshPriority.x + dot(_MeshPriority.yz, v.uv);
                #if defined(UNITY_REVERSED_Z)
                o.vertex.z = priority;
                #else
                o.vertex.z = 1 - priority;
                #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                o.vertex.z = o.vertex.z * 2 - 1;
                #endif
                #endif
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y = -o.vertex.y;
                #endif
                o.uv = v.uv; return o;
            }
            float4 basis(float t) { float s = 1 - t; return float4(s*s*s, 3*s*s*t, 3*s*t*t, t*t*t); }
            struct FragmentOutput { float4 color : SV_Target; float depth : SV_Depth; };
            FragmentOutput frag(v2f input)
            {
                float2 uv = input.uv;
                // Sample-qualified position and UV keep shading and depth at the same MSAA location.
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
                float2 residual = 0, magnitude = 0;
                [unroll] for (int j = 0; j < 4; j++) [unroll] for (int i = 0; i < 4; i++)
                {
                    float weight = bu[i] * bv[j];
                    color += _MeshColors[j*4+i] * weight;
                    float2 term = (_MeshPositions[j*4+i].xy - target) * weight;
                    residual += term; magnitude += abs(term);
                }
                // A triangle crossing a fold can cover a sample with no corresponding surface point.
                // Reject that fragment without hiding the valid surface underneath. Include rounding slack.
                if (any(abs(residual) > max(_MeshTargetSize.zw * 0.001, 2e-6 * magnitude))) discard;
                if (_MeshColorMode == 1) color.rgb = SpriteDecode(color.rgb);
                else if (_MeshColorMode == 2)
                {
                    float3 lms = float3(color.r + .3963377774*color.g + .2158037573*color.b,
                        color.r - .1055613458*color.g - .0638541728*color.b,
                        color.r - .0894841775*color.g - 1.2914855480*color.b);
                    lms = lms*lms*lms;
                    color.rgb = float3(4.0767416621*lms.x - 3.3077115913*lms.y + .2309699292*lms.z,
                        -1.2684380046*lms.x + 2.6097574011*lms.y - .3413193965*lms.z,
                        -.0041960863*lms.x - .7034186147*lms.y + 1.7076147010*lms.z);
                }
                color.a = saturate(color.a);
                FragmentOutput o; o.color = float4(color.rgb * color.a, color.a);
                // Resolve visibility using the same refined UV as the color, per covered sample.
                float priority = _MeshPriority.x + dot(_MeshPriority.yz, uv);
                #if defined(UNITY_REVERSED_Z)
                o.depth = priority;
                #else
                o.depth = 1 - priority;
                #endif
                return o;
            }
            ENDCG
        }
    }
}
