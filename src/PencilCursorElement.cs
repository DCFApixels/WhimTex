using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class PencilCursorElement : VisualElement
    {
        internal const int MaximumDetailSegments = 512;
        private const int MaximumDetailSize = 512;
        private readonly List<Vector2> contour = new List<Vector2>(MaximumDetailSegments + 4);
        private int cachedSize;
        private PencilShape cachedShape;
        private bool requestedDetail;
        private int cachedSmoothSegments;
        private Vector2 axisX, axisY, origin, translation;
        private float pixelRatio;
        private bool shown, erase;
        private int geometryVersion;
        internal bool Detailed { get; private set; }

        internal PencilCursorElement()
        {
            name = "pencilCursor";
            pickingMode = PickingMode.Ignore;
            usageHints = UsageHints.DynamicTransform;
            AddToClassList("whimtex-pencil-cursor");
            AddToClassList("whimtex-pencil-cursor--hidden");
            generateVisualContent += Draw;
        }

        internal void SetVisible(bool value)
        {
            if (shown == value) return;
            shown = value;
            EnableInClassList("whimtex-pencil-cursor--hidden", !value);
        }

        internal void SetState(int size, PencilShape shape, Vector2 center, Vector2 x, Vector2 y,
            bool erasing, float pixelsPerPoint)
        {
            size = Mathf.Clamp(size, 1, 4096);
            pixelsPerPoint = Mathf.Max(1f, pixelsPerPoint);
            float smallestPixel = Mathf.Min(x.magnitude, y.magnitude) * pixelsPerPoint;
            bool detail = smallestPixel >= (requestedDetail ? 1.75f : 2.25f);
            float screenRadius = size * 0.5f * Mathf.Max(x.magnitude, y.magnitude) * pixelsPerPoint;
            int smoothSegments = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * Mathf.Sqrt(screenRadius * 2f)), 32, 128);
            bool contourChanged = size != cachedSize || shape != cachedShape || detail != requestedDetail;
            if (!Detailed && smoothSegments != cachedSmoothSegments) contourChanged = true;
            if (contourChanged)
            {
                cachedSize = size;
                cachedShape = shape;
                requestedDetail = detail;
                cachedSmoothSegments = smoothSegments;
                Detailed = BuildContour(contour, size, shape, detail, smoothSegments);
            }
            bool geometryChanged = contourChanged || x != axisX || y != axisY || pixelsPerPoint != pixelRatio;
            if (geometryChanged)
            {
                axisX = x;
                axisY = y;
                pixelRatio = pixelsPerPoint;
                float radius = size * 0.5f;
                float padding = 2f / pixelRatio;
                origin = new Vector2((Mathf.Abs(x.x) + Mathf.Abs(y.x)) * radius + padding,
                    (Mathf.Abs(x.y) + Mathf.Abs(y.y)) * radius + padding);
                style.width = origin.x * 2f;
                style.height = origin.y * 2f;
                geometryVersion++;
            }
            // Translation changes do not invalidate the cached outline or request tessellation.
            Vector2 position = center - origin;
            if (position != translation)
            {
                translation = position;
                style.translate = new Translate(position.x, position.y);
            }
            if (geometryChanged || erase != erasing) MarkDirtyRepaint();
            erase = erasing;
            SetVisible(true);
        }

        internal static bool BuildContour(List<Vector2> points, int size, PencilShape shape, bool detail, int smoothSegments)
        {
            points.Clear();
            float radius = size * 0.5f;
            if (shape == PencilShape.Square || size == 1)
            {
                points.Add(new Vector2(-radius, -radius));
                points.Add(new Vector2(radius, -radius));
                points.Add(new Vector2(radius, radius));
                points.Add(new Vector2(-radius, radius));
                return true;
            }
            if (detail && size <= MaximumDetailSize && BuildPixelBoundary(points, size, shape)) return true;
            points.Clear();
            if (shape == PencilShape.Diamond)
            {
                points.Add(new Vector2(0f, -radius));
                points.Add(new Vector2(radius, 0f));
                points.Add(new Vector2(0f, radius));
                points.Add(new Vector2(-radius, 0f));
            }
            else
            {
                for (int i = 0; i < smoothSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / smoothSegments;
                    points.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
                }
            }
            return false;
        }

        private static bool BuildPixelBoundary(List<Vector2> points, int size, PencilShape shape)
        {
            float radius = size * 0.5f;
            int Left(int row)
            {
                double dy = row + 0.5 - radius;
                double limit = radius * 1.00001;
                double extent = shape == PencilShape.Diamond ? limit - System.Math.Abs(dy)
                    : System.Math.Sqrt(System.Math.Max(0, limit * limit - dy * dy));
                return Mathf.Clamp((int)System.Math.Ceiling(radius - 0.5 - extent), 0, size - 1);
            }
            void Add(float x, float y)
            {
                Vector2 next = new Vector2(x - radius, y - radius);
                int count = points.Count;
                if (count > 0 && points[count - 1] == next) return;
                if (count > 1)
                {
                    Vector2 a = points[count - 1] - points[count - 2], b = next - points[count - 1];
                    if (a.x * b.y == a.y * b.x && Vector2.Dot(a, b) >= 0f)
                    {
                        points[count - 1] = next;
                        return;
                    }
                }
                points.Add(next);
            }
            Add(Left(0), 0);
            for (int row = 0; row < size; row++)
            {
                int right = size - Left(row);
                Add(right, row);
                Add(right, row + 1);
                if (points.Count > MaximumDetailSegments) return false;
            }
            for (int row = size - 1; row >= 0; row--)
            {
                int left = Left(row);
                Add(left, row + 1);
                Add(left, row);
                if (points.Count > MaximumDetailSegments) return false;
            }
            if (points[points.Count - 1] == points[0]) points.RemoveAt(points.Count - 1);
            return true;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (!shown || contour.Count < 3 || contentRect.width <= 0f || contentRect.height <= 0f) return;
            Painter2D painter = context.painter2D;
            painter.lineJoin = LineJoin.Miter;
            painter.lineCap = LineCap.Butt;
            for (int pass = 0; pass < 2; pass++)
            {
                painter.lineWidth = (pass == 0 ? 3f : 1f) / pixelRatio;
                painter.strokeColor = pass == 0 ? new Color(0f, 0f, 0f, 0.95f)
                    : erase ? new Color(1f, 0.35f, 0.25f, 1f) : new Color(1f, 1f, 1f, 0.95f);
                painter.BeginPath();
                for (int i = 0; i < contour.Count; i++)
                {
                    Vector2 point = origin + contour[i].x * axisX + contour[i].y * axisY;
                    if (i == 0) painter.MoveTo(point);
                    else painter.LineTo(point);
                }
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
