using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum StrokeSmoothingMode { None, Smooth, Stabilizer }

    [Serializable]
    internal sealed class StrokeSmoothingSettings
    {
        public StrokeSmoothingMode mode;
        public float distance = 12f;
        public bool smoothPressure;
        public bool finishStroke = true;

        internal void Normalize()
        {
            if (!Enum.IsDefined(typeof(StrokeSmoothingMode), mode)) mode = StrokeSmoothingMode.None;
            distance = Mathf.Clamp(float.IsFinite(distance) ? distance : 12f, 1f, 256f);
        }
    }

    // Coordinates and filter distance are canvas pixels, independent of view zoom/rotation.
    internal struct StrokeSmoother
    {
        private Vector2 previousInput;
        internal Vector2 Position { get; private set; }
        internal float Pressure { get; private set; }

        internal void Begin(Vector2 position, float pressure)
        {
            previousInput = Position = position;
            Pressure = Mathf.Clamp01(pressure);
        }

        internal void Move(Vector2 input, float pressure, StrokeSmoothingSettings settings, bool exact = false)
        {
            if (!float.IsFinite(input.x) || !float.IsFinite(input.y)) return;
            Vector2 delta = input - previousInput;
            float length = delta.magnitude;
            float distance = Mathf.Clamp(settings.distance, 1f, 256f);
            float decay = Mathf.Exp(-length / distance);
            if (exact || settings.mode == StrokeSmoothingMode.None) Position = input;
            else if (settings.mode == StrokeSmoothingMode.Smooth && length > 0f)
            {
                // Exact integration of a first-order filter over the straight input segment.
                // Subdividing the same segment does not change its filtered endpoint.
                Vector2 lag = delta * (distance / length);
                Position = input - lag + (Position - previousInput + lag) * decay;
            }
            else if (settings.mode == StrokeSmoothingMode.Stabilizer)
            {
                Vector2 rope = input - Position;
                float reach = rope.magnitude;
                if (reach > distance) Position = input - rope * (distance / reach);
            }
            pressure = Mathf.Clamp01(pressure);
            Pressure = settings.smoothPressure && !exact
                ? Mathf.Lerp(pressure, Pressure, decay) : pressure;
            previousInput = input;
        }

        internal void Finish(bool complete)
        {
            if (complete) Position = previousInput;
        }
    }
}
