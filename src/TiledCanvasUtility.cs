using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class TiledCanvasUtility
    {
        internal static Vector2 Wrap(Vector2 uv) => new Vector2(uv.x - Mathf.Floor(uv.x), uv.y - Mathf.Floor(uv.y));

        internal static bool IsInvertible(TextureTransform transform) =>
            IsFinite(transform.scale.x) && IsFinite(transform.scale.y) &&
            IsFinite(transform.position.x) && IsFinite(transform.position.y) &&
            IsFinite(transform.pivot.x) && IsFinite(transform.pivot.y) && IsFinite(transform.rotation) &&
            Mathf.Abs(transform.scale.x) >= 0.00001f && Mathf.Abs(transform.scale.y) >= 0.00001f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static Vector2 ToDocument(Vector2 uv, TextureTransform transform, int width, int height)
        {
            Vector2 size = new Vector2(width, height);
            Vector2 local = Vector2.Scale(Vector2.Scale(uv - transform.pivot, size), transform.scale);
            float angle = transform.rotation * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            return transform.pivot + new Vector2(
                (cosine * local.x - sine * local.y + transform.position.x) / width,
                (sine * local.x + cosine * local.y + transform.position.y) / height);
        }

        internal static Vector2 ToSource(Vector2 uv, TextureTransform transform, int width, int height)
        {
            Vector2 local = Vector2.Scale(uv - transform.pivot, new Vector2(width, height)) - transform.position;
            float angle = transform.rotation * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            return transform.pivot + new Vector2(
                (cosine * local.x + sine * local.y) / (width * transform.scale.x),
                (-sine * local.x + cosine * local.y) / (height * transform.scale.y));
        }

        internal static Vector2 CanonicalSource(Vector2 uv, TextureTransform transform, int width, int height) =>
            ToSource(Wrap(ToDocument(uv, transform, width, height)), transform, width, height);

        internal static void GetPeriodBasis(TextureTransform transform, int width, int height, out Vector2 u, out Vector2 v)
        {
            float angle = transform.rotation * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            u = new Vector2(cosine * width / transform.scale.x, -sine * width / transform.scale.y);
            v = new Vector2(sine * height / transform.scale.x, cosine * height / transform.scale.y);
            // Reduce the document-period lattice in source pixels. Independent UV wrapping
            // does not find the closest brush copy after rotation and non-uniform scaling.
            for (int i = 0; i < 32; i++)
            {
                if (v.sqrMagnitude < u.sqrMagnitude) (u, v) = (v, u);
                float multiple = Mathf.Floor(Vector2.Dot(u, v) / u.sqrMagnitude + 0.5f);
                if (multiple == 0f) return;
                v -= multiple * u;
            }
        }

        internal static Vector2 NearestPeriodicDelta(Vector2 delta, Vector2 u, Vector2 v)
        {
            float cross = u.x * v.y - u.y * v.x;
            float row = Mathf.Floor((u.x * delta.y - u.y * delta.x) / cross + 0.5f);
            Vector2 best = Vector2.zero;
            float bestDistance = float.PositiveInfinity;
            for (int i = -1; i <= 1; i++)
            {
                Vector2 candidate = delta - (row + i) * v;
                candidate -= Mathf.Floor(Vector2.Dot(candidate, u) / u.sqrMagnitude + 0.5f) * u;
                float distance = candidate.sqrMagnitude;
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }
    }
}
