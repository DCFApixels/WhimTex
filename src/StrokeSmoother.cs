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
        private float rawPressure;
        private float[] pressureHistory, pressureDistances;
        private int pressureHead, pressureCount;

        internal void Begin(Vector2 position, float pressure)
        {
            previousInput = Position = position;
            rawPressure = Pressure = Mathf.Clamp01(pressure);
            pressureHead = pressureCount = 0;
            PushPressure(Pressure, 0f);
        }

        internal void Move(Vector2 input, float pressure, StrokeSmoothingSettings settings, bool exact = false)
            => MoveScaled(input, pressure, settings, 1f, exact);

        internal void MoveScaled(Vector2 input, float pressure, StrokeSmoothingSettings settings, float pixelScale, bool exact = false)
        {
            if (!float.IsFinite(input.x) || !float.IsFinite(input.y)) return;
            Vector2 delta = input - previousInput;
            float length = delta.magnitude;
            float distance = Mathf.Max(.0001f, Mathf.Clamp(settings.distance, 1f, 256f) * pixelScale);
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
            rawPressure = pressure;
            PushPressure(pressure, length);
            Pressure = settings.smoothPressure && !exact ? FilterPressure(settings, distance) : pressure;
            previousInput = input;
        }

        internal void Finish(bool complete)
        {
            if (complete) { Position = previousInput; Pressure = rawPressure; }
        }

        private void PushPressure(float pressure, float distance)
        {
            pressureHistory ??= new float[256];
            pressureDistances ??= new float[256];
            pressureHistory[pressureHead] = pressure;
            pressureDistances[pressureHead] = distance;
            pressureHead = (pressureHead + 1) & 255;
            pressureCount = Mathf.Min(256, pressureCount + 1);
        }

        private float FilterPressure(StrokeSmoothingSettings settings, float distance)
        {
            if (settings.mode == StrokeSmoothingMode.Stabilizer)
            {
                int samples = Mathf.Min(pressureCount, Mathf.Max(2, Mathf.CeilToInt(distance)));
                float sum = 0f;
                for (int i = 0; i < samples; i++) sum += pressureHistory[(pressureHead - 1 - i) & 255];
                return sum / samples;
            }
            if (pressureCount <= 3) return rawPressure;
            float sigma = Mathf.Max(.0001f, distance) / 3f;
            float travelled = 0f, sumWeighted = 0f, weights = 0f, newestWeight = 0f;
            for (int i = 0; i < pressureCount; i++)
            {
                int index = (pressureHead - 1 - i) & 255;
                travelled += pressureDistances[index];
                float weight = Mathf.Exp(-travelled * travelled / (2f * sigma * sigma));
                if (i == 0) newestWeight = weight;
                else if (weight <= 0f || weight * 100f < newestWeight) break;
                sumWeighted += pressureHistory[index] * weight;
                weights += weight;
            }
            return weights > 0f ? Mathf.Clamp01(sumWeighted / weights) : rawPressure;
        }
    }
}
