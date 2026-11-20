Texture2D<float4> _GuideAtlas, _FrontLayer, _BackLayer, _CorrectionReference, _CorrectionCoverage, _CorrectionTone;
Texture2D<float4> _ChoicePreference;
int _HasForeground, _FrontSide, _EndpointIndex;
float2 _BlurStep;

// For a disputed patch, look for aligned neighbouring surfaces whose endpoint colors
// agree. Prefer the key continuous with those surfaces; ambiguous evidence keeps the crossfade.
float LocalPreference(float2 halfway, float2 qa, float2 qb, float4 a, float4 b, float visibility)
{
    float preference = visibility;
    {
        float vote=0, support=0;
        const float2 dirs[4]={float2(1,0),float2(-1,0),float2(0,1),float2(0,-1)};
        [unroll] for(int i=0;i<4;i++)
        {
            float2 q=halfway+dirs[i]*max(2.,min(_RasterSize.x,_RasterSize.y)*.025)/_RasterSize;
            float2 d=.5*_Flow.SampleLevel(sampler_linear_clamp,q,0).xy;
            float4 x=_MainTex.SampleLevel(sampler_linear_clamp,q-d,0), y=_ToTex.SampleLevel(sampler_linear_clamp,q+d,0);
            float w=min(x.a,y.a)/(1+16*EndpointDifference(float4(x.rgb*x.a,x.a),float4(y.rgb*y.a,y.a)));
            float sa=rcp(1+24*EndpointDifference(a,float4(x.rgb*x.a,x.a)));
            float sb=rcp(1+24*EndpointDifference(b,float4(y.rgb*y.a,y.a)));
            vote+=w*(sb-sa); support+=w;
        }
        preference=clamp(preference+vote/max(.001,support),-1,1);
    }
    if (_HasForeground != 0)
    {
        float ma = _GuideAtlas.SampleLevel(sampler_linear_clamp, qa, 0).r;
        float mb = _GuideAtlas.SampleLevel(sampler_linear_clamp, qb, 0).g;
        preference = lerp(preference, mb - ma, abs(mb - ma));
    }
    return preference;
}

// Decide in the reference frame once per pair of warped samples. Filtering this small
// field before warping keeps a source decision coherent over neighbouring strands of fur.
float4 MeasureLocalPreference(Varyings input) : SV_Target
{
    float2 uv = input.uv;
    float4 flow = _Flow.SampleLevel(sampler_linear_clamp,uv,0);
    float2 qa = saturate(uv-.5*flow.xy), qb = saturate(uv+.5*flow.xy);
    float4 a = SamplePremultiplied(_MainTex,qa*_RasterSize-.5);
    float4 b = SamplePremultiplied(_ToTex,qb*_RasterSize-.5);
    float vote = LocalPreference(uv,qa,qb,a,b,_UseVisibility != 0 ? flow.z : 0);
    return float4(vote,0,0,1);
}

float4 SmoothLocalPreference(Varyings input) : SV_Target
{
    uint w,h;
    _MainTex.GetDimensions(w,h);
    float2 texel = 1./float2(w,h);
    float vote = 0;
    [unroll] for (int y=-1; y<=1; y++)
        [unroll] for (int x=-1; x<=1; x++)
            vote += (x==0?2:1)*(y==0?2:1)*_MainTex.SampleLevel(sampler_linear_clamp,input.uv+float2(x,y)*texel,0).r;
    return float4(vote/16,0,0,1);
}

float GuideMatte(float2 uv)
{
    float2 m=_GuideAtlas.SampleLevel(sampler_linear_clamp,uv,0).rg;
    return _EndpointIndex==0?m.x:m.y;
}

float4 ExtractMorphSurface(Varyings input):SV_Target
{
    float2 uv=input.uv;
    float4 pixel=_MainTex.SampleLevel(sampler_linear_clamp,uv,0);
    uint w, h;
    _GuideAtlas.GetDimensions(w, h);
    // Search a narrow band on each side of the authored silhouette. Local RGB evidence
    // moves the matte inside the band and a soft geometric prior handles flat colours.
    float2 stepUV = max(1.5, min(w,h)*.011) / float2(w,h);
    float4 front = 0, back = 0;
    float coverage = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 q = uv + float2(x,y)*stepUV;
            float m = GuideMatte(q);
            float4 texel = _MainTex.SampleLevel(sampler_linear_clamp,q,0);
            float4 weighted = float4(texel.rgb,1)*texel.a;
            front += weighted*m;
            back += weighted*(1-m);
            coverage += m;
        }
    }
    float m = GuideMatte(uv);
    if (front.a > .001 && back.a > .001)
    {
        float3 f = front.rgb/front.a, b = back.rgb/back.a;
        float3 axis = f-b;
        float prior = .002*(1+dot(abs(pixel.rgb),float3(.2126,.7152,.0722)));
        m = saturate((dot(pixel.rgb-b,axis)+prior*coverage/9)/(dot(axis,axis)+prior));
        float3 residual = pixel.rgb-lerp(b,f,m);
        float alpha = _FrontSide != 0 ? m : 1-m;
        pixel.rgb = (_FrontSide != 0 ? f : b) + residual*alpha/max(.5,m*m+(1-m)*(1-m));
    }
    pixel.a*=_FrontSide!=0?m:1-m;
    return pixel;
}

Texture2D<float4> _ContourDistance, _SurfaceKeyA, _SurfaceKeyB;

float SurfaceDistance(float2 uv, int key)
{
    float2 pair = _ContourDistance.SampleLevel(sampler_linear_clamp,uv,0).rg;
    float distance = key == 0 ? pair.x : pair.y;
    return distance * min(_RasterSize.x, _RasterSize.y) * (_FrontSide != 0 ? 1 : -1);
}

float3 ExtendSurface(Texture2D<float4> surface, float2 uv, float distance, int key)
{
    if (distance < 1.5)
    {
        uint w, h;
        _ContourDistance.GetDimensions(w,h);
        float2 texel = 1.0 / float2(w,h);
        float2 normal = float2(
            SurfaceDistance(uv+float2(texel.x,0),key)-SurfaceDistance(uv-float2(texel.x,0),key),
            SurfaceDistance(uv+float2(0,texel.y),key)-SurfaceDistance(uv-float2(0,texel.y),key));
        uv += normal / max(length(normal), .001) * (1.5-distance) / _RasterSize;
    }
    return surface.SampleLevel(sampler_linear_clamp,uv,0).rgb;
}

float4 WarpMorphSurface(float2 uv, float2 qa, float2 qb)
{
    // A separately solved matte can introduce subpixel drift on identical fine texture.
    // Keep the exact source when the matte, original colors and their neighbourhood agree.
    bool stationary = false;
    float4 aligned = 0;
    if (length((qa-qb)*_RasterSize) < 2)
    {
        float4 first = _MainTex.SampleLevel(sampler_linear_clamp,uv,0);
        float4 second = _ToTex.SampleLevel(sampler_linear_clamp,uv,0);
        if (all(first == second))
        {
            float error = 0;
            [unroll] for (int k = 0; k < 3; k++)
            {
                float2 q = uv + float2(k == 1, k == 2) / _RasterSize;
                error += EndpointDifference(_SurfaceKeyA.SampleLevel(sampler_linear_clamp,q,0),
                    _SurfaceKeyB.SampleLevel(sampler_linear_clamp,q,0));
            }
            stationary = error == 0;
            aligned = first;
        }
    }
    float da = SurfaceDistance(qa,0), db = SurfaceDistance(qb,1);
    float t = _MorphTime, d = lerp(da,db,t);
    float2 weights = float2(1-t,t) * exp2(max(-15, min(0, float2(da,db)) / 6));
    float blend = weights.y / max(1e-7, weights.x+weights.y);
    float aa = _SurfaceKeyA.SampleLevel(sampler_linear_clamp,qa,0).a;
    float ab = _SurfaceKeyB.SampleLevel(sampler_linear_clamp,qb,0).a;
    float ma = _MainTex.SampleLevel(sampler_linear_clamp,qa,0).a / max(aa,1e-7);
    float mb = _ToTex.SampleLevel(sampler_linear_clamp,qb,0).a / max(ab,1e-7);
    float width = max(1.5, min(_RasterSize.x,_RasterSize.y)*.006);
    float profile = saturate(.5+d/width);
    float2 residual = float2(ma,mb)-saturate(.5+float2(da,db)/width);
    residual *= exp2(-abs(d-float2(da,db))/width);
    float coverage = saturate(profile+lerp(residual.x,residual.y,t));
    float alpha = lerp(aa,ab,blend);
    float3 ca = ExtendSurface(_MainTex,qa,da,0), cb = ExtendSurface(_ToTex,qb,db,1);
    // Preserve the unmixed colors of an already aligned antialiased edge.
    if (abs(da-db) < .01 && abs(ma-mb) < .002)
    {
        ca = _MainTex.SampleLevel(sampler_linear_clamp,qa,0).rgb;
        cb = _ToTex.SampleLevel(sampler_linear_clamp,qb,0).rgb;
    }
    float3 rgb = lerp(ca*aa,cb*ab,blend) / max(alpha,1e-7);
    return stationary ? aligned : float4(rgb,alpha*coverage);
}

float4 CombineMorphLayers(Varyings input):SV_Target
{
    float4 original=_MainTex.SampleLevel(sampler_linear_clamp,input.uv,0);
    float4 f=_FrontLayer.SampleLevel(sampler_linear_clamp,input.uv,0);
    float4 b=_BackLayer.SampleLevel(sampler_linear_clamp,input.uv,0);
    float back=min(1-saturate(f.a),b.a);
    float missing=max(0,original.a-(f.a+back));
    float a=f.a+back+missing;
    return float4((f.rgb*f.a+b.rgb*back+lerp(original.rgb,b.rgb,step(1e-6,b.a))*missing)/max(1e-7,a),a);
}

float4 MorphToneDifference(Varyings input):SV_Target
{
    float4 a=_MainTex.SampleLevel(sampler_linear_clamp,input.uv,0);
    float4 b=_CorrectionReference.SampleLevel(sampler_linear_clamp,input.uv,0);
    float w=min(a.a,b.a);
    return float4((b.rgb-a.rgb)*w,w);
}
float4 MorphToneBlur(Varyings input):SV_Target
{
    float4 sum=0; float total=0;
    [unroll] for(int i=-4;i<=4;i++)
    {
        float w=exp2(-.22*i*i);
        sum+=w*_MainTex.SampleLevel(sampler_linear_clamp,input.uv+i*_BlurStep,0);
        total+=w;
    }
    return sum/total;
}
float4 ApplyMorphCorrection(Varyings input):SV_Target
{
    float2 uv=input.uv;
    float4 a=_MainTex.SampleLevel(sampler_linear_clamp,uv,0);
    float2 weights=_CorrectionCoverage.SampleLevel(sampler_linear_clamp,uv,0).rg;
    float4 b=_CorrectionReference.SampleLevel(sampler_linear_clamp,uv,0);
    float4 low=_CorrectionTone.SampleLevel(sampler_linear_clamp,uv,0);
    if (max(weights.x, weights.y) <= 0) return a;
    // Keep the detail from the layered image. Per-pixel switching of high-frequency
    // residuals here would stamp patches of the other texture onto a lighting seam.
    a.rgb += weights.y * min(a.a,b.a) * 4*_MorphTime*(1-_MorphTime) * low.rgb/max(.0001,low.a);
    float alpha=lerp(a.a,b.a,weights.x);
    return float4(lerp(a.rgb*a.a,b.rgb*b.a,weights.x)/max(alpha,1e-7),alpha);
}
