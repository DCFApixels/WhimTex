using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private const float ToolkitPreviewHeaderRowHeight = 24f;
        private const float ToolkitLayerRowHeight = 26f;
        private const float ToolkitLayerIndent = 14f;

        [NonSerialized] private VisualElement toolkitPreviewPane;
        [NonSerialized] private VisualElement toolkitPreviewHeader;
        [NonSerialized] private VisualElement toolkitCanvasToolbar;
        [NonSerialized] private VisualElement toolkitPreviewActions;
        [NonSerialized] private VisualElement toolkitDocumentRoot;
        [NonSerialized] private ScrollView toolkitSettingsScroll;
        [NonSerialized] private VisualElement toolkitLayerFooter;
        [NonSerialized] private SpritePreviewElement toolkitPreviewCanvas;
        [NonSerialized] private PreviewFooterHintLabel toolkitPreviewFooter;
        [NonSerialized] private VisualElement toolkitPreviewErrorRoot;
        [NonSerialized] private ObjectField toolkitDocumentField;
        [NonSerialized] private VisualElement toolkitLayerHierarchyRoot;
        [NonSerialized] private VisualElement toolkitLayerEndDropZone;
        [NonSerialized] private VisualElement activeDropElement;
        [NonSerialized] private StyleLength activeDropMarginLeft;
        [NonSerialized] private LayerDragManipulator activeLayerDrag;
        [NonSerialized] private int paintingPointerId = -1;
        [NonSerialized] private bool previewPointerInside;
        [NonSerialized] private bool previewPointerAlt;
        [NonSerialized] private bool previewPointerControl;
        [NonSerialized] private Vector2 previewPointerPosition;
        [NonSerialized] private bool applyingToolkitChange;
        [NonSerialized] private bool rebuildingToolkit;
        [NonSerialized] private bool toolkitRefreshRequested;
        [NonSerialized] private TextureCompositor toolkitBoundDocument;
        [NonSerialized] private bool toolkitHeaderBuilt;
        [NonSerialized] private Button toolkitSaveButton;
        [NonSerialized] private Button toolkitSaveAsButton;
        [NonSerialized] private HelpBox toolkitPreviewError;
        private readonly WhimTexUI.ValueBindings toolkitSettingsBindings = new WhimTexUI.ValueBindings();
        private readonly WhimTexUI.ValueBindings toolkitHeaderBindings = new WhimTexUI.ValueBindings();
        private readonly WhimTexUI.ValueBindings toolkitLayerBindings = new WhimTexUI.ValueBindings();
        private readonly List<LayerTreeEntry> toolkitLayerTree = new List<LayerTreeEntry>();
        private readonly List<LayerTreeEntry> toolkitNextLayerTree = new List<LayerTreeEntry>();

        private readonly struct LayerTreeEntry : IEquatable<LayerTreeEntry>
        {
            public readonly List<Layer> Container;
            public readonly Layer Layer;
            public readonly LayerBehaviour Behaviour;
            public readonly int Index;
            public readonly int Depth;

            public LayerTreeEntry(List<Layer> container, Layer layer, int index, int depth)
            {
                Container = container;
                Layer = layer;
                Behaviour = layer?.Behaviour;
                Index = index;
                Depth = depth;
            }

            public bool Equals(LayerTreeEntry other) =>
                ReferenceEquals(Container, other.Container) && ReferenceEquals(Layer, other.Layer) &&
                ReferenceEquals(Behaviour, other.Behaviour) && Index == other.Index && Depth == other.Depth;
        }

        public void CreateGUI()
        {
            ClearPreviewPointerCursor();
            CancelPreviewEyedropper();
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            if (compositor == null)
                SetCompositor(CreateTemporaryCompositor());

            VisualElement root = rootVisualElement;
            InstallLayerDragGhost();
            root.UnregisterCallback<KeyDownEvent>(OnToolkitKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyUpEvent>(OnToolkitKeyUp, TrickleDown.TrickleDown);
            root.UnregisterCallback<DragExitedEvent>(OnToolkitDragExited);
            root.UnregisterCallback<DragUpdatedEvent>(OnBrushPresetDragUpdated, TrickleDown.TrickleDown);
            root.UnregisterCallback<DragPerformEvent>(OnBrushPresetDragPerform, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerDownEvent>(OnOpacityPointerDown, TrickleDown.TrickleDown);
            ResetOpacityEntry();
            root.Clear();
            WhimTexUI.ApplyWindowStyles(root);
            toolkitBoundDocument = null;
            toolkitHeaderBuilt = false;
            toolkitSettingsBindings.Clear();
            toolkitHeaderBindings.Clear();
            toolkitLayerBindings.Clear();
            ResetToolkitLayerInspector();
            toolkitLayerTree.Clear();
            root.focusable = true;
            root.style.flexGrow = 1f;
            root.style.backgroundColor = WhimTexUI.PanelColor;
            root.RegisterCallback<KeyDownEvent>(OnToolkitKeyDown, TrickleDown.TrickleDown);
            RegisterAreaSelectionCommands(root);
            root.RegisterCallback<KeyUpEvent>(OnToolkitKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<DragExitedEvent>(OnToolkitDragExited);
            root.RegisterCallback<DragUpdatedEvent>(OnBrushPresetDragUpdated, TrickleDown.TrickleDown);
            root.RegisterCallback<DragPerformEvent>(OnBrushPresetDragPerform, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(OnOpacityPointerDown, TrickleDown.TrickleDown);

            toolkitDocumentRoot = new VisualElement();
            toolkitDocumentRoot.style.flexShrink = 0f;
            root.Add(toolkitDocumentRoot);

            VisualElement workspace = new VisualElement { name = "whimTexWorkspace" };
            workspace.AddToClassList("whimtex-workspace");
            root.Add(workspace);
            workspace.Add(BuildPreviewToolToolbar());

            if (settingsPaneWidth <= 0f)
                settingsPaneWidth = DefaultSettingsPaneWidth;
            TwoPaneSplitView split = new TwoPaneSplitView(
                1,
                Mathf.Max(SettingsPaneMinWidth, settingsPaneWidth),
                TwoPaneSplitViewOrientation.Horizontal);
            WhimTexUI.StyleSplitView(split);
            split.AddToClassList("whimtex-workspace-split");
            split.style.flexGrow = 1f;
            split.style.minHeight = 0f;
            workspace.Add(split);

            toolkitPreviewPane = BuildToolkitPreviewPane();
            toolkitPreviewPane.style.minWidth = PreviewPaneMinWidth;
            toolkitPreviewPane.style.flexGrow = 1f;
            split.Add(toolkitPreviewPane);

            VisualElement settingsPane = new VisualElement();
            settingsPane.style.minWidth = SettingsPaneMinWidth;
            settingsPane.style.minHeight = 0f;
            settingsPane.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.width >= SettingsPaneMinWidth)
                    settingsPaneWidth = evt.newRect.width;
            });
            settingsPane.AddToClassList("whimtex-settings-pane");
            settingsPane.EnableInClassList("whimtex-settings-pane--light", !EditorGUIUtility.isProSkin);
            split.Add(settingsPane);

            TwoPaneSplitView settingsSplit = new TwoPaneSplitView(
                0, Mathf.Max(100f, layerSettingsPaneHeight), TwoPaneSplitViewOrientation.Vertical);
            WhimTexUI.StyleSplitView(settingsSplit);
            settingsSplit.name = "layer-settings-split";
            settingsSplit.style.flexGrow = 1f;
            settingsSplit.style.minHeight = 0f;
            settingsPane.Add(settingsSplit);

            VisualElement layerSettingsPane = new VisualElement();
            layerSettingsPane.AddToClassList("whimtex-layer-settings-pane");
            var layerSettingsHeader = new VisualElement();
            layerSettingsHeader.AddToClassList("whimtex-pane-header");
            layerSettingsHeader.AddToClassList("whimtex-layer-settings-header");
            layerSettingsHeader.EnableInClassList("whimtex-pane-header--light", !EditorGUIUtility.isProSkin);
            toolkitLayerSettingsTitle = new Label("Layer Settings") { name = "selectedLayerTitle", enableRichText = false };
            toolkitLayerSettingsTitle.AddToClassList("whimtex-layer-settings-title");
            layerSettingsHeader.Add(toolkitLayerSettingsTitle);
            toolkitLayerGuidButton = new Button(() =>
            {
                Layer selected = GetSelectedLayer();
                if (selected != null)
                {
                    GUIUtility.systemCopyBuffer = selected.Id;
                    ShowNotification(new GUIContent("Layer GUID copied to clipboard."));
                }
            }) { name = "copyLayerGuid", text = "GUID" };
            toolkitLayerGuidButton.AddToClassList("whimtex-layer-guid-copy");
            layerSettingsHeader.Add(toolkitLayerGuidButton);
            layerSettingsPane.Add(layerSettingsHeader);
            settingsSplit.Add(layerSettingsPane);

            toolkitLayerSettingsScroll = new ScrollView(ScrollViewMode.Vertical);
            toolkitLayerSettingsScroll.name = "selected-layer-settings";
            toolkitLayerSettingsScroll.AddToClassList("whimtex-layer-inspector");
            layerSettingsPane.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (evt.newRect.height >= 100f)
                    layerSettingsPaneHeight = evt.newRect.height;
            });
            layerSettingsPane.Add(toolkitLayerSettingsScroll);

            VisualElement layersPane = new VisualElement();
            layersPane.AddToClassList("whimtex-layers-pane");
            layersPane.Add(CreatePaneHeader("Layers", "layersTitle"));
            settingsSplit.Add(layersPane);

            VisualElement layerTableHeader = BuildLayerTableHeader();
            layersPane.Add(layerTableHeader);

            toolkitSettingsScroll = new ScrollView(ScrollViewMode.Vertical);
            toolkitSettingsScroll.name = "layer-list";
            toolkitSettingsScroll.AddToClassList("whimtex-layer-list");
            toolkitSettingsScroll.EnableInClassList("whimtex-layer-list--light", !EditorGUIUtility.isProSkin);
            toolkitSettingsScroll.AddManipulator(new ProjectTextureDropManipulator(this));
            toolkitSettingsScroll.AddManipulator(layerDragAutoScroll = new LayerDragAutoScrollManipulator(this, toolkitSettingsScroll));
            toolkitSettingsScroll.contentViewport.AddManipulator(new LayerListEndDropManipulator(this));
            toolkitSettingsScroll.style.minHeight = 0f;
            toolkitSettingsScroll.style.flexGrow = 1f;
            layersPane.Add(toolkitSettingsScroll);
            ScrollView layerList = toolkitSettingsScroll;
            layerList.contentViewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float scrollbarWidth = Mathf.Max(0f, layerList.contentRect.width - evt.newRect.width);
                if (!Mathf.Approximately(layerTableHeader.resolvedStyle.marginRight, scrollbarWidth))
                    layerTableHeader.style.marginRight = scrollbarWidth;
            });

            toolkitLayerFooter = new VisualElement { name = "layersFooter" };
            toolkitLayerFooter.AddToClassList("whimtex-layers-footer");
            toolkitLayerFooter.EnableInClassList("whimtex-layers-footer--light", !EditorGUIUtility.isProSkin);
            layersPane.Add(toolkitLayerFooter);

            RefreshToolkitInterface();
        }

        private void OnToolkitDragExited(DragExitedEvent evt)
        {
            ClearToolkitDropIndicator();
            ClearLayerDragData();
        }

        private VisualElement BuildToolkitPreviewPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.backgroundColor = WhimTexUI.PanelColor;

            toolkitCanvasToolbar = WhimTexUI.CreateToolbar();
            toolkitCanvasToolbar.AddToClassList("whimtex-canvas-toolbar");
            pane.Add(toolkitCanvasToolbar);

            toolkitPreviewHeader = new VisualElement();
            toolkitPreviewHeader.style.flexShrink = 0f;
            pane.Add(toolkitPreviewHeader);

            toolkitPreviewErrorRoot = new VisualElement();
            toolkitPreviewErrorRoot.style.flexShrink = 0f;
            toolkitPreviewErrorRoot.style.paddingLeft = PanePadding;
            toolkitPreviewErrorRoot.style.paddingRight = PanePadding;
            pane.Add(toolkitPreviewErrorRoot);
            toolkitPreviewError = WhimTexUI.AddHelpBox(toolkitPreviewErrorRoot, string.Empty, HelpBoxMessageType.Error);
            toolkitPreviewError.style.display = DisplayStyle.None;

            toolkitPreviewCanvas = new SpritePreviewElement(previewViewport);
            toolkitPreviewCanvas.AddManipulator(new ProjectTextureDropManipulator(this, prependToRoot: true));
            toolkitPreviewCanvas.style.flexGrow = 1f;
            BuildGradientCanvasTool();
            BuildPreviewGuides();
            BuildPreviewZoomTool();
            BuildPreviewTransformTool();
            previewEyedropper = new PreviewEyedropperManipulator(this);
            toolkitPreviewCanvas.AddManipulator(previewEyedropper);
            BuildUvOverlay();
            BuildAreaSelectionTools();
            BuildShapeTool();
            toolkitPreviewCanvas.RegisterCallback<PointerDownEvent>(OnPreviewPointerDown);
            toolkitPreviewCanvas.RegisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
            toolkitPreviewCanvas.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp);
            toolkitPreviewCanvas.RegisterCallback<PointerEnterEvent>(OnPreviewPointerEnter);
            toolkitPreviewCanvas.RegisterCallback<PointerLeaveEvent>(OnPreviewPointerLeave);
            toolkitPreviewCanvas.RegisterCallback<PointerCaptureOutEvent>(OnPreviewPointerCaptureOut);
            pane.Add(BuildPostFxPreview(toolkitPreviewCanvas));

            pane.Add(BuildPreviewFooter());
            return pane;
        }

        private void RefreshToolkitInterface(bool forceValues = false)
        {
            if (rootVisualElement == null || toolkitDocumentRoot == null || rebuildingToolkit)
                return;

            rebuildingToolkit = true;
            try
            {
                toolkitRefreshRequested = false;
                NormalizeLayerSelection();
                if (toolkitBoundDocument != compositor)
                {
                    toolkitBoundDocument = compositor;
                    toolkitSettingsBindings.Clear();
                    toolkitLayerBindings.Clear();
                    toolkitLayerTree.Clear();
                    toolkitHeaderBuilt = false;
                    BuildToolkitDocumentArea();
                    BuildToolkitCanvasToolbar();
                    BuildToolkitSettings();
                }
                toolkitSettingsBindings.Refresh(forceValues);
                RefreshToolkitLayerHierarchy(forceValues);
                RefreshToolkitLayerInspector(forceValues);
                RefreshToolkitPreviewHeader(forceValues);
                UpdateToolkitPreviewPresentation();
            }
            finally
            {
                rebuildingToolkit = false;
            }
        }

        private void BuildToolkitDocumentArea()
        {
            toolkitDocumentRoot.Clear();
            VisualElement toolbar = WhimTexUI.CreateToolbar();
            toolbar.style.backgroundColor = StyleKeyword.Null;
            toolbar.style.borderBottomWidth = StyleKeyword.Null;
            toolbar.AddToClassList("whimtex-document-header");
            toolbar.EnableInClassList("whimtex-document-header--light", !EditorGUIUtility.isProSkin);

            var newDocument = WhimTexUI.CreateToolbarButton("New", () => OpenNewDocument(), 46f);
            newDocument.tooltip = "Create a new document in a separate WhimTex tab. The current document stays open.";
            toolbar.Add(newDocument);

            toolkitDocumentField = new ObjectField
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false
            };
            toolkitDocumentField.style.flexGrow = 1f;
            toolkitDocumentField.style.minWidth = 140f;
            toolkitDocumentField.tooltip = "A WhimTex document or its generated texture/sprite. Double-click the saved asset in Project to edit its layers.";
            toolkitSettingsBindings.Track(toolkitDocumentField,
                () => compositor.OutputTexture != null ? (UnityEngine.Object)compositor.OutputTexture : compositor);
            toolkitDocumentField.RegisterValueChangedCallback(evt =>
            {
                TextureCompositor selected = TextureCompositor.FindDocument(evt.newValue);
                if (selected == null || selected == compositor)
                {
                    toolkitDocumentField.SetValueWithoutNotify(compositor.OutputTexture != null ? (UnityEngine.Object)compositor.OutputTexture : compositor);
                    return;
                }

                if (ResolveUnsavedTemporaryDocument())
                {
                    SetCompositor(selected);
                }
                else
                {
                    toolkitDocumentField.SetValueWithoutNotify(compositor.OutputTexture != null ? (UnityEngine.Object)compositor.OutputTexture : compositor);
                }
            });
            toolbar.Add(toolkitDocumentField);
            toolkitSaveButton = WhimTexUI.CreateToolbarButton("Save", SaveAsset, 46f);
            toolkitSaveButton.tooltip = "Save layers and update the embedded full-resolution texture and sprite (Ctrl+S).";
            toolbar.Add(toolkitSaveButton);
            toolkitSaveAsButton = WhimTexUI.CreateToolbarButton("Save As", () =>
            {
                SaveAsAsset();
            }, 82f);
            toolbar.Add(toolkitSaveAsButton);
            toolkitSettingsBindings.Add(RefreshDocumentSaveControls);
            RefreshDocumentSaveControls();
            Button export = WhimTexUI.CreateToolbarButton("Export", ShowExportMenu, 64f);
            export.tooltip = "Export the flattened texture as PNG, JPEG, TGA, EXR, or a Unity Texture2D asset.";
            toolbar.Add(export);
            Button userSettings = WhimTexUI.CreateToolbarButton(string.Empty, WhimTexUserSettingsWindow.Open, 26f);
            userSettings.name = "userSettingsButton";
            userSettings.tooltip = "User Settings";
            userSettings.AddToClassList("whimtex-user-settings-button");
            userSettings.Add(new LayerActionIcon(LayerActionIcon.Kind.Settings));
            toolbar.Add(userSettings);
            toolkitDocumentRoot.Add(toolbar);

            VisualElement separator = new VisualElement
            {
                name = "documentHeaderSeparator",
                pickingMode = PickingMode.Ignore
            };
            separator.AddToClassList("whimtex-document-separator");
            separator.EnableInClassList("whimtex-document-separator--light", !EditorGUIUtility.isProSkin);
            toolkitDocumentRoot.Add(separator);
        }

        private void RefreshDocumentSaveControls()
        {
            bool saved = compositor != null && AssetDatabase.Contains(compositor);
            toolkitSaveButton?.SetEnabled(saved && (HasDocumentChanges() || paintingLayer != null ||
                previewTransformManipulator != null && previewTransformManipulator.IsDragging));
            if (toolkitSaveAsButton == null) return;
            toolkitSaveAsButton.text = compositor != null && !saved ? "⚠ Save As" : "Save As";
            toolkitSaveAsButton.tooltip = saved
                ? "Save a copy of this document to a new file."
                : "This document has no saved file. Use Save As to keep its layers.";
        }

        private void BuildToolkitCanvasToolbar()
        {
            toolkitCanvasToolbar.Clear();
            Label title = new Label("Canvas");
            title.AddToClassList("whimtex-canvas-title");
            toolkitCanvasToolbar.Add(title);

            IntegerField width = new IntegerField("W") { isDelayed = true };
            width.AddToClassList("whimtex-canvas-size");
            width.tooltip = "Canvas width in pixels. Press Enter or leave the field to apply.";
            width.SetValueWithoutNotify(compositor.width);
            toolkitSettingsBindings.Track(width, () => compositor.width);
            width.RegisterValueChangedCallback(evt =>
            {
                int value = Mathf.Clamp(evt.newValue, 1, 16384);
                width.SetValueWithoutNotify(value);
                if (compositor.width != value)
                    ApplyToolkitChange("Change Sprite Canvas Width", () => compositor.width = value);
            });
            toolkitCanvasToolbar.Add(width);
            toolkitCanvasToolbar.Add(new Label("×") { pickingMode = PickingMode.Ignore });

            IntegerField height = new IntegerField("H") { isDelayed = true };
            height.AddToClassList("whimtex-canvas-size");
            height.tooltip = "Canvas height in pixels. Press Enter or leave the field to apply.";
            height.SetValueWithoutNotify(compositor.height);
            toolkitSettingsBindings.Track(height, () => compositor.height);
            height.RegisterValueChangedCallback(evt =>
            {
                int value = Mathf.Clamp(evt.newValue, 1, 16384);
                height.SetValueWithoutNotify(value);
                if (compositor.height != value)
                    ApplyToolkitChange("Change Sprite Canvas Height", () => compositor.height = value);
            });
            toolkitCanvasToolbar.Add(height);
            var filter = new EnumField("Filter", compositor.outputFilter) { name = "canvasOutputFilter" };
            filter.AddToClassList("whimtex-canvas-filter");
            filter.tooltip = "Final image filtering, saved with the document. Point keeps pixels sharp; Bilinear smooths them. Trilinear blends mip levels when available (this does not generate mipmaps). Pencil temporarily uses Point in the preview only.";
            toolkitSettingsBindings.Track(filter, () => (Enum)compositor.outputFilter);
            filter.RegisterValueChangedCallback(evt =>
            {
                var value = (FilterMode)evt.newValue;
                if (compositor.outputFilter != value)
                    ApplyToolkitChange("Change Canvas Filter", () => compositor.outputFilter = value);
            });
            toolkitCanvasToolbar.Add(filter);
            AddTiledPreviewControl();

            toolkitPreviewActions = new VisualElement();
            toolkitPreviewActions.AddToClassList("whimtex-preview-actions");
            toolkitCanvasToolbar.Add(toolkitPreviewActions);
        }

        private void BuildToolkitSettings()
        {
            toolkitSettingsScroll.Clear();

            toolkitLayerHierarchyRoot = new VisualElement();
            toolkitLayerHierarchyRoot.style.flexShrink = 0f;
            toolkitSettingsScroll.Add(toolkitLayerHierarchyRoot);

            toolkitSettingsScroll.scrollOffset = scrollPosition;
            BuildToolkitLayerFooter();
        }

        private VisualElement BuildLayerTableHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("whimtex-layer-table-header");
            var showAll = new Button(() =>
            {
                if (compositor == null) return;
                FinishPreviewTransform();
                FinishPaintingStroke();
                ApplyToolkitChange("Show All Layers", () => ShowAllLayers(compositor.layers));
            }) { tooltip = "Show all layers and groups" };
            showAll.AddToClassList("whimtex-layer-enabled");
            showAll.Add(new LayerActionIcon(LayerActionIcon.Kind.Eye));
            header.Add(showAll);
            var name = new Label("Name");
            name.AddToClassList("whimtex-layer-name-cell");
            header.Add(name);
            var alpha = new VisualElement { tooltip = "Opacity" };
            alpha.AddToClassList("whimtex-layer-opacity");
            alpha.Add(new LayerActionIcon(LayerActionIcon.Kind.Alpha));
            header.Add(alpha);
            var blend = new Label("Blend");
            blend.AddToClassList("whimtex-layer-blend");
            header.Add(blend);
            var menuSpace = new VisualElement { pickingMode = PickingMode.Ignore };
            menuSpace.AddToClassList("whimtex-layer-menu-space");
            header.Add(menuSpace);
            return header;
        }

        private static void ShowAllLayers(List<Layer> layers)
        {
            foreach (Layer layer in layers)
            {
                if (layer == null) continue;
                layer.enabled = true;
                if (layer?.AsGroup() is Layer group) ShowAllLayers(group.layers);
            }
        }

        private static Label CreatePaneHeader(string text, string name)
        {
            Label header = new Label(text) { name = name, enableRichText = false };
            header.AddToClassList("whimtex-pane-header");
            header.EnableInClassList("whimtex-pane-header--light", !EditorGUIUtility.isProSkin);
            return header;
        }

        private void BuildToolkitLayerFooter()
        {
            toolkitLayerFooter.Clear();
            Button add = CreateLayerActionButton(
                LayerActionIcon.Kind.Add, "Add layer. Drop layers here to duplicate them.", ShowAddMenuForSelection);
            Button drawing = CreateLayerActionButton(
                LayerActionIcon.Kind.AddDrawing,
                "New Drawing Layer. Drop layers or groups here to create a merged Drawing copy; originals are kept.",
                AddDrawingLayerForSelection);
            drawing.name = "whimTexAddDrawingLayer";
            Button group = CreateLayerActionButton(
                LayerActionIcon.Kind.Group, "Group selected layers", GroupSelectedLayer);
            Button delete = CreateLayerActionButton(
                LayerActionIcon.Kind.Delete, "Delete selected layers", DeleteSelectedLayers);
            drawing.AddToClassList("whimtex-layer-action--separated");
            group.AddToClassList("whimtex-layer-action--separated");
            delete.AddToClassList("whimtex-layer-action--separated");
            group.tooltip = "Group selected layers. You can also drop layers here.";
            delete.tooltip = "Delete selected layers. You can also drop layers here.";
            add.AddManipulator(new LayerFooterDropManipulator(this, LayerFooterDropAction.Duplicate));
            drawing.AddManipulator(new LayerFooterDropManipulator(this, LayerFooterDropAction.MergeCopy));
            group.AddManipulator(new LayerFooterDropManipulator(this, LayerFooterDropAction.Group));
            delete.AddManipulator(new LayerFooterDropManipulator(this, LayerFooterDropAction.Delete));
            toolkitLayerFooter.Add(add);
            toolkitLayerFooter.Add(drawing);
            toolkitLayerFooter.Add(group);
            toolkitLayerFooter.Add(delete);
            toolkitSettingsBindings.Add(() =>
            {
                bool hasTarget = GetSelectedLayer() != null || GetDraggedLayer() != null;
                group.SetEnabled(hasTarget);
                delete.SetEnabled(hasTarget);
            });
        }

        private static Button CreateLayerActionButton(LayerActionIcon.Kind icon, string tooltip, Action clicked)
        {
            Button button = new Button(clicked) { tooltip = tooltip };
            button.AddToClassList("whimtex-layer-action");
            button.Add(new LayerActionIcon(icon));
            return button;
        }

        private void RefreshToolkitLayerHierarchy(bool forceValues = false)
        {
            if (toolkitLayerHierarchyRoot == null)
                return;

            toolkitNextLayerTree.Clear();
            CollectToolkitLayerTree(compositor?.layers, 0);
            bool structureChanged = toolkitLayerTree.Count != toolkitNextLayerTree.Count || toolkitLayerHierarchyRoot.childCount == 0;
            for (int i = 0; !structureChanged && i < toolkitLayerTree.Count; i++)
                structureChanged = !toolkitLayerTree[i].Equals(toolkitNextLayerTree[i]);
            if (!structureChanged)
            {
                toolkitLayerBindings.Refresh(forceValues);
                return;
            }

            ClearToolkitDropIndicator();
            toolkitLayerBindings.Clear();
            toolkitInspectorEffectTarget?.Invalidate();
            toolkitLayerTree.Clear();
            toolkitLayerTree.AddRange(toolkitNextLayerTree);
            compositor?.RefreshThumbnailStructure();
            toolkitLayerHierarchyRoot.Clear();
            toolkitLayerEndDropZone = null;
            if (compositor == null || compositor.layers.Count == 0)
            {
                WhimTexUI.AddHelpBox(
                    toolkitLayerHierarchyRoot,
                    "Add a layer or group to start composing.",
                    HelpBoxMessageType.Info);
                return;
            }

            AddToolkitLayerRows(compositor.layers, 0, toolkitLayerHierarchyRoot);
            toolkitLayerBindings.Refresh(forceValues);
        }

        private void CollectToolkitLayerTree(List<Layer> layers, int depth)
        {
            if (layers == null)
                return;
            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                toolkitNextLayerTree.Add(new LayerTreeEntry(layers, layer, i, depth));
                if (layer?.AsGroup() is Layer group && GetGroupExpanded(group))
                    CollectToolkitLayerTree(group.layers, depth + 1);
            }
            toolkitNextLayerTree.Add(new LayerTreeEntry(layers, null, -1, depth));
        }

        private void AddToolkitLayerRows(List<Layer> layers, int depth, VisualElement root)
        {
            if (layers == null)
                return;

            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                if (layer == null)
                {
                    root.Add(BuildMissingLayerRow(layers, i, depth));
                    continue;
                }

                VisualElement row = layer?.AsGroup() is Layer group
                    ? BuildToolkitGroupRow(group, layers, i, depth)
                    : BuildToolkitLeafRow(layer, layers, i, depth);
                root.Add(row);

                if (layer?.AsGroup() is Layer expandedGroup && GetGroupExpanded(expandedGroup))
                    AddToolkitLayerRows(expandedGroup.layers, depth + 1, root);
            }

            if (depth == 0)
                root.Add(toolkitLayerEndDropZone = BuildContainerEndDropZone(layers, depth));
        }

        private VisualElement CreateToolkitLayerRow(Layer layer, int depth)
        {
            VisualElement row = new VisualElement();
            row.userData = layer.Id;
            ApplyLayerSelectionStyle(row, layer.Id);
            VisualElement activeOutline = new VisualElement { pickingMode = PickingMode.Ignore };
            activeOutline.AddToClassList("whimtex-layer-active-outline");
            row.Add(activeOutline);
            var dropMarker = new VisualElement { pickingMode = PickingMode.Ignore };
            dropMarker.AddToClassList("whimtex-layer-drop-marker");
            row.Add(dropMarker);
            toolkitLayerBindings.Add(() =>
            {
                if (row != activeDropElement)
                    ApplyLayerSelectionStyle(row, layer.Id);
            });
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (TryOpenFileLayerDocument(row, layer, evt)) return;
                if (evt.button == 0 && IsFocusedLayerTextControl(FindLayerDragControl(row, evt.target as VisualElement)))
                    return;
                if (TrySelectLayerAlpha(row, layer, evt)) return;
                if (evt.button == 0 && evt.altKey && TryToggleClippingAtBoundary(row, layer, evt.position))
                {
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
                if (evt.button == 1)
                {
                    WhimTexUI.ConsumeEvent(evt);
                    FinishPreviewTransform();
                    FinishPaintingStroke();
                    Focus();
                    if (!IsLayerSelected(layer.Id))
                    {
                        SelectOnlyLayer(layer.Id);
                        RefreshToolkitInterface();
                    }
                    return;
                }
                if (evt.button != 0 || IsLayerDragArea(row, evt.target as VisualElement))
                    return;
                for (VisualElement field = evt.target as VisualElement; field != null && field != row; field = field.parent)
                {
                    if (!field.ClassListContains("whimtex-layer-multi-edit")) continue;
                    if (!IsLayerSelected(layer.Id))
                    {
                        FinishPreviewTransform();
                        FinishPaintingStroke();
                        SelectOnlyLayer(layer.Id);
                        RefreshToolkitInterface();
                    }
                    return;
                }
                if (evt.target is VisualElement menuTarget &&
                    menuTarget.ClassListContains("whimtex-layer-menu-button"))
                {
                    if (!IsLayerSelected(layer.Id)) SelectLayerFromPointer(layer, evt);
                    return;
                }
                SelectLayerFromPointer(layer, evt);
                if (evt.ctrlKey || evt.commandKey || evt.shiftKey)
                {
                    WhimTexUI.ConsumeEvent(evt);
                }
            }, TrickleDown.TrickleDown);
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 1) return;
                WhimTexUI.ConsumeEvent(evt);
                if (compositor != null && compositor.TryFindLayer(layer, out List<Layer> container, out int index))
                    ShowLayerContextMenu(layer, container, index);
            }, TrickleDown.TrickleDown);
            row.AddManipulator(new LayerDragManipulator(this, layer));
            return row;
        }

        private static bool IsLayerDragArea(VisualElement row, VisualElement element)
        {
            if (FindLayerDragControl(row, element) != null) return true;
            for (; element != null && element != row; element = element.parent)
            {
                if (element.ClassListContains("whimtex-group-foldout")) return true;
                if (element is Button || element.focusable || element.ClassListContains("unity-base-field"))
                    return false;
            }
            return element == row;
        }

        private static VisualElement FindLayerDragControl(VisualElement row, VisualElement element)
        {
            for (; element != null && element != row; element = element.parent)
                if (element.ClassListContains("whimtex-layer-name") ||
                    element.ClassListContains("whimtex-layer-opacity") ||
                    element.ClassListContains("whimtex-layer-enabled"))
                    return element;
            return null;
        }

        private static bool IsFocusedLayerTextControl(VisualElement control)
        {
            if (!(control is TextField) && !(control is FloatField)) return false;
            var focused = control.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == control || control.Contains(focused));
        }

        private VisualElement CreateLayerNameCell(VisualElement row, int depth, Layer layer)
        {
            var cell = new VisualElement();
            cell.AddToClassList("whimtex-layer-name-cell");
            cell.style.paddingLeft = depth * ToolkitLayerIndent;
            if (layer == null) { row.Add(cell); return cell; }
            var clipping = new VisualElement { pickingMode = PickingMode.Ignore };
            clipping.style.width = 12f;
            clipping.style.height = 18f;
            clipping.style.flexShrink = 0f;
            clipping.style.alignSelf = Align.Center;
            clipping.generateVisualContent += context =>
            {
                if (clipping.contentRect.width < 1f || clipping.contentRect.height < 1f) return;
                Painter2D painter = context.painter2D;
                painter.strokeColor = clipping.resolvedStyle.color;
                painter.lineWidth = 1.3f;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(new Vector2(10f, 7f));
                painter.LineTo(new Vector2(5f, 7f));
                painter.LineTo(new Vector2(5f, 16f));
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(new Vector2(2f, 13f));
                painter.LineTo(new Vector2(5f, 16f));
                painter.LineTo(new Vector2(8f, 13f));
                painter.Stroke();
            };
            cell.Add(clipping);
            toolkitLayerBindings.Add(() =>
            {
                clipping.style.display = layer.clippingMask ? DisplayStyle.Flex : DisplayStyle.None;
                Layer basis = compositor.GetClippingBase(layer);
                cell.tooltip = !layer.clippingMask ? "Ctrl-click the thumbnail to select layer alpha." : basis == null
                    ? "Clipping Mask: no base below this layer" : "Clipping Mask → " + basis.layerName;
            });
            row.Add(cell);
            return cell;
        }

        private bool TryToggleClippingAtBoundary(VisualElement row, Layer layer, Vector3 position)
        {
            if (compositor == null || !compositor.TryFindLayer(layer, out var container, out int index)) return false;
            float y = row.WorldToLocal(position).y;
            if (y >= row.layout.height - 3f && layer?.AsGroup() is Layer expanded &&
                GetGroupExpanded(expanded) && expanded.layers.Count > 0) return false;
            // Both sides of the boundary target its upper sibling. Group children
            // are separate containers, so a boundary never clips across a folder.
            int targetIndex = y <= 3f ? index - 1 : y >= row.layout.height - 3f ? index : -1;
            if (targetIndex < 0 || targetIndex + 1 >= container.Count) return false;
            Layer target = container[targetIndex];
            if (target == null || WhimTexApi.IsLayerContentLocked(compositor, target) || target?.Behaviour is ShaderProcessorLayerBehaviour || container[targetIndex + 1]?.Behaviour is ShaderProcessorLayerBehaviour) return false;
            ExecuteContextChange("Change Clipping Mask", () => target.clippingMask = !target.clippingMask);
            return true;
        }

        private void ToggleLayerGroup(Layer group)
        {
            groupExpansion[group.Id] = !GetGroupExpanded(group);
            RefreshToolkitLayerHierarchy();
        }

        private VisualElement BuildToolkitGroupRow(
            Layer group,
            List<Layer> container,
            int index,
            int depth)
        {
            VisualElement row = CreateToolkitLayerRow(group, depth);

            row.Add(CreateLayerVisibilityButton(group));
            VisualElement nameCell = CreateLayerNameCell(row, depth, group);
            var foldout = new VisualElement { focusable = true, tooltip = "Expand or collapse group; drag to move; Ctrl-click to select group alpha" };
            foldout.AddToClassList("whimtex-group-foldout");
            foldout.EnableInClassList("whimtex-layer-menu-button--light", !EditorGUIUtility.isProSkin);
            foldout.Add(new Label(GetGroupExpanded(group) ? "▼" : "▶") { pickingMode = PickingMode.Ignore });
            foldout.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return) return;
                WhimTexUI.ConsumeEvent(evt);
                ToggleLayerGroup(group);
            });
            nameCell.Add(foldout);

            TextField name = new TextField { isDelayed = true, isReadOnly = group.Behaviour == null };
            name.AddToClassList("whimtex-layer-name");
            name.SetValueWithoutNotify(group.layerName);
            toolkitLayerBindings.Track(name, () => group.layerName);
            name.RegisterValueChangedCallback(evt => ApplyToolkitChange(
                "Rename Sprite Group",
                () => group.layerName = evt.newValue));
            nameCell.Add(name);

            toolkitLayerBindings.Add(() => name.tooltip = $"{group.layers.Count} items");
            var opacity = new FloatField { isDelayed = true, tooltip = "Group opacity from 0 to 1." };
            opacity.AddToClassList("whimtex-layer-opacity");
            opacity.AddToClassList("whimtex-layer-multi-edit");
            toolkitLayerBindings.Track(opacity, () => group.opacity);
            opacity.RegisterValueChangedCallback(evt => ApplySelectedOpacity(group, evt.newValue));
            row.Add(opacity);
            var blend = LayerColorSettingsView.GroupBlend(group,
                (mode, passThrough) => ApplySelectedBlend(group, mode, passThrough), toolkitLayerBindings);
            blend.AddToClassList("whimtex-layer-blend");
            blend.AddToClassList("whimtex-layer-multi-edit");
            row.Add(blend);
            toolkitLayerBindings.Add(() =>
            {
                bool editable = !WhimTexApi.IsLayerContentLocked(compositor, group);
                opacity.SetEnabled(editable); blend.SetEnabled(editable);
            });
            row.Add(CreateLayerMenuButton(() => ShowLayerContextMenu(group, container, index)));
            RegisterToolkitLayerDrop(row, group, container, index, depth);
            return row;
        }

        private VisualElement BuildToolkitLeafRow(
            Layer layer,
            List<Layer> container,
            int index,
            int depth)
        {
            VisualElement row = CreateToolkitLayerRow(layer, depth);
            if (layer.Behaviour is FileLayerBehaviour)
                row.tooltip = "If this texture belongs to a compositor, double-click the thumbnail or row background to open it in another WhimTex tab.";

            row.Add(CreateLayerVisibilityButton(layer));
            VisualElement nameCell = CreateLayerNameCell(row, depth, layer);

            Image thumbnail = new Image
            {
                image = compositor.GetLayerThumbnail(layer, 18, EffectsAreInteractive),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            thumbnail.AddToClassList("whimtex-layer-thumbnail");
            nameCell.Add(thumbnail);
            if (layer.Behaviour is FileLayerBehaviour fileLayer)
            {
                var referenceAccent = new VisualElement { pickingMode = PickingMode.Ignore };
                referenceAccent.AddToClassList("whimtex-compositor-reference-accent");
                referenceAccent.AddToClassList("whimtex-hidden");
                row.Add(referenceAccent);
                Texture2D checkedSource = null;
                string checkedPath = null;
                toolkitLayerBindings.Add(() =>
                {
                    Texture2D source = fileLayer.sourceTexture;
                    string path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                    if (ReferenceEquals(source, checkedSource) && path == checkedPath) return;
                    checkedSource = source;
                    checkedPath = path;
                    referenceAccent.EnableInClassList("whimtex-hidden", TextureCompositor.FindDocument(source) == null);
                });
            }
            if (layer.Behaviour == null)
            {
                var missing = new Label("!") { tooltip = "Missing behaviour — select this layer to restore it.", pickingMode = PickingMode.Ignore };
                missing.AddToClassList("whimtex-missing-thumbnail");
                nameCell.Add(missing);
            }
            void RefreshThumbnail() => thumbnail.image = compositor.GetLayerThumbnail(layer, 18, EffectsAreInteractive);
            toolkitLayerBindings.Add(RefreshThumbnail);
            // Also finish deferred refreshes after the last input event. UI Toolkit pauses
            // this callback when the row is detached; only thumbnails are polled, not the inspector.
            if (layer.Behaviour is TargetedLayerBehaviour || layer.Behaviour is ShaderProcessorLayerBehaviour)
                thumbnail.schedule.Execute(RefreshThumbnail).Every(200);

            TextField name = new TextField { isDelayed = true };
            name.AddToClassList("whimtex-layer-name");
            name.SetValueWithoutNotify(layer.layerName);
            name.isReadOnly = layer.Behaviour == null;
            toolkitLayerBindings.Track(name, () => layer.layerName);
            name.RegisterValueChangedCallback(evt => ApplyToolkitChange(
                "Rename Sprite Layer",
                () => layer.layerName = evt.newValue));
            nameCell.Add(name);

            FloatField opacity = new FloatField { isDelayed = true };
            opacity.AddToClassList("whimtex-layer-multi-edit");
            opacity.tooltip = "Layer opacity from 0 to 1.";
            opacity.AddToClassList("whimtex-layer-opacity");
            opacity.SetValueWithoutNotify(layer.opacity);
            toolkitLayerBindings.Track(opacity, () => layer.opacity);
            opacity.RegisterValueChangedCallback(evt => ApplySelectedOpacity(layer, evt.newValue));
            row.Add(opacity);

            EnumField blend = new EnumField(layer.blendMode);
            blend.AddToClassList("whimtex-layer-multi-edit");
            toolkitLayerBindings.Track(blend, () => (Enum)layer.blendMode);
            blend.AddToClassList("whimtex-layer-blend");
            blend.RegisterValueChangedCallback(evt => ApplySelectedBlend(layer, (BlendMode)evt.newValue));
            row.Add(blend);

            if (layer?.Behaviour is PendingLayerBehaviour pending)
            {
                opacity.SetEnabled(false);
                blend.SetEnabled(false);
                var status = new Label("…") { pickingMode = PickingMode.Ignore };
                status.AddToClassList("whimtex-layer-agent-status");
                nameCell.Add(status);
                toolkitLayerBindings.Add(() => status.tooltip = WhimTexApi.LiveReservationStatus(pending));
            }
            else toolkitLayerBindings.Add(() =>
            {
                bool editable = !WhimTexApi.IsLayerContentLocked(compositor, layer);
                opacity.SetEnabled(editable); blend.SetEnabled(editable);
            });

            if (layer?.Behaviour is TargetedLayerBehaviour effect)
            {
                Label warning = new Label("!");
                warning.tooltip = effect.inputMode == EffectInputMode.Specific
                    ? "Select an existing non-cyclic target layer or group in the effect settings."
                    : "This effect needs a layer or group directly below it.";
                warning.style.color = new Color(1f, 0.65f, 0.15f, 1f);
                warning.style.unityFontStyleAndWeight = FontStyle.Bold;
                warning.style.width = 12f;
                nameCell.Add(warning);
                toolkitLayerBindings.Add(() =>
                {
                    warning.style.display = compositor.HasUsableEffectInput(effect, container, index)
                        ? DisplayStyle.None : DisplayStyle.Flex;
                    warning.tooltip = effect.inputMode == EffectInputMode.Specific
                        ? "Select an existing non-cyclic target layer or group in the effect settings."
                        : "This effect needs a layer or group directly below it.";
                });
            }

            row.Add(CreateLayerMenuButton(() => ShowLayerContextMenu(layer, container, index)));
            RegisterToolkitLayerDrop(row, layer, container, index, depth);
            return row;
        }

        private Button CreateLayerVisibilityButton(Layer layer)
        {
            var button = new Button(() => ToggleLayerVisibility(layer));
            button.AddToClassList("whimtex-layer-enabled");
            var eye = new LayerActionIcon(LayerActionIcon.Kind.Eye);
            eye.AddToClassList("whimtex-layer-eye");
            var eyeOff = new LayerActionIcon(LayerActionIcon.Kind.EyeOff);
            eyeOff.AddToClassList("whimtex-layer-eye-off");
            button.Add(eye);
            button.Add(eyeOff);
            void Refresh()
            {
                button.EnableInClassList("whimtex-layer-enabled--hidden", !layer.enabled);
                button.tooltip = layer?.IsGroup == true
                    ? (layer.enabled ? "Hide group and its descendants" : "Show group")
                    : (layer.enabled ? "Hide layer" : "Show layer");
            }
            Refresh();
            toolkitLayerBindings.Add(Refresh);
            return button;
        }

        private void ToggleLayerVisibility(Layer layer)
        {
            ApplyToolkitChange(layer?.IsGroup == true ? "Toggle Sprite Group" : "Toggle Sprite Layer",
                () => layer.enabled = !layer.enabled);
        }

        private static Button CreateLayerMenuButton(Action clicked)
        {
            Button button = new Button(clicked) { tooltip = "Layer menu" };
            button.AddToClassList("whimtex-layer-menu-button");
            button.EnableInClassList("whimtex-layer-menu-button--light", !EditorGUIUtility.isProSkin);
            for (int i = 0; i < 3; i++)
            {
                VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("whimtex-layer-menu-dot");
                button.Add(dot);
            }
            return button;
        }

        private VisualElement BuildMissingLayerRow(List<Layer> container, int index, int depth)
        {
            return new HelpBox("Unsupported layer data from an older document. This document format has no automatic migration.", HelpBoxMessageType.Warning);
        }

        private sealed class LayerDragManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private readonly Layer layer;
            private Vector2 start;
            private int pointerId = -1;
            private VisualElement pressedFoldout;
            private VisualElement pressedControl;
            private VisualElement textInputTarget;
            private PointerDownEvent pendingTextDown;
            private bool selectingText;

            public LayerDragManipulator(TextureCompositorWindow owner, Layer layer)
            {
                this.owner = owner;
                this.layer = layer;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Release();
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointerId >= 0 || !IsLayerDragArea(target, evt.target as VisualElement))
                    return;
                VisualElement control = FindLayerDragControl(target, evt.target as VisualElement);
                if (IsFocusedLayerTextControl(control)) return;
                if (control == null) owner.rootVisualElement.Focus();
                if (evt.ctrlKey || evt.commandKey || evt.shiftKey)
                {
                    owner.SelectLayerFromPointer(layer, evt, preserveSelection: true);
                    evt.StopImmediatePropagation();
                    return;
                }
                owner.FinishPreviewTransform();
                owner.FinishPaintingStroke();
                owner.activeLayerDrag?.Cancel();
                owner.activeLayerDrag = this;
                pressedControl = control;
                for (var element = evt.target as VisualElement; element != null && element != target; element = element.parent)
                    if (element.ClassListContains("whimtex-group-foldout"))
                    {
                        pressedFoldout = element;
                        element.Focus();
                        break;
                    }
                start = evt.position;
                pointerId = evt.pointerId;
                if (control is TextField || control is FloatField)
                {
                    textInputTarget = evt.target as VisualElement;
                    pendingTextDown = PointerDownEvent.GetPooled(evt);
                }
                target.CapturePointer(pointerId);
                WhimTexUI.ConsumeEvent(evt);
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (pointerId != evt.pointerId || selectingText)
                    return;
                if ((evt.pressedButtons & 1) == 0)
                {
                    Release();
                    return;
                }
                Vector2 delta = (Vector2)evt.position - start;
                if (delta.sqrMagnitude < 16f)
                {
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }

                if (pendingTextDown != null && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                {
                    BeginTextSelection(evt);
                    return;
                }

                if (pressedControl is TextField name)
                    name.SetValueWithoutNotify(layer.layerName);
                else if (pressedControl is FloatField opacity)
                    opacity.SetValueWithoutNotify(layer.opacity);
                owner.rootVisualElement.Focus();
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                bool selected = owner.IsLayerSelected(layer.Id);
                DragAndDrop.SetGenericData(DraggedLayerIdKey, selected ? owner.selectedLayerId : layer.Id);
                DragAndDrop.SetGenericData(DraggedCompositorIdKey, owner.compositor);
                DragAndDrop.SetGenericData(DraggedLayersKey, selected ? owner.GetSelectedRoots() : new List<Layer> { layer });
                owner.toolkitSettingsBindings.Refresh(true);
                DragAndDrop.StartDrag(string.IsNullOrEmpty(layer.layerName) ? "Layer" : layer.layerName);
                owner.ShowLayerDragGhost(target, layer, start, evt.position);
                Release();
                evt.StopImmediatePropagation();
            }

            private void BeginTextSelection(PointerMoveEvent evt)
            {
                selectingText = true;
                if (owner.IsLayerSelected(layer.Id)) owner.ActivateSelectedLayer(layer.Id);
                else owner.SelectOnlyLayer(layer.Id);
                owner.selectionAnchorId = layer.Id;
                owner.RefreshToolkitInterface();
                target.ReleasePointer(pointerId);
                using (var down = pendingTextDown)
                {
                    pendingTextDown = null;
                    down.target = textInputTarget;
                    textInputTarget.SendEvent(down);
                }
                using (var move = PointerMoveEvent.GetPooled(evt))
                {
                    move.target = textInputTarget;
                    textInputTarget.SendEvent(move);
                }
                WhimTexUI.ConsumeEvent(evt);
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (pointerId != evt.pointerId || evt.button != 0)
                    return;
                if (selectingText)
                {
                    Release();
                    return;
                }
                WhimTexUI.ConsumeEvent(evt);
                VisualElement foldout = pressedFoldout;
                VisualElement control = pressedControl;
                Release();
                if (foldout != null)
                {
                    evt.StopImmediatePropagation();
                    if (layer?.AsGroup() is Layer group && foldout.worldBound.Contains(evt.position))
                        owner.ToggleLayerGroup(group);
                    return;
                }
                if (owner.IsLayerSelected(layer.Id))
                    owner.ActivateSelectedLayer(layer.Id);
                else
                    owner.SelectOnlyLayer(layer.Id);
                owner.selectionAnchorId = layer.Id;
                owner.RefreshToolkitInterface();
                if (control == null || !control.worldBound.Contains(evt.position)) return;
                if (control is TextField name)
                {
                    name.Focus();
                    name.SelectAll();
                }
                else if (control is FloatField opacity)
                {
                    opacity.Focus();
                    opacity.SelectAll();
                }
                else if (control.ClassListContains("whimtex-layer-enabled"))
                    owner.ToggleLayerVisibility(layer);
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt)
            {
                if (pointerId == evt.pointerId && !selectingText && evt.target == target)
                    Release();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                if (pointerId == evt.pointerId) Release();
            }

            private void OnDetach(DetachFromPanelEvent evt) => Release();

            public void Cancel() => Release();

            private void Release()
            {
                int previousPointer = pointerId;
                pointerId = -1;
                pressedFoldout = null;
                pressedControl = null;
                textInputTarget = null;
                pendingTextDown?.Dispose();
                pendingTextDown = null;
                selectingText = false;
                if (owner.activeLayerDrag == this)
                    owner.activeLayerDrag = null;
                if (previousPointer >= 0 && target.HasPointerCapture(previousPointer))
                    target.ReleasePointer(previousPointer);
            }
        }

        private void RegisterToolkitLayerDrop(
            VisualElement row,
            Layer rowLayer,
            List<Layer> rowContainer,
            int rowIndex,
            int depth)
        {
            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!TryGetToolkitDrop(
                        row,
                        evt.mousePosition,
                        rowLayer,
                        rowContainer,
                        rowIndex,
                        out Layer dragged,
                        out List<Layer> destination,
                        out int destinationIndex,
                        out Layer groupToExpand,
                        out bool insertBefore))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                SetToolkitDropIndicator(row, groupToExpand != null, insertBefore, depth);
                evt.StopImmediatePropagation();
            });
            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (!TryGetToolkitDrop(
                        row,
                        evt.mousePosition,
                        rowLayer,
                        rowContainer,
                        rowIndex,
                        out Layer dragged,
                        out List<Layer> destination,
                        out int destinationIndex,
                        out Layer groupToExpand,
                        out _))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                ClearToolkitDropIndicator();
                PerformLayerDrop(dragged, destination, destinationIndex, groupToExpand);
                ClearLayerDragData();
                evt.StopImmediatePropagation();
            });
            row.RegisterCallback<DragLeaveEvent>(_ => ClearToolkitDropIndicator());
        }

        private bool TryGetToolkitDrop(
            VisualElement row,
            Vector2 mousePosition,
            Layer rowLayer,
            List<Layer> rowContainer,
            int rowIndex,
            out Layer dragged,
            out List<Layer> destination,
            out int destinationIndex,
            out Layer groupToExpand,
            out bool insertBefore)
        {
            dragged = GetDraggedLayer();
            destination = null;
            destinationIndex = -1;
            groupToExpand = null;
            insertBefore = false;
            if (dragged == null)
                return false;

            Vector2 local = row.WorldToLocal(mousePosition);
            float height = Mathf.Max(1f, row.resolvedStyle.height);
            bool dropInside = rowLayer?.IsGroup == true &&
                              local.y >= height * 0.25f &&
                              local.y <= height * 0.75f;
            insertBefore = local.y < height * 0.5f;
            if (dropInside)
            {
                groupToExpand = (Layer)rowLayer;
                destination = groupToExpand.layers;
                destinationIndex = 0;
            }
            else
            {
                destination = rowContainer;
                destinationIndex = insertBefore ? rowIndex : rowIndex + 1;
            }
            return CanDropLayer(dragged, destination, destinationIndex);
        }

        private VisualElement BuildContainerEndDropZone(List<Layer> destination, int depth)
        {
            VisualElement zone = new VisualElement();
            zone.userData = destination;
            zone.style.height = 8f;
            zone.style.marginLeft = 4f + depth * ToolkitLayerIndent;
            zone.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                Layer dragged = GetDraggedLayer();
                if (dragged == null || !CanDropLayer(dragged, destination, destination.Count))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                SetToolkitDropIndicator(zone, false, true, depth);
                evt.StopImmediatePropagation();
            });
            zone.RegisterCallback<DragPerformEvent>(evt =>
            {
                Layer dragged = GetDraggedLayer();
                if (dragged == null || !CanDropLayer(dragged, destination, destination.Count))
                    return;
                DragAndDrop.AcceptDrag();
                ClearToolkitDropIndicator();
                PerformLayerDrop(dragged, destination, destination.Count, null);
                ClearLayerDragData();
                evt.StopImmediatePropagation();
            });
            zone.RegisterCallback<DragLeaveEvent>(_ => ClearToolkitDropIndicator());
            return zone;
        }

        private void SetToolkitDropIndicator(
            VisualElement element,
            bool insideGroup,
            bool insertBefore,
            int depth)
        {
            if (activeDropElement != element)
            {
                ClearToolkitDropIndicator();
                activeDropMarginLeft = element.style.marginLeft;
            }
            activeDropElement = element;

            if (element.userData is string)
            {
                element.EnableInClassList("whimtex-layer-row--drop-inside", insideGroup);
                element.EnableInClassList("whimtex-layer-row--drop-before", !insideGroup && insertBefore);
                element.EnableInClassList("whimtex-layer-row--drop-after", !insideGroup && !insertBefore);
                return;
            }

            if (insideGroup)
            {
                element.style.backgroundColor = GroupDropHighlightColor;
                element.style.borderTopWidth = 1f;
                element.style.borderRightWidth = 1f;
                element.style.borderBottomWidth = 1f;
                element.style.borderLeftWidth = 1f;
                SetBorderColor(element, DropIndicatorColor);
            }
            else
            {
                element.style.borderTopWidth = insertBefore ? 2f : 0f;
                element.style.borderBottomWidth = insertBefore ? 0f : 2f;
                element.style.borderLeftWidth = 0f;
                element.style.borderRightWidth = 0f;
                element.style.borderTopColor = DropIndicatorColor;
                element.style.borderBottomColor = DropIndicatorColor;
                element.style.marginLeft = Mathf.Max(element.resolvedStyle.marginLeft, depth * ToolkitLayerIndent);
            }
        }

        private void ClearToolkitDropIndicator()
        {
            if (activeDropElement == null)
                return;

            activeDropElement.RemoveFromClassList("whimtex-layer-row--drop-inside");
            activeDropElement.RemoveFromClassList("whimtex-layer-row--drop-before");
            activeDropElement.RemoveFromClassList("whimtex-layer-row--drop-after");

            if (activeDropElement.userData is string layerId)
            {
                activeDropElement.style.backgroundColor = StyleKeyword.Null;
                ApplyLayerSelectionStyle(activeDropElement, layerId);
            }
            else
            {
                activeDropElement.style.borderTopWidth = 0f;
                activeDropElement.style.borderRightWidth = 0f;
                activeDropElement.style.borderBottomWidth = 0f;
                activeDropElement.style.borderLeftWidth = 0f;
                activeDropElement.style.marginLeft = activeDropMarginLeft;
                activeDropElement.style.backgroundColor = Color.clear;
            }
            activeDropElement = null;
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
        }

        private void RefreshToolkitPreviewHeader(bool forceValues = false)
        {
            if (toolkitPreviewHeader == null || compositor == null)
                return;

            if (toolkitHeaderBuilt)
            {
                toolkitHeaderBindings.Refresh(forceValues);
                return;
            }

            toolkitHeaderBuilt = true;
            CancelPreviewZoomGesture();
            toolkitHeaderBindings.Clear();
            toolkitPreviewHeader.Clear();
            BuildToolkitPreviewHeader();
            toolkitHeaderBindings.Refresh(forceValues);
        }

        private void BuildToolkitPreviewHeader()
        {
            toolkitPreviewActions.Clear();
            Button clear = WhimTexUI.CreateToolbarButton("Clear", () =>
            {
                if (GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour drawing) ClearDrawingLayer(drawing);
            }, 46f);
            toolkitHeaderBindings.Add(() => clear.SetEnabled(GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer)));
            toolkitPreviewActions.Add(clear);
            toolkitPreviewActions.Add(WhimTexUI.CreateToolbarButton("Refresh", () => RequestPreview(true), 64f));
            AddPreviewTransformSettings();
            AddPreviewZoomSettings();
            AddShapeSettings();
            AddAreaSelectionSettings(PreviewTool.RectangleSelect);
            AddAreaSelectionSettings(PreviewTool.PolygonSelect);

            VisualElement emptyRow = WhimTexUI.CreateToolbar();
            AddLayerPickSettings(emptyRow);
            toolkitHeaderBindings.Add(() => emptyRow.EnableInClassList("whimtex-tool-options--hidden",
                previewTool != PreviewTool.None || previewSettingsTool != PreviewTool.None));
            toolkitPreviewHeader.Add(emptyRow);

            AddFillSettings();
            AddPencilSettings();
            VisualElement brushRow = WhimTexUI.CreateToolbar();
            BindPreviewSettingsRow(brushRow, PreviewTool.Brush);
            EnumField tool = CompactField(new EnumField(paintSettings.tool), 72f);
            toolkitHeaderBindings.Track(tool, () => (Enum)paintSettings.tool);
            tool.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.tool = (PaintToolMode)evt.newValue));
            brushRow.Add(tool);

            AddPaintColorFields(brushRow);

            FloatField size = CompactField(new FloatField("Size"), 76f);
            size.AddToClassList("whimtex-brush-size");
            size.SetValueWithoutNotify(paintSettings.brushSize);
            toolkitHeaderBindings.Track(size, () => paintSettings.brushSize);
            size.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.brushSize = Mathf.Max(1f, evt.newValue)));
            brushRow.Add(size);
            AddBrushEdgeHeader(brushRow);
            AddBrushHeaderPercent(brushRow, "Opacity", () => paintSettings.dynamics.opacity,
                v => paintSettings.dynamics.opacity = v, "Opacity (%): maximum strength of one stroke. Release and start a new stroke to build up further.");
            AddBrushHeaderPercent(brushRow, "Flow", () => paintSettings.dynamics.flow,
                v => paintSettings.dynamics.flow = v, "Flow (%): strength of each stamp. Overlapping stamps build up within the stroke.");
            toolkitPreviewHeader.Add(brushRow);
        }

        private void AddPencilSettings()
        {
            VisualElement row = WhimTexUI.CreateToolbar();
            BindPreviewSettingsRow(row, PreviewTool.Pencil);
            DropdownField mode = CompactField(new DropdownField(new List<string> { "Pencil", "Eraser" }, 0), 78f);
            toolkitHeaderBindings.Track(mode, () => paintSettings.tool == PaintToolMode.Eraser ? "Eraser" : "Pencil");
            mode.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.tool = evt.newValue == "Eraser" ? PaintToolMode.Eraser : PaintToolMode.Brush));
            row.Add(mode);
            AddPaintColorFields(row);
            IntegerField size = CompactField(new IntegerField("Size"), 76f);
            size.AddToClassList("whimtex-brush-size");
            toolkitHeaderBindings.Track(size, () => paintSettings.pencilSize);
            size.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.pencilSize = Mathf.Clamp(evt.newValue, 1, 4096)));
            row.Add(size);
            row.Add(CreateCompactLabel("Shape", 42f));
            EnumField shape = CompactField(new EnumField(paintSettings.pencilShape), 94f);
            toolkitHeaderBindings.Track(shape, () => (Enum)paintSettings.pencilShape);
            shape.RegisterValueChangedCallback(evt => ApplyPaintToolChange(
                () => paintSettings.pencilShape = (PencilShape)evt.newValue));
            row.Add(shape);
            toolkitPreviewHeader.Add(row);
        }

        private static T CompactField<T>(T field, float width) where T : VisualElement
        {
            field.style.width = width;
            field.style.height = ToolkitPreviewHeaderRowHeight - 2f;
            field.style.marginLeft = 1f;
            field.style.marginRight = 1f;
            return field;
        }

        private static Label CreateCompactLabel(string text, float width)
        {
            Label result = new Label(text);
            result.style.width = width;
            result.style.unityTextAlign = TextAnchor.MiddleLeft;
            return result;
        }

        private void ApplyToolkitChange(
            string undoName,
            Action change)
        {
            if (compositor == null || change == null)
                return;

            Undo.RecordObject(compositor, undoName);
            applyingToolkitChange = true;
            try
            {
                change();
                CommitModelChange();
            }
            finally
            {
                applyingToolkitChange = false;
            }

            RefreshToolkitInterface();
        }

        private void UpdateToolkitPreviewPresentation()
        {
            if (toolkitPreviewCanvas == null || compositor == null)
                return;

            bool hasLayers = HasPreviewLayers;
            toolkitPreviewCanvas.SetCanvasVisible(hasLayers);
            DrawingLayerBehaviour drawing = GetSelectedLayer()?.Behaviour as DrawingLayerBehaviour;
            ApplyPreviewTextureFilter();
            RefreshPreviewQualityControl();
            RefreshPreviewToolToolbar();
            RefreshPreviewTransformTool();
            GetAreaSelection();
            areaSelectionOverlay?.Invalidate();
            bool transforming = IsPreviewTransformEnabled;
            toolkitPreviewCanvas.SetTiled(tiledPreview);
            toolkitPreviewCanvas.SetPencilCursor(previewTool == PreviewTool.Pencil);
            toolkitPreviewCanvas.SetDocument(channelPreviewTexture != null ? (Texture)channelPreviewTexture : PreviewPresentationSource,
                compositor.width, compositor.height,
                IsPreviewBrushEnabled ? drawing : null, transforming,
                IsPreviewPaintTool ? paintSettings : null);
            RefreshPreviewPointerCursor();
            if (toolkitPreviewError != null)
            {
                toolkitPreviewError.text = previewError ?? string.Empty;
                toolkitPreviewError.style.display = string.IsNullOrEmpty(previewError) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (toolkitPreviewFooter != null)
            {
                if (transforming)
                {
                    toolkitPreviewFooter.text = "Drag move • handles scale • circle rotate • gold cross pivot • Shift constrain • Esc cancel • T exit";
                }
                else if (IsPreviewZoomEnabled)
                {
                    toolkitPreviewFooter.text = "Click zoom in • Alt-click zoom out • Drag frame • MMB pan • Shift+MMB rotate • Wheel zoom";
                }
                else if (IsPreviewFillEnabled)
                {
                    toolkitPreviewFooter.text = "LMB fill • Alt pick color • X colors • All Layers / Contiguous / Tolerance / Antialias / Expand";
                }
                else if (previewTool == PreviewTool.Shape)
                {
                    toolkitPreviewFooter.text = "Drag new shape • Shift equal proportions / 45° line • Ctrl no snapping • Esc cancel • T transform";
                }
                else if (IsAreaSelectionTool)
                {
                    toolkitPreviewFooter.text = IsUvSelectionTool
                        ? "Click UV island • Shift add • Alt subtract • Ctrl+C copy • Ctrl+D deselect"
                        : previewTool == PreviewTool.RectangleSelect
                        ? "Drag select • Shift add • Alt subtract • Ctrl+C copy • Ctrl+V paste • Ctrl+D deselect"
                        : "Click vertices • Enter/double-click close • Backspace remove vertex • Esc cancel • Ctrl+D deselect";
                }
                else if (IsPreviewBrushEnabled)
                {
                    toolkitPreviewFooter.text = previewTexture != null
                        ? $"LMB paint • RMB erase • Alt pick color • Shift lines • X colors • [ ] size • {(previewTool == PreviewTool.Pencil ? paintSettings.pencilSize : paintSettings.brushSize):0.#} px"
                        : "Rendering painting preview…";
                }
                else if (IsPreviewPaintTool || previewTool == PreviewTool.Fill)
                {
                    toolkitPreviewFooter.text = GetSelectedLayer() == null
                        ? "Select a layer to paint or fill • Tool settings are shared"
                        : "Click to convert the selected layer to Drawing • Tool settings are shared";
                }
                else if (previewTool == PreviewTool.Transform)
                {
                    toolkitPreviewFooter.text = "Select a non-group layer to transform";
                }
                else
                {
                    toolkitPreviewFooter.text = !hasLayers ? "Add a layer to start"
                        : previewTexture != null
                        ? (tiledPreview ? "Tiled canvas • seamless brush and eraser • auto refresh" : "Transparent canvas • auto refresh")
                        : "Rendering preview…";
                }
                toolkitPreviewFooter.RefreshVisibility();
            }
        }

        private void OnPreviewPointerEnter(PointerEnterEvent evt)
        {
            previewPointerControl = evt.ctrlKey;
            UpdatePreviewCursor(evt.localPosition, evt.altKey);
        }

        private void OnPreviewPointerLeave(PointerLeaveEvent evt)
        {
            ClearPreviewPointerCursor();
        }

        private void OnPreviewPointerDown(PointerDownEvent evt)
        {
            previewPointerControl = evt.ctrlKey;
            if (HandleLayerPickPointerDown(evt)) return;
            if (HandlePaintConversionPrompt(evt)) return;
            if (HandleFillPointerDown(evt)) return;
            DrawingLayerBehaviour layer = GetSelectedLayer()?.Behaviour as DrawingLayerBehaviour;
            if (!IsPreviewBrushEnabled || paintingLayer != null || layer == null || (evt.button != 0 && evt.button != 1) || evt.altKey)
                return;
            if (!toolkitPreviewCanvas.contentRect.Contains(evt.localPosition)) return;

            Focus();
            toolkitPreviewCanvas.Focus();
            bool erase = evt.button == 1 || paintSettings.tool == PaintToolMode.Eraser;
            if (!erase && (previewChannels & 8) == 0)
            {
                WhimTexUI.ConsumeEvent(evt);
                return;
            }
            paintingMouseButton = evt.button;
            paintingPointerId = evt.pointerId;
            paintingErase = erase;
            paintingPointerMoved = false;
            previewPointerPosition = evt.localPosition;
            toolkitPreviewCanvas.CapturePointer(evt.pointerId);
            CapturePaintingGuide(evt.localPosition, evt.ctrlKey);
            if (!TryBeginPreviewStroke(GetPreviewPaintPosition(evt.localPosition, evt.shiftKey, evt.ctrlKey), evt.shiftKey)) FinishPaintingStroke();
            UpdatePreviewCursor(evt.localPosition, false);
            WhimTexUI.ConsumeEvent(evt);
        }

        private bool TryBeginPreviewStroke(Vector2 position, bool shift)
        {
            DrawingLayerBehaviour layer = GetSelectedLayer()?.Behaviour as DrawingLayerBehaviour;
            if (layer == null ||
                !TryMapPreviewToLayerUv(position, toolkitPreviewCanvas.ImageRect, layer, out Vector2 startUv, allowOutside: true)) return false;
            paintingLayer = layer;
            if (previewTool == PreviewTool.Brush) paintSettings.dynamics.seed = Environment.TickCount;
            bool connect = shift && ReferenceEquals(lineAnchorLayer, layer) &&
                           lineAnchorCanvasSize == new Vector2Int(compositor.width, compositor.height);
            Vector2 originUv = connect ? lineAnchorUv : startUv;
            lastPaintingUv = originUv;
            hasLastPaintingUv = true;
            Undo.RecordObject(compositor, "Paint Stroke");
            layer.PrepareStroke(compositor.width, compositor.height, "Paint Stroke");
            if (tiledPreview)
                layer.BeginTiledStroke(originUv, compositor.width, compositor.height);
            else
                layer.BeginStroke(originUv);
            RememberPaintingPoint(originUv);
            if (connect && originUv != startUv)
                PaintTowardsLayerPoint(startUv);
            else
                layer.PaintPoint(startUv, compositor.width, compositor.height, GetPaintingParameters());
            paintingShiftHeld = false;
            SetPaintingShift(shift);
            RefreshPreviewDuringPainting();
            return true;
        }

        private void OnPreviewPointerMove(PointerMoveEvent evt)
        {
            previewPointerControl = evt.ctrlKey;
            if (paintingLayer == null || paintingPointerId != evt.pointerId)
            {
                UpdatePreviewCursor(evt.localPosition, evt.altKey);
                return;
            }

            Vector2 paintPosition = GetPreviewPaintPosition(evt.localPosition, evt.shiftKey, evt.ctrlKey);
            paintingPointerMoved |= evt.deltaPosition.sqrMagnitude > 0f;
            UpdatePreviewCursor(evt.localPosition, evt.altKey);

            if (toolkitPreviewCanvas.contentRect.Contains(evt.localPosition) && TryMapPreviewToLayerUv(
                    paintPosition,
                    toolkitPreviewCanvas.ImageRect,
                    paintingLayer,
                    out Vector2 dragUv, allowOutside: true))
            {
                PaintTowardsLayerPoint(dragUv);
            }
            else
            {
                hasLastPaintingUv = false;
            }

            WhimTexUI.ConsumeEvent(evt);
        }

        private void PaintTowardsLayerPoint(Vector2 pointUv)
        {
            if (!paintingLayer.IsStrokePointInsideRepeatShape(pointUv, compositor.width, compositor.height))
            {
                if (hasLastPaintingUv && paintingLayer.TryClipStrokeSegmentToRepeatShape(
                        lastPaintingUv, pointUv, compositor.width, compositor.height, out Vector2 clippedUv))
                {
                    paintingLayer.PaintSegment(lastPaintingUv, clippedUv, compositor.width, compositor.height, false,
                        GetPaintingParameters());
                    RememberPaintingPoint(clippedUv);
                    RefreshPreviewDuringPainting();
                }
                hasLastPaintingUv = false;
                return;
            }

            if (hasLastPaintingUv)
            {
                if (lastPaintingUv == pointUv)
                    return;
                paintingLayer.PaintSegment(lastPaintingUv, pointUv, compositor.width, compositor.height, false,
                    GetPaintingParameters());
            }
            else
            {
                paintingLayer.PaintPoint(pointUv, compositor.width, compositor.height, GetPaintingParameters());
            }
            RememberPaintingPoint(pointUv);
            hasLastPaintingUv = true;
            RefreshPreviewDuringPainting();
        }

        private void SetPaintingShift(bool held)
        {
            if (paintingShiftHeld == held)
                return;
            paintingShiftHeld = held;
            paintingLockedAxis = 0;
            paintingAxisAnchor = lastPaintingDocumentUv;
            paintingAxisPointerAnchor = previewPointerPosition;
        }

        private Vector2 ConstrainPaintingPosition(Vector2 position, bool shift)
        {
            SetPaintingShift(shift);
            if (!shift)
                return position;

            Rect rect = toolkitPreviewCanvas.ImageRect;
            Vector2 anchor = toolkitPreviewCanvas.ToView(new Vector2(
                rect.x + paintingAxisAnchor.x * rect.width,
                rect.y + (1f - paintingAxisAnchor.y) * rect.height));
            Vector2 delta = position - paintingAxisPointerAnchor;
            if (paintingLockedAxis == 0)
            {
                if (delta.sqrMagnitude < 4f)
                    return anchor;
                paintingLockedAxis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? 1 : 2;
            }
            return paintingLockedAxis == 1 ? new Vector2(position.x, anchor.y) : new Vector2(anchor.x, position.y);
        }

        private void OnPreviewPointerUp(PointerUpEvent evt)
        {
            previewPointerControl = evt.ctrlKey;
            if (paintingLayer == null ||
                paintingPointerId != evt.pointerId ||
                paintingMouseButton != evt.button)
            {
                return;
            }

            Vector2 paintPosition = GetPreviewPaintPosition(evt.localPosition, evt.shiftKey, evt.ctrlKey);
            if (paintingPointerMoved && toolkitPreviewCanvas.contentRect.Contains(evt.localPosition) &&
                TryMapPreviewToLayerUv(paintPosition, toolkitPreviewCanvas.ImageRect, paintingLayer, out Vector2 endUv, allowOutside: true))
                PaintTowardsLayerPoint(endUv);

            paintingPointerId = -1;
            if (toolkitPreviewCanvas.HasPointerCapture(evt.pointerId))
                toolkitPreviewCanvas.ReleasePointer(evt.pointerId);
            FinishPaintingStroke();
            UpdatePreviewCursor(evt.localPosition, evt.altKey);
            WhimTexUI.ConsumeEvent(evt);
        }

        private void OnPreviewPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (paintingLayer == null || paintingPointerId != evt.pointerId)
                return;
            paintingPointerId = -1;
            FinishPaintingStroke();
        }

        private void UpdatePreviewCursor(Vector2 localPosition, bool alt)
        {
            previewPointerPosition = localPosition;
            previewPointerAlt = alt;
            previewPointerInside = toolkitPreviewCanvas != null && toolkitPreviewCanvas.contentRect.Contains(localPosition);
            previewEyedropper?.UpdateCursor(localPosition, alt);
            bool panning = previewZoomManipulator?.IsNavigating ?? false;
            if (!panning && previewGuideManipulator != null && previewGuideManipulator.WantsCursor(localPosition, alt))
            {
                toolkitPreviewCanvas.SetCursor(false, localPosition, false);
                toolkitPreviewCanvas.SetToolCursor(PreviewTool.Transform, false, false, MouseCursor.MoveArrow);
                return;
            }
            bool visible = IsPreviewPaintTool &&
                           !panning && !alt && previewPointerInside;
            toolkitPreviewCanvas?.SetCursor(
                visible,
                visible ? GetPreviewPaintPosition(localPosition, paintingShiftHeld, previewPointerControl, updateConstraint: false) : localPosition,
                paintingLayer != null ? paintingErase : paintSettings.tool == PaintToolMode.Eraser);
            MouseCursor transformCursor = !panning && previewPointerInside && previewTool == PreviewTool.Transform
                ? previewTransformManipulator?.GetCursor(localPosition, alt) ?? MouseCursor.Pan
                : MouseCursor.Pan;
            toolkitPreviewCanvas?.SetToolCursor(previewTool, visible, panning, transformCursor,
                previewZoomManipulator?.IsRotating ?? false);
        }

        private void RefreshPreviewPointerCursor()
        {
            if (previewPointerInside)
                UpdatePreviewCursor(previewPointerPosition, previewPointerAlt);
            else
                toolkitPreviewCanvas?.SetToolCursor(previewTool, false, previewZoomManipulator?.IsNavigating ?? false,
                    rotating: previewZoomManipulator?.IsRotating ?? false);
        }

        private void ClearPreviewPointerCursor()
        {
            previewPointerInside = false;
            previewPointerAlt = false;
            previewPointerControl = false;
            toolkitPreviewCanvas?.SetCursor(false, default, false);
            toolkitPreviewCanvas?.SetToolCursor(previewTool, false, false);
        }

        private void OnToolkitKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl)
            {
                previewPointerControl = evt.ctrlKey;
                RefreshPreviewPointerCursor();
            }
            if ((evt.ctrlKey || evt.commandKey) && !evt.altKey && !evt.shiftKey && evt.keyCode == KeyCode.S)
            {
                ResetOpacityEntry();
                WhimTexUI.ConsumeEvent(evt);
                if (compositor != null && AssetDatabase.Contains(compositor))
                    SaveAsset();
                else
                    SaveAsAsset();
                return;
            }

            if (IsTextInputTarget(evt.target as VisualElement))
            {
                ResetOpacityEntry();
                return;
            }

            if (evt.keyCode == KeyCode.Escape && previewGuideManipulator?.IsDragging == true)
            {
                previewGuideManipulator.Cancel();
                WhimTexUI.ConsumeEvent(evt);
                return;
            }

            if (evt.keyCode == KeyCode.Escape && gradientCanvasManipulator?.IsDragging == true)
            {
                gradientCanvasManipulator.End(true);
                WhimTexUI.ConsumeEvent(evt);
                return;
            }
            if (gradientCanvasManipulator?.HandleDelete(evt) == true) return;
            if (HandlePreviewGuideKey(evt)) return;
            if (HandleAreaSelectionKey(evt)) return;
            if (HandleLayerNavigationKey(evt)) return;

            if (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt)
            {
                previewEyedropper?.UpdateModifier(true);
                if (CanUsePreviewEyedropper)
                {
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
            }

            if (HandleOpacityKey(evt))
                return;
            ResetOpacityEntry();

            if (evt.keyCode == KeyCode.Escape && previewZoomManipulator != null && previewZoomManipulator.IsDragging)
            {
                CancelPreviewZoomGesture();
                WhimTexUI.ConsumeEvent(evt);
                return;
            }

            if (HandlePreviewTransformKey(evt))
                return;

            if (paintingLayer != null && (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift))
            {
                SetPaintingShift(true);
                RefreshPreviewPointerCursor();
                evt.StopImmediatePropagation();
                return;
            }

            bool actionModifier = evt.ctrlKey || evt.commandKey;
            if (actionModifier && !evt.shiftKey && evt.keyCode == KeyCode.E)
            {
                WhimTexUI.ConsumeEvent(evt);
                if (compositor != null) MergeSelectedLayers(GetSelectedRoots(), evt.altKey);
                return;
            }
            bool undo = actionModifier && !evt.altKey && evt.keyCode == KeyCode.Z && !evt.shiftKey;
            bool redo = actionModifier && !evt.altKey &&
                        ((evt.keyCode == KeyCode.Z && evt.shiftKey) ||
                         (evt.keyCode == KeyCode.Y && !evt.shiftKey));
            if (undo || redo)
            {
                FinishPreviewTransform();
                FinishPaintingStroke();
                if (undo)
                    Undo.PerformUndo();
                else
                    Undo.PerformRedo();
                WhimTexUI.ConsumeEvent(evt);
                return;
            }

            if (!IsPreviewPaintTool && previewTool != PreviewTool.Fill)
                return;

            bool swapColors = !actionModifier && !evt.altKey && evt.keyCode == KeyCode.X;
            if (swapColors)
            {
                ApplyPaintToolChange(paintSettings.SwapBrushColors);
                WhimTexUI.ConsumeEvent(evt);
                return;
            }

            if (!IsPreviewPaintTool) return;
            bool decrease = evt.keyCode == KeyCode.LeftBracket || evt.character == '[';
            bool increase = evt.keyCode == KeyCode.RightBracket || evt.character == ']';
            if (!decrease && !increase)
                return;

            float currentSize = previewTool == PreviewTool.Pencil ? paintSettings.pencilSize : paintSettings.brushSize;
            float step = PaintToolSettings.GetSizeShortcutStep(currentSize);
            float nextSize = Mathf.Max(1f, Mathf.Round(currentSize + (increase ? step : -step)));
            if (previewTool == PreviewTool.Pencil)
                ApplyPaintToolChange(() => paintSettings.pencilSize = Mathf.Clamp(Mathf.RoundToInt(nextSize), 1, 4096));
            else
                ApplyPaintToolChange(() => paintSettings.brushSize = nextSize);
            WhimTexUI.ConsumeEvent(evt);
        }

        private void OnToolkitKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode == KeyCode.LeftControl || evt.keyCode == KeyCode.RightControl)
            {
                previewPointerControl = evt.ctrlKey;
                RefreshPreviewPointerCursor();
            }
            if (evt.keyCode == KeyCode.LeftAlt || evt.keyCode == KeyCode.RightAlt)
            {
                previewEyedropper?.UpdateModifier(evt.altKey);
                if (CanUsePreviewEyedropper)
                {
                    WhimTexUI.ConsumeEvent(evt);
                    return;
                }
            }
            if (paintingLayer != null && (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift))
            {
                SetPaintingShift(evt.shiftKey);
                RefreshPreviewPointerCursor();
                evt.StopImmediatePropagation();
            }
        }

        private static bool IsTextInputTarget(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current is TextField ||
                    current is IntegerField ||
                    current is FloatField ||
                    current is Vector2Field ||
                    current.ClassListContains("unity-base-text-field__input"))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class SpritePreviewElement : VisualElement
        {
            private readonly PreviewViewport viewport;
            private readonly Image backdrop;
            private readonly PreviewInsetShadow insetShadow;
            private readonly VisualElement checker;
            private Texture2D checkerTexture;
            private Texture2D transparentCursorTexture;
            private bool toolCursorHidden;
            private readonly Image image;
            private readonly VisualElement tiledImage;
            private readonly PreviewCanvasShadow canvasShadow;
            private readonly VisualElement overlay;
            private readonly PencilCursorElement pencilCursorElement;
            private Texture texture;
            private DrawingLayerBehaviour drawingLayer;
            private PaintToolSettings brushSettings;
            private bool pencilCursor;
            private int documentWidth = 1;
            private int documentHeight = 1;
            private bool cursorVisible;
            private bool cursorErase;
            private bool transformMode;
            private Vector2 cursorPosition;
            private bool tiled;
            private bool canvasVisible = true;
            private Rect presentationRect;
            private float presentedRotation;

            public Rect ImageRect { get; private set; }
            public float PixelScale => ImageRect.width / Mathf.Max(1, documentWidth);
            public Vector2 ToCanvas(Vector2 point) => viewport.ToCanvas(contentRect, point);
            public Vector2 ToView(Vector2 point) => viewport.ToView(contentRect, point);
            public Rect VisibleCanvasBounds => viewport.VisibleCanvasBounds(contentRect);
            public event Action ViewChanged;

            public SpritePreviewElement(PreviewViewport viewport)
            {
                this.viewport = viewport;
                AddToClassList("whimtex-preview-canvas");
                focusable = true;
                style.minHeight = 96f;
                style.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.08f, 0.08f, 0.08f, 1f)
                    : new Color(0.58f, 0.58f, 0.58f, 1f);

                backdrop = new Image
                {
                    image = WhimTexBranding.PreviewBackdrop,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore,
                    focusable = false
                };
                backdrop.AddToClassList("whimtex-preview-backdrop");
                Add(backdrop);

                insetShadow = new PreviewInsetShadow();
                Add(insetShadow);
                RefreshBackdropVisibility();

                canvasShadow = new PreviewCanvasShadow();
                Add(canvasShadow);

                checker = new VisualElement { pickingMode = PickingMode.Ignore };
                checker.AddToClassList("whimtex-preview-surface");
                checker.style.position = Position.Absolute;
                checker.style.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.26f, 0.26f, 0.26f, 1f)
                    : new Color(0.76f, 0.76f, 0.76f, 1f);
                checker.generateVisualContent += DrawCheckerboard;
                Add(checker);
                RegisterCallback<AttachToPanelEvent>(_ => CreateCheckerTexture());
                RegisterCallback<DetachFromPanelEvent>(_ => ReleaseCheckerTexture());
                RegisterCallback<DetachFromPanelEvent>(_ => ReleaseToolCursor());

                image = new Image
                {
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore
                };
                image.style.position = Position.Absolute;
                image.AddToClassList("whimtex-preview-surface");
                Add(image);

                tiledImage = new VisualElement { pickingMode = PickingMode.Ignore };
                tiledImage.AddToClassList("whimtex-tiled-image");
                tiledImage.AddToClassList("whimtex-preview-surface");
                tiledImage.generateVisualContent += DrawTiledImage;
                Add(tiledImage);

                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.generateVisualContent += DrawOverlay;
                Add(overlay);
                pencilCursorElement = new PencilCursorElement();
                overlay.Add(pencilCursorElement);

                RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    UpdateBackdropLayout();
                    UpdateImageLayout();
                });
            }

            private sealed class PreviewInsetShadow : VisualElement
            {
                public PreviewInsetShadow()
                {
                    pickingMode = PickingMode.Ignore;
                    AddToClassList("whimtex-preview-inset-shadow");
                    generateVisualContent += Draw;
                }

                private void Draw(MeshGenerationContext context)
                {
                    Rect bounds = contentRect;
                    if (bounds.width < 1f || bounds.height < 1f) return;
                    const int steps = 16;
                    float depth = Mathf.Min(61.44f, Mathf.Min(bounds.width, bounds.height) * 0.5f);
                    MeshWriteData mesh = context.Allocate((steps + 1) * 4, steps * 24);
                    for (int ring = 0; ring <= steps; ring++)
                    {
                        float t = ring / (float)steps;
                        float inset = depth * t;
                        float fade = (1f - t) * (1f - t) * (1f - t);
                        Color32 top = new Color(0f, 0f, 0f, 0.36f * fade);
                        Color32 bottom = new Color(0f, 0f, 0f, 0.21f * fade);
                        mesh.SetNextVertex(new Vertex { position = new Vector3(bounds.xMin + inset, bounds.yMin + inset, Vertex.nearZ), tint = top });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(bounds.xMax - inset, bounds.yMin + inset, Vertex.nearZ), tint = top });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(bounds.xMax - inset, bounds.yMax - inset, Vertex.nearZ), tint = bottom });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(bounds.xMin + inset, bounds.yMax - inset, Vertex.nearZ), tint = bottom });
                    }
                    for (int ring = 0; ring < steps; ring++)
                    {
                        for (int side = 0; side < 4; side++)
                        {
                            ushort a = (ushort)(ring * 4 + side);
                            ushort b = (ushort)(ring * 4 + (side + 1) % 4);
                            ushort c = (ushort)(b + 4);
                            ushort d = (ushort)(a + 4);
                            mesh.SetNextIndex(a); mesh.SetNextIndex(b); mesh.SetNextIndex(c);
                            mesh.SetNextIndex(c); mesh.SetNextIndex(d); mesh.SetNextIndex(a);
                        }
                    }
                }
            }

            private sealed class PreviewCanvasShadow : VisualElement
            {
                private const int Rings = 16;
                private const float Spread = 30f;
                private const float DropOffset = 8f;

                public static Rect BoundsFor(Rect canvas) => new Rect(
                    canvas.x - Spread,
                    canvas.y - Spread + DropOffset,
                    canvas.width + Spread * 2f,
                    canvas.height + Spread * 2f);

                public PreviewCanvasShadow()
                {
                    pickingMode = PickingMode.Ignore;
                    AddToClassList("whimtex-preview-canvas-shadow");
                    AddToClassList("whimtex-preview-surface");
                    generateVisualContent += Draw;
                }

                private void Draw(MeshGenerationContext context)
                {
                    Rect bounds = contentRect;
                    if (bounds.width <= Spread * 2f + 1f || bounds.height <= Spread * 2f + 1f) return;

                    // Canvas footprint inside this element: Spread on the sides, Spread minus DropOffset
                    // above and Spread plus DropOffset below, so the falloff is heavier under the canvas.
                    float left = bounds.xMin + Spread;
                    float top = bounds.yMin + Spread - DropOffset;
                    float right = bounds.xMax - Spread;
                    float bottom = bounds.yMax - Spread - DropOffset;

                    // Same band mesh as PreviewInsetShadow: consecutive rings share vertices, so the
                    // cubic fade interpolates smoothly instead of stepping. The peak sits on the
                    // canvas edge and falls to nothing at the outer bounds.
                    // 40% of the way from the soft variant (0.38) back toward the original strong one (0.78).
                    float peak = EditorGUIUtility.isProSkin ? 0.54f : 0.19f;
                    MeshWriteData mesh = context.Allocate((Rings + 1) * 4, Rings * 24);
                    for (int ring = 0; ring <= Rings; ring++)
                    {
                        float t = ring / (float)Rings;
                        float fade = (1f - t) * (1f - t) * (1f - t);
                        Color32 tint = new Color(0f, 0f, 0f, peak * fade);
                        mesh.SetNextVertex(new Vertex { position = new Vector3(Mathf.Lerp(left, bounds.xMin, t), Mathf.Lerp(top, bounds.yMin, t), Vertex.nearZ), tint = tint });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(Mathf.Lerp(right, bounds.xMax, t), Mathf.Lerp(top, bounds.yMin, t), Vertex.nearZ), tint = tint });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(Mathf.Lerp(right, bounds.xMax, t), Mathf.Lerp(bottom, bounds.yMax, t), Vertex.nearZ), tint = tint });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(Mathf.Lerp(left, bounds.xMin, t), Mathf.Lerp(bottom, bounds.yMax, t), Vertex.nearZ), tint = tint });
                    }
                    for (int ring = 0; ring < Rings; ring++)
                    {
                        for (int side = 0; side < 4; side++)
                        {
                            ushort a = (ushort)(ring * 4 + side);
                            ushort b = (ushort)(ring * 4 + (side + 1) % 4);
                            ushort c = (ushort)(b + 4);
                            ushort d = (ushort)(a + 4);
                            // Rings grow outwards here, the opposite of PreviewInsetShadow, so the
                            // winding has to be reversed to keep the faces front-facing.
                            mesh.SetNextIndex(a); mesh.SetNextIndex(c); mesh.SetNextIndex(b);
                            mesh.SetNextIndex(a); mesh.SetNextIndex(d); mesh.SetNextIndex(c);
                        }
                    }
                }
            }

            public void RefreshBackdropVisibility()
            {
                backdrop.EnableInClassList("whimtex-preview-backdrop--hidden", !WhimTexUserSettings.ShowManta);
                insetShadow.EnableInClassList("whimtex-preview-backdrop--hidden", !WhimTexUserSettings.ShowManta);
            }

            private void UpdateBackdropLayout()
            {
                Rect bounds = contentRect;
                float size = Mathf.Min(bounds.width * 1.15f, bounds.height * 1.30f);
                if (size <= 0f || float.IsNaN(size) || float.IsInfinity(size)) return;
                PositionElement(backdrop, new Rect(
                    bounds.xMin - size * 0.22f,
                    bounds.yMax - size * 0.72f,
                    size, size));
            }

            public void SetToolCursor(PreviewTool tool, bool hide, bool panning, MouseCursor transformCursor = MouseCursor.Pan, bool rotating = false)
            {
                bool transforming = !panning && tool == PreviewTool.Transform;
                EnableInClassList("whimtex-preview-cursor--pan", (panning && !rotating) || (transforming && transformCursor == MouseCursor.Pan));
                EnableInClassList("whimtex-preview-cursor--zoom", !panning && tool == PreviewTool.Zoom);
                EnableInClassList("whimtex-preview-cursor--scale", transforming && transformCursor == MouseCursor.ScaleArrow);
                EnableInClassList("whimtex-preview-cursor--rotate", rotating || (transforming && transformCursor == MouseCursor.RotateArrow));
                EnableInClassList("whimtex-preview-cursor--move", transforming && transformCursor == MouseCursor.MoveArrow);
                if (toolCursorHidden == hide) return;
                toolCursorHidden = hide;
                if (!hide)
                {
                    style.cursor = StyleKeyword.Null;
                    return;
                }
                if (transparentCursorTexture == null)
                {
                    transparentCursorTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
                    {
                        name = "WhimTex Transparent Cursor",
                        hideFlags = HideFlags.HideAndDontSave,
                        alphaIsTransparency = true
                    };
                    transparentCursorTexture.SetPixels32(new Color32[16 * 16]);
                    transparentCursorTexture.Apply(false, false);
                }
                style.cursor = new UnityEngine.UIElements.Cursor { texture = transparentCursorTexture, hotspot = Vector2.zero };
            }

            public void ReleaseToolCursor()
            {
                style.cursor = StyleKeyword.Null;
                toolCursorHidden = false;
                if (transparentCursorTexture != null) UnityEngine.Object.DestroyImmediate(transparentCursorTexture);
                transparentCursorTexture = null;
            }

            public void SetCanvasVisible(bool visible)
            {
                if (canvasVisible == visible) return;
                canvasVisible = visible;
                EnableInClassList("whimtex-preview-canvas--empty", !visible);
                if (visible) viewport.Reset();
                UpdateImageLayout(true);
            }

            public void SetTiled(bool enabled)
            {
                if (tiled == enabled) return;
                tiled = enabled;
                image.EnableInClassList("whimtex-preview-image--hidden", tiled);
                tiledImage.EnableInClassList("whimtex-preview-image--visible", tiled);
                canvasShadow.EnableInClassList("whimtex-preview-canvas-shadow--hidden", tiled);
                UpdateImageLayout(true);
            }

            public void SetPencilCursor(bool value)
            {
                if (pencilCursor == value) return;
                pencilCursor = value;
                if (!value) pencilCursorElement.SetVisible(false);
                overlay.MarkDirtyRepaint();
            }

            public void SetDocument(Texture nextTexture, int width, int height, DrawingLayerBehaviour layer, bool transforming = false, PaintToolSettings brush = null)
            {
                transformMode = transforming;
                texture = nextTexture;
                if (texture is RenderTexture) texture.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                documentWidth = Mathf.Max(1, width);
                documentHeight = Mathf.Max(1, height);
                drawingLayer = layer;
                brushSettings = brush;
                if (brushSettings == null)
                    cursorVisible = false;
                overlay.style.display = brushSettings == null
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                image.image = texture;
                image.MarkDirtyRepaint();
                tiledImage.MarkDirtyRepaint();
                UpdateImageLayout();
                UpdatePencilCursor();
                overlay.MarkDirtyRepaint();
            }

            public void ClearTexture()
            {
                texture = null;
                image.image = null;
                tiledImage.MarkDirtyRepaint();
            }

            public void SetCursor(bool visible, Vector2 position, bool erase)
            {
                cursorVisible = visible;
                cursorPosition = position;
                cursorErase = erase;
                if (pencilCursor) UpdatePencilCursor();
                else overlay.MarkDirtyRepaint();
            }

            public void ZoomAt(Vector2 point, float scale)
            {
                if (!canvasVisible) return;
                viewport.ZoomAt(contentRect, new Vector2(documentWidth, documentHeight), ImageRect, point, scale);
                UpdateImageLayout();
            }

            public void Frame(Rect region)
            {
                if (!canvasVisible) return;
                viewport.Frame(contentRect, new Vector2(documentWidth, documentHeight), ImageRect, region);
                UpdateImageLayout();
            }

            public void Pan(Vector2 delta)
            {
                if (!canvasVisible) return;
                viewport.Pan(contentRect, new Vector2(documentWidth, documentHeight), ImageRect, delta);
                UpdateImageLayout();
            }

            public void UpdateImageLayout()
            {
                UpdateImageLayout(false);
            }

            public void SetViewRotation(float degrees, bool snap = false)
            {
                if (!canvasVisible) return;
                viewport.SetRotation(degrees, snap);
                UpdateImageLayout();
            }

            private void UpdateImageLayout(bool force)
            {
                Rect nextRect = viewport.ImageRect(contentRect, new Vector2(documentWidth, documentHeight));
                Rect nextPresentation = tiled ? contentRect : nextRect;
                if (!force && nextRect == ImageRect && nextPresentation == presentationRect &&
                    presentedRotation == viewport.Rotation)
                    return;
                ImageRect = nextRect;
                presentationRect = nextPresentation;
                presentedRotation = viewport.Rotation;
                PositionSurface(checker, presentationRect, !tiled);
                PositionSurface(canvasShadow, PreviewCanvasShadow.BoundsFor(ImageRect), true);
                PositionSurface(image, ImageRect, true);
                PositionElement(tiledImage, contentRect);
                PositionElement(overlay, contentRect);
                UpdatePencilCursor();
                checker.MarkDirtyRepaint();
                tiledImage.MarkDirtyRepaint();
                overlay.MarkDirtyRepaint();
                ViewChanged?.Invoke();
            }

            private void PositionSurface(VisualElement element, Rect rect, bool rotate)
            {
                // Keep the background/logo stationary and rotate only the canvas surfaces.
                if (rotate) rect.position = ToView(rect.center) - rect.size * 0.5f;
                PositionElement(element, rect);
                element.style.rotate = new UnityEngine.UIElements.Rotate(
                    new Angle(rotate ? viewport.Rotation : 0f, AngleUnit.Degree));
            }

            private static void PositionElement(VisualElement element, Rect rect)
            {
                element.style.left = rect.x;
                element.style.top = rect.y;
                element.style.width = rect.width;
                element.style.height = rect.height;
            }

            private void CreateCheckerTexture()
            {
                if (checkerTexture != null) return;
                checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "WhimTex Checkerboard",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
                RefreshCheckerColors();
            }

            public void RefreshCheckerColors()
            {
                if (checkerTexture == null) return;
                Color light = WhimTexUserSettings.CheckerLight;
                Color dark = WhimTexUserSettings.CheckerDark;
                checkerTexture.SetPixels(new[] { light, dark, dark, light });
                checkerTexture.Apply(false, false);
                checker.MarkDirtyRepaint();
            }

            public void ReleaseCheckerTexture()
            {
                if (checkerTexture == null) return;
                UnityEngine.Object.DestroyImmediate(checkerTexture);
                checkerTexture = null;
            }

            private void DrawCheckerboard(MeshGenerationContext context)
            {
                Rect rect = checker.contentRect;
                if (checkerTexture == null || rect.width <= 0f || rect.height <= 0f) return;

                float period = 2f * WhimTexUserSettings.CheckerSize;
                float u = rect.width / period;
                float v = rect.height / period;
                context.AllocateTempMesh(4, 6, out var vertices, out var indices);
                vertices[0] = new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = Color.white, uv = Vector2.zero };
                vertices[1] = new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = Color.white, uv = new Vector2(u, 0f) };
                vertices[2] = new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(u, v) };
                vertices[3] = new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(0f, v) };
                indices[0] = 0;
                indices[1] = 1;
                indices[2] = 2;
                indices[3] = 0;
                indices[4] = 2;
                indices[5] = 3;
#if UNITY_6000_3_OR_NEWER
                context.DrawMesh(vertices, indices, checkerTexture, TextureOptions.SkipDynamicAtlas);
#else
                context.DrawMesh(vertices, indices, checkerTexture);
#endif
            }

            private void DrawTiledImage(MeshGenerationContext context)
            {
                Rect rect = tiledImage.contentRect;
                if (!tiled || texture == null || rect.width <= 0f || rect.height <= 0f ||
                    ImageRect.width <= 0f || ImageRect.height <= 0f) return;
                Vector2 Uv(Vector2 point)
                {
                    point = ToCanvas(point + contentRect.position);
                    return new Vector2((point.x - ImageRect.xMin) / ImageRect.width,
                        1f - (point.y - ImageRect.yMin) / ImageRect.height);
                }
                Vector2 origin = Uv(rect.min);
                // Rebase all corners together: preserve interpolation across repeated UVs.
                Vector2 offset = new Vector2(Mathf.Floor(origin.x), Mathf.Floor(origin.y));
                context.AllocateTempMesh(4, 6, out var vertices, out var indices);
                vertices[0] = new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = Color.white, uv = origin - offset };
                vertices[1] = new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = Color.white, uv = Uv(new Vector2(rect.xMax, rect.yMin)) - offset };
                vertices[2] = new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = Color.white, uv = Uv(rect.max) - offset };
                vertices[3] = new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = Color.white, uv = Uv(new Vector2(rect.xMin, rect.yMax)) - offset };
                indices[0] = 0; indices[1] = 1; indices[2] = 2;
                indices[3] = 0; indices[4] = 2; indices[5] = 3;
#if UNITY_6000_3_OR_NEWER
                context.DrawMesh(vertices, indices, texture, TextureOptions.SkipDynamicAtlas);
#else
                context.DrawMesh(vertices, indices, texture);
#endif
            }

            private void DrawOverlay(MeshGenerationContext context)
            {
                if (brushSettings == null)
                    return;

                Rect rect = new Rect(ImageRect.position - contentRect.position, ImageRect.size);
                Painter2D painter = context.painter2D;
                Color guide = new Color(0.20f, 0.70f, 1f, 0.55f);
                painter.lineWidth = 1f;
                painter.strokeColor = guide;

                if (drawingLayer != null)
                    DrawPatternGuides(painter, rect);

                if (!cursorVisible || pencilCursor)
                    return;
                Vector2 localCursor = cursorPosition - contentRect.position;
                float pixelScale = PixelScale;
                float transformScale = drawingLayer == null ? 1f :
                    (Mathf.Abs(drawingLayer.transform.scale.x) + Mathf.Abs(drawingLayer.transform.scale.y)) * 0.5f;
                float radius = Mathf.Max(
                    2f,
                    brushSettings.brushSize * pixelScale * Mathf.Max(0.0001f, transformScale) * 0.5f);
                painter.lineWidth = 1f;
                painter.strokeColor = new Color(0f, 0f, 0f, 0.95f);
                StrokeCircle(painter, localCursor, radius + 1f);
                painter.strokeColor = cursorErase
                    ? new Color(1f, 0.35f, 0.25f, 1f)
                    : new Color(1f, 1f, 1f, 0.95f);
                StrokeCircle(painter, localCursor, radius);
            }

            private void UpdatePencilCursor()
            {
                if (pencilCursorElement == null) return;
                bool visible = pencilCursor && cursorVisible && brushSettings != null;
                TextureTransform transform = drawingLayer?.transform ?? TextureTransform.Default;
                Rect rect = new Rect(ImageRect.position - contentRect.position, ImageRect.size);
                if (!visible || !TiledCanvasUtility.IsInvertible(transform) || rect.width <= 0f || rect.height <= 0f)
                {
                    pencilCursorElement.SetVisible(false);
                    return;
                }
                Vector2 canvasCursor = ToCanvas(cursorPosition);
                Vector2 documentUv = new Vector2((canvasCursor.x - ImageRect.x) / ImageRect.width,
                    1f - (canvasCursor.y - ImageRect.y) / ImageRect.height);
                Vector2 source = TiledCanvasUtility.ToSource(documentUv, transform, documentWidth, documentHeight);
                if (tiled) source = TiledCanvasUtility.CanonicalSource(source, transform, documentWidth, documentHeight);
                Vector2 center = PaintStrokeParameters.SnapPencilCenter(source, documentWidth, documentHeight, brushSettings.pencilSize);
                Vector2 tileOffset = Vector2.zero;
                if (tiled)
                {
                    Vector2 documentCenter = TiledCanvasUtility.ToDocument(center, transform, documentWidth, documentHeight);
                    tileOffset = new Vector2(Mathf.Round(documentUv.x - documentCenter.x), Mathf.Round(documentUv.y - documentCenter.y));
                }
                Vector2 uv = TiledCanvasUtility.ToDocument(center, transform, documentWidth, documentHeight) + tileOffset;
                Vector2 screenCenter = rect.position + new Vector2(uv.x * rect.width, (1f - uv.y) * rect.height);
                float angle = transform.rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                float px = rect.width / documentWidth, py = rect.height / documentHeight;
                Vector2 x = new Vector2(cos * px, -sin * py) * transform.scale.x;
                Vector2 y = new Vector2(-sin * px, -cos * py) * transform.scale.y;
                screenCenter = ToView(screenCenter + contentRect.position) - contentRect.position;
                x = viewport.ToViewDelta(x);
                y = viewport.ToViewDelta(y);
                pencilCursorElement.SetState(brushSettings.pencilSize, brushSettings.pencilShape,
                    screenCenter, x, y, cursorErase, EditorGUIUtility.pixelsPerPoint);
            }

            private void DrawPatternGuides(Painter2D painter, Rect rect)
            {
                if (drawingLayer.UsesMirrorPattern && drawingLayer.mirrorAcrossVerticalAxis)
                    DrawMirrorGuide(painter, rect, true);
                if (drawingLayer.UsesMirrorPattern && drawingLayer.mirrorAcrossHorizontalAxis)
                    DrawMirrorGuide(painter, rect, false);

                int count = Mathf.Clamp(drawingLayer.repeatCount, 2, 64);
                if (drawingLayer.repeatMode == PaintRepeatMode.Horizontal ||
                    drawingLayer.repeatMode == PaintRepeatMode.Grid)
                {
                    for (int i = 1; i < count; i++)
                    {
                        float x = rect.width * i / count;
                        StrokeLine(painter, rect.position + new Vector2(x, 0f), rect.position + new Vector2(x, rect.height));
                    }
                }
                if (drawingLayer.repeatMode == PaintRepeatMode.Vertical ||
                    drawingLayer.repeatMode == PaintRepeatMode.Grid)
                {
                    int verticalCount = drawingLayer.repeatMode == PaintRepeatMode.Grid
                        ? Mathf.Clamp(drawingLayer.repeatSecondaryCount, 2, 64)
                        : count;
                    for (int i = 1; i < verticalCount; i++)
                    {
                        float y = rect.height * i / verticalCount;
                        StrokeLine(painter, rect.position + new Vector2(0f, y), rect.position + new Vector2(rect.width, y));
                    }
                }
                if (drawingLayer.repeatMode == PaintRepeatMode.Radial)
                {
                    Vector2 center = rect.position + new Vector2(
                        rect.width * drawingLayer.patternCenter.x,
                        rect.height * (1f - drawingLayer.patternCenter.y));
                    float length = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height);
                    for (int i = 0; i < count; i++)
                    {
                        float angle = drawingLayer.RadialStartAngleRadians + i * Mathf.PI * 2f / count;
                        Vector2 direction = new Vector2(
                            Mathf.Cos(angle) * rect.width / Mathf.Max(1, documentWidth),
                            -Mathf.Sin(angle) * rect.height / Mathf.Max(1, documentHeight)).normalized;
                        StrokeLine(painter, center, center + direction * length);
                    }
                }
            }

            private void DrawMirrorGuide(Painter2D painter, Rect rect, bool vertical)
            {
                Vector2 center = rect.position + new Vector2(rect.width * drawingLayer.patternCenter.x,
                    rect.height * (1f - drawingLayer.patternCenter.y));
                Vector2 axis = drawingLayer.GetMirrorAxisDirection(vertical);
                Vector2 direction = new Vector2(axis.x * rect.width / Mathf.Max(1, documentWidth),
                    -axis.y * rect.height / Mathf.Max(1, documentHeight)).normalized;
                float forward = float.PositiveInfinity, backward = float.PositiveInfinity;
                if (Mathf.Abs(direction.x) > 0.000001f)
                {
                    forward = (direction.x > 0f ? rect.xMax - center.x : rect.xMin - center.x) / direction.x;
                    backward = (direction.x > 0f ? center.x - rect.xMin : center.x - rect.xMax) / direction.x;
                }
                if (Mathf.Abs(direction.y) > 0.000001f)
                {
                    forward = Mathf.Min(forward, (direction.y > 0f ? rect.yMax - center.y : rect.yMin - center.y) / direction.y);
                    backward = Mathf.Min(backward, (direction.y > 0f ? center.y - rect.yMin : center.y - rect.yMax) / direction.y);
                }
                if (!float.IsInfinity(forward) && !float.IsInfinity(backward))
                    StrokeLine(painter, center - direction * backward, center + direction * forward);
            }

            private static void FillRect(Painter2D painter, float x, float y, float width, float height)
            {
                if (width <= 0f || height <= 0f)
                    return;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + width, y));
                painter.LineTo(new Vector2(x + width, y + height));
                painter.LineTo(new Vector2(x, y + height));
                painter.ClosePath();
                painter.Fill();
            }

            private void StrokeLine(Painter2D painter, Vector2 from, Vector2 to)
            {
                painter.BeginPath();
                painter.MoveTo(ToView(from + contentRect.position) - contentRect.position);
                painter.LineTo(ToView(to + contentRect.position) - contentRect.position);
                painter.Stroke();
            }

            private static void StrokeCircle(Painter2D painter, Vector2 center, float radius)
            {
                painter.BeginPath();
                painter.Arc(center, radius, 0f, 360f);
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
