// Standalone owned native import-error fixture and independent release Faults/Deferred tests.
// Native postprocessor is generated only in this GUID's Editor folder, never in the Pipeline bundle.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FaultFixture
{
    const string Project = "D:/DCFA/Projects/Test6.6", Root = "Assets/WhimTexTestMigration";
    const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    [Serializable] public sealed class State
    {
        public int version = 1;
        public string runId, phase, folder, folderGuid, className, scriptGuid, simulatedGuid;
        public string[] selection;
        public string active, focus;
    }
    [Serializable] public sealed class Evidence
    {
        public int version = 1, compilationStarted, compilationFinished, beforeReload;
        public string runId, kind;
        public string[] errors = new string[0];
    }
    [Serializable] public sealed class Result
    {
        public int checks, beforeReload;
        public string status, message, runId, phase, className;
        public string[] failures = new string[0];
        public bool recoveryRequired, nativeCompilationComplete, nativeCompileRequired, oldDomainGone;
    }
    static void Id(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var guid) || guid.ToString("N") != id) throw new ArgumentException("Lowercase N GUID required.");
        if (!string.Equals(Path.GetFullPath(Application.dataPath), Path.GetFullPath(Project + "/Assets"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath("."), Path.GetFullPath(Project), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only connected Test6.6 may execute this fixture.");
    }
    static string Output(string id) { Id(id); return Path.GetFullPath(Project + "/Temp/WhimTex/fault-release/" + id); }
    static string StatePath(string id) => Path.Combine(Output(id), "state.json");
    static string Script(State s) => s.folder + "/Editor/" + s.className + ".cs";
    static string CompilePath(string id, string kind)
    { if (kind != "install" && kind != "cleanup") throw new ArgumentException("Expected install/cleanup."); return Path.Combine(Output(id), kind + "-compile.json"); }
    static string Key(string id, string kind) => "WhimTex.FaultFixture." + id + "." + kind;
    static string DocName(string id) => "WhimTex.FaultFixture.Document." + id;
    static void NoLinks(string path, bool tree = false)
    {
        for (string p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected fixture path: " + p);
        if (tree && Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) NoLinks(child, Directory.Exists(child));
    }
    static void NewJson(string path, object value)
    { NoLinks(path); using (var w = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))) w.Write(JsonUtility.ToJson(value, true)); }
    static void ReplaceJson(string path, object value)
    { string staged = path + ".new"; NewJson(staged, value); if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path); }
    static State Read(string id)
    {
        Id(id); NoLinks(StatePath(id)); var s = JsonUtility.FromJson<State>(File.ReadAllText(StatePath(id)));
        if (s == null || s.version != 1 || s.runId != id || s.folder != Root + "/" + id ||
            s.className != "WhimTexFaultProbe_" + id || s.selection == null) throw new InvalidOperationException("Malformed ownership journal.");
        NoLinks(s.folder); return s;
    }
    static void Save(State s) => ReplaceJson(StatePath(s.runId), s);
    static void Check(Result r, bool ok, string message) { r.checks++; if (!ok) throw new InvalidOperationException(message); }
    static void Idle()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Editor must be idle; native work cannot overlap.");
    }
    static Result Reply(State s, string message) => new Result { runId = s.runId, phase = s.phase, className = s.className, message = message };
    static string Entry(string id, string message, Action<State, Result> body)
    {
        var r = new Result { runId = id, message = message };
        try { var s = Read(id); r = Reply(s, message); Idle(); body(s, r); if (r.status == null) r.status = "passed"; }
        catch (Exception e) {
            r.status = "failed"; r.failures = new[] { e.ToString() };
            try { var state = Read(id); r.phase = state.phase; r.recoveryRequired = state.phase == "faults-running" || state.phase == "deferred-running"; }
            catch { r.recoveryRequired = true; }
        }
        return JsonUtility.ToJson(r);
    }
    static void Owned(State s, Result r) => Check(r, !string.IsNullOrEmpty(s.folderGuid) && AssetDatabase.IsValidFolder(s.folder) &&
        AssetDatabase.AssetPathToGUID(s.folder) == s.folderGuid, "Recorded folder GUID owns the fixture.");
    static Type Session => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession", true);
    static object Call(Type type, object instance, string name, params object[] args)
    {
        var method = type.GetMethods(Members).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        try { return method.Invoke(instance, args); } catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    static bool IsLive => (bool)Session.GetProperty("IsLive", Members).GetValue(null);
    static string Recovery => (string)Session.GetField("RecoveryKey", Members).GetValue(null);
    static void LiveIdle() { if (IsLive || EditorPrefs.HasKey(Recovery)) throw new InvalidOperationException("User live/recovery state must be idle; never replace it."); }
    static string Identity(Object value)
    {
        if (value == null) return "";
#if UNITY_6000_4_OR_NEWER
        return "entity:" + value.GetEntityId().ToString();
#else
        return "instance:" + value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
    static void Restore(State s)
    {
        var loaded = Resources.FindObjectsOfTypeAll<Object>();
        Object Find(string id) { if (string.IsNullOrEmpty(id)) return null; foreach (var o in loaded) if (o != null && Identity(o) == id) return o; return null; }
        var selected = new List<Object>(); foreach (string id in s.selection) { var value = Find(id); if (value != null) selected.Add(value); }
        Selection.objects = selected.ToArray(); Selection.activeObject = Find(s.active); var focus = Find(s.focus) as EditorWindow; if (focus != null) focus.Focus();
    }
    static Evidence EvidenceRead(string id, string kind)
    {
        var e = JsonUtility.FromJson<Evidence>(File.ReadAllText(CompilePath(id, kind)));
        if (e == null || e.version != 1 || e.runId != id || e.kind != kind || e.errors == null) throw new InvalidOperationException("Malformed native evidence."); return e;
    }
    static void Detach(string id, string kind)
    {
        var handlers = AppDomain.CurrentDomain.GetData(Key(id, kind)) as object[];
        if (handlers != null)
        {
            UnityEditor.Compilation.CompilationPipeline.compilationStarted -= (Action<object>)handlers[0];
            UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished -= (Action<string, UnityEditor.Compilation.CompilerMessage[]>)handlers[1];
            UnityEditor.Compilation.CompilationPipeline.compilationFinished -= (Action<object>)handlers[2];
            AssemblyReloadEvents.beforeAssemblyReload -= (AssemblyReloadEvents.AssemblyReloadCallback)handlers[3];
        }
        AppDomain.CurrentDomain.SetData(Key(id, kind), null);
    }
    static void Arm(string id, string kind)
    {
        NewJson(CompilePath(id, kind), new Evidence { runId = id, kind = kind });
        Action<Evidence> write = e => ReplaceJson(CompilePath(id, kind), e);
        Action<object> started = _ => { var e = EvidenceRead(id, kind); e.compilationStarted++; write(e); };
        Action<string, UnityEditor.Compilation.CompilerMessage[]> errors = (assembly, messages) => {
            var e = EvidenceRead(id, kind); var list = new List<string>(e.errors);
            foreach (var m in messages) if (m.type == UnityEditor.Compilation.CompilerMessageType.Error) list.Add(assembly + ": " + m.message);
            e.errors = list.ToArray(); write(e);
        };
        Action<object> finished = _ => { var e = EvidenceRead(id, kind); e.compilationFinished++; write(e); };
        AssemblyReloadEvents.AssemblyReloadCallback reload = () => { var e = EvidenceRead(id, kind); e.beforeReload++; write(e); };
        AppDomain.CurrentDomain.SetData(Key(id, kind), new object[] { started, errors, finished, reload });
        UnityEditor.Compilation.CompilationPipeline.compilationStarted += started;
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += errors;
        UnityEditor.Compilation.CompilationPipeline.compilationFinished += finished;
        AssemblyReloadEvents.beforeAssemblyReload += reload;
    }
    public static string Setup(string id)
    {
        var r = new Result { runId = id, message = "Install exact-GUID native import-error postprocessor" };
        try
        {
            Id(id); Idle(); LiveIdle(); string folder = Root + "/" + id, output = Output(id); NoLinks(folder); NoLinks(output);
            Check(r, !Directory.Exists(folder) && !File.Exists(folder) && !File.Exists(folder + ".meta") && !Directory.Exists(output) && !File.Exists(output), "Fresh GUID paths.");
            var selection = Selection.objects; var ids = new string[selection.Length]; for (int i = 0; i < ids.Length; i++) ids[i] = Identity(selection[i]);
            var s = new State { runId = id, phase = "installing", folder = folder, className = "WhimTexFaultProbe_" + id,
                selection = ids, active = Identity(Selection.activeObject), focus = Identity(EditorWindow.focusedWindow) };
            Directory.CreateDirectory(output); Save(s); NoLinks(Root);
            if (!AssetDatabase.IsValidFolder(Root)) { if (Directory.Exists(Root) || File.Exists(Root + ".meta")) throw new IOException("Unresolved asset root."); AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration"); }
            s.folderGuid = AssetDatabase.CreateFolder(Root, id); Save(s); Owned(s, r);
            Check(r, !string.IsNullOrEmpty(AssetDatabase.CreateFolder(folder, "Editor")), "Owned Editor folder.");
            Arm(id, "install");
            string source = "using UnityEditor;\npublic sealed class " + s.className + " : AssetPostprocessor {\n" +
                "void OnPreprocessTexture() { if (assetPath.StartsWith(\"" + folder + "/\", System.StringComparison.Ordinal) && System.IO.File.Exists(assetPath + \".failimport\")) " +
                "context.LogImportError(\"WHIMTEX_EXPECTED_IMPORT_FAILURE: exact owned GUID fixture.\"); }\n}\n";
            using (var writer = new StreamWriter(new FileStream(Script(s), FileMode.CreateNew, FileAccess.Write))) writer.Write(source);
            AssetDatabase.ImportAsset(Script(s), ImportAssetOptions.ForceSynchronousImport);
            s.scriptGuid = AssetDatabase.AssetPathToGUID(Script(s)); s.phase = "installed"; Save(s);
            Check(r, !string.IsNullOrEmpty(s.scriptGuid), "Native postprocessor script GUID recorded."); r.phase = s.phase; r.className = s.className; r.status = "passed";
        }
        catch (Exception e) { r.status = "failed"; r.failures = new[] { e.ToString() }; }
        return JsonUtility.ToJson(r);
    }
    public static string VerifyNativeCompile(string id, string kind) => Entry(id, "Verify native own-class compilation/domain reload", (s, r) => {
        var e = EvidenceRead(id, kind); r.beforeReload = e.beforeReload; r.oldDomainGone = AppDomain.CurrentDomain.GetData(Key(id, kind)) == null;
        Check(r, e.errors.Length == 0 && e.compilationStarted > 0 && e.compilationFinished > 0 && e.beforeReload > 0 && r.oldDomainGone, "Owned native events, no compiler errors, and old-domain carrier gone.");
        if (kind == "install")
        {
            Owned(s, r); var script = AssetDatabase.LoadAssetAtPath<MonoScript>(Script(s)); var type = script == null ? null : script.GetClass();
            Check(r, AssetDatabase.AssetPathToGUID(Script(s)) == s.scriptGuid && type != null && type.FullName == s.className && typeof(AssetPostprocessor).IsAssignableFrom(type), "Exact GUID native MonoScript.GetClass resolves own postprocessor.");
        }
        else
        {
            Check(r, s.phase == "cleaned" && !AssetDatabase.IsValidFolder(s.folder) && AssetDatabase.LoadAssetAtPath<MonoScript>(Script(s)) == null, "Exact owned script/folder absent.");
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Check(r, assembly.GetType(s.className) == null, "Exact GUID postprocessor class absent from every post-cleanup assembly.");
            Restore(s);
        }
        r.nativeCompilationComplete = true;
    });
    static TextureCompositor Document(State s)
    {
        var doc = ScriptableObject.CreateInstance<TextureCompositor>(); doc.hideFlags = HideFlags.HideAndDontSave; doc.name = DocName(s.runId);
        doc.width = doc.height = 32; doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.green })); return doc;
    }
    static TextureCompositor FindDoc(State s) => Resources.FindObjectsOfTypeAll<TextureCompositor>().SingleOrDefault(d => d.name == DocName(s.runId) && !AssetDatabase.Contains(d));
    static void StopOwned(TextureCompositor doc)
    { if (doc != null) Call(Session, null, "StopFor", doc, "owned fault fixture cleanup"); }
    static void Reject(Result r, Action action, string message)
    {
        try { action(); } catch (Exception e) { if (e is TargetInvocationException) e = e.InnerException;
            Check(r, e is IOException || e is WhimTexDocumentException || e is UnityEditor.Build.BuildFailedException || e is InvalidOperationException || e is OperationCanceledException, message + ": " + e); return; }
        Check(r, false, "Expected failure: " + message);
    }
    static void ClearOwnRecovery(State s)
    {
        string current = EditorPrefs.GetString(Recovery, ""), guid = AssetDatabase.AssetPathToGUID(s.folder + "/Fault.tiff");
        if (current.Length != 0 && (current == guid || current == s.simulatedGuid)) EditorPrefs.DeleteKey(Recovery);
    }
    public static string Faults(string id) => Entry(id, "Independent write/import/build-guard/recovery fault assertions", (s, r) => {
        Owned(s, r); LiveIdle(); Check(r, s.phase == "installed", "Faults is one-shot after native installation.");
        s.phase = "faults-running"; Save(s); var doc = Document(s); TextureCompositor loaded = null;
        string raw = s.folder + "/transaction.bin", path = s.folder + "/Fault.tiff", orphan = raw + ".00000000000000000000000000000000.whimtex-tmp";
        try
        {
            byte[] original = { 10, 20, 30, 40 }; File.WriteAllBytes(raw, original);
            Reject(r, () => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(stream => { stream.WriteByte(99); throw new IOException("Injected interrupted write"); })), "interrupted existing write");
            Check(r, File.ReadAllBytes(raw).SequenceEqual(original), "Interrupted write preserves old bytes.");
            string fresh = s.folder + "/new.bin";
            Reject(r, () => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", fresh, (Action<Stream>)(stream => { stream.WriteByte(99); throw new OperationCanceledException(); })), "cancel first save");
            Check(r, !File.Exists(fresh), "No cancelled partial destination.");
            Reject(r, () => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(stream => { stream.WriteByte(98); File.WriteAllBytes(raw, new byte[] { 7, 8, 9 }); })), "external write during staging");
            Check(r, File.ReadAllBytes(raw).SequenceEqual(new byte[] { 7, 8, 9 }), "External bytes retained.");
            using (var locked = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(r, () => Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(stream => stream.WriteByte(12))), "locked destination");
            Check(r, Directory.GetFiles(s.folder, "*.whimtex-tmp").Length == 0, "Failed staging cleaned temporary files.");
            File.WriteAllBytes(orphan, new byte[] { 255 });
            Call(typeof(WhimTexDocumentContainer), null, "WriteStaged", raw, (Action<Stream>)(stream => stream.Write(original, 0, original.Length)));
            Check(r, File.ReadAllBytes(raw).SequenceEqual(original) && File.Exists(orphan), "Hard-exit orphan neither promoted nor deleted.");
            WhimTexDocumentFile.Save(doc, path); string guid = AssetDatabase.AssetPathToGUID(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = false; importer.SaveAndReimport();
            string meta = File.ReadAllText(path + ".meta"); byte[] saved = File.ReadAllBytes(path);
            ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
            Check(r, (bool)Call(Session, null, "Start", doc, path), "Owned red live before build guard.");
            Call(Session, null, "PrepareForBuild"); Check(r, !IsLive, "Build guard ends live.");
            Check(r, !((TextureImporter)AssetImporter.GetAtPath(path)).isReadable && File.ReadAllText(path + ".meta") == meta, "Exact saved meta restored.");
            Check(r, File.ReadAllBytes(path).SequenceEqual(saved) && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.red, "Guard neither saves nor discards red edits.");
            Call(Session, null, "PrepareForBuild"); Check(r, !EditorPrefs.HasKey(Recovery), "Guard idempotent.");
            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            Check(r, (bool)Call(Session, null, "Start", doc, path), "Originally readable live session.");
            using (var pause = (IDisposable)Call(Session, null, "SuspendForSave", doc)) Reject(r, () => Call(Session, null, "PrepareForBuild"), "build during save rejected");
            Call(Session, null, "PrepareForBuild"); Check(r, ((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Originally readable setting retained.");
            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            EditorPrefs.SetString(Recovery, guid); Call(Session, null, "RecoverReadable");
            Check(r, !EditorPrefs.HasKey(Recovery) && !((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Crash journal restores readability.");
            s.simulatedGuid = Guid.NewGuid().ToString("N"); Save(s); EditorPrefs.SetString(Recovery, s.simulatedGuid); Call(Session, null, "RecoverReadable");
            Check(r, EditorPrefs.HasKey(Recovery), "Missing asset retains journal.");
            Reject(r, () => Call(Session, null, "PrepareForBuild"), "unresolved journal blocks guard");
            Check(r, !(bool)Call(Session, null, "Start", doc, path) && EditorPrefs.GetString(Recovery) == s.simulatedGuid, "Unresolved journal cannot be replaced by another start.");
            ClearOwnRecovery(s);
            File.WriteAllText(path + ".failimport", "expected fault");
            Reject(r, () => WhimTexDocumentFile.Save(doc, path), "import failure reported after saved bytes");
            Check(r, AssetDatabase.AssetPathToGUID(path) == guid && File.Exists(path), "Failed import retains file/GUID.");
            File.Delete(path + ".failimport"); WhimTexDocumentFile.Save(doc, path);
            Check(r, !(bool)Call(typeof(WhimTexDocumentFile), null, "ImportHasErrors", path), "Identical-byte retry repairs failed import.");
            loaded = WhimTexDocumentFile.Load(path); Check(r, ((ColorFillLayerBehaviour)loaded.layers[0].Behaviour).color == Color.red, "Retry retains newest contents.");
            importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.isReadable = true; importer.SaveAndReimport();
            EditorPrefs.SetString(Recovery, guid); File.WriteAllText(path + ".failimport", "expected recovery fault");
            Reject(r, () => Call(Session, null, "RecoverReadable"), "failed recovery import reported");
            Check(r, EditorPrefs.HasKey(Recovery), "Failed recovery keeps durable journal.");
            Reject(r, () => Call(Session, null, "PrepareForBuild"), "failed recovery blocks guard");
            File.Delete(path + ".failimport"); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Call(Session, null, "RecoverReadable"); Check(r, !EditorPrefs.HasKey(Recovery), "Recovery retry succeeds.");
            var external = ScriptableObject.CreateInstance<TextureCompositor>(); external.hideFlags = HideFlags.HideAndDontSave; external.width = external.height = 32;
            external.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.blue }));
            try { WhimTexDocumentFile.Save(external, s.folder + "/External.tiff"); } finally { Object.DestroyImmediate(external); }
            File.Copy(s.folder + "/External.tiff", path, true); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(3));
            Reject(r, () => WhimTexDocumentFile.Save(doc, path), "external replacement blocks stale save");
        }
        finally
        {
            File.Delete(path + ".failimport"); File.Delete(orphan); ClearOwnRecovery(s); StopOwned(doc);
            if (loaded != null) Object.DestroyImmediate(loaded); Object.DestroyImmediate(doc); Restore(s);
            s.phase = "faults-completed"; Save(s); r.phase = s.phase;
        }
    });
    public static string StartDeferred(string id) => Entry(id, "Start independent native deferred import-failure/retry", (s, r) => {
        Owned(s, r); LiveIdle(); Check(r, s.phase == "faults-completed", "Faults has completed and drained.");
        var doc = Document(s); string path = s.folder + "/Deferred.tiff";
        WhimTexDocumentFile.Save(doc, path); ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
        File.WriteAllText(path + ".failimport", "expected deferred fault");
        s.phase = "deferred-running"; Save(s); WhimTexDocumentFile.Save(doc, path, true);
        double after = EditorApplication.timeSinceStartup + .25;
        EditorApplication.CallbackFunction callback = null;
        callback = () => { if (EditorApplication.timeSinceStartup < after) return; EditorApplication.update -= callback;
            AppDomain.CurrentDomain.SetData(Key(id, "deferred"), null); FinishDeferred(id); };
        AppDomain.CurrentDomain.SetData(Key(id, "deferred"), callback); EditorApplication.update += callback;
        r.status = "running"; r.phase = s.phase;
    });
    static void FinishDeferred(string id)
    {
        var s = Read(id); var r = Reply(s, "Deferred failure is retryable and retry repairs native import"); var doc = FindDoc(s);
        string path = s.folder + "/Deferred.tiff";
        try
        {
            Check(r, doc != null, "Exact GUID deferred document remains owned.");
            var binding = typeof(TextureCompositor).GetField("documentBinding", Members).GetValue(doc);
            Check(r, binding != null && (bool)binding.GetType().GetField("dirty", Members).GetValue(binding), "Deferred failure marks document retryable.");
            File.Delete(path + ".failimport"); WhimTexDocumentFile.Save(doc, path);
            Check(r, !(bool)Call(typeof(WhimTexDocumentFile), null, "ImportHasErrors", path), "Retry repairs deferred failed import."); r.status = "passed";
        }
        catch (Exception e) { r.status = "failed"; r.failures = new[] { e.ToString() }; }
        finally
        {
            try { File.Delete(path + ".failimport"); if (doc != null) Object.DestroyImmediate(doc); Restore(s); }
            catch (Exception e) { r.status = "failed"; r.failures = new[] { e.ToString() }; r.recoveryRequired = true; }
            s.phase = "deferred-completed"; Save(s); r.phase = s.phase; NewJson(Path.Combine(Output(id), "deferred-result.json"), r);
        }
    }
    public static string PollDeferred(string id)
    {
        var s = Read(id); string path = Path.Combine(Output(id), "deferred-result.json");
        if (File.Exists(path)) return File.ReadAllText(path);
        var r = Reply(s, "Waiting for owned deferred retry"); r.status = "running";
        if (s.phase != "deferred-running" || AppDomain.CurrentDomain.GetData(Key(id, "deferred")) == null)
        { r.status = "failed"; r.failures = new[] { "Deferred owner/callback disappeared without a terminal result." }; r.recoveryRequired = true; }
        return JsonUtility.ToJson(r);
    }
    public static string Cleanup(string id) => Entry(id, "Remove exact GUID native fixture and restore borrowed selection/focus", (s, r) => {
        Check(r, s.phase != "deferred-running" && s.phase != "faults-running", "Do not clean running/uncertain tests.");
        var doc = FindDoc(s); StopOwned(doc); if (doc != null) { Undo.ClearUndo(doc); Object.DestroyImmediate(doc); }
        ClearOwnRecovery(s);
        if (AssetDatabase.IsValidFolder(s.folder))
        {
            Owned(s, r); NoLinks(s.folder, true); Detach(id, "install");
            r.nativeCompileRequired = File.Exists(Script(s)) || !string.IsNullOrEmpty(s.scriptGuid);
            if (r.nativeCompileRequired) Arm(id, "cleanup");
            Check(r, AssetDatabase.DeleteAsset(s.folder), "Delete owned GUID folder through AssetDatabase.");
        }
        else Check(r, !Directory.Exists(s.folder) && !File.Exists(s.folder + ".meta"), "No unresolved folder/meta.");
        Restore(s); s.phase = "cleaned"; Save(s); r.phase = s.phase;
    });
}
