using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradientWindow
    {
        private const string HistoryExpandedKey = "DCFApixels.WhimTex.Gradient.HistoryExpanded";
        private const string PresetsExpandedKey = "DCFApixels.WhimTex.Gradient.PresetsExpanded";
        private VisualElement presetGrid;
        private HelpBox presetWarning;
        private Image newPresetImage;
        private readonly List<Texture2D> presetPreviews = new List<Texture2D>();

        private Foldout BuildLibrarySection(string title, string name, string preference)
        {
            var section = new Foldout { text = title, name = name, value = EditorPrefs.GetBool(preference, true) };
            section.AddToClassList("whimtex-color-library-section");
            section.RegisterValueChangedCallback(e =>
            {
                if (e.target == section) EditorPrefs.SetBool(preference, e.newValue);
            });
            rootVisualElement.Add(section);
            return section;
        }

        private void BuildPresets()
        {
            var section = BuildLibrarySection("Presets", "gradientPresets", PresetsExpandedKey);
            var actions = new VisualElement();
            actions.AddToClassList("whimtex-gradient-presets-actions");
            var import = new Button(() => PresetAction(() =>
            {
                string path = EditorUtility.OpenFilePanel("Import Gradient Presets", "", "grd");
                if (string.IsNullOrEmpty(path)) return;
                var result = WhimTexGradientPresets.Import(path);
                RefreshPresets();
                if (result.warnings.Count > 0)
                {
                    presetWarning.text = string.Join("\n", result.warnings);
                    presetWarning.EnableInClassList("whimtex-gradient-hidden", false);
                    Debug.LogWarning("[WhimTex] GRD import: " + presetWarning.text, this);
                }
                ShowNotification(new GUIContent($"Imported {result.presets.Count} gradient presets."));
            })) { text = "Import…", name = "gradientPresetsImport", tooltip = "Import a GRD gradient library" };
            actions.Add(import);
            var refresh = new Button(RefreshPresets) { text = "↻", name = "gradientPresetsRefresh", tooltip = "Refresh gradient presets" };
            refresh.AddToClassList("whimtex-gradient-presets-refresh");
            actions.Add(refresh);
            section.hierarchy.Add(actions);
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
            section.Add(scroll);
            presetWarning = new HelpBox("", HelpBoxMessageType.Warning);
            section.Add(presetWarning);
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
