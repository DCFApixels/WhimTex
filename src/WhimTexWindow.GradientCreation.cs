using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum GradientCreationType { Linear, Radial, Angular, Diamond, Square }
        [Serializable]
        private sealed class GradientCreationSettings
        {
            public GradientCreationType type;
            public WhimTexGradient gradient = GradientUtility.Create(GradientUtility.WhiteToBlack);
        }
        [SerializeField] private GradientCreationSettings gradientCreationSettings = new GradientCreationSettings();
        private GradientCreationManipulator gradientCreationManipulator;

        private void BuildGradientCreationTool()
        {
            gradientCreationManipulator?.Cancel();
            gradientCreationSettings ??= new GradientCreationSettings();
            gradientCreationManipulator = new GradientCreationManipulator(this);
            toolkitCanvas.AddManipulator(gradientCreationManipulator);
        }

        private void AddGradientCreationSettings()
        {
            gradientCreationSettings ??= new GradientCreationSettings();
            var row = CreateCanvasSettingsRow(); BindCanvasSettingsRow(row, CanvasTool.Gradient);
            var type = new EnumField("Type", gradientCreationSettings.type) { name = "gradientToolType" };
            type.AddToClassList("whimtex-view-field"); row.Add(type);
            toolkitHeaderBindings.Track(type, () => (Enum)gradientCreationSettings.type);
            type.RegisterValueChangedCallback(e =>
            {
                gradientCreationManipulator?.Cancel();
                gradientCreationSettings.type = (GradientCreationType)e.newValue;
            });
            var ramp = new WhimTexGradientValueField("Gradient") { name = "gradientToolRamp" };
            ramp.AddToClassList("whimtex-gradient-tool-ramp"); row.Add(ramp);
            toolkitHeaderBindings.Track(ramp, () => gradientCreationSettings.gradient);
            ramp.RegisterValueChangedCallback(e =>
            {
                gradientCreationManipulator?.Cancel();
                gradientCreationSettings.gradient = e.newValue.Clone();
            });
            toolkitCanvasViewHeader.Add(row);
        }

        private void CreateGradient(Vector2 start, Vector2 end, Layer insertionAnchor)
        {
            if (activeDocument == null) return;
            Vector2 size = new Vector2(activeDocument.width, activeDocument.height);
            Vector2 delta = end - start;
            if (!TextPointFinite(start) || !TextPointFinite(end) || delta.sqrMagnitude < .01f) return;
            GradientCreationType type = gradientCreationSettings.type;
            var behaviour = new GradientLayerBehaviour
            {
                gradient = gradientCreationSettings.gradient.Clone(),
                gradientType = type == GradientCreationType.Linear ? GradientLayerBehaviour.GradientType.Horizontal :
                    type == GradientCreationType.Angular ? GradientLayerBehaviour.GradientType.Circular :
                    type == GradientCreationType.Diamond ? GradientLayerBehaviour.GradientType.Diamond :
                    type == GradientCreationType.Square ? GradientLayerBehaviour.GradientType.Square : GradientLayerBehaviour.GradientType.Radial
            };
            var layer = new Layer(behaviour);
            bool linear = type == GradientCreationType.Linear;
            layer.transform.position = (linear ? (start + end) * .5f : start) - size * .5f;
            layer.transform.rotation = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + (type == GradientCreationType.Angular ? 180 : 0);
            layer.transform.scale = linear ? new Double2(delta.magnitude / size.x, 1) :
                new Double2(delta.magnitude * 2 / size.x, delta.magnitude * 2 / size.y);
            foreach (var key in behaviour.gradient.ColorKeys)
                if (key.color.r < 0 || key.color.g < 0 || key.color.b < 0 || key.color.maxColorComponent > 1)
                { layer.colorRange = LayerColorRange.HDR; layer.blendRange = LayerBlendRange.HDR; }
            List<Layer> container = activeDocument.layers; int index = 0;
            if (insertionAnchor != null && activeDocument.TryFindLayer(insertionAnchor, out var selectedContainer, out int selectedIndex))
            { container = selectedContainer; index = selectedIndex; }
            activeDocument.PlaceCanvasTransform(layer, container);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            try
            {
                AddLayer(container, index, layer, "Gradient");
                Undo.SetCurrentGroupName("Create Gradient");
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undo);
            }
            finally { Undo.IncrementCurrentGroup(); }
        }

        private sealed class GradientCreationManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly VisualElement overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            private int pointer = -1;
            private Vector2 start, end, dimensions;
            private WhimTexDocument document;
            private Layer anchor;
            internal bool IsDragging => pointer >= 0;
            internal GradientCreationManipulator(WhimTexWindow owner)
            {
                this.owner = owner;
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
                target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitCanvas.ViewChanged += Repaint;
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
                target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                owner.toolkitCanvas.ViewChanged -= Repaint;
                overlay.RemoveFromHierarchy();
            }
            private Vector2 Point(Vector2 position, bool control)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 p = owner.toolkitCanvas.ToCanvas(position);
                p = new Vector2((p.x - image.x) / image.width * dimensions.x, (image.yMax - p.y) / image.height * dimensions.y);
                return control ? p : owner.SnapCanvasGuidePoint(p);
            }
            private Vector2 View(Vector2 pixels)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                return owner.toolkitCanvas.ToView(new Vector2(image.x + pixels.x / dimensions.x * image.width, image.yMax - pixels.y / dimensions.y * image.height));
            }
            private void Down(PointerDownEvent e)
            {
                if (IsDragging || owner.canvasTool != CanvasTool.Gradient || owner.activeDocument == null || e.button != 0 || e.altKey ||
                    e.target != target || !target.contentRect.Contains(e.localPosition) || owner.toolkitCanvas.PixelScale <= 0) return;
                owner.FinishPaintingStroke(); owner.FinishCanvasTransform(); target.Focus();
                document = owner.activeDocument; dimensions = new Vector2(document.width, document.height); anchor = owner.GetSelectedLayer();
                start = end = Point(e.localPosition, e.ctrlKey);
                if (!TextPointFinite(start)) { Cancel(); return; }
                pointer = e.pointerId; target.CapturePointer(pointer); Repaint(); WhimTexUI.ConsumeEvent(e);
            }
            private void UpdatePoint(Vector2 position, bool control, bool shift)
            {
                end = Point(position, control);
                if (!shift) return;
                Vector2 delta = end - start;
                float angle = Mathf.Round(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg / 45) * 45 * Mathf.Deg2Rad;
                end = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * delta.magnitude;
            }
            private bool Valid => owner.activeDocument == document && owner.canvasTool == CanvasTool.Gradient &&
                ReferenceEquals(owner.GetSelectedLayer(), anchor) && document.width == dimensions.x && document.height == dimensions.y;
            private void Move(PointerMoveEvent e)
            {
                if (pointer != e.pointerId) return;
                if (!Valid) { Cancel(); return; }
                UpdatePoint(e.localPosition, e.ctrlKey, e.shiftKey); Repaint(); WhimTexUI.ConsumeEvent(e);
            }
            private void Up(PointerUpEvent e)
            {
                if (pointer != e.pointerId || e.button != 0) return;
                bool valid = Valid;
                UpdatePoint(e.localPosition, e.ctrlKey, e.shiftKey);
                Vector2 a = start, b = end; Layer insertion = anchor;
                Cancel();
                if (valid) owner.CreateGradient(a, b, insertion);
                WhimTexUI.ConsumeEvent(e);
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1; document = null; anchor = null;
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
                Repaint();
            }
            private void Repaint() => overlay.MarkDirtyRepaint();
            private void Lost(PointerCaptureOutEvent e) { if (pointer == e.pointerId) Cancel(); }
            private void Interrupted(PointerCancelEvent e) { if (pointer == e.pointerId) Cancel(); }
            private void Detached(DetachFromPanelEvent e) => Cancel();
            private void Key(KeyDownEvent e)
            {
                if (IsDragging && e.keyCode == KeyCode.Escape) { Cancel(); WhimTexUI.ConsumeEvent(e); }
            }
            private void Draw(MeshGenerationContext context)
            {
                if (!IsDragging || !Valid || !TextPointFinite(end)) return;
                Vector2 a = View(start), b = View(end);
                var painter = context.painter2D;
                painter.lineWidth = 3; painter.strokeColor = Color.black;
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
                painter.lineWidth = 1; painter.strokeColor = Color.white;
                painter.Stroke();
                void Endpoint(Vector2 point)
                {
                    painter.fillColor = Color.white; painter.BeginPath();
                    painter.Arc(point, 3, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
                }
                Endpoint(a); Endpoint(b);
            }
        }
    }
}
