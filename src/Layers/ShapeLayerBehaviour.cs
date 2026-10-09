using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class ShapeLayerBehaviour : LayerBehaviour
    {
        public enum ShapeKind { Rectangle, Ellipse, Polygon, Star, Line, Arc, Sector }
        public enum FeatherPosition { Inside, Outside, Centered }
        public enum StrokePosition { Inside, Center, Outside }
        public enum LineCap { Butt, Round, Square }
        public enum EdgeMode { Antialiased, Step }
        public enum CornerStyle { Round, Bevel }
        [Serializable]
        public struct Corner
        {
            public CornerStyle style;
            public float amount;
            public Corner(float amount, CornerStyle style = CornerStyle.Round) { this.amount = amount; this.style = style; }
        }
        public ShapeKind kind;
        public Color fillColor = Color.white;
        public bool fill = true;
        public bool stroke;
        public Color strokeColor = Color.black;
        public float strokeWidth = 2f;
        public float arcThickness = 8f;
        public StrokePosition strokePosition;
        public LineCap lineCap = LineCap.Round;
        public EdgeMode edgeMode;
        public float feather;
        public FeatherPosition featherPosition = FeatherPosition.Centered;
        public Corner[] rectangleCorners = new Corner[4];
        public Corner[] polygonCorners = new Corner[5];
        public Corner outerCorner, innerCorner;
        public bool linkCorners = true;
        public int sides = 5;
        public float innerRadius = .5f;
        public float startAngle;
        public float sweepAngle = 90f;
        [NonSerialized] private ShapeContour contour;
        [NonSerialized] private ProceduralLayerThumbnail thumbnail;
        internal ShapeContour Contour => contour ??= new ShapeContour();

        public override Texture2D GetPreviewTexture(int size)
        {
            EnsureCorners();
            var hash = new HashCode();
            hash.Add(kind); hash.Add(fillColor); hash.Add(fill); hash.Add(stroke); hash.Add(strokeColor);
            hash.Add(strokeWidth); hash.Add(arcThickness); hash.Add(strokePosition); hash.Add(lineCap); hash.Add(edgeMode);
            hash.Add(sides); hash.Add(innerRadius); hash.Add(filterMode); hash.Add(startAngle); hash.Add(sweepAngle);
            AddCornerHash(ref hash);
            hash.Add(feather); hash.Add(featherPosition);
            thumbnail ??= new ProceduralLayerThumbnail();
            return thumbnail.Get(this, size, hash.ToHashCode());
        }

        internal override void ReleaseTransientResources()
        {
            thumbnail?.Dispose();
            thumbnail = null;
            contour = null;
        }

        internal void AddCornerHash(ref HashCode hash)
        {
            if (rectangleCorners != null) foreach (var value in rectangleCorners) hash.Add(value);
            if (polygonCorners != null) foreach (var value in polygonCorners) hash.Add(value);
            hash.Add(outerCorner); hash.Add(innerCorner);
        }

        internal override void InitializeLayer(Layer layer) => layer.transform.scaleF = Vector2.one * .5f;
        internal static float Limit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal void EnsureCorners()
        {
            sides = Mathf.Clamp(sides, 3, 32);
            if (rectangleCorners == null || rectangleCorners.Length != 4) Array.Resize(ref rectangleCorners, 4);
            if (polygonCorners == null || polygonCorners.Length != sides) Array.Resize(ref polygonCorners, sides);
            for (int i = 0; i < rectangleCorners.Length; i++) rectangleCorners[i] = Clean(rectangleCorners[i]);
            for (int i = 0; i < polygonCorners.Length; i++) polygonCorners[i] = Clean(polygonCorners[i]);
            outerCorner = Clean(outerCorner); innerCorner = Clean(innerCorner);
        }
        private static Corner Clean(Corner value) => new Corner(Limit(value.amount, 0f, 1f, 0f),
            value.style == CornerStyle.Bevel ? CornerStyle.Bevel : CornerStyle.Round);
        internal Vector2 GeometryHalfSize(WhimTexDocument document)
        {
            if (document == null) return Vector2.one;
            Owner.RenderTransform.GetDisplay(new Vector2(document.width, document.height), out _, out var scale, out _);
            return new Vector2(Mathf.Max(1e-5f, Mathf.Abs((float)scale.x) * document.width * .5f),
                Mathf.Max(1e-5f, Mathf.Abs((float)scale.y) * document.height * .5f));
        }
        private static Vector2 SafeHalfSize(Vector2 size) => new Vector2(Limit(Mathf.Abs(size.x), 1e-5f, 1e9f, 1f), Limit(Mathf.Abs(size.y), 1e-5f, 1e9f, 1f));
        public void NormalizeCorners(Vector2 halfSize) { EnsureCorners(); ShapeContour.Restrict(this, SafeHalfSize(halfSize)); }
        public void SetCorner(int index, Corner value, Vector2 halfSize)
        {
            EnsureCorners();
            ShapeContour.Edit(this, index, Clean(value), SafeHalfSize(halfSize));
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Material material = WhimTexMaterials.Shape;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Shape shader is unavailable or unsupported on this graphics device.");
            TextureTransform applied = context.applyTransform ? Owner.RenderTransform : TextureTransform.Default;
            float width = context.activeDocument.width, height = context.activeDocument.height;
            material.SetVector("_CanvasSize", new Vector4(width, height, 0f, 0f));
            Owner.SetRenderInverse(material,"_ShapeRow",context);
            applied.GetDisplay(new Vector2(width,height),out _,out var displayScale,out _);
            material.SetVector("_ShapeScale",(Vector2)displayScale);
            material.SetInt("_ShapeTiling", (int)applied.tiling);
            material.SetInt("_ShapeKind", Mathf.Clamp((int)kind, 0, 6));
            material.SetInt("_ShapeEdgeMode", (int)edgeMode);
            material.SetInt("_ShapeLineCap", (int)lineCap);
            material.SetFloat("_ShapeArcThickness", Limit(arcThickness, 0f, 8192f, 8f));
            material.SetVector("_ShapeFill", HdrUtility.Decode(fillColor));
            material.SetVector("_ShapeStroke", HdrUtility.Decode(strokeColor));
            material.SetVector("_ShapeStyle", new Vector4(fill ? 1f : 0f, stroke ? 1f : 0f,
                Limit(strokeWidth, 0f, 8192f, 2f), (int)strokePosition));
            material.SetVector("_ShapeFeather", new Vector4(edgeMode == EdgeMode.Step ? 0f : Limit(feather, 0f, 8192f, 0f),
                featherPosition == FeatherPosition.Inside ? 0f : featherPosition == FeatherPosition.Outside ? 1f : .5f, 0f, 0f));
            material.SetVector("_ShapeAngles", new Vector4(Limit(startAngle, -360000f, 360000f, 0f) * Mathf.Deg2Rad,
                Limit(sweepAngle, 0f, 360f, 90f) * Mathf.Deg2Rad, 0, 0));
            if (kind == ShapeKind.Rectangle || kind == ShapeKind.Polygon || kind == ShapeKind.Star || kind == ShapeKind.Sector)
            {
                contour ??= new ShapeContour();
                contour.SetMaterial(material, this, new Vector2(Mathf.Abs((float)displayScale.x) * width * .5f, Mathf.Abs((float)displayScale.y) * height * .5f));
            }
            var source = RenderTexture.GetTemporary(context.width, context.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            source.filterMode = edgeMode == EdgeMode.Step ? FilterMode.Point : FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(null, source, material);
                var renderedContext = new LayerRenderContext(context.activeDocument, context.input, context.width,
                    context.height, context.scaleMultiplier, applyTransform: false, applyFx: context.applyFx);
                return ApplyTransformAndFx(source, renderedContext);
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
