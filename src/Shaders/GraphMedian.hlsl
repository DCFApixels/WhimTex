#ifndef WT_GRAPH_MEDIAN_INCLUDED
#define WT_GRAPH_MEDIAN_INCLUDED
// Ordered float bits are used only to choose pivots. Results are original float32
// samples, including signed and HDR values; no quantization or color weighting.
uint4 GraphMedianOrder(float4 value)
{
    uint4 bits=asuint(value);
    return bits ^ ((bits & 0x80000000u)!=0 ? 0xffffffffu : 0x80000000u);
}
float4 GraphMedianFloat(uint4 order)
{
    return asfloat(order ^ ((order & 0x80000000u)!=0 ? 0x80000000u : 0xffffffffu));
}
float4 GraphMedianPivot(float4 low,float4 high)
{
    uint4 a=GraphMedianOrder(low),b=GraphMedianOrder(high);
    return GraphMedianFloat(a+((b-a)>>1));
}

// SAMPLE evaluates sampleUV, with the caller's UV/loop context intact.
#define GRAPH_MEDIAN_SMALL(SAMPLE) \
    radius=min(radius,2u); \
    uint side=2*radius+1, count=side*side; \
    float4 sorted[25]; \
    [unroll] for(uint medianClear=0;medianClear<25;medianClear++) sorted[medianClear]=0; \
    float2 sampleUV=center-radius*texel; sorted[0]=SAMPLE; \
    [loop] for(uint medianSample=1;medianSample<count;medianSample++) { \
        sampleUV=center+(float2(medianSample%side,medianSample/side)-radius)*texel; \
        float4 candidate=SAMPLE; \
        [loop] for(uint medianInsert=0;medianInsert<medianSample;medianInsert++) { \
            float4 previous=sorted[medianInsert]; sorted[medianInsert]=min(previous,candidate); candidate=max(previous,candidate); \
        } \
        sorted[medianSample]=candidate; \
    } \
    return sorted[count/2];

// Exact constant-storage fallback for varying coordinates/radii and sampled scopes.
// Tightening both bounds to observed samples makes flat/low-cardinality areas cheap.
#define GRAPH_MEDIAN_DENSE(SAMPLE) \
    uint side=2*radius+1,count=side*side,rank=count/2; \
    float4 low=3.402823466e38,high=-3.402823466e38; \
    [loop] for(uint medianInit=0;medianInit<count;medianInit++) { \
        float2 sampleUV=center+(float2(medianInit%side,medianInit/side)-radius)*texel; \
        float4 value=SAMPLE; low=min(low,value); high=max(high,value); \
    } \
    [loop] for(uint medianPass=0;medianPass<32 && any(low<high);medianPass++) { \
        float4 pivot=GraphMedianPivot(low,high),below=low,above=high; uint4 counts=0; \
        [loop] for(uint medianScan=0;medianScan<count;medianScan++) { \
            float2 sampleUV=center+(float2(medianScan%side,medianScan/side)-radius)*texel; \
            float4 value=SAMPLE; bool4 lower=value<=pivot; counts+=(uint4)lower; \
            below=max(below,lower?value:low); above=min(above,lower?high:value); \
        } \
        bool4 takeLower=counts>rank; high=takeLower?below:high; low=takeLower?low:above; \
    } \
    return low;
#endif
