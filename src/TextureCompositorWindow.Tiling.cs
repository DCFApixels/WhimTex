using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private bool tiledPreview;

        private void AddTiledPreviewControl()
        {
            Toggle tiled = new Toggle("Tiled")
            {
                value = tiledPreview,
                tooltip = "Repeat the canvas across Preview. Brush and eraser wrap across canvas edges without changing layer transforms or export size."
            };
            tiled.AddToClassList("whimtex-preview-tiling");
            toolkitSettingsBindings.Track(tiled, () => tiledPreview);
            tiled.RegisterValueChangedCallback(evt => SetTiledPreview(evt.newValue));
            toolkitCanvasToolbar.Add(tiled);
        }

        private void SetTiledPreview(bool enabled)
        {
            if (tiledPreview == enabled) return;
            FinishPaintingStroke();
            FinishPreviewTransform();
            CancelPreviewZoomGesture();
            tiledPreview = enabled;
            lineAnchorLayer = null;
            UpdateToolkitPreviewPresentation();
        }

        private bool PreviewContainsPaintPoint(Vector2 position) => toolkitPreviewCanvas != null &&
            toolkitPreviewCanvas.contentRect.Contains(position) &&
            (tiledPreview || toolkitPreviewCanvas.ImageRect.Contains(toolkitPreviewCanvas.ToCanvas(position)));
    }
}
