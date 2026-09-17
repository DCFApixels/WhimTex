using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ProceduralUv
    {
        internal static LayerRenderContext Prepare(Material material, Layer layer, in LayerRenderContext context)
        {
            bool enabled = context.applyTransform && layer.transform.tiling == TransformTilingMode.Unbounded;
            material.SetInt("_UnboundedUv", enabled ? 1 : 0);
            if (!enabled) return context;
            layer.SetRenderInverse(material, "_UvRow", context);
            return WithoutTransform(context);
        }

        internal static LayerRenderContext WithoutTransform(in LayerRenderContext context) =>
            new LayerRenderContext(context.compositor, context.input, context.width, context.height,
                context.scaleMultiplier, applyTransform: false, applyModifiers: context.applyModifiers,
                transformFxCoordinates: context.transformFxCoordinates);
    }
}
