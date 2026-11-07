float4 VMPointOperation(Instruction t, float4 a,float4 b,float4 c,float4 d,float4 e,float4 f,float4 g,float4 h)
{
    switch(t.o)
    {
        case 29: return d.x+(a-b.x)/(c.x-b.x)*(e.x-d.x);
        case 36:
            return float4(t.i==0?a.x<b.x:t.i==1?a.x<=b.x:t.i==2?a.x>b.x:t.i==3?a.x>=b.x:t.i==4?a.x==b.x:a.x!=b.x,0,0,0);
        case 37: return c.x!=0?a:b;
        case 45:
        {
            float4 result=0;
            [unroll] for(int lane=0;lane<4;lane++) if(t.value[lane]>=0) result[lane]=a[(int)t.value[lane]];
            return result;
        }
        case 93: return float4(cross(a.xyz,b.xyz),0);
        case 94: return reflect(ActiveLanes(a,t.m),ActiveLanes(b,t.m));
        case 95: return refract(ActiveLanes(a,t.m),ActiveLanes(b,t.m),c.x);
        case 96: return float4(a.x!=0 && b.x!=0,0,0,0);
        case 97: return float4(a.x!=0 || b.x!=0,0,0,0);
        case 98: return float4(a.x==0,0,0,0);
        case 127: return t.i==0?float4(GraphRotatePoint(a.xy,b.x),0,0):float4(GraphRotateVector(a.xyz,c.xyz,b.x),0);
        case 160: return GraphRangeMask(a,b.x,c.x,d.x,t.i);
        case 38: return float4(GraphTransform(a.xy,b.xy,c.xy,d.x,e.xy),0,0);
        case 49: return float4(GraphRandom(a.xy,b.x),0,0,0);
        case 54: return float4(GraphGradientFactor((a.xy-b.xy)*c.xy,d.x,t.i),0,0,0);
        case 56: return float4(GraphPolar(a.xy,b.xy,c.x,t.i),0,0);
        case 57: return float4((t.m==1?GraphRepeat(a.xy*b.xy+c.xy).zw:GraphRepeat(a.xy*b.xy+c.xy).xy),0,0);
        case 58: return float4(GraphMirror(a.xy,b.xy,t.i),0,0);
        case 59: return float4(GraphKaleidoscope(a.xy,b.xy,c.x,d.x),0,0);
        case 61: return float4(a.xy+(t.i==1?b.xy*2-1:b.xy)*c.xy,0,0);
        case 62: return float4(GraphRadialWarp(a.xy,b.xy,c.x,d.x,0),0,0);
        case 63: return float4(GraphRadialWarp(a.xy,b.xy,c.x,d.x,1),0,0);
        case 64: return float4(GraphWaveWarp(a.xy,b.x,c.x,d.x,e.x),0,0);
        case 65: return float4(GraphRipple(a.xy,b.xy,c.x,d.x,e.x,f.x),0,0);
        case 105: return float4(GraphFlipbook(a.xy,b.x,c.x,d.x,t.i),0,0);
        case 125: return float4(GraphPerspectiveUV(a.xy,b.xy,c.xy,d.xy,e.xy),0);
        case 123: return float4(GraphHeightBlend(a.x,b.x,c.x,d.x,e.x),0,0);
        case 66: return GraphHue(a,b.x);
        case 67: return GraphSaturation(a,b.x);
        case 68: return float4(a.rgb*exp2(b.x),a.a);
        case 69: return GraphLightness(a,b.x);
        case 70: return GraphLevels(a,b.xy,c.x,d.xy,e.x!=0,t.i);
        case 71: return GraphColorBalance(a,b.xyz,c.xyz,d.xyz,e.x,f.x,g.x!=0);
        case 119: return float4(dot(a.rgb,b.xyz),dot(a.rgb,c.xyz),dot(a.rgb,d.xyz),a.a);
        case 120: return GraphWhiteBalance(a,b.x,c.x);
        case 72: return GraphPosterize(a,b.x);
        case 73: return GraphConvertColor(a,t.k,t.l);
        case 74: return float4(GraphLuminance(a.rgb),0,0,0);
        case 77: return float4(GraphColorMask(a.rgb,b.rgb,c.x,d.x,t.i),0,0,0);
        case 78: return float4(GraphHueRange(a.rgb,b.xy,c.x,d.xy),0,0,0);
        case 79: return GraphMixColor(a,b,c.x,t.i);
        case 80: return float4(a.rgb,b.x*(t.i==1?a.a:1));
        case 82: return float4(GraphNormalBlend(a.xyz,b.xyz,c.x,t.i,t.j),0);
        case 84: return float4((GraphDecodeNormal(a.xyz,t.j).xy*b.xy*float2(c.x!=0?-1:1,d.x!=0?-1:1))*(t.i==1?0.5:1)+(t.i==1?0.5:0),0,0);
        case 99: return float4(a.xyz*0.5+0.5,0);
        case 100: return float4(a.xyz*2-1,0);
        case 101: return float4(GraphNormalStrength(a.xyz,b.x,t.j),0);
        case 103: return float4(a.rgb*a.a,a.a);
        case 104: return GraphUnpremultiply(a);
    }
    return 0;
}
