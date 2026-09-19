using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
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

        /// <summary>A document whose file is an imported image is updated through the document session.</summary>
        private bool HasDocumentFile => compositor != null && TryGetDocumentFile(compositor, out _);

        private bool CanPublishLiveOutput => compositor != null &&
            (HasDocumentFile || compositor.OutputTexture != null && AssetDatabase.Contains(compositor) &&
             compositor.OutputTexture.isReadable &&
             !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(compositor.OutputTexture.graphicsFormat));

        private Button BuildLiveOutputButton()
        {
            liveOutputButton = new Button(() =>
            {
                if (HasDocumentFile)
                {
                    ToggleLiveUpdate();
                    RefreshLiveOutputButton();
                    return;
                }
                liveOutputEnabled = !liveOutputEnabled;
                if (!liveOutputEnabled) compositor?.StopLiveOutput();
                else RequestPreview(true);
                RefreshLiveOutputButton();
            });
            liveOutputButton.AddToClassList("whimtex-channel-button");
            liveOutputButton.AddToClassList("whimtex-live-output-button");
            var indicator = new VisualElement { pickingMode = PickingMode.Ignore };
            indicator.AddToClassList("whimtex-live-output-indicator");
            var disc = new VisualElement { pickingMode = PickingMode.Ignore };
            disc.AddToClassList("whimtex-live-output-disc");
            indicator.Add(disc);
            liveOutputButton.Add(indicator);
            RefreshLiveOutputButton();
            return liveOutputButton;
        }

        private void RefreshLiveOutputButton()
        {
            if (liveOutputButton == null) return;
            liveOutputButton.SetEnabled(CanPublishLiveOutput);
            liveOutputButton.EnableInClassList("whimtex-channel-button--enabled",
                HasDocumentFile ? WhimTexDocumentSession.IsLive : liveOutputEnabled);
            liveOutputButton.tooltip = HasDocumentFile
                ? (WhimTexDocumentSession.IsLive
                    ? "Live Update edits the imported image of this document. Click to stop and restore the imported texture."
                    : "Live Update: edit the imported document image in place, so materials show edits without re-encoding the file.")
                : CanPublishLiveOutput
                ? "Live Update: show edits on objects and in File layers using this compositor texture. Turning off restores the saved image. Save writes the changes; texture references stay unchanged."
                : compositor != null && compositor.OutputTexture != null && UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(compositor.OutputTexture.graphicsFormat)
                    ? "Live Update: compressed output updates on Save. Choose Compression None and save to enable live updates."
                    : compositor != null && compositor.OutputTexture != null && !compositor.OutputTexture.isReadable
                        ? "Live Update requires Read/Write. Enable it in Output Settings and save."
                        : "Live Update: save the compositor first, then assign its texture to a material.";
        }

        private void PublishLiveOutput()
        {
            if (HasDocumentFile) return; // the document session publishes on its own
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
