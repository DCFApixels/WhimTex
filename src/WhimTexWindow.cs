using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow : EditorWindow, IHasCustomMenu
    {
        private const int CanvasMaxSize = 512;
        private const double CanvasDelay = 0.12d;
        private const double PaintingCanvasInterval = 1d / 30d;
        private const float DefaultPaintingCanvasScale = 1f;
        private const float MinimumPaintingCanvasScale = 0.125f;
        private const float MaximumPaintingCanvasScale = 1f;
        private const float DefaultSettingsPaneWidth = 400f;
        private const float DefaultLayerSettingsPaneHeight = 320f;
        private const float CanvasViewMinWidth = 200f;
        private const float SettingsPaneMinWidth = 320f;
        private const float PanePadding = 8f;
        private const string DraggedLayerIdKey = "DCFApixels.WhimTex.DraggedLayerId";
        private const string DraggedDocumentIdKey = "DCFApixels.WhimTex.DraggedDocumentId";
        private const string PaintingCanvasScalePrefKey = "DCFApixels.WhimTex.Canvas.PaintingScale";

        private static readonly Color DropIndicatorColor = new Color(0.20f, 0.58f, 0.95f, 1f);
        private static readonly Color GroupDropHighlightColor = new Color(0.20f, 0.58f, 0.95f, 0.22f);
        private static readonly GUIContent LiveCanvasQualityContent = new GUIContent(
            "Live Quality",
            "Resolution scale used while painting. Lower values make effect-heavy canvas updates faster. Outside painting, Canvas View is limited to 512 pixels, even at 100%. Select Pencil for full resolution; save and export always use full resolution.");
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

        [SerializeField] private WhimTexDocument activeDocument;
        [SerializeField] private string selectedLayerId;
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private float settingsPaneWidth = DefaultSettingsPaneWidth;
        [SerializeField] private float layerSettingsPaneHeight = DefaultLayerSettingsPaneHeight;

        [NonSerialized] private RenderTexture canvasTexture;
        [NonSerialized] private bool canvasRequested;
        [NonSerialized] private double canvasAt;
        [SerializeField] private bool temporaryDocumentDirty;
        [NonSerialized] private string canvasError;
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
        [NonSerialized] private double nextPaintingCanvasAt;
        [NonSerialized] private float paintingCanvasScale;
        [NonSerialized] private bool ownsUnityShortcutSuppression;

        [MenuItem("Window/WhimTex")]
        public static void ShowWindow()
        {
            var window = GetWindow<WhimTexWindow>("WhimTex");
            window.RefreshDocumentTitle(true);
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("User Settings…"), false, WhimTexUserSettingsWindow.Open);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save As WhimTex File…"), false, () => SaveDocumentAs(activeDocument));
            menu.AddItem(new GUIContent(WhimTexDocumentSession.IsLiveFor(activeDocument) ? "Stop Live Update" : "Start Live Update"),
                false, () => ToggleLiveUpdate(activeDocument));
        }

        internal static void ConfirmResetEditorSettings(EditorWindow notificationWindow)
        {
            if (!EditorUtility.DisplayDialog(
                "Reset WhimTex Settings",
                "Reset panel sizes, scrolling, selection, foldouts, RGBA channels and canvas tool state in all open " +
                "WhimTex windows, and remove the saved Live Quality preference?\n\n" +
                "Shared brush, color, fill, Canvas View appearance settings and the presets folder path will also be reset. Preset files will not be deleted. " +
                "Open documents (including unsaved work), layers, textures and Shader FX " +
                "will be preserved. Unity settings and window docking will not change. " +
                "This settings reset cannot be undone.",
                "Reset Settings", "Cancel"))
                return;

            WhimTexWindow[] windows = Resources.FindObjectsOfTypeAll<WhimTexWindow>();
            foreach (WhimTexWindow window in windows)
            {
                window.rootVisualElement.Focus();
                window.FinishCanvasTransform();
                window.FinishPaintingStroke();
            }
            Undo.FlushUndoRecordObjects();
            EditorPrefs.DeleteKey(PaintingCanvasScalePrefKey);
            EditorPrefs.DeleteKey(PaintToolSettingsPrefKey);
            EditorPrefs.DeleteKey(CanvasToolPrefKey);
            EditorPrefs.DeleteKey(CanvasTransformReturnToolPrefKey);
            WhimTexColorInputs.Reset();
            WhimTexUserSettings.Reset();
            foreach (WhimTexWindow window in windows)
                window.ResetEditorWindowSettings();
            notificationWindow?.ShowNotification(new GUIContent("WhimTex settings reset."));
        }

        private void ResetEditorWindowSettings()
        {
            StopLiveOutput();
            CancelCanvasEyedropper();
            Undo.ClearUndo(this);
            CancelCanvasZoomGesture();
            canvasViewport.Reset();
            ClearLayerDragData();
            ClearToolkitDropIndicator();
            settingsPaneWidth = DefaultSettingsPaneWidth;
            layerSettingsPaneHeight = DefaultLayerSettingsPaneHeight;
            paintingCanvasScale = DefaultPaintingCanvasScale;
            canvasChannels = AllCanvasChannels;
            canvasDebug = false;
            canvasExposure = 0f;
            transformSettingsExpanded = false;
            renderingSettingsExpanded = false;
            layerPropertiesExpanded = true;
            layerFxExpanded = false;
            tiledCanvas = false;
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
            canvasTool = CanvasTool.None;
            previousCanvasTool = null;
            canvasToolToggleKeyHeld = false;
            lastBaseCanvasTool = CanvasTool.None;
            temporaryReturnTool = CanvasTool.None;
            temporaryDocument = toolContextDocument = null;
            temporaryLayerId = toolContextLayerId = null;
            canvasTransformReturnTool = CanvasTool.None;
            paintSettings?.ReleasePresetTip();
            paintSettings = new PaintToolSettings();
            selectedBrushPreset = selectedBrushPresetSnapshot = null;
            lineAnchorLayer = null;
            hasLastPaintingUv = false;
            paintingShiftHeld = false;
            paintingLockedAxis = 0;
            ReleaseCanvasRender();
            ReleaseEffectCache();
            CreateGUI();
            RequestCanvasRender(true);
            Repaint();
        }

        public static void Open(WhimTexDocument target)
        {
            OpenReferencedDocument(target);
        }

        private void OnEnable()
        {
            RestoreSourceImage();
            RefreshDocumentTitle(true);
            canvasExposure = 0f;
            LoadCanvasToolSettings();
            LoadPaintToolSettings();
            EditorApplication.delayCall += RestoreBrushTipAfterReload;
            EditorApplication.projectChanged += RestoreBrushTipAfterReload;
            EditorApplication.projectChanged += CancelHealing;
            minSize = new Vector2(640f, 420f);
            groupExpansion = new Dictionary<string, bool>();
            paintingCanvasScale = ClampPaintingCanvasScale(
                EditorPrefs.GetFloat(PaintingCanvasScalePrefKey, DefaultPaintingCanvasScale));
            WhimTexDocument.Changed += OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged += OnDocumentRenderResourcesChanged;
            WhimTexDocument.LayerPreviewRequested += OnLayerPreviewRequested;
            WhimTexDocument.OutputTextureChanged += OnOutputTextureChanged;
            WhimTexUserSettings.Changed += OnCanvasViewAppearanceChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopLiveOutput;
            EditorApplication.quitting += StopLiveOutput;
            WhimTexDocumentSession.StateChanged += RefreshLiveOutputButton;
            EditorApplication.projectChanged += OnLiveOutputProjectChanged;

            if (activeDocument == null || AssetDatabase.Contains(activeDocument))
                SetDocument(CreateTemporaryDocument());
            else
                activeDocument.NormalizeModel();
            WhimTexDocumentService.Attach(this, activeDocument);
            UpdateUnsavedChangesState();
            RequestCanvasRender(true);

            if (focusedWindow == this)
                SuppressUnityShortcuts();
        }

        private void OnDisable()
        {
            WhimTexDocument.LayerPreviewRequested -= OnLayerPreviewRequested;
            toolkitLayerPreview?.Dispose();
            toolkitLayerPreview = null;
            WhimTexDocumentService.Detach(this);
            StopKeyboardNudge();
            CancelImageUrlPaste();
            uvMap = null; uvCachedMesh = null; uvCachedDocument = null;
            WhimTexApi.CloseLiveSession(agentSessionId);
            ReleaseBrushStrokePreview();
            EditorApplication.delayCall -= RestoreBrushTipAfterReload;
            EditorApplication.projectChanged -= RestoreBrushTipAfterReload;
            EditorApplication.projectChanged -= CancelHealing;
            StopLiveOutput();
            CancelCanvasEyedropper();
            CancelCanvasZoomGesture();
            FinishCanvasTransform();
            RestoreUnityShortcuts();
            FinishPaintingStroke();
            ResetAreaSelection();
            paintSettings?.ReleasePresetTip();
            WhimTexDocument.Changed -= OnDocumentChanged;
            WhimTexDocument.RenderResourcesChanged -= OnDocumentRenderResourcesChanged;
            WhimTexDocument.OutputTextureChanged -= OnOutputTextureChanged;
            WhimTexUserSettings.Changed -= OnCanvasViewAppearanceChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopLiveOutput;
            EditorApplication.quitting -= StopLiveOutput;
            WhimTexDocumentSession.StateChanged -= RefreshLiveOutputButton;
            EditorApplication.projectChanged -= OnLiveOutputProjectChanged;
            ClearLayerDragData();
            ReleaseCanvasRender();
            ReleaseEffectCache();
            activeDocument?.ReleaseLayerThumbnails();
            toolkitCanvas?.ReleaseCheckerTexture();
            toolkitCanvas?.ReleaseToolCursor();
            ReleasePostFx();
        }

        private void OnFocus()
        {
            RecordAgentFocus();
            SuppressUnityShortcuts();
            RestoreBrushTipAfterReload();
        }

        private void OnCanvasViewAppearanceChanged()
        {
            toolkitCanvas?.RefreshBackdropVisibility();
            toolkitHeaderBindings.Refresh();
            canvasGuideOverlay?.MarkDirtyRepaint();
            healingOverlay?.MarkDirtyRepaint();
            postFxDirty = true;
            postFxBackgroundField?.SetValueWithoutNotify(WhimTexUserSettings.PostFxBackground);
            refreshPostFxFields?.Invoke();
            toolkitCanvas?.RefreshCheckerColors();
            if (canvasDebug)
            {
                UpdateChannelCanvas();
                UpdateToolkitCanvasPresentation();
            }
        }

        private void OnDestroy()
        {
            ClearDocumentFile();
            ClearSourceImage();
            if (activeDocument != null && !AssetDatabase.Contains(activeDocument))
                DestroyImmediate(activeDocument);
            activeDocument = null;
        }

        private void UpdateUnsavedChangesState()
        {
            RefreshDocumentTitle();
            RefreshLiveOutputButton();
            hasUnsavedChanges = HasDocumentChanges() || paintingLayer != null ||
                 canvasTransformManipulator != null && canvasTransformManipulator.IsDragging;
            saveChangesMessage = "Save this WhimTex document before closing?\n\n" +
                "Save opens Save As to choose a file. Discard closes without saving. Cancel keeps the window open.";
            RefreshDocumentSaveControls();
        }

        private bool HasDocumentChanges() => activeDocument != null &&
            (temporaryDocumentDirty || activeDocument.documentBinding?.dirty == true);

        public override void SaveChanges()
        {
            if (!SaveDocument())
                return;
            temporaryDocumentDirty = false;
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            FinishCanvasTransform();
            FinishPaintingStroke();
            StopLiveOutput();
            temporaryDocumentDirty = false;
            base.DiscardChanges();
        }

        private void OnLostFocus()
        {
            canvasToolToggleKeyHeld = false;
            if (healingPointer >= 0) CancelHealing();
            StopKeyboardNudge();
            ClearLayerDragGhost();
            ClearCanvasPointerCursor();
            areaSelectionManipulator?.Cancel();
            if (!OwnsScreenEyedropper) CancelCanvasEyedropper();
            CancelCanvasZoomGesture();
            ResetOpacityEntry();
            FinishCanvasTransform();
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
            UpdateHealing();
            if (ReconcileCanvasToolContext()) toolkitRefreshRequested = true;
            UpdatePostFx();
            RequestEffectRefinement();
            UpdateUnsavedChangesState();
            if (toolkitRefreshRequested)
                RefreshToolkitInterface();
            if (toolkitSettingsScroll != null)
                scrollPosition = toolkitSettingsScroll.scrollOffset;
            if (!canvasRequested || EditorApplication.timeSinceStartup < canvasAt)
                return;

            canvasRequested = false;
            bool paintingCanvas = paintingLayer != null;
            // Limit update starts, not render completion + another full interval.
            // Otherwise an expensive composition lowers the stroke's refresh rate twice.
            double canvasStartedAt = EditorApplication.timeSinceStartup;
            UpdateCanvasRender();
            if (canvasTransformManipulator != null && canvasTransformManipulator.IsDragging)
                nextTransformCanvasAt = canvasStartedAt + PaintingCanvasInterval;
            if (paintingCanvas)
                nextPaintingCanvasAt = canvasStartedAt + PaintingCanvasInterval;
        }

        private Layer GetDraggedLayer() => GetDraggedLayerForDocument(activeDocument);

        internal static Layer GetDraggedLayerForDocument(WhimTexDocument document)
        {
            WhimTexDocument draggedDocument =
                DragAndDrop.GetGenericData(DraggedDocumentIdKey) as WhimTexDocument;
            if (draggedDocument == null || document == null || draggedDocument != document)
                return null;

            string draggedLayerId = DragAndDrop.GetGenericData(DraggedLayerIdKey) as string;
            return document.FindLayer(draggedLayerId);
        }

        internal static void ClearDraggedLayerReference()
        {
            DragAndDrop.SetGenericData(DraggedLayerIdKey, null);
            DragAndDrop.SetGenericData(DraggedLayersKey, null);
            DragAndDrop.SetGenericData(DraggedDocumentIdKey, null);
            DragAndDrop.SetGenericData(DraggedWindowKey, null);
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
                !activeDocument.TryFindLayer(layer, out sourceContainer, out sourceIndex))
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
            if (layer == null || activeDocument == null || WhimTexApi.IsLayerContentLocked(activeDocument, layer))
                return;
            if (lineAnchorLayer == layer)
                lineAnchorLayer = null;
            Undo.RecordObject(activeDocument, "Clear Drawing Layer");
            layer.PrepareStroke(activeDocument.width, activeDocument.height, "Clear Drawing Layer");
            layer.ClearSurface(activeDocument.width, activeDocument.height);
            temporaryDocumentDirty = true;
            activeDocument.MarkChanged();
            RequestCanvasRender(true);
        }

        private void RefreshCanvasDuringPainting()
        {
            double now = EditorApplication.timeSinceStartup;
            double requestedAt = Math.Max(now, nextPaintingCanvasAt);
            if (!canvasRequested || requestedAt < canvasAt)
                canvasAt = requestedAt;
            canvasRequested = true;
            toolkitCanvas?.MarkDirtyRepaint();
        }

        private void FinishPaintingStroke()
        {
            CancelHealing();
            if (blurSampleTexture != null)
            {
                RenderTexture.ReleaseTemporary(blurSampleTexture);
                blurSampleTexture = null;
            }
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
            if (capturedPointer >= 0 && toolkitCanvas != null && toolkitCanvas.HasPointerCapture(capturedPointer))
                toolkitCanvas.ReleasePointer(capturedPointer);

            if (finishedLayer == null)
                return;

            bool pixelsChanged = canvasTool != CanvasTool.SmudgeBrush || finishedLayer.SmudgeStrokeChanged;
            finishedLayer.EndStroke();
            if (!ReferenceEquals(finishedLayer.Owner.Behaviour, finishedLayer) || activeDocument == null ||
                !ReferenceEquals(activeDocument.FindLayer(finishedLayer.Id), finishedLayer.Owner))
            {
                lineAnchorLayer = null;
                return;
            }
            if (!pixelsChanged)
            {
                Undo.FlushUndoRecordObjects();
                return;
            }
            finishedLayer.SyncSurfaceToTexture();
            nextPaintingCanvasAt = 0d;
            if (canvasTool == CanvasTool.BlurBrush || canvasTool == CanvasTool.SmudgeBrush)
            {
                // Pixel retouching changes the sampled image. Once the stroke
                // ends, do not keep the surrounding FX stack in its
                // interactive approximation: the next preview must use settled
                // quality (notably for Sharpen layers above the Drawing layer).
                effectInteractiveUntil = 0d;
                effectRefinementPending = false;
            }
            temporaryDocumentDirty |= activeDocument != null;
            if (activeDocument != null)
                activeDocument.MarkChanged();
            Undo.FlushUndoRecordObjects();
            RequestCanvasRender(true);
        }

        private bool TryMapCanvasToLayerUv(
            Vector2 mousePosition,
            Rect imageRect,
            DrawingLayerBehaviour layer,
            out Vector2 sourceUv,
            bool allowOutside = false)
        {
            sourceUv = default;
            if (imageRect.width <= 0f || imageRect.height <= 0f || layer == null)
                return false;

            if (toolkitCanvas != null) mousePosition = toolkitCanvas.ToCanvas(mousePosition);
            Vector2 documentUv = new Vector2(
                (mousePosition.x - imageRect.x) / imageRect.width,
                1f - (mousePosition.y - imageRect.y) / imageRect.height);
            if (tiledCanvas)
            {
                if (!TiledCanvasUtility.IsInvertible(activeDocument.GetPaintTransform(layer))) return false;
                sourceUv = TiledCanvasUtility.ToSource(documentUv, activeDocument.GetPaintTransform(layer), activeDocument.width, activeDocument.height);
                return !float.IsNaN(sourceUv.x) && !float.IsNaN(sourceUv.y) &&
                       !float.IsInfinity(sourceUv.x) && !float.IsInfinity(sourceUv.y);
            }
            bool inside = TryMapDocumentToLayerUv(documentUv, layer, out sourceUv);
            return inside || allowOutside && !float.IsNaN(sourceUv.x) && !float.IsNaN(sourceUv.y) &&
                !float.IsInfinity(sourceUv.x) && !float.IsInfinity(sourceUv.y);
        }

        private bool TryMapDocumentToLayerUv(Vector2 documentUv, DrawingLayerBehaviour layer, out Vector2 sourceUv)
        {
            sourceUv = activeDocument.GetPaintTransform(layer).Unmap(documentUv, new Vector2(activeDocument.width, activeDocument.height));
            return sourceUv.x >= 0f && sourceUv.x <= 1f && sourceUv.y >= 0f && sourceUv.y <= 1f;
        }

        private Vector2 MapLayerToDocumentUv(Vector2 sourceUv, DrawingLayerBehaviour layer) =>
            activeDocument.GetPaintTransform(layer).Map(sourceUv, new Vector2(activeDocument.width, activeDocument.height));

        private void RememberPaintingPoint(Vector2 sourceUv)
        {
            lastPaintingUv = sourceUv;
            lastPaintingDocumentUv = MapLayerToDocumentUv(sourceUv, paintingLayer);
            lineAnchorLayer = paintingLayer;
            lineAnchorUv = sourceUv;
            lineAnchorCanvasSize = new Vector2Int(activeDocument.width, activeDocument.height);
        }

        private void ShowAddMenuForSelection()
        {
            Layer selected = GetSelectedLayer();
            if (selected != null && activeDocument.TryFindLayer(selected, out List<Layer> container, out int index))
                ShowAddMenu(container, index);
            else
                ShowAddMenu(activeDocument.layers, 0);
        }

        private void AddDrawingLayerForSelection()
        {
            if (activeDocument == null) return;
            FinishCanvasTransform();
            FinishPaintingStroke();
            Layer selected = GetSelectedLayer();
            if (selected != null && activeDocument.TryFindLayer(selected, out List<Layer> container, out int index))
                AddLayer(container, index, new DrawingLayerBehaviour());
            else
                AddLayer(activeDocument.layers, 0, new DrawingLayerBehaviour());
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
                layer.layerName = activeDocument.AllocateLayerName(layer, namePrefix);
                if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                    drawing.InitializeCanvas(activeDocument.width, activeDocument.height);
                insertionIndex = Mathf.Clamp(insertionIndex, 0, container.Count);
                container.Insert(insertionIndex, layer);
                activeDocument.NormalizeModel();
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour) activeDocument.AddEmbeddedShaderFX(layer);
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
            FinishCanvasTransform();
            FinishPaintingStroke();
            if (selected.Count == 0 || !activeDocument.TryFindLayer(selected[0], out List<Layer> container, out _))
                return;

            foreach (Layer layer in selected)
            {
                while (!ContainerContainsLayer(container, layer))
                {
                    if (!activeDocument.TryFindParentGroup(container, out _, out List<Layer> parent, out _))
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
                Layer group = new GroupLayerBehaviour { layerName = activeDocument.AllocateGroupName() };
                foreach (Layer layer in selected) activeDocument.PreserveTransformForMove(layer, container);
                foreach (Layer layer in selected)
                    if (activeDocument.TryFindLayer(layer, out List<Layer> source, out _))
                        source.Remove(layer);
                group.layers.AddRange(selected);
                container.Insert(Mathf.Min(index, container.Count), group);
                activeDocument.NormalizeModel();
                SelectOnlyLayer(group.Id);
                groupExpansion[group.Id] = true;
            });
        }

        private void ShowLayerContextMenu(Layer layer, List<Layer> container, int index)
        {
            var selected = new HashSet<Layer>(GetSelectedParameterLayers(layer));
            List<Layer> targets = LayerSelectionOperations.Collect(activeDocument.layers, selected, false);
            List<Layer> roots = LayerSelectionOperations.Collect(activeDocument.layers, selected, true);
            if (roots.Count == 0) return;
            GenericMenu menu = new GenericMenu();
            if (LayerSelectionOperations.Move(activeDocument, roots, -1, false))
                menu.AddItem(new GUIContent("Move Up"), false, () => MoveContextLayers(roots, -1));
            else
                menu.AddDisabledItem(new GUIContent("Move Up"));
            if (LayerSelectionOperations.Move(activeDocument, roots, 1, false))
                menu.AddItem(new GUIContent("Move Down"), false, () => MoveContextLayers(roots, 1));
            else
                menu.AddDisabledItem(new GUIContent("Move Down"));

            menu.AddSeparator(string.Empty);
            if (LayerSelectionOperations.PlanGroupMoves(activeDocument, roots, true).Count > 0)
                menu.AddItem(new GUIContent("Move Into Group Above"), false, () => MoveContextLayersAcrossGroups(roots, true));
            else
                menu.AddDisabledItem(new GUIContent("Move Into Group Above"));

            if (LayerSelectionOperations.PlanGroupMoves(activeDocument, roots, false).Count > 0)
                menu.AddItem(new GUIContent("Move Out Of Group"), false, () => MoveContextLayersAcrossGroups(roots, false));
            else
                menu.AddDisabledItem(new GUIContent("Move Out Of Group"));

            menu.AddItem(new GUIContent("Group Selected"), false, () => GroupLayers(roots));
            if (roots.Exists(WhimTexApi.ContainsReservation))
            {
                menu.AddSeparator(string.Empty);
                menu.AddDisabledItem(new GUIContent("Content reserved for agent"));
                foreach (var locked in targets)
                    if (WhimTexApi.IsLayerContentLocked(activeDocument, locked))
                    {
                        menu.AddItem(new GUIContent("Cancel Agent Edit"), false, () =>
                        {
                            foreach (var target in targets) WhimTexApi.CancelLayerEdit(activeDocument, target);
                        });
                        break;
                    }
                menu.AddItem(new GUIContent("Delete / Cancel Generation"), false, () => DeleteLayers(roots));
                menu.ShowAsContext();
                return;
            }
            if (LayerSelectionOperations.CanApplyChannelPreset(targets.Count))
                menu.AddItem(new GUIContent("Assign Channels", "Top to bottom. 1–3 layers: RGB with alpha 1 and upper-layer Add. 4 layers: RGBA ChannelMapping only, other channels zero."),
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
                        if (!WhimTexApi.IsLayerContentLocked(activeDocument, target) &&
                            activeDocument.TryFindLayer(target, out _, out _)) target.clippingMask = !allClipped;
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
            menu.AddItem(new GUIContent("Copy as JSON"), false, () =>
            {
                FinishPaintingStroke();
                FinishCanvasTransform();
                try
                {
                    string json = WhimTexApi.WritePortableClipboardReport(activeDocument, roots, out var warnings);
                    GUIUtility.systemCopyBuffer = json;
                    ShowNotification(new GUIContent("Layer JSON copied."));
                    if (warnings.Count > 0)
                        EditorUtility.DisplayDialog("Copy as JSON — warnings", string.Join("\n\n", warnings), "OK");
                }
                catch (Exception error) { EditorUtility.DisplayDialog("Copy as JSON", error.Message, "OK"); }
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
                        if (target?.Behaviour != null) LayerFxEditorWindow.Open(target, activeDocument);
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
            FinishCanvasTransform();
            FinishPaintingStroke();
            if (activeDocument == null || layers.Count == 0)
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
                    if (!activeDocument.TryFindLayer(layer, out _, out _))
                        throw new InvalidOperationException("The selected layer is no longer in the document.");
                    Texture2D texture = activeDocument.RasterizeLayer(layer, applyTransform);
                    textures.Add(texture);
                    replacements.Add(DrawingLayerBehaviour.FromRasterizedLayer(layer, texture, applyTransform,
                        layer?.AsGroup() is Layer group && activeDocument.IsGroupIsolatedByClipping(group)));
                }
                string undoName = applyTransform ? "Convert to Drawing (Apply Transform)" : "Convert to Drawing (Keep Transform)";
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                for (int i = 0; i < layers.Count; i++)
                {
                    Undo.RegisterCreatedObjectUndo(textures[i], undoName);
                    registeredTextures++;
                }
                Undo.RegisterCompleteObjectUndo(activeDocument, undoName);
                for (int i = 0; i < layers.Count; i++)
                {
                    Layer layer = layers[i];
                    if (!activeDocument.TryFindLayer(layer, out List<Layer> container, out int index))
                        throw new InvalidOperationException("The selected layer is no longer in the document.");
                    activeDocument.DestroyLayerAssets(layer);
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
                RequestCanvasRender(true);
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
                    DrawingLayerEditorWindow.Open(drawingLayer, activeDocument);
                    break;
                case FileLayerBehaviour fileLayer:
                    FileLayerEditorWindow.Open(fileLayer, activeDocument);
                    break;
                case ColorFillLayerBehaviour colorFillLayer:
                    ColorFillLayerEditorWindow.Open(colorFillLayer, activeDocument);
                    break;
                case GradientLayerBehaviour gradientLayer:
                    GradientLayerEditorWindow.Open(gradientLayer, activeDocument);
                    break;
                case NoiseLayerBehaviour noiseLayer:
                    NoiseLayerEditorWindow.Open(noiseLayer, activeDocument);
                    break;
                case ShapeLayerBehaviour shapeLayer:
                    ShapeLayerEditorWindow.Open(shapeLayer, activeDocument);
                    break;
                case OutlineLayerBehaviour outlineLayer:
                    OutlineLayerEditorWindow.Open(outlineLayer, activeDocument);
                    break;
                case SDFLayerBehaviour sdfLayer:
                    SDFLayerEditorWindow.Open(sdfLayer, activeDocument);
                    break;
                case NormalMapLayerBehaviour normalMap:
                    NormalMapLayerEditorWindow.Open(normalMap, activeDocument);
                    break;
                case BlurLayerBehaviour blur:
                    BlurLayerEditorWindow.Open(blur, activeDocument);
                    break;
                case SharpenLayerBehaviour sharpen:
                    SharpenLayerEditorWindow.Open(sharpen, activeDocument);
                    break;
                case MakeSeamlessLayerBehaviour seamless:
                    MakeSeamlessLayerEditorWindow.Open(seamless, activeDocument);
                    break;
                case ShaderProcessorLayerBehaviour processor:
                    ShaderProcessorLayerEditorWindow.Open(processor, activeDocument);
                    break;
            }
        }

        private Layer GetSelectedLayer()
        {
            return activeDocument == null ? null : activeDocument.FindLayer(selectedLayerId);
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
            if (activeDocument == null || action == null)
                return;

            Undo.RecordObject(activeDocument, undoName);
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
            temporaryDocumentDirty = true;
            activeDocument.NormalizeModel();
            activeDocument.MarkChanged();
            RequestCanvasRender();
        }

        private void RequestCanvasRender(bool immediate = false)
        {
            immediate |= GetSelectedLayer()?.Behaviour is NoiseLayerBehaviour;
            double requestedAt = EditorApplication.timeSinceStartup + (immediate ? 0d : CanvasDelay);
            if (!canvasRequested || requestedAt < canvasAt)
                canvasAt = requestedAt;
            canvasRequested = true;
        }

        private void OnLayerPreviewRequested(WhimTexDocument document)
        {
            if (document == activeDocument) RequestCanvasRender(true);
        }

        private void UpdateCanvasRender()
        {
            if (outputDependencyDirty)
            {
                outputDependencyDirty = false;
                ReleaseEffectCache();
            }
            ReleaseCanvasRender(keepChannelBuffer: true);
            canvasError = null;
            if (activeDocument == null)
            {
                ReleaseChannelCanvas();
                return;
            }

            try
            {
                bool interactive = EffectsAreInteractive;
                int maxSize = canvasTool == CanvasTool.Pencil
                    ? Mathf.Max(activeDocument.width, activeDocument.height)
                    : paintingLayer != null ? GetPaintingCanvasMaxSize() : CanvasMaxSize;
                canvasEffectCache ??= new EffectRenderCache();
                canvasTexture = activeDocument.RenderCanvasWithCache(maxSize, canvasEffectCache, interactive, paintingLayer);
                effectRefinementPending = interactive;
                ApplyCanvasTextureFilter();
                PublishLiveOutput();
                RenderPostFx();
                UpdateChannelCanvas();
            }
            catch (Exception exception)
            {
                canvasError = exception.Message;
                Debug.LogException(exception);
                ReleaseChannelCanvas();
            }
            UpdateToolkitCanvasPresentation();
        }

        private int GetPaintingCanvasMaxSize()
        {
            return Mathf.Clamp(
                Mathf.RoundToInt(CanvasMaxSize * paintingCanvasScale),
                Mathf.RoundToInt(CanvasMaxSize * MinimumPaintingCanvasScale),
                CanvasMaxSize);
        }

        private static float ClampPaintingCanvasScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultPaintingCanvasScale;
            return Mathf.Clamp(value, MinimumPaintingCanvasScale, MaximumPaintingCanvasScale);
        }

        private void ReleaseCanvasRender(bool keepChannelBuffer = false)
        {
            postFxValid = false;
            postFxDirty = true;
            toolkitCanvas?.ClearTexture();
            if (!keepChannelBuffer)
                ReleaseChannelCanvas();
            if (canvasTexture == null)
                return;
            RenderTexture.ReleaseTemporary(canvasTexture);
            canvasTexture = null;
        }

        private WhimTexDocument CreateTemporaryDocument()
        {
            WhimTexDocument result = CreateInstance<WhimTexDocument>();
            result.name = "Untitled";
            result.hideFlags = HideFlags.HideAndDontSave;
            result.NormalizeModel();
            temporaryDocumentDirty = false;
            return result;
        }

        private void SetDocument(WhimTexDocument next)
        {
            if (next != null && AssetDatabase.Contains(next))
                throw new System.InvalidOperationException("Editable documents must be in-memory models, not Unity assets.");
            if (next == null || next == activeDocument)
                return;

            if (HasPendingImageUrl) { CancelImageUrlPaste(); RemoveNotification(); }
            CancelCanvasEyedropper();
            CancelCanvasZoomGesture();
            canvasViewport.Reset();
            FinishCanvasTransform();
            FinishPaintingStroke();
            ClearLayerDragData();
            lineAnchorLayer = null;
            WhimTexDocument previous = activeDocument;
            previous?.ReleaseLayerThumbnails();
            if (agentSessionDocument != next) WhimTexApi.CloseLiveSession(agentSessionId);
            StopLiveOutput();
            ReleaseEffectCache();
            ResetAreaSelection();
            ClearCanvasGuides();
            canvasGuidesDocument = next;
            ClearDocumentFile();
            activeDocument = next;
            WhimTexDocumentService.Attach(this, activeDocument);
            outputDependencyDirty = false;
            activeDocument.NormalizeModel();
            SelectOnlyLayer(null);
            temporaryDocumentDirty = false;
            groupExpansion?.Clear();
            RequestCanvasRender(true);
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
                "Save the current document before replacing it?",
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
            FinishCanvasTransform();
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
                importer.filterMode = activeDocument != null ? activeDocument.outputFilter : FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            UnityEngine.Object imported = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (imported == null)
                imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Selection.activeObject = imported;
            EditorGUIUtility.PingObject(imported);
        }

        private void OnDocumentChanged(WhimTexDocument changedDocument)
            => OnDocumentUpdated(changedDocument, true);

        private void OnDocumentRenderResourcesChanged(WhimTexDocument changedDocument)
            => OnDocumentUpdated(changedDocument, false);

        private void OnDocumentUpdated(WhimTexDocument changedDocument, bool contentChanged)
        {
            if (changedDocument != activeDocument)
                return;
            if (healingJob != null || healingPointer >= 0) CancelHealing();

            if (paintingLayer != null && (!ReferenceEquals(paintingLayer.Owner.Behaviour, paintingLayer) ||
                !ReferenceEquals(activeDocument.FindLayer(paintingLayer.Id), paintingLayer.Owner)))
                FinishPaintingStroke();
            if (lineAnchorLayer != null && !ReferenceEquals(lineAnchorLayer.Owner.Behaviour, lineAnchorLayer))
                lineAnchorLayer = null;

            toolkitInspectorEffectTarget?.Invalidate();
            CancelCanvasEyedropper();
            if (contentChanged && WhimTexDocument.IsRefreshingUndo)
            {
                OnUndoRedo();
                return;
            }

            temporaryDocumentDirty |= contentChanged;
            if (!contentChanged) ReleaseEffectCache();
            effectInteractiveUntil = EditorApplication.timeSinceStartup + .2d;
            UpdateUnsavedChangesState();
            RequestCanvasRender();
            if (!applyingToolkitChange)
            {
                ResetOpacityEntry();
                toolkitRefreshRequested = true;
            }
        }

        private void OnUndoRedo()
        {
            CancelHealing();
            ReleaseEffectCache();
            ResetOpacityEntry();
            canvasTransformManipulator?.End(false, false);
            gradientCanvasManipulator?.End(false, false);
            if (activeDocument == null)
                return;
            paintingLayer?.EndStroke();
            paintingLayer = null;
            hasLastPaintingUv = false;
            lineAnchorLayer = null;
            paintingShiftHeld = false;
            paintingLockedAxis = 0;
            paintingPointerId = -1;
            selectedLayerId = activeDocument.FindLayer(selectedLayerId)?.Id;
            temporaryDocumentDirty = true;
            RequestCanvasRender(true);
            RefreshToolkitInterface(forceValues: true);
        }
    }
}
