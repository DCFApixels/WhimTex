float GraphLuminance(float3 rgb) { return dot(rgb,float3(0.2126,0.7152,0.0722)); }
float3 GraphWhiteResponse(float x,float y)
{
    float3 xyz=float3(x/y,1,(1-x-y)/y);
    return float3(dot(xyz,float3(.8951,.2664,-.1614)),dot(xyz,float3(-.7502,1.7135,.0367)),dot(xyz,float3(.0389,-.0685,1.0296)));
}
float4 GraphWhiteBalance(float4 color,float temperature,float tint)
{
    temperature=clamp(temperature,-1,1); tint=clamp(tint,-1,1);
    if(temperature==0 && tint==0) return color;
    float x=.3127-temperature*.05;
    float y=2.87*x-3*x*x-.27509507+tint*.05;
    float3 gain=GraphWhiteResponse(.3127,.3290)/GraphWhiteResponse(x,y);
    float3 response=float3(dot(color.rgb,float3(.4227252927,.4913453244,.0273579445)),
        dot(color.rgb,float3(.0556997770,.9615340509,.0231838105)),dot(color.rgb,float3(.0213826437,.0876418678,.9805081326)))*gain;
    return float4(dot(response,float3(2.5380445168,-1.2932769979,-.0402368843)),
        dot(response,float3(-.1460040576,1.1166483055,-.0223290262)),dot(response,float3(-.0422985104,-.0716072203,1.0227526883)),color.a);
}
float GraphChannel(float4 c,int channel)
{
    if(channel == 0) return GraphLuminance(c.rgb);
    if(channel == 1) return c.r; if(channel == 2) return c.g; if(channel == 3) return c.b; return c.a;
}
float3 GraphMapLuminance(float3 rgb,float oldValue,float newValue)
{
    return abs(oldValue)>1e-6 ? rgb*(newValue/oldValue) : rgb+(newValue-oldValue);
}
float3 GraphHueRGB(float hue)
{
    return saturate(abs(frac(hue+float3(0,2.0/3.0,1.0/3.0))*6-3)-1);
}
float GraphHueValue(float3 rgb,float chroma,float maximum)
{
    if(chroma<=0) return 0;
    float hue = maximum==rgb.r ? (rgb.g-rgb.b)/chroma : maximum==rgb.g ? 2+(rgb.b-rgb.r)/chroma : 4+(rgb.r-rgb.g)/chroma;
    return frac(hue/6);
}
float3 GraphRGBToHSV(float3 rgb)
{
    rgb=max(rgb,0); float maximum=max(rgb.r,max(rgb.g,rgb.b)), minimum=min(rgb.r,min(rgb.g,rgb.b));
    float chroma=maximum-minimum;
    return float3(GraphHueValue(rgb,chroma,maximum),maximum>0 ? chroma/maximum : 0,maximum);
}
float3 GraphHSVToRGB(float3 hsv) { return max(hsv.z,0)*lerp(1,GraphHueRGB(hsv.x),saturate(hsv.y)); }
float3 GraphRGBToHSL(float3 rgb)
{
    rgb=saturate(rgb); float maximum=max(rgb.r,max(rgb.g,rgb.b)), minimum=min(rgb.r,min(rgb.g,rgb.b));
    float chroma=maximum-minimum, lightness=(maximum+minimum)*0.5;
    return float3(GraphHueValue(rgb,chroma,maximum),chroma/max(1-abs(2*lightness-1),1e-20),lightness);
}
float3 GraphHSLToRGB(float3 hsl)
{
    float lightness=saturate(hsl.z), chroma=(1-abs(2*lightness-1))*saturate(hsl.y);
    return (GraphHueRGB(hsl.x)-0.5)*chroma+lightness;
}
float3 GraphRGBToOklab(float3 rgb)
{
    float3 lms=float3(dot(rgb,float3(.4122214708,.5363325363,.0514459929)),
        dot(rgb,float3(.2119034982,.6806995451,.1073969566)),dot(rgb,float3(.0883024619,.2817188376,.6299787005)));
    lms=sign(lms)*pow(abs(lms),1.0/3.0);
    return float3(dot(lms,float3(.2104542553,.7936177850,-.0040720468)),
        dot(lms,float3(1.9779984951,-2.4285922050,.4505937099)),dot(lms,float3(.0259040371,.7827717662,-.8086757660)));
}
float3 GraphOklabToRGB(float3 lab)
{
    float3 lms=float3(lab.x+.3963377774*lab.y+.2158037573*lab.z,
        lab.x-.1055613458*lab.y-.0638541728*lab.z,lab.x-.0894841775*lab.y-1.2914855480*lab.z);
    lms=lms*lms*lms;
    return float3(dot(lms,float3(4.0767416621,-3.3077115913,.2309699292)),
        dot(lms,float3(-1.2684380046,2.6097574011,-.3413193965)),dot(lms,float3(-.0041960863,-.7034186147,1.7076147010)));
}
float4 GraphConvertColor(float4 c,int from,int to)
{
    if(from==to) return c;
    float3 rgb=c.rgb;
    if(from==1) rgb=SpriteDecode(rgb);
    else if(from==2) rgb=SpriteDecode(GraphHSVToRGB(rgb));
    else if(from==3) rgb=SpriteDecode(GraphHSLToRGB(rgb));
    else if(from==4) rgb=GraphOklabToRGB(rgb);
    if(to==1) rgb=SpriteEncode(rgb);
    else if(to==2) rgb=GraphRGBToHSV(SpriteEncode(rgb));
    else if(to==3) rgb=GraphRGBToHSL(SpriteEncode(rgb));
    else if(to==4) rgb=GraphRGBToOklab(rgb);
    return float4(rgb,c.a);
}
float4 GraphHue(float4 c,float degrees)
{
    if(degrees==0) return c;
    float3 rgb=SpriteEncode(c.rgb); float minimum=min(rgb.r,min(rgb.g,rgb.b)), maximum=max(rgb.r,max(rgb.g,rgb.b));
    float chroma=maximum-minimum;
    return float4(SpriteDecode(GraphHueRGB(GraphHueValue(rgb,chroma,maximum)+degrees/360)*chroma+minimum),c.a);
}
float4 GraphSaturation(float4 c,float amount)
{
    if(amount==1) return c;
    float3 rgb=SpriteEncode(c.rgb); float gray=GraphLuminance(rgb);
    return float4(SpriteDecode(lerp(gray.xxx,rgb,max(amount,0))),c.a);
}
float4 GraphLightness(float4 c,float amount)
{
    if(amount==0) return c;
    float lift=clamp(amount*0.01,-1,1); float3 rgb=SpriteEncode(c.rgb);
    return float4(SpriteDecode(lift<0 ? rgb*(1+lift) : lerp(rgb,1,lift)),c.a);
}
float GraphLevelValue(float value,float2 inputRange,float gamma,float2 outputRange,bool bounded)
{
    float t=inputRange.y<=inputRange.x ? step(inputRange.x,value) : (value-inputRange.x)/(inputRange.y-inputRange.x);
    if(bounded) t=saturate(t);
    t=sign(t)*pow(abs(t),1/max(gamma,0.0001));
    return lerp(outputRange.x,outputRange.y,t);
}
float4 GraphLevels(float4 c,float2 inputRange,float gamma,float2 outputRange,bool bounded,int mode)
{
    float3 rgb;
    if(mode==0)
    {
        float luma=GraphLuminance(c.rgb);
        rgb=GraphMapLuminance(c.rgb,luma,GraphLevelValue(luma,inputRange,gamma,outputRange,bounded));
    }
    else rgb=float3(GraphLevelValue(c.r,inputRange,gamma,outputRange,bounded),
        GraphLevelValue(c.g,inputRange,gamma,outputRange,bounded),GraphLevelValue(c.b,inputRange,gamma,outputRange,bounded));
    return float4(rgb,c.a);
}
float4 GraphColorBalance(float4 c,float3 shadows,float3 midtones,float3 highlights,float shadowRange,float highlightRange,bool preserveLuminance)
{
    float luma=GraphLuminance(c.rgb);
    float sw=shadowRange>0 ? 1-smoothstep(0,shadowRange,luma) : 0;
    float hw=highlightRange>0 ? smoothstep(1-highlightRange,1,luma) : 0;
    float3 shift=shadows*sw+midtones*max(0,1-sw-hw)+highlights*hw;
    if(preserveLuminance) shift-=GraphLuminance(shift);
    return float4(c.rgb+shift,c.a);
}
float4 GraphPosterize(float4 c,float levels)
{
    float steps=max(floor(levels),2)-1;
    return float4(floor(c.rgb*steps+0.5)/steps,c.a);
}
float GraphColorMask(float3 rgb,float3 key,float tolerance,float softness,int mode)
{
    float distance;
    if(mode==2) distance=length(GraphRGBToOklab(rgb)-GraphRGBToOklab(key));
    else
    {
        float3 difference=SpriteEncode(rgb)-SpriteEncode(key);
        if(mode==1) distance=length(difference)*0.5773502692;
        else
        {
            float luma=GraphLuminance(difference);
            distance=length(float2((difference.b-luma)/1.8556,(difference.r-luma)/1.5748))*0.7071067812;
        }
    }
    tolerance=max(tolerance,0);
    return softness>0 ? 1-smoothstep(tolerance,tolerance+softness,distance) : (distance<=tolerance ? 1:0);
}
float GraphHueRange(float3 rgb,float2 range,float feather,float2 saturationRange)
{
    float3 hsv=GraphRGBToHSV(SpriteEncode(rgb));
    float span=range.y-range.x, width=abs(span)>=360 ? 360 : span-floor(span/360)*360;
    float distance=abs(frac(hsv.x-(range.x+width*0.5)/360+0.5)-0.5)*360;
    float radius=width*0.5, falloff=min(clamp(feather,0,180),180-radius);
    float mask=width>=360 ? 1 : falloff>0 ? 1-smoothstep(radius,radius+falloff,distance) : (width>0 && distance<=radius ? 1:0);
    float2 gate=saturate(saturationRange);
    return mask*(gate.y<=gate.x ? step(gate.x,hsv.y) : smoothstep(gate.x,gate.y,hsv.y));
}
float4 GraphMixColor(float4 a,float4 b,float t,int mode)
{
    if(t==0) return a; if(t==1) return b;
    float3 rgb=mode==2 ? SpriteDecode(lerp(SpriteEncode(a.rgb),SpriteEncode(b.rgb),t)) :
        mode==0 ? GraphOklabToRGB(lerp(GraphRGBToOklab(a.rgb),GraphRGBToOklab(b.rgb),t)) : lerp(a.rgb,b.rgb,t);
    return float4(rgb,lerp(a.a,b.a,t));
}
float3 GraphUnitNormal(float3 n)
{
    float scale=max(abs(n.x),max(abs(n.y),abs(n.z)));
    if(scale<1e-20) return float3(0,0,1);
    n/=scale; return n*rsqrt(dot(n,n));
}
float3 GraphDecodeNormal(float3 n,int encoding) { return encoding==0 ? n*2-1 : n; }
float3 GraphEncodeNormal(float3 n,int encoding) { return encoding==0 ? n*0.5+0.5 : n; }
float3 GraphNormalBlend(float3 baseNormal,float3 detailNormal,float strength,int mode,int encoding)
{
    float3 b=GraphUnitNormal(GraphDecodeNormal(baseNormal,encoding));
    float3 d=GraphUnitNormal(GraphDecodeNormal(detailNormal,encoding));
    d=GraphUnitNormal(float3(d.xy*strength,lerp(1,d.z,saturate(abs(strength)))));
    float3 blended;
    if(mode==1) blended=float3(b.xy+d.xy,b.z*d.z);
    else if(b.z < -0.999999) blended=float3(d.x,-d.y,-d.z);
    else
    {
        float3 t=b+float3(0,0,1), u=d*float3(-1,-1,1);
        blended=t*dot(t,u)-u*t.z;
    }
    return GraphEncodeNormal(GraphUnitNormal(blended),encoding);
}
