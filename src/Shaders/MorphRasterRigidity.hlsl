// A planar rotation is linear on the halfway domain: full displacement at r
// is translation + 2*tan(angle/2)*perp(r). Fit in isotropic image units so the
// same constraint also represents rotations on a rectangular canvas.
Texture2D<float4> _RigidGuides, _RigidTargets;
float2 _RigidGridInv;
int _HasRigidGuides;

float2 RigidPerp(float2 v) { return float2(-v.y, v.x); }

struct RigidFit
{
    float weight, weightSq, radiusSq, crossMoment, motionSq;
    float2 position, motion;
};

void RigidObserve(inout RigidFit fit, float2 r, float2 d, float w)
{
    fit.weight += w;
    fit.weightSq += w*w;
    fit.position += w*r;
    fit.motion += w*d;
    fit.radiusSq += w*dot(r,r);
    fit.crossMoment += w*dot(RigidPerp(r),d);
    fit.motionSq += w*dot(d,d);
}

float4 RigidEstimate(RigidFit fit)
{
    if (fit.weight < .01) return 0;
    float2 r = fit.position / fit.weight, d = fit.motion / fit.weight;
    float spread = max(0, fit.radiusSq / fit.weight - dot(r,r));
    float crossMoment = fit.crossMoment / fit.weight - dot(RigidPerp(r),d);
    float slope = clamp(crossMoment / max(spread, 1e-8), -4, 4);
    float error = max(0, fit.motionSq / fit.weight - dot(d,d) - 2*slope*crossMoment + slope*slope*spread);
    float support = smoothstep(1.15, 2, fit.weight*fit.weight / max(fit.weightSq, 1e-8))
        * smoothstep(.000025, .00025, spread);
    return float4(d - slope*RigidPerp(r), slope, support * (1 - smoothstep(.00006, .0008, error)));
}

float4 RigidGuideModels(Varyings input) : SV_Target
{
    float2 uv = input.pos.xy * _Level.zw;
    float2 metric = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    float4 model = 0;
    [unroll] for (int iteration = 0; iteration < 2; iteration++)
    {
        RigidFit fit = (RigidFit)0;
        [loop] for (int i = 0; i < _AnchorCount; i++)
        {
            uint2 cell = uint2((uint)i % (uint)_AnchorWidth, (uint)i / (uint)_AnchorWidth);
            float4 pin = _AnchorTable.Load(int3(cell, 0));
            float2 r = (.5 * (pin.xy + pin.zw) - uv) * metric;
            float radius = PointRadius(i);
            if (radius <= 0) radius = .16;
            float w = saturate(1 - dot(r,r) / (radius * radius));
            if (w <= 0) continue;
            float2 d = (pin.zw - pin.xy) * metric;
            float2 e = d - model.xy - model.z * RigidPerp(r);
            if (iteration != 0) w /= 1 + dot(e,e) / (.018*.018);
            RigidObserve(fit, r, d, w*w);
        }
        model = RigidEstimate(fit);
        model.w *= saturate(fit.weight);
    }
    return model;
}

float4 RigidFlowModel(float2 uv, float2 metric, float radius)
{
    float4 model = 0;
    [unroll] for (int iteration = 0; iteration < 2; iteration++)
    {
        RigidFit fit = (RigidFit)0;
        [loop] for (int y = -2; y <= 2; y++)
        {
            [unroll] for (int x = -2; x <= 2; x++)
            {
                float2 offset = float2(x,y);
                float2 r = offset * (.5 * radius);
                float2 q = uv + r / metric;
                if (any(q < 0) || any(q > 1)) continue;
                float2 d = _Flow.SampleLevel(sampler_linear_clamp, q, 0).xy * metric;
                float w = exp(-.25 * dot(offset,offset));
                float2 e = d - model.xy - model.z * RigidPerp(r);
                if (iteration != 0) w /= 1 + dot(e,e) / (.02*.02);
                RigidObserve(fit, r, d, w);
            }
        }
        model = RigidEstimate(fit);
    }
    return model;
}

// Compare the candidate and the freely deformed patch against actual endpoint
// samples. Center the residual to tolerate a uniform change in illumination.
float2 RigidAppearance(float2 uv, float2 metric, float radius, float4 model, bool useFlow)
{
    float2 sum = 0, squares = 0;
    float differenceSq = 0, total = 0;
    [loop] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            float2 offset = float2(x,y);
            float2 r = offset * (.25 * radius);
            float2 q = uv + r / metric;
            float2 d = useFlow ? _Flow.SampleLevel(sampler_linear_clamp, q, 0).xy : (model.xy + model.z * RigidPerp(r)) / metric;
            float2 pair = LiaoSignals(q, .5 * d);
            float w = exp(-.22 * dot(offset,offset));
            sum += w * pair;
            squares += w * pair * pair;
            differenceSq += w * (pair.x-pair.y) * (pair.x-pair.y);
            total += w;
        }
    }
    float2 mean = sum / total;
    float variance = max(0, dot(squares / total - mean*mean, float2(1,1)));
    float error = max(0, differenceSq / total - .95*(mean.x-mean.y)*(mean.x-mean.y));
    return float2(error / (.0004 + variance), smoothstep(.0001, .002, variance));
}

float4 RigidTargets(Varyings input) : SV_Target
{
    float2 uv = input.pos.xy * _RigidGridInv;
    float2 metric = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    float radius = max(.085, 2 / min(_Level.x, _Level.y));
    float4 model = RigidFlowModel(uv, metric, radius);
    float2 observed = RigidAppearance(uv, metric, radius, 0, true);
    float2 fit = RigidAppearance(uv, metric, radius, model, false);
    float best = fit.x + .12 * (1 - model.w);
    if (_HasRigidGuides != 0)
    {
        float4 guide = _RigidGuides.SampleLevel(sampler_linear_clamp, uv, 0);
        if (guide.w > .1)
        {
            float2 agreement = RigidAppearance(uv, metric, radius, guide, false);
            float cost = agreement.x + .12 * (1 - guide.w) - .06 * guide.w;
            if (cost < best) { best = cost; fit = agreement; model = guide; }
        }
    }
    // The identity is itself a rigid motion. Only nominate it when there
    // is visible structure that independently matches at its original location.
    float2 stationary = RigidAppearance(uv, metric, radius, 0, false);
    if (stationary.y > .15 && stationary.x < .12 && stationary.x <= best + .008)
    {
        model = float4(0,0,0,stationary.y);
        fit = stationary;
    }

    float confidence = model.w * (1 - smoothstep(.08, .4, fit.x - observed.x))
        * (1 - smoothstep(.22, .9, fit.x));
    return float4(confidence * model.xy / metric, confidence, 0);
}

float4 RigidForce(Varyings input) : SV_Target
{
    float4 force = LiaoForce(input);
    float2 uv = input.pos.xy * _Level.zw;
    float4 constraint = _RigidTargets.SampleLevel(sampler_linear_clamp, uv, 0);
    int3 p = int3((int2)input.pos.xy, 0);
    float strength = 3;
    if (_LiaoGuided != 0)
    {
        float4 guide = _LiaoPins.Load(p);
        strength *= 1 - saturate(max(guide.z, guide.w));
    }
    float2 motion = _Flow.Load(p).xy;
    force.xy += .5 * strength * _Level.xy * (constraint.xy - motion * constraint.z);
    force.zw += strength * constraint.z;
    return force;
}
