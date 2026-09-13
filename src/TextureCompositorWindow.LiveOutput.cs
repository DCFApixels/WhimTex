using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private bool liveOutputEnabled;
        [NonSerialized] private Button liveOutputButton;
        [NonSerialized] private bool outputDependencyDirty;

        private void OnOutputTextureChanged(CompositorOutputChange change)
        {
            if (compositor == null || !change.ShouldRefresh(compositor)) return;
            outputDependencyDirty = true;
            RequestPreview();
        }

        private bool CanPublishLiveOutput => compositor != null &&
            compositor.OutputTexture != null && AssetDatabase.Contains(compositor);

        private Button BuildLiveOutputButton()
        {
            liveOutputButton = new Button(() =>
            {
                liveOutputEnabled = !liveOutputEnabled;
                if (!liveOutputEnabled) compositor?.StopLiveOutput();
                else RequestPreview(true);
                RefreshLiveOutputButton();
            });
            liveOutputButton.AddToClassList("sprite-editor-channel-button");
            liveOutputButton.AddToClassList("sprite-editor-live-output-button");
            var indicator = new VisualElement { pickingMode = PickingMode.Ignore };
            indicator.AddToClassList("sprite-editor-live-output-indicator");
            var disc = new VisualElement { pickingMode = PickingMode.Ignore };
            disc.AddToClassList("sprite-editor-live-output-disc");
            indicator.Add(disc);
            liveOutputButton.Add(indicator);
            RefreshLiveOutputButton();
            return liveOutputButton;
        }

        private void RefreshLiveOutputButton()
        {
            if (liveOutputButton == null) return;
            liveOutputButton.SetEnabled(CanPublishLiveOutput);
            liveOutputButton.EnableInClassList("sprite-editor-channel-button--enabled", liveOutputEnabled);
            liveOutputButton.tooltip = CanPublishLiveOutput
                ? "Live Update: show edits on objects and in File layers using this compositor texture. Turning off restores the saved image. Save writes the changes; texture references stay unchanged."
                : "Live Update: save the compositor first, then assign its texture to a material.";
        }

        private void PublishLiveOutput()
        {
            if (!liveOutputEnabled || !CanPublishLiveOutput) return;
            try
            {
                compositor.PublishLiveOutput(previewTexture);
            }
            catch (Exception exception)
            {
                liveOutputEnabled = false;
                compositor.StopLiveOutput();
                RefreshLiveOutputButton();
                ShowNotification(new GUIContent("Live Update unavailable. See Console for details."));
                Debug.LogWarning("WhimTex Live Update: " + exception.Message, compositor);
            }
        }

        private void StopLiveOutput()
        {
            compositor?.StopLiveOutput();
            liveOutputEnabled = false;
            RefreshLiveOutputButton();
        }

        private void OnLiveOutputProjectChanged()
        {
            if (!CanPublishLiveOutput) StopLiveOutput();
            else if (liveOutputEnabled) RequestPreview(true);
        }
    }
}
