using System;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>CPU-prepared noise coordinates stored as float values and their rounding residuals.</summary>
    public readonly struct WTNoiseDomain
    {
        public readonly Vector4 Scale;
        public readonly Vector4 ScaleLow;
        public readonly Vector4 Offset;
        public readonly Vector4 OffsetLow;
        public readonly Vector4 Period;
        public readonly Vector4 PeriodLow;

        /// <summary>
        /// Reduces each offset by its positive period before splitting. Zero or negative periods disable repetition.
        /// </summary>
        public WTNoiseDomain(double3 scale, double3 offset, double3 period)
        {
            RequireFinite(scale, nameof(scale));
            RequireFinite(offset, nameof(offset));
            RequireFinite(period, nameof(period));

            if (period.x > 0) offset.x %= period.x;
            if (period.y > 0) offset.y %= period.y;
            if (period.z > 0) offset.z %= period.z;

            SplitChecked(scale, nameof(scale), out Scale, out ScaleLow);
            SplitChecked(offset, nameof(offset), out Offset, out OffsetLow);
            SplitChecked(period, nameof(period), out Period, out PeriodLow);
            if ((period.x > 0 && Period.x == 0) || (period.y > 0 && Period.y == 0) || (period.z > 0 && Period.z == 0))
                throw new ArgumentOutOfRangeException(nameof(period), "Positive noise periods must remain positive in their float high parts.");
        }

        private static void RequireFinite(double3 value, string parameterName)
        {
            if (!double.IsFinite(value.x) || !double.IsFinite(value.y) || !double.IsFinite(value.z))
                throw new ArgumentOutOfRangeException(parameterName, "Noise domain components must be finite.");
        }

        private static void SplitChecked(double3 value, string parameterName, out Vector4 high, out Vector4 low)
        {
            NoisePrecision.Split(value, out high, out low);
            if (!float.IsFinite(high.x) || !float.IsFinite(high.y) || !float.IsFinite(high.z))
                throw new ArgumentOutOfRangeException(parameterName, "Noise domain components must have finite float high parts after periodic offset reduction.");
        }
    }

    /// <summary>Caches the six shader property IDs used to upload a noise domain.</summary>
    public readonly struct WTNoiseDomainBinding
    {
        private readonly int scaleId;
        private readonly int scaleLowId;
        private readonly int offsetId;
        private readonly int offsetLowId;
        private readonly int periodId;
        private readonly int periodLowId;
        private readonly bool initialized;

        public WTNoiseDomainBinding(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("A noise domain property prefix is required.", nameof(prefix));

            scaleId = Shader.PropertyToID(prefix + "_Scale");
            scaleLowId = Shader.PropertyToID(prefix + "_ScaleLow");
            offsetId = Shader.PropertyToID(prefix + "_Offset");
            offsetLowId = Shader.PropertyToID(prefix + "_OffsetLow");
            periodId = Shader.PropertyToID(prefix + "_Period");
            periodLowId = Shader.PropertyToID(prefix + "_PeriodLow");
            initialized = true;
        }

        /// <summary>Uploads all six vectors without creating managed objects on the successful path.</summary>
        public void Apply(Material material, in WTNoiseDomain domain)
        {
            if (!initialized)
                throw new InvalidOperationException("Construct the noise domain binding with a property prefix before applying it.");
            if (material == null) throw new ArgumentNullException(nameof(material));

            material.SetVector(scaleId, domain.Scale);
            material.SetVector(scaleLowId, domain.ScaleLow);
            material.SetVector(offsetId, domain.Offset);
            material.SetVector(offsetLowId, domain.OffsetLow);
            material.SetVector(periodId, domain.Period);
            material.SetVector(periodLowId, domain.PeriodLow);
        }
    }
}
