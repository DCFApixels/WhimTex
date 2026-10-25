using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradient
    {
        public void ConvertCustomColorMode(WhimTexGradientColorMode mode)
        {
            RequireCustom();
            if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (mode != customColorMode) BuildCustom(mode);
        }
        private static double SampleDerivative(Func<double, double> sample, double time)
        {
            double a = Math.Max(0, time - .0001), b = Math.Min(1, time + .0001);
            return b > a ? (sample(b) - sample(a)) / (b - a) : 0;
        }
        private void BuildCustom(WhimTexGradientColorMode targetMode)
        {
            var source = Clone(); source.WrapMode = WhimTexGradientWrapMode.Clamp;
            bool fixedSource = source.Interpolation == WhimTexGradientInterpolation.Fixed;
            const int hueSamples = 1024;
            double[] hueReference = targetMode == WhimTexGradientColorMode.Hue ? new double[hueSamples + 1] : null;
            if (hueReference != null)
            {
                var chromatic = new bool[hueReference.Length];
                for (int i = 0; i < hueReference.Length; i++)
                {
                    Color c = source.EvaluateEncoded(i / (float)hueSamples);
                    var v = ToCustomWorking(new GradientVector(c.r, c.g, c.b), targetMode);
                    chromatic[i] = v.y > 1e-7 * Math.Max(1, Math.Abs(v.x)); hueReference[i] = v.z;
                }
                int first = Array.FindIndex(chromatic, x => x);
                if (first < 0) Array.Clear(hueReference, 0, hueReference.Length);
                else
                {
                    for (int i = 0; i < first; i++) hueReference[i] = hueReference[first];
                    for (int i = first + 1; i < hueReference.Length; i++)
                        hueReference[i] = chromatic[i] ? hueReference[i-1] + ShortHue(hueReference[i] - hueReference[i-1]) : hueReference[i-1];
                }
            }
            double Sample(int channel, double t)
            {
                Color c = source.EvaluateEncoded((float)t);
                if (channel == 3) return c.a;
                var work = ToCustomWorking(new GradientVector(c.r, c.g, c.b), targetMode);
                if (targetMode == WhimTexGradientColorMode.Hue)
                {
                    double position = Math.Max(0, Math.Min(hueSamples, t * hueSamples));
                    int index = Math.Min(hueSamples - 1, (int)position);
                    double hue = hueReference[index] + (hueReference[index+1] - hueReference[index]) * (position - index);
                    if (work.y <= 1e-7 * Math.Max(1, Math.Abs(work.x))) { work.y = 0; work.z = hue; }
                    else work.z += 360 * Math.Round((hue - work.z) / 360);
                }
                return work[channel];
            }
            var colorTimes = new List<float>(); foreach (var key in source.ColorKeys) colorTimes.Add(key.time);
            var alphaTimes = new List<float>(); foreach (var key in source.AlphaKeys) alphaTimes.Add(key.time);
            float rgbError = FitCustomTimes(colorTimes, 0, 3, targetMode, source, Sample, fixedSource);
            float alphaError = FitCustomTimes(alphaTimes, 3, 1, targetMode, source, Sample, fixedSource);
            var result = new List<WhimTexGradientCurveKey>();
            int id = 1;
            for (int channel = 0; channel < 4; channel++)
            {
                var times = channel == 3 ? alphaTimes : colorTimes;
                var track = FitCustomTrack(times, channel, Sample, fixedSource);
                for (int i = 0; i < track.Length; i++)
                {
                    var key = track[i]; key.id = id++; result.Add(key);
                }
            }
            SetCustomData(result.ToArray(), targetMode);
            customApproximationError = Math.Max(rgbError, alphaError);
        }
        private static double ShortHue(double difference)
        {
            difference %= 360; if (difference > 180) difference -= 360; else if (difference < -180) difference += 360;
            return difference;
        }
        private static WhimTexGradientCurveKey[] FitCustomTrack(List<float> times, int channel, Func<int, double, double> sample, bool fixedSource)
        {
            var keys = new WhimTexGradientCurveKey[times.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                double t = times[i], value = sample(channel, t);
                double incoming = 0, outgoing = 0;
                if (!fixedSource)
                {
                    if (i > 0)
                    {
                        double h = Math.Min(.0001, (t - times[i-1]) / 16);
                        float p = (float)(t - h); if (p < t) incoming = (value - sample(channel, p)) / (t - p);
                    }
                    if (i + 1 < keys.Length)
                    {
                        double h = Math.Min(.0001, (times[i+1] - t) / 16);
                        float p = (float)(t + h); if (p > t) outgoing = (sample(channel, p) - value) / (p - t);
                    }
                }
                keys[i] = new WhimTexGradientCurveKey { channel = channel, time = times[i], value = (float)value,
                    inTangent = (float)incoming, outTangent = (float)outgoing,
                    tangentMode = fixedSource ? WhimTexGradientTangentMode.Constant : WhimTexGradientTangentMode.Broken };
            }
            return keys;
        }
        private static readonly double[] CustomFitProbes =
            { .015625, .03125, .0625, .125, .25, .375, .5, .625, .75, .875, .9375, .96875, .984375 };
        private static float FitCustomTimes(List<float> times, int firstChannel, int channelCount,
            WhimTexGradientColorMode mode, WhimTexGradient source, Func<int, double, double> sample, bool fixedSource)
        {
            if (fixedSource || times.Count < 2) return 0;
            double worst = 0;
            while (true)
            {
                var tracks = new CustomTrack[channelCount];
                for (int c = 0; c < channelCount; c++) tracks[c] = MakeCustomTrack(FitCustomTrack(times, c + firstChannel, sample, false));
                worst = 0; float insertion = -1;
                for (int segment = 0; segment < times.Count - 1; segment++)
                foreach (double probe in CustomFitProbes)
                {
                    float t = (float)(times[segment] + (times[segment+1] - (double)times[segment]) * probe);
                    var expected = source.EvaluateEncoded(t);
                    double error;
                    if (firstChannel == 3) error = Math.Abs(GradientCurve.Clamp(tracks[0].Value(t), 0, 1) - expected.a);
                    else
                    {
                        var rgb = FromCustomWorking(new GradientVector(tracks[0].Value(t), tracks[1].Value(t), tracks[2].Value(t)), mode);
                        error = Math.Max(RelativeColorError(rgb.x, expected.r),
                            Math.Max(RelativeColorError(rgb.y, expected.g), RelativeColorError(rgb.z, expected.b)));
                    }
                    if (error > worst)
                    {
                        worst = error;
                        if (t - times[segment] >= CustomTimeSpacing && times[segment+1] - t >= CustomTimeSpacing) insertion = t;
                    }
                }
                if (worst <= .00025 || times.Count >= CustomColorPositionLimit || insertion < 0) break;
                int index = times.BinarySearch(insertion);
                if (index >= 0) break;
                times.Insert(~index, insertion);
            }
            return (float)worst;
        }
        private static double RelativeColorError(double actual, double expected)
            => Math.Abs(actual - expected) / Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected)));
    }
}
