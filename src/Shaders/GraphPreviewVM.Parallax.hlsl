#include "GraphParallax.hlsl"

// Parallax shares the existing replay stack; no additional per-pixel frame storage.
GraphParallaxTrace VMLoadParallax(Frame f)
{
    GraphParallaxTrace t = (GraphParallaxTrace)0;
    t.origin = f.sum.xy; t.ray = f.sum.zw;
    t.depth = f.total.x; t.step = f.total.y; t.high = f.total.z; t.highDelta = f.total.w;
    t.low = f.previous.x; t.lowDelta = f.previous.y;
    t.remaining = (int)f.previous.z; t.refinements = (int)f.previous.w;
    return t;
}
