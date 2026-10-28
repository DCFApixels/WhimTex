using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public sealed class ShaderFXContainersWindow : EditorWindow { }

public static class ShaderFXContainersTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly A = typeof(ShaderFX).Assembly;
    static Type T(string name) => A.GetType("DCFApixels.WhimTex." + name, true);
    static string Key(string id) => "WhimTex.Tests.Containers." + Guid.Parse(id).ToString("D");
    static List<ShaderFXParameter> Parse(string code) => (List<ShaderFXParameter>)T("ShaderFXMetadata")
        .GetMethod("Parse", F).Invoke(null, new object[] { code, false, null });
    const string Code = @"
// @param toggle(float) _Show = true
// @tabs
// @tab(Color)
// @foldout(Advanced; _Enabled)
// @param hidden toggle(float) _Enabled = true
// @param preview(Preview) float _Amount = 0.25 [0 .. 1]
// @group(Tint)
// @param color _Tint = #FFFFFFFF
// @endgroup
// @endfoldout
// @endtab
// @tab(Mask; _MaskEnabled)
// @param hidden toggle(float) _MaskEnabled = false
// @if _Show == 1
// @param float _Threshold = 0.5 [0 .. 1]
// @endif
// @preview(Preview)
// @endtab
// @endtabs
// @tabs(Settings)
// @tab(First)
// @param float _First = 1
// @endtab
// @tab(Second)
// @param float _Second = 2
// @endtab
// @endtabs
float4 Preview(float2 uv) { return float4(uv.x, _Amount, 0, 1); }
float4 ApplyFX(PinnedUV _pinnedUv, float4 color) { float2 uv = _pinnedUv; return color * _Amount; }
";

    public static string Start(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) != null)
            return TestContext.Result("failed", 0, "Duplicate container fixture").ToJson();
        var s = new object[5]; AppDomain.CurrentDomain.SetData(Key(id), s);
        try
        {
            var result = JsonUtility.FromJson<TestResult>(TestContext.Run("Container parsing and export", t => Metadata(t)));
            s[4] = result.checks;
            if (result.status != "passed") { s[3] = result.ToJson(); return (string)s[3]; }
            var effect = (ShaderFX)typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderFXParameter>) }, null)
                .Invoke(null, new object[] { null, Code, Parse(Code) });
            s[1] = effect; s[2] = EditorWindow.focusedWindow;
            var window = ScriptableObject.CreateInstance<ShaderFXContainersWindow>(); s[0] = window;
            T("WhimTexUI").GetMethod("ApplyWindowStyles", F).Invoke(null, new object[] { window.rootVisualElement });
            window.rootVisualElement.Add(View(effect));
            window.titleContent = new GUIContent("FX Containers Test");
            window.ShowUtility(); window.position = new Rect(100, 100, 420, 420);
            window.rootVisualElement.schedule.Execute(() => Check(s)).StartingIn(500);
        }
        catch (Exception e) { s[3] = TestContext.Result("failed", 0, "Container setup", e.ToString()).ToJson(); Release(s); }
        return Poll(id);
    }

    static VisualElement View(ShaderFX effect) => (VisualElement)Activator.CreateInstance(T("ShaderFXParameterView"), F, null, new object[] { effect }, null);
    static void Refresh(VisualElement view) => view.GetType().GetMethod("Refresh", F).Invoke(view, null);

    static void Metadata(TestContext t)
    {
        var values = Parse(Code);
        var amount = values.Find(p => p.name == "_Amount");
        t.Equal(3, amount.controls[0].containers.Length, "Tabs, page, foldout path");
        t.Equal(ShaderFXContainerKind.Foldout, amount.controls[0].containers[2].kind, "Foldout kind");
        t.Equal("_Enabled", amount.controls[0].groupHeaderParameter, "Foldout header binding");
        t.Equal(4, values.Find(p => p.name == "_Tint").controls[0].containers.Length, "Group nested in foldout");
        t.True(values.Find(p => p.name == "_First").controls[0].containers[0].title == "Settings", "Optional tabs heading");
        var maskHeader = values.Find(p => p.name == "_MaskEnabled").controls[0];
        t.True(maskHeader.containers[1].headerParameter == "_MaskEnabled" && !maskHeader.inGroup,
            "Tab checkbox binding belongs to the page, not a group");
        var copy = (ShaderFXParameter)typeof(ShaderFXParameter).GetMethod("Copy", F).Invoke(amount, null);
        t.True(!ReferenceEquals(copy.controls[0].containers, amount.controls[0].containers), "Copy owns its path array");
        var native = JsonUtility.FromJson<ShaderFXParameter>(JsonUtility.ToJson(amount));
        t.Equal(amount.controls[0].containers.Length, native.controls[0].containers.Length, "Native serialization retains layout");
        for (int n = 0; n < native.controls[0].containers.Length; n++)
            t.True(amount.controls[0].containers[n].Equals(native.controls[0].containers[n]), "Native container values");
        object[] args = { Code, false, null, null };
        T("ShaderFXMetadata").GetMethod("ParseWithPreviews", F).Invoke(null, args);
        var previews = (List<ShaderFXParameterControl>)args[3];
        t.Equal(2, previews[0].containers.Length, "Standalone preview belongs to page");
        string exported = (string)T("ShaderFXPresetWriter").GetMethod("BuildParameterSource", F).Invoke(null,
            new object[] { Code, values, "Tests/Containers", "whimtex-effect", null });
        var restored = Parse(exported);
        for (int i = 0; i < values.Count; i++)
        {
            t.Equal(values[i].name, restored[i].name, "Export parameter order");
            var a = values[i].controls[0].containers; var b = restored[i].controls[0].containers;
            t.Equal(a.Length, b.Length, "Export path length");
            for (int n = 0; n < a.Length; n++) t.True(a[n].Equals(b[n]), "Export path identity");
        }
        foreach (string invalid in new[] {
            "// @tab(A)\n// @param float _X\n// @endtab",
            "// @tabs\n// @param float _X\n// @endtabs",
            "// @tabs\n// @tab(A)\n// @param float _X\n// @endtabs",
            "// @tabs\n// @endtabs",
            "// @tabs\n// @tab(A)\n// @param hidden float _X\n// @endtab\n// @endtabs",
            "// @tabs\n// @tab(A)\n// @param float _X\n// @endtab\n// @tab(A)\n// @param float _Y\n// @endtab\n// @endtabs",
            "// @tabs\n// @tab(A)\n// @tabs\n// @tab(B)\n// @param float _X\n// @endtab\n// @endtabs\n// @endtab\n// @endtabs",
            "// @foldout\n// @param float _X\n// @endfoldout",
            "// @foldout(A; _X)\n// @group(B)\n// @param float _X\n// @endgroup\n// @endfoldout",
            "// @foldout(A)\n// @param float _X\n// @endgroup",
            "// @param toggle(float) _Enabled\n// @if _Enabled == 1\n// @foldout(A)\n// @param float _X\n// @endfoldout\n// @endif"
        })
        {
            bool rejected = false;
            try { Parse(invalid); } catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            t.True(rejected, "Invalid container rejected: " + invalid);
        }
    }

    static void Check(object[] s)
    {
        try
        {
            var report = JsonUtility.FromJson<TestResult>(TestContext.Run("Container controls and layout", t =>
            {
                var effect = (ShaderFX)s[1]; var window = (EditorWindow)s[0]; var view = window.rootVisualElement[0];
                var blocks = view.Query<VisualElement>(className: "whimtex-fx-tabs").ToList();
                t.Equal(2, blocks.Count, "Independent tab blocks");
                t.True(blocks[0].Q<Label>(className: "whimtex-fx-tabs-title") == null, "Untitled block has no header row");
                t.Equal("Settings", blocks[1].Q<Label>(className: "whimtex-fx-tabs-title").text, "Optional title displayed");
                var tabs = blocks[0].Query<Button>(className: "whimtex-fx-tab").ToList();
                var pages = blocks[0].Query<VisualElement>(className: "whimtex-fx-tab-page").ToList();
                t.True(tabs[0].ClassListContains("whimtex-fx-tab--selected") && pages[1].resolvedStyle.display == DisplayStyle.None, "First page initially selected");
                t.True(tabs[0].layout.width + tabs[1].layout.width < blocks[0].layout.width - 20, "Content-width headers are not stretched buttons");
                var tabToggle = tabs[1].parent.Q<Toggle>(className: "whimtex-fx-tab-toggle");
                t.True(tabToggle != null && !tabToggle.value && tabs[1].text == "Mask", "Linked bool is promoted left of native tab title");
                float titleWidth = tabs[1].MeasureTextSize(tabs[1].text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                t.True(titleWidth > 10 && tabs[1].contentRect.width >= titleWidth - .5f &&
                    tabToggle.worldBound.width >= 16 && tabToggle.worldBound.xMax <= tabs[1].worldBound.xMin + tabs[1].contentRect.xMin,
                    "Native label has its measured width and does not overlap the checkbox: text=" + titleWidth +
                    ", content=" + tabs[1].contentRect + ", tab=" + tabs[1].worldBound + ", toggle=" + tabToggle.worldBound);
                t.Near(tabs[1].worldBound.xMin + tabs[1].resolvedStyle.borderLeftWidth + 4, tabToggle.worldBound.xMin, .5,
                    "Tab checkbox has a small four-pixel left inset");
                Undo.IncrementCurrentGroup();
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = tabToggle; tabToggle.SendEvent(submit); }
                Undo.FlushUndoRecordObjects();
                t.True(tabToggle.value && tabs[0].ClassListContains("whimtex-fx-tab--selected"), "Checkbox keyboard input does not switch pages");
                t.Equal(1f, ((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect)).Find(p => p.name == "_MaskEnabled").floatValue,
                    "Native checkbox event updates the shader bool");
                void RefreshTabs()
                {
                    Refresh(view);
                    blocks = view.Query<VisualElement>(className: "whimtex-fx-tabs").ToList();
                    tabs = blocks[0].Query<Button>(className: "whimtex-fx-tab").ToList();
                    pages = blocks[0].Query<VisualElement>(className: "whimtex-fx-tab-page").ToList();
                    tabToggle = tabs[1].parent.Q<Toggle>(className: "whimtex-fx-tab-toggle");
                }
                Undo.PerformUndo(); RefreshTabs();
                t.True(!tabToggle.value && tabs[0].ClassListContains("whimtex-fx-tab--selected"), "Undo restores checkbox without changing selected tab");
                Undo.PerformRedo(); RefreshTabs();
                t.True(tabToggle.value, "Redo restores checkbox value");
                Undo.ClearUndo(effect);
                var foldout = view.Q<Foldout>();
                t.True(foldout.value, "Foldout expanded initially");
                var header = foldout.Q<VisualElement>(className: "whimtex-fx-parameter-foldout-header");
                t.True(header.layout.width > 100 && header.layout.height >= 18 && header.worldBound.xMin >= foldout.worldBound.xMin, "Foldout header has usable inline geometry");
                var linked = foldout.Q<Toggle>(className: "whimtex-fx-parameter-group-toggle");
                t.True(linked != null && linked.value, "Separate linked checkbox");
                linked.value = false;
                t.True(foldout.value, "Checkbox edit does not collapse foldout");
                foldout.value = false;
                t.True(!linked.value, "Collapse does not alter parameter");
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = tabs[1]; tabs[1].SendEvent(submit); }
                t.True(tabs[1].ClassListContains("whimtex-fx-tab--selected") && pages[0].ClassListContains("whimtex-fx-tab-page--inactive"), "Native submit switches tabs");
                t.True(tabToggle.value, "Tab navigation does not alter the linked bool");
                t.True(blocks[1].Q<Button>(className: "whimtex-fx-tab").ClassListContains("whimtex-fx-tab--selected"), "Other tabs block unaffected");
                Refresh(view);
                t.True(tabs[1].ClassListContains("whimtex-fx-tab--selected") && !foldout.value, "Refresh retains local UI state");
                var recreated = View(effect); window.rootVisualElement.Clear(); window.rootVisualElement.Add(recreated);
                t.True(!recreated.Q<Foldout>().value && recreated.Query<Button>(className: "whimtex-fx-tab").ToList()[1].ClassListContains("whimtex-fx-tab--selected"), "Recreated inspector restores session UI state");
                t.True(((List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect)).Find(p => p.name == "_Amount").floatValue == .25f, "Navigation does not edit shader values");
                var document = ScriptableObject.CreateInstance<WhimTexDocument>();
                try
                {
                    var layer = new Layer(new ColorFillLayerBehaviour());
                    typeof(Layer).GetMethod("AssignNewId", F).Invoke(layer, null);
                    document.layers.Add(layer); layer.fx.Add(effect);
                    foreach (var mode in new[] { WhimTexJsonWriteMode.Full, WhimTexJsonWriteMode.FullOptimized, WhimTexJsonWriteMode.Compact })
                    {
                        string json = WhimTexDocumentJson.Write(document, new WhimTexJsonWriteOptions { Mode = mode }).Json;
                        using (var restored = WhimTexDocumentJson.Read(json))
                        {
                            t.Equal(1, restored.Document.layers[0].fx.Count, "JSON restores FX in " + mode);
                            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(restored.Document.layers[0].fx[0]);
                            var path = parameters.Find(p => p.name == "_Amount").controls[0].containers;
                            t.True(path.Length == 3 && path[2].kind == ShaderFXContainerKind.Foldout && path[2].headerParameter == "_Enabled", "JSON retains layout in " + mode);
                            var tabPath = parameters.Find(p => p.name == "_MaskEnabled").controls[0].containers;
                            t.Equal("_MaskEnabled", tabPath[1].headerParameter, "JSON retains tab checkbox binding in " + mode);
                        }
                    }
                    layer.fx.Clear();
                }
                finally { document.layers.Clear(); UnityEngine.Object.DestroyImmediate(document); }
            }));
            report.checks += (int)s[4]; s[3] = report.ToJson();
        }
        catch (Exception e) { s[3] = TestContext.Result("failed", 0, "Container UI check", e.ToString()).ToJson(); }
        finally { Release(s); }
    }

    static void Release(object[] s)
    {
        var errors = new System.Collections.Generic.List<Exception>();
        void ReleaseOwned(Action action)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
        ReleaseOwned(() =>
        {
            if (s[0] is EditorWindow window && window != null)
            {
                try { if (window.rootVisualElement.panel != null) window.Close(); }
                finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            }
            s[0] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[1] is ShaderFX effect && effect != null)
            {
                try
                {
                    Undo.ClearUndo(effect);
                    var values = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", F).GetValue(effect);
                    var keys = new HashSet<string>();
                    foreach (var p in values) foreach (var c in p.controls) foreach (var scope in c.containers)
                    {
                        string key = "WhimTex.FX.Layout." + typeof(ShaderFX).GetProperty("ShaderKey", F).GetValue(effect) + "." + scope.kind + "." + scope.id + "." + scope.title;
                        if (keys.Add(key)) { SessionState.EraseBool(key); SessionState.EraseInt(key); }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(effect); }
            }
            s[1] = null;
        });
        ReleaseOwned(() =>
        {
            if (s[2] is EditorWindow focus && focus != null) focus.Focus(); s[2] = null;
        });
        if (errors.Count != 0)
        {
            var failure = new AggregateException("Owned FX fixture cleanup unfinished; retry cleanup before another run", errors);
            s[3] = TestContext.Result("failed", 0, "Cleanup requires recovery; prior result: " + s[3], failure.ToString()).ToJson();
            throw failure;
        }
    }
    public static string Poll(string id) => AppDomain.CurrentDomain.GetData(Key(id)) is object[] s && s[3] is string done ? done : TestContext.Result("running", 0, "Container UI pending").ToJson();
    public static string Cleanup(string id)
    {
        if (AppDomain.CurrentDomain.GetData(Key(id)) is object[] s) Release(s);
        AppDomain.CurrentDomain.SetData(Key(id), null);
        return TestContext.Result("passed", 0, "Container fixture cleanup").ToJson();
    }
}
