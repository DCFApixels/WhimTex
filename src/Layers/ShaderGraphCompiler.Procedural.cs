using System;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesProcedural;
        Value ProceduralShared(ShaderGraphNode n, ShaderGraphValueType type, Func<string> expression)
        {
            string key = n.id + ":$procedural";
            if (values.TryGetValue(key, out var cached)) return cached;
            string text = expression();
            string variable = "gp" + values.Count;
            body.Append("#line 1 \"").Append(n.id).AppendLine("\"");
            body.Append(Type(type)).Append(' ').Append(variable).Append(" = ").Append(text).AppendLine(";");
            var result = new Value(type, variable); values.Add(key, result); return result;
        }
        Value EmitProcedural(ShaderGraphNode n, string port)
        {
            usesProcedural = true;
            string V(string name) => Input(n, name).text;
            var type = ShaderGraphValueType.Float;
            string text;
            switch (n.kind)
            {
                case ShaderGraphNodeKind.Noise:
                    text = $"GraphFractalNoise({V("UV")}*{V("Scale")},{V("Seed")},{n.option},{(int)n.fractal},{V("Octaves")},{V("Lacunarity")},{V("Gain")},{V("PingPongStrength")})"; break;
                case ShaderGraphNodeKind.Cellular:
                    var cellular = ProceduralShared(n, ShaderGraphValueType.Float3, () => $"GraphCellular({V("UV")}*{V("Scale")},{V("Seed")},{V("Jitter")},{n.option})");
                    text = cellular.text + (port == "F1" ? ".x" : port == "F2" ? ".y" : ".z"); break;
                case ShaderGraphNodeKind.Random: text = $"GraphRandom({V("Coordinates")},{V("Seed")})"; break;
                case ShaderGraphNodeKind.Checkerboard: text = $"GraphCheckerboard({V("UV")}*{V("Scale")}+{V("Offset")})"; break;
                case ShaderGraphNodeKind.Stripes: text = $"GraphStripes({V("UV")},{V("Frequency")},{V("Width")},{V("Angle")},{V("Phase")},{n.option})"; break;
                case ShaderGraphNodeKind.Grid: text = $"GraphGrid({V("UV")}*{V("Scale")}+{V("Offset")},{V("Width")})"; break;
                case ShaderGraphNodeKind.Rings: text = $"GraphPulse(length({V("UV")}-{V("Center")})*{V("Frequency")}+{V("Phase")},{V("Width")})"; break;
                case ShaderGraphNodeKind.GradientFactor: text = $"GraphGradientFactor(({V("UV")}-{V("Center")})*{V("Scale")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Shape:
                    var shape = ProceduralShared(n, ShaderGraphValueType.Float, () => n.option switch
                    {
                        3 or 4 => $"GraphPolygonDistance({V("UV")},{V("Center")},{V("Size")},{V("Sides")},{(n.option == 4 ? V("InnerRadius") : "1.0")},{V("Rotation")},{n.option})",
                        5 => $"GraphCapsuleDistance({V("UV")},{V("Center")},{V("Size")},{V("Rotation")})",
                        6 => $"GraphSegmentDistance({V("UV")},{V("Start")},{V("End")})-max({V("Width")},0)*0.5",
                        _ => $"GraphShapeDistance({V("UV")},{V("Center")},{V("Size")},{V("Radius")},{n.option})"
                    });
                    text = port == "Distance" ? shape.text : $"GraphDistanceMask({shape.text},{V("Feather")})"; break;
                case ShaderGraphNodeKind.CombineDistance:
                    var distance = ProceduralShared(n, ShaderGraphValueType.Float, () => $"GraphCombineDistance({V("A")},{V("B")},{V("Smoothness")},{n.option})");
                    text = port == "Mask" ? $"GraphDistanceMask({distance.text},{V("Feather")})" : distance.text; break;
                case ShaderGraphNodeKind.PolarCoordinates: type = ShaderGraphValueType.Float2; text = $"GraphPolar({V("UV")},{V("Center")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Repeat:
                    type = ShaderGraphValueType.Float2;
                    var repeat = ProceduralShared(n, ShaderGraphValueType.Float4, () => $"GraphRepeat({V("UV")}*{V("Scale")}+{V("Offset")})");
                    text = repeat.text + (port == "Cell" ? ".zw" : ".xy"); break;
                case ShaderGraphNodeKind.Mirror: type = ShaderGraphValueType.Float2; text = $"GraphMirror({V("UV")},{V("Center")},{n.option})"; break;
                case ShaderGraphNodeKind.Kaleidoscope: type = ShaderGraphValueType.Float2; text = $"GraphKaleidoscope({V("UV")},{V("Center")},{V("Sectors")},{V("Angle")})"; break;
                case ShaderGraphNodeKind.DomainWarp:
                    type = ShaderGraphValueType.Float2;
                    var warp = ProceduralShared(n, ShaderGraphValueType.Float4, () => $"GraphDomainWarp({V("UV")},{V("Scale")},{V("Offset")},{V("Seed")},{V("Strength")},{n.option},{(int)n.fractal},{V("Octaves")},{V("Lacunarity")},{V("Gain")})");
                    text = warp.text + (port == "Displacement" ? ".zw" : ".xy"); break;
                case ShaderGraphNodeKind.VectorDisplacement:
                    type = ShaderGraphValueType.Float2; text = $"({V("UV")}+({V("Displacement")}{(n.option == 1 ? "*2.0-1.0" : "")})*{V("Strength")})"; break;
                case ShaderGraphNodeKind.Twirl: case ShaderGraphNodeKind.BulgePinch:
                    type = ShaderGraphValueType.Float2; text = $"GraphRadialWarp({V("UV")},{V("Center")},{V("Strength")},{V("Radius")},{(n.kind == ShaderGraphNodeKind.Twirl ? 0 : 1)})"; break;
                case ShaderGraphNodeKind.Wave:
                    type = ShaderGraphValueType.Float2; text = $"GraphWaveWarp({V("UV")},{V("Strength")},{V("Frequency")},{V("Direction")},{V("Phase")})"; break;
                case ShaderGraphNodeKind.Ripple:
                    type = ShaderGraphValueType.Float2; text = $"GraphRipple({V("UV")},{V("Center")},{V("Strength")},{V("Frequency")},{V("Radius")},{V("Phase")})"; break;
                default: throw new ShaderGraphException(n.id, "Unknown procedural node.");
            }
            return new Value(type, text);
        }
        const string ProceduralHelpers = @"
float2 GraphDirection(float angle) { float s,c; sincos(radians(angle),s,c); return float2(c,s); }
float2 GraphRotatePoint(float2 p, float angle) { float2 d=GraphDirection(angle); return float2(d.x*p.x-d.y*p.y,d.y*p.x+d.x*p.y); }
float GraphAngle(float2 p) { return dot(p,p)>1e-20 ? atan2(p.y,p.x) : 0; }
uint GraphHash(uint x) { x^=x>>16; x*=0x7feb352du; x^=x>>15; x*=0x846ca68bu; return x^(x>>16); }
uint GraphHashPoint(float2 p, float seed)
{
    p=float2(p.x==0 ? 0.0 : p.x,p.y==0 ? 0.0 : p.y);
    return GraphHash(asuint(p.x)^GraphHash(asuint(p.y))^GraphHash(asuint(seed)));
}
float GraphHashUnit(uint x) { return (x>>8)*(1.0/16777216.0); }
float GraphRandom(float2 p, float seed) { return GraphHashUnit(GraphHashPoint(p,seed)); }
float GraphFractalNoise(float2 p,float seed,int kind,int fractal,float octaves,float lacunarity,float gain,float pingPong)
{
    fnl_state state=fnlCreateState((int)clamp(seed,-2147483000.0,2147483000.0));
    state.frequency=1; state.noise_type=kind; state.fractal_type=fractal;
    state.octaves=(int)clamp(round(octaves),1.0,8.0); state.lacunarity=clamp(lacunarity,0.01,16.0);
    state.gain=saturate(gain); state.ping_pong_strength=max(pingPong,0);
    return fnlGetNoise2D(state,p.x,p.y)*0.5+0.5;
}
float3 GraphCellular(float2 p,float seed,float jitter,int metric)
{
    float2 baseCell=floor(p); float f1=1e20,f2=1e20,cellValue=0;
    jitter=saturate(jitter);
    [unroll] for(int y=-2;y<=2;y++) [unroll] for(int x=-2;x<=2;x++)
    {
        float2 cell=baseCell+float2(x,y); uint h=GraphHashPoint(cell,seed);
        float2 samplePoint=cell+0.5+(float2(GraphHashUnit(h),GraphHashUnit(GraphHash(h^0x9e3779b9u)))-0.5)*jitter;
        float2 d=abs(samplePoint-p); float q=dot(d,d);
        float distance=metric<2 ? q : metric==2 ? d.x+d.y : d.x+d.y+q;
        if(distance<f1) { f2=f1; f1=distance; cellValue=GraphHashUnit(GraphHash(h^0x68bc21ebu)); }
        else f2=min(f2,distance);
    }
    if(metric==0) { f1=sqrt(f1); f2=sqrt(f2); }
    return float3(f1,f2,cellValue);
}
float GraphPulseIntegral(float t,float width) { return floor(t)*width+min(frac(t),width); }
float GraphPulse(float t,float width)
{
    width=saturate(width); float fw=max(fwidth(t),1e-5);
    return saturate((GraphPulseIntegral(t+fw*0.5,width)-GraphPulseIntegral(t-fw*0.5,width))/fw);
}
float GraphTriangleIntegral(float t)
{
    float q=frac(t); return floor(t)*0.5+(q<0.5 ? q*q : 2*q-q*q-0.5);
}
float GraphStripes(float2 uv,float frequency,float width,float angle,float phase,int profile)
{
    float t=dot(uv,GraphDirection(angle))*frequency+phase;
    if(profile==0) return GraphPulse(t,width);
    float fw=max(fwidth(t),1e-5);
    if(profile==1) return saturate((GraphTriangleIntegral(t+fw*0.5)-GraphTriangleIntegral(t-fw*0.5))/fw);
    float a=3.14159265359*fw;
    return 0.5+0.5*sin(6.28318530718*t)*sin(a)/a;
}
float GraphCheckerboard(float2 p)
{
    float x=GraphPulse(p.x*0.5,0.5), y=GraphPulse(p.y*0.5,0.5); return x+y-2*x*y;
}
float GraphGrid(float2 p,float2 width)
{
    float x=GraphPulse(p.x+width.x*0.5,width.x),y=GraphPulse(p.y+width.y*0.5,width.y); return 1-(1-x)*(1-y);
}
float GraphGradientFactor(float2 p,float angle,int mode)
{
    if(mode==0) return dot(p,GraphDirection(angle))+0.5;
    if(mode==1) return length(p)*2;
    return frac(GraphAngle(p)/6.28318530718-angle/360.0);
}
float GraphShapeDistance(float2 uv,float2 center,float2 size,float radius,int kind)
{
    float2 halfSize=max(abs(size)*0.5,1e-7); float2 p=uv-center;
    float r=clamp(radius,0,min(halfSize.x,halfSize.y));
    float2 q=abs(p)-halfSize+(kind==2 ? r : 0);
    return kind==0 ? (length(p/halfSize)-1)*min(halfSize.x,halfSize.y) : length(max(q,0))+min(max(q.x,q.y),0)-(kind==2 ? r : 0);
}
float GraphDistanceMask(float distance,float feather)
{
    float edge=max(max(fwidth(distance)*0.5,1e-7),max(feather,0)*0.5); return 1-smoothstep(-edge,edge,distance);
}
float GraphSegmentDistance(float2 p,float2 a,float2 b)
{
    float2 ab=b-a; float h=saturate(dot(p-a,ab)/max(dot(ab,ab),1e-14)); return length(p-a-ab*h);
}
float GraphPolygonDistance(float2 uv,float2 center,float2 size,float sides,float innerRadius,float rotation,int kind)
{
    float2 halfSize=max(abs(size)*0.5,1e-7); float2 p=GraphRotatePoint(uv-center,-rotation)/halfSize;
    float count=clamp(round(sides),kind==4 ? 2.0 : 3.0,64.0); float sector=6.28318530718/count;
    float angle=abs(frac(GraphAngle(p)/sector+0.5)-0.5)*sector;
    float2 q=length(p)*float2(cos(angle),sin(angle)); float2 a=float2(1,0);
    float endpoint=kind==4 ? sector*0.5 : sector;
    float2 b=float2(cos(endpoint),sin(endpoint))*(kind==4 ? clamp(innerRadius,0.001,1.0) : 1.0);
    float2 ab=b-a, aq=q-a; float cross=ab.x*aq.y-ab.y*aq.x;
    float distance=GraphSegmentDistance(q,a,b)*(cross>=0 ? -1 : 1);
    return distance*min(halfSize.x,halfSize.y);
}
float GraphCapsuleDistance(float2 uv,float2 center,float2 size,float rotation)
{
    float2 h=max(abs(size)*0.5,1e-7),p=GraphRotatePoint(uv-center,-rotation);
    if(h.x>h.y) { p.x-=clamp(p.x,-(h.x-h.y),h.x-h.y); return length(p)-h.y; }
    p.y-=clamp(p.y,-(h.y-h.x),h.y-h.x); return length(p)-h.x;
}
float GraphSmoothUnion(float a,float b,float k)
{
    if(k<=0) return min(a,b); float h=saturate(0.5+0.5*(b-a)/k); return lerp(b,a,h)-k*h*(1-h);
}
float GraphCombineDistance(float a,float b,float smoothness,int mode)
{
    if(mode==1) return -GraphSmoothUnion(-a,-b,smoothness);
    if(mode==2) return -GraphSmoothUnion(-a,b,smoothness);
    return GraphSmoothUnion(a,b,smoothness);
}
float2 GraphPolar(float2 uv,float2 center,float angle,int mode)
{
    if(mode==0) { float2 p=uv-center; return float2(frac(GraphAngle(p)/6.28318530718-angle/360.0),length(p)*2); }
    return center+GraphDirection(uv.x*360+angle)*(uv.y*0.5);
}
float4 GraphRepeat(float2 p) { return float4(frac(p),floor(p)); }
float2 GraphMirror(float2 uv,float2 center,int axes)
{
    float2 p=uv-center; if(axes!=1) p.x=abs(p.x); if(axes!=0) p.y=abs(p.y); return center+p;
}
float2 GraphKaleidoscope(float2 uv,float2 center,float sectors,float angle)
{
    float2 p=uv-center; float sector=6.28318530718/clamp(round(sectors),1.0,128.0);
    float a=GraphAngle(p)-radians(angle); a=abs(frac(a/sector+0.5)-0.5)*sector+radians(angle);
    return center+length(p)*float2(cos(a),sin(a));
}
float4 GraphDomainWarp(float2 uv,float2 scale,float2 offset,float seed,float strength,int kind,int fractal,float octaves,float lacunarity,float gain)
{
    if(strength==0) return float4(uv,0,0);
    fnl_state state=fnlCreateState((int)clamp(seed,-2147483000.0,2147483000.0));
    state.frequency=1; state.domain_warp_type=kind; state.domain_warp_amp=strength; state.fractal_type=fractal;
    state.octaves=fractal==0 ? 1 : (int)clamp(round(octaves),1.0,8.0); state.lacunarity=clamp(lacunarity,0.01,16.0); state.gain=saturate(gain);
    float2 original=uv*scale+offset,warped=original; fnlDomainWarp2D(state,warped.x,warped.y);
    float2 displacement=warped-original; return float4(uv+displacement,displacement);
}
float GraphRadialFalloff(float distance,float radius)
{
    float f=saturate(1-distance/max(radius,1e-7)); return f*f*(3-2*f);
}
float2 GraphRadialWarp(float2 uv,float2 center,float strength,float radius,int mode)
{
    if(strength==0 || radius<=0) return uv;
    float2 p=uv-center; float falloff=GraphRadialFalloff(length(p),radius);
    if(mode==0) return center+GraphRotatePoint(p,strength*falloff);
    return center+p*exp2(clamp(-strength*falloff,-32.0,32.0));
}
float2 GraphWaveWarp(float2 uv,float strength,float frequency,float direction,float phase)
{
    if(strength==0) return uv; float2 axis=GraphDirection(direction);
    float t=dot(uv,float2(-axis.y,axis.x))*frequency+phase;
    return uv+axis*(strength*sin(t*6.28318530718));
}
float2 GraphRipple(float2 uv,float2 center,float strength,float frequency,float radius,float phase)
{
    if(strength==0 || radius<=0) return uv;
    float2 p=uv-center; float r=length(p);
    if(r<=1e-7) return uv;
    float core=smoothstep(0,max(0.25/max(abs(frequency),1e-4),1e-7),r);
    float wave=sin(6.28318530718*(r*frequency-phase))*strength*core*GraphRadialFalloff(r,radius);
    return uv+(p/r)*wave;
}
";
    }
}
