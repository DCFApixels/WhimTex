Shader "Hidden/WhimTex/HistogramSeamless"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "HistogramTransfer.hlsl"
        sampler2D _MainTex, _Gaussian;
        float4 _Size, _AnalysisSize, _Mean, _Variance, _CovX, _CovY, _CovXY, _CovXNY;
        float4 _Edges;
        float _BandWidth, _Contrast, _Falloff, _TransitionStart;
        int _Mirror;
        float4 Prepare(v2f_img i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            c.a = saturate(c.a);
            return float4(c.a > 0 ? c.rgb * c.a : 0, c.a);
        }
        float4 Gaussian(v2f_img i) : SV_Target
        {
            return GaussianValue(tex2D(_MainTex,i.uv));
        }
        float4 SampleGaussian(float2 p) { return tex2D(_Gaussian, (p+.5)*_Size.zw); }
        float4 Sample(float2 p) { return tex2D(_MainTex, (p + .5) * _Size.zw); }
        float4 Analyze(v2f_img i) : SV_Target
        {
            float2 cell = min(floor(i.uv * _AnalysisSize.xy), _AnalysisSize.xy-1);
            uint2 key = (uint2)cell % max((uint2)(_AnalysisSize.xy*.5),1);
            if (_Mirror != 0) key = (uint2)min(cell,_AnalysisSize.xy-1-cell);
            uint h = key.x*1597334677u ^ key.y*3812015801u;
            h ^= h >> 16; h *= 2246822519u; h ^= h >> 13;
            float2 jitter = float2(h & 65535u, h >> 16) / 65536.0;
            float2 p = min(floor((cell+jitter)*_Size.xy/_AnalysisSize.xy),_Size.xy-1);
            if (_Mirror != 0)
            {
                float2 low = floor(((float2)key+jitter)*_Size.xy/_AnalysisSize.xy);
                p = float2(cell.x > (_AnalysisSize.x-1)*.5 ? _Size.x-1-low.x : low.x,
                           cell.y > (_AnalysisSize.y-1)*.5 ? _Size.y-1-low.y : low.y);
            }
            return Sample(p);
        }
        float4 Straight(float4 c)
        {
            c.a = saturate(c.a);
            return float4(c.a > 1e-8 ? c.rgb / c.a : 0, c.a);
        }
        float4 Blend(v2f_img i) : SV_Target
        {
            float2 p = min(floor(i.uv * _Size.xy), _Size.xy - 1);
            float2 donor = p - floor(_Size.xy * .5);
            if (donor.x < 0) donor.x += _Size.x;
            if (donor.y < 0) donor.y += _Size.y;
            if (_Mirror != 0) donor = _Size.xy-1-p;
            float4 distances = float4(p.x,_Size.x-1-p.x,p.y,_Size.y-1-p.y);
            float4 sizes = _Size.xxyy;
            float4 t = saturate((distances - _TransitionStart * _BandWidth * sizes) / ((1 - _TransitionStart) * _BandWidth * sizes));
            float4 fades = (1-t*t*t*(10+t*(-15+6*t))) * _Edges;
            if (_Mirror != 0)
            {
                float4 distance = distances / max(sizes-1,1);
                float4 q = saturate((1-distance/_BandWidth)/(1-_TransitionStart));
                fades = pow(q*q*(3-2*q),_Falloff)*_Edges;
            }
            float2 fade = float2(max(fades.x,fades.y),max(fades.z,fades.w));
            if (_Size.x == 1) fade.x = 0;
            if (_Size.y == 1) fade.y = 0;
            float4 w = float4((1-fade.x)*(1-fade.y),fade.x*(1-fade.y),(1-fade.x)*fade.y,fade.x*fade.y);
            float4 a = Sample(p), b = Sample(float2(donor.x,p.y)), c = Sample(float2(p.x,donor.y)), d = Sample(donor);
            float4 raw = w.x*a+w.y*b+w.z*c+w.w*d;
            if (_Contrast == 0) return Straight(raw);
            if (w.x == 1) return Straight(a);
            if (w.y == 1) return Straight(b);
            if (w.z == 1) return Straight(c);
            if (w.w == 1) return Straight(d);
            float4 z = w.x*SampleGaussian(p)+w.y*SampleGaussian(float2(donor.x,p.y))+
                w.z*SampleGaussian(float2(p.x,donor.y))+w.w*SampleGaussian(donor);
            float4 v = dot(w,w)*_Variance + 2*((w.x*w.y+w.z*w.w)*_CovX +
                (w.x*w.z+w.y*w.w)*_CovY + w.x*w.w*_CovXY + w.y*w.z*_CovXNY);
            float4 scale = sqrt(_Variance / max(v, max(_Variance*1e-6, 1e-20)));
            float4 result = Table(((z-_Mean)*scale+_Mean+5)/10,.75);
            result = lerp(raw,result,step(1e-20,_Extent));
            return Straight(lerp(raw,result,_Contrast));
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
            #pragma fragment Analyze
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Gaussian
            ENDCG }
    }
}
