using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class PreviewViewport
    {
        internal const float MinimumScale = 1f / 1024f;
        internal const float MaximumScale = 64f;
        private const float FitInset = 36f;
        private bool fit = true;
        private float scale = 1f;
        private Vector2 center = new Vector2(0.5f, 0.5f);
        private float rotation, cosine = 1f, sine;
        internal float Rotation => rotation;

        internal void SetRotation(float degrees, bool snap = false)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            if (snap)
            {
                float nearest = Mathf.Round(degrees / 90f) * 90f;
                if (Mathf.Abs(degrees - nearest) <= 3f) degrees = nearest;
            }
            rotation = Mathf.Repeat(degrees + 180f, 360f) - 180f;
            cosine = Mathf.Cos(rotation * Mathf.Deg2Rad);
            sine = Mathf.Sin(rotation * Mathf.Deg2Rad);
        }

        // ImageRect and document tools stay in unrotated canvas coordinates.
        // Only the presentation and the incoming pointer cross this boundary.
        internal Vector2 ToViewDelta(Vector2 delta) => new Vector2(
            cosine * delta.x - sine * delta.y, sine * delta.x + cosine * delta.y);
        internal Vector2 ToCanvasDelta(Vector2 delta) => new Vector2(
            cosine * delta.x + sine * delta.y, -sine * delta.x + cosine * delta.y);
        internal Vector2 ToView(Rect viewport, Vector2 point) => viewport.center + ToViewDelta(point - viewport.center);
        internal Vector2 ToCanvas(Rect viewport, Vector2 point) => viewport.center + ToCanvasDelta(point - viewport.center);

        internal Rect VisibleCanvasBounds(Rect viewport)
        {
            Vector2 a = ToCanvas(viewport, viewport.min);
            Vector2 b = ToCanvas(viewport, new Vector2(viewport.xMax, viewport.yMin));
            Vector2 c = ToCanvas(viewport, viewport.max);
            Vector2 d = ToCanvas(viewport, new Vector2(viewport.xMin, viewport.yMax));
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal static float WheelScale(float currentScale, float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return currentScale;
            return Mathf.Clamp(currentScale * Mathf.Pow(1.2f, Mathf.Clamp(-delta / 3f, -12f, 12f)),
                MinimumScale, MaximumScale);
        }

        internal void Reset()
        {
            fit = true;
            scale = 1f;
            center = new Vector2(0.5f, 0.5f);
            SetRotation(0f);
        }

        internal Rect ImageRect(Rect viewport, Vector2 dimensions)
        {
            if (!Valid(viewport, dimensions)) return Rect.zero;
            float currentScale = fit
                ? Mathf.Min(Mathf.Max(0f, viewport.width - FitInset * 2f) / dimensions.x,
                    Mathf.Max(0f, viewport.height - FitInset * 2f) / dimensions.y)
                : scale;
            Vector2 size = dimensions * currentScale;
            return new Rect(viewport.center - Vector2.Scale(center, size), size);
        }

        internal void ZoomAt(Rect viewport, Vector2 dimensions, Rect current, Vector2 anchor, float nextScale)
        {
            if (!Valid(viewport, dimensions) || current.width <= 0f || current.height <= 0f ||
                float.IsNaN(nextScale) || float.IsInfinity(nextScale)) return;
            anchor = ToCanvas(viewport, anchor);
            Vector2 uv = new Vector2((anchor.x - current.x) / current.width, (anchor.y - current.y) / current.height);
            scale = Mathf.Clamp(nextScale, MinimumScale, MaximumScale);
            Vector2 size = dimensions * scale;
            center = uv + new Vector2((viewport.center.x - anchor.x) / size.x, (viewport.center.y - anchor.y) / size.y);
            fit = false;
        }

        internal void Frame(Rect viewport, Vector2 dimensions, Rect current, Rect selection)
        {
            if (!Valid(viewport, dimensions) || current.width <= 0f || current.height <= 0f ||
                selection.width < 4f || selection.height < 4f) return;
            Vector2 selectionCenter = ToCanvas(viewport, selection.center);
            center = new Vector2((selectionCenter.x - current.x) / current.width,
                (selectionCenter.y - current.y) / current.height);
            float ratio = Mathf.Min(Mathf.Max(1f, viewport.width - 8f) / selection.width,
                Mathf.Max(1f, viewport.height - 8f) / selection.height);
            scale = Mathf.Clamp(current.width / dimensions.x * ratio, MinimumScale, MaximumScale);
            fit = false;
        }

        internal void Pan(Rect viewport, Vector2 dimensions, Rect current, Vector2 delta)
        {
            if (!Valid(viewport, dimensions) || current.width <= 0f || current.height <= 0f) return;
            ZoomAt(viewport, dimensions, current, viewport.center, current.width / dimensions.x);
            delta = ToCanvasDelta(delta);
            center -= new Vector2(delta.x / (dimensions.x * scale), delta.y / (dimensions.y * scale));
        }

        private static bool Valid(Rect viewport, Vector2 dimensions) =>
            viewport.width > 0f && viewport.height > 0f && dimensions.x > 0f && dimensions.y > 0f &&
            !float.IsNaN(viewport.x) && !float.IsNaN(viewport.y) &&
            !float.IsInfinity(viewport.width) && !float.IsInfinity(viewport.height) &&
            !float.IsInfinity(viewport.x) && !float.IsInfinity(viewport.y) &&
            !float.IsInfinity(dimensions.x) && !float.IsInfinity(dimensions.y);
    }
}
