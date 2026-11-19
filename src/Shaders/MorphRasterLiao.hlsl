// Halfway-domain correspondence, after Liao et al., "Automating Image Morphing
// Using Structural Similarity on a Halfway Domain" (2014). Displacements in the
// texture span the entire transition in UV units. The solver varies half of
// that displacement in level texels, so both endpoint maps use identical units.
Texture2D<float4> _LiaoStats, _LiaoForce, _LiaoPins;
int _LiaoParity, _LiaoGuided;

int2 LiaoPixel(int2 p)
{
    return clamp(p, 0, (int2)_Level.xy - 1);
}

// Seed the local optimizer with a single translation and uniform magnification.
// Each low-resolution hypothesis compares the same halfway samples. Flat or
// ambiguous pairs prefer the silhouette translation and unit magnification.
float2 LiaoSignals(float2 uv, float2 halfMotion);

float4 LiaoSeedScore(Varyings input) : SV_Target
{
    uint2 cell = (uint2)input.pos.xy;
    float2 offset = (float2(cell.x % 9u, cell.y) - 4) * _Level.zw;
    float zoom = ((int)(cell.x / 9u) - 2) * .09;
    float2 translation = _Flow.Load(int3(0, 0, 0)).xy / _RasterSize + offset;
    float4 sums = 0;
    float product = 0;
    float gradProduct = 0, gradEnergy = 0;
    [loop] for (int y = 0; y < (int)_Level.y; y++)
    {
        [loop] for (int x = 0; x < (int)_Level.x; x++)
        {
            float2 uv = (float2(x,y) + .5) * _Level.zw;
            float2 v = .5 * translation + zoom * (uv - .5);
            float2 signals = LiaoSignals(uv, v);
            sums += float4(signals, signals * signals);
            product += signals.x * signals.y;
            float2 dx = LiaoSignals(uv + float2(_Level.z, 0), v) - LiaoSignals(uv - float2(_Level.z, 0), v);
            float2 dy = LiaoSignals(uv + float2(0, _Level.w), v) - LiaoSignals(uv - float2(0, _Level.w), v);
            gradProduct += dx.x * dx.y + dy.x * dy.y;
            gradEnergy += dot(dx, dx) + dot(dy, dy);
        }
    }
    float count = _Level.x * _Level.y;
    float covariance = product - sums.x * sums.y / count;
    float variance = max(0, sums.z + sums.w - dot(sums.xy, sums.xy) / count);
    float similarity = 2 * abs(covariance) / (variance + .0009 * count);
    similarity += 2 * abs(gradProduct) / (gradEnergy + .0009 * count);
    float cost = .012 * (dot(offset, offset) * 16 + zoom * zoom * 8);
    return float4(translation, zoom, similarity - cost);
}

float4 LiaoBestSeed(Varyings input) : SV_Target
{
    float4 best = _MainTex.Load(int3(22, 4, 0));
    [loop] for (int y = 0; y < 9; y++)
    {
        [loop] for (int x = 0; x < 45; x++)
        {
            float4 value = _MainTex.Load(int3(x, y, 0));
            if (value.w > best.w) best = value;
        }
    }
    return float4(best.xyz, _Flow.Load(int3(0, 0, 0)).w);
}

float4 LiaoInitialize(Varyings input) : SV_Target
{
    if (_Coarsest != 0)
    {
        float4 translation = _Flow.Load(int3(0, 0, 0));
        return float4(translation.xy + 2 * translation.z * (input.pos.xy * _Level.zw - .5), 0, translation.w);
    }
    return _Flow.SampleLevel(sampler_linear_clamp, input.pos.xy * _Level.zw, 0);
}

float4 LiaoConstraints(Varyings input) : SV_Target
{
    float2 target = 0;
    float total = 0;
    [loop] for (int pin = 0; pin < _AnchorCount; pin++)
    {
        float4 pair = _AnchorTable.Load(int3((uint)pin % (uint)_AnchorWidth, (uint)pin / (uint)_AnchorWidth, 0));
        float2 midpoint = .5 * (pair.xy + pair.zw) * _Level.xy;
        float2 weight = saturate(1 - abs(input.pos.xy - midpoint));
        float w = weight.x * weight.y;
        target += w * .5 * (pair.zw - pair.xy) * _Level.xy;
        total += w;
    }
    return float4(target / max(1e-8, total), total, 0);
}

// Mix visible color detail and the silhouette for the scalar matching signal.
// The final renderer always samples the original straight HDR textures.
float2 LiaoSignals(float2 uv, float2 halfMotion)
{
    float2 a = _MainTex.SampleLevel(sampler_linear_clamp, uv - halfMotion, 0).xy;
    float2 b = _MainTex.SampleLevel(sampler_linear_clamp, uv + halfMotion, 0).zw;
    // A transparent sprite that touches an edge must not grow a strip of
    // opaque pixels outside the canvas during matching.
    if (_Flow.Load(int3(0, 0, 0)).w < .5)
    {
        float4 coords = float4(uv - halfMotion, uv + halfMotion);
        float4 coverage = saturate(coords * _Level.xyxy + .5) * saturate((1 - coords) * _Level.xyxy + .5);
        a *= coverage.x * coverage.y;
        b *= coverage.z * coverage.w;
    }
    return .5 * float2(a.x + a.y, b.x + b.y);
}

float4 LiaoWarp(Varyings input) : SV_Target
{
    float2 v = .5 * _Flow.Load(int3((int2)input.pos.xy, 0)).xy;
    return float4(LiaoSignals(input.pos.xy * _Level.zw, v), 0, 0);
}

float4 LiaoCoefficients(Varyings input) : SV_Target
{
    int2 p = (int2)input.pos.xy;
    float2 mean = 0, second = 0;
    float product = 0;
    [loop] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            float2 ab = _MainTex.Load(int3(LiaoPixel(p + int2(x, y)), 0)).xy;
            mean += ab;
            second += ab * ab;
            product += ab.x * ab.y;
        }
    }
    mean /= 25;
    float2 variance = max(0, second / 25 - mean * mean);
    float covariance = product / 25 - mean.x * mean.y;
    const float stabilizer = .0009;
    float denominator = variance.x + variance.y + stabilizer;
    float sim = (2 * abs(covariance) + stabilizer) / denominator;
    float k = 2 * sign(covariance) / denominator;
    float c = 2 * sim / denominator;
    return float4(k, c, c * mean - k * mean.yx);
}

float4 LiaoForce(Varyings input) : SV_Target
{
    int2 p = (int2)input.pos.xy;
    float4 coeff = 0;
    // A texel contributes to every 5x5 window containing it.
    [loop] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
            coeff += _LiaoStats.Load(int3(LiaoPixel(p + int2(x, y)), 0));
    }
    coeff /= 25;
    float2 uv = input.pos.xy * _Level.zw;
    float2 v = .5 * _Flow.Load(int3(p, 0)).xy;
    float2 ab = LiaoSignals(uv, v);
    float2 slope = coeff.x * ab.yx - coeff.y * ab + coeff.zw;
    slope.x = -slope.x;
    float2 dx = .5 * (LiaoSignals(uv + float2(_Level.z, 0), v) - LiaoSignals(uv - float2(_Level.z, 0), v));
    float2 dy = .5 * (LiaoSignals(uv + float2(0, _Level.w), v) - LiaoSignals(uv - float2(0, _Level.w), v));
    float curvature = abs(coeff.y) * (dot(dx, dx) + dot(dy, dy));
    return float4(dot(slope, dx), dot(slope, dy), curvature, 0);
}

float2 LiaoV(int2 p)
{
    return _MainTex.Load(int3(LiaoPixel(p), 0)).xy * (.5 * _Level.xy);
}

float LiaoCross(float2 a, float2 b)
{
    return a.x * b.y - a.y * b.x;
}

// Oriented area is linear in the motion of a single vertex. Check both
// endpoint maps, preserving a small positive area for the incident triangles.
float LiaoAreaLimit(float2 a, float2 b, float2 step)
{
    float area = LiaoCross(a, b);
    float loss = LiaoCross(step, b - a);
    if (loss <= 0) return 1;
    return saturate((area - .20) / max(loss, 1e-8));
}

float4 LiaoRelax(Varyings input) : SV_Target
{
    int2 p = (int2)input.pos.xy;
    float4 prior = _MainTex.Load(int3(p, 0));
    if ((p.x & 1) + 2 * (p.y & 1) != _LiaoParity) return prior;
    float2 v = prior.xy * (.5 * _Level.xy);
    const int2 axes[4] = { int2(1,0), int2(0,1), int2(-1,0), int2(0,-1) };
    float2 axial[4], diagonal[4];
    float2 bending = 20 * v;
    [unroll] for (int i = 0; i < 4; i++)
    {
        int2 axis = axes[i];
        int2 corner = axis + axes[(i + 1) & 3];
        axial[i] = LiaoV(p + axis) - v;
        diagonal[i] = LiaoV(p + corner) - v;
        bending += -8 * (v + axial[i]) + 2 * (v + diagonal[i]) + LiaoV(p + 2 * axis);
    }
    float4 data = _LiaoForce.Load(int3(p, 0));
    float4 guide = 0;
    if (_LiaoGuided != 0) guide = _LiaoPins.Load(int3(p, 0));
    const float rigidity = .12;
    float pinWeight = 24 * guide.z;
    float2 step = (data.xy - rigidity * bending + pinWeight * (guide.xy - v))
        / (.025 + data.z + 20 * rigidity + pinWeight);
    step *= min(1, .45 / max(length(step), 1e-8));
    float limit = 1;
    [unroll] for (int j = 0; j < 4; j++)
    {
        int next = (j + 1) & 3;
        int2 corner = axes[j] + axes[next];
        if (any(p + corner < 0) || any(p + corner >= (int2)_Level.xy)) continue;
        [unroll] for (int signIndex = 0; signIndex < 2; signIndex++)
        {
            float s = signIndex == 0 ? -1 : 1;
            float2 a = axes[j] + s * axial[j];
            float2 b = axes[next] + s * axial[next];
            float2 c = corner + s * diagonal[j];
            limit = min(limit, LiaoAreaLimit(a, b, s * step));
            limit = min(limit, LiaoAreaLimit(a, c, s * step));
            limit = min(limit, LiaoAreaLimit(c, b, s * step));
        }
    }
    return float4(prior.xy + 2 * limit * step * _Level.zw, 0, prior.w);
}
