using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Pins are scoped to a synchronous worker job. No managed/native round-trip copies,
    // texture operations, or thread-affine Unity objects are involved.
    internal static unsafe class QuickSelectionRaster
    {
        internal sealed class Mask
        {
            internal readonly uint[] words;
            internal Mask(int count){words=new uint[(count+31)/32];}
            internal void Add(int i)=>words[i>>5]|=1u<<(i&31);
            internal IEnumerable<int> Indices()
            {
                for(int i=0;i<words.Length;i++)
                {uint word=words[i];if(word==0)continue;for(int b=0;b<32;b++)if((word&(1u<<b))!=0)yield return i*32+b;}
            }
        }
        internal static void Refine(byte[] old,byte[] result,int oldWidth,int width,int height,Mask mask,bool wrap)
        {
            fixed(byte* a=old,b=result)fixed(uint* c=mask.words)
                new RefineJob{source=a,result=b,active=c,oldWidth=oldWidth,width=width,height=height,wrap=wrap}.Run();
        }
        [BurstCompile]
        private struct RefineJob:IJob
        {
            [NativeDisableUnsafePtrRestriction]internal byte* source;
            [NativeDisableUnsafePtrRestriction]internal byte* result;
            [NativeDisableUnsafePtrRestriction]internal uint* active;
            internal int oldWidth,width,height;internal bool wrap;
            public void Execute()
            {
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)result[y*width+x]=source[y/2*oldWidth+x/2];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x,r=x+1<width?i+1:wrap?y*width:-1,u=y+1<height?i+width:wrap?x:-1;
                    if((r<0||result[i]==result[r])&&(u<0||result[i]==result[u]))continue;
                    for(int yy=y-3;yy<=y+3;yy++)for(int xx=x-3;xx<=x+3;xx++)
                    {
                        int bx=xx,by=yy;if(wrap){bx=(bx%width+width)%width;by=(by%height+height)%height;}
                        if(bx<0||by<0||bx>=width||by>=height)continue;int p=by*width+bx;active[p>>5]|=1u<<(p&31);
                    }
                }
            }
        }
        internal static void Publication(byte[] labels,byte[] baseline,byte[] existing,int width,int height,bool subtract,
            byte[] changed,RectInt[] bounds,byte[] uniform)
        {
            fixed(byte* a=labels,b=baseline,c=existing,d=changed,e=uniform)fixed(RectInt* f=bounds)
                new PublicationJob{labels=a,baseline=b,existing=c,changed=d,uniform=e,bounds=f,width=width,height=height,subtract=subtract}.Run();
        }
        [BurstCompile]
        private struct PublicationJob:IJob
        {
            [NativeDisableUnsafePtrRestriction]internal byte* labels;
            [NativeDisableUnsafePtrRestriction]internal byte* baseline;
            [NativeDisableUnsafePtrRestriction]internal byte* existing;
            [NativeDisableUnsafePtrRestriction]internal byte* changed;
            [NativeDisableUnsafePtrRestriction]internal byte* uniform;
            [NativeDisableUnsafePtrRestriction]internal RectInt* bounds;
            internal int width,height;internal bool subtract;
            public void Execute()
            {
                int columns=(width+255)/256,rows=(height+255)/256;
                for(int ty=0;ty<rows;ty++)for(int tx=0;tx<columns;tx++)
                {
                    int index=ty*columns+tx,left=tx*256,bottom=ty*256,right=math.min(width,left+256),top=math.min(height,bottom+256);
                    int x0=right,y0=top,x1=0,y1=0;bool full=true;
                    for(int y=bottom;y<top;y++)for(int x=left;x<right;x++)
                    {
                        int i=y*width+x;byte value=labels==null?(byte)0:labels[i];
                        if(baseline!=null&&!subtract)value=(byte)math.max((int)value,baseline[i]);
                        if(labels!=null)labels[i]=value;
                        if(value!=(existing==null?0:existing[i]))changed[index]=1;
                        if(value!=255)full=false;
                        if(value!=0){x0=math.min(x0,x);y0=math.min(y0,y);x1=math.max(x1,x+1);y1=math.max(y1,y+1);}
                    }
                    bounds[index]=x1>x0?new RectInt(x0,y0,x1-x0,y1-y0):default;
                    uniform[index]=(byte)(full?1:x1==0?0:2);
                }
            }
        }
    }
}
