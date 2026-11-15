// Shared coordinate convention and bounded height search for native and VM previews.
bool GraphParallaxFinite(float value) { return (asuint(value) & 0x7fffffffU) < 0x7f800000U; }
bool GraphParallaxFinite(float2 value) { return all((asuint(value) & 0x7fffffffU) < 0x7f800000U); }
bool GraphParallaxFinite(float3 value) { return all((asuint(value) & 0x7fffffffU) < 0x7f800000U); }

float2 GraphParallaxOrigin(float2 uv) { return GraphParallaxFinite(uv) ? uv : 0; }

float2 GraphParallaxRay(float3 view, float amplitude, bool occlusion)
{
    if (!GraphParallaxFinite(view) || !GraphParallaxFinite(amplitude)) return 0;
    // Scale before squaring so finite, very large directions cannot overflow.
    view /= max(1, max(max(abs(view.x), abs(view.y)), abs(view.z)));
    view *= rsqrt(max(dot(view, view), 1e-12));
    float divisor = occlusion ? max(view.z, 0.05) : max(view.z + 0.42, 0.42);
    float2 ray = amplitude * view.xy / divisor;
    return view.z > 0 && GraphParallaxFinite(ray) ? ray : 0;
}

float2 GraphParallaxDisplace(float2 uv, float2 ray, float height)
{
    uv = GraphParallaxOrigin(uv);
    height = GraphParallaxFinite(height) ? height : 0.5;
    float2 displaced = uv + ray * (height - 0.5);
    return GraphParallaxFinite(displaced) ? displaced : uv;
}

float2 GraphParallaxOffset(float2 uv, float3 view, float amplitude, float height)
{
    return GraphParallaxDisplace(uv, GraphParallaxRay(view, amplitude, false), height);
}

struct GraphParallaxTrace
{
    float2 origin, ray;
    float depth, step, high, highDelta, low, lowDelta;
    int remaining, refinements;
};

GraphParallaxTrace GraphParallaxBegin(float2 uv, float3 view, float amplitude, float steps)
{
    GraphParallaxTrace t;
    t.origin = GraphParallaxOrigin(uv);
    t.ray = GraphParallaxRay(view, amplitude, true);
    steps = GraphParallaxFinite(steps) ? clamp(steps, 1.0, 64.0) : 1.0;
    int requested = (int)floor(steps + 0.5);
    // Use a 1/64-UV coarse stride as a quality floor, up to the existing 64-step cap.
    // This is independent of image resolution; Steps still controls finer height detail.
    // Clamp before squaring so very large finite displacements remain safe.
    float longest = max(abs(t.ray.x), abs(t.ray.y));
    float distance = min(longest, 1.0) * length(t.ray / max(longest, 1e-20));
    int distanceSteps = (int)ceil(saturate(distance) * 64.0);
    t.remaining = max(requested, distanceSteps);
    // Move one refinement sample into the denser coarse search when it is needed.
    t.refinements = t.remaining > requested ? 2 : 3;
    t.step = 1.0 / t.remaining;
    t.depth = 1.0; t.high = 1.0; t.highDelta = 0.0;
    t.low = 0.0; t.lowDelta = 0.0;
    return t;
}

float2 GraphParallaxUV(GraphParallaxTrace t) { return GraphParallaxDisplace(t.origin, t.ray, t.depth); }

bool GraphParallaxAdvance(inout GraphParallaxTrace t, float height)
{
    height = GraphParallaxFinite(height) ? saturate(height) : 0.5;
    float delta = t.depth - height;
    if (delta > 0 && t.remaining > 0)
    {
        t.high = t.depth; t.highDelta = delta;
        t.remaining--;
        t.depth = t.remaining * t.step;
        return true;
    }
    t.remaining = -1; // Fix the coarse bracket; subsequent samples only refine it.
    // An exact hit needs no interpolation, including constant zero/one fields.
    if (abs(delta) < 1e-7) return false;
    if (delta > 0) { t.high = t.depth; t.highDelta = delta; }
    else { t.low = t.depth; t.lowDelta = delta; }
    float denominator = t.highDelta - t.lowDelta;
    t.depth = lerp(t.low, t.high, denominator > 1e-8 ? saturate(-t.lowDelta / max(denominator, 1e-8)) : 0.5);
    return t.refinements-- > 0;
}
