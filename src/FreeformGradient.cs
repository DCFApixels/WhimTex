using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public struct FreeformGradientPoint
    {
        public Vector2 position;
        public Color color;
        public float influence;

        public FreeformGradientPoint(Vector2 position, Color color, float influence = .35f)
        { this.position = position; this.color = color; this.influence = influence; }
    }

    [Serializable]
    public sealed class FreeformGradient
    {
        public const int MaxPoints = 64;
        public const float MinInfluence = .01f, MaxInfluence = 4f;
        public List<FreeformGradientPoint> points = new List<FreeformGradientPoint>
        {
            new FreeformGradientPoint(new Vector2(.25f, .3f), new Color(.95f, .35f, .2f)),
            new FreeformGradientPoint(new Vector2(.75f, .3f), new Color(.2f, .5f, .95f)),
            new FreeformGradientPoint(new Vector2(.5f, .75f), new Color(.7f, .25f, .85f))
        };
        [NonSerialized] private Vector4[] positions, colors;

        public FreeformGradient Clone() => new FreeformGradient
            { points = points == null ? null : new List<FreeformGradientPoint>(points) };

        public void Validate()
        {
            if (points == null || points.Count < 1 || points.Count > MaxPoints)
                throw new ArgumentException("Freeform requires 1..64 points.");
            foreach (var point in points)
            {
                if (!float.IsFinite(point.position.x) || !float.IsFinite(point.position.y) ||
                    Mathf.Abs(point.position.x) > 10000 || Mathf.Abs(point.position.y) > 10000)
                    throw new ArgumentException("Freeform point positions must be finite local coordinates within -10000..10000.");
                if (!float.IsFinite(point.influence) || point.influence < MinInfluence || point.influence > MaxInfluence)
                    throw new ArgumentException("Freeform influence must be within 0.01..4.");
                if (!float.IsFinite(point.color.r) || !float.IsFinite(point.color.g) || !float.IsFinite(point.color.b) ||
                    !float.IsFinite(point.color.a) || point.color.a < 0 || point.color.a > 1)
                    throw new ArgumentException("Freeform colors must be finite with alpha within 0..1.");
            }
        }

        // Relative inverse-distance weights keep distant coordinates numerically stable.
        // RGB is straight linear data: transparent points retain their own hidden color.
        public Color Evaluate(Vector2 uv)
        {
            if (points == null || points.Count == 0) return Color.clear;
            float nearest = float.MaxValue;
            foreach (var p in points) nearest = Mathf.Min(nearest, Distance(uv, p));
            Color sum = Color.clear;
            float total = 0;
            foreach (var p in points)
            {
                float weight = nearest / Distance(uv, p);
                weight *= weight;
                sum += HdrUtility.Decode(p.color) * weight;
                total += weight;
            }
            return sum / total;
        }

        private static float Distance(Vector2 uv, FreeformGradientPoint p) =>
            Mathf.Max(1e-8f, (uv - p.position).sqrMagnitude / (p.influence * p.influence));

        internal void Bind(Material material, bool stroke = false)
        {
            Validate();
            positions ??= new Vector4[MaxPoints]; colors ??= new Vector4[MaxPoints];
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                positions[i] = new Vector4(point.position.x, point.position.y, 1 / (point.influence * point.influence), 0);
                colors[i] = HdrUtility.Decode(point.color);
            }
            material.SetInt(stroke ? "_StrokeFreeformCount" : "_FreeformCount", points.Count);
            material.SetVectorArray(stroke ? "_StrokeFreeformPositions" : "_FreeformPositions", positions);
            material.SetVectorArray(stroke ? "_StrokeFreeformColors" : "_FreeformColors", colors);
        }

        internal int ContentHash()
        {
            unchecked
            {
                int hash = 17;
                if (points != null) foreach (var point in points)
                { hash = hash * 31 + point.position.GetHashCode(); hash = hash * 31 + point.color.GetHashCode(); hash = hash * 31 + point.influence.GetHashCode(); }
                return hash;
            }
        }
    }
}
