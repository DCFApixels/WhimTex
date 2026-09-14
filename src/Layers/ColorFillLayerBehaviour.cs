using System;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    [Serializable]
    public sealed class ColorFillLayerBehaviour : LayerBehaviour
    {
        [SerializeField] private Color storedColor = Color.white;

        public Color color
        {
            get => storedColor;
            set => storedColor = value;
        }

        private Color LinearColor => HdrUtility.Decode(storedColor);

        [NonSerialized] private Texture2D cachedPreview;
        [NonSerialized] private Color cachedColor;

        public override Texture2D GetPreviewTexture(int size)
        {
            if (cachedPreview != null && cachedColor == color)
                return cachedPreview;

            ReleaseTransientResources();
            cachedPreview = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            cachedPreview.SetPixel(0, 0, color);
            cachedPreview.Apply(false, false);
            cachedColor = color;
            return cachedPreview;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            RenderTexture source = RenderTexture.GetTemporary(
                context.width,
                context.height,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                GL.Clear(true, true, LinearColor);
                var renderedContext = context.applyTransform && transform.tiling == TransformTilingMode.Unbounded
                    ? ProceduralUv.WithoutTransform(context) : context;
                return ApplyTransformAndModifiers(source, renderedContext);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(source);
            }
        }

        internal override void ReleaseTransientResources()
        {
            if (cachedPreview == null)
                return;
            UnityEngine.Object.DestroyImmediate(cachedPreview);
            cachedPreview = null;
        }
    }
}
