using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private enum CanvasTool
        {
            None, Brush, BlurBrush, Transform, Fill, Zoom, Pencil, RectangleSelect, PolygonSelect, Shape,
            GradientHandles, UvIslandSelect, FXTransform, FXPoint, FXNormal, HealingBrush, SmudgeBrush
        }

        [NonSerialized] private CanvasTool canvasTool = CanvasTool.None;
        [NonSerialized] private CanvasTool canvasTransformReturnTool = CanvasTool.None;
        [NonSerialized] private PaintToolSettings paintSettings = new PaintToolSettings();
        private const string PaintToolSettingsPrefKey = "DCFApixels.WhimTex.Canvas.PaintToolSettings";
        private const string CanvasToolPrefKey = "DCFApixels.WhimTex.Canvas.Tool";
        private const string CanvasTransformReturnToolPrefKey = "DCFApixels.WhimTex.Canvas.TransformReturnTool";
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
        [NonSerialized] private Button canvasZoomButton;
        [NonSerialized] private Button canvasRectangleSelectButton;
        [NonSerialized] private Button canvasPolygonSelectButton;
        private ScrollView canvasToolScroll;

        private bool IsCanvasPaintTool => canvasTool == CanvasTool.Brush || canvasTool == CanvasTool.BlurBrush || canvasTool == CanvasTool.SmudgeBrush || canvasTool == CanvasTool.Pencil;
        private bool HasCanvasLayers
        {
            get
            {
                if (compositor != null && compositor.layers != null)
                    foreach (Layer layer in compositor.layers)
                        if (layer != null) return true;
                return false;
            }
        }
        private bool IsCanvasBrushEnabled => (canvasTool == CanvasTool.Brush || canvasTool == CanvasTool.Pencil) &&
            (canvasTool != CanvasTool.Brush || paintSettings.dynamics.source != BrushTipSource.HLSL || paintSettings.dynamics.tip != null) &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);
        private bool IsCanvasBlurBrushEnabled => canvasTool == CanvasTool.BlurBrush &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);
        private bool IsCanvasSmudgeBrushEnabled => canvasTool == CanvasTool.SmudgeBrush &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);
        private bool IsCanvasFillEnabled => canvasTool == CanvasTool.Fill && GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);

        private void ApplyCanvasTextureFilter()
        {
            FilterMode filter = canvasTool == CanvasTool.Pencil ? FilterMode.Point : compositor != null ? compositor.outputFilter : FilterMode.Bilinear;
            if (canvasTexture != null) canvasTexture.filterMode = filter;
            if (channelCanvasTexture != null) channelCanvasTexture.filterMode = filter;
            if (postFxTexture != null) postFxTexture.filterMode = filter;
        }

        private static CanvasTool ParseCanvasTool(string value)
        {
            return Enum.TryParse(value, out CanvasTool tool) && Enum.IsDefined(typeof(CanvasTool), tool) && IsBaseCanvasTool(tool)
                ? tool : CanvasTool.None;
        }

        private void LoadCanvasToolSettings()
        {
            canvasTool = ParseCanvasTool(EditorPrefs.GetString(CanvasToolPrefKey, string.Empty));
            lastBaseCanvasTool = canvasTool;
            canvasTransformReturnTool = ParseCanvasTool(
                EditorPrefs.GetString(CanvasTransformReturnToolPrefKey, string.Empty));
            if (canvasTransformReturnTool == CanvasTool.Transform)
                canvasTransformReturnTool = CanvasTool.None;
        }

        private void LoadPaintToolSettings()
        {
            paintSettings?.ReleasePresetTip();
            paintSettings ??= new PaintToolSettings();
            try
            {
                if (EditorPrefs.HasKey(PaintToolSettingsPrefKey))
                {
                    JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(PaintToolSettingsPrefKey), paintSettings);
                }
            }
            catch (ArgumentException)
            {
                paintSettings = new PaintToolSettings();
            }
            paintSettings.dynamics ??= new BrushDynamics();
            paintSettings.dynamics.Normalize();
            paintSettings.dynamics.tip = null;
            paintSettings.TryRestoreBrushTip();
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
            SavePaintToolSettings();
            toolkitHeaderBindings.Refresh();
            brushSettingsBindings?.Refresh();
            UpdateToolkitCanvasPresentation();
            RefreshBrushPresetButton();
        }

        private void SavePaintToolSettings()
        {
            brushStrokePreviewDirty = true;
            paintSettings.RememberBrushTip();
            EditorPrefs.SetString(PaintToolSettingsPrefKey, JsonUtility.ToJson(paintSettings));
        }

        private PaintStrokeParameters GetPaintingParameters()
        {
            PaintStrokeParameters parameters = canvasTool == CanvasTool.Pencil
                ? paintSettings.GetPencilParameters(paintingErase, GetPaintingColor())
                : paintSettings.GetStrokeParameters(paintingErase, GetPaintingColor());
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
            if ((!painting && !filling) || evt.altKey || compositor == null ||
                !CanvasContainsPaintPoint(evt.localPosition) || GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour && !WhimTexApi.IsLayerContentLocked(compositor, GetSelectedLayer()))
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
                case CanvasTool.RectangleSelect:
                case CanvasTool.PolygonSelect: return compositor != null;
                default: return false;
            }
        }

        private static VisualElement CreateCanvasSettingsRow()
        {
            var row = new VisualElement();
            row.AddToClassList("whimtex-tool-settings-row");
            return row;
        }

        private void BindCanvasSettingsRow(VisualElement row, CanvasTool tool)
        {
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
                "Layer Select (V). Click visible pixels to select a layer. Click a selected group again to select inside it. Shift toggles selection; Ctrl selects nested layers directly. Click empty space to deselect.");
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
            canvasRectangleSelectButton = CreateCanvasToolButton("rectangleSelectTool", CanvasTool.RectangleSelect,
                "Area Select (M). Hold or drag this button to choose Rectangle or Ellipse. Shift adds; Alt subtracts; Ctrl+D deselects. Selection limits painting and filling.");
            marqueePicker = new ShapePickerManipulator(this, true);
            canvasRectangleSelectButton.AddManipulator(marqueePicker);
            canvasPolygonSelectButton = CreateCanvasToolButton("polygonSelectTool", CanvasTool.PolygonSelect,
                "Polygonal Lasso (L). Click vertices; Enter, double-click or click the first point to close. Backspace/RMB removes a vertex; Escape cancels.");
            toolbar.Add(canvasRectangleSelectButton);
            toolbar.Add(canvasPolygonSelectButton);
            canvasShapeButton = CreateCanvasToolButton("shapeTool", CanvasTool.Shape,
                "Shape (U). Hold or drag this button to pick a figure, then release over its icon. Drag on the canvas to create it.");
            shapePicker = new ShapePickerManipulator(this);
            canvasShapeButton.AddManipulator(shapePicker);
            toolbar.Add(canvasShapeButton);
            toolbar.Add(canvasBrushButton);
            toolbar.Add(canvasBlurBrushButton);
            toolbar.Add(canvasSmudgeBrushButton);
            canvasHealingButton = CreateCanvasToolButton("healingBrushTool", CanvasTool.HealingBrush,
                "Healing Brush. Paint over a defect, then release to reconstruct it from nearby pixels. Esc cancels. Writes only the selected Drawing layer.");
            toolbar.Add(canvasHealingButton);
            toolbar.Add(canvasPencilButton);
            toolbar.Add(canvasFillButton);
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
            canvasToolScroll.RegisterCallback<GeometryChangedEvent>(_ => RevealActiveCanvasTool());
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
            else
                button.Add(new CanvasToolIcon(tool));
            if (tool == CanvasTool.Shape || tool == CanvasTool.RectangleSelect)
                button.Add(new ToolDropdownMarker());
            return button;
        }

        private void RefreshCanvasToolToolbar()
        {
            RefreshPostFxPanel();
            RefreshContextToolButtons();
            bool hasLayers = HasCanvasLayers;
            CanvasTool displayedTool = canvasTool;
            Layer selected = hasLayers ? GetSelectedLayer() : null;
            shapeToolIcon?.SetKind(shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle);
            marqueeToolIcon?.SetEllipse(marqueeShape == MarqueeShape.Ellipse);
            if (!IsUvSelectionTool) SetUvHovered(-1);
            if (uvDisplayedSelection != IsUvSelectionTool || uvDisplayedLayers != hasLayers)
            {
                uvDisplayedSelection = IsUvSelectionTool; uvDisplayedLayers = hasLayers;
                uvOverlay?.MarkDirtyRepaint();
            }
            canvasShapeButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.Shape);
            canvasShapeButton?.EnableInClassList("whimtex-tool-button--unavailable", compositor == null);
            canvasRectangleSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.RectangleSelect);
            canvasPolygonSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == CanvasTool.PolygonSelect);
            canvasRectangleSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            canvasPolygonSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
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

            internal CanvasToolIcon(CanvasTool tool, bool ellipse = false)
            {
                this.tool = tool;
                this.ellipse = ellipse;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
            }

            internal void SetEllipse(bool value)
            {
                if (ellipse == value) return;
                ellipse = value;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f)
                    return;
                Painter2D painter = context.painter2D;
                painter.strokeColor = resolvedStyle.color;
                painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 1.35f;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                if (tool == CanvasTool.None)
                    DrawPointer(painter);
                else if (tool == CanvasTool.Transform)
                    DrawHand(painter);
                else if (tool == CanvasTool.GradientHandles || IsTemporaryCanvasTool(tool))
                    DrawHand(painter, withHandle: true);
                else if (tool == CanvasTool.UvIslandSelect)
                    DrawUvSelect(painter);
                else if (tool == CanvasTool.Fill)
                    DrawBucket(painter);
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
                    DrawPolygonSelect(painter);
                else
                    DrawBrush(painter);
            }

            private Vector2 P(float x, float y) => new Vector2(
                contentRect.x + x * contentRect.width / 24f,
                contentRect.y + y * contentRect.height / 24f);

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
                for (int i = 0; i < 12; i++)
                {
                    float start = i * Mathf.PI / 6f;
                    for (int j = 0; j <= 3; j++)
                    {
                        float angle = start + j * Mathf.PI / 27f;
                        Vector2 point = P(12f + Mathf.Cos(angle) * 9f, 12f + Mathf.Sin(angle) * 8f);
                        if (j == 0) painter.MoveTo(point); else painter.LineTo(point);
                    }
                }
                painter.Stroke();
            }
            private void DrawRectangleSelect(Painter2D painter)
            {
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                for (int i = 0; i < 4; i++)
                {
                    float a = 3f + i * 5f, b = Mathf.Min(a + 3f, 21f);
                    painter.MoveTo(P(a, 4)); painter.LineTo(P(b, 4));
                    painter.MoveTo(P(a, 20)); painter.LineTo(P(b, 20));
                    painter.MoveTo(P(3, a + 1)); painter.LineTo(P(3, Mathf.Min(b + 1, 20)));
                    painter.MoveTo(P(21, a + 1)); painter.LineTo(P(21, Mathf.Min(b + 1, 20)));
                }
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
                Color ink = resolvedStyle.color;
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.2f);
                painter.BeginPath();
                painter.MoveTo(P(4f, 11f));
                painter.LineTo(P(11f, 4f));
                painter.LineTo(P(18f, 11f));
                painter.LineTo(P(11f, 18f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(4f, 11f));
                painter.LineTo(P(18f, 11f));
                painter.MoveTo(P(11f, 8f));
                painter.LineTo(P(7.5f, 3f));
                painter.BezierCurveTo(P(5f, 0f), P(2f, 3f), P(4f, 6f));
                painter.Stroke();
                painter.fillColor = ink;
                painter.BeginPath();
                painter.MoveTo(P(19f, 13f));
                painter.BezierCurveTo(P(18f, 15f), P(16.5f, 17f), P(17f, 18.5f));
                painter.BezierCurveTo(P(18f, 21f), P(22f, 19.5f), P(21f, 17.5f));
                painter.ClosePath();
                painter.Fill();
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
                Color ink = resolvedStyle.color;
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.16f);
                painter.BeginPath();
                painter.MoveTo(P(9.8f, 10.6f));
                painter.BezierCurveTo(P(13.5f, 8.5f), P(17.6f, 4.1f), P(19.9f, 2.6f));
                painter.BezierCurveTo(P(21.4f, 1.6f), P(22.3f, 2.8f), P(21.3f, 4.2f));
                painter.BezierCurveTo(P(19.5f, 6.7f), P(15.8f, 11.1f), P(13.8f, 14.6f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.45f);
                painter.BeginPath();
                painter.MoveTo(P(9.8f, 10.6f));
                painter.LineTo(P(7.8f, 12.6f));
                painter.LineTo(P(11.8f, 16.6f));
                painter.LineTo(P(13.8f, 14.6f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();

                painter.fillColor = ink;
                painter.BeginPath();
                painter.MoveTo(P(7.8f, 12.6f));
                painter.BezierCurveTo(P(4.4f, 12f), P(3.9f, 15.4f), P(4.2f, 17.1f));
                painter.BezierCurveTo(P(4.5f, 19.2f), P(2.8f, 20.7f), P(1.8f, 21.4f));
                painter.BezierCurveTo(P(6.4f, 22.6f), P(11.7f, 20.9f), P(12.3f, 18.4f));
                painter.BezierCurveTo(P(12.5f, 17.6f), P(12.2f, 17f), P(11.8f, 16.6f));
                painter.ClosePath();
                painter.MoveTo(P(7.1f, 14.6f));
                painter.BezierCurveTo(P(5.9f, 16f), P(7f, 17.3f), P(5.1f, 19.6f));
                painter.BezierCurveTo(P(8f, 18.3f), P(7.2f, 16.6f), P(7.1f, 14.6f));
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
                // A single extended finger, distinct from the Transform hand.
                painter.BeginPath();
                painter.MoveTo(P(8, 20)); painter.LineTo(P(6, 16));
                painter.BezierCurveTo(P(4, 13), P(6, 11), P(8, 13));
                painter.LineTo(P(9, 14)); painter.LineTo(P(9, 5));
                painter.BezierCurveTo(P(9, 2), P(13, 2), P(13, 5));
                painter.LineTo(P(13, 11));
                painter.BezierCurveTo(P(15, 9), P(17, 10), P(17, 12));
                painter.BezierCurveTo(P(20, 11), P(21, 14), P(19, 17));
                painter.LineTo(P(17, 20)); painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(4, 5)); painter.LineTo(P(6, 5));
                painter.MoveTo(P(3, 8)); painter.LineTo(P(6, 8));
                painter.Stroke();
            }

            private void DrawHealingBrush(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(4, 14)); painter.LineTo(P(14, 4));
                painter.BezierCurveTo(P(18, 0), P(24, 6), P(20, 10));
                painter.LineTo(P(10, 20));
                painter.BezierCurveTo(P(6, 24), P(0, 18), P(4, 14));
                painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(8, 11)); painter.LineTo(P(13, 16));
                painter.MoveTo(P(11, 8)); painter.LineTo(P(16, 13));
                painter.Stroke();
            }

            private void DrawPencil(Painter2D painter)
            {
                Color ink = resolvedStyle.color;
                painter.fillColor = new Color(ink.r, ink.g, ink.b, ink.a * 0.25f);
                painter.BeginPath();
                painter.MoveTo(P(6f, 14f));
                painter.LineTo(P(16f, 4f));
                painter.LineTo(P(20f, 8f));
                painter.LineTo(P(10f, 18f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(P(8f, 16f));
                painter.LineTo(P(17f, 7f));
                painter.MoveTo(P(6f, 14f));
                painter.LineTo(P(3f, 21f));
                painter.LineTo(P(10f, 18f));
                painter.Stroke();
                painter.fillColor = ink;
                painter.BeginPath();
                painter.MoveTo(P(3f, 21f));
                painter.LineTo(P(4.5f, 17.5f));
                painter.LineTo(P(6.5f, 19.5f));
                painter.ClosePath();
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(P(17.5f, 2.5f));
                painter.LineTo(P(18.5f, 1.5f));
                painter.LineTo(P(22.5f, 5.5f));
                painter.LineTo(P(21.5f, 6.5f));
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
