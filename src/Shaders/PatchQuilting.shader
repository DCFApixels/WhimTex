Shader "Hidden/TextureCompositor/PatchQuilting"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "HistogramTransfer.hlsl"
        sampler2D _MainTex, _Paths, _Gaussian;
        float4 _Size, _Channels;
        float4 _MeanA, _MeanB, _VarianceA, _VarianceB, _Covariance;
        float4 _AlongShift;
        float _ShiftGuard;
        float _Band, _Feather, _Contrast;
        int _Transpose, _Independent, _Tiny;
        float4 Prepare(v2f_img i) : SV_Target
        {
            float4 c=tex2D(_MainTex,i.uv);
            c.a=saturate(c.a);
            return _Independent != 0 ? c : float4(c.rgb*c.a,c.a);
        }
        float4 Straight(v2f_img i) : SV_Target
        {
            float4 c=tex2D(_MainTex,i.uv);
            if (_Independent != 0) return c;
            return float4(c.a>1e-8 ? c.rgb/c.a : 0,saturate(c.a));
        }
        float4 Profile(float4 distance, float4 feather)
        {
            float4 t=saturate(.5+distance/max(feather,1e-6));
            return lerp(step(0,distance),t*t*(3-2*t),step(1e-6,feather));
        }
        float4 DonorSample(sampler2D source, float2 p, float4 donor)
        {
            float row=_Transpose!=0?p.x:p.y;
            float length=_Transpose!=0?_Size.x:_Size.y;
            float span=max(0,1-2*_ShiftGuard);
            float t=saturate((row/max(1,length-1)-_ShiftGuard)/max(span,1e-6));
            float q=t*(1-t);
            float4 along=row+_AlongShift*(length-1)*span*16*q*q;
            if (_Transpose != 0)
                return float4(tex2D(source,float2((along.r+.5)*_Size.z,(donor.r+.5)*_Size.w)).r,
                    tex2D(source,float2((along.g+.5)*_Size.z,(donor.g+.5)*_Size.w)).g,
                    tex2D(source,float2((along.b+.5)*_Size.z,(donor.b+.5)*_Size.w)).b,
                    tex2D(source,float2((along.a+.5)*_Size.z,(donor.a+.5)*_Size.w)).a);
            return float4(tex2D(source,float2((donor.r+.5)*_Size.z,(along.r+.5)*_Size.w)).r,
                tex2D(source,float2((donor.g+.5)*_Size.z,(along.g+.5)*_Size.w)).g,
                tex2D(source,float2((donor.b+.5)*_Size.z,(along.b+.5)*_Size.w)).b,
                tex2D(source,float2((donor.a+.5)*_Size.z,(along.a+.5)*_Size.w)).a);
        }
        float4 Gaussian(v2f_img i) : SV_Target { return GaussianValue(tex2D(_MainTex,i.uv)); }
        float4 Blend(v2f_img i) : SV_Target
        {
            float2 p=min(floor(i.uv*_Size.xy),_Size.xy-1);
            float n=_Transpose != 0 ? _Size.y : _Size.x;
            float x=_Transpose != 0 ? p.y : p.x;
            float row=_Transpose != 0 ? (p.x+.5)*_Size.z : (p.y+.5)*_Size.w;
            float4 original=tex2D(_MainTex,(p+.5)*_Size.zw);
            if (x>=_Band && x<n-_Band) return original;
            float strip=x>=n-_Band ? x-(n-_Band) : x+_Band;
            float4 left=tex2D(_Paths,float2(row,1.0/6))*(2*_Band-1);
            float4 right=tex2D(_Paths,float2(row,.5))*(2*_Band-1);
            float4 donor=round(tex2D(_Paths,float2(row,5.0/6)))+strip;
            // A coarse search can have too few samples for a cut even when the
            // full-resolution band has plenty of room. Use centered cuts there.
            if (_Tiny != 0)
            {
                left=(_Band-1)*.5;
                right=_Band+(_Band-1)*.5;
            }
            float4 leftFeather=2*saturate(_Feather)*max(0,min(left,_Band-1-left));
            float4 rightFeather=2*saturate(_Feather)*max(0,min(right-_Band,2*_Band-1-right));
            float4 weight=_Band <= 1 ? 1 : min(Profile(strip-left,leftFeather),Profile(right-strip,rightFeather));
            if (_Independent != 0) weight *= _Channels;
            float4 copied=DonorSample(_MainTex,p,donor);
            float4 raw=lerp(original,copied,weight);
            if (_Contrast <= 0 || all(weight <= 0 || weight >= 1)) return raw;
            float4 z=lerp(tex2D(_Gaussian,(p+.5)*_Size.zw),DonorSample(_Gaussian,p,donor),weight);
            float4 mean=lerp(_MeanA,_MeanB,weight);
            float4 target=lerp(_VarianceA,_VarianceB,weight);
            float4 remaining=(1-weight)*(1-weight)*_VarianceA+weight*weight*_VarianceB+2*weight*(1-weight)*_Covariance;
            float4 scale=sqrt(target/max(remaining,max(target*1e-6,1e-20)));
            float4 corrected=Table(((z-mean)*scale+mean+5)/10,.75);
            // Fade correction to zero at pure samples, including LUT round-trip error.
            float4 strength=_Contrast*4*weight*(1-weight)*step(1e-20,_Extent)*step(1e-12,target);
            strength*=1-step(abs(original-copied),0);
            return lerp(raw,corrected,strength);
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Prepare
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Blend
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Straight
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Gaussian
            ENDCG }
    }
}
