// Intentionally included once per value type; shared by public overloads and graph templates.
// @wt.feature(Bounded)
// @wt.feature(Ridged)
// @wt.feature(PingPong)
// @wt.feature(Product)
// @wt.feature(Hybrid)
// @wt.feature(Height)
// @wt.feature(RidgedCascade)
// @wt.block(Fold)
WTFractalValue WTFractalInternalFold(WTFractalValue value,WTNoiseFractal f,bool bounded,out WTFractalValue slope)
{
    slope=(WTFractalValue)1;
    // @wt.if(Bounded)
    if(bounded) { slope=(WTFractalValue)(value>0)*(WTFractalValue)(value<1); value=saturate(value); }
    // @wt.endif
    // @wt.if(Ridged)
    if(f.mode==WT_NOISE_RIDGED)
    { slope*=-2*sign(value*2-1); value=1-abs(value*2-1); }
    // @wt.endif
    // @wt.if(PingPong)
    if(f.mode==WT_NOISE_PINGPONG)
    {
        float strength=max(f.pingPongStrength,0);
        WTFractalValue t=value*2*strength; t-=floor(t*0.5)*2;
        slope*=(WTFractalValue)(t!=0)*(-2*strength)*sign(t-1); value=1-abs(t-1);
    }
    // @wt.endif
    return value;
}
// @wt.end
// @wt.block(Weight)
WTFractalValue WTFractalInternalWeight(WTFractalValue value,WTNoiseFractal f)
{ return saturate(f.gain)*lerp((WTFractalValue)1,value,saturate(f.weightedStrength)); }
// @wt.end
// @wt.block(Accumulate; Fold, Weight)
void WTFractalInternalAccumulate(WTFractalValue value,WTNoiseFractal f,float visibility,int octave,
    inout WTFractalValue sum,inout WTFractalValue total,inout WTFractalValue amplitude)
{
    WTFractalValue ignored; value=WTFractalInternalFold(value,f,false,ignored);
    if(octave==0) visibility=max(visibility,0.000001);
    sum+=value*amplitude*visibility; total+=amplitude*visibility;
    amplitude*=WTFractalInternalWeight(saturate(value),f);
}
// @wt.end
// @wt.block(Resolve)
WTFractalValue WTFractalInternalResolve(WTFractalValue sum,WTFractalValue total) { return sum/max(total,(WTFractalValue)0.0000001); }
// @wt.end

// @wt.block(Cascade)
void WTFractalInternalCascade(WTFractalValue value,int mode,float gain,float bias,float feedback,float visibility,int octave,
    inout WTFractalValue result,inout WTFractalValue weight,inout float amplitude)
{
    WTFractalValue signal=value*2-1;
    float visible=saturate(visibility);
    // @wt.if(Product)
    if(mode==0)
    {
        if(octave==0) result=(WTFractalValue)1;
        result*=1+signal*amplitude*visible;
    }
    // @wt.endif
    // @wt.if(Hybrid)
    if(mode==1)
    {
        signal=(signal+bias)*amplitude;
        weight=saturate(weight);
        result+=weight*signal*visible;
        weight*=lerp((WTFractalValue)1,signal*max(feedback,0),visible);
    }
    // @wt.endif
    // @wt.if(Height)
    if(mode==2)
    {
        signal+=bias;
        if(octave==0) result=signal*visible;
        else result+=result*signal*amplitude*visible;
    }
    // @wt.endif
    // @wt.if(RidgedCascade)
    if(mode!=0 && mode!=1 && mode!=2)
    {
        signal=bias-abs(signal); signal*=signal;
        if(octave>0) signal*=saturate(weight*max(feedback,0));
        result+=signal*amplitude*visible;
        weight=signal;
    }
    // @wt.endif
    amplitude*=saturate(gain);
}
// @wt.end
