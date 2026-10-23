using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using WhimTex.Tests;

public sealed class ShaderFXCodeReferenceWindow : EditorWindow { }

public static class ShaderFXCodeReferenceTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Source = "// float4 ApplyFX(float4 ignored) { }\nfloat4 ApplyFX(float2 coords, float4 tint)\n{\n    return tint;\n}\n";
    static string Key(string id) => "WhimTex.Tests.CodeReference." + Guid.Parse(id).ToString("D");

    public static string Start(string runId)
    {
        if (AppDomain.CurrentDomain.GetData(Key(runId)) != null)
            return TestContext.Result("failed", 0, "Duplicate code reference test").ToJson();
        var state = new object[6];
        AppDomain.CurrentDomain.SetData(Key(runId), state);
        try
        {
            state[2] = EditorWindow.focusedWindow;
            var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, Source, new List<ShaderFXParameter>() });
            effect.hideFlags = HideFlags.HideAndDontSave;
            state[1] = effect;
            var window = ScriptableObject.CreateInstance<ShaderFXCodeReferenceWindow>();
            state[0] = window;
            window.titleContent = new GUIContent("FX Code Reference Test");
            var assembly = typeof(ShaderFX).Assembly;
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true).GetMethod("ApplyWindowStyles", F)
                .Invoke(null, new object[] { window.rootVisualElement });
            var data = new SerializedObject(effect);
            state[5] = data;
            var editor = assembly.GetType("DCFApixels.WhimTex.ShaderFXEditor", true);
            var view = (VisualElement)editor.GetMethod("BuildView", F).Invoke(null, new object[] { effect, data, null, null, -1 });
            view.Q<Foldout>("shaderFXCodeFoldout").value = true;
            view.Q<Foldout>("shaderFXInputs").value = true;
            var scroll = new ScrollView();
            scroll.Add(view);
            scroll.Add(new Button { name = "otherFocus", text = "Other control" });
            window.rootVisualElement.Add(scroll);
            window.ShowUtility();
            window.position = new Rect(100, 100, 640, 900);
            state[4] = window.rootVisualElement.schedule.Execute(() => Check(state)).StartingIn(300);
            return Poll(runId);
        }
        catch (Exception error)
        {
            state[3] = TestContext.Result("failed", 0, "Code reference setup", error.ToString()).ToJson();
            Release(state);
            return (string)state[3];
        }
    }

    static void Check(object[] state)
    {
        try
        {
            state[3] = TestContext.Run("Shader Inputs insertion, signature replacement, focus and Undo", t =>
            {
                var root = ((EditorWindow)state[0]).rootVisualElement;
                var code = root.Q<TextField>("shaderFXCode");
                var effect = (ShaderFX)state[1];
                string Current() => (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(effect);
                bool CodeFocused() => root.panel.focusController.focusedElement is VisualElement focused &&
                    (ReferenceEquals(focused, code) || code.Contains(focused));
                void SetCode(string source)
                {
                    typeof(ShaderFX).GetMethod("SetDraftCode", F).Invoke(effect, new object[] { source });
                    code.GetType().GetMethod("SyncFromModel", F).Invoke(code, null);
                    code.Focus();
                }
                void Key(KeyCode keyCode, char character, EventModifiers modifiers = EventModifiers.None)
                {
                    var target = code.textEdition as VisualElement ?? code;
                    using (var key = KeyDownEvent.GetPooled(new Event
                    {
                        type = EventType.KeyDown, keyCode = keyCode, character = character, modifiers = modifiers
                    }))
                    { key.target = target; target.SendEvent(key); }
                }
                void Tab(bool shift = false) => Key(KeyCode.Tab, '\t', shift ? EventModifiers.Shift : EventModifiers.None);
                void CheckEdit(string before, int cursor, int select, string after, int afterCursor, int afterSelect,
                    KeyCode keyCode, char character, EventModifiers modifiers = EventModifiers.None)
                {
                    SetCode(before); code.textSelection.SelectRange(cursor, select);
                    Key(keyCode, character, modifiers);
                    string label = keyCode + " in " + before.Replace("\n", "\\n").Replace("\t", "\\t");
                    t.Equal(after, Current(), label);
                    t.Equal(after, code.value, "Field and draft agree after indentation");
                    t.True(CodeFocused(), "Key edits code instead of navigating away; focused=" + code.panel.focusController.focusedElement);
                    t.Equal(afterCursor, code.textSelection.cursorIndex, "Indentation maps caret");
                    t.Equal(afterSelect, code.textSelection.selectIndex, "Indentation maps selection");
                    Undo.PerformUndo();
                    t.Equal(before, Current(), "Indentation is one Undo step");
                    t.Equal(cursor, code.textSelection.cursorIndex, "Indentation Undo restores caret");
                    t.Equal(select, code.textSelection.selectIndex, "Indentation Undo restores selection");
                    Undo.PerformRedo();
                    t.Equal(after, Current(), "Indentation Redo restores code");
                    t.Equal(afterCursor, code.textSelection.cursorIndex, "Indentation Redo restores caret");
                    t.Equal(afterSelect, code.textSelection.selectIndex, "Indentation Redo restores selection");
                }
                void CheckIndent(string before, int cursor, int select, string after, int afterCursor, int afterSelect, bool shift = false) =>
                    CheckEdit(before, cursor, select, after, afterCursor, afterSelect, KeyCode.Tab, '\t', shift ? EventModifiers.Shift : EventModifiers.None);
                void CheckTyping(string before, string after, char character = '\n', EventModifiers modifiers = EventModifiers.None)
                {
                    int cursor = before.IndexOf('|'), afterCursor = after.IndexOf('|');
                    t.True(cursor >= 0 && afterCursor >= 0, "Typing fixture defines both carets");
                    CheckEdit(before.Replace("|", ""), cursor, cursor, after.Replace("|", ""), afterCursor, afterCursor,
                        character == '\n' ? KeyCode.Return : KeyCode.RightBracket, character, modifiers);
                }
                bool Insert(string snippet) => (bool)code.GetType().GetMethod("UseReference", F).Invoke(code, new object[] { snippet });
                string nativeTrace = "";
                void Click(string snippet, int clicks = 2, bool nativeHitTest = false)
                {
                    Label label = null;
                    string id = null;
                    foreach (var candidate in root.Query<Label>(className: "whimtex-fx-reference-text").ToList())
                        foreach (Match link in Regex.Matches(candidate.text, "<link=\"([^\"]+)\">(.*?)</link>"))
                            if (link.Groups[2].Value.Contains("<b>" + snippet + "</b>"))
                            { label = candidate; id = link.Groups[1].Value; break; }
                    t.True(label != null && id != null, "Public rich-text link exists: " + snippet);
                    var position = new Vector2(label.worldBound.xMin + 8, label.worldBound.yMin + 7);
                    if (nativeHitTest)
                    {
                        nativeTrace = "label=" + label.worldBound + "; root=" + root.worldBound + "; picked=" + root.panel.Pick(position)?.GetType().Name;
                        t.True(label.worldBound.height > 12 && label.worldBound.Contains(position), "Native link has a real hit rectangle: " + nativeTrace);
                        label.RegisterCallback<PointerDownLinkTagEvent>(e => nativeTrace += "; downLink=" + e.linkID);
                        label.RegisterCallback<PointerUpLinkTagEvent>(e => nativeTrace += "; upLink=" + e.linkID);
                        label.RegisterCallback<ClickEvent>(e => nativeTrace += "; click=" + e.clickCount);
                        for (int count = 1; count <= clicks; count++)
                        {
                            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = count, mousePosition = position }))
                            { down.target = label; label.SendEvent(down); }
                            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = 1, mousePosition = position }))
                            { up.target = label; label.SendEvent(up); }
                        }
                        return;
                    }
                    for (int count = 1; count <= clicks; count++)
                    {
                        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = count, mousePosition = position }))
                        {
                            down.target = label; label.SendEvent(down);
                            using (var link = PointerDownLinkTagEvent.GetPooled(down, id, snippet))
                            { link.target = label; label.SendEvent(link); }
                        }
                        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = 1, mousePosition = position }))
                        using (var link = PointerUpLinkTagEvent.GetPooled(up, id, snippet))
                        { link.target = label; label.SendEvent(link); }
                        using (var click = ClickEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, clickCount = count, mousePosition = position }))
                        { click.target = label; label.SendEvent(click); }
                    }
                }

                int at = Source.IndexOf("return", StringComparison.Ordinal);
                code.Focus(); code.textSelection.SelectRange(at, at);
                t.True(CodeFocused(), "Code initially owns focus");
                Click("_InputSize", 1);
                t.Equal(Source, Current(), "Single click does not edit code");
                t.True(CodeFocused(), "Single click does not steal code focus");
                Click("_InputSize");
                t.Equal(Source.Insert(at, "_InputSize"), Current(), "Double click inserts identifier at caret");
                t.Equal(at + "_InputSize".Length, code.textSelection.cursorIndex, "Insertion advances caret");
                t.True(CodeFocused(), "Double click keeps focus on code");
                Undo.PerformUndo();
                t.Equal(Source, Current(), "Insertion is one Undo step");
                t.Equal(at, code.textSelection.cursorIndex, "Undo restores caret");
                Undo.PerformRedo();
                t.Equal(Source.Insert(at, "_InputSize"), Current(), "Redo restores inserted identifier");
                t.Equal(at + "_InputSize".Length, code.textSelection.cursorIndex, "Redo restores caret");

                foreach (string snippet in new[] { "SampleInput(uv)", "SampleTexture(_Mask, uv)", "SampleTexture(_MainTex, uv)" })
                {
                    SetCode(Source);
                    code.textSelection.SelectRange(at + 6, at);
                    Click(snippet);
                    t.Equal(Source.Remove(at, 6).Insert(at, snippet), Current(), "Function snippet replaces saved selection: " + snippet);
                    t.True(CodeFocused(), "Function insertion retains code focus");
                    Undo.PerformUndo();
                    t.Equal(Source, Current(), "Function insertion Undo");
                    t.Equal(at + 6, code.textSelection.cursorIndex, "Undo retains selection direction");
                    t.Equal(at, code.textSelection.selectIndex, "Undo restores selection extent");
                }
                SetCode(Source); code.textSelection.SelectRange(at, at);
                root.Q<Button>("otherFocus").Focus();
                Click("_CanvasSize");
                t.Equal(Source.Insert(at, "_CanvasSize"), Current(), "Last caret survives focus in another control");
                t.True(CodeFocused(), "Insertion returns focus to code");

                SetCode(Source); code.textSelection.SelectRange(at, at);
                Click("float4 ApplyFX(PinnedUV uv, float4 color)", nativeHitTest: true);
                string pinned = Source.Replace("float2 coords", "PinnedUV coords");
                t.Equal(pinned, Current(), "Only definition header changes, preserving argument names, body and comment: " + nativeTrace);
                t.Equal(at + "PinnedUV".Length - "float2".Length, code.textSelection.cursorIndex, "Header edit maps body caret");
                Undo.PerformUndo(); t.Equal(Source, Current(), "Signature Undo");
                Undo.PerformRedo(); t.Equal(pinned, Current(), "Signature Redo");
                Click("float4 ApplyFX(float4 color)");
                t.Equal(Source.Replace("float2 coords, ", ""), Current(), "Color-only header retains color argument name");
                Click("float4 ApplyFX(PinnedUV uv, float4 color)");
                t.True(Current().Contains("float4 ApplyFX(PinnedUV uv, float4 tint)"), "Missing UV name uses default without renaming color");
                t.True(Current().EndsWith("{\n    return tint;\n}\n", StringComparison.Ordinal), "Body remains untouched after switches");

                SetCode("// empty draft\n"); code.textSelection.SelectRange(15, 15);
                t.True(Insert("float4 ApplyFX(float4 color)"), "No definition permits insertion");
                t.Equal("// empty draft\nfloat4 ApplyFX(float4 color)", Current(), "Missing declaration inserts at saved caret");
                SetCode(Source + "float4 ApplyFX(float4 other) { return other; }");
                string ambiguous = Current();
                t.True(!Insert("float4 ApplyFX(float4 color)"), "Multiple definitions are not replaced arbitrarily");
                t.Equal(ambiguous, Current(), "Ambiguous draft stays unchanged");
                code.isReadOnly = true;
                t.True(!Insert("_InputSize"), "Read-only field rejects insertion");
                code.isReadOnly = false; code.SetEnabled(false);
                t.True(!Insert("_InputSize"), "Disabled field rejects insertion");
                t.True(!(bool)typeof(ShaderFX).GetProperty("HasAppliedShader", F).GetValue(effect), "Reference edits never compile");
                code.SetEnabled(true); SetCode(Source);
                var first = root.Query<Label>(className: "whimtex-fx-reference-text").ToList()[0];
                string beforeHover = first.text;
                var position = new Vector2(first.worldBound.xMin + 8, first.worldBound.yMin + 7);
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position }))
                { move.target = first; first.SendEvent(move); }
                t.True(first.ClassListContains("whimtex-fx-reference-text--link-hover"), "Native link hover enables hand cursor class");
                t.True(first.text != beforeHover && first.text.Contains(EditorGUIUtility.isProSkin ? "#FFFFFF30" : "#00000020"), "Only hovered code receives stronger background");
                t.Equal(Regex.Replace(beforeHover, "<[^>]*>", ""), Regex.Replace(first.text, "<[^>]*>", ""), "Hover does not change visible text or insert spacing");
                string hoveredText = first.text;
                using (var move = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position }))
                { move.target = first; first.SendEvent(move); }
                t.True(ReferenceEquals(hoveredText, first.text), "Moving inside one link does not rebuild its text");
                using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = first; first.SendEvent(leave); }
                t.Equal(beforeHover, first.text, "Leaving restores original code highlight");
                t.True(!first.ClassListContains("whimtex-fx-reference-text--link-hover"), "Leaving removes hand cursor class");
                t.True(string.IsNullOrEmpty(first.tooltip), "Interactive fragments have no tooltip");
                using (var tooltip = TooltipEvent.GetPooled())
                {
                    tooltip.target = first; first.SendEvent(tooltip);
                    t.True(tooltip.isImmediatePropagationStopped, "Shader Inputs blocks inherited tooltips too");
                }
                CheckIndent("x", 0, 0, "    x", 4, 4);
                CheckIndent("abc", 1, 1, "a   bc", 4, 4);
                CheckIndent("    code", 4, 4, "        code", 8, 8);
                CheckIndent("a\tb", 2, 2, "a\t    b", 6, 6);
                CheckIndent("    code", 8, 8, "code", 4, 4, true);
                CheckIndent("  code", 6, 6, "code", 4, 4, true);
                CheckIndent("\tcode", 5, 5, "code", 4, 4, true);
                CheckIndent("one\n  two\nthree", 10, 0, "    one\n      two\nthree", 18, 0);
                CheckIndent("one\n  two\nthree", 0, 10, "    one\n      two\nthree", 0, 18);
                CheckIndent("  alpha beta", 7, 2, "      alpha beta", 11, 6);
                CheckIndent("a\r\nb\r\nc", 6, 0, "    a\r\n    b\r\nc", 14, 0);
                CheckIndent("    a\n\tb\nc", 9, 0, "a\nb\nc", 4, 0, true);
                CheckIndent("", 0, 0, "    ", 4, 4);
                SetCode("");
                int unchangedGroup = Undo.GetCurrentGroup();
                Tab(true);
                t.Equal("", Current(), "Outdenting an empty line is a no-op");
                t.Equal(unchangedGroup, Undo.GetCurrentGroup(), "No-op does not create an Undo group");
                SetCode(Source); code.isReadOnly = true;
                Tab();
                t.Equal(Source, Current(), "Read-only code rejects indentation");
                code.isReadOnly = false;
                var template = ScriptableObject.CreateInstance<ShaderFX>();
                template.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    string source = (string)typeof(ShaderFX).GetProperty("Code", F).GetValue(template);
                    t.True(source.Contains("\n{\n    return color * _Tint;\n}"), "New ApplyFX body uses the editor's four-space indentation");
                    t.True(!source.Contains("\t"), "New FX template uses soft tabs");
                }
                finally { UnityEngine.Object.DestroyImmediate(template); }
                CheckTyping("    value;|", "    value;\n    |");
                CheckTyping("    |value;", "    \n    |value;");
                CheckTyping("  |  value;", "  \n  |  value;");
                CheckTyping("    if (test) {|", "    if (test) {\n        |");
                CheckTyping("{|}", "{\n    |\n}");
                CheckTyping("    {|  }", "    {\n        |\n    }");
                CheckTyping("{\n    call();|}", "{\n    call();\n|}");
                CheckTyping("    // {|", "    // {\n    |");
                CheckTyping("    if (x) { // note|", "    if (x) { // note\n        |");
                CheckTyping("    /* { */|", "    /* { */\n    |");
                CheckTyping("    /* open {|", "    /* open {\n    |");
                CheckTyping("    text = \"{\";|", "    text = \"{\";\n    |");
                CheckTyping("    text = \"escaped \\\" {\";|", "    text = \"escaped \\\" {\";\n    |");
                CheckTyping("    text = '{';|", "    text = '{';\n    |");
                CheckTyping("    text = \"{|\"", "    text = \"{\n    |\"");
                CheckTyping("    value;\r\n    next;|", "    value;\r\n    next;\r\n    |");
                CheckTyping("    {|}\r\nnext", "    {\r\n        |\r\n    }\r\nnext");
                CheckTyping("\tif (x) {|", "\tif (x) {\n\t    |");
                CheckTyping("|", "\n|");
                CheckTyping("    |", "    \n    |");
                CheckTyping("{\n    |", "{\n}|", '}', EventModifiers.Shift);
                CheckTyping("    {\n        |", "    {\n    }|", '}', EventModifiers.Shift);
                CheckTyping("    {\n            |", "    {\n    }|", '}', EventModifiers.Shift);
                CheckTyping("{\n    {\n        }\n    |", "{\n    {\n        }\n}|", '}', EventModifiers.Shift);
                CheckTyping("    |", "}|", '}', EventModifiers.Shift);
                CheckTyping("  |", "}|", '}', EventModifiers.Shift);
                CheckTyping("\t|", "}|", '}', EventModifiers.Shift);
                CheckTyping("\t{\n\t    |", "\t{\n\t}|", '}', EventModifiers.Shift);
                CheckTyping("    return x; |", "    return x; }|", '}', EventModifiers.Shift);
                CheckTyping("    // |", "    // }|", '}', EventModifiers.Shift);
                CheckTyping("/* {\n    |", "/* {\n    }|", '}', EventModifiers.Shift);
                CheckTyping("\"{\n    |", "\"{\n    }|", '}', EventModifiers.Shift);
                CheckTyping("{\n    // {\n    |", "{\n    // {\n}|", '}', EventModifiers.Shift);
                CheckTyping("{\n    \"{\";\n    |", "{\n    \"{\";\n}|", '}', EventModifiers.Shift);
                CheckTyping("    {\n        |", "    {\n    }|", '}', EventModifiers.Control | EventModifiers.Alt);
                CheckEdit("    alpha beta", 14, 4, "    \n    ", 9, 9, KeyCode.Return, '\n');
                CheckEdit("    alpha beta", 4, 14, "    \n    ", 9, 9, KeyCode.Return, '\n');
                CheckEdit("    alpha", 9, 4, "}", 1, 1, KeyCode.RightBracket, '}', EventModifiers.Shift);
                CheckEdit("    x;", 6, 6, "    x;\n    ", 11, 11, KeyCode.KeypadEnter, '\n');
                SetCode(Source); code.isReadOnly = true;
                Key(KeyCode.Return, '\n'); Key(KeyCode.RightBracket, '}', EventModifiers.Shift);
                t.Equal(Source, Current(), "Read-only code rejects automatic newline/brace edits");
                code.isReadOnly = false;
                foreach (char newlineCharacter in new[] { '\r', '\n' })
                {
                    SetCode("    value;"); code.textSelection.SelectRange(10, 10);
                    Key(KeyCode.Return, '\0');
                    Key(KeyCode.None, newlineCharacter);
                    var target = code.textEdition as VisualElement ?? code;
                    using (var up = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = KeyCode.Return }))
                    { up.target = target; target.SendEvent(up); }
                    t.Equal("    value;\n    ", Current(), "One physical Enter with a separate character event inserts only one newline");
                    Undo.PerformUndo();
                    t.Equal("    value;", Current(), "Physical Enter including its character event remains one Undo step");
                    code.textSelection.SelectRange(10, 10);
                    Key(KeyCode.None, newlineCharacter);
                    t.Equal("    value;\n    ", Current(), "Standalone newline character still receives auto-indent after key release");
                }
                SetCode("    value;"); code.textSelection.SelectRange(10, 10);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Key(KeyCode.Return, '\0');
                    Key(KeyCode.None, '\r'); Key(KeyCode.None, '\n');
                }
                t.Equal("    value;\n    \n    ", Current(), "Held Enter inserts one newline per key repeat, not per translated character");
                t.True(CodeFocused(), "Physical Enter sequence retains editor focus");
                CheckEdit("    x;", 6, 6, "    x;\n    ", 11, 11, KeyCode.Return, '\r');
            });
        }
        finally { Release(state); }
    }

    public static string Poll(string runId)
    {
        var state = AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        return state == null ? TestContext.Result("failed", 0, "Missing code reference state").ToJson() :
            (string)state[3] ?? TestContext.Result("running", 0, "Waiting for code reference layout").ToJson();
    }
    public static string Cleanup(string runId)
    {
        var state = AppDomain.CurrentDomain.GetData(Key(runId)) as object[];
        if (state == null) return TestContext.Result("failed", 0, "Missing cleanup state").ToJson();
        Release(state); AppDomain.CurrentDomain.SetData(Key(runId), null);
        return TestContext.Result("passed", 1, "Owned code reference controls released").ToJson();
    }
    static void Release(object[] state)
    {
        var errors = new List<Exception>();
        void ReleaseOwned(Action release) { try { release(); } catch (Exception error) { errors.Add(error); } }
        ReleaseOwned(() => { (state[4] as IVisualElementScheduledItem)?.Pause(); state[4] = null; });
        ReleaseOwned(() =>
        {
            if (state[0] is EditorWindow window && window != null)
            {
                try
                {
                    window.rootVisualElement.Unbind();
                    if (window.rootVisualElement.panel != null) window.Close();
                }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            }
            state[0] = null;
        });
        ReleaseOwned(() => { (state[5] as SerializedObject)?.Dispose(); state[5] = null; });
        ReleaseOwned(() =>
        {
            if (state[1] is ShaderFX effect && effect != null)
            {
                try { Undo.ClearUndo(effect); }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            state[1] = null;
        });
        ReleaseOwned(() =>
        {
            if (state[2] is EditorWindow previousFocus && previousFocus != null) previousFocus.Focus();
            state[2] = null;
        });
        if (errors.Count != 0)
        {
            var error = new AggregateException("Owned code reference cleanup requires recovery", errors);
            state[3] = TestContext.Result("failed", 0, "Code reference cleanup requires recovery; prior result: " + state[3], error.ToString()).ToJson();
            throw error;
        }
    }
}
