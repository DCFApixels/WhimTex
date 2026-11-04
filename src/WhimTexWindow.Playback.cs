using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private Toolbar playbackToolbar;
        [NonSerialized] private ToolbarButton playbackButton;
        [NonSerialized] private DoubleField playbackTimeField;
        [NonSerialized] private Slider playbackSlider;
        [NonSerialized] private WhimTexDocument playbackDocument;
        [NonSerialized] private ulong playbackRevision;
        [NonSerialized] private bool publishLiveOutputPending;

        private VisualElement BuildPlaybackToolbar()
        {
            playbackToolbar = new Toolbar { name = "canvas-playback" };
            playbackToolbar.style.flexShrink = 0;
            playbackButton = new ToolbarButton(() =>
            {
                if (activeDocument != null) activeDocument.SetPlayback(!activeDocument.IsPlaying);
            }) { text = "Play", name = "playback-toggle", tooltip = "Play or pause document preview time. Saving and export use time 0." };
            playbackToolbar.Add(playbackButton);
            playbackToolbar.Add(new ToolbarButton(() =>
            {
                if (activeDocument == null) return;
                activeDocument.SetPlayback(false);
                activeDocument.SetPreviewTime(0);
            }) { text = "Reset", tooltip = "Pause and return to time 0." });
            playbackToolbar.Add(new Label("Time"));
            playbackTimeField = new DoubleField { name = "playback-time", tooltip = "Preview time in seconds. This does not change the document or create Undo steps." };
            // Match the native Editor numeric input width (50 pt in the connected Editor).
            playbackTimeField.style.width = EditorGUIUtility.fieldWidth;
            playbackTimeField.style.flexShrink = 0;
            playbackTimeField.RegisterValueChangedCallback(evt =>
            {
                if (activeDocument == null) return;
                double value = evt.newValue;
                if (!double.IsFinite(value) || value < 0 || value > float.MaxValue)
                    playbackTimeField.SetValueWithoutNotify(activeDocument.PreviewTime);
                else activeDocument.SetPreviewTime(value);
            });
            playbackToolbar.Add(playbackTimeField);
            playbackToolbar.Add(new Label("s"));
            playbackSlider = new Slider(0, 10) { name = "playback-scrub", tooltip = "Scrub preview time. The displayed range expands when time exceeds it." };
            playbackSlider.style.flexGrow = 1;
            playbackSlider.style.flexShrink = 1;
            playbackSlider.style.minWidth = 0;
            playbackSlider.RegisterValueChangedCallback(evt => activeDocument?.SetPreviewTime(evt.newValue));
            playbackToolbar.Add(playbackSlider);
            RefreshPlaybackVisibility();
            return playbackToolbar;
        }

        private void RefreshPlaybackVisibility()
        {
            if (playbackToolbar == null) return;
            bool show = WhimTexUserSettings.ShowTimeline;
            if (!show && playbackToolbar.style.display.value != DisplayStyle.None)
                activeDocument?.SetPlayback(false);
            playbackToolbar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) RefreshPlaybackControls();
        }

        private void RefreshPlaybackControls()
        {
            if (playbackToolbar == null || playbackToolbar.style.display.value == DisplayStyle.None) return;
            playbackToolbar.SetEnabled(activeDocument != null);
            double seconds = activeDocument != null ? activeDocument.PreviewTime : 0;
            playbackButton.text = activeDocument != null && activeDocument.IsPlaying ? "Pause" : "Play";
            if (playbackTimeField.value != seconds && !playbackTimeField.Contains(playbackTimeField.focusController?.focusedElement as VisualElement))
                playbackTimeField.SetValueWithoutNotify(seconds);
            if (seconds > playbackSlider.highValue)
                playbackSlider.highValue = (float)Math.Min(float.MaxValue, Math.Ceiling(seconds / 10d) * 10d);
            playbackSlider.SetValueWithoutNotify((float)seconds);
        }

        private void UpdatePlayback()
        {
            if (playbackDocument != activeDocument)
            {
                playbackDocument = activeDocument;
                playbackRevision = ulong.MaxValue;
                if (playbackSlider != null) playbackSlider.highValue = 10;
            }
            activeDocument?.TickPlayback(EditorApplication.timeSinceStartup);
            ulong revision = activeDocument != null ? activeDocument.TimeRevision : 0;
            if (playbackRevision == revision) return;
            playbackRevision = revision;
            RefreshPlaybackControls();
            if (activeDocument == null || canvasEffectCache != null && !canvasEffectCache.HasTimeDependencies) return;
            // Playback never invalidates the cache or republishes the imported time-zero image.
            ProfileCanvasRequest("document.time");
            canvasAt = EditorApplication.timeSinceStartup;
            canvasRequested = true;
        }
    }
}
