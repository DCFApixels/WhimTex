using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow : EditorWindow, IHasCustomMenu
    {
        private const int PreviewMaxSize = 512;
        private const double PreviewDelay = 0.12d;
        private const double PaintingPreviewInterval = 1d / 30d;
        private const float DefaultPaintingPreviewScale = 1f;
        private const float MinimumPaintingPreviewScale = 0.125f;
        private const float MaximumPaintingPreviewScale = 1f;
        private const float DefaultSettingsPaneWidth = 400f;
        private const float DefaultLayerSettingsPaneHeight = 320f;
        private const float PreviewPaneMinWidth = 200f;
        private const float SettingsPaneMinWidth = 320f;
        private const float PanePadding = 8f;
        private const string DraggedLayerIdKey = "DCFApixels.WhimTex.DraggedLayerId";
        private const string DraggedCompositorIdKey = "DCFApixels.WhimTex.DraggedCompositorId";
        private const string PaintingPreviewScalePrefKey = "DCFApixels.WhimTex.PaintingPreviewScale";

        private static readonly Color DropIndicatorColor = new Color(0.20f, 0.58f, 0.95f, 1f);
        private static readonly Color GroupDropHighlightColor = new Color(0.20f, 0.58f, 0.95f, 0.22f);
        private static readonly GUIContent LivePreviewQualityContent = new GUIContent(
            "Live Quality",
            "Resolution used while painting. 100% disables downscaling; lower values make effect-heavy previews faster.");
        private static readonly GUIContent PrimaryBrushColorContent = new GUIContent(
            string.Empty,
            "Foreground brush color. Press X to swap it with the background color.");
        private static readonly GUIContent SecondaryBrushColorContent = new GUIContent(
            string.Empty,
            "Background brush color. Press X to swap it with the foreground color.");
        private static readonly System.Reflection.PropertyInfo UnityShortcutsEnabledProperty =
            typeof(EditorWindow).Assembly
                .GetType("UnityEditor.ShortcutManagement.ShortcutIntegration")
                ?.GetProperty(
                    "enabled",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

        private static int unityShortcutSuppressionOwners;
        private static bool restoreUnityShortcutsEnabled;
        private static bool shortcutSuppressionWarningLogged;

        [SerializeField] private TextureCompositor compositor;
        [SerializeField] private string selectedLayerId;
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private float settingsPaneWidth = DefaultSettingsPaneWidth;
        [SerializeField] private float layerSettingsPaneHeight = DefaultLayerSettingsPaneHeight;

        [NonSerialized] private RenderTexture previewTexture;
        [NonSerialized] private bool previewRequested;
        [NonSerialized] private double previewAt;
        [SerializeField] private bool temporaryDocumentDirty;
        [NonSerialized] private string previewError;
        [NonSerialized] private Dictionary<string, bool> groupExpansion;
        [NonSerialized] private DrawingLayerBehaviour paintingLayer;
        [NonSerialized] private Vector2 lastPaintingUv;
        [NonSerialized] private bool hasLastPaintingUv;
        [NonSerialized] private DrawingLayerBehaviour lineAnchorLayer;
        [NonSerialized] private Vector2 lineAnchorUv;
        [NonSerialized] private Vector2Int lineAnchorCanvasSize;
        [NonSerialized] private Vector2 lastPaintingDocumentUv;
        [NonSerialized] private Vector2 paintingAxisAnchor;
        [NonSerialized] private Vector2 paintingAxisPointerAnchor;
        [NonSerialized] private bool paintingShiftHeld;
        [NonSerialized] private int paintingLockedAxis;
        [NonSerialized] private bool paintingPointerMoved;
        [NonSerialized] private bool paintingErase;
        [NonSerialized] private int paintingMouseButton = -1;
        [NonSerialized] private double nextPaintingPreviewAt;
        [NonSerialized] private float paintingPreviewScale;
        [NonSerialized] private bool ownsUnityShortcutSuppression;

        [MenuItem("Window/WhimTex")]
        public static void ShowWindow()
        {
            var window = GetWindow<TextureCompositorWindow>("WhimTex");
            window.RefreshDocumentTitle(true);
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("User Settings…"), false, WhimTexUserSettingsWindow.Open);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save As WhimTex File…"), false, () => SaveDocumentAs(compositor));
            menu.AddItem(new GUIContent(WhimTexDocumentSession.IsLiveFor(compositor) ? "Stop Live Update" : "Start Live Update"),
                false, () => ToggleLiveUpdate(compositor));
        }

        internal static void ConfirmResetEditorSettings(EditorWindow notificationWindow)
        {
            if (!EditorUtility.DisplayDialog(
                "Reset WhimTex Settings",
                "Reset panel sizes, scrolling, selection, foldouts, RGBA channels and preview tool state in all open " +
                "WhimTex windows, and remove the saved Live Quality preference?\n\n" +
                "Shared brush, color, fill, preview appearance settings and the presets folder path will also be reset. Preset files will not be deleted. " +
                "Open documents (including unsaved work), layers, textures and Shader FX " +
                "will be preserved. Unity settings and window docking will not change. " +
                "This settings reset cannot be undone.",
                "Reset Settings", "Cancel"))
                return;

            TextureCompositorWindow[] windows = Resources.FindObjectsOfTypeAll<TextureCompositorWindow>();
            foreach (TextureCompositorWindow window in windows)
            {
                window.rootVisualElement.Focus();
                window.FinishPreviewTransform();
                window.FinishPaintingStroke();
            }
            Undo.FlushUndoRecordObjects();
            EditorPrefs.DeleteKey(PaintingPreviewScalePrefKey);
            EditorPrefs.DeleteKey(PaintToolSettingsPrefKey);
            EditorPrefs.DeleteKey(PreviewToolPrefKey);
            EditorPrefs.DeleteKey(PreviewTransformReturnToolPrefKey);
            WhimTexColorInputs.Reset();
            WhimTexUserSettings.Reset();
            foreach (TextureCompositorWindow window in windows)
                window.ResetEditorWindowSettings();
            notificationWindow?.ShowNotification(new GUIContent("WhimTex settings reset."));
        }

        private void ResetEditorWindowSettings()
        {
            StopLiveOutput();
            CancelPreviewEyedropper();
            Undo.ClearUndo(this);
            CancelPreviewZoomGesture();
            previewViewport.Reset();
            ClearLayerDragData();
            ClearToolkitDropIndicator();
            settingsPaneWidth = DefaultSettingsPaneWidth;
            layerSettingsPaneHeight = DefaultLayerSettingsPaneHeight;
            paintingPreviewScale = DefaultPaintingPreviewScale;
            previewChannels = AllPreviewChannels;
            previewDebug = false;
            previewExposure = 0f;
            transformSettingsExpanded = false;
            colorSettingsExpanded = false;
            layerPropertiesExpanded = true;
            layerFxExpanded = false;
            tiledPreview = false;
            ReleasePostFx();
            postFxEnabled = false;
            postFxExpanded = true;
            brushesExpanded = false;
            uvEnabled = uvExpanded = false;
            uvLineColor = new Color(.35f, .85f, 1f, 1f);
            uvLineOpacity = .65f;
            postFxSettings = new PostFxPreviewSettings();
            postFxMessage = null;
            postFxFailed = false;
            scrollPosition = Vector2.zero;
            SelectOnlyLayer(null);
            groupExpansion?.Clear();
            previewTool = PreviewTool.None;
            previewSettingsTool = PreviewTool.None;
            previewTransformReturnTool = PreviewTool.None;
            paintSettings?.ReleasePresetTip();
            paintSettings = new PaintToolSettings();
            selectedBrushPreset = selectedBrushPresetSnapshot = null;
            lineAnchorLayer = null;
            hasLastPaintingUv = false;
            paintingShiftHeld = false;
            paintingLockedAxis = 0;
            ReleasePreview();
            ReleaseEffectCache();
            CreateGUI();
            RequestPreview(true);
            Repaint();
        }

        public static void Open(TextureCompositor target)
        {
            OpenReferencedDocument(target);
        }

        private void OnEnable()
        {
            RestoreSourceImage();
            RefreshDocumentTitle(true);
            previewExposure = 0f;
            LoadPreviewToolSettings();
            LoadPaintToolSettings();
            EditorApplication.delayCall += RestoreBrushTipAfterReload;
            EditorApplication.projectChanged += RestoreBrushTipAfterReload;
            minSize = new Vector2(640f, 420f);
            groupExpansion = new Dictionary<string, bool>();
            paintingPreviewScale = ClampPaintingPreviewScale(
                EditorPrefs.GetFloat(PaintingPreviewScalePrefKey, DefaultPaintingPreviewScale));
            TextureCompositor.Changed += OnCompositorChanged;
            TextureCompositor.OutputTextureChanged += OnOutputTextureChanged;
            WhimTexUserSettings.Changed += OnPreviewAppearanceChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopLiveOutput;
            EditorApplication.quitting += StopLiveOutput;
            WhimTexDocumentSession.StateChanged += RefreshLiveOutputButton;
            EditorApplication.projectChanged += OnLiveOutputProjectChanged;

            if (compositor == null)
                SetCompositor(CreateTemporaryCompositor());
            else
                compositor.NormalizeModel();
            WhimTexDocumentService.Attach(this, compositor);
            UpdateUnsavedChangesState();
            RequestPreview(true);

            if (focusedWindow == this)
                SuppressUnityShortcuts();
        }

        private void OnDisable()
        {
            WhimTexDocumentService.Detach(this);
            StopKeyboardNudge();
            CancelImageUrlPaste();
            uvMap = null; uvCachedMesh = null; uvCachedDocument = null;
            WhimTexApi.CloseLiveSession(agentSessionId);
            ReleaseBrushStrokePreview();
            EditorApplication.delayCall -= RestoreBrushTipAfterReload;
            EditorApplication.projectChanged -= RestoreBrushTipAfterReload;
            StopLiveOutput();
            CancelPreviewEyedropper();
            CancelPreviewZoomGesture();
            FinishPreviewTransform();
            RestoreUnityShortcuts();
            FinishPaintingStroke();
            ResetAreaSelection();
            paintSettings?.ReleasePresetTip();
            TextureCompositor.Changed -= OnCompositorChanged;
            TextureCompositor.OutputTextureChanged -= OnOutputTextureChanged;
            WhimTexUserSettings.Changed -= OnPreviewAppearanceChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopLiveOutput;
            EditorApplication.quitting -= StopLiveOutput;
            WhimTexDocumentSession.StateChanged -= RefreshLiveOutputButton;
            EditorApplication.projectChanged -= OnLiveOutputProjectChanged;
            ClearLayerDragData();
            ReleasePreview();
            ReleaseEffectCache();
            compositor?.ReleaseLayerThumbnails();
            toolkitPreviewCanvas?.ReleaseCheckerTexture();
            toolkitPreviewCanvas?.ReleaseToolCursor();
            ReleasePostFx();
        }

        private void OnFocus()
        {
            RecordAgentFocus();
            SuppressUnityShortcuts();
            RestoreBrushTipAfterReload();
        }

        private void OnPreviewAppearanceChanged()
        {
            toolkitPreviewCanvas?.RefreshBackdropVisibility();
            toolkitHeaderBindings.Refresh();
            previewGuideOverlay?.MarkDirtyRepaint();
            postFxDirty = true;
            postFxBackgroundField?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackground);
            refreshPostFxFields?.Invoke();
            toolkitPreviewCanvas?.RefreshCheckerColors();
            if (previewDebug)
            {
                UpdateChannelPreview();
                UpdateToolkitPreviewPresentation();
            }
        }

        private void OnDestroy()
        {
            ClearDocumentFile();
            ClearSourceImage();
            if (compositor != null && !AssetDatabase.Contains(compositor))
                DestroyImmediate(compositor);
            compositor = null;
        }

        private void UpdateUnsavedChangesState()
        {
            RefreshDocumentTitle();
            RefreshLiveOutputButton();
            hasUnsavedChanges = HasDocumentChanges() || paintingLayer != null ||
                 previewTransformManipulator != null && previewTransformManipulator.IsDragging;
            saveChangesMessage = "Save this WhimTex document before closing?\n\n" +
                "Save opens Save As to choose a file. Discard closes without saving. Cancel keeps the window open.";
            RefreshDocumentSaveControls();
        }

        private bool HasDocumentChanges() => compositor != null &&
            (AssetDatabase.Contains(compositor) ? compositor.HasUnsavedAssetChanges() : temporaryDocumentDirty || compositor.documentBinding?.dirty == true);

        public override void SaveChanges()
        {
            if (!SaveDocument())
                return;
            temporaryDocumentDirty = false;
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            StopLiveOutput();
            temporaryDocumentDirty = false;
            base.DiscardChanges();
        }

        private void OnLostFocus()
        {
            StopKeyboardNudge();
            ClearLayerDragGhost();
            ClearPreviewPointerCursor();
            areaSelectionManipulator?.Cancel();
            if (!OwnsScreenEyedropper) CancelPreviewEyedropper();
            CancelPreviewZoomGesture();
            ResetOpacityEntry();
            FinishPreviewTransform();
            RestoreUnityShortcuts();
        }

        private void SuppressUnityShortcuts()
        {
            if (ownsUnityShortcutSuppression)
                return;
            ownsUnityShortcutSuppression = AcquireUnityShortcutSuppression();
        }

        private void RestoreUnityShortcuts()
        {
            if (!ownsUnityShortcutSuppression)
                return;
            ownsUnityShortcutSuppression = false;
            ReleaseUnityShortcutSuppression();
        }

        private static bool AcquireUnityShortcutSuppression()
        {
            if (UnityShortcutsEnabledProperty == null)
            {
                LogShortcutSuppressionWarning("Unity shortcut integration API is unavailable.");
                return false;
            }

            try
            {
                if (unityShortcutSuppressionOwners == 0)
                {
                    restoreUnityShortcutsEnabled = (bool)UnityShortcutsEnabledProperty.GetValue(null);
                    if (restoreUnityShortcutsEnabled)
                        UnityShortcutsEnabledProperty.SetValue(null, false);
                }
                unityShortcutSuppressionOwners++;
                return true;
            }
            catch (Exception exception)
            {
                LogShortcutSuppressionWarning(exception.Message);
                return false;
            }
        }

        private static void ReleaseUnityShortcutSuppression()
        {
            if (unityShortcutSuppressionOwners <= 0)
                return;

            unityShortcutSuppressionOwners--;
            if (unityShortcutSuppressionOwners > 0)
                return;

            try
            {
                if (restoreUnityShortcutsEnabled && UnityShortcutsEnabledProperty != null)
                    UnityShortcutsEnabledProperty.SetValue(null, true);
            }
            catch (Exception exception)
            {
                LogShortcutSuppressionWarning(exception.Message);
            }
            finally
            {
                restoreUnityShortcutsEnabled = false;
            }
        }

        private static void LogShortcutSuppressionWarning(string details)
        {
            if (shortcutSuppressionWarningLogged)
                return;
            shortcutSuppressionWarningLogged = true;
            Debug.LogWarning($"WhimTex could not isolate Unity shortcuts: {details}");
        }

        private void Update()
        {
            UpdatePostFx();
            RequestEffectRefinement();
            UpdateUnsavedChangesState();
            if (toolkitRefreshRequested)
                RefreshToolkitInterface();
            if (toolkitSettingsScroll != null)
                scrollPosition = toolkitSettingsScroll.scrollOffset;
            if (!previewRequested || EditorApplication.timeSinceStartup < previewAt)
                return;

            previewRequested = false;
            bool paintingPreview = paintingLayer != null;
            UpdatePreview();
            if (previewTransformManipulator != null && previewTransformManipulator.IsDragging)
                nextTransformPreviewAt = EditorApplication.timeSinceStartup + PaintingPreviewInterval;
            if (paintingPreview)
                nextPaintingPreviewAt = EditorApplication.timeSinceStartup + PaintingPreviewInterval;
        }

        private Layer GetDraggedLayer() => GetDraggedLayerForDocument(compositor);

        internal static Layer GetDraggedLayerForDocument(TextureCompositor document)
        {
            TextureCompositor draggedCompositor =
                DragAndDrop.GetGenericData(DraggedCompositorIdKey) as TextureCompositor;
            if (draggedCompositor == null || document == null || draggedCompositor != document)
                return null;

            string draggedLayerId = DragAndDrop.GetGenericData(DraggedLayerIdKey) as string;
            return document.FindLayer(draggedLayerId);
        }

        internal static void ClearDraggedLayerReference()
        {
            DragAndDrop.SetGenericData(DraggedLayerIdKey, null);
            DragAndDrop.SetGenericData(DraggedLayersKey, null);
            DragAndDrop.SetGenericData(DraggedCompositorIdKey, null);
        }

        private bool CanDropLayer(Layer layer, List<Layer> destinationContainer, int destinationIndex)
        {
            List<Layer> dragged = GetDraggedRoots();
            if (dragged != null)
                return dragged.Count == 1
                    ? TryResolveLayerDrop(dragged[0], destinationContainer, destinationIndex, out _, out _, out _)
                    : CanDropLayers(dragged, destinationContainer);
            return TryResolveLayerDrop(
                layer,
                destinationContainer,
                destinationIndex,
                out _,
                out _,
                out _);
        }

        private bool TryResolveLayerDrop(
            Layer layer,
            List<Layer> destinationContainer,
            int destinationIndex,
            out List<Layer> sourceContainer,
            out int sourceIndex,
            out int normalizedDestinationIndex)
        {
            sourceContainer = null;
            sourceIndex = -1;
            normalizedDestinationIndex = -1;
            if (layer == null ||
                destinationContainer == null ||
                !compositor.TryFindLayer(layer, out sourceContainer, out sourceIndex))
            {
                return false;
            }

            if (layer?.AsGroup() is Layer group && ContainsLayerContainer(group, destinationContainer))
                return false;

            normalizedDestinationIndex = Mathf.Clamp(destinationIndex, 0, destinationContainer.Count);
            if (ReferenceEquals(sourceContainer, destinationContainer) && sourceIndex < normalizedDestinationIndex)
                normalizedDestinationIndex--;

            return !ReferenceEquals(sourceContainer, destinationContainer) ||
                   normalizedDestinationIndex != sourceIndex;
        }

        private static bool ContainsLayerContainer(Layer group, List<Layer> candidateContainer)
        {
            if (group == null || group.layers == null)
                return false;
            if (ReferenceEquals(group.layers, candidateContainer))
                return true;

            for (int i = 0; i < group.layers.Count; i++)
            {
                if (group.layers[i]?.AsGroup() is Layer nestedGroup &&
                    ContainsLayerContainer(nestedGroup, candidateContainer))
                {
                    return true;
                }
            }
            return false;
        }

        private void PerformLayerDrop(
            Layer layer,
            List<Layer> destinationContainer,
            int destinationIndex,
            Layer groupToExpand)
        {
            List<Layer> dragged = GetDraggedRoots();
            if (dragged != null)
            {
                PerformSelectedLayersDrop(dragged, destinationContainer, destinationIndex, groupToExpand);
                return;
            }
            if (!TryResolveLayerDrop(
                    layer,
                    destinationContainer,
                    destinationIndex,
                    out List<Layer> sourceContainer,
                    out int sourceIndex,
                    out int normalizedDestinationIndex))
            {
                return;
            }

            ExecuteModelChange("Move Sprite Layer", () =>
            {
                sourceContainer.RemoveAt(sourceIndex);
                normalizedDestinationIndex = Mathf.Clamp(
                    normalizedDestinationIndex,
                    0,
                    destinationContainer.Count);
                destinationContainer.Insert(normalizedDestinationIndex, layer);
                if (groupToExpand != null)
                    groupExpansion[groupToExpand.Id] = true;
            });
        }

        private void ClearLayerDragData()
        {
            ClearLayerDragGhost();
            layerDragAutoScroll?.Stop();
            ClearFooterDropIndicator();
            activeLayerDrag?.Cancel();
            ClearDraggedLayerReference();
            toolkitSettingsBindings?.Refresh(true);
        }

        private void ClearDrawingLayer(DrawingLayerBehaviour layer)
        {
            if (layer == null || compositor == null || WhimTexApi.IsLayerContentLocked(compositor, layer))
                return;
            if (lineAnchorLayer == layer)
                lineAnchorLayer = null;
            Undo.RecordObject(compositor, "Clear Drawing Layer");
            layer.PrepareStroke(compositor.width, compositor.height, "Clear Drawing Layer");
            layer.ClearSurface(compositor.width, compositor.height);
            temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
            compositor.MarkChanged();
            RequestPreview(true);
        }

        private void RefreshPreviewDuringPainting()
        {
            double now = EditorApplication.timeSinceStartup;
            double requestedAt = Math.Max(now, nextPaintingPreviewAt);
            if (!previewRequested || requestedAt < previewAt)
                previewAt = requestedAt;
            previewRequested = true;
            toolkitPreviewCanvas?.MarkDirtyRepaint();
        }

        private void FinishPaintingStroke()
        {
            int capturedPointer = paintingPointerId;
            DrawingLayerBehaviour finishedLayer = paintingLayer;
            paintingLayer = null;
            paintingErase = false;
            paintingMouseButton = -1;
            paintingPointerId = -1;
            hasLastPaintingUv = false;
            paintingShiftHeld = false;
            paintingLockedAxis = 0;
            paintingPointerMoved = false;
            paintingGuideIndex = -1;
            if (capturedPointer >= 0 && toolkitPreviewCanvas != null && toolkitPreviewCanvas.HasPointerCapture(capturedPointer))
                toolkitPreviewCanvas.ReleasePointer(capturedPointer);

            if (finishedLayer == null)
                return;

            finishedLayer.EndStroke();
            if (!ReferenceEquals(finishedLayer.Owner.Behaviour, finishedLayer) || compositor == null ||
                !ReferenceEquals(compositor.FindLayer(finishedLayer.Id), finishedLayer.Owner))
            {
                lineAnchorLayer = null;
                return;
            }
            finishedLayer.SyncSurfaceToTexture();
            nextPaintingPreviewAt = 0d;
            temporaryDocumentDirty |= compositor != null && !AssetDatabase.Contains(compositor);
            if (compositor != null)
                compositor.MarkChanged();
            Undo.FlushUndoRecordObjects();
            RequestPreview(true);
        }

        private bool TryMapPreviewToLayerUv(
            Vector2 mousePosition,
            Rect imageRect,
            DrawingLayerBehaviour layer,
            out Vector2 sourceUv,
            bool allowOutside = false)
        {
            sourceUv = default;
            if (imageRect.width <= 0f || imageRect.height <= 0f || layer == null)
                return false;

            if (toolkitPreviewCanvas != null) mousePosition = toolkitPreviewCanvas.ToCanvas(mousePosition);
            Vector2 documentUv = new Vector2(
                (mousePosition.x - imageRect.x) / imageRect.width,
                1f - (mousePosition.y - imageRect.y) / imageRect.height);
            if (tiledPreview)
            {
                if (!TiledCanvasUtility.IsInvertible(compositor.GetCanvasTransform(layer))) return false;
                sourceUv = TiledCanvasUtility.ToSource(documentUv, compositor.GetCanvasTransform(layer), compositor.width, compositor.height);
                return !float.IsNaN(sourceUv.x) && !float.IsNaN(sourceUv.y) &&
                       !float.IsInfinity(sourceUv.x) && !float.IsInfinity(sourceUv.y);
            }
            bool inside = TryMapDocumentToLayerUv(documentUv, layer, out sourceUv);
            return inside || allowOutside && !float.IsNaN(sourceUv.x) && !float.IsNaN(sourceUv.y) &&
                !float.IsInfinity(sourceUv.x) && !float.IsInfinity(sourceUv.y);
        }

        private bool TryMapDocumentToLayerUv(Vector2 documentUv, DrawingLayerBehaviour layer, out Vector2 sourceUv)
        {
            sourceUv = compositor.GetCanvasTransform(layer).Unmap(documentUv, new Vector2(compositor.width, compositor.height));
            return sourceUv.x >= 0f && sourceUv.x <= 1f && sourceUv.y >= 0f && sourceUv.y <= 1f;
        }

        private Vector2 MapLayerToDocumentUv(Vector2 sourceUv, DrawingLayerBehaviour layer) =>
            compositor.GetCanvasTransform(layer).Map(sourceUv, new Vector2(compositor.width, compositor.height));

        private void RememberPaintingPoint(Vector2 sourceUv)
        {
            lastPaintingUv = sourceUv;
            lastPaintingDocumentUv = MapLayerToDocumentUv(sourceUv, paintingLayer);
            lineAnchorLayer = paintingLayer;
            lineAnchorUv = sourceUv;
            lineAnchorCanvasSize = new Vector2Int(compositor.width, compositor.height);
        }

        private void ShowAddMenuForSelection()
        {
            Layer selected = GetSelectedLayer();
            if (selected != null && compositor.TryFindLayer(selected, out List<Layer> container, out int index))
                ShowAddMenu(container, index);
            else
                ShowAddMenu(compositor.layers, 0);
        }

        private void AddDrawingLayerForSelection()
        {
            if (compositor == null) return;
            FinishPreviewTransform();
            FinishPaintingStroke();
            Layer selected = GetSelectedLayer();
            if (selected != null && compositor.TryFindLayer(selected, out List<Layer> container, out int index))
                AddLayer(container, index, new DrawingLayerBehaviour());
            else
                AddLayer(compositor.layers, 0, new DrawingLayerBehaviour());
        }

        private void ShowAddMenu(List<Layer> container, int insertionIndex)
        {
            GenericMenu menu = new GenericMenu();
            int section = 0;
            foreach (var descriptor in LayerTypeRegistry.Entries)
            {
                if (section != descriptor.Section) menu.AddSeparator(string.Empty);
                section = descriptor.Section;
                menu.AddItem(new GUIContent(descriptor.MenuName), false,
                    () => AddLayer(container, insertionIndex, descriptor.CreateLayer()));
            }
            menu.ShowAsContext();
        }

        private void AddLayer(List<Layer> container, int insertionIndex, Layer layer, string namePrefix = null)
        {
            ExecuteModelChange("Add Sprite Layer", () =>
            {
                layer.layerName = compositor.AllocateLayerName(layer, namePrefix);
                if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                    drawing.InitializeCanvas(compositor.width, compositor.height);
                insertionIndex = Mathf.Clamp(insertionIndex, 0, container.Count);
                container.Insert(insertionIndex, layer);
                compositor.NormalizeModel();
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour) compositor.AddEmbeddedShaderFX(layer);
                SelectOnlyLayer(layer.Id);
                if (layer?.IsGroup == true)
                    groupExpansion[layer.Id] = true;
            });
        }

        private void GroupSelectedLayer()
        {
            GroupLayers(GetSelectedRoots());
        }

        private void GroupLayers(List<Layer> selected)
        {
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (selected.Count == 0 || !compositor.TryFindLayer(selected[0], out List<Layer> container, out _))
                return;

            foreach (Layer layer in selected)
            {
                while (!ContainerContainsLayer(container, layer))
                {
                    if (!compositor.TryFindParentGroup(container, out _, out List<Layer> parent, out _))
                        return;
                    container = parent;
                }
            }
            int index = 0;
            while (index < container.Count && container[index] != selected[0] &&
                !(container[index]?.AsGroup() is Layer parentGroup && ContainerContainsLayer(parentGroup.layers, selected[0])))
                index++;
            ExecuteContextChange("Group Sprite Layers", () =>
            {
                Layer group = new GroupLayerBehaviour { layerName = compositor.AllocateGroupName() };
                foreach (Layer layer in selected) compositor.PreserveTransformForMove(layer, container);
                foreach (Layer layer in selected)
                    if (compositor.TryFindLayer(layer, out List<Layer> source, out _))
                        source.Remove(layer);
                group.layers.AddRange(selected);
                container.Insert(Mathf.Min(index, container.Count), group);
                compositor.NormalizeModel();
                SelectOnlyLayer(group.Id);
                groupExpansion[group.Id] = true;
            });
        }

        private void ShowLayerContextMenu(Layer layer, List<Layer> container, int index)
        {
            var selected = new HashSet<Layer>(GetSelectedParameterLayers(layer));
            List<Layer> targets = LayerSelectionOperations.Collect(compositor.layers, selected, false);
            List<Layer> roots = LayerSelectionOperations.Collect(compositor.layers, selected, true);
            if (roots.Count == 0) return;
            GenericMenu menu = new GenericMenu();
            if (LayerSelectionOperations.Move(compositor, roots, -1, false))
                menu.AddItem(new GUIContent("Move Up"), false, () => MoveContextLayers(roots, -1));
            else
                menu.AddDisabledItem(new GUIContent("Move Up"));
            if (LayerSelectionOperations.Move(compositor, roots, 1, false))
                menu.AddItem(new GUIContent("Move Down"), false, () => MoveContextLayers(roots, 1));
            else
                menu.AddDisabledItem(new GUIContent("Move Down"));

            menu.AddSeparator(string.Empty);
            if (LayerSelectionOperations.PlanGroupMoves(compositor, roots, true).Count > 0)
                menu.AddItem(new GUIContent("Move Into Group Above"), false, () => MoveContextLayersAcrossGroups(roots, true));
            else
                menu.AddDisabledItem(new GUIContent("Move Into Group Above"));

            if (LayerSelectionOperations.PlanGroupMoves(compositor, roots, false).Count > 0)
                menu.AddItem(new GUIContent("Move Out Of Group"), false, () => MoveContextLayersAcrossGroups(roots, false));
            else
                menu.AddDisabledItem(new GUIContent("Move Out Of Group"));

            menu.AddItem(new GUIContent("Group Selected"), false, () => GroupLayers(roots));
            if (roots.Exists(WhimTexApi.ContainsReservation))
            {
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("Content reserved for agent"));
                foreach (var locked in targets)
                    if (WhimTexApi.IsLayerContentLocked(compositor, locked))
                    {
                        menu.AddItem(new GUIContent("Cancel Agent Edit"), false, () =>
                        {
                            foreach (var target in targets) WhimTexApi.CancelLayerEdit(compositor, target);
                        });
                        break;
                    }
                menu.AddItem(new GUIContent("Delete / Cancel Generation"), false, () => DeleteLayers(roots));
                menu.ShowAsContext();
                return;
            }
            if (LayerSelectionOperations.CanApplyChannelPreset(targets.Count))
                menu.AddItem(new GUIContent("Assign Channels", "Top to bottom. 1–3 layers: RGB with alpha 1 and upper-layer Add. 4 layers: RGBA Swizzle only, other channels zero."),
                    false, () => ApplyContextChannelPreset(targets));
            else menu.AddDisabledItem(new GUIContent("Assign Channels"));
            var clippingTargets = targets.FindAll(target => !(target?.Behaviour is ShaderProcessorLayerBehaviour));
            bool allClipped = clippingTargets.Count > 0 && clippingTargets.TrueForAll(target => target.clippingMask);
            if (clippingTargets.Count == 0) menu.AddDisabledItem(new GUIContent("Clipping Mask"));
            else
            menu.AddItem(new GUIContent("Clipping Mask"), allClipped, () =>
                ExecuteContextChange("Change Clipping Mask", () =>
                {
                    foreach (Layer target in clippingTargets)
                        if (!WhimTexApi.IsLayerContentLocked(compositor, target) &&
                            compositor.TryFindLayer(target, out _, out _)) target.clippingMask = !allClipped;
                }));
            if (targets.Exists(target => target?.IsGroup == true))
            {
                menu.AddSeparator(string.Empty);
                foreach (var descriptor in LayerTypeRegistry.Entries)
                    menu.AddItem(new GUIContent("Add Inside/" + descriptor.InsideMenuName), false,
                        () => AddInsideContextGroups(targets, descriptor.CreateLayer));
                menu.AddItem(new GUIContent("Ungroup"), false, () => UngroupContextLayers(targets));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Convert to Drawing/Keep Transform"), false,
                () => ConvertLayersToDrawing(roots, false));
            menu.AddItem(new GUIContent("Convert to Drawing/Apply Transform"), false,
                () => ConvertLayersToDrawing(roots, true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Duplicate"), false, () => DuplicateLayers(roots));
            menu.AddItem(new GUIContent("Copy as Portable"), false, () =>
            {
                FinishPaintingStroke();
                FinishPreviewTransform();
                try
                {
                    string json = WhimTexApi.WritePortableClipboardReport(compositor, roots, out var warnings);
                    GUIUtility.systemCopyBuffer = json;
                    ShowNotification(new GUIContent("Portable layer JSON copied."));
                    if (warnings.Count > 0)
                        EditorUtility.DisplayDialog("Copied with warnings", string.Join("\n\n", warnings), "OK");
                }
                catch (Exception error) { EditorUtility.DisplayDialog("Copy as Portable", error.Message, "OK"); }
            });
            menu.AddItem(new GUIContent("Merge Selected %e"), false, () => MergeSelectedLayers(roots, false));
            menu.AddItem(new GUIContent("Merge Selected as Copy %&e"), false, () => MergeSelectedLayers(roots, true));
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteLayers(roots));
            if (targets.Exists(target => target?.Behaviour != null))
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("FX"), false, () =>
                {
                    foreach (Layer target in targets)
                        if (target?.Behaviour != null) ModifierEditorWindow.Open(target, compositor);
                });
            }
            if (targets.Exists(target => !(target?.IsGroup == true)))
            {
                menu.AddItem(new GUIContent("Properties"), false, () =>
                {
                    foreach (Layer target in targets) OpenLayerEditor(target);
                });
            }
            menu.ShowAsContext();
        }

        private const string GroupConversionWarning =
            "The group's visible children will be merged against transparency into one Drawing layer. " +
            "Pass-through blending with layers outside the group may change. Effects targeting individual " +
            "children will lose those targets. Groups have no active Transform, so both conversion modes " +
            "produce an identity Transform. You can undo the entire conversion.";

        private void ConvertLayerToDrawing(Layer layer, bool applyTransform, bool groupConfirmed = false)
            => ConvertLayersToDrawing(new List<Layer> { layer }, applyTransform, groupConfirmed);

        private void ConvertLayersToDrawing(List<Layer> layers, bool applyTransform, bool groupConfirmed = false)
        {
            if (layers.Exists(WhimTexApi.ContainsReservation))
            { ShowNotification(new GUIContent("Finish or cancel generation before converting these layers.")); return; }
            FinishPreviewTransform();
            FinishPaintingStroke();
            if (compositor == null || layers.Count == 0)
                return;
            if (layers.Exists(layer => layer?.IsGroup == true) && !groupConfirmed && !EditorUtility.DisplayDialog(
                "Convert Groups to Drawing",
                GroupConversionWarning,
                "Convert", "Cancel"))
                return;

            var textures = new List<Texture2D>();
            var replacements = new List<DrawingLayerBehaviour>();
            int undoGroup = -1;
            int registeredTextures = 0;
            string activeId = selectedLayerId;
            var previousSelection = new List<string>(selectedLayerIds);
            try
            {
                // Resolve every input before replacing any source or effect target.
                foreach (Layer layer in layers)
                {
                    if (!compositor.TryFindLayer(layer, out _, out _))
                        throw new InvalidOperationException("The selected layer is no longer in the document.");
                    Texture2D texture = compositor.RasterizeLayer(layer, applyTransform);
                    textures.Add(texture);
                    replacements.Add(DrawingLayerBehaviour.FromRasterizedLayer(layer, texture, applyTransform,
                        layer?.AsGroup() is Layer group && compositor.IsGroupIsolatedByClipping(group)));
                }
                string undoName = applyTransform ? "Convert to Drawing (Apply Transform)" : "Convert to Drawing (Keep Transform)";
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                for (int i = 0; i < layers.Count; i++)
                {
                    replacements[i].MakeTexturePersistent(compositor);
                    Undo.RegisterCreatedObjectUndo(textures[i], undoName);
                    registeredTextures++;
                }
                Undo.RegisterCompleteObjectUndo(compositor, undoName);
                for (int i = 0; i < layers.Count; i++)
                {
                    Layer layer = layers[i];
                    if (!compositor.TryFindLayer(layer, out List<Layer> container, out int index))
                        throw new InvalidOperationException("The selected layer is no longer in the document.");
                    compositor.DestroyLayerAssets(layer);
                    if (layer.IsGroup)
                        foreach (Layer child in layer.layers)
                            ReleaseConvertedChildren(child);
                    layer.AdoptContent(replacements[i]);
                }
                SelectContextLayers(layers, activeId);
                lineAnchorLayer = null;
                applyingToolkitChange = true;
                CommitModelChange();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch (Exception exception)
            {
                if (undoGroup >= 0)
                    Undo.RevertAllDownToGroup(undoGroup);
                for (int i = registeredTextures; i < textures.Count; i++)
                    if (textures[i] != null) DestroyImmediate(textures[i], true);
                selectedLayerIds = previousSelection;
                selectedLayerId = activeId;
                NormalizeLayerSelection();
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Cannot Convert Layer", exception.Message, "OK");
            }
            finally
            {
                if (undoGroup >= 0) Undo.IncrementCurrentGroup();
                applyingToolkitChange = false;
                RequestPreview(true);
                RefreshToolkitInterface(forceValues: true);
            }
        }

        private static void ReleaseConvertedChildren(Layer layer)
        {
            if (layer == null) return;
            layer.ReleaseTransientResources();
            if (layer.IsGroup)
                foreach (Layer child in layer.layers) ReleaseConvertedChildren(child);
        }

        private void OpenLayerEditor(Layer layer)
        {
            switch (layer?.Behaviour)
            {
                case DrawingLayerBehaviour drawingLayer:
                    DrawingLayerEditorWindow.Open(drawingLayer, compositor);
                    break;
                case FileLayerBehaviour fileLayer:
                    FileLayerEditorWindow.Open(fileLayer, compositor);
                    break;
                case ColorFillLayerBehaviour colorFillLayer:
                    ColorFillLayerEditorWindow.Open(colorFillLayer, compositor);
                    break;
                case GradientLayerBehaviour gradientLayer:
                    GradientLayerEditorWindow.Open(gradientLayer, compositor);
                    break;
                case NoiseLayerBehaviour noiseLayer:
                    NoiseLayerEditorWindow.Open(noiseLayer, compositor);
                    break;
                case ShapeLayerBehaviour shapeLayer:
                    ShapeLayerEditorWindow.Open(shapeLayer, compositor);
                    break;
                case OutlineLayerBehaviour outlineLayer:
                    OutlineLayerEditorWindow.Open(outlineLayer, compositor);
                    break;
                case SDFLayerBehaviour sdfLayer:
                    SDFLayerEditorWindow.Open(sdfLayer, compositor);
                    break;
                case NormalMapLayerBehaviour normalMap:
                    NormalMapLayerEditorWindow.Open(normalMap, compositor);
                    break;
                case BlurLayerBehaviour blur:
                    BlurLayerEditorWindow.Open(blur, compositor);
                    break;
                case MakeSeamlessLayerBehaviour seamless:
                    MakeSeamlessLayerEditorWindow.Open(seamless, compositor);
                    break;
                case ShaderProcessorLayerBehaviour processor:
                    ShaderProcessorLayerEditorWindow.Open(processor, compositor);
                    break;
            }
        }

        private Layer GetSelectedLayer()
        {
            return compositor == null ? null : compositor.FindLayer(selectedLayerId);
        }

        private bool GetGroupExpanded(Layer group)
        {
            groupExpansion ??= new Dictionary<string, bool>();
            if (!groupExpansion.TryGetValue(group.Id, out bool expanded))
            {
                expanded = true;
                groupExpansion.Add(group.Id, true);
            }
            return expanded;
        }

        private void ExecuteModelChange(string undoName, Action action)
        {
            if (compositor == null || action == null)
                return;

            Undo.RecordObject(compositor, undoName);
            applyingToolkitChange = true;
            try
            {
                action();
                CommitModelChange();
            }
            finally
            {
                applyingToolkitChange = false;
            }
            RefreshToolkitInterface();
        }

        private void CommitModelChange()
        {
            temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
            compositor.NormalizeModel();
            compositor.MarkChanged();
            RequestPreview();
        }

        private void RequestPreview(bool immediate = false)
        {
            immediate |= GetSelectedLayer()?.Behaviour is NoiseLayerBehaviour;
            double requestedAt = EditorApplication.timeSinceStartup + (immediate ? 0d : PreviewDelay);
            if (!previewRequested || requestedAt < previewAt)
                previewAt = requestedAt;
            previewRequested = true;
        }

        private void UpdatePreview()
        {
            if (outputDependencyDirty)
            {
                outputDependencyDirty = false;
                ReleaseEffectCache();
            }
            ReleasePreview(keepChannelBuffer: true);
            previewError = null;
            if (compositor == null)
            {
                ReleaseChannelPreview();
                return;
            }

            try
            {
                bool interactive = EffectsAreInteractive;
                int maxSize = previewTool == PreviewTool.Pencil || liveOutputEnabled && !interactive
                    ? Mathf.Max(compositor.width, compositor.height)
                    : paintingLayer != null ? GetPaintingPreviewMaxSize() : PreviewMaxSize;
                previewEffectCache ??= new EffectRenderCache();
                previewTexture = compositor.RenderCachedPreview(maxSize, previewEffectCache, interactive, paintingLayer);
                effectRefinementPending = interactive;
                ApplyPreviewTextureFilter();
                PublishLiveOutput();
                RenderPostFx();
                UpdateChannelPreview();
            }
            catch (Exception exception)
            {
                previewError = exception.Message;
                Debug.LogException(exception);
                ReleaseChannelPreview();
            }
            UpdateToolkitPreviewPresentation();
        }

        private int GetPaintingPreviewMaxSize()
        {
            return Mathf.Clamp(
                Mathf.RoundToInt(PreviewMaxSize * paintingPreviewScale),
                Mathf.RoundToInt(PreviewMaxSize * MinimumPaintingPreviewScale),
                PreviewMaxSize);
        }

        private static float ClampPaintingPreviewScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultPaintingPreviewScale;
            return Mathf.Clamp(value, MinimumPaintingPreviewScale, MaximumPaintingPreviewScale);
        }

        private void ReleasePreview(bool keepChannelBuffer = false)
        {
            postFxValid = false;
            postFxDirty = true;
            toolkitPreviewCanvas?.ClearTexture();
            if (!keepChannelBuffer)
                ReleaseChannelPreview();
            if (previewTexture == null)
                return;
            RenderTexture.ReleaseTemporary(previewTexture);
            previewTexture = null;
        }

        private TextureCompositor CreateTemporaryCompositor()
        {
            TextureCompositor result = CreateInstance<TextureCompositor>();
            result.name = "Untitled";
            result.hideFlags = HideFlags.HideAndDontSave;
            result.NormalizeModel();
            temporaryDocumentDirty = false;
            return result;
        }

        private void SetCompositor(TextureCompositor next)
        {
            if (next == null || next == compositor)
                return;

            if (HasPendingImageUrl) { CancelImageUrlPaste(); RemoveNotification(); }
            CancelPreviewEyedropper();
            CancelPreviewZoomGesture();
            previewViewport.Reset();
            FinishPreviewTransform();
            FinishPaintingStroke();
            ClearLayerDragData();
            lineAnchorLayer = null;
            TextureCompositor previous = compositor;
            previous?.ReleaseLayerThumbnails();
            if (agentSessionDocument != next) WhimTexApi.CloseLiveSession(agentSessionId);
            StopLiveOutput();
            ReleaseEffectCache();
            ResetAreaSelection();
            ClearPreviewGuides();
            previewGuidesDocument = next;
            ClearDocumentFile();
            compositor = next;
            WhimTexDocumentService.Attach(this, compositor);
            outputDependencyDirty = false;
            compositor.NormalizeModel();
            SelectOnlyLayer(null);
            temporaryDocumentDirty = false;
            groupExpansion?.Clear();
            RequestPreview(true);
            RefreshToolkitInterface();

            UpdateUnsavedChangesState();

            if (previous != null && !AssetDatabase.Contains(previous))
                DestroyImmediate(previous);
        }

        private bool ResolveUnsavedTemporaryDocument()
        {
            PrepareDocumentSave();
            if (!HasDocumentChanges())
                return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved WhimTex document",
                "Save the current compositor before replacing it?",
                "Save As",
                "Don't Save",
                "Cancel");
            if (choice == 0)
                return SaveDocument();
            return choice == 1;
        }

        private void PrepareDocumentSave()
        {
            rootVisualElement.Focus();
            FinishPreviewTransform();
            FinishPaintingStroke();
            Undo.FlushUndoRecordObjects();
        }

        private void ImportExportedTextureIfNeeded(string path, bool asSprite)
        {
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string assetsPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!fullPath.StartsWith(assetsPath + "/", StringComparison.OrdinalIgnoreCase))
                return;

            string assetPath = "Assets" + fullPath.Substring(assetsPath.Length);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                if (asSprite)
                    importer.spriteImportMode = SpriteImportMode.Single;
                importer.sRGBTexture = asSprite;
                importer.alphaIsTransparency = asSprite;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = compositor != null ? compositor.outputFilter : FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            UnityEngine.Object imported = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (imported == null)
                imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Selection.activeObject = imported;
            EditorGUIUtility.PingObject(imported);
        }

        private void OnCompositorChanged(TextureCompositor changedCompositor)
        {
            if (changedCompositor != compositor)
                return;

            if (paintingLayer != null && (!ReferenceEquals(paintingLayer.Owner.Behaviour, paintingLayer) ||
                !ReferenceEquals(compositor.FindLayer(paintingLayer.Id), paintingLayer.Owner)))
                FinishPaintingStroke();
            if (lineAnchorLayer != null && !ReferenceEquals(lineAnchorLayer.Owner.Behaviour, lineAnchorLayer))
                lineAnchorLayer = null;

            toolkitInspectorEffectTarget?.Invalidate();
            CancelPreviewEyedropper();
            if (TextureCompositor.IsRefreshingUndo)
            {
                OnUndoRedo();
                return;
            }

            temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
            effectInteractiveUntil = EditorApplication.timeSinceStartup + .2d;
            UpdateUnsavedChangesState();
            RequestPreview();
            if (!applyingToolkitChange)
            {
                ResetOpacityEntry();
                toolkitRefreshRequested = true;
            }
        }

        private void OnUndoRedo()
        {
            ReleaseEffectCache();
            ResetOpacityEntry();
            previewTransformManipulator?.End(false, false);
            gradientCanvasManipulator?.End(false, false);
            if (compositor == null)
                return;
            paintingLayer?.EndStroke();
            paintingLayer = null;
            hasLastPaintingUv = false;
            lineAnchorLayer = null;
            paintingShiftHeld = false;
            paintingLockedAxis = 0;
            paintingPointerId = -1;
            selectedLayerId = compositor.FindLayer(selectedLayerId)?.Id;
            temporaryDocumentDirty |= !AssetDatabase.Contains(compositor);
            RequestPreview(true);
            RefreshToolkitInterface(forceValues: true);
        }
    }
}
