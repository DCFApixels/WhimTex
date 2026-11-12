#ifndef WHIMTEX_CAUSTICS_SPECTRUM_INCLUDED
#define WHIMTEX_CAUSTICS_SPECTRUM_INCLUDED
// @wt.block(Spectrum)
float4 WTNoiseInternalCausticsSpectrum(int band)
{
    // Six broad display-RGB responses, 650 to 450 nm. Positive weights sum
    // to white per channel; this is a compact spectral approximation, not colorimetry.
    float3 response=band==0?float3(1,0,0):band==1?float3(1,0.5,0):
        band==2?float3(0.5,1,0):band==3?float3(0,1,0.2):
        band==4?float3(0,0.5,1):float3(0.2,0,1);
    float wavelength=(650-40*band)*0.001;
    // Cauchy-shaped dispersion, normalized to [-1,1] across these bands.
    float shift=2*(rcp(wavelength*wavelength)-rcp(0.65*0.65))/
        (rcp(0.45*0.45)-rcp(0.65*0.65))-1;
    return float4(response/float3(2.7,3,2.2),shift);
}
// @wt.end

// @wt.block(Palette)
#ifndef WT_CAUSTICS_PALETTE_INCLUDED
#define WT_CAUSTICS_PALETTE_INCLUDED
struct WTCausticsPalette { float3 band[6]; float3 total; };
WTCausticsPalette WTCausticsMakePalette(float3 a,float3 b,float3 c,float3 d,float3 e,float3 f,bool whiteBalance)
{
    WTCausticsPalette p;
    p.band[0]=max(a,0);p.band[1]=max(b,0);p.band[2]=max(c,0);
    p.band[3]=max(d,0);p.band[4]=max(e,0);p.band[5]=max(f,0);
    p.total=p.band[0]+p.band[1]+p.band[2]+p.band[3]+p.band[4]+p.band[5];
    float3 divisor=whiteBalance?max(p.total,1e-6):float3(6,6,6);
    [unroll] for(int paletteBand=0;paletteBand<6;paletteBand++) p.band[paletteBand]/=divisor;
    p.total/=divisor;
    return p;
}

WTCausticsPalette WTCausticsCieD65Palette()
{
    // Signed linear sRGB responses, normalized to white. Keep in sync with
    // ShaderProgramRuntime.Caustics.cs, which binds one weight per projection.
    WTCausticsPalette p;
    p.band[0]=float3( 0.098109451,-0.010122706,-0.000726397);
    p.band[1]=float3( 0.761519073,-0.027248051,-0.011985062);
    p.band[2]=float3( 0.549441211, 0.405399950,-0.060711233);
    p.band[3]=float3(-0.357448968, 0.622482230,-0.023446752);
    p.band[4]=float3(-0.113213731, 0.069418560, 0.609301223);
    p.band[5]=float3( 0.061592964,-0.059929983, 0.487568222);
    p.total=1;
    return p;
}

// Fit the signed sum to the procedural node's [0,1] range at constant luminance.
float3 WTCausticsFitSpectrumToUnitRGB(float3 rgb)
{
    float y=saturate(dot(rgb,float3(0.212639,0.715169,0.072192)));
    float3 chroma=rgb-y;
    float low=min(chroma.r,min(chroma.g,chroma.b));
    float high=max(chroma.r,max(chroma.g,chroma.b));
    float scale=min(1,min(y/max(-low,1e-6),(1-y)/max(high,1e-6)));
    return saturate(y+chroma*scale);
}
#endif
// @wt.end

// @wt.block(ProjectionSpectrum)
// Representative wavelengths adapted from vis.gl contributors (MIT).
// Source revision and full notice: ThirdPartyNotices.md, Spectral caustics weights.
float WTCausticsProjectionShift(int band)
{
    float wavelength=band==0?664.46:band==1?618.64:band==2?574.60:band==3?528.33:band==4?468.28:434.72;
    return 550.0*550.0/(wavelength*wavelength)-1;
}

float WTCausticsRefractiveIndex(float ior,float dispersion,int band)
{
    ior=max(ior,1);
    float d=max(dispersion,0), baseD=min(d,1);
    // IOR stays fixed at 550 nm. At n=1.333, d=0.1 gives Cauchy B~0.0031 um^2.
    // The cubic part reserves strong artistic separation for the upper range.
    float spread=(ior-1)*baseD*(0.3+0.7*baseD*baseD);
    // Continue along the tangent at 1, retaining both value and slope.
    spread+=(ior-1)*2.4*max(d-1,0);
    return ior+spread*WTCausticsProjectionShift(band);
}

// Fit the completed signed sum to nonnegative linear RGB at constant luminance.
// There is no upper bound for HDR light and no change to the spatial footprint.
float3 WTCausticsFitSpectrumToRGB(float3 rgb)
{
    float lowest = min(rgb.r, min(rgb.g, rgb.b));
    if (lowest >= 0) return rgb;
    float luminance = max(0, dot(rgb, float3(0.212639, 0.715169, 0.072192)));
    return max(0, (rgb - lowest) * (luminance / (luminance - lowest)));
}

// @wt.end
#endif
