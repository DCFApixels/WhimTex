// @whimtex-effect Stylization/Halftone
// @group(Screen Mode; _Mode)
// @param hidden enum _Mode = 1 {Monochrome: 0, CMYKTriangle: 2, CMYKSquare: 3, CMYKManual: 1, RGBTriangle: 4, RGBManual: 5}
// @param float _DotSize = 8 [1 .. ~64] // Screen-cell size in canvas pixels.
// @param enum _DotShape = 0 {Round: 0, Square: 1, Line: 2} // Round and square dots, or a line screen.
// @param float _InkDensity = 1 [0 .. ~2] // Overall ink amount; values above 1 deepen the darkest tones.
// @if _Mode == 0
// @header(Monochrome Screen)
// @param color _PaperColor = (1, 1, 1, 1) // Paper tint; alpha is ignored.
// @param float _Angle = 0 [~-180 .. ~180] // Screen angle in degrees.
// @param bool _InvertPattern = false // Reverse dot polarity while preserving the average tone.
// @param color _InkColor = (0, 0, 0, 1) // Dot color; alpha is ignored.
// @endif
// @if _Mode == 1
// @header(CMYK Screen Angles)
// @param color _PaperColor = (1, 1, 1, 1) // Paper tint; alpha is ignored.
// @param float _CyanAngle = 0 [~-180 .. ~180]
// @param float _MagentaAngle = 0 [~-180 .. ~180]
// @param float _YellowAngle = 0 [~-180 .. ~180]
// @param float _BlackAngle = 0 [~-180 .. ~180]
// @header(Plate Registration)
// @param float2 _CyanOffset = (0, 0) // Shift the cyan plate in canvas pixels.
// @param float2 _MagentaOffset = (0, 0) // Shift the magenta plate in canvas pixels.
// @param float2 _YellowOffset = (0, 0) // Shift the yellow plate in canvas pixels.
// @param float2 _BlackOffset = (0, 0) // Shift the black plate in canvas pixels.
// @endif
// @if _Mode == 2
// @header(CMYK Screen Angles)
// @param color _PaperColor = (1, 1, 1, 1) // Paper tint; alpha is ignored.
// @param float _CyanAngle = 0 [~-180 .. ~180]
// @param float _MagentaAngle = 0 [~-180 .. ~180]
// @param float _YellowAngle = 0 [~-180 .. ~180]
// @param float _BlackAngle = 0 [~-180 .. ~180]
// @header(CMYK Plate Registration)
// @param float _PlateSpread = 0 [0 .. ~64] // Spacing between neighboring plates in canvas pixels.
// @param float _PlateRotation = 0 [~-180 .. ~180] // Rotate the automatic plate layout around its center.
// @endif
// @if _Mode == 3
// @header(CMYK Screen Angles)
// @param color _PaperColor = (1, 1, 1, 1) // Paper tint; alpha is ignored.
// @param float _CyanAngle = 0 [~-180 .. ~180]
// @param float _MagentaAngle = 0 [~-180 .. ~180]
// @param float _YellowAngle = 0 [~-180 .. ~180]
// @param float _BlackAngle = 0 [~-180 .. ~180]
// @header(CMYK Plate Registration)
// @param float _PlateSpread = 0 [0 .. ~64] // Spacing between neighboring plates in canvas pixels.
// @param float _PlateRotation = 0 [~-180 .. ~180] // Rotate the automatic plate layout around its center.
// @endif
// @if _Mode == 4
// @header(RGB Screen Angles)
// @param float _RedAngle = 0 [~-180 .. ~180]
// @param float _GreenAngle = 0 [~-180 .. ~180]
// @param float _BlueAngle = 0 [~-180 .. ~180]
// @header(RGB Plate Registration)
// @param float _PlateSpread = 0 [0 .. ~64] // Spacing between neighboring plates in canvas pixels.
// @param float _PlateRotation = 0 [~-180 .. ~180] // Rotate the automatic plate layout around its center.
// @endif
// @if _Mode == 5
// @header(RGB Screen Angles)
// @param float _RedAngle = 0 [~-180 .. ~180]
// @param float _GreenAngle = 0 [~-180 .. ~180]
// @param float _BlueAngle = 0 [~-180 .. ~180]
// @header(RGB Plate Registration)
// @param float2 _RedOffset = (0, 0) // Shift the red plate in canvas pixels.
// @param float2 _GreenOffset = (0, 0) // Shift the green plate in canvas pixels.
// @param float2 _BlueOffset = (0, 0) // Shift the blue plate in canvas pixels.
// @endif
// @endgroup

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
        float invertPattern = step(0.5001, _InvertPattern);
        float inkAmount = lerp(1.0 - luminance, luminance, invertPattern);
        float dots = HalftoneScreen(pixel, _Angle, inkAmount * density);
        float dotBlend = lerp(dots, 1.0 - dots, invertPattern);
        result.rgb = lerp(_PaperColor.rgb, _InkColor.rgb, dotBlend);
    }
    else if (_Mode > 3.5)
    {
        float2 redOffset;
        float2 greenOffset;
        float2 blueOffset;

        if (_Mode < 4.5)
        {
            // Keep the RGB plates centered as an equilateral triangle.
            redOffset = float2(0.0, -0.57735027) * _PlateSpread;
            greenOffset = float2(-0.5, 0.28867513) * _PlateSpread;
            blueOffset = float2(0.5, 0.28867513) * _PlateSpread;

            float sine, cosine;
            sincos(radians(_PlateRotation), sine, cosine);
            redOffset = float2(cosine * redOffset.x - sine * redOffset.y,
                               sine * redOffset.x + cosine * redOffset.y);
            greenOffset = float2(cosine * greenOffset.x - sine * greenOffset.y,
                                 sine * greenOffset.x + cosine * greenOffset.y);
            blueOffset = float2(cosine * blueOffset.x - sine * blueOffset.y,
                                sine * blueOffset.x + cosine * blueOffset.y);
        }
        else
        {
            redOffset = _RedOffset;
            greenOffset = _GreenOffset;
            blueOffset = _BlueOffset;
        }

        float red = SampleRegisteredPlate(uv, redOffset, color).r;
        float green = SampleRegisteredPlate(uv, greenOffset, color).g;
        float blue = SampleRegisteredPlate(uv, blueOffset, color).b;
        result.rgb = float3(
            HalftoneScreen(pixel, _RedAngle, red * density),
            HalftoneScreen(pixel, _GreenAngle, green * density),
            HalftoneScreen(pixel, _BlueAngle, blue * density));
    }
    else
    {
        float2 cyanOffset;
        float2 magentaOffset;
        float2 yellowOffset;
        float2 blackOffset;

        if (_Mode > 1.5 && _Mode < 2.5)
        {
            // Keep black centered; the CMY triangle's side length equals _PlateSpread.
            cyanOffset = float2(0.0, -0.57735027) * _PlateSpread;
            magentaOffset = float2(-0.5, 0.28867513) * _PlateSpread;
            yellowOffset = float2(0.5, 0.28867513) * _PlateSpread;
            blackOffset = 0.0;
        }
        else if (_Mode > 2.5)
        {
            // The distance between neighboring corners equals _PlateSpread.
            cyanOffset = float2(-0.5, -0.5) * _PlateSpread;
            magentaOffset = float2(0.5, -0.5) * _PlateSpread;
            yellowOffset = float2(-0.5, 0.5) * _PlateSpread;
            blackOffset = float2(0.5, 0.5) * _PlateSpread;
        }
        else
        {
            cyanOffset = _CyanOffset;
            magentaOffset = _MagentaOffset;
            yellowOffset = _YellowOffset;
            blackOffset = _BlackOffset;
        }

        if (_Mode > 1.5)
        {
            float sine, cosine;
            sincos(radians(_PlateRotation), sine, cosine);
            cyanOffset = float2(cosine * cyanOffset.x - sine * cyanOffset.y,
                                sine * cyanOffset.x + cosine * cyanOffset.y);
            magentaOffset = float2(cosine * magentaOffset.x - sine * magentaOffset.y,
                                   sine * magentaOffset.x + cosine * magentaOffset.y);
            yellowOffset = float2(cosine * yellowOffset.x - sine * yellowOffset.y,
                                  sine * yellowOffset.x + cosine * yellowOffset.y);
            blackOffset = float2(cosine * blackOffset.x - sine * blackOffset.y,
                                 sine * blackOffset.x + cosine * blackOffset.y);
        }

        float3 cyanSource = RGBToCMY(SampleRegisteredPlate(uv, cyanOffset, color));
        float3 magentaSource = RGBToCMY(SampleRegisteredPlate(uv, magentaOffset, color));
        float3 yellowSource = RGBToCMY(SampleRegisteredPlate(uv, yellowOffset, color));
        float3 blackRGB = saturate(SampleRegisteredPlate(uv, blackOffset, color));
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
