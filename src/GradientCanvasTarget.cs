using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal readonly struct GradientCanvasTarget : IEquatable<GradientCanvasTarget>
    {
        internal readonly LayerBehaviour Behaviour;
        internal readonly GradientLayerBehaviour GradientLayer;
        private readonly ShapeLayerBehaviour shape;
        private readonly VectorShape figure;
        private readonly TextLayerBehaviour text;
        private readonly Rect textFrame;
        private readonly Vector2 canvasSize;
        private readonly int character;
        private readonly Vector2 metric;
        internal readonly GradientLayerBehaviour.GradientType gradientType;
        internal readonly bool IsStroke;
        internal bool IsValid => Behaviour != null;
        internal bool IsFill => IsValid && GradientLayer == null;
        internal Layer Owner => Behaviour?.Owner;
        internal TextureTransform transform { get => Owner.transform; set => Owner.transform = value; }
        internal TextureTransform CanvasTransform
        {
            get
            {
                var value = Owner.CanvasTransform;
                if (text != null)
                    value.TrySetMatrix(value.ToMatrix(canvasSize.x, canvasSize.y) *
                        ProjectiveMatrix.Translate(.5 + textFrame.xMin / canvasSize.x, .5 + textFrame.yMin / canvasSize.y) *
                        ProjectiveMatrix.Scale(textFrame.width / canvasSize.x, textFrame.height / canvasSize.y));
                return value;
            }
        }

        internal GradientCanvasTarget(Layer layer, Vector2 canvasSize, bool stroke = false, int character = 0)
        {
            this.canvasSize = canvasSize; this.character = character; textFrame = default;
            IsStroke = stroke;
            Behaviour = layer?.Behaviour;
            GradientLayer = Behaviour as GradientLayerBehaviour;
            shape = Behaviour as ShapeLayerBehaviour;
            text = Behaviour as TextLayerBehaviour;
            var group = Behaviour as BooleanGroupLayerBehaviour;
            figure = group != null ? group.style :
                Behaviour is VectorLayerBehaviour vector && vector.shapes?.Count == 1 ? vector.shapes[0] : null;
            metric = group != null ? group.referenceSize : Behaviour is VectorLayerBehaviour v ? v.referenceSize : canvasSize;
            gradientType = GradientLayer?.gradientType ?? GradientLayerBehaviour.GradientType.Horizontal;
            if (GradientLayer != null) { if (stroke) Behaviour = null; return; }
            var mode = shape != null ? (stroke ? shape.strokeMode : shape.fillMode) :
                figure != null ? (stroke ? figure.strokeMode : figure.fillMode) : text != null && !stroke ? text.fillMode : VectorFillMode.None;
            bool enabled = shape != null ? (stroke ? shape.stroke && shape.strokeWidth > 0 : shape.fill) :
                figure != null && (!stroke || figure.stroke && figure.strokeWidth > 0) || text != null && !stroke && text.FontAvailable;
            if (!FigureFill.IsGradient(mode) || !enabled || group == null && figure != null && !figure.visible ||
                group != null && group.appearance == BooleanGroupAppearance.Layers && BooleanGroupLayerBehaviour.ReadMode(layer) == VectorBooleanOperation.Combine ||
                shape == null && figure == null && text == null)
            { Behaviour = null; return; }
            if (text != null)
            {
                textFrame = text.GetGradientFrame((int)canvasSize.x, (int)canvasSize.y, character);
                metric = textFrame.size;
            }
            gradientType = mode switch
            {
                VectorFillMode.RadialGradient => GradientLayerBehaviour.GradientType.Radial,
                VectorFillMode.SquareGradient => GradientLayerBehaviour.GradientType.Square,
                VectorFillMode.DiamondGradient => GradientLayerBehaviour.GradientType.Diamond,
                VectorFillMode.CircularGradient => GradientLayerBehaviour.GradientType.Circular,
                _ => GradientLayerBehaviour.GradientType.Horizontal
            };
        }

        internal WhimTexGradient gradient
        {
            get => text != null ? text.gradient : GradientLayer != null ? GradientLayer.gradient : shape != null ? (IsStroke ? shape.strokeGradient : shape.gradient) : (IsStroke ? figure.strokeGradient : figure.gradient);
            set { if (text != null) text.gradient = value; else if (GradientLayer != null) GradientLayer.gradient = value; else if (shape != null) { if (IsStroke) shape.strokeGradient = value; else shape.gradient = value; } else { if (IsStroke) figure.strokeGradient = value; else figure.gradient = value; } }
        }
        internal Vector2 FillStart
        {
            get => text != null ? text.gradientStart : shape != null ? (IsStroke ? shape.strokeGradientStart : shape.gradientStart) : (IsStroke ? figure.strokeGradientStart : figure.gradientStart);
            set { if (text != null) text.gradientStart = value; else if (shape != null) { if (IsStroke) shape.strokeGradientStart = value; else shape.gradientStart = value; } else { if (IsStroke) figure.strokeGradientStart = value; else figure.gradientStart = value; } }
        }
        internal Vector2 FillEnd
        {
            get => text != null ? text.gradientEnd : shape != null ? (IsStroke ? shape.strokeGradientEnd : shape.gradientEnd) : (IsStroke ? figure.strokeGradientEnd : figure.gradientEnd);
            set { if (text != null) text.gradientEnd = value; else if (shape != null) { if (IsStroke) shape.strokeGradientEnd = value; else shape.gradientEnd = value; } else { if (IsStroke) figure.strokeGradientEnd = value; else figure.gradientEnd = value; } }
        }
        internal float circularRepetitions
        {
            get => text != null ? text.gradientRepetitions : GradientLayer != null ? GradientLayer.circularRepetitions : shape != null ? (IsStroke ? shape.strokeGradientRepetitions : shape.gradientRepetitions) : (IsStroke ? figure.strokeGradientRepetitions : figure.gradientRepetitions);
            set { if (text != null) text.gradientRepetitions = value; else if (GradientLayer != null) GradientLayer.circularRepetitions = value; else if (shape != null) { if (IsStroke) shape.strokeGradientRepetitions = value; else shape.gradientRepetitions = value; } else { if (IsStroke) figure.strokeGradientRepetitions = value; else figure.gradientRepetitions = value; } }
        }
        internal float circularStartAngle
        {
            get
            {
                if (GradientLayer != null) return GradientLayer.circularStartAngle;
                Vector2 axis = Vector2.Scale(FillEnd - FillStart, metric);
                return Mathf.Repeat(Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg - 180f, 360f);
            }
            set
            {
                if (GradientLayer != null) GradientLayer.circularStartAngle = value;
                else FillEnd = CircularUv(value);
            }
        }
        internal Vector2 CircularCenter => IsFill ? FillStart : GradientLayerBehaviour.BaseCenter;
        internal Vector2 CircularUv(float angle)
        {
            Vector2 units = IsFill ? metric : Vector2.one;
            float radius = IsFill ? Vector2.Scale(FillEnd - FillStart, metric).magnitude : GradientLayerBehaviour.BaseRadius;
            Vector2 axis = GradientCanvasGeometry.Rotate(Vector2.left * radius, angle);
            return CircularCenter + new Vector2(axis.x / units.x, axis.y / units.y);
        }
        internal float CircularAngle(Vector2 uv)
        {
            Vector2 d = Vector2.Scale(uv - CircularCenter, IsFill ? metric : Vector2.one);
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 180f;
        }
        internal void UvEndpoints(out Vector2 a, out Vector2 b)
        {
            if (IsFill) { a = FillStart; b = FillEnd; }
            else GradientCanvasGeometry.UvEndpoints(GradientLayer, out a, out b);
        }
        internal void MoveEndpoint(WhimTexDocument document, Vector2 size, Vector2 a, Vector2 b,
            bool first, Vector2 delta, TextureTransform originalTransform, Vector2 originalStart, Vector2 originalEnd)
        {
            if (!IsFill)
            {
                var next = GradientCanvasGeometry.MoveEndpointTransform(GradientLayer, size, a, b, first, delta, originalTransform);
                document.SetCanvasTransform(Behaviour, next);
                return;
            }
            Vector2 anchor = first ? originalStart : originalEnd;
            Vector2 canvas = originalTransform.Map(anchor, size) + new Vector2(delta.x / size.x, delta.y / size.y);
            Vector2 nextAnchor = originalTransform.Unmap(canvas, size);
            if (!float.IsFinite(nextAnchor.x) || !float.IsFinite(nextAnchor.y)) return;
            if (first)
            {
                FillStart = nextAnchor;
                FillEnd = gradientType == GradientLayerBehaviour.GradientType.Horizontal ? originalEnd : originalEnd + nextAnchor - originalStart;
            }
            else { FillStart = originalStart; FillEnd = nextAnchor; }
        }
        public bool Equals(GradientCanvasTarget other) => Behaviour == other.Behaviour && figure == other.figure && gradientType == other.gradientType && IsStroke == other.IsStroke && (text == null || character == other.character && textFrame == other.textFrame);
        public override bool Equals(object obj) => obj is GradientCanvasTarget other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Behaviour, figure, gradientType, IsStroke);
        public static bool operator ==(GradientCanvasTarget a, GradientCanvasTarget b) => a.Equals(b);
        public static bool operator !=(GradientCanvasTarget a, GradientCanvasTarget b) => !a.Equals(b);
    }
}
