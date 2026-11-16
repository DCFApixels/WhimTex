using System.Collections.Generic;
using System;
using UnityEngine.UIElements;
using UnityEngine;
using static DCFApixels.WhimTex.ShapeLayerBehaviour;

namespace DCFApixels.WhimTex
{
    internal static partial class ShapeCornerSettingsView
    {
        private sealed class CornerStyleIcon : VisualElement
        {
            private CornerStyle value;
            internal CornerStyleIcon()
            {
                pickingMode = PickingMode.Ignore; AddToClassList("whimtex-shape-corner-icon");
                generateVisualContent += Draw;
            }
            internal void SetStyle(CornerStyle next) { if (next == value) return; value = next; MarkDirtyRepaint(); }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1 || contentRect.height < 1) return;
                Vector2 P(float x, float y) => contentRect.position + new Vector2(x * contentRect.width / 18, y * contentRect.height / 18);
                var p = context.painter2D; p.strokeColor = resolvedStyle.color; p.lineWidth = 1.6f;
                p.BeginPath(); p.MoveTo(P(3, 15)); p.LineTo(P(3, 9));
                if (value == CornerStyle.Round) p.BezierCurveTo(P(3, 5.7f), P(5.7f, 3), P(9, 3));
                else p.LineTo(P(9, 3));
                p.LineTo(P(15, 3)); p.Stroke();
            }
        }
    }
}
