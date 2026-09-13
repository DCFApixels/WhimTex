using System;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    [Serializable]
    public sealed class ShapeLayerBehaviour : LayerBehaviour
    {
        public enum ShapeKind { Rectangle, Ellipse, Polygon, Star, Line }
        public ShapeKind kind;
        public Color fillColor = Color.white;
        public bool fill = true;
        public bool stroke;
        public Color strokeColor = Color.black;
        public float strokeWidth = 2f;
        public float roundness;
        public Vector4 cornerRoundness = -Vector4.one;
        public bool linkCorners = true;
        public int sides = 5;
        public float innerRadius = .5f;
        [NonSerialized] private Vector4[] polygonVertices;
        [NonSerialized] private int cachedVertexCount;
        [NonSerialized] private float cachedInnerRadius;
        [NonSerialized] private ProceduralLayerThumbnail thumbnail;

        public override Texture2D GetPreviewTexture(int size)
        {
            var hash = new HashCode();
            hash.Add(kind); hash.Add(fillColor); hash.Add(fill); hash.Add(stroke); hash.Add(strokeColor);
            hash.Add(strokeWidth); hash.Add(GetCornerRoundness()); hash.Add(sides); hash.Add(innerRadius); hash.Add(filterMode);
            thumbnail ??= new ProceduralLayerThumbnail();
            return thumbnail.Get(this, size, hash.ToHashCode());
        }

        internal override void ReleaseTransientResources()
        {
            thumbnail?.Dispose();
            thumbnail = null;
        }

        private void SetPolygon(Material material)
        {
            bool star = kind == ShapeKind.Star;
            int count = Mathf.Clamp(sides, 3, 32) * (star ? 2 : 1);
            float inner = star ? Limit(innerRadius, .01f, 1f, .5f) : 1f;
            if (polygonVertices == null || cachedVertexCount != count || cachedInnerRadius != inner)
            {
                polygonVertices ??= new Vector4[64];
                for (int i = 0; i < count; i++)
                {
                    float angle = Mathf.PI * .5f + i * (Mathf.PI * 2f / count);
                    float radius = (i & 1) != 0 ? inner : 1f;
                    polygonVertices[i] = new Vector4(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f, 0f);
                }
                cachedVertexCount = count;
                cachedInnerRadius = inner;
            }
            material.SetInt("_ShapeVertexCount", count);
            material.SetVectorArray("_ShapeVertices", polygonVertices);
        }

        internal override void InitializeLayer(Layer layer) => layer.transform.scale = Vector2.one * .5f;
        internal static float Limit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        // Clockwise from top-left, in the shape's local (unrotated) orientation.
        internal Vector4 GetCornerRoundness()
        {
            Vector4 result = cornerRoundness;
            float uniform = Limit(roundness, 0f, 1f, 0f);
            for (int i = 0; i < 4; i++)
                result[i] = result[i] < 0f ? uniform : Limit(result[i], 0f, 1f, uniform);
            return result;
        }

        internal static Vector4 AdjustCorner(Vector4 values, int corner, float value, bool linked)
        {
            for (int i = 0; i < 4; i++) values[i] = Limit(values[i], 0f, 1f, 0f);
            value = Limit(value, 0f, 1f, values[corner]);
            if (!linked) { values[corner] = value; return values; }
            float previous = values[corner];
            float largest = Mathf.Max(Mathf.Max(values.x, values.y), Mathf.Max(values.z, values.w));
            if (previous > 0f)
            {
                double factor = Math.Min((double)value / previous, 1.0 / largest);
                for (int i = 0; i < 4; i++) values[i] = (float)(values[i] * factor);
            }
            else
                values += Vector4.one * Mathf.Min(value, 1f - largest);
            return values;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Material material = SpriteEditorMaterials.Shape;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Shape shader is unavailable or unsupported on this graphics device.");
            TextureTransform applied = context.applyTransform ? transform : TextureTransform.Default;
            float width = context.compositor.width, height = context.compositor.height;
            material.SetVector("_CanvasSize", new Vector4(width, height, 0f, 0f));
            material.SetVector("_ShapePivot", applied.pivot);
            material.SetVector("_ShapePosition", applied.position);
            material.SetVector("_ShapeScale", applied.scale);
            material.SetFloat("_ShapeRotation", applied.rotation * Mathf.Deg2Rad);
            material.SetInt("_ShapeTiling", (int)applied.tiling);
            material.SetInt("_ShapeKind", Mathf.Clamp((int)kind, 0, 4));
            material.SetVector("_ShapeFill", HdrUtility.Decode(fillColor));
            material.SetVector("_ShapeStroke", HdrUtility.Decode(strokeColor));
            material.SetVector("_ShapeStyle", new Vector4(fill ? 1f : 0f, stroke ? 1f : 0f,
                Limit(strokeWidth, 0f, 8192f, 2f), 0f));
            material.SetVector("_ShapeCorners", GetCornerRoundness());
            if (kind == ShapeKind.Polygon || kind == ShapeKind.Star) SetPolygon(material);
            var source = RenderTexture.GetTemporary(context.width, context.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(null, source, material);
                var renderedContext = new LayerRenderContext(context.compositor, context.input, context.width,
                    context.height, context.scaleMultiplier, applyTransform: false, applyModifiers: context.applyModifiers);
                return ApplyTransformAndModifiers(source, renderedContext);
            }
            finally
            {
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(source);
            }
        }
    }
}
