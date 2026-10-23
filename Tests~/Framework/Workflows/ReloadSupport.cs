// Test-only reload ownership. SessionState survives a domain reload; AppDomain data does not.
// No reflection into Unity internals and no persistent project script/asset is installed.
public static class UnityBReload
{
    static readonly string[] PreferenceKeys = {
        "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool",
        "DCFApixels.WhimTex.Canvas.PaintToolSettings", "DCFApixels.WhimTex.Canvas.ToolSettings",
        "DCFApixels.WhimTex.CommentAuthor", "DCFApixels.WhimTex.CommentAuthorColor", "DCFApixels.WhimTex.CommentAuthorId"
    };
    [System.Serializable]
    public sealed class Snapshot
    {
        public int version;
        public string runId;
        public string[] selection;
        public string active;
        public string focus;
        public bool armed;
        public int beforeReload;
        public bool triggered;
        public bool requestQueued;
        public int requestCount;
        public string requestError;
        public string[] compilationErrors;
        public string[] preferenceKeys;
        public string[] preferenceValues;
        public bool[] preferenceExisted;
    }

    [System.Serializable]
    public sealed class PollResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public bool requestQueued;
        public int requestCount;
        public int beforeReload;
        public bool oldDomainGone;
        public string requestError;
        public string[] compilationErrors;
    }

    static string Key(string kind, string runId)
    {
        if ((kind != "Document" && kind != "Guide" && kind != "Profiler" && kind != "Agent") || !System.Guid.TryParseExact(runId, "N", out _))
            throw new System.ArgumentException("Expected Document/Guide/Profiler/Agent and a per-run N-format GUID.");
        return "WhimTex.Tests.UnityB.Reload." + kind + "." + runId;
    }
    static Snapshot Read(string key)
    {
        string json = UnityEditor.SessionState.GetString(key, "");
        if (json.Length == 0) throw new System.InvalidOperationException("Missing owned reload state: " + key);
        var snapshot = UnityEngine.JsonUtility.FromJson<Snapshot>(json);
        if (snapshot == null || snapshot.version != 5 || snapshot.selection == null || snapshot.compilationErrors == null ||
            snapshot.preferenceKeys == null || snapshot.preferenceKeys.Length != PreferenceKeys.Length ||
            snapshot.preferenceValues == null || snapshot.preferenceValues.Length != PreferenceKeys.Length ||
            snapshot.preferenceExisted == null || snapshot.preferenceExisted.Length != PreferenceKeys.Length)
            throw new System.InvalidOperationException("Unsupported or malformed reload identity/preference snapshot; preserve it for recovery, never infer missing values.");
        for (int i = 0; i < PreferenceKeys.Length; i++)
            if (snapshot.preferenceKeys[i] != PreferenceKeys[i] || snapshot.preferenceValues[i] == null)
                throw new System.InvalidOperationException("Malformed reload preference snapshot; cleanup uncertain, preserve recovery state.");
        return snapshot;
    }
    static void Write(string key, Snapshot state)
    {
        string json = UnityEngine.JsonUtility.ToJson(state);
        UnityEditor.SessionState.SetString(key, json);
        if (UnityEditor.SessionState.GetString(key, "") != json || Read(key).runId != state.runId)
            throw new System.InvalidOperationException("Reload identity/preference snapshot failed its persisted round trip; retain recovery state.");
    }
    static string Identity(UnityEngine.Object value)
    {
        if (value == null) return "";
        // Same public version boundary as package UnityObjectID and UnityAScope.
        // Keep the full EntityId string, never a hash or narrowed int.
#if UNITY_6000_4_OR_NEWER
        return "entity:" + value.GetEntityId().ToString();
#else
        return "instance:" + value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
    static UnityEngine.Object Resolve(string identity, UnityEngine.Object[] loaded)
    {
        if (string.IsNullOrEmpty(identity)) return null;
        // Public loaded-object enumeration avoids parsing private EntityId representation
        // and does not reload or modify any borrowed asset while restoring selection/focus.
        foreach (var value in loaded) if (value != null && Identity(value) == identity) return value;
        return null;
    }

    public static void Drain(System.Collections.Generic.IEnumerable<System.Action> steps)
    {
        var failures = new System.Collections.Generic.List<System.Exception>();
        foreach (var step in steps) { try { step(); } catch (System.Exception error) { failures.Add(error); } }
        if (failures.Count != 0) throw new System.AggregateException("Owned reload cleanup failed", failures);
    }
    public static void DestroyOwned(UnityEngine.Object value)
    {
        if (value == null) return;
        if (UnityEditor.AssetDatabase.Contains(value)) throw new System.InvalidOperationException("Refusing to destroy an imported object.");
        System.Action disposePreferences = null;
        void CapturePreferences()
        {
            if (!(value is DCFApixels.WhimTex.WhimTexWindow window)) return;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(DCFApixels.WhimTex.WhimTexWindow);
            var session = type.GetField("toolPreferences", flags).GetValue(window) as System.IDisposable;
            var brush = (UnityEditor.EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(
                typeof(UnityEditor.EditorApplication.CallbackFunction), window, type.GetMethod("RestoreBrushTipAfterReload", flags));
            var projectBrush = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), window, type.GetMethod("RestoreBrushTipAfterReload", flags));
            disposePreferences = () =>
            {
                try
                {
                    session?.Dispose();
                    if (session != null && !(bool)session.GetType().GetField("disposed", flags).GetValue(session))
                        throw new System.InvalidOperationException("Owned reload preference session is still live; cleanup uncertain.");
                }
                finally { UnityEditor.EditorApplication.delayCall -= brush; UnityEditor.EditorApplication.projectChanged -= projectBrush; }
            };
        }
        Drain(new System.Action[] { CapturePreferences, () => UnityEditor.Undo.ClearUndo(value),
            () => { if (value != null) UnityEngine.Object.DestroyImmediate(value); },
            () => disposePreferences?.Invoke() });
    }
    public static void ValidateAssetDirectory(string runId, bool creating)
    {
        if (!System.Guid.TryParseExact(runId, "N", out _)) throw new System.ArgumentException("Owned GUID required.");
        string assets = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath);
        string folder = System.IO.Path.GetFullPath("Assets/WhimTexTestMigration/" + runId);
        string expected = System.IO.Path.Combine(assets, "WhimTexTestMigration", runId);
        if (!string.Equals(folder, expected, System.StringComparison.OrdinalIgnoreCase))
            throw new System.IO.IOException("Asset directory is not in the exact Editor project's Assets root.");
        for (string current = folder; current != null; current = System.IO.Path.GetDirectoryName(current))
        {
            if ((System.IO.Directory.Exists(current) || System.IO.File.Exists(current))
                && (System.IO.File.GetAttributes(current) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new System.IO.IOException("Refusing redirected owned asset path: " + current);
            if (string.Equals(current, assets, System.StringComparison.OrdinalIgnoreCase)) break;
        }
        if (creating && (System.IO.Directory.Exists(folder) || System.IO.File.Exists(folder) || System.IO.File.Exists(folder + ".meta")))
            throw new System.IO.IOException("Fresh owned asset directory/meta already exists.");
        if (!creating && System.IO.Directory.Exists(folder))
            foreach (string child in System.IO.Directory.EnumerateFileSystemEntries(folder, "*", System.IO.SearchOption.TopDirectoryOnly))
                if ((System.IO.File.GetAttributes(child) & System.IO.FileAttributes.ReparsePoint) != 0 || System.IO.Directory.Exists(child))
                    throw new System.IO.IOException("Unexpected nested or redirected reload fixture; refusing directory deletion: " + child);
    }

    public static void Begin(string kind, string runId, WhimTex.Tests.TestContext context)
    {
        string key = Key(kind, runId);
        context.True(UnityEditor.SessionState.GetString(key, "").Length == 0,
            "Duplicate Begin must not replace an existing reload fixture.");
        var selected = UnityEditor.Selection.objects;
        var ids = new string[selected.Length];
        for (int i = 0; i < selected.Length; i++) ids[i] = Identity(selected[i]);
        // Use primitive arrays, as for selection, instead of ephemeral nested DTOs.
        var preferenceValues = new string[PreferenceKeys.Length];
        var preferenceExisted = new bool[PreferenceKeys.Length];
        for (int i = 0; i < PreferenceKeys.Length; i++)
        {
            preferenceExisted[i] = UnityEditor.EditorPrefs.HasKey(PreferenceKeys[i]);
            preferenceValues[i] = UnityEditor.EditorPrefs.GetString(PreferenceKeys[i]);
        }
        Write(key, new Snapshot { version = 5, runId = runId, selection = ids, compilationErrors = new string[0],
            preferenceKeys = (string[])PreferenceKeys.Clone(), preferenceValues = preferenceValues, preferenceExisted = preferenceExisted,
            active = Identity(UnityEditor.Selection.activeObject),
            focus = Identity(UnityEditor.EditorWindow.focusedWindow) });
    }

    public static void Arm(string kind, string runId)
    {
        string key = Key(kind, runId);
        var state = Read(key);
        if (state.armed || System.AppDomain.CurrentDomain.GetData(key) != null)
            throw new System.InvalidOperationException("Reload marker already armed.");
        UnityEditor.AssemblyReloadEvents.AssemblyReloadCallback callback = null;
        callback = () => {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= callback;
            var current = Read(key);
            current.beforeReload++;
            Write(key, current);
            // Leave the BCL delegate slot intact: Verify must see that the old AppDomain is gone.
        };
        state.armed = true;
        Write(key, state);
        System.AppDomain.CurrentDomain.SetData(key, callback);
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += callback;
    }

    public static string Trigger(string kind, string runId) => WhimTex.Tests.TestContext.Run("Owned deferred native compilation acknowledgement", context => {
        string key = Key(kind, runId);
        var state = Read(key);
        context.True(state.runId == runId && state.armed, "Same armed GUID owns the native compilation request.");
        context.True(!state.triggered && !state.requestQueued && state.requestCount == 0 && state.beforeReload == 0,
            "Duplicate/native-after-reload trigger is rejected; never issue a fallback compilation.");
        context.True(System.AppDomain.CurrentDomain.GetData(key) != null && System.AppDomain.CurrentDomain.GetData(key + ".request") == null,
            "Old domain marker is armed and no deferred request is already installed.");
        context.True(!UnityEditor.EditorApplication.isCompiling && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode,
            "Native compilation request starts only from a stopped, non-compiling Editor.");
        // Return the structured acknowledgement synchronously. A later main-thread update,
        // at least one second later, performs the request; no reload interrupts this body.
        double notBefore = UnityEditor.EditorApplication.timeSinceStartup + 1.0;
        UnityEditor.EditorApplication.CallbackFunction callback = null;
        callback = () => {
            if (UnityEditor.EditorApplication.timeSinceStartup < notBefore) return;
            UnityEditor.EditorApplication.update -= callback;
            System.AppDomain.CurrentDomain.SetData(key + ".request", null);
            var current = Read(key);
            current.requestQueued = false;
            try {
                if (current.requestCount != 0 || current.beforeReload != 0 || !current.armed)
                    throw new System.InvalidOperationException("Deferred request ownership/one-shot guard failed.");
                if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new System.InvalidOperationException("Editor became busy; no duplicate compilation was requested.");
                System.Action<string, UnityEditor.Compilation.CompilerMessage[]> errors = (assembly, messages) => {
                    var snapshot = Read(key);
                    var failures = new System.Collections.Generic.List<string>(snapshot.compilationErrors);
                    foreach (var message in messages)
                        if (message.type == UnityEditor.Compilation.CompilerMessageType.Error)
                            failures.Add(assembly + ": " + message.message);
                    snapshot.compilationErrors = failures.ToArray();
                    Write(key, snapshot);
                };
                System.AppDomain.CurrentDomain.SetData(key + ".compilation", errors);
                UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += errors;
                current.requestCount = 1;
                Write(key, current); // Persist BEFORE the public request; never retry after a thrown/unknown call.
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            }
            catch (System.Exception error) { current.requestError = error.ToString(); Write(key, current); }
        };
        state.triggered = true;
        state.requestQueued = true;
        Write(key, state);
        System.AppDomain.CurrentDomain.SetData(key + ".request", callback);
        UnityEditor.EditorApplication.update += callback;
    });

    public static string ReloadPoll(string kind, string runId)
    {
        var context = new WhimTex.Tests.TestContext();
        var result = new PollResult { status = "failed", message = "Owned real reload markers", failures = new string[0], compilationErrors = new string[0] };
        try {
            string key = Key(kind, runId);
            var state = Read(key);
            result.requestQueued = state.requestQueued;
            result.requestCount = state.requestCount;
            result.beforeReload = state.beforeReload;
            result.oldDomainGone = System.AppDomain.CurrentDomain.GetData(key) == null;
            result.requestError = state.requestError;
            result.compilationErrors = state.compilationErrors;
            context.True(state.runId == runId && state.armed && state.triggered, "Same armed, triggered persisted GUID is polled.");
            context.True(state.requestCount >= 0 && state.requestCount <= 1 && state.beforeReload >= 0 && state.beforeReload <= 1,
                "At most one actual native request and before-reload event.");
            context.True(string.IsNullOrEmpty(state.requestError), "Deferred native request failed: " + state.requestError);
            context.True(state.compilationErrors.Length == 0, "Native compilation errors: " + string.Join("\n", state.compilationErrors));
            context.True(!result.oldDomainGone || state.beforeReload == 1, "Old domain cannot disappear without the persisted before-reload marker.");
            bool done = state.requestCount == 1 && !state.requestQueued && state.beforeReload == 1 && result.oldDomainGone;
            result.status = done ? "passed" : "running";
            result.message = done ? "One native request and actual domain reload confirmed" : "Waiting for owned native request/domain reload (not a CLI status)";
        }
        catch (System.Exception error) { result.failures = new[] { error.ToString() }; }
        result.checks = context.Checks;
        return UnityEngine.JsonUtility.ToJson(result);
    }

    public static void Verify(string kind, string runId, WhimTex.Tests.TestContext context)
    {
        string key = Key(kind, runId);
        var state = Read(key);
        context.True(state.runId == runId && state.armed, "Same GUID fixture was armed before the requested reload.");
        context.True(state.triggered && state.requestCount == 1 && !state.requestQueued && string.IsNullOrEmpty(state.requestError) && state.compilationErrors.Length == 0,
            "One native request completed without reported compilation errors.");
        context.Equal(1, state.beforeReload, "Supported beforeAssemblyReload event actually ran once.");
        context.True(System.AppDomain.CurrentDomain.GetData(key) == null,
            "Old AppDomain delegate state disappeared: CreateGUI or up_to_date alone is not a reload.");
    }

    public static string Cleanup(string kind, string runId, System.Action fixtureCleanup)
    {
        var failures = new System.Collections.Generic.List<string>();
        var context = new WhimTex.Tests.TestContext();
        string key;
        try { key = Key(kind, runId); }
        catch (System.Exception error) { return WhimTex.Tests.TestContext.Result("failed", 0, "Reload cleanup", error.ToString()).ToJson(); }
        // Cancel actual deferred work FIRST, including when the fixture/body failed. This
        // does not claim cancellation of a compilation already issued to the native Editor.
        try {
            Drain(new System.Action[] {
                () => {
                    var request = System.AppDomain.CurrentDomain.GetData(key + ".request") as UnityEditor.EditorApplication.CallbackFunction;
                    if (request != null) UnityEditor.EditorApplication.update -= request;
                    System.AppDomain.CurrentDomain.SetData(key + ".request", null);
                },
                () => {
                    var errors = System.AppDomain.CurrentDomain.GetData(key + ".compilation") as System.Action<string, UnityEditor.Compilation.CompilerMessage[]>;
                    if (errors != null) UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished -= errors;
                    System.AppDomain.CurrentDomain.SetData(key + ".compilation", null);
                },
                () => {
                    if (UnityEditor.SessionState.GetString(key, "").Length == 0) return;
                    var state = Read(key);
                    state.requestQueued = false;
                    Write(key, state);
                    // A throwing earlier beforeAssemblyReload subscriber can skip our marker.
                    // Loss of the old domain plus the idle guard below is enough for cleanup,
                    // but Verify still requires the marker and must report that run as failed.
                    if (state.requestCount == 1 && System.AppDomain.CurrentDomain.GetData(key) != null
                        && string.IsNullOrEmpty(state.requestError) && state.compilationErrors.Length == 0)
                        throw new System.InvalidOperationException("Native request already issued without a completed domain reload; cannot cancel it or delete owned fixtures.");
                }
            });
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Editor is not idle; detached pending callbacks but retained owned fixtures for recovery.");
        }
        catch (System.Exception error) { return WhimTex.Tests.TestContext.Result("failed", context.Checks, "Deferred callback cleanup/idle guard", error.ToString()).ToJson(); }
        try { fixtureCleanup(); } catch (System.Exception error) { failures.Add(error.ToString()); }
        try
        {
            var callback = System.AppDomain.CurrentDomain.GetData(key) as UnityEditor.AssemblyReloadEvents.AssemblyReloadCallback;
            if (callback != null) UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= callback;
            System.AppDomain.CurrentDomain.SetData(key, null);
        }
        catch (System.Exception error) { failures.Add(error.ToString()); }
        try
        {
            if (UnityEditor.SessionState.GetString(key, "").Length != 0)
            {
                var state = Read(key);
                if (state.runId != runId) throw new System.InvalidOperationException("Reload ownership mismatch.");
                // Body-owned windows/sessions must be terminal before this point. A failed
                // fixture cleanup retains the snapshot and reports uncertainty, never PASS.
                for (int i = 0; i < PreferenceKeys.Length; i++)
                    try
                    {
                        if (state.preferenceExisted[i]) UnityEditor.EditorPrefs.SetString(state.preferenceKeys[i], state.preferenceValues[i]);
                        else UnityEditor.EditorPrefs.DeleteKey(state.preferenceKeys[i]);
                    }
                    catch (System.Exception error) { failures.Add(error.ToString()); }
                var loaded = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                var selected = new System.Collections.Generic.List<UnityEngine.Object>();
                foreach (string id in state.selection)
                {
                    var value = Resolve(id, loaded);
                    if (value != null) selected.Add(value);
                }
                UnityEditor.Selection.objects = selected.ToArray();
                var active = Resolve(state.active, loaded);
                if (active != null) UnityEditor.Selection.activeObject = active;
                var focus = Resolve(state.focus, loaded) as UnityEditor.EditorWindow;
                if (focus != null && UnityEditor.EditorWindow.focusedWindow != focus) focus.Focus();
                for (int i = 0; i < PreferenceKeys.Length; i++)
                    if (UnityEditor.EditorPrefs.HasKey(state.preferenceKeys[i]) != state.preferenceExisted[i] ||
                        (state.preferenceExisted[i] && UnityEditor.EditorPrefs.GetString(state.preferenceKeys[i]) != state.preferenceValues[i]))
                        failures.Add("Preference changed after restoration/focus: " + state.preferenceKeys[i] + "; cleanup uncertain.");
                // Retain the snapshot on any fixture-cleanup failure for public GUID recovery.
                if (failures.Count == 0) UnityEditor.SessionState.EraseString(key);
            }
        }
        catch (System.Exception error) { failures.Add(error.ToString()); }
        if (failures.Count == 0)
        {
            try {
                context.True(UnityEditor.SessionState.GetString(key, "").Length == 0, "Owned reload marker erased by cleanup.");
                context.True(System.AppDomain.CurrentDomain.GetData(key) == null, "Owned before-reload delegate slot cleared by cleanup.");
                context.True(System.AppDomain.CurrentDomain.GetData(key + ".request") == null && System.AppDomain.CurrentDomain.GetData(key + ".compilation") == null,
                    "Deferred request and native compilation diagnostic callbacks detached and released.");
            } catch (System.Exception error) { failures.Add(error.ToString()); }
        }
        return WhimTex.Tests.TestContext.Result(failures.Count == 0 ? "passed" : "failed", context.Checks,
            "Owned " + kind + " reload cleanup (independent of the primary verdict)", failures.ToArray()).ToJson();
    }
}
