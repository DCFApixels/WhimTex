using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class FillPatternSettings : IDisposable
    {
        public enum Shape { Triangles, Squares, Hexagons, Circles }
        public enum CircleLayout { Square, Dense }
        public enum CellColor { Uniform, Random, Pattern }
        public enum ColorBlend { Multiply, ReplaceRGB }
        public CellColor cellColor;
        public ColorBlend colorBlend;
        public int seed;
        public float variation = 1;
        public WhimTexGradient palette = GradientUtility.CreateLinearWhiteToBlack();
        public Shape shape = Shape.Hexagons;
        public CircleLayout circleLayout = CircleLayout.Dense;
        public float size = 64f;
        // Zero inherits the legacy scalar size until the axes are edited explicitly.
        public float sizeY;
        public bool linkSize = true;
        public Vector2 Size
        {
            get => new Vector2(Limit(size, 1, 16384, 64), Limit(sizeY == 0 ? size : sizeY, 1, 16384, 64));
            set { size = Limit(value.x, 1, 16384, 64); sizeY = Limit(value.y, 1, 16384, 64); }
        }
        internal Vector2 AdjustSize(Vector2 value)
        {
            Vector2 current = Size;
            value = new Vector2(Limit(value.x, 1, 16384, current.x), Limit(value.y, 1, 16384, current.y));
            if (!linkSize) return value;
            int axis = value.x != current.x ? 0 : 1;
            float scale = Mathf.Clamp(value[axis] / current[axis],
                1 / Mathf.Min(current.x, current.y), 16384 / Mathf.Max(current.x, current.y));
            return current * scale;
        }
        public float rotation;
        public Vector2 offset;
        public bool seamless;
        public float gap;
        public float roundness;
        public float bulge;
        public float distanceRange = 1f;
        public SDFLayerBehaviour.DistancePosition position = SDFLayerBehaviour.DistancePosition.Signed;
        public bool inverted;
        public AnimationCurve profile = AnimationCurve.Linear(0, 0, 1, 1);
        public WhimTexGradient gradient = GradientUtility.CreateLinearWhiteToBlack();

        [NonSerialized] private WhimTexGradientTexture gradientLut;
        [NonSerialized] private WhimTexGradientTexture paletteLut;
        [NonSerialized] private WhimTexCurveTexture profileLut;
        [NonSerialized] private ProceduralLayerThumbnail thumbnail;
        [NonSerialized] private Vector4[] vertices;
        [NonSerialized] private Vector2[] corners;
        internal Vector2 EffectiveCell { get; private set; }
        internal float EffectiveRotation { get; private set; }
        private const float Root3 = 1.73205080757f;
        internal bool Staggered => shape != Shape.Squares && !(shape == Shape.Circles && circleLayout == CircleLayout.Square);
        internal static float Limit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal Texture2D Preview(ColorFillLayerBehaviour layer, int size)
        {
            thumbnail ??= new ProceduralLayerThumbnail();
            var hash = new HashCode();
            hash.Add(shape); hash.Add(circleLayout); hash.Add(Size); hash.Add(rotation); hash.Add(offset);
            hash.Add(seamless); hash.Add(gap); hash.Add(roundness); hash.Add(bulge); hash.Add(distanceRange);
            hash.Add(position); hash.Add(inverted); hash.Add(gradient);
            hash.Add(cellColor); hash.Add(colorBlend); hash.Add(seed); hash.Add(variation); hash.Add(palette);
            if (profile != null) foreach (var key in profile.keys) hash.Add(key);
            var cell = EffectiveCell; float angle = EffectiveRotation;
            try { return thumbnail.Get(layer, size, hash.ToHashCode()); }
            finally { EffectiveCell = cell; EffectiveRotation = angle; }
        }

        internal LayerRenderContext Prepare(Material material, Layer layer, in LayerRenderContext context)
        {
            float width = context.compositor.width, height = context.compositor.height;
            Vector2 requested = Size;
            float angle = Limit(rotation, -360000, 360000, 0) * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            var rotatedX = new Vector2(c, s);
            var rotatedY = new Vector2(-s, c);
            Vector2 phase = new Vector2(Limit(offset.x, -1000000, 1000000, 0) / requested.x,
                Limit(offset.y, -1000000, 1000000, 0) / requested.y);
            Vector2 cell = requested;
            LayerRenderContext rendered;
            if (seamless)
            {
                // Fit the combined layer/group transform at the canvas center. A periodic
                // lattice cannot retain arbitrary shear or projective distortion.
                layer.SetRenderInverse(material, "_UvRow", context);
                Vector3 a = material.GetVector("_UvRow0"), b = material.GetVector("_UvRow1"), w = material.GetVector("_UvRow2");
                Vector3 center = new Vector3(.5f, .5f, 1);
                float denominator = Vector3.Dot(w, center);
                if (Mathf.Abs(denominator) < 1e-6f) denominator = 1e-6f;
                Vector2 source = new Vector2(Vector3.Dot(a, center), Vector3.Dot(b, center)) / denominator;
                Vector2 dx = (new Vector2(a.x, a.y) - source.x * new Vector2(w.x, w.y)) / denominator;
                Vector2 dy = (new Vector2(b.x, b.y) - source.y * new Vector2(w.x, w.y)) / denominator;
                Vector2 x = (dx * (width * c) + dy * (height * s)) / requested.x;
                Vector2 y = (dx * (-width * s) + dy * (height * c)) / requested.y;
                Vector2 physicalX = new Vector2(x.x / width, x.y / height);
                Vector2 physicalY = new Vector2(y.x / width, y.y / height);
                int quarter = Mathf.RoundToInt(Mathf.Atan2(physicalX.y, physicalX.x) / (Mathf.PI * .5f));
                EffectiveRotation = Mathf.Repeat(quarter * 90, 360);
                Vector2 axisX = (quarter & 3) == 0 ? Vector2.right : (quarter & 3) == 1 ? Vector2.up :
                    (quarter & 3) == 2 ? Vector2.left : Vector2.down;
                float handedness = x.x * y.y - x.y * y.x < 0 ? -1 : 1;
                Vector2 axisY = new Vector2(-axisX.y, axisX.x) * handedness;
                float extentX = axisX.x != 0 ? width : height, extentY = axisY.x != 0 ? width : height;
                float periodY = Staggered ? Root3 : 1;
                float countX = Mathf.Clamp(Mathf.Round(Limit(physicalX.magnitude * extentX, 1, 32768, 1)), 1, 32768);
                float countY = Mathf.Clamp(Mathf.Round(Limit(physicalY.magnitude * extentY / periodY, 1, 32768, 1)), 1, 32768);
                if (cellColor == CellColor.Pattern)
                {
                    int repeatX = !Staggered ? 2 : shape == Shape.Triangles ? 1 : 3;
                    int repeatY = !Staggered ? 2 : 1;
                    countX = Mathf.Clamp(Mathf.Round(countX / repeatX), 1, 32768 / repeatX) * repeatX;
                    countY = Mathf.Clamp(Mathf.Round(countY / repeatY), 1, 32768 / repeatY) * repeatY;
                }
                material.SetVector("_CellPeriod", new Vector2(countX, countY * (Staggered ? 2 : 1)));
                x = axisX * countX; y = axisY * (countY * periodY);
                Vector2 p = new Vector2((source.x - .5f) * width, (source.y - .5f) * height);
                Vector2 atCenter = new Vector2(Vector2.Dot(rotatedX, p) / requested.x, Vector2.Dot(rotatedY, p) / requested.y) - phase;
                // Reduce phase without changing the pattern; prevents precision loss on large offsets.
                atCenter.x = Mathf.Repeat(atCenter.x, cellColor == CellColor.Uniform ? 1 : countX);
                atCenter.y = Mathf.Repeat(atCenter.y, periodY * (cellColor == CellColor.Uniform ? 1 : countY));
                material.SetVector("_PatternRow0", new Vector4(x.x, x.y, atCenter.x - .5f * (x.x + x.y), 0));
                material.SetVector("_PatternRow1", new Vector4(y.x, y.y, atCenter.y - .5f * (y.x + y.y), 0));
                cell = new Vector2(extentX / countX, extentY / (countY * periodY));
                rendered = ProceduralUv.WithoutTransform(context);
            }
            else
            {
                rendered = ProceduralUv.Prepare(material, layer, context);
                material.SetVector("_PatternRow0", new Vector4(c * width / requested.x, s * height / requested.x,
                    -.5f * (c * width + s * height) / requested.x - phase.x, 0));
                material.SetVector("_PatternRow1", new Vector4(-s * width / requested.y, c * height / requested.y,
                    -.5f * (-s * width + c * height) / requested.y - phase.y, 0));
            }
            material.SetInt("_Seamless", seamless ? 1 : 0);
            EffectiveCell = cell;
            material.SetInt("_PatternShape", (int)shape);
            material.SetInt("_Staggered", Staggered ? 1 : 0);
            material.SetVector("_Cell", cell);
            PrepareContour(material, cell);
            material.SetFloat("_Bulge", Limit(bulge, 0, 1, 0));
            material.SetFloat("_DistanceRange", Limit(distanceRange, .001f, 16, 1));
            material.SetInt("_Position", (int)position);
            material.SetInt("_Inverted", inverted ? 1 : 0);
            gradient ??= GradientUtility.CreateLinearWhiteToBlack();
            gradientLut ??= new WhimTexGradientTexture();
            profileLut ??= new WhimTexCurveTexture();
            material.SetTexture("_GradientLut", gradientLut.GetTexture(gradient, ColorSpace.Gamma));
            material.SetTexture("_ProfileLut", profileLut.GetTexture(profile));
            material.SetInt("_GradientWrapMode", (int)gradient.WrapMode);
            material.SetInt("_CellColor", (int)cellColor);
            material.SetInt("_ColorBlend", (int)colorBlend);
            material.SetInteger("_Seed", seed);
            material.SetFloat("_Variation", Limit(variation, 0, 1, 1));
            if (cellColor != CellColor.Uniform)
            {
                palette ??= GradientUtility.CreateLinearWhiteToBlack();
                paletteLut ??= new WhimTexGradientTexture();
                material.SetTexture("_PaletteLut", paletteLut.GetTexture(palette, ColorSpace.Gamma));
            }
            return rendered;
        }

        private void PrepareContour(Material material, Vector2 cell)
        {
            corners ??= new Vector2[6]; vertices ??= new Vector4[6];
            int count = shape == Shape.Triangles ? 3 : shape == Shape.Squares ? 4 : 6;
            float fill = 1 - Limit(gap, 0, .99f, 0);
            if (shape == Shape.Triangles)
            {
                corners[0] = new Vector2(0, Root3 / 3);
                corners[1] = new Vector2(-.5f, -Root3 / 6);
                corners[2] = new Vector2(.5f, -Root3 / 6);
            }
            else if (shape == Shape.Squares)
            {
                corners[0] = new Vector2(-.5f, -.5f); corners[1] = new Vector2(.5f, -.5f);
                corners[2] = new Vector2(.5f, .5f); corners[3] = new Vector2(-.5f, .5f);
            }
            else for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * .5f + i * Mathf.PI / 3;
                corners[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) / Root3;
            }
            float inradius = float.MaxValue;
            for (int i = 0; i < count; i++) corners[i] = Vector2.Scale(corners[i], cell) * fill;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = corners[i], edge = corners[(i + 1) % count] - a;
                inradius = Mathf.Min(inradius, Mathf.Abs(edge.x * a.y - edge.y * a.x) / Mathf.Max(edge.magnitude, 1e-8f));
            }
            float rounding = inradius * Limit(roundness, 0, 1, 0);
            // Leave a tiny core at full rounding, avoiding degenerate distance segments.
            rounding = Mathf.Min(rounding, inradius * .9999f);
            for (int i = 0; i < count; i++)
            {
                Vector2 prev = (corners[i] - corners[(i + count - 1) % count]).normalized;
                Vector2 next = (corners[(i + 1) % count] - corners[i]).normalized;
                Vector2 n0 = new Vector2(-prev.y, prev.x), n1 = new Vector2(-next.y, next.x);
                Vector2 v = corners[i] + (n0 + n1) * (rounding / Mathf.Max(1 + Vector2.Dot(n0, n1), 1e-6f));
                vertices[i] = new Vector4(v.x, v.y, 0, 0);
            }
            float radius = Mathf.Min(cell.x, cell.y) * .5f * fill;
            material.SetVectorArray("_Vertices", vertices);
            material.SetInt("_VertexCount", count);
            material.SetFloat("_Rounding", rounding);
            material.SetFloat("_CircleRadius", radius);
            material.SetFloat("_Inradius", shape == Shape.Circles ? radius : inradius);
        }

        public void Dispose()
        {
            gradientLut?.Dispose(); gradientLut = null;
            paletteLut?.Dispose(); paletteLut = null;
            profileLut?.Dispose(); profileLut = null;
            thumbnail?.Dispose(); thumbnail = null;
        }
    }
}
