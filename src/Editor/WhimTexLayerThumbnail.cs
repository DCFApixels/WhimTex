using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexLayerThumbnail : VisualElement
    {
        internal WhimTexLayerThumbnail()
        {
            AddToClassList("whimtex-layer-thumbnail");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
            => DrawCheckerboard(context.painter2D, contentRect);

        internal static void DrawCheckerboard(Painter2D painter, Rect bounds)
        {
            if (bounds.width < 1f || bounds.height < 1f) return;
            float size = Mathf.Min(18f, Mathf.Min(bounds.width, bounds.height));
            Rect square = new Rect(bounds.center - Vector2.one * (size * .5f), Vector2.one * size);
            bool dark = EditorGUIUtility.isProSkin;
            Color low = dark ? new Color(.26f, .26f, .26f) : new Color(.73f, .73f, .73f);
            Color high = dark ? new Color(.34f, .34f, .34f) : new Color(.83f, .83f, .83f);
            Fill(painter, square, low);
            float cell = size * .5f;
            Fill(painter, new Rect(square.x + cell, square.y, cell, cell), high);
            Fill(painter, new Rect(square.x, square.y + cell, cell, cell), high);
        }

        private static void Fill(Painter2D painter, Rect rectangle, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(rectangle.min);
            painter.LineTo(new Vector2(rectangle.xMax, rectangle.yMin));
            painter.LineTo(rectangle.max);
            painter.LineTo(new Vector2(rectangle.xMin, rectangle.yMax));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
