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
            #include "UnityCG.cginc"
            #include "ProceduralUv.cginc"
            #include "WTNoiseBasis.hlsl"
            #include "WTNoiseGabor.hlsl"
            #include "WTNoiseCellular.hlsl"

            struct Instruction
            {
                int op,a,b,c,d,e,f,g,h,i,j,k,l,m,n,o;
                float4 value;
            };
            StructuredBuffer<Instruction> _VMCode;
            int _VMCount, _VMResult;
            float _VMTime;
            struct Frame
            {
                float2 uv;
                float4 pixel;
                int seed, octave;
                float frequency, scalarAmplitude;
                float4 sum, total, amplitude, weight;
            };
            float4 ActiveLanes(float4 v,int count)
            { return float4(v.x,count>1?v.y:0,count>2?v.z:0,count>3?v.w:0); }
            float4 Fragment(v2f_img input) : SV_Target
            {
                float4 r[128];
                Frame frames[2];
                int level=-1, seed=0, pc=0;
                float2 uv=ProceduralSourceUv(input.uv);
                float4 pixel=float4(ddx(uv),ddy(uv));
                [loop] while(pc<_VMCount)
                {
                    Instruction t=_VMCode[pc];
                    float4 v=0;
                    int next=pc+1;
                    // Every operand is written before it is read; loop bodies have their own registers.
                    switch(t.op)
                    {
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
                        case 20: case 21: case 22:
                        {
                            WTNoiseSettings settings=WTNoiseDefaults(asint(asuint((int)r[t.c].x)+asuint(seed)));
                            settings.scale=float3(r[t.b].xy,0);
                            if(t.op==20) v.x=WTNoisePerlin(r[t.a].xy,settings);
                            else if(t.op==21)
                            {
                                WTNoiseGaborSettings g=WTNoiseGaborDefaults();
                                g.frequency=r[t.d].x; g.bandwidth=r[t.e].x; g.anisotropy=r[t.f].x; g.direction=float3(r[t.g].xy,0);
                                v.x=WTNoiseGabor(r[t.a].xy,settings,g);
                            }
                            else
                            {
                                WTNoiseCellularSettings cell=WTNoiseCellularDefaults();
                                cell.jitter=r[t.d].x; cell.evolution=r[t.e].x; cell.smoothness=r[t.f].x;
                                cell.distribution=t.g; cell.metric=t.h; cell.output=t.i;
                                WTNoiseCellularSample2D sample;
                                float value=WTNoiseCellular(r[t.a].xy,settings,cell,sample);
                                if(t.j==0) v.x=value;
                                else if(t.j==1) v.x=sample.distance1;
                                else if(t.j==2) v.x=sample.distance2;
                                else if(t.j==3) v.x=sample.value;
                                else v.xy=sample.toPoint;
                            }
                            break;
                        }
                        case 30:
                        {
                            Frame f;
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
                    }
                    r[pc]=v; pc=next;
                }
                return r[_VMResult];
            }
            ENDHLSL
        }
    }
}
