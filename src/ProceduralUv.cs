using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ProceduralUv
    {
        internal static LayerRenderContext Prepare(Material material, TextureTransform transform, in LayerRenderContext context)
        {
            bool enabled = context.applyTransform && transform.tiling == TransformTilingMode.Unbounded;
            material.SetInt("_UnboundedUv", enabled ? 1 : 0);
            if (!enabled) return context;
            transform.ToMatrix(context.compositor.width, context.compositor.height).TryInverse(out var inverse);
            inverse.SetShader(material, "_UvRow");
            return WithoutTransform(context);
        }

        internal static LayerRenderContext WithoutTransform(in LayerRenderContext context) =>
            new LayerRenderContext(context.compositor, context.input, context.width, context.height,
                context.scaleMultiplier, applyTransform: false, applyModifiers: context.applyModifiers);
    }
}
