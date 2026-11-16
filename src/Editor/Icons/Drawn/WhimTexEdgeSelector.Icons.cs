using System.Collections.Generic;
using System;
using UnityEngine.UIElements;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed partial class WhimTexEdgeSelector
    {
        private sealed class ImageIcon : VisualElement
        {
            public ImageIcon()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-seamless-center-icon");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width < 1f || r.height < 1f) return;
                var painter = context.painter2D;
                painter.fillColor = resolvedStyle.color;
                Vector2 Point(float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
                painter.BeginPath();
                painter.Arc(Point(.7f, .29f), r.width * .075f, 0f, 360f);
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(Point(.16f, .77f));
                painter.LineTo(Point(.4f, .37f));
                painter.LineTo(Point(.57f, .62f));
                painter.LineTo(Point(.69f, .49f));
                painter.LineTo(Point(.85f, .77f));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
