using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum VectorTangentMode { Corner, Aligned, Mirrored }
    public enum VectorFillMode { None, Solid, LinearGradient, RadialGradient, SquareGradient, DiamondGradient, CircularGradient }
    public enum VectorFillRule { EvenOdd, NonZero }
    public enum VectorStrokePosition { Center, Inside, Outside }
    public enum VectorStrokeJoin { Miter, Round, Bevel }
    public enum VectorStrokeCap { Butt, Round, Square }
    public enum VectorEdgeMode { Antialiased, Step }
    public enum VectorFeatherPosition { Inside, Outside, Centered }

    /// <summary>Coordinates and handle offsets are layer-local, normalized, y-up.</summary>
    [Serializable]
    public sealed class VectorPoint
    {
        public string id = Guid.NewGuid().ToString("N");
        public Vector2 position, inTangent, outTangent;
        public VectorTangentMode tangentMode;
        public VectorPoint() { }
        public VectorPoint(Vector2 position) { this.position = position; }
        public VectorPoint Clone(bool preserveIds = true) => new VectorPoint(position)
        {
            id = preserveIds ? id : Guid.NewGuid().ToString("N"),
            inTangent = inTangent, outTangent = outTangent, tangentMode = tangentMode
        };
    }

    [Serializable]
    public sealed class VectorContour
    {
        public string id = Guid.NewGuid().ToString("N");
        public bool closed = true;
        public List<VectorPoint> points = new List<VectorPoint>();
        public VectorContour Clone(bool preserveIds = true)
        {
            var copy = new VectorContour { id = preserveIds ? id : Guid.NewGuid().ToString("N"), closed = closed };
            if (points == null) copy.points = null;
            else foreach (var point in points) copy.points.Add(point?.Clone(preserveIds));
            return copy;
        }
    }

    /// <summary>
    /// Ordered compound shape. Only closed contours contribute fill. Stroke width and dash
    /// offset use referenceSize pixels; relative dash lengths multiply strokeWidth.
    /// Tangent modes guide editing; rendering always uses the stored handles verbatim.
    /// </summary>
    [Serializable]
    public sealed class VectorShape
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Shape";
        public bool visible = true;
        public List<VectorContour> contours = new List<VectorContour>();
        public VectorFillMode fillMode = VectorFillMode.Solid;
        public Color fillColor = Color.white;
        public WhimTexGradient gradient = new WhimTexGradient();
        public Vector2 gradientStart = Vector2.zero, gradientEnd = Vector2.right;
        public float gradientRepetitions = 1f;
        public VectorFillRule fillRule;
        public bool stroke;
        public Color strokeColor = Color.black;
        public VectorFillMode strokeMode = VectorFillMode.Solid;
        public WhimTexGradient strokeGradient = new WhimTexGradient();
        public Vector2 strokeGradientStart = Vector2.zero, strokeGradientEnd = Vector2.right;
        public float strokeGradientRepetitions = 1f;
        public float strokeWidth = 2;
        public VectorStrokePosition strokePosition;
        public VectorStrokeJoin join;
        public VectorStrokeCap cap;
        public float miterLimit = 4;
        public List<float> dashPattern = new List<float>();
        public bool dashPatternRelative;
        public float dashOffset;

        internal float DashLengthScale => dashPatternRelative ? strokeWidth : 1;

        internal List<float> RelativeDashPattern()
        {
            return StrokeStyle.From(this).RelativeDashPattern();
        }

        internal void MakeDashPatternRelative()
        {
            if (dashPatternRelative) return;
            dashPattern = RelativeDashPattern();
            dashPatternRelative = true;
        }

        public VectorShape Clone(bool preserveIds = true)
        {
            var copy = (VectorShape)MemberwiseClone();
            if (!preserveIds) copy.id = Guid.NewGuid().ToString("N");
            copy.gradient = gradient?.Clone();
            copy.strokeGradient = strokeGradient?.Clone();
            copy.dashPattern = dashPattern == null ? null : new List<float>(dashPattern);
            copy.contours = contours == null ? null : new List<VectorContour>(contours.Count);
            if (contours != null) foreach (var contour in contours) copy.contours.Add(contour?.Clone(preserveIds));
            return copy;
        }

        // Render-only resolved Boolean figures share appearance without cloning their retained geometry.
        internal void CopyAppearanceFrom(VectorShape source)
        {
            fillMode = source.fillMode; fillColor = source.fillColor; gradient = source.gradient;
            gradientStart = source.gradientStart; gradientEnd = source.gradientEnd; gradientRepetitions = source.gradientRepetitions;
            stroke = source.stroke; strokeColor = source.strokeColor; strokeMode = source.strokeMode; strokeGradient = source.strokeGradient;
            strokeGradientStart = source.strokeGradientStart; strokeGradientEnd = source.strokeGradientEnd;
            strokeGradientRepetitions = source.strokeGradientRepetitions; strokeWidth = source.strokeWidth;
            strokePosition = source.strokePosition; join = source.join; cap = source.cap; miterLimit = source.miterLimit;
            dashPattern = source.dashPattern; dashPatternRelative = source.dashPatternRelative; dashOffset = source.dashOffset;
        }
    }
}
