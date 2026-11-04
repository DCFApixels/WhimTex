using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexUserSettingsWindow : EditorWindow
    {
        private ColorField checkerLight;
        private ColorField checkerDark;
        private ColorField invalidPixels;
        private ColorField postFxBackground;
        private EnumField postFxBackgroundMode;
        private EnumField imageOpening;
        private EnumField imageLayer;
        private SliderInt checkerSize;
        private TextField presetsFolder;
        private Toggle cleanBackground;
        private Toggle showTimeline;
        private Slider layerPickAlpha;
        private Slider snapRadius;
        private ColorField guideAlignedColor;
        private ColorField guideAngledColor;
        private ColorField guideActiveColor;
        private ColorField healingStrokeColor;
        private TextField vsCodeExecutable;
        private Button openVsCodeFolder;
        private CurveField tabletPressureCurve;
        private TextField commentAuthorName;
        private Label commentAuthorStatus;
        private ColorField commentAuthorColor;
        private Button resetCommentAuthorColor;
        private IntegerField undoDiskLimit, paintUndoSteps;
        private IntegerField colorHistoryLimit;
        private Label undoUsage;

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
            WhimTexUserSettings.EnsureCommentAuthorName();
            VisualElement root = rootVisualElement;
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-user-settings");
            var scroll = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("whimtex-user-settings-scroll");
            scroll.AddToClassList(InspectorElement.ussClassName);
            scroll.contentContainer.AddToClassList("whimtex-user-settings-content");
            root.Add(scroll);
            var section = AddSection(scroll, "Comments");
            var authorRow = new VisualElement();
            authorRow.AddToClassList("whimtex-user-settings-folder-row");
            commentAuthorName = new TextField("Nickname")
            {
                name = "commentAuthorName", isDelayed = true,
                tooltip = "Nickname saved with new comments and replies. Automatic detection uses your personal Unity organization, then the part of your Unity email before @. Changing it does not rename existing messages."
            };
            AlignField(commentAuthorName);
            commentAuthorName.AddToClassList("whimtex-user-settings-folder");
            commentAuthorName.RegisterValueChangedCallback(evt =>
            {
                if (!WhimTexUserSettings.TrySetCommentAuthorName(evt.newValue, out string error))
                    EditorUtility.DisplayDialog("Comment Author", error, "OK");
                RefreshValues();
            });
            authorRow.Add(commentAuthorName);
            var detectAuthor = new Button(WhimTexUserSettings.UseUnityCommentAuthorName)
            {
                name = "detectCommentAuthor", text = "↺",
                tooltip = "Detect your nickname from your personal Unity organization, falling back to your email before @. Replaces the saved name."
            };
            detectAuthor.AddToClassList("whimtex-user-settings-folder-button");
            detectAuthor.AddToClassList("whimtex-user-settings-folder-button--last");
            authorRow.Add(detectAuthor);
            section.Add(authorRow);
            commentAuthorStatus = new Label { name = "commentAuthorStatus" };
            commentAuthorStatus.AddToClassList("whimtex-user-settings-note");
            section.Add(commentAuthorStatus);
            var authorColorRow = new VisualElement();
            authorColorRow.AddToClassList("whimtex-user-settings-folder-row");
            commentAuthorColor = AddColor(authorColorRow, "Color", value => WhimTexUserSettings.CommentAuthorColor = value);
            commentAuthorColor.name = "commentAuthorColor";
            commentAuthorColor.tooltip = "Color for your automatic comments. Reset to choose a hue from your nickname.";
            commentAuthorColor.AddToClassList("whimtex-user-settings-folder");
            resetCommentAuthorColor = new Button(WhimTexUserSettings.ResetCommentAuthorColor)
            { name = "resetCommentAuthorColor", text = "↺", tooltip = "Use a color calculated from your nickname." };
            resetCommentAuthorColor.AddToClassList("whimtex-user-settings-folder-button");
            resetCommentAuthorColor.AddToClassList("whimtex-user-settings-folder-button--last");
            authorColorRow.Add(resetCommentAuthorColor);
            section.Add(authorColorRow);
            section = AddSection(scroll, "Color History");
            colorHistoryLimit = new IntegerField("Maximum Colors")
            {
                name = "colorHistoryLimit", isDelayed = true,
                tooltip = "Maximum colors in each document's Color History. Default: 256. Minimum: 1. Adding or reusing a color removes the oldest entries beyond this limit. Lowering the limit takes effect on the next addition or reuse."
            };
            AlignField(colorHistoryLimit);
            colorHistoryLimit.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.ColorHistoryLimit = evt.newValue;
                colorHistoryLimit.SetValueWithoutNotify(WhimTexUserSettings.ColorHistoryLimit);
            });
            section.Add(colorHistoryLimit);
            section = AddSection(scroll, "Tablet Input");
            var pressureRow = new VisualElement();
            pressureRow.AddToClassList("whimtex-user-settings-folder-row");
            tabletPressureCurve = new CurveField("Pressure Curve")
            { ranges = new Rect(0, 0, 1, 1), tooltip = "Global pressure response before brush curves. Mouse input is 1. This setting is not stored in brush presets." };
            AlignField(tabletPressureCurve);
            tabletPressureCurve.AddToClassList("whimtex-user-settings-folder");
            tabletPressureCurve.RegisterValueChangedCallback(e => WhimTexUserSettings.TabletPressureCurve = e.newValue);
            pressureRow.Add(tabletPressureCurve);
            var resetPressure = new Button(() => WhimTexUserSettings.TabletPressureCurve = WhimTexCurveTexture.Default())
                { name = "resetPressureCurve", text = "↺", tooltip = "Reset Pressure Curve" };
            resetPressure.AddToClassList("whimtex-user-settings-folder-button");
            resetPressure.AddToClassList("whimtex-user-settings-folder-button--last");
            pressureRow.Add(resetPressure);
            section.Add(pressureRow);
            section = AddSection(scroll, "Open Images");
            imageOpening = new EnumField("Double Click", WhimTexUserSettings.ImageOpening)
            {
                tooltip = "Open only layered WhimTex TIFF documents, or also PNG, JPEG, TGA, EXR, ordinary TIFF and Texture2D assets. PSD is excluded."
            };
            AlignField(imageOpening);
            TwoChoiceDropdown.Attach(imageOpening);
            imageOpening.RegisterValueChangedCallback(evt => WhimTexUserSettings.ImageOpening = (ImageOpenMode)evt.newValue);
            section.Add(imageOpening);
            imageLayer = new EnumField("Open As", WhimTexUserSettings.ImageLayer)
            {
                tooltip = "Drawing copies the imported pixels for painting. File references the imported texture. Layered TIFF documents always retain their layers. Source files and import settings are not changed."
            };
            AlignField(imageLayer);
            TwoChoiceDropdown.Attach(imageLayer);
            imageLayer.RegisterValueChangedCallback(evt => WhimTexUserSettings.ImageLayer = (ImageOpenLayer)evt.newValue);
            section.Add(imageLayer);
            section = AddSection(scroll, "External Code Editor");
            var vsCodeRow = new VisualElement();
            vsCodeRow.AddToClassList("whimtex-user-settings-folder-row");
            vsCodeExecutable = new TextField("VS Code Command")
            {
                isDelayed = true,
                tooltip = "Optional absolute path to the VS Code command or executable. WhimTex first checks Unity's registered editors and PATH, and uses this only as a fallback."
            };
            AlignField(vsCodeExecutable);
            vsCodeExecutable.AddToClassList("whimtex-user-settings-folder");
            vsCodeExecutable.RegisterValueChangedCallback(evt => SetVsCodeExecutable(evt.newValue));
            vsCodeRow.Add(vsCodeExecutable);
            var browseVsCode = new Button(() =>
            {
                string current = WhimTexUserSettings.VsCodeExecutable;
                string directory = System.IO.File.Exists(current) ? System.IO.Path.GetDirectoryName(current) : string.Empty;
                string selected = EditorUtility.OpenFilePanel("Choose VS Code Command", directory, string.Empty);
                if (!string.IsNullOrEmpty(selected)) SetVsCodeExecutable(selected);
            }) { text = "…", tooltip = "Choose the VS Code command or executable" };
            browseVsCode.AddToClassList("whimtex-user-settings-folder-button");
            vsCodeRow.Add(browseVsCode);
            openVsCodeFolder = CreateOpenFolderButton("openVsCodeFolder", "Open the containing folder",
                () => System.IO.Path.GetDirectoryName(WhimTexUserSettings.VsCodeExecutable));
            vsCodeRow.Add(openVsCodeFolder);
            var resetVsCode = new Button(WhimTexUserSettings.ResetVsCodeExecutable)
                { text = "↺", tooltip = "Clear the custom path and use automatic detection" };
            resetVsCode.AddToClassList("whimtex-user-settings-folder-button");
            resetVsCode.AddToClassList("whimtex-user-settings-folder-button--last");
            vsCodeRow.Add(resetVsCode);
            section.Add(vsCodeRow);
            var vsCodeNote = new Label("Optional fallback for installations not found by Unity or PATH. On macOS, install the code shell command in PATH or select its executable here.");
            vsCodeNote.AddToClassList("whimtex-user-settings-note");
            section.Add(vsCodeNote);
            section = AddSection(scroll, "Canvas View");
            cleanBackground = new Toggle("Clean Canvas View Background")
            {
                tooltip = "Hide the logo on the Canvas View background. Does not affect your image or exports."
            };
            AlignField(cleanBackground);
            cleanBackground.RegisterValueChangedCallback(evt => WhimTexUserSettings.ShowManta = !evt.newValue);
            section.Add(cleanBackground);
            checkerLight = AddColor(section, "Light Squares", value => WhimTexUserSettings.CheckerLight = value);
            checkerLight.AddToClassList("whimtex-user-settings-subsection-start");
            checkerDark = AddColor(section, "Dark Squares", value => WhimTexUserSettings.CheckerDark = value);
            checkerSize = new SliderInt("Cell Size", WhimTexUserSettings.MinimumCheckerSize, WhimTexUserSettings.MaximumCheckerSize)
            {
                showInputField = true,
                tooltip = "Size of one checkerboard square in UI pixels, independent of canvas zoom. Default: 16."
            };
            AlignField(checkerSize);
            checkerSize.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.CheckerSize = evt.newValue;
                checkerSize.SetValueWithoutNotify(WhimTexUserSettings.CheckerSize);
            });
            section.Add(checkerSize);
            invalidPixels = AddColor(section, "Invalid Pixels", value => WhimTexUserSettings.InvalidPixels = value);
            invalidPixels.AddToClassList("whimtex-user-settings-subsection-start");
            invalidPixels.tooltip = "Display color for the accumulated numeric-error mask when Debug is enabled. Does not change image pixels or exports.";
            section = AddSection(scroll, "Post FX Preview");
            postFxBackgroundMode = new EnumField("Background Mode", WhimTexUserSettings.PostFxBackgroundMode)
            {
                name = "postFxBackgroundMode",
                tooltip = "Solid Color or Checkerboard behind the composition before Post FX. Shared with the Post FX panel in every window."
            };
            AlignField(postFxBackgroundMode);
            TwoChoiceDropdown.Attach(postFxBackgroundMode);
            postFxBackgroundMode.RegisterValueChangedCallback(evt => WhimTexUserSettings.PostFxBackgroundMode = (PostFxBackground)evt.newValue);
            section.Add(postFxBackgroundMode);
            postFxBackground = AddColor(section, "Background", value => WhimTexUserSettings.PostFxBackground = value);
            postFxBackground.tooltip = "Opaque fill behind the composition before Post FX. Shared with the Post FX panel. Original alpha is still used for depth; document pixels and exports are unchanged.";
            section = AddSection(scroll, "Healing Brush");
            healingStrokeColor = AddColor(section, "Stroke Color", value => WhimTexUserSettings.HealingStrokeColor = value);
            healingStrokeColor.name = "healingStrokeColor";
            healingStrokeColor.showAlpha = true;
            healingStrokeColor.tooltip = "Color and opacity of the temporary repair-area overlay. Does not change the repair mask or result.";
            var appearanceActions = new VisualElement();
            appearanceActions.AddToClassList("whimtex-user-settings-section-content");
            appearanceActions.AddToClassList("whimtex-user-settings-appearance-actions");
            var note = new Label("Saved for your user account. Applies to all WhimTex windows; documents and exports are unaffected.");
            note.AddToClassList("whimtex-user-settings-note");
            appearanceActions.Add(note);
            var reset = new Button(WhimTexUserSettings.ResetCanvasViewAppearance) { text = "Reset Canvas View Appearance" };
            reset.AddToClassList("whimtex-user-settings-reset");
            appearanceActions.Add(reset);
            scroll.Add(appearanceActions);
            section = AddSection(scroll, "Layer Select");
            layerPickAlpha = new Slider("Alpha ≥ %", 0f, 100f)
            {
                showInputField = true,
                tooltip = "Minimum visible alpha when selecting layers on the canvas with Layer Select (V). Default: 10%. Fully transparent pixels are always ignored."
            };
            AlignField(layerPickAlpha);
            layerPickAlpha.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.LayerPickAlphaThreshold = evt.newValue * .01f;
                layerPickAlpha.SetValueWithoutNotify(WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            });
            section.Add(layerPickAlpha);
            section = AddSection(scroll, "Guides & Snapping", reset: WhimTexUserSettings.ResetGuidesAndSnapping);
            snapRadius = new Slider("Snap Radius (px)", WhimTexUserSettings.MinimumSnapRadius, WhimTexUserSettings.MaximumSnapRadius)
            {
                showInputField = true,
                tooltip = "Snapping distance in UI pixels, independent of canvas zoom. Applies to guides, intersections, canvas edges and pivot anchors. Default: 8. Does not change angular snapping or guide click targets."
            };
            AlignField(snapRadius);
            snapRadius.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.SnapRadius = evt.newValue;
                snapRadius.SetValueWithoutNotify(WhimTexUserSettings.SnapRadius);
            });
            section.Add(snapRadius);
            guideAlignedColor = AddColor(section, "Aligned Guides", value => WhimTexUserSettings.GuideAlignedColor = value);
            guideAlignedColor.tooltip = "Idle guides parallel to the current canvas view's horizontal or vertical axis.";
            guideAngledColor = AddColor(section, "Angled Guides", value => WhimTexUserSettings.GuideAngledColor = value);
            guideAngledColor.tooltip = "Idle guides at other angles in the current canvas view.";
            guideActiveColor = AddColor(section, "Active Guide", value => WhimTexUserSettings.GuideActiveColor = value);
            guideActiveColor.tooltip = "A hovered, selected or dragged guide. A guide about to be deleted stays red.";
            section = AddSection(scroll, "Presets");
            var folderRow = new VisualElement();
            folderRow.AddToClassList("whimtex-user-settings-folder-row");
            presetsFolder = new TextField("Presets Folder")
            {
                isDelayed = true,
                tooltip = "Absolute path to the shared preset library. Saved in EditorPrefs for this user, independently of the project. Changing this path does not move or delete files."
            };
            AlignField(presetsFolder);
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
            folderRow.Add(CreateOpenFolderButton("openPresetsFolder", "Open presets folder",
                () => WhimTexUserSettings.PresetsFolder, createIfMissing: true));
            var defaultFolder = new Button(WhimTexUserSettings.ResetPresetsFolder)
                { text = "↺", tooltip = "Use the default presets folder. Existing files are left untouched." };
            defaultFolder.AddToClassList("whimtex-user-settings-folder-button");
            defaultFolder.AddToClassList("whimtex-user-settings-folder-button--last");
            folderRow.Add(defaultFolder);
            section.Add(folderRow);
            var folderNote = new Label("Shared across projects. Brushes and Shader FX use the Brushes and ShaderFX subfolders. Presets in the project are also available. Changing the path does not move files.");
            folderNote.AddToClassList("whimtex-user-settings-note");
            section.Add(folderNote);
            section = AddSection(scroll, "Paint Undo", last: true);
            undoDiskLimit = new IntegerField("Undo Disk Limit (GiB)") { name = "undoDiskLimit", isDelayed = true,
                tooltip = "Shared by all WhimTex projects and Editor versions for this user. Includes queued writes and exact Half checkpoints. Range: 1–1024 GiB." };
            AlignField(undoDiskLimit);
            undoDiskLimit.RegisterValueChangedCallback(e => ChangeUndoLimits(() => WhimTexUserSettings.SetUndoDiskGiB(e.newValue)));
            section.Add(undoDiskLimit);
            paintUndoSteps = new IntegerField("Paint Undo Steps") { name = "paintUndoSteps", isDelayed = true,
                tooltip = "Maximum retained Brush/Pencil pixel operations per document, including Redo. One stroke is one step, regardless of tile count. Parameter edits use Unity Undo and do not count toward this limit. Default: 50. Range: 1–10000." };
            AlignField(paintUndoSteps);
            paintUndoSteps.RegisterValueChangedCallback(e => ChangeUndoLimits(() => WhimTexUserSettings.SetPaintUndoSteps(e.newValue)));
            section.Add(paintUndoSteps);
            undoUsage = new Label { name = "paintUndoUsage" };
            undoUsage.AddToClassList("whimtex-user-settings-note"); section.Add(undoUsage);
            var clearUndo = new Button(() =>
            {
                if (!EditorUtility.DisplayDialog("Clear Paint Undo",
                    "Keep current images, but remove previous Brush/Pencil Undo and Redo in this Editor? Exact Half checkpoints remain. Abandoned sessions are also removed; other running Editors are untouched.", "Clear", "Cancel")) return;
                ChangeUndoLimits(PaintHistoryStore.ClearCurrentEditor);
            }) { name = "clearPaintUndo", text = "Clear Paint Undo…" };
            clearUndo.AddToClassList("whimtex-user-settings-reset"); section.Add(clearUndo);
            scroll.schedule.Execute(RefreshUndoUsage).Every(2000);
            section = AddSection(scroll, "Experimental");
            showTimeline = new Toggle("Show Timeline")
            {
                name = "showTimeline",
                tooltip = "Show the experimental Canvas playback controls. Hiding them pauses playback and keeps the current preview time."
            };
            AlignField(showTimeline);
            showTimeline.RegisterValueChangedCallback(evt => WhimTexUserSettings.ShowTimeline = evt.newValue);
            section.Add(showTimeline);
            var footer = new VisualElement();
            footer.AddToClassList("whimtex-user-settings-section-content");
            footer.AddToClassList("whimtex-user-settings-footer");
            scroll.Add(footer);
            var resetAll = new Button(() => WhimTexWindow.ConfirmResetEditorSettings(this))
            {
                text = "Reset WhimTex Settings…",
                tooltip = "Restore all WhimTex preferences and workspace settings. Asks for confirmation; documents and preset files are preserved."
            };
            resetAll.AddToClassList("whimtex-user-settings-reset");
            footer.Add(resetAll);
            RefreshValues();
        }

        private static Button CreateOpenFolderButton(string name, string tooltip, System.Func<string> getFolder,
            bool createIfMissing = false)
        {
            var button = new Button(() =>
            {
                try
                {
                    string folder = getFolder();
                    if (string.IsNullOrWhiteSpace(folder)) return;
                    folder = System.IO.Path.GetFullPath(folder);
                    if (createIfMissing) System.IO.Directory.CreateDirectory(folder);
                    if (!System.IO.Directory.Exists(folder))
                    {
                        EditorUtility.DisplayDialog("Open Folder", "The folder no longer exists:\n" + folder, "OK");
                        return;
                    }
                    Application.OpenURL(new System.Uri(folder + System.IO.Path.DirectorySeparatorChar).AbsoluteUri);
                }
                catch (System.Exception error)
                {
                    EditorUtility.DisplayDialog("Open Folder", "Cannot open the folder: " + error.Message, "OK");
                }
            }) { name = name, tooltip = tooltip };
            button.AddToClassList("whimtex-user-settings-folder-button");
            button.AddToClassList("whimtex-user-settings-open-folder");
            button.Add(new LayerActionIcon(LayerActionIcon.Kind.Group));
            return button;
        }

        private void SetPresetsFolder(string path)
        {
            if (!WhimTexUserSettings.TrySetPresetsFolder(path, out string error))
                EditorUtility.DisplayDialog("Presets Folder", error, "OK");
            presetsFolder.SetValueWithoutNotify(WhimTexUserSettings.PresetsFolder);
        }

        private void SetVsCodeExecutable(string path)
        {
            if (!WhimTexUserSettings.TrySetVsCodeExecutable(path, out string error))
                EditorUtility.DisplayDialog("VS Code Command", error, "OK");
            vsCodeExecutable.SetValueWithoutNotify(WhimTexUserSettings.VsCodeExecutable);
        }

        private static VisualElement AddSection(VisualElement parent, string text, bool last = false,
            System.Action reset = null)
        {
            var section = new VisualElement();
            section.AddToClassList("whimtex-user-settings-section");
            if (last) section.AddToClassList("whimtex-user-settings-section--last");
            var header = new VisualElement();
            header.AddToClassList("whimtex-user-settings-header");
            var label = new Label(text);
            label.AddToClassList("whimtex-user-settings-heading");
            header.Add(label);
            if (reset != null)
            {
                var button = new Button(reset) { name = "resetSection", text = "↺", tooltip = "Reset " + text };
                button.AddToClassList("whimtex-user-settings-folder-button");
                button.AddToClassList("whimtex-user-settings-folder-button--last");
                header.Add(button);
            }
            section.Add(header);
            var content = new VisualElement();
            content.AddToClassList("whimtex-user-settings-section-content");
            section.Add(content);
            parent.Add(section);
            return content;
        }

        private static void AlignField(VisualElement field)
        {
            field.AddToClassList("whimtex-user-settings-field");
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
        }

        private static ColorField AddColor(VisualElement parent, string label, System.Action<Color> write)
        {
            var field = new ColorField(label) { hdr = false, showAlpha = false, Range = WhimTexColorRange.StandardOnly };
            AlignField(field);
            field.RegisterValueChangedCallback(evt => write(evt.newValue));
            parent.Add(field);
            return field;
        }

        private void RefreshValues()
        {
            RefreshUndoUsage();
            colorHistoryLimit?.SetValueWithoutNotify(WhimTexUserSettings.ColorHistoryLimit);
            commentAuthorColor?.SetValueWithoutNotify(WhimTexUserSettings.CommentAuthorColor);
            resetCommentAuthorColor?.SetEnabled(WhimTexUserSettings.HasCustomCommentAuthorColor);
            commentAuthorName?.SetValueWithoutNotify(WhimTexUserSettings.AssignedCommentAuthorName);
            if (commentAuthorStatus != null)
                commentAuthorStatus.text = WhimTexUserSettings.CommentAuthorNameSource switch
                {
                    CommentAuthorSource.UnityPrimaryOrganization => "Automatic: personal Unity organization.",
                    CommentAuthorSource.UnityEmail => "Automatic: name before @ in your Unity email.",
                    CommentAuthorSource.Manual => "Custom name. Used for new messages in all projects.",
                    _ => "Not assigned. Using User; detection will retry when WhimTex opens or you post."
                };
            tabletPressureCurve?.SetValueWithoutNotify(WhimTexUserSettings.TabletPressureCurve);
            imageOpening?.SetValueWithoutNotify(WhimTexUserSettings.ImageOpening);
            imageLayer?.SetValueWithoutNotify(WhimTexUserSettings.ImageLayer);
            imageLayer?.SetEnabled(WhimTexUserSettings.ImageOpening == ImageOpenMode.AllSupportedImages);
            cleanBackground?.SetValueWithoutNotify(!WhimTexUserSettings.ShowManta);
            showTimeline?.SetValueWithoutNotify(WhimTexUserSettings.ShowTimeline);
            layerPickAlpha?.SetValueWithoutNotify(WhimTexUserSettings.LayerPickAlphaThreshold * 100f);
            snapRadius?.SetValueWithoutNotify(WhimTexUserSettings.SnapRadius);
            guideAlignedColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideAlignedColor);
            guideAngledColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideAngledColor);
            guideActiveColor?.SetValueWithoutNotify(WhimTexUserSettings.GuideActiveColor);
            healingStrokeColor?.SetValueWithoutNotify(WhimTexUserSettings.HealingStrokeColor);
            checkerLight?.SetValueWithoutNotify(WhimTexUserSettings.CheckerLight);
            checkerDark?.SetValueWithoutNotify(WhimTexUserSettings.CheckerDark);
            invalidPixels?.SetValueWithoutNotify(WhimTexUserSettings.InvalidPixels);
            postFxBackground?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackground);
            postFxBackgroundMode?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackgroundMode);
            postFxBackground?.SetEnabled(WhimTexUserSettings.PostFxBackgroundMode == PostFxBackground.SolidColor);
            checkerSize?.SetValueWithoutNotify(WhimTexUserSettings.CheckerSize);
            presetsFolder?.SetValueWithoutNotify(WhimTexUserSettings.PresetsFolder);
            vsCodeExecutable?.SetValueWithoutNotify(WhimTexUserSettings.VsCodeExecutable);
            openVsCodeFolder?.SetEnabled(System.IO.File.Exists(WhimTexUserSettings.VsCodeExecutable));
        }
        private void ChangeUndoLimits(System.Action change)
        {
            try { change(); }
            catch (System.Exception error) { EditorUtility.DisplayDialog("Paint Undo", error.Message, "OK"); }
            RefreshUndoUsage();
        }
        private void RefreshUndoUsage()
        {
            if (undoUsage == null) return;
            try
            {
                var disk = PaintUndoDisk.Current;
                var limits = disk.ReadLimits();
                undoDiskLimit.SetValueWithoutNotify(limits.diskGiB); paintUndoSteps.SetValueWithoutNotify(limits.steps);
                long total = disk.Usage(), own = PaintUndoDisk.Measure(disk.Root);
                long baseline = PaintUndoDisk.Measure(System.IO.Path.Combine(disk.Root, "owners"));
                undoUsage.text = $"Total {total / (double)PaintUndoDisk.GiB:0.00} / {limits.diskGiB} GiB · This Editor {own / (double)PaintUndoDisk.GiB:0.00} GiB\nHalf checkpoints {baseline / (double)PaintUndoDisk.GiB:0.00} GiB · 512 MiB free-space reserve";
                if (PaintHistoryStore.LastMaintenanceError != null) undoUsage.text += "\n" + PaintHistoryStore.LastMaintenanceError;
                undoUsage.tooltip = disk.Home;
            }
            catch (System.Exception error) { undoUsage.text = error.Message; }
        }
    }
}
