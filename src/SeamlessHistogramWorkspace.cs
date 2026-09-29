using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class SeamlessHistogramWorkspace : IDisposable
    {
        private const int TableSize=4096, Capacity=128*128;
        private struct Statistics { internal float low,range,mean,variance,x,y,xy,xny; }
        private NativeArray<float> values,knots,ranks,z,planes;
        private NativeArray<float4> packed;
        private NativeArray<Statistics> statistics;
        private Texture2D readback,table;
        private readonly SeamlessQuantiles quantiles=new SeamlessQuantiles();
        // One idle owner, bounded by 128² analysis and 4096-entry tables (~2.2 MiB).
        private static SeamlessHistogramWorkspace spare;
        private static double idleSince;

        static SeamlessHistogramWorkspace()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=Clear;
            EditorApplication.quitting+=Clear;
            EditorApplication.update+=Trim;
        }
        private static void Trim(){if(spare!=null&&EditorApplication.timeSinceStartup-idleSince>=30)Clear();}
        private static void Clear(){var old=spare;spare=null;old?.Dispose();}
        internal static SeamlessHistogramWorkspace Rent()
        {var result=spare;spare=null;return result??new SeamlessHistogramWorkspace();}
        internal static void Return(SeamlessHistogramWorkspace item)
        {Clear();spare=item;idleSince=EditorApplication.timeSinceStartup;}

        internal void Configure(RenderTexture analysis,Material material,bool mirror)
        {
            Ensure(analysis.width,analysis.height);
            quantiles.Ensure(analysis.width*analysis.height,false);
            var previous=RenderTexture.active;JobHandle pending=default;
            try
            {
                RenderTexture.active=analysis;
                readback.ReadPixels(new Rect(0,0,analysis.width,analysis.height),0,0,false);
                pending=new Build {pixels=readback.GetPixelData<float4>(0),values=values,knots=knots,ranks=ranks,z=z,planes=planes,
                    statistics=statistics,quantiles=quantiles.values,width=analysis.width,height=analysis.height,mirror=mirror}.Schedule(4,1);
                pending=new Pack{planes=planes,packed=packed}.Schedule(TableSize*2,64,pending);
                pending.Complete();table.SetPixelData(packed,0);table.Apply(false,false);
                float4 low=default,range=default,mean=default,variance=default,x=default,y=default,xy=default,xny=default;
                for(int c=0;c<4;c++)
                {
                    var s=statistics[c];low[c]=s.low;range[c]=s.range;mean[c]=s.mean;variance[c]=s.variance;
                    x[c]=s.x;y[c]=s.y;xy[c]=s.xy;xny[c]=s.xny;
                }
                material.SetTexture("_Tables",table);material.SetVector("_Minimum",(Vector4)low);material.SetVector("_Extent",(Vector4)range);
                material.SetVector("_Mean",(Vector4)mean);material.SetVector("_Variance",(Vector4)variance);
                material.SetVector("_CovX",(Vector4)x);material.SetVector("_CovY",(Vector4)y);
                material.SetVector("_CovXY",(Vector4)xy);material.SetVector("_CovXNY",(Vector4)xny);
            }
            finally{pending.Complete();RenderTexture.active=previous;}
        }
        private void Ensure(int width,int height)
        {
            if(width*height>Capacity)throw new ArgumentOutOfRangeException(nameof(width));
            if(!values.IsCreated)
            {
                values=new NativeArray<float>(4*Capacity,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                knots=new NativeArray<float>(4*Capacity,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                ranks=new NativeArray<float>(4*Capacity,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                z=new NativeArray<float>(4*Capacity,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                planes=new NativeArray<float>(4*TableSize*2,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                packed=new NativeArray<float4>(TableSize*2,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                statistics=new NativeArray<Statistics>(4,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                table=new Texture2D(TableSize,2,TextureFormat.RGBAFloat,false,true)
                    {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            }
            if(readback!=null&&readback.width==width&&readback.height==height)return;
            if(readback!=null)UnityEngine.Object.DestroyImmediate(readback);
            readback=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
        }
        public void Dispose()
        {
            if(values.IsCreated)values.Dispose();if(knots.IsCreated)knots.Dispose();if(ranks.IsCreated)ranks.Dispose();
            if(z.IsCreated)z.Dispose();if(planes.IsCreated)planes.Dispose();if(packed.IsCreated)packed.Dispose();if(statistics.IsCreated)statistics.Dispose();
            values=knots=ranks=z=planes=default;packed=default;statistics=default;
            if(readback!=null)UnityEngine.Object.DestroyImmediate(readback);if(table!=null)UnityEngine.Object.DestroyImmediate(table);
            readback=table=null;
            quantiles.Dispose();
        }
        [BurstCompile(FloatMode=FloatMode.Strict, FloatPrecision=FloatPrecision.High)]
        private struct Build:IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> pixels;
            [ReadOnly] public NativeArray<float> quantiles;
            [NativeDisableParallelForRestriction] public NativeArray<float> values,knots,ranks,z,planes;
            [WriteOnly] public NativeArray<Statistics> statistics;
            public int width,height;public bool mirror;
            public void Execute(int channel)
            {
                int length=width*height,offset=channel*Capacity,table=channel*TableSize*2;
                for(int i=0;i<length;i++){float v=pixels[i][channel];values[offset+i]=math.isfinite(v)?v:0;}
                values.GetSubArray(offset,length).Sort();
                float low=values[offset],range=values[offset+length-1]-low;
                var result=new Statistics{low=low,range=range};
                if(range<=1e-20f)
                {
                    for(int i=0;i<TableSize;i++){planes[table+i]=0;planes[table+TableSize+i]=low;}
                    statistics[channel]=result;return;
                }
                int count=0;
                for(int i=0;i<length;)
                {
                    int end=i+1;while(end<length&&values[offset+end]==values[offset+i])end++;
                    knots[offset+count]=values[offset+i];ranks[offset+count++]=quantiles[i+end];i=end;
                }
                int forward=0,inverse=0;
                for(int i=0;i<TableSize;i++)
                {
                    // Explicit intermediates preserve the original managed evaluation
                    // before float storage, including nearly transparent HDR inputs.
                    planes[table+i]=Interpolate(knots,ranks,offset,count,(float)(low+(double)range*i/(TableSize-1)),ref forward);
                    planes[table+TableSize+i]=Interpolate(ranks,knots,offset,count,(float)(-5d+10d*i/(TableSize-1)),ref inverse);
                }
                double sum=0,square=0,sx=0,sy=0,sxy=0,sxny=0;
                for(int i=0;i<length;i++)
                {float v=Lookup(table,pixels[i][channel],low,range);z[offset+i]=v;sum+=v;square+=(double)v*v;}
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int xx=mirror?width-1-x:(x+width-width/2)%width;
                    int yy=mirror?height-1-y:(y+height-height/2)%height;
                    int oppositeY=mirror?yy:(y+height/2)%height;
                    double v=z[offset+y*width+x];sx+=v*z[offset+y*width+xx];sy+=v*z[offset+yy*width+x];
                    sxy+=v*z[offset+yy*width+xx];sxny+=v*z[offset+oppositeY*width+xx];
                }
                double average=sum/length,m2=average*average;
                result.mean=(float)average;result.variance=(float)Math.Max(0,square/length-m2);
                result.x=(float)(sx/length-m2);result.y=(float)(sy/length-m2);result.xy=(float)(sxy/length-m2);result.xny=(float)(sxny/length-m2);
                statistics[channel]=result;
            }
            private static float Interpolate(NativeArray<float> x,NativeArray<float> y,int offset,int count,float value,ref int at)
            {
                while(at+1<count&&x[offset+at+1]<value)at++;
                if(value<=x[offset])return y[offset];if(at+1==count)return y[offset+at];
                float t=Mathf.Clamp01((float)(((double)value-x[offset+at])/((double)x[offset+at+1]-x[offset+at])));
                return (float)(y[offset+at]+((double)y[offset+at+1]-y[offset+at])*t);
            }
            private float Lookup(int offset,float value,float low,float range)
            {
                if(value<=low)return planes[offset];if(value>=(double)low+range)return planes[offset+TableSize-1];
                int left=0,right=TableSize-1;
                while(left+1<right){int middle=(left+right)/2;if(planes[offset+TableSize+middle]<value)left=middle;else right=middle;}
                float a=planes[offset+TableSize+left],b=planes[offset+TableSize+right];
                float t=b>a?Mathf.Clamp01((float)(((double)value-a)/((double)b-a))):.5f;
                return (float)(-5d+10d*(left+(double)t)/(TableSize-1));
            }
        }
        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct Pack:IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> planes;
            [WriteOnly] public NativeArray<float4> packed;
            public void Execute(int i)=>packed[i]=new float4(planes[i],planes[TableSize*2+i],planes[TableSize*4+i],planes[TableSize*6+i]);
        }
    }
}
