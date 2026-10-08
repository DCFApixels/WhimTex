// Test-only ownership and asynchronous lifecycle support. Appended after a case; no imports.
namespace WhimTex.Tests.UnityA
{
    public sealed class UnityAScope : System.IDisposable
    {
        const string WindowJournalKey = "WhimTex.Tests.UnityA.OwnedWindows.v3";
        const string JournalHeader = "WHIMTEX_UNITY_A_WINDOWS_V3";
        static readonly System.Text.UTF8Encoding WireEncoding = new System.Text.UTF8Encoding(false, true);
        sealed class WindowOwnership
        {
            public string scopeGuid, objectId, typeName, assignedName;
            public string rawLine;
        }
        sealed class WindowJournal
        {
            public System.Collections.Generic.List<WindowOwnership> windows = new System.Collections.Generic.List<WindowOwnership>();
        }
        static string EncodeField(string value) => System.Convert.ToBase64String(WireEncoding.GetBytes(value ?? ""));
        static string DecodeField(string value) => WireEncoding.GetString(System.Convert.FromBase64String(value));
        static string EncodeJournal(WindowJournal journal)
        {
            var wire = new System.Text.StringBuilder(JournalHeader);
            foreach (var record in journal.windows)
            {
                wire.Append('\n');
                if (record.rawLine != null) wire.Append(record.rawLine);
                else wire.Append(EncodeField(record.scopeGuid)).Append('|').Append(EncodeField(record.objectId))
                    .Append('|').Append(EncodeField(record.typeName)).Append('|').Append(EncodeField(record.assignedName));
            }
            return wire.ToString();
        }
        static WindowJournal DecodeJournal(string wire)
        {
            var journal = new WindowJournal();
            if (string.IsNullOrEmpty(wire)) return journal;
            var lines = wire.Split('\n');
            if (lines[0] != JournalHeader) throw new System.InvalidOperationException("Unknown owned-window journal format; preserve it and do not infer ownership.");
            for (int i = 1; i < lines.Length; i++)
            {
                var record = new WindowOwnership { rawLine = lines[i] };
                try
                {
                    var fields = lines[i].Split('|');
                    if (fields.Length == 4)
                    {
                        string scopeGuid = DecodeField(fields[0]), objectId = DecodeField(fields[1]);
                        string typeName = DecodeField(fields[2]), assignedName = DecodeField(fields[3]);
                        record.scopeGuid = scopeGuid; record.objectId = objectId;
                        record.typeName = typeName; record.assignedName = assignedName;
                    }
                }
                catch (System.FormatException) { }
                catch (System.Text.DecoderFallbackException) { }
                // Preserve invalid raw lines byte-for-byte; they cannot authorize cleanup.
                journal.windows.Add(record);
            }
            return journal;
        }
        static WindowJournal ReadWindowJournal() => DecodeJournal(UnityEditor.SessionState.GetString(WindowJournalKey, ""));
        static void WriteWindowJournal(WindowJournal journal)
        {
            string wire = EncodeJournal(journal);
            UnityEditor.SessionState.SetString(WindowJournalKey, wire);
            string stored = UnityEditor.SessionState.GetString(WindowJournalKey, "");
            if (stored != wire || EncodeJournal(DecodeJournal(stored)) != wire)
                throw new System.InvalidOperationException("Owned-window journal failed immediate SessionState/wire round trip; retain current-scope references for cleanup.");
        }
        static string ObjectIdentity(UnityEngine.Object value)
        {
            // Same version boundary as package UnityObjectID; persist the full identity,
            // not a hash or a narrowed EntityId. v1 records are not reinterpreted.
#if UNITY_6000_4_OR_NEWER
            return "entity:" + value.GetEntityId().ToString();
#else
            return "instance:" + value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
        }
        static bool HasCreationEvidence(WindowOwnership record)
        {
            return record != null && System.Guid.TryParseExact(record.scopeGuid, "N", out var scopeId)
                && scopeId != System.Guid.Empty && !string.IsNullOrEmpty(record.objectId)
                && !string.IsNullOrEmpty(record.typeName) && record.assignedName != null
                && record.assignedName.StartsWith(record.scopeGuid + "-owned-", System.StringComparison.Ordinal);
        }
        static bool MatchesOwnership(WindowOwnership record, UnityEditor.EditorWindow window)
        {
            return HasCreationEvidence(record) && window != null
                && record.typeName == window.GetType().FullName && record.objectId == ObjectIdentity(window);
        }
        static WindowOwnership FindOwnership(UnityEditor.EditorWindow window)
        {
            WindowOwnership found = null;
            foreach (var record in ReadWindowJournal().windows)
            {
                if (!MatchesOwnership(record, window)) continue;
                if (found != null) throw new System.InvalidOperationException("Ambiguous owned-window creation records; leave the window untouched.");
                found = record;
            }
            if (found != null) return found;
            throw new System.InvalidOperationException("No exact GUID/type/object identity journal evidence for this window; leave it untouched.");
        }
        static void ForgetOwnership(WindowOwnership record)
        {
            var journal = ReadWindowJournal();
            journal.windows.RemoveAll(item => HasCreationEvidence(item) && item.objectId == record.objectId
                && item.scopeGuid == record.scopeGuid && item.typeName == record.typeName && item.assignedName == record.assignedName);
            WriteWindowJournal(journal);
        }
        public static string RecoverOwnedJournal() => WhimTex.Tests.TestContext.Run("Exact journaled windows recovered", context =>
        {
            var journal = ReadWindowJournal();
            var windows = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
            var actions = new System.Collections.Generic.List<System.Action>();
            foreach (var record in journal.windows)
            {
                // Unmatched/stale/malformed records are retained, not guessed or erased.
                if (!HasCreationEvidence(record)) continue;
                foreach (var window in windows)
                {
                    if (!MatchesOwnership(record, window)) continue;
                    var target = window; var evidence = record;
                    actions.Add(() =>
                    {
                        context.True(MatchesOwnership(evidence, target), "Full stored object identity/type/creation GUID still matches");
                        CloseOwned(target);
                    });
                    actions.Add(() =>
                    {
                        bool remaining = false;
                        foreach (var candidate in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>())
                            if (MatchesOwnership(evidence, candidate)) { remaining = true; break; }
                        context.True(!remaining, "No window with this exact journaled identity/type/creation GUID remains");
                    });
                }
            }
            // All exact targets and all postconditions are attempted even after a
            // genuine Close/Destroy error. A no-target recovery fails with zero checks.
            RunCleanup(null, actions.ToArray());
        });
        readonly string guid = System.Guid.NewGuid().ToString("N");
        readonly UnityEditor.EditorWindow focus = UnityEditor.EditorWindow.focusedWindow;
        readonly UnityEngine.RenderTexture target = UnityEngine.RenderTexture.active;
        readonly bool srgb = UnityEngine.GL.sRGBWrite;
        readonly string clipboard = UnityEngine.GUIUtility.systemCopyBuffer;
        readonly UnityEngine.Object[] drag = UnityEditor.DragAndDrop.objectReferences;
        readonly System.Collections.Generic.List<UnityEditor.EditorWindow> windows = new System.Collections.Generic.List<UnityEditor.EditorWindow>();
        readonly System.Collections.Generic.List<WindowOwnership> creationRecords = new System.Collections.Generic.List<WindowOwnership>();
        readonly System.Collections.Generic.List<System.Action> restores = new System.Collections.Generic.List<System.Action>();
        readonly System.Collections.Generic.List<System.Action> finalizers = new System.Collections.Generic.List<System.Action>();
        readonly System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>> asyncFinalizers = new System.Collections.Generic.List<System.Func<System.Threading.Tasks.Task>>();
        string assets, temp;
        public string Tag => guid;
        public UnityAScope()
        {
            StringPreference("DCFApixels.WhimTex.Canvas.Tool");
            StringPreference("DCFApixels.WhimTex.Canvas.TransformReturnTool");
            StringPreference("DCFApixels.WhimTex.Canvas.PaintToolSettings");
            IntPreference("WhimTex.ColorPicker.ColorMode");
            BoolPreference("WhimTex.ColorPicker.Channels");
            BoolPreference("DCFApixels.WhimTex.ColorPicker.HistoryExpanded");
            BoolPreference("DCFApixels.WhimTex.HdrColorInputs");
            var inputType = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexColorInputs", true);
            var hdrField = inputType.GetField("hdr", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            object savedHdr = hdrField.GetValue(null);
            restores.Add(() => { if (!object.Equals(hdrField.GetValue(null), savedHdr)) hdrField.SetValue(null, savedHdr); });
            // Window lifecycle clears these package-owned drag slots through public DragAndDrop.
            foreach (string key in new[] { "DCFApixels.WhimTex.DraggedLayerId", "DCFApixels.WhimTex.DraggedLayers", "DCFApixels.WhimTex.DraggedDocumentId", "DCFApixels.WhimTex.DraggedWindow" })
            {
                string slot = key; object value = UnityEditor.DragAndDrop.GetGenericData(slot);
                restores.Add(() => UnityEditor.DragAndDrop.SetGenericData(slot, value));
            }
            // Restore the package cache independently of the persisted preference's presence.
            var channels = typeof(DCFApixels.WhimTex.WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexColorChannels", true);
            var field = channels.GetField("enabled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            object saved = field.GetValue(null);
            restores.Add(() => { if (!object.Equals(field.GetValue(null), saved)) field.SetValue(null, saved); });
        }
        void StringPreference(string key)
        {
            bool had = UnityEditor.EditorPrefs.HasKey(key); string value = UnityEditor.EditorPrefs.GetString(key);
            restores.Add(() => { if (had) { if (!UnityEditor.EditorPrefs.HasKey(key) || UnityEditor.EditorPrefs.GetString(key) != value) UnityEditor.EditorPrefs.SetString(key, value); } else if (UnityEditor.EditorPrefs.HasKey(key)) UnityEditor.EditorPrefs.DeleteKey(key); });
        }
        void IntPreference(string key)
        {
            bool had = UnityEditor.EditorPrefs.HasKey(key); int value = UnityEditor.EditorPrefs.GetInt(key);
            restores.Add(() => { if (had) { if (!UnityEditor.EditorPrefs.HasKey(key) || UnityEditor.EditorPrefs.GetInt(key) != value) UnityEditor.EditorPrefs.SetInt(key, value); } else if (UnityEditor.EditorPrefs.HasKey(key)) UnityEditor.EditorPrefs.DeleteKey(key); });
        }
        void BoolPreference(string key)
        {
            bool had = UnityEditor.EditorPrefs.HasKey(key); bool value = UnityEditor.EditorPrefs.GetBool(key);
            restores.Add(() => { if (had) { if (!UnityEditor.EditorPrefs.HasKey(key) || UnityEditor.EditorPrefs.GetBool(key) != value) UnityEditor.EditorPrefs.SetBool(key, value); } else if (UnityEditor.EditorPrefs.HasKey(key)) UnityEditor.EditorPrefs.DeleteKey(key); });
        }
        public string Assets
        {
            get
            {
                if (assets != null) return assets;
                const string parent = "Assets/WhimTexTestMigration";
                if (!UnityEditor.AssetDatabase.IsValidFolder(parent)) UnityEditor.AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration");
                string path = parent + "/" + guid;
                if (UnityEditor.AssetDatabase.IsValidFolder(path) || System.IO.Directory.Exists(path)) throw new System.InvalidOperationException("Fixture path already exists.");
                if (string.IsNullOrEmpty(UnityEditor.AssetDatabase.CreateFolder(parent, guid))) throw new System.IO.IOException("Cannot create owned fixture folder.");
                assets = path; return path;
            }
        }
        public string Temp
        {
            get
            {
                if (temp == null)
                {
                    temp = System.IO.Path.GetFullPath("Temp/WhimTex/tests-unity-a/" + guid);
                    if (System.IO.Directory.Exists(temp)) throw new System.InvalidOperationException("Temporary fixture already exists.");
                    System.IO.Directory.CreateDirectory(temp);
                }
                return temp;
            }
        }
        public T OwnWindow<T>(T window) where T : UnityEditor.EditorWindow
        {
            if (window == null) throw new System.ArgumentNullException(nameof(window));
            if (windows.Contains(window)) return window;
            var journal = ReadWindowJournal();
            string objectId = ObjectIdentity(window);
            if (journal.windows.Exists(item => item != null && item.objectId == objectId)) throw new System.InvalidOperationException("Window already journaled; do not replace existing ownership evidence.");
            windows.Add(window);
            string assignedName = guid + "-owned-" + window.GetType().Name + "-" + windows.Count;
            var ownership = new WindowOwnership { scopeGuid = guid, objectId = objectId, typeName = window.GetType().FullName, assignedName = assignedName };
            creationRecords.Add(ownership);
            window.name = assignedName;
            journal.windows.Add(ownership);
            // Package Finish/Apply can destroy a window before scope disposal. Keep the
            // journal while it is alive, and retire this exact record only after destruction.
            finalizers.Add(() => { if (window == null) ForgetOwnership(ownership); });
            WriteWindowJournal(journal);
            var persisted = FindOwnership(window);
            if (persisted.scopeGuid != ownership.scopeGuid || persisted.assignedName != ownership.assignedName)
                throw new System.InvalidOperationException("Owned-window creation fields failed immediate persisted round trip.");
            return window;
        }
        internal static void TestJournalWire(WhimTex.Tests.TestContext context)
        {
            string scopeGuid = System.Guid.NewGuid().ToString("N");
            var record = new WindowOwnership { scopeGuid = scopeGuid, objectId = "entity:58747:2816|\nΩ",
                typeName = "Wire.Type|\nÉ", assignedName = scopeGuid + "-owned-Wire.Type-1|\n雪" };
            var journal = new WindowJournal(); journal.windows.Add(record);
            string wire = EncodeJournal(journal);
            context.True(wire.StartsWith(JournalHeader + "\n", System.StringComparison.Ordinal), "Versioned wire header and a nonempty creation record");
            var decoded = DecodeJournal(wire);
            context.Equal(1, decoded.windows.Count, "One decoded record");
            context.Equal(record.scopeGuid, decoded.windows[0].scopeGuid, "Creation GUID round trip");
            context.Equal(record.objectId, decoded.windows[0].objectId, "Full object identity including delimiter/newline/Unicode round trip");
            context.Equal(record.typeName, decoded.windows[0].typeName, "Exact type round trip");
            context.Equal(record.assignedName, decoded.windows[0].assignedName, "Initial creation name round trip");
            context.Equal(wire, EncodeJournal(decoded), "Wire bytes round trip");
            string malformed = wire + "\nnot|base64!|a|record\n";
            var mixed = DecodeJournal(malformed);
            context.Equal(malformed, EncodeJournal(mixed), "Malformed and empty lines preserved exactly");
            context.True(!HasCreationEvidence(mixed.windows[1]) && !HasCreationEvidence(mixed.windows[2]), "Malformed lines never grant creation authority");
            bool rejected = false;
            try { DecodeJournal("{}"); } catch (System.InvalidOperationException) { rejected = true; }
            context.True(rejected, "Empty old JSON is not reinterpreted as a valid journal");
            string key = WindowJournalKey + ".RoundTrip." + scopeGuid;
            context.Equal("", UnityEditor.SessionState.GetString(key, ""), "Unique probe key is unused");
            try
            {
                UnityEditor.SessionState.SetString(key, wire);
                string stored = UnityEditor.SessionState.GetString(key, "");
                context.Equal(wire, stored, "Actual SessionState retains full BCL wire, not empty JSON");
                var persisted = DecodeJournal(stored);
                context.Equal(1, persisted.windows.Count, "Actual SessionState retains record count");
                context.Equal(record.scopeGuid, persisted.windows[0].scopeGuid, "Actual SessionState retains creation GUID");
                context.Equal(record.objectId, persisted.windows[0].objectId, "Actual SessionState retains full object identity");
                context.Equal(record.typeName, persisted.windows[0].typeName, "Actual SessionState retains exact type");
                context.Equal(record.assignedName, persisted.windows[0].assignedName, "Actual SessionState retains initial assigned name");
            }
            finally { UnityEditor.SessionState.EraseString(key); }
            context.Equal("", UnityEditor.SessionState.GetString(key, ""), "Owned probe key erased");
        }
        internal static string AssertJournalOwnership(WhimTex.Tests.TestContext context, UnityEditor.EditorWindow window)
        {
            var record = FindOwnership(window);
            context.True(HasCreationEvidence(record), "Actual persisted creation proof is complete");
            context.Equal(ObjectIdentity(window), record.objectId, "Actual native identity retained");
            context.Equal(window.GetType().FullName, record.typeName, "Actual native type retained");
            context.True(MatchesOwnership(record, window), "Actual owned window matches without mutable-name authority");
            return record.objectId;
        }
        internal static void AssertJournalRetired(WhimTex.Tests.TestContext context, string objectId)
            => context.True(!ReadWindowJournal().windows.Exists(record => record.objectId == objectId), "Destroyed test window's exact journal record retired");
        public T OwnObject<T>(T value) where T : UnityEngine.Object
        {
            finalizers.Add(() =>
            {
                if (value == null) return;
                RunCleanup(null,
                    () => UnityEditor.Undo.ClearUndo(value),
                    () => { if (value != null) UnityEngine.Object.DestroyImmediate(value); });
            });
            return value;
        }
        public void Finally(System.Action cleanup) => finalizers.Add(cleanup);
        public void FinallyAsync(System.Func<System.Threading.Tasks.Task> cleanup) => asyncFinalizers.Add(cleanup);
        public static void RunCleanup(System.Exception bodyFailure, params System.Action[] actions)
        {
            var failures = new System.Collections.Generic.List<System.Exception>();
            foreach (var action in actions)
                try { action(); } catch (System.Exception error) { failures.Add(error); }
            if (failures.Count == 0) return;
            var cleanup = new System.AggregateException("Owned cleanup failed.", failures);
            System.Exception combined = bodyFailure == null ? cleanup : new System.AggregateException("Body and cleanup both failed.", bodyFailure, cleanup);
            combined.Data["WhimTexCleanupFailure"] = cleanup.ToString();
            throw combined;
        }
        public static void RunOwned(System.Action<UnityAScope> body)
        {
            var scope = new UnityAScope(); System.Exception failure = null;
            try { body(scope); } catch (System.Exception error) { failure = error; }
            Finish(scope, failure);
        }
        public static async System.Threading.Tasks.Task RunOwnedAsync(System.Func<UnityAScope, System.Threading.Tasks.Task> body)
        {
            var scope = new UnityAScope(); System.Exception failure = null;
            try { await body(scope); } catch (System.Exception error) { failure = error; }
            try { await scope.DisposeOwnedAsync(); }
            catch (System.Exception cleanup)
            {
                var combined = failure == null ? cleanup : new System.AggregateException("Body and cleanup both failed.", failure, cleanup);
                combined.Data["WhimTexCleanupFailure"] = cleanup.ToString(); throw combined;
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        async System.Threading.Tasks.Task DisposeOwnedAsync()
        {
            var failures = new System.Collections.Generic.List<System.Exception>();
            for (int i = asyncFinalizers.Count - 1; i >= 0; i--)
                try { await asyncFinalizers[i](); } catch (System.Exception error) { failures.Add(error); }
            asyncFinalizers.Clear();
            try { Dispose(); } catch (System.Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new System.AggregateException("Async ownership cleanup failed.", failures);
        }
        static void Finish(UnityAScope scope, System.Exception failure)
        {
            try { scope.Dispose(); }
            catch (System.Exception cleanup)
            {
                System.Exception combined = failure == null ? cleanup : new System.AggregateException("Body and cleanup both failed.", failure, cleanup);
                combined.Data["WhimTexCleanupFailure"] = cleanup.ToString();
                throw combined;
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        public static void CloseOwned(UnityEditor.EditorWindow window)
        {
            if (window == null) return;
            CloseProvenWindow(window, FindOwnership(window));
        }
        static void CloseProvenWindow(UnityEditor.EditorWindow window, WindowOwnership ownership)
        {
            if (window == null) return;
            if (!MatchesOwnership(ownership, window)) throw new System.InvalidOperationException("Captured creation proof no longer matches; leave the window untouched.");
            var failures = new System.Collections.Generic.List<System.Exception>();
            void Clean(System.Action action) { try { action(); } catch (System.Exception error) { failures.Add(error); } }
            try
            {
                if (window is DCFApixels.WhimTex.WhimTexWindow documentWindow)
                {
                    Clean(() =>
                    {
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var document = (DCFApixels.WhimTex.WhimTexDocument)typeof(DCFApixels.WhimTex.WhimTexWindow).GetField("activeDocument", flags).GetValue(window);
                        if (document != null && !UnityEditor.AssetDatabase.Contains(document))
                        {
                            Clean(() => UnityEditor.Undo.ClearUndo(document));
                            Clean(() => ClearDrawingUndo(document.layers));
                        }
                    });
                    Clean(documentWindow.DiscardChanges);
                }
                // A CreateInstance-only window has no host. Public panel attachment is
                // sufficient here because every shown fixture uses the UI Toolkit root.
                Clean(() => { if (window != null && window.rootVisualElement.panel != null) window.Close(); });
            }
            finally { Clean(() => { if (window != null) UnityEngine.Object.DestroyImmediate(window); }); }
            Clean(() => { if (window == null) ForgetOwnership(ownership); });
            if (failures.Count != 0) throw new System.AggregateException("Owned window cleanup failed.", failures);
        }
        static void ClearDrawingUndo(System.Collections.Generic.List<DCFApixels.WhimTex.Layer> layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                if (layer.Behaviour is DCFApixels.WhimTex.DrawingLayerBehaviour drawing)
                {
                    var field = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).GetField("pixels", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var texture = (UnityEngine.Texture2D)field.GetValue(drawing);
                    if (texture != null && !UnityEditor.AssetDatabase.Contains(texture)) UnityEditor.Undo.ClearUndo(texture);
                }
                ClearDrawingUndo(layer.children);
            }
        }
        void PreserveDiagnostics()
        {
            if (temp == null || !System.IO.Directory.Exists(temp)) return;
            var files = new System.Collections.Generic.List<string>();
            foreach (string file in System.IO.Directory.GetFiles(temp, "*", System.IO.SearchOption.AllDirectories))
            {
                string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".png" || extension == ".csv" || extension == ".txt" || extension == ".bin") files.Add(file);
            }
            if (files.Count == 0) return;
            string output = System.IO.Path.GetFullPath("Temp/WhimTex/tests-unity-a-artifacts/" + guid);
            if (System.IO.Directory.Exists(output)) throw new System.IO.IOException("Diagnostic artifact GUID path already exists; never overwrite it.");
            System.IO.Directory.CreateDirectory(output);
            foreach (string file in files)
            {
                string relative = System.IO.Path.GetRelativePath(temp, file);
                if (System.IO.Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", System.StringComparison.Ordinal)
                    || relative.StartsWith("..\\", System.StringComparison.Ordinal)) throw new System.IO.IOException("Diagnostic source escaped the owned temporary folder.");
                string destination = System.IO.Path.Combine(output, relative);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                System.IO.File.Copy(file, destination, false);
            }
            UnityEngine.Debug.Log("WhimTex UnityA diagnostic artifacts: " + output);
        }
        public void Dispose()
        {
            var failures = new System.Collections.Generic.List<System.Exception>();
            void Clean(System.Action action) { try { action(); } catch (System.Exception error) { failures.Add(error); } }
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i]; var ownership = creationRecords[i];
                // Only this scope's captured native reference/creation record can bypass
                // persistence failure. Explicit recovery still requires a v3 wire record.
                Clean(() => CloseProvenWindow(window, ownership));
            }
            for (int i = finalizers.Count - 1; i >= 0; i--) Clean(finalizers[i]);
            for (int i = restores.Count - 1; i >= 0; i--) Clean(restores[i]);
            Clean(() => { UnityEngine.RenderTexture.active = target; UnityEngine.GL.sRGBWrite = srgb; });
            Clean(() =>
            {
                if (UnityEngine.GUIUtility.systemCopyBuffer != clipboard) UnityEngine.GUIUtility.systemCopyBuffer = clipboard;
                var current = UnityEditor.DragAndDrop.objectReferences; bool same = current.Length == drag.Length;
                for (int i = 0; same && i < current.Length; i++) same &= current[i] == drag[i];
                if (!same) UnityEditor.DragAndDrop.objectReferences = drag;
            });
            if (assets != null) Clean(() => { if (!UnityEditor.AssetDatabase.DeleteAsset(assets)) throw new System.IO.IOException("Could not delete owned fixture " + assets); });
            Clean(PreserveDiagnostics);
            if (temp != null) Clean(() => { if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, true); });
            Clean(() => { if (focus != null) focus.Focus(); });
            if (failures.Count != 0) throw new System.AggregateException("Fixture cleanup failed.", failures);
        }
    }

    public sealed class UnityAAsyncCarrier : UnityEngine.ScriptableObject
    {
        [System.NonSerialized] public object[] State;
    }
    public sealed class UnityAHeaderWindow : UnityEditor.EditorWindow { }
    public static class UnityAAsync
    {
        static string Name(string id) => "WhimTex.UnityA." + System.Guid.Parse(id).ToString("N");
        static UnityEngine.ScriptableObject Find(string id)
        {
            string name = Name(id);
            foreach (var carrier in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>())
                if (carrier.name == name && carrier.GetType().FullName == "WhimTex.Tests.UnityA.UnityAAsyncCarrier") return carrier;
            return null;
        }
        static object[] State(UnityEngine.ScriptableObject carrier) => (object[])carrier.GetType().GetField("State").GetValue(carrier);
        // The carrier retains framework types/delegates across ephemeral Pipeline assemblies.
        // Poll never reads a fresh assembly's static fields. Cancellation joins the body and its finally.
        public static string Start(string id, System.Func<WhimTex.Tests.TestContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> body)
        {
            if (Find(id) != null) return WhimTex.Tests.TestContext.Result("failed", 0, "Run already exists", "Duplicate run GUID").ToJson();
            var carrier = UnityEngine.ScriptableObject.CreateInstance<UnityAAsyncCarrier>();
            carrier.name = Name(id); carrier.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            var context = new WhimTex.Tests.TestContext();
            var cancellation = new System.Threading.CancellationTokenSource();
            var state = new object[] { cancellation, null, (System.Func<int>)(() => context.Checks), null, null };
            carrier.State = state;
            async System.Threading.Tasks.Task Execute()
            {
                string status = "passed"; string failure = null;
                try { await body(context, cancellation.Token); if (context.Checks == 0) throw new System.InvalidOperationException("No assertions executed."); }
                catch (System.OperationCanceledException) { status = "cancelled"; }
                catch (System.Exception error) { status = "failed"; failure = error.ToString(); state[4] = error.Data["WhimTexCleanupFailure"] as string; }
                state[3] = failure == null ? WhimTex.Tests.TestContext.Result(status, context.Checks, "UnityA asynchronous case").ToJson()
                    : WhimTex.Tests.TestContext.Result(status, context.Checks, "UnityA asynchronous case", failure).ToJson();
            }
            state[1] = Execute();
            return Poll(id);
        }
        public static string Poll(string id)
        {
            var carrier = Find(id);
            if (carrier == null) return WhimTex.Tests.TestContext.Result("failed", 0, "Run missing", "No owned run carrier").ToJson();
            var state = State(carrier);
            return state[3] as string ?? WhimTex.Tests.TestContext.Result("running", ((System.Func<int>)state[2])(), "Waiting for owned work and cleanup").ToJson();
        }
        public static async System.Threading.Tasks.Task<string> Cancel(string id)
        {
            var carrier = Find(id);
            if (carrier == null) return WhimTex.Tests.TestContext.Result("failed", 0, "Cancellation cannot prove completion", "Owned carrier is missing; inspect Editor work").ToJson();
            var state = State(carrier);
            ((System.Threading.CancellationTokenSource)state[0]).Cancel();
            await (System.Threading.Tasks.Task)state[1];
            string result = (string)state[3];
            var finished = UnityEngine.JsonUtility.FromJson<WhimTex.Tests.TestResult>(result);
            return finished.status == "failed" ? result : WhimTex.Tests.TestContext.Result("cancelled", finished.checks, "Owned work stopped and its cleanup completed").ToJson();
        }
        public static async System.Threading.Tasks.Task<string> Cleanup(string id)
        {
            var carrier = Find(id);
            string cleanupFailure = null;
            if (carrier != null)
            {
                var state = State(carrier);
                ((System.Threading.CancellationTokenSource)state[0]).Cancel();
                await (System.Threading.Tasks.Task)state[1];
                cleanupFailure = state[4] as string;
                ((System.Threading.CancellationTokenSource)state[0]).Dispose();
                UnityEngine.Object.DestroyImmediate(carrier);
            }
            return WhimTex.Tests.TestContext.Run("Owned async carrier released", context =>
            {
                context.True(Find(id) == null, "No carrier remains");
                context.True(cleanupFailure == null, "Owned case cleanup failed: " + cleanupFailure);
            });
        }
        public static System.Threading.Tasks.Task Delay(int milliseconds, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.Delay(milliseconds, token);
    }

    public static class UnityACapture
    {
        sealed class Probe : UnityEngine.UIElements.ImmediateModeElement
        {
            internal UnityEditor.EditorWindow owner;
            internal string path;
            internal System.Threading.Tasks.TaskCompletionSource<bool> completion;
            bool done;
            protected override void ImmediateRepaint()
            {
                if (done) return;
                done = true;
                UnityEngine.Texture2D image = null;
                try
                {
                    var target = UnityEngine.RenderTexture.active;
                    int width = target != null ? target.width : UnityEngine.Mathf.RoundToInt(owner.position.width * UnityEditor.EditorGUIUtility.pixelsPerPoint);
                    int height = target != null ? target.height : UnityEngine.Mathf.RoundToInt(owner.position.height * UnityEditor.EditorGUIUtility.pixelsPerPoint);
                    image = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
                    image.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0, false); image.Apply(false);
                    System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(image));
                    completion.TrySetResult(true);
                }
                catch (System.Exception error) { completion.TrySetException(error); }
                finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
            }
        }
        public static async System.Threading.Tasks.Task Capture(UnityEditor.EditorWindow owner, string path, System.Threading.CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var completion = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var probe = new Probe { owner = owner, path = path, completion = completion, pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            probe.style.position = UnityEngine.UIElements.Position.Absolute; probe.style.width = 1; probe.style.height = 1;
            owner.rootVisualElement.Add(probe);
            try
            {
                using (token.Register(() => completion.TrySetCanceled())) { owner.Repaint(); await completion.Task; }
            }
            finally { probe.RemoveFromHierarchy(); }
        }
    }

    public static class UnityAFillWorker
    {
        public static async System.Threading.Tasks.Task Stop(UnityEditor.EditorWindow window)
        {
            if (window == null) return;
            var type = window.GetType();
            if (type.Assembly != typeof(DCFApixels.WhimTex.WhimTexWindow).Assembly || type.Name != "ContentFillWindow")
                throw new System.InvalidOperationException("Only an owned WhimTex ContentFillWindow can be joined.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            type.GetMethod("Cancel", flags).Invoke(window, null);
            var task = (System.Threading.Tasks.Task)type.GetField("task", flags).GetValue(window);
            if (task != null)
            {
                try { await task; }
                catch (System.OperationCanceledException) { }
                catch (System.Exception error) { throw new System.InvalidOperationException("Owned fill worker failed during cleanup.", error); }
            }
            if (window != null) type.GetMethod("Update", flags).Invoke(window, null);
        }
    }
}
