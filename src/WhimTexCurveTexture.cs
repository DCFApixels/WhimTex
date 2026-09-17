using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexCurveTexture : IDisposable
    {
        private Texture2D texture;
        private AnimationCurve snapshot;
        private readonly float[] pixels = new float[1024];
        internal int BakeCount { get; private set; }

        internal static AnimationCurve Default() => AnimationCurve.Linear(0, 0, 1, 1);
        internal static AnimationCurve Copy(AnimationCurve curve) => curve == null ? Default() :
            new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };

        internal Texture2D GetTexture(AnimationCurve curve)
        {
            curve ??= Default();
            if (texture != null && snapshot != null && snapshot.Equals(curve)) return texture;
            var sampled = Copy(curve);
            sampled.preWrapMode = sampled.postWrapMode = WrapMode.ClampForever;
            for (int i = 0; i < 512; i++)
            {
                float value = sampled.Evaluate(i / 511f);
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
                pixels[i] = pixels[i + 512] = value;
            }
            if (texture == null)
                texture = new Texture2D(512, 2, TextureFormat.RFloat, false, true)
                {
                    name = "WhimTex Curve LUT", hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, false);
            snapshot = Copy(curve);
            BakeCount++;
            return texture;
        }

        internal static string Format(AnimationCurve curve)
        {
            var result = new StringBuilder("keys(");
            var keys = (curve ?? Default()).keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (i != 0) result.Append(", ");
                var k = keys[i];
                result.Append('(').Append(Number(k.time)).Append(", ").Append(Number(k.value))
                    .Append(", ").Append(Number(k.inTangent)).Append(", ").Append(Number(k.outTangent))
                    .Append(", ").Append(Number(k.inWeight)).Append(", ").Append(Number(k.outWeight))
                    .Append(", ").Append((int)k.weightedMode).Append(')');
            }
            return result.Append(')').ToString();
        }

        private static string Number(float v) => float.IsPositiveInfinity(v) ? "inf" :
            float.IsNegativeInfinity(v) ? "-inf" : v.ToString("R", CultureInfo.InvariantCulture);

        internal static AnimationCurve Parse(string text)
        {
            text = (text ?? "").Trim();
            if (text == "linear") return Default();
            if (text == "one") return AnimationCurve.Linear(0, 1, 1, 1);
            if (text == "easeInOut") return AnimationCurve.EaseInOut(0, 0, 1, 1);
            if (text == "easeIn") return new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 2));
            if (text == "easeOut") return new AnimationCurve(new Keyframe(0, 0, 2, 2), new Keyframe(1, 1, 0, 0));
            if (!text.StartsWith("keys(", StringComparison.Ordinal) || !text.EndsWith(")", StringComparison.Ordinal))
                throw new FormatException("Curve default must be linear, easeIn, easeOut, easeInOut, one or keys((time, value, inTangent, outTangent, inWeight, outWeight, weightedMode), ...).");
            string body = text.Substring(5, text.Length - 6);
            var keys = new List<Keyframe>();
            int end = 0;
            foreach (Match match in Regex.Matches(body, @"\(([^()]*)\)"))
            {
                string separator = body.Substring(end, match.Index - end).Trim();
                if (separator != (keys.Count == 0 ? "" : ",")) throw new FormatException("Invalid curve key separator.");
                string[] parts = match.Groups[1].Value.Split(',');
                if (parts.Length != 7) throw new FormatException("Curve keys require seven values.");
                var v = new float[7];
                for (int i = 0; i < 7; i++)
                {
                    string part = parts[i].Trim();
                    if ((i == 2 || i == 3) && (part == "inf" || part == "-inf"))
                        v[i] = part == "inf" ? float.PositiveInfinity : float.NegativeInfinity;
                    else if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) ||
                        float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                        throw new FormatException("Curve key values must be finite; tangents also accept inf and -inf.");
                }
                if (keys.Count > 0 && v[0] <= keys[keys.Count - 1].time)
                    throw new FormatException("Curve times must be strictly increasing.");
                if (v[4] < 0 || v[4] > 1 || v[5] < 0 || v[5] > 1 || v[6] < 0 || v[6] > 3 || v[6] != (int)v[6])
                    throw new FormatException("Curve weights must be within 0..1; weightedMode must be 0, 1, 2 or 3.");
                keys.Add(new Keyframe(v[0], v[1], v[2], v[3], v[4], v[5]) { weightedMode = (WeightedMode)(int)v[6] });
                if (keys.Count > 256) throw new FormatException("A curve may contain at most 256 keys.");
                end = match.Index + match.Length;
            }
            if (body.Substring(end).Trim().Length != 0) throw new FormatException("Invalid curve keys.");
            return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        public void Dispose()
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
            snapshot = null;
        }
    }
}
