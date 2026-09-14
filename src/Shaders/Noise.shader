Shader "Hidden/TextureCompositor/Noise"
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
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            #include "ProceduralUv.cginc"
            #include "ThirdParty/FastNoiseLite.hlsl"

            float4 _NoiseDomain, _NoiseFractalSettings;
            float4 _NoiseAxis;
            int _NoiseOneD;
            float _NoiseScale, _NoiseCellularJitter, _NoiseWarpStrength;
            int _NoiseSeed, _NoiseType, _NoiseFractal, _NoiseOctaves;
            int _NoiseCellularDistance, _NoiseCellularReturn, _NoiseWarp, _NoiseEncoding, _NoiseInverted;
            float4 _WhiteNoiseGrid;
            int _WhiteNoiseColor;
            Texture2D<float4> _BlueNoise2D, _BlueNoise1D;

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

            float4 frag(v2f_img i) : SV_Target
            {
                i.uv = ProceduralSourceUv(i.uv);
                if (_NoiseType == 6 || _NoiseType == 7)
                {
                    float3 rgb = _NoiseType == 7 ? BlueNoise(i.uv) : WhiteNoise(i.uv);
                    if (_NoiseInverted != 0) rgb = 1.0 - rgb;
                    if (_NoiseEncoding == 0) rgb = SpriteDecode(rgb);
                    return float4(rgb, 1.0);
                }
                fnl_state state = fnlCreateState(_NoiseSeed);
                state.frequency = 1.0;
                state.noise_type = _NoiseType;
                state.fractal_type = _NoiseFractal;
                state.octaves = _NoiseOctaves;
                state.lacunarity = _NoiseFractalSettings.x;
                state.gain = _NoiseFractalSettings.y;
                state.weighted_strength = _NoiseFractalSettings.z;
                state.ping_pong_strength = _NoiseFractalSettings.w;
                state.cellular_distance_func = _NoiseCellularDistance;
                state.cellular_return_type = _NoiseCellularReturn;
                state.cellular_jitter_mod = _NoiseCellularJitter;
                float2 p = (i.uv - .5) * _NoiseDomain.xy * _NoiseScale + _NoiseDomain.zw;
                if (_NoiseOneD != 0)
                {
                    float2 centered = (i.uv - .5) * _NoiseDomain.xy * _NoiseScale;
                    p = float2(dot(centered, _NoiseAxis.xy), 0.0) + _NoiseDomain.zw;
                }
                if (_NoiseWarp > 0 && _NoiseWarpStrength > 0.0)
                {
                    fnl_state warp = fnlCreateState(_NoiseSeed);
                    warp.frequency = 1.0;
                    warp.domain_warp_type = _NoiseWarp - 1;
                    warp.domain_warp_amp = _NoiseWarpStrength;
                    fnlDomainWarp2D(warp, p.x, p.y);
                }
                float value = saturate(fnlGetNoise2D(state, p.x, p.y) * .5 + .5);
                if (_NoiseInverted != 0) value = 1.0 - value;
                float3 rgb = value.xxx;
                if (_NoiseEncoding == 0) rgb = SpriteDecode(rgb);
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
