using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [SerializeField] private ShapeLayerBehaviour shapeToolSettings = new ShapeLayerBehaviour();
        private Button previewShapeButton;
        private ShapeManipulator shapeManipulator;

        private void BuildShapeTool()
        {
            shapeToolSettings ??= new ShapeLayerBehaviour();
            shapeManipulator = new ShapeManipulator(this);
            toolkitPreviewCanvas.AddManipulator(shapeManipulator);
        }

        private void AddShapeSettings()
        {
            shapeToolSettings ??= new ShapeLayerBehaviour();
            var row = CreatePreviewSettingsRow();
            row.AddToClassList("whimtex-fill-settings");
            BindPreviewSettingsRow(row, PreviewTool.Shape);
            var kind = new EnumField(shapeToolSettings.kind);
            kind.AddToClassList("whimtex-shape-kind");
            kind.tooltip = "Drag to create a new Shape layer. Shift: equal proportions / 45-degree line. Ctrl: no guide snapping.";
            toolkitHeaderBindings.Track(kind, () => (Enum)shapeToolSettings.kind);
            kind.RegisterValueChangedCallback(evt =>
            {
                shapeManipulator?.Cancel();
                shapeToolSettings.kind = (ShapeLayerBehaviour.ShapeKind)evt.newValue;
                shapeToolIcon?.SetKind(shapeToolSettings.kind);
            });
            row.Add(kind);
            void Color(string label, Func<Color> get, Action<Color> set)
            {
                var field = WhimTexColorInputs.Bind(new ColorField(label), toolkitHeaderBindings, get);
                field.AddToClassList("whimtex-shape-color");
                field.RegisterValueChangedCallback(evt => { shapeManipulator?.Cancel(); set(evt.newValue); });
                row.Add(field);
            }
            var fill = new Toggle("Fill");
            toolkitHeaderBindings.Track(fill, () => shapeToolSettings.fill);
            fill.RegisterValueChangedCallback(evt => { shapeManipulator?.Cancel(); shapeToolSettings.fill = evt.newValue; });
            row.Add(fill);
            Color("", () => shapeToolSettings.fillColor, value => shapeToolSettings.fillColor = value);
            var stroke = new Toggle("Stroke");
            toolkitHeaderBindings.Track(stroke, () => shapeToolSettings.stroke);
            stroke.RegisterValueChangedCallback(evt => { shapeManipulator?.Cancel(); shapeToolSettings.stroke = evt.newValue; });
            row.Add(stroke);
            Color("", () => shapeToolSettings.strokeColor, value => shapeToolSettings.strokeColor = value);
            var width = new FloatField("Width") { tooltip = "Inside stroke width in canvas pixels." };
            width.AddToClassList("whimtex-view-field");
            toolkitHeaderBindings.Track(width, () => shapeToolSettings.strokeWidth);
            width.RegisterValueChangedCallback(evt =>
            {
                shapeManipulator?.Cancel();
                shapeToolSettings.strokeWidth = ShapeLayerBehaviour.Limit(evt.newValue, 0f, 8192f, 2f);
                width.SetValueWithoutNotify(shapeToolSettings.strokeWidth);
            });
            row.Add(width);
            toolkitPreviewHeader.Add(row);
        }

        private static TextureTransform ShapeDragTransform(Vector2 start, Vector2 end, Vector2 canvasSize,
            ShapeLayerBehaviour.ShapeKind kind, bool constrain, float lineWidth)
        {
            Vector2 delta = end - start;
            var result = TextureTransform.Default;
            if (kind == ShapeLayerBehaviour.ShapeKind.Line)
            {
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                if (constrain) angle = Mathf.Round(angle / 45f) * 45f;
                float length = delta.magnitude;
                Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                result.position = start + direction * length * .5f - canvasSize * .5f;
                result.rotation = angle;
                result.scale = new Vector2(Mathf.Max(1f, length) / canvasSize.x, Mathf.Max(1f, lineWidth) / canvasSize.y);
            }
            else
            {
                if (constrain)
                {
                    float size = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                    delta = new Vector2(delta.x < 0f ? -size : size, delta.y < 0f ? -size : size);
                }
                result.position = start + delta * .5f - canvasSize * .5f;
                result.scale = new Vector2(Mathf.Max(1f, Mathf.Abs(delta.x)) / canvasSize.x,
                    Mathf.Max(1f, Mathf.Abs(delta.y)) / canvasSize.y);
            }
            return result;
        }

        private sealed class ShapeManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            private readonly VisualElement overlay;
            private int pointer = -1;
            private Vector2 start, current, startView, dimensions;
            private TextureTransform placement;
            private TextureCompositor document;
            private Layer insertionAnchor;
            private ShapeLayerBehaviour shape;
            internal bool IsDragging => pointer >= 0;
            internal ShapeManipulator(TextureCompositorWindow owner)
            {
                this.owner = owner;
                overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                overlay.AddToClassList("whimtex-area-overlay");
                overlay.generateVisualContent += Draw;
            }
            protected override void RegisterCallbacksOnTarget()
            {
                target.Add(overlay);
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<GeometryChangedEvent>(Geometry);
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitPreviewCanvas.ViewChanged += overlay.MarkDirtyRepaint;
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<GeometryChangedEvent>(Geometry);
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitPreviewCanvas.ViewChanged -= overlay.MarkDirtyRepaint;
                overlay.RemoveFromHierarchy();
            }
            private Vector2 CanvasPoint(Vector2 position, bool control)
            {
                var canvas = owner.toolkitPreviewCanvas;
                Rect image = canvas.ImageRect;
                Vector2 p = canvas.ToCanvas(position);
                p = new Vector2((p.x - image.x) / image.width * dimensions.x,
                    (image.yMax - p.y) / image.height * dimensions.y);
                return control ? p : owner.SnapPreviewGuidePoint(p);
            }
            private void Down(PointerDownEvent evt)
            {
                if (IsDragging || owner.previewTool != PreviewTool.Shape || owner.compositor == null || evt.button != 0 ||
                    evt.altKey || !target.contentRect.Contains(evt.localPosition) || owner.toolkitPreviewCanvas.PixelScale <= 0f) return;
                owner.FinishPaintingStroke(); owner.FinishPreviewTransform();
                owner.Focus(); target.Focus();
                document = owner.compositor;
                dimensions = new Vector2(document.width, document.height);
                insertionAnchor = owner.GetSelectedLayer();
                var settings = owner.shapeToolSettings;
                shape = new ShapeLayerBehaviour { kind = settings.kind, fill = settings.fill, stroke = settings.stroke,
                    fillColor = WhimTexColorInputs.DisplayColor(settings.fillColor),
                    strokeColor = WhimTexColorInputs.DisplayColor(settings.strokeColor),
                    strokeWidth = settings.strokeWidth, roundness = settings.roundness,
                    cornerRoundness = settings.cornerRoundness, linkCorners = settings.linkCorners, sides = Mathf.Clamp(settings.sides, 3, 32),
                    innerRadius = ShapeLayerBehaviour.Limit(settings.innerRadius, .01f, 1f, .5f) };
                startView = evt.localPosition;
                start = CanvasPoint(evt.localPosition, evt.ctrlKey);
                pointer = evt.pointerId;
                target.CapturePointer(pointer);
                Update(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            private bool Valid => document != null && owner.compositor == document && owner.previewTool == PreviewTool.Shape &&
                dimensions == new Vector2(document.width, document.height);
            private void Update(Vector2 position, bool shift, bool control)
            {
                current = CanvasPoint(position, control);
                placement = ShapeDragTransform(start, current, dimensions, shape.kind, shift, 8f);
                overlay.MarkDirtyRepaint();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointer) return;
                if (!Valid || (evt.pressedButtons & 1) == 0) Cancel();
                else Update(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (!IsDragging || evt.pointerId != pointer || evt.button != 0) return;
                if (Valid && ((Vector2)evt.localPosition - startView).sqrMagnitude >= 9f)
                {
                    Update(evt.localPosition, evt.shiftKey, evt.ctrlKey);
                    var layer = new Layer(shape);
                    layer.transform = placement;
                    if (shape.fillColor.maxColorComponent > 1f || shape.strokeColor.maxColorComponent > 1f)
                    {
                        layer.colorRange = LayerColorRange.HDR;
                        layer.blendRange = LayerBlendRange.HDR;
                    }
                    List<Layer> container = document.layers;
                    int index = 0;
                    if (insertionAnchor != null && document.TryFindLayer(insertionAnchor, out var selectedContainer, out int selectedIndex))
                    { container = selectedContainer; index = selectedIndex; }
                    string namePrefix = shape.kind.ToString();
                    document.PlaceCanvasTransform(layer, container);
                    Cancel();
                    owner.AddLayer(container, index, layer, namePrefix);
                }
                else Cancel();
                WhimTexUI.ConsumeEvent(evt);
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1;
                shape = null; document = null; insertionAnchor = null;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                overlay.MarkDirtyRepaint();
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
            private void Key(KeyDownEvent evt)
            {
                if (IsDragging && evt.keyCode == KeyCode.Escape) { Cancel(); WhimTexUI.ConsumeEvent(evt); }
            }
            private void Draw(MeshGenerationContext context)
            {
                if (!IsDragging || !Valid || shape == null) return;
                var painter = context.painter2D;
                painter.strokeColor = new Color(.3f, .85f, 1f, .95f);
                painter.fillColor = new Color(.3f, .85f, 1f, .1f);
                painter.lineWidth = 1.5f;
                Vector2 half = Vector2.Scale(dimensions, placement.scale) * .5f;
                Vector2 center = dimensions * .5f + placement.positionF;
                float angle = placement.rotationF * Mathf.Deg2Rad;
                int count = shape.kind == ShapeLayerBehaviour.ShapeKind.Rectangle ? 4 :
                    shape.kind == ShapeLayerBehaviour.ShapeKind.Polygon ? shape.sides :
                    shape.kind == ShapeLayerBehaviour.ShapeKind.Star ? shape.sides * 2 : 64;
                painter.BeginPath();
                for (int i = 0; i < count; i++)
                {
                    Vector2 local;
                    if (shape.kind == ShapeLayerBehaviour.ShapeKind.Rectangle)
                        local = new Vector2(i == 0 || i == 3 ? -half.x : half.x, i < 2 ? -half.y : half.y);
                    else
                    {
                        float a = Mathf.PI * .5f + i * Mathf.PI * 2f / count;
                        float radius = shape.kind == ShapeLayerBehaviour.ShapeKind.Star && i % 2 == 1 ? shape.innerRadius : 1f;
                        local = new Vector2(Mathf.Cos(a) * half.x, Mathf.Sin(a) * half.y) * radius;
                        if (shape.kind == ShapeLayerBehaviour.ShapeKind.Line)
                        {
                            float cap = Mathf.Min(half.x, half.y);
                            local = new Vector2(Mathf.Cos(a) * cap + Mathf.Sign(Mathf.Cos(a)) * (half.x - cap),
                                Mathf.Sin(a) * cap + Mathf.Sign(Mathf.Sin(a)) * (half.y - cap));
                        }
                    }
                    Vector2 p = center + new Vector2(Mathf.Cos(angle) * local.x - Mathf.Sin(angle) * local.y,
                        Mathf.Sin(angle) * local.x + Mathf.Cos(angle) * local.y);
                    Rect image = owner.toolkitPreviewCanvas.ImageRect;
                    p = owner.toolkitPreviewCanvas.ToView(new Vector2(image.x + p.x / dimensions.x * image.width,
                        image.yMax - p.y / dimensions.y * image.height));
                    if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
                }
                painter.ClosePath(); painter.Fill(); painter.Stroke();
            }
        }
    }
}
