using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class HistogramSeamless
    {
        private const int AnalysisSize = 128;

        internal static RenderTexture Render(Texture input, int width, int height)
            => RenderConfigured(input, width, height, Vector4.one, .2f, 1f);

        internal static RenderTexture RenderConfigured(Texture input, int width, int height,
            Vector4 edges, float bandWidth, float contrast)
            => RenderOffset(input,width,height,edges,bandWidth,contrast,false,0,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges);

        internal static RenderTexture RenderOffset(Texture input, int width, int height,
            Vector4 edges, float bandWidth, float contrast, bool correctSeam, float correctionRadius,
            MakeSeamlessLayerBehaviour.PoissonEdges poissonEdges, float transitionStart = .325f)
            => RenderCore(input,width,height,edges,bandWidth,contrast,false,1,correctSeam,correctionRadius,poissonEdges,transitionStart);

        internal static RenderTexture RenderMirror(Texture input, int width, int height,
            Vector4 edges, float bandWidth, float falloff, float contrast)
            => RenderMirrorTransition(input,width,height,edges,bandWidth,falloff,contrast,0);

        internal static RenderTexture RenderMirrorTransition(Texture input, int width, int height,
            Vector4 edges, float bandWidth, float falloff, float contrast, float transitionStart)
            => RenderCore(input,width,height,edges,bandWidth,contrast,true,falloff,false,0,MakeSeamlessLayerBehaviour.PoissonEdges.AllEdges,transitionStart);

        private static RenderTexture RenderCore(Texture input, int width, int height,
            Vector4 edges, float bandWidth, float contrast, bool mirror, float falloff,
            bool correctSeam, float correctionRadius, MakeSeamlessLayerBehaviour.PoissonEdges poissonEdges, float transitionStart = .325f)
        {
            var material = WhimTexMaterials.HistogramSeamless;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Offset Blend shader is unavailable.");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture source = null, analysis = null, gaussian = null, result = null;
            SeamlessHistogramWorkspace workspace = null;
            bool reusable = false;
            try
            {
                GL.sRGBWrite = false;
                material.SetVector("_Edges",edges);
                material.SetFloat("_BandWidth",bandWidth);
                material.SetFloat("_TransitionStart",transitionStart);
                material.SetFloat("_Contrast",contrast);
                material.SetInt("_Mirror",mirror ? 1 : 0);
                material.SetFloat("_Falloff",falloff);
                source = Temporary(width, height);
                Graphics.Blit(input, source, material, 0);
                material.SetVector("_Size", new Vector4(width, height, 1f / width, 1f / height));
                if (contrast <= 0)
                {
                    result = Temporary(width,height);
                    Graphics.Blit(source,result,material,1);
                    CorrectOffset(ref result,width,height,poissonEdges,bandWidth,!mirror && correctSeam,correctionRadius);
                    var plain = result; result = null; return plain;
                }
                int aw = Math.Min(width, AnalysisSize), ah = Math.Min(height, AnalysisSize);
                analysis = Temporary(aw, ah);
                material.SetVector("_Size", new Vector4(width, height, 1f / width, 1f / height));
                material.SetVector("_AnalysisSize", new Vector4(aw, ah, 1f / aw, 1f / ah));
                Graphics.Blit(source, analysis, material, 2);
                workspace = SeamlessHistogramWorkspace.Rent();
                workspace.Configure(analysis,material,mirror);
                material.SetVector("_Size", new Vector4(width, height, 1f / width, 1f / height));
                gaussian = Temporary(width, height);
                Graphics.Blit(source, gaussian, material, 3);
                material.SetTexture("_Gaussian", gaussian);
                result = Temporary(width, height);
                Graphics.Blit(source, result, material, 1);
                CorrectOffset(ref result,width,height,poissonEdges,bandWidth,!mirror && correctSeam,correctionRadius);
                reusable = true;
                var returned = result; result = null; return returned;
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                material.SetTexture("_Tables", null);
                material.SetTexture("_Gaussian", null);
                if (source != null) RenderTexture.ReleaseTemporary(source);
                if (analysis != null) RenderTexture.ReleaseTemporary(analysis);
                if (gaussian != null) RenderTexture.ReleaseTemporary(gaussian);
                if (result != null) RenderTexture.ReleaseTemporary(result);
                if (workspace != null)
                {
                    if (reusable) SeamlessHistogramWorkspace.Return(workspace); else workspace.Dispose();
                }
            }
        }

        private static void CorrectOffset(ref RenderTexture result, int width, int height,
            MakeSeamlessLayerBehaviour.PoissonEdges edges, float bandWidth, bool enabled, float radius)
        {
            if (!enabled || edges == MakeSeamlessLayerBehaviour.PoissonEdges.None) return;
            var corrected = ScreenedSeamless.RenderConfigured(result,width,height,edges,
                radius > 0 ? radius : Mathf.Max(.005f,bandWidth*.25f));
            RenderTexture.ReleaseTemporary(result);
            result = corrected;
        }

        private static RenderTexture Temporary(int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Point; rt.wrapMode = TextureWrapMode.Clamp; return rt;
        }

        internal static float InverseNormal(double probability)
        {
            double signed = 2 * probability - 1;
            double log = Math.Log(1 - signed * signed);
            double first = 2 / (Math.PI * .147) + log * .5;
            double x = Math.Sign(signed) * Math.Sqrt(2 * (Math.Sqrt(first * first - log / .147) - first));
            for (int i = 0; i < 2; i++)
            {
                double a = Math.Abs(x), t = 1 / (1 + .2316419 * a);
                double density = Math.Exp(-a * a * .5) / Math.Sqrt(2 * Math.PI);
                double tail = density * t *
                    (.319381530 + t * (-.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
                double cdf = x >= 0 ? 1 - tail : tail;
                x -= (cdf - probability) / density;
            }
            return (float)x;
        }
    }
}
