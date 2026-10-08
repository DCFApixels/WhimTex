// Test-owned support. Every input is compiled with this source; no production/Legacy dependency.
public static class UnityBRun
{
    // Native Unity 6000.7 Close requires a shown host. Always destroy owned objects even
    // if a genuine shown-window Close fails, and retain every cleanup error.
    public static void CloseOwned(UnityEditor.EditorWindow window)
    {
        if (window == null) return;
        var failures = new System.Collections.Generic.List<System.Exception>();
        try
        {
            if (window.rootVisualElement.panel != null)
            {
                try { window.DiscardChanges(); } catch (System.Exception error) { failures.Add(error); }
                try { window.Close(); } catch (System.Exception error) { failures.Add(error); }
            }
        }
        catch (System.Exception error) { failures.Add(error); }
        finally
        {
            try { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
            catch (System.Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new System.AggregateException("Owned window cleanup failed", failures);
    }
    static WhimTex.Tests.TestContext context;
    static UnityBOwned owned;
    static System.Threading.CancellationToken token;
    static readonly object assertionLock = new object();

    public static void Check(bool condition, string message)
    {
        token.ThrowIfCancellationRequested();
        lock (assertionLock) context.True(condition, message);
    }
    public static void Fail(string message) => Check(false, message);
    public static string Run(string name, System.Action body)
    {
        string skipped = null; bool cleaned = false;
        int checks = 0;
        string result = WhimTex.Tests.TestContext.Run(name, value =>
        {
            var previousContext = context; var previousOwned = owned; var previousToken = token;
            try {
            context = value; token = default;
            UnityBSharedState shared = null; owned = null;
            System.Exception primary = null;
            try { shared = new UnityBSharedState(); owned = new UnityBOwned(); body(); }
            catch (UnityBSkipException error) { skipped = error.Message; }
            catch (System.Exception error) { primary = error; }
            finally { FinishScope(shared, primary); }
            cleaned = true;
            } finally { checks = value.Checks; context = previousContext; owned = previousOwned; token = previousToken; }
        });
        return skipped != null && cleaned ? WhimTex.Tests.TestContext.Result("skipped", checks, skipped).ToJson() : result;
    }
    public static string Skip(string message) => WhimTex.Tests.TestContext.Result("skipped", 0, message).ToJson();
    public static string AssetPath(string leaf) => owned.AssetPath(leaf);
    public static string TempPath(string leaf) => owned.TempPath(leaf);
    public static string EvidencePath(string leaf) => owned.EvidencePath(leaf);
    public static void EnsureFolder(string path) => owned.EnsureFolder(path);
    public static bool IsOwnedTemp(string path) => owned.IsOwnedTemp(path);
    public static void DeleteTemp(string path) => owned.DeleteTemp(path);
    public static T Track<T>(T value) where T : UnityEngine.Object => owned.Track(value);
    public static T Create<T>() where T : UnityEngine.ScriptableObject
    {
        T value = Track(UnityEngine.ScriptableObject.CreateInstance<T>());
        if (value is DCFApixels.WhimTex.WhimTexWindow)
        {
            var field = typeof(DCFApixels.WhimTex.WhimTexWindow).GetField("activeDocument", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Track(field.GetValue(value) as UnityEngine.Object);
        }
        return value;
    }
    public static UnityEngine.ScriptableObject Create(System.Type type) => Track(UnityEngine.ScriptableObject.CreateInstance(type));
    public static System.Threading.Tasks.Task Delay(int milliseconds) => System.Threading.Tasks.Task.Delay(milliseconds, token);
    public static void CheckCancellation() => token.ThrowIfCancellationRequested();
    public static async System.Threading.Tasks.Task NextUpdate()
    {
        var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
        using var timer = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token);
        void Tick() { UnityEditor.EditorApplication.update -= Tick; completion.TrySetResult(true); }
        UnityEditor.EditorApplication.update += Tick;
        try
        {
            var timeout = System.Threading.Tasks.Task.Delay(5000, timer.Token);
            var first = await System.Threading.Tasks.Task.WhenAny(completion.Task, timeout);
            token.ThrowIfCancellationRequested();
            if (first != completion.Task) throw new System.TimeoutException("Editor did not update.");
            await completion.Task;
        }
        finally { UnityEditor.EditorApplication.update -= Tick; timer.Cancel(); }
    }
    static void FinishScope(UnityBSharedState shared, System.Exception primary, object[] bridge = null)
    {
        var errors = new System.Collections.Generic.List<System.Exception>();
        var cleanupErrors = new System.Collections.Generic.List<string>();
        if (primary != null) errors.Add(primary);
        try { owned?.Dispose(); } catch (System.Exception error) { errors.Add(error); cleanupErrors.Add(error.ToString()); }
        try { shared?.Dispose(); } catch (System.Exception error) { errors.Add(error); cleanupErrors.Add(error.ToString()); }
        if (bridge != null) bridge[2] = cleanupErrors.Count == 0 ? null : string.Join("\n", cleanupErrors);
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new System.AggregateException("Body and/or owned cleanup failed", errors);
    }

    static string Key(string runId)
    {
        if (!System.Guid.TryParseExact(runId, "N", out _) && !System.Guid.TryParseExact(runId, "D", out _))
            throw new System.ArgumentException("A runner-owned GUID is required.");
        return "WhimTex.Tests.UnityB." + runId;
    }
    // BCL-typed bridge survives separate ephemeral Pipeline assemblies. Poll never reads their statics.
    public static string Start(string runId, string name, System.Func<System.Threading.Tasks.Task> body)
    {
        string key = Key(runId);
        if (System.AppDomain.CurrentDomain.GetData(key) != null) return Failure("Run already exists: " + runId);
        var cancellation = new System.Threading.CancellationTokenSource();
        var state = new object[] { cancellation, null, null, null };
        System.AppDomain.CurrentDomain.SetData(key, state);
        UnityEditor.SessionState.SetString(key, WhimTex.Tests.TestContext.Result("running", 0, name).ToJson());
        state[1] = ExecuteAsync(key, name, cancellation.Token, body, state);
        return Poll(runId);
    }
    static async System.Threading.Tasks.Task ExecuteAsync(string key, string name, System.Threading.CancellationToken cancellation, System.Func<System.Threading.Tasks.Task> body, object[] bridge)
    {
        var value = new WhimTex.Tests.TestContext();
        var previousContext = context; var previousOwned = owned; var previousToken = token;
        context = value; token = cancellation;
        string status = "passed", message = name;
        string[] failures = System.Array.Empty<string>();
        try
        {
            UnityBSharedState shared = null; owned = null;
            System.Exception primary = null;
            try { shared = new UnityBSharedState(); owned = new UnityBOwned(); await body(); } catch (System.Exception error) { primary = error; }
            finally { FinishScope(shared, primary, bridge); }
            cancellation.ThrowIfCancellationRequested();
            if (value.Checks == 0) throw new System.InvalidOperationException("No assertions were executed.");
        }
        catch (System.OperationCanceledException) { status = "cancelled"; }
        catch (UnityBSkipException error) { status = "skipped"; message = error.Message; }
        catch (System.Exception error) { status = "failed"; failures = new[] { error.ToString() }; }
        finally { context = previousContext; owned = previousOwned; token = previousToken; }
        string result = WhimTex.Tests.TestContext.Result(status, value.Checks, message, failures).ToJson();
        bridge[3] = result;
        UnityEditor.SessionState.SetString(key, result);
    }
    static string Failure(string message) => WhimTex.Tests.TestContext.Result("failed", 0, message, message).ToJson();
    public static string Poll(string runId)
    {
        string key = Key(runId);
        if (System.AppDomain.CurrentDomain.GetData(key) == null) return Failure("No owned bridge for run " + runId);
        return UnityEditor.SessionState.GetString(key, Failure("No state for run " + runId));
    }
    public static async System.Threading.Tasks.Task<string> Cancel(string runId)
    {
        string key = Key(runId);
        var state = System.AppDomain.CurrentDomain.GetData(key) as object[];
        if (state == null) return Failure("Cannot confirm termination of unknown run.");
        ((System.Threading.CancellationTokenSource)state[0]).Cancel();
        // Body must unwind its finally and drain jobs before cancellation is acknowledged.
        await (System.Threading.Tasks.Task)state[1];
        var result = UnityEngine.JsonUtility.FromJson<WhimTex.Tests.TestResult>(Poll(runId));
        if (result.status == "failed") return result.ToJson();
        return WhimTex.Tests.TestContext.Result("cancelled", result.checks, "Owned work stopped and cleanup completed").ToJson();
    }
    public static string Cleanup(string runId)
    {
        string key = Key(runId);
        var state = System.AppDomain.CurrentDomain.GetData(key) as object[];
        if (state != null && !((System.Threading.Tasks.Task)state[1]).IsCompleted)
            return WhimTex.Tests.TestContext.Result("failed", 0, "Owned task still running; cancel and wait before cleanup", "Task is not drained").ToJson();
        if (state == null) return Failure("No owned bridge for cleanup.");
        string primary = state[3] as string ?? Poll(runId);
        var cleanupErrors = new System.Collections.Generic.List<string>();
        if (state[2] is string scopeError) cleanupErrors.Add(scopeError);
        try { ((System.Threading.CancellationTokenSource)state[0]).Dispose(); }
        catch (System.Exception error) { cleanupErrors.Add(error.ToString()); }
        finally { System.AppDomain.CurrentDomain.SetData(key, null); UnityEditor.SessionState.EraseString(key); }
        if (cleanupErrors.Count != 0) return WhimTex.Tests.TestContext.Result("failed", 0, "Actual owned cleanup failure; primary verdict: " + primary, cleanupErrors.ToArray()).ToJson();
        // A failed body remains the primary Poll verdict, not an uncertain cleanup failure.
        return WhimTex.Tests.TestContext.Result("passed", 0, "Owned cleanup completed; primary verdict: " + primary).ToJson();
    }
}

public sealed class UnityBSkipException : System.Exception
{ public UnityBSkipException(string message) : base(message) {} }

public static class UnityBTemp
{
    const string Prefix = "WhimTexTestMigration-";
    static readonly System.StringComparison PathComparison = System.IO.Path.DirectorySeparatorChar == '\\'
        ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
    static string FullPath(string path) => System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    public static string Root(string runId)
    {
        if (!System.Guid.TryParseExact(runId, "N", out _)) throw new System.ArgumentException("Owned GUID required");
        return FullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp/WhimTex", Prefix + runId));
    }
    internal static bool IsChild(string ownedRoot, string target) => FullPath(target).StartsWith(FullPath(ownedRoot) + System.IO.Path.DirectorySeparatorChar, PathComparison);
    internal static void CheckPath(string path, bool directory = false)
    {
        string full = System.IO.Path.GetFullPath(path);
        var ancestors = new System.Collections.Generic.Stack<string>();
        for (string current = full; current != null; current = System.IO.Path.GetDirectoryName(current))
            ancestors.Push(current);
        while (ancestors.Count != 0)
        {
            string current = ancestors.Pop();
            try
            {
                var attributes = System.IO.File.GetAttributes(current); RejectLink(current, attributes);
                if ((directory || current != full) && (attributes & System.IO.FileAttributes.Directory) == 0)
                    throw new System.IO.IOException("Owned temporary directory was replaced by a file: " + current);
            }
            catch (System.IO.FileNotFoundException) {}
            catch (System.IO.DirectoryNotFoundException) {}
        }
    }
    public static void Delete(string ownedRoot, string target)
    {
        string root = FullPath(ownedRoot);
        string full = FullPath(target);
        string name = System.IO.Path.GetFileName(root);
        if (!name.StartsWith(Prefix, System.StringComparison.Ordinal) || !System.Guid.TryParseExact(name.Substring(Prefix.Length), "N", out _)
            || !string.Equals(root, Root(name.Substring(Prefix.Length)), PathComparison))
            throw new System.IO.IOException("Not an owned GUID temporary root");
        if (!string.Equals(full, root, PathComparison) && !IsChild(root, full))
            throw new System.IO.IOException("Temporary cleanup target escaped its owned root");
        // Validate all existing ancestors before inspecting descendants. Never traverse a junction.
        CheckPath(full, true);
        if (!System.IO.Directory.Exists(full))
        { if (System.IO.File.Exists(full)) throw new System.IO.IOException("Owned temporary directory was replaced by a file"); return; }
        var pending = new System.Collections.Generic.Stack<string>(); pending.Push(full);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            RejectLink(directory, System.IO.File.GetAttributes(directory));
            foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = System.IO.File.GetAttributes(entry); RejectLink(entry, attributes);
                string child = System.IO.Path.GetFullPath(entry);
                if (!IsChild(root, child))
                    throw new System.IO.IOException("Descendant escaped owned temporary root");
                if ((attributes & System.IO.FileAttributes.Directory) != 0) pending.Push(child);
            }
        }
        // Only this checked, absolute target is recursively deleted.
        CheckPath(full, true);
        System.IO.Directory.Delete(full, true);
    }
    static void RejectLink(string path, System.IO.FileAttributes attributes)
    {
        if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
            throw new System.IO.IOException("Refusing recursive cleanup through a junction/symlink: " + path);
    }
}


// Persistent manual reload fixtures: caller supplies the same N-format GUID in both phases.
public static class UnityBManualAssets
{
    public static string Create(string runId)
    {
        if (!System.Guid.TryParseExact(runId, "N", out _)) throw new System.ArgumentException("Owned GUID required");
        string path = "Assets/WhimTexTestMigration/" + runId;
        if (System.IO.Directory.Exists(path) || UnityEditor.AssetDatabase.IsValidFolder(path)) throw new System.IO.IOException("Owned directory already exists");
        if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/WhimTexTestMigration")) UnityEditor.AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration");
        UnityEditor.AssetDatabase.CreateFolder("Assets/WhimTexTestMigration", runId);
        if (!UnityEditor.AssetDatabase.IsValidFolder(path)) throw new System.IO.IOException("Owned directory creation failed");
        return path;
    }
}

// Package-owned caches are detached, not destroyed. Restore original values and LRU node identity.
public sealed class UnityBSharedState : System.IDisposable
{
    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    readonly System.Collections.IDictionary cache, nodes, known;
    readonly System.Collections.Generic.LinkedList<string> order;
    readonly System.Collections.Generic.List<System.Collections.DictionaryEntry> savedCache = new System.Collections.Generic.List<System.Collections.DictionaryEntry>();
    readonly System.Collections.Generic.List<System.Collections.DictionaryEntry> savedNodes = new System.Collections.Generic.List<System.Collections.DictionaryEntry>();
    readonly System.Collections.Generic.List<System.Collections.DictionaryEntry> savedKnown = new System.Collections.Generic.List<System.Collections.DictionaryEntry>();
    readonly System.Collections.Generic.List<System.Collections.Generic.LinkedListNode<string>> savedOrder = new System.Collections.Generic.List<System.Collections.Generic.LinkedListNode<string>>();
    readonly System.Reflection.FieldInfo bytesField;
    readonly System.Reflection.FieldInfo spareField, idleField;
    readonly object spare, idle;
    readonly long bytes;
    public UnityBSharedState()
    {
        var type = typeof(DCFApixels.WhimTex.WhimTexDocumentContainer);
        cache = (System.Collections.IDictionary)type.GetField("CompressedCache", F).GetValue(null);
        nodes = (System.Collections.IDictionary)type.GetField("CompressedCacheNodes", F).GetValue(null);
        order = (System.Collections.Generic.LinkedList<string>)type.GetField("CompressedCacheOrder", F).GetValue(null);
        bytesField = type.GetField("_compressedCacheBytes", F); bytes = (long)bytesField.GetValue(null);
        lock (cache)
        {
            foreach (System.Collections.DictionaryEntry item in cache) savedCache.Add(item);
            foreach (System.Collections.DictionaryEntry item in nodes) savedNodes.Add(item);
            for (var node = order.First; node != null; node = node.Next) savedOrder.Add(node);
        }
        var serializer = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        known = (System.Collections.IDictionary)serializer.GetField("KnownTypes", F).GetValue(null);
        foreach (System.Collections.DictionaryEntry item in known) savedKnown.Add(item);
        var histogram = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace", true);
        spareField = histogram.GetField("spare", F); idleField = histogram.GetField("idleSince", F);
        spare = spareField.GetValue(null); idle = idleField.GetValue(null);
        // Resolve/snapshot every dependency before mutating borrowed state.
        lock (cache) { cache.Clear(); nodes.Clear(); order.Clear(); bytesField.SetValue(null, 0L); }
        spareField.SetValue(null, null);
    }
    public void Dispose()
    {
        lock (cache)
        {
            cache.Clear(); nodes.Clear(); order.Clear();
            foreach (var item in savedCache) cache.Add(item.Key, item.Value);
            foreach (var item in savedNodes) nodes.Add(item.Key, item.Value);
            foreach (var node in savedOrder) order.AddLast(node);
            bytesField.SetValue(null, bytes);
        }
        known.Clear(); foreach (var item in savedKnown) known.Add(item.Key, item.Value);
        try { (spareField.GetValue(null) as System.IDisposable)?.Dispose(); }
        finally { spareField.SetValue(null, spare); idleField.SetValue(null, idle); }
    }
}

public static class UnityBHealing
{
    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    static readonly System.Type Type = typeof(DCFApixels.WhimTex.WhimTexWindow);
    public static void RequireIdle()
    {
        var worker = Type.GetField("healingWorker", F).GetValue(null) as System.Threading.Tasks.Task;
        if (worker != null && !worker.IsCompleted) throw new UnityBSkipException("A borrowed healing worker is active; not verified.");
    }
    public static async System.Threading.Tasks.Task StopAndDrain(DCFApixels.WhimTex.WhimTexWindow window)
    {
        // This test checked idle before starting; only its own window can own this worker.
        var worker = Type.GetField("healingWorker", F).GetValue(null) as System.Threading.Tasks.Task;
        Type.GetMethod("CancelHealing", F).Invoke(window, null);
        if (worker != null)
        {
            try { await worker; }
            catch (System.OperationCanceledException) {}
        }
    }
}


public sealed class UnityBOwned : System.IDisposable
{
    readonly string id = System.Guid.NewGuid().ToString("N");
    readonly System.Collections.Generic.List<UnityEngine.Object> objects = new System.Collections.Generic.List<UnityEngine.Object>();
    readonly UnityEngine.RenderTexture active = UnityEngine.RenderTexture.active;
    readonly bool srgb = UnityEngine.GL.sRGBWrite;
    readonly UnityEngine.Object[] selection = UnityEditor.Selection.objects;
    readonly UnityEditor.EditorWindow focus = UnityEditor.EditorWindow.focusedWindow;
    string assets, temporary;
    public T Track<T>(T value) where T : UnityEngine.Object
    { if (value != null && !objects.Contains(value)) objects.Add(value); return value; }
    public string AssetPath(string leaf)
    {
        if (assets == null)
        {
            string candidate = "Assets/WhimTexTestMigration/" + id;
            if (System.IO.Directory.Exists(candidate) || System.IO.File.Exists(candidate) || System.IO.File.Exists(candidate + ".meta") || UnityEditor.AssetDatabase.IsValidFolder(candidate))
                throw new System.IO.IOException("Owned GUID directory already exists.");
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/WhimTexTestMigration"))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration");
            UnityEditor.AssetDatabase.CreateFolder("Assets/WhimTexTestMigration", id);
            assets = candidate;
            if (!UnityEditor.AssetDatabase.IsValidFolder(assets)) throw new System.IO.IOException("Owned asset directory creation failed.");
        }
        return Child(assets, leaf);
    }
    public string TempPath(string leaf)
    {
        if (temporary == null)
        {
            string candidate = UnityBTemp.Root(id);
            UnityBTemp.CheckPath(candidate, true);
            if (System.IO.Directory.Exists(candidate) || System.IO.File.Exists(candidate)) throw new System.IO.IOException("Owned temporary directory already exists.");
            temporary = candidate;
            System.IO.Directory.CreateDirectory(temporary);
        }
        UnityBTemp.CheckPath(temporary, true);
        if (leaf.Length == 0) return temporary;
        string path = System.IO.Path.GetFullPath(Child(temporary, leaf)).Replace('\\', '/');
        if (!UnityBTemp.IsChild(temporary, path)) throw new System.IO.IOException("Fixture path escaped its owned directory.");
        UnityBTemp.CheckPath(path);
        UnityBTemp.CheckPath(System.IO.Path.GetDirectoryName(path), true);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        UnityBTemp.CheckPath(path);
        return path;
    }
    public string EvidencePath(string leaf)
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Temp/WhimTex/diagnostics", id));
        string path = Child(root, leaf);
        for (string current = path; current != null; current = System.IO.Path.GetDirectoryName(current))
            if ((System.IO.Directory.Exists(current) || System.IO.File.Exists(current))
                && (System.IO.File.GetAttributes(current) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new System.IO.IOException("Redirected diagnostic evidence path.");
        if (System.IO.File.Exists(path)) throw new System.IO.IOException("Diagnostic evidence already exists.");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        return path; // GUID evidence is retained, unlike disposable execution scratch files.
    }
    public bool IsOwnedTemp(string path)
    {
        if (temporary == null || temporary != UnityBTemp.Root(id) || !UnityBTemp.IsChild(temporary, path)) return false;
        UnityBTemp.CheckPath(path);
        return true;
    }
    public void DeleteTemp(string path)
    {
        string expected = UnityBTemp.Root(id);
        if (temporary == null || System.IO.Path.GetFullPath(temporary) != System.IO.Path.GetFullPath(expected)) throw new System.IO.IOException("Unrecognized owned temporary root");
        UnityBTemp.Delete(temporary, path);
    }
    public void EnsureFolder(string path)
    {
        if (assets == null || !System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath(assets) + System.IO.Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
            throw new System.IO.IOException("Folder is not inside this run's asset root.");
        string relative = path.Substring(assets.Length).TrimStart('/', '\\');
        string parent = assets;
        foreach (string segment in relative.Replace('\\', '/').Split('/'))
        { string child = parent + "/" + segment; if (!UnityEditor.AssetDatabase.IsValidFolder(child)) UnityEditor.AssetDatabase.CreateFolder(parent, segment); parent = child; }
    }
    static string Child(string root, string leaf)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, leaf));
        string prefix = System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) throw new System.IO.IOException("Fixture path escaped its owned directory.");
        return System.IO.Path.Combine(root, leaf).Replace('\\', '/');
    }
    public void Dispose()
    {
        var failures = new System.Collections.Generic.List<System.Exception>();
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            var value = objects[i];
            if (value == null || UnityEditor.AssetDatabase.Contains(value)) continue;
            try { UnityEditor.Undo.ClearUndo(value); }
            catch (System.Exception error) { failures.Add(error); }
            try
            {
                if (value is UnityEditor.EditorWindow window) UnityBRun.CloseOwned(window);
                else if (value != null) UnityEngine.Object.DestroyImmediate(value);
            }
            catch (System.Exception error) { failures.Add(error); }
        }
        try { UnityEngine.RenderTexture.active = active; UnityEngine.GL.sRGBWrite = srgb; }
        catch (System.Exception error) { failures.Add(error); }
        try { UnityEditor.Selection.objects = selection; if (focus != null) focus.Focus(); }
        catch (System.Exception error) { failures.Add(error); }
        try
        {
            if (assets != null && System.IO.Directory.Exists(assets))
            {
                if (assets != "Assets/WhimTexTestMigration/" + id) throw new System.IO.IOException("Unsafe asset cleanup.");
                UnityEditor.AssetDatabase.DeleteAsset(assets);
                if (System.IO.Directory.Exists(assets)) throw new System.IO.IOException("Owned asset directory survived cleanup: " + assets);
            }
        }
        catch (System.Exception error) { failures.Add(error); }
        try { if (temporary != null) DeleteTemp(temporary); }
        catch (System.Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new System.AggregateException("UnityB cleanup failed", failures);
    }
}
