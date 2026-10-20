#if UNITY_6000_7_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXCodeLineNumbers : VisualElement
    {
        internal struct Number
        {
            internal int line;
            internal Vector2 position;
            internal Color color;
        }
        sealed class CurrentLineBackground : VisualElement
        {
            internal Rect drawnRect;
            internal CurrentLineBackground(ShaderFXCodeLineNumbers owner)
            {
                name = "shaderFXCurrentLine";
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-code-current-line");
                generateVisualContent += context =>
                {
                    drawnRect = owner.HasTextFocus ? FillLine(context, this, owner.CurrentLineRect(this)) : default;
                };
                RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
            }
        }
        static readonly CustomStyleProperty<Color> LineColor = new CustomStyleProperty<Color>("--whimtex-code-current-line-color");
        static readonly CustomStyleProperty<Color> NumberColor = new CustomStyleProperty<Color>("--whimtex-code-current-number-color");
        readonly TextField field;
        readonly TextElement text;
        readonly ShaderFX effect;
        readonly Label metrics;
        readonly CurrentLineBackground highlight;
        readonly List<int> starts = new List<int>();
        readonly List<string> numbers = new List<string>();
        readonly List<Number> drawn = new List<Number>();
        ScrollView scroll;
        string source;
        internal int LineCount => starts.Count;
        internal int LineStart(int line) => starts[line];
        internal int CurrentLine
        {
            get
            {
                int index = starts.BinarySearch(Mathf.Clamp(field.textSelection.cursorIndex, 0, source?.Length ?? 0));
                return index >= 0 ? index : Math.Max(0, ~index - 1);
            }
        }
        internal bool HasTextFocus => field.enabledInHierarchy && field.panel?.focusController?.focusedElement is VisualElement focused &&
            (focused == field || field.Contains(focused));

        internal ShaderFXCodeLineNumbers(TextField field, ShaderFX effect)
        {
            this.field = field; this.effect = effect;
            text = (TextElement)field.textEdition;
            highlight = new CurrentLineBackground(this);
            field.EnableInClassList("whimtex-code-light", !EditorGUIUtility.isProSkin);
            name = "shaderFXLineNumbers";
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-code-line-numbers");
            EnableInClassList("whimtex-code-line-numbers-light", !EditorGUIUtility.isProSkin);
            metrics = new Label("0") { name = "shaderFXLineMetrics", pickingMode = PickingMode.Ignore, enableRichText = false };
            metrics.AddToClassList("whimtex-code-line-metrics");
            Add(metrics);
            generateVisualContent += DrawNumbers;
            RegisterCallback<CustomStyleResolvedEvent>(_ => Invalidate());
            field.RegisterCallback<FocusInEvent>(_ => Invalidate(), TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Invalidate(), TrickleDown.TrickleDown);
            field.RegisterCallback<ChangeEvent<string>>(evt => { if (evt.target == field) Refresh(evt.newValue); });
            field.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != field) return;
                scroll = field.Q<ScrollView>();
                if (scroll == null) return;
                var host = scroll.contentViewport.parent;
                if (parent != host) host.Insert(host.IndexOf(scroll.contentViewport), this);
                if (highlight.parent != scroll.contentViewport) scroll.contentViewport.hierarchy.Insert(0, highlight);
                scroll.verticalScroller.valueChanged += OnScroll;
                scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                text.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                field.textSelection.OnCursorIndexChange += Invalidate;
                field.textSelection.OnSelectIndexChange += Invalidate;
                Invalidate();
            });
            field.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != field || scroll == null) return;
                scroll.verticalScroller.valueChanged -= OnScroll;
                scroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                text.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                field.textSelection.OnCursorIndexChange -= Invalidate;
                field.textSelection.OnSelectIndexChange -= Invalidate;
                scroll = null;
            });
            Refresh(field.value);
        }
        void Invalidate() { MarkDirtyRepaint(); highlight.MarkDirtyRepaint(); }
        void OnScroll(float _) => Invalidate();
        void OnGeometry(GeometryChangedEvent _) => Invalidate();

        internal void Refresh(string value)
        {
            value ??= string.Empty;
            if (string.Equals(source, value, StringComparison.Ordinal)) return;
            source = value;
            starts.Clear(); starts.Add(0);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\r')
                {
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    starts.Add(i + 1);
                }
                else if (value[i] == '\n') starts.Add(i + 1);
            }
            while (numbers.Count < starts.Count) numbers.Add((numbers.Count + 1).ToString(CultureInfo.InvariantCulture));
            EnableInClassList("whimtex-code-line-numbers-wide", starts.Count >= 1000);
            EnableInClassList("whimtex-code-line-numbers-very-wide", starts.Count >= 100000);
            Invalidate();
        }

        internal Rect CurrentLineRect(VisualElement target)
        {
            if (scroll == null || starts.Count == 0) return default;
            int line = CurrentLine;
            float baseline = field.textSelection.GetCursorPositionFromStringIndex(starts[line]).y;
            float height = line + 1 < starts.Count
                ? field.textSelection.GetCursorPositionFromStringIndex(starts[line + 1]).y - baseline
                : line > 0 ? baseline - field.textSelection.GetCursorPositionFromStringIndex(starts[line - 1]).y : 0;
            if (!(height > 0)) height = metrics.MeasureTextSize("0", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).y;
            float top = target.WorldToLocal(text.LocalToWorld(text.contentRect.position + new Vector2(0, baseline - height))).y;
            return new Rect(target.contentRect.xMin, top, target.contentRect.width, height);
        }

        static Rect FillLine(MeshGenerationContext context, VisualElement target, Rect rect)
        {
            rect = Rect.MinMaxRect(Math.Max(rect.xMin, target.contentRect.xMin), Math.Max(rect.yMin, target.contentRect.yMin),
                Math.Min(rect.xMax, target.contentRect.xMax), Math.Min(rect.yMax, target.contentRect.yMax));
            if (rect.width <= 0 || rect.height <= 0 || !target.customStyle.TryGetValue(LineColor, out Color color)) return default;
            var painter = context.painter2D;
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath(); painter.Fill();
            return rect;
        }

        void DrawNumbers(MeshGenerationContext context)
        {
            drawn.Clear();
            if (scroll == null || text.panel == null || contentRect.width <= 0 || contentRect.height <= 0) return;
            using var timing = WhimTexProfiler.Measure("ui.code.lines", effect != null ? effect.EmbeddedOwner : null, detail: "HLSL");
            float calibration = metrics.selection.GetCursorPositionFromStringIndex(0).y;
            float numberHeight = metrics.MeasureTextSize("0", 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).y;
            Vector2 origin = this.WorldToLocal(text.LocalToWorld(text.contentRect.position));
            float Baseline(int line) => origin.y + field.textSelection.GetCursorPositionFromStringIndex(starts[line]).y;
            int low = 0, high = starts.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (Baseline(middle) - calibration + numberHeight < contentRect.yMin) low = middle + 1;
                else high = middle;
            }
            var font = text.resolvedStyle.unityFontDefinition.fontAsset;
            float size = text.resolvedStyle.fontSize;
            Color color = resolvedStyle.color;
            bool focused = HasTextFocus;
            int current = CurrentLine;
            Color activeColor = customStyle.TryGetValue(NumberColor, out Color active) ? active : color;
            if (focused) FillLine(context, this, CurrentLineRect(this));
            for (int line = low; line < starts.Count; line++)
            {
                float y = Baseline(line) - calibration;
                if (y > contentRect.yMax) break;
                string number = numbers[line];
                float width = metrics.MeasureTextSize(number, 0, MeasureMode.Undefined, 0, MeasureMode.Undefined).x;
                var position = new Vector2(contentRect.xMax - width, y);
                Color tint = focused && line == current ? activeColor : color;
                context.DrawText(number, position, size, tint, font);
                drawn.Add(new Number { line = line, position = position, color = tint });
            }
        }
    }
}
#endif
