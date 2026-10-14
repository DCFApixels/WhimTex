using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum LassoMode { Polygonal, Freehand }
        [SerializeField] private LassoMode lassoMode = LassoMode.Polygonal;
        private LassoPickerManipulator lassoPicker;
        private CanvasToolIcon lassoToolIcon;
        private bool IsFreehandLasso => canvasTool == CanvasTool.PolygonSelect && lassoMode == LassoMode.Freehand;

        private void SetLassoMode(LassoMode mode)
        {
            if (mode != LassoMode.Polygonal && mode != LassoMode.Freehand) return;
            if (lassoMode != mode)
            {
                areaSelectionManipulator?.Cancel();
                lassoMode = mode;
            }
            RefreshCanvasToolToolbar();
            toolkitHeaderBindings.Refresh();
            UpdateToolkitCanvasPresentation();
            toolkitCanvas?.Focus();
        }

        private sealed class LassoPickerManipulator : ToolSubtypePickerManipulator
        {
            internal LassoPickerManipulator(WhimTexWindow owner) : base(owner, 2) { }
            protected override string MenuName => "lassoPicker";
            protected override string ItemName(int index) => index == 0 ? "lassoPolygonal" : "lassoFreehand";
            protected override string ItemTooltip(int index) => index == 0 ? "Polygonal Lasso" : "Freehand Lasso";
            protected override bool IsSelected(int index) => owner.lassoMode == (LassoMode)index;
            protected override VisualElement ItemIcon(int index)
            {
                var icon = new CanvasToolIcon(CanvasTool.PolygonSelect);
                icon.SetFreehand(index == 1);
                return icon;
            }
            protected override void Select(int index) => owner.SetLassoMode((LassoMode)index);
            protected override void Activate() => owner.SetCanvasTool(CanvasTool.PolygonSelect);
        }
    }
}
