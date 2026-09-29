using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class GpuFourierTransform
    {
        private static void Swap(ref RenderTexture a, ref RenderTexture b) { var t = a; a = b; b = t; }

        internal static void Transform(ref RenderTexture a, ref RenderTexture b, Material material,
            int length, int axis, bool inverse)
        {
            if (length == 1) return;
            TransformPlanned(ref a,ref b,material,GetPlan(length),axis,inverse);
        }

        internal sealed class Plan
        {
            internal int count;
            // Unity's uniform-array API requires managed Vector4 storage, not NativeArray.
            internal readonly Vector4[] permutation=new Vector4[16];
        }
        private static readonly Dictionary<int,Plan> plans=new Dictionary<int,Plan>();
        internal static Plan GetPlan(int length)
        {
            if(plans.TryGetValue(length,out var cached))return cached;
            var plan=new Plan();
            var factors = new List<int>();
            int remaining = length;
            for (int divisor = 2; divisor <= remaining / divisor; divisor++)
                while (remaining % divisor == 0) { factors.Add(divisor); remaining /= divisor; }
            if (remaining > 1) factors.Add(remaining);
            factors.Reverse();
            int stride = length;
            for (int i = 0; i < factors.Count; i++)
            {
                stride /= factors[i];
                plan.permutation[i] = new Vector4(factors[i], stride, 0, 0);
            }
            plan.count=factors.Count;
            if(plans.Count>=16)plans.Clear();
            plans.Add(length,plan);return plan;
        }

        internal static void TransformPlanned(ref RenderTexture a,ref RenderTexture b,Material material,Plan plan,int axis,bool inverse)
        {
            if(plan.count==0)return;
            material.SetInt("_Axis", axis);
            material.SetInt("_FactorCount", plan.count);
            material.SetVectorArray("_Factors", plan.permutation);
            Graphics.Blit(a, b, material, 0); Swap(ref a, ref b);
            material.SetFloat("_Sign", inverse ? 1f : -1f);
            int previous = 1;
            for(int i=0;i<plan.count;i++)
            {
                int radix=(int)plan.permutation[i].x;
                material.SetInt("_Radix", radix);
                material.SetInt("_Previous", previous);
                material.SetFloat("_Scale", inverse ? 1f / radix : 1f);
                Graphics.Blit(a, b, material, 1); Swap(ref a, ref b);
                previous *= radix;
            }
        }
    }
}
