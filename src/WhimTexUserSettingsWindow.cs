using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexUserSettingsWindow : EditorWindow
    {
        private ColorField checkerLight;
        private ColorField checkerDark;
        private ColorField invalidPixels;
        private ColorField postFxBackground;
        private EnumField postFxBackgroundMode;
        private SliderInt checkerSize;
        private TextField presetsFolder;
        private Toggle cleanBackground;
        private Slider layerPickAlpha;
        private Slider snapRadius;
        private ColorField guideAlignedColor;
        private ColorField guideAngledColor;
        private ColorField guideActiveColor;

        internal static void Open()
        {
            var window = GetWindow<WhimTexUserSettingsWindow>(true, "WhimTex Settings");
            window.titleContent = WhimTexBranding.WindowTitle("WhimTex Settings");
            window.minSize = new Vector2(340f, 250f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = WhimTexBranding.WindowTitle("WhimTex Settings");
            WhimTexUserSettings.Changed += RefreshValues;
        }
        private void OnDisable() => WhimTexUserSettings.Changed -= RefreshValues;
        private void OnFocus() => RefreshValues();

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-user-settings");
            var scroll = new ScrollView();
            root.Add(scroll);
            AddHeading(scroll, "Preview Background");
            cleanBackground = new Toggle("Clean Preview Background")
            {
                tooltip = "Hide the logo on the preview background. Does not affect your image or exports."
            };
            cleanBackground.AddToClassList("whimtex-user-settings-color");
            cleanBackground.RegisterValueChangedCallback(evt => WhimTexUserSettings.ShowManta = !evt.newValue);
            scroll.Add(cleanBackground);
            AddHeading(scroll, "Transparency Checkerboard");
            checkerLight = AddColor(scroll, "Light Squares", value => WhimTexUserSettings.CheckerLight = value);
            checkerDark = AddColor(scroll, "Dark Squares", value => WhimTexUserSettings.CheckerDark = value);
            checkerSize = new SliderInt("Cell Size", WhimTexUserSettings.MinimumCheckerSize, WhimTexUserSettings.MaximumCheckerSize)
            {
                showInputField = true,
                tooltip = "Size of one checkerboard square in UI pixels, independent of canvas zoom. Default: 16."
            };
            checkerSize.AddToClassList("whimtex-user-settings-color");
            checkerSize.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.CheckerSize = evt.newValue;
                checkerSize.SetValueWithoutNotify(WhimTexUserSettings.CheckerSize);
            });
            scroll.Add(checkerSize);
            AddHeading(scroll, "Debug Preview");
            invalidPixels = AddColor(scroll, "Invalid Pixels", value => WhimTexUserSettings.InvalidPixels = value);
            invalidPixels.tooltip = "Display color for the accumulated numeric-error mask when Debug is enabled. Does not change image pixels or exports.";
            AddHeading(scroll, "Post FX Preview");
            postFxBackgroundMode = new EnumField("Background Mode", WhimTexUserSettings.PostFxBackgroundMode)
            {
                name = "postFxBackgroundMode",
                tooltip = "Solid Color or Checkerboard behind the composition before Post FX. Shared with the Post FX panel in every window."
            };
            postFxBackgroundMode.AddToClassList("whimtex-user-settings-color");
            postFxBackgroundMode.RegisterValueChangedCallback(evt => WhimTexUserSettings.PostFxBackgroundMode = (PostFxBackground)evt.newValue);
            scroll.Add(postFxBackgroundMode);
            postFxBackground = AddColor(scroll, "Background", value => WhimTexUserSettings.PostFxBackground = value);
            postFxBackground.tooltip = "Opaque fill behind the composition before Post FX. Shared with the Post FX panel. Original alpha is still used for depth; document pixels and exports are unchanged.";
            var note = new Label("Saved for your user account. Applies to all WhimTex windows; documents and exports are unaffected.");
            note.AddToClassList("whimtex-user-settings-note");
            scroll.Add(note);
            var reset = new Button(WhimTexUserSettings.ResetPreviewAppearance) { text = "Reset Preview Appearance" };
            reset.AddToClassList("whimtex-user-settings-reset");
            scroll.Add(reset);
            AddHeading(scroll, "Layer Select");
            layerPickAlpha = new Slider("Alpha ≥ %", 0f, 100f)
            {
                showInputField = true,
                tooltip = "Minimum visible alpha when selecting layers on the canvas with Layer Select (V). Default: 10%. Fully transparent pixels are always ignored."
            };
            layerPickAlpha.AddToClassList("whimtex-user-settings-color");
            layerPickAlpha.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.LayerPickAlphaThreshold = evt.newValue * .01f;
                layerPickAlpha.SetValueWithoutNotify(WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            });
            scroll.Add(layerPickAlpha);
            AddHeading(scroll, "Guides & Snapping");
            snapRadius = new Slider("Snap Radius (px)", WhimTexUserSettings.MinimumSnapRadius, WhimTexUserSettings.MaximumSnapRadius)
            {
                showInputField = true,
                tooltip = "Snapping distance in UI pixels, independent of canvas zoom. Applies to guides, intersections, canvas edges and pivot anchors. Default: 8. Does not change angular snapping or guide click targets."
            };
            snapRadius.AddToClassList("whimtex-user-settings-color");
            snapRadius.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.SnapRadius = evt.newValue;
                snapRadius.SetValueWithoutNotify(WhimTexUserSettings.SnapRadius);
            });
            scroll.Add(snapRadius);
            guideAlignedColor = AddColor(scroll, "Aligned Guides", value => WhimTexUserSettings.GuideAlignedColor = value);
            guideAlignedColor.tooltip = "Idle guides parallel to the current preview's horizontal or vertical axis.";
            guideAngledColor = AddColor(scroll, "Angled Guides", value => WhimTexUserSettings.GuideAngledColor = value);
            guideAngledColor.tooltip = "Idle guides at other angles in the current preview.";
            guideActiveColor = AddColor(scroll, "Active Guide", value => WhimTexUserSettings.GuideActiveColor = value);
            guideActiveColor.tooltip = "A hovered, selected or dragged guide. A guide about to be deleted stays red.";
            var resetGuides = new Button(WhimTexUserSettings.ResetGuidesAndSnapping) { text = "Reset Guides & Snapping" };
            resetGuides.AddToClassList("whimtex-user-settings-reset");
            scroll.Add(resetGuides);
            AddHeading(scroll, "Presets");
            var folderRow = new VisualElement();
            folderRow.AddToClassList("whimtex-user-settings-folder-row");
            presetsFolder = new TextField("Presets Folder")
            {
                isDelayed = true,
                tooltip = "Absolute path to the shared preset library. Saved in EditorPrefs for this user, independently of the project. Changing this path does not move or delete files."
            };
            presetsFolder.AddToClassList("whimtex-user-settings-folder");
            presetsFolder.RegisterValueChangedCallback(evt => SetPresetsFolder(evt.newValue));
            folderRow.Add(presetsFolder);
            var browse = new Button(() =>
            {
                string current = WhimTexUserSettings.PresetsFolder;
                string selected = EditorUtility.OpenFolderPanel("Choose Presets Folder",
                    System.IO.Directory.Exists(current) ? current : System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), string.Empty);
                if (!string.IsNullOrEmpty(selected)) SetPresetsFolder(selected);
            }) { text = "…", tooltip = "Choose presets folder" };
            browse.AddToClassList("whimtex-user-settings-folder-button");
            folderRow.Add(browse);
            var defaultFolder = new Button(WhimTexUserSettings.ResetPresetsFolder)
                { text = "↺", tooltip = "Use the default presets folder. Existing files are left untouched." };
            defaultFolder.AddToClassList("whimtex-user-settings-folder-button");
            folderRow.Add(defaultFolder);
            scroll.Add(folderRow);
            var folderNote = new Label("Shared across projects. Brushes and Shader FX use the Brushes and ShaderFX subfolders. Presets in the project are also available. Changing the path does not move files.");
            folderNote.AddToClassList("whimtex-user-settings-note");
            scroll.Add(folderNote);
            AddHeading(scroll, "Reset Settings");
            var resetAll = new Button(() => TextureCompositorWindow.ConfirmResetEditorSettings(this))
            {
                text = "Reset WhimTex Settings…",
                tooltip = "Restore all WhimTex preferences and workspace settings. Asks for confirmation; documents and preset files are preserved."
            };
            resetAll.AddToClassList("whimtex-user-settings-reset");
            scroll.Add(resetAll);
            RefreshValues();
        }

        private void SetPresetsFolder(string path)
        {
            if (!WhimTexUserSettings.TrySetPresetsFolder(path, out string error))
                EditorUtility.DisplayDialog("Presets Folder", error, "OK");
            presetsFolder.SetValueWithoutNotify(WhimTexUserSettings.PresetsFolder);
        }

        private static void AddHeading(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("whimtex-user-settings-heading");
            parent.Add(label);
        }

        private static ColorField AddColor(VisualElement parent, string label, System.Action<Color> write)
        {
            var field = new ColorField(label) { hdr = false, showAlpha = false };
            field.AddToClassList("whimtex-user-settings-color");
            field.RegisterValueChangedCallback(evt => write(evt.newValue));
            parent.Add(field);
            return field;
        }

        private void RefreshValues()
        {
            cleanBackground?.SetValueWithoutNotify(!WhimTexUserSettings.ShowManta);
            layerPickAlpha?.SetValueWithoutNotify(WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            snapRadius?.SetValueWithoutNotify(WhimTexUserSettings.SnapRadius);
            guideAlignedColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideAlignedColor);
            guideAngledColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideAngledColor);
            guideActiveColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideActiveColor);
            checkerLight?.SetValueWithoutNotify(WhimTexUserSettings.CheckerLight);
            checkerDark?.SetValueWithoutNotify(WhimTexUserSettings.CheckerDark);
            invalidPixels?.SetValueWithoutNotify(WhimTexUserSettings.InvalidPixels);
            postFxBackground?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackground);
            postFxBackgroundMode?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackgroundMode);
            postFxBackground?.SetEnabled(WhimTexUserSettings.PostFxBackgroundMode == PostFxBackground.SolidColor);
            checkerSize?.SetValueWithoutNotify(WhimTexUserSettings.CheckerSize);
            presetsFolder?.SetValueWithoutNotify(WhimTexUserSettings.PresetsFolder);
        }
    }
}
