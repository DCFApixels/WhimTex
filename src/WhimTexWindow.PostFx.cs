using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [SerializeField] private PostFxPreviewSettings postFxSettings = new PostFxPreviewSettings();
        [SerializeField] private bool postFxEnabled;
        [SerializeField] private bool postFxExpanded;
        private PostFxPreviewBackend postFxBackend;
        private RenderTexture postFxTexture;
        private bool postFxValid;
        private bool postFxDirty = true;
        private int postFxStateHash;
        private double nextPostFxCheck;
        private VisualElement postFxOverlay, postFxDrawer, postFxDrawerContent;
        private Button postFxTab;
        private Toggle postFxEnabledToggle;
        private HelpBox postFxStatus;
        private ColorField postFxBackgroundField;
        private Action refreshPostFxFields;
        private string postFxMessage;
        private bool postFxFailed;

        private Texture CanvasPresentationSource => postFxEnabled && postFxValid && postFxTexture != null ? postFxTexture : canvasTexture;

        private VisualElement BuildCanvasViewWorkspace(VisualElement canvas)
        {
            postFxSettings ??= new PostFxPreviewSettings();
            var workspace = new VisualElement { name = "canvasViewWorkspace" };
            workspace.AddToClassList("whimtex-canvas-view-workspace");
            var viewport = new VisualElement { name = "canvasViewViewport" };
            viewport.AddToClassList("whimtex-canvas-view-viewport");
            viewport.Add(canvas);
            workspace.Add(BuildCanvasNavigation(viewport));
            postFxOverlay = new VisualElement { name = "postFxOverlay", pickingMode = PickingMode.Ignore };
            postFxOverlay.AddToClassList("whimtex-post-fx-overlay");
            workspace.Add(postFxOverlay);
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.AddToClassList("whimtex-post-fx-panel");
            postFxOverlay.Add(panel);
            var tabs = new VisualElement { name = "canvasViewPanelToolbar" };
            tabs.AddToClassList("whimtex-tools");
            tabs.EnableInClassList("whimtex-tools--light", !EditorGUIUtility.isProSkin);
            tabs.AddToClassList("whimtex-canvas-view-drawer-tabs");
            workspace.Add(tabs);
            BuildBrushTab(tabs);
            BuildCommentTab(tabs);
            postFxTab = CreateCanvasDrawerTab("postFxTab",
                "Post FX — show or hide settings. Closing this panel keeps Post FX enabled.",
                new CanvasPreviewPanelIcon(CanvasPreviewPanelIcon.Kind.PostFx), () =>
            {
                postFxExpanded = !postFxExpanded;
                if (postFxExpanded) brushesExpanded = uvExpanded = commentsExpanded = false;
                RefreshPostFxPanel();
            });
            tabs.Add(postFxTab);
            BuildUvTab(tabs);
            BuildBrushDrawer(panel);
            BuildCommentDrawer(panel);
            BuildUvDrawer(panel);
            postFxDrawer = new VisualElement();
            postFxDrawer.AddToClassList("whimtex-post-fx-drawer");
            postFxEnabledToggle = AddCanvasDrawerToggleHeader(postFxDrawer, "Post FX", "postFxTitle",
                "postFxEnabled", postFxEnabled, "Enable Post FX preview. Does not affect painting, sampling or export.", SetPostFxEnabled);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            postFxDrawerContent = scroll;
            scroll.AddToClassList("whimtex-post-fx-settings");
            postFxDrawer.Add(scroll);
            panel.Add(postFxDrawer);
            BuildPostFxFields(scroll);
            RefreshPostFxPanel();
            return workspace;
        }

        private void SetPostFxEnabled(bool enabled)
        {
            postFxEnabled = enabled;
            if (!enabled) ReleasePostFx();
            else postFxDirty = true;
            RefreshPostFxPanel();
            if (enabled) RenderPostFx();
            UpdateChannelCanvas();
            UpdateToolkitCanvasPresentation();
        }

        private void BuildPostFxFields(VisualElement root)
        {
            root.Add(WhimTexUI.CreateHeading("Source"));
            AddPostFxEnum(root, "Source", postFxSettings.source, value => postFxSettings.source = value);
            var camera = WhimTexUI.ConfigureField(new ObjectField("Camera") { objectType = typeof(Camera), allowSceneObjects = true, value = postFxSettings.camera,
                tooltip = "An empty field uses the MainCamera-tagged camera." });
            camera.RegisterValueChangedCallback(evt => { postFxSettings.camera = evt.newValue as Camera; PostFxSettingsChanged(); });
            root.Add(camera);
            var profile = WhimTexUI.ConfigureField(new ObjectField("Profile") { objectType = typeof(ScriptableObject), allowSceneObjects = false, value = postFxSettings.profile });
            profile.RegisterValueChangedCallback(evt => { postFxSettings.profile = evt.newValue; PostFxSettingsChanged(); });
            root.Add(profile);
            AddPostFxToggle(root, "Animate", postFxSettings.animate, value => postFxSettings.animate = value,
                "Refresh animated effects at up to 8 fps. Off refreshes when source settings or image change.");
            root.Add(WhimTexUI.CreateHeading("Camera"));
            AddPostFxEnum(root, "Projection", postFxSettings.projection, value => postFxSettings.projection = value);
            var manual = new VisualElement(); root.Add(manual);
            var fov = AddPostFxFloat(manual, "Field of View", postFxSettings.fieldOfView, 1, 179, value => postFxSettings.fieldOfView = value);
            var ortho = AddPostFxFloat(manual, "Ortho Size", postFxSettings.orthographicSize, .001f, 100000, value => postFxSettings.orthographicSize = value);
            AddPostFxFloat(manual, "Near", postFxSettings.near, .001f, 100000, value => postFxSettings.near = value);
            AddPostFxFloat(manual, "Far", postFxSettings.far, .01f, 1000000, value => postFxSettings.far = value);
            root.Add(WhimTexUI.CreateHeading("Background"));
            var backgroundMode = WhimTexUI.ConfigureField(new EnumField("Mode", WhimTexUserSettings.PostFxBackgroundMode)
            {
                name = "postFxBackgroundMode",
                tooltip = "Shared with User Settings and all WhimTex windows."
            });
            backgroundMode.RegisterValueChangedCallback(evt => WhimTexUserSettings.PostFxBackgroundMode = (PostFxBackground)evt.newValue);
            root.Add(backgroundMode);
            postFxBackgroundField = WhimTexUI.ConfigureField(new ColorField("Color")
            {
                name = "postFxBackground",
                Range = WhimTexColorRange.StandardOnly,
                hdr = false,
                showAlpha = false,
                value = WhimTexUserSettings.PostFxBackground,
                tooltip = "Opaque fill behind the composition before Post FX. Shared with User Settings; original alpha is still used for depth. Does not affect document pixels or export."
            });
            postFxBackgroundField.RegisterValueChangedCallback(evt =>
            {
                WhimTexUserSettings.PostFxBackground = evt.newValue;
                postFxBackgroundField.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackground);
            });
            root.Add(postFxBackgroundField);
            var checkerInfo = new HelpBox("Uses checkerboard colors and cell size from User Settings. Cells are measured in canvas pixels and zoom with the image.", HelpBoxMessageType.Info);
            root.Add(checkerInfo);
            root.Add(WhimTexUI.CreateHeading("Depth"));
            AddPostFxEnum(root, "Mode", postFxSettings.depth, value => postFxSettings.depth = value);
            AddPostFxFloat(root, "Distance", postFxSettings.distance, .001f, 1000000, value => postFxSettings.distance = value);
            var range = AddPostFxFloat(root, "Depth Range", postFxSettings.depthRange, 0, 1000000, value => postFxSettings.depthRange = value);
            var threshold = WhimTexUI.ConfigureField(new Slider("Threshold", 0, 1) { value = postFxSettings.threshold, showInputField = true });
            threshold.RegisterValueChangedCallback(evt => { postFxSettings.threshold = Mathf.Clamp01(evt.newValue); PostFxSettingsChanged(); });
            root.Add(threshold);
            var invert = AddPostFxToggle(root, "Invert", postFxSettings.invert, value => postFxSettings.invert = value);
            AddPostFxToggle(root, "Link to Zoom", postFxSettings.linkDistanceToZoom, value => postFxSettings.linkDistanceToZoom = value,
                "Simulate distance changes when zooming the canvas. Otherwise zoom only magnifies the processed result.");
            postFxStatus = new HelpBox("Preview only. Alpha Height creates relief; Alpha Mask places pixels below Threshold at the far plane.", HelpBoxMessageType.Info);
            root.Add(postFxStatus);
            root.Add(new Button(() => { postFxDirty = true; nextPostFxCheck = 0; }) { text = "Refresh Post FX" });
            refreshPostFxFields = () =>
            {
                backgroundMode.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackgroundMode);
                bool checkerBackground = WhimTexUserSettings.PostFxBackgroundMode == PostFxBackground.Checkerboard;
                postFxBackgroundField.EnableInClassList("whimtex-post-fx-field--hidden", checkerBackground);
                checkerInfo.EnableInClassList("whimtex-post-fx-field--hidden", !checkerBackground);
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
            var field = WhimTexUI.ConfigureField(new EnumField(label, value));
            field.RegisterValueChangedCallback(evt => { write((T)evt.newValue); PostFxSettingsChanged(); });
            root.Add(field);
        }

        private VisualElement AddPostFxFloat(VisualElement root, string label, float value, float min, float max, Action<float> write)
        {
            var field = WhimTexUI.ConfigureField(new FloatField(label) { value = value, isDelayed = true });
            field.RegisterValueChangedCallback(evt =>
            {
                float next = float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue) ? min : Mathf.Clamp(evt.newValue, min, max);
                field.SetValueWithoutNotify(next); write(next); PostFxSettingsChanged();
            });
            root.Add(field); return field;
        }

        private VisualElement AddPostFxToggle(VisualElement root, string label, bool value, Action<bool> write, string tooltip = null)
        {
            var field = WhimTexUI.ConfigureField(new Toggle(label) { value = value, tooltip = tooltip });
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
            bool brushAvailable = canvasTool == CanvasTool.Brush;
            if (commentsExpanded) brushesExpanded = postFxExpanded = uvExpanded = false;
            else if (brushesExpanded) postFxExpanded = uvExpanded = false;
            else if (postFxExpanded) uvExpanded = false;
            postFxOverlay?.EnableInClassList("whimtex-post-fx-overlay--hidden", !brushesExpanded && !commentsExpanded && !postFxExpanded && !uvExpanded);
            RefreshCanvasDrawerTab(commentTab, commentsExpanded);
            commentDrawer?.EnableInClassList("whimtex-post-fx-drawer--hidden", !commentsExpanded);
            RefreshUvPanel();
            RefreshCanvasDrawerTab(brushTab, brushesExpanded);
            brushDrawer?.EnableInClassList("whimtex-post-fx-drawer--hidden", !brushesExpanded);
            brushDrawerContent?.SetEnabled(brushAvailable);
            selectBrushButton?.EnableInClassList("whimtex-hidden", brushAvailable);
            SetBrushStrokePreviewActive(brushAvailable && brushesExpanded);
            RefreshCanvasDrawerTab(postFxTab, postFxExpanded);
            postFxDrawer?.EnableInClassList("whimtex-post-fx-drawer--hidden", !postFxExpanded);
            postFxEnabledToggle?.SetValueWithoutNotify(postFxEnabled);
            if (commentOverlay != null && commentOverlay.ClassListContains("whimtex-hidden") == CanvasCommentsVisible)
                RefreshCanvasComments();
            refreshPostFxFields?.Invoke();
            if (postFxStatus != null && !string.IsNullOrEmpty(postFxMessage))
            {
                postFxStatus.text = postFxMessage;
                postFxStatus.messageType = postFxFailed ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            }
        }

        private void UpdatePostFx()
        {
            if (!postFxEnabled || canvasTexture == null || EditorApplication.timeSinceStartup < nextPostFxCheck) return;
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
            UpdateChannelCanvas();
            UpdateToolkitCanvasPresentation();
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
            if (!postFxEnabled || canvasTexture == null) return;
            try
            {
                EnsurePostFxBackend();
                if (postFxBackend == null) throw new InvalidOperationException("No Post FX adapter for the active pipeline. URP 17.x is supported; other pipelines need an adapter. The original preview is shown.");
                if (postFxTexture == null || postFxTexture.width != canvasTexture.width || postFxTexture.height != canvasTexture.height)
                {
                    ReleasePostFxTexture();
                    postFxTexture = new RenderTexture(canvasTexture.width, canvasTexture.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                        { name = "WhimTex Post FX Preview", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    postFxTexture.Create();
                }
                postFxTexture.filterMode = canvasTexture.filterMode;
                float zoom = toolkitCanvas != null ? toolkitCanvas.PixelScale : 1f;
                var canvasSize = activeDocument != null ? new Vector2(activeDocument.width, activeDocument.height) : new Vector2(canvasTexture.width, canvasTexture.height);
                postFxSettings.backgroundMode = WhimTexUserSettings.PostFxBackgroundMode;
                postFxMessage = postFxBackend.Render(new PostFxPreviewRequest(postFxSettings, canvasTexture, WhimTexUserSettings.PostFxBackground, zoom, canvasSize), postFxTexture);
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
