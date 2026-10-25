using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexGradientTangentMode { Auto, Smooth, Broken, Linear, Constant }

    [Serializable]
    public struct WhimTexGradientCurveKey : IEquatable<WhimTexGradientCurveKey>
    {
        public int id, channel;
        public float time, value, inTangent, outTangent;
        public WhimTexGradientTangentMode tangentMode;
        public bool Equals(WhimTexGradientCurveKey other) => id == other.id && channel == other.channel &&
            time == other.time && value == other.value &&
            inTangent == other.inTangent && outTangent == other.outTangent && tangentMode == other.tangentMode;
        public override int GetHashCode()
        {
            unchecked { return ((((id * 397 ^ channel) * 397 ^ time.GetHashCode()) *
                397 ^ value.GetHashCode()) * 397 ^ inTangent.GetHashCode()) * 397 ^ outTangent.GetHashCode() ^ (int)tangentMode; }
        }
    }

    public sealed partial class WhimTexGradient
    {
        [SerializeField] private WhimTexGradientCurveKey[] customKeys;
        [SerializeField] private WhimTexGradientColorMode customColorMode = WhimTexGradientColorMode.Perceptual;
        [SerializeField] private float customApproximationError;
        [NonSerialized] private CustomPrepared customPrepared;
        [NonSerialized] private CustomMarkerCache customMarkers;
        public bool HasCustomCurves => customKeys != null && customKeys.Length != 0;
        public WhimTexGradientColorMode CustomColorMode => customColorMode;
        public float CustomApproximationError => customApproximationError;
        public WhimTexGradientCurveKey[] CustomKeys => customKeys == null ? Array.Empty<WhimTexGradientCurveKey>() :
            (WhimTexGradientCurveKey[])customKeys.Clone();
        public const int CustomColorPositionLimit = 64;
        public const float CustomMarkerTimeTolerance = .00001f;
        private const float CustomTimeSpacing = .000001f;

        internal bool HasOpaqueAlpha
        {
            get
            {
                if (interpolation != WhimTexGradientInterpolation.Custom)
                {
                    foreach (var key in alphas) if (key.alpha != 1f) return false;
                    return true;
                }
                PrepareCustom();
                var track = customPrepared.tracks[3];
                foreach (var key in track.keys) if (key.value != 1f) return false;
                for (int i = 0; i + 1 < track.keys.Length; i++)
                    if (track.keys[i].tangentMode != WhimTexGradientTangentMode.Constant &&
                        (track.outgoing[i] < 0 || track.incoming[i + 1] > 0)) return false;
                // Between unit endpoints these signs guarantee a cubic >= 1; output alpha clamps to one.
                return true;
            }
        }

        private sealed class CustomPrepared
        {
            internal CustomTrack[] tracks;
            internal double[] times, minimum, maximum;
            internal GradientColorKey[] colors;
            internal GradientAlphaKey[] alphas;
        }
        private sealed class CustomMarkerCache
        {
            internal readonly Dictionary<int, int[]> groups = new Dictionary<int, int[]>();
            internal WhimTexGradientCurveKey[] colors, alphas, all;
        }
        private sealed class CustomTrack
        {
            internal WhimTexGradientCurveKey[] keys;
            internal double[] incoming, outgoing;
            internal double Value(double time)
            {
                int last = keys.Length - 1;
                if (time <= keys[0].time) return keys[0].value;
                if (time >= keys[last].time) return keys[last].value;
                int low = 0, high = last;
                while (high - low > 1) { int middle = (low + high) / 2; if (time < keys[middle].time) high = middle; else low = middle; }
                var a = keys[low]; var b = keys[high];
                if (time == a.time) return a.value;
                // Same right-key convention as Fixed, including exact values at the knots.
                if (a.tangentMode == WhimTexGradientTangentMode.Constant) return b.value;
                double span = b.time - a.time, u = (time - a.time) / span, u2 = u * u, u3 = u2 * u;
                return (2*u3-3*u2+1)*a.value + (u3-2*u2+u)*span*outgoing[low] +
                    (-2*u3+3*u2)*b.value + (u3-u2)*span*incoming[high];
            }
        }

        public string CustomChannelName(int channel)
        {
            if (channel == 3) return "A";
            if (channel < 0 || channel > 3) throw new ArgumentOutOfRangeException(nameof(channel));
            if (customColorMode == WhimTexGradientColorMode.Perceptual) return channel == 0 ? "L" : channel == 1 ? "a" : "b";
            if (customColorMode == WhimTexGradientColorMode.Hue) return channel == 0 ? "L" : channel == 1 ? "C" : "H";
            return channel == 0 ? "R" : channel == 1 ? "G" : "B";
        }
        public bool TryGetCustomKey(int id, out WhimTexGradientCurveKey key)
        {
            if (customKeys != null) foreach (var item in customKeys) if (item.id == id) { key = item; return true; }
            key = default; return false;
        }
        public void SetCustomData(WhimTexGradientCurveKey[] keys, WhimTexGradientColorMode mode)
        {
            if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (keys == null || keys.Length == 0)
            {
                if (interpolation == WhimTexGradientInterpolation.Custom) throw new ArgumentException("Active Custom curves cannot be empty.");
                customKeys = null; customColorMode = mode; customApproximationError = 0; Invalidate(); return;
            }
            var copy = (WhimTexGradientCurveKey[])keys.Clone();
            Array.Sort(copy, CompareCustomKeys);
            ValidateCustom(copy);
            var candidate = (WhimTexGradient)MemberwiseClone();
            candidate.customKeys = copy; candidate.customColorMode = mode; candidate.customPrepared = null;
            candidate.PrepareCustom();
            customKeys = copy; customColorMode = mode; customApproximationError = 0; Invalidate();
            customPrepared = candidate.customPrepared;
        }
        private static int CompareCustomKeys(WhimTexGradientCurveKey a, WhimTexGradientCurveKey b)
        {
            int order = a.channel.CompareTo(b.channel);
            if (order == 0) order = a.time.CompareTo(b.time);
            return order != 0 ? order : a.id.CompareTo(b.id);
        }
        private static void ValidateCustom(WhimTexGradientCurveKey[] keys)
        {
            if (keys == null || keys.Length < 4 || keys.Length > 256) throw new ArgumentException("Custom requires four nonempty channels, at most 64 keys each.");
            var ids = new HashSet<int>(); var positions = new HashSet<float>();
            int[] count = new int[4]; float[] previous = { -1, -1, -1, -1 };
            foreach (var key in keys)
            {
                if (key.id <= 0 || !ids.Add(key.id) || key.channel < 0 || key.channel > 3)
                    throw new ArgumentException("Invalid Custom key identity or channel.");
                CheckTime(key.time, previous[key.channel]); previous[key.channel] = key.time;
                if (++count[key.channel] > 64 || !Finite(key.value) || !Finite(key.inTangent) || !Finite(key.outTangent) ||
                    !Enum.IsDefined(typeof(WhimTexGradientTangentMode), key.tangentMode) ||
                    (key.channel == 3 && (key.value < 0 || key.value > 1)))
                    throw new ArgumentException("Custom values/tangents must be finite; alpha is 0..1, each channel has at most 64 keys.");
                if (key.channel < 3) positions.Add(key.time);
            }
            foreach (int n in count) if (n == 0) throw new ArgumentException("Each Custom channel requires at least one key.");
            if (positions.Count > CustomColorPositionLimit) throw new ArgumentException("Custom supports at most 64 distinct color-key positions.");
        }
        private static int NextCustomId(WhimTexGradientCurveKey[] keys)
        {
            int largest = 0; foreach (var key in keys) largest = Math.Max(largest, key.id);
            return checked(largest + 1);
        }
        private void RequireCustom()
        {
            if (interpolation != WhimTexGradientInterpolation.Custom) throw new InvalidOperationException("Select Custom interpolation first.");
        }
        private static int CompareCustomMarkerKeys(WhimTexGradientCurveKey a, WhimTexGradientCurveKey b)
        {
            int order = a.time.CompareTo(b.time);
            return order != 0 ? order : a.id.CompareTo(b.id);
        }
        private void PrepareCustomMarkers()
        {
            if (customMarkers != null) return;
            var keys = CustomKeys;
            Array.Sort(keys, CompareCustomMarkerKeys);
            var state = new CustomMarkerCache();
            var colors = new List<WhimTexGradientCurveKey>();
            var alphas = new List<WhimTexGradientCurveKey>();
            var all = new List<WhimTexGradientCurveKey>();
            for (int start = 0; start < keys.Length;)
            {
                int end = CustomMarkerEnd(keys, start);
                var ids = new int[end - start];
                bool hasColor = false, hasAlpha = false;
                all.Add(keys[start]);
                for (int i = start; i < end; i++)
                {
                    var key = keys[i];
                    ids[i - start] = key.id;
                    state.groups.Add(key.id, ids);
                    if (key.channel == 3)
                    {
                        if (!hasAlpha) { alphas.Add(key); hasAlpha = true; }
                    }
                    else if (!hasColor) { colors.Add(key); hasColor = true; }
                }
                start = end;
            }
            state.colors = colors.ToArray(); state.alphas = alphas.ToArray(); state.all = all.ToArray();
            customMarkers = state;
        }
        private static int CustomMarkerEnd(WhimTexGradientCurveKey[] keys, int start)
        {
            int end = start + 1;
            // Compare with the earliest anchor, never with the previous member.
            while (end < keys.Length && (double)keys[end].time - keys[start].time <= CustomMarkerTimeTolerance) end++;
            return end;
        }
        public int[] GetCustomMarkerGroup(int id)
        {
            PrepareCustomMarkers();
            return customMarkers.groups.TryGetValue(id, out var ids) ? (int[])ids.Clone() : Array.Empty<int>();
        }
        public WhimTexGradientCurveKey[] GetCustomMarkers(bool? alpha = false)
        {
            PrepareCustomMarkers();
            var markers = !alpha.HasValue ? customMarkers.all : alpha.Value ? customMarkers.alphas : customMarkers.colors;
            return (WhimTexGradientCurveKey[])markers.Clone();
        }
        public int AddCustomKey(int channel, float time)
        {
            RequireCustom();
            if (channel < 0 || channel > 3 || !Finite(time)) throw new ArgumentOutOfRangeException();
            time = Mathf.Clamp01(time);
            foreach (var key in customKeys) if (key.channel == channel && Mathf.Abs(key.time - time) < CustomTimeSpacing) return key.id;
            PrepareCustom();
            var list = new List<WhimTexGradientCurveKey>(customKeys);
            var track = customPrepared.tracks[channel];
            float value = (float)track.Value(time);
            if (channel == 3) value = Mathf.Clamp01(value);
            float derivative = (float)SampleDerivative(track.Value, time);
            int id = NextCustomId(customKeys);
            list.Add(new WhimTexGradientCurveKey { id = id, channel = channel, time = time, value = value,
                inTangent = derivative, outTangent = derivative, tangentMode = WhimTexGradientTangentMode.Smooth });
            SetCustomData(list.ToArray(), customColorMode);
            return id;
        }
        public int AddCustomColorKeys(float time)
        {
            RequireCustom();
            if (!Finite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            time = Mathf.Clamp01(time);
            foreach (var key in customKeys)
                if (key.channel < 3 && Mathf.Abs(key.time - time) < CustomTimeSpacing) { time = key.time; break; }
            var next = Clone();
            int[] ids = { next.AddCustomKey(0, time), next.AddCustomKey(1, time), next.AddCustomKey(2, time) };
            var keys = next.CustomKeys;
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].id == ids[0] || keys[i].id == ids[1] || keys[i].id == ids[2]) keys[i].time = time;
            SetCustomData(keys, next.customColorMode);
            return ids[0];
        }
        public void MoveCustomKey(int id, float time)
        {
            RequireCustom();
            if (!Finite(time) || !TryGetCustomKey(id, out var key)) throw new ArgumentException("Invalid Custom key or time.");
            MoveCustomKeysCore(new HashSet<int> { id }, (double)Mathf.Clamp01(time) - key.time);
        }
        public void MoveCustomKeys(int[] ids, float deltaTime)
        {
            RequireCustom();
            if (!Finite(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            var moving = CustomSelection(ids);
            MoveCustomKeysCore(moving, deltaTime);
        }
        private void MoveCustomKeysCore(HashSet<int> moving, double deltaTime)
        {
            if (moving.Count == 0 || deltaTime == 0) return;
            double lower = double.NegativeInfinity, upper = double.PositiveInfinity;
            int neighbour = -1;
            for (int i = 0; i < customKeys.Length; i++)
            {
                var key = customKeys[i];
                if (i == 0 || customKeys[i - 1].channel != key.channel) neighbour = -1;
                if (!moving.Contains(key.id)) { neighbour = i; continue; }
                lower = Math.Max(lower, -(double)key.time);
                upper = Math.Min(upper, 1.0 - key.time);
                if (neighbour >= 0)
                    lower = Math.Max(lower, Math.Min(0, (double)customKeys[neighbour].time + CustomTimeSpacing - key.time));
            }
            neighbour = -1;
            for (int i = customKeys.Length - 1; i >= 0; i--)
            {
                var key = customKeys[i];
                if (i == customKeys.Length - 1 || customKeys[i + 1].channel != key.channel) neighbour = -1;
                if (!moving.Contains(key.id)) { neighbour = i; continue; }
                if (neighbour >= 0)
                    upper = Math.Min(upper, Math.Max(0, (double)customKeys[neighbour].time - CustomTimeSpacing - key.time));
            }
            double delta = Math.Max(lower, Math.Min(upper, deltaTime));
            if (delta == 0) return;
            var keys = CustomKeys;
            if (!TryMoveCustomTimes(keys, moving, delta))
            {
                // One common delta also needs to respect spacing after float storage rounding.
                // Reduce the whole movement rather than nudging individual keys out of the set.
                double accepted = 0, rejected = delta;
                for (int i = 0; i < 48; i++)
                {
                    double candidate = accepted + (rejected - accepted) * .5;
                    if (TryMoveCustomTimes(keys, moving, candidate)) accepted = candidate;
                    else rejected = candidate;
                }
                TryMoveCustomTimes(keys, moving, accepted);
            }
            bool changed = false;
            for (int i = 0; i < keys.Length; i++) if (keys[i].time != customKeys[i].time) { changed = true; break; }
            if (!changed) return;
            SetCustomData(keys, customColorMode);
        }
        private HashSet<int> CustomSelection(int[] ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var selected = new HashSet<int>(ids);
            foreach (int id in selected) if (!TryGetCustomKey(id, out _)) throw new ArgumentException("Unknown Custom key.", nameof(ids));
            return selected;
        }
        private bool TryMoveCustomTimes(WhimTexGradientCurveKey[] keys, HashSet<int> moving, double delta)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time = moving.Contains(keys[i].id) ? (float)(customKeys[i].time + delta) : customKeys[i].time;
                if (keys[i].time < 0 || keys[i].time > 1 ||
                    (i > 0 && keys[i - 1].channel == keys[i].channel && keys[i].time - keys[i - 1].time < CustomTimeSpacing))
                    return false;
            }
            return true;
        }
        public void UpdateCustomKey(WhimTexGradientCurveKey key)
        {
            RequireCustom();
            if (!TryGetCustomKey(key.id, out var original) || key.channel != original.channel)
                throw new ArgumentException("A Custom key must retain its identity and channel.");
            var next = Clone();
            if (key.time != original.time) next.MoveCustomKey(key.id, key.time);
            var keys = next.CustomKeys;
            for (int i = 0; i < keys.Length; i++) if (keys[i].id == key.id)
            {
                key.time = keys[i].time;
                if (key.tangentMode == WhimTexGradientTangentMode.Smooth) key.inTangent = key.outTangent;
                keys[i] = key; break;
            }
            SetCustomData(keys, customColorMode);
        }
        public void RemoveCustomKey(int id)
        {
            RequireCustom();
            if (!TryGetCustomKey(id, out _)) return;
            RemoveCustomKeys(new[] { id });
        }
        public void RemoveCustomKeys(int[] ids)
        {
            RequireCustom();
            var selected = CustomSelection(ids);
            if (selected.Count == 0) return;
            var list = new List<WhimTexGradientCurveKey>(customKeys.Length - selected.Count);
            foreach (var key in customKeys) if (!selected.Contains(key.id)) list.Add(key);
            SetCustomData(list.ToArray(), customColorMode);
        }

        private void PrepareCustom()
        {
            if (customPrepared != null) return;
            ValidateCustom(customKeys);
            if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), customColorMode)) throw new ArgumentException("Invalid Custom color mode.");
            var state = new CustomPrepared { tracks = new CustomTrack[4] };
            for (int channel = 0; channel < 4; channel++)
            {
                var list = new List<WhimTexGradientCurveKey>();
                foreach (var key in customKeys) if (key.channel == channel) list.Add(key);
                state.tracks[channel] = MakeCustomTrack(list.ToArray());
            }
            var times = new SortedSet<double>();
            foreach (var key in customKeys) if (key.channel < 3) times.Add(key.time);
            state.times = new double[times.Count]; times.CopyTo(state.times);
            state.minimum = new double[Math.Max(0, times.Count - 1)]; state.maximum = new double[state.minimum.Length];
            var anchors = new GradientVector[times.Count];
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i] = CustomEncoded(state, state.times[i]);
                var linear = GradientColorMath.Decode(anchors[i]);
                for (int channel = 0; channel < 3; channel++)
                    if (double.IsNaN(anchors[i][channel]) || Math.Abs(anchors[i][channel]) > float.MaxValue ||
                        double.IsNaN(linear[channel]) || Math.Abs(linear[channel]) > float.MaxValue)
                        throw new ArgumentException("Custom curve keys exceed the finite RGB calculation range.");
            }
            for (int i = 0; i < state.minimum.Length; i++)
            {
                double minimum = 0, maximum = 1;
                for (int c = 0; c < 3; c++) { minimum = Math.Min(minimum, Math.Min(anchors[i][c], anchors[i+1][c])); maximum = Math.Max(maximum, Math.Max(anchors[i][c], anchors[i+1][c])); }
                state.minimum[i] = minimum; state.maximum[i] = maximum;
            }
            customPrepared = state;
        }
        private static CustomTrack MakeCustomTrack(WhimTexGradientCurveKey[] keys)
        {
            var result = new CustomTrack { keys = keys, incoming = new double[keys.Length], outgoing = new double[keys.Length] };
            for (int i = 0; i < keys.Length; i++)
            {
                double left = i > 0 ? (keys[i].value - (double)keys[i-1].value) / (keys[i].time - (double)keys[i-1].time) : 0;
                double right = i + 1 < keys.Length ? (keys[i+1].value - (double)keys[i].value) / (keys[i+1].time - (double)keys[i].time) : 0;
                double incoming = keys[i].inTangent, outgoing = keys[i].outTangent;
                switch (keys[i].tangentMode)
                {
                    case WhimTexGradientTangentMode.Auto:
                        incoming = outgoing = i == 0 ? right : i == keys.Length - 1 ? left :
                            left * right <= 0 ? 0 : 2 * left * right / (left + right);
                        break;
                    case WhimTexGradientTangentMode.Linear: incoming = left; outgoing = right; break;
                    case WhimTexGradientTangentMode.Smooth: incoming = outgoing; break;
                    case WhimTexGradientTangentMode.Constant: incoming = outgoing = 0; break;
                }
                result.incoming[i] = incoming; result.outgoing[i] = outgoing;
            }
            return result;
        }
        private GradientVector CustomEncoded(CustomPrepared state, double time) => FromCustomWorking(
            new GradientVector(state.tracks[0].Value(time), state.tracks[1].Value(time), state.tracks[2].Value(time)), customColorMode);
        private static GradientVector FromCustomWorking(GradientVector value, WhimTexGradientColorMode mode)
        {
            switch (mode)
            {
                case WhimTexGradientColorMode.Gamma: return value;
                case WhimTexGradientColorMode.Linear: return GradientColorMath.Encode(value);
                case WhimTexGradientColorMode.Perceptual: return GradientColorMath.FromLab(value);
                default: return GradientColorMath.FromLab(GradientColorMath.HueLab(value));
            }
        }
        private static GradientVector ToCustomWorking(GradientVector rgb, WhimTexGradientColorMode mode)
        {
            switch (mode)
            {
                case WhimTexGradientColorMode.Gamma: return rgb;
                case WhimTexGradientColorMode.Linear: return GradientColorMath.Decode(rgb);
                case WhimTexGradientColorMode.Perceptual: return GradientColorMath.ToLab(rgb);
                default: return GradientColorMath.ToLch(GradientColorMath.ToLab(rgb));
            }
        }
        private Color EvaluateCustom(double time, bool encoded)
        {
            PrepareCustom();
            var rgb = CustomEncoded(customPrepared, time);
            if (customPrepared.times.Length > 1)
            {
                int i = GradientCurve.Segment(customPrepared.times, time);
                double min = customPrepared.minimum[i], max = customPrepared.maximum[i];
                rgb = new GradientVector(GradientCurve.Clamp(rgb.x, min, max), GradientCurve.Clamp(rgb.y, min, max), GradientCurve.Clamp(rgb.z, min, max));
            }
            if (!encoded && colorSpace == ColorSpace.Linear) rgb = GradientColorMath.Decode(rgb);
            return new Color((float)rgb.x, (float)rgb.y, (float)rgb.z,
                (float)GradientCurve.Clamp(customPrepared.tracks[3].Value(time), 0, 1));
        }
        public float EvaluateCustomChannel(int channel, float time, bool final = false)
        {
            if (channel < 0 || channel > 3 || !Finite(time)) throw new ArgumentOutOfRangeException();
            PrepareCustom();
            if (!final) return (float)customPrepared.tracks[channel].Value(Mathf.Clamp01(time));
            var color = EvaluateCustom(Mathf.Clamp01(time), true);
            if (channel == 3) return color.a;
            var value = ToCustomWorking(new GradientVector(color.r, color.g, color.b), customColorMode);
            if (channel == 2 && customColorMode == WhimTexGradientColorMode.Hue)
                value.z += 360 * Math.Round((customPrepared.tracks[2].Value(time) - value.z) / 360);
            return (float)value[channel];
        }
        public float CustomKeyTangent(int id, bool incoming)
        {
            PrepareCustom();
            foreach (var track in customPrepared.tracks) for (int i = 0; i < track.keys.Length; i++)
                if (track.keys[i].id == id) return (float)(incoming ? track.incoming[i] : track.outgoing[i]);
            return 0;
        }
        private GradientColorKey[] CustomColorKeys()
        {
            PrepareCustom();
            if (customPrepared.colors == null)
            {
                customPrepared.colors = new GradientColorKey[customPrepared.times.Length];
                for (int i = 0; i < customPrepared.colors.Length; i++)
                    customPrepared.colors[i] = new GradientColorKey(EvaluateCustom(customPrepared.times[i], false), (float)customPrepared.times[i]);
            }
            return (GradientColorKey[])customPrepared.colors.Clone();
        }
        private GradientAlphaKey[] CustomAlphaKeys()
        {
            PrepareCustom();
            if (customPrepared.alphas == null)
            {
                var keys = customPrepared.tracks[3].keys; customPrepared.alphas = new GradientAlphaKey[keys.Length];
                for (int i = 0; i < keys.Length; i++) customPrepared.alphas[i] = new GradientAlphaKey(keys[i].value, keys[i].time);
            }
            return (GradientAlphaKey[])customPrepared.alphas.Clone();
        }
        private void ReverseCustom()
        {
            var keys = CustomKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time = 1 - keys[i].time;
                float old = keys[i].inTangent; keys[i].inTangent = -keys[i].outTangent; keys[i].outTangent = -old;
            }
            SetCustomData(keys, customColorMode);
        }
        private bool EqualsCustom(WhimTexGradient other)
        {
            if (HasCustomCurves != other.HasCustomCurves) return false;
            if (!HasCustomCurves) return true;
            if (customColorMode != other.customColorMode || customKeys.Length != other.customKeys.Length) return false;
            for (int i = 0; i < customKeys.Length; i++) if (!customKeys[i].Equals(other.customKeys[i])) return false;
            return true;
        }
        private int CustomHash()
        {
            if (!HasCustomCurves) return 0;
            unchecked { int hash = (int)customColorMode; foreach (var key in customKeys) hash = hash * 397 ^ key.GetHashCode(); return hash; }
        }
        private ulong CustomFingerprint(ulong hash)
        {
            hash = RenderFingerprint.Mix(hash, HasCustomCurves ? 1UL : 0UL);
            if (!HasCustomCurves) return hash;
            hash = RenderFingerprint.Mix(hash, (ulong)customColorMode);
            foreach (var key in customKeys)
            {
                hash = RenderFingerprint.Mix(hash, (ulong)key.id); hash = RenderFingerprint.Mix(hash, (ulong)key.channel);
                hash = RenderFingerprint.Mix(hash, (ulong)key.tangentMode);
                hash = RenderFingerprint.Float(hash, key.time); hash = RenderFingerprint.Float(hash, key.value);
                hash = RenderFingerprint.Float(hash, key.inTangent); hash = RenderFingerprint.Float(hash, key.outTangent);
            }
            return hash;
        }
    }
}
