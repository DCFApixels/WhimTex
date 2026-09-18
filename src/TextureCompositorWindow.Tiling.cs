using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool tiledPreview;
        [System.NonSerialized] private Button tiledPreviewButton;

        private Button BuildTiledPreviewButton()
        {
            tiledPreviewButton = new Button(() => SetTiledPreview(!tiledPreview))
            {
                name = "tiledPreviewButton",
                text = "Tiled",
                tooltip = "Repeat the canvas across Preview. Brush and eraser wrap across canvas edges without changing layer transforms or export size."
            };
            tiledPreviewButton.AddToClassList("whimtex-channel-button");
            tiledPreviewButton.AddToClassList("whimtex-tiled-button");
            tiledPreviewButton.EnableInClassList("whimtex-channel-button--enabled", tiledPreview);
            return tiledPreviewButton;
        }

        private void SetTiledPreview(bool enabled)
        {
            if (tiledPreview == enabled) return;
            FinishPaintingStroke();
            FinishPreviewTransform();
            CancelPreviewZoomGesture();
            tiledPreview = enabled;
            tiledPreviewButton?.EnableInClassList("whimtex-channel-button--enabled", tiledPreview);
            lineAnchorLayer = null;
            UpdateToolkitPreviewPresentation();
        }

        private bool PreviewContainsPaintPoint(Vector2 position) => toolkitPreviewCanvas != null &&
            toolkitPreviewCanvas.contentRect.Contains(position) &&
            (tiledPreview || toolkitPreviewCanvas.ImageRect.Contains(toolkitPreviewCanvas.ToCanvas(position)));
    }
}
