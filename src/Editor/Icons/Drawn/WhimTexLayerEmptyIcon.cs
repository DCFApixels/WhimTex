using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexLayerEmptyIcon : VisualElement
    {
        internal WhimTexLayerEmptyIcon()
        {
            pickingMode = PickingMode.Ignore;
            tooltip = "Empty layer — all pixels of its own content, including FX, have zero alpha. Layer Opacity and visibility are ignored.";
            AddToClassList("whimtex-layer-empty-icon");
            AddToClassList("whimtex-hidden");
            style.width = style.height = 18f;
            style.marginRight = 0f;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect square = WhimTexLayerThumbnail.DrawCheckerboard(context);
            if (square.width < 1f) return;
            var painter = context.painter2D;

            painter.strokeColor = Color.red;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(square.xMin, square.yMax));
            painter.LineTo(new Vector2(square.xMax, square.yMin));
            painter.Stroke();
        }

    }
}
