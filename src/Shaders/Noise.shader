Shader "Hidden/WhimTex/Noise"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma multi_compile_local __ WT_NOISE_3D
            #pragma multi_compile_local __ WT_NOISE_PERIODIC
            #pragma multi_compile_local WT_NOISE_0 WT_NOISE_1 WT_NOISE_2 WT_NOISE_3 WT_NOISE_4 WT_NOISE_5
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            #include "ProceduralUv.cginc"
            #if defined(WT_NOISE_PERIODIC)
                #include "NoiseLattice.cginc"
            #endif
            #if defined(WT_NOISE_0)
                #define WHIMTEX_NOISE_TYPE 0
            #elif defined(WT_NOISE_1)
                #define WHIMTEX_NOISE_TYPE 1
            #elif defined(WT_NOISE_2)
                #define WHIMTEX_NOISE_TYPE 2
            #elif defined(WT_NOISE_3)
                #define WHIMTEX_NOISE_TYPE 3
            #elif defined(WT_NOISE_4)
                #define WHIMTEX_NOISE_TYPE 4
            #else
                #define WHIMTEX_NOISE_TYPE 5
            #endif
            #include "ThirdParty/FastNoiseLite.hlsl"

            float4 _NoiseDomain, _NoiseFractalSettings;
            float4 _NoiseAxis;
            float3 _NoiseWarpScale;
            int _NoiseOneD, _NoiseThreeD, _NoisePeriodic;
            float2 _NoiseScale;
            float _NoiseZ, _NoiseCellularJitter, _NoiseWarpStrength;
            int _NoiseSeed, _NoiseType, _NoiseFractal, _NoiseOctaves;
            int _NoiseCellularDistance, _NoiseCellularReturn, _NoiseWarp, _NoiseEncoding, _NoiseInverted;
            float4 _WhiteNoiseGrid;
            int _WhiteNoiseColor;
            Texture2D<float4> _BlueNoise2D, _BlueNoise1D;
            sampler2D _GradientLut;
            float4 _GradientLut_TexelSize;
            int _UseGradient, _GradientWrapMode;

            float4 MapGradient(float t)
            {
                if (_GradientWrapMode == 1) t = frac(t);
                else if (_GradientWrapMode == 2) t = 1 - abs(frac(t * .5) * 2 - 1);
                float u = lerp(.5 * _GradientLut_TexelSize.x, 1 - .5 * _GradientLut_TexelSize.x, t);
                float4 c = tex2Dlod(_GradientLut, float4(u, .5, 0, 0));
                c.rgb = SpriteDecode(c.rgb);
                return c;
            }

            uint WhiteHash(uint value)
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            float WhiteValue(uint value)
            {
                return (WhiteHash(value) >> 8) * (1.0 / 16777216.0);
            }

            int2 GrainCell(float2 uv)
            {
                float2 pixel = uv * _WhiteNoiseGrid.xy;
                if (_NoiseOneD != 0)
                    pixel = float2(dot(pixel - .5 * _WhiteNoiseGrid.xy, _NoiseAxis.xy), 0.0);
                return (int2)floor((pixel + _NoiseDomain.zw) / _WhiteNoiseGrid.z);
            }

            float3 WhiteNoise(float2 uv)
            {
                int2 cell = GrainCell(uv);
                uint key = WhiteHash(asuint(cell.x)) ^ WhiteHash(asuint(cell.y) ^ 0x9e3779b9u)
                    ^ WhiteHash(asuint(_NoiseSeed) ^ 0x68bc21ebu);
                float r = WhiteValue(key);
                return _WhiteNoiseColor != 0
                    ? float3(r, WhiteValue(key ^ 0xa511e9b3u), WhiteValue(key ^ 0x63d83595u)) : r.xxx;
            }

            float BlueValue(int2 cell, uint channel)
            {
                uint key = WhiteHash(asuint(_NoiseSeed) ^ (0x68bc21ebu + channel * 0x9e3779b9u));
                if (_NoiseOneD != 0)
                {
                    key = WhiteHash(key ^ asuint(cell.y));
                    int x = (key & 256u) != 0u ? -cell.x : cell.x;
                    return _BlueNoise1D.Load(int3((x + (int)(key & 255u)) & 255, 0, 0))[channel];
                }
                if ((key & 16384u) != 0u) cell = cell.yx;
                if ((key & 32768u) != 0u) cell.x = -cell.x;
                if ((key & 65536u) != 0u) cell.y = -cell.y;
                cell = (cell + int2(key & 127u, (key >> 7) & 127u)) & 127;
                return _BlueNoise2D.Load(int3(cell, 0))[channel];
            }

            float3 BlueNoise(float2 uv)
            {
                int2 cell = GrainCell(uv);
                float r = BlueValue(cell, 0u);
                return _WhiteNoiseColor != 0 ? float3(r, BlueValue(cell, 1u), BlueValue(cell, 2u)) : r.xxx;
            }

            #if defined(WT_NOISE_PERIODIC)
            float PeriodicNoise(fnl_state state, float2 uv)
            {
                if (_NoiseOneD != 0)
                    uv = float2(dot(uv - .5, _NoiseAxis.zw) + .5, .5);
                // Canonical tile coordinates avoid loss of phase on transformed repeats.
                // Continuity comes from the lattice hash, not a fade at the boundary.
                if ((_NoisePeriodic & 1) != 0) uv.x = frac(uv.x);
                if ((_NoisePeriodic & 2) != 0) uv.y = frac(uv.y);
                float3 displacement = 0;
                if (_NoiseWarp > 0 && _NoiseWarpStrength > 0)
                {
                    fnl_state warp = fnlCreateState(_NoiseSeed);
                    warp.domain_warp_type = _NoiseWarp - 1;
                    float3 p = WtPosition(8, uv, 0);
                    float amp = _NoiseWarpStrength * _fnlCalculateFractalBounding(warp);
                    #if defined(WT_NOISE_3D)
                        _fnlDoSingleDomainWarp3D(warp, _NoiseSeed, amp, 1, p.x, p.y, p.z, displacement.x, displacement.y, displacement.z);
                    #else
                        _fnlDoSingleDomainWarp2D(warp, _NoiseSeed, amp, 1, p.x, p.y, displacement.x, displacement.y);
                    #endif
                    if ((int)_NoiseWarpInverse.w == 2)
                        displacement.xy = float2(displacement.x + displacement.y, displacement.y - displacement.x) * .7071067811865475;
                    displacement *= _NoiseWarpInverse.xyz;
                }
                int count = _NoiseFractal == 0 ? 1 : _NoiseOctaves;
                float amp = _NoiseFractal == 0 ? 1 : _fnlCalculateFractalBounding(state);
                float sum = 0;
                [loop] for (int octave = 0; octave < count; octave++)
                {
                    float3 p = WtPosition(octave, uv, displacement);
                    #if defined(WT_NOISE_3D)
                    float n = _fnlGenNoiseSingle3D(state, _NoiseSeed + octave, p.x, p.y, p.z);
                    #else
                    float n = _fnlGenNoiseSingle2D(state, _NoiseSeed + octave, p.x, p.y);
                    #endif
                    if (_NoiseFractal == 2)
                    {
                        n = abs(n);
                        sum += (1 - 2 * n) * amp;
                        amp *= lerp(1, 1 - n, state.weighted_strength);
                    }
                    else if (_NoiseFractal == 3)
                    {
                        n = _fnlPingPong((n + 1) * state.ping_pong_strength);
                        sum += (n - .5) * 2 * amp;
                        amp *= lerp(1, n, state.weighted_strength);
                    }
                    else
                    {
                        sum += n * amp;
                        amp *= lerp(1, min(n + 1, 2) * .5, state.weighted_strength);
                    }
                    amp *= state.gain;
                }
                return sum;
            }
            #endif

            float4 frag(v2f_img i) : SV_Target
            {
                i.uv = ProceduralSourceUv(i.uv);
                if (_NoiseType == 6 || _NoiseType == 7)
                {
                    float3 rgb = _NoiseType == 7 ? BlueNoise(i.uv) : WhiteNoise(i.uv);
                    if (_NoiseInverted != 0) rgb = 1.0 - rgb;
                    if (_UseGradient != 0) return MapGradient(rgb.r);
                    if (_NoiseEncoding == 0) rgb = SpriteDecode(rgb);
                    return float4(rgb, 1.0);
                }
                fnl_state state = fnlCreateState(_NoiseSeed);
                state.frequency = 1.0;
                state.noise_type = WHIMTEX_NOISE_TYPE;
                state.fractal_type = _NoiseFractal;
                state.octaves = _NoiseOctaves;
                state.lacunarity = _NoiseFractalSettings.x;
                state.gain = _NoiseFractalSettings.y;
                state.weighted_strength = _NoiseFractalSettings.z;
                state.ping_pong_strength = _NoiseFractalSettings.w;
                state.cellular_distance_func = _NoiseCellularDistance;
                state.cellular_return_type = _NoiseCellularReturn;
                state.cellular_jitter_mod = _NoiseCellularJitter;
                float3 p = float3((i.uv - .5) * _NoiseDomain.xy * _NoiseScale + _NoiseDomain.zw, _NoiseZ);
                if (_NoiseOneD != 0)
                {
                    float2 centered = (i.uv - .5) * _NoiseDomain.xy * _NoiseScale;
                    p.xy = float2(dot(centered, _NoiseAxis.xy), 0.0) + _NoiseDomain.zw;
                }
                #if !defined(WT_NOISE_PERIODIC)
                if (_NoiseWarp > 0 && _NoiseWarpStrength > 0.0)
                {
                    fnl_state warp = fnlCreateState(_NoiseSeed);
                    warp.frequency = 1.0;
                    warp.domain_warp_type = _NoiseWarp - 1;
                    warp.domain_warp_amp = _NoiseWarpStrength;
                    float3 warpPosition = p * _NoiseWarpScale;
                    float3 warpedPosition = warpPosition;
                    #if defined(WT_NOISE_3D)
                    fnlDomainWarp3D(warp, warpedPosition.x, warpedPosition.y, warpedPosition.z);
                    #else
                    fnlDomainWarp2D(warp, warpedPosition.x, warpedPosition.y);
                    #endif
                    p += warpedPosition - warpPosition;
                }
                #endif
                float raw;
                #if defined(WT_NOISE_PERIODIC)
                raw = PeriodicNoise(state, i.uv);
                #elif defined(WT_NOISE_3D)
                raw = fnlGetNoise3D(state, p.x, p.y, p.z);
                #else
                raw = fnlGetNoise2D(state, p.x, p.y);
                #endif
                float value = saturate(raw * .5 + .5);
                if (_NoiseInverted != 0) value = 1.0 - value;
                if (_UseGradient != 0) return MapGradient(value);
                float3 rgb = value.xxx;
                if (_NoiseEncoding == 0) rgb = SpriteDecode(rgb);
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
