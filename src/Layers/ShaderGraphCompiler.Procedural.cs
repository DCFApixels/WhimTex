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
                case ShaderGraphNodeKind.PixelateUV:
                    type = ShaderGraphValueType.Float2; text = $"GraphPixelateUV({V("UV")},{V("CellSize")},{V("Offset")})"; break;
                case ShaderGraphNodeKind.Random: text = $"GraphRandom({V("Coordinates")},{V("Seed")})"; break;
                case ShaderGraphNodeKind.Checkerboard:
                {
                    string Field() => $"float4({V("UV")}*{V("Scale")}*0.5,{V("Offset")}*0.5)";
                    string position = PeriodicField(n, ShaderGraphValueType.Float4, Field, out string width);
                    text = $"GraphCheckerboard({position},{width})"; break;
                }
                case ShaderGraphNodeKind.Stripes:
                {
                    string Field() => $"float2(dot({V("UV")},GraphDirection({V("Angle")}))*{V("Frequency")},{V("Phase")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float2, Field, out string width);
                    text = $"GraphStripeProfile({position},{V("Width")},{width},{n.option})"; break;
                }
                case ShaderGraphNodeKind.Grid:
                {
                    string Field() => $"float4({V("UV")}*{V("Scale")}+{V("Width")}*0.5,{V("Offset")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float4, Field, out string width);
                    text = $"GraphGridPhase({position},{V("Width")},{width})"; break;
                }
                case ShaderGraphNodeKind.Rings:
                {
                    string Field() => $"float2(length({V("UV")}-{V("Center")})*{V("Frequency")},{V("Phase")})";
                    string position = PeriodicField(n, ShaderGraphValueType.Float2, Field, out string width);
                    text = $"GraphPulse({position},{V("Width")},{width})"; break;
                }
                case ShaderGraphNodeKind.GradientFactor: text = $"GraphGradientFactor(({V("UV")}-{V("Center")})*{V("Scale")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Shape:
                    var shape = ProceduralShared(n, ShaderGraphValueType.Float, () => ShapeDistanceExpression(n));
                    text = port == "Distance" ? shape.text : $"GraphDistanceMask({shape.text},{V("Feather")}" +
                        (sampledEvaluation ? "," + SampledWidth(n, ShaderGraphValueType.Float, () => ShapeDistanceExpression(n)) : "") + ")"; break;
                case ShaderGraphNodeKind.CombineDistance:
                    var distance = ProceduralShared(n, ShaderGraphValueType.Float, () => $"GraphCombineDistance({V("A")},{V("B")},{V("Smoothness")},{n.option})");
                    text = port == "Mask" ? $"GraphDistanceMask({distance.text},{V("Feather")}" +
                        (sampledEvaluation ? "," + SampledWidth(n, ShaderGraphValueType.Float, () => $"GraphCombineDistance({V("A")},{V("B")},{V("Smoothness")},{n.option})") : "") + ")" : distance.text; break;
                case ShaderGraphNodeKind.PolarCoordinates: type = ShaderGraphValueType.Float2; text = $"GraphPolar({V("UV")},{V("Center")},{V("Angle")},{n.option})"; break;
                case ShaderGraphNodeKind.Repeat:
                    type = ShaderGraphValueType.Float2;
                    var repeat = ProceduralShared(n, ShaderGraphValueType.Float4, () => $"GraphRepeat({V("UV")}*{V("Scale")}+{V("Offset")})");
                    text = repeat.text + (port == "Cell" ? ".zw" : ".xy"); break;
                case ShaderGraphNodeKind.Mirror: type = ShaderGraphValueType.Float2; text = $"GraphMirror({V("UV")},{V("Center")},{n.option})"; break;
                case ShaderGraphNodeKind.Kaleidoscope: type = ShaderGraphValueType.Float2; text = $"GraphKaleidoscope({V("UV")},{V("Center")},{V("Sectors")},{V("Angle")})"; break;
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
        // Keep the spatial coordinate and offset separate until both have been reduced
        // to a period. Differentiate their unwrapped parts so seams do not widen AA.
        string PeriodicField(ShaderGraphNode node, ShaderGraphValueType type, Func<string> expression, out string width)
        {
            var parts = ProceduralShared(node, type, expression);
            width = sampledEvaluation ? SampledWidth(node, type, expression, true) : $"GraphPeriodicWidth({parts.text})";
            return $"GraphPeriodicPosition({parts.text})";
        }
        string ShapeDistanceExpression(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            return node.option switch
            {
                3 or 4 => $"GraphPolygonDistance({V("UV")},{V("Center")},{V("Size")},{V("Sides")},{(node.option == 4 ? V("InnerRadius") : "1.0")},{V("Rotation")},{node.option})",
                5 => $"GraphCapsuleDistance({V("UV")},{V("Center")},{V("Size")},{V("Rotation")})",
                6 => $"GraphSegmentDistance({V("UV")},{V("Start")},{V("End")})-max({V("Width")},0)*0.5",
                _ => $"GraphShapeDistance({V("UV")},{V("Center")},{V("Size")},{V("Radius")},{node.option})"
            };
        }
        const string ProceduralHelpers = @"
float2 GraphDirection(float angle) { float s,c; sincos(radians(angle),s,c); return float2(c,s); }
float2 GraphPixelateUV(float2 uv,float2 cellSize,float2 offset)
{
    float2 size=max(abs(cellSize),1);
    return ((floor((uv*_CanvasSize.xy-offset)/size)+0.5)*size+offset)*_CanvasSize.zw;
}
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
float GraphPeriodSum(float2 parts) { return parts.x+parts.y; }
float2 GraphPeriodSum(float4 parts) { return parts.xy+parts.zw; }
float GraphPeriodicPosition(float2 parts) { return frac(frac(parts.x)+frac(parts.y)); }
float2 GraphPeriodicPosition(float4 parts) { return frac(frac(parts.xy)+frac(parts.zw)); }
float GraphPeriodicWidth(float2 parts) { return abs(GraphPeriodSum(ddx(parts)))+abs(GraphPeriodSum(ddy(parts))); }
float2 GraphPeriodicWidth(float4 parts) { return abs(GraphPeriodSum(ddx(parts)))+abs(GraphPeriodSum(ddy(parts))); }
float GraphPulse(float t,float width,float fw)
{
    width=saturate(width); fw=max(fw,1e-5);
    if(width==0 || width==1) return width;
    float start=frac(frac(t)-fw*0.5), tail=frac(fw);
    float covered=min(tail,max(width-start,0))+min(max(tail-(1-start),0),width);
    return saturate((floor(fw)*width+covered)/fw);
}
// Integrate a triangle over a non-wrapping interval using its local trapezoids.
float GraphTriangleArea(float start,float span)
{
    float rising=min(span,max(0.5-start,0)), falling=span-rising;
    return rising*(2*start+rising)+falling*(2*(1-max(start,0.5))-falling);
}
float GraphStripeProfile(float t,float width,float fw,int profile)
{
    if(profile==0) return GraphPulse(t,width,fw);
    t=frac(t); fw=max(fw,1e-5);
    if(profile==1)
    {
        float start=frac(t-fw*0.5), tail=frac(fw), first=min(tail,1-start);
        return saturate((floor(fw)*0.5+GraphTriangleArea(start,first)+GraphTriangleArea(0,tail-first))/fw);
    }
    float a=3.14159265359*fw;
    float attenuation=abs(a)<0.1 ? 1-a*a*(1.0/6.0-a*a/120.0) : sin(a)/a;
    return 0.5+0.5*sin(6.28318530718*t)*attenuation;
}
float GraphCheckerboard(float2 p,float2 fw)
{
    float x=GraphPulse(p.x,0.5,fw.x), y=GraphPulse(p.y,0.5,fw.y); return x+y-2*x*y;
}
float GraphGridPhase(float2 p,float2 width,float2 fw)
{
    float x=GraphPulse(p.x,width.x,fw.x),y=GraphPulse(p.y,width.y,fw.y); return 1-(1-x)*(1-y);
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
float GraphDistanceMask(float distance,float feather,float fw)
{
    float edge=max(max(fw*0.5,1e-7),max(feather,0)*0.5); return 1-smoothstep(-edge,edge,distance);
}
float GraphDistanceMask(float distance,float feather) { return GraphDistanceMask(distance,feather,fwidth(distance)); }
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
    float sign=cross>=0 ? -1 : 1;
    if(halfSize.x==halfSize.y) return GraphSegmentDistance(q,a,b)*sign*halfSize.x;
    // Anisotropic scaling destroys the sector's distance metric. Measure in the
    // rotated UV plane against the actual scaled edges, retaining the same contour.
    float2 position=GraphRotatePoint(uv-center,-rotation);
    int vertices=(int)count*(kind==4 ? 2 : 1);
    float step=6.28318530718/vertices;
    float2 direction=float2(cos(step),sin(step)),unit=float2(1,0);
    float2 start=unit*halfSize;
    float minimumSquared=dot(position-start,position-start);
    [loop] for(int i=1;i<=vertices;i++)
    {
        unit=i==vertices ? float2(1,0) : float2(unit.x*direction.x-unit.y*direction.y,unit.x*direction.y+unit.y*direction.x);
        float r=kind==4 && (i&1)!=0 ? clamp(innerRadius,0.001,1.0) : 1;
        float2 end=unit*halfSize*r,edge=end-start;
        float2 delta=position-start-edge*saturate(dot(position-start,edge)/max(dot(edge,edge),1e-14));
        minimumSquared=min(minimumSquared,dot(delta,delta));
        start=end;
    }
    return sqrt(minimumSquared)*sign;
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
    float t=frac(frac(dot(uv,float2(-axis.y,axis.x))*frequency)+frac(phase));
    return uv+axis*(strength*sin(t*6.28318530718));
}
float2 GraphRipple(float2 uv,float2 center,float strength,float frequency,float radius,float phase)
{
    if(strength==0 || radius<=0) return uv;
    float2 p=uv-center; float r=length(p);
    if(r<=1e-7) return uv;
    float core=smoothstep(0,max(0.25/max(abs(frequency),1e-4),1e-7),r);
    float wave=sin(6.28318530718*frac(frac(r*frequency)-frac(phase)))*strength*core*GraphRadialFalloff(r,radius);
    return uv+(p/r)*wave;
}
";
    }
}
