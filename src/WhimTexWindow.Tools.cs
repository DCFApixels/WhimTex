using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum CanvasTool
        {
            None, Brush, BlurBrush, Transform, Fill, Zoom, Pencil, RectangleSelect, PolygonSelect, Shape,
            GradientHandles, UvIslandSelect, FXTransform, FXPoint, FXNormal, HealingBrush, SmudgeBrush, Text, Gradient, Crop, Comment,
            VectorPen, VectorNodes, VectorBrush, QuickSelection
        }

        [NonSerialized] private CanvasTool canvasTool = CanvasTool.None;
        [NonSerialized] private CanvasTool? previousCanvasTool;
        [NonSerialized] private bool canvasToolToggleKeyHeld;
        private const KeyCode CanvasToolToggleKey = KeyCode.Q;
        [NonSerialized] private CanvasTool canvasTransformReturnTool = CanvasTool.None;
        [NonSerialized] private PaintToolSettings paintSettings = new PaintToolSettings();
        [NonSerialized] private bool conversionPromptOpen;
        [NonSerialized] private RenderTexture blurSampleTexture;
        [NonSerialized] private float paintingPressure = 1f;
        [NonSerialized] private Button canvasNoneButton;
        [NonSerialized] private Button canvasBrushButton;
        [NonSerialized] private Button canvasBlurBrushButton;
        [NonSerialized] private Button canvasSmudgeBrushButton;
        [NonSerialized] private Button canvasPencilButton;
        [NonSerialized] private Button canvasTransformButton;
        [NonSerialized] private Button canvasFillButton;
        [NonSerialized] private Button canvasGradientButton;
        [NonSerialized] private Button canvasZoomButton;
        [NonSerialized] private Button canvasRectangleSelectButton;
        [NonSerialized] private Button canvasPolygonSelectButton;
        private ScrollView canvasToolScroll;

        private bool IsCanvasPaintTool => canvasTool == CanvasTool.Brush || canvasTool == CanvasTool.BlurBrush || canvasTool == CanvasTool.SmudgeBrush || canvasTool == CanvasTool.Pencil;
        private bool HasCanvasLayers
        {
            get
            {
                if (activeDocument != null && activeDocument.layers != null)
                    foreach (Layer layer in activeDocument.layers)
                        if (layer != null) return true;
                return false;
            }
        }
        private bool IsCanvasBrushEnabled => (canvasTool == CanvasTool.Brush || canvasTool == CanvasTool.Pencil) &&
            (canvasTool != CanvasTool.Brush || paintSettings.dynamics.source != BrushTipSource.HLSL || paintSettings.dynamics.HasTip) &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(activeDocument, layer);
        private bool IsCanvasBlurBrushEnabled => canvasTool == CanvasTool.BlurBrush &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(activeDocument, layer);
        private bool IsCanvasSmudgeBrushEnabled => canvasTool == CanvasTool.SmudgeBrush &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(activeDocument, layer);
        private bool IsCanvasFillEnabled => canvasTool == CanvasTool.Fill && GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(activeDocument, layer);

        private void ApplyCanvasTextureFilter()
        {
            FilterMode filter = canvasTool == CanvasTool.Pencil ? FilterMode.Point : activeDocument != null ? activeDocument.outputFilter : FilterMode.Bilinear;
            if (canvasTexture != null) canvasTexture.filterMode = filter;
            if (channelCanvasTexture != null) channelCanvasTexture.filterMode = filter;
            if (postFxTexture != null) postFxTexture.filterMode = filter;
        }

        private static CanvasTool ParseCanvasTool(string value)
        {
            return Enum.TryParse(value, out CanvasTool tool) && Enum.IsDefined(typeof(CanvasTool), tool) && IsBaseCanvasTool(tool)
                ? tool : CanvasTool.None;
        }

        private void RestoreBrushTipAfterReload()
        {
            brushStrokePreviewDirty = true;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall -= RestoreBrushTipAfterReload;
                EditorApplication.delayCall += RestoreBrushTipAfterReload;
                return;
            }
            if (paintSettings == null || !paintSettings.TryRestoreBrushTip()) return;
            brushSettingsBindings?.Refresh();
            UpdateToolkitCanvasPresentation();
        }

        private void ApplyPaintToolChange(Action change)
        {
            FinishPaintingStroke();
            change();
            paintSettings.dynamics.Normalize();
            paintSettings.smoothing.Normalize();
            SavePaintToolSettings();
            toolkitHeaderBindings.Refresh();
            brushSettingsBindings?.Refresh();
            UpdateToolkitCanvasPresentation();
            RefreshBrushPresetButton();
        }

        private void SavePaintToolSettings()
        {
            brushStrokePreviewDirty = true;
            SaveToolPreferences();
        }

        private PaintStrokeParameters GetPaintingParameters()
        {
            PaintStrokeParameters parameters = canvasTool == CanvasTool.Pencil
                ? paintSettings.GetScaledPencilParameters(paintingErase, GetPaintingColor(), CurrentPaintingPixelScale)
                : paintSettings.GetScaledStrokeParameters(paintingErase, GetPaintingColor(), CurrentPaintingPixelScale);
            if (canvasTool == CanvasTool.Brush && paintSettings.dynamics != null && paintSettings.dynamics.pressure)
                parameters = parameters.WithPressure(paintingPressure);
            if (tiledCanvas) parameters = parameters.WithCanvasWrap();
            return parameters.WithSelectionMask(GetAreaSelectionTexture());
        }

        private bool HandlePaintConversionPrompt(PointerDownEvent evt)
        {
            if (!HasCanvasLayers)
            {
                WhimTexUI.ConsumeEvent(evt);
                return true;
            }
            bool painting = (IsCanvasPaintTool && (evt.button == 0 || evt.button == 1 && canvasTool != CanvasTool.SmudgeBrush)) ||
                canvasTool == CanvasTool.HealingBrush && evt.button == 0;
            bool filling = canvasTool == CanvasTool.Fill && evt.button == 0;
            if ((!painting && !filling) || evt.altKey || activeDocument == null ||
                !CanvasContainsPaintPoint(evt.localPosition) || GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour && !WhimTexApi.IsLayerContentLocked(activeDocument, GetSelectedLayer()))
                return false;

            WhimTexUI.ConsumeEvent(evt);
            if (conversionPromptOpen) return true;
            conversionPromptOpen = true;
            try
            {
                Layer layer = GetSelectedLayer();
                if (layer != null && WhimTexApi.ContainsReservation(layer))
                {
                    ShowNotification(new GUIContent("This layer is reserved for the agent."));
                    return true;
                }
                if (layer == null)
                {
                    EditorUtility.DisplayDialog("No Layer Selected", "Select a layer before painting or filling.", "OK");
                    return true;
                }
                if (layer.Behaviour == null)
                {
                    ShowNotification(new GUIContent("Restore this layer's behaviour in Layer Settings before painting."));
                    return true;
                }
                string warning = layer.IsGroup ? "\n\n" + GroupConversionWarning : string.Empty;
                int choice = EditorUtility.DisplayDialogComplex("Convert to Drawing",
                    $"The selected layer '{layer.layerName}' is not a Drawing layer. Convert it to enable painting and filling?\n\n" +
                    "Keep Transform: preserve the current transform.\n" +
                    "Apply Transform: bake it into the pixels and reset the transform.\n\n" +
                    "This click will not paint or fill. Conversion can be undone." + warning,
                    "Keep Transform", "Cancel", "Apply Transform");
                if (choice == 0 || choice == 2)
                    ConvertLayerToDrawing(layer, choice == 2, groupConfirmed: true);
            }
            finally
            {
                conversionPromptOpen = false;
            }
            return true;
        }

        private bool IsCanvasToolAvailable(CanvasTool tool)
        {
            Layer layer = GetSelectedLayer();
            switch (tool)
            {
                case CanvasTool.Brush:
                case CanvasTool.Pencil:
                case CanvasTool.Fill: return layer?.Behaviour is DrawingLayerBehaviour;
                case CanvasTool.BlurBrush:
                case CanvasTool.SmudgeBrush: return layer?.Behaviour != null;
                case CanvasTool.HealingBrush: return layer?.Behaviour != null;
                case CanvasTool.Transform: return layer?.Behaviour != null;
                case CanvasTool.Zoom:
                case CanvasTool.Shape:
                case CanvasTool.Text:
                case CanvasTool.Gradient:
                case CanvasTool.Crop:
                case CanvasTool.Comment:
                case CanvasTool.VectorPen:
                case CanvasTool.VectorNodes:
                case CanvasTool.VectorBrush:
                case CanvasTool.RectangleSelect:
                case CanvasTool.QuickSelection:
                case CanvasTool.PolygonSelect: return activeDocument != null;
                default: return false;
            }
        }

        private static VisualElement CreateCanvasSettingsRow()
        {
            var row = new WhimTexCanvasHeaderRow();
            row.AddToClassList("whimtex-tool-settings-row");
            return row;
        }

        private void BindCanvasSettingsRow(VisualElement row, CanvasTool tool)
        {
            TrackToolPreferenceChanges(row);
            toolkitHeaderBindings.Add(() =>
            {
                CanvasTool settings = canvasTool;
                if (settings == CanvasTool.UvIslandSelect) settings = CanvasTool.RectangleSelect;
                bool visible = settings == tool;
                row.EnableInClassList("whimtex-tool-options--hidden", !visible);
            });
        }

        private VisualElement BuildCanvasToolToolbar()
        {
            VisualElement toolbar = new VisualElement { name = "canvasTools" };
            toolbar.AddToClassList("whimtex-tools");
            toolbar.EnableInClassList("whimtex-tools--light", !EditorGUIUtility.isProSkin);
            canvasNoneButton = CreateCanvasToolButton("noTool", CanvasTool.None,
                "Layer Select (V). Click visible pixels or Vector geometry to select a layer, or drag a guide to move it. Click a selected group again to select inside it. Shift toggles selection; Ctrl bypasses guides and selects nested layers directly. Click empty space to deselect. Use Transform (T) to move, scale or rotate.");
            canvasBrushButton = CreateCanvasToolButton("brushTool", CanvasTool.Brush,
                "Brush (B). Paint on the selected Drawing layer. Choose Brush/Eraser in the header; RMB temporarily erases.");
            canvasBlurBrushButton = CreateCanvasToolButton("blurBrushTool", CanvasTool.BlurBrush,
                "Blur Brush. Paint a soft circular blur on the selected Drawing layer. Choose the current layer or the layers below as the sample.");
            canvasSmudgeBrushButton = CreateCanvasToolButton("smudgeBrushTool", CanvasTool.SmudgeBrush,
                "Smudge Brush. Drag to carry existing pixels along the stroke. Writes only the selected Drawing layer; a click without movement does not paint.");
            canvasPencilButton = CreateCanvasToolButton("pencilTool", CanvasTool.Pencil,
                "Pencil (P). Paint crisp pixels with a Circle, Square or Diamond tip. RMB temporarily erases; [ and ] change size.");
            canvasFillButton = CreateCanvasToolButton("fillTool", CanvasTool.Fill,
                "Fill (G). Fill similar pixels on the selected Drawing layer, sampling this layer or all visible layers. Contiguous limits the fill to the clicked region.");
            canvasTransformButton = CreateCanvasToolButton("transformTool", CanvasTool.Transform,
                "Transform (T). Drag inside to move, handles to scale, circle to rotate. " +
                "Drag the gold cross to move the pivot without moving the image (requires nonzero scale). " +
                "The pivot snaps to frame anchors; hold Ctrl to disable snapping. " +
                "Shift: constrain movement / preserve proportions / snap rotation to 15°. Groups are not supported yet.");
            toolbar.Add(canvasNoneButton);
            toolbar.Add(canvasTransformButton);
            canvasCropButton = CreateCanvasToolButton("cropTool", CanvasTool.Crop,
                GetCanvasCropToolTooltip());
            cropPicker = new CropPickerManipulator(this);
            canvasCropButton.AddManipulator(cropPicker);
            canvasCommentButton = CreateCanvasToolButton("commentTool", CanvasTool.Comment,
                "Comment (N). Click the canvas to start a comment, click a pin to read or reply, and drag a pin to move it. Ctrl+Enter posts; Escape cancels. Comments are saved with the document.");
            toolbar.Add(canvasCommentButton);
            toolbar.Add(canvasCropButton);
            canvasRectangleSelectButton = CreateCanvasToolButton("rectangleSelectTool", CanvasTool.RectangleSelect,
                "Area Select (M). Hold or drag this button to choose Rectangle or Ellipse. Shift adds; Alt subtracts; Ctrl+D deselects. Selection limits painting and filling.");
            marqueePicker = new ShapePickerManipulator(this, true);
            canvasRectangleSelectButton.AddManipulator(marqueePicker);
            canvasPolygonSelectButton = CreateCanvasToolButton("polygonSelectTool", CanvasTool.PolygonSelect,
                GetLassoToolTooltip());
            lassoPicker = new LassoPickerManipulator(this);
            canvasPolygonSelectButton.AddManipulator(lassoPicker);
            toolbar.Add(canvasRectangleSelectButton);
            toolbar.Add(canvasPolygonSelectButton);
            canvasQuickSelectionButton = CreateCanvasToolButton("quickSelectionTool", CanvasTool.QuickSelection,
                "Quick Selection (W). Brush to select connected colours up to image boundaries. Right mouse or Alt subtracts; [ and ] change size. Esc stops pending work.");
            toolbar.Add(canvasQuickSelectionButton);
            canvasShapeButton = CreateCanvasToolButton("shapeTool", CanvasTool.Shape,
                "Shape (U). Hold or drag this button to pick a figure, then release over its icon. Drag on the canvas to create it.");
            shapePicker = new ShapePickerManipulator(this);
            canvasShapeButton.AddManipulator(shapePicker);
            toolbar.Add(canvasShapeButton);
            AddVectorToolButtons(toolbar);
            canvasTextButton = CreateCanvasToolButton("textTool", CanvasTool.Text,
                "Text. Click for point text; drag for framed text. Click text to edit; drag the center to move its layout, or frame corners/sides to reflow. Shift constrains movement or corner proportions. Ctrl creates new text and disables guide snapping.");
            toolbar.Add(canvasTextButton);
            toolbar.Add(canvasBrushButton);
            toolbar.Add(canvasPencilButton);
            toolbar.Add(canvasBlurBrushButton);
            toolbar.Add(canvasSmudgeBrushButton);
            canvasHealingButton = CreateCanvasToolButton("healingBrushTool", CanvasTool.HealingBrush,
                "Healing Brush. Paint over a defect, then release to reconstruct it from nearby pixels. Esc cancels. Writes only the selected Drawing layer.");
            toolbar.Add(canvasHealingButton);
            toolbar.Add(canvasFillButton);
            canvasGradientButton = CreateCanvasToolButton("gradientTool", CanvasTool.Gradient,
                GetGradientCreationToolTooltip());
            gradientPicker = new GradientPickerManipulator(this);
            canvasGradientButton.AddManipulator(gradientPicker);
            toolbar.Add(canvasGradientButton);
            canvasZoomButton = CreateCanvasToolButton("zoomTool", CanvasTool.Zoom,
                "Zoom (Z). Click to zoom in, drag a rectangle to frame an area, or Alt-click to zoom out. MMB-drag pans the canvas.");
            toolbar.Add(canvasZoomButton);
            BuildContextToolButtons(toolbar);
            canvasToolScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "canvasToolScroll",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            canvasToolScroll.AddToClassList("whimtex-tools-scroll");
            canvasToolScroll.EnableInClassList("whimtex-tools-scroll--light", !EditorGUIUtility.isProSkin);
            canvasToolScroll.Add(toolbar);
            canvasToolScroll.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                RevealActiveCanvasTool();
                canvasHealingButton?.Q<CanvasToolIcon>()?.AlignHealingVectorImage();
            });
            canvasToolScroll.verticalScroller.valueChanged += _ =>
                canvasHealingButton?.Q<CanvasToolIcon>()?.AlignHealingVectorImage();
            return canvasToolScroll;
        }

        private void RevealActiveCanvasTool()
        {
            var scroll = canvasToolScroll;
            scroll?.schedule.Execute(() =>
            {
                var button = scroll.Q<Button>(className: "whimtex-tool-button--selected");
                if (scroll.panel != null && button != null && button.resolvedStyle.display != DisplayStyle.None)
                    scroll.ScrollTo(button);
            });
        }

        private Button CreateCanvasToolButton(string name, CanvasTool tool, string tooltip)
        {
            Button button = new Button(() => SetCanvasTool(tool)) { name = name, tooltip = tooltip };
            button.AddToClassList("whimtex-tool-button");
            if (tool == CanvasTool.Shape)
                button.Add(shapeToolIcon = new ShapeToolIcon(shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle));
            else if (tool == CanvasTool.RectangleSelect)
                button.Add(marqueeToolIcon = new CanvasToolIcon(tool, marqueeShape == MarqueeShape.Ellipse));
            else if (tool == CanvasTool.Crop)
                button.Add(cropToolIcon = new CanvasToolIcon(tool, cropMode: canvasCropMode));
            else if (tool == CanvasTool.Gradient)
                button.Add(gradientCreationToolIcon = new CanvasToolIcon(tool, gradientType: gradientCreationSettings?.type ?? GradientCreationType.Linear));
            else if (tool == CanvasTool.Comment)
            {
                var icon = new CanvasToolIcon(tool);
                icon.SetCommentsState(true, false);
                button.Add(icon);
            }
            else if (tool == CanvasTool.PolygonSelect)
            {
                lassoToolIcon = new CanvasToolIcon(tool);
                lassoToolIcon.SetFreehand(lassoMode == LassoMode.Freehand);
                button.Add(lassoToolIcon);
            }
            else
                button.Add(new CanvasToolIcon(tool));
            if (tool == CanvasTool.Shape || tool == CanvasTool.RectangleSelect || tool == CanvasTool.Crop || tool == CanvasTool.PolygonSelect || tool == CanvasTool.Gradient)
                button.Add(new ToolDropdownMarker());
            return button;
        }

        private string GetLassoToolTooltip() => lassoMode == LassoMode.Freehand
            ? "Freehand Lasso (L). Hold or drag this button to choose Polygonal or Freehand. " +
                "Drag to draw the selection contour; release to close it. " +
                "Shift adds, Alt subtracts, Shift+Alt intersects. Escape cancels."
            : "Polygonal Lasso (L). Hold or drag this button to choose Polygonal or Freehand. " +
                "Click to extend; Shift constrains new segments and existing-point movement to 45-degree canvas directions. " +
                "Click a committed edge to insert, drag a vertex to move, Alt-click a vertex to remove. " +
                "Keep the button held after placing/inserting a point to position it immediately. Near the last point, short clicks extend the contour. " +
                "Click-release the highlighted first point to close, or use Enter/double-click at the same position when adding a point. " +
                "Ctrl before pressing bypasses editing and snapping; during a drag it disables snapping. " +
                "Smooth curves pass through placed points; C or Corner toggles a sharp point. " +
                "Backspace/RMB removes the last vertex; Escape cancels.";

        private string GetCanvasCropToolTooltip() => canvasCropMode == CanvasCropMode.Scale
            ? "Crop: Scale (C). Hold or drag this button to choose Scale or Resize. " +
                "Drag frame handles to scale the whole canvas and its contents. " +
                "Enter applies; Escape resets. Shift keeps proportions; Ctrl disables snapping. Source pixels are preserved."
            : "Crop: Resize (C). Hold or drag this button to choose Scale or Resize. " +
                "Drag the frame to crop or expand the canvas without scaling its contents. " +
                "Enter applies; Escape resets. Shift keeps proportions; Ctrl disables snapping. Source pixels are preserved.";

        private void RefreshCanvasToolToolbar()
        {
            RefreshPostFxPanel();
            RefreshContextToolButtons();
            RefreshVectorTools();
            bool hasLayers = HasCanvasLayers;
            CanvasTool displayedTool = canvasTool;
            Layer selected = hasLayers ? GetSelectedLayer() : null;
            shapeToolIcon?.SetKind(shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle);
            gradientCreationToolIcon?.SetGradientType(gradientCreationSettings?.type ?? GradientCreationType.Linear);
            if (canvasGradientButton != null) canvasGradientButton.tooltip = GetGradientCreationToolTooltip();
            marqueeToolIcon?.SetEllipse(marqueeShape == MarqueeShape.Ellipse);
            cropToolIcon?.SetCropMode(canvasCropMode);
            if (canvasCropButton != null) canvasCropButton.tooltip = GetCanvasCropToolTooltip();
            lassoToolIcon?.SetFreehand(lassoMode == LassoMode.Freehand);
            if (canvasPolygonSelectButton != null) canvasPolygonSelectButton.tooltip = GetLassoToolTooltip();
            if (!IsUvSelectionTool) SetUvHovered(-1);
            if (uvDisplayedSelection != IsUvSelectionTool || uvDisplayedLayers != hasLayers)
            {
                uvDisplayedSelection = IsUvSelectionTool; uvDisplayedLayers = hasLayers;
                uvOverlay?.MarkDirtyRepaint();
            }
            canvasShapeButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Shape);
            canvasShapeButton?.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            canvasTextButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Text);
            canvasTextButton?.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            canvasGradientButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Gradient);
            canvasGradientButton?.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            canvasCropButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Crop);
            canvasCropButton?.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            canvasCommentButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Comment);
            canvasCommentButton?.EnableInClassList("whimtex-tool-button--unavailable", activeDocument == null);
            canvasRectangleSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.RectangleSelect);
            canvasPolygonSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.PolygonSelect);
            canvasRectangleSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            canvasPolygonSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            canvasQuickSelectionButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            canvasQuickSelectionButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.QuickSelection);
            canvasZoomButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            canvasZoomButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Zoom);
            canvasNoneButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.None);
            if (canvasBrushButton != null)
            {
                canvasBrushButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                canvasBrushButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Brush);
            }
            if (canvasBlurBrushButton != null)
            {
                canvasBlurBrushButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                canvasBlurBrushButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.BlurBrush);
            }
            if (canvasPencilButton != null)
            {
                canvasPencilButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                canvasPencilButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Pencil);
            }
            canvasSmudgeBrushButton?.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
            canvasSmudgeBrushButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.SmudgeBrush);
            canvasHealingButton?.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
            canvasHealingButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.HealingBrush);
            if (canvasTransformButton != null)
            {
                canvasTransformButton.EnableInClassList("whimtex-tool-button--unavailable", selected == null);
                canvasTransformButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Transform);
            }
            if (canvasFillButton != null)
            {
                canvasFillButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                canvasFillButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Fill);
            }
        }

        private sealed class CanvasToolIcon : VisualElement
        {
            private readonly CanvasTool tool;
            private bool ellipse;
            private bool freehand;
            private bool hasComments;
            private bool hasReviewComments;
            private CanvasCropMode cropMode;
            private GradientCreationType gradientType;
            private readonly bool largeGradient;
            private readonly VectorFillMode? flatFill;
            private readonly Image healingVectorImage;
            private readonly Image alignedVectorImage;
            private Vector2 healingVectorOffset;
            private bool hasHealingVectorOffset;
            private Vector2 alignedVectorOffset;
            private bool hasAlignedVectorOffset;

            internal CanvasToolIcon(CanvasTool tool, bool ellipse = false, CanvasCropMode cropMode = CanvasCropMode.Resize,
                GradientCreationType gradientType = GradientCreationType.Linear, bool largeGradient = false, VectorFillMode? flatFill = null)
            {
                this.tool = tool;
                this.ellipse = ellipse;
                this.cropMode = cropMode;
                this.gradientType = gradientType;
                this.largeGradient = largeGradient;
                this.flatFill = flatFill;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
                if (tool == CanvasTool.QuickSelection)
                {
                    AddToClassList("whimtex-tool-icon--quick-selection");
                    Add(new CanvasToolIcon(CanvasTool.Brush));
                }
#if UNITY_6000_3_OR_NEWER
                if (tool == CanvasTool.HealingBrush)
                {
                    var vector = AssetDatabase.LoadAssetAtPath<VectorImage>(
                        "Packages/com.dcfapixels.whimtex/src/Editor/Icons/HealingBrush.svg");
                    if (vector != null)
                    {
                        healingVectorImage = new Image
                        {
                            vectorImage = vector,
                            scaleMode = ScaleMode.ScaleToFit,
                            pickingMode = PickingMode.Ignore
                        };
                        healingVectorImage.AddToClassList("whimtex-tool-vector-image");
                        Add(healingVectorImage);
                        RegisterCallback<GeometryChangedEvent>(OnHealingVectorGeometryChanged);
                        healingVectorImage.RegisterCallback<GeometryChangedEvent>(OnHealingVectorGeometryChanged);
                        RegisterCallback<AttachToPanelEvent>(_ => AlignHealingVectorImage());
                    }
                }
                else if (tool == CanvasTool.Fill || tool == CanvasTool.Pencil)
                {
                    var vector = AssetDatabase.LoadAssetAtPath<VectorImage>(
                        "Packages/com.dcfapixels.whimtex/src/Editor/Icons/" +
                        (tool == CanvasTool.Fill ? "Fill.svg" : "Pencil.svg"));
                    if (vector != null)
                    {
                        alignedVectorImage = new Image
                        {
                            vectorImage = vector,
                            scaleMode = ScaleMode.ScaleToFit,
                            pickingMode = PickingMode.Ignore
                        };
                        alignedVectorImage.AddToClassList("whimtex-tool-vector-image");
                        if (tool == CanvasTool.Pencil)
                            alignedVectorImage.AddToClassList("whimtex-tool-vector-image--pencil");
                        Add(alignedVectorImage);
                        RegisterCallback<GeometryChangedEvent>(_ => AlignVectorImage());
                        alignedVectorImage.RegisterCallback<GeometryChangedEvent>(_ => AlignVectorImage());
                        RegisterCallback<AttachToPanelEvent>(_ => AlignVectorImage());
                    }
                }
#endif
            }

            private void AlignVectorImage()
            {
                if (alignedVectorImage == null || panel == null || alignedVectorImage.contentRect.width < 1f) return;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 pixels = alignedVectorImage.LocalToWorld(Vector2.zero) * pixelsPerPoint;
                Vector2 snapped = new Vector2(Mathf.Round(pixels.x), Mathf.Round(pixels.y));
                Translate applied = alignedVectorImage.resolvedStyle.translate;
                Vector2 offset = new Vector2(applied.x.value, applied.y.value) + this.WorldToLocal(snapped / pixelsPerPoint)
                    - this.WorldToLocal(pixels / pixelsPerPoint);
                if (hasAlignedVectorOffset && (offset - alignedVectorOffset).sqrMagnitude < .000001f) return;
                alignedVectorOffset = offset; hasAlignedVectorOffset = true;
                alignedVectorImage.style.translate = new Translate(offset.x, offset.y);
            }

            private void OnHealingVectorGeometryChanged(GeometryChangedEvent evt) => AlignHealingVectorImage();

            internal void AlignHealingVectorImage()
            {
                if (healingVectorImage == null || panel == null || healingVectorImage.contentRect.width < 1f)
                    return;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 pixels = healingVectorImage.LocalToWorld(healingVectorImage.contentRect.center) * pixelsPerPoint;
                Vector2 snapped = new Vector2(Mathf.Floor(pixels.x) + 0.5f,
                    Mathf.Floor(pixels.y) + 0.5f);
                Translate applied = healingVectorImage.resolvedStyle.translate;
                Vector2 offset = new Vector2(applied.x.value, applied.y.value) + this.WorldToLocal(snapped / pixelsPerPoint)
                    - this.WorldToLocal(pixels / pixelsPerPoint);
                if (hasHealingVectorOffset && (offset - healingVectorOffset).sqrMagnitude < 0.000001f)
                    return;
                healingVectorOffset = offset;
                hasHealingVectorOffset = true;
                healingVectorImage.style.translate = new Translate(offset.x, offset.y);
            }

            internal void SetEllipse(bool value)
            {
                if (ellipse == value) return;
                ellipse = value;
                MarkDirtyRepaint();
            }

            internal void SetCropMode(CanvasCropMode value)
            {
                if (cropMode == value) return;
                cropMode = value;
                MarkDirtyRepaint();
            }

            internal void SetFreehand(bool value)
            {
                if (freehand == value) return;
                freehand = value;
                MarkDirtyRepaint();
            }

            internal void SetGradientType(GradientCreationType value)
            {
                if (gradientType == value) return;
                gradientType = value;
                MarkDirtyRepaint();
            }

            internal void SetCommentsState(bool value, bool review)
            {
                if (hasComments == value && hasReviewComments == review) return;
                hasComments = value;
                hasReviewComments = review;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f)
                    return;
                if (healingVectorImage != null || alignedVectorImage != null)
                    return;
                Painter2D painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 1.35f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                if (tool == CanvasTool.None)
                    DrawPointer(painter);
                else if (IsVectorTool(tool))
                    DrawVectorToolIcon(painter, tool, P);
                else if (tool == CanvasTool.Transform)
                    DrawTransform(painter);
                else if (tool == CanvasTool.Text)
                    DrawTextTool(painter);
                else if (tool == CanvasTool.Comment)
                    DrawCommentTool(painter);
                else if (tool == CanvasTool.Crop)
                {
                    if (cropMode == CanvasCropMode.Scale) DrawCanvasScaleTool(painter);
                    else DrawCropTool(painter);
                }
                else if (tool == CanvasTool.GradientHandles || IsTemporaryCanvasTool(tool))
                    DrawHand(painter, withHandle: true);
                else if (tool == CanvasTool.UvIslandSelect)
                    DrawUvSelect(painter);
                else if (tool == CanvasTool.Fill)
                    DrawBucket(painter);
                else if (tool == CanvasTool.Gradient)
                    DrawGradient(context);
                else if (tool == CanvasTool.Zoom)
                    DrawMagnifier(painter);
                else if (tool == CanvasTool.Pencil)
                    DrawPencil(painter);
                else if (tool == CanvasTool.BlurBrush)
                    DrawBlurBrush(context);
                else if (tool == CanvasTool.SmudgeBrush)
                    DrawSmudgeBrush(painter);
                else if (tool == CanvasTool.HealingBrush)
                    DrawHealingBrush(painter);
                else if (tool == CanvasTool.RectangleSelect)
                {
                    if (ellipse) DrawEllipseSelect(painter);
                    else DrawRectangleSelect(painter);
                }
                else if (tool == CanvasTool.PolygonSelect)
                {
                    if (freehand) DrawFreehandSelect(painter);
                    else DrawPolygonSelect(painter);
                }
                else if (tool == CanvasTool.QuickSelection)
                    DrawQuickSelection(painter);
                else
                    DrawBrush(painter);
            }

            private void DrawQuickSelection(Painter2D painter)
            {
                painter.lineWidth = 1.2f;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(P(1.00f, 13.00f));
                painter.BezierCurveTo(P(1.00f, 11.83f), P(1.41f, 10.69f), P(2.17f, 9.79f));
                painter.MoveTo(P(4.29f, 8.30f));
                painter.BezierCurveTo(P(5.40f, 7.90f), P(6.60f, 7.90f), P(7.71f, 8.30f));
                painter.MoveTo(P(9.83f, 9.79f));
                painter.BezierCurveTo(P(10.59f, 10.69f), P(11.00f, 11.83f), P(11.00f, 13.00f));
                painter.MoveTo(P(11.00f, 18.00f));
                painter.BezierCurveTo(P(11.00f, 19.18f), P(10.59f, 20.31f), P(9.83f, 21.21f));
                painter.MoveTo(P(7.71f, 22.70f));
                painter.BezierCurveTo(P(6.60f, 23.10f), P(5.40f, 23.10f), P(4.29f, 22.70f));
                painter.MoveTo(P(2.17f, 21.21f));
                painter.BezierCurveTo(P(1.41f, 20.31f), P(1.00f, 19.18f), P(1.00f, 18.00f));
                painter.MoveTo(P(1.00f, 14.10f));
                painter.LineTo(P(1.00f, 16.90f));
                painter.MoveTo(P(11.00f, 14.10f));
                painter.LineTo(P(11.00f, 16.90f));
                painter.Stroke();
            }

            private void DrawCommentTool(Painter2D painter)
            {
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 24f;
                painter.BeginPath();
                painter.MoveTo(P(20, hasReviewComments ? 9.5f : 5)); painter.LineTo(P(20, 16));
                painter.LineTo(P(11, 16)); painter.LineTo(P(5, 21)); painter.LineTo(P(5, 5));
                painter.LineTo(P(hasReviewComments ? 15.5f : 20, 5));
                if (!hasReviewComments) painter.ClosePath();
                painter.Stroke();
                if (hasReviewComments)
                {
                    // Leave a real transparent gap; do not paint a panel-colored ring over the icon.
                    painter.Arc(P(20, 5), 4.5f * unit, 180, 90, ArcDirection.CounterClockwise);
                    painter.ClosePath();
                }
                if (hasComments)
                {
                    TraceRectangle(painter, P(8, 8), P(hasReviewComments ? 16 : 17, 10));
                    TraceRectangle(painter, P(8, 11), P(15, 13));
                    painter.Fill(FillRule.OddEven);
                    if (hasReviewComments)
                        DrawCommentReviewBadge(painter, P(20, 5), 3f * unit, false);
                    return;
                }
                painter.BeginPath(); painter.MoveTo(P(8, 9)); painter.LineTo(P(17, 9));
                painter.MoveTo(P(8, 12)); painter.LineTo(P(15, 12)); painter.Stroke();
            }

            private Vector2 P(float x, float y) => new Vector2(
                contentRect.x + x * contentRect.width / 24f,
                contentRect.y + y * contentRect.height / 24f);

            private void DrawCanvasScaleTool(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                int strokePixels = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.Min(contentRect.width, contentRect.height) / 22f * ppi));
                float offset = (strokePixels & 1) == 0 ? 0f : .5f;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 local = new Vector2(contentRect.x + x * contentRect.width / 22f,
                        contentRect.y + y * contentRect.height / 22f);
                    Vector2 world = this.LocalToWorld(local) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x - offset) + offset,
                        Mathf.Round(world.y - offset) + offset) / ppi);
                }
                painter.lineWidth = strokePixels / ppi;
                painter.lineCap = LineCap.Butt;
                painter.lineJoin = LineJoin.Miter;
                painter.BeginPath();
                painter.MoveTo(Pixel(6, 6)); painter.LineTo(Pixel(16, 6));
                painter.LineTo(Pixel(16, 16)); painter.LineTo(Pixel(6, 16)); painter.ClosePath();
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Pixel(16, 6)); painter.LineTo(Pixel(20, 2));
                painter.MoveTo(Pixel(16, 2)); painter.LineTo(Pixel(20, 2)); painter.LineTo(Pixel(20, 6));
                painter.MoveTo(Pixel(6, 16)); painter.LineTo(Pixel(2, 20));
                painter.MoveTo(Pixel(2, 16)); painter.LineTo(Pixel(2, 20)); painter.LineTo(Pixel(6, 20));
                painter.Stroke();
            }

            private void DrawCropTool(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 world = this.LocalToWorld(P(x, y)) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x), Mathf.Round(world.y)) / ppi);
                }
                painter.BeginPath();
                painter.MoveTo(Pixel(5, 2)); painter.LineTo(Pixel(8, 2)); painter.LineTo(Pixel(8, 16));
                painter.LineTo(Pixel(22, 16)); painter.LineTo(Pixel(22, 19)); painter.LineTo(Pixel(5, 19)); painter.ClosePath();
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(Pixel(2, 5)); painter.LineTo(Pixel(19, 5)); painter.LineTo(Pixel(19, 22));
                painter.LineTo(Pixel(16, 22)); painter.LineTo(Pixel(16, 8)); painter.LineTo(Pixel(2, 8)); painter.ClosePath();
                painter.Fill();
            }

            private void DrawTextTool(Painter2D painter)
            {
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 Pixel(float x, float y)
                {
                    Vector2 point = this.LocalToWorld(P(x, y)) * pixelsPerPoint;
                    return this.WorldToLocal(new Vector2(Mathf.Round(point.x), Mathf.Round(point.y)) / pixelsPerPoint);
                }
                painter.BeginPath();
                painter.MoveTo(Pixel(4, 3)); painter.LineTo(Pixel(20, 3));
                painter.LineTo(Pixel(20, 7)); painter.LineTo(Pixel(18.5f, 7));
                painter.BezierCurveTo(Pixel(18.4f, 5.6f), Pixel(17.8f, 5), Pixel(16, 5));
                painter.LineTo(Pixel(13.5f, 5)); painter.LineTo(Pixel(13.5f, 18));
                painter.BezierCurveTo(Pixel(13.5f, 19.1f), Pixel(14.2f, 19.6f), Pixel(16, 19.6f));
                painter.LineTo(Pixel(16, 21)); painter.LineTo(Pixel(8, 21));
                painter.LineTo(Pixel(8, 19.6f));
                painter.BezierCurveTo(Pixel(9.8f, 19.6f), Pixel(10.5f, 19.1f), Pixel(10.5f, 18));
                painter.LineTo(Pixel(10.5f, 5)); painter.LineTo(Pixel(8, 5));
                painter.BezierCurveTo(Pixel(6.2f, 5), Pixel(5.6f, 5.6f), Pixel(5.5f, 7));
                painter.LineTo(Pixel(4, 7)); painter.ClosePath();
                painter.Fill();
            }

            private void DrawGradient(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                Color tint = resolvedStyle.color;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 halfStroke = Vector2.one * (painter.lineWidth * .5f);
                float inset = largeGradient ? 2 : 4;
                Vector2 pixelMin = this.LocalToWorld(P(inset, inset) - halfStroke) * pixelsPerPoint;
                Vector2 pixelMax = this.LocalToWorld(P(24 - inset, 24 - inset) + halfStroke) * pixelsPerPoint;
                pixelMin = new Vector2(Mathf.Round(pixelMin.x), Mathf.Round(pixelMin.y));
                pixelMax = new Vector2(Mathf.Round(pixelMax.x), Mathf.Round(pixelMax.y));
                Vector2 outerMin = this.WorldToLocal(pixelMin / pixelsPerPoint);
                Vector2 outerMax = this.WorldToLocal(pixelMax / pixelsPerPoint);
                Vector2 innerMin = this.WorldToLocal((pixelMin + Vector2.one) / pixelsPerPoint);
                Vector2 innerMax = this.WorldToLocal((pixelMax - Vector2.one) / pixelsPerPoint);

                painter.fillColor = tint;
                painter.BeginPath();
                TraceRectangle(painter, outerMin, outerMax);
                if (flatFill == VectorFillMode.Solid) { painter.Fill(); return; }
                TraceRectangle(painter, innerMin, innerMax);
                painter.Fill(FillRule.OddEven);
                if (flatFill == VectorFillMode.None)
                {
                    painter.strokeColor = tint;
                    painter.lineWidth = 1.5f / pixelsPerPoint;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(innerMin.x, innerMax.y));
                    painter.LineTo(new Vector2(innerMax.x, innerMin.y));
                    painter.Stroke();
                    return;
                }

                Color light = new Color(.95f, .95f, .95f, tint.a), dark = new Color(.1f, .1f, .1f, tint.a);
                Vector2 center = (innerMin + innerMax) * .5f, extent = (innerMax - innerMin) * .5f;
                bool linear = gradientType == GradientCreationType.Linear;
                bool radial = gradientType == GradientCreationType.Radial;
                painter.fillColor = linear || radial ? Color.white : dark;
                if (linear)
                    painter.fillGradient = FillGradient.MakeLinearGradient(light, dark, innerMin, new Vector2(innerMax.x, innerMin.y), AddressMode.Clamp);
                else if (radial)
                    painter.fillGradient = FillGradient.MakeRadialGradient(light, dark, center, extent.x, center, AddressMode.Clamp);
                painter.BeginPath();
                TraceRectangle(painter, innerMin, innerMax);
                painter.Fill();
                if (linear || radial) return;
                if (gradientType == GradientCreationType.Mesh)
                {
                    painter.lineWidth = 1 / pixelsPerPoint; painter.strokeColor = tint;
                    painter.BeginPath();
                    for (int i = 1; i < 3; i++)
                    {
                        float t = i / 3f;
                        painter.MoveTo(new Vector2(Mathf.Lerp(innerMin.x, innerMax.x, t), innerMin.y));
                        painter.BezierCurveTo(innerMin + Vector2.Scale(new Vector2(t + .12f, .33f), innerMax - innerMin), innerMin + Vector2.Scale(new Vector2(t - .12f, .67f), innerMax - innerMin), new Vector2(Mathf.Lerp(innerMin.x, innerMax.x, t), innerMax.y));
                        painter.MoveTo(new Vector2(innerMin.x, Mathf.Lerp(innerMin.y, innerMax.y, t)));
                        painter.LineTo(new Vector2(innerMax.x, Mathf.Lerp(innerMin.y, innerMax.y, t)));
                    }
                    painter.Stroke(); return;
                }
                if (gradientType == GradientCreationType.Freeform)
                {
                    const int steps = 16, stride = steps + 1;
                    var field = context.Allocate(stride * stride, steps * steps * 6);
                    for (int y = 0; y <= steps; y++) for (int x = 0; x <= steps; x++)
                    {
                        Vector2 uv = new Vector2(x / (float)steps, y / (float)steps);
                        float distance = Mathf.Min(Vector2.Distance(uv, new Vector2(.22f, .72f)),
                            Mathf.Min(Vector2.Distance(uv, new Vector2(.78f, .72f)), Vector2.Distance(uv, new Vector2(.5f, .22f))));
                        Vector2 position = innerMin + Vector2.Scale(uv, innerMax - innerMin);
                        field.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ),
                            tint = Color.Lerp(light, dark, Mathf.SmoothStep(0, 1, distance / .4f)) });
                    }
                    for (int y = 0; y < steps; y++) for (int x = 0; x < steps; x++)
                    {
                        int i = y * stride + x;
                        field.SetNextIndex((ushort)i); field.SetNextIndex((ushort)(i + 1)); field.SetNextIndex((ushort)(i + stride));
                        field.SetNextIndex((ushort)(i + 1)); field.SetNextIndex((ushort)(i + stride + 1)); field.SetNextIndex((ushort)(i + stride));
                    }
                    return;
                }

                bool angular = gradientType == GradientCreationType.Angular;
                int segments = angular ? 64 : 4;
                var mesh = context.Allocate(segments * 3, segments * 3);
                for (int i = 0; i < segments; i++)
                {
                    Vector2 Edge(int step)
                    {
                        float angle = step * Mathf.PI * 2f / segments + (gradientType == GradientCreationType.Square ? Mathf.PI * .25f : 0f);
                        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        direction /= Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
                        return center + Vector2.Scale(direction, extent);
                    }
                    Color a = angular ? Color.Lerp(light, dark, i / (float)segments) : dark;
                    Color b = angular ? Color.Lerp(light, dark, (i + 1f) / segments) : dark;
                    void VertexAt(Vector2 position, Color color) => mesh.SetNextVertex(new Vertex
                    { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = color });
                    VertexAt(center, angular ? Color.Lerp(a, b, .5f) : light);
                    VertexAt(Edge(i), a);
                    VertexAt(Edge(i + 1), b);
                    mesh.SetNextIndex((ushort)(i * 3)); mesh.SetNextIndex((ushort)(i * 3 + 1)); mesh.SetNextIndex((ushort)(i * 3 + 2));
                }
            }

            private static void TraceRectangle(Painter2D painter, Vector2 min, Vector2 max)
            {
                painter.MoveTo(min); painter.LineTo(new Vector2(max.x, min.y));
                painter.LineTo(max); painter.LineTo(new Vector2(min.x, max.y));
                painter.ClosePath();
            }

            private void DrawUvSelect(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(3, 5)); painter.LineTo(P(11, 3)); painter.LineTo(P(14, 9));
                painter.LineTo(P(9, 15)); painter.LineTo(P(3, 12)); painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(17, 12)); painter.LineTo(P(22, 14)); painter.LineTo(P(20, 21));
                painter.LineTo(P(13, 20)); painter.LineTo(P(12, 17)); painter.ClosePath(); painter.Stroke();
                painter.BeginPath(); painter.Arc(P(8, 9), 1.2f, 0, 360); painter.ClosePath(); painter.Fill();
            }

            private void DrawMagnifier(Painter2D painter)
            {
                painter.BeginPath();
                painter.Arc(P(9.5f, 9.5f), contentRect.width * 6.5f / 24f, 0f, 360f);
                painter.ClosePath();
                painter.Stroke();
                painter.lineWidth = 3f;
                painter.BeginPath();
                painter.MoveTo(P(14.5f, 14.5f));
                painter.LineTo(P(21f, 21f));
                painter.Stroke();
            }

            private void DrawEllipseSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                for (int i = 0; i < 8; i++)
                {
                    float start = i * 45f - 13f;
                    float angle = start * Mathf.Deg2Rad;
                    painter.MoveTo(P(12f + Mathf.Cos(angle) * 9f, 12f + Mathf.Sin(angle) * 9f));
                    painter.Arc(P(12f, 12f), contentRect.width * 9f / 24f, start, start + 26f);
                }
                painter.Stroke();
            }
            private void DrawRectangleSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Butt;
                painter.lineJoin = LineJoin.Miter;
                painter.BeginPath();
                painter.MoveTo(P(3, 6)); painter.LineTo(P(3, 3)); painter.LineTo(P(6, 3));
                painter.MoveTo(P(9, 3)); painter.LineTo(P(15, 3));
                painter.MoveTo(P(18, 3)); painter.LineTo(P(21, 3)); painter.LineTo(P(21, 6));
                painter.MoveTo(P(21, 9)); painter.LineTo(P(21, 15));
                painter.MoveTo(P(21, 18)); painter.LineTo(P(21, 21)); painter.LineTo(P(18, 21));
                painter.MoveTo(P(15, 21)); painter.LineTo(P(9, 21));
                painter.MoveTo(P(6, 21)); painter.LineTo(P(3, 21)); painter.LineTo(P(3, 18));
                painter.MoveTo(P(3, 15)); painter.LineTo(P(3, 9));
                painter.Stroke();
            }
            private void DrawPolygonSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(14.8f, 16.3f));
                painter.LineTo(P(5f, 13.5f));
                painter.LineTo(P(8.7f, 10.3f));
                painter.LineTo(P(2.5f, 7.1f));
                painter.LineTo(P(11.2f, 2.7f));
                painter.LineTo(P(21.3f, 6.8f));
                painter.LineTo(P(15f, 16.4f));
                painter.Stroke();
                DrawLassoTail(painter);
            }

            private void DrawFreehandSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(15f, 16.4f));
                painter.BezierCurveTo(P(12f, 17f), P(5f, 15f), P(4f, 11f));
                painter.BezierCurveTo(P(2f, 5f), P(8f, 2f), P(13f, 3f));
                painter.BezierCurveTo(P(19f, 2f), P(23f, 6f), P(20f, 11f));
                painter.BezierCurveTo(P(19f, 14f), P(17f, 15f), P(15f, 16.4f));
                painter.Stroke();
                DrawLassoTail(painter);
            }

            private void DrawLassoTail(Painter2D painter)
            {
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(P(15f, 16.4f));
                painter.BezierCurveTo(P(12.2f, 15.7f), P(11.5f, 17.5f), P(13.4f, 18.1f));
                painter.BezierCurveTo(P(15.5f, 18.8f), P(18f, 17.3f), P(15f, 16.4f));
                painter.BezierCurveTo(P(14.4f, 18.1f), P(18.3f, 18.8f), P(16.9f, 20.4f));
                painter.BezierCurveTo(P(16.3f, 21.1f), P(14.8f, 21.6f), P(13.3f, 21.6f));
                painter.Stroke();
            }

            private void DrawBucket(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(9.95f, 4.65f));
                painter.BezierCurveTo(P(10.9f, 3.8f), P(12.28f, 3.86f), P(13.28f, 5));
                painter.LineTo(P(20.96f, 13.76f));
                painter.BezierCurveTo(P(21.72f, 14.63f), P(21.5f, 15.1f), P(20.33f, 15.04f));
                painter.LineTo(P(18.66f, 15.01f)); painter.LineTo(P(12.91f, 20.06f));
                painter.BezierCurveTo(P(10.85f, 22), P(8.72f, 22.24f), P(6.86f, 20.13f));
                painter.LineTo(P(2.25f, 14.94f));
                painter.BezierCurveTo(P(1, 13.57f), P(1.37f, 11.95f), P(2.4f, 11.04f));
                painter.ClosePath();
                painter.MoveTo(P(11.41f, 5.93f));
                painter.BezierCurveTo(P(11.65f, 5.88f), P(11.76f, 6.1f), P(11.9f, 6.27f));
                painter.LineTo(P(18.27f, 13.32f));
                painter.QuadraticCurveTo(P(18.37f, 13.44f), P(18.2f, 13.62f));
                painter.LineTo(P(11.76f, 19.22f));
                painter.BezierCurveTo(P(10.51f, 20.33f), P(8.91f, 19.98f), P(7.89f, 18.83f));
                painter.LineTo(P(3.72f, 14.2f));
                painter.BezierCurveTo(P(3.16f, 13.49f), P(3.18f, 12.81f), P(3.84f, 12.22f));
                painter.LineTo(P(10.95f, 6.15f));
                painter.QuadraticCurveTo(P(11.24f, 5.9f), P(11.41f, 5.93f));
                painter.ClosePath(); painter.Fill(FillRule.OddEven);
                painter.BeginPath();
                painter.MoveTo(P(5.2f, 13.2f)); painter.LineTo(P(16.34f, 13.2f));
                painter.BezierCurveTo(P(16.84f, 13.2f), P(17.02f, 13.36f), P(16.64f, 13.72f));
                painter.LineTo(P(11.35f, 18.27f));
                painter.BezierCurveTo(P(10.33f, 19.15f), P(9.16f, 18.99f), P(8.28f, 18.02f));
                painter.LineTo(P(4.9f, 14.2f));
                painter.BezierCurveTo(P(4.38f, 13.6f), P(4.45f, 13.2f), P(5.2f, 13.2f));
                painter.ClosePath(); painter.Fill();
                painter.BeginPath();
                painter.MoveTo(P(20.16f, 16.44f));
                painter.BezierCurveTo(P(20.34f, 16.09f), P(20.7f, 16.09f), P(20.88f, 16.44f));
                painter.BezierCurveTo(P(21.49f, 17.57f), P(22.48f, 19.55f), P(22.48f, 20.59f));
                painter.BezierCurveTo(P(22.48f, 21.75f), P(21.6f, 22.7f), P(20.5f, 22.7f));
                painter.BezierCurveTo(P(19.36f, 22.7f), P(18.46f, 21.78f), P(18.46f, 20.61f));
                painter.BezierCurveTo(P(18.46f, 19.55f), P(19.52f, 17.57f), P(20.16f, 16.44f));
                painter.ClosePath(); painter.Fill();
                painter.lineWidth = 1.8f * contentRect.width / 24;
                painter.BeginPath();
                painter.MoveTo(P(11.73f, 8.43f)); painter.LineTo(P(6.03f, 2.73f));
                painter.BezierCurveTo(P(5.35f, 2.05f), P(4.23f, 2.05f), P(3.55f, 2.73f));
                painter.BezierCurveTo(P(2.87f, 3.41f), P(2.87f, 4.53f), P(3.55f, 5.21f));
                painter.LineTo(P(6.64f, 8.3f)); painter.Stroke();
            }

            private void DrawPointer(Painter2D painter)
            {
                Color ink = resolvedStyle.color;
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.2f);
                painter.BeginPath();
                painter.MoveTo(P(5f, 2.5f));
                painter.LineTo(P(19f, 12.5f));
                painter.LineTo(P(12.8f, 13.5f));
                painter.LineTo(P(16.3f, 20.1f));
                painter.LineTo(P(12.8f, 21.8f));
                painter.LineTo(P(9.5f, 15.2f));
                painter.LineTo(P(5f, 19f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }

            private void DrawBrush(Painter2D painter)
            {
                painter.fillColor = resolvedStyle.color;
                painter.BeginPath();
                painter.MoveTo(P(10.6788f, 9.5538f));
                painter.BezierCurveTo(P(13.3108f, 7.5569f), P(18.4064f, 3.4138f), P(19.8064f, 2.0138f));
                painter.BezierCurveTo(P(21.2596f, .5606f), P(23.4394f, 2.7404f), P(21.9862f, 4.1936f));
                painter.BezierCurveTo(P(20.5862f, 5.5936f), P(16.4431f, 10.6892f), P(14.4462f, 13.3212f));
                painter.BezierCurveTo(P(13.3968f, 11.4047f), P(12.5953f, 10.6032f), P(10.6788f, 9.5538f));
                painter.ClosePath();
                painter.MoveTo(P(10.0939f, 10.3283f));
                painter.BezierCurveTo(P(11.7831f, 11.3378f), P(12.9751f, 12.7642f), P(13.9061f, 14.3982f));
                painter.LineTo(P(12.6846f, 16.2940f));
                painter.BezierCurveTo(P(11.4771f, 13.9615f), P(10.1410f, 12.8363f), P(8.0341f, 11.5966f));
                painter.ClosePath();
                painter.MoveTo(P(7.2375f, 12.6f));
                painter.BezierCurveTo(P(3.8375f, 12f), P(3.3375f, 15.4f), P(3.6375f, 17.1f));
                painter.BezierCurveTo(P(3.9375f, 19.2f), P(2.2375f, 20.7f), P(1.2375f, 21.4f));
                painter.BezierCurveTo(P(5.8375f, 22.6f), P(11.1375f, 20.9f), P(11.7375f, 18.4f));
                painter.BezierCurveTo(P(12.375f, 16.1016f), P(9.3047f, 12.8906f), P(7.2375f, 12.6f));
                painter.ClosePath();
                painter.MoveTo(P(6.5375f, 14.6f));
                painter.BezierCurveTo(P(5.3375f, 16f), P(6.4375f, 17.3f), P(4.5375f, 19.6f));
                painter.BezierCurveTo(P(7.4375f, 18.3f), P(6.6375f, 16.6f), P(6.5375f, 14.6f));
                painter.ClosePath();
                painter.Fill(FillRule.OddEven);
            }

            private static readonly Vector2[] BlurBrushOutline = CreateBlurBrushOutline();

            private static Vector2[] CreateBlurBrushOutline()
            {
                const int steps = 16;
                var points = new Vector2[steps * 4];
                void Curve(int segment, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
                {
                    for (int i = 0; i < steps; i++)
                    {
                        float t = i / (float)steps;
                        float u = 1f - t;
                        points[segment * steps + i] = u * u * u * a + 3f * u * u * t * b
                            + 3f * u * t * t * c + t * t * t * d;
                    }
                }
                Curve(0, new Vector2(12f, 2.2f), new Vector2(10.2f, 5.1f), new Vector2(5.1f, 10.2f), new Vector2(5.1f, 14.1f));
                Curve(1, new Vector2(5.1f, 14.1f), new Vector2(5.1f, 18.5f), new Vector2(8.1f, 21.6f), new Vector2(12f, 21.6f));
                Curve(2, new Vector2(12f, 21.6f), new Vector2(15.9f, 21.6f), new Vector2(18.9f, 18.5f), new Vector2(18.9f, 14.1f));
                Curve(3, new Vector2(18.9f, 14.1f), new Vector2(18.9f, 10.2f), new Vector2(13.8f, 5.1f), new Vector2(12f, 2.2f));
                return points;
            }

            private void DrawBlurBrush(MeshGenerationContext context)
            {
                Color ink = resolvedStyle.color;
                var mesh = context.Allocate(BlurBrushOutline.Length + 1, BlurBrushOutline.Length * 3);
                void AddVertex(Vector2 point)
                {
                    Vector2 position = P(point.x, point.y);
                    Color tint = ink;
                    tint.a *= Mathf.Lerp(0.015f, 0.55f, Mathf.InverseLerp(2f, 22f, point.y));
                    mesh.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = tint });
                }
                AddVertex(new Vector2(12f, 14.1f));
                foreach (Vector2 point in BlurBrushOutline) AddVertex(point);
                for (int i = 0; i < BlurBrushOutline.Length; i++)
                {
                    mesh.SetNextIndex(0);
                    mesh.SetNextIndex((ushort)((i + 1) % BlurBrushOutline.Length + 1));
                    mesh.SetNextIndex((ushort)(i + 1));
                }
                Painter2D painter = context.painter2D;
                painter.strokeColor = ink;
                painter.lineWidth = 1.35f;
                painter.BeginPath();
                painter.MoveTo(P(12f, 2.2f));
                painter.BezierCurveTo(P(10.2f, 5.1f), P(5.1f, 10.2f), P(5.1f, 14.1f));
                painter.BezierCurveTo(P(5.1f, 18.5f), P(8.1f, 21.6f), P(12f, 21.6f));
                painter.BezierCurveTo(P(15.9f, 21.6f), P(18.9f, 18.5f), P(18.9f, 14.1f));
                painter.BezierCurveTo(P(18.9f, 10.2f), P(13.8f, 5.1f), P(12f, 2.2f));
                painter.ClosePath();
                painter.Stroke();
            }

            private void DrawSmudgeBrush(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(12.27f, 2.02f));
                painter.BezierCurveTo(P(9.35f, 2.28f), P(3.33f, 2.92f), P(3.45f, 6.96f));
                painter.BezierCurveTo(P(3.51f, 9.06f), P(4.25f, 11.24f), P(5.42f, 12.32f));
                painter.BezierCurveTo(P(5.75f, 12.64f), P(6.30f, 12.62f), P(6.55f, 12.75f));
                painter.BezierCurveTo(P(6.20f, 13.25f), P(5.75f, 13.76f), P(5.31f, 14.39f));
                painter.BezierCurveTo(P(4.48f, 15.57f), P(3.53f, 16.94f), P(2.72f, 18.18f));
                painter.BezierCurveTo(P(2.01f, 19.26f), P(1.58f, 20.36f), P(2.29f, 21.14f));
                painter.BezierCurveTo(P(3.00f, 21.92f), P(4.02f, 21.57f), P(4.81f, 20.80f));
                painter.BezierCurveTo(P(6.67f, 18.98f), P(8.04f, 16.59f), P(10.54f, 14.98f));
                painter.BezierCurveTo(P(12.76f, 13.55f), P(18.56f, 10.15f), P(16.56f, 15.12f));
                painter.BezierCurveTo(P(15.85f, 16.89f), P(12.56f, 15.10f), P(11.79f, 16.75f));
                painter.BezierCurveTo(P(10.03f, 20.55f), P(17.43f, 18.50f), P(18.76f, 17.75f));
                painter.BezierCurveTo(P(21.84f, 16.01f), P(22.40f, 11.77f), P(22.35f, 8.65f));
                painter.BezierCurveTo(P(22.33f, 7.50f), P(21.74f, 6.57f), P(20.80f, 5.91f));
                painter.BezierCurveTo(P(18.10f, 4.02f), P(15.35f, 3.01f), P(12.27f, 2.02f));
                painter.ClosePath();
                painter.Fill();
            }

            private void DrawHealingBrush(Painter2D painter)
            {
                const float diagonal = 0.70710678f;
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Vector2 center = this.LocalToWorld(contentRect.center) * pixelsPerPoint;
                center = this.WorldToLocal(new Vector2(Mathf.Floor(center.x) + 0.5f,
                    Mathf.Floor(center.y) + 0.5f) / pixelsPerPoint);
                float unit = Mathf.Min(contentRect.width, contentRect.height) * (20f / 22f)
                    / (2f * (9.09f * diagonal + 4.05f));
                Vector2 Point(float along, float across) => center +
                    new Vector2((along + across) * diagonal, (across - along) * diagonal) * unit;

                void Wing(float direction)
                {
                    painter.MoveTo(Point(direction * 5.07f, -4.05f));
                    painter.LineTo(Point(direction * 9.09f, -4.05f));
                    painter.Arc(Point(direction * 9.09f, 0f), 4.05f * unit,
                        -135f, direction > 0f ? 45f : -315f,
                        direction > 0f ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                    painter.LineTo(Point(direction * 5.07f, 4.05f));
                    painter.ClosePath();
                }

                void Corner(float along, float across, float start, float end)
                    => painter.Arc(Point(along, across), 1.11f * unit, start, end);

                void Pore(float along, float across)
                {
                    Vector2 point = Point(along, across);
                    painter.MoveTo(point + Vector2.right * (1.07f * unit));
                    painter.Arc(point, 1.07f * unit, 0f, 360f);
                    painter.ClosePath();
                }

                Color ink = resolvedStyle.color;
                ink.a = 1f;
                painter.fillColor = ink;
                painter.BeginPath();
                Wing(1f);
                Wing(-1f);
                painter.MoveTo(Point(-3.22f, -4.75f));
                painter.LineTo(Point(3.22f, -4.75f));
                Corner(3.22f, -3.64f, -135f, -45f);
                painter.LineTo(Point(4.33f, 3.64f));
                Corner(3.22f, 3.64f, -45f, 45f);
                painter.LineTo(Point(-3.22f, 4.75f));
                Corner(-3.22f, 3.64f, 45f, 135f);
                painter.LineTo(Point(-4.33f, -3.64f));
                Corner(-3.22f, -3.64f, 135f, 225f);
                painter.ClosePath();
                Pore(-2.2f, -2.2f);
                Pore(-2.2f, 2.2f);
                Pore(2.2f, -2.2f);
                Pore(2.2f, 2.2f);
                Pore(0f, 0f);
                painter.Fill(FillRule.OddEven);
            }

            private void DrawPencil(Painter2D painter)
            {
                float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                float unit = Mathf.Min(contentRect.width, contentRect.height) / 22f;
                Vector2 origin = this.LocalToWorld(contentRect.center - Vector2.one * (12f * unit)) * pixelsPerPoint;
                origin = this.WorldToLocal(new Vector2(Mathf.Round(origin.x), Mathf.Round(origin.y)) / pixelsPerPoint);
                Vector2 Point(float x, float y) => origin + new Vector2(x, y) * unit;
                painter.BeginPath();
                painter.MoveTo(Point(17f, 2.4f));
                painter.BezierCurveTo(Point(17.9f, 1.5f), Point(19.19f, 1.59f), Point(20.225f, 2.625f));
                painter.LineTo(Point(21.375f, 3.775f));
                painter.BezierCurveTo(Point(22.41f, 4.81f), Point(22.5f, 6.1f), Point(21.6f, 7f));
                painter.LineTo(Point(20.5f, 8.1f));
                painter.LineTo(Point(19.21f, 8.01f));
                painter.LineTo(Point(15.99f, 4.79f));
                painter.LineTo(Point(15.9f, 3.5f));
                painter.ClosePath();
                painter.MoveTo(Point(13.6f, 5.8f));
                painter.LineTo(Point(14.2f, 5.8f));
                painter.LineTo(Point(18.2f, 9.8f));
                painter.LineTo(Point(18.2f, 10.4f));
                painter.LineTo(Point(7.9f, 20.7f));
                painter.LineTo(Point(7.2f, 21f));
                painter.LineTo(Point(3.6f, 21f));
                painter.LineTo(Point(3f, 20.4f));
                painter.LineTo(Point(3f, 16.8f));
                painter.LineTo(Point(3.3f, 16.1f));
                painter.ClosePath();
                painter.MoveTo(Point(4f, 16.8f));
                painter.LineTo(Point(5.1f, 15.7f));
                painter.LineTo(Point(8.3f, 18.9f));
                painter.LineTo(Point(7.2f, 20f));
                painter.LineTo(Point(5f, 20f));
                painter.LineTo(Point(5.95f, 19.35f));
                painter.LineTo(Point(4.65f, 18.05f));
                painter.LineTo(Point(4f, 19f));
                painter.ClosePath();
                painter.Fill(FillRule.OddEven);
            }

            private void DrawTransform(Painter2D painter)
            {
                float ppi = EditorGUIUtility.pixelsPerPoint;
                Vector2 Point(float x, float y) => new Vector2(
                    contentRect.x + x * contentRect.width / 22f,
                    contentRect.y + y * contentRect.height / 22f);
                Vector2 Pixel(float x, float y)
                {
                    Vector2 world = this.LocalToWorld(Point(x, y)) * ppi;
                    return this.WorldToLocal(new Vector2(Mathf.Round(world.x), Mathf.Round(world.y)) / ppi);
                }

                void Box(float x0, float y0, float x1, float y1)
                {
                    painter.BeginPath();
                    TraceRectangle(painter, Pixel(x0, y0), Pixel(x1, y1));
                    painter.Fill();
                }
                Box(5, 3, 17, 4);
                Box(3, 5, 4, 17);
                Box(18, 5, 19, 11);
                Box(5, 18, 11, 19);
                Box(2, 2, 5, 5);
                Box(17, 2, 20, 5);
                Box(2, 17, 5, 20);

                float radius = 1.25f * Mathf.Min(contentRect.width, contentRect.height) / 22f;
                void Dot(float x, float y)
                {
                    painter.BeginPath();
                    painter.Arc(Point(x, y), radius, 0, 360);
                    painter.ClosePath();
                    painter.Fill();
                }
                Dot(11, 3.5f);
                Dot(3.5f, 11);
                Dot(18.5f, 11);
                Dot(11, 18.5f);

                painter.BeginPath();
                painter.MoveTo(Point(11.25f, 10.6f));
                painter.LineTo(Point(20.8f, 14.8f));
                painter.BezierCurveTo(Point(21.15f, 14.96f), Point(21.1f, 15.4f), Point(20.72f, 15.49f));
                painter.LineTo(Point(16.62f, 16.44f));
                painter.BezierCurveTo(Point(16.52f, 16.46f), Point(16.46f, 16.52f), Point(16.44f, 16.62f));
                painter.LineTo(Point(15.49f, 20.72f));
                painter.BezierCurveTo(Point(15.4f, 21.1f), Point(14.96f, 21.15f), Point(14.8f, 20.8f));
                painter.LineTo(Point(10.6f, 11.25f));
                painter.BezierCurveTo(Point(10.41f, 10.82f), Point(10.82f, 10.41f), Point(11.25f, 10.6f));
                painter.ClosePath();
                painter.Fill();
            }

            private void DrawHand(Painter2D painter, bool withHandle = false)
            {
                if (withHandle)
                {
                    painter.BeginPath();
                    painter.Arc(P(7f, 6.5f), 4.6f * contentRect.width / 24f, 120f, 325f);
                    painter.Stroke();
                }
                Vector2 HandPoint(float x, float y) => withHandle ? P(x * .8f + 4.5f, y * .8f + 4f) : P(x, y);
                painter.BeginPath();
                painter.MoveTo(HandPoint(7f, 12.2f));
                painter.LineTo(HandPoint(7f, 6f));
                painter.BezierCurveTo(HandPoint(7f, 3.8f), HandPoint(10f, 3.8f), HandPoint(10f, 6f));
                painter.LineTo(HandPoint(10f, 11f));
                painter.LineTo(HandPoint(10f, 3.8f));
                painter.BezierCurveTo(HandPoint(10f, 1.6f), HandPoint(13f, 1.6f), HandPoint(13f, 3.8f));
                painter.LineTo(HandPoint(13f, 11f));
                painter.LineTo(HandPoint(13f, 5f));
                painter.BezierCurveTo(HandPoint(13f, 2.8f), HandPoint(16f, 2.8f), HandPoint(16f, 5f));
                painter.LineTo(HandPoint(16f, 11.8f));
                painter.LineTo(HandPoint(16f, 8f));
                painter.BezierCurveTo(HandPoint(16f, 5.8f), HandPoint(19f, 5.8f), HandPoint(19f, 8f));
                painter.LineTo(HandPoint(19f, 14f));
                painter.BezierCurveTo(HandPoint(19f, 18f), HandPoint(17f, 18.5f), HandPoint(17f, 21f));
                painter.LineTo(HandPoint(9f, 21f));
                painter.BezierCurveTo(HandPoint(9f, 18.7f), HandPoint(7f, 18f), HandPoint(5.8f, 16f));
                painter.LineTo(HandPoint(3.5f, 12.5f));
                painter.BezierCurveTo(HandPoint(2f, 10f), HandPoint(4.3f, 9.2f), HandPoint(5.8f, 11f));
                painter.LineTo(HandPoint(7f, 12.2f));
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
