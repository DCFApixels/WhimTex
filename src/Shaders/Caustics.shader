Shader "Hidden/WhimTex/Caustics"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend One One
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _RayMap;
            float4 _RaySize, _ReceiverSize;
            int _Periodic;
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 distance : TEXCOORD0;
                nointerpolation float3 inverseCovariance : TEXCOORD1;
                nointerpolation float energy : TEXCOORD2;
            };
            float2 Displacement(float2 uv) { return tex2Dlod(_RayMap,float4(uv,0,0)).xy; }
            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                uint index=vertex/6, corner=vertex%6;
                float2 uv=(float2(index%(uint)_RaySize.x,index/(uint)_RaySize.x)+0.5)*_RaySize.zw;
                float4 ray=tex2Dlod(_RayMap,float4(uv,0,0));
                float2 dx=float2(_RaySize.z,0)+(Displacement(uv+float2(_RaySize.z,0))-Displacement(uv-float2(_RaySize.z,0)))*0.5;
                float2 dy=float2(0,_RaySize.w)+(Displacement(uv+float2(0,_RaySize.w))-Displacement(uv-float2(0,_RaySize.w)))*0.5;
                // Finite elliptical footprints keep folds finite and preserve flux.
                // Half-cell source footprints overlap smoothly, without a triangle seam.
                float2 softness=max(ray.w,0.5)*_ReceiverSize.zw;
                float a=0.25*(dx.x*dx.x+dy.x*dy.x)+softness.x*softness.x;
                float b=0.25*(dx.x*dx.y+dy.x*dy.y);
                float c=0.25*(dx.y*dx.y+dy.y*dy.y)+softness.y*softness.y;
                float determinant=max(a*c-b*b,1e-24);
                float2 radius=3*sqrt(float2(a,c));
                float2 center=uv+ray.xy;
                if (_Periodic!=0) center=frac(center)+float2((int)(instance%3)-1,(int)(instance/3)-1);
                float2 signs=corner==0 ? float2(-1,-1) : corner==1 ? float2(-1,1) :
                    corner==2 || corner==3 ? float2(1,1) : corner==4 ? float2(1,-1) : float2(-1,-1);
                Varyings o;
                o.distance=signs*radius;
                o.position=float4((center+o.distance)*2-1,0,1);
                #if UNITY_UV_STARTS_AT_TOP
                    o.position.y=-o.position.y;
                #endif
                o.inverseCovariance=float3(c,-b,a)/determinant;
                o.energy=ray.z*_RaySize.z*_RaySize.w/(6.28318530718*sqrt(determinant)*0.98889100346);
                // Reject invalid rays before rasterizing; invalid user math cannot cover the screen.
                if (!all(isfinite(float4(center,radius))) || !isfinite(o.energy) || ray.z<=0)
                { o.position=float4(2,2,0,1); o.energy=0; }
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 d=i.distance;
                float exponent=0.5*(i.inverseCovariance.x*d.x*d.x+2*i.inverseCovariance.y*d.x*d.y+i.inverseCovariance.z*d.y*d.y);
                clip(4.5-exponent);
                return float4(i.energy*exp(-max(exponent,0)),0,0,0);
            }
            ENDHLSL
        }
    }
}
