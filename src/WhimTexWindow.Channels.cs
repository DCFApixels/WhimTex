using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private const int AllCanvasChannels = 15;
        internal static Func<int> FindColorChannelSource(WhimTexDocument document)
        {
            if (document == null) return null;
            WhimTexWindow match = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (window.activeDocument == document)
                {
                    if (match != null) return null;
                    match = window;
                }
            return match == null ? null : () => match != null && match.activeDocument == document ? match.canvasChannels : -1;
        }
        [SerializeField] private int canvasChannels = AllCanvasChannels;
        [NonSerialized] private RenderTexture channelCanvasTexture;
        [NonSerialized] private Button[] channelButtons;
        [SerializeField] private bool canvasDebug;
        [NonSerialized] private float canvasExposure;
        [NonSerialized] private Slider canvasQualitySlider;
        [NonSerialized] private Label canvasQualityValue;

        private Vector4 CanvasChannelMask => new Vector4(
            (canvasChannels & 1) != 0 ? 1f : 0f,
            (canvasChannels & 2) != 0 ? 1f : 0f,
            (canvasChannels & 4) != 0 ? 1f : 0f,
            (canvasChannels & 8) != 0 ? 1f : 0f);

        private VisualElement BuildCanvasViewFooter()
        {
            VisualElement footer = new VisualElement();
            footer.AddToClassList("whimtex-canvas-view-footer");
            var left = new VisualElement { name = "canvasFooterLeft" };
            left.AddToClassList("whimtex-canvas-view-footer-side");
            footer.Add(left);
            var updates = CreateCanvasViewFooterGroup("canvasFooterUpdates");
            updates.Add(BuildCanvasQualityControl());
            updates.Add(BuildLiveOutputButton());
            left.Add(updates);
            var context = CreateCanvasViewFooterGroup("canvasFooterContext", true);
            context.Add(BuildPostFxButton());
            context.Add(BuildUvButton());
            context.Add(BuildTiledCanvasButton());
            context.Add(BuildGuidesButton());
            left.Add(context);
            toolkitCanvasViewFooter = new CanvasViewFooterHintLabel();
            toolkitCanvasViewFooter.AddToClassList("whimtex-canvas-view-status");
            footer.Add(toolkitCanvasViewFooter);
            var right = new VisualElement { name = "canvasFooterRight" };
            right.AddToClassList("whimtex-canvas-view-footer-side");
            right.AddToClassList("whimtex-canvas-view-channels");
            footer.Add(right);
            var inspection = CreateCanvasViewFooterGroup("canvasFooterInspection");
            right.Add(inspection);
            inspection.Add(WhimTexColorInputs.CreateToggleControl());
            var exposure = new FloatField("EV") { value = canvasExposure, tooltip = "Canvas View exposure only, in stops. Does not affect painting, fill sampling or export." };
            exposure.AddToClassList("whimtex-canvas-view-exposure");
            exposure.EnableInClassList("whimtex-canvas-view-exposure--adjusted", canvasExposure != 0f);
            exposure.RegisterValueChangedCallback(evt =>
            {
                canvasExposure = float.IsNaN(evt.newValue) ? 0f : Mathf.Clamp(evt.newValue, -20f, 20f);
                exposure.SetValueWithoutNotify(canvasExposure);
                exposure.EnableInClassList("whimtex-canvas-view-exposure--adjusted", canvasExposure != 0f);
                UpdateChannelCanvas(); UpdateToolkitCanvasPresentation();
            });
            inspection.Add(exposure);
            var debug = new Button(() =>
            {
                canvasDebug = !canvasDebug;
                UpdateChannelCanvas(); UpdateToolkitCanvasPresentation();
            }) { tooltip = "Debug numeric errors: highlights invalid or overflowing components before they were replaced with zero. Canvas View only; choose the highlight color in User Settings." };
            debug.AddToClassList("whimtex-channel-button");
            debug.AddToClassList("whimtex-debug-button");
            debug.Add(new LayerActionIcon(LayerActionIcon.Kind.Bug));
            debug.schedule.Execute(() =>
            {
                debug.EnableInClassList("whimtex-channel-button--enabled", canvasDebug);
                debug.EnableInClassList("whimtex-channel-button--error", activeDocument != null && activeDocument.HasNumericErrors);
            }).Every(150);
            inspection.Add(debug);
            var channels = CreateCanvasViewFooterGroup("canvasFooterColor", true);
            right.Add(channels);
            channelButtons = new Button[4];
            string[] labels = { "R", "G", "B", "A" };
            for (int i = 0; i < labels.Length; i++)
            {
                int bit = 1 << i;
                Button button = new Button(() => ToggleCanvasChannel(bit)) { text = labels[i] };
                button.tooltip = i == 3
                    ? "Alpha: off ignores transparency in Canvas View. Enable only A to view alpha in grayscale. Painting is unaffected; use Write channels in the header to protect pixels."
                    : labels[i] + " channel: show in Canvas View. " +
                      "A single RGB channel is shown in grayscale; A controls its transparency. " +
                      "Painting is unaffected; use Write channels in the header to protect pixels.";
                button.AddToClassList("whimtex-channel-button");
                if (i < 3)
                    button.AddToClassList("whimtex-channel-button--" + labels[i].ToLowerInvariant());
                channelButtons[i] = button;
                channels.Add(button);
            }
            channels.AddManipulator(new ChannelDragManipulator(channelButtons, () => canvasChannels, ToggleCanvasChannel));
            var hint = toolkitCanvasViewFooter;
            float normalUpdatesWidth = 0f;
            void RefreshFooterLayout()
            {
                bool compact = footer.ClassListContains("whimtex-canvas-view-footer--compact");
                if (!compact) normalUpdatesWidth = updates.layout.width;
                float RequiredWidth(VisualElement group) => group.layout.width +
                    group.resolvedStyle.marginLeft + group.resolvedStyle.marginRight;
                float required = Mathf.Max(normalUpdatesWidth, updates.layout.width) +
                    RequiredWidth(context) + RequiredWidth(inspection) + RequiredWidth(channels) +
                    hint.resolvedStyle.marginLeft + hint.resolvedStyle.marginRight;
                if (float.IsNaN(required) || normalUpdatesWidth <= 0f) return;
                footer.EnableInClassList("whimtex-canvas-view-footer--compact", footer.contentRect.width < required);
            }
            footer.RegisterCallback<GeometryChangedEvent>(_ => RefreshFooterLayout());
            foreach (var group in new[] { updates, context, inspection, channels })
                group.RegisterCallback<GeometryChangedEvent>(_ => RefreshFooterLayout());
            RefreshChannelButtons();
            return footer;
        }

        private static VisualElement CreateCanvasViewFooterGroup(string name, bool separated = false)
        {
            var group = new VisualElement { name = name };
            group.AddToClassList("whimtex-canvas-view-footer-group");
            if (separated) group.AddToClassList("whimtex-canvas-view-footer-group--separated");
            return group;
        }

        private sealed class CanvasViewFooterHintLabel : Label
        {
            private string measuredText;
            private float measuredWidth;

            public CanvasViewFooterHintLabel()
            {
                RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    measuredText = null;
                    RefreshVisibility();
                });
            }

            public void RefreshVisibility()
            {
                if (panel == null) return;
                if (measuredText != text)
                {
                    measuredWidth = MeasureTextSize(text, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
                    measuredText = text;
                }
                EnableInClassList("whimtex-canvas-view-status--hidden",
                    string.IsNullOrEmpty(text) || !(measuredWidth <= contentRect.width));
            }
        }

        private VisualElement BuildCanvasQualityControl()
        {
            VisualElement control = new VisualElement { tooltip = LiveCanvasQualityContent.tooltip };
            control.AddToClassList("whimtex-canvas-view-quality");
            Label label = new Label("Live Quality");
            label.AddToClassList("whimtex-canvas-view-quality-label");
            control.Add(label);
            Slider quality = new Slider(
                MinimumPaintingCanvasScale * 100f, MaximumPaintingCanvasScale * 100f)
            {
                value = paintingCanvasScale * 100f,
                tooltip = LiveCanvasQualityContent.tooltip
            };
            quality.AddToClassList("whimtex-canvas-view-quality-slider");
            Label value = new Label($"{paintingCanvasScale * 100f:0.#}%");
            value.AddToClassList("whimtex-canvas-view-quality-value");
            canvasQualitySlider = quality;
            canvasQualityValue = value;
            quality.RegisterValueChangedCallback(evt =>
            {
                if (canvasTool == CanvasTool.Pencil) return;
                paintingCanvasScale = ClampPaintingCanvasScale(evt.newValue * 0.01f);
                EditorPrefs.SetFloat(PaintingCanvasScalePrefKey, paintingCanvasScale);
                value.text = $"{paintingCanvasScale * 100f:0.#}%";
            });
            control.Add(quality);
            control.Add(value);
            RefreshCanvasQualityControl();
            return control;
        }

        private void RefreshCanvasQualityControl()
        {
            if (canvasQualitySlider == null || canvasQualityValue == null) return;
            bool pencil = canvasTool == CanvasTool.Pencil;
            float percent = pencil ? 100f : paintingCanvasScale * 100f;
            canvasQualitySlider.SetEnabled(!pencil);
            if (!Mathf.Approximately(canvasQualitySlider.value, percent))
                canvasQualitySlider.SetValueWithoutNotify(percent);
            string text = $"{percent:0.#}%";
            if (canvasQualityValue.text != text) canvasQualityValue.text = text;
            canvasQualitySlider.tooltip = pencil
                ? "Pencil uses full canvas resolution. Your Live Quality preference is restored with other tools."
                : LiveCanvasQualityContent.tooltip;
        }

        private void ToggleCanvasChannel(int bit)
        {
            FinishCanvasTransform();
            FinishPaintingStroke();
            canvasChannels = (canvasChannels ^ bit) & AllCanvasChannels;
            RefreshChannelButtons();
            UpdateChannelCanvas();
            UpdateToolkitCanvasPresentation();
        }

        private void RefreshChannelButtons()
        {
            if (channelButtons == null)
                return;
            for (int i = 0; i < channelButtons.Length; i++)
                channelButtons[i].EnableInClassList("whimtex-channel-button--enabled", (canvasChannels & (1 << i)) != 0);
        }

        private Color GetPaintingColor()
        {
            return WhimTexColorInputs.DisplayColor(paintSettings.brushColor);
        }

        private void UpdateChannelCanvas()
        {
            if (canvasTexture == null)
            {
                ReleaseChannelCanvas();
                return;
            }
            Material material = WhimTexMaterials.DisplayChannels;
            if (material == null)
            {
                ReleaseChannelCanvas();
                return;
            }
            if (channelCanvasTexture == null || channelCanvasTexture.width != canvasTexture.width ||
                channelCanvasTexture.height != canvasTexture.height)
            {
                ReleaseChannelCanvas();
                channelCanvasTexture = new RenderTexture(canvasTexture.width, canvasTexture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "WhimTex Canvas Channels",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            channelCanvasTexture.filterMode = canvasTexture.filterMode;
            material.SetVector("_Channels", CanvasChannelMask);
            material.SetFloat("_Exposure", Mathf.Pow(2f, canvasExposure));
            material.SetFloat("_Debug", canvasDebug ? 1f : 0f);
            Color errorColor = WhimTexUserSettings.InvalidPixels;
            material.SetVector("_ErrorColor", (Vector4)(QualitySettings.activeColorSpace == ColorSpace.Linear ? errorColor.linear : errorColor));
            material.SetTexture("_Errors", activeDocument != null && activeDocument.NumericErrorMask != null ? activeDocument.NumericErrorMask : Texture2D.blackTexture);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(CanvasPresentationSource, channelCanvasTexture, material);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void ReleaseChannelCanvas()
        {
            if (channelCanvasTexture == null)
                return;
            channelCanvasTexture.Release();
            DestroyImmediate(channelCanvasTexture);
            channelCanvasTexture = null;
        }
    }
}
