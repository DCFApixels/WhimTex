// Each exposed endpoint supplies a background observation for the other.
// Fit the background fraction shared by both translated edge samples.
float2 MovingCoverage(float4 a, float4 b, float4 backA, float4 backB)
{
    float changed = min(EndpointDifference(a, backA), EndpointDifference(b, backB));
    float4 backgrounds = backA - backB;
    float fraction = saturate(dot(a - b, backgrounds) / max(dot(backgrounds, backgrounds), 1e-8));
    float residual = EndpointDifference(a - fraction * backA, b - fraction * backB);
    float confidence = smoothstep(.008, .045, changed)
        * (1 - smoothstep(.05, .30, residual / max(.015, changed)));
    return float2(confidence, fraction);
}

bool OnCanvas(float2 p) { return all(p >= 0) && all(p <= _RasterSize - 1); }

// Which endpoint contains the moving surface at p? Require a reciprocal
// change at its translated partner. A repeated static texture alone
// cannot establish ownership of a disocclusion.
float2 MovingOwners(float2 p, float4 a, float4 b)
{
    float2 ahead = p + _LayerTranslation.xy, behind = p - _LayerTranslation.xy;
    float2 owners = 0;
    if (OnCanvas(ahead))
        owners.x = MovingCoverage(a, SamplePremultiplied(_ToTex, ahead), b, SamplePremultiplied(_MainTex, ahead)).x;
    if (OnCanvas(behind))
        owners.y = MovingCoverage(SamplePremultiplied(_MainTex, behind), b, SamplePremultiplied(_ToTex, behind), a).x;
    return owners;
}

float TranslationEvidence(Varyings input) : SV_Target
{
    float changed = 0, explained = 0, stationary = 0;
    [loop] for (int y = 0; y < 16; ++y)
    {
        [loop] for (int x = 0; x < 16; ++x)
        {
            float2 uv = (float2(x, y) + .5) / 16;
            float2 p = floor(_LayerTranslation.zw + (2 * uv - 1) * _LayerReach + .5);
            if (!OnCanvas(p)) continue;
            float4 a = PremultipliedTexel(_MainTex, (int2)p), b = PremultipliedTexel(_ToTex, (int2)p);
            float difference = EndpointDifference(a, b);
            float weight = smoothstep(.01, .06, difference);
            if (weight > 0)
            {
                float2 owners = MovingOwners(p, a, b);
                changed += weight;
                explained += weight * max(owners.x, owners.y);
            }
            else
            {
                // A camera pan explains textured background with the
                // same translation; it is not a stationary layer.
                float2 ahead = p + _LayerTranslation.xy, behind = p - _LayerTranslation.xy;
                if (OnCanvas(ahead))
                    stationary += .5 * smoothstep(.01, .06, EndpointDifference(a, SamplePremultiplied(_ToTex, ahead)));
                if (OnCanvas(behind))
                    stationary += .5 * smoothstep(.01, .06, EndpointDifference(b, SamplePremultiplied(_MainTex, behind)));
            }
        }
    }
    return smoothstep(2, 6, changed) * smoothstep(.72, .93, explained / max(1, changed))
        * saturate(stationary / 2);
}

// When both exposed backgrounds have the same colour, the two keys
// alone cannot reveal the coverage of an antialiased edge. Look for an
// interior colour across from a verified exterior tap, and require it
// to explain both observations before changing the matte.
float EdgeBackgroundFraction(float2 pa, float2 pb, float4 a, float4 b, float4 backA, float4 backB)
{
    const int2 axes[4] = { int2(-1,0), int2(1,0), int2(0,-1), int2(0,1) };
    float best = .025, fraction = 0;
    [loop] for (int i = 0; i < 4; ++i)
    {
        float2 offset = axes[i];
        float2 outA = pa - offset, outB = pb - offset;
        if (!OnCanvas(outA) || !OnCanvas(outB) || !OnCanvas(pa + offset) || !OnCanvas(pb + offset)) continue;
        float outside = max(EndpointDifference(SamplePremultiplied(_MainTex, outA), SamplePremultiplied(_ToTex, outA)),
            EndpointDifference(SamplePremultiplied(_MainTex, outB), SamplePremultiplied(_ToTex, outB)));
        if (outside > .02) continue;
        float4 fa = SamplePremultiplied(_MainTex, pa + offset), fb = SamplePremultiplied(_ToTex, pb + offset);
        float4 fg = .5 * (fa + fb), da = fg - backA, db = fg - backB;
        float energy = dot(da, da) + dot(db, db);
        if (energy < .001) continue;
        float opacity = saturate((dot(a - backA, da) + dot(b - backB, db)) / energy);
        float error = max(EndpointDifference(fa, fb), max(EndpointDifference(a, lerp(backA, fg, opacity)),
            EndpointDifference(b, lerp(backB, fg, opacity))));
        if (error < best) { best = error; fraction = 1 - opacity; }
    }
    return fraction * (1 - smoothstep(.005, .025, best));
}

float4 ResolveTranslation(float2 p, float4 dense)
{
    // A wrong correspondence can drag background out past either endpoint.
    float2 edge = abs(p - _LayerTranslation.zw) / (_LayerReach + length(_LayerTranslation.xy));
    float weight = (1 - smoothstep(.85, 1, max(edge.x, edge.y))) * _TranslationEvidence.Load(int3(0, 0, 0)).r;
    weight *= smoothstep(0, .06, min(_MorphTime, 1 - _MorphTime));
    float2 pa = p - _MorphTime * _LayerTranslation.xy, pb = p + (1 - _MorphTime) * _LayerTranslation.xy;
    if (weight <= 0) return dense;
    float4 a0 = PremultipliedTexel(_MainTex, (int2)p), b0 = PremultipliedTexel(_ToTex, (int2)p);
    float4 background = lerp(a0, b0, _MorphTime);
    float visible = 1 - smoothstep(.003, .025, EndpointDifference(a0, b0));
    if (visible < 1)
    {
        float2 owners = MovingOwners(p, a0, b0);
        float unique = abs(owners.x - owners.y);
        background = lerp(background, owners.x > owners.y ? b0 : a0, unique);
        visible = max(visible, unique);
    }
    if (!OnCanvas(pa) || !OnCanvas(pb))
    {
        // Outside the picture a moving sample has no opposite key.
        // Restore background only if the visible side supports it.
        if (visible <= 0 || (!OnCanvas(pa) && !OnCanvas(pb))) return dense;
        bool source = OnCanvas(pa);
        float2 q = source ? pa : pb;
        float4 qa = SamplePremultiplied(_MainTex, q), qb = SamplePremultiplied(_ToTex, q);
        float stable = 1 - smoothstep(.003, .025, EndpointDifference(qa, qb));
        if (stable < 1)
        {
            float2 owners = MovingOwners(q, qa, qb);
            stable = max(stable, source ? owners.y - owners.x : owners.x - owners.y);
        }
        return lerp(dense, background, weight * visible * stable);
    }

    float4 a = SamplePremultiplied(_MainTex, pa), b = SamplePremultiplied(_ToTex, pb);
    float4 backA = SamplePremultiplied(_ToTex, pa), backB = SamplePremultiplied(_MainTex, pb);
    float2 moving = MovingCoverage(a, b, backA, backB);

    // If a translated sample cannot be distinguished from the exposed
    // background, trust the stationary layer only at validated positions.
    float unchanged = 1 - smoothstep(.003, .025, min(EndpointDifference(a, backA), EndpointDifference(b, backB)));
    if (moving.x > 0 && visible > 0 && EndpointDifference(backA, backB) < .003 &&
        EndpointDifference(background, backA) > .02)
        moving.y = EdgeBackgroundFraction(pa, pb, a, b, backA, backB);
    // A premultiplied foreground cannot contribute negative opacity, or
    // more opacity than the covered fraction, even on transparent keys.
    const float epsilon = 1e-8;
    float2 alpha = float2(a.a, b.a), backAlpha = float2(backA.a, backB.a);
    float2 limit = min((alpha + epsilon) / (backAlpha + epsilon),
        (1 - alpha + epsilon) / (1 - backAlpha + epsilon));
    moving.y = min(moving.y, min(limit.x, limit.y));
    float4 front = lerp(a - moving.y * backA, b - moving.y * backB, _MorphTime);
    float4 result = lerp(dense, background, unchanged * visible);
    result = lerp(result, front + moving.y * background, moving.x * lerp(1, visible, moving.y));
    return lerp(dense, result, weight);
}
