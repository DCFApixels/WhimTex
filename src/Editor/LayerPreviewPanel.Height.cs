using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class LayerPreviewPanel
    {
        internal VisualElement HeaderControls { get; } = new VisualElement { name = "layer-preview-controls" };
        private ToolbarToggle imageModeButton, heightModeButton;
        private ToolbarButton heightSettingsButton;
        private HeightPreviewRenderer heightRenderer;
        private HeightPreviewUI.SettingsPopup heightSettings;

        private void BuildPreviewControls()
        {
            HeaderControls.AddToClassList("whimtex-layer-preview-controls");
            imageModeButton = HeightPreviewUI.ModeButton(false, "layer-preview-image", () => SetMode(false));
            heightModeButton = HeightPreviewUI.ModeButton(true, "layer-preview-height", () => SetMode(true));
            HeaderControls.Add(imageModeButton); HeaderControls.Add(heightModeButton);
            if (!HeightPreviewRenderer.Supported) state.heightMode = false;
            heightSettingsButton = new ToolbarButton(() =>
            {
                heightSettings = new HeightPreviewUI.SettingsPopup(state,
                    geometry => { if (geometry) RebuildHeight(); else UpdateChannels(); }, () => heightSettings = null);
                UnityEditor.PopupWindow.Show(heightSettingsButton.worldBound, heightSettings);
            }) { name = "layer-preview-height-settings", text = "⋮", tooltip = "Height settings" };
            HeaderControls.Add(heightSettingsButton);
            UpdateModeButtons();
        }

        private void SetMode(bool height)
        {
            if (state.heightMode != height)
            {
                state.heightMode = height;
                heightSettings?.editorWindow?.Close(); heightSettings = null;
                ReleaseTextures();
                RequestLayerPreview();
            }
            UpdateModeButtons();
        }

        private void UpdateModeButtons()
        {
            imageModeButton.SetValueWithoutNotify(!state.heightMode);
            heightModeButton.SetValueWithoutNotify(state.heightMode);
            heightSettingsButton.style.display = state.heightMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RebuildHeight()
        {
            if (source == null || document == null) { heightRenderer?.Dispose(); UpdateChannels(); return; }
            heightRenderer ??= new HeightPreviewRenderer();
            heightRenderer.Render(source, new Vector2Int(document.width, document.height), PreviewSize, state.heightChannel, state.heightScale);
            UpdateChannels();
        }

    }
}
