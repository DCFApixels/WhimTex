using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesColor;
        void ValidateColorOptions(ShaderGraphNode node)
        {
            if (node.kind == ShaderGraphNodeKind.ConvertColor &&
                (!Enum.IsDefined(typeof(ShaderGraphColorSpace), node.inputColorSpace) || !Enum.IsDefined(typeof(ShaderGraphColorSpace), node.outputColorSpace)))
                Error(node, "Unknown color space.");
            if (ShaderGraphNodes.UsesChannel(node.kind) && !Enum.IsDefined(typeof(ShaderGraphChannel), node.channel)) Error(node, "Unknown source channel.");
            if (ShaderGraphNodes.UsesHeightTexture(node.kind) && !Enum.IsDefined(typeof(ShaderGraphDerivative), node.derivative)) Error(node, "Unknown height derivative.");
            if (ShaderGraphNodes.UsesHeightTexture(node.kind) && !Enum.IsDefined(typeof(ShaderGraphHeightSource), node.heightSource)) Error(node, "Unknown height source.");
            if (ShaderGraphNodes.UsesNormalEncoding(node.kind) && !Enum.IsDefined(typeof(ShaderGraphNormalEncoding), node.normalEncoding)) Error(node, "Unknown normal encoding.");
        }

        Value EmitColor(ShaderGraphNode node)
        {
            usesColor = true;
            string V(string name) => Input(node, name).text;
            ShaderGraphNodes.TryColorOutputType(node.kind, out var type);
            string text;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Hue: text = $"GraphHue({V("Color")},{V("Degrees")})"; break;
                case ShaderGraphNodeKind.Saturation: text = $"GraphSaturation({V("Color")},{V("Amount")})"; break;
                case ShaderGraphNodeKind.Exposure: text = $"float4({V("Color")}.rgb*exp2({V("Stops")}),{V("Color")}.a)"; break;
                case ShaderGraphNodeKind.Lightness: text = $"GraphLightness({V("Color")},{V("Amount")})"; break;
                case ShaderGraphNodeKind.Levels: text = $"GraphLevels({V("Color")},{V("InputRange")},{V("Gamma")},{V("OutputRange")},{V("Clamp")},{node.option})"; break;
                case ShaderGraphNodeKind.ColorBalance: text = $"GraphColorBalance({V("Color")},{V("Shadows")},{V("Midtones")},{V("Highlights")},{V("ShadowRange")},{V("HighlightRange")},{V("PreserveLuminance")})"; break;
                case ShaderGraphNodeKind.Posterize: text = $"GraphPosterize({V("Color")},{V("Levels")})"; break;
                case ShaderGraphNodeKind.ConvertColor: text = $"GraphConvertColor({V("Color")},{(int)node.inputColorSpace},{(int)node.outputColorSpace})"; break;
                case ShaderGraphNodeKind.Luminance: text = $"GraphLuminance({V("Color")}.rgb)"; break;
                case ShaderGraphNodeKind.GradientMap:
                    string mapped = "gm" + node.id;
                    string color = V("Color"), gradient = V("Gradient");
                    body.Append("float4 ").Append(mapped).Append(" = ").Append(gradient).Append("_Sample(GraphChannel(")
                        .Append(color).Append(',').Append((int)node.channel).AppendLine("));");
                    text = $"float4({mapped}.rgb,{(node.option == 0 ? color + ".a" : node.option == 1 ? mapped + ".a*" + color + ".a" : mapped + ".a")})"; break;
                case ShaderGraphNodeKind.ColorCurves: text = EmitColorCurves(node, V("Color")); break;
                case ShaderGraphNodeKind.ColorMask: text = $"GraphColorMask({V("Color")}.rgb,{V("KeyColor")}.rgb,{V("Tolerance")},{V("Softness")},{node.option})"; break;
                case ShaderGraphNodeKind.HueRange: text = $"GraphHueRange({V("Color")}.rgb,{V("Range")},{V("Feather")},{V("SaturationRange")})"; break;
                case ShaderGraphNodeKind.MixColor: text = $"GraphMixColor({V("A")},{V("B")},{V("T")},{node.option})"; break;
                case ShaderGraphNodeKind.SetAlpha: text = $"float4({V("Color")}.rgb,{V("Alpha")}{(node.option == 1 ? "*" + V("Color") + ".a" : "")})"; break;
                case ShaderGraphNodeKind.HeightToNormal:
                case ShaderGraphNodeKind.HeightToDistortion:
                    string slope = node.heightSource == ShaderGraphHeightSource.Value ? EmitHeightFieldSlope(node)
                        : $"GraphHeightSlope({V("Height")},{V("UV")},{V("Radius")},{(int)node.channel},{(int)node.derivative},{(int)node.wrap},{(int)node.filter})";
                    slope += $"*{V("Strength")}*float2({V("FlipX")}? -1.0:1.0,{V("FlipY")}? -1.0:1.0)";
                    text = node.kind == ShaderGraphNodeKind.HeightToNormal
                        ? $"GraphEncodeNormal(GraphUnitNormal(float3(-({slope}),1)),{(int)node.normalEncoding})"
                        : $"(({slope}){(node.option == 1 ? "*0.5+0.5" : "")})"; break;
                case ShaderGraphNodeKind.NormalBlend: text = $"GraphNormalBlend({V("Base")},{V("Detail")},{V("Strength")},{node.option},{(int)node.normalEncoding})"; break;
                case ShaderGraphNodeKind.NormalToDistortion:
                    text = $"((GraphDecodeNormal({V("Normal")},{(int)node.normalEncoding}).xy*{V("Strength")}*float2({V("FlipX")}? -1.0:1.0,{V("FlipY")}? -1.0:1.0)){(node.option == 1 ? "*0.5+0.5" : "")})"; break;
                default: throw new ShaderGraphException(node.id, "Unknown color node.");
            }
            return new Value(type, text);
        }

        string EmitColorCurves(ShaderGraphNode node, string color)
        {
            string variable = "gc" + node.id;
            body.Append("float4 ").Append(variable).Append(" = ").Append(color).AppendLine(";");
            string Curve(string port, string value)
            {
                if (!incoming.ContainsKey(node.id + ":" + port)) return value;
                string curve = Input(node, port).text;
                return $"({value}+{curve}_Sample(saturate({value}))-saturate({value}))";
            }
            if (incoming.ContainsKey(node.id + ":Master"))
            {
                if (node.option == 0)
                    body.Append(variable).Append(".rgb = float3(").Append(Curve("Master", variable + ".r")).Append(',')
                        .Append(Curve("Master", variable + ".g")).Append(',').Append(Curve("Master", variable + ".b")).AppendLine(");");
                else
                {
                    string luma = variable + "_luma";
                    body.Append("float ").Append(luma).Append(" = GraphLuminance(").Append(variable).AppendLine(".rgb);");
                    body.Append(variable).Append(".rgb = GraphMapLuminance(").Append(variable).Append(".rgb,").Append(luma).Append(',').Append(Curve("Master", luma)).AppendLine(");");
                }
            }
            body.Append(variable).Append(" = float4(").Append(Curve("Red", variable + ".r")).Append(',')
                .Append(Curve("Green", variable + ".g")).Append(',').Append(Curve("Blue", variable + ".b")).Append(',')
                .Append(Curve("Alpha", variable + ".a")).AppendLine(");");
            return variable;
        }

        const string ColorHelpers = @"
float GraphLuminance(float3 rgb) { return dot(rgb,float3(0.2126,0.7152,0.0722)); }
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
float GraphReadHeight(WhimTexTexture tex,float2 uv,int channel,int wrap,int filter)
{
    return GraphChannel(GraphSample(tex,uv,wrap,filter),channel);
}
float2 GraphHeightSlope(WhimTexTexture tex,float2 uv,float radius,int channel,int derivative,int wrap,int filter)
{
    radius=max(radius,0.0001); float2 stepUV=radius/max(tex.texelSize.zw,1);
    float l=GraphReadHeight(tex,uv-float2(stepUV.x,0),channel,wrap,filter);
    float r=GraphReadHeight(tex,uv+float2(stepUV.x,0),channel,wrap,filter);
    float b=GraphReadHeight(tex,uv-float2(0,stepUV.y),channel,wrap,filter);
    float t=GraphReadHeight(tex,uv+float2(0,stepUV.y),channel,wrap,filter);
    if(derivative==2) return float2(r-l,t-b)/(2*radius);
    float tl=GraphReadHeight(tex,uv+stepUV*float2(-1,1),channel,wrap,filter);
    float tr=GraphReadHeight(tex,uv+stepUV,channel,wrap,filter);
    float bl=GraphReadHeight(tex,uv-stepUV,channel,wrap,filter);
    float br=GraphReadHeight(tex,uv+stepUV*float2(1,-1),channel,wrap,filter);
    float side=derivative==0 ? 3:1, center=derivative==0 ? 10:2;
    return float2(side*(br+tr-bl-tl)+center*(r-l),side*(tl+tr-bl-br)+center*(t-b))/((4*side+2*center)*radius);
}
";
    }
}
