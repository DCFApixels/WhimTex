using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexIcons
    {
        internal enum Kind { Function, Wave, Step, Curve, Range, Matrix, Vector, Noise, Pattern, Shape,
            Color, Filter, Normal, Split, Combine, Logic, Input, Texture, Code, Graph, Buffer, Time, Loop, Light,
            Add, Subtract, Multiply, Divide, Power, SquareRoot, Absolute, OneMinus, Minimum, Maximum,
            Exponential, Logarithm, And, Or, Not, Compare, Reciprocal }

        // Definitions are shared; each owner receives its own element and UI state.
        internal sealed class Definition
        {
            internal readonly Kind kind;
            internal readonly string symbol;
            internal readonly LayerActionIcon.Kind? action;
            internal Definition(Kind kind, string symbol) { this.kind = kind; this.symbol = symbol; }
            internal Definition(LayerActionIcon.Kind action) => this.action = action;
            internal WhimTexIcon Create() => new WhimTexIcon(this);
        }

        private static readonly Definition[] definitions = BuildDefinitions();
        private static readonly Definition[] actions = BuildActions();
        internal static Definition Get(Kind kind) => definitions[(int)kind];
        internal static Definition Action(LayerActionIcon.Kind kind) => actions[(int)kind];

        private static Definition[] BuildDefinitions()
        {
            var values = (Kind[])Enum.GetValues(typeof(Kind));
            var result = new Definition[values.Length];
            foreach (var kind in values)
            {
                string symbol = kind switch
                {
                    Kind.Function => "ƒ", Kind.Add => "+", Kind.Subtract => "−",
                    Kind.Multiply => "×", Kind.Divide => "÷", Kind.Power => "x²", Kind.SquareRoot => "√",
                    Kind.Absolute => "|x|", Kind.OneMinus => "1−x", Kind.Minimum => "min", Kind.Maximum => "max",
                    Kind.Exponential => "exp", Kind.Logarithm => "log", Kind.And => "&", Kind.Or => "∨",
                    Kind.Not => "!", Kind.Compare => "≶", Kind.Reciprocal => "1/x", _ => null
                };
                result[(int)kind] = new Definition(kind, symbol);
            }
            return result;
        }

        private static Definition[] BuildActions()
        {
            var values = (LayerActionIcon.Kind[])Enum.GetValues(typeof(LayerActionIcon.Kind));
            var result = new Definition[values.Length];
            foreach (var kind in values) result[(int)kind] = new Definition(kind);
            return result;
        }

        internal static void Draw(Kind kind, MeshGenerationContext context, Color color)
        {
            var p = context.painter2D;
            p.strokeColor = p.fillColor = color;
            p.lineWidth = 1.25f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(float x, float y, float x2, float y2)
            { p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x2, y2)); }
            void Box(float x, float y, float w, float h)
            { p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + h)); p.LineTo(new Vector2(x, y + h)); p.ClosePath(); }
            void Circle(float x, float y, float r)
            { p.MoveTo(new Vector2(x + r, y)); p.Arc(new Vector2(x, y), r, 0, 360); }
            p.BeginPath();
            switch (kind)
            {
                case Kind.Wave:
                    p.MoveTo(new Vector2(1, 8));
                    for (int i = 1; i <= 24; i++) p.LineTo(new Vector2(1 + i * 14f / 24, 8 - Mathf.Sin(i * Mathf.PI * 2 / 24) * 4));
                    break;
                case Kind.Step:
                    Line(1, 13, 5, 13); Line(5, 13, 5, 8); Line(5, 8, 10, 8); Line(10, 8, 10, 3); Line(10, 3, 15, 3); break;
                case Kind.Curve:
                    p.MoveTo(new Vector2(1, 13)); p.BezierCurveTo(new Vector2(11, 13), new Vector2(5, 3), new Vector2(15, 3)); break;
                case Kind.Range:
                    Line(2, 8, 14, 8); Line(3, 4, 3, 12); Line(13, 4, 13, 12); Line(6, 6, 10, 6); Line(6, 10, 10, 10); break;
                case Kind.Matrix:
                    Line(4, 2, 2, 2); Line(2, 2, 2, 14); Line(2, 14, 4, 14);
                    Line(12, 2, 14, 2); Line(14, 2, 14, 14); Line(14, 14, 12, 14);
                    for (int y = 5; y <= 11; y += 6) for (int x = 6; x <= 10; x += 4) Circle(x, y, .6f);
                    break;
                case Kind.Vector:
                    Line(3, 13, 13, 3); Line(8, 3, 13, 3); Line(13, 3, 13, 8); break;
                case Kind.Noise:
                    Line(1, 10, 3, 5); Line(3, 5, 6, 12); Line(6, 12, 9, 3); Line(9, 3, 11, 9); Line(11, 9, 15, 6); break;
                case Kind.Pattern:
                    for (int y = 3; y <= 13; y += 5) for (int x = 3; x <= 13; x += 5) Circle(x, y, 1); break;
                case Kind.Shape:
                    p.MoveTo(new Vector2(8, 2)); p.LineTo(new Vector2(14, 13)); p.LineTo(new Vector2(2, 13)); p.ClosePath(); break;
                case Kind.Color:
                    Circle(6, 6, 3.5f); Circle(10, 6, 3.5f); Circle(8, 10, 3.5f); break;
                case Kind.Filter:
                    Line(2, 4, 14, 4); Line(4, 8, 12, 8); Line(6, 12, 10, 12); break;
                case Kind.Normal:
                    p.MoveTo(new Vector2(1, 12)); p.BezierCurveTo(new Vector2(5, 5), new Vector2(11, 5), new Vector2(15, 12));
                    Line(8, 9, 8, 1); Line(5, 4, 8, 1); Line(8, 1, 11, 4); break;
                case Kind.Split:
                case Kind.Combine:
                    bool split = kind == Kind.Split;
                    float a = split ? 2 : 14, b = split ? 14 : 2;
                    Line(a, 8, 7, 8); Line(7, 8, b, 3); Line(7, 8, b, 8); Line(7, 8, b, 13);
                    Circle(a, 8, 1); break;
                case Kind.Logic:
                    p.MoveTo(new Vector2(8, 2)); p.LineTo(new Vector2(13, 8)); p.LineTo(new Vector2(8, 14)); p.LineTo(new Vector2(3, 8)); p.ClosePath();
                    Line(0, 8, 3, 8); Line(13, 8, 16, 8); break;
                case Kind.Input:
                    Line(2, 4, 14, 4); Line(2, 12, 14, 12); Box(5, 2, 3, 4); Box(9, 10, 3, 4); break;
                case Kind.Texture:
                    Box(2, 2, 12, 12); Circle(10, 5, 1); Line(3, 12, 6, 7); Line(6, 7, 10, 12); Line(10, 12, 13, 9); break;
                case Kind.Code:
                    Line(5, 3, 1, 8); Line(1, 8, 5, 13); Line(11, 3, 15, 8); Line(15, 8, 11, 13); Line(9, 3, 7, 13); break;
                case Kind.Buffer:
                    p.MoveTo(new Vector2(2, 4)); p.BezierCurveTo(new Vector2(2, 1), new Vector2(14, 1), new Vector2(14, 4));
                    p.BezierCurveTo(new Vector2(14, 7), new Vector2(2, 7), new Vector2(2, 4));
                    Line(2, 4, 2, 12); p.BezierCurveTo(new Vector2(2, 15), new Vector2(14, 15), new Vector2(14, 12)); Line(14, 12, 14, 4); break;
                case Kind.Time:
                    Circle(8, 8, 6); Line(8, 4, 8, 8); Line(8, 8, 11, 10); break;
                case Kind.Loop:
                    p.MoveTo(new Vector2(13, 6)); p.Arc(new Vector2(8, 8), 5.4f, -22, 285);
                    Line(13, 2, 13, 6); Line(13, 6, 9, 6); break;
                case Kind.Light:
                    Circle(8, 8, 3);
                    for (int i = 0; i < 8; i++)
                    { var d = new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4)); Line(8 + d.x * 5, 8 + d.y * 5, 8 + d.x * 7, 8 + d.y * 7); }
                    break;
                default:
                    Box(1, 6, 4, 4); Box(11, 1, 4, 4); Box(11, 11, 4, 4); Line(5, 8, 11, 3); Line(5, 8, 11, 13); break;
            }
            p.Stroke();
        }
    }
}
