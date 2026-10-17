using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class VectorBrushBuilder
    {
        struct ClipPlane
        {
            public double x, y, c;
            public double At(Double2 point) => x * point.x + y * point.y + c;
            public double PixelSupport => .5 * (Math.Abs(x) + Math.Abs(y));
        }

        readonly ClipPlane[] canvasPlanes = new ClipPlane[4];
        bool clipEnabled;

        /// <summary>Clip new paint/erase to canvas UV [0,1]. Existing coverage is not clipped.</summary>
        public void SetCanvasClip(ProjectiveMatrix localToCanvas)
        {
            if (!IsDrawing || SampleCount != 0) throw new InvalidOperationException("Set the canvas clip before the first brush sample.");
            if (!localToCanvas.ValidUnitQuad()) throw new ArgumentException("The brush requires a valid layer transform.", nameof(localToCanvas));
            double sign = localToCanvas.m22 < 0 ? -1 : 1;
            // Homogeneous half-planes also handle rotations, reflections and perspective.
            // Unlike an inverse canvas quad, they do not join across a projective horizon.
            SetPlane(0, localToCanvas.m00, localToCanvas.m01, localToCanvas.m02, sign);
            SetPlane(1, localToCanvas.m20 - localToCanvas.m00, localToCanvas.m21 - localToCanvas.m01, localToCanvas.m22 - localToCanvas.m02, sign);
            SetPlane(2, localToCanvas.m10, localToCanvas.m11, localToCanvas.m12, sign);
            SetPlane(3, localToCanvas.m20 - localToCanvas.m10, localToCanvas.m21 - localToCanvas.m11, localToCanvas.m22 - localToCanvas.m12, sign);
            clipEnabled = true;
        }

        void SetPlane(int index, double x, double y, double c, double sign)
        {
            var plane = new ClipPlane { x = sign * x * pixelStep.x, y = sign * y * pixelStep.y,
                c = sign * (x * Frame.xMin + y * Frame.yMin + c) };
            double length = Math.Sqrt(plane.x * plane.x + plane.y * plane.y);
            if (length > 0) { plane.x /= length; plane.y /= length; plane.c /= length; }
            canvasPlanes[index] = plane;
        }

        void ShiftCanvasClip(Vector2Int shift)
        {
            if (!clipEnabled) return;
            for (int i = 0; i < canvasPlanes.Length; i++)
            {
                ClipPlane plane = canvasPlanes[i];
                plane.c += plane.x * shift.x + plane.y * shift.y;
                canvasPlanes[i] = plane;
            }
        }

        bool RasterBounds(Vector2 start, Vector2 end, float startRadius, float endRadius, out RectInt result)
        {
            result = default;
            double extent = Math.Max(startRadius, endRadius) + .5;
            double left = Math.Min(start.x, end.x) - extent, right = Math.Max(start.x, end.x) + extent;
            double bottom = Math.Min(start.y, end.y) - extent, top = Math.Max(start.y, end.y) + extent;
            if (!editing || erase)
            {
                left = Math.Max(0, left); bottom = Math.Max(0, bottom);
                right = Math.Min(Width, right); top = Math.Min(Height, top);
            }
            if (right <= left || top <= bottom) return false;
            if (clipEnabled)
            {
                Span<Double2> polygon = stackalloc Double2[16];
                int count = ClipRectangle(left, bottom, right, top, polygon);
                if (count < 3) return false;
                left = right = polygon[0].x; bottom = top = polygon[0].y;
                for (int i = 1; i < count; i++)
                {
                    left = Math.Min(left, polygon[i].x); right = Math.Max(right, polygon[i].x);
                    bottom = Math.Min(bottom, polygon[i].y); top = Math.Max(top, polygon[i].y);
                }
            }
            // A strip this thin cannot contribute even one byte of coverage. This also
            // avoids allocating a 1025th row for sub-ulp drift at a 1024-pixel canvas edge.
            const double emptyStrip = .5 / 255 - 1e-9;
            double trim = clipEnabled ? emptyStrip : 0;
            int x0 = (int)Math.Floor(left + trim), y0 = (int)Math.Floor(bottom + trim);
            int x1 = (int)Math.Ceiling(right - trim), y1 = (int)Math.Ceiling(top - trim);
            if (x1 <= x0 || y1 <= y0) return false;
            result = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        bool CanvasContains(RectInt bounds)
        {
            if (!clipEnabled) return true;
            var center = new Double2(bounds.xMin + bounds.width * .5, bounds.yMin + bounds.height * .5);
            for (int i = 0; i < canvasPlanes.Length; i++)
            {
                ClipPlane plane = canvasPlanes[i];
                double support = .5 * (Math.Abs(plane.x) * bounds.width + Math.Abs(plane.y) * bounds.height);
                if (plane.At(center) < support) return false;
            }
            return true;
        }

        void ClipRow(int row, ref int left, ref int right)
        {
            if (!clipEnabled) return;
            for (int i = 0; i < canvasPlanes.Length && left <= right; i++)
            {
                ClipPlane plane = canvasPlanes[i];
                double c = plane.y * (row + .5) + plane.c + plane.PixelSupport;
                if (plane.x == 0) { if (c < 0) right = left - 1; continue; }
                double edge = -c / plane.x - .5;
                if (plane.x > 0)
                {
                    if (edge > right) { right = left - 1; return; }
                    if (edge > left) left = (int)Math.Ceiling(edge);
                }
                else
                {
                    if (edge < left) { right = left - 1; return; }
                    if (edge < right) right = (int)Math.Floor(edge);
                }
            }
        }

        byte CanvasCoverage(int x, int y)
        {
            if (!clipEnabled) return 255;
            bool interior = true;
            var center = new Double2(x + .5, y + .5);
            for (int i = 0; i < canvasPlanes.Length; i++)
            {
                ClipPlane plane = canvasPlanes[i];
                double value = plane.At(center), support = plane.PixelSupport;
                if (value < -support) return 0;
                if (value < support) interior = false;
            }
            if (interior) return 255;
            // Only boundary cells need polygon clipping. Exact cell area keeps rotated
            // canvas edges antialiased without supersampling or resampling the old mask.
            Span<Double2> polygon = stackalloc Double2[16];
            int count = ClipRectangle(x, y, x + 1, y + 1, polygon);
            double area = 0;
            for (int i = 1; i + 1 < count; i++)
            {
                Double2 a = polygon[i] - polygon[0], b = polygon[i + 1] - polygon[0];
                area += a.x * b.y - a.y * b.x;
            }
            return (byte)Math.Round(Math.Min(1, Math.Abs(area) * .5) * 255);
        }

        int ClipRectangle(double left, double bottom, double right, double top, Span<Double2> polygon)
        {
            polygon[0] = new Double2(left, bottom); polygon[1] = new Double2(right, bottom);
            polygon[2] = new Double2(right, top); polygon[3] = new Double2(left, top);
            Span<Double2> scratch = stackalloc Double2[16];
            int count = 4;
            for (int p = 0; p < canvasPlanes.Length && count != 0; p++)
            {
                ClipPlane plane = canvasPlanes[p];
                int output = 0;
                Double2 previous = polygon[count - 1]; double before = plane.At(previous);
                for (int i = 0; i < count; i++)
                {
                    Double2 current = polygon[i]; double after = plane.At(current);
                    if ((before >= 0) != (after >= 0)) scratch[output++] = previous + (current - previous) * (before / (before - after));
                    if (after >= 0) scratch[output++] = current;
                    previous = current; before = after;
                }
                scratch.Slice(0, output).CopyTo(polygon); count = output;
            }
            return count;
        }
    }
}
