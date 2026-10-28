// @whimtex-effect Distortion/Height to Distortion
// @control(_Strength)
// @group("Normal")
// @icon("../Editor/Icons/FX/Normal.svg")
// @param hidden float _Strength = 8 [~-64 .. ~64] // Multiply the raw height gradient. Zero outputs a neutral map; negative values reverse both axes.
// @header(Height Source)
// @param texture2D _HeightMap = self // Linear height data. Self reads the image before this FX; another layer or texture can be assigned.
// @param enum _Channel = Luminance {Luminance: 0, R: 1, G: 2, B: 3, Alpha: 4} // Read the selected channel without color conversion or height clamping.
// @header(Gradient)
// @param enum _Filter = Scharr {Scharr: 0, Sobel: 1, CentralDifference: 2}
// @param px label(Radius (px)) float _Radius = 1 [0.5 .. ~32] // Sampling radius in document pixels. The derivative is divided by the radius, not normalized to unit length.
// @param enum _Edges = Clamp {Clamp: 0, Repeat: 1, Mirror: 2} // Address the height map independently of its texture import settings.
// @param toggle(float) _FlipX = false
// @param toggle(float) _FlipY = false
// @header(Output)
// @param enum _Output = PackedRG {PackedRG: 0, SignedRG: 1} // Packed RG: Neutral 0.5 in Displacement Map. Signed RG: Neutral 0 and half the X/Y strengths for the same displacement. Use HDR Color Range to retain signed/out-of-range values.

float4 DistortionHeightRead(float2 uv)
{
    // A finite-difference stencil requires base-level samples. In particular, wrapped
    // texel-centre coordinates have discontinuous screen derivatives at repeat seams.
    return SampleTextureLod(_HeightMap, uv, 0);
}

float4 DistortionHeightSample(float2 uv)
{
    float2 halfTexel = _HeightMap_TexelSize.xy * 0.5;
    float4 result = 0.0;
    if (_Edges < 0.5)
        result = DistortionHeightRead(clamp(uv, halfTexel, 1.0 - halfTexel));
    else if (_Edges > 1.5)
    {
        uv = 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);
        result = DistortionHeightRead(clamp(uv, halfTexel, 1.0 - halfTexel));
    }
    else
    {
        uv = frac(uv);
        if (all(uv >= halfTexel) && all(uv <= 1.0 - halfTexel))
            result = DistortionHeightRead(uv);
        else
        {
            // Interpolate across a repeat seam even when the assigned texture uses Clamp.
            float2 pixel = uv * _HeightMap_TexelSize.zw - 0.5;
            float2 origin = floor(pixel);
            float2 weight = pixel - origin;
            float2 first = frac((origin + 0.5) * _HeightMap_TexelSize.xy);
            float2 second = frac((origin + 1.5) * _HeightMap_TexelSize.xy);
            float4 a = DistortionHeightRead(first);
            float4 b = DistortionHeightRead(float2(second.x, first.y));
            float4 c = DistortionHeightRead(float2(first.x, second.y));
            float4 d = DistortionHeightRead(second);
            result = lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
        }
    }
    return result;
}

float DistortionHeight(float2 uv)
{
    float4 source = DistortionHeightSample(uv);
    if (_Channel < 0.5) return dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
    if (_Channel < 1.5) return source.r;
    if (_Channel < 2.5) return source.g;
    if (_Channel < 3.5) return source.b;
    return source.a;
}

float4 ApplyFX(PinnedUV uv, float4 color)
{
    float2 gradient = 0.0;
    if (_Strength != 0.0)
    {
        float radius = max(_Radius, 1e-4);
        float2 stepUV = radius * _CanvasSize.zw;
        float l = DistortionHeight(uv - float2(stepUV.x, 0.0));
        float r = DistortionHeight(uv + float2(stepUV.x, 0.0));
        float b = DistortionHeight(uv - float2(0.0, stepUV.y));
        float t = DistortionHeight(uv + float2(0.0, stepUV.y));
        gradient = float2(r - l, t - b) / (2.0 * radius);
        if (_Filter < 1.5)
        {
            float tl = DistortionHeight(uv + stepUV * float2(-1.0, 1.0));
            float tr = DistortionHeight(uv + stepUV);
            float bl = DistortionHeight(uv - stepUV);
            float br = DistortionHeight(uv + stepUV * float2(1.0, -1.0));
            float side = _Filter < 0.5 ? 3.0 : 1.0;
            float center = _Filter < 0.5 ? 10.0 : 2.0;
            gradient = float2(side * (br + tr - bl - tl) + center * (r - l),
                side * (tl + tr - bl - br) + center * (t - b)) / ((4.0 * side + 2.0 * center) * radius);
        }
        gradient *= _Strength * float2(_FlipX > 0.5 ? -1.0 : 1.0, _FlipY > 0.5 ? -1.0 : 1.0);
    }
    // Keep signed values and large slopes; packing is affine, never a clamp or normalization.
    float2 encoded = _Output < 0.5 ? gradient * 0.5 + 0.5 : gradient;
    return float4(encoded, 0.0, 1.0);
}
