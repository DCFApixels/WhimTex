using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    // The query is transient UI state, separate from the code's value and Undo.
    internal sealed class ShaderCodeSearch
    {
        static readonly CustomStyleProperty<Color> SelectionColor = new CustomStyleProperty<Color>("--unity-selection-color");
        static readonly char[] LineBreaks = { '\r', '\n' };
        readonly ShaderCodeField code;
        readonly Action endEdit;
        readonly List<int> matches = new List<int>();
        readonly List<int> matchingLines = new List<int>();
        Toolbar bar;
        VisualElement overlay, highlight;
        ScrollView scroll;
        ToolbarSearchField query;
        Label count;
        ToolbarButton previous, next;
        int current = -1;
        int savedCursor, savedSelect;
        bool open, queryFocused;
        IVisualElementScheduledItem reveal;

        internal ShaderCodeSearch(ShaderCodeField code, Action endEdit)
        {
            this.code = code;
            this.endEdit = endEdit;
            code.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            code.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != code) return;
                reveal?.Pause();
                if (scroll == null) return;
                scroll.verticalScroller.valueChanged -= OnScroll;
                scroll.horizontalScroller.valueChanged -= OnScroll;
#if UNITY_6000_7_OR_NEWER
                code.textSelection.OnCursorIndexChange -= OnSelectionChanged;
                code.textSelection.OnSelectIndexChange -= OnSelectionChanged;
#endif
                scroll = null;
            });
            code.RegisterCallback<AttachToPanelEvent>(evt => { if (evt.target == code && bar != null) AttachHighlight(); });
            ((VisualElement)code.textEdition).RegisterCallback<GeometryChangedEvent>(_ => highlight?.MarkDirtyRepaint());
        }

        // TextField is a composite root: Unity retargets its nested input events
        // to the outer field. Track local focus rather than inspecting internals.
        internal bool ContainsTarget(IEventHandler target) => bar != null &&
            ((target == code && queryFocused) || target is VisualElement element && (element == bar || bar.Contains(element)));

        void CreateBar()
        {
            bar = new Toolbar { name = "codeSearch" };
            bar.AddToClassList("whimtex-code-search");
            query = new ToolbarSearchField { name = "codeSearchQuery" };
            count = new Label { name = "codeSearchCount" };
            previous = new ToolbarButton(() => Move(-1)) { text = "↑", tooltip = "Previous match (Shift+Enter)" };
            next = new ToolbarButton(() => Move(1)) { text = "↓", tooltip = "Next match (Enter)" };
            var close = new ToolbarButton(Close) { text = "×", tooltip = "Close search (Esc)" };
            previous.name = "codeSearchPrevious"; next.name = "codeSearchNext"; close.name = "codeSearchClose";
            bar.Add(query); bar.Add(count); bar.Add(previous); bar.Add(next); bar.Add(close);
            query.RegisterValueChangedCallback(_ => { Refresh(); SelectCurrent(); });
            // Ancestors listen to the code field's ChangeEvent, never to its query.
            bar.RegisterCallback<ChangeEvent<string>>(evt => evt.StopPropagation());
            bar.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation());
            bar.RegisterCallback<FocusInEvent>(_ => queryFocused = true);
            bar.RegisterCallback<FocusOutEvent>(_ => queryFocused = false);
            overlay = new VisualElement { name = "codeSearchOverlay", pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("whimtex-code-search-overlay");
            overlay.Add(bar);
            code.hierarchy.Add(overlay);
            AttachHighlight();
        }

        void Open()
        {
            endEdit();
            if (bar == null) CreateBar();
            bool fromQuery = queryFocused;
            int start = Math.Min(code.textSelection.cursorIndex, code.textSelection.selectIndex);
            int end = Math.Max(code.textSelection.cursorIndex, code.textSelection.selectIndex);
            if (!fromQuery) { savedCursor = code.textSelection.cursorIndex; savedSelect = code.textSelection.selectIndex; }
            string source = code.value ?? string.Empty;
            if (!fromQuery && start >= 0 && end <= source.Length && end > start)
            {
                string selection = source.Substring(start, end - start);
                if (selection.IndexOfAny(LineBreaks) < 0) query.SetValueWithoutNotify(selection);
            }
            open = true;
            overlay.style.display = DisplayStyle.Flex;
            current = -1;
            Refresh();
            var input = query.Q<TextField>();
            input.Focus(); input.SelectAll();
            SelectCurrent();
        }

        void Close()
        {
            open = false;
            reveal?.Pause();
            overlay.style.display = DisplayStyle.None;
            highlight?.MarkDirtyRepaint();
            code.Focus();
            int start = current >= 0 ? matches[current] : savedCursor;
            int end = current >= 0 ? matches[current] + query.value.Length : savedSelect;
            code.textSelection.SelectRange(Mathf.Clamp(start, 0, code.value.Length), Mathf.Clamp(end, 0, code.value.Length));
        }

        internal void Refresh()
        {
            if (!open) return;
            int cursor = current >= 0 ? matches[current] : Math.Min(code.textSelection.cursorIndex, code.textSelection.selectIndex);
            matches.Clear(); matchingLines.Clear(); current = -1;
            string source = code.value ?? string.Empty, needle = query.value ?? string.Empty;
            if (needle.Length > 0)
            {
                int lineEnd = -1;
                for (int from = 0; from <= source.Length - needle.Length;)
                {
                    int found = source.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    if (found > lineEnd)
                    {
                        matchingLines.Add(matches.Count);
                        lineEnd = source.IndexOfAny(LineBreaks, found);
                        if (lineEnd < 0) lineEnd = source.Length;
                    }
                    matches.Add(found); from = found + needle.Length;
                }
                current = matches.FindIndex(position => position >= cursor);
                if (current < 0 && matches.Count > 0) current = 0;
            }
            UpdateCount();
            highlight?.MarkDirtyRepaint();
        }

        void UpdateCount()
        {
            count.text = (current + 1) + " / " + matches.Count;
            previous.SetEnabled(matches.Count > 0); next.SetEnabled(matches.Count > 0);
        }

        void Move(int direction)
        {
            if (matches.Count == 0) return;
            current = (current + direction + matches.Count) % matches.Count;
            UpdateCount(); SelectCurrent();
        }

        void SelectCurrent()
        {
            highlight?.MarkDirtyRepaint();
            if (current < 0) return;
            SelectMatchText();
            reveal?.Pause();
            reveal = code.schedule.Execute(RevealCurrent);
        }

        void SelectMatchText()
        {
            code.textSelection.cursorIndex = matches[current];
            code.textSelection.selectIndex = matches[current] + query.value.Length;
        }

        void OnScroll(float _) => highlight?.MarkDirtyRepaint();
        void OnSelectionChanged() => highlight?.MarkDirtyRepaint();

        void AttachHighlight()
        {
            scroll = ((VisualElement)code.textEdition).GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) return;
            if (highlight == null)
            {
                highlight = new VisualElement { name = "codeSearchMatch", pickingMode = PickingMode.Ignore };
                highlight.AddToClassList("whimtex-code-search-match");
                highlight.generateVisualContent += DrawMatch;
            }
            scroll.contentViewport.hierarchy.Insert(0, highlight);
            scroll.verticalScroller.valueChanged += OnScroll;
            scroll.horizontalScroller.valueChanged += OnScroll;
#if UNITY_6000_7_OR_NEWER
            code.textSelection.OnCursorIndexChange += OnSelectionChanged;
            code.textSelection.OnSelectIndexChange += OnSelectionChanged;
#endif
        }

        float LineHeight() => ((TextElement)code.textEdition).MeasureTextSize("M", 0,
            VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;

        Rect MatchRect(VisualElement target) => GetMatchRect(current, target, LineHeight());

        Rect GetMatchRect(int index, VisualElement target, float height)
        {
            var text = (TextElement)code.textEdition;
            int start = matches[index], end = start + query.value.Length;
#if UNITY_6000_7_OR_NEWER
            Vector2 a = code.textSelection.GetCursorPositionFromStringIndex(start);
            Vector2 b = code.textSelection.GetCursorPositionFromStringIndex(end);
#else
            // Older Editors expose only the current caret position. Measure the
            // unwrapped logical line with the same native font as the code field.
            int lineStart = code.value.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            int lines = 1;
            for (int i = 0; i < lineStart; i++) if (code.value[i] == '\n') lines++;
            float Width(int index) => text.MeasureTextSize(code.value.Substring(lineStart, index - lineStart), 0,
                VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
            Vector2 a = new Vector2(Width(start), lines * height), b = new Vector2(Width(end), lines * height);
#endif
            Vector2 position = target.WorldToLocal(text.LocalToWorld(text.contentRect.position + a));
            return new Rect(position.x, position.y - height, Math.Max(1, b.x - a.x), height);
        }

        void DrawMatch(MeshGenerationContext context)
        {
            if (!open || current < 0) return;
            float height = LineHeight();
            if (!((VisualElement)code.textEdition).customStyle.TryGetValue(SelectionColor, out Color color) &&
                !code.customStyle.TryGetValue(SelectionColor, out color)) return;
            color.a *= 0.6f;
            Rect clip = highlight.contentRect;
            int selectionStart = Math.Min(code.textSelection.cursorIndex, code.textSelection.selectIndex);
            int selectionEnd = Math.Max(code.textSelection.cursorIndex, code.textSelection.selectIndex);

            // Matches are ordered by source offset and indexed by logical line.
            // Skip offscreen lines, then skip horizontally clipped matches in each
            // visible line. Long files and long single lines do not require a scan.
            int low = 0, high = matchingLines.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (GetMatchRect(matchingLines[middle], highlight, height).yMax <= clip.yMin) low = middle + 1;
                else high = middle;
            }
            for (int line = low; line < matchingLines.Count; line++)
            {
                int first = matchingLines[line], end = line + 1 < matchingLines.Count ? matchingLines[line + 1] : matches.Count;
                if (GetMatchRect(first, highlight, height).yMin >= clip.yMax) break;
                low = first; high = end;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (GetMatchRect(middle, highlight, height).xMax <= clip.xMin) low = middle + 1;
                    else high = middle;
                }
                for (int index = low; index < end; index++)
                {
                    Rect rect = GetMatchRect(index, highlight, height);
                    if (rect.xMin >= clip.xMax) break;
                    if (selectionEnd > selectionStart && matches[index] < selectionEnd && matches[index] + query.value.Length > selectionStart)
                    {
#if !UNITY_6000_7_OR_NEWER
                        // The native selection is hidden while the query owns focus.
                        if (queryFocused) { Color selected = color; selected.a /= 0.6f; Fill(context, rect, selected); }
#endif
                        continue;
                    }
                    Fill(context, rect, color);
                }
            }
        }

        void Fill(MeshGenerationContext context, Rect rect, Color color)
        {
            Rect clip = highlight.contentRect;
            rect = Rect.MinMaxRect(Math.Max(rect.xMin, clip.xMin), Math.Max(rect.yMin, clip.yMin),
                Math.Min(rect.xMax, clip.xMax), Math.Min(rect.yMax, clip.yMax));
            if (rect.width <= 0 || rect.height <= 0) return;
            var painter = context.painter2D;
            painter.fillColor = color;
            painter.BeginPath(); painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin)); painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax)); painter.ClosePath(); painter.Fill();
        }

        void RevealCurrent()
        {
            if (!open || current < 0 || code.panel == null) return;
            if (scroll == null) return;
            SelectMatchText();
            Rect match = MatchRect(scroll.contentViewport);
            Rect view = scroll.contentViewport.contentRect;
            // Keep a found first-line fragment below the overlaid search toolbar.
            if (match.xMax > scroll.contentViewport.WorldToLocal(bar.worldBound.position).x)
                view.yMin = Math.Max(view.yMin, scroll.contentViewport.WorldToLocal(new Vector2(0, bar.worldBound.yMax)).y);
            Vector2 delta = Vector2.zero;
            if (match.xMin < view.xMin) delta.x = match.xMin - view.xMin;
            else if (match.xMax > view.xMax) delta.x = Math.Min(match.xMin - view.xMin, match.xMax - view.xMax);
            if (match.yMin < view.yMin) delta.y = match.yMin - view.yMin;
            else if (match.yMax > view.yMax) delta.y = match.yMax - view.yMax;
            scroll.scrollOffset += delta;
            highlight?.MarkDirtyRepaint();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            bool action = evt.ctrlKey || evt.commandKey;
            if (action && !evt.altKey && !evt.shiftKey && evt.keyCode == KeyCode.F)
            {
                WhimTexUI.ConsumeEvent(evt); Open();
            }
            else if (open && evt.keyCode == KeyCode.Escape)
            {
                WhimTexUI.ConsumeEvent(evt); Close();
            }
            else if (open && ContainsTarget(evt.target) && !action && !evt.altKey &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                WhimTexUI.ConsumeEvent(evt); Move(evt.shiftKey ? -1 : 1);
            }
        }
    }
}
