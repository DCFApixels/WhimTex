using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum PencilShape { Circle, Square, Diamond }

    internal readonly struct PaintStrokeParameters
    {
        internal readonly Color Color;
        internal readonly float Size;
        internal readonly float Hardness;
        internal readonly float SpacingPixels;
        internal readonly bool Erase;
        internal readonly bool WrapCanvas;
        internal readonly bool PixelPerfect;
        internal readonly PencilShape Shape;
        internal readonly Texture SelectionMask;
        internal readonly BrushDynamics Dynamics;
        internal readonly bool StandardColorInputs;
        internal readonly float Pressure;

        internal static Vector2 SnapPencilCenter(Vector2 uv, int width, int height, float size)
        {
            float offset = ((Mathf.RoundToInt(size) & 1) == 0) ? 0f : 0.5f;
            return new Vector2((Mathf.Floor(uv.x * width + 0.5f - offset) + offset) / width,
                (Mathf.Floor(uv.y * height + 0.5f - offset) + offset) / height);
        }

        internal PaintStrokeParameters WithCanvasWrap() => new PaintStrokeParameters(this, true, SelectionMask);
        internal PaintStrokeParameters WithSelectionMask(Texture mask) => new PaintStrokeParameters(this, WrapCanvas, mask);

        internal bool OverlapsCanvas(Vector2 center, int width, int height)
        {
            if (WrapCanvas) return true;
            float radiusX = Size * .5f / Mathf.Max(1, width);
            float radiusY = Size * .5f / Mathf.Max(1, height);
            return center.x + radiusX > 0f && center.x - radiusX < 1f &&
                   center.y + radiusY > 0f && center.y - radiusY < 1f;
        }

        private PaintStrokeParameters(PaintStrokeParameters source, bool wrap, Texture mask)
        {
            Color = source.Color;
            Size = source.Size;
            Hardness = source.Hardness;
            SpacingPixels = source.SpacingPixels;
            Erase = source.Erase;
            WrapCanvas = wrap;
            SelectionMask = mask;
            PixelPerfect = source.PixelPerfect;
            Shape = source.Shape;
            Dynamics = source.Dynamics;
            StandardColorInputs = source.StandardColorInputs;
            Pressure = source.Pressure;
        }

        private PaintStrokeParameters(PaintStrokeParameters source, float pressure)
        {
            Color = source.Color;
            Size = source.Size;
            Hardness = source.Hardness;
            SpacingPixels = source.SpacingPixels;
            Erase = source.Erase;
            WrapCanvas = source.WrapCanvas;
            SelectionMask = source.SelectionMask;
            PixelPerfect = source.PixelPerfect;
            Shape = source.Shape;
            Dynamics = source.Dynamics;
            StandardColorInputs = source.StandardColorInputs;
            Pressure = Mathf.Clamp01(float.IsNaN(pressure) || float.IsInfinity(pressure) ? 1f : pressure);
        }

        internal PaintStrokeParameters WithPressure(float pressure) => new PaintStrokeParameters(this, pressure);

        internal PaintStrokeParameters(Color color, float size, float hardness, float spacing, bool erase,
            bool pixelPerfect = false, PencilShape shape = PencilShape.Circle, BrushDynamics dynamics = null, bool standardColorInputs = false)
        {
            Color = color;
            Size = pixelPerfect ? Mathf.Clamp(Mathf.Round(size), 1f, 4096f) : Mathf.Max(1f, size);
            Hardness = pixelPerfect ? 1f : Mathf.Clamp01(hardness);
            SpacingPixels = pixelPerfect ? Mathf.Max(1f, Mathf.Floor(Size * 0.16f)) : Mathf.Max(1f, Size * Mathf.Clamp(spacing,
                DrawingLayerBehaviour.MinimumBrushSpacing, DrawingLayerBehaviour.MaximumBrushSpacing));
            Erase = erase;
            WrapCanvas = false;
            SelectionMask = null;
            PixelPerfect = pixelPerfect;
            Shape = shape;
            Dynamics = pixelPerfect ? null : dynamics;
            StandardColorInputs = standardColorInputs;
            Pressure = 1f;
        }
    }
}
