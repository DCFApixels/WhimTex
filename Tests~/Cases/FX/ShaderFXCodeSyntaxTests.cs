using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXCodeSyntaxWindow : EditorWindow { }

public static class ShaderFXCodeSyntaxTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Source = "// 😀 Unicode é\r\n// @param _Amount float [0 .. 1] = 0.5 @preview(PreviewAmount)\r\n#define LIMIT 4\r\nfloat4 ApplyFX(PinnedUV uv, float4 color)\r\n{\r\n    /* return 22 */\r\n    return SampleTexture(_MainTex, uv) * 1.5e-2;\r\n}\r\n";
    static string Key(string id) => "WhimTex.Tests.CodeSyntax." + Guid.Parse(id).ToString("D");
    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate syntax fixture").ToJson();
        var fixture = new Fixture();
        AppDomain.CurrentDomain.SetData(Key(id), (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        try { fixture.Begin(); } catch (Exception e) { fixture.Fail(e); }
        return fixture.Poll();
    }
    public static string Poll(string id) => (AppDomain.CurrentDomain.GetData(Key(id)) as Func<string>)?.Invoke()
        ?? TestContext.Result("failed", 0, "Missing syntax fixture").ToJson();
    public static string Cleanup(string id) => TestContext.Run("Release syntax fixture", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id), null); AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Owned FX/window released; user capture untouched");
    });
    sealed class Fixture : IDisposable
    {
        readonly TestContext test = new TestContext();
        EditorWindow previous, window;
        ShaderFX effect;
        TextField field;
        TextElement text;
        object syntax;
        Type type;
        Action<TextElement.GlyphsEnumerable> production;
        IVisualElementScheduledItem schedule;
        string result;
        int stage, callbacks, waits;
        bool selectionSeen, colorsSeen, tailSeen;
        const string Tail = "float4 TailValue;";
        string Kind(int at) => type.GetMethod("At", F).Invoke(syntax, new object[] { at }).ToString();
        void Update(string value) => type.GetMethod("Update", F).Invoke(syntax, new object[] { value, null });
        int Revision => (int)type.GetProperty("Revision", F).GetValue(syntax);
        void CheckProfiler()
        {
            var profiler = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
            if ((bool)profiler.GetProperty("Recording", F).GetValue(null)) return;
            var captureField = profiler.GetField("capture", F); var currentField = profiler.GetField("current", F);
            object previousCapture = captureField.GetValue(null), previousCurrent = currentField.GetValue(null);
            var captureType = profiler.GetNestedType("Capture", F); var sampleType = profiler.GetNestedType("Sample", F);
            object capture = Activator.CreateInstance(captureType, true);
            var events = Array.CreateInstance(sampleType, 32);
            captureType.GetField("events", F).SetValue(capture, events);
            captureType.GetField("recording", F).SetValue(capture, true);
            captureType.GetField("deadline", F).SetValue(capture, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5);
            try
            {
                captureField.SetValue(null, capture); currentField.SetValue(null, -1);
                Update("float4 profilerToken;");
                test.Equal(1, (int)captureType.GetField("count", F).GetValue(capture), "Changed source records one tokenization span");
                object sample = events.GetValue(0);
                string phase = (string)sampleType.GetField("phase", F).GetValue(sample);
                test.Equal("ui.code.tokenize", phase, "Profiler exposes code tokenization");
                test.True((bool)sampleType.GetField("completed", F).GetValue(sample), "Tokenization span completes");
                test.Equal("UI", profiler.GetMethod("GroupFor", F).Invoke(null, new object[] { phase, "span" }).ToString(), "Code work belongs to UI & Input");
                Update("float4 profilerToken;");
                test.Equal(1, (int)captureType.GetField("count", F).GetValue(capture), "Cache hit adds no tokenization span");
            }
            finally { captureField.SetValue(null, previousCapture); currentField.SetValue(null, previousCurrent); }
        }
        void Expect(string source, string token, string expected)
        {
            int at = source.IndexOf(token, StringComparison.Ordinal);
            test.True(at >= 0, "Lexer fixture contains " + token);
            test.Equal(expected, Kind(at), "Token " + token);
        }
        public void Begin()
        {
            previous = EditorWindow.focusedWindow;
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, Source, new List<ShaderFXParameter>() });
            effect.hideFlags = HideFlags.HideAndDontSave;
            field = (TextField)Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCodeField", true), F, null, new object[] { effect }, null);
            text = (TextElement)field.textEdition;
            syntax = field.GetType().GetField("syntax", F).GetValue(field);
            type = syntax.GetType();
            production = text.PostProcessTextVertices;
            test.True(production != null, "Production glyph callback actually installed");
            test.True(!text.enableRichText, "Source is plain text, not rich markup");
            string lexer = "// @param _Gain float2 [0 % 360] = (0, 1) @enableif(_On)\n// ordinary float4 123\n/* float4\n return */\n#define VALUE 0xFFu\nfloat3x3 matrix; min16float4 preciseColor;\nfloat4 ApplyFX(PinnedUV uv, float4 color) { return SampleInput(uv) * .25e-2f; }\n\"string \\\" return\"; \"unterminated\nfloat value; // comment";
            Update(lexer);
            Expect(lexer, "@param", "Directive"); Expect(lexer, "@enableif", "Directive");
            Expect(lexer, "_Gain", "Parameter"); Expect(lexer, "float2", "Type");
            Expect(lexer, "360", "Number"); Expect(lexer, "ordinary", "Comment");
            Expect(lexer, "float4 123", "Comment"); Expect(lexer, "return */", "Comment");
            Expect(lexer, "#define", "Directive"); Expect(lexer, "0xFFu", "Number");
            Expect(lexer, "float3x3", "Type"); Expect(lexer, "min16float4", "Type");
            Expect(lexer, "ApplyFX", "Function"); Expect(lexer, "PinnedUV", "Type");
            Expect(lexer, "SampleInput", "Function"); Expect(lexer, ".25e-2f", "Number");
            Expect(lexer, "string ", "String"); Expect(lexer, "unterminated", "String");
            Expect(lexer, "float value", "Type"); Expect(lexer, "preciseColor", "Plain");
            string metadata = "// @whimtex-effect Test/Color\n// @param hidden label(Tint) preview(PreviewTint) color _Tint = #FFFFFFFF\n// @param texture2D _Mask = none";
            Update(metadata);
            Expect(metadata, "@whimtex-effect", "Directive");
            test.Equal("Directive", Kind(metadata.IndexOf("effect", StringComparison.Ordinal)), "Hyphenated directive is entirely colored");
            Expect(metadata, "preview", "Directive"); Expect(metadata, "hidden", "Directive");
            Expect(metadata, "color _Tint", "Type"); Expect(metadata, "texture2D", "Type");
            Update(lexer);
            int revision = Revision; Update(new string(lexer.ToCharArray()));
            test.Equal(revision, Revision, "Equal contents skip tokenization even with a new string reference");
            Update("/* unclosed\nfloat4"); test.Equal("Comment", Kind(12), "Unclosed block comment");
            string crlf = "// @param _X float = \"caption\"\r\nreturn color;";
            Update(crlf); test.Equal("Keyword", Kind(crlf.IndexOf("return", StringComparison.Ordinal)), "CRLF ends metadata comment");
            Update(""); test.Equal("Plain", Kind(0), "Empty text safe");
            CheckProfiler();
            Update(Source);
            text.PostProcessTextVertices = VerifyGlyphs;
            window = ScriptableObject.CreateInstance<ShaderFXCodeSyntaxWindow>();
            window.titleContent = new GUIContent("FX Syntax Check");
            typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            window.rootVisualElement.Add(field);
            window.ShowUtility(); window.position = new Rect(100, 100, 640, 480);
            field.Focus(); field.textSelection.SelectRange(0, 0);
            schedule = window.rootVisualElement.schedule.Execute(Step).Every(50).StartingIn(300);
        }
        void VerifyGlyphs(TextElement.GlyphsEnumerable glyphs)
        {
            if (result != null) { production(glyphs); return; }
            try
            {
                var before = new List<Vertex[]>();
                foreach (var glyph in glyphs) before.Add(glyph.vertices.ToArray());
                string value = field.value;
                int cursor = field.textSelection.cursorIndex, select = field.textSelection.selectIndex;
                Rect clip = (Rect)type.GetMethod("VisibleRect", F).Invoke(syntax, null);
                production(glyphs);
                test.Equal(value, field.value, "Coloring never rewrites source");
                test.Equal(cursor, field.textSelection.cursorIndex, "Callback preserves caret");
                test.Equal(select, field.textSelection.selectIndex, "Callback preserves selection");
                int index = 0;
                foreach (var glyph in glyphs)
                {
                    var range = glyph.textRange;
                    for (int i = 0; i < glyph.vertices.Length; i++)
                    {
                        var vertex = glyph.vertices[i]; var old = before[index][i];
                        test.Equal(old.position, vertex.position, "Glyph positions unchanged");
                        test.Equal(old.uv, vertex.uv, "Glyph UVs unchanged");
                        test.Equal(old.tint.a, vertex.tint.a, "Original alpha unchanged");
                        bool selected = range.start < Math.Max(cursor, select) && range.end > Math.Min(cursor, select);
                        if (selected) { test.Equal(old.tint, vertex.tint, "Native selected text tint retained"); selectionSeen = true; }
                        else if (range.length > 0 && range.start >= 0 && range.start < value.Length && Kind(range.start) != "Plain" &&
                            Math.Min(glyph.vertices[0].position.y, glyph.vertices[1].position.y) <= clip.yMax && Math.Max(glyph.vertices[0].position.y, glyph.vertices[1].position.y) >= clip.yMin)
                        {
                            var palette = (Color32[])type.GetField("palette", F).GetValue(syntax);
                            int kind = (int)Convert.ChangeType(type.GetMethod("At", F).Invoke(syntax, new object[] { range.start }), typeof(int));
                            Color32 expected = palette[kind]; expected.a = old.tint.a;
                            test.Equal(expected, vertex.tint, "Real generated glyph receives lexical tint at UTF-16 index " + range.start);
                            colorsSeen |= old.tint.r != vertex.tint.r || old.tint.g != vertex.tint.g || old.tint.b != vertex.tint.b;
                            tailSeen |= value.EndsWith(Tail, StringComparison.Ordinal) && range.start >= value.Length - Tail.Length;
                        }
                    }
                    index++;
                }
                callbacks++;
            }
            catch (Exception e) { result = TestContext.Result("failed", test.Checks, "Generated glyph verification", e.ToString()).ToJson(); }
        }
        void Step()
        {
            try
            {
                if (result != null) return;
                if (callbacks == 0 || stage == 1 && !selectionSeen) { if (++waits > 30) throw new InvalidOperationException("Real glyph callback/selection did not render"); window.Repaint(); return; }
                if (stage == 0)
                {
                    test.True(colorsSeen, "Real rendered glyphs changed color");
                    test.Equal(Source, field.value, "Plain CRLF, surrogate pair and literal source preserved");
                    int start = Source.IndexOf("return Sample", StringComparison.Ordinal);
                    field.textSelection.SelectRange(start + 6, start);
                    text.MarkDirtyRepaint(); waits = 0; stage = 1; return;
                }
                if (stage == 1)
                {
                    test.True(selectionSeen, "Backward selection survives a real colored repaint");
                    int revision = Revision;
                    Update(field.value); test.Equal(revision, Revision, "Caret-only repaint reuses token cache");
                    test.Equal(Source, (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(effect), "Model remains exact source");
                    test.True(!(bool)typeof(ShaderFX).GetProperty("HasAppliedShader", F).GetValue(effect), "Highlighting never compiles FX");
                    string longSource = Source + new string('M', 256) + "\n" + string.Concat(System.Linq.Enumerable.Repeat("float4 offscreen;\n", 120)) + Tail;
                    typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { longSource });
                    field.GetType().GetMethod("SyncFromModel", F).Invoke(field, null);
                    field.textSelection.SelectRange(0, 0); stage = 2; waits = 0; return;
                }
                var scroll = field.Q<ScrollView>();
                test.True(scroll != null, "Native multiline field has a public scroll viewport");
                if (stage == 2)
                {
                    if (scroll.verticalScroller.highValue <= 0) { if (++waits > 30) throw new InvalidOperationException("Long text never acquired scrolling"); return; }
                    test.Equal(WhiteSpace.NoWrap, text.resolvedStyle.whiteSpace, "Native source text does not wrap visually");
                    test.Equal(ScrollViewMode.VerticalAndHorizontal, scroll.mode, "Both native scroll axes are enabled");
                    test.True(scroll.horizontalScroller.highValue > 0, "Long logical line gets a horizontal scroll range");
                    test.Equal(DisplayStyle.Flex, scroll.horizontalScroller.resolvedStyle.display, "Horizontal scrollbar is displayed for overflow");
                    test.True(!tailSeen, "Offscreen tail is not recolored prematurely");
                    scroll.verticalScroller.value = scroll.verticalScroller.highValue;
                    stage = 3; waits = 0; return;
                }
                if (!tailSeen) { if (++waits > 30) throw new InvalidOperationException("Scroll did not recolor newly visible tail"); window.Repaint(); return; }
                test.True(tailSeen, "Newly exposed glyphs are colored after scrolling without source edits");
                result = TestContext.Result("passed", test.Checks, "Lexer, actual glyph tint/positions/UV/alpha, caret, selection, cache and scrolling").ToJson(); Dispose();
            }
            catch (Exception e) { Fail(e); }
        }
        public string Poll() => result ?? TestContext.Result("running", test.Checks, "Waiting for actual glyph repaint").ToJson();
        public void Fail(Exception e) { result = TestContext.Result("failed", test.Checks, "Syntax highlighting", e.ToString()).ToJson(); Dispose(); }
        public void Dispose()
        {
            var errors = new List<Exception>();
            void ReleaseOwned(Action release) { try { release(); } catch (Exception error) { errors.Add(error); } }
            ReleaseOwned(() => { schedule?.Pause(); schedule = null; });
            ReleaseOwned(() => { if (text != null) text.PostProcessTextVertices = production; });
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
                var error = new AggregateException("Owned syntax highlighting cleanup requires recovery", errors);
                result = TestContext.Result("failed", test.Checks, "Syntax highlighting cleanup requires recovery; prior result: " + result, error.ToString()).ToJson();
                throw error;
            }
        }
    }
}
