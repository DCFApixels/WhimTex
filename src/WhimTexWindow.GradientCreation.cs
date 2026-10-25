using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum GradientCreationType { Linear, Radial, Angular, Square, Freeform, Mesh }
        private const float FreeformCreationHeight = .8660254f;
        private static Vector2 FreeformCreationApex(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            return (start + end) * .5f + new Vector2(-delta.y, delta.x) * FreeformCreationHeight;
        }
        [Serializable]
        private sealed class GradientCreationSettings
        {
            public GradientCreationType type;
            public int meshRows = 2, meshColumns = 2;
            public WhimTexGradient gradient = GradientUtility.Create(GradientUtility.WhiteToBlack);
        }
        [SerializeField] private GradientCreationSettings gradientCreationSettings = new GradientCreationSettings();
        private GradientCreationManipulator gradientCreationManipulator;
        private GradientPickerManipulator gradientPicker;
        private CanvasToolIcon gradientCreationToolIcon;

        internal static VisualElement CreateGradientTypeIcon(GradientLayerBehaviour.GradientType type) =>
            new CanvasToolIcon(CanvasTool.Gradient, gradientType: ToGradientCreationType(type), largeGradient: true);

        internal static VisualElement CreateFigureFillTypeIcon(VectorFillMode type) => type switch
        {
            VectorFillMode.None or VectorFillMode.Solid => new CanvasToolIcon(CanvasTool.Gradient, largeGradient: true, flatFill: type),
            VectorFillMode.LinearGradient => CreateGradientTypeIcon(GradientLayerBehaviour.GradientType.Linear),
            VectorFillMode.RadialGradient => CreateGradientTypeIcon(GradientLayerBehaviour.GradientType.Radial),
            VectorFillMode.AngularGradient => CreateGradientTypeIcon(GradientLayerBehaviour.GradientType.Angular),
            VectorFillMode.SquareGradient => CreateGradientTypeIcon(GradientLayerBehaviour.GradientType.Square),
            VectorFillMode.Freeform => CreateGradientTypeIcon(GradientLayerBehaviour.GradientType.Freeform),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static GradientCreationType ToGradientCreationType(GradientLayerBehaviour.GradientType type) => type switch
        {
            GradientLayerBehaviour.GradientType.Linear => GradientCreationType.Linear,
            GradientLayerBehaviour.GradientType.Radial => GradientCreationType.Radial,
            GradientLayerBehaviour.GradientType.Angular => GradientCreationType.Angular,
            GradientLayerBehaviour.GradientType.Square => GradientCreationType.Square,
            GradientLayerBehaviour.GradientType.Freeform => GradientCreationType.Freeform,
            GradientLayerBehaviour.GradientType.Mesh => GradientCreationType.Mesh,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static GradientLayerBehaviour.GradientType ToGradientLayerType(GradientCreationType type) => type switch
        {
            GradientCreationType.Linear => GradientLayerBehaviour.GradientType.Linear,
            GradientCreationType.Radial => GradientLayerBehaviour.GradientType.Radial,
            GradientCreationType.Angular => GradientLayerBehaviour.GradientType.Angular,
            GradientCreationType.Square => GradientLayerBehaviour.GradientType.Square,
            GradientCreationType.Freeform => GradientLayerBehaviour.GradientType.Freeform,
            GradientCreationType.Mesh => GradientLayerBehaviour.GradientType.Mesh,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private void SetGradientCreationType(GradientCreationType type)
        {
            gradientCreationManipulator?.Cancel();
            gradientCreationSettings.type = type;
            SaveToolPreferences();
            gradientCreationToolIcon?.SetGradientType(type);
            if (canvasGradientButton != null) canvasGradientButton.tooltip = GetGradientCreationToolTooltip();
            toolkitHeaderBindings?.Refresh();
            RefreshCanvasPointerCursor();
        }

        private string GetGradientCreationToolTooltip() =>
            "Gradient: " + (gradientCreationSettings?.type ?? GradientCreationType.Linear) +
            ". Hold or drag this button to choose a type, then release over its icon. " +
            "Drag on the canvas to create an editable Gradient layer. Shift constrains the angle; Ctrl disables snapping; Escape cancels.";

        private sealed class GradientPickerManipulator : ToolSubtypePickerManipulator
        {
            internal GradientPickerManipulator(WhimTexWindow owner)
                : base(owner, Enum.GetValues(typeof(GradientCreationType)).Length) { }
            protected override string MenuName => "gradientPicker";
            protected override string ItemName(int index) => "gradient" + (GradientCreationType)index;
            protected override string ItemTooltip(int index) => "Gradient: " + (GradientCreationType)index;
            protected override bool IsSelected(int index) => owner.gradientCreationSettings.type == (GradientCreationType)index;
            protected override VisualElement ItemIcon(int index) => new CanvasToolIcon(CanvasTool.Gradient, gradientType: (GradientCreationType)index);
            protected override void Select(int index) => owner.SetGradientCreationType((GradientCreationType)index);
            protected override void Activate() => owner.SetCanvasTool(CanvasTool.Gradient);
        }

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
            var type = new GradientTypeField("Type") { name = "gradientToolType" };
            type.AddToClassList("whimtex-view-field"); row.Add(type);
            toolkitHeaderBindings.Track(type, () => ToGradientLayerType(gradientCreationSettings.type));
            type.RegisterValueChangedCallback(e => SetGradientCreationType(ToGradientCreationType(e.newValue)));
            var ramp = new WhimTexGradientValueField("Gradient") { name = "gradientToolRamp" };
            ramp.AddToClassList("whimtex-gradient-tool-ramp"); row.Add(ramp);
            toolkitHeaderBindings.Track(ramp, () => gradientCreationSettings.gradient);
            ramp.RegisterValueChangedCallback(e =>
            {
                gradientCreationManipulator?.Cancel();
                gradientCreationSettings.gradient = e.newValue.Clone();
                SaveToolPreferences();
            });
            var columns = new IntegerField("Columns") { name = "gradientMeshColumns" }; columns.AddToClassList("whimtex-view-field"); row.Add(columns);
            var rows = new IntegerField("Rows") { name = "gradientMeshRows" }; rows.AddToClassList("whimtex-view-field"); row.Add(rows);
            toolkitHeaderBindings.Track(columns, () => gradientCreationSettings.meshColumns);
            toolkitHeaderBindings.Track(rows, () => gradientCreationSettings.meshRows);
            columns.RegisterValueChangedCallback(e => { gradientCreationSettings.meshColumns = Mathf.Clamp(e.newValue, 1, MeshGradient.MaxCells); SaveToolPreferences(); });
            rows.RegisterValueChangedCallback(e => { gradientCreationSettings.meshRows = Mathf.Clamp(e.newValue, 1, MeshGradient.MaxCells); SaveToolPreferences(); });
            toolkitHeaderBindings.Add(() =>
            {
                bool mesh = gradientCreationSettings.type == GradientCreationType.Mesh;
                columns.style.display = rows.style.display = mesh ? DisplayStyle.Flex : DisplayStyle.None;
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
                gradientType = ToGradientLayerType(type)
            };
            var layer = new Layer(behaviour);
            bool linear = type == GradientCreationType.Linear;
            bool freeform = type == GradientCreationType.Freeform;
            Vector2 center = linear || freeform ? (start + end) * .5f : start;
            if (freeform)
            {
                center = (center + FreeformCreationApex(start, end)) * .5f;
                float bottom = (1 - FreeformCreationHeight) * .5f;
                behaviour.freeform.points = new List<FreeformGradientPoint>
                {
                    new FreeformGradientPoint(new Vector2(0, bottom), behaviour.gradient.EvaluateEncoded(0)),
                    new FreeformGradientPoint(new Vector2(.5f, 1 - bottom), behaviour.gradient.EvaluateEncoded(.5f)),
                    new FreeformGradientPoint(new Vector2(1, bottom), behaviour.gradient.EvaluateEncoded(1))
                };
            }
            layer.transform.position = center - size * .5f;
            layer.transform.rotation = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + (type == GradientCreationType.Angular ? 180 : 0);
            float side = type == GradientCreationType.Angular ? Mathf.Min(size.x, size.y) : delta.magnitude * (linear || freeform ? 1 : 2);
            layer.transform.scale = new Double2(side / size.x, side / size.y);
            if (type == GradientCreationType.Mesh)
            {
                if (Mathf.Abs(delta.x) < 1 || Mathf.Abs(delta.y) < 1) return;
                behaviour.mesh = MeshGradient.Create(behaviour.gradient, gradientCreationSettings.meshColumns, gradientCreationSettings.meshRows);
                layer.transform.position = (start + end) * .5f - size * .5f;
                layer.transform.rotation = 0;
                layer.transform.scale = new Double2(Mathf.Abs(delta.x) / size.x, Mathf.Abs(delta.y) / size.y);
            }
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
            if ((freeform || type == GradientCreationType.Mesh) && canvasTool != CanvasTool.GradientHandles) SetCanvasTool(CanvasTool.GradientHandles);
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
                bool freeform = owner.gradientCreationSettings.type == GradientCreationType.Freeform;
                Vector2 apex = freeform ? View(FreeformCreationApex(start, end)) : default;
                var painter = context.painter2D;
                if (owner.gradientCreationSettings.type == GradientCreationType.Mesh)
                {
                    int columns = Mathf.Clamp(owner.gradientCreationSettings.meshColumns, 1, MeshGradient.MaxCells);
                    int rows = Mathf.Clamp(owner.gradientCreationSettings.meshRows, 1, MeshGradient.MaxCells);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        painter.lineWidth = pass == 0 ? 3 : 1; painter.strokeColor = pass == 0 ? Color.black : Color.white; painter.BeginPath();
                        for (int x = 0; x <= columns; x++) { float px = Mathf.Lerp(a.x, b.x, x / (float)columns); painter.MoveTo(new Vector2(px, a.y)); painter.LineTo(new Vector2(px, b.y)); }
                        for (int y = 0; y <= rows; y++) { float py = Mathf.Lerp(a.y, b.y, y / (float)rows); painter.MoveTo(new Vector2(a.x, py)); painter.LineTo(new Vector2(b.x, py)); }
                        painter.Stroke();
                    }
                    return;
                }
                painter.lineWidth = 3; painter.strokeColor = Color.black;
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b);
                if (freeform) { painter.LineTo(apex); painter.ClosePath(); }
                painter.Stroke();
                painter.lineWidth = 1; painter.strokeColor = Color.white;
                painter.Stroke();
                void Endpoint(Vector2 point)
                {
                    painter.fillColor = Color.white; painter.BeginPath();
                    painter.Arc(point, 3, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
                }
                if (freeform)
                {
                    var palette = owner.gradientCreationSettings.gradient;
                    DrawFreeformPoint(painter, a, palette.EvaluateEncoded(0));
                    DrawFreeformPoint(painter, apex, palette.EvaluateEncoded(.5f));
                    DrawFreeformPoint(painter, b, palette.EvaluateEncoded(1));
                }
                else { Endpoint(a); Endpoint(b); }
            }
        }
    }
}
