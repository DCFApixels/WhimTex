using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private PostFxPreviewSettings postFxSettings = new PostFxPreviewSettings();
        [SerializeField] private bool postFxEnabled;
        [SerializeField] private bool postFxExpanded = true;
        private PostFxPreviewBackend postFxBackend;
        private RenderTexture postFxTexture;
        private bool postFxValid;
        private bool postFxDirty = true;
        private int postFxStateHash;
        private double nextPostFxCheck;
        private VisualElement postFxOverlay, postFxDrawer;
        private Button postFxTab, postFxButton;
        private HelpBox postFxStatus;
        private ColorField postFxBackgroundField;
        private Action refreshPostFxFields;
        private string postFxMessage;
        private bool postFxFailed;

        private Texture PreviewPresentationSource => postFxEnabled && postFxValid && postFxTexture != null ? postFxTexture : previewTexture;

        private VisualElement BuildPostFxPreview(VisualElement preview)
        {
            postFxSettings ??= new PostFxPreviewSettings();
            var workspace = new VisualElement { name = "previewWorkspace" };
            workspace.AddToClassList("sprite-editor-preview-workspace");
            workspace.Add(preview);
            postFxOverlay = new VisualElement { name = "postFxOverlay", pickingMode = PickingMode.Ignore };
            postFxOverlay.AddToClassList("sprite-editor-post-fx-overlay");
            workspace.Add(postFxOverlay);
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.AddToClassList("sprite-editor-post-fx-panel");
            postFxOverlay.Add(panel);
            var tabs = new VisualElement { pickingMode = PickingMode.Ignore };
            tabs.AddToClassList("sprite-editor-preview-drawer-tabs");
            panel.Add(tabs);
            BuildBrushTab(tabs);
            postFxTab = new Button(() =>
            {
                postFxExpanded = !postFxExpanded;
                if (postFxExpanded) brushesExpanded = uvExpanded = false;
                RefreshPostFxPanel();
            })
                { tooltip = "Show or hide post-processing settings. Closing this panel keeps Post FX enabled." };
            postFxTab.AddToClassList("sprite-editor-post-fx-tab");
            tabs.Add(postFxTab);
            BuildUvTab(tabs);
            BuildBrushDrawer(panel);
            BuildUvDrawer(panel);
            postFxDrawer = new VisualElement();
            postFxDrawer.AddToClassList("sprite-editor-post-fx-drawer");
            postFxDrawer.Add(CreatePaneHeader("Post FX", "postFxTitle"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("sprite-editor-post-fx-settings");
            postFxDrawer.Add(scroll);
            panel.Add(postFxDrawer);
            BuildPostFxFields(scroll);
            RefreshPostFxPanel();
            return workspace;
        }

        private Button BuildPostFxButton()
        {
            postFxButton = new Button(() =>
            {
                postFxEnabled = !postFxEnabled;
                if (!postFxEnabled) ReleasePostFx();
                else
                {
                    postFxDirty = true;
                    if (postFxExpanded) brushesExpanded = uvExpanded = false;
                }
                RefreshPostFxPanel();
                if (postFxEnabled) RenderPostFx();
                UpdateChannelPreview();
                UpdateToolkitPreviewPresentation();
            }) { text = "Post FX", tooltip = "Preview through the scene, game camera or a Volume Profile. Does not affect painting, sampling or export." };
            postFxButton.AddToClassList("sprite-editor-channel-button");
            postFxButton.AddToClassList("sprite-editor-post-fx-button");
            postFxButton.EnableInClassList("sprite-editor-channel-button--enabled", postFxEnabled);
            return postFxButton;
        }

        private void BuildPostFxFields(VisualElement root)
        {
            root.Add(SpriteEditorUI.CreateHeading("Source"));
            AddPostFxEnum(root, "Source", postFxSettings.source, value => postFxSettings.source = value);
            var camera = SpriteEditorUI.ConfigureField(new ObjectField("Camera") { objectType = typeof(Camera), allowSceneObjects = true, value = postFxSettings.camera,
                tooltip = "An empty field uses the MainCamera-tagged camera." });
            camera.RegisterValueChangedCallback(evt => { postFxSettings.camera = evt.newValue as Camera; PostFxSettingsChanged(); });
            root.Add(camera);
            var profile = SpriteEditorUI.ConfigureField(new ObjectField("Profile") { objectType = typeof(ScriptableObject), allowSceneObjects = false, value = postFxSettings.profile });
            profile.RegisterValueChangedCallback(evt => { postFxSettings.profile = evt.newValue; PostFxSettingsChanged(); });
            root.Add(profile);
            AddPostFxToggle(root, "Animate", postFxSettings.animate, value => postFxSettings.animate = value,
                "Refresh animated effects at up to 8 fps. Off refreshes when source settings or image change.");
            root.Add(SpriteEditorUI.CreateHeading("Camera"));
            AddPostFxEnum(root, "Projection", postFxSettings.projection, value => postFxSettings.projection = value);
            var manual = new VisualElement(); root.Add(manual);
            var fov = AddPostFxFloat(manual, "Field of View", postFxSettings.fieldOfView, 1, 179, value => postFxSettings.fieldOfView = value);
            var ortho = AddPostFxFloat(manual, "Ortho Size", postFxSettings.orthographicSize, .001f, 100000, value => postFxSettings.orthographicSize = value);
            AddPostFxFloat(manual, "Near", postFxSettings.near, .001f, 100000, value => postFxSettings.near = value);
            AddPostFxFloat(manual, "Far", postFxSettings.far, .01f, 1000000, value => postFxSettings.far = value);
            root.Add(SpriteEditorUI.CreateHeading("Background"));
            AddPostFxEnum(root, "Mode", postFxSettings.backgroundMode, value => postFxSettings.backgroundMode = value);
            postFxBackgroundField = SpriteEditorUI.ConfigureField(new ColorField("Color")
            {
                name = "postFxBackground",
                hdr = false,
                showAlpha = false,
                value = SpriteEditorUserSettings.PostFxBackground,
                tooltip = "Opaque fill behind the composition before Post FX. Shared with User Settings; original alpha is still used for depth. Does not affect document pixels or export."
            });
            postFxBackgroundField.RegisterValueChangedCallback(evt =>
            {
                SpriteEditorUserSettings.PostFxBackground = evt.newValue;
                postFxBackgroundField.SetValueWithoutNotify(SpriteEditorUserSettings.PostFxBackground);
            });
            root.Add(postFxBackgroundField);
            var checkerInfo = new HelpBox("Uses checkerboard colors and cell size from User Settings. Cells are measured in canvas pixels and zoom with the image.", HelpBoxMessageType.Info);
            root.Add(checkerInfo);
            root.Add(SpriteEditorUI.CreateHeading("Depth"));
            AddPostFxEnum(root, "Mode", postFxSettings.depth, value => postFxSettings.depth = value);
            AddPostFxFloat(root, "Distance", postFxSettings.distance, .001f, 1000000, value => postFxSettings.distance = value);
            var range = AddPostFxFloat(root, "Depth Range", postFxSettings.depthRange, 0, 1000000, value => postFxSettings.depthRange = value);
            var threshold = SpriteEditorUI.ConfigureField(new Slider("Threshold", 0, 1) { value = postFxSettings.threshold, showInputField = true });
            threshold.RegisterValueChangedCallback(evt => { postFxSettings.threshold = Mathf.Clamp01(evt.newValue); PostFxSettingsChanged(); });
            root.Add(threshold);
            var invert = AddPostFxToggle(root, "Invert", postFxSettings.invert, value => postFxSettings.invert = value);
            AddPostFxToggle(root, "Link to Zoom", postFxSettings.linkDistanceToZoom, value => postFxSettings.linkDistanceToZoom = value,
                "Simulate distance changes when zooming the preview. Otherwise zoom only magnifies the processed result.");
            postFxStatus = new HelpBox("Preview only. Alpha Height creates relief; Alpha Mask places pixels below Threshold at the far plane.", HelpBoxMessageType.Info);
            root.Add(postFxStatus);
            root.Add(new Button(() => { postFxDirty = true; nextPostFxCheck = 0; }) { text = "Refresh Post FX" });
            refreshPostFxFields = () =>
            {
                bool checkerBackground = postFxSettings.backgroundMode == PostFxBackground.Checkerboard;
                postFxBackgroundField.EnableInClassList("sprite-editor-post-fx-field--hidden", checkerBackground);
                checkerInfo.EnableInClassList("sprite-editor-post-fx-field--hidden", !checkerBackground);
                camera.style.display = postFxSettings.source == PostFxSource.GameCamera ? DisplayStyle.Flex : DisplayStyle.None;
                profile.style.display = postFxSettings.source == PostFxSource.Profile ? DisplayStyle.Flex : DisplayStyle.None;
                profile.objectType = postFxBackend?.ProfileType ?? typeof(ScriptableObject);
                manual.style.display = postFxSettings.source == PostFxSource.Profile || postFxSettings.projection != PostFxProjection.Source ? DisplayStyle.Flex : DisplayStyle.None;
                fov.style.display = postFxSettings.projection != PostFxProjection.Orthographic ? DisplayStyle.Flex : DisplayStyle.None;
                ortho.style.display = postFxSettings.projection == PostFxProjection.Orthographic ? DisplayStyle.Flex : DisplayStyle.None;
                range.style.display = postFxSettings.depth == PostFxDepth.AlphaHeight ? DisplayStyle.Flex : DisplayStyle.None;
                threshold.style.display = postFxSettings.depth == PostFxDepth.AlphaMask ? DisplayStyle.Flex : DisplayStyle.None;
                invert.style.display = postFxSettings.depth != PostFxDepth.Solid ? DisplayStyle.Flex : DisplayStyle.None;
            };
        }

        private void AddPostFxEnum<T>(VisualElement root, string label, T value, Action<T> write) where T : Enum
        {
            var field = SpriteEditorUI.ConfigureField(new EnumField(label, value));
            field.RegisterValueChangedCallback(evt => { write((T)evt.newValue); PostFxSettingsChanged(); });
            root.Add(field);
        }

        private VisualElement AddPostFxFloat(VisualElement root, string label, float value, float min, float max, Action<float> write)
        {
            var field = SpriteEditorUI.ConfigureField(new FloatField(label) { value = value, isDelayed = true });
            field.RegisterValueChangedCallback(evt =>
            {
                float next = float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue) ? min : Mathf.Clamp(evt.newValue, min, max);
                field.SetValueWithoutNotify(next); write(next); PostFxSettingsChanged();
            });
            root.Add(field); return field;
        }

        private VisualElement AddPostFxToggle(VisualElement root, string label, bool value, Action<bool> write, string tooltip = null)
        {
            var field = SpriteEditorUI.ConfigureField(new Toggle(label) { value = value, tooltip = tooltip });
            field.RegisterValueChangedCallback(evt => { write(evt.newValue); PostFxSettingsChanged(); });
            root.Add(field); return field;
        }

        private void PostFxSettingsChanged()
        {
            postFxDirty = true;
            RefreshPostFxPanel();
        }

        private void RefreshPostFxPanel()
        {
            bool brushAvailable = previewTool == PreviewTool.Brush;
            if (!brushAvailable) brushesExpanded = false;
            if (brushesExpanded) postFxExpanded = false;
            postFxOverlay?.EnableInClassList("sprite-editor-post-fx-overlay--hidden", !postFxEnabled && !brushAvailable && !uvEnabled);
            RefreshUvPanel();
            brushTab?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !brushAvailable);
            if (brushTab != null) brushTab.text = brushesExpanded ? "›" : "‹";
            brushDrawer?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !brushAvailable || !brushesExpanded);
            SetBrushStrokePreviewActive(brushAvailable && brushesExpanded);
            postFxTab?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !postFxEnabled);
            if (postFxTab != null) postFxTab.text = postFxExpanded ? "›" : "‹";
            postFxDrawer?.EnableInClassList("sprite-editor-post-fx-drawer--hidden", !postFxEnabled || !postFxExpanded);
            postFxButton?.EnableInClassList("sprite-editor-channel-button--enabled", postFxEnabled);
            refreshPostFxFields?.Invoke();
            if (postFxStatus != null && !string.IsNullOrEmpty(postFxMessage))
            {
                postFxStatus.text = postFxMessage;
                postFxStatus.messageType = postFxFailed ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            }
        }

        private void UpdatePostFx()
        {
            if (!postFxEnabled || previewTexture == null || EditorApplication.timeSinceStartup < nextPostFxCheck) return;
            nextPostFxCheck = EditorApplication.timeSinceStartup + (postFxSettings.animate ? .125 : .25);
            try
            {
                EnsurePostFxBackend();
                int hash = postFxBackend?.StateHash(postFxSettings) ?? 0;
                if (!postFxDirty && !postFxSettings.animate && hash == postFxStateHash) return;
                postFxStateHash = hash;
                RenderPostFx();
            }
            catch (Exception exception) { SetPostFxFailure(exception.Message); postFxDirty = false; }
            UpdateChannelPreview();
            UpdateToolkitPreviewPresentation();
        }

        private void EnsurePostFxBackend()
        {
            if (postFxBackend != null && !postFxBackend.IsAvailable) { postFxBackend.Dispose(); postFxBackend = null; postFxValid = false; postFxDirty = true; }
            if (postFxBackend == null)
            {
                postFxBackend = PostFxPreviewBackend.Create();
                if (postFxBackend != null) postFxDirty = true;
            }
        }

        private void RenderPostFx()
        {
            postFxValid = false;
            postFxDirty = false;
            if (!postFxEnabled || previewTexture == null) return;
            try
            {
                EnsurePostFxBackend();
                if (postFxBackend == null) throw new InvalidOperationException("No Post FX adapter for the active pipeline. URP 17.x is supported; other pipelines need an adapter. The original preview is shown.");
                if (postFxTexture == null || postFxTexture.width != previewTexture.width || postFxTexture.height != previewTexture.height)
                {
                    ReleasePostFxTexture();
                    postFxTexture = new RenderTexture(previewTexture.width, previewTexture.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { name = "WhimTex Post FX Preview", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    postFxTexture.Create();
                }
                postFxTexture.filterMode = previewTexture.filterMode;
                float zoom = toolkitPreviewCanvas != null ? toolkitPreviewCanvas.PixelScale : 1f;
                var canvasSize = compositor != null ? new Vector2(compositor.width, compositor.height) : new Vector2(previewTexture.width, previewTexture.height);
                postFxMessage = postFxBackend.Render(new PostFxPreviewRequest(postFxSettings, previewTexture, SpriteEditorUserSettings.PostFxBackground, zoom, canvasSize), postFxTexture);
                postFxValid = true;
                postFxFailed = false;
                postFxDirty = false;
            }
            catch (Exception exception) { SetPostFxFailure(exception.Message); }
            RefreshPostFxPanel();
        }

        private void SetPostFxFailure(string message)
        {
            postFxValid = false;
            postFxFailed = true;
            postFxMessage = "Post FX: " + message + "\nUnprocessed preview is shown; document and export are unchanged.";
            RefreshPostFxPanel();
        }

        private void ReleasePostFxTexture()
        {
            postFxValid = false;
            if (postFxTexture != null) { postFxTexture.Release(); DestroyImmediate(postFxTexture); }
            postFxTexture = null;
        }

        private void ReleasePostFx()
        {
            ReleasePostFxTexture();
            postFxBackend?.Dispose(); postFxBackend = null;
            postFxDirty = true;
        }
    }
}
