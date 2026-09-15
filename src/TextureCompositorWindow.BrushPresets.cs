using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private string selectedBrushPreset;
        [SerializeField] private string selectedBrushPresetSnapshot;
        [NonSerialized] private Button brushPresetButton;

        private static string GetDraggedBrushPresetPath()
        {
            if (DragAndDrop.GetGenericData(DraggedCompositorIdKey) != null) return null;
            var objects = DragAndDrop.objectReferences;
            if (objects.Length != 1 || !(objects[0] is BrushPresetAsset)) return null;
            string path = AssetDatabase.GetAssetPath(objects[0]);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private void OnBrushPresetDragUpdated(DragUpdatedEvent evt)
        {
            if (GetDraggedBrushPresetPath() == null) return;
            ClearToolkitDropIndicator();
            ClearFooterDropIndicator();
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopImmediatePropagation();
        }

        private void OnBrushPresetDragPerform(DragPerformEvent evt)
        {
            string path = GetDraggedBrushPresetPath();
            if (path == null) return;
            DragAndDrop.AcceptDrag();
            evt.StopImmediatePropagation();
            ClearToolkitDropIndicator();
            ClearFooterDropIndicator();
            FinishPaintingStroke();
            LoadBrushPreset(PresetLibraryPaths.PhysicalPath(path));
        }

        private void BuildBrushPresetControls(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-brush-preset-row");
            brushPresetButton = new Button(ShowBrushPresets);
            brushPresetButton.AddToClassList("whimtex-brush-preset-picker");
            row.Add(brushPresetButton);
            var save = new Button(SaveBrushPresetAs) { text = "Save As…", tooltip = "Save the brush and its tip as a portable preset. Palette colors are not included." };
            save.AddToClassList("whimtex-brush-preset-save");
            row.Add(save);
            parent.Add(row);
            RefreshBrushPresetButton();
        }

        private string BrushPresetSnapshot() => JsonUtility.ToJson(BrushPresetLibrary.Capture(paintSettings)) + "\n" +
            paintSettings.brushTipGuid + ":" + paintSettings.brushTipLocalId + "\n" + paintSettings.brushTipPresetPath;

        private void RefreshBrushPresetButton()
        {
            if (brushPresetButton == null || paintSettings == null) return;
            string name = string.IsNullOrEmpty(selectedBrushPreset) ? "Custom Brush" : Path.GetFileNameWithoutExtension(selectedBrushPreset);
            if (!string.IsNullOrEmpty(selectedBrushPreset) && selectedBrushPresetSnapshot != BrushPresetSnapshot()) name += " *";
            brushPresetButton.text = name + " ▾";
            brushPresetButton.tooltip = "Choose a brush preset. * means the current brush differs from the saved preset. Changes are not saved automatically.";
        }

        private void ShowBrushPresets()
        {
            try
            {
                var menu = new GenericMenu();
                string[] paths = BrushPresetLibrary.List();
                if (paths.Length == 0) menu.AddDisabledItem(new GUIContent("No saved brushes"));
                foreach (string path in paths)
                {
                    string selected = path;
                    menu.AddItem(new GUIContent(BrushPresetLibrary.MenuLabel(path), path), path == selectedBrushPreset,
                        () => LoadBrushPreset(selected));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Save As…"), false, SaveBrushPresetAs);
                menu.AddItem(new GUIContent("Save to Project…"), false, () => SaveBrushPresetAs(true));
                bool canOverwrite = !string.IsNullOrEmpty(selectedBrushPreset) && File.Exists(selectedBrushPreset);
                if (canOverwrite)
                {
                    try { BrushPresetLibrary.ValidateDestination(selectedBrushPreset); }
                    catch { canOverwrite = false; }
                }
                if (canOverwrite) menu.AddItem(new GUIContent("Overwrite Selected…"), false, () => SaveBrushPreset(selectedBrushPreset));
                else menu.AddDisabledItem(new GUIContent("Overwrite Selected…"));
                menu.AddItem(new GUIContent("Open Brushes Folder"), false, () =>
                {
                    try { Directory.CreateDirectory(BrushPresetLibrary.Folder); EditorUtility.RevealInFinder(BrushPresetLibrary.Folder); }
                    catch (Exception exception) { ShowBrushPresetError(exception); }
                });
                menu.AddItem(new GUIContent("Library Settings…"), false, WhimTexUserSettingsWindow.Open);
                menu.ShowAsContext();
            }
            catch (Exception exception) { ShowBrushPresetError(exception); }
        }

        private void LoadBrushPreset(string path)
        {
            Texture2D tip = null;
            try
            {
                BrushPresetLibrary.Preset preset = BrushPresetLibrary.Load(path, out tip);
                ApplyPaintToolChange(() => paintSettings.ApplyPreset(preset, tip, path));
                selectedBrushPreset = path;
                selectedBrushPresetSnapshot = BrushPresetSnapshot();
                RefreshBrushPresetButton();
            }
            catch (Exception exception)
            {
                if (tip != null && paintSettings.dynamics.tip != tip) DestroyImmediate(tip);
                ShowBrushPresetError(exception);
            }
        }

        private void SaveBrushPresetAs()
        {
            SaveBrushPresetAs(false);
        }

        private void SaveBrushPresetAs(bool project)
        {
            try
            {
                string folder = project ? Application.dataPath : BrushPresetLibrary.Folder;
                Directory.CreateDirectory(folder);
                string name = string.IsNullOrEmpty(selectedBrushPreset) ? "Brush" : Path.GetFileNameWithoutExtension(selectedBrushPreset);
                string path = EditorUtility.SaveFilePanel("Save Brush Preset", folder, name, BrushPresetLibrary.Extension);
                if (!string.IsNullOrEmpty(path)) SaveBrushPreset(path);
            }
            catch (Exception exception) { ShowBrushPresetError(exception); }
        }

        private void SaveBrushPreset(string path)
        {
            try
            {
                path = BrushPresetLibrary.ValidateDestination(path);
                bool overwrite = File.Exists(path);
                if (overwrite && !EditorUtility.DisplayDialog("Overwrite Brush Preset",
                    "Replace '" + Path.GetFileNameWithoutExtension(path) + "' with the current brush? A .bak copy will be kept.", "Overwrite", "Cancel")) return;
                BrushPresetLibrary.Save(path, paintSettings, overwrite);
                if (!string.IsNullOrEmpty(paintSettings.brushTipPresetPath))
                {
                    paintSettings.brushTipPresetPath = path;
                    SavePaintToolSettings();
                }
                selectedBrushPreset = path;
                selectedBrushPresetSnapshot = BrushPresetSnapshot();
                RefreshBrushPresetButton();
                ShowNotification(new GUIContent("Brush preset saved."));
            }
            catch (Exception exception) { ShowBrushPresetError(exception); }
        }

        private static void ShowBrushPresetError(Exception exception) =>
            EditorUtility.DisplayDialog("Brush Presets", exception.Message, "OK");
    }
}
