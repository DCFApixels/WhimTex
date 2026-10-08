using System;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private Button liveOutputButton;
        [NonSerialized] private bool outputDependencyDirty;

        private void OnOutputTextureChanged(WhimTexDocumentOutputChange change)
        {
            if (healingLayer != null && change.ShouldRefresh(activeDocument)) CancelHealing();
            if (activeDocument == null || !change.ShouldRefresh(activeDocument)) return;
            outputDependencyDirty = true;
            RequestCanvasRender();
        }

        private bool HasDocumentFile => activeDocument != null && TryGetDocumentFile(activeDocument, out string path) && !WhimTexDocumentJson.IsJsonPath(path);

        private Button BuildLiveOutputButton()
        {
            liveOutputButton = new Button(() => ToggleLiveUpdate(activeDocument));
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
            liveOutputButton.SetEnabled(HasDocumentFile);
            liveOutputButton.EnableInClassList("whimtex-channel-button--enabled", WhimTexDocumentSession.IsLiveFor(activeDocument));
            liveOutputButton.tooltip = WhimTexDocumentJson.IsJsonPath(WhimTexDocumentService.PathOf(activeDocument))
                ? "JSON has no imported image. Save as TIFF to use Live Update."
                : !HasDocumentFile
                ? "Save as TIFF first: Live Update edits the imported image of that file."
                : WhimTexDocumentSession.IsLiveFor(activeDocument)
                ? "Live Update edits the imported image of this document. Click to stop and restore the imported texture."
                : "Live Update: edit the imported document image in place, so materials show edits without re-encoding the file.";
        }

        private void PublishLiveOutput()
        {
            if (HasDocumentFile) WhimTexDocumentSession.Publish(activeDocument, canvasTexture);
        }

        private void StopLiveOutput()
        {
            WhimTexDocumentSession.StopFor(activeDocument, "window closed or document changed");
            RefreshLiveOutputButton();
        }

        private void OnLiveOutputProjectChanged()
        {
            if (!HasDocumentFile) StopLiveOutput();
            RefreshLiveOutputButton();
        }
    }
}
