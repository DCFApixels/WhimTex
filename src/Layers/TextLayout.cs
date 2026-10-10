using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class TextLayout
    {
        internal readonly struct Line
        {
            internal readonly string text;
            internal readonly bool paragraphEnd;
            internal Line(string text, bool paragraphEnd) { this.text = text; this.paragraphEnd = paragraphEnd; }
        }

        internal static List<Line> Ellipsize(List<Line> lines, IReadOnlyList<float> offsets, float lineHeight,
            Vector2 frame, Func<string, float> measure)
        {
            var result = new List<Line>();
            for (int i = 0; i < lines.Count && offsets[i] + lineHeight <= frame.y; i++)
            {
                var line = lines[i];
                bool omitted = i + 1 < lines.Count && offsets[i + 1] + lineHeight > frame.y;
                if (omitted || measure(line.text) > frame.x)
                {
                    const string suffix = "\u2026";
                    if (measure(suffix) > frame.x) result.Add(new Line("", true));
                    else
                    {
                        int[] elements = StringInfo.ParseCombiningCharacters(line.text);
                        int low = 0, high = elements.Length;
                        string Fit(int count) => line.text.Substring(0, count < elements.Length ? elements[count] : line.text.Length).TrimEnd(' ') + suffix;
                        while (low < high)
                        {
                            int count = (low + high + 1) / 2;
                            if (measure(Fit(count)) <= frame.x) low = count; else high = count - 1;
                        }
                        result.Add(new Line(Fit(low), true));
                    }
                }
                else result.Add(line);
            }
            if (result.Count == 0) result.Add(new Line("", true));
            return result;
        }

        internal static List<Line> Build(string text, TextWrapping wrapping, float width, Func<string, float> measure, float characterSpacing = 0)
        {
            var result = new List<Line>();
            foreach (string paragraph in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n'))
            {
                if (wrapping == TextWrapping.Manual) { result.Add(new Line(paragraph, true)); continue; }
                int[] elements = StringInfo.ParseCombiningCharacters(paragraph);
                if (elements.Length == 0) { result.Add(new Line("", true)); continue; }
                var widths = new float[elements.Length];
                for (int i = 0; i < elements.Length; i++)
                {
                    int offset = elements[i], end = i + 1 < elements.Length ? elements[i + 1] : paragraph.Length;
                    widths[i] = measure(paragraph.Substring(offset, end - offset));
                }
                int start = 0;
                while (start < elements.Length)
                {
                    int end = start, lastSpace = -1; float length = 0;
                    while (end < elements.Length)
                    {
                        float increment = widths[end] + (end == start ? 0 : Math.Max(0, widths[end - 1] + characterSpacing) - widths[end - 1]);
                        if (end > start && length + increment > width) break;
                        if (paragraph[elements[end]] == ' ' && end > start) lastSpace = end;
                        length += increment; end++;
                    }
                    if (end < elements.Length && paragraph[elements[end]] == ' ') lastSpace = end;
                    int split = end < elements.Length && wrapping == TextWrapping.Words && lastSpace > start ? lastSpace : end;
                    int finish = split < elements.Length ? elements[split] : paragraph.Length;
                    int next = split;
                    while (next < elements.Length && paragraph[elements[next]] == ' ') next++;
                    result.Add(new Line(paragraph.Substring(elements[start], finish - elements[start]).TrimEnd(' '), next == elements.Length));
                    start = next;
                }
            }
            return result;
        }
    }
}
