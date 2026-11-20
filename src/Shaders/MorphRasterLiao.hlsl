// Halfway-domain correspondence, after Liao et al., "Automating Image Morphing
// Using Structural Similarity on a Halfway Domain" (2014). Displacements in the
// texture span the entire transition in UV units. The solver varies half of
// that displacement in level texels, so both endpoint maps use identical units.
Texture2D<float4> _LiaoStats, _LiaoForce, _LiaoPins, _LiaoWarped;
int _LiaoParity, _LiaoGuided;
int _LiaoSeedRefine;
int _LiaoGuideFrame;
float3 _LiaoGuideX, _LiaoGuideY;
Texture2D<float4> _LiaoPrior;
Texture2D<float4> _LiaoDetail;
int _LiaoHasDetail;
float2 _LiaoSeedStep;

int2 LiaoPixel(int2 p)
{
    return clamp(p, 0, (int2)_Level.xy - 1);
}

// Search translations and uniform scales, then refine translation and each axis scale.
// Each low-resolution hypothesis compares the same halfway samples. Flat or
// ambiguous pairs prefer the silhouette translation and unit magnification.
float2 LiaoSignals(float2 uv, float2 halfMotion);

float4 LiaoHypothesis(uint2 cell)
{
    float2 offset = (float2(cell.x % 9u, cell.y) - 4) * _LiaoSeedStep;
    float zoom = ((int)(cell.x / 9u) - 2) * .09;
    float2 translation = _Flow.Load(int3(0, 0, 0)).xy / _RasterSize + offset;
    float4 candidate = float4(translation, zoom, zoom);
    if (_LiaoSeedRefine == 2)
        candidate = cell.x == 9 ? _LiaoPrior.Load(int3(0, 0, 0)) : float4((float2(cell) - 4) * _Level.zw, 0, 0);
    else if (_LiaoSeedRefine == 1)
    {
        float4 prior = _LiaoPrior.Load(int3(0, 0, 0));
        float2 delta = (float2(cell % 5u) - 2) * (.25 * _LiaoSeedStep);
        float2 stretch = (float2(cell / 5u) - 1) * .045;
        candidate = prior + float4(delta, stretch);
    }
    return candidate;
}

float LiaoSeedScore(Varyings input) : SV_Target
{
    float4 candidate = LiaoHypothesis((uint2)input.pos.xy);
    if (_LiaoSeedRefine == 2)
    {
        // Compare translations at integer feature texels, symmetrically over
        // the same interior crop. Reject alternatives without a near-exact fit.
        float2 mean = 0, energy = 0;
        float crossMoment = 0;
        float count = 0;
        [loop] for (int y = 3; y < (int)_Level.y - 3; y++)
        {
            [loop] for (int x = 3; x < (int)_Level.x - 3; x++)
            {
                float2 uv = (float2(x,y) + .5) * _Level.zw;
                float2 shift = .5 * candidate.xy + candidate.zw * (uv - .5);
                float2 ab = LiaoSignals(uv, shift);
                mean += ab;
                energy += ab * ab;
                crossMoment += ab.x * ab.y;
                count++;
            }
        }
        mean /= max(1,count);
        float variance = dot(max(0, energy / max(1,count) - mean * mean), float2(1,1));
        float diff = max(0, (energy.x + energy.y - 2*crossMoment) / max(1,count) - (mean.x-mean.y)*(mean.x-mean.y));
        float score = 1 - diff / max(.00001, variance);
        bool original = (uint)input.pos.x == 9;
        if (variance < .00005 || score < .985) return original ? 0 : -1;
        return score - .0001 * length(candidate.xy * _Level.xy);
    }
    float4 sums = 0;
    float product = 0;
    float gradientMatch = 0;
    [loop] for (int y = 0; y < (int)_Level.y; y++)
    {
        [loop] for (int x = 0; x < (int)_Level.x; x++)
        {
            float2 uv = (float2(x,y) + .5) * _Level.zw;
            float2 v = .5 * candidate.xy + candidate.zw * (uv - .5);
            float2 signals = LiaoSignals(uv, v);
            sums += float4(signals, signals * signals);
            product += signals.x * signals.y;
            float2 dx = LiaoSignals(uv + float2(_Level.z, 0), v) - LiaoSignals(uv - float2(_Level.z, 0), v);
            float2 dy = LiaoSignals(uv + float2(0, _Level.w), v) - LiaoSignals(uv - float2(0, _Level.w), v);
            gradientMatch += 2 * (dx.x * dx.y + dy.x * dy.y) / (.002 + dot(dx, dx) + dot(dy, dy));
        }
    }
    float count = _Level.x * _Level.y;
    float covariance = product - sums.x * sums.y / count;
    float variance = max(0, sums.z + sums.w - dot(sums.xy, sums.xy) / count);
    // A periodic texture can also match half a cycle away with its contrast
    // inverted. Prefer like-polarity matches while retaining global inversions.
    float similarity = 2 * max(covariance, -.65 * covariance) / (variance + .0009 * count);
    similarity += 3 * max(gradientMatch, -.65 * gradientMatch) / count;
    float2 offset = candidate.xy - _Flow.Load(int3(0, 0, 0)).xy / _RasterSize;
    return similarity - .012 * (16 * dot(offset, offset) + 4 * dot(candidate.zw, candidate.zw));
}

float4 LiaoBestSeed(Varyings input) : SV_Target
{
    uint width, height;
    _MainTex.GetDimensions(width, height);
    uint2 winner = _LiaoSeedRefine == 2 ? uint2(9,4) : uint2(width, height) / 2u;
    float best = _MainTex.Load(int3(winner, 0)).x;
    [loop] for (uint y = 0; y < height; y++)
    {
        [loop] for (uint x = 0; x < width; x++)
        {
            float value = _MainTex.Load(int3(x, y, 0)).x;
            if (value > best) { best = value; winner = uint2(x, y); }
        }
    }
    return LiaoHypothesis(winner);
}

float2 LiaoInherited(float2 uv)
{
    float2 inherited = 0;
    if (_Coarsest == 0) inherited = _LiaoPrior.SampleLevel(sampler_linear_clamp, uv, 0).xy;
    else if (_LiaoGuideFrame != 0)
    {
        float3 p = float3(uv, 1);
        inherited = float2(dot(_LiaoGuideX, p), dot(_LiaoGuideY, p));
    }
    else
    {
        float4 seed = _LiaoPrior.Load(int3(0, 0, 0));
        inherited = seed.xy + 2 * seed.zw * (uv - .5);
    }
    return inherited;
}

float4 LiaoInitialize(Varyings input) : SV_Target
{
    if (_Coarsest != 0)
    {
        return float4(LiaoInherited(input.pos.xy * _Level.zw), 0, _Flow.Load(int3(0, 0, 0)).w);
    }
    return _Flow.SampleLevel(sampler_linear_clamp, input.pos.xy * _Level.zw, 0);
}

Texture2D<float4> _BoundaryLinks;
int _HasBoundary;

float LiaoLink(float side, float slope, float alongStart, float alongStep, float edgeLengthSq)
{
    if (abs(slope) < 1e-5) return 1;
    float t = -side / slope;
    float along = alongStart + t * alongStep;
    return t >= 0 && t <= 1 && along >= 0 && along <= edgeLengthSq ? .04 : 1;
}

// Per pyramid level, bake links cut by each flattened halfway curve. Opposite
// pixels evaluate the same crossing; open ends leave room to move around them.
float4 LiaoBoundaryLinks(Varyings input) : SV_Target
{
    float4 links = 1;
    [loop] for (int i = 0; i < _SegmentCount; i++)
    {
        if (GuideOptions(i).z == 0) continue;
        float4 a, b;
        float t;
        float d2 = ProjectSegment(i, input.pos.xy, a, b, t);
        if (d2 > 1.001) continue;
        float2 origin = .5 * (a.xy + a.zw) * _Level.xy;
        float2 edge = .5 * (b.xy + b.zw) * _Level.xy - origin;
        float2 normal = float2(-edge.y, edge.x);
        float side = dot(input.pos.xy - origin, normal);
        float along = dot(input.pos.xy - origin, edge), lenSq = dot(edge, edge);
        links = min(links, float4(LiaoLink(side, normal.x, along, edge.x, lenSq),
            LiaoLink(side, normal.y, along, edge.y, lenSq),
            LiaoLink(side, -normal.x, along, -edge.x, lenSq),
            LiaoLink(side, -normal.y, along, -edge.y, lenSq)));
    }
    return links;
}

// A soft boundary permits separate motions on its two sides. Away from its
// centerline, stationary texture should survive a moving outline when the
// two endpoints actually agree there. Accept that hypothesis only on detail.
float BoundaryStationarySupport(float2 pixel, float2 nearest, float2 halfMotion)
{
    float2 offset = pixel - nearest;
    float d = length(offset);
    if (d < .5) return 0;
    float2 normal = offset / d, tangent = float2(-normal.y, normal.x);
    float errorStill = 0, errorGuided = 0, variation = 0;
    float2 center = LiaoSignals(pixel * _Level.zw, 0);
    [unroll] for (int j = -1; j <= 1; j++)
    {
        float2 uv = (pixel + normal + 1.5 * j * tangent) * _Level.zw;
        float2 still = LiaoSignals(uv, 0);
        float2 moving = LiaoSignals(uv, halfMotion);
        errorStill += abs(still.x - still.y);
        errorGuided += abs(moving.x - moving.y);
        variation += dot(abs(still - center), float2(.5,.5));
    }
    return smoothstep(.5, 1.6, d) * smoothstep(.008, .06, variation)
        * smoothstep(.01, .09, errorGuided - 2 * errorStill);
}

// Test guide corrections once per level while preparing constraints. A pin
// on the curve itself remains shared by either side.
float CrossingWeight(float2 start, float2 end)
{
    if (_HasBoundary == 0) return 1;
    float2 ray = end - start;
    [loop] for (int i = 0; i < _SegmentCount; i++)
    {
        if (GuideOptions(i).z == 0) continue;
        float4 a, b;
        float u;
        ProjectSegment(i, start, a, b, u);
        float2 origin = .5 * (a.xy + a.zw) * _Level.xy;
        float2 edge = .5 * (b.xy + b.zw) * _Level.xy - origin;
        float2 n = float2(-edge.y, edge.x);
        float den = dot(ray, n);
        if (abs(den) < 1e-6) continue;
        float t = dot(origin - start, n) / den;
        if (t <= .001 || t >= .999) continue;
        float along = dot(start + t * ray - origin, edge);
        if (along >= 0 && along <= dot(edge, edge)) return .04;
    }
    return 1;
}

float4 LiaoConstraints(Varyings input) : SV_Target
{
    float2 target = 0;
    float total = 0;
    float nearestSq = 9;
    float2 residual = 0;
    float support = 0;
    [loop] for (int pin = 0; pin < _AnchorCount; pin++)
    {
        float4 pair = _AnchorTable.Load(int3((uint)pin % (uint)_AnchorWidth, (uint)pin / (uint)_AnchorWidth, 0));
        float2 midpoint = .5 * (pair.xy + pair.zw) * _Level.xy;
        float2 distance = input.pos.xy - midpoint;
        float distanceSq = dot(distance, distance);
        nearestSq = min(nearestSq, distanceSq);
        float2 weight = saturate(1 - abs(input.pos.xy - midpoint));
        float w = weight.x * weight.y;
        float radius = PointRadius(pin) * min(_Level.x, _Level.y);
        if (radius > 0)
            w = max(w, 1 - smoothstep(0, max(1, radius * radius), distanceSq));
        bool spread = radius == 0 && _AnchorCount >= 2 && max(_Level.x, _Level.y) <= 64 && distanceSq < 36;
        float transmission = (w > 0 || spread) ? CrossingWeight(midpoint, input.pos.xy) : 1;
        w *= transmission;
        float2 desired = .5 * (pair.zw - pair.xy) * _Level.xy;
        target += w * desired;
        total += w;
        // Move the neighbourhood along with a large correction. Accumulate
        // residuals to retain the spatial variation already found one level up.
        if (spread)
        {
            float falloff = 1 - distanceSq / 36;
            float influence = transmission * falloff * falloff / (1 + distanceSq);
            residual += influence * (desired - .5 * _Level.xy * LiaoInherited(midpoint * _Level.zw));
            support += influence;
        }
    }
    float pointCoverage = saturate(total);
    [loop] for (int i = 0; i < _SegmentCount;)
    {
        float4 a, b;
        float u;
        float4 options = GuideOptions(i);
        float radius = options.y * min(_Level.x, _Level.y);
        float distanceSq = ProjectGuide(i, input.pos.xy, a, b, u);
        nearestSq = min(nearestSq, distanceSq);
        float falloff = radius > 0 ? 1 - smoothstep(.25, max(1, radius * radius), distanceSq)
            : 1 - smoothstep(.25, 4, distanceSq);
        float w = falloff * (1 - pointCoverage);
        float4 pair = lerp(a, b, u);
        float2 halfMotion = .5 * (pair.zw - pair.xy);
        float2 nearest = .5 * (pair.xy + pair.zw) * _Level.xy;
        if (w > 0) w *= CrossingWeight(nearest, input.pos.xy);
        if (options.z != 0)
        {
            float support = max(2, radius > 0 ? radius : .14 * min(_Level.x, _Level.y));
            if (distanceSq < support * support)
            {
                float stationary = BoundaryStationarySupport(input.pos.xy, nearest, halfMotion);
                w *= 1 - stationary;
                total += stationary * (1 - smoothstep(0, support * support, distanceSq)) * (1 - pointCoverage);
            }
        }
        target += w * halfMotion * _Level.xy;
        total += w;
    }
    if (support > 0)
    {
        float weight = .3 * saturate(support) * (1 - saturate(total));
        float2 nearby = .5 * _Level.xy * LiaoInherited(input.pos.xy * _Level.zw) + residual / support;
        target += weight * nearby;
        total += weight;
    }
    return float4(target / max(1e-8, total), total, 1 - smoothstep(1, 9, nearestSq));
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

Texture2D<float4> _StationaryEvidence;

float StationaryGuideClearance(float2 uv, float4 pair, float radius)
{
    // A moving guide can sweep over background that happens to be identical
    // in both keys. Leave room for the transported detail on its entire path.
    float2 scale = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    float2 travel = (pair.zw - pair.xy) * scale, offset = (uv - pair.xy) * scale;
    float t = saturate(dot(offset, travel) / max(dot(travel, travel), 1e-8));
    float pixel = 1 / min(_Level.x, _Level.y);
    // A coarse test patch must clear the moving guide with its whole footprint.
    float reach = max(radius, .45 * length(travel)) + 2 * pixel;
    return smoothstep(reach, reach + max(3 * pixel, .5 * reach), length(offset - t * travel));
}

// Center the patch differences so a small exposure change does not look like travel.
float LiaoStationaryEvidence(Varyings input) : SV_Target
{
    float2 uv = input.pos.xy * _Level.zw;
    float2 shift = .5 * _Flow.SampleLevel(sampler_linear_clamp, uv, 0).xy;
    if (_LiaoHasDetail != 0) shift += .5 * _LiaoDetail.Load(int3((int2)input.pos.xy, 0)).xy;
    float4 center = _MainTex.SampleLevel(sampler_linear_clamp, uv, 0);
    float4 reference = 0;
    float4 difference = 0;
    float2 squares = 0;
    float contrast = 0, transportedAppearance = 0, transportedEnergy = 0;
    [unroll] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            float2 q = uv + float2(x, y) * _Level.zw;
            float4 patch = _MainTex.SampleLevel(sampler_linear_clamp, q, 0);
            float2 a = _MainTex.SampleLevel(sampler_linear_clamp, q - shift, 0).xy;
            float2 b = _MainTex.SampleLevel(sampler_linear_clamp, q + shift, 0).zw;
            float4 errors = float4(patch.xy - patch.zw, a - b);
            // The background can match in both keys while a different object
            // passes through this halfway position. A coherent transported
            // foreground patch must be allowed to cover that background.
            float2 layerDifference = .5 * (a + b - patch.xy - patch.zw);
            transportedAppearance += dot(layerDifference, layerDifference);
            transportedEnergy += dot(a - b, a - b);
            difference += errors;
            squares += float2(dot(errors.xy, errors.xy), dot(errors.zw, errors.zw));
            float4 signal = patch - center;
            reference += signal;
            contrast += dot(signal, signal);
        }
    }
    reference *= .04;
    difference *= .04;
    float2 meanSquared = float2(dot(difference.xy, difference.xy), dot(difference.zw, difference.zw));
    float2 error = .5 * max(0, .04 * squares - meanSquared);
    float variance = .25 * max(0, .04 * contrast - dot(reference, reference));
    float absoluteMatch = (1 - smoothstep(.000025, .0007, error.x))
        * (1 - smoothstep(.001, .01, .5 * meanSquared.x));
    float structureMatch = 1 - smoothstep(.015, .14, error.x / (.0001 + variance));
    float textured = smoothstep(.000015, .0003, variance);
    // If transporting the patch explains more detail, leave it to the solver.
    float movingWins = smoothstep(.00003, .00035, error.x - error.y);
    float crossing = smoothstep(.0005, .005, .02 * transportedAppearance)
        * (1 - smoothstep(.00015, .0015, .02 * transportedEnergy))
        * smoothstep(.5, 2, length(shift * _Level.xy));
    float confidence = textured * absoluteMatch * structureMatch * (1 - movingWins) * (1 - crossing);
    if (_LiaoGuided != 0)
    {
        float4 guide = _LiaoPins.Load(int3((int2)input.pos.xy, 0));
        confidence *= 1 - smoothstep(0, .08, max(guide.z, guide.w));
        [loop] for (int pin = 0; pin < _AnchorCount && confidence > 0; pin++)
        {
            uint column = (uint)pin % (uint)_AnchorWidth, row = (uint)pin / (uint)_AnchorWidth;
            float4 pair = _AnchorTable.Load(int3(column, row, 0));
            confidence *= StationaryGuideClearance(uv, pair, PointRadius(pin));
        }
        [loop] for (int i = 0; i < _SegmentCount && confidence > 0;)
        {
            float radius = GuideOptions(i).y;
            float4 a, b;
            float u;
            ProjectGuide(i, input.pos.xy, a, b, u);
            confidence *= StationaryGuideClearance(uv, lerp(a, b, u), radius);
        }
    }
    return confidence;
}

float4 LiaoWarp(Varyings input) : SV_Target
{
    float2 v = .5 * _Flow.Load(int3((int2)input.pos.xy, 0)).xy;
    float2 uv = input.pos.xy * _Level.zw;
    float2 dx = .5 * (LiaoSignals(uv + float2(_Level.z, 0), v) - LiaoSignals(uv - float2(_Level.z, 0), v));
    float2 dy = .5 * (LiaoSignals(uv + float2(0, _Level.w), v) - LiaoSignals(uv - float2(0, _Level.w), v));
    return float4(LiaoSignals(uv, v), dx.x + dx.y, dy.x + dy.y);
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
    // Fit a shared subpixel shift across the patch, allowing a constant brightness offset.
    float3 tensor = 0, means = 0;
    float2 rhs = 0;
    // A texel contributes to every 5x5 window containing it.
    [loop] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            int3 tap = int3(LiaoPixel(p + int2(x, y)), 0);
            coeff += _LiaoStats.Load(tap);
            float4 warped = _LiaoWarped.Load(tap);
            float2 g = warped.zw;
            float e = warped.x - warped.y;
            means += float3(g, e);
            tensor += float3(g.x * g.x, g.x * g.y, g.y * g.y);
            rhs += g * e;
        }
    }
    coeff /= 25;
    float2 uv = input.pos.xy * _Level.zw;
    float2 v = .5 * _Flow.Load(int3(p, 0)).xy;
    float2 ab = LiaoSignals(uv, v);
    float2 slope = coeff.x * ab.yx - coeff.y * ab + coeff.zw;
    slope.x = -slope.x;
    float2 dx = .5 * (LiaoSignals(uv + float2(_Level.z, 0), v) - LiaoSignals(uv - float2(_Level.z, 0), v));
    float2 dy = .5 * (LiaoSignals(uv + float2(0, _Level.w), v) - LiaoSignals(uv - float2(0, _Level.w), v));
    // Diagonal bound on the 2x2 structure tensor. A strong gradient on one
    // axis should not suppress a subpixel correction on the other.
    float2 curvature = abs(coeff.y) * (float2(dot(dx, dx), dot(dy, dy)) + abs(dot(dx, dy)));
    tensor -= float3(means.x * means.x, means.x * means.y, means.y * means.y) / 25;
    rhs -= means.xy * means.z / 25;
    float weight = coeff.x > 0 && max(_Level.x, _Level.y) >= 48 ? 1 / (.005 + max(0, tensor.x + tensor.z)) : 0;
    float2 patchCurvature = max(0, tensor.xz) + abs(tensor.y);
    return float4(float2(dot(slope, dx), dot(slope, dy)) + weight * rhs, curvature + weight * patchCurvature);
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
    if (_HasBoundary != 0)
    {
        // The biharmonic stencil reaches two texels. At a cut switch to a
        // one-sided membrane stencil so its diagonal and outer taps cannot
        // carry motion through the curve. Keep the topology checks below.
        float4 links = _BoundaryLinks.Load(int3(p, 0));
        float nearCut = min(min(links.x, links.z), min(links.y, links.w));
        float2 membrane = 0;
        [unroll] for (int j = 0; j < 4; j++)
        {
            float4 outer = _BoundaryLinks.Load(int3(LiaoPixel(p + axes[j]), 0));
            nearCut = min(nearCut, min(min(outer.x, outer.z), min(outer.y, outer.w)));
            membrane -= links[j] * axial[j];
        }
        if (nearCut < 1) bending = 2 * membrane;
    }
    float4 data = _LiaoForce.Load(int3(p, 0));
    float4 guide = 0;
    if (_LiaoGuided != 0) guide = _LiaoPins.Load(int3(p, 0));
    const float rigidity = .12;
    float pinWeight = 24 * guide.z;
    float2 uv = input.pos.xy * _Level.zw;
    float2 inherited = LiaoInherited(uv);
    if (_LiaoHasDetail != 0) inherited += _LiaoDetail.Load(int3(p, 0)).xy;
    float axis = max(_Level.x, _Level.y);
    // Anchor ambiguous coarse matches to the inherited field. Relax that bias near guides and on fine levels.
    float pinInfluence = max(saturate(guide.z), guide.w * (1 - saturate(data.z + data.w)));
    float priorWeight = min(4, 64 / axis) * min(1, 64 / axis) * (1 - pinInfluence);
    float stationary = 0;
    if (_CheckStationary != 0) stationary = _StationaryEvidence.Load(int3(p, 0)).r;
    float stillWeight = 12 * stationary;
    float2 step = (data.xy - rigidity * bending + pinWeight * (guide.xy - v) - stillWeight * v
        + priorWeight * (.5 * _Level.xy * inherited - v))
        / (.025 + data.zw + 20 * rigidity + pinWeight + priorWeight + stillWeight);
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
    // Negative confidence marks authored motion on the halfway grid.
    float manual = smoothstep(0, .08, max(guide.z, guide.w));
    float coverage = _CheckStationary != 0 ? (prior.w > .5 ? 1 : 0) + .25 * (stationary - manual) : prior.w;
    return float4(prior.xy + 2 * limit * step * _Level.zw, 0, coverage);
}
