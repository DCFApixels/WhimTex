using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXCodeLinesWindow : EditorWindow { }

public static class ShaderFXCodeLinesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Key(string id) => "WhimTex.Tests.CodeLines." + Guid.Parse(id).ToString("D");
    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate line-number fixture").ToJson();
        var fixture = new Fixture();
        AppDomain.CurrentDomain.SetData(Key(id), (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        try { fixture.Begin(); } catch (Exception e) { fixture.Fail(e); }
        return fixture.Poll();
    }
    public static string Poll(string id) => (AppDomain.CurrentDomain.GetData(Key(id)) as Func<string>)?.Invoke()
        ?? TestContext.Result("failed", 0, "Missing line-number fixture").ToJson();
    public static string Cleanup(string id) => TestContext.Run("Release line-number fixture", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id), null); AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Only owned window, FX and Undo released");
    });
    sealed class Fixture : IDisposable
    {
        readonly TestContext test = new TestContext();
        ShaderFX effect;
        EditorWindow window, previous;
        TextField code;
        TextElement text;
        VisualElement gutter, highlight;
        Button other;
        Label metrics;
        ScrollView scroll;
        IVisualElementScheduledItem schedule;
        string result;
        int stage, ticks;
        float initialGutterX, baselineBefore;
        int referenceLine;
        string longSource;
        int Count => (int)gutter.GetType().GetProperty("LineCount", F).GetValue(gutter);
        int CurrentLine => (int)gutter.GetType().GetProperty("CurrentLine", F).GetValue(gutter);
        object Field(object obj, string name) => obj.GetType().GetField(name, F).GetValue(obj);
        IList Drawn => (IList)Field(gutter, "drawn");
        int StartOf(int line) => (int)gutter.GetType().GetMethod("LineStart", F).Invoke(gutter, new object[] { line });
        string Current => (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(effect);
        bool Focused => code.panel.focusController.focusedElement is VisualElement focused && (ReferenceEquals(code, focused) || code.Contains(focused));
        void SetCode(string value)
        {
            typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { value });
            code.GetType().GetMethod("SyncFromModel", F).Invoke(code, null);
            code.Focus(); code.textSelection.SelectRange(0, 0);
        }
        void Enter()
        {
            var target = (VisualElement)code.textEdition;
            using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' }))
            { evt.target = target; target.SendEvent(evt); }
        }
        void CheckDrawn()
        {
            test.True(Drawn.Count > 0, "Actual gutter generateVisualContent submitted line numbers");
            float calibration = metrics.selection.GetCursorPositionFromStringIndex(0).y;
            foreach (object entry in Drawn)
            {
                int line = (int)Field(entry, "line");
                var point = (Vector2)Field(entry, "position");
                Vector2 actualBaseline = gutter.LocalToWorld(point + new Vector2(0, calibration));
                Vector2 expected = text.LocalToWorld(text.contentRect.position + code.textSelection.GetCursorPositionFromStringIndex(StartOf(line)));
                test.Near(expected.y, actualBaseline.y, .15, "Rendered number aligns with native text cursor baseline for line " + (line + 1));
                test.True(line >= 0 && line < Count, "Rendered line within source count");
            }
            test.Equal(code.value, Current, "Gutter never edits code");
        }
        void CheckActiveLine(int line, bool focused = true)
        {
            test.Equal(line, CurrentLine, "Active logical line follows native caret, including empty lines");
            var rect = (Rect)Field(highlight, "drawnRect");
            if (focused)
            {
                test.True(rect.width > 0 && rect.height > 0, "Focused current row actually submitted a background fill; focus=" + Focused +
                    ", bounds=" + highlight.worldBound + ", line=" + gutter.GetType().GetMethod("CurrentLineRect", F).Invoke(gutter, new object[] { highlight }) +
                    ", color=" + highlight.customStyle.TryGetValue(new CustomStyleProperty<Color>("--whimtex-code-current-line-color"), out Color _) +
                    ", cursor=" + code.textSelection.GetCursorPositionFromStringIndex(StartOf(line)));
                test.Near(scroll.contentViewport.contentRect.width, rect.width, .2, "Row fills visible width independently of horizontal scroll");
                float bottom = highlight.LocalToWorld(new Vector2(0, rect.yMax)).y;
                float expected = text.LocalToWorld(text.contentRect.position + code.textSelection.GetCursorPositionFromStringIndex(StartOf(line))).y;
                test.Near(Math.Min(expected, highlight.worldBound.yMax), bottom, .2, "Row bottom follows native caret baseline and viewport clipping");
            }
            else test.Equal(default(Rect), rect, "Unfocused code does not submit an active-row background");
            bool found = false;
            foreach (object entry in Drawn)
            {
                int number = (int)Field(entry, "line");
                Color tint = (Color)Field(entry, "color");
                if (focused && number == line)
                {
                    found = true;
                    test.True(tint != gutter.resolvedStyle.color, "Current line number uses a distinct higher-contrast tint");
                }
                else test.Equal(gutter.resolvedStyle.color, tint, "Other numbers retain ordinary tint");
            }
            if (focused) test.True(found, "Current number actually drawn");
            test.Equal(focused, Focused, "Background does not change native focus");
            test.Equal(code.value, Current, "Current row decoration never changes source");
        }
        public void Begin()
        {
            previous = EditorWindow.focusedWindow;
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, "", new List<ShaderFXParameter>() });
            effect.hideFlags = HideFlags.HideAndDontSave;
            code = (TextField)Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCodeField", true), F, null, new object[] { effect }, null);
            text = (TextElement)code.textEdition;
            window = ScriptableObject.CreateInstance<ShaderFXCodeLinesWindow>();
            window.titleContent = new GUIContent("FX Line Numbers Check");
            typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            window.rootVisualElement.Add(code);
            other = new Button { text = "Focus check" };
            window.rootVisualElement.Add(other);
            window.ShowUtility(); window.position = new Rect(100, 100, 640, 480);
            code.Focus();
            schedule = window.rootVisualElement.schedule.Execute(Step).Every(50).StartingIn(300);
        }
        void Step()
        {
            try
            {
                if (++ticks < 4) return;
                ticks = 0;
                if (stage == 0)
                {
                    gutter = code.Q("shaderFXLineNumbers"); metrics = gutter?.Q<Label>("shaderFXLineMetrics"); scroll = code.Q<ScrollView>();
                    highlight = code.Q("shaderFXCurrentLine");
                    test.True(gutter != null && metrics != null && scroll != null, "Gutter and native scrolling available");
                    test.True(highlight != null && highlight.pickingMode == PickingMode.Ignore && !highlight.focusable, "Row background never intercepts focus or pointer input");
                    test.True(ReferenceEquals(highlight.parent, scroll.contentViewport), "Row background is fixed to viewport rather than horizontal content");
                    test.True(scroll.contentViewport.hierarchy.IndexOf(highlight) < scroll.contentViewport.hierarchy.IndexOf(scroll.contentContainer), "Background is behind native text, caret and selection");
                    test.Equal(PickingMode.Ignore, gutter.pickingMode, "Numbers do not intercept editing input");
                    test.True(!gutter.focusable, "Gutter cannot steal focus");
                    test.True(ReferenceEquals(gutter.parent, scroll.contentViewport.parent), "Gutter sits beside viewport, outside horizontally moving content");
                    test.True(gutter.worldBound.xMax <= scroll.contentViewport.worldBound.xMin + .2, "Numbers are left of code, without overlap");
                    test.Equal(text.resolvedStyle.fontSize, metrics.resolvedStyle.fontSize, "Number calibration uses code font size");
                    test.Equal(text.resolvedStyle.unityFontDefinition, metrics.resolvedStyle.unityFontDefinition, "Number calibration uses code font");
                    test.Equal(1, Count, "Empty document still has line 1"); CheckDrawn(); CheckActiveLine(0);
                    SetCode("alpha\r\n\r\nbeta\r\ngamma\r\n"); stage++; return;
                }
                if (stage == 1)
                {
                    test.Equal(5, Count, "CRLF, blank and trailing lines counted once");
                    int[] starts = { 0, 7, 9, 15, 22 };
                    for (int i = 0; i < starts.Length; i++) test.Equal(starts[i], StartOf(i), "Exact logical start " + i);
                    CheckDrawn(); CheckActiveLine(0);
                    int cursor = code.value.Length;
                    code.textSelection.SelectRange(cursor, cursor); Enter();
                    test.Equal(6, Count, "Enter updates line numbers immediately");
                    test.Equal(cursor + 2, code.textSelection.cursorIndex, "CRLF Enter retains caret");
                    test.Equal(5, CurrentLine, "Enter immediately changes active line");
                    Undo.PerformUndo(); test.Equal(5, Count, "Undo removes inserted line number");
                    test.Equal(cursor, code.textSelection.cursorIndex, "Undo retains caret");
                    test.Equal(4, CurrentLine, "Undo restores active line");
                    Undo.PerformRedo(); test.Equal(6, Count, "Redo restores inserted line number");
                    test.Equal(5, CurrentLine, "Redo restores active line");
                    test.True(Focused, "Gutter refresh keeps code focus");
                    longSource = new string('M', 400) + "\n" + string.Concat(System.Linq.Enumerable.Repeat("float4 value;\n", 1100));
                    SetCode(longSource); stage++; return;
                }
                if (stage == 2)
                {
                    test.Equal(1102, Count, "Large file count includes trailing empty line");
                    test.True(Drawn.Count < 100, "Only viewport numbers drawn, not all 1102 lines"); CheckDrawn(); CheckActiveLine(0);
                    code.textSelection.SelectRange(StartOf(6), StartOf(6));
                    initialGutterX = gutter.worldBound.x;
                    scroll.horizontalScroller.value = Math.Min(120, scroll.horizontalScroller.highValue);
                    test.True(scroll.horizontalScroller.value > 0, "Fixture genuinely scrolls horizontally");
                    stage++; return;
                }
                if (stage == 3)
                {
                    test.Near(initialGutterX, gutter.worldBound.x, .1, "Gutter fixed under horizontal scrolling"); CheckDrawn(); CheckActiveLine(6);
                    referenceLine = Math.Min(6, Count - 1);
                    baselineBefore = text.LocalToWorld(text.contentRect.position + code.textSelection.GetCursorPositionFromStringIndex(StartOf(referenceLine))).y;
                    scroll.verticalScroller.value = 74.5f;
                    stage++; return;
                }
                if (stage == 4)
                {
                    CheckDrawn(); CheckActiveLine(6);
                    float after = text.LocalToWorld(text.contentRect.position + code.textSelection.GetCursorPositionFromStringIndex(StartOf(referenceLine))).y;
                    test.Near(74.5, baselineBefore - after, 1, "Text and numbers follow fractional vertical scrolling");
                    test.Near(initialGutterX, gutter.worldBound.x, .1, "Vertical scroll does not move gutter horizontally");
                    code.textSelection.SelectRange(code.value.Length, code.value.Length);
                    scroll.verticalScroller.value = scroll.verticalScroller.highValue; stage++; return;
                }
                if (stage == 5)
                {
                    CheckDrawn(); CheckActiveLine(Count - 1);
                    int last = (int)Field(Drawn[Drawn.Count - 1], "line");
                    test.Equal(Count - 1, last, "Last empty line number rendered at bottom");
                    code.SetValueWithoutNotify("x\n\ny\n");
                    test.Equal(4, Count, "Silent external field update refreshes gutter");
                    SetCode(""); stage++; return;
                }
                if (stage == 6)
                {
                    CheckDrawn(); CheckActiveLine(0); test.Equal(1, Count, "Returning to empty source restores single number");
                    SetCode("alpha\n\nbeta\n"); code.textSelection.SelectRange(6, 1);
                    stage++; return;
                }
                if (stage == 7)
                {
                    CheckActiveLine(1);
                    test.Equal(6, code.textSelection.cursorIndex, "Row highlight preserves caret on blank line");
                    test.Equal(1, code.textSelection.selectIndex, "Row highlight preserves native text selection");
                    other.Focus(); stage++; return;
                }
                if (stage == 8)
                {
                    CheckActiveLine(1, false);
                    code.Focus(); code.textSelection.SelectRange(code.value.Length, code.value.Length);
                    stage++; return;
                }
                CheckActiveLine(3);
                test.Equal(code.value.Length, code.textSelection.cursorIndex, "Focus restore highlights final empty row without changing caret");
                result = TestContext.Result("passed", test.Checks, "Native line numbers/current row, focus, selection, scroll axes, CRLF/empty lines and Undo/Redo").ToJson(); Dispose();
            }
            catch (Exception e) { Fail(e); }
        }
        public string Poll() => result ?? TestContext.Result("running", test.Checks, "Line-number stage " + stage).ToJson();
        public void Fail(Exception e) { result = TestContext.Result("failed", test.Checks, "Line numbers", e.ToString()).ToJson(); Dispose(); }
        public void Dispose()
        {
            var errors = new List<Exception>();
            void ReleaseOwned(Action release) { try { release(); } catch (Exception error) { errors.Add(error); } }
            ReleaseOwned(() => { schedule?.Pause(); schedule = null; });
            ReleaseOwned(() =>
            {
                if (window != null)
                {
                    try { if (window.rootVisualElement.panel != null) window.Close(); }
                    finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
                }
                window = null;
            });
            ReleaseOwned(() =>
            {
                if (effect != null)
                {
                    try { Undo.ClearUndo(effect); }
                    finally { UnityEngine.Object.DestroyImmediate(effect); }
                }
                effect = null;
            });
            ReleaseOwned(() => { if (previous != null) previous.Focus(); previous = null; });
            if (errors.Count != 0)
            {
                var error = new AggregateException("Owned line numbers cleanup requires recovery", errors);
                result = TestContext.Result("failed", test.Checks, "Line numbers cleanup requires recovery; prior result: " + result, error.ToString()).ToJson();
                throw error;
            }
        }
    }
}
