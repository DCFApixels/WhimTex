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
            VectorPen, VectorNodes, VectorBrush, QuickSelection, Lights
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

    }
}
