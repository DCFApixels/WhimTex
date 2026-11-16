using ColorField = DCFApixels.WhimTex.WhimTexColorField;
using System.Collections.Generic;
using System;
using UnityEditor.UIElements;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        private static void DrawReverseIcon(MeshGenerationContext context)
        {
            var p = context.painter2D;
            p.strokeColor = context.visualElement.resolvedStyle.color;
            p.lineWidth = 1.5f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(11.5f, 6));
            p.BezierCurveTo(new Vector2(10.5f, 1), new Vector2(5, 1), new Vector2(2.5f, 4.5f));
            p.MoveTo(new Vector2(2.5f, 1.5f)); p.LineTo(new Vector2(2.5f, 4.5f)); p.LineTo(new Vector2(5.5f, 4.5f));
            p.MoveTo(new Vector2(2.5f, 8));
            p.BezierCurveTo(new Vector2(3.5f, 13), new Vector2(9, 13), new Vector2(11.5f, 9.5f));
            p.MoveTo(new Vector2(11.5f, 12.5f)); p.LineTo(new Vector2(11.5f, 9.5f)); p.LineTo(new Vector2(8.5f, 9.5f));
            p.Stroke();
        }
    }
}
