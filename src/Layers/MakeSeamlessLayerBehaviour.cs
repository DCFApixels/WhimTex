using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "MakeSeamlessLayerBehaviour")]
    [Serializable]
    public sealed partial class MakeSeamlessLayerBehaviour : TargetedLayerBehaviour
    {
        public enum HorizontalDirection { Off, LeftToRight, RightToLeft }
        public enum VerticalDirection { Off, BottomToTop, TopToBottom }
        public HorizontalDirection horizontal = HorizontalDirection.LeftToRight;
        public VerticalDirection vertical = VerticalDirection.BottomToTop;
        public float blendWidth = .2f;
        public float falloff = 1f;

        public override string ToString() => "Make Seamless";
        internal override bool RequiresColorInput => true;

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            if (context.input == null) return null;
            if (horizontal == HorizontalDirection.Off && vertical == VerticalDirection.Off)
                return ApplyTransformAndModifiers(context.input, context);
            Material material = WhimTexMaterials.MakeSeamless;
            if (material == null) throw new InvalidOperationException("Make Seamless shader is unavailable.");
            RenderTexture result = null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetVector("_Directions", new Vector4((int)horizontal, (int)vertical, 0f, 0f));
                material.SetFloat("_BlendWidth", Limit(blendWidth, .001f, .5f, .2f));
                material.SetFloat("_Falloff", Limit(falloff, .25f, 4f, 1f));
                result = RenderTexture.GetTemporary(context.width, context.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(context.input, result, material, 0);
                return ApplyTransformAndModifiers(result, context);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (result != null) RenderTexture.ReleaseTemporary(result);
            }
        }

        private static float Limit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
