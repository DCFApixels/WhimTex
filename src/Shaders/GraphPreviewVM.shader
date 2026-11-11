Shader "Hidden/WhimTex/GraphPreviewVM"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment Fragment
            #pragma multi_compile_local _ WT_VM_EXTENDED WT_VM_VOLUME
            #pragma multi_compile_local _ WT_VM_IMAGES WT_VM_IMAGE_MATH
            #pragma multi_compile_local _ WT_VM_BASIS
            #pragma multi_compile_local _ WT_VM_BASIS_DERIVATIVES
            #pragma multi_compile_local _ WT_VM_GABOR
            #pragma multi_compile_local _ WT_VM_CELLULAR
            #pragma multi_compile_local _ WT_VM_SHARD
            #pragma multi_compile_local _ WT_VM_SPOTS
            #pragma multi_compile_local _ WT_VM_TRIANGLE
            #pragma multi_compile_local _ WT_VM_WARP
            #pragma multi_compile_local _ WT_VM_GRAIN
            #pragma multi_compile_local _ WT_VM_CAUSTICS
            #pragma multi_compile_local _ WT_VM_PALETTES
            #pragma multi_compile_local _ WT_VM_TRANSFORMS
            #pragma multi_compile_local _ WT_VM_SAMPLING
            #pragma multi_compile_local _ WT_VM_FRACTAL
            #pragma multi_compile_local _ WT_VM_SURFACE
            #pragma multi_compile_local _ WT_VM_PERIODIC
            #pragma multi_compile_local _ WT_VM_BLEND
            #if defined(WT_VM_IMAGE_MATH)
            #define WT_VM_IMAGES
            #endif
            #if defined(WT_VM_VOLUME)
            #define WT_VM_EXTENDED

            #endif
            #include "UnityCG.cginc"
            #include "ProceduralUv.cginc"
            #include "GraphCoordinates.cginc"
            float4 _VMViewport;
            #if !defined(WT_VM_IMAGE_MATH)
            #define WT_NOISE_INTERNAL_DYNAMIC_HASH
            #if !defined(WT_VM_PERIODIC)
            #define WT_NOISE_INTERNAL_NONPERIODIC
            #endif
            #define WT_NOISE_INTERNAL_DYNAMIC_CUBATURE
            #include "WTNoiseCommon.hlsl"
            #if defined(WT_VM_BASIS)
            #include "WTNoiseBasis.hlsl"
            #include "WTNoiseFlowTypes.hlsl"
            #endif
            #if defined(WT_VM_GABOR)
            #include "WTNoiseGabor.hlsl"
            #endif
            #if defined(WT_VM_CELLULAR)
            #include "WTNoiseCellular.hlsl"
            #endif
            #if defined(WT_VM_SHARD)
            #include "WTNoiseShard.hlsl"
            #endif
            #if defined(WT_VM_SPOTS)
            #include "WTNoiseSpots.hlsl"
            #endif
            #if defined(WT_VM_TRIANGLE)
            #include "WTNoiseTriangleVoronoi.hlsl"
            #endif
            #if defined(WT_VM_WARP)
            #include "WTNoiseWarp.hlsl"
            #endif
            #if defined(WT_VM_CAUSTICS)
            #include "WTNoiseCaustics.hlsl"
            #endif
            #if defined(WT_VM_GRAIN)
            #include "WTNoiseGrain.hlsl"
            #endif
            #endif
            #if defined(WT_VM_EXTENDED)
            #include "HdrColor.cginc"
            float4 _CanvasSize;
            float _PixelScale;
            #include "GraphBasic.hlsl"
            #include "GraphProcedural.hlsl"
            #include "GraphColor.hlsl"
            #include "GraphExtended.hlsl"
            #include "GraphSurface.hlsl"
            #include "GraphBlackbody.hlsl"
            #if defined(WT_VM_BLEND)
            static float _HdrBlend;
            #include "ColorBlend.cginc"
            #include "GraphBlend.hlsl"
            #endif
            #endif

            struct Instruction
            {
                int op,a,b,c,d,e,f,g,h,i,j,k,l,m,n,o;
                float4 value;
            };
            StructuredBuffer<Instruction> _VMCode;
            int _VMCount, _VMResult;
            float _VMTime;
            #if defined(WT_VM_TRANSFORMS)
            float4 _VMTransformRows[48];
            #endif
            #if defined(WT_VM_IMAGES)
            #include "WTNoiseCausticsSpectrum.hlsl"
            sampler2D _VMImage0; float4 _VMImageSize0, _WhimTex_Texture_VMImage0_UvMap, _WhimTex_Texture_VMImage0_Size;
            sampler2D _VMImage1; float4 _VMImageSize1, _WhimTex_Texture_VMImage1_UvMap, _WhimTex_Texture_VMImage1_Size;
            sampler2D _VMImage2; float4 _VMImageSize2, _WhimTex_Texture_VMImage2_UvMap, _WhimTex_Texture_VMImage2_Size;
            sampler2D _VMImage3; float4 _VMImageSize3, _WhimTex_Texture_VMImage3_UvMap, _WhimTex_Texture_VMImage3_Size;
            sampler2D _VMImage4; float4 _VMImageSize4, _WhimTex_Texture_VMImage4_UvMap, _WhimTex_Texture_VMImage4_Size;
            sampler2D _VMImage5; float4 _VMImageSize5, _WhimTex_Texture_VMImage5_UvMap, _WhimTex_Texture_VMImage5_Size;
            sampler2D _VMImage6; float4 _VMImageSize6, _WhimTex_Texture_VMImage6_UvMap, _WhimTex_Texture_VMImage6_Size;
            sampler2D _VMImage7; float4 _VMImageSize7, _WhimTex_Texture_VMImage7_UvMap, _WhimTex_Texture_VMImage7_Size;
            float2 VMImageSize(int slot) { switch(slot) {
                case 0: return _WhimTex_Texture_VMImage0_Size.z>0?_WhimTex_Texture_VMImage0_Size.zw:_VMImageSize0.xy;
                case 1: return _WhimTex_Texture_VMImage1_Size.z>0?_WhimTex_Texture_VMImage1_Size.zw:_VMImageSize1.xy;
                case 2: return _WhimTex_Texture_VMImage2_Size.z>0?_WhimTex_Texture_VMImage2_Size.zw:_VMImageSize2.xy;
                case 3: return _WhimTex_Texture_VMImage3_Size.z>0?_WhimTex_Texture_VMImage3_Size.zw:_VMImageSize3.xy;
                case 4: return _WhimTex_Texture_VMImage4_Size.z>0?_WhimTex_Texture_VMImage4_Size.zw:_VMImageSize4.xy;
                case 5: return _WhimTex_Texture_VMImage5_Size.z>0?_WhimTex_Texture_VMImage5_Size.zw:_VMImageSize5.xy;
                case 6: return _WhimTex_Texture_VMImage6_Size.z>0?_WhimTex_Texture_VMImage6_Size.zw:_VMImageSize6.xy;
                case 7: return _WhimTex_Texture_VMImage7_Size.z>0?_WhimTex_Texture_VMImage7_Size.zw:_VMImageSize7.xy;
                } return 1; }
            float4 VMImageLoad(int slot,float2 uv) { switch(slot) {
                case 0: return tex2Dlod(_VMImage0,float4(uv*_WhimTex_Texture_VMImage0_UvMap.xy+_WhimTex_Texture_VMImage0_UvMap.zw,0,0));
                case 1: return tex2Dlod(_VMImage1,float4(uv*_WhimTex_Texture_VMImage1_UvMap.xy+_WhimTex_Texture_VMImage1_UvMap.zw,0,0));
                case 2: return tex2Dlod(_VMImage2,float4(uv*_WhimTex_Texture_VMImage2_UvMap.xy+_WhimTex_Texture_VMImage2_UvMap.zw,0,0));
                case 3: return tex2Dlod(_VMImage3,float4(uv*_WhimTex_Texture_VMImage3_UvMap.xy+_WhimTex_Texture_VMImage3_UvMap.zw,0,0));
                case 4: return tex2Dlod(_VMImage4,float4(uv*_WhimTex_Texture_VMImage4_UvMap.xy+_WhimTex_Texture_VMImage4_UvMap.zw,0,0));
                case 5: return tex2Dlod(_VMImage5,float4(uv*_WhimTex_Texture_VMImage5_UvMap.xy+_WhimTex_Texture_VMImage5_UvMap.zw,0,0));
                case 6: return tex2Dlod(_VMImage6,float4(uv*_WhimTex_Texture_VMImage6_UvMap.xy+_WhimTex_Texture_VMImage6_UvMap.zw,0,0));
                case 7: return tex2Dlod(_VMImage7,float4(uv*_WhimTex_Texture_VMImage7_UvMap.xy+_WhimTex_Texture_VMImage7_UvMap.zw,0,0));
                } return 0; }
            float4 VMFetch(int slot,float2 pixel,float2 size,int wrap)
            {
                if(wrap==3 && (any(pixel<0)||any(pixel>=size))) return 0;
                if(wrap==1) pixel-=floor(pixel/size)*size;
                else if(wrap==2) { pixel-=floor(pixel/(size*2))*(size*2); pixel=min(pixel,size*2-1-pixel); }
                else pixel=clamp(pixel,0,size-1);
                return VMImageLoad(slot,(pixel+0.5)/size);
            }
            float4 VMSample(int slot,float2 uv,int wrap,int filter)
            {
                float2 size=VMImageSize(slot),p=uv*size-0.5;
                if(filter==0) return VMFetch(slot,floor(p+0.5),size,wrap);
                float2 i=floor(p),f=frac(p);
                return lerp(lerp(VMFetch(slot,i,size,wrap),VMFetch(slot,i+float2(1,0),size,wrap),f.x),
                    lerp(VMFetch(slot,i+float2(0,1),size,wrap),VMFetch(slot,i+1,size,wrap),f.x),f.y);
            }
            #endif
            #if defined(WT_VM_PALETTES)
            #include "CurveSampling.hlsl"
            sampler2D _VMPalette0, _VMPalette1, _VMPalette2, _VMPalette3, _VMPalette4, _VMPalette5, _VMPalette6, _VMPalette7;
            float4 _VMCurveSampling[8];
            float4 VMPalette(int slot,float t,int wrap,int curve)
            {
                float u;
                if (curve != 0) u = _WhimTex_CurveCoordinate(t, _VMCurveSampling[slot]);
                else
                {
                    if(wrap==0) t=saturate(t);
                    else if(wrap==1) t=frac(t);
                    else { t-=floor(t*0.5)*2; t=t<=1?t:2-t; }
                    u=(saturate(t)*511+0.5)/512;
                }
                float4 uv=float4(u,0.5,0,0);
                switch(slot) {
                    case 0: return tex2Dlod(_VMPalette0,uv);
                    case 1: return tex2Dlod(_VMPalette1,uv);
                    case 2: return tex2Dlod(_VMPalette2,uv);
                    case 3: return tex2Dlod(_VMPalette3,uv);
                    case 4: return tex2Dlod(_VMPalette4,uv);
                    case 5: return tex2Dlod(_VMPalette5,uv);
                    case 6: return tex2Dlod(_VMPalette6,uv);
                    case 7: return tex2Dlod(_VMPalette7,uv);
                } return 0;
            }
            #endif
            struct Frame
            {
                float2 uv;
                float4 pixel;
                int seed, octave;
                float frequency, scalarAmplitude;
                float4 sum, total, amplitude, weight;
                float2 center, step;
                float4 previous;
            };
            float2 VMSampleOffset(Frame f,Instruction start)
            {
                if(start.i==3) return f.center;
                int pair=f.octave>>1;
                float sign=(f.octave&1)==0?1:-1;
                if(start.i<0) return f.uv+(pair==0?f.pixel.xy:f.pixel.zw)*(0.5*sign);
                float2 offset=start.i==2 ? (pair==0?float2(1,0):float2(0,1)) : (pair<3?float2(1,pair-1):float2(0,1));
                return f.center+offset*(sign*f.step);
            }
            float4 ActiveLanes(float4 v,int count)
            { return float4(v.x,count>1?v.y:0,count>2?v.z:0,count>3?v.w:0); }
            #if defined(WT_VM_EXTENDED)
            #include "GraphPreviewVM.Simple.hlsl"
            #if defined(WT_VM_IMAGES) && defined(WT_VM_SAMPLING)
            #define WT_GRAPH_HEIGHT_TEXTURE int
            #define WT_GRAPH_HEIGHT_SIZE(tex) VMImageSize(tex)
            #define WT_GRAPH_HEIGHT_SAMPLE(tex,uv,wrap,filter) VMSample(tex,uv,wrap,filter)
            #include "GraphHeight.hlsl"
            #undef WT_GRAPH_HEIGHT_TEXTURE
            #undef WT_GRAPH_HEIGHT_SIZE
            #undef WT_GRAPH_HEIGHT_SAMPLE
            #endif
            #endif
            #if defined(WT_VM_SURFACE)
            float4 _VMSurface0, _VMSurface1, _VMSurfaceViewport;
            int _VMSurfaceNormalEncoding;
            struct VMSurfaceTargets { float4 color:SV_Target0; float4 normal:SV_Target1; float4 position:SV_Target2; float4 specular:SV_Target3; };
            VMSurfaceTargets Fragment(v2f_img input)
            #else
            float4 Fragment(v2f_img input) : SV_Target
            #endif
            {
                #if defined(WT_VM_SURFACE)
                float2 screenUV=(_VMViewport.xy+input.uv*_VMViewport.zw-_VMSurfaceViewport.xy)/_VMSurfaceViewport.zw;
                #else
                float2 screenUV=input.uv;
                #endif
                float4 r[128];
                Frame frames[4];
                int level=-1, seed=0, pc=0;
                float2 uv=GraphSourceUv(_VMViewport.xy+input.uv*_VMViewport.zw);
                #if defined(WT_VM_SURFACE)
                float4 pixel=float4(ddx(_GraphRawUV)*_GraphUvDirection,ddy(_GraphRawUV)*_GraphUvDirection);
                #else
                float4 pixel=float4(ddx(uv),ddy(uv));
                #endif
                [loop] while(pc<_VMCount)
                {
                    Instruction t=_VMCode[pc];
                    float4 v=0;
                    int next=pc+1;
                    // Every operand is written before it is read; loop bodies have their own registers.
                    switch(t.op)
                    {
                        #if defined(WT_VM_IMAGES)
                        case 48: v.xy=t.i==0?VMImageSize(t.a):rcp(VMImageSize(t.a));break;
                        case 41:
                            v=t.e!=0 ? VMImageLoad(t.a,(floor(screenUV*VMImageSize(t.a))+0.5)/VMImageSize(t.a)) : VMSample(t.a,r[t.b].xy,t.c,t.d);
                            if(t.g!=0) v.rgb=WTCausticsFitSpectrumToRGB(v.rgb);
                            if(t.f!=0) v.w=1;
                            break;
                        #endif
                        #if defined(WT_VM_PALETTES)
                        case 42: v=VMPalette(t.a,r[t.b].x,t.c,t.d); break;
                        #endif
                        case 0: v=t.value; break;
                        case 1:
                            v=r[t.a];
                            if(t.b==1) v=v.xxxx;
                            else v=ActiveLanes(v,t.b);
                            if(t.d!=0 && t.b<4) v.w=1;
                            v=ActiveLanes(v,t.c); break;
                        case 2: v=t.b==1?float4(t.a==1?uv.y:uv.x,0,0,0):float4(uv,t.b==3?t.value.z:0,0); break;
                        case 3: v.x=r[t.a].x+(t.c!=0?_VMTime:0)*r[t.b].x; break;
                        case 4: v=r[t.a]; v.w*=r[t.b].x; break;
                        case 10:
                        {
                            float4 a=r[t.a], b=r[t.b];
                            switch(t.c)
                            {
                                case 10: v=a+b; break;
                                case 11: v=a-b; break;
                                case 12: v=a*b; break;
                                case 13: v=a/b; break;
                                case 14: v=min(a,b); break;
                                case 15: v=max(a,b); break;
                                case 20: v=pow(a,b); break;
                                case 30: v=step(a,b); break;
                                case 33: v.x=length(ActiveLanes(a-b,t.d)); break;
                                case 34: v.x=dot(ActiveLanes(a,t.d),ActiveLanes(b,t.d)); break;
                                case 87: v=a-b*floor(a/b); break;
                                case 88: v=atan2(a,b); break;
                            }
                            break;
                        }
                        case 11:
                        {
                            float4 a=r[t.a];
                            switch(t.c)
                            {
                                case 17: v=abs(a); break;
                                case 18: v=1-a; break;
                                case 19: v=-a; break;
                                case 21: v=sqrt(a); break;
                                case 22: v=floor(a); break;
                                case 23: v=ceil(a); break;
                                case 24: v=round(a); break;
                                case 25: v=frac(a); break;
                                case 26: v=sin(a); break;
                                case 27: v=cos(a); break;
                                case 32: v.x=length(ActiveLanes(a,t.d)); break;
                                case 35: v=normalize(ActiveLanes(a,t.d)); break;
                                case 85: v=sign(a); break;
                                case 86: v=saturate(a); break;
                                case 89: v=asin(a); break;
                                case 90: v=acos(a); break;
                                case 91: v=exp(a); break;
                                case 92: v=log(a); break;
                            }
                            break;
                        }
                        case 12:
                        {
                            float4 a=r[t.a],b=r[t.b],c=r[t.c];
                            if(t.d==16) v=clamp(a,b,c);
                            else if(t.d==31) v=smoothstep(b,c,a);
                            else if(t.d==117) v=(c-a)/(b-a);
                            else v=lerp(a,b,c);
                            break;
                        }
                        case 13: v=float4(r[t.a].x,r[t.b].x,r[t.c].x,r[t.d].x); break;
                        case 14: v.x=r[t.a][t.b]; break;
                        #if defined(WT_VM_EXTENDED)
                        case 5: v=t.i==0?float4(_CanvasSize.xy,0,0):t.i==1?float4(_CanvasSize.zw,0,0):float4(_PixelScale,0,0,0); break;
                        case 6: v.xy=GraphCanvasPosition(uv)*(t.i==1?float2(_CanvasSize.x*_CanvasSize.w,1):t.i==2?float2(1,_CanvasSize.y*_CanvasSize.z):1); break;
                        case 44:
                        {
                            float4 dx,dy;
                            if(t.j!=0) { dx=r[t.b];dy=r[t.c]; }
                            else { dx=ddx(r[t.a]);dy=ddy(r[t.a]); }
                            if(t.i==2) { dx.x+=dx.y;dy.x+=dy.y; }
                            else if(t.i==4) { dx.xy+=dx.zw;dy.xy+=dy.zw; }
                            v=abs(dx)+abs(dy); break;
                        }
                        case 45:
                        {
                            float4 field=r[t.a],width=r[t.b],edge=r[t.c];
                            if(t.i==50) v.x=GraphCheckerboard(GraphPeriodicPosition(field),width.xy);
                            else if(t.i==52) v.x=GraphGridPhase(GraphPeriodicPosition(field),edge.xy,width.xy);
                            else if(t.i==51) v.x=GraphStripeProfile(GraphPeriodicPosition(field.xy),edge.x,width.x,t.j);
                            else if(t.i==53) v.x=GraphPulse(GraphPeriodicPosition(field.xy),edge.x,width.x);
                            else v.x=GraphDistanceMask(field.x,edge.x,width.x);
                            break;
                        }
                        case 40: v=VMPointOperation(t,r[t.a],r[t.b],r[t.c],r[t.d],r[t.e],r[t.f],r[t.g],r[t.h]); break;
                        case 43: v=GraphNormalLighting(r[t.a],r[t.b].xyz,r[t.c].xyz,r[t.d],r[t.e].x,r[t.f].x,r[t.g],r[t.h].x,r[t.n].x,r[t.m].x!=0,t.j); break;
                        #if defined(WT_VM_TRANSFORMS)
                        case 46:
                        {
                            int row=t.a*6+t.i; float3 p=float3(r[t.b].xy,1);
                            float w=dot(_VMTransformRows[row+2].xyz,p);
                            w=w<0?min(w,-1e-7):max(w,1e-7);
                            v.xy=float2(dot(_VMTransformRows[row].xyz,p),dot(_VMTransformRows[row+1].xyz,p))/w;
                            break;
                        }
                        #endif
                        #if defined(WT_VM_SAMPLING)
                        #if defined(WT_VM_IMAGES)
                        case 47: v.xy=GraphHeightSlope(t.a,r[t.b].xy,r[t.c].x,t.i,t.j,t.k,t.l);break;
                        #endif
                        case 32:
                        {
                            Frame f=(Frame)0; f.uv=uv; f.pixel=pixel; f.seed=seed;
                            f.center=t.i<0?uv:r[t.a].xy;
                            f.frequency=t.i<0 || t.i==3?1:t.j!=0?abs(r[t.b].x):max(abs(r[t.b].x),0.001);
                            f.step=f.frequency*_CanvasSize.zw;
                            frames[++level]=f; uv=VMSampleOffset(f,t);
                            break;
                        }
                        case 33:
                        {
                            Frame f=frames[level]; Instruction start=_VMCode[t.b];
                            if(start.i==3) {v=r[t.a];uv=f.uv;pixel=f.pixel;seed=f.seed;level--;break;}
                            if((f.octave&1)==0) f.previous=r[t.a];
                            else {
                                float4 delta=f.previous-r[t.a]; int pair=f.octave>>1;
                                if(start.i<0) { if(pair==0) f.sum=delta; else f.total=delta; }
                                else {
                                    float2 offset=start.i==2 ? (pair==0?float2(1,0):float2(0,1)) : (pair<3?float2(1,pair-1):float2(0,1));
                                    float side=start.i==0?3:1, middle=start.i==0?10:2;
                                    float2 weight=start.i==2?offset:offset*float2(offset.y==0?middle:side,offset.x==0?middle:side);
                                    if(start.j!=0) {f.sum+=weight.x*delta;f.total+=weight.y*delta;}
                                    else f.sum.xy+=weight*delta.x;
                                }
                            }
                            f.octave++;
                            if(f.octave<(start.i<0 || start.i==2?4:8))
                            { frames[level]=f; uv=VMSampleOffset(f,start); next=t.b+1; }
                            else {
                                if(start.i<0) { r[t.b]=f.sum; v=f.total; }
                                else if(start.j!=0) {float4 dx=f.sum/8,dy=f.total/8;v=sqrt(dx*dx+dy*dy);}
                                else v.xy=f.sum.xy/((start.i==2?2:start.i==0?32:8)*f.frequency);
                                uv=f.uv; pixel=f.pixel; seed=f.seed; level--;
                            }
                            break;
                        }
                        case 34:
                        {
                            float2 slope=r[t.a].xy*r[t.b].x*float2(r[t.c].x!=0?-1:1,r[t.d].x!=0?-1:1);
                            if(t.i==0) v.xyz=GraphEncodeNormal(GraphUnitNormal(float3(-slope,1)),t.j);
                            else v.xy=t.k==1?slope*0.5+0.5:slope;
                            break;
                        }
                        case 35:
                            v=abs(r[t.d].x)<=1e-7?0:r[t.a]*r[t.c].x;
                            if(t.i!=0) v.w=r[t.b].w;
                            break;
                        #endif
                        #endif
                        #if !defined(WT_VM_IMAGE_MATH)
                        case 20: case 21: case 22: case 25: case 26: case 27: case 28: case 36:
                        {
                            int dimensions=t.m==1?1:2;
                            #if defined(WT_VM_VOLUME)
                            dimensions=t.m;
                            #endif
                            float3 position=ActiveLanes(r[t.a],dimensions).xyz;
                            WTNoiseSettings settings=WTNoiseDefaults(asint(asuint((int)r[t.c].x)+asuint(seed)));
                            settings.scale=ActiveLanes(r[t.b],dimensions).xyz;
                            if(t.value.x==1) { settings.dpdx=ddx(position); settings.dpdy=ddy(position); }
                            else if(t.value.x==2) { settings.dpdx=r[(int)t.value.y].xyz; settings.dpdy=r[(int)t.value.z].xyz; }
                            else if(t.value.x==3)
                            {
                                float2 dx=(uv+pixel.xy*0.5)-(uv+pixel.xy*(-0.5));
                                float2 dy=(uv+pixel.zw*0.5)-(uv+pixel.zw*(-0.5));
                                settings.dpdx=t.value.z==1?float3(dx[(int)t.value.y],0,0):float3(dx,0);
                                settings.dpdy=t.value.z==1?float3(dy[(int)t.value.y],0,0):float3(dy,0);
                            }
                            #if defined(WT_VM_EXTENDED)
                            settings.period=ActiveLanes(r[t.n],dimensions).xyz;
                            // Noise value.w stores Offset's register plus one; zero leaves the default offset.
                            if(t.value.w>0) settings.offset=ActiveLanes(r[(int)t.value.w-1],dimensions).xyz;
                            #endif
                            #if defined(WT_VM_BASIS)
                            #if defined(WT_VM_EXTENDED)
                            if(t.op==20)
                            {
                                float2 savedRotation=WTNoiseInternalRotation;
                                if(t.i!=0) WTNoiseInternalRotation=WTNoiseInternalFlowRotation(r[t.d].x);
                                #if !defined(WT_VM_BASIS_DERIVATIVES)
                                v.x=WTNoiseInternalBasisSingle(position,settings,dimensions,t.o);
                                #else
                                int fields=1;
                                #if defined(WT_VM_VOLUME)
                                fields=dimensions==3 && t.j==2?3:1;
                                #endif
                                int originalSeed=settings.seed;
                                float3 gx=0,gy=0,gz=0;
                                // Reuse one kernel call site for the independently seeded 3D Curl fields.
                                #if defined(WT_VM_VOLUME)
                                [loop] for(int fieldIndex=0;fieldIndex<fields;fieldIndex++)
                                #else
                                const int fieldIndex=0;
                                #endif
                                {
                                    settings.seed=originalSeed^(fieldIndex==1?0x68bc21eb:fieldIndex==2?0x63d83595:0);
                                    float3 gradient;
                                    float value=WTNoiseInternalBasisSampleSingle(position,settings,dimensions,t.o,gradient);
                                    if(fieldIndex==0)
                                    {
                                        gx=gradient;
                                        v=t.j==0?float4(value,0,0,0):t.j==1?float4(gradient,0):float4(gradient.y,-gradient.x,0,0);
                                    }
                                    else if(fieldIndex==1) gy=gradient;
                                    else gz=gradient;
                                }
                                if(fields==3) v.xyz=float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
                                #endif
                                WTNoiseInternalRotation=savedRotation;
                            }
                            #else
                            if(t.op==20) v.x=WTNoisePerlin(r[t.a].xy,settings);
                            #endif
                            #endif
                            #if defined(WT_VM_GABOR)
                            if(t.op==21)
                            {
                                WTNoiseGaborSettings g=WTNoiseGaborDefaults();
                                g.frequency=r[t.d].x; g.bandwidth=r[t.e].x; g.anisotropy=r[t.f].x;
                                g.direction=float3(r[t.g].xy,dimensions==3?r[t.g].z:0);
                                g.phaseOffset=r[t.k].x;
                                #if defined(WT_VM_EXTENDED)
                                g.profile=t.i; g.width=r[t.l].x;
                                int fields=1;
                                #if defined(WT_VM_VOLUME)
                                fields=dimensions==3 && t.j==2?3:1;
                                #endif
                                int originalSeed=settings.seed;
                                float3 gx=0,gy=0,gz=0;
                                [loop] for(int fieldIndex=0;fieldIndex<fields;fieldIndex++)
                                {
                                    settings.seed=originalSeed^(fieldIndex==1?0x68bc21eb:fieldIndex==2?0x63d83595:0);
                                    WTNoiseGaborSample sample; float3 gradient;
                                    float value=WTNoiseInternalGabor(position,settings,g,dimensions,sample,gradient);
                                    if(fieldIndex==0)
                                    {
                                        gx=gradient;
                                        if(t.j==0) v.x=value;
                                        else if(t.j==1) v.xyz=gradient;
                                        else if(t.j==2) v.xy=float2(gradient.y,-gradient.x);
                                        else if(t.j==3) v.x=sample.phase;
                                        else if(t.j==4) v.x=sample.intensity;
                                        else v.x=sample.phasor;
                                    }
                                    else if(fieldIndex==1) gy=gradient;
                                    else gz=gradient;
                                }
                                if(fields==3) v.xyz=float3(gz.y-gy.z,gx.z-gz.x,gy.x-gx.y);
                                #else
                                v.x=WTNoiseGabor(r[t.a].xy,settings,g);
                                #endif
                            }
                            #endif
                            #if defined(WT_VM_CELLULAR)
                            if(t.op==22)
                            {
                                WTNoiseCellularSettings cell=WTNoiseCellularDefaults();
                                cell.jitter=r[t.d].x; cell.evolution=r[t.e].x; cell.smoothness=r[t.f].x;
                                cell.distribution=t.g; cell.metric=t.h; cell.output=t.i;
                                WTNoiseInternalCell sample;
                                float value=WTNoiseInternalCellular(position,settings,cell,dimensions,sample);
                                if(t.j==0) v.x=value;
                                else if(t.j==1) v.x=sample.distance1;
                                else if(t.j==2) v.x=sample.distance2;
                                else if(t.j==3) v.x=sample.value;
                                else v.xyz=sample.toPoint;
                            }
                            #endif
                            #if defined(WT_VM_CAUSTICS)
                            if(t.op==36) {
                                WTNoiseCausticsSettings caustics=WTNoiseCausticsDefaults();
                                caustics.strength=r[t.d].x; caustics.evolution=r[t.e].x;
                                WTCausticsPalette palette=WTCausticsMakePalette(1,1,1,1,1,1,true);
                                #if defined(WT_VM_PALETTES)
                                if(t.j!=0) {
                                    palette=WTCausticsMakePalette(
                                        VMPalette(t.g,0,t.h,0).rgb,VMPalette(t.g,0.2,t.h,0).rgb,
                                        VMPalette(t.g,0.4,t.h,0).rgb,VMPalette(t.g,0.6,t.h,0).rgb,
                                        VMPalette(t.g,0.8,t.h,0).rgb,VMPalette(t.g,1,t.h,0).rgb,t.k!=0);
                                }
                                #endif
                                v=float4(WTNoiseInternalCausticsSamplePalette(position.xy,settings,caustics,t.i,true,t.j!=0?r[t.f].x:0,palette),1);
                            }
                            #endif
                            #if defined(WT_VM_SHARD)
                            if(t.op==25) {
                                WTNoiseShardSettings shard=WTNoiseShardDefaults(); shard.sharpness=r[t.d].x;
                                v.x=WTNoiseInternalShard(position,settings,shard,dimensions);
                            }
                            #endif
                            #if defined(WT_VM_SPOTS)
                            if(t.op==26) {
                                WTNoiseSpotSettings spots=WTNoiseSpotDefaults();
                                spots.density=r[t.d].x;spots.radius=r[t.e].x;spots.softness=r[t.f].x;
                                spots.elongation=r[t.g].x;spots.direction=ActiveLanes(r[t.h],dimensions).xyz;
                                v.x=WTNoiseInternalSpots(position,settings,spots,dimensions);
                            }
                            #endif
                            #if defined(WT_VM_TRIANGLE)
                            if(t.op==27) {
                                WTNoiseTriangleVoronoiSettings triangleSettings=WTNoiseTriangleVoronoiDefaults();
                                triangleSettings.distribution=t.g;triangleSettings.jitter=r[t.d].x;triangleSettings.evolution=r[t.e].x;triangleSettings.stretch=r[t.f].xy;
                                WTNoiseTriangleVoronoiSample2D sample;
                                float value=WTNoiseInternalTriangle(position.xy,settings,triangleSettings,true,sample);
                                if(t.j==0) v.x=value;else if(t.j==1) v.x=sample.distance;else if(t.j==2) v.x=sample.value;
                                else v.xy=t.j==3?sample.toPoint:sample.borderNormal;
                            }
                            #endif
                            #if defined(WT_VM_WARP)
                            if(t.op==28) {
                                WTNoiseWarpSettings warp=WTNoiseWarpDefaults();warp.strength=r[t.d].x;warp.mode=t.h;warp.algorithm=t.i;
                                settings.fractal.octaves=(int)r[t.e].x;settings.fractal.lacunarity=r[t.f].x;settings.fractal.gain=r[t.g].x;
                                v.xyz=WTNoiseInternalDisplacement(position,settings,warp,dimensions);
                                if(t.j!=0) v.xyz+=position;
                            }
                            #endif
                            break;
                        }
                        #if defined(WT_VM_GRAIN)
                        case 29:
                        {
                            WTNoiseGrainSettings grain=WTNoiseGrainDefaults(r[t.b].xy,asint(asuint((int)r[t.c].x)+asuint(seed)));
                            grain.grainSize=r[t.d].x;grain.direction=r[t.e].x;grain.oneD=t.m==1;grain.color=t.j!=0;grain.inverted=t.k!=0;
                            float3 value=t.i!=0?WTNoiseBlue(r[t.a].xy,grain):WTNoiseWhite(r[t.a].xy,grain);
                            v=t.j!=0?float4(value,1):float4(value.x,0,0,0);break;
                        }
                        #endif
                        #if defined(WT_VM_FRACTAL)
                        case 30:
                        {
                            Frame f=(Frame)0;
                            f.uv=uv; f.pixel=pixel; f.seed=seed; f.octave=0; f.frequency=1; f.scalarAmplitude=1;
                            f.sum=0; f.total=0; f.amplitude=1; f.weight=1;
                            frames[++level]=f;
                            break;
                        }
                        case 31:
                        {
                            Frame frame=frames[level]; Instruction start=_VMCode[t.b];
                            WTNoiseFractal f;
                            f.mode=start.i; f.octaves=(int)r[start.a].x; f.lacunarity=r[start.b].x;
                            f.gain=r[start.c].x; f.weightedStrength=r[start.d].x; f.pingPongStrength=r[start.e].x;
                            float visibility=start.k!=0?WTFractalInternalVisibility(float3(pixel.xy,0),float3(pixel.zw,0)):1;
                            if(start.j<0) WTFractalInternalAccumulate(r[t.a],f,visibility,frame.octave,frame.sum,frame.total,frame.amplitude);
                            else WTFractalInternalCascade(r[t.a],start.j,f.gain,r[start.g].x,r[start.h].x,visibility,frame.octave,frame.sum,frame.weight,frame.scalarAmplitude);
                            frame.octave++;
                            if(frame.octave<WTNoiseInternalOctaves(f))
                            {
                                frame.frequency=WTNoiseInternalNextFrequency(frame.frequency,f.lacunarity);
                                uv=frame.uv*frame.frequency; pixel=frame.pixel*frame.frequency;
                                seed=WTNoiseInternalSeed(frame.seed,frame.octave,(int)r[start.f].x);
                                frames[level]=frame; next=t.b+1;
                            }
                            else
                            {
                                v=start.j<0?WTFractalInternalResolve(frame.sum,frame.total):frame.sum;
                                uv=frame.uv; pixel=frame.pixel; seed=frame.seed; level--;
                            }
                            break;
                        }
                        #endif
                        #endif
                    }
                    r[pc]=v; pc=next;
                }
                #if defined(WT_VM_SURFACE)
                int4 a=(int4)_VMSurface0,b=(int4)_VMSurface1;
                VMSurfaceTargets o;
                o.color=r[a.x];
                o.normal=float4(GraphDecodeNormal(r[a.y].xyz,_VMSurfaceNormalEncoding),r[a.z].x);
                o.position=float4(GraphCanvasPosition(r[a.w].xy)*_CanvasSize.xy,r[b.x].x,r[b.y].x);
                o.specular=float4(r[b.z].rgb,r[b.w].x*r[b.z].a);
                return o;
                #else
                return r[_VMResult];
                #endif
            }
            ENDHLSL
        }
    }
}
