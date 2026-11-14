float4 VMPointOperation(Instruction t, float4 a,float4 b,float4 c,float4 d,float4 e,float4 f,float4 g,float4 h)
{
    switch(t.o)
    {
        case 118: return float4(GraphPixelateUV(a.xy,b.xy,c.xy),0,0);
        case 128: return GraphBlackbody(a.x);
        case 108: return GraphDither(a,b.xy,c.x,d.x,t.i);
        case 124:
        {
            GraphTileData tile=GraphTileUV(a.xy,b.xy,c.xy,t.i==1?d.x:0,e.x,f.x,g.x);
            return t.m==0?float4(tile.uv,0,0):t.m==1?float4(tile.cell,0,0):t.m==2?float4(tile.center,0,0):t.m==3?float4(tile.random,0,0,0):float4(tile.mask,0,0,0);
        }
        case 75: return float4(GraphChannel(a,t.k),0,0,0);
        case 76: return float4(GraphMapLuminance(a.rgb,b.x,c.x),a.a);
        // Unwrapped fields: the VM differentiates these before the shared AA helpers.
        case 50: return float4(a.xy*b.xy*0.5,c.xy*0.5);
        case 51: return float4(dot(a.xy,GraphDirection(b.x))*c.x,d.x,0,0);
        case 52: return float4(a.xy*b.xy+d.xy*0.5,c.xy);
        case 53: return float4(length(a.xy-b.xy)*c.x,d.x,0,0);
        case 55: return float4(GraphCombineDistance(a.x,b.x,c.x,t.i),0,0,0);
        case 40:
            if(t.i==3 || t.i==4) return float4(GraphPolygonDistance(a.xy,b.xy,c.xy,d.x,e.x,f.x,t.i),0,0,0);
            if(t.i==5) return float4(GraphCapsuleDistance(a.xy,b.xy,c.xy,d.x),0,0,0);
            if(t.i==6) return float4(GraphSegmentDistance(a.xy,b.xy,c.xy)-max(d.x,0)*0.5,0,0,0);
            return float4(GraphShapeDistance(a.xy,b.xy,c.xy,d.x,t.i),0,0,0);
        #if defined(WT_VM_BLEND)
        case 41: return GraphBlend(a,b,c.x,t.i,t.j);
        #endif
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
        case 175: return float4(GraphRandomRange(a.xy,b.x,c.x),0,0,0);
        case 177: return GraphNoiseSineWave(a,b.xy);
        case 178: return GraphSawtoothWave(a);
        case 179: return GraphSquareWave(a);
        case 180: return GraphTriangleWave(a);
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
