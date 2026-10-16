using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Json = ProfilerTests.Json;

public static class ProfilerReloadTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type Profiler = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler", true);
    static string Key(string runId) => "WhimTex.Tests.ProfilerReload." + runId;
    static Json Call(string action, params object[] fields)
    {
        var request = new Json { ["apiVersion"] = 1, ["action"] = action };
        for (int i = 0; i < fields.Length; i += 2) request[(string)fields[i]] = Json.FromObject(fields[i + 1]);
        var result = Json.Parse(WhimTexApi.ProfileJson(request.ToString()));
        if (!(bool)result["success"]) throw new InvalidOperationException(result.ToString());
        return result;
    }
    static IDisposable Span(string phase, WhimTexDocument document = null) => (IDisposable)Profiler.GetMethod("Measure", Static)
        .Invoke(null, new object[] { phase, document, null, -1, null });

    public static string Begin(string runId) => TestContext.Run("Prepare owned profiler capture for real reload", t =>
    {
        t.True(Call("status")["capture"].Type == "Null", "Do not replace a user's capture");
        UnityBReload.Begin("Profiler", runId, t);
        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            string documentId = (string)Profiler.GetMethod("DocumentId", Static).Invoke(null, new object[] { document });
            var started = Call("start", "durationSeconds", 600, "documentId", documentId);
            string id = (string)started["capture"]["captureId"];
            SessionState.SetString(Key(runId) + ".id", id);
            var open = Span("reload.open", document);
            AppDomain.CurrentDomain.SetData(Key(runId) + ".dispose", (Action)(() => open.Dispose()));
            using (Span("reload.complete"))
                Profiler.GetMethod("Event", Static).Invoke(null, new object[] { "reload.cache", "cache", "hit", 0d });
            var before = Call("read", "captureId", id);
            SessionState.SetString(Key(runId) + ".before", before.ToString());
            t.Equal(3, before["events"].Count, "Nested completed and open events prepared");
            UnityBReload.Arm("Profiler", runId);
        }
        finally { UnityEngine.Object.DestroyImmediate(document); }
    });

    public static string Trigger(string runId) => UnityBReload.Trigger("Profiler", runId);
    public static string ReloadPoll(string runId) => UnityBReload.ReloadPoll("Profiler", runId);

    public static string Verify(string runId) => TestContext.Run("Verify actual automatic profiler recovery", t =>
    {
        UnityBReload.Verify("Profiler", runId, t);
        string id = SessionState.GetString(Key(runId) + ".id", "");
        var before = Json.Parse(SessionState.GetString(Key(runId) + ".before", ""));
        var after = Call("read", "captureId", id);
        t.Equal(id, (string)after["capture"]["captureId"], "Capture identity survives actual domain reload");
        t.Equal("assembly_reload", (string)after["capture"]["stopReason"], "Active capture stops before reload");
        t.True(!(bool)after["capture"]["recording"], "Recording never spans the compilation gap");
        t.Equal(0, (int)after["capture"]["openSpans"], "Restored capture is not locked by vanished scopes");
        t.Equal(3, after["events"].Count, "No events lost or added during reload");
        var oldEvents = before["events"].ToArray(); var newEvents = after["events"].ToArray();
        for (int i = 1; i < 3; i++) t.Equal(oldEvents[i].ToString(), newEvents[i].ToString(), "Completed event and exact timing survive reload");
        t.True((bool)newEvents[0]["interrupted"] && !(bool)newEvents[0]["completed"] && newEvents[0]["durationMs"].Type == "Null", "Interrupted root keeps unknown timing");
        t.Equal(before["summary"].ToString(), after["summary"].ToString(), "Summary is identical after reload");
        var windowType = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfilerWindow", true);
        var window = (EditorWindow)ScriptableObject.CreateInstance(windowType);
        try
        {
            windowType.GetMethod("CreateGUI").Invoke(window, null);
            t.Equal(3, window.rootVisualElement.Query<ListView>().ToList()[1].itemsSource.Count, "Window displays recovered events without manual Refresh");
        }
        finally { UnityEngine.Object.DestroyImmediate(window); }
    });

    public static string Cleanup(string runId) => UnityBReload.Cleanup("Profiler", runId, () =>
    {
        (AppDomain.CurrentDomain.GetData(Key(runId) + ".dispose") as Action)?.Invoke();
        AppDomain.CurrentDomain.SetData(Key(runId) + ".dispose", null);
        string id = SessionState.GetString(Key(runId) + ".id", "");
        var current = Call("status")["capture"];
        if (id.Length > 0 && current.Type != "Null")
        {
            if ((string)current["captureId"] != id) throw new InvalidOperationException("Another capture replaced the fixture; leave it untouched.");
            Call("stop", "captureId", id); Call("clear", "captureId", id);
        }
        SessionState.EraseString(Key(runId) + ".id");
        SessionState.EraseString(Key(runId) + ".before");
    });
}
