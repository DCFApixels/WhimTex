using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // A borrowed image view. Coordinates describe the complete input at the render
    // scale, independently of the physical allocation. This type never owns pixels.
    internal readonly struct ShaderTextureView
    {
        internal readonly Texture texture;
        internal readonly Vector4 uvMap, texelSize;
        internal static readonly Vector4 Identity = new Vector4(1, 1, 0, 0);
        internal static readonly int MainMapId = Shader.PropertyToID("_WhimTex_Texture_MainTex_UvMap");
        internal static readonly int MainSizeId = Shader.PropertyToID("_WhimTex_Texture_MainTex_Size");

        internal ShaderTextureView(Texture texture, Rect region, Vector2 fullSize)
        {
            if (!(region.width > 0) || !(region.height > 0) ||
                !(fullSize.x > 0) || !(fullSize.y > 0))
                throw new ArgumentOutOfRangeException(nameof(region), "An image view requires a positive region and complete input size.");
            this.texture = texture;
            uvMap = new Vector4(1f / region.width, 1f / region.height,
                -region.x / region.width, -region.y / region.height);
            texelSize = new Vector4(1f / fullSize.x, 1f / fullSize.y, fullSize.x, fullSize.y);
        }

        internal static void BindFull(Material material, int mapId, int sizeId)
        {
            material.SetVector(mapId, Identity);
            // A full image needs no size override. Let Unity's _TexelSize follow
            // the actual bound texture, including later external replacements.
            material.SetVector(sizeId, Vector4.zero);
        }

        internal static ShaderTextureView ForContext(Texture texture, in LayerRenderContext context)
        {
            var document = context.activeDocument;
            if (!document.RenderingViewport)
                return new ShaderTextureView(texture, new Rect(0, 0, 1, 1), new Vector2(context.width, context.height));
            var viewport = document.ViewportToCanvas;
            return new ShaderTextureView(texture,
                new Rect((float)viewport.m02, (float)viewport.m12, (float)viewport.m00, (float)viewport.m11),
                new Vector2(Mathf.Ceil(document.width / context.scaleMultiplier),
                    Mathf.Ceil(document.height / context.scaleMultiplier)));
        }

        internal void BindMetadata(Material material, int mapId, int sizeId)
        {
            material.SetVector(mapId, uvMap);
            material.SetVector(sizeId, texelSize);
        }
    }
}
