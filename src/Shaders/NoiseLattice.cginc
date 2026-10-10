#ifndef WHIMTEX_NOISE_LATTICE_INCLUDED
#define WHIMTEX_NOISE_LATTICE_INCLUDED

// This opt-in hook leaves the shared FNL include unchanged for brushes and FX.
// Mutable statics are private to this shader invocation, never shared GPU state.
#define WHIMTEX_NOISE_LATTICE
static int3 wtOrigin = 0;
static int2 wtPeriod = 0;
static int wtLayout = 0;
float4 _NoiseLattice[144];
float4 _NoiseWarpInverse;

int WtMod(int a, int period)
{
    period = max(period, 1);
    int r = a % period;
    return r < 0 ? r + period : r;
}

int WtHash2D(int seed, int x, int y)
{
    x += wtOrigin.x; y += wtOrigin.y;
    if (wtLayout == 2)
    {
        int h = x + y, v = x - y;
        if (wtPeriod.x > 0) h = WtMod(h, 2 * wtPeriod.x);
        if (wtPeriod.y > 0) v = WtMod(v, 2 * wtPeriod.y);
        x = (h + v) / 2; y = (h - v) / 2;
    }
    else
    {
        if (wtPeriod.x > 0) x = WtMod(x, wtPeriod.x);
        if (wtPeriod.y > 0) y = WtMod(y, wtPeriod.y);
    }
    return (seed ^ (x * 501125321) ^ (y * 1136930381)) * 0x27d4eb2d;
}

int WtHash3D(int seed, int x, int y, int z)
{
    int3 p = int3(x, y, z) + wtOrigin;
    if (wtLayout == 3)
    {
        // Reduce by translation vectors; do not wrap the external Z direction.
        if (wtPeriod.x > 0)
        {
            int h = -p.x + 2 * p.y + 2 * p.z;
            int q = (h - WtMod(h, 3 * wtPeriod.x)) / max(3 * wtPeriod.x, 1);
            p -= q * (wtPeriod.x / 3) * int3(-1, 2, 2);
        }
        if (wtPeriod.y > 0)
        {
            int v = 2 * p.x - p.y + 2 * p.z;
            int q = (v - WtMod(v, 3 * wtPeriod.y)) / max(3 * wtPeriod.y, 1);
            p -= q * (wtPeriod.y / 3) * int3(2, -1, 2);
        }
    }
    else
    {
        if (wtPeriod.x > 0) p.x = WtMod(p.x, wtPeriod.x);
        if (wtPeriod.y > 0) p.y = WtMod(p.y, wtPeriod.y);
    }
    return (seed ^ (p.x * 501125321) ^ (p.y * 1136930381) ^ (p.z * 1720413743)) * 0x27d4eb2d;
}

// Compensated float arithmetic keeps the local fraction at high octave/offset
// values. No double-precision GPU support or platform-specific code is required.
void WtProduct(inout int3 whole, inout float3 fraction, float value, int index)
{
    float3 a = _NoiseLattice[index].xyz;
    precise float splitV = value * 4097.0;
    precise float vh = splitV - (splitV - value);
    precise float vl = value - vh;
    precise float3 splitA = a * 4097.0;
    precise float3 ah = splitA - (splitA - a);
    precise float3 al = a - ah;
    precise float3 product = a * value;
    precise float3 error = ((ah * vh - product) + ah * vl + al * vh) + al * vl;
    error += _NoiseLattice[index + 1].xyz * value;
    int3 integer = (int3)floor(product);
    precise float3 part = (product - (float3)integer) + error;
    int3 carry = (int3)floor(part);
    whole += integer + carry;
    fraction += part - (float3)carry;
}

precise float3 WtPosition(int octave, float2 uv, float3 displacement, int layout)
{
    int start = octave * 16;
    int3 whole = (int3)_NoiseLattice[start + 13].xyz * 4096 + (int3)_NoiseLattice[start + 14].xyz;
    precise float3 fraction = _NoiseLattice[start + 10].xyz + _NoiseLattice[start + 11].xyz;
    WtProduct(whole, fraction, uv.x, start);
    WtProduct(whole, fraction, uv.y, start + 2);
    WtProduct(whole, fraction, displacement.x, start + 4);
    WtProduct(whole, fraction, displacement.y, start + 6);
    WtProduct(whole, fraction, displacement.z, start + 8);
    int3 carry = (int3)floor(fraction);
    wtOrigin = whole + carry;
    float4 metadata = _NoiseLattice[start + 12];
    int packed = (int)metadata.w;
    wtPeriod = (int2)metadata.xy * 4096 + int2(packed & 4095, (packed >> 12) & 4095);
    // The noise kernel supplies a constant layout; only Warp selects it at runtime.
    // This lets the compiler remove the other lattice reductions from each hash.
    wtLayout = layout;
    precise float3 local = fraction - (float3)carry;
    return local;
}
#endif
