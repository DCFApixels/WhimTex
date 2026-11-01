namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphCompiler
    {
        bool usesExtended;

        Value EmitExtended(ShaderGraphNode node)
        {
            string V(string name) => Input(node, name).text;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.Sign: case ShaderGraphNodeKind.Saturate:
                case ShaderGraphNodeKind.Asin: case ShaderGraphNodeKind.Acos:
                case ShaderGraphNodeKind.Exp: case ShaderGraphNodeKind.Log:
                {
                    var input = Input(node, "In"); var type = Common(node, input);
                    // Natural exp/log and radian angles. Asin/Acos require [-1,1];
                    // Log requires positive inputs. Do not silently clip the domain.
                    return new Value(type, node.kind.ToString().ToLowerInvariant() + "(" + Cast(input, type, node).text + ")");
                }
                case ShaderGraphNodeKind.Modulo: case ShaderGraphNodeKind.Atan2:
                {
                    bool atan = node.kind == ShaderGraphNodeKind.Atan2;
                    var a = Input(node, atan ? "Y" : "A"); var b = Input(node, atan ? "X" : "B");
                    var type = Common(node, a, b);
                    string x = Cast(a, type, node).text, y = Cast(b, type, node).text;
                    // Floor modulo has the divisor's sign. Zero divisors, like
                    // atan2(0,0), retain the intrinsic's undefined-domain behavior.
                    return new Value(type, atan ? $"atan2({x},{y})" : $"(({x})-({y})*floor(({x})/({y})))");
                }
                case ShaderGraphNodeKind.Cross:
                    return new Value(ShaderGraphValueType.Float3, $"cross({V("A")},{V("B")})");
                case ShaderGraphNodeKind.Reflect: case ShaderGraphNodeKind.Refract:
                {
                    var incident = Input(node, "Incident"); var normal = Input(node, "Normal");
                    var type = Common(node, incident, normal);
                    string i = Cast(incident, type, node).text, n = Cast(normal, type, node).text;
                    return new Value(type, node.kind == ShaderGraphNodeKind.Reflect
                        ? $"reflect({i},{n})" : $"refract({i},{n},{V("Eta")})");
                }
                case ShaderGraphNodeKind.And: case ShaderGraphNodeKind.Or:
                    return new Value(ShaderGraphValueType.Boolean, $"({V("A")} {(node.kind == ShaderGraphNodeKind.And ? "&&" : "||")} {V("B")})");
                case ShaderGraphNodeKind.Not:
                    return new Value(ShaderGraphValueType.Boolean, $"(!({V("In")}))");
                case ShaderGraphNodeKind.NormalEncode:
                    return new Value(ShaderGraphValueType.Float3, $"(({V("Normal")})*0.5+0.5)");
                case ShaderGraphNodeKind.NormalDecode:
                    return new Value(ShaderGraphValueType.Float3, $"(({V("Normal")})*2.0-1.0)");
                case ShaderGraphNodeKind.Premultiply:
                {
                    string color = V("Color");
                    return new Value(ShaderGraphValueType.Color, $"float4(({color}).rgb*({color}).a,({color}).a)");
                }
            }

            if (!ShaderGraphNodes.TryExtendedOutputType(node.kind, out var outputType))
                throw new ShaderGraphException(node.id, "Unknown extended node.");
            // ExtendedHelpers contains references to both shared helper blocks, even
            // when only one of its functions is called. Emit those blocks first.
            usesExtended = usesProcedural = usesColor = true;
            string text;
            switch (node.kind)
            {
                case ShaderGraphNodeKind.NormalStrength:
                    text = $"GraphNormalStrength({V("Normal")},{V("Strength")},{(int)node.normalEncoding})"; break;
                case ShaderGraphNodeKind.NormalLighting:
                    text = $"GraphNormalLighting({V("Color")},{V("Normal")},{V("LightDirection")},{V("LightColor")},{V("Intensity")},{V("Ambient")}," +
                        $"{V("SpecularColor")},{V("SpecularIntensity")},{V("Roughness")},{V("SpecularOnly")},{(int)node.normalEncoding})"; break;
                case ShaderGraphNodeKind.Unpremultiply:
                    text = $"GraphUnpremultiply({V("Color")})"; break;
                case ShaderGraphNodeKind.Flipbook:
                    text = $"GraphFlipbook({V("UV")},{V("Columns")},{V("Rows")},{V("Frame")},{node.option})"; break;
                case ShaderGraphNodeKind.TileableNoise:
                    text = $"GraphTileableNoise({V("UV")},{V("Period")},{V("Cells")},{V("Offset")},{V("Seed")})"; break;
                case ShaderGraphNodeKind.VoronoiEdge:
                    text = $"GraphVoronoiEdge({V("UV")}*{V("Scale")},{V("Seed")},{V("Jitter")})"; break;
                case ShaderGraphNodeKind.Dither:
                    text = $"GraphDither({V("Color")},{V("UV")},{V("Levels")},{V("PixelSize")},{node.option})"; break;
                default: throw new ShaderGraphException(node.id, "Unknown extended node.");
            }
            return new Value(outputType, text);
        }

        const string ExtendedHelpers = @"
float3 GraphNormalStrength(float3 normal,float strength,int encoding)
{
    float3 n=GraphUnitNormal(GraphDecodeNormal(normal,encoding));
    // Strength 0 is flat, 1 is the original unit normal; negative values reverse XY.
    n=float3(n.xy*strength,lerp(1,n.z,saturate(abs(strength))));
    return GraphEncodeNormal(GraphUnitNormal(n),encoding);
}
float GraphExtendedSpecular(float3 normal,float3 lightDirection,float roughness)
{
    // Orthographic view V=(0,0,1), with the same finite sharpness rule as the FX.
    float3 h=lightDirection+float3(0,0,1);
    float h2=dot(h,h);
    if(h2<=1e-12 || normal.z<=0) return 0;
    h*=rsqrt(h2);
    roughness=max(saturate(roughness),0.01);
    float exponent=max(2/(roughness*roughness)-2,1);
    return pow(saturate(dot(normal,h)),exponent)*saturate(dot(normal,lightDirection));
}
float4 GraphNormalLighting(float4 surface,float3 normal,float3 lightDirection,float4 lightColor,
    float intensity,float ambient,float4 specularColor,float specularIntensity,float roughness,bool specularOnly,int encoding)
{
    float3 n=GraphUnitNormal(GraphDecodeNormal(normal,encoding));
    float3 l=GraphUnitNormal(lightDirection);
    // Tint alpha scales light strength; surface alpha passes through in both modes.
    // RGB and intensities are unrestricted, including negative values and HDR.
    float3 rgb=0;
    if(!specularOnly) rgb=surface.rgb*(ambient+lightColor.rgb*(max(dot(n,l),0)*intensity*lightColor.a));
    if(specularIntensity!=0)
        rgb+=specularColor.rgb*(specularColor.a*specularIntensity*GraphExtendedSpecular(n,l,roughness));
    return float4(rgb,surface.a);
}
float4 GraphUnpremultiply(float4 color)
{
    // Exactly zero alpha has no recoverable straight RGB. Nonzero alpha, however
    // small or negative, is divided as supplied without an epsilon or RGB clipping.
    float divisor=color.a==0 ? 1.0 : color.a;
    return float4(color.a==0 ? float3(0,0,0) : color.rgb/divisor,color.a);
}
float2 GraphFlipbook(float2 uv,float columns,float rows,float frame,int rowOrder)
{
    // Integer dimensions in [1,4096]; the product remains exactly representable.
    float2 grid=clamp(floor(float2(columns,rows)),1,4096);
    float count=grid.x*grid.y;
    frame=floor(frame); frame-=floor(frame/count)*count;
    float row=floor(frame/grid.x), column=frame-row*grid.x;
    if(rowOrder==1) row=grid.y-1-row;
    // Frame 0 is bottom-left by default. Top-to-bottom changes row order only,
    // keeping the local UV orientation. Negative frames wrap around the grid.
    return (uv+float2(column,row))/grid;
}
float GraphTileableNoise(float2 uv,float2 period,float2 cells,float2 offset,float seed)
{
    // Smooth periodic Perlin: wrap gradient lattice indices before the shared
    // FastNoiseLite hash, never blend finished images across a seam.
    period=max(abs(period),1e-5);
    cells=clamp(floor(cells+0.5),1,4096);
    float2 p=frac((uv+offset)/period)*cells;
    float2 baseCell=floor(p), f=p-baseCell;
    int2 i0=(int2)baseCell, i1=(int2)(baseCell+1);
    int2 size=(int2)cells;
    // Also handles a rounded p exactly at cells near a floating-point seam.
    i0=int2(i0.x%size.x,i0.y%size.y);
    i1=int2(i1.x%size.x,i1.y%size.y);
    int s=(int)clamp(seed,-2147483000.0,2147483000.0);
    float2 fade=float2(_fnlInterpQuintic(f.x),_fnlInterpQuintic(f.y));
    float a=_fnlGradCoord2D(s,i0.x*PRIME_X,i0.y*PRIME_Y,f.x,f.y);
    float b=_fnlGradCoord2D(s,i1.x*PRIME_X,i0.y*PRIME_Y,f.x-1,f.y);
    float c=_fnlGradCoord2D(s,i0.x*PRIME_X,i1.y*PRIME_Y,f.x,f.y-1);
    float d=_fnlGradCoord2D(s,i1.x*PRIME_X,i1.y*PRIME_Y,f.x-1,f.y-1);
    return lerp(lerp(a,b,fade.x),lerp(c,d,fade.x),fade.y)*1.4247691104677813*0.5+0.5;
}
float2 GraphVoronoiFeature(float2 cell,float seed,float jitter)
{
    uint h=GraphHashPoint(cell,seed);
    return 0.5+(float2(GraphHashUnit(h),GraphHashUnit(GraphHash(h^0x9e3779b9u)))-0.5)*jitter;
}
float GraphVoronoiEdge(float2 p,float seed,float jitter)
{
    float2 baseCell=floor(p), local=frac(p);
    float2 nearestCell=0, nearestVector=0;
    float nearestSquared=1e20;
    jitter=saturate(jitter);
    // One feature per unit cell gives covering radius <=sqrt(2). A 5x5 search
    // therefore contains the closest site, including maximum jitter.
    [loop] for(int y=-2;y<=2;y++) [loop] for(int x=-2;x<=2;x++)
    {
        float2 cell=float2(x,y);
        float2 r=cell+GraphVoronoiFeature(baseCell+cell,seed,jitter)-local;
        float q=dot(r,r);
        if(q<nearestSquared) { nearestSquared=q; nearestCell=cell; nearestVector=r; }
    }
    float edge=1e20;
    // Adjacent sites are at most 2*sqrt(2) apart by the same covering bound.
    // Every cell that can contribute a facet is inside this 7x7 neighborhood
    // of the closest site's cell. Work in local coordinates for precision.
    [loop] for(int edgeY=-3;edgeY<=3;edgeY++) [loop] for(int edgeX=-3;edgeX<=3;edgeX++)
    {
        if(edgeX==0 && edgeY==0) continue;
        float2 cell=nearestCell+float2(edgeX,edgeY);
        float2 r=cell+GraphVoronoiFeature(baseCell+cell,seed,jitter)-local;
        float2 between=r-nearestVector;
        float lengthSquared=dot(between,between);
        if(lengthSquared>0)
        {
            // Perpendicular distance from p to the feature-pair bisector.
            float distance=dot((nearestVector+r)*0.5,between*rsqrt(lengthSquared));
            edge=min(edge,distance);
        }
    }
    // Distance is in UV*Scale space. The final max removes roundoff at ties.
    return max(edge,0);
}
float GraphBayerThreshold(float2 pixel,int mode)
{
    int bits=mode==1 ? 1 : mode==2 ? 3 : 2;
    float side=(float)(1<<bits);
    // Positive wrapping also gives a consistent pattern for negative UV.
    float2 wrapped=pixel-floor(pixel/side)*side;
    int2 q=(int2)wrapped;
    int rank=0;
    [unroll] for(int bit=0;bit<3;bit++)
    {
        if(bit<bits)
        {
            int x=(q.x>>bit)&1, y=(q.y>>bit)&1;
            rank=4*rank+((x^y)<<1)+y;
        }
    }
    return (rank+0.5)/(side*side);
}
float4 GraphDither(float4 color,float2 uv,float levels,float pixelSize,int mode)
{
    float steps=max(floor(levels+0.5),2)-1;
    float2 pixel=floor(uv*max(_CanvasSize.xy,1)/max(abs(pixelSize),1));
    float threshold=GraphBayerThreshold(pixel,mode);
    // Quantization continues beyond [0,1] with spacing 1/(Levels-1). Alpha is
    // untouched, including for transparent colors with nonzero RGB.
    return float4(floor(color.rgb*steps+1-threshold)/steps,color.a);
}
";
    }
}
