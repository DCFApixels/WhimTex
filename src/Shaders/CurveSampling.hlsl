#ifndef WHIMTEX_CURVE_SAMPLING_INCLUDED
#define WHIMTEX_CURVE_SAMPLING_INCLUDED

// x: first key time, y: inverse time span, zw: pre/post wrap (Clamp, Loop, PingPong).
float _WhimTex_CurveCoordinate(float t, float4 sampling)
{
    t = (t - sampling.x) * sampling.y;
    if (t < 0 || t >= 1)
    {
        float wrap = t < 0 ? sampling.z : sampling.w;
        if (wrap < .5) t = saturate(t);
        else if (wrap < 1.5) t = frac(t);
        else { t -= floor(t * .5) * 2; t = t <= 1 ? t : 2 - t; }
    }
    return (t * 511 + .5) / 512;
}

#endif
