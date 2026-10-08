using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexColorChannels
    {
        private sealed class Context { internal Func<int> read; }
        private static readonly ConditionalWeakTable<VisualElement, Context> Contexts = new ConditionalWeakTable<VisualElement, Context>();
        private const string Preference = "WhimTex.ColorPicker.Channels";
        private static bool enabled = EditorPrefs.GetBool(Preference, true);
        internal static bool Enabled
        {
            get => enabled;
            set { if (enabled == value) return; enabled = value; EditorPrefs.SetBool(Preference, value); }
        }
        internal static void SetSource(VisualElement root, Func<int> read)
        { Contexts.Remove(root); Contexts.Add(root, new Context { read = read }); }
        internal static Func<int> FindSource(VisualElement element, WhimTexDocument document)
        {
            for (var parent = element; parent != null; parent = parent.parent)
                if (Contexts.TryGetValue(parent, out var context)) return context.read;
            return WhimTexWindow.FindColorChannelSource(document);
        }
        internal static Color Apply(Color color, int mask)
        {
            if (mask < 0) return color;
            int rgb = mask & 7;
            if (rgb == 0) return (mask & 8) != 0 ? new Color(color.a, color.a, color.a, 1) : Color.black;
            float alpha = (mask & 8) != 0 ? color.a : 1;
            if (rgb == 1 || rgb == 2 || rgb == 4)
            {
                float v = rgb == 1 ? color.r : rgb == 2 ? color.g : color.b;
                return new Color(v, v, v, alpha);
            }
            return new Color((rgb & 1) != 0 ? color.r : 0, (rgb & 2) != 0 ? color.g : 0, (rgb & 4) != 0 ? color.b : 0, alpha);
        }
        internal static void DrawSplit(Painter2D painter, Rect rect, Color original, int mask)
        {
            Color adapted = Apply(original, mask);
            original.a = adapted.a = 1;
            painter.fillColor = original;
            painter.BeginPath(); painter.MoveTo(rect.min); painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(rect.max); painter.LineTo(new Vector2(rect.xMin, rect.yMax)); painter.ClosePath(); painter.Fill();
            if (original.Equals(adapted)) return;
            painter.fillColor = adapted;
            painter.BeginPath(); painter.MoveTo(new Vector2(rect.xMax, rect.yMin)); painter.LineTo(rect.max);
            painter.LineTo(new Vector2(rect.xMin, rect.yMax)); painter.ClosePath(); painter.Fill();
        }
    }
}
