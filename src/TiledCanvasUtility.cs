using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class TiledCanvasUtility
    {
        internal static Vector2 Wrap(Vector2 uv) => new Vector2(uv.x - Mathf.Floor(uv.x), uv.y - Mathf.Floor(uv.y));

        internal static bool IsInvertible(TextureTransform transform) =>
            transform.storage == TransformStorage.Projective ? transform.matrix.ValidUnitQuad() :
            ProjectiveMatrix.Finite(transform.pivot.x) && ProjectiveMatrix.Finite(transform.pivot.y) &&
            ProjectiveMatrix.Finite(transform.rotation) && ProjectiveMatrix.Finite(transform.position.x) &&
            ProjectiveMatrix.Finite(transform.position.y) && ProjectiveMatrix.Finite(transform.scale.x) &&
            ProjectiveMatrix.Finite(transform.scale.y) && System.Math.Abs(transform.scale.x) >= 1e-7 &&
            System.Math.Abs(transform.scale.y) >= 1e-7;

        internal static Vector2 ToDocument(Vector2 uv, TextureTransform transform, int width, int height) =>
            transform.Map(uv, new Vector2(width,height));

        internal static Vector2 ToSource(Vector2 uv, TextureTransform transform, int width, int height) =>
            transform.Unmap(uv, new Vector2(width,height));

        internal static Vector2 CanonicalSource(Vector2 uv, TextureTransform transform, int width, int height) =>
            ToSource(Wrap(ToDocument(uv, transform, width, height)), transform, width, height);

        internal static void GetPeriodBasis(TextureTransform transform, int width, int height, out Vector2 u, out Vector2 v)
        {
            float angle = transform.rotationF * Mathf.Deg2Rad;
            float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
            u = new Vector2(cosine * width / transform.scaleF.x, -sine * width / transform.scaleF.y);
            v = new Vector2(sine * height / transform.scaleF.x, cosine * height / transform.scaleF.y);
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
