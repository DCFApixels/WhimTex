using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    // A render/edit adapter, not a second serialized source of truth. Width and offset
    // must be expressed in the same coordinates as the contour passed to the stroker.
    internal struct StrokeStyle
    {
        internal float strokeWidth, miterLimit, dashOffset;
        internal VectorStrokePosition strokePosition;
        internal VectorStrokeJoin join;
        internal VectorStrokeCap cap;
        internal List<float> dashPattern;
        internal bool dashPatternRelative;
        internal float DashLengthScale => dashPatternRelative ? strokeWidth : 1;

        internal static StrokeStyle From(VectorShape shape) => new StrokeStyle
        {
            strokeWidth = shape.strokeWidth, strokePosition = shape.strokePosition,
            join = shape.join, cap = shape.cap, miterLimit = shape.miterLimit,
            dashPattern = shape.dashPattern, dashPatternRelative = shape.dashPatternRelative, dashOffset = shape.dashOffset
        };

        internal static StrokeStyle From(ShapeLayerBehaviour shape, float pixelScale = 1) => new StrokeStyle
        {
            strokeWidth = ShapeLayerBehaviour.Limit(shape.strokeWidth, 0, 8192, 2) * pixelScale,
            strokePosition = shape.strokePosition == ShapeLayerBehaviour.StrokePosition.Inside ? VectorStrokePosition.Inside :
                shape.strokePosition == ShapeLayerBehaviour.StrokePosition.Outside ? VectorStrokePosition.Outside : VectorStrokePosition.Center,
            join = shape.strokeJoin, cap = shape.strokeCap, miterLimit = shape.miterLimit,
            dashPattern = shape.dashPattern, dashPatternRelative = true, dashOffset = shape.dashOffset * pixelScale
        };

        internal List<float> RelativeDashPattern()
        {
            var result = dashPattern == null ? new List<float>() : new List<float>(dashPattern);
            if (!dashPatternRelative && strokeWidth > 0)
                for (int i = 0; i < result.Count; i++) result[i] /= strokeWidth;
            return result;
        }

        internal void Validate()
        {
            if (!float.IsFinite(strokeWidth) || strokeWidth < 0 || !float.IsFinite(miterLimit) || miterLimit < 1 ||
                !float.IsFinite(dashOffset) || (uint)strokePosition > 2 || (uint)join > 2 || (uint)cap > 2)
                throw new InvalidOperationException("Invalid stroke style.");
            if ((dashPattern?.Count ?? 0) > 256) throw new InvalidOperationException("Stroke pattern exceeds 256 values.");
            if (dashPattern != null) foreach (float value in dashPattern)
                if (!float.IsFinite(value) || value <= 0)
                    throw new InvalidOperationException("Stroke dash/gap lengths must be finite and positive.");
        }
    }
}
