// Independent replacement for the legacy Player release lifecycle. Parent owns ALL execution.
// Compile this source alone in Pipeline; never append the runtime probe to its temporary assembly.
// Standalone PlayerWorkflow owns Setup -> native compilation/reload -> PreparePlayer -> RestartPlayerLive -> Trigger
// -> connected native build/build_status -> native-result.json -> idle Editor -> InspectBuild -> Player -> InspectPlayer -> Cleanup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PlayerReleaseTests
{
    const string Project = "D:/DCFA/Projects/Test6.6";
    const string AssetRoot = "Assets/WhimTexTestMigration";
    const string ProbeSource = "Packages/com.dcfapixels.whimtex/Tests~/Framework/Native/PlayerProbe.cs";
    const string ProbeClass = "WhimTexMigrationPlayerProbe";
    const string DocumentMarker = "WhimTex.PlayerRelease.Document.";
    const BindingFlags PackageMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    // Public serializable DTOs: durable wire is JSON of BCL values, never Unity-object/static owner state.
    [Serializable] public sealed class State
    {
        public int version = 1, attempt;
        public string runId, folderGuid, phase, target, sceneGuid, documentGuid, probeGuid;
        public string savedHash, metaHash, folder, output, scene, document, resourcePrefix;
        public string[] selection;
        public string activeSelection, focus, activeScene;
        public long sourceBytes, imageBytes;
    }
    [Serializable] public sealed class Packed
    {
        public string path, type;
        public ulong bytes;
    }
    [Serializable] public sealed class Result
    {
        public int version = 1, checks, attempt;
        public string status, message, runId, phase, target, folder, output, scene, executable, buildMarker, nativeMarker, playerReport;
        public string[] failures = new string[0];
        public bool recoveryRequired, buildReturned;
        public bool nativeCompilationComplete, nativeCompileRequired, oldDomainGone;
        public int beforeReload;
        public long sourceBytes, imageBytes;
        public ulong totalBytes;
        public double buildSeconds;
        public string buildResult;
        public Color livePixel;
        public List<Packed> packed = new List<Packed>();
    }
    [Serializable] public sealed class PlayerSample
    {
        public string name, format;
        public int width, height, mips;
        public bool readable;
        public double loadMs;
        public Color pixel;
    }
    [Serializable] public sealed class PlayerResult
    {
        public int version, checks;
        public string runId, status, unity, graphics;
        public string[] failures, assemblies;
        public PlayerSample[] samples;
    }
    [Serializable] public sealed class NativeCompletion
    {
        public int version, attempt;
        public string runId, target, outputPath, status;
        public bool completed;
    }
    [Serializable] public sealed class CompileEvidence
    {
        public int version = 1, beforeReload, compilationStarted, compilationFinished;
        public string runId, kind;
        public string[] errors = new string[0];
    }

    static string Id(string runId)
    {
        if (!Guid.TryParseExact(runId, "N", out var guid) || guid.ToString("N") != runId)
            throw new ArgumentException("Lowercase N-format GUID required.");
        RequireProject();
        return runId;
    }
    static void RequireProject()
    {
        if (!Same(Path.GetFullPath(Application.dataPath), Path.GetFullPath(Project + "/Assets")) ||
            !Same(Path.GetFullPath("."), Path.GetFullPath(Project)))
            throw new InvalidOperationException("Only the connected Test6.6 Editor/project may execute these entries.");
    }
    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static string Output(string id) => Path.GetFullPath(Project + "/Temp/WhimTex/player-release/" + id);
    static string StatePath(string id) => Path.Combine(Output(id), "state.json");
    static string AttemptDirectory(State s) => Path.Combine(s.output, "attempt-" + s.attempt.ToString("D3"));
    static string BuildMarker(State s) => Path.Combine(AttemptDirectory(s), "build-result.json");
    static string NativeMarker(State s) => Path.Combine(AttemptDirectory(s), "native-result.json");
    static string PlayerReport(State s) => Path.Combine(AttemptDirectory(s), "player-result.json");
    static string ProbePath(State s) => s.folder + "/" + ProbeClass + ".cs";
    static string CompilePath(string runId, string kind)
    {
        Id(runId);
        if (kind != "install" && kind != "cleanup") throw new ArgumentException("Expected install/cleanup native phase.");
        return Path.Combine(Output(runId), kind + "-compile.json");
    }
    static string CompileKey(string runId, string kind) => "WhimTex.PlayerRelease.Native." + runId + "." + kind;
    static CompileEvidence ReadCompile(string runId, string kind)
    {
        string path = CompilePath(runId, kind); NoLinks(path);
        var e = JsonUtility.FromJson<CompileEvidence>(File.ReadAllText(path));
        if (e == null || e.version != 1 || e.runId != runId || e.kind != kind || e.errors == null)
            throw new InvalidOperationException("Malformed native compilation evidence.");
        return e;
    }
    static void WriteCompile(CompileEvidence e)
    {
        string path = CompilePath(e.runId, e.kind), staged = path + ".new";
        NewJson(staged, e); File.Replace(staged, path, null);
    }
    static void DetachCompile(string runId, string kind)
    {
        // BCL carrier contains only public callback delegates. It disappears on a real domain reload.
        var callbacks = AppDomain.CurrentDomain.GetData(CompileKey(runId, kind)) as object[];
        if (callbacks != null)
        {
            UnityEditor.Compilation.CompilationPipeline.compilationStarted -= (Action<object>)callbacks[0];
            UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished -= (Action<string, UnityEditor.Compilation.CompilerMessage[]>)callbacks[1];
            UnityEditor.Compilation.CompilationPipeline.compilationFinished -= (Action<object>)callbacks[2];
            AssemblyReloadEvents.beforeAssemblyReload -= (AssemblyReloadEvents.AssemblyReloadCallback)callbacks[3];
        }
        AppDomain.CurrentDomain.SetData(CompileKey(runId, kind), null);
    }
    static void ArmCompile(string runId, string kind)
    {
        string path = CompilePath(runId, kind);
        NewJson(path, new CompileEvidence { runId = runId, kind = kind });
        Action<object> started = _ => { var e = ReadCompile(runId, kind); e.compilationStarted++; WriteCompile(e); };
        Action<string, UnityEditor.Compilation.CompilerMessage[]> errors = (assembly, messages) => {
            var e = ReadCompile(runId, kind); var list = new List<string>(e.errors);
            foreach (var message in messages) if (message.type == UnityEditor.Compilation.CompilerMessageType.Error)
                list.Add(assembly + ": " + message.message);
            e.errors = list.ToArray(); WriteCompile(e);
        };
        Action<object> finished = _ => { var e = ReadCompile(runId, kind); e.compilationFinished++; WriteCompile(e); };
        AssemblyReloadEvents.AssemblyReloadCallback reload = () => { var e = ReadCompile(runId, kind); e.beforeReload++; WriteCompile(e); };
        AppDomain.CurrentDomain.SetData(CompileKey(runId, kind), new object[] { started, errors, finished, reload });
        UnityEditor.Compilation.CompilationPipeline.compilationStarted += started;
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += errors;
        UnityEditor.Compilation.CompilationPipeline.compilationFinished += finished;
        AssemblyReloadEvents.beforeAssemblyReload += reload;
    }
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
    static void NoLinks(string path)
    {
        for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing redirected fixture/output: " + current);
    }
    static void NoTreeLinks(string path)
    {
        NoLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (string child in Directory.EnumerateFileSystemEntries(path))
        {
            NoLinks(child);
            if (Directory.Exists(child)) NoTreeLinks(child);
        }
    }
    static void NewJson(string path, object value)
    {
        NoLinks(path);
        using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)))
            writer.Write(JsonUtility.ToJson(value, true));
    }
    static void WriteState(State s)
    {
        string path = StatePath(s.runId), staged = path + ".new";
        NewJson(staged, s);
        if (File.Exists(path)) File.Replace(staged, path, null);
        else File.Move(staged, path);
    }
    static State Read(string runId)
    {
        string id = Id(runId); NoLinks(StatePath(id));
        var s = JsonUtility.FromJson<State>(File.ReadAllText(StatePath(id)));
        if (s == null || s.version != 1 || s.runId != id || s.folder != AssetRoot + "/" + id ||
            s.output != Output(id) || s.resourcePrefix != "WhimTexPlayer_" + id || s.selection == null ||
            s.scene != s.folder + "/Probe.unity" ||
            s.document != s.folder + "/Resources/" + s.resourcePrefix + "/Document.tiff")
            throw new InvalidOperationException("Missing/malformed owned GUID state; retain the outer lock.");
        NoLinks(s.folder); NoLinks(s.output);
        return s;
    }
    static void Check(Result r, bool value, string message)
    { r.checks++; if (!value) throw new InvalidOperationException(message); }
    static void EditorIdle()
    {
        RequireProject();
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Editor must be idle; do not compile/build/mutate concurrently.");
    }
    static bool InFlight(State s) => s.phase == "queued" || s.phase == "building";
    static void Quiescent(State s)
    {
        EditorIdle();
        if (InFlight(s)) throw new InvalidOperationException("Build completion is uncertain; retain outer lock and fixtures. Poll files only.");
    }
    static Result Reply(State s, string message)
    {
        return new Result { runId = s.runId, attempt = s.attempt, phase = s.phase, target = s.target,
            folder = s.folder, output = s.output, scene = s.scene, sourceBytes = s.sourceBytes, imageBytes = s.imageBytes,
            executable = s.attempt == 0 ? null : Executable(s), buildMarker = s.attempt == 0 ? null : BuildMarker(s),
            nativeMarker = s.attempt == 0 ? null : NativeMarker(s),
            playerReport = s.attempt == 0 ? null : PlayerReport(s), message = message };
    }
    static string Entry(string runId, string message, Action<State, Result> body)
    {
        var result = new Result { runId = runId, message = message };
        try
        {
            var s = Read(runId); result = Reply(s, message); body(s, result);
            if (result.status == null) result.status = "passed";
        }
        catch (Exception error)
        {
            result.status = "failed"; result.failures = new[] { error.ToString() };
            // A failed protocol/transport is never implicit cancellation of Editor work.
            try { result.recoveryRequired = InFlight(Read(runId)) || BuildPipeline.isBuildingPlayer; }
            catch { result.recoveryRequired = true; }
        }
        return JsonUtility.ToJson(result);
    }

    // Reflection is confined to package types. No Unity internals/reflected ownership DTOs.
    static Type SessionType => typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession", true);
    static object SessionCall(string name, Type[] signature, params object[] args)
    {
        var method = SessionType.GetMethod(name, PackageMembers, null, signature, null);
        if (method == null) throw new MissingMethodException(SessionType.FullName, name);
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    static bool IsLive => (bool)SessionType.GetProperty("IsLive", PackageMembers).GetValue(null);
    static string LivePath => (string)SessionType.GetProperty("LivePath", PackageMembers).GetValue(null);
    static bool IsLiveFor(TextureCompositor doc) => (bool)SessionCall("IsLiveFor", new[] { typeof(TextureCompositor) }, doc);
    static void LiveIdle()
    {
        if (IsLive) throw new InvalidOperationException("A live session already exists; never stop/replace another document.");
        string key = (string)SessionType.GetField("RecoveryKey", PackageMembers).GetValue(null);
        if (EditorPrefs.HasKey(key)) throw new InvalidOperationException("Existing live recovery must be resolved by its owner first.");
    }
    static string Identity(Object value)
    {
        if (value == null) return "";
#if UNITY_6000_4_OR_NEWER
        return "entity:" + value.GetEntityId().ToString();
#else
        return "instance:" + value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
    static Object Resolve(string identity, Object[] loaded)
    {
        if (string.IsNullOrEmpty(identity)) return null;
        foreach (var value in loaded) if (value != null && Identity(value) == identity) return value;
        return null;
    }
    static void RestoreBorrowed(State s)
    {
        var loaded = Resources.FindObjectsOfTypeAll<Object>(); var selected = new List<Object>();
        foreach (string id in s.selection) { var value = Resolve(id, loaded); if (value != null) selected.Add(value); }
        Selection.objects = selected.ToArray(); Selection.activeObject = Resolve(s.activeSelection, loaded);
        var focus = Resolve(s.focus, loaded) as EditorWindow; if (focus != null) focus.Focus();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.handle.ToString() == s.activeScene) { SceneManager.SetActiveScene(scene); break; }
        }
    }
    static void OwnedFolder(State s, Result r)
    {
        Check(r, !string.IsNullOrEmpty(s.folderGuid) && AssetDatabase.IsValidFolder(s.folder) &&
            AssetDatabase.AssetPathToGUID(s.folder) == s.folderGuid, "Exact recorded GUID owns the asset folder.");
    }
    static void CreateFolder(string parent, string name)
    {
        string path = parent + "/" + name; NoLinks(path);
        if (Directory.Exists(path) || File.Exists(path) || File.Exists(path + ".meta"))
            throw new IOException("Fresh fixture folder/meta already exists: " + path);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name))) throw new IOException("Cannot create fixture folder: " + path);
    }
    static BuildTarget Target()
    {
        var target = EditorUserBuildSettings.activeBuildTarget;
        if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64 &&
            target != BuildTarget.StandaloneOSX && target != BuildTarget.StandaloneLinux64)
            throw new InvalidOperationException("Current target must already be supported Standalone; parent must not switch project settings.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            throw new InvalidOperationException("Current Standalone support is unavailable; never install dependencies/modules.");
        return target;
    }
    static string Executable(State s)
    {
        var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), s.target);
        string suffix = target == BuildTarget.StandaloneOSX ? ".app" :
            target == BuildTarget.StandaloneLinux64 ? "" : ".exe";
        return Path.Combine(AttemptDirectory(s), "Player" + suffix);
    }

    public static string Setup(string runId)
    {
        var r = new Result { runId = runId, message = "Install owned native Player probe; parent must wait for native compilation/reload" };
        try
        {
            string id = Id(runId); EditorIdle(); LiveIdle(); var target = Target();
            string output = Output(id), folder = AssetRoot + "/" + id; NoLinks(output); NoLinks(folder);
            Check(r, !Directory.Exists(output) && !File.Exists(output) && !Directory.Exists(folder) &&
                !File.Exists(folder) && !File.Exists(folder + ".meta"), "GUID output/asset paths are fresh.");
            var selection = Selection.objects; var ids = new string[selection.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Identity(selection[i]);
            var s = new State { runId = id, phase = "installing", target = target.ToString(), folder = folder,
                output = output, resourcePrefix = "WhimTexPlayer_" + id, selection = ids,
                activeSelection = Identity(Selection.activeObject), focus = Identity(EditorWindow.focusedWindow),
                activeScene = SceneManager.GetActiveScene().handle.ToString(), scene = folder + "/Probe.unity",
                document = folder + "/Resources/WhimTexPlayer_" + id + "/Document.tiff" };
            Directory.CreateDirectory(output); WriteState(s);
            NoLinks(AssetRoot);
            if (!AssetDatabase.IsValidFolder(AssetRoot)) CreateFolder("Assets", "WhimTexTestMigration");
            CreateFolder(AssetRoot, id); s.folderGuid = AssetDatabase.AssetPathToGUID(folder); WriteState(s);
            ArmCompile(id, "install"); // Must precede native script import, including a very fast automatic reload.
            File.Copy(ProbeSource, ProbePath(s), false);
            AssetDatabase.ImportAsset(ProbePath(s), ImportAssetOptions.ForceSynchronousImport);
            s.probeGuid = AssetDatabase.AssetPathToGUID(ProbePath(s)); s.phase = "installed"; WriteState(s);
            r = Reply(s, r.message);
            Check(r, !string.IsNullOrEmpty(s.probeGuid), "Probe installed through AssetDatabase in the owned GUID folder.");
            r.status = "passed";
        }
        catch (Exception error) { r.status = "failed"; r.failures = new[] { error.ToString() }; }
        return JsonUtility.ToJson(r);
    }

    public static string VerifyNativeCompile(string runId, string kind) => Entry(runId, "Verify actual native compilation/reload and installed probe class", (s, r) =>
    {
        Quiescent(s); var e = ReadCompile(runId, kind);
        r.beforeReload = e.beforeReload; r.oldDomainGone = AppDomain.CurrentDomain.GetData(CompileKey(runId, kind)) == null;
        Check(r, e.errors.Length == 0, "Native compiler errors: " + string.Join("\n", e.errors));
        Check(r, e.compilationStarted > 0 && e.compilationFinished > 0 && e.beforeReload > 0 && r.oldDomainGone,
            "Owned callbacks observed compilation and actual domain reload; old BCL carrier disappeared.");
        if (kind == "install")
        {
            Check(r, AssetDatabase.AssetPathToGUID(ProbePath(s)) == s.probeGuid, "Exact native probe GUID.");
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ProbePath(s)); var type = script == null ? null : script.GetClass();
            Check(r, type != null && type.FullName == ProbeClass && typeof(MonoBehaviour).IsAssignableFrom(type), "Native installed MonoScript.GetClass resolves the runtime probe.");
        }
        else
        {
            Check(r, s.phase == "cleaned" && !AssetDatabase.IsValidFolder(s.folder) &&
                AssetDatabase.LoadAssetAtPath<MonoScript>(ProbePath(s)) == null, "Owned native script is removed after cleanup compilation.");
            RestoreBorrowed(s); // Restore focus/selection again after the final native reload.
        }
        r.nativeCompilationComplete = true;
    });

    static TextureCompositor FindDocument(State s)
    {
        TextureCompositor found = null;
        foreach (var doc in Resources.FindObjectsOfTypeAll<TextureCompositor>())
            if (doc.name == DocumentMarker + s.runId && !AssetDatabase.Contains(doc))
            {
                if (found != null) throw new InvalidOperationException("Ambiguous owned document marker.");
                found = doc;
            }
        return found;
    }
    static Color ReadLiveGpuPixel(Texture2D texture)
    {
        // Live.Publish uses Graphics.CopyTexture: the readable CPU copy is not the published GPU image.
        // Never Apply the imported target to make GetPixel work: that would overwrite live GPU pixels.
        RenderTexture previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        RenderTexture readback = null;
        Texture2D cpu = null;
        try
        {
            readback = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            GL.sRGBWrite = false;
            Graphics.Blit(texture, readback);
            RenderTexture.active = readback;
            cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            cpu.Apply(false, false);
            return cpu.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousSrgb;
            if (readback != null) RenderTexture.ReleaseTemporary(readback);
            if (cpu != null) Object.DestroyImmediate(cpu);
        }
    }
    static void StartRed(State s, Result r)
    {
        var doc = FindDocument(s);
        if (IsLive)
        {
            Check(r, doc != null && IsLiveFor(doc) && LivePath == s.document, "Existing live session belongs to this exact document.");
            SessionCall("StopFor", new[] { typeof(TextureCompositor), typeof(string) }, doc, "owned Player restart");
        }
        LiveIdle();
        if (doc == null) { doc = WhimTexDocumentFile.Load(s.document); doc.name = DocumentMarker + s.runId; doc.hideFlags = HideFlags.HideAndDontSave; }
        Check(r, doc.width == 256 && doc.height == 256 && doc.layers.Count == 3 &&
            doc.layers[1].Behaviour is DrawingLayerBehaviour && doc.layers[2].Behaviour is DrawingLayerBehaviour &&
            !doc.layers[1].enabled && !doc.layers[2].enabled, "256px document retains both disabled Drawing layers.");
        ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color = Color.red;
        Check(r, (bool)SessionCall("Start", new[] { typeof(TextureCompositor), typeof(string) }, doc, s.document), "Owned unsaved red Live Update starts.");
        SessionCall("Publish", Type.EmptyTypes);
        Check(r, IsLiveFor(doc) && LivePath == s.document, "Publish keeps the same owned Live Update.");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(s.document);
        Check(r, texture != null && texture.isReadable, "Live target is temporarily readable.");
        Color red = r.livePixel = ReadLiveGpuPixel(texture);
        Check(r, red.r >= .9f && red.g <= .05f && red.b <= .05f, "Red unsaved pixels are actually published before build.");
        Check(r, Hash(s.document) == s.savedHash, "Live red did not overwrite the saved green TIFF.");
    }

    public static string PreparePlayer(string runId) => Entry(runId, "Prepare saved green and unsaved live red Player fixtures", (s, r) =>
    {
        Quiescent(s); LiveIdle(); OwnedFolder(s, r);
        Check(r, s.phase == "installed" && s.attempt == 0, "Prepare is one-shot after native probe compilation.");
        Check(r, Target().ToString() == s.target, "Current supported Standalone target has not changed.");
        Check(r, AssetDatabase.AssetPathToGUID(ProbePath(s)) == s.probeGuid, "Same owned probe GUID.");
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ProbePath(s));
        var probeType = script == null ? null : script.GetClass();
        Check(r, probeType != null && probeType.FullName == ProbeClass && typeof(MonoBehaviour).IsAssignableFrom(probeType),
            "Native compilation has installed the runtime MonoBehaviour; temporary Pipeline definitions cannot substitute.");
        s.phase = "preparing"; WriteState(s);
        string resources = s.folder + "/Resources", images = resources + "/" + s.resourcePrefix;
        CreateFolder(s.folder, "Resources"); CreateFolder(resources, s.resourcePrefix);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.name = DocumentMarker + s.runId; doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = 256; doc.layers.Add(new Layer(new ColorFillLayerBehaviour { color = Color.green }));
        for (int i = 0; i < 2; i++)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                byte[] noise = new byte[1024 * 1024 * 4]; new System.Random(17 + i).NextBytes(noise);
                texture.LoadRawTextureData(noise); texture.Apply();
                var drawing = new DrawingLayerBehaviour();
                // Package-private data injection preserves the exact archived seed/size fixture.
                typeof(DrawingLayerBehaviour).GetField("pixels", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(drawing, texture);
                doc.layers.Add(new Layer(drawing) { enabled = false });
            }
            catch { Object.DestroyImmediate(texture); throw; }
        }
        WhimTexDocumentFile.Save(doc, s.document);
        byte[] file = File.ReadAllBytes(s.document);
        Check(r, file.Length >= 16, "Layered TIFF has a container footer.");
        long containerLength = BitConverter.ToInt64(file, file.Length - 16);
        Check(r, containerLength > 0 && containerLength < file.Length - 16, "Container length leaves a nonempty identical flattened TIFF.");
        int imageBytes = checked((int)(file.Length - 16 - containerLength));
        using (var stream = new FileStream(images + "/Plain.tiff", FileMode.CreateNew, FileAccess.Write)) stream.Write(file, 0, imageBytes);
        File.Copy(s.document, images + "/Compressed.tiff", false);
        s.sourceBytes = file.Length; s.imageBytes = imageBytes;
        foreach (string name in new[] { "Document", "Plain", "Compressed" })
        {
            string path = images + "/" + name + ".tiff";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(r, importer != null, "TIFF imports through TextureImporter: " + name);
            importer.sRGBTexture = true; importer.isReadable = false; importer.mipmapEnabled = name == "Compressed";
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
            if (name == "Compressed") importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Standalone", overridden = true, maxTextureSize = 128, format = TextureImporterFormat.DXT5,
                textureCompression = TextureImporterCompression.Compressed });
            importer.SaveAndReimport();
            Check(r, !((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, "Saved importer is non-readable: " + name);
        }
        s.documentGuid = AssetDatabase.AssetPathToGUID(s.document); s.savedHash = Hash(s.document); s.metaHash = Hash(s.document + ".meta");
        WriteState(s);
        Scene previous = SceneManager.GetActiveScene(), scene = default(Scene);
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var go = new GameObject("WhimTex Player probe"); SceneManager.MoveGameObjectToScene(go, scene);
            var probe = go.AddComponent(probeType);
            probeType.GetField("resourcePrefix").SetValue(probe, s.resourcePrefix);
            probeType.GetField("runId").SetValue(probe, s.runId);
            probeType.GetField("outputDirectory").SetValue(probe, s.output);
            Check(r, EditorSceneManager.SaveScene(scene, s.scene), "Save only the owned additive probe scene.");
            s.sceneGuid = AssetDatabase.AssetPathToGUID(s.scene); WriteState(s);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            RestoreBorrowed(s);
        }
        StartRed(s, r); s.phase = "prepared"; WriteState(s);
        r.phase = s.phase; r.sourceBytes = s.sourceBytes; r.imageBytes = s.imageBytes;
    });

    public static string RestartPlayerLive(string runId) => Entry(runId, "Restart only the GUID-owned unsaved red live document", (s, r) =>
    {
        Quiescent(s); OwnedFolder(s, r);
        Check(r, s.phase == "prepared" || s.phase == "completed", "Prepared or returned build required for restart.");
        Check(r, AssetDatabase.AssetPathToGUID(s.document) == s.documentGuid && Hash(s.document) == s.savedHash, "Saved document identity and bytes are intact.");
        StartRed(s, r);
    });

    public static string Trigger(string runId) => Entry(runId, "Arm one connected native build; returns paths before parent calls build", (s, r) =>
    {
        Quiescent(s); OwnedFolder(s, r);
        Check(r, s.phase == "prepared" || s.phase == "completed", "No pending build may be retried.");
        Check(r, Target().ToString() == s.target, "Use current supported target without changing project settings.");
        Check(r, AssetDatabase.AssetPathToGUID(s.scene) == s.sceneGuid && AssetDatabase.AssetPathToGUID(s.document) == s.documentGuid,
            "Exactly the recorded owned scene/document are build inputs.");
        var doc = FindDocument(s);
        Check(r, doc != null && IsLiveFor(doc) && LivePath == s.document && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.red,
            "RestartPlayerLive must precede Trigger, including after parent compilation.");
        Check(r, Hash(s.document) == s.savedHash, "Saved green bytes intact before build.");
        s.attempt++; string attempt = AttemptDirectory(s); NoLinks(attempt);
        Check(r, !Directory.Exists(attempt) && !File.Exists(attempt), "Build attempt output is fresh.");
        Directory.CreateDirectory(attempt); s.phase = "queued"; WriteState(s);
        NewJson(Path.Combine(attempt, "build-request.json"), Reply(s, "Armed once; parent uses native build/build_status; never profileName"));
        var acknowledgement = Reply(s, r.message);
        r.attempt = s.attempt; r.phase = s.phase; r.executable = acknowledgement.executable;
        r.buildMarker = acknowledgement.buildMarker; r.nativeMarker = acknowledgement.nativeMarker; r.playerReport = acknowledgement.playerReport;
        r.status = "running";
    });

    static void PackAssertions(State s, BuildReport report, Result r)
    {
        Check(r, report != null && report.summary.result == BuildResult.Succeeded, "Player build succeeds.");
        Check(r, Same(Path.GetFullPath(report.summary.outputPath), Executable(s)) && report.summary.platform.ToString() == s.target,
            "Build report belongs to this exact GUID attempt/target.");
        r.buildResult = report.summary.result.ToString(); r.totalBytes = report.summary.totalSize; r.buildSeconds = report.summary.totalTime.TotalSeconds;
        string prefix = s.folder + "/Resources/"; ulong document = 0, plain = 0, compressed = 0;
        foreach (var pack in report.packedAssets) foreach (var item in pack.contents)
            if (item.sourceAssetPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                var row = new Packed { path = item.sourceAssetPath, type = item.type.Name, bytes = item.packedSize }; r.packed.Add(row);
                Check(r, row.type == "Texture2D", "Only Texture2D from TIFF enters Player: " + row.path);
                if (row.path == s.document) document += row.bytes;
                else if (row.path == prefix + s.resourcePrefix + "/Plain.tiff") plain += row.bytes;
                else if (row.path == prefix + s.resourcePrefix + "/Compressed.tiff") compressed += row.bytes;
                else Check(r, false, "Unexpected resource packed from owned fixture: " + row.path);
            }
        Check(r, r.packed.Count >= 3 && document > 0 && plain > 0 && compressed > 0, "All three TIFF textures are present in packed assets.");
        Check(r, Math.Abs((double)document - plain) < 1024, "Layered TIFF packed size differs from identical plain TIFF by less than 1024 bytes.");
        foreach (var file in report.GetFiles())
            Check(r, file.path.IndexOf("DCFApixels.WhimTex", StringComparison.OrdinalIgnoreCase) < 0, "No WhimTex assembly shipped: " + file.path);
    }
    static void SavedAssertions(State s, Result r)
    {
        Check(r, AssetDatabase.AssetPathToGUID(s.document) == s.documentGuid && Hash(s.document) == s.savedHash,
            "Build retained saved green bytes and document GUID.");
        Check(r, Hash(s.document + ".meta") == s.metaHash && !((TextureImporter)AssetImporter.GetAtPath(s.document)).isReadable,
            "Build restores exact importer meta and non-readable setting.");
        var doc = FindDocument(s);
        Check(r, doc != null && ((ColorFillLayerBehaviour)doc.layers[0].Behaviour).color == Color.red, "Unsaved red editor document survives build.");
        Check(r, !IsLive, "Actual build callback ended Live Update.");
    }
    public static string InspectBuild(string runId) => Entry(runId, "Verify this GUID's completed Player build and saved/live separation", (s, r) =>
    {
        EditorIdle(); OwnedFolder(s, r);
        Check(r, (s.phase == "queued" || s.phase == "completed") && s.attempt > 0, "An armed native build is required.");
        NoLinks(NativeMarker(s));
        var marker = JsonUtility.FromJson<NativeCompletion>(File.ReadAllText(NativeMarker(s)));
        Check(r, marker != null && marker.version == 1 && marker.runId == s.runId && marker.attempt == s.attempt &&
            marker.status == "completed" && marker.completed && marker.outputPath == Executable(s) && marker.target == s.target,
            "Parent observed native build_status completed for this exact attempt before any new compilation/mutation.");
        var report = BuildReport.GetLatestReport();
        Check(r, report != null && Same(Path.GetFullPath(report.summary.outputPath), Executable(s)) && report.summary.platform.ToString() == s.target,
            "Latest native report confirms this owned output/target; unrelated/stale builds cannot release the fixture.");
        // Confirmed terminal build (including Failed/Cancelled) can now be cleaned. Assertions remain strict.
        s.phase = "completed"; WriteState(s); r.phase = s.phase; r.buildReturned = true; r.buildResult = report.summary.result.ToString();
        try
        {
            PackAssertions(s, report, r); SavedAssertions(s, r);
            Check(r, File.Exists(Executable(s)) || Directory.Exists(Executable(s)), "Native Player output exists.");
            r.status = "passed";
        }
        catch (Exception error) { r.status = "failed"; r.failures = new[] { error.ToString() }; }
        if (!File.Exists(BuildMarker(s))) NewJson(BuildMarker(s), r);
    });

    // Pipeline interprets these temporary types; Unity's native serializer can omit a
    // List<interpreted Sample> even when the native Player wrote a correct JSON array.
    // Decode native JSON tokens explicitly, never deserialize an interpreted DTO.
    // Bind the package's existing public JSON assembly: other integrations can expose
    // copies with identical type names. No dependency installation or Unity internals.
    static object ParsePlayerJson(string json)
    {
        var reference = Array.Find(typeof(WhimTexApi).Assembly.GetReferencedAssemblies(), a => a.Name == "Newtonsoft.Json");
        if (reference == null) throw new InvalidOperationException("Package's existing JSON assembly is required.");
        return Assembly.Load(reference).GetType("Newtonsoft.Json.Linq.JObject", true)
            .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
    }
    static string WireKind(object token) => token == null ? null : token.GetType().GetProperty("Type").GetValue(token).ToString();
    static object WireField(object node, string key, string kind)
    {
        if (WireKind(node) != "Object") throw new InvalidDataException("Expected JSON object for " + key);
        var token = node.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(node, new object[] { key });
        if (WireKind(token) != kind) throw new InvalidDataException("Missing/wrong JSON field type: " + key + " (" + kind + ")");
        return token;
    }
    static object WireValue(object token) => token.GetType().GetProperty("Value").GetValue(token);
    static string WireString(object node, string key) => (string)WireValue(WireField(node, key, "String"));
    static int WireInt(object node, string key) => Convert.ToInt32(WireValue(WireField(node, key, "Integer")));
    static bool WireBool(object node, string key) => (bool)WireValue(WireField(node, key, "Boolean"));
    static double WireNumber(object node, string key)
    {
        if (WireKind(node) != "Object") throw new InvalidDataException("Expected JSON object for " + key);
        var token = node.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(node, new object[] { key });
        if (WireKind(token) != "Float" && WireKind(token) != "Integer") throw new InvalidDataException("Expected JSON number: " + key);
        double value = Convert.ToDouble(WireValue(token));
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidDataException("Non-finite JSON number: " + key);
        return value;
    }
    static string[] WireStrings(object node, string key)
    {
        var values = new List<string>();
        foreach (object token in (System.Collections.IEnumerable)WireField(node, key, "Array"))
        {
            if (WireKind(token) != "String") throw new InvalidDataException("Expected JSON string array: " + key);
            values.Add((string)WireValue(token));
        }
        return values.ToArray();
    }
    static PlayerResult ParsePlayerResult(string json)
    {
        object node = ParsePlayerJson(json);
        var result = new PlayerResult { version = WireInt(node, "version"), checks = WireInt(node, "checks"),
            runId = WireString(node, "runId"), status = WireString(node, "status"), unity = WireString(node, "unity"),
            graphics = WireString(node, "graphics"), failures = WireStrings(node, "failures"), assemblies = WireStrings(node, "assemblies") };
        var samples = new List<PlayerSample>();
        foreach (object row in (System.Collections.IEnumerable)WireField(node, "samples", "Array"))
        {
            object pixel = WireField(row, "pixel", "Object");
            samples.Add(new PlayerSample { name = WireString(row, "name"), format = WireString(row, "format"),
                width = WireInt(row, "width"), height = WireInt(row, "height"), mips = WireInt(row, "mips"),
                readable = WireBool(row, "readable"), loadMs = WireNumber(row, "loadMs"),
                pixel = new Color((float)WireNumber(pixel, "r"), (float)WireNumber(pixel, "g"),
                    (float)WireNumber(pixel, "b"), (float)WireNumber(pixel, "a")) });
        }
        result.samples = samples.ToArray();
        return result;
    }
    static void InspectPlayerEvidence(State s, Result r)
    {
        NoLinks(BuildMarker(s));
        var build = JsonUtility.FromJson<Result>(File.ReadAllText(BuildMarker(s)));
        Check(r, build != null && build.runId == s.runId && build.attempt == s.attempt && build.status == "passed" &&
            build.failures != null && build.failures.Length == 0, "Player must come from this attempt's verified successful build.");
        NoLinks(PlayerReport(s));
        var result = ParsePlayerResult(File.ReadAllText(PlayerReport(s)));
        Check(r, result != null && result.version == 1 && result.runId == s.runId && result.status == "passed" && result.checks > 0 &&
            result.failures != null && result.failures.Length == 0, "Native probe result passes for this GUID.");
        Check(r, result.assemblies != null && result.assemblies.Length > 0, "Player recorded actually loaded assemblies.");
        foreach (string name in result.assemblies) Check(r, !name.StartsWith("DCFApixels.WhimTex", StringComparison.Ordinal), "No WhimTex assembly loaded in Player.");
        Check(r, result.samples != null && result.samples.Length == 3, "Three runtime texture samples.");
        var names = new HashSet<string>();
        foreach (var sample in result.samples)
        {
            Check(r, sample != null && names.Add(sample.name) &&
                (sample.name == "Document" || sample.name == "Plain" || sample.name == "Compressed"), "Unique named runtime resource.");
            Check(r, !sample.readable && !float.IsNaN(sample.pixel.g) && sample.pixel.g >= .9f &&
                !float.IsNaN(sample.pixel.r) && sample.pixel.r <= .05f && !float.IsNaN(sample.pixel.b) && sample.pixel.b <= .05f,
                "Runtime texture is saved green and non-readable: " + sample.name);
            if (sample.name == "Compressed") Check(r, sample.width == 128 && sample.height == 128 && sample.mips == 8 && sample.format == "DXT5",
                "Standalone MaxSize 128, DXT5 and eight mip levels reached Player.");
            else Check(r, sample.width == 256 && sample.height == 256 && sample.mips == 1, "Uncompressed 256px fixture without mips.");
        }
        r.checks += result.checks;
    }
    public static string InspectPlayer(string runId) => Entry(runId, "Verify native Player pixels, import overrides and loaded assembly boundary", (s, r) =>
    {
        Quiescent(s); Check(r, s.phase == "completed", "Player report belongs to a completed build.");
        InspectPlayerEvidence(s, r);
    });
    // Read-only diagnostic for a retained successful build/Player artifact AFTER
    // cleanup. This does not rerun a workflow, grant coverage PASS or write state.
    public static string InspectPlayerArtifact(string runId) => Entry(runId, "Read-only reinspection of retained native Player evidence", (s, r) =>
    {
        Quiescent(s); Check(r, s.phase == "completed" || s.phase == "cleaned", "Completed or cleaned owned attempt required.");
        InspectPlayerEvidence(s, r);
    });

    public static string Cleanup(string runId) => Entry(runId, "Delete only GUID-owned test assets; restore borrowed selection/focus; keep Temp evidence", (s, r) =>
    {
        Quiescent(s); // NEVER cancel a build or infer native completion from a timeout/idle Editor.
        var failures = new List<Exception>();
        Action<Action> drain = step => { try { step(); } catch (Exception error) { failures.Add(error); } };
        drain(() => {
            var doc = FindDocument(s);
            if (IsLive && LivePath == s.document)
            {
                Check(r, doc != null && IsLiveFor(doc), "Only our exact document owns the live stop.");
                SessionCall("StopFor", new[] { typeof(TextureCompositor), typeof(string) }, doc, "owned Player cleanup");
                Check(r, !IsLiveFor(doc), "Owned live session drained.");
            }
            if (doc != null) { Check(r, !IsLiveFor(doc), "Do not destroy a still-live owned document."); Undo.ClearUndo(doc); Object.DestroyImmediate(doc); }
        });
        drain(() => {
            if (AssetDatabase.IsValidFolder(s.folder)) OwnedFolder(s, r);
            if (!string.IsNullOrEmpty(s.sceneGuid)) Check(r, AssetDatabase.AssetPathToGUID(s.scene) == s.sceneGuid, "Only recorded scene GUID may close.");
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == s.scene) Check(r, EditorSceneManager.CloseScene(scene, true), "Close only owned scene.");
            }
        });
        // Preserve fixtures on a live/scene cleanup failure. Retry with the same GUID after resolving it.
        if (failures.Count == 0) drain(() => {
            if (AssetDatabase.IsValidFolder(s.folder))
            {
                OwnedFolder(s, r); NoTreeLinks(s.folder);
                Check(r, LivePath != s.document, "Do not delete assets of an active owned session.");
                DetachCompile(runId, "install");
                r.nativeCompileRequired = File.Exists(ProbePath(s)) || !string.IsNullOrEmpty(s.probeGuid);
                if (r.nativeCompileRequired) ArmCompile(runId, "cleanup"); // Observe automatic native compilation after deletion.
                Check(r, AssetDatabase.DeleteAsset(s.folder), "Delete exact owned GUID asset folder through AssetDatabase.");
            }
            else Check(r, !Directory.Exists(s.folder) && !File.Exists(s.folder + ".meta"), "No unresolved owned directory remains.");
        });
        drain(() => RestoreBorrowed(s));
        if (failures.Count != 0) throw new AggregateException("Owned Player cleanup failed; retain state and outer lock", failures);
        s.phase = "cleaned"; WriteState(s); r.phase = s.phase;
        Check(r, !AssetDatabase.IsValidFolder(s.folder), "Owned assets removed; native script deletion may require parent's final compilation wait.");
    });
}
