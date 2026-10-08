// Test-only support; appended after the selected case. No production assembly.
namespace WhimTex.Tests.UnityC
{
    public static class FixtureContext
    {
        static readonly System.Threading.AsyncLocal<TestContext> contextSlot = new System.Threading.AsyncLocal<TestContext>();
        static readonly System.Threading.AsyncLocal<FixtureScope> scopeSlot = new System.Threading.AsyncLocal<FixtureScope>();
        public static TestContext Context { get => contextSlot.Value; set => contextSlot.Value = value; }
        public static FixtureScope Scope { get => scopeSlot.Value; set => scopeSlot.Value = value; }
        public static string Run(string label, System.Action body) => TestContext.Run(label, context =>
        {
            var previousContext = Context; var previousScope = Scope;
            Context = context;
            try { using (var scope = new FixtureScope()) { Scope = scope; body(); } }
            finally { Context = previousContext; Scope = previousScope; }
        });
        public static string RunReport(string label, System.Func<string> body)
        {
            string report = null;
            var result = UnityEngine.JsonUtility.FromJson<TestResult>(Run(label, () => { report = body(); }));
            if (result.status == "passed" && !string.IsNullOrEmpty(report)) result.message += ": " + report;
            return result.ToJson();
        }
        public static string RunIncomplete(string label, System.Action body, string reason)
        {
            var result = UnityEngine.JsonUtility.FromJson<TestResult>(Run(label, body));
            if (result.status == "passed") { result.status = "skipped"; result.message = reason; }
            return result.ToJson(); // Preserve actual assertion/cleanup failures; never green a partial port.
        }
        public static string Diagnostic(string label, System.Func<string> body)
        {
            var context = new TestContext();
            var previousContext = Context; var previousScope = Scope;
            Context = context;
            try
            {
                string report;
                DiagnosticArtifact[] artifacts;
                using (var scope = new FixtureScope())
                {
                    Scope = scope; report = body();
                    var outputs = new System.Collections.Generic.List<DiagnosticArtifact>();
                    // Preserve actual generated diagnostic output in the result before owned cleanup.
                    // This enumerates only our GUID outputs, never input/baseline candidates.
                    foreach (string file in System.IO.Directory.GetFiles(scope.Temp, "*", System.IO.SearchOption.AllDirectories))
                        outputs.Add(new DiagnosticArtifact {
                            name = System.IO.Path.GetRelativePath(scope.Temp, file).Replace('\\', '/'),
                            encoding = "base64", content = System.Convert.ToBase64String(System.IO.File.ReadAllBytes(file)) });
                    artifacts = outputs.ToArray();
                }
                return UnityEngine.JsonUtility.ToJson(new DiagnosticResult {
                    status = "skipped", checks = context.Checks, message = label + ": diagnostic/manual result only. " + report,
                    failures = System.Array.Empty<string>(), artifacts = artifacts });
            }
            catch (System.Exception error) { return TestContext.Result("failed", context.Checks, label, error.ToString()).ToJson(); }
            finally { Context = previousContext; Scope = previousScope; }
        }
        [System.Serializable] sealed class DiagnosticArtifact { public string name, encoding, content; }
        [System.Serializable] sealed class DiagnosticResult
        {
            public string status, message;
            public int checks;
            public string[] failures;
            public DiagnosticArtifact[] artifacts;
        }
        public static async System.Threading.Tasks.Task RunAsync(AsyncFixture job, System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> body)
        {
            var previousContext = Context; var previousScope = Scope;
            Context = job.Context; Scope = job.Scope;
            try { await body(job.Cancellation.Token); job.Pass(); }
            catch (System.OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { job.DisposeOwned(); }
            catch (System.Exception error) { job.Fail(error); }
            finally { Context = previousContext; Scope = previousScope; }
        }
        public static void With(AsyncFixture job, System.Action body)
        {
            var previousContext = Context; var previousScope = Scope;
            Context = job.Context; Scope = job.Scope;
            try { body(); } finally { Context = previousContext; Scope = previousScope; }
        }
    }
    public sealed class FixtureScope : System.IDisposable
    {
        public readonly string Token = System.Guid.NewGuid().ToString("N");
        public readonly string Temp;
        public string RelativeTemp => "Temp/WhimTex/UnityC-" + Token;
        public readonly string Assets;
        readonly bool assetsExisted;
        readonly string tempRoot;
        readonly string assetRoot;
        readonly string absoluteAssets;
        readonly UnityEngine.RenderTexture active = UnityEngine.RenderTexture.active;
        readonly bool srgb = UnityEngine.GL.sRGBWrite;
        readonly UnityEditor.EditorWindow focus = UnityEditor.EditorWindow.focusedWindow;
        readonly string clipboard = UnityEngine.GUIUtility.systemCopyBuffer;
        readonly UnityEngine.Object[] selection = UnityEditor.Selection.objects;
        readonly UnityEngine.Random.State random = UnityEngine.Random.state;
        readonly System.Collections.Generic.List<System.Action> stateRestorations = new System.Collections.Generic.List<System.Action>();
        readonly System.Collections.Generic.List<System.IDisposable> disposables = new System.Collections.Generic.List<System.IDisposable>();
        readonly System.Collections.Generic.HashSet<string> generatedFiles = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        readonly System.Collections.Generic.HashSet<UnityEngine.Object> objects = new System.Collections.Generic.HashSet<UnityEngine.Object>();
        readonly System.Collections.Generic.HashSet<UnityEngine.RenderTexture> temporaries = new System.Collections.Generic.HashSet<UnityEngine.RenderTexture>();
        readonly System.Collections.Generic.List<System.Exception> windowCleanupFailures = new System.Collections.Generic.List<System.Exception>();
        public T Own<T>(T value) where T : UnityEngine.Object { if (value != null) objects.Add(value); return value; }
        public T OwnDisposable<T>(T value) where T : System.IDisposable { if (value != null) disposables.Add(value); return value; }
        public void OwnLiveSession(string session)
        {
            stateRestorations.Add(() =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                var jobs = (System.Collections.IDictionary)typeof(DCFApixels.WhimTex.WhimTexApi).GetField("liveJobs", flags).GetValue(null);
                var owned = new System.Collections.Generic.List<object>();
                foreach (System.Collections.DictionaryEntry entry in jobs)
                    if ((string)entry.Value.GetType().GetField("session", flags).GetValue(entry.Value) == session) owned.Add(entry.Key);
                foreach (object key in owned) jobs.Remove(key);
            });
        }
        public void OwnGeneratedFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string absolute = System.IO.Path.GetFullPath(path);
            string agentRoot = System.IO.Path.Combine(tempRoot, "Agent").TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(Temp + System.IO.Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) &&
                !absolute.StartsWith(agentRoot, System.StringComparison.OrdinalIgnoreCase))
                throw new System.IO.IOException("Generated result is outside owned scope or own Live Agent results");
            RejectLinks(absolute);
            generatedFiles.Add(absolute); // Only paths returned by requests in our own unsaved session.
        }
        public UnityEngine.RenderTexture Temporary(UnityEngine.RenderTexture value) { if (value != null) temporaries.Add(value); return value; }
        public void Destroy(UnityEngine.Object value) { objects.Remove(value); if (value != null) UnityEngine.Object.DestroyImmediate(value); }
        public void CloseWindow(UnityEditor.EditorWindow window)
        {
            if (window == null) return;
            var failures = new System.Collections.Generic.List<System.Exception>();
            try
            {
                // Unity 6000.7 Close requires a host; CreateInstance-only windows have no panel.
                if (window.rootVisualElement?.panel != null)
                {
                    try { window.DiscardChanges(); } catch (System.Exception error) { failures.Add(error); }
                    try { if (window != null) window.Close(); } catch (System.Exception error) { failures.Add(error); }
                }
            }
            catch (System.Exception error) { failures.Add(error); }
            finally
            {
                // Always release this owned object, even if host lookup, discard or close failed.
                try { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
                catch (System.Exception error) { failures.Add(error); }
                objects.Remove(window);
            }
            // Defer propagation until scope disposal so the caller's remaining finally cleanup runs.
            // Dispose aggregates these genuine errors and emits failure, never a false pass.
            if (failures.Count > 0) windowCleanupFailures.Add(new System.AggregateException("Owned window cleanup failed", failures));
        }
        public void Release(UnityEngine.RenderTexture value) { temporaries.Remove(value); if (value != null) UnityEngine.RenderTexture.ReleaseTemporary(value); }
        public FixtureScope()
        {
            string project = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));
            if (!string.Equals(project, System.IO.Path.GetFullPath("D:/DCFA/Projects/Test6.6"), System.StringComparison.OrdinalIgnoreCase))
                throw new System.InvalidOperationException("UnityC fixtures require the assigned Test6.6 Editor");
            tempRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(project, "Temp/WhimTex"));
            assetRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(project, "Assets/WhimTexTestMigration"));
            Temp = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempRoot, "UnityC-" + Token));
            Assets = "Assets/WhimTexTestMigration/UnityC-" + Token;
            absoluteAssets = System.IO.Path.GetFullPath(System.IO.Path.Combine(assetRoot, "UnityC-" + Token));
            if (System.IO.Directory.Exists(Temp) || System.IO.File.Exists(Temp) || System.IO.Directory.Exists(absoluteAssets) || System.IO.File.Exists(absoluteAssets) || System.IO.File.Exists(absoluteAssets + ".meta"))
                throw new System.IO.IOException("Refusing to borrow a pre-existing GUID fixture path");
            RejectLinks(Temp); RejectLinks(absoluteAssets);
            assetsExisted = UnityEditor.AssetDatabase.IsValidFolder("Assets/WhimTexTestMigration");
            System.IO.Directory.CreateDirectory(Temp);
            try
            {
            // Supported EditorPrefs getters preserve the exact borrowed key presence/value.
            foreach (string key in new[] { "DCFApixels.WhimTex.Canvas.PaintToolSettings", "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool" })
            {
                bool exists = UnityEditor.EditorPrefs.HasKey(key); string value = UnityEditor.EditorPrefs.GetString(key);
                stateRestorations.Add(() => { if (exists) UnityEditor.EditorPrefs.SetString(key, value); else UnityEditor.EditorPrefs.DeleteKey(key); });
            }
            {
                const string key = "DCFApixels.WhimTex.Canvas.PaintingScale";
                bool exists = UnityEditor.EditorPrefs.HasKey(key); float value = UnityEditor.EditorPrefs.GetFloat(key);
                stateRestorations.Add(() => { if (exists) UnityEditor.EditorPrefs.SetFloat(key, value); else UnityEditor.EditorPrefs.DeleteKey(key); });
            }
            // Package-owned scratch: detach borrowed storage, then dispose only the new test workspace.
            var core = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.PatchQuiltingSeamless", true);
            const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var spare = core.GetField("spare", hidden); var since = core.GetField("spareSince", hidden);
            object borrowed = spare.GetValue(null), borrowedSince = since.GetValue(null);
            spare.SetValue(null, null);
            stateRestorations.Add(() =>
            {
                try { core.GetMethod("ClearWorkspace", hidden).Invoke(null, null); }
                finally { spare.SetValue(null, borrowed); since.SetValue(null, borrowedSince); }
            });
            }
            catch (System.Exception original)
            {
                try { Dispose(); } catch (System.Exception cleanup) { throw new System.AggregateException(original, cleanup); }
                throw;
            }
        }
        public string AssetFolder()
        {
            RejectLinks(absoluteAssets);
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/WhimTexTestMigration"))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration");
            if (!UnityEditor.AssetDatabase.IsValidFolder(Assets))
                UnityEditor.AssetDatabase.CreateFolder("Assets/WhimTexTestMigration", "UnityC-" + Token);
            return Assets;
        }
        public bool DeleteAsset(string path)
        {
            if (path != Assets && !path.StartsWith(Assets + "/", System.StringComparison.Ordinal))
                throw new System.IO.IOException("Refusing deletion of a borrowed asset: " + path);
            Boundary(absoluteAssets, assetRoot);
            return UnityEditor.AssetDatabase.DeleteAsset(path);
        }
        public void Dispose()
        {
            var failures = new System.Collections.Generic.List<System.Exception>();
            void Attempt(System.Action action) { try { action(); } catch (System.Exception error) { failures.Add(error); } }
            foreach (var value in disposables) Attempt(value.Dispose);
            disposables.Clear();
            foreach (var value in temporaries) Attempt(() => { if (value != null) UnityEngine.RenderTexture.ReleaseTemporary(value); });
            temporaries.Clear();
            var ownedObjects = new System.Collections.Generic.List<UnityEngine.Object>(objects);
            objects.Clear();
            foreach (var value in ownedObjects)
            {
                if (value == null || UnityEditor.EditorUtility.IsPersistent(value)) continue;
                Attempt(() => { if (value is UnityEditor.EditorWindow window) CloseWindow(window); else UnityEngine.Object.DestroyImmediate(value); });
            }
            failures.AddRange(windowCleanupFailures);
            windowCleanupFailures.Clear();
            foreach (var restore in stateRestorations) Attempt(restore);
            stateRestorations.Clear();
            Attempt(() => UnityEngine.RenderTexture.active = active);
            Attempt(() => UnityEngine.GL.sRGBWrite = srgb);
            Attempt(() => UnityEngine.GUIUtility.systemCopyBuffer = clipboard);
            Attempt(() => UnityEditor.Selection.objects = selection);
            Attempt(() => UnityEngine.Random.state = random);
            Attempt(() => { if (focus != null && UnityEditor.EditorWindow.focusedWindow != focus) focus.Focus(); });
            foreach (string path in generatedFiles) Attempt(() =>
            {
                RejectLinks(path); if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                string parent = System.IO.Path.GetDirectoryName(path);
                if (System.Guid.TryParse(System.IO.Path.GetFileName(parent), out _) && System.IO.Directory.Exists(parent) && System.IO.Directory.GetFileSystemEntries(parent).Length == 0)
                    System.IO.Directory.Delete(parent, false);
            });
            generatedFiles.Clear();
            Attempt(() => { Boundary(absoluteAssets, assetRoot); if (UnityEditor.AssetDatabase.IsValidFolder(Assets) && !UnityEditor.AssetDatabase.DeleteAsset(Assets)) throw new System.IO.IOException("Failed to remove owned assets: " + Assets); });
            Attempt(() => { Boundary(Temp, tempRoot); if (System.IO.Directory.Exists(Temp)) System.IO.Directory.Delete(Temp, true); });
            // Shared parent may now contain fixtures from other batches; remove only our empty creation.
            if (!assetsExisted && System.IO.Directory.Exists("Assets/WhimTexTestMigration") &&
                System.IO.Directory.GetFileSystemEntries("Assets/WhimTexTestMigration").Length == 0)
                Attempt(() => { if (!UnityEditor.AssetDatabase.DeleteAsset("Assets/WhimTexTestMigration")) throw new System.IO.IOException("Failed to remove empty owned parent"); });
            if (failures.Count > 0) throw new System.AggregateException("Owned fixture cleanup failed", failures);
        }
        static void Boundary(string target, string root)
        {
            string resolved = System.IO.Path.GetFullPath(target);
            string parent = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            if (!resolved.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) ||
                System.IO.Path.GetFileName(resolved).Length != "UnityC-".Length + 32 ||
                !System.IO.Path.GetFileName(resolved).StartsWith("UnityC-", System.StringComparison.Ordinal))
                throw new System.IO.IOException("Refusing deletion outside the exact owned GUID scope");
            if (System.IO.Directory.Exists(resolved) && (System.IO.File.GetAttributes(resolved) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new System.IO.IOException("Refusing recursive deletion through a link");
            RejectLinks(resolved);
            if (System.IO.Directory.Exists(resolved))
            {
                var pending = new System.Collections.Generic.Stack<string>(); pending.Push(resolved);
                while (pending.Count > 0) foreach (string child in System.IO.Directory.GetFileSystemEntries(pending.Pop()))
                {
                    if ((System.IO.File.GetAttributes(child) & System.IO.FileAttributes.ReparsePoint) != 0) throw new System.IO.IOException("Refusing deletion of linked fixture contents");
                    if (System.IO.Directory.Exists(child)) pending.Push(child);
                }
            }
        }
        static void RejectLinks(string path)
        {
            for (string current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
                if ((System.IO.Directory.Exists(current) || System.IO.File.Exists(current)) && (System.IO.File.GetAttributes(current) & System.IO.FileAttributes.ReparsePoint) != 0)
                    throw new System.IO.IOException("Refusing fixture access through linked ancestor: " + current);
        }
    }

    public static class PublicInput
    {
        public static void Click(UnityEngine.UIElements.Button button)
        {
            using (var evt = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled())
            { evt.target = button; button.SendEvent(evt); }
        }
        // Public IPointerEvent input is copied by PointerMoveEvent.GetPooled(IPointerEvent).
        public sealed class Pointer : UnityEngine.UIElements.IPointerEvent
        {
            public int pointerId => UnityEngine.UIElements.PointerId.mousePointerId;
            public string pointerType => UnityEngine.UIElements.PointerType.mouse;
            public bool isPrimary => true;
            public int button { get; set; }
            public int pressedButtons { get; set; }
            public UnityEngine.Vector3 position { get; set; }
            public UnityEngine.Vector3 localPosition => position;
            public UnityEngine.Vector3 deltaPosition => UnityEngine.Vector3.zero;
            public float deltaTime => 0;
            public int clickCount => 1;
            public float pressure => pressedButtons != 0 ? 1 : 0;
            public float tangentialPressure => 0;
            public float altitudeAngle => 0;
            public float azimuthAngle => 0;
            public float twist => 0;
            public UnityEngine.Vector2 tilt => UnityEngine.Vector2.zero;
            public UnityEngine.PenStatus penStatus => UnityEngine.PenStatus.None;
            public UnityEngine.Vector2 radius => UnityEngine.Vector2.zero;
            public UnityEngine.Vector2 radiusVariance => UnityEngine.Vector2.zero;
            public UnityEngine.EventModifiers modifiers => UnityEngine.EventModifiers.None;
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }
    }

    // This Unity-owned instance survives ephemeral Pipeline assemblies. Poll/Cancel resolve it
    // as ScriptableObject and reflect only this test's own public methods, never Unity internals.
    public sealed class AsyncFixture : UnityEngine.ScriptableObject
    {
        public TestContext Context = new TestContext();
        public FixtureScope Scope;
        public System.Threading.CancellationTokenSource Cancellation;
        public System.Threading.Tasks.Task Worker;
        public string CleanupError;
        string key;
        System.Action cleanup;
        bool cleaned;
        bool cleaning;
        readonly System.Collections.Generic.List<UnityEngine.UIElements.IVisualElementScheduledItem> schedules =
            new System.Collections.Generic.List<UnityEngine.UIElements.IVisualElementScheduledItem>();
        public bool Stopped { get; private set; }
        static string Key(string id)
        {
            if (!System.Guid.TryParseExact(id, "D", out _)) throw new System.ArgumentException("Expected per-run GUID");
            return "WhimTex.Tests.UnityC." + id;
        }
        public static AsyncFixture Create(string id)
        {
            string k = Key(id);
            if (UnityEditor.SessionState.GetString(k, "") != "") throw new System.InvalidOperationException("Duplicate run GUID");
            var job = UnityEngine.ScriptableObject.CreateInstance<AsyncFixture>();
            job.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            try
            {
                job.name = k; job.key = k; job.Scope = new FixtureScope();
                job.Cancellation = new System.Threading.CancellationTokenSource();
                UnityEditor.SessionState.SetString(k, TestContext.Result("running", 0, "Started").ToJson());
                return job;
            }
            catch (System.Exception original)
            {
                var failures = new System.Collections.Generic.List<System.Exception> { original };
                try { job.Scope?.Dispose(); } catch (System.Exception error) { failures.Add(error); }
                try { job.Cancellation?.Dispose(); } catch (System.Exception error) { failures.Add(error); }
                try { UnityEngine.Object.DestroyImmediate(job); } catch (System.Exception error) { failures.Add(error); }
                throw new System.AggregateException("Async fixture setup failed", failures);
            }
        }
        public void OwnCleanup(System.Action action) { cleanup = action; }
        public void Schedule(UnityEngine.UIElements.VisualElement root, System.Action action, long delay)
        {
            if (Stopped) return;
            schedules.Add(root.schedule.Execute(() => { if (!Stopped) action(); }).StartingIn(delay));
        }
        public void DisposeOwned()
        {
            if (cleaned || cleaning) return;
            cleaning = true; Stopped = true;
            var failures = new System.Collections.Generic.List<System.Exception>();
            foreach (var schedule in schedules) { try { schedule.Pause(); } catch (System.Exception error) { failures.Add(error); } }
            schedules.Clear();
            try { cleanup?.Invoke(); } catch (System.Exception error) { failures.Add(error); }
            try { Scope?.Dispose(); } catch (System.Exception error) { failures.Add(error); }
            cleanup = null; cleaned = true; cleaning = false;
            if (failures.Count > 0) { var failure = new System.AggregateException("Async owned cleanup failed", failures); CleanupError = failure.ToString(); throw failure; }
        }
        void Finish(string status, System.Exception error = null)
        {
            try { DisposeOwned(); }
            catch (System.Exception failure) { error = error == null ? failure : new System.AggregateException(error, failure); }
            if (error != null) status = "failed";
            if (status == "passed" && Context.Checks == 0) { status = "failed"; error = new System.InvalidOperationException("No assertions were executed"); }
            UnityEditor.SessionState.SetString(key, TestContext.Result(status, Context.Checks, "UnityC async fixture", error == null ? System.Array.Empty<string>() : new[] { error.ToString() }).ToJson());
        }
        public void Pass() { Finish("passed"); }
        public void Fail(System.Exception error) { Finish("failed", error); }
        public async System.Threading.Tasks.Task<string> Stop()
        {
            Stopped = true;
            var failures = new System.Collections.Generic.List<System.Exception>();
            try { Cancellation.Cancel(); } catch (System.Exception error) { failures.Add(error); }
            if (Worker != null)
            {
                try { await Worker; }
                catch (System.OperationCanceledException) { }
                catch (System.Exception error) { failures.Add(error); }
            }
            string payload = Read();
            var existing = UnityEngine.JsonUtility.FromJson<TestResult>(payload);
            try { DisposeOwned(); } catch (System.Exception error) { failures.Add(error); }
            if (failures.Count > 0)
                UnityEditor.SessionState.SetString(key, TestContext.Result("failed", Context.Checks, "Stop/cleanup failure; prior result: " + payload, new System.AggregateException(failures).ToString()).ToJson());
            else if (existing == null || existing.status == "running") Finish("cancelled");
            return Read();
        }
        public string Read() => UnityEditor.SessionState.GetString(key, "");
        static UnityEngine.ScriptableObject Find(string id)
        {
            string k = Key(id);
            foreach (var obj in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
                if (obj != null && obj.name == k && obj.GetType().FullName == typeof(AsyncFixture).FullName) return obj;
            return null;
        }
        public static string Poll(string id)
        {
            string result = UnityEditor.SessionState.GetString(Key(id), "");
            return result != "" ? result : TestContext.Result("failed", 0, "Missing run", "No state for this GUID").ToJson();
        }
        public static async System.Threading.Tasks.Task<string> Cancel(string id)
        {
            var obj = Find(id);
            if (obj == null) return TestContext.Result("failed", 0, "Missing fixture", "Cannot confirm work stopped").ToJson();
            return await (System.Threading.Tasks.Task<string>)obj.GetType().GetMethod("Stop").Invoke(obj, null);
        }
        public static async System.Threading.Tasks.Task<string> Cleanup(string id)
        {
            var obj = Find(id);
            string payload = Poll(id);
            var failures = new System.Collections.Generic.List<System.Exception>();
            if (obj != null)
            {
                try { payload = await (System.Threading.Tasks.Task<string>)obj.GetType().GetMethod("Stop").Invoke(obj, null); }
                catch (System.Exception error) { failures.Add(error); }
                try
                {
                    string cleanupError = (string)obj.GetType().GetField("CleanupError").GetValue(obj);
                    if (!string.IsNullOrEmpty(cleanupError)) failures.Add(new System.InvalidOperationException(cleanupError));
                }
                catch (System.Exception error) { failures.Add(error); }
                try { var source = (System.Threading.CancellationTokenSource)obj.GetType().GetField("Cancellation").GetValue(obj); source?.Dispose(); }
                catch (System.Exception error) { failures.Add(error); }
                try { UnityEngine.Object.DestroyImmediate(obj); } catch (System.Exception error) { failures.Add(error); }
            }
            else
            {
                failures.Add(new System.InvalidOperationException("Missing fixture: cannot confirm work stopped or release owned state"));
            }
            UnityEditor.SessionState.EraseString(Key(id));
            if (failures.Count > 0) return TestContext.Result("failed", 0, "Cleanup failure; prior result: " + payload, new System.AggregateException(failures).ToString()).ToJson();
            return TestContext.Result("passed", 1, "Owned work stopped; GUID fixture removed. Prior result: " + payload).ToJson();
        }
    }
}
