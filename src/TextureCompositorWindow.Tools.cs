using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        private enum PreviewTool { None, Brush, BlurBrush, Transform, Fill, Zoom, Pencil, RectangleSelect, PolygonSelect, Shape }

        [NonSerialized] private PreviewTool previewTool = PreviewTool.None;
        [NonSerialized] private PreviewTool previewSettingsTool = PreviewTool.None;
        [NonSerialized] private PreviewTool previewTransformReturnTool = PreviewTool.None;
        [NonSerialized] private PaintToolSettings paintSettings = new PaintToolSettings();
        private const string PaintToolSettingsPrefKey = "DCFApixels.WhimTex.PaintToolSettings";
        private const string PreviewToolPrefKey = "DCFApixels.WhimTex.PreviewTool";
        private const string PreviewTransformReturnToolPrefKey = "DCFApixels.WhimTex.PreviewTransformReturnTool";
        [NonSerialized] private bool conversionPromptOpen;
        [NonSerialized] private RenderTexture blurSampleTexture;
        [NonSerialized] private float paintingPressure = 1f;
        [NonSerialized] private Button previewNoneButton;
        [NonSerialized] private Button previewBrushButton;
        [NonSerialized] private Button previewBlurBrushButton;
        [NonSerialized] private Button previewPencilButton;
        [NonSerialized] private Button previewTransformButton;
        [NonSerialized] private Button previewFillButton;
        [NonSerialized] private Button previewZoomButton;
        [NonSerialized] private Button previewRectangleSelectButton;
        [NonSerialized] private Button previewPolygonSelectButton;

        private bool IsPreviewPaintTool => previewTool == PreviewTool.Brush || previewTool == PreviewTool.BlurBrush || previewTool == PreviewTool.Pencil;
        private bool HasPreviewLayers
        {
            get
            {
                if (compositor != null && compositor.layers != null)
                    foreach (Layer layer in compositor.layers)
                        if (layer != null) return true;
                return false;
            }
        }
        private bool IsPreviewBrushEnabled => (previewTool == PreviewTool.Brush || previewTool == PreviewTool.Pencil) &&
            (previewTool != PreviewTool.Brush || paintSettings.dynamics.source != BrushTipSource.HLSL || paintSettings.dynamics.tip != null) &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);
        private bool IsPreviewBlurBrushEnabled => previewTool == PreviewTool.BlurBrush &&
            GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);
        private bool IsPreviewFillEnabled => previewTool == PreviewTool.Fill && GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour layer && !WhimTexApi.IsLayerContentLocked(compositor, layer);

        private void ApplyPreviewTextureFilter()
        {
            FilterMode filter = previewTool == PreviewTool.Pencil ? FilterMode.Point : compositor != null ? compositor.outputFilter : FilterMode.Bilinear;
            if (previewTexture != null) previewTexture.filterMode = filter;
            if (channelPreviewTexture != null) channelPreviewTexture.filterMode = filter;
            if (postFxTexture != null) postFxTexture.filterMode = filter;
        }

        private static PreviewTool ParsePreviewTool(string value)
        {
            return Enum.TryParse(value, out PreviewTool tool) && Enum.IsDefined(typeof(PreviewTool), tool)
                ? tool : PreviewTool.None;
        }

        private void LoadPreviewToolSettings()
        {
            previewTool = ParsePreviewTool(EditorPrefs.GetString(PreviewToolPrefKey, string.Empty));
            previewSettingsTool = previewTool;
            previewTransformReturnTool = ParsePreviewTool(
                EditorPrefs.GetString(PreviewTransformReturnToolPrefKey, string.Empty));
            if (previewTransformReturnTool == PreviewTool.Transform)
                previewTransformReturnTool = PreviewTool.None;
        }

        private void LoadPaintToolSettings()
        {
            paintSettings?.ReleasePresetTip();
            paintSettings ??= new PaintToolSettings();
            try
            {
                if (EditorPrefs.HasKey(PaintToolSettingsPrefKey))
                    JsonUtility.FromJsonOverwrite(EditorPrefs.GetString(PaintToolSettingsPrefKey), paintSettings);
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
            UpdateToolkitPreviewPresentation();
        }

        private void ApplyPaintToolChange(Action change)
        {
            FinishPaintingStroke();
            change();
            paintSettings.dynamics.Normalize();
            SavePaintToolSettings();
            toolkitHeaderBindings.Refresh();
            brushSettingsBindings?.Refresh();
            UpdateToolkitPreviewPresentation();
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
            PaintStrokeParameters parameters = previewTool == PreviewTool.Pencil
                ? paintSettings.GetPencilParameters(paintingErase, GetPaintingColor())
                : paintSettings.GetStrokeParameters(paintingErase, GetPaintingColor());
            if (tiledPreview) parameters = parameters.WithCanvasWrap();
            return parameters.WithSelectionMask(GetAreaSelectionTexture());
        }

        private bool HandlePaintConversionPrompt(PointerDownEvent evt)
        {
            if (!HasPreviewLayers)
            {
                WhimTexUI.ConsumeEvent(evt);
                return true;
            }
            bool painting = IsPreviewPaintTool && (evt.button == 0 || evt.button == 1);
            bool filling = previewTool == PreviewTool.Fill && evt.button == 0;
            if ((!painting && !filling) || evt.altKey || compositor == null ||
                !PreviewContainsPaintPoint(evt.localPosition) || GetSelectedLayer()?.Behaviour is DrawingLayerBehaviour && !WhimTexApi.IsLayerContentLocked(compositor, GetSelectedLayer()))
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

        private bool IsPreviewToolAvailable(PreviewTool tool)
        {
            Layer layer = GetSelectedLayer();
            switch (tool)
            {
                case PreviewTool.Brush:
                case PreviewTool.Pencil:
                case PreviewTool.Fill: return layer?.Behaviour is DrawingLayerBehaviour;
                case PreviewTool.BlurBrush: return layer?.Behaviour != null;
                case PreviewTool.Transform: return layer?.Behaviour != null;
                case PreviewTool.Zoom:
                case PreviewTool.Shape:
                case PreviewTool.RectangleSelect:
                case PreviewTool.PolygonSelect: return compositor != null;
                default: return false;
            }
        }

        private void BindPreviewSettingsRow(VisualElement row, PreviewTool tool)
        {
            toolkitHeaderBindings.Add(() =>
            {
                bool empty = previewTool == PreviewTool.None;
                bool visible = (empty ? previewSettingsTool : previewTool) == tool;
                row.EnableInClassList("whimtex-tool-options--hidden", !visible);
                row.EnableInClassList("whimtex-tool-options--empty", empty);
            });
        }

        private VisualElement BuildPreviewToolToolbar()
        {
            VisualElement toolbar = new VisualElement { name = "previewTools" };
            toolbar.AddToClassList("whimtex-tools");
            toolbar.EnableInClassList("whimtex-tools--light", !EditorGUIUtility.isProSkin);
            previewNoneButton = CreatePreviewToolButton("noTool", PreviewTool.None,
                "Layer Select (V). Click visible pixels to select a layer. Click a selected group again to select inside it. Shift toggles selection; Ctrl selects nested layers directly. Click empty space to deselect.");
            previewBrushButton = CreatePreviewToolButton("brushTool", PreviewTool.Brush,
                "Brush (B). Paint on the selected Drawing layer. Choose Brush/Eraser in the header; RMB temporarily erases.");
            previewBlurBrushButton = CreatePreviewToolButton("blurBrushTool", PreviewTool.BlurBrush,
                "Blur Brush. Paint a soft circular blur on the selected Drawing layer. Choose the current layer or the layers below as the sample.");
            previewPencilButton = CreatePreviewToolButton("pencilTool", PreviewTool.Pencil,
                "Pencil (P). Paint crisp pixels with a Circle, Square or Diamond tip. RMB temporarily erases; [ and ] change size.");
            previewFillButton = CreatePreviewToolButton("fillTool", PreviewTool.Fill,
                "Fill (G). Fill similar pixels on the selected Drawing layer, sampling this layer or all visible layers. Contiguous limits the fill to the clicked region.");
            previewTransformButton = CreatePreviewToolButton("transformTool", PreviewTool.Transform,
                "Transform (T). Drag inside to move, handles to scale, circle to rotate. " +
                "Drag the gold cross to move the pivot without moving the image (requires nonzero scale). " +
                "The pivot snaps to frame anchors; hold Ctrl to disable snapping. " +
                "Shift: constrain movement / preserve proportions / snap rotation to 15°. Groups are not supported yet.");
            toolbar.Add(previewNoneButton);
            toolbar.Add(previewTransformButton);
            previewRectangleSelectButton = CreatePreviewToolButton("rectangleSelectTool", PreviewTool.RectangleSelect,
                "Area Select (M). Hold or drag this button to choose Rectangle, Ellipse or UV Island. Shift adds; Alt subtracts; Ctrl+D deselects. Selection limits painting and filling.");
            marqueePicker = new ShapePickerManipulator(this, true);
            previewRectangleSelectButton.AddManipulator(marqueePicker);
            previewPolygonSelectButton = CreatePreviewToolButton("polygonSelectTool", PreviewTool.PolygonSelect,
                "Polygonal Lasso (L). Click vertices; Enter, double-click or click the first point to close. Backspace/RMB removes a vertex; Escape cancels.");
            toolbar.Add(previewRectangleSelectButton);
            toolbar.Add(previewPolygonSelectButton);
            previewShapeButton = CreatePreviewToolButton("shapeTool", PreviewTool.Shape,
                "Shape (U). Hold or drag this button to pick a figure, then release over its icon. Drag on the canvas to create it.");
            shapePicker = new ShapePickerManipulator(this);
            previewShapeButton.AddManipulator(shapePicker);
            toolbar.Add(previewShapeButton);
            toolbar.Add(previewBrushButton);
            toolbar.Add(previewBlurBrushButton);
            toolbar.Add(previewPencilButton);
            toolbar.Add(previewFillButton);
            previewZoomButton = CreatePreviewToolButton("zoomTool", PreviewTool.Zoom,
                "Zoom (Z). Click to zoom in, drag a rectangle to frame an area, or Alt-click to zoom out. MMB-drag pans the preview.");
            toolbar.Add(previewZoomButton);
            return toolbar;
        }

        private Button CreatePreviewToolButton(string name, PreviewTool tool, string tooltip)
        {
            Button button = new Button(() => SetPreviewTool(tool)) { name = name, tooltip = tooltip };
            button.AddToClassList("whimtex-tool-button");
            if (tool == PreviewTool.Shape)
                button.Add(shapeToolIcon = new ShapeToolIcon(shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle));
            else if (tool == PreviewTool.RectangleSelect)
                button.Add(marqueeToolIcon = new PreviewToolIcon(tool, marqueeShape == MarqueeShape.Ellipse, marqueeShape == MarqueeShape.UvIsland));
            else
                button.Add(new PreviewToolIcon(tool));
            if (tool == PreviewTool.Shape || tool == PreviewTool.RectangleSelect)
                button.Add(new ToolDropdownMarker());
            return button;
        }

        private void RefreshPreviewToolToolbar()
        {
            RefreshPostFxPanel();
            bool hasLayers = HasPreviewLayers;
            PreviewTool displayedTool = previewTool;
            Layer selected = hasLayers ? GetSelectedLayer() : null;
            shapeToolIcon?.SetKind(shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle);
            marqueeToolIcon?.SetEllipse(marqueeShape == MarqueeShape.Ellipse);
            marqueeToolIcon?.SetUv(marqueeShape == MarqueeShape.UvIsland);
            if (!IsUvSelectionTool) SetUvHovered(-1);
            if (uvDisplayedSelection != IsUvSelectionTool || uvDisplayedLayers != hasLayers)
            {
                uvDisplayedSelection = IsUvSelectionTool; uvDisplayedLayers = hasLayers;
                uvOverlay?.MarkDirtyRepaint();
            }
            previewShapeButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Shape);
            previewShapeButton?.EnableInClassList("whimtex-tool-button--unavailable", compositor == null);
            previewRectangleSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.RectangleSelect);
            previewPolygonSelectButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.PolygonSelect);
            previewRectangleSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            previewPolygonSelectButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            previewZoomButton?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
            previewZoomButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Zoom);
            previewNoneButton?.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.None);
            if (previewBrushButton != null)
            {
                previewBrushButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                previewBrushButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Brush);
            }
            if (previewBlurBrushButton != null)
            {
                previewBlurBrushButton.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers);
                previewBlurBrushButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.BlurBrush);
            }
            if (previewPencilButton != null)
            {
                previewPencilButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                previewPencilButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Pencil);
            }
            if (previewTransformButton != null)
            {
                previewTransformButton.EnableInClassList("whimtex-tool-button--unavailable", selected == null);
                previewTransformButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Transform);
            }
            if (previewFillButton != null)
            {
                previewFillButton.EnableInClassList("whimtex-tool-button--unavailable", !(selected?.Behaviour is DrawingLayerBehaviour));
                previewFillButton.EnableInClassList("whimtex-tool-button--selected", displayedTool == PreviewTool.Fill);
            }
        }

        private sealed class PreviewToolIcon : VisualElement
        {
            private readonly PreviewTool tool;
            private bool ellipse;
            private bool uv;

            internal PreviewToolIcon(PreviewTool tool, bool ellipse = false, bool uv = false)
            {
                this.tool = tool;
                this.ellipse = ellipse;
                this.uv = uv;
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

            internal void SetUv(bool value)
            {
                if (uv == value) return;
                uv = value; MarkDirtyRepaint();
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
                if (tool == PreviewTool.None)
                    DrawPointer(painter);
                else if (tool == PreviewTool.Transform)
                    DrawHand(painter);
                else if (tool == PreviewTool.Fill)
                    DrawBucket(painter);
                else if (tool == PreviewTool.Zoom)
                    DrawMagnifier(painter);
                else if (tool == PreviewTool.Pencil)
                    DrawPencil(painter);
                else if (tool == PreviewTool.BlurBrush)
                    DrawBlurBrush(painter);
                else if (tool == PreviewTool.RectangleSelect)
                {
                    if (uv) DrawUvSelect(painter);
                    else if (ellipse) DrawEllipseSelect(painter);
                    else DrawRectangleSelect(painter);
                }
                else if (tool == PreviewTool.PolygonSelect)
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

            private void DrawBlurBrush(Painter2D painter)
            {
                Color ink = resolvedStyle.color;
                Color top = new Color(ink.r, ink.g, ink.b, ink.a * 0.015f);
                Color bottom = new Color(ink.r, ink.g, ink.b, ink.a * 0.55f);
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(top, 0f), new GradientColorKey(bottom, 1f) },
                    new[] { new GradientAlphaKey(top.a, 0f), new GradientAlphaKey(bottom.a, 1f) });
                painter.fillGradient = FillGradient.MakeLinearGradient(
                    gradient, P(0f, 2f), P(0f, 22f), AddressMode.Clamp);
                painter.strokeColor = ink;
                painter.lineWidth = 1.35f;
                painter.BeginPath();
                painter.MoveTo(P(12f, 2.2f));
                painter.BezierCurveTo(P(10.2f, 5.1f), P(5.1f, 10.2f), P(5.1f, 14.1f));
                painter.BezierCurveTo(P(5.1f, 18.5f), P(8.1f, 21.6f), P(12f, 21.6f));
                painter.BezierCurveTo(P(15.9f, 21.6f), P(18.9f, 18.5f), P(18.9f, 14.1f));
                painter.BezierCurveTo(P(18.9f, 10.2f), P(13.8f, 5.1f), P(12f, 2.2f));
                painter.ClosePath();
                painter.Fill();
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

            private void DrawHand(Painter2D painter)
            {
                painter.BeginPath();
                painter.MoveTo(P(7f, 12.2f));
                painter.LineTo(P(7f, 6f));
                painter.BezierCurveTo(P(7f, 3.8f), P(10f, 3.8f), P(10f, 6f));
                painter.LineTo(P(10f, 11f));
                painter.LineTo(P(10f, 3.8f));
                painter.BezierCurveTo(P(10f, 1.6f), P(13f, 1.6f), P(13f, 3.8f));
                painter.LineTo(P(13f, 11f));
                painter.LineTo(P(13f, 5f));
                painter.BezierCurveTo(P(13f, 2.8f), P(16f, 2.8f), P(16f, 5f));
                painter.LineTo(P(16f, 11.8f));
                painter.LineTo(P(16f, 8f));
                painter.BezierCurveTo(P(16f, 5.8f), P(19f, 5.8f), P(19f, 8f));
                painter.LineTo(P(19f, 14f));
                painter.BezierCurveTo(P(19f, 18f), P(17f, 18.5f), P(17f, 21f));
                painter.LineTo(P(9f, 21f));
                painter.BezierCurveTo(P(9f, 18.7f), P(7f, 18f), P(5.8f, 16f));
                painter.LineTo(P(3.5f, 12.5f));
                painter.BezierCurveTo(P(2f, 10f), P(4.3f, 9.2f), P(5.8f, 11f));
                painter.LineTo(P(7f, 12.2f));
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
