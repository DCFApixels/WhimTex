using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class MakeSeamlessLayerBehaviour : TargetedLayerBehaviour
    {
        public enum SeamlessMode { Mirror = 0, ScreenedPoisson = 2, OffsetBlend = 3, PatchQuilting = 4 }
        public enum QuiltingQuality { Draft, Normal, High }
        public enum QuiltingChannels { Linked, Independent }
        public PoissonEdges quiltingEdges;
        public float quiltingWidth = .2f;
        public float quiltingAlongSearch;
        public float quiltingFeather = 50f;
        public bool quiltingContrastCompensation;
        public float quiltingContrast = 1f;
        public QuiltingQuality quiltingQuality = QuiltingQuality.Normal;
        public int quiltingSeed;
        public QuiltingChannels quiltingChannels;
        public bool quiltingSeamCorrection;
        public PoissonEdges quiltingPoissonEdges;
        public float quiltingCorrectionRadius = .05f;
        public enum PoissonEdges { AllEdges, [InspectorName("Top & Bottom")] TopAndBottom, [InspectorName("Left & Right")] LeftAndRight, None }
        public PoissonEdges poissonEdges;
        public PoissonEdges mirrorPoissonEdges;
        public PoissonEdges offsetPoissonEdges;
        public SeamlessMode mode;
        internal SeamlessMode EffectiveMode => mode == SeamlessMode.Mirror || mode == SeamlessMode.OffsetBlend || mode == SeamlessMode.PatchQuilting ? mode : SeamlessMode.ScreenedPoisson;
        public static MakeSeamlessLayerBehaviour CreateDefault() => new MakeSeamlessLayerBehaviour
        {
            mode = SeamlessMode.OffsetBlend,
            edgeWidth = .2f, blendWidth = .2f,
            offsetTransitionStart = -.25f, mirrorTransitionStart = -.25f,
            offsetSeamCorrection = true, mirrorSeamCorrection = true,
            offsetPoissonEdges = PoissonEdges.AllEdges, mirrorPoissonEdges = PoissonEdges.AllEdges,
            leftEdge = true, rightEdge = true, bottomEdge = true, topEdge = true,
            horizontal = HorizontalDirection.LeftToRight, vertical = VerticalDirection.BottomToTop
        };
        public enum HorizontalDirection { Off, LeftToRight, RightToLeft }
        public enum VerticalDirection { Off, BottomToTop, TopToBottom }
        public HorizontalDirection horizontal = HorizontalDirection.LeftToRight;
        public VerticalDirection vertical = VerticalDirection.BottomToTop;
        public float blendWidth = .2f;
        public float mirrorTransitionStart;
        public float falloff = 1f;
        public bool mirrorContrastCompensation;
        public float mirrorContrast = 1f;
        public bool mirrorSeamCorrection;
        public bool mirrorAutoRadius = true;
        public float mirrorCorrectionRadius = .05f;
        internal float EffectiveMirrorRadius => mirrorAutoRadius
            ? Mathf.Max(.005f, Limit(blendWidth, .001f, .5f, .2f) * .25f)
            : Limit(mirrorCorrectionRadius, .005f, .25f, .05f);
        public bool leftEdge = true, rightEdge = true, bottomEdge = true, topEdge = true;
        public float screeningRadius = .05f;
        public float edgeWidth = .2f;
        public float offsetTransitionStart = .325f;
        public float histogramContrast = 1f;
        public bool offsetContrastCompensation = true;
        public bool offsetSeamCorrection = true;
        public bool offsetAutoRadius = true;
        public float offsetCorrectionRadius = .05f;
        public bool processRed = true, processGreen = true, processBlue = true, processAlpha = true;
        internal Vector4 SelectedEdges => new Vector4(leftEdge ? 1 : 0, rightEdge ? 1 : 0, bottomEdge ? 1 : 0, topEdge ? 1 : 0);

        public override string ToString() => "Make Seamless";
        internal override bool RequiresColorInput => true;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            if (context.input == null) return null;
            if (!processRed && !processGreen && !processBlue && !processAlpha)
                return ApplyTransformAndModifiers(context.input, context);
            if (EffectiveMode == SeamlessMode.PatchQuilting)
            {
                if (quiltingEdges == PoissonEdges.None && (!quiltingSeamCorrection || quiltingPoissonEdges == PoissonEdges.None))
                    return ApplyTransformAndModifiers(context.input, context);
                var quilt = context.compositor.RenderQuilting(this, context,
                    Limit(quiltingWidth,.02f,.45f,.2f), Limit(quiltingFeather,0,100,50),
                    new Vector4(processRed?1:0,processGreen?1:0,processBlue?1:0,processAlpha?1:0));
                try
                {
                    if (quiltingSeamCorrection && quiltingPoissonEdges != PoissonEdges.None)
                    {
                        var corrected = ScreenedSeamless.RenderConfigured(quilt, context.width, context.height,
                            quiltingPoissonEdges, Limit(quiltingCorrectionRadius,.005f,.25f,.05f));
                        RenderTexture.ReleaseTemporary(quilt); quilt = corrected;
                    }
                    return Finish(quilt, context);
                }
                finally { RenderTexture.ReleaseTemporary(quilt); }
            }
            if (EffectiveMode == SeamlessMode.OffsetBlend && (!offsetSeamCorrection || offsetPoissonEdges == PoissonEdges.None) && !leftEdge && !rightEdge && !bottomEdge && !topEdge)
                return ApplyTransformAndModifiers(context.input, context);
            if (EffectiveMode == SeamlessMode.OffsetBlend)
            {
                var histogram = HistogramSeamless.RenderOffset(context.input, context.width, context.height,
                    SelectedEdges, Limit(edgeWidth, .02f, .5f, .2f), offsetContrastCompensation ? Limit(histogramContrast, 0, 1, 1) : 0,
                    offsetSeamCorrection, offsetAutoRadius ? 0 : Limit(offsetCorrectionRadius,.005f,.25f,.05f), offsetPoissonEdges,
                    Limit(offsetTransitionStart, -1, .95f, .325f));
                try { return Finish(histogram, context); }
                finally { RenderTexture.ReleaseTemporary(histogram); }
            }
            if (EffectiveMode == SeamlessMode.ScreenedPoisson)
            {
                if (poissonEdges == PoissonEdges.None) return ApplyTransformAndModifiers(context.input, context);
                var screened = ScreenedSeamless.RenderConfigured(context.input, context.width, context.height,
                    poissonEdges, Limit(screeningRadius, .005f, .25f, .05f));
                try { return Finish(screened, context); }
                finally { RenderTexture.ReleaseTemporary(screened); }
            }
            if (horizontal == HorizontalDirection.Off && vertical == VerticalDirection.Off && (!mirrorSeamCorrection || mirrorPoissonEdges == PoissonEdges.None))
                return ApplyTransformAndModifiers(context.input, context);
            Material material = WhimTexMaterials.MakeSeamless;
            if (material == null) throw new InvalidOperationException("Make Seamless shader is unavailable.");
            RenderTexture result = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                var edges = new Vector4(horizontal == HorizontalDirection.RightToLeft ? 1 : 0,
                    horizontal == HorizontalDirection.LeftToRight ? 1 : 0,
                    vertical == VerticalDirection.TopToBottom ? 1 : 0,
                    vertical == VerticalDirection.BottomToTop ? 1 : 0);
                float contrast = Limit(mirrorContrast,0,1,1);
                if (mirrorContrastCompensation && contrast > 0)
                    result = HistogramSeamless.RenderMirrorTransition(context.input,context.width,context.height,
                        edges,Limit(blendWidth,.001f,.5f,.2f),Limit(falloff,.25f,4,1),contrast,Limit(mirrorTransitionStart,-1,.95f,0));
                else
                {
                    material.SetVector("_Directions", new Vector4((int)horizontal, (int)vertical, 0f, 0f));
                    material.SetFloat("_BlendWidth", Limit(blendWidth, .001f, .5f, .2f));
                    material.SetFloat("_TransitionStart", Limit(mirrorTransitionStart, -1, .95f, 0));
                    material.SetFloat("_Falloff", Limit(falloff, .25f, 4f, 1f));
                    result = RenderTexture.GetTemporary(context.width, context.height, 0,
                        RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    result.filterMode = FilterMode.Bilinear;
                    result.wrapMode = TextureWrapMode.Clamp;
                    Graphics.Blit(context.input, result, material, 0);
                }
                if (mirrorSeamCorrection && mirrorPoissonEdges != PoissonEdges.None)
                {
                    var corrected = ScreenedSeamless.RenderConfigured(result,context.width,context.height,
                        mirrorPoissonEdges, EffectiveMirrorRadius);
                    RenderTexture.ReleaseTemporary(result);
                    result = corrected;
                }
                return Finish(result, context);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (result != null) RenderTexture.ReleaseTemporary(result);
            }
        }

        private RenderTexture Finish(RenderTexture result, in LayerRenderContext context)
        {
            if (processRed && processGreen && processBlue && processAlpha)
                return ApplyTransformAndModifiers(result, context);
            var material = WhimTexMaterials.MakeSeamless;
            if (material == null) throw new InvalidOperationException("Make Seamless shader is unavailable.");
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var masked = RenderTexture.GetTemporary(context.width, context.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            try
            {
                GL.sRGBWrite = false;
                material.SetTexture("_Original", context.input);
                material.SetVector("_Channels", new Vector4(processRed ? 1 : 0, processGreen ? 1 : 0, processBlue ? 1 : 0, processAlpha ? 1 : 0));
                masked.filterMode = FilterMode.Bilinear;
                masked.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(result, masked, material, 1);
                return ApplyTransformAndModifiers(masked, context);
            }
            finally
            {
                material.SetTexture("_Original", null);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(masked);
            }
        }

        private static float Limit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
