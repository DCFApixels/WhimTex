// @whimtex-effect Distortion/Displacement Map
// @header(Map)
// @param texture2D _DisplacementMap = self // Source field. Vector mode reads R/G; Grayscale mode reads the selected channel.
// @param transform2D _MapTransform = (0.5, 0.5, 1, 1, 0) // Positions and scales the map independently from the image being distorted.
// @param enum _MapWrap = Clamp {Clamp: 0, Repeat: 1, Mirror: 2} // Addressing for the map and an optional separate strength mask.
// @param enum _Mode = VectorRG {VectorRG: 0, Grayscale: 1}
// @param float _Neutral = 0.5 [0 .. 1] // Neutral value for vector channels and the grayscale displacement.
// @if _Mode == 0
// @header(Vector)
// @param float _StrengthX = 0 [~-512 .. ~512] // Horizontal displacement in canvas pixels at a full channel value.
// @param float _StrengthY = 0 [~-512 .. ~512] // Vertical displacement in canvas pixels at a full channel value.
// @endif
// @if _Mode == 1
// @header(Grayscale)
// @param enum _MapChannel = Luminance {Luminance: 0, R: 1, G: 2, B: 3, Alpha: 4}
// @param enum _Direction = Horizontal {Horizontal: 0, Vertical: 1, Radial: 2, Tangential: 3, Angle: 4}
// @param float _Strength = 0 [~-512 .. ~512] // Displacement in canvas pixels. The sign reverses the selected direction.
// @param point _Center = (0.5, 0.5) // Origin for Radial and Tangential directions.
// @param float _Angle = 0 [~-180 .. ~180] // Direction angle in degrees when Direction is Angle.
// @endif
// @header(Strength Mask)
// @param enum _MaskSource = Constant1 {Constant1: 0, MapChannel: 1, InputAlpha: 2, SeparateTexture: 3} // Constant1 needs no second texture; MapChannel reuses the displacement map.
// @if _MaskSource != 0
// @param bool _InvertMask = false
// @param curve _MaskProfile // Remaps mask values; the default linear curve preserves them.
// @endif
// @if _MaskSource == 1
// @param enum _MapMaskChannel = Alpha {Alpha: 0, Luminance: 1, R: 2, G: 3, B: 4}
// @endif
// @if _MaskSource == 3
// @param texture2D _StrengthMask = none // Optional second texture. Leave Mask Source at Constant1 to work with only one map.
// @param enum _SeparateMaskChannel = Alpha {Alpha: 0, Luminance: 1, R: 2, G: 3, B: 4}
// @endif
// @header(Output)
// @param float _Mix = 1 [0 .. 1] // Blend the displaced sample with the original image.
// @param enum _InputEdge = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Transparent: 3} // Addressing for displaced image samples, independent of map wrapping.

float ReadDisplacementChannel(float4 value, float channel)
{
    float result = dot(value.rgb, float3(0.2126, 0.7152, 0.0722));
    if (channel > 0.5 && channel < 1.5) result = value.r;
    else if (channel > 1.5 && channel < 2.5) result = value.g;
    else if (channel > 2.5 && channel < 3.5) result = value.b;
    else if (channel > 3.5) result = value.a;
    return result;
}

float ReadStrengthChannel(float4 value, float channel)
{
    float result = value.a;
    if (channel > 0.5 && channel < 1.5) result = dot(value.rgb, float3(0.2126, 0.7152, 0.0722));
    else if (channel > 1.5 && channel < 2.5) result = value.r;
    else if (channel > 2.5 && channel < 3.5) result = value.g;
    else if (channel > 3.5) result = value.b;
    return result;
}

float2 AddressMapUV(float2 uv, float mode, float2 texelSize)
{
    if (mode < 0.5)
        return clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
    if (mode < 1.5)
        uv = frac(uv);
    else
        uv = 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);
    return uv;
}

float2 AddressInputUV(float2 uv, float mode, float2 texelSize, out float inside)
{
    inside = 1.0;
    if (mode < 0.5)
        return clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
    if (mode < 1.5)
        return frac(uv);
    if (mode < 2.5)
        return 1.0 - abs(frac(uv * 0.5) * 2.0 - 1.0);

    inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    return clamp(uv, texelSize * 0.5, 1.0 - texelSize * 0.5);
}

float2 GrayscaleDirection(float2 uv)
{
    float2 direction = float2(1.0, 0.0);
    if (_Direction > 0.5 && _Direction < 1.5)
        direction = float2(0.0, 1.0);
    else if (_Direction > 1.5 && _Direction < 3.5)
    {
        float2 radial = (uv - _Center) * _CanvasSize.xy;
        float radius = length(radial);
        direction = radius > 1e-6 ? radial / radius : float2(0.0, 0.0);
        if (_Direction > 2.5)
            direction = float2(-direction.y, direction.x);
    }
    else if (_Direction > 3.5)
    {
        float sine, cosine;
        sincos(radians(_Angle), sine, cosine);
        direction = float2(cosine, sine);
    }
    return direction;
}

float4 ApplyFX(float2 uv, float4 color)
{
    float2 mapUV = _MapTransform_ToLocal(uv);
    mapUV = AddressMapUV(mapUV, _MapWrap, _DisplacementMap_TexelSize.xy);
    float4 mapSample = tex2D(_DisplacementMap, mapUV);
    float2 displacementPixels = float2(0.0, 0.0);

    if (_Mode < 0.5)
    {
        displacementPixels = (mapSample.rg - float2(_Neutral, _Neutral)) * 2.0 * float2(_StrengthX, _StrengthY);
    }
    else
    {
        float mapValue = ReadDisplacementChannel(mapSample, _MapChannel);
        float signedValue = (mapValue - _Neutral) * 2.0;
        displacementPixels = GrayscaleDirection(uv) * signedValue * _Strength;
    }

    float strengthMask = 1.0;
    if (_MaskSource > 0.5)
    {
        float maskValue = 1.0;
        if (_MaskSource < 1.5)
            maskValue = ReadStrengthChannel(mapSample, _MapMaskChannel);
        else if (_MaskSource < 2.5)
            maskValue = color.a;
        else
        {
            float2 strengthMaskUV = AddressMapUV(_MapTransform_ToLocal(uv), _MapWrap, _StrengthMask_TexelSize.xy);
            maskValue = ReadStrengthChannel(tex2D(_StrengthMask, strengthMaskUV), _SeparateMaskChannel);
        }
        if (_InvertMask > 0.5)
            maskValue = 1.0 - maskValue;
        strengthMask = saturate(_MaskProfile_Sample(saturate(maskValue)));
    }

    float2 inputUV = uv + displacementPixels * strengthMask * _CanvasSize.zw;
    float inside;
    inputUV = AddressInputUV(inputUV, _InputEdge, _MainTex_TexelSize.xy, inside);
    float4 distorted = SampleInput(inputUV) * inside;
    float4 result = lerp(color, distorted, saturate(_Mix));
    return result;
}
