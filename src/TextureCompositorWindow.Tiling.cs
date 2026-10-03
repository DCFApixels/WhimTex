using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [UnityEngine.Serialization.FormerlySerializedAs("tiledPreview")]
        [SerializeField] private bool tiledCanvas;
        [System.NonSerialized] private Button tiledCanvasButton;

        private Button BuildTiledCanvasButton()
        {
            tiledCanvasButton = new Button(() => SetTiledCanvas(!tiledCanvas))
            {
                name = "tiledCanvasButton",
                text = "Tiled",
                tooltip = "Repeat the canvas across Canvas View. Brush and eraser wrap across canvas edges without changing layer transforms or export size."
            };
            tiledCanvasButton.AddToClassList("whimtex-channel-button");
            tiledCanvasButton.AddToClassList("whimtex-tiled-button");
            tiledCanvasButton.EnableInClassList("whimtex-channel-button--enabled", tiledCanvas);
            return tiledCanvasButton;
        }

        private void SetTiledCanvas(bool enabled)
        {
            if (tiledCanvas == enabled) return;
            FinishPaintingStroke();
            FinishCanvasTransform();
            CancelCanvasZoomGesture();
            tiledCanvas = enabled;
            tiledCanvasButton?.EnableInClassList("whimtex-channel-button--enabled", tiledCanvas);
            lineAnchorLayer = null;
            UpdateToolkitCanvasPresentation();
        }

        private bool CanvasContainsPaintPoint(Vector2 position) => toolkitCanvas != null &&
            toolkitCanvas.contentRect.Contains(position) &&
            (tiledCanvas || toolkitCanvas.ImageRect.Contains(toolkitCanvas.ToCanvas(position)));
    }
}
