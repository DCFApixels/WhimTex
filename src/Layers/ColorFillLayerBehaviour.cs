using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "ColorFillLayerBehaviour")]
    [Serializable]
    public sealed class ColorFillLayerBehaviour : LayerBehaviour
    {
        public enum FillMode { Color, UV }
        public FillMode mode = FillMode.Color;
        internal override void InitializeLayer(Layer layer) => layer.transform.tiling = TransformTilingMode.Unbounded;
        [SerializeField] private Color storedColor = Color.white;

        public Color color
        {
            get => storedColor;
            set => storedColor = value;
        }

        private Color LinearColor => HdrUtility.Decode(storedColor);

        [NonSerialized] private Texture2D cachedPreview;
        [NonSerialized] private Color cachedColor;
        [NonSerialized] private FillMode cachedMode;

        public override Texture2D GetPreviewTexture(int size)
        {
            int previewSize = mode == FillMode.UV ? Mathf.Clamp(size, 2, 256) : 1;
            if (cachedPreview != null && cachedColor == color && cachedMode == mode && cachedPreview.width == previewSize)
                return cachedPreview;

            ReleaseTransientResources();
            cachedPreview = new Texture2D(previewSize, previewSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            if (mode == FillMode.UV)
            {
                var pixels = new Color[previewSize * previewSize];
                for (int y = 0; y < previewSize; y++)
                    for (int x = 0; x < previewSize; x++)
                        pixels[y * previewSize + x] = new Color((x + .5f) / previewSize, (y + .5f) / previewSize, 0, 1).gamma;
                cachedPreview.SetPixels(pixels);
            }
            else cachedPreview.SetPixel(0, 0, color);
            cachedPreview.Apply(false, false);
            cachedColor = color;
            cachedMode = mode;
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
            bool srgb = GL.sRGBWrite;
            try
            {
                if (mode == FillMode.UV)
                {
                    Material material = WhimTexMaterials.FillUv;
                    if (material == null || !material.shader.isSupported)
                        throw new InvalidOperationException("UV fill shader is unavailable or unsupported.");
                    GL.sRGBWrite = false;
                    var uvContext = ProceduralUv.Prepare(material, Owner, context);
                    Graphics.Blit(null, source, material, 0);
                    return ApplyTransformAndModifiers(source, uvContext);
                }
                RenderTexture.active = source;
                GL.Clear(true, true, LinearColor);
                var renderedContext = context.applyTransform && transform.tiling == TransformTilingMode.Unbounded
                    ? ProceduralUv.WithoutTransform(context) : context;
                return ApplyTransformAndModifiers(source, renderedContext);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
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
