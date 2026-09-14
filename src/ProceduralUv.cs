using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    internal static class ProceduralUv
    {
        internal static LayerRenderContext Prepare(Material material, TextureTransform transform, in LayerRenderContext context)
        {
            bool enabled = context.applyTransform && transform.tiling == TransformTilingMode.Unbounded;
            material.SetInt("_UnboundedUv", enabled ? 1 : 0);
            if (!enabled) return context;
            material.SetVector("_UvCanvas", new Vector4(context.compositor.width, context.compositor.height, 0, 0));
            material.SetVector("_UvPivot", transform.pivot);
            material.SetVector("_UvPosition", transform.position);
            material.SetVector("_UvScale", transform.scale);
            material.SetFloat("_UvRotation", transform.rotation * Mathf.Deg2Rad);
            return WithoutTransform(context);
        }

        internal static LayerRenderContext WithoutTransform(in LayerRenderContext context) =>
            new LayerRenderContext(context.compositor, context.input, context.width, context.height,
                context.scaleMultiplier, applyTransform: false, applyModifiers: context.applyModifiers);
    }
}
