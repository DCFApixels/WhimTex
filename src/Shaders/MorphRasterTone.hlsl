// Split endpoint appearance into an edge-aware RGB base and a signed detail residual.
// The fourth statistics channel is the local luminance deviation in scene-linear units.
Texture2D<float4> _ToneGuide, _ToneA, _ToneB, _SurfaceAgreement;
float4 _ToneSize;
int _MaskBlend;

float ToneLuma(float3 rgb) { return dot(rgb, float3(.2126, .7152, .0722)); }

float VisibilityBlend(float time, float visibility)
{
    float exponent = 1 + 2 * abs(visibility);
    float wa = pow(1 - time, exponent);
    float wb = pow(time, exponent) * exp2(5 * visibility);
    return wb / (wa + wb);
}

float4 FilterTone(float2 uv, bool guided)
{
    float3 center = _ToneGuide.SampleLevel(sampler_linear_clamp, uv, 0).rgb;
    float2 spacing = max(1., min(_ToneSize.x, _ToneSize.y) / 180.) * _ToneSize.zw;
    float3 sum = 0;
    float weight = 0, secondMoment = 0;
    [loop] for (int y = -2; y <= 2; ++y)
    {
        [loop] for (int x = -2; x <= 2; ++x)
        {
            float2 d = float2(x,y), q = uv + spacing * d;
            float4 sampleColor = _MainTex.SampleLevel(sampler_linear_clamp, q, 0);
            float w = exp2(-.32 * dot(d,d)) * saturate(sampleColor.a);
            if (guided)
            {
                float3 guide = _ToneGuide.SampleLevel(sampler_linear_clamp, q, 0).rgb;
                float3 distance = (guide - center) / (.065 + .28 * max(abs(guide), abs(center)));
                w *= exp2(-dot(distance, distance));
            }
            sum += w * sampleColor.rgb;
            float luma = ToneLuma(sampleColor.rgb);
            secondMoment += w * luma * luma;
            weight += w;
        }
    }
    float inv = rcp(max(weight, 1e-8));
    float3 mean = sum * inv;
    float luma = ToneLuma(mean);
    return float4(mean, sqrt(max(0, secondMoment * inv - luma * luma)));
}

float4 ToneGuide(Varyings input) : SV_Target { return FilterTone(input.pos.xy * _ToneSize.zw, false); }
float4 ToneStatistics(Varyings input) : SV_Target { return FilterTone(input.pos.xy * _ToneSize.zw, true); }

// Compare the shape of the two warped luminance patches, allowing a local exposure change.
// When an ownership vote only follows a narrow contour it can imprint a light or dark
// stripe on flat color. Across a lasting ownership transition at least one side still
// supports the center vote. Store the supported fraction for tone blending at any time.
float SupportedToneVote(float2 uv)
{
    float vote = _Flow.SampleLevel(sampler_linear_clamp, uv, 0).z;
    if (abs(vote) < .0001) return 1;
    const float2 axes[4] = { float2(1,0), float2(0,1), float2(.7071,.7071), float2(.7071,-.7071) };
    float2 radius = max(2., min(_Level.x, _Level.y) * .04) * _Level.zw;
    float supported = abs(vote);
    [unroll] for (int axis = 0; axis < 4; ++axis)
    {
        float2 d = axes[axis] * radius;
        float left = _Flow.SampleLevel(sampler_linear_clamp, uv - d, 0).z * sign(vote);
        float right = _Flow.SampleLevel(sampler_linear_clamp, uv + d, 0).z * sign(vote);
        supported = min(supported, max(left, right));
    }
    return saturate(supported / abs(vote));
}

float4 SurfaceAgreement(Varyings input) : SV_Target
{
    float2 uv = input.pos.xy * _Level.zw;
    float2 mean = 0;
    float3 moments = 0;
    float total = 0;
    [loop] for (int y = -2; y <= 2; ++y)
    {
        [loop] for (int x = -2; x <= 2; ++x)
        {
            float2 d = float2(x, y), q = uv + d * _Level.zw;
            float2 halfFlow = .5 * _Flow.SampleLevel(sampler_linear_clamp, q, 0).xy;
            float2 qa = q - halfFlow, qb = q + halfFlow;
            if (any(abs(qa - .5) > .5) || any(abs(qb - .5) > .5)) continue;
            float4 a = _MainTex.SampleLevel(sampler_linear_clamp, qa, 0);
            float4 b = _ToTex.SampleLevel(sampler_linear_clamp, qb, 0);
            float w = exp2(-.32 * dot(d,d)) * min(saturate(a.a), saturate(b.a));
            float2 value = float2(ToneLuma(a.rgb), ToneLuma(b.rgb));
            mean += w * value;
            moments += w * value.xxy * value.xyy;
            total += w;
        }
    }
    float inv = rcp(max(total, 1e-8));
    mean *= inv;
    float3 cov = moments * inv - mean.xxy * mean.xyy;
    float2 deviation = sqrt(max(0, cov.xz));
    float corr = cov.y / max(1e-9, deviation.x * deviation.y);
    float2 contrast = deviation / (.04 + abs(mean));
    float agreement = smoothstep(.08, .68, corr) * smoothstep(.005, .045, min(contrast.x, contrast.y))
        * smoothstep(1, 5, total);
    return float4(agreement, SupportedToneVote(uv), 0, 0);
}

float3 ToneCorrection(float4 a, float4 b, float2 pa, float2 pb, float2 halfway, float blend, float visibility)
{
    float coverage = (1 - smoothstep(.005, .08, abs(a.a - b.a))) * smoothstep(.01, .15, min(a.a, b.a));
    if (coverage <= 0) return 0;
    float2 support = _SurfaceAgreement.SampleLevel(sampler_linear_clamp, halfway, 0).rg;
    float regionBlend = VisibilityBlend(_MorphTime, visibility * support.y);
    float4 ta = _ToneA.SampleLevel(sampler_linear_clamp, (pa + .5) / _RasterSize, 0);
    float4 tb = _ToneB.SampleLevel(sampler_linear_clamp, (pb + .5) / _RasterSize, 0);
    float3 ca = a.rgb / max(a.a, 1e-8), cb = b.rgb / max(b.a, 1e-8);
    // Unrelated surfaces cannot exchange signed detail without inventing colored rims.
    // Blend their actual samples with the regional ownership weight instead.
    float3 correction = lerp((regionBlend - blend) * (cb - ca),
        (_MorphTime - blend) * (tb.rgb - ta.rgb), support.x);
    if (_MaskBlend == 2)
    {
        float3 da = ca - ta.rgb;
        float3 db = cb - tb.rgb;
        float target = lerp(ta.a, tb.a, _MorphTime);
        float floorValue = .003 + .02 * target;
        float2 gain = clamp((target + floorValue) / (float2(ta.a, tb.a) + floorValue), .5, 2);
        correction += support.x * smoothstep(0, .12, abs(blend - _MorphTime))
            * lerp(da * (gain.x - 1), db * (gain.y - 1), blend);
    }
    return coverage * correction;
}
