using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Constant-screening gradient/data solve with discrete seam-slope constraints.
    // Projected, FFT-preconditioned CG; all reductions and channel steps stay on GPU.
    internal static class ScreenedSeamless
    {
        private static readonly string[] TextureBindings={"_Source","_X","_Other","_InitialNorm","_Norm","_RZ","_PAP","_Direction","_OldRZ","_Z"};
        internal static RenderTexture Render(Texture input, int width, int height)
            => RenderConfigured(input, width, height, MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges, .05f);

        internal static RenderTexture RenderConfigured(Texture input, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float radius)
        {
            var m = WhimTexMaterials.ScreenedSeamless;
            var fft = WhimTexMaterials.GpuFourierTransform;
            if (m == null || fft == null || !m.shader.isSupported || !fft.shader.isSupported)
                throw new InvalidOperationException("Screened Poisson shader is unavailable.");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var owned = new List<RenderTexture>(24);
            RenderTexture Temp(int w, int h)
            {
                var t = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
                owned.Add(t); return t;
            }
            try
            {
                GL.sRGBWrite = false;
                if (edges == MakeSeamlessLayerBehaviour.PoissonEdges.None)
                {
                    var copy=Temp(width,height); Graphics.Blit(input,copy);
                    copy.filterMode=FilterMode.Bilinear;
                    owned.Remove(copy); return copy;
                }
                bool wrapX = edges != MakeSeamlessLayerBehaviour.PoissonEdges.TopAndBottom;
                bool wrapY = edges != MakeSeamlessLayerBehaviour.PoissonEdges.LeftAndRight;
                int fw = wrapX ? width : width * 2, fh = wrapY ? height : height * 2;
                if (fw > SystemInfo.maxTextureSize || fh > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Poisson direction requires a transform texture larger than this device supports.");
                var size = new Vector4(width, height, 1f / width, 1f / height);
                var fftSize = new Vector4(fw, fh, 1f / fw, 1f / fh);
                var planX=GpuFourierTransform.GetPlan(fw);var planY=GpuFourierTransform.GetPlan(fh);
                m.SetVector("_Size", size); fft.SetVector("_Size", fftSize);
                m.SetVector("_FftSize", fftSize);
                m.SetVector("_Axes", new Vector4(wrapX ? 1 : 0, wrapY ? 1 : 0, 0, 0));
                float length = radius * Mathf.Min(width, height);
                m.SetFloat("_Lambda", 1f / (length * length));
                var u = Temp(width,height); var x = Temp(width,height);
                var r = Temp(width,height); var p = Temp(width,height);
                var z = Temp(width,height); var ap = Temp(width,height);
                var scratch = Temp(width,height);
                var fa = Temp(fw,fh); var fb = Temp(fw,fh);
                var rz = Temp(1,1); var oldRz = Temp(1,1); var pap = Temp(1,1);
                var initialNorm = Temp(1,1); var norm = Temp(1,1);
                var reduction = new List<RenderTexture>(8);
                int rw = width, rh = height;
                do { rw = Mathf.Max(1,(rw+3)/4); rh = Mathf.Max(1,(rh+3)/4); reduction.Add(Temp(rw,rh)); }
                while (rw > 1 || rh > 1);

                void Project(RenderTexture source, RenderTexture destination)
                {
                    m.SetInt("_Axis",0); Graphics.Blit(source,scratch,m,1);
                    m.SetInt("_Axis",1); Graphics.Blit(scratch,destination,m,1);
                }
                void Apply(RenderTexture source, RenderTexture destination)
                {
                    Graphics.Blit(source,destination,m,2);
                    Project(destination,destination);
                }
                void Dot(RenderTexture a, RenderTexture b, RenderTexture destination)
                {
                    m.SetTexture("_Other",b);
                    Texture current = a;
                    for (int j=0;j<reduction.Count;j++)
                    {
                        m.SetVector("_ReduceSize",new Vector4(current.width,current.height,1f/current.width,1f/current.height));
                        m.SetInt("_Multiply",j==0?1:0);
                        var target=j==reduction.Count-1?destination:reduction[j];
                        Graphics.Blit(current,target,m,7); current=target;
                    }
                }
                void Precondition()
                {
                    for (int pair=0;pair<2;pair++)
                    {
                        m.SetInt("_Pair",pair); Graphics.Blit(r,fa,m,4);
                        GpuFourierTransform.TransformPlanned(ref fa,ref fb,fft,planX,0,false);
                        GpuFourierTransform.TransformPlanned(ref fa,ref fb,fft,planY,1,false);
                        Graphics.Blit(fa,fb,m,5); Swap(ref fa,ref fb);
                        GpuFourierTransform.TransformPlanned(ref fa,ref fb,fft,planY,1,true);
                        GpuFourierTransform.TransformPlanned(ref fa,ref fb,fft,planX,0,true);
                        if (pair == 0) Graphics.Blit(fa,z,m,6);
                        else
                        {
                            m.SetTexture("_Other",z);
                            Graphics.Blit(fa,scratch,m,12); Swap(ref scratch,ref z);
                        }
                    }
                    Project(z,z);
                }

                Graphics.Blit(input,u,m,0);
                Project(u,x);
                m.SetTexture("_Source",u); m.SetTexture("_X",x);
                // Solve for a correction to P*u. Sparse initial residual avoids
                // cancellation of lambda*u against large interior Laplacians.
                Graphics.Blit(u,r,m,3); Project(r,r);
                Dot(r,r,initialNorm); m.SetTexture("_InitialNorm",initialNorm);
                Precondition(); Graphics.Blit(z,p); Dot(r,z,rz);
                for (int iteration=0;iteration<16;iteration++)
                {
                    Dot(r,r,norm); m.SetTexture("_Norm",norm);
                    Apply(p,ap); Dot(p,ap,pap);
                    m.SetTexture("_RZ",rz); m.SetTexture("_PAP",pap);
                    m.SetTexture("_Direction",p); Graphics.Blit(x,scratch,m,8); Swap(ref x,ref scratch);
                    m.SetTexture("_Direction",ap); Graphics.Blit(r,scratch,m,9); Swap(ref r,ref scratch);
                    Precondition(); Swap(ref rz,ref oldRz); Dot(r,z,rz);
                    m.SetTexture("_RZ",rz); m.SetTexture("_OldRZ",oldRz);
                    m.SetTexture("_Z",z); Graphics.Blit(p,scratch,m,10); Swap(ref p,ref scratch);
                }
                Project(x,x);
                Graphics.Blit(x,scratch,m,11);
                scratch.filterMode = FilterMode.Bilinear;
                owned.Remove(scratch); return scratch;
            }
            finally
            {
                foreach (string property in TextureBindings)
                    m.SetTexture(property,null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                foreach (var rt in owned) RenderTexture.ReleaseTemporary(rt);
            }
        }
        private static void Swap(ref RenderTexture a, ref RenderTexture b) { var t=a; a=b; b=t; }
    }
}
