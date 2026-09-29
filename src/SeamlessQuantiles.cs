using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace DCFApixels.WhimTex
{
    internal sealed class SeamlessQuantiles : IDisposable
    {
        internal NativeArray<float> values;
        private int sampleCount;
        internal long Bytes=>values.IsCreated?values.Length*4L:0;
        internal void Ensure(int count,bool burst)
        {
            if(count==sampleCount)return;
            if(!values.IsCreated||values.Length<2*count+1)
            {
                Dispose();values=new NativeArray<float>(2*count+1,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
            }
            if(burst)new Fill{values=values,count=count}.Schedule(2*count-1,64).Complete();
            else for(int i=1;i<2*count;i++)values[i]=HistogramSeamless.InverseNormal(i*.5/count);
            sampleCount=count;
        }
        public void Dispose(){if(values.IsCreated)values.Dispose();values=default;sampleCount=0;}
        [BurstCompile(FloatMode=FloatMode.Strict)]
        private struct Fill:IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<float> values;
            public int count;
            public void Execute(int index){int rank=index+1;values[rank]=HistogramSeamless.InverseNormal(rank*.5/count);}
        }
    }
}
