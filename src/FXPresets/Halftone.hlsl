// @whimtex-effect Stylization/Halftone
// @param enum _Mode = 0 {Monochrome: 0, CMYK: 1}
// @param float _DotSize = 8 [1 .. ~64] // Screen-cell size in canvas pixels.
// @param enum _DotShape = 0 {Round: 0, Square: 1, Line: 2} // Round and square dots, or a line screen.
// @param float _InkDensity = 1 [0 .. ~2] // Overall ink amount; values above 1 deepen the darkest tones.
// @param color _PaperColor = (1, 1, 1, 1) // Paper tint; alpha is ignored.
// @if _Mode == 0
// @header(Monochrome Screen)
// @param float _Angle = 45 [~-180 .. ~180] // Screen angle in degrees.
// @param bool _InvertPattern = false // Reverse dot polarity while preserving the average tone.
// @param color _InkColor = (0, 0, 0, 1) // Dot color; alpha is ignored.
// @endif
// @if _Mode == 1
// @header(CMYK Screen Angles)
// @param float _CyanAngle = 15 [~-180 .. ~180]
// @param float _MagentaAngle = 75 [~-180 .. ~180]
// @param float _YellowAngle = 0 [~-180 .. ~180]
// @param float _BlackAngle = 45 [~-180 .. ~180]
// @header(Plate Registration)
// @param float2 _CyanOffset = (0, 0) // Shift the cyan plate in canvas pixels.
// @param float2 _MagentaOffset = (0, 0) // Shift the magenta plate in canvas pixels.
// @param float2 _YellowOffset = (0, 0) // Shift the yellow plate in canvas pixels.
// @param float2 _BlackOffset = (0, 0) // Shift the black plate in canvas pixels.
// @endif

float HalftoneCircleCoverage(float radius)
{
    float radiusSq = radius * radius;
    float cornerReach = sqrt(max(radiusSq - 0.25, 0.0));
    float cornerAngle = atan2(cornerReach, 0.5);
    return radiusSq * (3.14159265 - 4.0 * cornerAngle) + 2.0 * cornerReach;
}

float HalftonePatternThreshold(float2 pixel, float angleDegrees)
{
    float angle = radians(angleDegrees);
    float sine, cosine;
    sincos(angle, sine, cosine);
    float2 screen = float2(cosine * pixel.x - sine * pixel.y,
                           sine * pixel.x + cosine * pixel.y) / max(_DotSize, 1.0);
    float2 cell = frac(screen) - 0.5;

    if (_DotShape < 0.5)
        return HalftoneCircleCoverage(length(cell));
    if (_DotShape < 1.5)
    {
        float squareRadius = max(abs(cell.x), abs(cell.y));
        return 4.0 * squareRadius * squareRadius;
    }
    return 2.0 * abs(cell.y);
}

float HalftoneScreen(float2 pixel, float angleDegrees, float inkAmount)
{
    inkAmount = saturate(inkAmount);
    if (inkAmount <= 0.0) return 0.0;
    if (inkAmount >= 1.0) return 1.0;

    float threshold = HalftonePatternThreshold(pixel, angleDegrees);
    float edge = max(fwidth(threshold), 1e-4);
    return 1.0 - smoothstep(inkAmount - edge, inkAmount + edge, threshold);
}

float3 SampleRegisteredPlate(float2 uv, float2 offsetPixels, float4 fallbackColor)
{
    float3 sampledColor = fallbackColor.rgb;
    if (any(abs(offsetPixels) > 1e-5))
    {
        float2 halfTexel = 0.5 * _CanvasSize.zw;
        float2 sampleUV = uv - offsetPixels * _CanvasSize.zw;
        sampleUV = clamp(sampleUV, halfTexel, 1.0 - halfTexel);
        sampledColor = SampleInput(sampleUV).rgb;
    }
    return sampledColor;
}

float RGBToBlack(float3 rgb)
{
    rgb = saturate(rgb);
    return 1.0 - max(rgb.r, max(rgb.g, rgb.b));
}

float3 RGBToCMY(float3 rgb)
{
    rgb = saturate(rgb);
    float black = RGBToBlack(rgb);
    return saturate((1.0 - rgb - black) / max(1.0 - black, 1e-5));
}

float4 ApplyFX(float2 uv, float4 color)
{
    float4 result = color;
    float density = max(_InkDensity, 0.0);
    float2 pixel = uv * _CanvasSize.xy;

    if (_Mode < 0.5)
    {
        float luminance = saturate(dot(max(color.rgb, 0.0), float3(0.2126, 0.7152, 0.0722)));
        bool invertPattern = _InvertPattern > 0.5;
        float inkAmount = invertPattern ? luminance : 1.0 - luminance;
        float dots = HalftoneScreen(pixel, _Angle, inkAmount * density);
        result.rgb = invertPattern
            ? lerp(_InkColor.rgb, _PaperColor.rgb, dots)
            : lerp(_PaperColor.rgb, _InkColor.rgb, dots);
    }
    else
    {
        float3 cyanSource = RGBToCMY(SampleRegisteredPlate(uv, _CyanOffset, color));
        float3 magentaSource = RGBToCMY(SampleRegisteredPlate(uv, _MagentaOffset, color));
        float3 yellowSource = RGBToCMY(SampleRegisteredPlate(uv, _YellowOffset, color));
        float3 blackRGB = saturate(SampleRegisteredPlate(uv, _BlackOffset, color));
        float sourceBlack = RGBToBlack(blackRGB);

        float cyanDots = HalftoneScreen(pixel, _CyanAngle, cyanSource.x * density);
        float magentaDots = HalftoneScreen(pixel, _MagentaAngle, magentaSource.y * density);
        float yellowDots = HalftoneScreen(pixel, _YellowAngle, yellowSource.z * density);
        float blackDots = HalftoneScreen(pixel, _BlackAngle, sourceBlack * density);

        result.rgb = _PaperColor.rgb * float3(
            (1.0 - cyanDots) * (1.0 - blackDots),
            (1.0 - magentaDots) * (1.0 - blackDots),
            (1.0 - yellowDots) * (1.0 - blackDots));
    }

    return result;
}
