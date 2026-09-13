using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        private const int AllPreviewChannels = 15;
        [SerializeField] private int previewChannels = AllPreviewChannels;
        [NonSerialized] private RenderTexture channelPreviewTexture;
        [NonSerialized] private Button[] channelButtons;
        [SerializeField] private bool previewDebug;
        [NonSerialized] private float previewExposure;
        [NonSerialized] private Slider previewQualitySlider;
        [NonSerialized] private Label previewQualityValue;

        private Vector4 PreviewChannelMask => new Vector4(
            (previewChannels & 1) != 0 ? 1f : 0f,
            (previewChannels & 2) != 0 ? 1f : 0f,
            (previewChannels & 4) != 0 ? 1f : 0f,
            (previewChannels & 8) != 0 ? 1f : 0f);

        private VisualElement BuildPreviewFooter()
        {
            VisualElement footer = new VisualElement();
            footer.AddToClassList("sprite-editor-preview-footer");
            var left = new VisualElement { name = "previewFooterLeft" };
            left.AddToClassList("sprite-editor-preview-footer-side");
            footer.Add(left);
            var updates = CreatePreviewFooterGroup("previewFooterUpdates");
            updates.Add(BuildPreviewQualityControl());
            updates.Add(BuildLiveOutputButton());
            left.Add(updates);
            var context = CreatePreviewFooterGroup("previewFooterContext", true);
            context.Add(BuildPostFxButton());
            context.Add(BuildUvButton());
            left.Add(context);
            footer.RegisterCallback<GeometryChangedEvent>(evt =>
                footer.EnableInClassList("sprite-editor-preview-footer--compact", evt.newRect.width < 600f));
            toolkitPreviewFooter = new Label();
            toolkitPreviewFooter.AddToClassList("sprite-editor-preview-status");
            footer.Add(toolkitPreviewFooter);
            var right = new VisualElement { name = "previewFooterRight" };
            right.AddToClassList("sprite-editor-preview-footer-side");
            right.AddToClassList("sprite-editor-preview-channels");
            footer.Add(right);
            var inspection = CreatePreviewFooterGroup("previewFooterInspection");
            right.Add(inspection);
            inspection.Add(SpriteEditorColorInputs.CreateToggleControl());
            var exposure = new FloatField("EV") { value = previewExposure, tooltip = "Preview exposure only, in stops. Does not affect painting, fill sampling or export." };
            exposure.AddToClassList("sprite-editor-preview-exposure");
            exposure.EnableInClassList("sprite-editor-preview-exposure--adjusted", previewExposure != 0f);
            exposure.RegisterValueChangedCallback(evt =>
            {
                previewExposure = float.IsNaN(evt.newValue) ? 0f : Mathf.Clamp(evt.newValue, -20f, 20f);
                exposure.SetValueWithoutNotify(previewExposure);
                exposure.EnableInClassList("sprite-editor-preview-exposure--adjusted", previewExposure != 0f);
                UpdateChannelPreview(); UpdateToolkitPreviewPresentation();
            });
            inspection.Add(exposure);
            var debug = new Button(() =>
            {
                previewDebug = !previewDebug;
                UpdateChannelPreview(); UpdateToolkitPreviewPresentation();
            }) { tooltip = "Debug numeric errors: highlights invalid or overflowing components before they were replaced with zero. Preview only; choose the highlight color in User Settings." };
            debug.AddToClassList("sprite-editor-channel-button");
            debug.AddToClassList("sprite-editor-debug-button");
            debug.Add(new LayerActionIcon(LayerActionIcon.Kind.Bug));
            debug.schedule.Execute(() =>
            {
                debug.EnableInClassList("sprite-editor-channel-button--enabled", previewDebug);
                debug.EnableInClassList("sprite-editor-channel-button--error", compositor != null && compositor.HasNumericErrors);
            }).Every(150);
            inspection.Add(debug);
            var channels = CreatePreviewFooterGroup("previewFooterColor", true);
            right.Add(channels);
            channelButtons = new Button[4];
            string[] labels = { "R", "G", "B", "A" };
            for (int i = 0; i < labels.Length; i++)
            {
                int bit = 1 << i;
                Button button = new Button(() => TogglePreviewChannel(bit)) { text = labels[i] };
                button.tooltip = i == 3
                    ? "Alpha: off ignores transparency in Preview and gives the brush A=0 (no paint). " +
                      "Enable only A to view alpha in grayscale. Eraser is unaffected."
                    : labels[i] + " channel: show in Preview and use the brush value; off paints this component as 0. " +
                      "A single RGB channel is shown in grayscale; A controls its transparency. " +
                      "Existing pixels are not changed by toggling. Eraser is unaffected.";
                button.AddToClassList("sprite-editor-channel-button");
                if (i < 3)
                    button.AddToClassList("sprite-editor-channel-button--" + labels[i].ToLowerInvariant());
                channelButtons[i] = button;
                channels.Add(button);
            }
            RefreshChannelButtons();
            return footer;
        }

        private static VisualElement CreatePreviewFooterGroup(string name, bool separated = false)
        {
            var group = new VisualElement { name = name };
            group.AddToClassList("sprite-editor-preview-footer-group");
            if (separated) group.AddToClassList("sprite-editor-preview-footer-group--separated");
            return group;
        }

        private VisualElement BuildPreviewQualityControl()
        {
            VisualElement control = new VisualElement { tooltip = LivePreviewQualityContent.tooltip };
            control.AddToClassList("sprite-editor-preview-quality");
            Label label = new Label("Live Quality");
            label.AddToClassList("sprite-editor-preview-quality-label");
            control.Add(label);
            Slider quality = new Slider(
                MinimumPaintingPreviewScale * 100f, MaximumPaintingPreviewScale * 100f)
            {
                value = paintingPreviewScale * 100f,
                tooltip = LivePreviewQualityContent.tooltip
            };
            quality.AddToClassList("sprite-editor-preview-quality-slider");
            Label value = new Label($"{paintingPreviewScale * 100f:0.#}%");
            value.AddToClassList("sprite-editor-preview-quality-value");
            previewQualitySlider = quality;
            previewQualityValue = value;
            quality.RegisterValueChangedCallback(evt =>
            {
                if (previewTool == PreviewTool.Pencil) return;
                paintingPreviewScale = ClampPaintingPreviewScale(evt.newValue * 0.01f);
                EditorPrefs.SetFloat(PaintingPreviewScalePrefKey, paintingPreviewScale);
                value.text = $"{paintingPreviewScale * 100f:0.#}%";
            });
            control.Add(quality);
            control.Add(value);
            RefreshPreviewQualityControl();
            return control;
        }

        private void RefreshPreviewQualityControl()
        {
            if (previewQualitySlider == null || previewQualityValue == null) return;
            bool pencil = previewTool == PreviewTool.Pencil;
            float percent = pencil ? 100f : paintingPreviewScale * 100f;
            previewQualitySlider.SetEnabled(!pencil);
            if (!Mathf.Approximately(previewQualitySlider.value, percent))
                previewQualitySlider.SetValueWithoutNotify(percent);
            string text = $"{percent:0.#}%";
            if (previewQualityValue.text != text) previewQualityValue.text = text;
            previewQualitySlider.tooltip = pencil
                ? "Pencil uses full canvas resolution. Your Live Quality preference is restored with other tools."
                : LivePreviewQualityContent.tooltip;
        }

        private void TogglePreviewChannel(int bit)
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            previewChannels = (previewChannels ^ bit) & AllPreviewChannels;
            RefreshChannelButtons();
            UpdateChannelPreview();
            UpdateToolkitPreviewPresentation();
        }

        private void RefreshChannelButtons()
        {
            if (channelButtons == null)
                return;
            for (int i = 0; i < channelButtons.Length; i++)
                channelButtons[i].EnableInClassList("sprite-editor-channel-button--enabled", (previewChannels & (1 << i)) != 0);
        }

        private Color GetPaintingColor()
        {
            Color color = SpriteEditorColorInputs.DisplayColor(paintSettings.brushColor);
            if (paintingErase)
                return color;
            Vector4 mask = PreviewChannelMask;
            return HdrUtility.ApplyChannelMask(color, mask);
        }

        private void UpdateChannelPreview()
        {
            if (previewTexture == null)
            {
                ReleaseChannelPreview();
                return;
            }
            Material material = SpriteEditorMaterials.PreviewChannels;
            if (material == null)
            {
                ReleaseChannelPreview();
                return;
            }
            if (channelPreviewTexture == null || channelPreviewTexture.width != previewTexture.width ||
                channelPreviewTexture.height != previewTexture.height)
            {
                ReleaseChannelPreview();
                channelPreviewTexture = new RenderTexture(previewTexture.width, previewTexture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "WhimTex Channel Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            channelPreviewTexture.filterMode = previewTexture.filterMode;
            material.SetVector("_Channels", PreviewChannelMask);
            material.SetFloat("_Exposure", Mathf.Pow(2f, previewExposure));
            material.SetFloat("_Debug", previewDebug ? 1f : 0f);
            Color errorColor = SpriteEditorUserSettings.InvalidPixels;
            material.SetVector("_ErrorColor", (Vector4)(QualitySettings.activeColorSpace == ColorSpace.Linear ? errorColor.linear : errorColor));
            material.SetTexture("_Errors", compositor != null && compositor.NumericErrorMask != null ? compositor.NumericErrorMask : Texture2D.blackTexture);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(PreviewPresentationSource, channelPreviewTexture, material);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void ReleaseChannelPreview()
        {
            if (channelPreviewTexture == null)
                return;
            channelPreviewTexture.Release();
            DestroyImmediate(channelPreviewTexture);
            channelPreviewTexture = null;
        }
    }
}
