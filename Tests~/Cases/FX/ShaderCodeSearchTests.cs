using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using AtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderCodeSearchTestWindow : EditorWindow { }
public static class ShaderCodeSearchTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Key(string id) => "WhimTex.Tests.CodeSearch." + Guid.Parse(id).ToString("D");
    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate search fixture").ToJson();
        var fixture = new Fixture();
        AppDomain.CurrentDomain.SetData(Key(id), (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        try { fixture.Begin(); } catch (Exception e) { fixture.Fail(e); }
        return fixture.Poll();
    }
    public static string Poll(string id) => (AppDomain.CurrentDomain.GetData(Key(id)) as Func<string>)?.Invoke()
        ?? TestContext.Result("failed", 0, "Missing search fixture").ToJson();
    public static string Cleanup(string id) => TestContext.Run("Release search fixture", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id), null); AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Only owned window and FX released");
    });

    sealed class Fixture : IDisposable
    {
        readonly TestContext t = new TestContext();
        EditorWindow window, oldFocus;
        ShaderFX fx;
        TextField code, readOnly, outside;
        ToolbarSearchField query;
        Toolbar native;
        IVisualElementScheduledItem schedule;
        Capture capture;
        string result, source;
        int stage, changes;
        Rect inputBeforeSearch;
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, F).Invoke(target, args);
        static TextField Create(ShaderFX effect) => (TextField)Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderCodeField", true), F, null, new object[] { effect }, null);
        void Source(string value) { Call(fx, "SetDraftCode", value); Call(code, "SyncFromModel"); code.Focus(); code.textSelection.SelectRange(0, 0); }
        void KeyDown(TextField field, KeyCode key, EventModifiers mods = EventModifiers.None, char character = '\0')
        {
            var target = (VisualElement)field.textEdition;
            using var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = mods, character = character });
            e.target = target; target.SendEvent(e);
        }
        void Selection(int start, int length)
        {
            object search = code.GetType().GetField("search", F).GetValue(code);
            var matches = (List<int>)search.GetType().GetField("matches", F).GetValue(search);
            int current = (int)search.GetType().GetField("current", F).GetValue(search);
            t.Equal(start, matches[current], "Current match start");
            t.Equal(length, query.value.Length, "Match length");
        }
        string Count(TextField field) => field.Q<Label>("codeSearchCount").text;
        TextField Input => query.Q<TextField>();
        void Query(string text) { query.value = text; }
        public void Begin()
        {
            oldFocus = EditorWindow.focusedWindow;
            fx = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>) }, null)
                .Invoke(null, new object[] { null, "", new List<ShaderParameter>() });
            fx.hideFlags = HideFlags.HideAndDontSave;
            code = Create(fx); readOnly = Create(null); readOnly.style.height = 90;
            outside = new TextField();
            window = ScriptableObject.CreateInstance<ShaderCodeSearchTestWindow>(); window.titleContent = new GUIContent("Code search check");
            typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { window.rootVisualElement });
            window.rootVisualElement.Add(code); window.rootVisualElement.Add(readOnly); window.rootVisualElement.Add(outside);
            native = new Toolbar(); native.Add(new ToolbarSearchField()); native.Add(new ToolbarButton { text = "↑" }); window.rootVisualElement.Add(native);
            capture = new Capture(window); window.rootVisualElement.Add(capture);
            window.ShowUtility(); window.position = new Rect(220, 160, 450, 500); window.Focus();
            source = "float alpha;\nALPHA + beta + alpha;\n// literal .* and мир";
            Source(source); changes = 0; code.RegisterValueChangedCallback(_ => changes++);
            schedule = window.rootVisualElement.schedule.Execute(Step).Every(150).StartingIn(500);
        }
        public string Poll() => result ?? TestContext.Result("running", t.Checks, "Search stage " + stage).ToJson();
        public void Fail(Exception e) { result = TestContext.Result("failed", t.Checks, "Search fixture", e.ToString()).ToJson(); Dispose(); }
        void Step()
        {
            try
            {
                if (capture.error != null) throw capture.error;
                if (stage == 0)
                {
                    var text = (TextElement)code.textEdition;
                    var font = text.resolvedStyle.unityFontDefinition.fontAsset;
                    t.True(font != null, "Code uses an explicit bundled TextCore font");
                    t.Equal("Cascadia Mono", font.faceInfo.familyName, "Code font family");
                    t.Equal("Regular", font.faceInfo.styleName, "Code uses Regular");
                    t.Equal(AtlasPopulationMode.Dynamic, font.atlasPopulationMode, "Bundled font does not depend on the OS font catalog");
                    t.True(font.sourceFontFile != null && AssetDatabase.GetAssetPath(font.sourceFontFile).EndsWith("/CascadiaMono-Regular.ttf"), "Regular retains its bundled source");
                    var bold = font.fontWeightTable[7].regularTypeface;
                    t.True(bold != null && bold.sourceFontFile != null, "Bold weight has its own bundled font file");
                    t.Equal("Bold", bold.faceInfo.styleName, "Bold resolves to the real typeface");
                    t.Equal(font, ((TextElement)readOnly.textEdition).resolvedStyle.unityFontDefinition.fontAsset, "Readonly code uses the same bundled font");
                    t.True(font != ((TextElement)outside.textEdition).resolvedStyle.unityFontDefinition.fontAsset, "Ordinary fields retain their native font");
                    float advance = code.textSelection.GetCursorPositionFromStringIndex(1).x - code.textSelection.GetCursorPositionFromStringIndex(0).x;
                    t.True(advance > 0, "Native caret has a positive monospace advance");
                    for (int i = 0; i < source.Length; i++)
                    {
                        if (source[i] == '\n') continue;
                        Vector2 a = code.textSelection.GetCursorPositionFromStringIndex(i), b = code.textSelection.GetCursorPositionFromStringIndex(i + 1);
                        t.Near(advance, b.x - a.x, .02, "Native caret follows equal character cells, including Cyrillic, at " + i);
                    }
                    outside.Focus(); KeyDown(outside, KeyCode.F, EventModifiers.Control);
                    t.True(code.Q("codeSearch") == null, "Ctrl+F outside code does not open code search");
                    inputBeforeSearch = ((VisualElement)code.textEdition).GetFirstAncestorOfType<ScrollView>().worldBound;
                    code.Focus(); code.textSelection.SelectRange(6, 11); KeyDown(code, KeyCode.F, EventModifiers.Control);
                    query = code.Q<ToolbarSearchField>("codeSearchQuery");
                    t.True(query != null, "Ctrl+F opens local search"); t.Equal("alpha", query.value, "Selected code seeds query");
                    t.Equal("1 / 3", Count(code), "Ordinal case-insensitive match count");
                }
                else if (stage == 1)
                {
                    t.Equal(((TextElement)native.Q<ToolbarSearchField>().Q<TextField>().textEdition).resolvedStyle.unityFontDefinition,
                        ((TextElement)Input.textEdition).resolvedStyle.unityFontDefinition, "Search input keeps the native toolbar font");
                    Selection(6, 5);
                    var search = code.GetType().GetField("search", F).GetValue(code);
                    t.True((bool)search.GetType().GetField("queryFocused", F).GetValue(search), "Search input receives local FocusIn, despite composite-root retargeting");
                    KeyDown(Input, KeyCode.Return, character: '\n'); t.Equal("2 / 3", Count(code), "Next counter"); Selection(13, 5);
                    KeyDown(Input, KeyCode.Return, EventModifiers.Shift, '\n'); Selection(6, 5);
                    KeyDown(Input, KeyCode.Return, EventModifiers.Shift, '\n'); t.Equal("3 / 3", Count(code), "Wrap counter"); Selection(28, 5);
                    Query(".*"); t.Equal("1 / 1", Count(code), "Query is literal, not a regex");
                    Query("МИР"); t.Equal("1 / 1", Count(code), "Unicode query");
                    Query("not present"); t.Equal("0 / 0", Count(code), "Empty count"); t.True(!code.Q("codeSearchNext").enabledSelf, "No-result navigation disabled");
                    KeyDown(Input, KeyCode.Return, character: '\n'); Query(""); t.Equal("0 / 0", Count(code), "Empty count");
                    t.Equal(source, code.value, "Query/Enter never changes source"); t.Equal(0, changes, "Query does not bubble a code ChangeEvent");
                    KeyDown(Input, KeyCode.Escape); t.Equal(DisplayStyle.None, code.Q("codeSearchOverlay").style.display.value, "Esc closes search");
                    code.textSelection.SelectRange(0, 0); KeyDown(code, KeyCode.Return, character: '\n');
                    t.Equal("\n" + source, code.value, "Code editing still inserts newline");
                    KeyDown(code, KeyCode.F, EventModifiers.Control); Query("abc");
                    KeyDown(Input, KeyCode.Z, EventModifiers.Control);
                    t.Equal("\n" + source, code.value, "Query Ctrl+Z does not undo code edit");
                    KeyDown(Input, KeyCode.Escape); Undo.PerformUndo(); t.Equal(source, code.value, "One code Undo excludes query edits");
                    Undo.PerformRedo(); t.Equal("\n" + source, code.value, "Code Redo intact");
                    source = string.Concat(Enumerable.Repeat("float padding;\n", 200)) + new string('M', 200) + "needle";
                    Source(source); KeyDown(code, KeyCode.F, EventModifiers.Control); Query("needle");
                }
                else if (stage == 2)
                {
                    var text = (TextElement)code.textEdition; var scroll = text.GetFirstAncestorOfType<ScrollView>();
                    t.True(scroll.scrollOffset.y > 100 && scroll.scrollOffset.x > 100, "Match revealed on both scroll axes");
                    var p = scroll.contentViewport.WorldToLocal(text.LocalToWorld(text.contentRect.position + code.textSelection.GetCursorPositionFromStringIndex(source.IndexOf("needle", StringComparison.Ordinal))));
                    t.True(p.y >= 0 && p.y <= scroll.contentViewport.contentRect.height + 1, "Matching line visible");
                    t.Near(inputBeforeSearch.width, scroll.worldBound.width, .2, "Search does not resize code width");
                    t.Near(inputBeforeSearch.height, scroll.worldBound.height, .2, "Search does not resize code height");
                    var searchBar = code.Q("codeSearch");
                    t.Near(360, searchBar.worldBound.width, .2, "Search has maximum width");
                    t.Near(code.worldBound.xMax - 4, searchBar.worldBound.xMax, .2, "Search anchored at right inset");
                    t.Near(code.worldBound.yMin + 4, searchBar.worldBound.yMin, .2, "Search anchored at top inset");
                    t.Near(native.resolvedStyle.height, code.Q("codeSearch").resolvedStyle.height, .2, "Search uses native toolbar height");
                    t.Near(native.Q<ToolbarButton>().resolvedStyle.height, code.Q("codeSearchNext").resolvedStyle.height, .2, "Search uses native button height");
                    var highlight = code.Q("codeSearchMatch");
                    var search = code.GetType().GetField("search", F).GetValue(code);
                    var matchRect = (Rect)Call(search, "MatchRect", highlight);
                    var property = new CustomStyleProperty<Color>("--unity-selection-color");
                    bool hasColor = text.customStyle.TryGetValue(property, out Color color) || code.customStyle.TryGetValue(property, out color);
                    Directory.CreateDirectory(Path.GetFullPath("Temp/WhimTex/code-search"));
                    File.WriteAllText(Path.GetFullPath("Temp/WhimTex/code-search/geometry.txt"), "highlight=" + highlight.worldBound + "; rect=" + matchRect + "; content=" + highlight.contentRect + "; color=" + color + "; hasColor=" + hasColor);
                    t.True(hasColor && color.a > 0, "Visible native selection color is available to search highlight");
                    t.True(highlight.contentRect.Overlaps(matchRect), "Current match intersects highlight viewport");
                    t.True(code.Q("shaderFXCurrentLine").customStyle.TryGetValue(new CustomStyleProperty<Color>("--whimtex-code-current-line-color"), out Color lineColor), "Caret line color is defined");
                    t.Near(1, lineColor.r, .001, "Line tint is white"); t.True(lineColor.a > 0 && lineColor.a < .08, "Line tint stays very faint");
                    t.True(color.b > color.r && color.a > lineColor.a, "Matched word uses a stronger native blue");
                    t.Equal(source.IndexOf("needle", StringComparison.Ordinal), code.textSelection.cursorIndex, "Search sets actual code selection start");
                    t.Equal(source.Length, code.textSelection.selectIndex, "Search selects complete matching text while query keeps focus");
                    t.True((bool)code.Q("shaderFXLineNumbers").GetType().GetProperty("HasActiveLine", F).GetValue(code.Q("shaderFXLineNumbers")), "Caret row follows selected search result");
                    var activeNumber = code.Q<Label>("shaderFXActiveLineNumber");
                    t.Equal("201", activeNumber.text, "Active line number follows native selection");
                    t.Equal(FontStyle.Bold, activeNumber.resolvedStyle.unityFontStyleAndWeight, "Active number uses native bold font style");
                    capture.path = Path.GetFullPath("Temp/WhimTex/code-search/wide.png"); capture.MarkDirtyRepaint();
                }
                else if (stage == 3)
                {
                    if (capture.path != null) return;
                    window.position = new Rect(220, 160, 240, 500);
                    readOnly.SetValueWithoutNotify("readable readable"); readOnly.Focus(); KeyDown(readOnly, KeyCode.F, EventModifiers.Command);
                    readOnly.Q<ToolbarSearchField>().value = "readable";
                    t.Equal("1 / 2", Count(readOnly), "Read-only code supports Command+F");
                    t.Equal("needle", query.value, "Independent queries in two code areas");
                }
                else if (stage == 4)
                {
                    var bar = code.Q("codeSearch");
                    foreach (var child in bar.Children()) t.True(child.worldBound.xMin >= bar.worldBound.xMin - .2 && child.worldBound.xMax <= bar.worldBound.xMax + .2, "Narrow toolbar child not clipped: " + child.name);
                    t.True(query.worldBound.width >= 50, "Narrow search remains usable");
                    code.SetValueWithoutNotify(source + " needle"); t.Equal("1 / 2", Count(code), "Silent external/model updates refresh matches");
                    code.RemoveFromHierarchy(); window.rootVisualElement.Insert(0, code);
                    code.Focus(); KeyDown(code, KeyCode.F, EventModifiers.Control); Query("needle");
                    capture.path = Path.GetFullPath("Temp/WhimTex/code-search/narrow.png"); capture.MarkDirtyRepaint();
                }
                else if (stage == 5)
                {
                    if (capture.path != null) return;
                    t.True(code.Q("shaderFXLineNumbers") != null, "Gutter reattaches with search UI present");
                    KeyDown(Input, KeyCode.Escape); t.True(code == code.panel.focusController.focusedElement || code.Contains(code.panel.focusController.focusedElement as VisualElement), "Esc returns focus to code");
                }
                else if (stage == 6)
                {
                    t.Equal(source.IndexOf("needle", StringComparison.Ordinal), Math.Min(code.textSelection.cursorIndex, code.textSelection.selectIndex), "Closing search restores caret at result after native focus dispatch");
                    t.Equal(6, Math.Abs(code.textSelection.cursorIndex - code.textSelection.selectIndex), "Closing search selects matching text for editing");
                    window.position = new Rect(220, 160, 450, 500);
                    Source(string.Concat(Enumerable.Repeat("  a  b\n", 2000)) + string.Concat(Enumerable.Repeat("x  ", 2000)));
                    KeyDown(code, KeyCode.F, EventModifiers.Control); Query("  ");
                    t.Equal("1 / 6000", Count(code), "All matches counted across long files and long lines");
                }
                else if (stage == 7)
                {
                    capture.inspect = texture =>
                    {
                        Color active = MatchPixel(texture, 0), sameLine = MatchPixel(texture, 1), nextLine = MatchPixel(texture, 2);
                        t.True(active.b - active.r > sameLine.b - sameLine.r + .04f, "Current match visibly stronger than another match on its line");
                        t.True(sameLine.b - sameLine.r > .05f && nextLine.b - nextLine.r > .05f, "Other visible matches on both lines are blue");
                    };
                    capture.path = Path.GetFullPath("Temp/WhimTex/code-search/all-matches.png"); capture.MarkDirtyRepaint();
                }
                else if (stage == 8)
                {
                    if (capture.path != null) return;
                    var scroll = ((TextElement)code.textEdition).GetFirstAncestorOfType<ScrollView>();
                    scroll.verticalScroller.value = scroll.verticalScroller.highValue;
                    scroll.horizontalScroller.value = scroll.horizontalScroller.highValue;
                }
                else if (stage == 9)
                {
                    capture.inspect = texture =>
                    {
                        Color last = MatchPixel(texture, 5998);
                        t.True(last.b - last.r > .05f, "Horizontal and vertical scrolling reveal distant matches in a 2000-match line");
                    };
                    capture.path = Path.GetFullPath("Temp/WhimTex/code-search/scrolled-matches.png"); capture.MarkDirtyRepaint();
                }
                else if (stage == 10)
                {
                    if (capture.path != null) return;
                    Dispose(); result = TestContext.Result("passed", t.Checks, "Local search, events, Undo, scrolling, readonly and native narrow layout").ToJson(); return;
                }
                stage++;
            }
            catch (Exception e) { Fail(e); }
        }
        Color MatchPixel(Texture2D texture, int index)
        {
            object search = code.GetType().GetField("search", F).GetValue(code);
            var highlight = code.Q("codeSearchMatch");
            float height = (float)Call(search, "LineHeight");
            var rect = (Rect)Call(search, "GetMatchRect", index, highlight, height);
            t.True(highlight.contentRect.Contains(rect.center), "Sample is inside the visible code viewport");
            Vector2 point = window.rootVisualElement.WorldToLocal(highlight.LocalToWorld(rect.center)) * EditorGUIUtility.pixelsPerPoint;
            return texture.GetPixel(Mathf.FloorToInt(point.x), texture.height - 1 - Mathf.FloorToInt(point.y));
        }
        public void Dispose()
        {
            schedule?.Pause(); schedule = null;
            if (window != null) { window.Close(); window = null; }
            if (fx != null) { Undo.ClearUndo(fx); UnityEngine.Object.DestroyImmediate(fx); fx = null; }
            if (oldFocus != null) { oldFocus.Focus(); oldFocus = null; }
        }
    }
    sealed class Capture : ImmediateModeElement
    {
        readonly EditorWindow owner;
        internal string path;
        internal Action<Texture2D> inspect;
        internal Exception error;
        internal Capture(EditorWindow owner) { this.owner = owner; pickingMode = PickingMode.Ignore; style.position = Position.Absolute; style.width = 1; style.height = 1; }
        protected override void ImmediateRepaint()
        {
            if (path == null) return;
            Texture2D texture = null;
            try
            {
                var rt = RenderTexture.active;
                int w = rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int h = rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                texture = new Texture2D(w, h, TextureFormat.RGBA32, false); texture.ReadPixels(new Rect(0, 0, w, h), 0, 0, false); texture.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, texture.EncodeToPNG()); inspect?.Invoke(texture); inspect = null; path = null;
            }
            catch (Exception e) { error = e; path = null; }
            finally { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
