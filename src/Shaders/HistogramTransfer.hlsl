#ifndef WHIMTEX_HISTOGRAM_TRANSFER
#define WHIMTEX_HISTOGRAM_TRANSFER
sampler2D _Tables;
float4 _Minimum, _Extent;
float4 Table(float4 t, float row)
{
    t = (saturate(t) * 4095 + .5) / 4096;
    return float4(tex2D(_Tables, float2(t.r,row)).r, tex2D(_Tables, float2(t.g,row)).g,
                  tex2D(_Tables, float2(t.b,row)).b, tex2D(_Tables, float2(t.a,row)).a);
}
float4 GaussianValue(float4 c)
{
    float4 left = 0, right = 4095;
    [unroll] for (int j=0;j<12;j++)
    {
        float4 middle = floor((left+right)*.5);
        float4 value = Table(middle/4095,.75);
        float4 select = step(c,value);
        right = lerp(right,middle,select);
        left = lerp(middle,left,select);
    }
    float4 a = Table(left/4095,.75), b = Table(right/4095,.75);
    float4 z = -5+10*(left+saturate((c-a)/max(b-a,1e-20)))/4095;
    z = lerp(z,Table(0,.25),step(c,_Minimum));
    z = lerp(z,Table(1,.25),step(_Minimum+_Extent,c));
    return z;
}
#endif
