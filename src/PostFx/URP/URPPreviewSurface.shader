Shader "Hidden/TextureCompositor/URPPreviewSurface"
{
    Properties
    {
        // Register the blit input even though only the final pass samples it.
        [HideInInspector] _MainTex ("Blit Input", 2D) = "black" {}
        [HideInInspector] _Source ("Composite Input", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _Source;
        sampler2D _MainTex;
        sampler2D _ScreenSpaceOcclusionTexture;
        float4 _AmbientOcclusionParam, _ScaledScreenParams, _ScaleBiasRt;
        float4 _DepthSettings, _Background;
        float4 _CheckerLight, _CheckerDark, _CheckerSettings;
        float _Invert;
        float4x4 _SurfaceInverseProjection, _SurfaceCameraToWorld;
        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1);
            output.uv = input.uv;
            if (_ProjectionParams.x < 0) output.positionCS.y *= -1;
            return output;
        }
        float SurfaceDistance(float alpha)
        {
            float height = lerp(saturate(alpha), 1-saturate(alpha), _Invert);
            float distance = _DepthSettings.y;
            if (_DepthSettings.x > .5 && _DepthSettings.x < 1.5) distance += (1-height)*_DepthSettings.z;
            if (_DepthSettings.x > 1.5 && height < _DepthSettings.w) distance = _ProjectionParams.z;
            return clamp(distance, _ProjectionParams.y + .0001, _ProjectionParams.z);
        }
        float SurfaceDepth(float alpha)
        {
            float distance = SurfaceDistance(alpha);
            if (unity_OrthoParams.w > .5)
            {
                float z = (distance-_ProjectionParams.y)/(_ProjectionParams.z-_ProjectionParams.y);
                #if UNITY_REVERSED_Z
                    z = 1-z;
                #endif
                return z;
            }
            return saturate((rcp(distance)-_ZBufferParams.w)/_ZBufferParams.z);
        }
        struct SurfaceOutput { float4 color : SV_Target; float depth : SV_Depth; };
        float3 BackgroundColor(float2 uv)
        {
            if (_CheckerSettings.w < .5) return _Background.rgb;
            float2 cell = floor(float2(uv.x,1-uv.y) * _CheckerSettings.xy / _CheckerSettings.z);
            float alternate = frac((cell.x + cell.y) * .5) * 2;
            return lerp(_CheckerLight.rgb, _CheckerDark.rgb, alternate);
        }
        SurfaceOutput Frag(Varyings input)
        {
            float4 color = tex2D(_Source, input.uv);
            SurfaceOutput output;
            output.depth = SurfaceDepth(color.a);
            color = float4(lerp(BackgroundColor(input.uv),color.rgb,saturate(color.a)),1);
            #ifdef UNITY_COLORSPACE_GAMMA
                color.rgb = LinearToGammaSpace(color.rgb);
            #endif
            // Before-opaque SSAO expects the surface shader to consume its result.
            // After-opaque SSAO blends into camera color itself and leaves this keyword off.
            #if defined(_SCREEN_SPACE_OCCLUSION)
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    screenUV.y = 1 - (screenUV.y * _ScaleBiasRt.x + _ScaleBiasRt.y);
                #endif
                float occlusion = tex2D(_ScreenSpaceOcclusionTexture, screenUV).r;
                color.rgb *= saturate(occlusion + (1 - _AmbientOcclusionParam.x));
            #endif
            output.color = color;
            return output;
        }
        float DepthFrag(Varyings input) : SV_Depth
        {
            return SurfaceDepth(tex2D(_Source,input.uv).a);
        }
        float3 SurfaceViewPosition(float2 uv, float distance)
        {
            float4 projected = mul(_SurfaceInverseProjection, float4(uv * 2 - 1, 1, 1));
            float3 position = projected.xyz / projected.w;
            if (unity_OrthoParams.w > .5) position.z = -distance;
            else position *= -distance / position.z;
            return position;
        }
        float3 SurfaceNormal(float2 uv, float distance)
        {
            float3 normalVS = float3(0,0,1);
            if (_DepthSettings.x > .5)
            {
                // Match the requested normals attachment resolution, including render scale.
                float2 stepUV = max(fwidth(uv), float2(1e-6,1e-6));
                float2 left = uv - float2(stepUV.x,0), right = uv + float2(stepUV.x,0);
                float2 down = uv - float2(0,stepUV.y), up = uv + float2(0,stepUV.y);
                float dl = SurfaceDistance(tex2D(_Source,saturate(left)).a);
                float dr = SurfaceDistance(tex2D(_Source,saturate(right)).a);
                float dd = SurfaceDistance(tex2D(_Source,saturate(down)).a);
                float du = SurfaceDistance(tex2D(_Source,saturate(up)).a);
                float3 center = SurfaceViewPosition(uv, distance);
                // Prefer the side without a depth discontinuity at masked silhouettes.
                float3 dx = abs(dr-distance) <= abs(dl-distance)
                    ? SurfaceViewPosition(right,dr)-center : center-SurfaceViewPosition(left,dl);
                float3 dy = abs(du-distance) <= abs(dd-distance)
                    ? SurfaceViewPosition(up,du)-center : center-SurfaceViewPosition(down,dd);
                float3 n = cross(dx,dy);
                float lengthSquared = dot(n,n);
                if (lengthSquared > 1e-20) normalVS = n * rsqrt(lengthSquared);
                if (normalVS.z < 0) normalVS = -normalVS;
            }
            return normalize(mul((float3x3)_SurfaceCameraToWorld, normalVS));
        }
        float3 EncodeSurfaceNormal(float3 normalWS)
        {
            // Match URP's raw world normal / deferred octahedral 12+12-bit encodings
            // without a shader include dependency on an optional installed package.
            #if defined(_GBUFFER_NORMALS_OCT)
                normalWS /= max(dot(abs(normalWS),float3(1,1,1)),1e-6);
                float t = saturate(-normalWS.z);
                float2 oct = normalWS.xy + float2(normalWS.x >= 0 ? t : -t, normalWS.y >= 0 ? t : -t);
                uint2 packed = (uint2)(saturate(oct*.5+.5)*4095.5);
                uint2 high = packed >> 8;
                uint2 low = packed & 255;
                return float3(low.x,low.y,high.x | (high.y << 4)) / 255.0;
            #else
                return normalWS;
            #endif
        }
        struct NormalsOutput
        {
            float4 normal : SV_Target0;
            #if defined(_WRITE_RENDERING_LAYERS)
                uint renderingLayers : SV_Target1;
            #endif
            float depth : SV_Depth;
        };
        NormalsOutput NormalsFrag(Varyings input)
        {
            float alpha = tex2D(_Source,input.uv).a;
            NormalsOutput output;
            output.depth = SurfaceDepth(alpha);
            output.normal = float4(EncodeSurfaceNormal(SurfaceNormal(input.uv,SurfaceDistance(alpha))),0);
            #if defined(_WRITE_RENDERING_LAYERS)
                output.renderingLayers = 1u;
            #endif
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "Surface"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite On ZTest Always Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off ZWrite On ZTest Always ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass
        {
            Name "NormalizeOutput"
            Tags { "LightMode"="WhimTexNormalizeOutput" }
            Cull Off ZWrite Off ZTest Always Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BlitVert
            #pragma fragment Normalize
            Varyings BlitVert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            float4 Normalize(Varyings input) : SV_Target
            {
                float4 color = tex2D(_MainTex,input.uv);
                #ifdef UNITY_COLORSPACE_GAMMA
                    color.rgb = GammaToLinearSpace(color.rgb);
                #endif
                return color;
            }
            ENDHLSL
        }
        // Keep NormalizeOutput at pass 2: the backend uses it for the final color conversion.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Off ZWrite On ZTest Always Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
            ENDHLSL
        }
    }
}
