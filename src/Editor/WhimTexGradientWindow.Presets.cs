using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        private VisualElement presetGrid;
        private HelpBox presetWarning;
        private Image newPresetImage;
        private readonly List<Texture2D> presetPreviews = new List<Texture2D>();

        private void BuildPresets()
        {
            var header = new VisualElement();
            header.AddToClassList("whimtex-gradient-presets-header");
            header.Add(new Label("WhimTex Presets"));
            header.Add(new Button(RefreshPresets) { text = "↻", tooltip = "Refresh gradient presets" });
            rootVisualElement.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                scrollOffset = Vector2.zero
            };
            scroll.AddToClassList("whimtex-gradient-presets-scroll");
            presetGrid = new VisualElement();
            presetGrid.AddToClassList("whimtex-gradient-presets-grid");
            scroll.Add(presetGrid);
            rootVisualElement.Add(scroll);
            presetWarning = new HelpBox("", HelpBoxMessageType.Warning);
            rootVisualElement.Add(presetWarning);
            RefreshPresets();
        }

        private void RefreshPresets()
        {
            if (presetGrid == null) return;
            presetGrid.Clear();
            ReleasePresetPreviews();
            var entries = WhimTexGradientPresets.Read(out string warning);
            presetWarning.text = warning ?? "";
            presetWarning.EnableInClassList("whimtex-gradient-hidden", warning == null);
            foreach (var entry in entries)
            {
                var value = entry.Gradient;
                var button = new Button(() => UsePreset(value)) { tooltip = entry.Path };
                button.AddToClassList("whimtex-gradient-preset");
                var background = new VisualElement { pickingMode = PickingMode.Ignore };
                background.AddToClassList("whimtex-gradient-test-fill");
                background.generateVisualContent += DrawCheckerboard;
                button.Add(background);
                var image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill,
                    image = MakePresetPreview(value) };
                image.AddToClassList("whimtex-gradient-test-fill");
                button.Add(image);
                button.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    e.menu.AppendAction("Copy", _ => EditorGUIUtility.systemCopyBuffer = WhimTexGradientClipboard.Write(value));
                    e.menu.AppendAction("Delete", _ => PresetAction(() =>
                    {
                        WhimTexGradientPresets.Remove(entry.Path);
                        RefreshPresets();
                        ShowNotification(new GUIContent("Preset moved to Gradients/.trash."));
                    }));
                    e.StopPropagation();
                }));
                presetGrid.Add(button);
            }
            var add = new Button(() => PresetAction(() =>
            {
                WhimTexGradientPresets.Save(gradient);
                RefreshPresets();
                ShowNotification(new GUIContent("Gradient preset saved."));
            })) { tooltip = "Save current gradient as a new preset" };
            add.AddToClassList("whimtex-gradient-preset");
            add.AddToClassList("whimtex-gradient-preset-new");
            newPresetImage = new Image { image = preview, pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.StretchToFill };
            newPresetImage.AddToClassList("whimtex-gradient-test-fill");
            add.Add(newPresetImage);
            var label = new Label("New") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("whimtex-gradient-preset-new-label");
            add.Add(label);
            presetGrid.Insert(0, add);
        }

        private Texture2D MakePresetPreview(WhimTexGradient value)
        {
            var ramp = new Color[128];
            var pixels = new Color[256];
            value.Bake(ramp);
            for (int i = 0; i < 128; i++)
                pixels[i] = pixels[i + 128] = value.ColorSpace == ColorSpace.Linear ? ramp[i].gamma : ramp[i];
            var texture = new Texture2D(128, 2, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            presetPreviews.Add(texture);
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private void UsePreset(WhimTexGradient value)
        {
            Edit(() =>
            {
                gradient = value.Clone();
                selected = 0; midpointSelected = false; pendingRemoval = false;
                hdrPreferences.Clear(); intensityPreferences.Clear();
            });
        }

        private void PresetAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                Debug.LogError("[WhimTex] Gradient preset: " + error, this);
                ShowNotification(new GUIContent(error.Message));
            }
        }

        private void ReleasePresetPreviews()
        {
            foreach (var texture in presetPreviews) if (texture != null) DestroyImmediate(texture);
            presetPreviews.Clear();
        }
    }
}
