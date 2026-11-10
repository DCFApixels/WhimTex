using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        [Serializable]
        private sealed class NodePreviewViewState : HeightPreviewSettings
        {
            public string scope, node;
        }
        [SerializeField] private List<NodePreviewViewState> nodePreviewViews = new();
        private NodePreviewViewState NodePreviewState(string id)
        {
            string scope = editingSubGraphId ?? string.Empty;
            foreach (var state in nodePreviewViews) if (state.node == id && state.scope == scope) return state;
            var added = new NodePreviewViewState { node = id, scope = scope };
            nodePreviewViews.Add(added); return added;
        }

        private sealed partial class NodePreview
        {
            private NodePreviewViewState viewState;
            private HeightPreviewRenderer heightRenderer;
            private HeightPreviewUI.SettingsPopup heightSettings;
            private ToolbarToggle imageModeButton, heightModeButton;
            private ToolbarButton heightSettingsButton;
            private VisualElement viewControls;
            private bool presentationDirty, heightGeometryDirty = true;

            private void BuildViewControls()
            {
                viewState = window.NodePreviewState(nodeId);
                if (!HeightPreviewRenderer.Supported) viewState.heightMode = false;
                viewControls = new VisualElement { name = "node-preview-controls" };
                viewControls.AddToClassList("whimtex-node-preview-controls");
                viewControls.RegisterCallback<FocusInEvent>(_ => viewControls.AddToClassList("whimtex-node-preview-controls--focused"));
                viewControls.RegisterCallback<FocusOutEvent>(e =>
                {
                    if (e.relatedTarget is not VisualElement next || !viewControls.Contains(next))
                        viewControls.RemoveFromClassList("whimtex-node-preview-controls--focused");
                });
                imageModeButton = HeightPreviewUI.ModeButton(false, "node-preview-image", () => SetPreviewMode(false));
                heightModeButton = HeightPreviewUI.ModeButton(true, "node-preview-height", () => SetPreviewMode(true));
                viewControls.Add(imageModeButton); viewControls.Add(heightModeButton);
                heightSettingsButton = new ToolbarButton(() =>
                {
                    heightSettings = new HeightPreviewUI.SettingsPopup(viewState, InvalidatePresentation, () =>
                    {
                        heightSettings = null; viewControls.RemoveFromClassList("whimtex-node-preview-controls--open");
                    });
                    viewControls.AddToClassList("whimtex-node-preview-controls--open");
                    UnityEditor.PopupWindow.Show(heightSettingsButton.worldBound, heightSettings);
                }) { name = "node-preview-height-settings", text = "⋮", tooltip = "Height settings" };
                viewControls.Add(heightSettingsButton); viewControls.Add(output); surface.Add(viewControls);
                var badge = HeightPreviewUI.Icon(true);
                badge.AddToClassList("whimtex-node-preview-height-badge"); surface.Add(badge);
                UpdateViewControls();
                RegisterCallback<DetachFromPanelEvent>(_ => ReleaseHeight());
            }

            private void SetPreviewMode(bool height)
            {
                if (viewState.heightMode != height)
                {
                    viewState.heightMode = height;
                    heightSettings?.editorWindow?.Close();
                    if (!height) ReleaseHeight();
                    InvalidatePresentation(true);
                }
                UpdateViewControls();
            }

            private void UpdateViewControls()
            {
                imageModeButton.SetValueWithoutNotify(!viewState.heightMode);
                heightModeButton.SetValueWithoutNotify(viewState.heightMode);
                heightSettingsButton.EnableInClassList("whimtex-hidden", !viewState.heightMode);
                surface.EnableInClassList("whimtex-node-preview--height", viewState.heightMode);
            }

            private void InvalidatePresentation(bool geometry)
            {
                heightGeometryDirty |= geometry;
                presentationDirty = true;
                canvas.PrioritizePreview(nodeId);
            }

            private void PreviewSourceChanged()
            {
                heightGeometryDirty = presentationDirty = true;
            }

            private void PresentPreview()
            {
                var texture = source.Texture;
                if (viewState.heightMode && texture != null)
                {
                    heightRenderer ??= new HeightPreviewRenderer();
                    if (heightGeometryDirty || heightRenderer.Geometry == null)
                        heightRenderer.Render(texture, new Vector2Int(window.document.width, window.document.height),
                            texture.width, viewState.heightChannel, viewState.heightScale);
                    // Keep the raw node-preview convention rather than encoding its values twice.
                    image.image = heightRenderer.Display(texture, 15, viewState.heightTexture, encodeGamma: false,
                        highlightOutOfRange: viewState.highlightOutOfRange);
                }
                else image.image = texture;
                heightGeometryDirty = presentationDirty = false;
                image.MarkDirtyRepaint();
            }

            private void ReleaseHeight()
            {
                heightSettings?.editorWindow?.Close(); heightSettings = null;
                if (heightRenderer?.Output != null && ReferenceEquals(image.image, heightRenderer.Output)) image.image = null;
                heightRenderer?.Dispose();
                heightGeometryDirty = presentationDirty = true;
            }
        }
    }
}
