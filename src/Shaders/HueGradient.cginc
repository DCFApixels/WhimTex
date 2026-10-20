#ifndef WHIMTEX_HUE_GRADIENT_INCLUDED
#define WHIMTEX_HUE_GRADIENT_INCLUDED
sampler2D _WhimTexHueGradient;
float4 _WhimTexHueGradient_TexelSize;

float3 WhimTexHue(float hue)
{
    return tex2Dlod(_WhimTexHueGradient, float4(frac(hue) + 0.5 * _WhimTexHueGradient_TexelSize.x, 0.5, 0, 0)).rgb;
}
#endif
