using UnityEditor;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private enum CanvasCursorIcon
        {
            None, Fill, Gradient,
            ShapeRectangle, ShapeEllipse, ShapePolygon, ShapeStar, ShapeLine, ShapeArc, ShapeSector,
            SelectionRectangle, SelectionEllipse, SelectionUv, LassoPolygon, LassoFreehand,
            Comment, Text, VectorPen, VectorSelect, VectorNodes, Crop, CanvasScale
        }

        private CanvasCursorIcon GetCanvasToolCursorIcon(MouseCursor nativeCursor)
        {
            switch (canvasTool)
            {
                case CanvasTool.None: return CanvasCursorIcon.None;
                case CanvasTool.Fill: return CanvasCursorIcon.Fill;
                case CanvasTool.Gradient: return CanvasCursorIcon.Gradient;
                case CanvasTool.Shape:
                    switch (shapeToolSettings?.kind ?? ShapeLayerBehaviour.ShapeKind.Rectangle)
                    {
                        case ShapeLayerBehaviour.ShapeKind.Ellipse: return CanvasCursorIcon.ShapeEllipse;
                        case ShapeLayerBehaviour.ShapeKind.Polygon: return CanvasCursorIcon.ShapePolygon;
                        case ShapeLayerBehaviour.ShapeKind.Star: return CanvasCursorIcon.ShapeStar;
                        case ShapeLayerBehaviour.ShapeKind.Line: return CanvasCursorIcon.ShapeLine;
                        case ShapeLayerBehaviour.ShapeKind.Arc: return CanvasCursorIcon.ShapeArc;
                        case ShapeLayerBehaviour.ShapeKind.Sector: return CanvasCursorIcon.ShapeSector;
                        default: return CanvasCursorIcon.ShapeRectangle;
                    }
                case CanvasTool.RectangleSelect:
                    return marqueeShape == MarqueeShape.Ellipse ? CanvasCursorIcon.SelectionEllipse : CanvasCursorIcon.SelectionRectangle;
                case CanvasTool.PolygonSelect:
                    return lassoMode == LassoMode.Freehand ? CanvasCursorIcon.LassoFreehand : CanvasCursorIcon.LassoPolygon;
                case CanvasTool.UvIslandSelect: return CanvasCursorIcon.SelectionUv;
                case CanvasTool.Comment: return CanvasCursorIcon.Comment;
                case CanvasTool.Text: return textEditingLayer == null ? CanvasCursorIcon.Text : CanvasCursorIcon.None;
                case CanvasTool.VectorPen: return CanvasCursorIcon.VectorPen;
                case CanvasTool.VectorSelect: return CanvasCursorIcon.VectorSelect;
                case CanvasTool.VectorNodes: return CanvasCursorIcon.VectorNodes;
                case CanvasTool.Crop:
                    return nativeCursor != MouseCursor.Arrow ? CanvasCursorIcon.None :
                        canvasCropMode == CanvasCropMode.Scale ? CanvasCursorIcon.CanvasScale : CanvasCursorIcon.Crop;
                default: return CanvasCursorIcon.None;
            }
        }
    }
}
