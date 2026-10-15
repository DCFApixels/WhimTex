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
            float cell = size * .25f;
            float pixelScale = EditorGUIUtility.pixelsPerPoint;
            for (int y = 0; y < 4; y++)
            {
                float top = square.y + Mathf.Round(y * cell * pixelScale) / pixelScale;
                float bottom = y == 3 ? square.yMax : square.y + Mathf.Round((y + 1) * cell * pixelScale) / pixelScale;
                for (int x = (y + 1) & 1; x < 4; x += 2)
                {
                    float left = square.x + Mathf.Round(x * cell * pixelScale) / pixelScale;
                    float right = x == 3 ? square.xMax : square.x + Mathf.Round((x + 1) * cell * pixelScale) / pixelScale;
                    Fill(painter, Rect.MinMaxRect(left, top, right, bottom), high);
                }
            }
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
