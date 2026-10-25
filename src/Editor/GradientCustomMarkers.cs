using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class GradientCustomMarkers
    {
        internal static bool IsCustom(WhimTexGradient gradient) => gradient != null &&
            gradient.Interpolation == WhimTexGradientInterpolation.Custom;

        internal static string Label(WhimTexGradient gradient, WhimTexGradientCurveKey marker)
        {
            string result = "";
            var group = Group(gradient, marker.id);
            foreach (var key in gradient.CustomKeys)
                if (Array.IndexOf(group, key.id) >= 0)
                    result += (result.Length == 0 ? "" : "/") + gradient.CustomChannelName(key.channel);
            return result;
        }

        internal static Color Swatch(WhimTexGradient gradient, WhimTexGradientCurveKey key, bool alphaAsGray = true)
        {
            Color color = gradient.Evaluate(key.time);
            if (key.channel == 3 && alphaAsGray) color = new Color(color.a, color.a, color.a, 1);
            else if (gradient.ColorSpace == ColorSpace.Linear) color = HdrUtility.Encode(color);
            color /= Mathf.Max(1, Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
            color.a = 1;
            return color;
        }

        internal static bool CanRemove(WhimTexGradient gradient, int id)
        {
            if (!gradient.TryGetCustomKey(id, out _)) return false;
            return CanRemove(gradient, Group(gradient, id));
        }

        internal static bool CanRemove(WhimTexGradient gradient, int[] ids)
        {
            if (ids == null || ids.Length == 0) return false;
            int remainingChannels = 0;
            foreach (var key in gradient.CustomKeys)
                if (Array.IndexOf(ids, key.id) < 0) remainingChannels |= 1 << key.channel;
            return remainingChannels == 15;
        }

        internal static int[] Group(WhimTexGradient gradient, int id) => gradient.GetCustomMarkerGroup(id);

        internal static void Move(WhimTexGradient gradient, int id, float time)
        {
            if (gradient.TryGetCustomKey(id, out var key)) gradient.MoveCustomKeys(Group(gradient, id), time - key.time);
        }

        internal static void Remove(WhimTexGradient gradient, int id) => gradient.RemoveCustomKeys(Group(gradient, id));

        internal static void SetColor(WhimTexGradient gradient, Color encodedColor, int selectedId = 0)
        {
            var sample = new WhimTexGradient { ColorSpace = gradient.ColorSpace, ColorMode = gradient.ColorMode };
            sample.SetKeys(new[] { new GradientColorKey(encodedColor, 0) }, new[] { new GradientAlphaKey(1, 0) });
            sample.Interpolation = WhimTexGradientInterpolation.Custom;
            var keys = gradient.CustomKeys;
            var group = selectedId == 0 ? Array.Empty<int>() : Group(gradient, selectedId);
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].channel == 3 || selectedId != 0 && Array.IndexOf(group, keys[i].id) < 0) continue;
                keys[i].value = sample.EvaluateCustomChannel(keys[i].channel, 0);
                if (selectedId == 0) keys[i].inTangent = keys[i].outTangent = 0;
            }
            gradient.SetCustomData(keys, gradient.CustomColorMode);
        }
    }
}
