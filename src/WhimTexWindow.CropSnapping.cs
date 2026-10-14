using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private CanvasCropBoundsCache canvasCropBounds;
        [NonSerialized] private WhimTexDocument canvasCropBoundsOwner;
        private readonly List<Layer> canvasCropSelectedRoots = new List<Layer>();
        private RectInt? canvasCropSelectedContentBounds;

        private void EnsureCanvasCropSnapTargets()
        {
            if (canvasCropBoundsOwner == activeDocument && canvasCropBounds != null) return;
            DisposeCanvasCropSnapTargets();
            if (activeDocument == null) return;
            canvasCropBoundsOwner = activeDocument;
            canvasCropBounds = new CanvasCropBoundsCache(activeDocument);
        }

        private void RefreshCanvasCropSnapTargets()
        {
            EnsureCanvasCropSnapTargets();
            canvasCropSelectedContentBounds = null;
            canvasCropSelectedRoots.Clear();
            if (canvasCropBounds == null) return;
            CollectSelectedRoots(activeDocument.layers, canvasCropSelectedRoots);
            bool complete = true, present = false;
            RectInt combined = default;
            foreach (Layer layer in canvasCropSelectedRoots)
            {
                RectInt? bounds = canvasCropBounds.Get(layer);
                if (!bounds.HasValue) { complete = false; continue; }
                RectInt value = bounds.Value;
                if (value.width <= 0 || value.height <= 0) continue;
                if (!present) { combined = value; present = true; }
                else
                {
                    int x0 = Mathf.Min(combined.xMin, value.xMin), y0 = Mathf.Min(combined.yMin, value.yMin);
                    int x1 = Mathf.Max(combined.xMax, value.xMax), y1 = Mathf.Max(combined.yMax, value.yMax);
                    combined = new RectInt(x0, y0, x1 - x0, y1 - y0);
                }
            }
            if (complete && present) canvasCropSelectedContentBounds = combined;
        }

        private void BeginCanvasCropSnapSnapshot()
        {
            EnsureCanvasCropSnapTargets();
            canvasCropBounds?.BeginSnapshot();
            RefreshCanvasCropSnapTargets();
        }

        private void DisposeCanvasCropSnapTargets()
        {
            canvasCropBounds?.Dispose(); canvasCropBounds = null; canvasCropBoundsOwner = null;
            canvasCropSelectedRoots.Clear(); canvasCropSelectedContentBounds = null;
        }

        private bool TryCanvasCropSnapTolerance(out float tolerance)
        {
            tolerance = 0f;
            if (activeDocument == null || toolkitCanvas == null || toolkitCanvas.PixelScale <= .00001f) return false;
            tolerance = GuideSnapPixels / toolkitCanvas.PixelScale;
            return !float.IsNaN(tolerance) && !float.IsInfinity(tolerance);
        }

        private bool TryCanvasCropSelectionBounds(out RectInt bounds)
        {
            bounds = default;
            if (areaSelectionOwner != activeDocument || areaSelection == null || !areaSelection.Active ||
                areaSelection.Width != activeDocument.width || areaSelection.Height != activeDocument.height) return false;
            bounds = areaSelection.Bounds;
            return bounds.width > 0 && bounds.height > 0;
        }

        private int CanvasCropAxisTargets(int axis, Span<float> targets)
        {
            float extent = axis == 0 ? activeDocument.width : activeDocument.height;
            targets[0] = 0f; targets[1] = extent * .5f; targets[2] = extent;
            int count = 3;
            if (TryCanvasCropSelectionBounds(out RectInt selection))
            {
                targets[count++] = axis == 0 ? selection.xMin : selection.yMin;
                targets[count++] = axis == 0 ? selection.center.x : selection.center.y;
                targets[count++] = axis == 0 ? selection.xMax : selection.yMax;
            }
            if (canvasCropBoundsOwner == activeDocument && canvasCropSelectedContentBounds.HasValue)
            {
                RectInt content = canvasCropSelectedContentBounds.Value;
                targets[count++] = axis == 0 ? content.xMin : content.yMin;
                targets[count++] = axis == 0 ? content.center.x : content.center.y;
                targets[count++] = axis == 0 ? content.xMax : content.yMax;
            }
            return count;
        }

        // Points and Rect use canvas pixel edges, with bottom-left document coordinates.
        // A constrained motion line preserves a side's fixed axis or a corner's proportions.
        private Vector2 SnapCanvasCropPoint(Vector2 point, Vector2 direction, bool free)
        {
            if (!TryCanvasCropSnapTolerance(out float tolerance)) return point;
            Vector2 result = point;
            Span<float> targets = stackalloc float[9];
            if (free)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    float nearest = tolerance, best = point[axis];
                    int count = CanvasCropAxisTargets(axis, targets);
                    for (int i = 0; i < count; i++)
                    {
                        float distance = Mathf.Abs(targets[i] - point[axis]);
                        if (distance > nearest) continue;
                        nearest = distance; best = targets[i];
                    }
                    result[axis] = best;
                }
            }
            else
            {
                float nearest = tolerance * tolerance;
                for (int axis = 0; axis < 2; axis++)
                {
                    if (Mathf.Abs(direction[axis]) < .00001f) continue;
                    int count = CanvasCropAxisTargets(axis, targets);
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 offset = direction * ((targets[i] - point[axis]) / direction[axis]);
                        float distance = offset.sqrMagnitude;
                        if (!(distance <= nearest)) continue;
                        nearest = distance; result = point + offset;
                    }
                }
            }
            return SnapCanvasGuideResize(point, direction, free, Vector2.right, result);
        }

        private Vector2 SnapCanvasCropMove(Rect frame, bool horizontal, bool vertical)
        {
            if (!TryCanvasCropSnapTolerance(out float tolerance)) return Vector2.zero;
            Vector2 center = frame.center, half = new Vector2(Mathf.Abs(frame.width), Mathf.Abs(frame.height)) * .5f;
            Vector2 result = Vector2.zero;
            Span<float> targets = stackalloc float[9];
            for (int axis = 0; axis < 2; axis++)
            {
                if (!(axis == 0 ? horizontal : vertical)) continue;
                float nearest = tolerance, best = 0f;
                int count = CanvasCropAxisTargets(axis, targets);
                for (int i = 0; i < count; i++)
                {
                    for (int anchor = -1; anchor <= 1; anchor++)
                    {
                        float offset = targets[i] - center[axis] - anchor * half[axis];
                        if (Mathf.Abs(offset) > nearest) continue;
                        nearest = Mathf.Abs(offset); best = offset;
                    }
                }
                result[axis] = best;
            }
            return SnapCanvasGuideMove(center, Vector2.right, half, result, horizontal, vertical);
        }
    }
}
