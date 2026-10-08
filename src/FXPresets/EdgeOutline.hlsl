// @whimtex-effect Stylization/Edge Outline
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1] // Blend with the FX result. Zero keeps the original image, including in Outline Only.
// @group(Detection; _Detection)
// @param hidden enum _Detection = Color {Color: 0, Luminance: 1, Hue: 2} // Find changes in color, brightness, or hue.
// @param enum _Method = Boundary {Boundary: 0, Scharr: 1} // Boundary draws uniform lines. Scharr makes weaker transitions more transparent.
// @if _Detection != 2
// @param float _Threshold = 0.1 [0 .. ~1] // Higher values keep only stronger transitions.
// @endif
// @if _Detection == 2
// @param label("Hue Threshold (°)") float _HueThreshold = 15 [0 .. 180] // Minimum hue change along the shortest path around the color wheel.
// @param float _MinSaturation = 0.1 [0 .. 1] // Ignore colors below this saturation. Gray and black are always excluded.
// @endif
// @if _Method == 1
// @param float _Strength = 1 [0 .. ~4] // Make detected Scharr lines more opaque. Zero removes them.
// @endif
// @endgroup
// @group(Contour; _Shape)
// @param hidden enum _Shape = Round {Round: 0, Square: 1, Diamond: 2} // Shape of contour joins and corners.
// @param label("Thickness (px)") float _Thickness = 2 [0 .. 32] // Total width, centered on the boundary. Zero removes the line.
// @param label("Softness (px)") float _Softness = 0 [0 .. 8] // Soften line edges. Zero keeps them crisp and antialiased.
// @endgroup
// @group(Output; _Output)
// @param hidden enum _Output = Overlay {Overlay: 0, OutlineOnly: 1} // Draw over the image or keep only contours on transparency.
// @param label(Color) color _OutlineColor = (0, 0, 0, 1) // Line color; alpha controls opacity. Source transparency is preserved.
// @endgroup

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float2 EdgeOutlineHueSaturation(float3 linearRgb)
{
    // Match the HSV preset's display-encoded, nonnegative RGB convention, including HDR.
    float3 rgb = SpriteEncode(max(linearRgb, 0.0));
    float value = max(rgb.r, max(rgb.g, rgb.b));
    float chroma = value - min(rgb.r, min(rgb.g, rgb.b));
    if (value <= 1e-5 || chroma <= 1e-5) return 0.0;
    float hue;
    if (value == rgb.r) hue = (rgb.g - rgb.b) / chroma;
    else if (value == rgb.g) hue = 2.0 + (rgb.b - rgb.r) / chroma;
    else hue = 4.0 + (rgb.r - rgb.g) / chroma;
    return float2(frac(hue / 6.0), saturate(chroma / value));
}

float4 EdgeOutlineSample(float2 pixel)
{
    // Explicit LOD keeps sampling defined inside the data-dependent search loop.
    return tex2Dlod(_MainTex, float4((pixel + 0.5) * _InputSize.zw, 0, 0));
}

float EdgeOutlineBoundary(float4 a, float4 b)
{
    // Invisible RGB and alpha-only silhouettes must not seed contours.
    if (min(a.a, b.a) <= 1e-5) return 0.0;
    if (_Detection >= 1.5)
    {
        float2 first = EdgeOutlineHueSaturation(a.rgb);
        float2 second = EdgeOutlineHueSaturation(b.rgb);
        float saturation = min(first.y, second.y);
        if (saturation <= 1e-5 || saturation < saturate(_MinSaturation)) return 0.0;
        float difference = abs(first.x - second.x);
        difference = min(difference, 1.0 - difference) * 360.0;
        return step(max(clamp(_HueThreshold, 0.0, 180.0), 0.001), difference);
    }
    float3 delta = a.rgb - b.rgb;
    float difference;
    if (_Detection < 0.5)
    {
        delta = abs(delta);
        difference = max(delta.r, max(delta.g, delta.b));
    }
    else difference = abs(dot(delta, float3(0.2126, 0.7152, 0.0722)));
    return step(max(_Threshold, 1e-5), difference);
}

float3 EdgeOutlineScharrSample(float2 pixel, float4 center, float2 centerHue)
{
    float4 neighbor = EdgeOutlineSample(clamp(pixel, 0.0, _InputSize.xy - 1.0));
    // Substitute the center signal for invisible/undefined neighbors, avoiding alpha-only edges.
    if (neighbor.a <= 1e-5) return 0.0;
    if (_Detection >= 1.5)
    {
        float2 hue = EdgeOutlineHueSaturation(neighbor.rgb);
        if (hue.y <= 1e-5 || hue.y < saturate(_MinSaturation)) return 0.0;
        // Unwrap each hue around this center; 180 degrees maps to unit magnitude.
        return float3((frac(hue.x - centerHue.x + 0.5) - 0.5) * 2.0, 0, 0);
    }
    float3 difference = neighbor.rgb - center.rgb;
    if (_Detection >= 0.5) return float3(dot(difference, float3(0.2126, 0.7152, 0.0722)), 0, 0);
    return difference;
}

float EdgeOutlineScharr(float2 pixel)
{
    float4 center = EdgeOutlineSample(pixel);
    if (center.a <= 1e-5) return 0.0;
    float2 hue = 0.0;
    if (_Detection >= 1.5)
    {
        hue = EdgeOutlineHueSaturation(center.rgb);
        if (hue.y <= 1e-5 || hue.y < saturate(_MinSaturation)) return 0.0;
    }
    float3 a = EdgeOutlineScharrSample(pixel + float2(-1, -1), center, hue);
    float3 b = EdgeOutlineScharrSample(pixel + float2( 0, -1), center, hue);
    float3 c = EdgeOutlineScharrSample(pixel + float2( 1, -1), center, hue);
    float3 d = EdgeOutlineScharrSample(pixel + float2(-1,  0), center, hue);
    float3 f = EdgeOutlineScharrSample(pixel + float2( 1,  0), center, hue);
    float3 g = EdgeOutlineScharrSample(pixel + float2(-1,  1), center, hue);
    float3 h = EdgeOutlineScharrSample(pixel + float2( 0,  1), center, hue);
    float3 i = EdgeOutlineScharrSample(pixel + float2( 1,  1), center, hue);
    // True 3x3 Scharr kernels. A unit horizontal/vertical step has magnitude one.
    float3 dx = (3.0 * (c - a + i - g) + 10.0 * (f - d)) / 16.0;
    float3 dy = (3.0 * (g - a + i - c) + 10.0 * (h - b)) / 16.0;
    float3 response = sqrt(dx * dx + dy * dy);
    float gradient = max(response.r, max(response.g, response.b));
    float threshold = _Detection >= 1.5 ? max(clamp(_HueThreshold, 0.0, 180.0), 0.001) / 180.0 : max(_Threshold, 1e-5);
    return step(threshold, gradient) * saturate(gradient * max(_Strength, 0.0));
}

float EdgeOutlineDistance(float2 delta)
{
    if (_Shape < 0.5) return length(delta);
    if (_Shape < 1.5) return max(delta.x, delta.y);
    return delta.x + delta.y;
}

float EdgeOutlineCoverage(float2 delta, float radius, float transition)
{
    return saturate((radius - EdgeOutlineDistance(delta)) / transition + 0.5);
}

float EdgeOutlineMask(float2 uv)
{
    // Find adjacent-texel discontinuities and expand their boundary segments.
    // A dense neighborhood retains small features between sparse radial taps.
    float2 texelPixels = _CanvasSize.xy * _InputSize.zw;
    float transition = max(clamp(_Softness, 0.0, 8.0), max(texelPixels.x, texelPixels.y));
    float radius = clamp(_Thickness, 0.0, 32.0) * 0.5;
    float2 pixel = uv * _InputSize.xy - 0.5;
    float2 origin = floor(pixel + 0.5);
    int2 extent = (int2)ceil((radius + transition * 0.5) / texelPixels + 0.5);
    float mask = 0.0;

    [loop] for (int y = -extent.y; y <= extent.y; y++)
    {
        [loop] for (int x = -extent.x; x <= extent.x; x++)
        {
            float2 p = origin + float2(x, y);
            if (any(p < 0.0) || any(p >= _InputSize.xy)) continue;
            float2 delta = (p - pixel) * texelPixels;
            if (_Method >= 0.5)
            {
                // The 3x3 derivative spans both sides of a step. Account for its base footprint
                // before expansion; never stretch the derivative taps to imitate thicker lines.
                float coverage = EdgeOutlineCoverage(abs(delta), radius - max(texelPixels.x, texelPixels.y) * 0.5, transition);
                if (coverage > mask) mask = max(mask, coverage * EdgeOutlineScharr(p));
                if (mask >= 1.0) return 1.0;
                continue;
            }
            // Horizontal color pairs define vertical segments; vertical pairs define horizontal ones.
            float2 verticalDistance = float2(abs(delta.x + texelPixels.x * 0.5),
                max(abs(delta.y) - texelPixels.y * 0.5, 0.0));
            float2 horizontalDistance = float2(max(abs(delta.x) - texelPixels.x * 0.5, 0.0),
                abs(delta.y + texelPixels.y * 0.5));
            float vertical = EdgeOutlineCoverage(verticalDistance, radius, transition);
            float horizontal = EdgeOutlineCoverage(horizontalDistance, radius, transition);
            if (max(vertical, horizontal) <= mask) continue;

            float4 center = EdgeOutlineSample(p);
            if (vertical > mask && p.x + 1.0 < _InputSize.x)
                mask = max(mask, vertical * EdgeOutlineBoundary(center, EdgeOutlineSample(p + float2(1, 0))));
            if (horizontal > mask && p.y + 1.0 < _InputSize.y)
                mask = max(mask, horizontal * EdgeOutlineBoundary(center, EdgeOutlineSample(p + float2(0, 1))));
            if (mask >= 1.0) return 1.0;
        }
    }
    return mask;
}

float4 ApplyFX(float2 uv, float4 color)
{
    float amount = saturate(_Opacity);
    if (amount <= 0.0) return color;
    float mask = 0.0;
    if (_Thickness > 0.0 && color.a > 1e-5 && _OutlineColor.a > 0.0 && (_Method < 0.5 || _Strength > 0.0))
        mask = EdgeOutlineMask(uv) * saturate(_OutlineColor.a);
    float4 result = _Output > 0.5
        ? float4(_OutlineColor.rgb, color.a * mask)
        : float4(lerp(color.rgb, _OutlineColor.rgb, mask), color.a);
    return lerp(color, result, amount);
}
