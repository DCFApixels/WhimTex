using System;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
#line 1 "Tests~/Framework/TestApi.cs"
// Test-only support source, appended to a reviewed case by the runner. No production assembly.
namespace WhimTex.Tests
{
    [System.Serializable]
    public sealed class TestResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public string ToJson() => UnityEngine.JsonUtility.ToJson(this);
    }

    public sealed class TestContext
    {
        public int Checks { get; private set; }
        public void True(bool condition, string message)
        {
            Checks++;
            if (!condition) throw new System.InvalidOperationException(message);
        }
        public void Equal<T>(T expected, T actual, string message)
            => True(System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual),
                message + ": expected " + expected + ", got " + actual);
        public void Near(double expected, double actual, double tolerance, string message)
            => True(!double.IsNaN(expected) && !double.IsInfinity(expected) && !double.IsNaN(actual)
                && !double.IsInfinity(actual) && !double.IsInfinity(tolerance) && tolerance >= 0
                && System.Math.Abs(expected - actual) <= tolerance,
                message + ": expected " + expected + ", got " + actual + ", tolerance " + tolerance);
        public static string Run(string message, System.Action<TestContext> body)
        {
            var context = new TestContext();
            try
            {
                body(context); // The body's using/finally completes before a passed result is emitted.
                if (context.Checks == 0) throw new System.InvalidOperationException("No assertions were executed.");
                return Result("passed", context.Checks, message).ToJson();
            }
            catch (System.Exception error)
            {
                return Result("failed", context.Checks, message, error.ToString()).ToJson();
            }
        }
        public static TestResult Result(string status, int checks, string message, params string[] failures)
            => new TestResult { status = status, checks = checks, message = message, failures = failures };
    }
}

#line 1 "Tests~/Cases/UnityB/UnityBSupport.cs"
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
        if (value is DCFApixels.WhimTex.TextureCompositorWindow)
        {
            var field = typeof(DCFApixels.WhimTex.TextureCompositorWindow).GetField("compositor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
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
        var serializer = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
        known = (System.Collections.IDictionary)serializer.GetField("KnownTypes", F).GetValue(null);
        foreach (System.Collections.DictionaryEntry item in known) savedKnown.Add(item);
        var histogram = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.SeamlessHistogramWorkspace", true);
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
    static readonly System.Type Type = typeof(DCFApixels.WhimTex.TextureCompositorWindow);
    public static void RequireIdle()
    {
        var worker = Type.GetField("healingWorker", F).GetValue(null) as System.Threading.Tasks.Task;
        if (worker != null && !worker.IsCompleted) throw new UnityBSkipException("A borrowed healing worker is active; not verified.");
    }
    public static async System.Threading.Tasks.Task StopAndDrain(DCFApixels.WhimTex.TextureCompositorWindow window)
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

#line 1 "Tests~/Cases/UnityB/ReloadSupport.cs"
// Test-only reload ownership. SessionState survives a domain reload; AppDomain data does not.
// No reflection into Unity internals and no persistent project script/asset is installed.
public static class UnityBReload
{
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
        if ((kind != "Document" && kind != "Guide") || !System.Guid.TryParseExact(runId, "N", out _))
            throw new System.ArgumentException("Expected Document/Guide and a per-run N-format GUID.");
        return "WhimTex.Tests.UnityB.Reload." + kind + "." + runId;
    }
    static Snapshot Read(string key)
    {
        string json = UnityEditor.SessionState.GetString(key, "");
        if (json.Length == 0) throw new System.InvalidOperationException("Missing owned reload state: " + key);
        var snapshot = UnityEngine.JsonUtility.FromJson<Snapshot>(json);
        if (snapshot == null || snapshot.version != 3 || snapshot.selection == null || snapshot.compilationErrors == null)
            throw new System.InvalidOperationException("Unsupported or malformed reload identity snapshot; do not reinterpret old integer IDs.");
        return snapshot;
    }
    static void Write(string key, Snapshot state) => UnityEditor.SessionState.SetString(key, UnityEngine.JsonUtility.ToJson(state));
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
        Drain(new System.Action[] { () => UnityEditor.Undo.ClearUndo(value),
            () => { if (value != null) UnityEngine.Object.DestroyImmediate(value); } });
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
        Write(key, new Snapshot { version = 3, runId = runId, selection = ids, compilationErrors = new string[0],
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
                    if (state.requestCount == 1 && (state.beforeReload != 1 || System.AppDomain.CurrentDomain.GetData(key) != null)
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
                if (focus != null) focus.Focus();
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

#line 1 "Tests~/Cases/UnityB/DocumentReloadTests.cs"
// Node orchestration: Begin -> one real Unity recompile -> Verify -> public GUID Cleanup.







public static class DocumentReloadTests
{
    [Serializable]
    public sealed class ReloadResult
    {
        public string status;
        public int checks;
        public string message;
        public string[] failures;
        public string coverageBranch;
        public bool partialCoverage;
    }
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static string Key;
    static string RunId;
    static WhimTex.Tests.TestContext context;
    static Type Session => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession");
    static void Check(bool condition, string message) { context.True(condition, message); }
    static string ExecutePrepare()
    {
        Check(string.IsNullOrEmpty(EditorPrefs.GetString(Key, "")), "finish the previous reload probe first");
        Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "stop the user's Live Update before this probe");
        Check(!EditorPrefs.HasKey((string)Session.GetField("RecoveryKey", Any).GetValue(null)),
            "User readable-recovery journal must be resolved before this owned reload probe.");
        UnityBReload.ValidateAssetDirectory(RunId, true);
        string folder = UnityBManualAssets.Create(RunId);
        EditorPrefs.SetString(Key, folder);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.name = RunId;
        doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = 64; doc.height = 32;
        doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.red }));
        string path = WhimTexDocumentFile.Save(doc, folder + "/Reload.tiff");
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = false; importer.SaveAndReimport();
        // Use a real EditorWindow host: a bare ScriptableObject is not restored by Unity
        // across an assembly reload and would make this probe test the wrong lifecycle.
        var window = EditorWindow.CreateWindow<TextureCompositorWindow>();
        window.name = "WhimTex Document Reload " + RunId;
        var previousDefault = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
        typeof(TextureCompositorWindow).GetMethod("SetCompositor", Any).Invoke(window, new object[] { doc });
        if (previousDefault != null && previousDefault != doc) UnityEngine.Object.DestroyImmediate(previousDefault);
        typeof(TextureCompositorWindow).GetMethod("BindDocumentFile", Any).Invoke(window, new object[] { path });
        window.Show();
        EditorPrefs.SetString(Key + ".guid", AssetDatabase.AssetPathToGUID(path));
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.green;
        typeof(TextureCompositor).GetMethod("MarkChanged", Any).Invoke(doc, null);
        Check((bool)Session.GetMethod("Start", Any).Invoke(null, new object[] { doc, path }), "Live Update start");
        return "READY: run Unity recompile, wait for completion, then Verify. " + folder;
    }

    static string ExecuteVerify(out bool carrierOnly)
    {
        carrierOnly = false;
        string folder = EditorPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException("Missing owned marker; real reload was not verified.");
        Check(folder == "Assets/WhimTexTestMigration/" + RunId, "owned test folder");
        string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(Key + ".guid", ""));
        TextureCompositorWindow found = null;
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
            {
                var document = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
                var service = typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentService");
                if ((string)service.GetMethod("PathOf", Any).Invoke(null, new object[] { document }) == path) found = window;
            }
        if (found == null)
        {
            carrierOnly = true;
            // Unity may close utility windows created by an ephemeral test assembly during a
            // domain reload. The carrier/session lifecycle is still verifiable independently.
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored without a surviving utility window");
            var loadedAfterReload = WhimTexDocumentFile.Load(path);
            try { Check(((ColorFillLayerBehaviour)loadedAfterReload.layers[0].Behaviour).color == Color.red, "saved TIFF remains intact after reload"); }
            finally { UnityEngine.Object.DestroyImmediate(loadedAfterReload); }
            return "PARTIAL: carrier/session branch only; surviving-window binding/content/storage-GUID/save oracle was not executed.";
        }
        {
            Check(found != null, "window restored");
            var doc = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(found);
            object[] binding = { doc, null };
            Check((bool)typeof(TextureCompositorWindow).GetMethod("TryGetDocumentFile", Any).Invoke(null, binding) && (string)binding[1] == path, "binding restored after domain reload");
            Check(!((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Read/Write restored across domain reload");
            Check(!(bool)Session.GetProperty("IsLive", Any).GetValue(null), "live session ended before reload");
            Check(doc.width == 64 && doc.height == 32 && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.green, "unsaved document content retained");
            var storageBinding = typeof(TextureCompositor).GetField("documentBinding", Any).GetValue(doc) as UnityEngine.Object;
            Check(storageBinding != null, "storage binding survives reload independently of window fallback");
            WhimTexDocumentFile.Save(doc, path);
            Check((string)storageBinding.GetType().GetField("guid", Any).GetValue(storageBinding) == AssetDatabase.AssetPathToGUID(path), "save after reload retains storage GUID");
            var loaded = WhimTexDocumentFile.Load(path);
            try { Check(((ColorFillLayerBehaviour)loaded.layers[0].Behaviour).color == Color.green, "save after reload persists unsaved contents"); }
            finally { UnityEngine.Object.DestroyImmediate(loaded); }
            return "Surviving-window branch verified; owned cleanup follows separately.";
        }
    }

    static void Bind(string runId, WhimTex.Tests.TestContext value) {
        if (!Guid.TryParseExact(runId, "N", out _)) throw new ArgumentException("Use one stable per-run N-format GUID across the real reload.");
        RunId=runId; Key="WhimTex.Tests.UnityB.DocumentReloadTests."+runId; context=value;
    }
    public static string Begin(string runId) => WhimTex.Tests.TestContext.Run("DocumentReloadTests: Begin", value => {
        var previous = context;
        try {
            Bind(runId,value);
            if (EditorPrefs.HasKey(Key)) throw new InvalidOperationException("Existing owned run must be cleaned first.");
            UnityBReload.Begin("Document",runId,value);
            ExecutePrepare();
            UnityBReload.Arm("Document",runId);
        } finally { context = previous; }
    });
    public static string Trigger(string runId) => UnityBReload.Trigger("Document",runId);
    public static string ReloadPoll(string runId) => UnityBReload.ReloadPoll("Document",runId);
    public static string Verify(string runId)
    {
        bool carrierOnly = false;
        string branch = "unverified";
        string branchMessage = null;
        // Only extend the shared structured payload with coverage facts. Every old Check
        // still executes through TestContext; no text verdict or old PASS parser is used.
        string json = WhimTex.Tests.TestContext.Run("DocumentReloadTests: real reload verification", value => {
            var previous = context;
            try {
                Bind(runId,value); UnityBReload.Verify("Document",runId,value);
                branchMessage = ExecuteVerify(out carrierOnly);
                branch = carrierOnly ? "carrier-only" : "surviving-window";
            } finally { context = previous; }
        });
        var result = JsonUtility.FromJson<ReloadResult>(json);
        result.coverageBranch = branch;
        result.partialCoverage = carrierOnly || branch == "unverified";
        if (branchMessage != null) result.message += ": " + branchMessage;
        return JsonUtility.ToJson(result);
    }
    public static string Cleanup(string runId) => UnityBReload.Cleanup("Document",runId, () => {
        var previous = context;
        try { Bind(runId,null); CleanupOwned(); } finally { context = previous; }
    });
    static void CleanupOwned()
    {
        string folder = EditorPrefs.GetString(Key, "");
        if (folder.Length == 0) return;
        if (folder != "Assets/WhimTexTestMigration/" + RunId) throw new IOException("Ownership marker mismatch; refusing cleanup");
        var steps = new System.Collections.Generic.List<Action>();
        steps.Add(() => {
            string live = (string)Session.GetProperty("LivePath", Any).GetValue(null);
            if (live != null && live.StartsWith(folder + "/", StringComparison.Ordinal)) Session.GetMethod("Stop", Any).Invoke(null, new object[] { "owned reload cleanup" });
        });
        foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
        {
            var doc = (TextureCompositor)typeof(TextureCompositorWindow).GetField("compositor", Any).GetValue(window);
            if (window.name == "WhimTex Document Reload " + RunId || doc != null && doc.name == RunId)
            {
                steps.Add(() => UnityBRun.CloseOwned(window));
                if (doc != null && doc.name == RunId) steps.Add(() => UnityBReload.DestroyOwned(doc));
            }
        }
        foreach (var doc in Resources.FindObjectsOfTypeAll<TextureCompositor>())
            if (doc.name == RunId) steps.Add(() => UnityBReload.DestroyOwned(doc));
        steps.Add(() => {
            UnityBReload.ValidateAssetDirectory(RunId, false);
            string live = (string)Session.GetProperty("LivePath", Any).GetValue(null);
            string recoveryKey = (string)Session.GetField("RecoveryKey", Any).GetValue(null);
            string recoveryPath = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(recoveryKey, ""));
            if (live != null && live.StartsWith(folder + "/", StringComparison.Ordinal)
                || recoveryPath.StartsWith(folder + "/", StringComparison.Ordinal))
                throw new IOException("Owned importer recovery is still pending; retaining its asset directory.");
            if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder)) throw new IOException("Owned reload directory cleanup failed");
        });
        UnityBReload.Drain(steps);
        EditorPrefs.DeleteKey(Key); EditorPrefs.DeleteKey(Key + ".guid");
    }
}
