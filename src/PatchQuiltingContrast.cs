using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Histogram transfer shared with the other seamless methods; moments use the
    // selected quilting donor rather than the fixed half-period/reflection offsets.
    internal sealed class PatchQuiltingContrast : IDisposable
    {
        private const int TableSize = 4096, SampleLimit = 128, SampleCount = SampleLimit * SampleLimit;
        private struct Statistics { internal float low, extent, meanA, meanB, varianceA, varianceB, covariance; }
        private NativeArray<float> ordered, knots, ranks, planes;
        private NativeArray<Statistics> stats;
        private NativeArray<float4> packed;
        private Texture2D table;
        private readonly SeamlessQuantiles quantiles=new SeamlessQuantiles();

        internal long Bytes => !ordered.IsCreated ? 0 :
            (long)(ordered.Length + knots.Length + ranks.Length + planes.Length) * 4 +
            stats.Length * 28L + packed.Length * 16L + TableSize * 2L * 32 + quantiles.Bytes;

        internal void Configure(Material material, NativeArray<float4> source, int n, int rows, int band, float4 donors)
            => ConfigureShifted(material,source,n,rows,band,donors,default);

        internal void ConfigureShifted(Material material, NativeArray<float4> source, int n, int rows, int band, float4 donors,float4 shifts)
        {
            Ensure();
            quantiles.Ensure(Math.Min(n,SampleLimit)*Math.Min(rows,SampleLimit),true);
            JobHandle pending = default;
            try
            {
                pending = new BuildJob { source=source, ordered=ordered, knots=knots, ranks=ranks,
                    planes=planes, stats=stats, quantiles=quantiles.values, n=n, rows=rows, band=band, donors=donors,shifts=shifts }.Schedule(4,1);
                pending = new PackJob { planes=planes, packed=packed }.Schedule(TableSize*2,64,pending);
                pending.Complete();
                table.SetPixelData(packed,0); table.Apply(false,false);
                float4 low=default, extent=default, ma=default, mb=default, va=default, vb=default, cov=default;
                for(int c=0;c<4;c++)
                {
                    var s=stats[c];low[c]=s.low;extent[c]=s.extent;ma[c]=s.meanA;mb[c]=s.meanB;
                    va[c]=s.varianceA;vb[c]=s.varianceB;cov[c]=s.covariance;
                }
                material.SetTexture("_Tables",table);
                material.SetVector("_Minimum",(Vector4)low); material.SetVector("_Extent",(Vector4)extent);
                material.SetVector("_MeanA",(Vector4)ma);material.SetVector("_MeanB",(Vector4)mb);
                material.SetVector("_VarianceA",(Vector4)va);material.SetVector("_VarianceB",(Vector4)vb);
                material.SetVector("_Covariance",(Vector4)cov);
            }
            finally { pending.Complete(); }
        }

        private void Ensure()
        {
            if(ordered.IsCreated)return;
            try
            {
                ordered=new NativeArray<float>(4*SampleCount,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                knots=new NativeArray<float>(4*SampleCount,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                ranks=new NativeArray<float>(4*SampleCount,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                planes=new NativeArray<float>(4*TableSize*2,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                stats=new NativeArray<Statistics>(4,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                packed=new NativeArray<float4>(TableSize*2,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                table=new Texture2D(TableSize,2,TextureFormat.RGBAFloat,false,true)
                    {filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if(ordered.IsCreated)ordered.Dispose();if(knots.IsCreated)knots.Dispose();if(ranks.IsCreated)ranks.Dispose();
            if(planes.IsCreated)planes.Dispose();if(stats.IsCreated)stats.Dispose();if(packed.IsCreated)packed.Dispose();
            ordered=knots=ranks=planes=default;stats=default;packed=default;
            if(table!=null)UnityEngine.Object.DestroyImmediate(table);table=null;
            quantiles.Dispose();
        }

        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct BuildJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> source;
            [ReadOnly] public NativeArray<float> quantiles;
            [NativeDisableParallelForRestriction] public NativeArray<float> ordered,knots,ranks,planes;
            [WriteOnly] public NativeArray<Statistics> stats;
            public int n,rows,band;
            public float4 donors,shifts;
            public void Execute(int c)
            {
                int sw=Math.Min(n,SampleLimit),sh=Math.Min(rows,SampleLimit),count=sw*sh;
                int offset=c*SampleCount,tableOffset=c*TableSize*2;
                for(int y=0;y<sh;y++)for(int x=0;x<sw;x++)
                    ordered[offset+y*sw+x]=Finite(source[Cell(y,rows,sh)*n+Cell(x,n,sw)][c]);
                ordered.GetSubArray(offset,count).Sort();
                float low=ordered[offset],extent=ordered[offset+count-1]-low;
                if(extent<=1e-20f)
                {
                    for(int i=0;i<TableSize;i++){planes[tableOffset+i]=0;planes[tableOffset+TableSize+i]=low;}
                    stats[c]=new Statistics{low=low,extent=extent};return;
                }
                int unique=0;
                for(int i=0;i<count;)
                {
                    int end=i+1;while(end<count&&ordered[offset+end]==ordered[offset+i])end++;
                    knots[offset+unique]=ordered[offset+i];ranks[offset+unique]=quantiles[i+end];
                    unique++;i=end;
                }
                int forward=0,inverse=0;
                for(int i=0;i<TableSize;i++)
                {
                    planes[tableOffset+i]=Interpolate(knots,ranks,offset,unique,low+extent*i/(TableSize-1),ref forward);
                    planes[tableOffset+TableSize+i]=Interpolate(ranks,knots,offset,unique,-5f+10f*i/(TableSize-1),ref inverse);
                }
                int columns=Math.Min(2*band,SampleLimit),donor=(int)donors[c];
                double aSum=0,bSum=0,aa=0,bb=0,ab=0;
                for(int y=0;y<sh;y++)for(int x=0;x<columns;x++)
                {
                    int row=Cell(y,rows,sh),strip=Cell(x,2*band,columns);
                    double a=Lookup(tableOffset,Finite(source[row*n+(n-band+strip)%n][c]),low,extent);
                    int column=Math.Min(n-1,donor+strip);
                    float donorValue=shifts[c]==0?source[row*n+column][c]:PatchQuiltingSeamless.ShiftedSample(source,n,rows,column,row,shifts[c])[c];
                    double b=Lookup(tableOffset,Finite(donorValue),low,extent);
                    aSum+=a;bSum+=b;aa+=a*a;bb+=b*b;ab+=a*b;
                }
                double samples=sh*columns,ma=aSum/samples,mb=bSum/samples;
                double va=Math.Max(0,aa/samples-ma*ma),vb=Math.Max(0,bb/samples-mb*mb);
                double covariance=ab/samples-ma*mb,bound=Math.Sqrt(va*vb);
                stats[c]=new Statistics {low=low,extent=extent,meanA=(float)ma,meanB=(float)mb,
                    varianceA=(float)va,varianceB=(float)vb,covariance=(float)Math.Max(-bound,Math.Min(bound,covariance))};
            }
            private static int Cell(int i,int length,int count)=>Math.Min(length-1,(int)((i+.5f)*length/count));
            private static float Finite(float x)=>math.isfinite(x)?x:0;
            private static float Interpolate(NativeArray<float> x,NativeArray<float> y,int offset,int count,float value,ref int at)
            {
                while(at+1<count&&x[offset+at+1]<value)at++;
                if(value<=x[offset])return y[offset];if(at+1==count)return y[offset+at];
                float t=math.saturate((value-x[offset+at])/(x[offset+at+1]-x[offset+at]));
                return y[offset+at]+(y[offset+at+1]-y[offset+at])*t;
            }
            private float Lookup(int offset,float value,float low,float extent)
            {
                if(value<=low)return planes[offset];if(value>=low+extent)return planes[offset+TableSize-1];
                int left=0,right=TableSize-1;
                while(left+1<right){int middle=(left+right)/2;if(planes[offset+TableSize+middle]<value)left=middle;else right=middle;}
                float a=planes[offset+TableSize+left],b=planes[offset+TableSize+right];
                float t=b>a?math.saturate((value-a)/(b-a)):.5f;
                return -5f+10f*(left+t)/(TableSize-1);
            }
        }
        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct PackJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> planes;
            [WriteOnly] public NativeArray<float4> packed;
            public void Execute(int i)=>packed[i]=new float4(planes[i],planes[TableSize*2+i],planes[TableSize*4+i],planes[TableSize*6+i]);
        }
    }
}
