using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal readonly struct FigureFill
    {
        private readonly double originX, originY, axisX, axisY, lengthSquared, repetitions;
        internal FigureFill(Vector2 start, Vector2 end, Vector2 size, float repetitions = 1f)
        {
            this.repetitions = repetitions;
            originX = (double)start.x * size.x; originY = (double)start.y * size.y;
            axisX = ((double)end.x - start.x) * size.x; axisY = ((double)end.y - start.y) * size.y;
            lengthSquared = axisX * axisX + axisY * axisY;
        }
        internal static bool IsGradient(VectorFillMode mode) => IsPaletteGradient(mode) || mode == VectorFillMode.Freeform;
        internal static bool IsPaletteGradient(VectorFillMode mode) => mode == VectorFillMode.LinearGradient || mode == VectorFillMode.RadialGradient ||
            mode == VectorFillMode.AngularGradient || mode == VectorFillMode.SquareGradient;
        internal static void ValidateFreeform(VectorFillMode mode, FreeformGradient data)
        {
            if (mode == VectorFillMode.Freeform && data == null)
                throw new ArgumentException("Freeform gradient data is missing.");
            data?.Validate();
        }
        internal double Coordinate(VectorFillMode mode, double x, double y)
        {
            if (lengthSquared <= 1e-20) return 0;
            double dx = x - originX, dy = y - originY;
            double along = (dx * axisX + dy * axisY) / lengthSquared;
            double across = (dy * axisX - dx * axisY) / lengthSquared;
            switch (mode)
            {
                case VectorFillMode.LinearGradient: return along;
                case VectorFillMode.RadialGradient: return Math.Sqrt((dx * dx + dy * dy) / lengthSquared);
                case VectorFillMode.SquareGradient: return Math.Max(Math.Abs(along), Math.Abs(across));
                case VectorFillMode.AngularGradient:
                    double turn = along == 0 && across == 0 ? 0 : Math.Atan2(across, along) / (2 * Math.PI);
                    return (turn - Math.Floor(turn)) * repetitions;
                default: return 0;
            }
        }
    }
}
