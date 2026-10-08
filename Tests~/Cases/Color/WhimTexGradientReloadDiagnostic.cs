// Independent two-phase fixture. GradientReload.mjs owns the native reload sequence;
// Begin/End also remain available for parent-serialized manual verification.
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
public static class WhimTexGradientReloadDiagnostic
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Key(string runId)
    {
        if (!Guid.TryParseExact(runId, "D", out _)) throw new ArgumentException("Expected per-run GUID");
        return "WhimTex.Tests.UnityD.GradientReload." + runId;
    }
    [Serializable]
    public sealed class NativeReloadResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public bool queued, requested, reloaded, oldDomainCleared;
        public int requests;
        public string[] errors;
    }
    [Serializable]
    public sealed class CompilerErrors { public string[] errors = new string[0]; }
    static string[] ReadCompilerErrors(string key)
    {
        string json = SessionState.GetString(key + ".compilerErrors", "");
        return json.Length == 0 ? new string[0] : JsonUtility.FromJson<CompilerErrors>(json).errors;
    }
    static bool CanRequest() => !EditorApplication.isPlayingOrWillChangePlaymode
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
    static void DetachCompilationErrors(string key)
    {
        var callback = AppDomain.CurrentDomain.GetData(key + ".compilationCallback") as Action<string, CompilerMessage[]>;
        try { if (callback != null) CompilationPipeline.assemblyCompilationFinished -= callback; }
        finally { AppDomain.CurrentDomain.SetData(key + ".compilationCallback", null); }
    }
    static string NativeResult(string key, TestResult result)
    {
        string error = SessionState.GetString(key + ".triggerError", "");
        var errors = new System.Collections.Generic.List<string>(ReadCompilerErrors(key));
        if (error.Length != 0) errors.Add(error);
        return JsonUtility.ToJson(new NativeReloadResult {
            status = result.status, checks = result.checks, message = result.message, failures = result.failures,
            queued = SessionState.GetBool(key + ".queued", false),
            requested = SessionState.GetBool(key + ".requested", false),
            reloaded = SessionState.GetBool(key + ".reloaded", false),
            oldDomainCleared = AppDomain.CurrentDomain.GetData(key + ".callback") == null,
            requests = SessionState.GetInt(key + ".requests", 0),
            errors = errors.ToArray()
        });
    }
    public static string Trigger(string runId)
    {
        string key = Key(runId);
        if (SessionState.GetString(key, "") != "WhimTex gradient reload " + runId
            || AppDomain.CurrentDomain.GetData(key + ".callback") == null
            || SessionState.GetBool(key + ".queued", false)
            || SessionState.GetBool(key + ".requested", false))
            return NativeResult(key, TestContext.Result("failed", 0, "Trigger rejected",
                "Expected a fresh Begin fixture; never retry an existing compilation request."));
        if (!CanRequest())
            return NativeResult(key, TestContext.Result("failed", 0, "Trigger rejected",
                "Editor must be stopped, not compiling, and not updating at acknowledgement."));

        Action<string, CompilerMessage[]> compilationErrors = (assembly, messages) => {
            var errors = new System.Collections.Generic.List<string>(ReadCompilerErrors(key));
            foreach (var message in messages)
                if (message.type == CompilerMessageType.Error)
                    errors.Add(assembly + ": " + message.file + "(" + message.line + "," + message.column + "): " + message.message);
            SessionState.SetString(key + ".compilerErrors", JsonUtility.ToJson(new CompilerErrors { errors = errors.ToArray() }));
        };
        AppDomain.CurrentDomain.SetData(key + ".compilationCallback", compilationErrors);
        CompilationPipeline.assemblyCompilationFinished += compilationErrors;

        // Owned public update callback is deferred beyond this invocation's return. No static
        // state is shared with a later ephemeral Pipeline assembly; Unity delegates are bridged.
        double notBefore = EditorApplication.timeSinceStartup + 1.0;
        EditorApplication.CallbackFunction trigger = null;
        trigger = () => {
            if (EditorApplication.timeSinceStartup < notBefore) return;
            EditorApplication.update -= trigger;
            AppDomain.CurrentDomain.SetData(key + ".trigger", null);
            SessionState.SetBool(key + ".queued", false);
            if (!CanRequest())
            {
                SessionState.SetString(key + ".triggerError", "Deferred request rejected: Editor is not stopped/idle (compiling or updating).");
                try { DetachCompilationErrors(key); }
                catch (Exception error) { SessionState.SetString(key + ".triggerError", SessionState.GetString(key + ".triggerError", "") + "\n" + error); }
                return;
            }
            // Persist before calling Unity: even a thrown request is never retried.
            SessionState.SetBool(key + ".requested", true);
            SessionState.SetInt(key + ".requests", SessionState.GetInt(key + ".requests", 0) + 1);
            try { CompilationPipeline.RequestScriptCompilation(); }
            catch (Exception error)
            {
                SessionState.SetString(key + ".triggerError", error.ToString());
                try { DetachCompilationErrors(key); }
                catch (Exception detachError) { SessionState.SetString(key + ".triggerError", error + "\n" + detachError); }
            }
        };
        SessionState.SetBool(key + ".queued", true);
        AppDomain.CurrentDomain.SetData(key + ".trigger", trigger);
        EditorApplication.update += trigger;
        return NativeResult(key, TestContext.Result("running", 0, "One owned native compilation request deferred"));
    }
    public static string ReloadPoll(string runId)
    {
        string key = Key(runId);
        string error = SessionState.GetString(key + ".triggerError", "");
        string[] compilerErrors = ReadCompilerErrors(key);
        if (compilerErrors.Length != 0)
            return NativeResult(key, TestContext.Result("failed", 0, "Real assembly compiler errors captured", compilerErrors));
        if (error.Length != 0 || SessionState.GetString(key, "") != "WhimTex gradient reload " + runId)
            return NativeResult(key, TestContext.Result("failed", 0, "Native reload could not be confirmed",
                error.Length == 0 ? "Owned Begin fixture is missing" : error));
        if (!SessionState.GetBool(key + ".reloaded", false)
            || AppDomain.CurrentDomain.GetData(key + ".callback") != null)
            return NativeResult(key, TestContext.Result("running", 0, "Waiting for real marker and old AppDomain release"));
        var context = new TestContext();
        try
        {
            context.Equal(1, SessionState.GetInt(key + ".requests", 0), "Exactly one native request occurred");
            context.True(SessionState.GetBool(key + ".reloaded", false), "Real beforeAssemblyReload marker persisted");
            context.True(AppDomain.CurrentDomain.GetData(key + ".callback") == null, "Old AppDomain callback is gone");
            return NativeResult(key, TestContext.Result("passed", context.Checks, "Real native domain reload confirmed"));
        }
        catch (Exception failure)
        {
            return NativeResult(key, TestContext.Result("failed", context.Checks, "Native reload evidence failed", failure.ToString()));
        }
    }
    public static string Begin(string runId)
    {
        string key = Key(runId);
        if (SessionState.GetString(key, "").Length != 0)
            return TestContext.Result("failed", 0, "Existing reload fixture", "End/Cleanup the same GUID first").ToJson();
        WhimTexDocument host = null, previousDefault = null; WhimTexWindow window = null;
        AssemblyReloadEvents.AssemblyReloadCallback beforeReload = null;
        try
        {
            var previousFocus = EditorWindow.focusedWindow;
            SessionState.SetString(key + ".focus", FocusIdentity(previousFocus));
            host = ScriptableObject.CreateInstance<WhimTexDocument>();
            var gradient = new WhimTexGradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(4,2,1),0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(.3f,0), new GradientAlphaKey(1,1) });
            gradient.SetMidpoint(false,0,.23f);
            host.layers.Add(new GradientLayerBehaviour { gradient = gradient });
            host.name = "WhimTex gradient reload " + runId;
            host.hideFlags = HideFlags.HideAndDontSave;
            window = ScriptableObject.CreateInstance<WhimTexWindow>();
            // Only the default allocated by this fresh owned window, never a borrowed user model.
            previousDefault = (WhimTexDocument)typeof(WhimTexWindow).GetField("activeDocument", Flags).GetValue(window);
            window.name = "WhimTex gradient reload window " + runId;
            typeof(WhimTexWindow).GetMethod("SetDocument", Flags).Invoke(window, new object[] { host });
            DestroyOwnedDefault(previousDefault, host);
            previousDefault = null;
            window.Show();
            SessionState.SetString(key, host.name);
            SessionState.SetBool(key + ".reloaded", false);
            beforeReload = () => SessionState.SetBool(key + ".reloaded", true);
            AssemblyReloadEvents.beforeAssemblyReload += beforeReload;
            AppDomain.CurrentDomain.SetData(key + ".callback", beforeReload);
            // Do not fabricate an assertion count for merely opening a visual fixture.
            return TestContext.Result("skipped", 0,
                "Manual fixture ready. Trigger one real Unity domain reload externally, then End with GUID " + runId).ToJson();
        }
        catch (Exception error)
        {
            var errors = new System.Collections.Generic.List<Exception> { error };
            void Attempt(Action cleanup) { try { cleanup(); } catch (Exception cleanupError) { errors.Add(cleanupError); } }
            try
            {
                Attempt(() => { if (beforeReload != null) AssemblyReloadEvents.beforeAssemblyReload -= beforeReload; });
                Attempt(() => DestroyOwnedWindow(window));
            }
            finally
            {
                Attempt(() => DestroyOwnedDefault(previousDefault, host));
                Attempt(() => { if (host != null) UnityEngine.Object.DestroyImmediate(host); });
                Attempt(() => Remove(runId)); // Also restores focus and removes all owned session/callback state.
            }
            return TestContext.Result("failed", 0, "Manual reload fixture setup failed",
                new AggregateException("Setup and owned cleanup evidence", errors).ToString()).ToJson();
        }
    }
    public static string End(string runId) => TestContext.Run("Actual domain reload preserves gradient keys", context =>
    {
        string key = Key(runId);
        try
        {
            context.True(SessionState.GetBool(key + ".reloaded", false), "A real Unity domain reload occurred after Begin");
            string title = SessionState.GetString(key, "");
            WhimTexDocument host = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
                if (candidate.name == title && title == "WhimTex gradient reload " + runId) host = candidate;
            context.True(host != null, "EditorWindow-owned document restored after reload");
            var gradient = ((GradientLayerBehaviour)host.layers[0].Behaviour).gradient;
            context.Equal(4f, gradient.ColorKeys[0].color.r, "Stored HDR key retained");
            context.Equal(.3f, gradient.AlphaKeys[0].alpha, "Stored alpha retained");
            context.Equal(.23f, gradient.GetMidpoint(false, 0), "Stored midpoint retained");
        }
        finally { Remove(runId); }
    });
    public static string Cleanup(string runId) => TestContext.Run("Only this manual reload fixture is removed", context =>
    {
        Remove(runId);
        context.Equal("", SessionState.GetString(Key(runId), ""), "Owned SessionState key erased");
    });
    static void Remove(string runId)
    {
        string key = Key(runId), title = "WhimTex gradient reload " + runId;
        var errors = new System.Collections.Generic.List<Exception>();
        void Attempt(Action cleanup) { try { cleanup(); } catch (Exception error) { errors.Add(error); } }
        var callback = AppDomain.CurrentDomain.GetData(key + ".callback") as AssemblyReloadEvents.AssemblyReloadCallback;
        Attempt(() => { if (callback != null) AssemblyReloadEvents.beforeAssemblyReload -= callback; });
        Attempt(() => AppDomain.CurrentDomain.SetData(key + ".callback", null));
        var trigger = AppDomain.CurrentDomain.GetData(key + ".trigger") as EditorApplication.CallbackFunction;
        Attempt(() => { if (trigger != null) EditorApplication.update -= trigger; });
        Attempt(() => AppDomain.CurrentDomain.SetData(key + ".trigger", null));
        Attempt(() => DetachCompilationErrors(key));
        foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
            if (window.name == "WhimTex gradient reload window " + runId)
                Attempt(() => DestroyOwnedWindow(window));
        foreach (var host in Resources.FindObjectsOfTypeAll<WhimTexDocument>())
            if (host.name == title) Attempt(() => UnityEngine.Object.DestroyImmediate(host));
        Attempt(() => SessionState.EraseString(key));
        Attempt(() => SessionState.EraseBool(key + ".reloaded"));
        Attempt(() => SessionState.EraseBool(key + ".queued"));
        Attempt(() => SessionState.EraseBool(key + ".requested"));
        Attempt(() => SessionState.EraseInt(key + ".requests"));
        Attempt(() => SessionState.EraseString(key + ".triggerError"));
        Attempt(() => SessionState.EraseString(key + ".compilerErrors"));
        Attempt(() => RestoreFocus(key));
        if (errors.Count > 0) throw new AggregateException("Owned reload fixture cleanup/restoration failed", errors);
    }
    static void RestoreFocus(string key)
    {
        string id = SessionState.GetString(key + ".focus", "");
        try
        {
            if (id.Length != 0)
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (FocusIdentity(window) == id) { window.Focus(); break; }
        }
        finally { SessionState.EraseString(key + ".focus"); }
    }
    static string FocusIdentity(EditorWindow window)
    {
        if (window == null) return "";
#if UNITY_6000_4_OR_NEWER
        return window.GetEntityId().ToString();
#else
        return window.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
    static void DestroyOwnedWindow(EditorWindow window)
    {
        if (window == null) return;
        var errors = new System.Collections.Generic.List<Exception>();
        void Attempt(Action cleanup) { try { cleanup(); } catch (Exception error) { errors.Add(error); } }
        try
        {
            // An unshown window has no attached panel; Close would use unavailable native GUI state.
            Attempt(() => { if (window.rootVisualElement?.panel != null) window.DiscardChanges(); });
            Attempt(() => { if (window != null && window.rootVisualElement?.panel != null) window.Close(); });
        }
        finally { Attempt(() => { if (window != null) UnityEngine.Object.DestroyImmediate(window); }); }
        if (errors.Count > 0) throw new AggregateException("Owned reload window disposal failed", errors);
    }
    static void DestroyOwnedDefault(WhimTexDocument previousDefault, WhimTexDocument host)
    {
        if (previousDefault != null && previousDefault != host && !AssetDatabase.Contains(previousDefault))
            UnityEngine.Object.DestroyImmediate(previousDefault);
    }
}
