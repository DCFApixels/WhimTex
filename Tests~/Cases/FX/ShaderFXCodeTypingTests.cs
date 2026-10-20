using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXCodeTypingWindow : EditorWindow { }

public static class ShaderFXCodeTypingTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int Warmups = 8, Samples = 32;
    static string Key(string id) => "WhimTex.Tests.CodeTyping." + Guid.Parse(id).ToString("D");
    [Serializable] public sealed class Row
    {
        public string mode, generator;
        public int lines, characters, callbacks, glyphs, synchronousCallbacks;
        public List<double> inputMs = new List<double>(), coloringMs = new List<double>();
    }
    [Serializable] public sealed class Report
    {
        public string unity, note = "Native public KeyDown dispatch on the real textEdition element, alternating one-character selection replacement, one edit per panel update. Input includes draft/Undo/change handlers, not deferred layout/rendering. Coloring is the entire PostProcessTextVertices callback including tokenization. No GPU, frame-rate or whole-panel attribution. Fixed 640x480 utility window; 8 warmups + 32 edits per row; all rows repeated in reverse order. User capture/baseline retained.";
        public List<Row> rows = new List<Row>();
    }
    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null) return TestContext.Result("failed", 0, "Duplicate typing diagnostic").ToJson();
        var fixture = new Fixture(id);
        AppDomain.CurrentDomain.SetData(Key(id), (Func<string>)fixture.Poll);
        AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", (Action)fixture.Dispose);
        try { fixture.Begin(); } catch (Exception e) { fixture.Fail(e); }
        return fixture.Poll();
    }
    public static string Poll(string id) => (AppDomain.CurrentDomain.GetData(Key(id)) as Func<string>)?.Invoke()
        ?? TestContext.Result("failed", 0, "Missing typing fixture").ToJson();
    public static string Cleanup(string id) => TestContext.Run("Release typing diagnostic", t =>
    {
        (AppDomain.CurrentDomain.GetData(Key(id) + ".cleanup") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(id), null); AppDomain.CurrentDomain.SetData(Key(id) + ".cleanup", null);
        t.True(true, "Only owned window, FX and Undo released");
    });
    sealed class Fixture : IDisposable
    {
        readonly string id;
        readonly Report report = new Report { unity = Application.unityVersion };
        EditorWindow window, previous;
        ShaderFX effect;
        TextField field;
        TextElement text;
        IVisualElementScheduledItem schedule;
        readonly List<(string mode, int lines)> cases = new List<(string, int)>();
        int index = -1, edits, waits;
        Row row;
        string source, result;
        int caret;
        Action<TextElement.GlyphsEnumerable> production;
        bool measuring, sawGlyphs, dispatching;
        public Fixture(string id) { this.id = id; }
        TextField CreateField() => (TextField)Activator.CreateInstance(typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXCodeField", true), F, null, new object[] { effect }, null);
        public void Begin()
        {
            previous = EditorWindow.focusedWindow;
            effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, "", new List<ShaderFXParameter>() });
            effect.hideFlags = HideFlags.HideAndDontSave;
            field = CreateField();
            text = (TextElement)field.textEdition;
            production = text.PostProcessTextVertices;
            var modes = production == null ? new[] { "original" } : new[] { "original", "highlighted" };
            foreach (int lines in new[] { 100, 1000 }) foreach (string mode in modes) cases.Add((mode, lines));
            int count = cases.Count;
            for (int i = count - 1; i >= 0; i--) cases.Add(cases[i]);
            window = ScriptableObject.CreateInstance<ShaderFXCodeTypingWindow>();
            window.titleContent = new GUIContent("FX Typing Diagnostic");
            typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            window.rootVisualElement.Add(field);
            window.ShowUtility(); window.position = new Rect(100, 100, 640, 480);
            schedule = window.rootVisualElement.schedule.Execute(Step).Every(16).StartingIn(300);
        }
        public string Poll() => result ?? TestContext.Result("running", 0, "Typing diagnostic row " + index).ToJson();
        public void Fail(Exception e) { result = TestContext.Result("failed", 0, "Typing diagnostic", e.ToString()).ToJson(); Dispose(); }
        void Step()
        {
            try
            {
                if (row == null || edits >= Warmups + Samples)
                {
                    if (row != null && !sawGlyphs) throw new InvalidOperationException("No rendered glyph callback for " + row.mode);
                    if (++index == cases.Count)
                    {
                        string directory = Path.GetFullPath("Temp/WhimTex/code-typing"); Directory.CreateDirectory(directory);
                        Type json = null;
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                            if (assembly.GetName().Name == "Newtonsoft.Json") { json = assembly.GetType("Newtonsoft.Json.JsonConvert", true); break; }
                        string serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
                        string path = Path.Combine(directory, id + ".json"); File.WriteAllText(path, serialized);
                        result = TestContext.Result("passed", report.rows.Count * Samples, "Typing measurements: " + path).ToJson(); Dispose(); return;
                    }
                    (string mode, int lines) = cases[index];
                    var buffer = new StringBuilder("// @param _Amount float [0 .. 1] = 0.5\nfloat4 ApplyFX(PinnedUV uv, float4 color)\n{\n    float probe = 0;\n");
                    for (int i = 4; i < lines - 2; i++) buffer.Append("    color.rgb = lerp(color.rgb, saturate(color.rgb * 1.01), _Amount); // sample\n");
                    buffer.Append("    return color;\n}\n"); source = buffer.ToString(); caret = source.IndexOf("probe", StringComparison.Ordinal);
                    typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { source });
                    text.PostProcessTextVertices = production;
                    field.RemoveFromHierarchy();
                    field = CreateField(); text = (TextElement)field.textEdition; production = text.PostProcessTextVertices;
                    window.rootVisualElement.Add(field);
                    Undo.ClearUndo(effect);
                    text.PostProcessTextVertices = glyphs =>
                    {
                        long start = Stopwatch.GetTimestamp();
                        if (mode == "highlighted") production(glyphs);
                        double elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                        sawGlyphs = true;
                        if (measuring) { row.callbacks++; if (dispatching) row.synchronousCallbacks++; row.glyphs = glyphs.Count; row.coloringMs.Add(elapsed); }
                    };
                    row = new Row { mode = mode, lines = lines, characters = source.Length };
                    report.rows.Add(row); edits = 0; waits = 4; measuring = sawGlyphs = false;
                    field.Focus(); field.textSelection.SelectRange(caret, caret);
                    window.Repaint(); return;
                }
                if (waits-- > 0) return;
                measuring = edits >= Warmups;
                bool insert = edits % 2 == 0;
                string expected = insert ? source.Remove(caret, 1).Insert(caret, "x") : source;
                field.Focus(); field.textSelection.SelectRange(caret, caret + 1);
                var target = (VisualElement)field.textEdition;
                long start = Stopwatch.GetTimestamp();
                dispatching = true;
                using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.None, character = insert ? 'x' : 'p' }))
                { evt.target = target; target.SendEvent(evt); }
                dispatching = false;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                if (field.value != expected || (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(effect) != expected)
                    throw new InvalidOperationException("Native typing did not update exact draft text, mode=" + row.mode + ", edit=" + edits + ", caret=" + field.textSelection.cursorIndex + ", expected length=" + expected.Length + ", actual length=" + field.value.Length + ", actual prefix=" + field.value.Substring(0, Math.Min(130, field.value.Length)));
                if (field.textSelection.cursorIndex != caret + 1) throw new InvalidOperationException("Native caret moved incorrectly");
                row.generator = text.resolvedStyle.unityTextGenerator.ToString();
                if (measuring) row.inputMs.Add(ms);
                edits++; window.Repaint();
            }
            catch (Exception e) { Fail(e); }
        }
        public void Dispose()
        {
            schedule?.Pause(); schedule = null;
            if (text != null) text.PostProcessTextVertices = production;
            if (window != null) { window.Close(); window = null; }
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); effect = null; }
            if (previous != null) previous.Focus(); previous = null;
        }
    }
}
