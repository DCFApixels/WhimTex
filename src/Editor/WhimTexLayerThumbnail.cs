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

        private void Draw(MeshGenerationContext context) => DrawCheckerboard(context);

        internal static Rect DrawCheckerboard(MeshGenerationContext context)
        {
            Rect bounds = context.visualElement.contentRect;
            float size = Mathf.Min(18f, Mathf.Min(bounds.width, bounds.height));
            var square = new Rect(bounds.center - Vector2.one * (size * .5f), Vector2.one * size);
            if (size >= 1f) WhimTexCheckerboard.Draw(context, square, WhimTexCheckerboard.Thumbnail, size * .25f);
            return square;
        }
    }
}
