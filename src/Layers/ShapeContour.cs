using System;
using System.Collections.Generic;
using UnityEngine;
using static DCFApixels.WhimTex.ShapeLayerBehaviour;

namespace DCFApixels.WhimTex
{
    // A closed contour of straight segments and bounded circular/elliptic arcs.
    // Distances and winding use these same primitives; no raster SDF or polygonized round corners.
    internal sealed class ShapeContour
    {
        private const int Capacity = 128;
        private readonly Vector4[] segments = new Vector4[Capacity], curves = new Vector4[Capacity];
        private readonly Vector2[] vertices = new Vector2[64], entry = new Vector2[64], exit = new Vector2[64];
        private readonly float[] coefficients = new float[64];
        private int count, vertexCount, cachedHash;
        private bool valid;

        internal void SetMaterial(Material material, ShapeLayerBehaviour shape, Vector2 halfSize)
        {
            EnsureContour(shape, halfSize);
            material.SetInt("_ShapeSegmentCount", count);
            material.SetVectorArray("_ShapeSegments", segments);
            material.SetVectorArray("_ShapeCurves", curves);
        }

        private void EnsureContour(ShapeLayerBehaviour shape, Vector2 halfSize)
        {
            shape.EnsureCorners();
            var hash = new HashCode();
            hash.Add(shape.kind); hash.Add(halfSize); hash.Add(shape.sides); hash.Add(shape.innerRadius);
            hash.Add(shape.startAngle); hash.Add(shape.sweepAngle); shape.AddCornerHash(ref hash);
            int next = hash.ToHashCode();
            if (!valid || cachedHash != next)
            {
                count = 0;
                if (shape.kind == ShapeKind.Sector) BuildSector(shape, halfSize);
                else BuildPolygon(shape, halfSize);
                cachedHash = next; valid = true;
            }
        }

        internal void CopyPreview(ShapeLayerBehaviour shape, Vector2 halfSize, List<Vector2> points)
        {
            EnsureContour(shape, halfSize);
            points.Clear();
            for (int i = 0; i < count; i++)
            {
                Vector4 segment = segments[i], curve = curves[i];
                if (curve.z == 0)
                {
                    points.Add(new Vector2(segment.x, segment.y));
                    points.Add(new Vector2(segment.z, segment.w));
                }
                else
                {
                    int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(curve.y) * 16f), 2, 100);
                    for (int step = 0; step <= steps; step++)
                    {
                        float angle = curve.x + curve.y * step / steps;
                        points.Add(new Vector2(segment.x + segment.z * Mathf.Cos(angle), segment.y + segment.w * Mathf.Sin(angle)));
                    }
                }
            }
        }

        private Corner[] Settings(ShapeLayerBehaviour shape) => shape.kind == ShapeKind.Rectangle ? shape.rectangleCorners :
            shape.kind == ShapeKind.Star ? new[] { shape.outerCorner, shape.innerCorner } : shape.polygonCorners;
        private int Group(ShapeLayerBehaviour shape, int index) => shape.kind == ShapeKind.Star ? index & 1 : index;
        private void Store(ShapeLayerBehaviour shape, Corner[] values)
        {
            if (shape.kind == ShapeKind.Star) { shape.outerCorner = values[0]; shape.innerCorner = values[1]; }
        }
        private void Prepare(ShapeLayerBehaviour shape, Vector2 halfSize, Corner[] values)
        {
            vertexCount = shape.kind == ShapeKind.Rectangle ? 4 : shape.sides * (shape.kind == ShapeKind.Star ? 2 : 1);
            float unit = CornerUnit(shape, halfSize);
            for (int i = 0; i < vertexCount; i++)
            {
                if (shape.kind == ShapeKind.Rectangle)
                    vertices[i] = new Vector2(i == 0 || i == 3 ? -halfSize.x : halfSize.x, i < 2 ? halfSize.y : -halfSize.y);
                else
                {
                    float angle = Mathf.PI * .5f + i * (Mathf.PI * 2f / vertexCount);
                    float radius = shape.kind == ShapeKind.Star && (i & 1) != 0 ? Limit(shape.innerRadius, .01f, 1f, .5f) : 1f;
                    vertices[i] = Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, halfSize);
                }
            }
            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 before = (vertices[(i + vertexCount - 1) % vertexCount] - vertices[i]).normalized;
                Vector2 after = (vertices[(i + 1) % vertexCount] - vertices[i]).normalized;
                float cosine = Mathf.Clamp(Vector2.Dot(before, after), -.999999f, .999999f);
                coefficients[i] = values[Group(shape, i)].style == CornerStyle.Bevel ? unit :
                    unit * Mathf.Sqrt((1f + cosine) / (1f - cosine));
            }
        }
        private float Fit(ShapeLayerBehaviour shape, Corner[] values)
        {
            float factor = 1f;
            for (int i = 0; i < vertexCount; i++)
            {
                int j = (i + 1) % vertexCount;
                float consumed = coefficients[i] * values[Group(shape, i)].amount + coefficients[j] * values[Group(shape, j)].amount;
                if (consumed > 0) factor = Mathf.Min(factor, Vector2.Distance(vertices[i], vertices[j]) / consumed);
            }
            return Mathf.Clamp01(factor);
        }
        private static void Scale(Corner[] values, float factor)
        { for (int i = 0; i < values.Length; i++) values[i].amount *= factor; }

        internal static void Restrict(ShapeLayerBehaviour shape, Vector2 halfSize)
        {
            if (shape.kind == ShapeKind.Sector)
            {
                float factor = SectorFit(shape.outerCorner, shape.innerCorner, SectorSweep(shape));
                shape.outerCorner.amount *= factor; shape.innerCorner.amount *= factor; return;
            }
            if (shape.kind != ShapeKind.Rectangle && shape.kind != ShapeKind.Polygon && shape.kind != ShapeKind.Star) return;
            var geometry = shape.Contour;
            Corner[] values = geometry.Settings(shape);
            geometry.Prepare(shape, halfSize, values);
            Scale(values, geometry.Fit(shape, values)); geometry.Store(shape, values);
        }

        internal static void Edit(ShapeLayerBehaviour shape, int index, Corner value, Vector2 halfSize)
        {
            if (shape.kind == ShapeKind.Sector) { EditSector(shape, index, value); return; }
            if (shape.kind != ShapeKind.Rectangle && shape.kind != ShapeKind.Polygon && shape.kind != ShapeKind.Star)
                throw new InvalidOperationException("This shape has no editable corners.");
            Restrict(shape, halfSize);
            var geometry = shape.Contour;
            Corner[] values = geometry.Settings(shape);
            if (index < 0 || index >= values.Length) throw new ArgumentOutOfRangeException(nameof(index));
            float previous = values[index].amount;
            bool linked = shape.kind != ShapeKind.Star && shape.linkCorners && value.style == values[index].style;
            if (linked)
            {
                if (previous > 0f) Scale(values, value.amount / previous);
                else for (int i = 0; i < values.Length; i++) values[i].amount += value.amount;
                float largest = 1f;
                foreach (var item in values) largest = Mathf.Max(largest, item.amount);
                Scale(values, 1f / largest);
                geometry.Prepare(shape, halfSize, values);
                Scale(values, geometry.Fit(shape, values));
            }
            else
            {
                values[index] = value;
                geometry.Prepare(shape, halfSize, values);
                // First stop the edited group at the bare neighbouring vertex. Then consume its neighbours.
                for (int i = 0; i < geometry.vertexCount; i++)
                {
                    if (geometry.Group(shape, i) != index) continue;
                    int prev = (i + geometry.vertexCount - 1) % geometry.vertexCount, next = (i + 1) % geometry.vertexCount;
                    float limit = Mathf.Min(Vector2.Distance(geometry.vertices[i], geometry.vertices[prev]),
                        Vector2.Distance(geometry.vertices[i], geometry.vertices[next])) / Mathf.Max(geometry.coefficients[i], 1e-8f);
                    values[index].amount = Mathf.Min(values[index].amount, limit);
                }
                for (int i = 0; i < geometry.vertexCount; i++)
                {
                    int j = (i + 1) % geometry.vertexCount;
                    int a = geometry.Group(shape, i), b = geometry.Group(shape, j);
                    if (a != index && b != index) continue;
                    int activeVertex = a == index ? i : j, otherVertex = a == index ? j : i;
                    int neighbour = geometry.Group(shape, otherVertex);
                    float available = Mathf.Max(0, Vector2.Distance(geometry.vertices[i], geometry.vertices[j]) - geometry.coefficients[activeVertex] * values[index].amount);
                    values[neighbour].amount = Mathf.Min(values[neighbour].amount, available / Mathf.Max(geometry.coefficients[otherVertex], 1e-8f));
                }
            }
            geometry.Store(shape, values);
        }

        private void Line(Vector2 a, Vector2 b)
        {
            if ((b - a).sqrMagnitude < 1e-12f) return;
            segments[count] = new Vector4(a.x, a.y, b.x, b.y); curves[count++] = Vector4.zero;
        }
        private void Arc(Vector2 center, Vector2 axes, float start, float sweep)
        {
            if (Mathf.Abs(sweep) < 1e-6f || axes.x < 1e-6f || axes.y < 1e-6f) return;
            segments[count] = new Vector4(center.x, center.y, axes.x, axes.y);
            curves[count++] = new Vector4(start, sweep, 1, 0);
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float CornerUnit(ShapeLayerBehaviour shape, Vector2 halfSize) =>
            Mathf.Min(halfSize.x, halfSize.y) * (shape.kind == ShapeKind.Rectangle ? 2f : 1f);
        private void RoundArc(Vector2 a, Vector2 b, Vector2 center, float radius, float turn)
        {
            float start = Mathf.Atan2(a.y - center.y, a.x - center.x), end = Mathf.Atan2(b.y - center.y, b.x - center.x);
            float sweep = Mathf.Repeat((end - start) * Mathf.Sign(turn), Mathf.PI * 2f) * Mathf.Sign(turn);
            Arc(center, Vector2.one * radius, start, sweep);
        }
        private void BuildPolygon(ShapeLayerBehaviour shape, Vector2 halfSize)
        {
            var values = Settings(shape);
            Prepare(shape, halfSize, values);
            float factor = Fit(shape, values), unit = CornerUnit(shape, halfSize);
            for (int i = 0; i < vertexCount; i++)
            {
                float trim = coefficients[i] * values[Group(shape, i)].amount * factor;
                entry[i] = vertices[i] + (vertices[(i + vertexCount - 1) % vertexCount] - vertices[i]).normalized * trim;
                exit[i] = vertices[i] + (vertices[(i + 1) % vertexCount] - vertices[i]).normalized * trim;
            }
            for (int i = 0; i < vertexCount; i++)
            {
                var corner = values[Group(shape, i)];
                float radius = corner.amount * unit * factor;
                if (radius > 1e-6f && corner.style == CornerStyle.Round)
                {
                    Vector2 before = (vertices[(i + vertexCount - 1) % vertexCount] - vertices[i]).normalized;
                    Vector2 after = (vertices[(i + 1) % vertexCount] - vertices[i]).normalized;
                    Vector2 bisector = (before + after).normalized;
                    float sine = Mathf.Sqrt(Mathf.Max(1e-8f, (1 - Vector2.Dot(before, after)) * .5f));
                    RoundArc(entry[i], exit[i], vertices[i] + bisector * (radius / sine), radius, Cross(-before, after));
                }
                else Line(entry[i], exit[i]);
                Line(exit[i], entry[(i + 1) % vertexCount]);
            }
        }

        private static float SectorSweep(ShapeLayerBehaviour shape) => Limit(shape.sweepAngle, 0, 360, 90) * Mathf.Deg2Rad;
        private static void SectorCuts(Corner outer, Corner inner, float sweep, out float centerTrim, out float lineTrim, out float arcTrim)
        {
            float r = outer.amount * .5f, innerRadius = inner.amount * .5f;
            centerTrim = Mathf.Abs(sweep - Mathf.PI) < 1e-5f ? 0 :
                inner.style == CornerStyle.Bevel ? innerRadius : innerRadius * Mathf.Abs(1 / Mathf.Tan(sweep * .5f));
            if (outer.style == CornerStyle.Bevel) { lineTrim = r; arcTrim = r; }
            else
            {
                float t = Mathf.Sqrt(Mathf.Max(0, 1 - 2 * r));
                lineTrim = 1 - t; arcTrim = Mathf.Atan2(r, t);
            }
        }
        private static bool SectorFits(Corner outer, Corner inner, float sweep)
        {
            if (sweep < 1e-5f || sweep > Mathf.PI * 2 - 1e-5f) return true;
            SectorCuts(outer, inner, sweep, out float center, out float line, out float arc);
            return center + line <= 1 && arc * 2 <= sweep;
        }
        private static float SectorFit(Corner outer, Corner inner, float sweep)
        {
            bool Fits(float factor)
            {
                var a = outer; var b = inner; a.amount *= factor; b.amount *= factor;
                return SectorFits(a, b, sweep);
            }
            if (Fits(1)) return 1;
            float lo = 0, hi = 1;
            for (int i = 0; i < 24; i++) { float mid = (lo + hi) * .5f; if (Fits(mid)) lo = mid; else hi = mid; }
            return lo;
        }
        private static void EditSector(ShapeLayerBehaviour shape, int index, Corner value)
        {
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            float sweep = SectorSweep(shape);
            float initial = SectorFit(shape.outerCorner, shape.innerCorner, sweep);
            shape.outerCorner.amount *= initial; shape.innerCorner.amount *= initial;
            float limit = index == 0 ? SectorFit(value, new Corner(0), sweep) : SectorFit(new Corner(0), value, sweep);
            value.amount *= limit;
            Corner other = index == 0 ? shape.innerCorner : shape.outerCorner;
            bool Fits(float factor)
            {
                var neighbour = other; neighbour.amount *= factor;
                return index == 0 ? SectorFits(value, neighbour, sweep) : SectorFits(neighbour, value, sweep);
            }
            if (!Fits(1))
            {
                float lo = 0, hi = 1;
                for (int i = 0; i < 24; i++) { float mid = (lo + hi) * .5f; if (Fits(mid)) lo = mid; else hi = mid; }
                other.amount *= lo;
            }
            if (index == 0) { shape.outerCorner = value; shape.innerCorner = other; }
            else { shape.innerCorner = value; shape.outerCorner = other; }
        }
        private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        private void BuildSector(ShapeLayerBehaviour shape, Vector2 halfSize)
        {
            float start = Limit(shape.startAngle, -360000, 360000, 0) * Mathf.Deg2Rad;
            float sweep = SectorSweep(shape);
            if (sweep < 1e-5f) return;
            if (sweep > Mathf.PI * 2 - 1e-5f) { Arc(Vector2.zero, halfSize, 0, Mathf.PI * 2); return; }
            float factor = SectorFit(shape.outerCorner, shape.innerCorner, sweep);
            var outer = shape.outerCorner; var inner = shape.innerCorner;
            outer.amount *= factor; inner.amount *= factor;
            float outerRadius = outer.amount * .5f, innerRadius = inner.amount * .5f;
            SectorCuts(outer, inner, sweep, out float centerTrim, out float lineTrim, out float arcTrim);
            Vector2 a = Direction(start), b = Direction(start + sweep);
            Vector2 startLine = a * (1 - lineTrim), endLine = b * (1 - lineTrim);
            Vector2 startArc = Direction(start + arcTrim), endArc = Direction(start + sweep - arcTrim);
            Vector2 centerStart = a * centerTrim, centerEnd = b * centerTrim;
            void L(Vector2 x, Vector2 y) => Line(Vector2.Scale(x, halfSize), Vector2.Scale(y, halfSize));
            void A(Vector2 x, Vector2 y, Vector2 c, float turn, float radius)
            {
                float angle = Mathf.Atan2(x.y - c.y, x.x - c.x), end = Mathf.Atan2(y.y - c.y, y.x - c.x);
                float delta = Mathf.Repeat((end - angle) * Mathf.Sign(turn), Mathf.PI * 2) * Mathf.Sign(turn);
                Arc(Vector2.Scale(c, halfSize), halfSize * radius, angle, delta);
            }
            if (outer.style == CornerStyle.Round && outerRadius > 1e-6f)
                A(startLine, startArc, startLine + new Vector2(-a.y, a.x) * outerRadius, 1, outerRadius);
            else L(startLine, startArc);
            Arc(Vector2.zero, halfSize, start + arcTrim, Mathf.Max(0, sweep - arcTrim * 2));
            if (outer.style == CornerStyle.Round && outerRadius > 1e-6f)
                A(endArc, endLine, endLine + new Vector2(b.y, -b.x) * outerRadius, 1, outerRadius);
            else L(endArc, endLine);
            L(endLine, centerEnd);
            if (inner.style == CornerStyle.Round && innerRadius > 1e-6f && Mathf.Abs(sweep - Mathf.PI) > 1e-5f)
            {
                Vector2 c = Direction(start + sweep * .5f) * (innerRadius / Mathf.Sin(sweep * .5f)) * (sweep < Mathf.PI ? 1 : -1);
                A(centerEnd, centerStart, c, Mathf.PI - sweep, innerRadius);
            }
            else L(centerEnd, centerStart);
            L(centerStart, startLine);
        }
    }
}
