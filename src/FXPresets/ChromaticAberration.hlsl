// @whimtex-effect Stylization/Chromatic Aberration
// @param enum _Mode = 0 {Radial: 0, Directional: 1}
// @param float _Amount = 2 [0 .. ~16] // Red and blue channel offset from the original in canvas pixels.
// @param float _Blend = 1 [0 .. 1] // Blend the aberrated result with the original image.
// @if _Mode == 0
// @header(Radial)
// @param point _Center = (0.5, 0.5) // Origin of the radial color separation.
// @param float _Falloff = 1 [0.25 .. ~4] // How quickly the separation grows from the center toward the edges.
// @endif
// @if _Mode == 1
// @header(Directional)
// @param float _Angle = 0 [~-180 .. ~180] // Direction of the red channel shift; blue shifts in the opposite direction.
// @endif

float4 ApplyFX(float2 uv, float4 color)
{
    if (_Amount <= 0.0 || _Blend <= 0.0)
        return color;

    float2 direction;
    float amount = _Amount;
    if (_Mode < 0.5)
    {
        float2 radial = uv - _Center;
        float radius = length(radial);
        direction = radius > 1e-6 ? radial / radius : float2(0.0, 0.0);
        amount *= pow(saturate(radius * 1.41421356), max(_Falloff, 0.01));
    }
    else
    {
        float sine, cosine;
        sincos(radians(_Angle), sine, cosine);
        direction = float2(cosine, sine);
    }

    float2 offset = direction * amount * _CanvasSize.zw;
    float2 halfTexel = 0.5 * _CanvasSize.zw;
    float2 maxUV = 1.0 - halfTexel;
    float red = SampleInput(clamp(uv + offset, halfTexel, maxUV)).r;
    float blue = SampleInput(clamp(uv - offset, halfTexel, maxUV)).b;
    float3 shifted = float3(red, color.g, blue);

    return float4(lerp(color.rgb, shifted, saturate(_Blend)), color.a);
}
