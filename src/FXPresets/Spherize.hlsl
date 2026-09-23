// @whimtex-effect Distortion/Spherize
// @param enum _Mode = 0 {Classic: 0, Sphere: 1} // Classic is the current unbounded radial distortion. Sphere wraps the image over a sphere and clips outside its circular edge.
// @param float _Strength = 0.5 [~-1 .. ~1]
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    float2 p = (_Area_ToLocal(uv) - 0.5) * 2.0;
    float4 result = color;

    if (_Mode < 0.5)
    {
        if (_Strength != 0)
        {
            float exponent = exp2(_Strength);
            // The epsilon only protects the center; the radius is not limited to the frame.
            float scale = pow(max(dot(p, p), 1e-12), 0.5 * (exponent - 1.0));
            result = SampleInput(_Area_ToInput(0.5 + p * scale * 0.5));
        }
    }
    else
    {
        float radius = length(p);
        float clampedRadius = saturate(radius);
        // A sphere maps the disk to its angular surface coordinates. Positive values bulge;
        // negative values use the inverse mapping and pinch toward the center.
        float sphericalRadius = _Strength >= 0.0
            ? asin(clampedRadius) * 0.6366197723675814
            : sin(clampedRadius * 1.5707963267948966);
        float strength = abs(_Strength);
        float sampleRadius = lerp(radius, sphericalRadius, min(strength, 1.0));
        float excessStrength = max(strength - 1.0, 0.0);
        // Beyond one, continue strengthening smoothly without clamping the soft UI range.
        if (_Strength >= 0.0)
            sampleRadius *= exp2(-excessStrength * (1.0 - sampleRadius));
        else
            sampleRadius = 1.0 - (1.0 - sampleRadius) * exp2(-excessStrength * sampleRadius);
        float radialScale = sampleRadius / max(radius, 1e-6);
        result = SampleInput(_Area_ToInput(0.5 + p * radialScale * 0.5));

        // Keep a crisp circular silhouette with a sub-pixel antialiased edge.
        float edgeWidth = max(fwidth(radius), 1e-4);
        result.a *= 1.0 - smoothstep(1.0 - edgeWidth, 1.0 + edgeWidth, radius);
    }

    return result;
}
